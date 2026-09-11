// TerraAuth — 原版功能端到端测试
// 与 IntegrationTests 的区别：此处使用 **GameHost.Bootstrap 组装的真实权威管线**（六子系统 + 限流）
// 驱动 **真实 TCP 连接**，逐项验证原版客户端会用到的功能是否真正生效。
// 观测手段三类：
//   1) 客户端收到的出站包（登录链 / 广播 / 纠正）
//   2) 服务端权威世界状态（WorldSimulator.State：玩家位置 / 图格）
//   3) 拒绝原因计数器（PrometheusMetrics.ExportAsText，权威层 Reject → OnViolation → 计数）
//
// 注意：多客户端测试必须连到**同一个** VanillaServer（同一 GameHost），否则广播不会互通。

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using TerraAuth.Monitoring;
using TerraAuth.Net.Phase5;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class VanillaFeatureTests
{
    // ========================================================================
    // 服务端夹具：一个 GameHost（真实管线）可接受多个 TCP 客户端
    // ========================================================================
    private sealed class VanillaServer : IDisposable
    {
        private readonly string _dir;

        public GameHost Host { get; }
        private int Port => Host.Network.BoundPort;

        private VanillaServer(GameHost host, string dir)
        {
            Host = host;
            _dir = dir;
        }

        /// <summary>启动服务端（port 0 = OS 分配端口，避免测试间抢占）。</summary>
        public static VanillaServer Start(string? configJson = null)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"terraauth-vanilla-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var configPath = Path.Combine(dir, "server.json");
            if (configJson is not null) File.WriteAllText(configPath, configJson);

            var host = GameHost.Bootstrap(Path.Combine(dir, "state.db"), configPath, metricsPort: 0, port: 0);
            host.Network.Start();
            return new VanillaServer(host, dir);
        }

        /// <summary>接入一个客户端并完成登录链（1→4→6→8→12）至 Playing。</summary>
        public async Task<VanillaSession> ConnectAsync(string playerName)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, Port);
            var session = new VanillaSession(client);
            await session.HandshakeAsync(playerName);
            return session;
        }

        public void Dispose()
        {
            Host.Dispose();
            try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败可忽略 */ }
        }
    }

    // ========================================================================
    // 客户端会话：一个 TCP 连接 + 收发 / 等待工具
    // ========================================================================
    private sealed class VanillaSession : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly List<byte> _pending = new(); // 跨多次读取保留的半包
        private bool _disposed;

        public NetworkStream Stream { get; }
        public PacketEncoder Encoder { get; } = new(new TerrariaProtocol().Version);
        public PacketDecoder Decoder { get; } = new();

        /// <summary>握手期间（1→4→6→8→12）收到的全部出站包。</summary>
        public List<INetworkPacket> HandshakePackets { get; private set; } = new();

        public VanillaSession(TcpClient client)
        {
            _client = client;
            Stream = client.GetStream();
        }

        // ---- 出站 ----
        public Task SendAsync(PacketId type, INetworkPacket packet)
        {
            var writer = new ArrayBufferWriter<byte>();
            Encoder.Encode(writer, type, packet);
            return Stream.WriteAsync(writer.WrittenMemory).AsTask();
        }

        public Task SendRawAsync(PacketId type, ReadOnlyMemory<byte> payload)
        {
            var writer = new ArrayBufferWriter<byte>();
            Framing.WriteFrame(writer, type, payload.Span);
            return Stream.WriteAsync(writer.WrittenMemory).AsTask();
        }

        /// <summary>包 1 payload：7-bit 长度前缀 + UTF-8 版本串（"Terraria326"）。</summary>
        public static byte[] VersionPayload(string version)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                PacketEncoder.WritePrefixedString(bw, version);
            return ms.ToArray();
        }

        // ---- 入站 ----
        /// <summary>按原版顺序完成登录链：1 → 4 → 6 → 8 → 12，并收集到包 129 为止的出站包。</summary>
        public async Task HandshakeAsync(string name)
        {
            var connect = new TerrariaProtocol().Version.ConnectVersion;
            await SendRawAsync(PacketId.ConnectionRequest, VersionPayload(connect));
            await SendAsync(PacketId.PlayerInfo, new PlayerInfoPacket(0, name));
            await SendRawAsync(PacketId.RequestWorldInfo, ReadOnlyMemory<byte>.Empty);
            await SendAsync(PacketId.TileGetSection, new SpawnTileDataPacket(-1, -1, 0));
            await SendAsync(PacketId.PlayerSpawn, new PlayerSpawnPacket(0, 0, 0, 0, 0, 0, 0, 0));

            HandshakePackets = await CollectUntilAsync(p => p is FinishedConnectingPacket, TimeSpan.FromSeconds(10));
        }

        /// <summary>读取直到出现匹配包（含）；超时或断开返回已收取的包。</summary>
        public Task<List<INetworkPacket>> ReadUntilAsync(Func<INetworkPacket, bool> match, TimeSpan timeout)
            => CollectUntilAsync(match, timeout);

        private async Task<List<INetworkPacket>> CollectUntilAsync(
            Func<INetworkPacket, bool> match, TimeSpan timeout)
        {
            var got = new List<INetworkPacket>();
            using var cts = new CancellationTokenSource(timeout);
            var temp = new byte[16384];
            try
            {
                while (true)
                {
                    // 先把已缓冲数据全部解帧（_pending 跨调用保留，避免丢失已读字节）
                    while (true)
                    {
                        var buffer = new ReadOnlySequence<byte>(_pending.ToArray());
                        if (!Framing.TryReadFrame(ref buffer, out var type, out var payload)) break;
                        _pending.RemoveRange(0, _pending.Count - (int)buffer.Length);

                        var pkt = Decoder.Decode(type, payload.ToArray(),
                            new DecodeContext { ServerToClient = true }); // 本会话是客户端，收到的都是下行包
                        got.Add(pkt);
                        if (match(pkt)) return got;
                    }

                    var read = await Stream.ReadAsync(temp, cts.Token).ConfigureAwait(false);
                    if (read == 0) return got; // 服务端关闭
                    _pending.AddRange(temp[..read]);
                }
            }
            catch (OperationCanceledException)
            {
                return got;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    // ========================================================================
    // 共用断言工具（针对服务端世界 / 拒绝计数器）
    // ========================================================================

    /// <summary>推进仿真直到条件成立（服务端异步处理需轮询同步）。</summary>
    private static async Task<bool> TickUntilAsync(VanillaServer server, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            server.Host.Simulator.Tick();
            if (condition()) return true;
            await Task.Delay(10).ConfigureAwait(false);
        }
        server.Host.Simulator.Tick();
        return condition();
    }

    /// <summary>等待权威层出现指定拒绝原因（Prometheus 计数器）。</summary>
    private static async Task<bool> WaitForRejectAsync(VanillaServer server, string reason, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (MetricsText(server).Contains(reason, StringComparison.Ordinal)) return true;
            await Task.Delay(10).ConfigureAwait(false);
        }
        return MetricsText(server).Contains(reason, StringComparison.Ordinal);
    }

    private static string MetricsText(VanillaServer server)
        => ((PrometheusMetrics)server.Host.Metrics).ExportAsText();

    /// <summary>把玩家移到指定像素位置，并等待权威世界生效（未生效即断言失败）。</summary>
    private static async Task StandAtAsync(VanillaServer server, VanillaSession s, float pixelX, float pixelY)
    {
        await s.SendAsync(PacketId.PlayerPosition, new PlayerControlsPacket(1, new Vector2(pixelX, pixelY)));
        var ok = await TickUntilAsync(server,
            () => server.Host.Simulator.State.Players.TryGetValue(1, out var p)
                  && MathF.Abs(p.Position.X - pixelX) < 2f,
            TimeSpan.FromSeconds(5));
        Assert.True(ok, $"玩家未能站到 ({pixelX},{pixelY})：合法移动未被应用到权威世界");
    }

    // ========================================================================
    // 一、登录链（原版进服必经路径）
    // ========================================================================

    [Fact]
    public async Task Vanilla_LoginChain_Delivers_All_Required_Packets()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        var types = s.HandshakePackets.Select(p => p.Type).ToHashSet();
        Assert.Contains(PacketId.ContinueConnecting, types);  // 3   分配 PlayerId
        Assert.Contains(PacketId.WorldInfo, types);           // 7   世界元数据
        Assert.Contains(PacketId.StatusText, types);          // 9   区块进度
        Assert.Contains(PacketId.TileSendSection, types);     // 10  图格区块
        Assert.Contains(PacketId.InitialSpawn, types);        // 49  出生
        Assert.Contains(PacketId.FinishedConnecting, types);  // 129 进入世界

        // 包 10 至少下发一个区块；区块内容取自服务端真实生成的世界（以权威世界为参照断言）
        Assert.True(s.HandshakePackets.Count(p => p.Type == PacketId.TileSendSection) >= 1,
            "未下发任何图格区块");
        var world = server.Host.Simulator.State;
        Assert.Equal(WorldGenerator.SmallWorldWidth, world.MaxTilesX);
        Assert.Equal(WorldGenerator.SmallWorldHeight, world.MaxTilesY);
        Assert.False(string.IsNullOrEmpty(world.WorldName));
    }

    [Fact]
    public async Task Vanilla_Join_Marks_Self_Active()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        var got = await s.ReadUntilAsync(
            p => p is PlayerActivePacket { Active: true }, TimeSpan.FromSeconds(5));

        var active = Assert.Single(got.OfType<PlayerActivePacket>());
        Assert.Equal((byte)1, active.PlayerId); // 服务端分配的首个连接 = #1
    }

    [Fact]
    public async Task Vanilla_SecondPlayer_Is_Broadcast_To_First()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await a.ReadUntilAsync(p => p is PlayerActivePacket, TimeSpan.FromSeconds(5)); // 清空自身入站

        await using var b = await server.ConnectAsync("Bee");

        // A 应看到 B 的外观（包 4，Slot 为服务端分配的 #2）与激活（包 14）
        var got = await a.ReadUntilAsync(
            p => p is PlayerActivePacket { PlayerId: 2, Active: true }, TimeSpan.FromSeconds(5));

        var info = Assert.Single(got.OfType<PlayerInfoPacket>());
        Assert.Equal("Bee", info.Name);
        Assert.Equal((byte)2, info.Slot);
    }

    [Fact]
    public async Task Vanilla_PlayerDisconnect_Broadcasts_Inactive()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await a.ReadUntilAsync(p => p is PlayerActivePacket, TimeSpan.FromSeconds(5));

        var b = await server.ConnectAsync("Bee");
        await b.ReadUntilAsync(p => p is PlayerActivePacket, TimeSpan.FromSeconds(5));
        await a.ReadUntilAsync(p => p is PlayerActivePacket { PlayerId: 2 }, TimeSpan.FromSeconds(5));

        await b.DisposeAsync(); // 断开 B（模拟客户端退出）

        var got = await a.ReadUntilAsync(
            p => p is PlayerActivePacket { PlayerId: 2, Active: false }, TimeSpan.FromSeconds(5));

        Assert.Contains(got, p => p is PlayerActivePacket { PlayerId: 2, Active: false });
    }

    // ========================================================================
    // 二、移动权威（包 13）
    // ========================================================================

    [Fact]
    public async Task Vanilla_Movement_Accepted_And_Applied()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await StandAtAsync(server, s, 320f, 460f); // 未抛异常即表示已被权威接受并写入世界
    }

    [Fact]
    public async Task Vanilla_Movement_Overspeed_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        await StandAtAsync(server, s, 320f, 460f);

        var before = server.Host.Simulator.State.Players[1].Position;
        // 单包位移 100000px ≫ 允许上限（8*60*10 + 4 = 4804px）
        await s.SendAsync(PacketId.PlayerPosition,
            new PlayerControlsPacket(1, new Vector2(before.X + 100_000f, before.Y)));

        Assert.True(await WaitForRejectAsync(server, "speed_exceeded", TimeSpan.FromSeconds(5)),
            "超速移动未被权威拒绝");
    }

    // ========================================================================
    // 三、世界权威（包 17 挖砖 / 包 79 放砖）
    // ========================================================================

    [Fact]
    public async Task Vanilla_TileBreak_Removes_Solid_Tile()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        await StandAtAsync(server, s, sx * 16f + 8f, sy * 16f - 8f);

        int tx = sx, ty = sy + 3; // 地表下 3 格：实心，且在 160px 挖掘半径内
        Assert.True(world.Tiles[tx, ty].Active);
        var type = world.Tiles[tx, ty].Type;

        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(tx, ty, 0) { TileType = type });

        Assert.True(await TickUntilAsync(server, () => !world.Tiles[tx, ty].Active, TimeSpan.FromSeconds(5)),
            "挖砖权威通过后图格未变空");
    }

    [Fact]
    public async Task Vanilla_TileBreak_OutOfReach_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        await StandAtAsync(server, s, sx * 16f + 8f, sy * 16f - 8f);

        // 目标列：从出生点行向下找第一个实心图格（地表起伏，不能假定与出生点同高）
        int tx = sx + 200, ty = sy;
        while (ty < world.MaxTilesY && !world.Tiles[tx, ty].Active) ty++;
        Assert.True(ty < world.MaxTilesY, "目标列未找到实心图格");
        var type = world.Tiles[tx, ty].Type;

        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(tx, ty, 0) { TileType = type });

        Assert.True(await WaitForRejectAsync(server, "out_of_reach", TimeSpan.FromSeconds(5)),
            "远距离挖砖未被拒绝");
        Assert.True(world.Tiles[tx, ty].Active, "被拒绝的挖砖不应改动世界");
    }

    [Fact]
    public async Task Vanilla_InventoryReport_Then_TilePlace_Succeeds()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        await StandAtAsync(server, s, sx * 16f + 8f, sy * 16f - 8f);

        // 包 5：上报背包（SSC 启用时服务端持有唯一真相）→ 物品 ID 1（石）x10
        await s.SendAsync(PacketId.InventorySlot, new InventorySlotPacket(0, 1, 10));

        int tx = sx + 1, ty = sy - 2; // 地表上方空气格
        Assert.False(world.Tiles[tx, ty].Active);

        await s.SendAsync(PacketId.TilePlace, new TilePlacePacket(tx, ty, 1));

        Assert.True(await TickUntilAsync(server, () => world.Tiles[tx, ty].Active, TimeSpan.FromSeconds(5)),
            "放砖未生效（背包未同步或权威拒绝）");
        Assert.Equal((ushort)1, world.Tiles[tx, ty].Type);
    }

    [Fact]
    public async Task Vanilla_TilePlace_Without_InventoryItem_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        await StandAtAsync(server, s, sx * 16f + 8f, sy * 16f - 8f);

        // 未上报背包 → 服务端无该物品 → 应拒绝（空放作弊）
        await s.SendAsync(PacketId.TilePlace, new TilePlacePacket(sx + 1, sy - 2, 1));

        Assert.True(await WaitForRejectAsync(server, "item_not_in_inventory", TimeSpan.FromSeconds(5)),
            "背包无该物品时放砖未被拒绝");
    }

    // ========================================================================
    // 四、战斗 / 抛射物（包 28 / 27）
    // ========================================================================

    [Fact]
    public async Task Vanilla_NpcStrike_Above_SingleDamage_Limit_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        // 注意：Damage 线格式为 Int16，须取「能放进 Int16 且超过上限 30000」的值
        await s.SendAsync(PacketId.NpcStrike, new NpcStrikePacket(1, 32000));

        Assert.True(await WaitForRejectAsync(server, "damage_exceeded", TimeSpan.FromSeconds(5)),
            "超上限单次伤害未被拒绝");
    }

    [Fact]
    public async Task Vanilla_NpcStrike_Within_Limit_IsAccepted()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.NpcStrike, new NpcStrikePacket(1, 10));

        await Task.Delay(200);
        Assert.DoesNotContain("damage_exceeded", MetricsText(server));
        Assert.DoesNotContain("invalid_strike", MetricsText(server));
    }

    [Fact]
    public async Task Vanilla_Projectile_Is_Not_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.ProjectileNew,
            new ProjectileNewPacket(1, new Vector2(320f, 460f), new Vector2(1f, 0f), 1));

        await Task.Delay(200);
        Assert.DoesNotContain("projectile_rate_exceeded", MetricsText(server));
    }

    // ========================================================================
    // 五、库存 / 物品（包 5 / 21 / 31）
    // ========================================================================

    [Fact]
    public async Task Vanilla_InventorySlot_InvalidSlot_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.InventorySlot, new InventorySlotPacket(999, 1, 1)); // 槽位上限 59

        Assert.True(await WaitForRejectAsync(server, "invalid_slot", TimeSpan.FromSeconds(5)),
            "非法背包槽位未被拒绝");
    }

    [Fact]
    public async Task Vanilla_ItemDrop_UnknownItem_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.ItemDrop, new ItemDropPacket(99_999, 1));

        Assert.True(await WaitForRejectAsync(server, "unknown_item", TimeSpan.FromSeconds(5)),
            "未知物品的丢弃未被拒绝");
    }

    [Fact]
    public async Task Vanilla_Chest_OutOfBounds_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.Chest, new ChestPacket(-1, 0));

        Assert.True(await WaitForRejectAsync(server, "out_of_bounds", TimeSpan.FromSeconds(5)),
            "越界开箱未被拒绝");
    }

    // ========================================================================
    // 六、属性 / 传送（包 16 / 65）
    // ========================================================================

    [Fact]
    public async Task Vanilla_Health_Above_ServerMax_Gets_Correction()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.PlayerHealth, new PlayerHealthPacket(1, 9999, 9999));

        var got = await s.ReadUntilAsync(p => p is PlayerHealthPacket, TimeSpan.FromSeconds(5));
        var corrected = Assert.Single(got.OfType<PlayerHealthPacket>());
        Assert.Equal(500, corrected.MaxHp); // ServerConfig.MaxPlayerHp
        Assert.True(corrected.Hp <= 500, $"纠正后血量应被压到上限内，实际 {corrected.Hp}");
    }

    [Fact]
    public async Task Vanilla_Teleport_OutOfBounds_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.TeleportEntity,
            new TeleportEntityPacket(1, new Vector2(-10f, -10f)) { Kind = TeleportEntityKind.Player });

        Assert.True(await WaitForRejectAsync(server, "teleport_out_of_bounds", TimeSpan.FromSeconds(5)),
            "越界传送未被拒绝");
    }

    // ========================================================================
    // 七、健壮性：弃用包不应踢出连接
    // ========================================================================

    [Fact]
    public async Task Vanilla_DeprecatedChatPacket_DoesNot_Disconnect()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        // 包 25 自 1.4 起弃用（聊天走 NetTextModule）：应被透传接受而非踢出
        await s.SendRawAsync(PacketId.ChatText, new byte[] { 0x00 });

        // 连接仍可用：后续合法移动必须生效
        await StandAtAsync(server, s, 400f, 460f);
    }

    // ========================================================================
    // 八、他人可见性：权威通过后转发给其他玩家（原版服务端中继语义）
    // ========================================================================

    [Fact]
    public async Task Vanilla_Projectile_Is_Relayed_To_OtherPlayers()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await using var b = await server.ConnectAsync("Bee");

        await a.SendAsync(PacketId.ProjectileNew,
            new ProjectileNewPacket(7, new Vector2(320f, 460f), new Vector2(1f, 0f), 1));

        var got = await b.ReadUntilAsync(p => p is ProjectileNewPacket, TimeSpan.FromSeconds(5));
        var proj = Assert.Single(got.OfType<ProjectileNewPacket>());
        Assert.Equal(7, proj.ProjectileKey);
    }

    [Fact]
    public async Task Vanilla_ItemDrop_Is_Relayed_To_OtherPlayers()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await using var b = await server.ConnectAsync("Bee");

        await a.SendAsync(PacketId.ItemDrop, new ItemDropPacket(1, 5));

        var got = await b.ReadUntilAsync(p => p is ItemDropPacket, TimeSpan.FromSeconds(5));
        var item = Assert.Single(got.OfType<ItemDropPacket>());
        Assert.Equal(1, item.ItemId);
        Assert.Equal(5, item.Stack);
    }

    [Fact]
    public async Task Vanilla_PlayerHurt_Is_Relayed_With_ServerPlayerId()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await using var b = await server.ConnectAsync("Bee");

        // A 上报受击（客户端本地索引为 0）→ 服务端必须以分配的 #1 覆盖后再转发
        await a.SendAsync(PacketId.PlayerHurtV2, new PlayerHurtV2Packet(0, 10));

        var got = await b.ReadUntilAsync(p => p is PlayerHurtV2Packet, TimeSpan.FromSeconds(5));
        var hurt = Assert.Single(got.OfType<PlayerHurtV2Packet>());
        Assert.Equal(1, hurt.PlayerId);
    }

    [Fact]
    public async Task Vanilla_PlayerBuffs_Is_Relayed_With_ServerPlayerId()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await using var b = await server.ConnectAsync("Bee");

        await a.SendAsync(PacketId.PlayerBuffs, new PlayerBuffsPacket(0, new[] { 1, 2 }));

        var got = await b.ReadUntilAsync(p => p is PlayerBuffsPacket, TimeSpan.FromSeconds(5));
        var buffs = Assert.Single(got.OfType<PlayerBuffsPacket>());
        Assert.Equal(1, buffs.PlayerId);
        Assert.Equal(new[] { 1, 2 }, buffs.BuffTypes);
    }

    // ========================================================================
    // 九、世界状态同步（包 18 时间 / 包 23 NPC）
    // 布局来源：原版 Terraria.exe 的 NetMessage.SendData case 18 / case 23
    // ========================================================================

    [Fact]
    public async Task Vanilla_Time_Is_Synced_To_Client()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await server.Host.BroadcastWorldStateAsync();

        var got = await s.ReadUntilAsync(p => p is TimePacket, TimeSpan.FromSeconds(5));
        var time = Assert.Single(got.OfType<TimePacket>());
        Assert.True(time.DayTime, "世界生成时为白天");
        Assert.InRange(time.Time, 0, 54000);
    }

    [Fact]
    public async Task Vanilla_Npc_Is_Synced_To_Client()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await server.Host.BroadcastWorldStateAsync();

        var got = await s.ReadUntilAsync(p => p is NpcUpdatePacket, TimeSpan.FromSeconds(5));
        var npc = Assert.Single(got.OfType<NpcUpdatePacket>());
        Assert.Equal((short)22, npc.NetId); // NPCID.Guide
    }

    // ========================================================================
    // 十、聊天（包 82 = LoadNetModule → NetTextModule，模块号 1）
    // 布局来源：原版 NetTextModule / NetworkInitializer（模块号） / ChatMessage
    // ========================================================================

    [Fact]
    public async Task Vanilla_Chat_Is_Relayed_To_Other_Players()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await using var b = await server.ConnectAsync("Bee");

        // 上行形态：命令名为空串（普通发言）
        await a.SendAsync(PacketId.NetModule, new NetTextPacket("hello") { IsClientMessage = true });

        // 下行形态：作者 = 服务端（255），文本由服务端拼「名字: 」
        var got = await b.ReadUntilAsync(p => p is NetTextPacket, TimeSpan.FromSeconds(5));
        var chat = Assert.Single(got.OfType<NetTextPacket>());
        Assert.Equal("Alice: hello", chat.Text);
        Assert.Equal(byte.MaxValue, chat.AuthorId);
    }

    [Fact]
    public async Task Vanilla_ServerBroadcast_Reaches_Client()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        // IServerApi.Broadcast 走同一下发路径（包 82 下行形态）
        await server.Host.Network.BroadcastChatAsync("Server restarting soon", "Yellow");

        var got = await s.ReadUntilAsync(p => p is NetTextPacket, TimeSpan.FromSeconds(5));
        var chat = Assert.Single(got.OfType<NetTextPacket>());
        Assert.Equal("Server restarting soon", chat.Text);
        Assert.Equal(new RgbColor(255, 255, 0), chat.Color);
    }
}
