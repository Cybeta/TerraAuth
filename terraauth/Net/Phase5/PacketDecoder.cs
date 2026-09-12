// TerraAuth — Phase 5: 数据包解码器
// 字节流 → INetworkPacket
// 布局权威来源：原版客户端收包读取的字段顺序与类型（Terraria 1.4.5.8 / Protocol 326）

using System.Buffers;    // ReadOnlySequence<byte>
using System.IO;
using TerraAuth.Protocol;   // Phase 2 的 IPacket
using TerraAuth.Simulation; // SnapshotFrame（Phase 3/4）

namespace TerraAuth.Net.Phase5;

/// <summary>
/// 解码上下文：一次连接内共享的读取状态。
/// </summary>
public sealed class DecodeContext
{
    public ProtocolVersion Version { get; init; } = ProtocolVersion.Current;

    /// <summary>
    /// 解码方向：true 表示解「服务端 → 客户端」形态。仅包 82（NetTextModule）上下行负载不同，需据此区分。
    /// 入站（服务端解客户端）保持默认 false。
    /// </summary>
    public bool ServerToClient { get; init; }
}

/// <summary>
/// 包 15（Snapshot）的强类型包装。
/// 快照结构 <see cref="SnapshotFrame"/> 定义在 Simulation 层，Protocol 层不可反向依赖，
/// 故该包装置于网络层，仅承担「包类型 + 快照」的桥接。
/// </summary>
public sealed record SnapshotPacket(SnapshotFrame Frame) : INetworkPacket
{
    public PacketId Type => PacketId.Snapshot;
}

/// <summary>
/// Terraria 协议解码器。
/// 将 [PacketId + payload] 解析为强类型 INetworkPacket。
/// </summary>
public interface IPacketDecoder
{
    /// <summary>
    /// 从字节缓冲区解码一帧。
    /// </summary>
    INetworkPacket Decode(PacketId type, ReadOnlySpan<byte> payload, DecodeContext context);

    /// <summary>
    /// 尝试读取并解码一帧（自动处理分帧）。
    /// </summary>
    bool TryDecodeFrame(
        ref ReadOnlySequence<byte> buffer,
        DecodeContext context,
        out INetworkPacket? packet);
}

public sealed class PacketDecoder : IPacketDecoder
{
    public INetworkPacket Decode(PacketId type, ReadOnlySpan<byte> payload, DecodeContext context)
    {
        // terraria-protocol 使用小端字节序 + 特定布局
        using var ms = new MemoryStream(payload.ToArray());
        using var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: false);

        return type switch
        {
            PacketId.ConnectionRequest => DecodeConnectionRequest(reader),
            PacketId.PlayerInfo        => DecodePlayerInfo(reader),
            PacketId.InventorySlot     => DecodeInventorySlot(reader),
            PacketId.RequestWorldInfo  => new RequestWorldInfoPacket(),
            PacketId.TileGetSection    => DecodeSpawnTileData(reader),
            PacketId.StatusText        => DecodeStatusText(reader),
            PacketId.PlayerSpawn       => DecodePlayerSpawn(reader),
            PacketId.InitialSpawn      => new InitialSpawnPacket(),
            PacketId.FinishedConnecting => new FinishedConnectingPacket(),
            PacketId.PlayerPosition    => DecodePlayerControls(reader),
            PacketId.PlayerActive      => DecodePlayerActive(reader),
            PacketId.Snapshot          => new SnapshotPacket(DecodeSnapshot(payload)),
            PacketId.NpcStrike         => DecodeNpcStrike(reader),
            PacketId.PlayerHealth      => DecodePlayerHealth(reader),
            PacketId.PlayerMana        => DecodePlayerMana(reader),
            PacketId.TileBreak         => DecodeTileBreak(reader),
            PacketId.TilePlace         => DecodeTilePlace(reader),
            PacketId.ItemDrop          => DecodeSyncItem(reader),
            PacketId.ItemPickup        => DecodeItemPickup(reader),
            PacketId.SyncChestItem     => DecodeSyncChestItem(reader),
            PacketId.SyncPlayerChestIndex => DecodePlayerChestIndex(reader),
            PacketId.PlayerHeal        => DecodePlayerHeal(reader),
            PacketId.SyncPlayerZone    => DecodeSyncPlayerZone(reader),
            PacketId.PlayerBuffs       => DecodePlayerBuffs(reader),
            PacketId.TeleportEntity    => DecodeTeleportEntity(reader),
            PacketId.RequestTeleportationByServer => DecodeRequestTeleportationByServer(reader),
            PacketId.PlayerHurtV2      => DecodePlayerHurtV2(reader),
            PacketId.PlayerDeathV2     => DecodePlayerDeathV2(reader),
            PacketId.ProjectileNew     => DecodeProjectileNew(reader),
            PacketId.Chest             => DecodeChest(reader),
            PacketId.ProjectileDestroy => DecodeProjectileDestroy(reader),
            PacketId.Time              => DecodeTime(reader),
            PacketId.NpcUpdate         => DecodeNpcUpdate(reader),
            PacketId.NetModule         => DecodeNetModule(reader, context, payload),
            PacketId.Disconnect        => payload.Length == 0
                ? DisconnectPacket.Instance
                : DecodeDisconnect(reader),

            // 其余包统一透传为 UnknownPacket：保留原始 PacketId 与 payload，
            // 编码侧原样转发，无需逐包建模；仅权威校验所需的包在此显式结构化。
            // 如需结构化某包，先确认该包在客户端读取侧的字段顺序与类型（类型以读取侧为准）。
            _ => new UnknownPacket(type, payload.ToArray()),
        };
    }

    // ---------- 时间 / NPC / 聊天（包 18 / 23 / 82） ----------

    /// <summary>KillProjectile（包 29）：Int32 弹幕键 + Vector2 位置。</summary>
    private static ProjectileDestroyPacket DecodeProjectileDestroy(BinaryReader r)
        => new(r.ReadInt32(), new Vector2(r.ReadSingle(), r.ReadSingle()));

    /// <summary>Time（包 18）：Byte dayTime + Int32 time + Int16 sunModY + Int16 moonModY。</summary>
    private static TimePacket DecodeTime(BinaryReader r)
        => new(DayTime: r.ReadByte() == 1, Time: r.ReadInt32(), SunModY: r.ReadInt16(), MoonModY: r.ReadInt16());

    /// <summary>
    /// SyncNPC（包 23）：按条件位读取 ai / 玩家数 / 难度 / 生命等可选段。
    /// 本解码器只取「索引 / generation / 位置 / 速度 / 目标 / netID」等核心字段，其余段读到即跳过。
    /// </summary>
    private static NpcUpdatePacket DecodeNpcUpdate(BinaryReader r)
    {
        var index = r.ReadByte();
        var generation = r.ReadByte();
        var position = new Vector2(r.ReadSingle(), r.ReadSingle());
        var velocity = new Vector2(r.ReadSingle(), r.ReadSingle());
        var target = r.ReadUInt16();

        var bitsA = r.ReadByte();
        var bitsB = r.ReadByte();

        for (var i = 0; i < 4; i++)
        {
            if ((bitsA & (1 << (i + 2))) != 0) r.ReadSingle(); // 各 ai 存在时才写入
        }

        var netId = r.ReadInt16();

        if ((bitsB & 0x01) != 0) r.ReadByte();      // 玩家数缩放
        if ((bitsB & 0x04) != 0) r.ReadSingle();    // 难度覆盖
        if ((bitsA & 0x80) == 0)                    // 非满血 → 有生命段
        {
            _ = r.ReadByte() switch { 2 => (int)r.ReadInt16(), 4 => r.ReadInt32(), _ => (int)r.ReadSByte() };
        }

        return new NpcUpdatePacket(index, generation, position, velocity, target, netId);
    }

    /// <summary>
    /// NetModule（包 82）：按模块号分派。
    ///   模块 0 = NetLiquidModule（液体变更）；模块 1 = NetTextModule（聊天）。
    /// 未建模模块统一透传为 <see cref="UnknownPacket"/>。
    /// </summary>
    private static INetworkPacket DecodeNetModule(BinaryReader r, DecodeContext context, ReadOnlySpan<byte> payload)
    {
        const ushort netLiquidModuleId = 0; // NetLiquidModule
        const ushort netTextModuleId = 1;   // NetLiquidModule=0 → NetTextModule=1

        var moduleId = r.ReadUInt16();

        if (moduleId == netLiquidModuleId)
        {
            // 液体：UInt16 条目数 + 条目 ×（Int16 X + Int16 Y + Byte 液体量 + Byte 液体类型）
            int count = r.ReadUInt16();
            var changes = new List<LiquidChange>(count);
            for (int i = 0; i < count; i++)
                changes.Add(new LiquidChange(r.ReadInt16(), r.ReadInt16(), r.ReadByte(), r.ReadByte()));
            return new LiquidModulePacket(changes) { IsClientMessage = !context.ServerToClient };
        }

        if (moduleId != netTextModuleId)
            return new UnknownPacket(PacketId.NetModule, payload.ToArray()); // 其他模块不解析，原样透传

        if (context.ServerToClient)
        {
            var authorId = r.ReadByte();
            _ = r.ReadByte();                       // NetworkText 模式（本实现仅处理 Literal）
            var text = r.ReadString();
            var color = new RgbColor(r.ReadByte(), r.ReadByte(), r.ReadByte());
            return new NetTextPacket(text) { AuthorId = authorId, Color = color };
        }

        var commandName = r.ReadString();
        var message = r.ReadString();
        return new NetTextPacket(message) { IsClientMessage = true, CommandName = commandName };
    }

    public bool TryDecodeFrame(
        ref ReadOnlySequence<byte> buffer,
        DecodeContext context,
        out INetworkPacket? packet)
    {
        packet = null;
        if (!Framing.TryReadFrame(ref buffer, out var type, out var payload))
            return false;

        packet = Decode(type, payload.ToArray(), context);
        return true;
    }

    // ---------- 各包解析（占位，需对接真实协议布局） ----------

    private INetworkPacket DecodeConnectionRequest(BinaryReader r)
    {
        // ConnectRequest：版本串（7-bit 长度前缀 + UTF-8），例如 "Terraria326"
        var version = ReadPrefixedString(r);
        return new ConnectionRequestPacket(version);
    }

    private INetworkPacket DecodeDisconnect(BinaryReader r)
    {
        // Kick：NetworkText（mode 字节 + 字符串）。mode=0 为字面量。
        r.ReadByte(); // mode，暂只处理字面量
        var reason = ReadPrefixedString(r);
        return DisconnectPacket.WithReason(reason);
    }

    private INetworkPacket DecodePlayerInfo(BinaryReader r)
    {
        // SyncPlayer（包 4）：字段顺序严格对应 MessageBuffer case 4 / NetMessage.SendData case 4
        var slot = r.ReadByte();
        var skinVariant = r.ReadByte();
        var voiceVariant = r.ReadByte();
        var voicePitchOffset = r.ReadSingle();
        var hair = r.ReadByte();
        var name = ReadPrefixedString(r);
        var hairDye = r.ReadByte();
        var hideVisibleAccessory = r.ReadUInt16(); // ReadAccessoryVisibility：UInt16 位掩码
        var hideMisc = r.ReadByte();

        var hairColor = ReadRgb(r);
        var skinColor = ReadRgb(r);
        var eyeColor = ReadRgb(r);
        var shirtColor = ReadRgb(r);
        var underShirtColor = ReadRgb(r);
        var pantsColor = ReadRgb(r);
        var shoeColor = ReadRgb(r);

        var difficultyBits = r.ReadByte();
        byte difficulty = 0;
        if ((difficultyBits & (1 << 0)) != 0) difficulty = 1; // 中核
        if ((difficultyBits & (1 << 1)) != 0) difficulty = 2; // 硬核
        if ((difficultyBits & (1 << 3)) != 0) difficulty = 3; // 旅途
        bool extraAccessory = (difficultyBits & (1 << 2)) != 0;

        var torchFlags = r.ReadByte();
        var unlockFlags = r.ReadByte();

        return new PlayerInfoPacket(slot, name)
        {
            SkinVariant = skinVariant,
            VoiceVariant = voiceVariant,
            VoicePitchOffset = voicePitchOffset,
            Hair = hair,
            HairDye = hairDye,
            HideVisibleAccessory = hideVisibleAccessory,
            HideMisc = hideMisc,
            HairColor = hairColor,
            SkinColor = skinColor,
            EyeColor = eyeColor,
            ShirtColor = shirtColor,
            UnderShirtColor = underShirtColor,
            PantsColor = pantsColor,
            ShoeColor = shoeColor,
            Difficulty = difficulty,
            ExtraAccessory = extraAccessory,
            TorchFlags = torchFlags,
            UnlockFlags = unlockFlags,
        };
    }

    private INetworkPacket DecodeSpawnTileData(BinaryReader r)
    {
        // SpawnTileData（包 8）：Int32 SpawnX + Int32 SpawnY + Byte Team
        var spawnX = r.ReadInt32();
        var spawnY = r.ReadInt32();
        var team = r.ReadByte();
        return new SpawnTileDataPacket(spawnX, spawnY, team);
    }

    private INetworkPacket DecodeStatusText(BinaryReader r)
    {
        // StatusTextSize（包 9）：Int32 StatusMax + NetworkText + BitsByte
        var statusMax = r.ReadInt32();
        var text = ReadNetworkText(r);
        var flags = r.ReadByte();
        return new StatusTextPacket(statusMax, text, flags);
    }

    private INetworkPacket DecodePlayerSpawn(BinaryReader r)
    {
        // PlayerSpawn（包 12）：Byte + Int16 + Int16 + Int32 + Int16 + Int16 + Byte + Byte
        var playerId = r.ReadByte();
        var spawnX = r.ReadInt16();
        var spawnY = r.ReadInt16();
        var respawnTimer = r.ReadInt32();
        var deathsPve = r.ReadInt16();
        var deathsPvp = r.ReadInt16();
        var team = r.ReadByte();
        var spawnContext = r.ReadByte();
        return new PlayerSpawnPacket(playerId, spawnX, spawnY, respawnTimer, deathsPve, deathsPvp, team, spawnContext);
    }

    private INetworkPacket DecodeInventorySlot(BinaryReader r)
    {
        // SyncEquipment（包 5）：Byte id + Int16 slot + Int16 stack + Byte prefix + Int16 type + BitsByte
        var playerId = r.ReadByte();
        var slot = r.ReadInt16();
        var stack = r.ReadInt16();
        var prefix = r.ReadByte();
        var itemType = r.ReadInt16();
        var flags = r.ReadByte(); // bit0 = favorited，bit1 = blocked
        return new InventorySlotPacket(slot, itemType, stack)
        {
            PlayerId = playerId,
            Prefix = prefix,
            Favorited = (flags & (1 << 0)) != 0,
        };
    }

    private INetworkPacket DecodePlayerControls(BinaryReader r)
    {
        // PlayerControls（包 13）：Byte id + BitsByte×4 + Byte selectedItem + Vector2 position
        //   + 可选 Vector2 velocity（StateBits bit2）
        //   + 可选 UInt16 mount（StateBits bit7）
        //   + 可选 Vector2×2 回城（StateBits2 bit6）
        //   + 可选 Vector2 相机（StateBits3 bit5）
        var playerId = r.ReadByte();
        var controlBits = r.ReadByte();
        var stateBits = r.ReadByte();
        var stateBits2 = r.ReadByte();
        var stateBits3 = r.ReadByte();
        var selectedItem = r.ReadByte();
        var position = new Vector2(r.ReadSingle(), r.ReadSingle());

        var velocity = default(Vector2);
        var hasVelocity = (stateBits & 0x04) != 0;
        if (hasVelocity)
            velocity = new Vector2(r.ReadSingle(), r.ReadSingle());

        // 以下三个尾随段必须**读取并保留**：只跳过会让转发时丢失（他人看不到坐骑 / 相机 / 回城），
        // 而编码侧又按标志位写字段，标志位与负载不一致会整条连接错位。
        ushort? mountType = null;
        if ((stateBits & 0x80) != 0)
            mountType = r.ReadUInt16();

        Vector2? potionOriginal = null, potionHome = null;
        if ((stateBits2 & 0x40) != 0)
        {
            potionOriginal = new Vector2(r.ReadSingle(), r.ReadSingle());
            potionHome = new Vector2(r.ReadSingle(), r.ReadSingle());
        }

        Vector2? cameraTarget = null;
        if ((stateBits3 & 0x20) != 0)
            cameraTarget = new Vector2(r.ReadSingle(), r.ReadSingle());

        return new PlayerControlsPacket(
            playerId, position, velocity, selectedItem, controlBits, stateBits, stateBits2, stateBits3)
        {
            MountType = mountType,
            PotionReturnOriginal = potionOriginal,
            PotionReturnHome = potionHome,
            CameraTarget = cameraTarget,
        };
    }

    private INetworkPacket DecodePlayerActive(BinaryReader r)
    {
        // PlayerActive（包 14）：Byte PlayerId + Byte Active
        var playerId = r.ReadByte();
        var active = r.ReadByte() != 0;
        return new PlayerActivePacket(playerId, active);
    }

    private INetworkPacket DecodeNpcStrike(BinaryReader r)
    {
        // DamageNPC（包 28）：Byte npcId + Byte generation + Int16 damage
        //   + Single knockback + Byte direction + Byte crit
        var npcId = r.ReadByte();
        var generation = r.ReadByte();
        var damage = r.ReadInt16();
        var knockback = r.ReadSingle();
        var direction = r.ReadByte() - 1;
        var crit = r.ReadByte() != 0;
        return new NpcStrikePacket(npcId, damage)
        {
            Generation = generation,
            Knockback = knockback,
            Direction = direction,
            Crit = crit,
        };
    }

    private INetworkPacket DecodePlayerHealth(BinaryReader r)
    {
        // PlayerLifeMana（包 16）：Byte id + Int16 statLife + Int16 statLifeMax
        var playerId = r.ReadByte();
        var hp = r.ReadInt16();
        var maxHp = r.ReadInt16();
        return new PlayerHealthPacket(playerId, hp, maxHp);
    }

    private INetworkPacket DecodePlayerMana(BinaryReader r)
    {
        // PlayerMana（包 42）：Byte id + Int16 statMana + Int16 statManaMax
        var playerId = r.ReadByte();
        var mana = r.ReadInt16();
        var maxMana = r.ReadInt16();
        return new PlayerManaPacket(playerId, mana, maxMana);
    }

    private INetworkPacket DecodeTileBreak(BinaryReader r)
    {
        // TileManipulation（包 17）：Byte action + Int16 x + Int16 y + Int16 tileType + Byte style
        var action = r.ReadByte();
        var x = r.ReadInt16();
        var y = r.ReadInt16();
        var tileType = r.ReadInt16();
        var style = r.ReadByte();
        return new TileBreakPacket(x, y, action)
        {
            TileType = tileType,
            Style = style,
        };
    }

    private INetworkPacket DecodeTilePlace(BinaryReader r)
    {
        // PlaceObject（包 79）：Int16 x + Int16 y + Int16 type + Int16 style
        //   + Byte alternate + SByte random + Boolean direction（解码后 1 / -1）
        var x = r.ReadInt16();
        var y = r.ReadInt16();
        var tileType = r.ReadInt16();
        var style = r.ReadInt16();
        var alternate = r.ReadByte();
        var random = r.ReadSByte();
        var direction = r.ReadBoolean() ? 1 : -1;
        return new TilePlacePacket(x, y, tileType)
        {
            Style = style,
            Alternate = alternate,
            Random = random,
            Direction = direction,
        };
    }

    private INetworkPacket DecodeProjectileNew(BinaryReader r)
    {
        // SyncProjectile（包 27）：Int32 key + Vector2 pos + Vector2 vel + Int16 type
        //   + BitsByte flags1 + 可选 BitsByte flags2（flags1 bit2）
        //   + 依 flags1 按序：ai[0](Single) / ai[1](Single) / banner(UInt16) / damage(Int16)
        //     / knockBack(Single) / originalDamage(Int16) / ai[2](Single，flags2 bit0)
        var key = r.ReadInt32();
        var position = new Vector2(r.ReadSingle(), r.ReadSingle());
        var velocity = new Vector2(r.ReadSingle(), r.ReadSingle());
        var projectileType = r.ReadInt16();
        var flags1 = r.ReadByte();
        var flags2 = (flags1 & 0x04) != 0 ? r.ReadByte() : (byte)0;

        var ai0 = (flags1 & 0x01) != 0 ? r.ReadSingle() : 0f;
        var ai1 = (flags1 & 0x02) != 0 ? r.ReadSingle() : 0f;
        var banner = (flags1 & 0x08) != 0 ? r.ReadUInt16() : (ushort)0;
        var damage = (flags1 & 0x10) != 0 ? r.ReadInt16() : (short)0;
        var knockback = (flags1 & 0x20) != 0 ? r.ReadSingle() : 0f;
        var originalDamage = (flags1 & 0x40) != 0 ? r.ReadInt16() : (short)0;
        var ai2 = (flags2 & 0x01) != 0 ? r.ReadSingle() : 0f;

        return new ProjectileNewPacket(key, position, velocity, projectileType)
        {
            Ai0 = ai0,
            Ai1 = ai1,
            Ai2 = ai2,
            BannerIdToRespondTo = banner,
            Damage = damage,
            Knockback = knockback,
            OriginalDamage = originalDamage,
        };
    }

    private INetworkPacket DecodeChest(BinaryReader r)
    {
        // RequestChestOpen（包 31）：Int16 x + Int16 y（目标箱子图格坐标）
        var x = r.ReadInt16();
        var y = r.ReadInt16();
        return new ChestPacket(x, y);
    }

    private INetworkPacket DecodeSyncItem(BinaryReader r)
    {
        // SyncItem（包 21）：Int16 slot + Vector2 pos + Vector2 vel + Int16 stack + Byte prefix
        //   + BitsByte flags + Int16 type
        //   + 可选 Boolean shimmered + Single shimmerTime（flags bit2）
        //   + 可选 Byte enemyGrabDelayTime（flags bit3）
        var slotIndex = r.ReadInt16();
        var position = new Vector2(r.ReadSingle(), r.ReadSingle());
        var velocity = new Vector2(r.ReadSingle(), r.ReadSingle());
        var stack = r.ReadInt16();
        var prefix = r.ReadByte();
        var flags = r.ReadByte();
        var itemType = r.ReadInt16();

        var shimmered = false;
        var shimmerTime = 0f;
        if ((flags & 0x04) != 0)
        {
            shimmered = r.ReadByte() != 0;
            shimmerTime = r.ReadSingle();
        }
        var enemyGrabDelayTime = (flags & 0x08) != 0 ? r.ReadByte() : (byte)0;

        return new ItemDropPacket(itemType, stack)
        {
            ItemSlotIndex = slotIndex,
            Position = position,
            Velocity = velocity,
            Prefix = prefix,
            Shimmered = shimmered,
            ShimmerTime = shimmerTime,
            EnemyGrabDelayTime = enemyGrabDelayTime,
        };
    }

    private INetworkPacket DecodeItemPickup(BinaryReader r)
    {
        // SyncItemOwner（包 22）：Int16 世界物品槽位 + Byte 归属玩家
        var itemSlot = r.ReadInt16();
        var playerId = r.ReadByte();
        return new ItemPickupPacket(itemSlot) { PlayerId = playerId };
    }

    private INetworkPacket DecodeSyncChestItem(BinaryReader r)
    {
        // SyncChestItem（包 32）：Int16 chestIndex + Byte slot + Int16 stack + Byte prefix + Int16 type
        var chestIndex = r.ReadInt16();
        var itemSlot = r.ReadByte();
        var stack = r.ReadInt16();
        var prefix = r.ReadByte();
        var itemType = r.ReadInt16();
        return new SyncChestItemPacket(chestIndex, itemSlot, stack, prefix, itemType);
    }

    private INetworkPacket DecodePlayerChestIndex(BinaryReader r)
    {
        // SyncPlayerChestIndex（包 34）：Byte playerId + Int16 chestIndex
        var playerId = r.ReadByte();
        var chestIndex = r.ReadInt16();
        return new PlayerChestIndexPacket(playerId, chestIndex);
    }

    private INetworkPacket DecodePlayerHeal(BinaryReader r)
    {
        // PlayerHeal（包 35）：Byte playerId + Int16 amount
        var playerId = r.ReadByte();
        var amount = r.ReadInt16();
        return new PlayerHealPacket(playerId, amount);
    }

    private INetworkPacket DecodeSyncPlayerZone(BinaryReader r)
    {
        // SyncPlayerZone（包 36）：Byte playerId + Byte zone1..zone5 + Byte townNPCs
        var playerId = r.ReadByte();
        return new SyncPlayerZonePacket(
            playerId, r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
    }

    private INetworkPacket DecodePlayerBuffs(BinaryReader r)
    {
        // PlayerBuffs（包 50）：Byte playerId + UInt16 增益类型序列（以 0 结束）
        var playerId = r.ReadByte();
        var buffs = new List<int>();
        while (true)
        {
            var buffType = r.ReadUInt16();
            if (buffType == 0)
                break;
            buffs.Add(buffType);
        }
        return new PlayerBuffsPacket(playerId, buffs);
    }

    private INetworkPacket DecodeTeleportEntity(BinaryReader r)
    {
        // TeleportEntity（包 65）：BitsByte flags + Int16 entityId + Vector2 position + Byte style
        //   + 可选 Int32 extraInfo（flags bit3）。
        // flags：bit0/bit1 组合成种类（0=玩家、1=NPC、2=玩家间、3=确认），
        //        bit2=不带位置（服务端以自身记录覆盖），bit3=携带额外 Int32。
        var flags = r.ReadByte();
        var entityId = r.ReadInt16();
        var position = new Vector2(r.ReadSingle(), r.ReadSingle());
        var style = r.ReadByte();
        var extraInfo = (flags & 0x08) != 0 ? r.ReadInt32() : 0;

        var kind = (TeleportEntityKind)(((flags & 0x01) != 0 ? 1 : 0) + ((flags & 0x02) != 0 ? 2 : 0));
        return new TeleportEntityPacket(entityId, position, style)
        {
            Kind = kind,
            NoPosition = (flags & 0x04) != 0,
            ExtraInfo = extraInfo,
        };
    }

    private INetworkPacket DecodeRequestTeleportationByServer(BinaryReader r)
    {
        // RequestTeleportationByServer（包 73）：Byte 传送种类（0=传送药水、1=魔法海螺、
        //   2=恶魔海螺、3=贝壳电话回城、4=无空位传送）。
        return new RequestTeleportationByServerPacket((TeleportRequestKind)r.ReadByte());
    }

    private INetworkPacket DecodePlayerHurtV2(BinaryReader r)
    {
        // PlayerHurtV2（包 117）：Byte playerId + PlayerDeathReason + Int16 damage
        //   + Byte（击退方向+1）+ BitsByte（bit0=暴击、bit1=PvP）+ SByte 免伤冷却计数
        var playerId = r.ReadByte();
        var deathReason = ReadDeathReason(r);
        var damage = r.ReadInt16();
        var hitDirection = r.ReadByte() - 1;
        var flags = r.ReadByte();
        var cooldownCounter = r.ReadSByte();
        return new PlayerHurtV2Packet(playerId, damage)
        {
            DeathReason = deathReason,
            HitDirection = hitDirection,
            Crit = (flags & 0x01) != 0,
            Pvp = (flags & 0x02) != 0,
            CooldownCounter = cooldownCounter,
        };
    }

    private INetworkPacket DecodePlayerDeathV2(BinaryReader r)
    {
        // PlayerDeathV2（包 118）：Byte playerId + PlayerDeathReason + Int16 damage
        //   + Byte（击退方向+1）+ BitsByte（bit0=PvP）
        var playerId = r.ReadByte();
        var deathReason = ReadDeathReason(r);
        var damage = r.ReadInt16();
        var hitDirection = r.ReadByte() - 1;
        var flags = r.ReadByte();
        return new PlayerDeathV2Packet(playerId, damage)
        {
            DeathReason = deathReason,
            HitDirection = hitDirection,
            Pvp = (flags & 0x01) != 0,
        };
    }

    // ---------- 快照（包 15） ----------

    /// <summary>
    /// 解码包 15（Snapshot）载荷 → <see cref="SnapshotFrame"/>。
    /// 布局与 <see cref="PacketEncoder.EncodeSnapshot"/> 一一对应（小端）：
    /// <c>BaseTick(UInt32) + Tick(UInt32) + Checksum(UInt32)
    /// + EntityCount(UInt16) + [Id(Int32) + PosX/PosY/VelX/VelY(Float32) + State(Byte)] × N
    /// + RemovedCount(UInt16) + [Id(Int32) + Reason(Byte)] × M</c>。
    /// 校验和按线上值原样保留（不做重算比对，避免掩盖丢包/损坏的原始信息）。
    /// </summary>
    public SnapshotFrame DecodeSnapshot(ReadOnlySpan<byte> payload)
    {
        using var ms = new MemoryStream(payload.ToArray());
        using var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: false);
        return DecodeSnapshot(reader);
    }

    private static SnapshotFrame DecodeSnapshot(BinaryReader r)
    {
        var baseTick = r.ReadUInt32();
        var tick = r.ReadUInt32();
        var checksum = r.ReadUInt32();

        int entityCount = r.ReadUInt16();
        var entities = new List<EntityState>(entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            int id = r.ReadInt32();
            float px = r.ReadSingle();
            float py = r.ReadSingle();
            float vx = r.ReadSingle();
            float vy = r.ReadSingle();
            var state = (EntityStateType)r.ReadByte();
            entities.Add(new EntityState(id, new Vector2(px, py), new Vector2(vx, vy), state));
        }

        int removedCount = r.ReadUInt16();
        var removed = new List<RemovedEntity>(removedCount);
        for (int i = 0; i < removedCount; i++)
        {
            int id = r.ReadInt32();
            var reason = (RemoveReason)r.ReadByte();
            removed.Add(new RemovedEntity(id, reason));
        }

        return SnapshotFrame.Create(tick, entities, removed, baseTick: baseTick, checksum: checksum);
    }

    // ---------- 辅助 ----------

    /// <summary>读取 RGB 颜色（对接 <c>BinaryReader.ReadRGB</c>：R/G/B 各 1 字节）。</summary>
    private static RgbColor ReadRgb(BinaryReader r) =>
        new(r.ReadByte(), r.ReadByte(), r.ReadByte());

    /// <summary>
    /// 读取死亡原因（等价原版 <c>PlayerDeathReason.FromReader</c>）：
    /// 8 位标志 + 依标志存在的条件字段（Int16/Byte/字符串）。
    /// </summary>
    private static PlayerDeathReasonData ReadDeathReason(BinaryReader r)
    {
        var bits = r.ReadByte();
        return new PlayerDeathReasonData
        {
            SourcePlayerIndex = (bits & 0x01) != 0 ? r.ReadInt16() : -1,
            SourceNpcIndex = (bits & 0x02) != 0 ? r.ReadInt16() : -1,
            SourceProjectileLocalIndex = (bits & 0x04) != 0 ? r.ReadInt16() : -1,
            SourceOtherIndex = (bits & 0x08) != 0 ? r.ReadByte() : -1,
            SourceProjectileType = (bits & 0x10) != 0 ? r.ReadInt16() : 0,
            SourceItemType = (bits & 0x20) != 0 ? r.ReadInt16() : 0,
            SourceItemPrefix = (bits & 0x40) != 0 ? r.ReadByte() : 0,
            CustomReason = (bits & 0x80) != 0 ? ReadPrefixedString(r) : null,
        };
    }

    /// <summary>
    /// 读取 NetworkText 字面量（mode 字节 + 7-bit 长度前缀字符串）。仅处理 Literal(0)。
    /// </summary>
    internal static string ReadNetworkText(BinaryReader r)
    {
        r.ReadByte(); // NetworkText.Mode，暂只处理 Literal
        return ReadPrefixedString(r);
    }

    /// <summary>
    /// 读取 Terraria 字符串：7-bit 变长编码长度前缀 + UTF-8 字节。
    /// 与 .NET <see cref="BinaryReader.ReadString"/> / <see cref="BinaryWriter.Write(string)"/> 的线格式一致。
    /// </summary>
    internal static string ReadPrefixedString(BinaryReader r)
    {
        var len = Read7BitEncodedInt(r);
        var bytes = r.ReadBytes(len);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 读取 7-bit 变长编码整数（每字节最高位 0x80 表示续字节，最多 5 字节）。
    /// </summary>
    internal static int Read7BitEncodedInt(BinaryReader r)
    {
        int result = 0;
        int shift = 0;
        while (shift < 35)
        {
            byte b = r.ReadByte();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
        throw new FormatException("Too many bytes in 7-bit encoded int.");
    }
}
