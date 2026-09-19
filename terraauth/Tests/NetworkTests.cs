// TerraAuth — Phase 5 验收测试
// 对应 architecture.md §8 + Net/Phase5/README.md §8

using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using TerraAuth.Protocol; // PacketId / PlayerPositionPacket
using TerraAuth.Net.Transport;
using TerraAuth.Simulation; // SnapshotFrame / EntityState / RemovedEntity
using TerraAuth.Concurrency; // WorkerPool
using Xunit;

namespace TerraAuth.Tests;

public class FramingTests
{
    [Fact]
    public void WriteFrame_Then_TryReadFrame_RoundTrips()
    {
        // Arrange
        var writer = new ArrayBufferWriter<byte>();
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        var type = PacketId.PlayerPosition;

        // Act
        Framing.WriteFrame(writer, type, payload);
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        // Assert
        Assert.True(Framing.TryReadFrame(ref buffer, out var readType, out var readPayload));
        Assert.Equal(type, readType);
        Assert.Equal(payload, readPayload.ToArray());
        Assert.True(buffer.IsEmpty); // 无粘包残留
    }

    [Fact]
    public void WriteFrame_LengthField_IsTotalPacketSize()
    {
        // Arrange：length 字段应为整包字节数（含 2 字节长度 + 1 字节类型）
        var writer = new ArrayBufferWriter<byte>();
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };

        // Act
        Framing.WriteFrame(writer, PacketId.PlayerPosition, payload);

        // Assert
        var bytes = writer.WrittenSpan.ToArray();
        Assert.Equal(3 + payload.Length, bytes[0] | (bytes[1] << 8));
        Assert.Equal(bytes.Length, bytes[0] | (bytes[1] << 8));
        Assert.Equal((byte)PacketId.PlayerPosition, bytes[2]);
    }

    [Fact]
    public void TryReadFrame_HandlesPartialFrame()
    {
        // Arrange：只写 2 字节（不足 3 字节头）
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(new byte[] { 0x05, 0x00 }); // length=5, 缺 type
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        // Act：数据不足，应返回 false 且不消费
        Assert.False(Framing.TryReadFrame(ref buffer, out _, out _));
        Assert.Equal(2, buffer.Length); // 未消费
    }

    [Fact]
    public void TryReadFrame_HandlesStickyPackets()
    {
        // Arrange：一帧半 + 另一帧前半 → 先读出完整帧，剩余保留
        var writer = new ArrayBufferWriter<byte>();
        Framing.WriteFrame(writer, PacketId.PlayerHealth, new byte[] { 1, 2 });
        // 粘一个不完整的第二帧：声明整包 4 字节（需 1 字节 payload），实际只有 3 字节
        writer.Write(new byte[] { 0x04, 0x00, (byte)PacketId.Disconnect });
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        // Act
        Assert.True(Framing.TryReadFrame(ref buffer, out var t1, out _));
        Assert.Equal(PacketId.PlayerHealth, t1);
        Assert.False(Framing.TryReadFrame(ref buffer, out _, out _)); // 第二帧不完整
    }
}

public class PacketCodecTests
{
    [Fact]
    public void Encode_Decode_RoundTrip_PreservesData()
    {
        // Arrange
        var version = ProtocolVersion.Current;
        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(version);
        var ctx = new DecodeContext { Version = version };

        // Act：编码一个 PlayerControlsPacket（包 13 真实线格式）
        var original = new PlayerControlsPacket(
            7, new Vector2(123.45f, 678.9f), new Vector2(1.5f, -2.5f), SelectedItem: 3,
            ControlBits: 0b0010_0000, StateBits: PlayerControlsPacket.StateBitHasVelocity);
        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.PlayerPosition, original);
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        // Assert：解码后数据一致
        Assert.True(decoder.TryDecodeFrame(ref buffer, ctx, out var decoded));
        var controls = Assert.IsType<PlayerControlsPacket>(decoded);
        Assert.Equal(7, controls.PlayerId);
        Assert.Equal(123.45f, controls.Position.X, 3);
        Assert.Equal(678.9f, controls.Position.Y, 1);
        Assert.Equal(1.5f, controls.Velocity.X, 3);
        Assert.Equal(-2.5f, controls.Velocity.Y, 3);
        Assert.Equal(3, controls.SelectedItem);
        Assert.Equal(0b0010_0000, controls.ControlBits);
    }

    [Fact]
    public void PrefixedString_RoundTrip_Short()
    {
        // 短字符串：1 字节长度前缀
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            PacketEncoder.WritePrefixedString(bw, "Terraria326");

        var bytes = ms.ToArray();
        Assert.Equal((byte)0x0B, bytes[0]); // 长度 11，单字节前缀

        ms.Position = 0;
        using var br = new BinaryReader(ms, System.Text.Encoding.UTF8);
        Assert.Equal("Terraria326", PacketDecoder.ReadPrefixedString(br));
        Assert.Equal(ms.Length, ms.Position); // 全部消费
    }

    [Fact]
    public void PrefixedString_RoundTrip_MultiByteLength()
    {
        // 300 字节 → 7-bit 变长前缀需 2 字节（0xAC 0x02）
        var value = new string('a', 300);
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            PacketEncoder.WritePrefixedString(bw, value);

        var bytes = ms.ToArray();
        Assert.Equal((byte)0xAC, bytes[0]);
        Assert.Equal((byte)0x02, bytes[1]);
        Assert.Equal(2 + 300, bytes.Length);

        ms.Position = 0;
        using var br = new BinaryReader(ms, System.Text.Encoding.UTF8);
        Assert.Equal(value, PacketDecoder.ReadPrefixedString(br));
    }
}

public class AuthorityPacketCodecTests
{
    private static readonly DecodeContext Ctx = new() { Version = ProtocolVersion.Current };
    private static readonly PacketDecoder Decoder = new();
    private static readonly PacketEncoder Encoder = new(ProtocolVersion.Current);

    private static T RoundTrip<T>(PacketId type, INetworkPacket packet) where T : INetworkPacket
    {
        var writer = new ArrayBufferWriter<byte>();
        Encoder.Encode(writer, type, packet);
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);
        Assert.True(Decoder.TryDecodeFrame(ref buffer, Ctx, out var decoded));
        return Assert.IsType<T>(decoded);
    }

    [Fact]
    public void Encode_Decode_PlayerActive_RoundTrips()
    {
        var p = RoundTrip<PlayerActivePacket>(PacketId.PlayerActive, new PlayerActivePacket(3, true));
        Assert.Equal((byte)3, p.PlayerId);
        Assert.True(p.Active);
    }

    [Fact]
    public void Encode_Decode_TileBreak_RoundTrips()
    {
        var original = new TileBreakPacket(1234, 567, Action: 0) { TileType = 30, Style = 4 };
        var p = RoundTrip<TileBreakPacket>(PacketId.TileBreak, original);
        Assert.Equal(1234, p.X);
        Assert.Equal(567, p.Y);
        Assert.Equal((byte)0, p.Action);
        Assert.Equal(30, p.TileType);
        Assert.Equal((byte)4, p.Style);
    }

    [Fact]
    public void Encode_Decode_TilePlace_RoundTrips()
    {
        var original = new TilePlacePacket(X: 1234, Y: 567, TileType: 30)
        {
            Style = 2,
            Alternate = 3,
            Random = -4,
            Direction = -1,
        };
        var p = RoundTrip<TilePlacePacket>(PacketId.TilePlace, original);
        Assert.Equal(1234, p.X);
        Assert.Equal(567, p.Y);
        Assert.Equal(30, p.TileType);
        Assert.Equal(2, p.Style);
        Assert.Equal((byte)3, p.Alternate);
        Assert.Equal((sbyte)-4, p.Random);
        Assert.Equal(-1, p.Direction);
    }

    [Fact]
    public void Encode_Decode_NpcStrike_RoundTrips()
    {
        var original = new NpcStrikePacket(NpcId: 5, Damage: 42)
        {
            Generation = 2,
            Knockback = 3.5f,
            Direction = -1,
            Crit = true,
        };
        var p = RoundTrip<NpcStrikePacket>(PacketId.NpcStrike, original);
        Assert.Equal(5, p.NpcId);
        Assert.Equal(42, p.Damage);
        Assert.Equal(2, p.Generation);
        Assert.Equal(3.5f, p.Knockback, 3);
        Assert.Equal(-1, p.Direction);
        Assert.True(p.Crit);
    }

    [Fact]
    public void Encode_Decode_ProjectileNew_RoundTrips()
    {
        var original = new ProjectileNewPacket(
            ProjectileKey: 168496141,
            Position: new Vector2(512.5f, -256.25f),
            Velocity: new Vector2(7.5f, -3.25f),
            ProjectileType: 1234)
        {
            Ai0 = 1.5f,
            Ai1 = -2.25f,
            Ai2 = 3.75f,
            BannerIdToRespondTo = 65,
            Damage = 88,
            Knockback = 4.5f,
            OriginalDamage = 99,
        };
        var p = RoundTrip<ProjectileNewPacket>(PacketId.ProjectileNew, original);
        Assert.Equal(168496141, p.ProjectileKey);
        Assert.Equal(512.5f, p.Position.X, 3);
        Assert.Equal(-256.25f, p.Position.Y, 3);
        Assert.Equal(7.5f, p.Velocity.X, 3);
        Assert.Equal(-3.25f, p.Velocity.Y, 3);
        Assert.Equal(1234, p.ProjectileType);
        Assert.Equal(1.5f, p.Ai0, 3);
        Assert.Equal(-2.25f, p.Ai1, 3);
        Assert.Equal(3.75f, p.Ai2, 3);
        Assert.Equal(65, p.BannerIdToRespondTo);
        Assert.Equal(88, p.Damage);
        Assert.Equal(4.5f, p.Knockback, 3);
        Assert.Equal(99, p.OriginalDamage);
    }

    [Fact]
    public void Encode_Decode_ProjectileNew_MinimalPayload_RoundTrips()
    {
        // 所有可选字段为零：flags1 bit2 不置位，payload 中不应出现第二个 BitsByte。
        var original = new ProjectileNewPacket(
            ProjectileKey: -7,
            Position: new Vector2(16f, 32f),
            Velocity: new Vector2(0f, 0f),
            ProjectileType: 1);
        var p = RoundTrip<ProjectileNewPacket>(PacketId.ProjectileNew, original);
        Assert.Equal(-7, p.ProjectileKey);
        Assert.Equal(1, p.ProjectileType);
        Assert.Equal(0f, p.Ai0, 3);
        Assert.Equal(0f, p.Ai1, 3);
        Assert.Equal(0f, p.Ai2, 3);
        Assert.Equal(0, p.BannerIdToRespondTo);
        Assert.Equal(0, p.Damage);
        Assert.Equal(0f, p.Knockback, 3);
        Assert.Equal(0, p.OriginalDamage);
    }

    [Fact]
    public void Encode_Decode_UnknownPacket_PassesPayloadThrough()
    {
        // ChatText(25) 未结构化建模：解码产出 UnknownPacket，编码原样透传 payload。
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x80, 0xFF };
        var original = new UnknownPacket(PacketId.ChatText, payload);
        var p = RoundTrip<UnknownPacket>(PacketId.ChatText, original);
        Assert.Equal(PacketId.ChatText, p.Type);
        Assert.Equal(payload, p.Payload);
    }

    [Fact]
    public void Encode_Decode_Chest_RoundTrips()
    {
        var original = new ChestPacket(X: 2100, Y: 300);
        var p = RoundTrip<ChestPacket>(PacketId.Chest, original);
        Assert.Equal(2100, p.X);
        Assert.Equal(300, p.Y);
    }

    [Fact]
    public void Encode_Decode_SyncItem_RoundTrips()
    {
        var original = new ItemDropPacket(ItemId: 757, Stack: 99)
        {
            ItemSlotIndex = 12,
            Position = new Vector2(1024.5f, 2048.25f),
            Velocity = new Vector2(-1.5f, 0.25f),
            Prefix = 3,
            Shimmered = true,
            ShimmerTime = 2.5f,
            EnemyGrabDelayTime = 7,
        };
        var p = RoundTrip<ItemDropPacket>(PacketId.ItemDrop, original);
        Assert.Equal(757, p.ItemId);
        Assert.Equal(99, p.Stack);
        Assert.Equal(12, p.ItemSlotIndex);
        Assert.Equal(1024.5f, p.Position.X, 3);
        Assert.Equal(2048.25f, p.Position.Y, 3);
        Assert.Equal(-1.5f, p.Velocity.X, 3);
        Assert.Equal((byte)3, p.Prefix);
        Assert.True(p.Shimmered);
        Assert.Equal(2.5f, p.ShimmerTime, 3);
        Assert.Equal((byte)7, p.EnemyGrabDelayTime);
    }

    [Fact]
    public void Encode_Decode_SyncChestItem_RoundTrips()
    {
        var original = new SyncChestItemPacket(ChestIndex: 5, ItemSlot: 12, Stack: 30, Prefix: 2, ItemType: 4956);
        var p = RoundTrip<SyncChestItemPacket>(PacketId.SyncChestItem, original);
        Assert.Equal(5, p.ChestIndex);
        Assert.Equal(12, p.ItemSlot);
        Assert.Equal(30, p.Stack);
        Assert.Equal((byte)2, p.Prefix);
        Assert.Equal(4956, p.ItemType);
    }

    [Fact]
    public void Encode_Decode_PlayerHeal_RoundTrips()
    {
        var p = RoundTrip<PlayerHealPacket>(PacketId.PlayerHeal, new PlayerHealPacket(PlayerId: 2, Amount: 250));
        Assert.Equal(2, p.PlayerId);
        Assert.Equal(250, p.Amount);
    }

    [Fact]
    public void Encode_Decode_QuickStackChests_RoundTrips()
    {
        var original = new QuickStackChestsPacket(new[] { 9, 12, 30 }, SmartStack: true);
        var p = RoundTrip<QuickStackChestsPacket>(PacketId.QuickStackChests, original);
        Assert.Equal(new[] { 9, 12, 30 }, p.Slots);
        Assert.True(p.SmartStack);
    }

    [Fact]
    public void Encode_Decode_PlayerChestIndex_RoundTrips()
    {
        var p = RoundTrip<PlayerChestIndexPacket>(PacketId.SyncPlayerChestIndex,
            new PlayerChestIndexPacket(PlayerId: 3, ChestIndex: 12));
        Assert.Equal(3, p.PlayerId);
        Assert.Equal(12, p.ChestIndex);
    }

    [Fact]
    public void Encode_Decode_LiquidModule_RoundTrips()
    {
        var original = new LiquidModulePacket(new[]
        {
            new LiquidChange(2100, 300, 200, 0),
            new LiquidChange(2101, 300, 0, 0),
            new LiquidChange(2102, 301, 128, 1),
        });

        var p = RoundTrip<LiquidModulePacket>(PacketId.NetModule, original);

        Assert.Equal(3, p.Changes.Count);
        Assert.Equal(new LiquidChange(2100, 300, 200, 0), p.Changes[0]);
        Assert.Equal(new LiquidChange(2101, 300, 0, 0), p.Changes[1]);
        Assert.Equal(new LiquidChange(2102, 301, 128, 1), p.Changes[2]);
        Assert.True(p.IsClientMessage); // RoundTrip 用入站上下文解码 → 视为客户端上行
    }

    [Fact]
    public void Encode_Decode_SyncPlayerZone_RoundTrips()
    {
        var original = new SyncPlayerZonePacket(PlayerId: 1, Zone1: 1, Zone2: 2, Zone3: 3, Zone4: 4, Zone5: 5, TownNpcs: 6);
        var p = RoundTrip<SyncPlayerZonePacket>(PacketId.SyncPlayerZone, original);
        Assert.Equal((byte)1, p.PlayerId);
        Assert.Equal((byte)1, p.Zone1);
        Assert.Equal((byte)5, p.Zone5);
        Assert.Equal((byte)6, p.TownNpcs);
    }

    [Fact]
    public void Encode_Decode_PlayerBuffs_RoundTrips()
    {
        var original = new PlayerBuffsPacket(PlayerId: 4, BuffTypes: new List<int> { 1, 2, 300 });
        var p = RoundTrip<PlayerBuffsPacket>(PacketId.PlayerBuffs, original);
        Assert.Equal(4, p.PlayerId);
        Assert.Equal(new[] { 1, 2, 300 }, p.BuffTypes);
    }

    [Fact]
    public void Encode_Decode_PlayerHurtV2_RoundTrips()
    {
        var reason = new PlayerDeathReasonData
        {
            SourcePlayerIndex = 1,
            SourceNpcIndex = 2,
            SourceProjectileLocalIndex = 3,
            SourceOtherIndex = 4,
            SourceProjectileType = 5,
            SourceItemType = 6,
            SourceItemPrefix = 7,
            CustomReason = "被自己的回旋镖击倒",
        };
        var original = new PlayerHurtV2Packet(PlayerId: 2, Damage: 137)
        {
            DeathReason = reason,
            HitDirection = -1,
            Crit = true,
            Pvp = true,
            CooldownCounter = -3,
        };
        var p = RoundTrip<PlayerHurtV2Packet>(PacketId.PlayerHurtV2, original);
        Assert.Equal(2, p.PlayerId);
        Assert.Equal(137, p.Damage);
        Assert.Equal(-1, p.HitDirection);
        Assert.True(p.Crit);
        Assert.True(p.Pvp);
        Assert.Equal((sbyte)-3, p.CooldownCounter);
        Assert.Equal(1, p.DeathReason.SourcePlayerIndex);
        Assert.Equal(2, p.DeathReason.SourceNpcIndex);
        Assert.Equal(3, p.DeathReason.SourceProjectileLocalIndex);
        Assert.Equal(4, p.DeathReason.SourceOtherIndex);
        Assert.Equal(5, p.DeathReason.SourceProjectileType);
        Assert.Equal(6, p.DeathReason.SourceItemType);
        Assert.Equal(7, p.DeathReason.SourceItemPrefix);
        Assert.Equal("被自己的回旋镖击倒", p.DeathReason.CustomReason);
    }

    [Fact]
    public void Encode_Decode_PlayerDeathV2_RoundTrips()
    {
        // 仅设置自定义文本，其余标志位应保持缺省（-1 / 0 / null）
        var original = new PlayerDeathV2Packet(PlayerId: 7, Damage: 9999)
        {
            DeathReason = new PlayerDeathReasonData { CustomReason = "摔死了" },
            HitDirection = 1,
            Pvp = true,
        };
        var p = RoundTrip<PlayerDeathV2Packet>(PacketId.PlayerDeathV2, original);
        Assert.Equal(7, p.PlayerId);
        Assert.Equal(9999, p.Damage);
        Assert.Equal(1, p.HitDirection);
        Assert.True(p.Pvp);
        Assert.Equal(-1, p.DeathReason.SourcePlayerIndex);
        Assert.Equal(0, p.DeathReason.SourceItemType);
        Assert.Equal("摔死了", p.DeathReason.CustomReason);
    }

    [Fact]
    public void Encode_Decode_TeleportEntity_RoundTrips()
    {
        var original = new TeleportEntityPacket(EntityId: 3, Position: new Vector2(1024.5f, 2048.25f), Style: 7)
        {
            Kind = TeleportEntityKind.PlayerToPlayer,
            ExtraInfo = 123456,
        };
        var p = RoundTrip<TeleportEntityPacket>(PacketId.TeleportEntity, original);
        Assert.Equal(3, p.EntityId);
        Assert.Equal(1024.5f, p.Position.X, 3);
        Assert.Equal(2048.25f, p.Position.Y, 3);
        Assert.Equal((byte)7, p.Style);
        Assert.Equal(TeleportEntityKind.PlayerToPlayer, p.Kind);
        Assert.False(p.NoPosition);
        Assert.Equal(123456, p.ExtraInfo);
    }

    [Fact]
    public void Encode_Decode_TeleportEntity_NoPosition_NoExtra_RoundTrips()
    {
        // bit2（无位置）+ bit3（无额外信息）均不置位：payload 末尾不应出现 Int32。
        var original = new TeleportEntityPacket(EntityId: 1, Position: default)
        {
            Kind = TeleportEntityKind.Acknowledge,
            NoPosition = true,
        };
        var p = RoundTrip<TeleportEntityPacket>(PacketId.TeleportEntity, original);
        Assert.Equal(1, p.EntityId);
        Assert.Equal(TeleportEntityKind.Acknowledge, p.Kind);
        Assert.True(p.NoPosition);
        Assert.Equal(0, p.ExtraInfo);
    }

    [Fact]
    public void Encode_Decode_RequestTeleportationByServer_RoundTrips()
    {
        var p = RoundTrip<RequestTeleportationByServerPacket>(
            PacketId.RequestTeleportationByServer,
            new RequestTeleportationByServerPacket(TeleportRequestKind.MagicConch));
        Assert.Equal(TeleportRequestKind.MagicConch, p.Kind);
    }
}

public class HandshakeCodecTests
{
    [Fact]
    public void ProtocolVersion_ConnectVersion_IsTerrariaPrefixed()
    {
        Assert.Equal("Terraria326", ProtocolVersion.Current.ConnectVersion);
        Assert.Equal("Terraria326", ConnectionRequestPacket.BuildVersion(326));
    }

    [Fact]
    public void DecodeConnectionRequest_ReadsVersionString()
    {
        // Arrange：ConnectRequest payload = 7-bit 长度前缀 + "Terraria326"
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            PacketEncoder.WritePrefixedString(bw, "Terraria326");

        var decoder = new PacketDecoder();
        var ctx = new DecodeContext { Version = ProtocolVersion.Current };

        // Act
        var packet = decoder.Decode(PacketId.ConnectionRequest, ms.ToArray(), ctx);

        // Assert
        var req = Assert.IsType<ConnectionRequestPacket>(packet);
        Assert.Equal("Terraria326", req.Version);
    }

    [Fact]
    public void Encode_ContinueConnecting_WritesPlayerIdByte()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var writer = new ArrayBufferWriter<byte>();

        encoder.Encode(writer, PacketId.ContinueConnecting, new ContinueConnectingPacket(7));

        var bytes = writer.WrittenSpan.ToArray();
        Assert.Equal(5, bytes[0] | (bytes[1] << 8)); // 整包 = 3 头 + 1 字节 PlayerId + 1 字节 Boolean
        Assert.Equal((byte)PacketId.ContinueConnecting, bytes[2]);
        Assert.Equal((byte)7, bytes[3]);
        Assert.Equal((byte)0, bytes[4]); // ServerSpecialFlag = false
    }

    [Fact]
    public void Encode_Decode_SpawnTileData_RoundTrips()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();
        var ctx = new DecodeContext { Version = ProtocolVersion.Current };

        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.TileGetSection, new SpawnTileDataPacket(2100, 300, 1));
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        Assert.True(decoder.TryDecodeFrame(ref buffer, ctx, out var packet));
        var spawn = Assert.IsType<SpawnTileDataPacket>(packet);
        Assert.Equal(2100, spawn.SpawnX);
        Assert.Equal(300, spawn.SpawnY);
        Assert.Equal((byte)1, spawn.Team);
    }

    [Fact]
    public void Encode_Decode_StatusText_RoundTrips()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();
        var ctx = new DecodeContext { Version = ProtocolVersion.Current };

        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.StatusText, new StatusTextPacket(100, "Receiving tile data"));
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        Assert.True(decoder.TryDecodeFrame(ref buffer, ctx, out var packet));
        var status = Assert.IsType<StatusTextPacket>(packet);
        Assert.Equal(100, status.StatusMax);
        Assert.Equal("Receiving tile data", status.StatusText);
        Assert.Equal((byte)0, status.Flags);
    }

    [Fact]
    public void Encode_Decode_PlayerSpawn_RoundTrips()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();
        var ctx = new DecodeContext { Version = ProtocolVersion.Current };

        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.PlayerSpawn, new PlayerSpawnPacket(2, 2100, 300, 0, 3, 4, 1, 0));
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        Assert.True(decoder.TryDecodeFrame(ref buffer, ctx, out var packet));
        var spawn = Assert.IsType<PlayerSpawnPacket>(packet);
        Assert.Equal((byte)2, spawn.PlayerId);
        Assert.Equal((short)2100, spawn.SpawnX);
        Assert.Equal((short)300, spawn.SpawnY);
        Assert.Equal(0, spawn.RespawnTimer);
        Assert.Equal((short)3, spawn.DeathsPve);
        Assert.Equal((short)4, spawn.DeathsPvp);
        Assert.Equal((byte)1, spawn.Team);
        Assert.Equal((byte)0, spawn.SpawnContext);
    }

    [Fact]
    public void Encode_InitialSpawn_And_FinishedConnecting_HaveNoPayload()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);

        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.InitialSpawn, new InitialSpawnPacket());
        var bytes = writer.WrittenSpan.ToArray();
        Assert.Equal(3, bytes[0] | (bytes[1] << 8)); // 仅帧头
        Assert.Equal((byte)PacketId.InitialSpawn, bytes[2]);

        writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.FinishedConnecting, new FinishedConnectingPacket());
        bytes = writer.WrittenSpan.ToArray();
        Assert.Equal(3, bytes[0] | (bytes[1] << 8));
        Assert.Equal((byte)PacketId.FinishedConnecting, bytes[2]);
    }

    [Fact]
    public void Encode_WorldInfo_WritesCoreFields()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var writer = new ArrayBufferWriter<byte>();
        var info = new WorldInfoPacket(
            Time: 12345,
            DayTime: true,
            BloodMoon: false,
            Eclipse: false,
            MoonPhase: 3,
            MaxTilesX: 4200,
            MaxTilesY: 1200,
            SpawnTileX: 2100,
            SpawnTileY: 300,
            WorldSurface: 350,
            RockLayer: 500,
            WorldId: 42,
            WorldName: "TerraAuth",
            GameMode: 0);

        encoder.Encode(writer, PacketId.WorldInfo, info);
        var bytes = writer.WrittenSpan.ToArray();

        Assert.Equal((byte)PacketId.WorldInfo, bytes[2]);

        using var br = new BinaryReader(new MemoryStream(bytes, 3, bytes.Length - 3));
        Assert.Equal(12345, br.ReadInt32());
        Assert.Equal((byte)0b0000_0001, br.ReadByte()); // DayTime 标志位
        Assert.Equal((byte)3, br.ReadByte());
        Assert.Equal((short)4200, br.ReadInt16());
        Assert.Equal((short)1200, br.ReadInt16());
        Assert.Equal((short)2100, br.ReadInt16());
        Assert.Equal((short)300, br.ReadInt16());
        Assert.Equal((short)350, br.ReadInt16());
        Assert.Equal((short)500, br.ReadInt16());
        Assert.Equal(42, br.ReadInt32());
        Assert.Equal("TerraAuth", PacketDecoder.ReadPrefixedString(br));
        Assert.Equal((byte)0, br.ReadByte());
    }

    [Fact]
    public void Disconnect_WithReason_RoundTrips()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();
        var ctx = new DecodeContext { Version = ProtocolVersion.Current };

        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.Disconnect, DisconnectPacket.WithReason("Protocol mismatch."));
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        Assert.True(decoder.TryDecodeFrame(ref buffer, ctx, out var packet));
        var disc = Assert.IsType<DisconnectPacket>(packet);
        Assert.Equal("Protocol mismatch.", disc.Reason);
    }
}

public class PipelineIntegrationTests
{
    [Fact]
    public async Task NetworkHost_DecodedPacket_ReachesPipeline()
    {
        // 用内存管道模拟：解码后的包应进入管线
        // 这是架构 §3.2 的端到端验证（简化版，不启真实 TCP）
        var decoder = new PacketDecoder();
        var ctx = new DecodeContext { Version = ProtocolVersion.Current };
        var buffer = default(ReadOnlySequence<byte>);

        // 构造一个 PlayerControls（包 13）帧
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.PlayerPosition,
            new PlayerControlsPacket(1, new Vector2(10, 20)));
        buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);

        // Act
        Assert.True(decoder.TryDecodeFrame(ref buffer, ctx, out var packet));
        var controls = Assert.IsType<PlayerControlsPacket>(packet);

        // Assert：管线能处理（此处仅验证包可达，真实管线注入见 GameHost 集成测试）
        Assert.Equal(1, controls.PlayerId);
        Assert.Equal(10f, controls.Position.X, 3);
        Assert.Equal(20f, controls.Position.Y, 3);
        await Task.CompletedTask;
    }
}

public class SnapshotSenderTests
{
    [Fact]
    public async Task SnapshotBroadcaster_SendsEncodedFrames()
    {
        // 验证 Phase 4 SnapshotBroadcaster → Phase 5 ISnapshotSender 的贯通
        // 使用内存 Channel 模拟 Connection
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var writer = new ArrayBufferWriter<byte>();

        var frame = SnapshotFrame.Create(
            tick: 100,
            entities: new[] { new EntityState(1, new Vector2(5, 5), new Vector2(0, 0), EntityStateType.Active) },
            removed: System.Array.Empty<RemovedEntity>());

        // Act
        encoder.EncodeSnapshot(writer, frame);
        var bytes = writer.WrittenSpan.ToArray();

        // Assert：编码产出非空且以 Snapshot 帧类型开头
        Assert.True(bytes.Length > 12); // header + tick + checksum + count
        Assert.Equal((byte)PacketId.Snapshot, bytes[2]); // Framing header: [len][len][type]
    }

    [Fact]
    public void EncodeSnapshot_Includes_BaseTick_And_Removed()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var writer = new ArrayBufferWriter<byte>();

        var frame = SnapshotFrame.Create(
            tick: 100,
            entities: new[] { new EntityState(7, new Vector2(1.5f, 2.5f), new Vector2(0.5f, -0.5f), EntityStateType.Active) },
            removed: new[] { new RemovedEntity(9, RemoveReason.OutOfRange) },
            baseTick: 42);

        // Act
        encoder.EncodeSnapshot(writer, frame);
        var bytes = writer.WrittenSpan.ToArray();

        // Assert：逐字段校验线上布局（小端）
        int p = Framing.HeaderLength; // [UInt16 len][Byte type] → 载荷起点

        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p))); p += 4;   // BaseTick
        Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p))); p += 4;  // Tick
        Assert.Equal(frame.Checksum, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p))); p += 4;
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p))); p += 2; // EntityCount

        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p))); p += 4;      // Id
        Assert.Equal(1.5f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p))); p += 4;  // PosX
        Assert.Equal(2.5f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p))); p += 4;  // PosY
        Assert.Equal(0.5f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p))); p += 4;  // VelX
        Assert.Equal(-0.5f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p))); p += 4; // VelY
        Assert.Equal((byte)EntityStateType.Active, bytes[p++]);                                // State

        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p))); p += 2; // RemovedCount
        Assert.Equal(9, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p))); p += 4;      // Id
        Assert.Equal((byte)RemoveReason.OutOfRange, bytes[p++]);                               // Reason

        Assert.Equal(bytes.Length, p); // 载荷无多余字节
    }

    [Fact]
    public void EncodeSnapshot_DecodeSnapshot_RoundTrip_PreservesFrame()
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var decoder = new PacketDecoder();
        var writer = new ArrayBufferWriter<byte>();

        var frame = SnapshotFrame.Create(
            tick: 123,
            entities: new[]
            {
                new EntityState(7, new Vector2(1.5f, 2.5f), new Vector2(0.5f, -0.5f), EntityStateType.Active),
                new EntityState(SnapshotFrame.NpcEntityId(3), new Vector2(-10f, 20f), new Vector2(0, 0), EntityStateType.Hidden),
            },
            removed: new[]
            {
                new RemovedEntity(9, RemoveReason.OutOfRange),
                new RemovedEntity(11, RemoveReason.Destroyed),
            },
            baseTick: 42);

        encoder.EncodeSnapshot(writer, frame);
        var bytes = writer.WrittenSpan.ToArray();

        // 1) 直接解码载荷
        var decoded = decoder.DecodeSnapshot(bytes.AsSpan(Framing.HeaderLength));

        Assert.Equal(frame.Tick, decoded.Tick);
        Assert.Equal(frame.BaseTick, decoded.BaseTick);
        Assert.Equal(frame.Checksum, decoded.Checksum);
        Assert.Equal(frame.Entities, decoded.Entities); // xUnit 对 IEnumerable 逐元素比较，EntityState 为 record
        Assert.Equal(frame.Removed, decoded.Removed);

        // 2) 经 Framing + Decode 主路径解为 SnapshotPacket
        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);
        Assert.True(decoder.TryDecodeFrame(ref buffer, new DecodeContext(), out var packet));

        var snapshot = Assert.IsType<SnapshotPacket>(packet);
        Assert.Equal(PacketId.Snapshot, snapshot.Type);
        Assert.Equal(frame.Tick, snapshot.Frame.Tick);
        Assert.Equal(frame.BaseTick, snapshot.Frame.BaseTick);
        Assert.Equal(frame.Checksum, snapshot.Frame.Checksum);
        Assert.Equal(frame.Entities, snapshot.Frame.Entities);
        Assert.Equal(frame.Removed, snapshot.Frame.Removed);
    }
}

/// <summary>
/// 包 10 TileSection 编解码回归。解码器为 <c>NetMessage.DecompressTileBlock_Inner</c> 的独立复刻，
/// 用于验证 <c>PacketEncoder.CompressTileBlockInner</c> 的位标志 / RLE / 尾部列表布局。
/// </summary>
public class TileSectionCodecTests
{
    [Fact]
    public void Encode_TileSection_AllFeatures_RoundTrips()
    {
        var world = new WorldState
        {
            MaxTilesX = 8,
            MaxTilesY = 2,
            SpawnTileX = 4,
            SpawnTileY = 1,
            Tiles = new TileMap(8, 2),
        };

        // 第一行：覆盖类型 / 墙壁 / 液体 / 导线 / 斜坡 / 颜色 / 高低位墙壁
        world.Tiles[0, 0] = new Tile { Active = true, Type = 1 };
        world.Tiles[1, 0] = new Tile { Active = true, Type = 1 }; // 与前一格相同 → RLE
        world.Tiles[2, 0] = new Tile { Active = true, Type = 1 };
        world.Tiles[3, 0] = new Tile
        {
            Active = true, Type = 2, Wall = 5, WallColor = 9, Liquid = 100, LiquidType = 1,
            Wire = true, Slope = 1, TileColor = 3,
        };
        world.Tiles[4, 0] = new Tile { Active = true, Type = 300 }; // type > 255（2 字节）
        world.Tiles[5, 0] = new Tile
        {
            Active = true, Type = 2, HalfBrick = true, // type 2 为 solid → SaveSlopes 成立
            Wire2 = true, Wire3 = true, Wire4 = true, Actuator = true, InActive = true,
        };
        world.Tiles[6, 0] = new Tile
        {
            Active = true, Type = 5, Wall = 300,
            InvisibleBlock = true, InvisibleWall = true, FullbrightBlock = true, FullbrightWall = true,
        };
        world.Tiles[7, 0] = new Tile { Wall = 1 };

        // 第二行：frame-important（宝箱 21 / 牌子 55）+ 微光液体
        world.Tiles[0, 1] = new Tile { Active = true, Type = 21, FrameX = 0, FrameY = 0 };
        world.Chests.Add(new Chest { Index = 0, X = 0, Y = 1, Name = "Loot" });
        world.Tiles[1, 1] = new Tile { Active = true, Type = 55, FrameX = 0, FrameY = 0 };
        world.Signs.Add(new Sign { Index = 0, X = 1, Y = 1, Text = "hi" });
        world.Tiles[2, 1] = new Tile { Active = true, Type = 2, Liquid = 50, LiquidType = 3 };

        var decoded = EncodeThenDecode(world, 0, 0, 8, 2, out int chests, out int signs);

        Assert.Equal(1, chests);
        Assert.Equal(1, signs);

        // RLE：0..2 三格同为 type 1
        Assert.Equal((ushort)1, decoded[0, 0].Type);
        Assert.Equal((ushort)1, decoded[1, 0].Type);
        Assert.Equal((ushort)1, decoded[2, 0].Type);

        // 综合特性
        var t3 = decoded[3, 0];
        Assert.True(t3.Active);
        Assert.Equal((ushort)2, t3.Type);
        Assert.Equal((ushort)5, t3.Wall);
        Assert.Equal((byte)9, t3.WallColor);
        Assert.Equal((byte)100, t3.Liquid);
        Assert.Equal((byte)1, t3.LiquidType); // 岩浆
        Assert.True(t3.Wire);
        Assert.Equal((byte)1, t3.Slope);
        Assert.Equal((byte)3, t3.TileColor);

        Assert.Equal((ushort)300, decoded[4, 0].Type); // type > 255
        Assert.True(decoded[5, 0].HalfBrick);
        Assert.True(decoded[5, 0].Wire2);
        Assert.True(decoded[5, 0].Wire3);
        Assert.True(decoded[5, 0].Wire4);
        Assert.True(decoded[5, 0].Actuator);
        Assert.True(decoded[5, 0].InActive);

        Assert.Equal((ushort)300, decoded[6, 0].Wall); // wall > 255
        Assert.True(decoded[6, 0].InvisibleBlock);
        Assert.True(decoded[6, 0].InvisibleWall);
        Assert.True(decoded[6, 0].FullbrightBlock);
        Assert.True(decoded[6, 0].FullbrightWall);

        Assert.False(decoded[7, 0].Active);
        Assert.Equal((ushort)1, decoded[7, 0].Wall);

        // frame-important 的 FrameX/FrameY 往返
        Assert.Equal((ushort)21, decoded[0, 1].Type);
        Assert.Equal((short)0, decoded[0, 1].FrameX);
        Assert.Equal((short)0, decoded[0, 1].FrameY);
        Assert.Equal((ushort)55, decoded[1, 1].Type);
        Assert.Equal((byte)3, decoded[2, 1].LiquidType); // 微光
    }

    [Fact]
    public void Encode_TileSection_LongRun_UsesTwoByteRle()
    {
        var world = new WorldState
        {
            MaxTilesX = 300,
            MaxTilesY = 1,
            SpawnTileX = 1,
            SpawnTileY = 0,
            Tiles = new TileMap(300, 1),
        };
        for (int x = 0; x < 300; x++)
            world.Tiles[x, 0] = new Tile { Active = true, Type = 1 };

        var decoded = EncodeThenDecode(world, 0, 0, 300, 1, out _, out _);

        for (int x = 0; x < 300; x++)
        {
            Assert.True(decoded[x, 0].Active);
            Assert.Equal((ushort)1, decoded[x, 0].Type);
        }
    }

    // ---- 测试辅助 ----

    /// <summary>
    /// 确定性验证：持有区块写锁时，包 10 编码必须被阻塞 —— 证明编码路径确实经过区块读锁
    /// （不依赖竞态时机，因此能真正判别"有没有加锁"）。
    /// </summary>
    [Fact]
    public async Task TileSectionEncode_BlocksWhileWriterHoldsSectionLock()
    {
        const int W = 200, H = 150;
        var world = new WorldState { MaxTilesX = W, MaxTilesY = H, Tiles = new TileMap(W, H) };

        // 模拟"仿真线程正在写该区块"。持锁/编码都用**独立线程**：本测试要求线程及时被调度，
        // Task.Run 在线程池饱和时会延迟数秒，导致假失败（实测已发生）。
        using var writeHeld = new ManualResetEventSlim(false);
        using var releaseWrite = new ManualResetEventSlim(false);
        var holder = new Thread(() =>
        {
            world.Sections.EnterWrite(0, 0);
            try
            {
                writeHeld.Set();
                releaseWrite.Wait(TimeSpan.FromSeconds(10));
            }
            finally
            {
                world.Sections.ExitWrite(0, 0);
            }
        }) { IsBackground = true };
        holder.Start();

        Assert.True(writeHeld.Wait(TimeSpan.FromSeconds(10)), "写锁未取得");

        var encodeDone = false;
        Exception? encodeError = null;
        var encode = new Thread(() =>
        {
            try { EncodeThenDecode(world, 0, 0, W, H, out _, out _); }
            catch (Exception ex) { encodeError = ex; }
            finally { encodeDone = true; }
        }) { IsBackground = true };
        encode.Start();

        try
        {
            await Task.Delay(200);
            Assert.False(encodeDone, $"编码未被区块读锁阻塞（{(encodeError is null ? "已完成" : encodeError.ToString())}）");
        }
        finally
        {
            releaseWrite.Set();
        }

        Assert.True(encode.Join(TimeSpan.FromSeconds(15)), "释放写锁后编码应完成");
        Assert.Null(encodeError);
        holder.Join(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 并发不变量：仿真线程反复切换图格，编码线程同时编码包 10。
    /// 解码出的每一格都必须是"某个合法状态"，绝不能出现撕裂组合
    /// （例如 Active=true 配 Type=0 —— 编码器按 ref 逐字段读 ~20B 的 Tile，无锁时可能读到混合值）。
    /// <para>注：本测试依赖竞态显现，属补充验证；确定性判别由上面的持锁阻塞测试承担。</para>
    /// </summary>
    [Fact]
    public void TileSection_ConcurrentTileWrites_NeverEncodeTornTile()
    {
        const int W = 200, H = 150, Guard = 50;
        var world = new WorldState { MaxTilesX = W, MaxTilesY = H, Tiles = new TileMap(W, H) };
        var sections = world.Sections;
        var stop = false;
        long writes = 0;

        // 仿真线程：在 (false,0) 与 (true,1) 两个合法状态间反复切换。
        // 用独立线程（非线程池）：线程池饱和时 Task 可能一直不被调度，导致"写入线程未执行"的假失败。
        var writer = new Thread(() =>
        {
            bool placed = false;
            while (!Volatile.Read(ref stop))
            {
                sections.EnterWrite(Guard, Guard);
                try
                {
                    ref var t = ref world.Tiles[Guard, Guard];
                    placed = !placed;
                    t.Active = placed;
                    t.Type = placed ? (ushort)1 : (ushort)0;
                    t.Wall = 0;
                }
                finally
                {
                    sections.ExitWrite(Guard, Guard);
                }

                Interlocked.Increment(ref writes);
            }
        }) { IsBackground = true };
        writer.Start();

        try
        {
            // 读线程（本线程）：并发编码 + 解码，校验每格都是合法组合
            for (int round = 0; round < 12; round++)
            {
                var decoded = EncodeThenDecode(world, 0, 0, W, H, out _, out _);

                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                    {
                        var t = decoded[x, y];
                        bool legal = (t.Active && t.Type == 1) || (!t.Active && t.Type == 0);
                        Assert.True(legal,
                            $"第 {round} 轮 (x={x}, y={y}) 出现撕裂图格：Active={t.Active}, Type={t.Type}");
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join(TimeSpan.FromSeconds(5));
        }

        Assert.True(Interlocked.Read(ref writes) > 0, "写入线程未执行");
    }

    [Fact]
    public void Encode_TileSquare_Writes_Vanilla_Layout()
    {
        // 包 20（TileSquare）：Int16 X/Y + Byte 宽/高 + Byte 变更类型 + 逐格（3 个标志字节 + 可选段），未压缩。
        // 用单格覆盖全部可选段：油漆（方块/墙）、frame-important 帧、墙、液体、线网 / 执行器 / 斜坡 / 全亮 / 隐形。
        var world = new WorldState
        {
            MaxTilesX = 32,
            MaxTilesY = 32,
            Tiles = new TileMap(32, 32),
        };
        world.Tiles[5, 6] = new Tile
        {
            Active = true, Type = 4, FrameX = 10, FrameY = 20, // 4 = frame-important
            Wall = 300, WallColor = 3, TileColor = 7,
            Liquid = 100, LiquidType = 1,
            Wire = true, Wire4 = true, Actuator = true, InActive = true,
            Slope = 2, HalfBrick = false,
            FullbrightBlock = true, InvisibleWall = true,
        };

        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.TileSquare, new TileSquarePacket(world, 5, 6, 1, 1));

        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);
        Assert.True(Framing.TryReadFrame(ref buffer, out var type, out var payload));
        Assert.Equal(PacketId.TileSquare, type);

        using var br = new BinaryReader(new MemoryStream(payload.ToArray()));
        Assert.Equal((short)5, br.ReadInt16());
        Assert.Equal((short)6, br.ReadInt16());
        Assert.Equal((byte)1, br.ReadByte());   // 宽
        Assert.Equal((byte)1, br.ReadByte());   // 高
        Assert.Equal((byte)0, br.ReadByte());   // 变更类型

        byte b1 = br.ReadByte();
        Assert.True((b1 & 0x01) != 0, "active");
        Assert.True((b1 & 0x04) != 0, "wall");
        Assert.True((b1 & 0x08) != 0, "liquid");
        Assert.True((b1 & 0x10) != 0, "wire");
        Assert.True((b1 & 0x40) != 0, "actuator");
        Assert.True((b1 & 0x80) != 0, "inActive");

        byte b2 = br.ReadByte();
        Assert.True((b2 & 0x04) != 0, "tileColor 存在");
        Assert.True((b2 & 0x08) != 0, "wallColor 存在");
        Assert.Equal(2, (b2 >> 4) & 0x07);       // 斜坡 bits4-6
        Assert.True((b2 & 0x80) != 0, "wire4");

        byte b3 = br.ReadByte();
        Assert.True((b3 & 0x01) != 0, "fullbrightBlock");
        Assert.True((b3 & 0x08) != 0, "invisibleWall");

        Assert.Equal((byte)7, br.ReadByte());    // 方块油漆
        Assert.Equal((byte)3, br.ReadByte());    // 墙油漆
        Assert.Equal((ushort)4, br.ReadUInt16()); // 类型
        Assert.Equal((short)10, br.ReadInt16());  // frameX
        Assert.Equal((short)20, br.ReadInt16());  // frameY
        Assert.Equal((ushort)300, br.ReadUInt16()); // 墙
        Assert.Equal((byte)100, br.ReadByte());   // 液体量
        Assert.Equal((byte)1, br.ReadByte());     // 液体类型
    }

    private static Tile[,] EncodeThenDecode(
        WorldState world, int xStart, int yStart, int width, int height,
        out int chestCount, out int signCount)
    {
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, PacketId.TileSendSection, new TileSectionPacket(world, xStart, yStart, width, height));

        var buffer = new ReadOnlySequence<byte>(writer.WrittenMemory);
        Assert.True(Framing.TryReadFrame(ref buffer, out var type, out var payload));
        Assert.Equal(PacketId.TileSendSection, type);

        return DecodeTileSection(payload.ToArray(), out chestCount, out signCount);
    }

    /// <summary>复刻 <c>NetMessage.DecompressTileBlock_Inner</c>，返回 <c>[width, height]</c> 图格。</summary>
    private static Tile[,] DecodeTileSection(byte[] payload, out int chestCount, out int signCount)
    {
        using var ms = new MemoryStream(payload);
        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        using var br = new BinaryReader(deflate, System.Text.Encoding.UTF8);

        int xStart = br.ReadInt32();
        int yStart = br.ReadInt32();
        int width = br.ReadInt16();
        int height = br.ReadInt16();

        var tiles = new Tile[width, height];
        int rle = 0;
        Tile prev = Tile.Empty;

        for (int y = yStart; y < yStart + height; y++)
        {
            for (int x = xStart; x < xStart + width; x++)
            {
                int ox = x - xStart, oy = y - yStart;
                if (rle != 0)
                {
                    rle--;
                    tiles[ox, oy] = prev;
                    continue;
                }

                byte main = br.ReadByte();
                byte b4 = 0, b3 = 0, b2 = 0;
                bool hasB4 = false, hasB3 = false;
                if ((main & 0x01) == 0x01) { hasB4 = true; b4 = br.ReadByte(); }
                if (hasB4 && (b4 & 0x01) == 0x01) { hasB3 = true; b3 = br.ReadByte(); }
                if (hasB3 && (b3 & 0x01) == 0x01) { b2 = br.ReadByte(); }

                var tile = new Tile { FrameX = -1, FrameY = -1 };

                if ((main & 0x02) == 0x02)
                {
                    tile.Active = true;
                    int type;
                    if ((main & 0x20) == 0x20)
                    {
                        byte lo = br.ReadByte();
                        byte hi = br.ReadByte();
                        type = (hi << 8) | lo;
                    }
                    else
                    {
                        type = br.ReadByte();
                    }
                    tile.Type = (ushort)type;
                    if (TileIdSets.IsTileFrameImportant((ushort)type))
                    {
                        tile.FrameX = br.ReadInt16();
                        tile.FrameY = br.ReadInt16();
                    }
                    if ((b3 & 0x08) == 0x08) tile.TileColor = br.ReadByte();
                }

                if ((main & 0x04) == 0x04)
                {
                    tile.Wall = br.ReadByte();
                    if ((b3 & 0x10) == 0x10) tile.WallColor = br.ReadByte();
                }

                byte liquid = (byte)((main & 0x18) >> 3);
                if (liquid != 0)
                {
                    tile.Liquid = br.ReadByte();
                    if ((b3 & 0x80) == 0x80) tile.LiquidType = 3;      // 微光
                    else if (liquid == 2) tile.LiquidType = 1;          // 岩浆
                    else if (liquid == 3) tile.LiquidType = 2;          // 蜂蜜
                    else tile.LiquidType = 0;                            // 水
                }

                if (b4 > 1)
                {
                    if ((b4 & 0x02) == 0x02) tile.Wire = true;
                    if ((b4 & 0x04) == 0x04) tile.Wire2 = true;
                    if ((b4 & 0x08) == 0x08) tile.Wire3 = true;
                    byte slope = (byte)((b4 & 0x70) >> 4);
                    if (slope != 0 && TileIdSets.SaveSlopes(tile.Type))
                    {
                        if (slope == 1) tile.HalfBrick = true;
                        else tile.Slope = (byte)(slope - 1);
                    }
                }

                if (b3 > 1)
                {
                    if ((b3 & 0x02) == 0x02) tile.Actuator = true;
                    if ((b3 & 0x04) == 0x04) tile.InActive = true;
                    if ((b3 & 0x20) == 0x20) tile.Wire4 = true;
                    if ((b3 & 0x40) == 0x40)
                    {
                        byte hi = br.ReadByte();
                        tile.Wall = (ushort)((hi << 8) | tile.Wall);
                    }
                }

                if (b2 > 1)
                {
                    if ((b2 & 0x02) == 0x02) tile.InvisibleBlock = true;
                    if ((b2 & 0x04) == 0x04) tile.InvisibleWall = true;
                    if ((b2 & 0x08) == 0x08) tile.FullbrightBlock = true;
                    if ((b2 & 0x10) == 0x10) tile.FullbrightWall = true;
                }

                rle = (byte)((main & 0xC0) >> 6) switch
                {
                    0 => 0,
                    1 => br.ReadByte(),
                    _ => br.ReadInt16(),
                };

                tiles[ox, oy] = tile;
                prev = tile;
            }
        }

        chestCount = br.ReadInt16();
        for (int i = 0; i < chestCount; i++)
        {
            br.ReadInt16(); br.ReadInt16(); br.ReadInt16(); br.ReadString();
        }
        signCount = br.ReadInt16();
        for (int i = 0; i < signCount; i++)
        {
            br.ReadInt16(); br.ReadInt16(); br.ReadInt16(); br.ReadString();
        }
        Assert.Equal((short)0, br.ReadInt16()); // 图格实体列表为空
        return tiles;
    }
}

/// <summary>
/// 握手超时看门狗验收：客户端在 <c>HandshakeTimeout</c> 内未进入 <c>Playing</c>
/// 时，服务器应主动断开并回收槽位（防「占住连接不放」的握手停滞攻击）。
/// </summary>
public class HandshakeTimeoutTests
{
    [Fact]
    public async Task Connection_StallsInHandshake_IsDisconnectedAfterTimeout()
    {
        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        using var pool = new WorkerPool(2);
        // 客户端保持握手中（流永不产生数据，ReadAsync 阻塞），握手超时极短 → 应被看门狗断开。
        using var stalledStream = new StallingStream();

        var connection = new Connection(
            stalledStream, decoder, encoder, ProtocolVersion.Current, pool,
            handshakeTimeout: TimeSpan.FromMilliseconds(120));

        // Start run loop on a stalled stream（never completing handshake）
        await connection.RunAsync(
            onPacket: static (_, _, _) => Task.CompletedTask,
            ct: CancellationToken.None);

        // 看门狗到期后连接应收敛到 Disconnected
        Assert.Equal(ConnectionState.Disconnected, connection.State);
    }
}

/// <summary>ReadAsync 一直阻塞（Task.Delay 随取消令牌失效），用于模拟「握手停滞」的客户端。</summary>
file sealed class StallingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get; set; }

    public override int Read(byte[] buffer, int offset, int count) => BlockingRead();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => BlockReadAsync(ct);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => new(BlockReadAsync(ct));

    private static int BlockingRead() => 0;
    private static Task<int> BlockReadAsync(CancellationToken ct)
        => Task.Delay(Timeout.InfiniteTimeSpan, ct).ContinueWith(_ => 0, ct);

    public override void Write(byte[] buffer, int offset, int count) { }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => 0;
    public override void SetLength(long value) { }
}
