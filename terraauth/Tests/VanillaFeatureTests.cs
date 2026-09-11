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
    public async Task Vanilla_ProjectileDestroy_Is_Relayed()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await using var b = await server.ConnectAsync("Bee");

        await a.SendAsync(PacketId.ProjectileNew,
            new ProjectileNewPacket(9, new Vector2(320f, 460f), new Vector2(1f, 0f), 1));
        await a.SendAsync(PacketId.ProjectileDestroy, new ProjectileDestroyPacket(9, new Vector2(400f, 460f)));

        var got = await b.ReadUntilAsync(p => p is ProjectileDestroyPacket, TimeSpan.FromSeconds(5));
        var destroy = Assert.Single(got.OfType<ProjectileDestroyPacket>());
        Assert.Equal(9, destroy.ProjectileKey);
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
    // 布局依据：原版客户端（协议 326）包 18 / 包 23 的字段顺序
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

    [Fact]
    public async Task Vanilla_Enemy_Spawns_Then_Dies_From_Strike()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;

        // 玩家必须先在世界里（刷怪以在线玩家附近为目标）
        await StandAtAsync(server, s, sx * 16f + 8f, sy * 16f - 8f);

        // 推进仿真直到刷出敌怪
        var spawned = await TickUntilAsync(server, () => world.Npcs.Any(n => !n.IsTownNpc),
            TimeSpan.FromSeconds(20));
        Assert.True(spawned, "未在超时内刷出敌怪");

        WorldNpc slime;
        lock (world.NpcsLock) slime = world.Npcs.First(n => !n.IsTownNpc);
        Assert.Equal((short)1, slime.NetId); // NPCID.BlueSlime

        // 客户端应能收到该敌怪（netID=1）
        await server.Host.BroadcastWorldStateAsync();
        var got = await s.ReadUntilAsync(p => p is NpcUpdatePacket { NetId: 1 }, TimeSpan.FromSeconds(5));
        Assert.Contains(got, p => p is NpcUpdatePacket { NetId: 1 });

        // 包 28 击杀（史莱姆 25 血）→ 服务端扣血并置为死亡
        int index;
        lock (world.NpcsLock) index = world.Npcs.IndexOf(slime);
        await s.SendAsync(PacketId.NpcStrike, new NpcStrikePacket(index, 25));

        Assert.True(await TickUntilAsync(server, () => !slime.Active, TimeSpan.FromSeconds(5)),
            "NPC 受击后未被击杀");
        Assert.Equal(0, slime.Life);
    }

    // ========================================================================
    // 十一、世界实体的服务端权威模拟（包 21 掉落物 / 包 27·29 弹幕）
    // ========================================================================

    [Fact]
    public async Task Vanilla_ItemDrop_Is_Tracked_And_Falls_Under_Gravity()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;

        // 地表上方 3 格处掉落（下落距离短，便于验证落地）
        await s.SendAsync(PacketId.ItemDrop,
            new ItemDropPacket(1, 5) { Position = new Vector2(sx * 16f + 8f, (sy - 3) * 16f) });

        Assert.True(await TickUntilAsync(server, () => world.Items.Any(i => i.ItemId == 1),
            TimeSpan.FromSeconds(5)), "掉落物未被服务端登记");

        // 重力落地：垂直速度归零
        Assert.True(await TickUntilAsync(server, () => world.Items.All(i => i.Velocity.Y == 0f),
            TimeSpan.FromSeconds(10)), "掉落物未在重力作用下落地");
    }

    [Fact]
    public async Task Vanilla_Projectile_Is_Tracked_And_Expires_With_Server_Destroy()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;

        // 包 27：服务端登记该弹幕（权威跟踪）
        await s.SendAsync(PacketId.ProjectileNew,
            new ProjectileNewPacket(3, new Vector2(320f, 460f), new Vector2(1f, 0f), 1));

        Assert.True(await TickUntilAsync(server, () => world.Projectiles.Any(p => p.Active),
            TimeSpan.FromSeconds(5)), "弹幕未被服务端登记");

        // 生存期耗尽（默认 300 tick）→ 服务端标记失效
        Assert.True(await TickUntilAsync(server, () => world.Projectiles.Any(p => !p.Active),
            TimeSpan.FromSeconds(20)), "弹幕未在生存期结束后失效");

        // 世界同步时服务端补发销毁包 29
        await server.Host.BroadcastWorldStateAsync();
        var got = await s.ReadUntilAsync(p => p is ProjectileDestroyPacket, TimeSpan.FromSeconds(5));
        Assert.Contains(got, p => p is ProjectileDestroyPacket { ProjectileKey: 3 });
    }

    // ========================================================================
    // 十、聊天（包 82 = LoadNetModule → NetTextModule，模块号 1）
    // 布局依据：原版客户端（协议 326）文本网络模块（模块号 1）的上下行负载
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

    // ========================================================================
    // 十二、受伤 / 死亡 / 复活的服务端权威（包 117 / 118 / 12）
    // ========================================================================

    [Fact]
    public async Task Vanilla_Hurt_Reduces_ServerHealth()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        await s.SendAsync(PacketId.PlayerHurtV2, new PlayerHurtV2Packet(0, 30));

        Assert.True(await TickUntilAsync(server,
            () => world.Players.TryGetValue(1, out var p) && p.Hp == 70 && !p.Dead,
            TimeSpan.FromSeconds(5)), "受伤未在服务端结算（应为 100-30=70）");
    }

    [Fact]
    public async Task Vanilla_Negative_Hurt_Is_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        // 负伤害 = 治疗，属 CE 改血方向之一
        await s.SendAsync(PacketId.PlayerHurtV2, new PlayerHurtV2Packet(0, -5));

        Assert.True(await WaitForRejectAsync(server, "invalid_damage", TimeSpan.FromSeconds(5)),
            "负伤害未被拒绝");
    }

    [Fact]
    public async Task Vanilla_Lethal_Hurt_Kills_And_Broadcasts_Death()
    {
        using var server = VanillaServer.Start();
        await using var a = await server.ConnectAsync("Alice");
        await using var b = await server.ConnectAsync("Bee");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, a, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        await a.SendAsync(PacketId.PlayerHurtV2, new PlayerHurtV2Packet(0, 5000));

        Assert.True(await TickUntilAsync(server,
            () => world.Players.TryGetValue(1, out var p) && p.Dead && p.Hp == 0,
            TimeSpan.FromSeconds(5)), "致命伤害未在服务端置为死亡");

        // 服务端补发死亡包 118 给所有客户端（含旁观者）
        await server.Host.BroadcastWorldStateAsync();
        var got = await b.ReadUntilAsync(p => p is PlayerDeathV2Packet, TimeSpan.FromSeconds(5));
        var death = Assert.Single(got.OfType<PlayerDeathV2Packet>());
        Assert.Equal(1, death.PlayerId);
    }

    [Fact]
    public async Task Vanilla_Respawn_After_Death_Uses_Server_Spawn()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        await s.SendAsync(PacketId.PlayerHurtV2, new PlayerHurtV2Packet(0, 5000));
        Assert.True(await TickUntilAsync(server,
            () => world.Players.TryGetValue(1, out var p) && p.Dead, TimeSpan.FromSeconds(5)),
            "未进入死亡态");

        // 客户端上报伪造复活点 → 服务端必须忽略，改用世界出生点
        await s.SendAsync(PacketId.PlayerSpawn,
            new PlayerSpawnPacket(0, 9999, 9999, 0, 0, 0, 0, 0));

        Assert.True(await TickUntilAsync(server,
            () => world.Players.TryGetValue(1, out var p) && !p.Dead && p.Hp == p.HpMax,
            TimeSpan.FromSeconds(5)), "复活未在服务端结算");

        var player = world.Players[1];
        Assert.True(MathF.Abs(player.Position.X - (world.SpawnTileX + 0.5f) * 16f) < 2f,
            $"复活点应为服务端出生点，实际 X={player.Position.X}");
    }

    // ========================================================================
    // 十三、掉落物拾取（包 22）/ 弹幕命中判定（服务端权威）
    // ========================================================================

    [Fact]
    public async Task Vanilla_ItemPickup_Removes_WorldItem()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        var pos = world.Players[1].Position;
        await s.SendAsync(PacketId.ItemDrop, new ItemDropPacket(1, 5) { Position = pos });

        Assert.True(await TickUntilAsync(server, () => world.Items.Any(i => i.Active),
            TimeSpan.FromSeconds(5)), "掉落物未被服务端登记");
        int slot;
        lock (world.ItemsLock) slot = world.Items.First(i => i.Active).Slot;

        await s.SendAsync(PacketId.ItemPickup, new ItemPickupPacket(slot));

        Assert.True(await TickUntilAsync(server,
            () => world.Items.All(i => !i.Active), TimeSpan.FromSeconds(5)),
            "拾取未在服务端移除世界实体");
        Assert.DoesNotContain("pickup_rejected", MetricsText(server));
    }

    [Fact]
    public async Task Vanilla_ItemPickup_OutOfReach_Is_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        // 掉落物放在 3000px 之外（CE 远程拾取）
        await s.SendAsync(PacketId.ItemDrop, new ItemDropPacket(1, 5)
        {
            Position = new Vector2(world.Players[1].Position.X + 3000f, world.Players[1].Position.Y),
        });
        Assert.True(await TickUntilAsync(server, () => world.Items.Any(i => i.Active),
            TimeSpan.FromSeconds(5)), "掉落物未被服务端登记");
        int slot;
        lock (world.ItemsLock) slot = world.Items.First(i => i.Active).Slot;

        await s.SendAsync(PacketId.ItemPickup, new ItemPickupPacket(slot));

        Assert.True(await WaitForRejectAsync(server, "out_of_reach", TimeSpan.FromSeconds(5)),
            "远程拾取未被拒绝");
    }

    [Fact]
    public async Task Vanilla_Projectile_Hit_Damages_Enemy()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        // 等刷怪
        Assert.True(await TickUntilAsync(server, () => world.Npcs.Any(n => !n.IsTownNpc),
            TimeSpan.FromSeconds(20)), "未刷出敌怪");

        WorldNpc enemy;
        lock (world.NpcsLock) enemy = world.Npcs.First(n => !n.IsTownNpc);
        int before = enemy.Life;

        // 弹幕直接生成在敌怪位置（速度 0）→ 服务端命中判定应扣血
        await s.SendAsync(PacketId.ProjectileNew,
            new ProjectileNewPacket(77, new Vector2(enemy.X, enemy.Y), new Vector2(0f, 0f), 1) { Damage = 10 });

        Assert.True(await TickUntilAsync(server, () => enemy.Life < before || !enemy.Active,
            TimeSpan.FromSeconds(5)), "弹幕命中未在服务端结算伤害");
    }

    // ========================================================================
    // 十四、请求传送（包 73）
    // ========================================================================

    [Fact]
    public async Task Vanilla_TeleportRequest_Is_Accepted()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        await s.SendAsync(PacketId.RequestTeleportationByServer,
            new RequestTeleportationByServerPacket(TeleportRequestKind.MagicConch));

        await Task.Delay(200);
        Assert.DoesNotContain("invalid_teleport_request", MetricsText(server));
        Assert.DoesNotContain("teleport_rate_exceeded", MetricsText(server));
    }

    [Fact]
    public async Task Vanilla_TeleportRequest_RateExceeded_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        // 上限 5 次/秒（与包 65 共用窗口）→ 连发 6 次必触发限频
        for (int i = 0; i < 6; i++)
            await s.SendAsync(PacketId.RequestTeleportationByServer,
                new RequestTeleportationByServerPacket(TeleportRequestKind.MagicConch));

        Assert.True(await WaitForRejectAsync(server, "teleport_rate_exceeded", TimeSpan.FromSeconds(5)),
            "超频传送请求未被拒绝");
    }

    // ========================================================================
    // 十五、箱子内容服务端持有（包 31 / 32 / 34）
    // ========================================================================

    /// <summary>在权威世界里插入一个测试箱子（返回其列表索引）。</summary>
    private static int AddTestChest(VanillaServer server, int x, int y, params (int Type, short Stack)[] items)
    {
        var world = server.Host.Simulator.State;
        var chest = new Chest { X = x, Y = y, Name = "Test", Items = new ChestItem[40] };
        for (int i = 0; i < items.Length; i++)
            chest.Items[i] = new ChestItem { Type = items[i].Type, Stack = items[i].Stack };

        lock (world.ChestsLock)
        {
            chest.Index = world.Chests.Count;
            world.Chests.Add(chest);
            return chest.Index;
        }
    }

    [Fact]
    public async Task Vanilla_ChestOpen_Sends_Authoritative_Contents()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int index = AddTestChest(server, world.SpawnTileX, world.SpawnTileY, (1, 5), (2, 3));

        await s.SendAsync(PacketId.Chest, new ChestPacket(world.SpawnTileX, world.SpawnTileY));

        // 服务端先发包 34（箱子索引），再逐槽发包 32；读到槽位 1 即表示内容已下发
        var got = await s.ReadUntilAsync(
            p => p is SyncChestItemPacket { ItemSlot: 1 }, TimeSpan.FromSeconds(5));

        var openIndex = Assert.Single(got.OfType<PlayerChestIndexPacket>());
        Assert.Equal(index, openIndex.ChestIndex);

        var slot0 = Assert.Single(got.OfType<SyncChestItemPacket>().Where(p => p.ItemSlot == 0));
        Assert.Equal(5, slot0.Stack);
        Assert.Equal(1, slot0.ItemType);
        var slot1 = Assert.Single(got.OfType<SyncChestItemPacket>().Where(p => p.ItemSlot == 1));
        Assert.Equal(3, slot1.Stack);
        Assert.Equal(2, slot1.ItemType);
    }

    [Fact]
    public async Task Vanilla_ChestItem_Is_Applied_Authoritatively()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);
        int index = AddTestChest(server, world.SpawnTileX, world.SpawnTileY);

        await s.SendAsync(PacketId.SyncChestItem,
            new SyncChestItemPacket(index, ItemSlot: 3, Stack: 7, Prefix: 0, ItemType: 5));

        Assert.True(await TickUntilAsync(server,
            () => world.Chests[index].Items[3] is { Stack: 7, Type: 5 },
            TimeSpan.FromSeconds(5)), "箱内物品未在服务端落盘");
    }

    [Fact]
    public async Task Vanilla_ChestItem_Invalid_Slot_Is_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);
        int index = AddTestChest(server, world.SpawnTileX, world.SpawnTileY);

        await s.SendAsync(PacketId.SyncChestItem,
            new SyncChestItemPacket(index, ItemSlot: 200, Stack: 1, Prefix: 0, ItemType: 1));

        Assert.True(await WaitForRejectAsync(server, "invalid_slot", TimeSpan.FromSeconds(5)),
            "非法箱子槽位未被拒绝");
    }

    [Fact]
    public async Task Vanilla_ChestItem_OutOfReach_Is_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        // 箱子在 300 格外（4800px ≫ 160px 交互距离）
        int index = AddTestChest(server, world.SpawnTileX + 300, world.SpawnTileY);

        await s.SendAsync(PacketId.SyncChestItem,
            new SyncChestItemPacket(index, ItemSlot: 0, Stack: 1, Prefix: 0, ItemType: 1));

        Assert.True(await WaitForRejectAsync(server, "out_of_reach", TimeSpan.FromSeconds(5)),
            "远程箱子写入未被拒绝");
    }

    [Fact]
    public async Task Vanilla_ChestOpen_UnknownChest_Is_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        // 清空世界箱子，确保目标坐标处没有任何箱子
        lock (world.ChestsLock) world.Chests.Clear();

        // 坐标在世界内且玩家可达，但该处没有箱子
        await s.SendAsync(PacketId.Chest, new ChestPacket(world.SpawnTileX, world.SpawnTileY));

        Assert.True(await WaitForRejectAsync(server, "chest_not_found", TimeSpan.FromSeconds(5)),
            "打开不存在的箱子未被拒绝");
    }

    // ========================================================================
    // 十六、液体（包 82 模块 0 NetLiquidModule）：编辑权威 + 简化流动 + 同步
    // ========================================================================

    /// <summary>列方向自上而下第一个实心图格的 Y。</summary>
    private static int FindGroundTileY(WorldState world, int x)
    {
        for (int y = 1; y < world.MaxTilesY; y++)
            if (world.Tiles[x, y].Active && TileIdSets.IsTileSolid(world.Tiles[x, y].Type)) return y;
        return world.MaxTilesY - 1;
    }

    [Fact]
    public async Task Vanilla_Liquid_Edit_Is_Applied_And_Flows_Down()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX + 1;
        int ground = FindGroundTileY(world, x);
        int y = ground - 3;                          // 地表上方 3 格的空气格
        Assert.False(world.Tiles[x, y].Active);

        await s.SendAsync(PacketId.NetModule, new LiquidModulePacket(new[]
        {
            new LiquidChange(x, y, 200, 0),
        }) { IsClientMessage = true });

        // 权威落盘 → 同一 tick 内即开始下落，故不断言源格仍为 200，只断言液体到达近地格
        Assert.True(await TickUntilAsync(server, () => world.Tiles[x, ground - 1].Liquid > 0,
            TimeSpan.FromSeconds(5)), "液体未在服务端落盘并向下流动");
        Assert.DoesNotContain("liquid_rejected", MetricsText(server));
    }

    [Fact]
    public async Task Vanilla_Liquid_Changes_Are_Broadcast_To_Client()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX + 1;
        int y = FindGroundTileY(world, x) - 3;

        await s.SendAsync(PacketId.NetModule, new LiquidModulePacket(new[]
        {
            new LiquidChange(x, y, 200, 0),
        }) { IsClientMessage = true });

        Assert.True(await TickUntilAsync(server, () => world.Tiles[x, y].Liquid > 0 || world.Tiles[x, y + 1].Liquid > 0,
            TimeSpan.FromSeconds(5)), "液体未在服务端落盘");

        await server.Host.FlushLiquidAsync();

        var got = await s.ReadUntilAsync(p => p is LiquidModulePacket, TimeSpan.FromSeconds(5));
        var liquid = Assert.Single(got.OfType<LiquidModulePacket>());
        Assert.NotEmpty(liquid.Changes);
        Assert.Contains(liquid.Changes, c => c.Amount > 0);
    }

    [Fact]
    public async Task Vanilla_Liquid_OutOfReach_Is_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        // 300 格外（4800px ≫ 160px）
        await s.SendAsync(PacketId.NetModule, new LiquidModulePacket(new[]
        {
            new LiquidChange(world.SpawnTileX + 300, world.SpawnTileY, 200, 0),
        }) { IsClientMessage = true });

        Assert.True(await WaitForRejectAsync(server, "out_of_reach", TimeSpan.FromSeconds(5)),
            "远程液体编辑未被拒绝");
    }

    [Fact]
    public async Task Vanilla_Liquid_Invalid_Type_Is_Rejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX + 1;
        int y = FindGroundTileY(world, x) - 3;

        await s.SendAsync(PacketId.NetModule, new LiquidModulePacket(new[]
        {
            new LiquidChange(x, y, 100, 9), // 液体类型上限 3
        }) { IsClientMessage = true });

        Assert.True(await WaitForRejectAsync(server, "invalid_liquid_type", TimeSpan.FromSeconds(5)),
            "非法液体类型未被拒绝");
    }

    // ========================================================================
    // 十七、电路（包 17）：线网 / 执行器编辑权威 + 简化信号传播
    // ========================================================================

    [Fact]
    public async Task Vanilla_Wire_Place_And_Kill_Are_Applied()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX, y = world.SpawnTileY;

        // action 5 = PlaceWire
        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 5));
        Assert.True(await TickUntilAsync(server, () => world.Tiles[x, y].Wire, TimeSpan.FromSeconds(5)),
            "放置红电线未被应用");

        // action 6 = KillWire
        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 6));
        Assert.True(await TickUntilAsync(server, () => !world.Tiles[x, y].Wire, TimeSpan.FromSeconds(5)),
            "拆除红电线未被应用");
    }

    [Fact]
    public async Task Vanilla_Actuator_Place_And_Kill_Are_Applied()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX, y = world.SpawnTileY;

        // action 8 = PlaceActuator
        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 8));
        Assert.True(await TickUntilAsync(server, () => world.Tiles[x, y].Actuator, TimeSpan.FromSeconds(5)),
            "放置执行器未被应用");

        // action 9 = KillActuator（同时清除 InActive）
        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 9));
        Assert.True(await TickUntilAsync(server, () => !world.Tiles[x, y].Actuator, TimeSpan.FromSeconds(5)),
            "拆除执行器未被应用");
        Assert.False(world.Tiles[x, y].InActive);
    }

    [Fact]
    public async Task Vanilla_Actuate_Toggles_Connected_Actuators()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX, y = world.SpawnTileY;
        int ax = x + 1;

        // 铺设一条含执行器的线网：(x,y) 为触发点，(x+1,y) 挂执行器
        world.Tiles[x, y].Wire = true;
        world.Tiles[ax, y].Wire = true;
        world.Tiles[ax, y].Actuator = true;
        Assert.False(world.Tiles[ax, y].InActive);

        // action 19 = Actuate → 服务端沿电线传播并翻转执行器
        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 19));
        Assert.True(await TickUntilAsync(server, () => world.Tiles[ax, y].InActive, TimeSpan.FromSeconds(5)),
            "执行器未被通电（信号未传播）");

        // 再次触发 → 翻转回去
        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 19));
        Assert.True(await TickUntilAsync(server, () => !world.Tiles[ax, y].InActive, TimeSpan.FromSeconds(5)),
            "执行器未被断电（二次翻转失败）");
    }

    [Fact]
    public async Task Vanilla_Actuate_Does_Not_Propagate_Without_Wire()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX, y = world.SpawnTileY;
        int ax = x + 1;

        // 只有执行器，没有电线 → 触发点自身无线网，不应传播到相邻执行器
        world.Tiles[ax, y].Actuator = true;

        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 19));

        await Task.Delay(300);
        Assert.False(world.Tiles[ax, y].InActive, "无线网时不应发生信号传播");
    }

    // ========================================================================
    // 十八、Boss / 事件（简化状态机 + 击杀进度 + 包 7 广播）
    // ========================================================================

    [Fact]
    public async Task Vanilla_BloodMoon_Is_Broadcast_As_WorldData()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;

        server.Host.Simulator.StartBloodMoon();
        Assert.True(world.BloodMoon);
        Assert.True(world.ProgressDirty);

        await server.Host.BroadcastWorldStateAsync();

        var got = await s.ReadUntilAsync(p => p.Type == PacketId.WorldInfo, TimeSpan.FromSeconds(5));
        Assert.Contains(got, p => p.Type == PacketId.WorldInfo);
        Assert.False(world.ProgressDirty, "包 7 下发后应清除进度脏位");
    }

    [Fact]
    public async Task Vanilla_DayNight_Transition_Ends_BloodMoon()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;

        // 夜晚最后一刻 + 血月中 → tick 一次即天亮
        world.DayTime = false;
        world.BloodMoon = true;
        world.Time = 32399; // NightLength = 32400

        server.Host.Simulator.Tick();

        Assert.True(world.DayTime, "未切换到白天");
        Assert.False(world.BloodMoon, "天亮后血月未结束");
        Assert.True(world.ProgressDirty, "昼夜切换未标记进度变化");
    }

    [Fact]
    public async Task Vanilla_Boss_Spawn_And_Kill_Sets_Progress()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        var pos = world.Players[1].Position;
        var boss = server.Host.Simulator.SpawnBoss(4, pos.X, pos.Y - 32f);

        Assert.True(boss.Active);
        Assert.True(boss.IsBoss);
        Assert.Equal(2800, boss.Life); // 眼魔简化生命上限

        // 客户端应能看到该 Boss（netID = 4）
        await server.Host.BroadcastWorldStateAsync();
        var seen = await s.ReadUntilAsync(p => p is NpcUpdatePacket { NetId: 4 }, TimeSpan.FromSeconds(5));
        Assert.Contains(seen, p => p is NpcUpdatePacket { NetId: 4 });

        // 击杀 → 服务端记录世界进度
        int index;
        lock (world.NpcsLock) index = world.Npcs.IndexOf(boss);
        await s.SendAsync(PacketId.NpcStrike,
            new NpcStrikePacket(index, 2800) { Generation = boss.Generation });

        Assert.True(await TickUntilAsync(server, () => !boss.Active, TimeSpan.FromSeconds(5)),
            "Boss 未被击杀");
        Assert.True(world.Progress.DownedBoss1, "Boss 击杀未记录到世界进度");

        // 进度变化 → 重新下发包 7
        await server.Host.BroadcastWorldStateAsync();
        var progress = await s.ReadUntilAsync(p => p.Type == PacketId.WorldInfo, TimeSpan.FromSeconds(5));
        Assert.Contains(progress, p => p.Type == PacketId.WorldInfo);
    }

    [Fact]
    public async Task Vanilla_Invasion_Spawns_Enemies_Then_Ends()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        server.Host.Simulator.StartInvasion(1, 2);
        Assert.Equal(1, world.InvasionType);
        Assert.Equal(2, world.InvasionSize);

        Assert.True(await TickUntilAsync(server, () => world.Npcs.Any(n => n.Type == 26),
            TimeSpan.FromSeconds(20)), "入侵怪未刷新");

        Assert.True(await TickUntilAsync(server, () => world.InvasionType == 0 && world.InvasionSize == 0,
            TimeSpan.FromSeconds(20)), "入侵未在配额耗尽后结束");

        Assert.Equal(2, world.Npcs.Count(n => n.Type == 26));
    }

    [Fact]
    public async Task Vanilla_Eclipse_Is_Recorded_And_Broadcast()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;

        server.Host.Simulator.StartEclipse();
        Assert.True(world.Eclipse);

        await server.Host.BroadcastWorldStateAsync();
        var got = await s.ReadUntilAsync(p => p.Type == PacketId.WorldInfo, TimeSpan.FromSeconds(5));
        Assert.Contains(got, p => p.Type == PacketId.WorldInfo);
    }

    // ========================================================================
    // 十九、遗留项闭环：执行器翻转逐格下发 + 液体同步视口裁剪
    // ========================================================================

    [Fact]
    public async Task Vanilla_Actuate_Pushes_Tile_Update_To_Client()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX, y = world.SpawnTileY;
        world.Tiles[x, y].Wire = true;
        world.Tiles[x + 1, y].Wire = true;
        world.Tiles[x + 1, y].Actuator = true;

        await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(x, y, 19));
        Assert.True(await TickUntilAsync(server, () => world.Tiles[x + 1, y].InActive, TimeSpan.FromSeconds(5)),
            "执行器未被通电");

        // 服务端驱动的图格变更 → 由快照循环推送小矩形包 10
        await server.Host.FlushTileUpdatesAsync();

        var got = await s.ReadUntilAsync(p => p.Type == PacketId.TileSendSection, TimeSpan.FromSeconds(5));
        Assert.Contains(got, p => p.Type == PacketId.TileSendSection);
    }

    [Fact]
    public async Task Vanilla_Liquid_Sync_Is_Viewport_Culled()
    {
        // 视口半径收紧到 32px，使液体格落在视口之外（但仍处于 160px 的液体交互距离内）
        using var server = VanillaServer.Start("""{ "ViewportRadius": 32 }""");
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX + 4;          // 水平 64px（> 视口 32px，< 交互 160px）
        int y = world.SpawnTileY - 3;

        await s.SendAsync(PacketId.NetModule, new LiquidModulePacket(new[]
        {
            new LiquidChange(x, y, 200, 0),
        }) { IsClientMessage = true });

        Assert.True(await TickUntilAsync(server,
            () => world.Tiles[x, y].Liquid > 0 || world.Tiles[x, y + 1].Liquid > 0,
            TimeSpan.FromSeconds(5)), "液体未在服务端落盘（应通过交互距离校验）");

        await server.Host.FlushLiquidAsync();

        // 视口外 → 不应收到液体同步
        var got = await s.ReadUntilAsync(p => p is LiquidModulePacket, TimeSpan.FromMilliseconds(500));
        Assert.DoesNotContain(got, p => p is LiquidModulePacket);
    }

    // ========================================================================
    // 二十、服务端玩家伤害来源（敌怪 / Boss 接触伤害，包 117 + 16）
    // ========================================================================

    /// <summary>在玩家碰撞盒中心放置一只普通敌怪（史莱姆）。</summary>
    private static WorldNpc PlaceSlimeOn(VanillaServer server, float x, float y)
    {
        var world = server.Host.Simulator.State;
        var slime = new WorldNpc
        {
            Type = 1,
            NetId = 1,
            X = x,
            Y = y,
            IsTownNpc = false,
            Life = 25,
            LifeMax = 25,
            Active = true,
        };
        lock (world.NpcsLock) world.Npcs.Add(slime);
        return slime;
    }

    [Fact]
    public async Task Vanilla_EnemyContact_Damages_Player_And_Notifies()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        var spawnX = world.SpawnTileX * 16f + 8f;
        var spawnY = world.SpawnTileY * 16f - 8f;
        await StandAtAsync(server, s, spawnX, spawnY);

        var player = world.Players[1];
        PlaceSlimeOn(server, player.Position.X, player.Position.Y + 21f); // 玩家碰撞盒中心

        // 接触伤害 7（史莱姆）→ 100 - 7 = 93
        Assert.True(await TickUntilAsync(server, () => player.Hp == 93, TimeSpan.FromSeconds(5)),
            $"接触伤害未在服务端结算，实际 HP={player.Hp}");

        await server.Host.FlushPlayerHurtAsync();

        // 服务端先广播包 117（受击表现），随后向受击者单发包 16（权威血量）
        var got = await s.ReadUntilAsync(p => p is PlayerHealthPacket, TimeSpan.FromSeconds(5));

        var hurt = Assert.Single(got.OfType<PlayerHurtV2Packet>());
        Assert.Equal(7, hurt.Damage);
        Assert.Equal(1, hurt.PlayerId);

        // 权威血量同步给受击者（客户端血条据此更新）
        Assert.Contains(got, p => p is PlayerHealthPacket { Hp: 93 });
    }

    [Fact]
    public async Task Vanilla_ContactDamage_Has_ImmunityWindow()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        var player = world.Players[1];
        PlaceSlimeOn(server, player.Position.X, player.Position.Y + 21f);

        Assert.True(await TickUntilAsync(server, () => player.Hp == 93, TimeSpan.FromSeconds(5)),
            "首次接触伤害未结算");

        // 免伤帧 60 tick：30 tick 内不应再受伤
        for (int i = 0; i < 30; i++) server.Host.Simulator.Tick();
        Assert.Equal(93, player.Hp);

        // 超过免伤帧后应再次结算 7 点
        for (int i = 0; i < 40; i++) server.Host.Simulator.Tick();
        Assert.Equal(86, player.Hp);
    }

    // ========================================================================
    // 二十一、高熵区块：超帧上限时自动拆分（不中断登录）
    // ========================================================================

    [Fact]
    public async Task Vanilla_ChaoticSection_Is_Split_Without_Breaking_Login()
    {
        using var server = VanillaServer.Start();
        var world = server.Host.Simulator.State;

        // 把出生点所在区块填成高熵地形（多种方块 / 墙 / 液体 / 电线 / 油漆），
        // 使其压缩后超过 UInt16 帧上限（65535）→ 触发拆分逻辑
        int secX = world.SpawnTileX / 200 * 200;
        int secY = world.SpawnTileY / 150 * 150;
        for (int y = secY; y < secY + 150 && y < world.MaxTilesY; y++)
        {
            for (int x = secX; x < secX + 200 && x < world.MaxTilesX; x++)
            {
                world.Tiles[x, y] = new Tile
                {
                    Active = true,
                    Type = (ushort)(1 + (x * 7 + y * 13) % 200),
                    Wall = (ushort)((x + y) % 100),
                    Liquid = (byte)((x * 3 + y * 5) % 256),
                    LiquidType = (byte)((x + y) % 4),
                    Wire = x % 3 == 0,
                    Wire2 = y % 3 == 0,
                    TileColor = (byte)((x * y) % 256),
                };
            }
        }

        // 登录链必须仍然完成（拆分后逐块下发）
        await using var s = await server.ConnectAsync("Alice");
        Assert.Contains(s.HandshakePackets, p => p is FinishedConnectingPacket);

        // 出生点矩形正常为 5×3 = 15 个区块；高熵区块被拆分后总数应更多
        int sections = s.HandshakePackets.Count(p => p.Type == PacketId.TileSendSection);
        Assert.True(sections > 15, $"高熵区块未被拆分（区块数={sections}）");
    }

    // ========================================================================
    // 二十二、Phase 7 对抗清单自动化（服务端可自动化部分）
    // 覆盖手册中的 P 组（协议攻击）与 M 组（内存修改的服务端侧等价攻击）
    // ========================================================================

    [Fact]
    public async Task AntiCheat_PacketFlood_IsRateLimited_AndEventuallyKicked()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        await StandAtAsync(server, s, sx * 16f + 8f, sy * 16f - 8f);

        // 目标取真实实心图格，保证「非限流原因」不会干扰本用例
        int tx = sx, ty = sy + 3;
        Assert.True(world.Tiles[tx, ty].Active);
        var type = world.Tiles[tx, ty].Type;

        // P5/M6：瞬时海量合法格式的挖砖包（远超 60/s 与 120/s 全局上限）；
        // 被踢后写入会失败，故忽略写异常
        for (int i = 0; i < 400; i++)
        {
            try
            {
                await s.SendAsync(PacketId.TileBreak, new TileBreakPacket(tx, ty, 0) { TileType = type });
            }
            catch
            {
                break;
            }
        }

        var limited = await WaitForRejectAsync(server, "tile_break_rate_exceeded", TimeSpan.FromSeconds(5))
                      || await WaitForRejectAsync(server, "packet_rate_exceeded", TimeSpan.FromSeconds(5));
        Assert.True(limited, "洪水攻击未被限流");

        // 违规累计达阈值（10 次 / 窗口）→ 下发包 2 并踢出连接（处置闭环）
        var got = await s.ReadUntilAsync(p => p is DisconnectPacket, TimeSpan.FromSeconds(5));
        Assert.Contains(got, p => p is DisconnectPacket);
    }

    [Fact]
    public async Task AntiCheat_DpsWindow_RejectsBurst()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        // P1：单次都在上限内（20000 < 30000），但窗口内累计超过 MaxDps（50000）
        await s.SendAsync(PacketId.NpcStrike, new NpcStrikePacket(0, 20_000));
        await s.SendAsync(PacketId.NpcStrike, new NpcStrikePacket(0, 20_000));
        await s.SendAsync(PacketId.NpcStrike, new NpcStrikePacket(0, 20_000)); // 累计 60000 > 50000

        Assert.True(await WaitForRejectAsync(server, "dps_exceeded", TimeSpan.FromSeconds(5)),
            "DPS 窗口上限未生效");
    }

    [Fact]
    public async Task AntiCheat_UnidentifiedPlayer_IsSilentlyDropped()
    {
        using var server = VanillaServer.Start();

        // P4：管线对「无身份上下文」直接静默丢弃（网络层保证仅 Playing 入管线，此处为权威层兜底）
        var pipeline = server.Host.Pipeline;
        var result = await pipeline.ProcessAsync(new PlayerHealthPacket(0, 10, 10), playerId: 0, new CommandQueue());
        Assert.Equal(Authority.AuthorityDecision.RejectSilent, result.Decision);
    }

    [Fact]
    public async Task AntiCheat_IllegalItemStack_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");

        // P3：堆叠为 0 的掉落物（非法量纲）应被拒
        await s.SendAsync(PacketId.ItemDrop, new ItemDropPacket(1, 0));

        Assert.True(await WaitForRejectAsync(server, "invalid_stack", TimeSpan.FromSeconds(5)),
            "非法堆叠未被拒绝");
    }

    [Fact]
    public async Task AntiCheat_ChestItem_WithUnknownItem_IsRejected()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        var chest = new Chest { X = world.SpawnTileX, Y = world.SpawnTileY, Name = "T", Items = new ChestItem[40] };
        int index;
        lock (world.ChestsLock)
        {
            chest.Index = world.Chests.Count;
            world.Chests.Add(chest);
            index = chest.Index;
        }

        // P3 延伸：箱内写入未知物品 ID → 拒绝
        await s.SendAsync(PacketId.SyncChestItem,
            new SyncChestItemPacket(index, ItemSlot: 0, Stack: 1, Prefix: 0, ItemType: 99_999));

        Assert.True(await WaitForRejectAsync(server, "unknown_item", TimeSpan.FromSeconds(5)),
            "箱内写入未知物品未被拒绝");
    }

    [Fact]
    public async Task AntiCheat_Replayed_Malicious_Movement_DoesNotAdvance_Authority()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        int sx = world.SpawnTileX, sy = world.SpawnTileY;
        await StandAtAsync(server, s, sx * 16f + 8f, sy * 16f - 8f);

        var before = world.Players[1].Position;

        // P4：同一超速包重放多次 → 每次都应被拒，且绝不推进权威位置（无"首包放行"缺口）
        var overspeed = new PlayerControlsPacket(1, new Vector2(before.X + 100_000f, before.Y));
        for (int i = 0; i < 3; i++) await s.SendAsync(PacketId.PlayerPosition, overspeed);

        Assert.True(await WaitForRejectAsync(server, "speed_exceeded", TimeSpan.FromSeconds(5)),
            "超速移动未被拒绝");

        // 再发一次合法移动，确认权威位置基准未被污染
        await StandAtAsync(server, s, before.X + 8f, before.Y);
    }

    // ========================================================================
    // 二十三、Boss 掉落 / 进度位映射（按原版源码核对 ID）
    // ========================================================================

    [Fact]
    public async Task Vanilla_BossKill_Drops_Loot_And_Pushes_Packet21()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        var pos = world.Players[1].Position;
        var boss = server.Host.Simulator.SpawnBoss(4, pos.X, pos.Y - 32f); // Eye of Cthulhu

        int index;
        lock (world.NpcsLock) index = world.Npcs.IndexOf(boss);
        await s.SendAsync(PacketId.NpcStrike,
            new NpcStrikePacket(index, 2800) { Generation = boss.Generation });

        Assert.True(await TickUntilAsync(server, () => !boss.Active, TimeSpan.FromSeconds(5)),
            "Boss 未被击杀");

        // 服务端生成掉落物：Demonite Ore（物品 56）× 30
        WorldItemEntity? loot;
        lock (world.ItemsLock)
            loot = world.Items.FirstOrDefault(i => i.ItemId == 56);
        Assert.NotNull(loot);
        Assert.Equal(30, loot!.Stack);

        // 服务端主动生成的掉落物必须补发包 21（否则客户端看不到）
        await server.Host.FlushNewItemsAsync();
        var got = await s.ReadUntilAsync(p => p is ItemDropPacket { ItemId: 56 }, TimeSpan.FromSeconds(5));

        var drop = Assert.Single(got.OfType<ItemDropPacket>().Where(p => p.ItemId == 56));
        Assert.Equal(30, drop.Stack);
        Assert.Equal(loot.Slot, drop.ItemSlotIndex);
    }

    [Fact]
    public async Task Vanilla_Progress_Uses_Verified_BossIds()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);
        var pos = world.Players[1].Position;

        // Queen Bee = 222（此前误映射为 126 = Spazmatism）
        var queenBee = server.Host.Simulator.SpawnBoss(222, pos.X, pos.Y - 32f);
        int qbIndex;
        lock (world.NpcsLock) qbIndex = world.Npcs.IndexOf(queenBee);
        await s.SendAsync(PacketId.NpcStrike,
            new NpcStrikePacket(qbIndex, 1000) { Generation = queenBee.Generation });

        Assert.True(await TickUntilAsync(server, () => !queenBee.Active, TimeSpan.FromSeconds(5)),
            "Queen Bee 未被击杀");
        Assert.True(world.Progress.DownedQueenBee, "Queen Bee 未记录进度");
        Assert.False(world.Progress.DownedMechBoss2, "126 曾被误当作 Queen Bee");

        // The Twins = 125/126 → DownedMechBoss2 + DownedMechBossAny
        var twins = server.Host.Simulator.SpawnBoss(126, pos.X - 32f, pos.Y - 32f);
        int twinsIndex;
        lock (world.NpcsLock) twinsIndex = world.Npcs.IndexOf(twins);
        await s.SendAsync(PacketId.NpcStrike,
            new NpcStrikePacket(twinsIndex, 1000) { Generation = twins.Generation });

        Assert.True(await TickUntilAsync(server, () => !twins.Active, TimeSpan.FromSeconds(5)),
            "The Twins 未被击杀");
        Assert.True(world.Progress.DownedMechBoss2, "The Twins 未记录机械 Boss 进度");
        Assert.True(world.Progress.DownedMechBossAny, "未记录「任意机械 Boss」");
    }

    // ========================================================================
    // 二十四、液体混合反应（水 + 岩浆 → 黑曜石）
    // ========================================================================

    [Fact]
    public async Task Vanilla_LiquidMerge_Water_Plus_Lava_Creates_Obsidian()
    {
        using var server = VanillaServer.Start();
        await using var s = await server.ConnectAsync("Alice");
        var world = server.Host.Simulator.State;
        await StandAtAsync(server, s, world.SpawnTileX * 16f + 8f, world.SpawnTileY * 16f - 8f);

        int x = world.SpawnTileX, y = world.SpawnTileY - 3;

        // 岩浆（type 1）与水（type 0）相邻，各 200 单位（≥ 原版阈值 24）
        var lava = world.Tiles[x, y];
        lava.Liquid = 200;
        lava.LiquidType = 1;
        world.Tiles[x, y] = lava;

        var water = world.Tiles[x, y + 1];
        water.Liquid = 200;
        water.LiquidType = 0;
        world.Tiles[x, y + 1] = water;

        world.MarkLiquidChanged(x, y);
        world.MarkLiquidChanged(x, y + 1);

        // 反应生成黑曜石（图格 56）；生成位置取被访问的那一格
        Assert.True(await TickUntilAsync(server,
            () => (world.Tiles[x, y].Active && world.Tiles[x, y].Type == 56)
               || (world.Tiles[x, y + 1].Active && world.Tiles[x, y + 1].Type == 56),
            TimeSpan.FromSeconds(5)), "水 + 岩浆未生成黑曜石");

        // 两侧液体均被消耗
        Assert.Equal(0, world.Tiles[x, y].Liquid);
        Assert.Equal(0, world.Tiles[x, y + 1].Liquid);
    }
}
