// TerraAuth — 端到端集成测试
// 验证架构闭环：包 → 管线(Phase2) → Command(Phase3) → 仿真 → 快照(Phase4) → 编码(Phase5)

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using TerraAuth.Authority;
using TerraAuth.Concurrency;
using TerraAuth.Monitoring;
using TerraAuth.Net.Phase4;
using TerraAuth.Net.Phase5;
using TerraAuth.Persistence;
using TerraAuth.Plugins;
using TerraAuth.Protocol;
using TerraAuth.Security;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class EndToEndTests
{
    [Fact]
    public async Task FullLoop_ProcessesPacket_Through_Simulation_To_Snapshot()
    {
        // Arrange：组装最小主机（不启真实网络，直接喂包）
        var world = new WorldState();
        var commands = new CommandQueue();
        var recorder = new EventRecorder();
        var snapshots = new SnapshotStore();
        var simulator = new WorldSimulator(world, commands, recorder, snapshots);

        var rate = new RateLimits();
        var audit = new NoOpAuditLogger();
        var enforcers = new AuthorityEnforcers(rate, audit, new WorldState());
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new FrameStage(),
            new RateLimitStage(enforcers.Rate),
            new MovementAuthorityStage(enforcers.Movement, audit),
            new TerminalStage(),
        });

        // Act：模拟一个"合法移动"包
        var packet = new PlayerPositionPacket(1, new Vector2(100, 200));
        var result = await pipeline.ProcessAsync(packet, playerId: 1, commands, default)
            .ConfigureAwait(false);

        // Assert：管线接受 → Command 入队 → 仿真推进 → 快照产出
        Assert.Equal(AuthorityDecision.Accept, result.Decision);
        Assert.Equal(1, commands.Count); // Command 已入队

        // 仿真推进一 tick
        simulator.Tick();
        Assert.True(snapshots.Count > 0); // 至少一帧快照

        // 快照可被编码（Phase 5 就绪）
        var frame = snapshots.Latest;
        Assert.Equal(1u, frame.Tick);
    }

    [Fact]
    public async Task TcpRoundTrip_Packet13_Reaches_SnapshotOverWire()
    {
        // Arrange：组装最小主机（真实 TCP 监听，端口 0 由 OS 分配）
        var world = new WorldState();
        var commands = new CommandQueue();
        var recorder = new EventRecorder();
        var snapshots = new SnapshotStore();
        var simulator = new WorldSimulator(world, commands, recorder, snapshots);

        var rate = new RateLimits();
        var audit = new NoOpAuditLogger();
        var enforcers = new AuthorityEnforcers(rate, audit, new WorldState());
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new FrameStage(),
            new RateLimitStage(enforcers.Rate),
            new MovementAuthorityStage(enforcers.Movement, audit),
            new TerminalStage(),
        });

        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager();

        using var workers = new WorkerPool(2);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            decoder, encoder, protocol, connections, pipeline,
            commands, workers, world);
        network.Start();

        // 快照广播器与仿真共享同一 SnapshotStore（仿真写入即成为可下发帧）
        var broadcaster = new SnapshotBroadcaster(
            world, commands, new SnapshotConfig(), network.SnapshotSender, encoder, store: snapshots);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, network.BoundPort);
            var stream = client.GetStream();

            // Act 1：真实 TCP 完成握手至 Playing（包 1 → 4 → 6 → 8 → 12）
            await SendRawFrameAsync(stream, PacketId.ConnectionRequest,
                EncodeVersionPayload(protocol.Version.ConnectVersion));
            await SendPacketAsync(stream, encoder, PacketId.PlayerInfo,
                new PlayerInfoPacket(0, "Tester"));
            await SendRawFrameAsync(stream, PacketId.RequestWorldInfo,
                ReadOnlyMemory<byte>.Empty);
            await SendPacketAsync(stream, encoder, PacketId.TileGetSection,
                new SpawnTileDataPacket(-1, -1, 0));
            await SendPacketAsync(stream, encoder, PacketId.PlayerSpawn,
                new PlayerSpawnPacket(0, 0, 0, 0, 0, 0, 0, 0));

            // Act 2：发送包 13（真实线格式）→ 管线校验 → MoveCommand 入队
            var reported = new Vector2(320f, 480f);
            await SendPacketAsync(stream, encoder, PacketId.PlayerPosition,
                new PlayerControlsPacket(1, reported));

            // 服务端异步处理，轮询等待命令入队（间接确认握手已完成、管线已放行）
            Assert.True(await WaitUntilAsync(() => commands.Count > 0, TimeSpan.FromSeconds(5)),
                "包 13 未在超时内生成 MoveCommand（握手或权威管线未完成）");

            // Act 3：仿真推进 → 快照产出 → 经 ISnapshotSender 下发
            simulator.Tick();
            Assert.True(snapshots.Count > 0, "仿真未产出快照");
            await broadcaster.FlushAsync();

            // Assert：客户端经真实 TCP 收到包 15，玩家实体位置与上报一致
            var frame = await ReadSnapshotAsync(stream, decoder, TimeSpan.FromSeconds(5));
            Assert.NotNull(frame);
            var player = Assert.Single(frame!.Entities, e => e.Id == 1);
            Assert.Equal(reported.X, player.Position.X, 3);
            // Y 允许一个 tick 内的物理积分（重力）微调，仅验证位置确由包 13 驱动
            Assert.True(MathF.Abs(player.Position.Y - reported.Y) < 1f,
                $"玩家 Y 偏移过大：上报 {reported.Y}，快照 {player.Position.Y}");
        }
        finally
        {
            await network.DisposeAsync();
        }
    }

    [Fact]
    public async Task TcpRoundTrip_Packet13_IsForwarded_ToOtherPlayers()
    {
        // Arrange：真实 TCP 监听，两个客户端分别握手至 Playing
        var world = new WorldState();
        var commands = new CommandQueue();
        var recorder = new EventRecorder();
        var snapshots = new SnapshotStore();
        var simulator = new WorldSimulator(world, commands, recorder, snapshots);

        var rate = new RateLimits();
        var audit = new NoOpAuditLogger();
        var enforcers = new AuthorityEnforcers(rate, audit, new WorldState());
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new FrameStage(),
            new RateLimitStage(enforcers.Rate),
            new MovementAuthorityStage(enforcers.Movement, audit),
            new TerminalStage(),
        });

        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager();

        using var workers = new WorkerPool(2);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            decoder, encoder, protocol, connections, pipeline,
            commands, workers, world);
        network.Start();

        try
        {
            using var clientA = new TcpClient();
            using var clientB = new TcpClient();
            await clientA.ConnectAsync(IPAddress.Loopback, network.BoundPort);
            await clientB.ConnectAsync(IPAddress.Loopback, network.BoundPort);
            var streamA = clientA.GetStream();
            var streamB = clientB.GetStream();

            // 先 A 后 B，确保服务端分配 A=#1、B=#2
            await HandshakeAsync(streamA, encoder, protocol, "Alpha");
            Assert.True(await WaitUntilAsync(
                    () => connections.Get(1)?.State == ConnectionState.Playing, TimeSpan.FromSeconds(5)),
                "客户端 A 未进入 Playing");

            await HandshakeAsync(streamB, encoder, protocol, "Beta");
            Assert.True(await WaitUntilAsync(
                    () => connections.Get(2)?.State == ConnectionState.Playing, TimeSpan.FromSeconds(5)),
                "客户端 B 未进入 Playing");

            // Assert 1：B 进服即收到 A 的激活包（包 14），否则原版客户端不会绘制 A
            var active = await ReadPacketAsync(
                streamB, decoder, PacketId.PlayerActive, TimeSpan.FromSeconds(5));
            Assert.NotNull(active);
            var activePacket = Assert.IsType<PlayerActivePacket>(active);
            Assert.Equal(1, activePacket.PlayerId); // A 为服务端分配的 #1
            Assert.True(activePacket.Active);

            // Act：A 发包 13，伪造 PlayerId=7（服务端应覆盖为其真实分配值 #1）
            var reported = new Vector2(320f, 480f);
            await SendPacketAsync(streamA, encoder, PacketId.PlayerPosition,
                new PlayerControlsPacket(7, reported));

            // Assert：B 经真实 TCP 收到转发的包 13，身份与位置均为服务端权威值
            var forwarded = await ReadPacketAsync(
                streamB, decoder, PacketId.PlayerPosition, TimeSpan.FromSeconds(5));
            Assert.NotNull(forwarded);
            var controls = Assert.IsType<PlayerControlsPacket>(forwarded);
            Assert.Equal(1, controls.PlayerId); // 伪造的 7 被覆盖为服务端分配的 #1
            Assert.Equal(reported.X, controls.Position.X, 3);
            Assert.Equal(reported.Y, controls.Position.Y, 3);
        }
        finally
        {
            await network.DisposeAsync();
        }
    }

    [Fact]
    public async Task KickAsync_SendsDisconnect_ThenClosesConnection()
    {
        // Arrange：真实 TCP 监听，握手至 Playing 后由服务端主动踢出
        var world = new WorldState();
        var commands = new CommandQueue();
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new FrameStage(),
            new TerminalStage(),
        });

        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager();

        using var workers = new WorkerPool(2);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            decoder, encoder, protocol, connections, pipeline,
            commands, workers, world);
        network.Start();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, network.BoundPort);
            var stream = client.GetStream();

            await HandshakeAsync(stream, encoder, protocol, "Kickee");
            Assert.True(await WaitUntilAsync(
                    () => connections.Get(1)?.State == ConnectionState.Playing, TimeSpan.FromSeconds(5)),
                "客户端未进入 Playing");

            // Act：踢出玩家 #1
            await connections.KickAsync(1, "test kick");

            // Assert 1：客户端收到包 2（Disconnect），且携带踢出原因
            var kick = await ReadPacketAsync(stream, decoder, PacketId.Disconnect, TimeSpan.FromSeconds(5));
            Assert.NotNull(kick);
            Assert.Equal("test kick", Assert.IsType<DisconnectPacket>(kick).Reason);

            // Assert 2：连接已移除（容量槽位释放）
            Assert.True(await WaitUntilAsync(() => connections.Get(1) is null, TimeSpan.FromSeconds(5)),
                "踢出后连接未从管理器移除");
        }
        finally
        {
            await network.DisposeAsync();
        }
    }

    [Fact]
    public async Task ViolationEscalation_KicksPlayer_AfterThreshold()
    {
        // Arrange：入站管线一律拒绝 + 阈值 2 次 / 60s
        var world = new WorldState();
        var commands = new CommandQueue();
        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager();

        using var workers = new WorkerPool(2);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            decoder, encoder, protocol, connections, new AlwaysRejectPipeline(),
            commands, workers, world,
            violationKick: new ViolationKickLimits(MaxViolations: 2, WindowSeconds: 60));
        network.Start();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, network.BoundPort);
            var stream = client.GetStream();

            await HandshakeAsync(stream, encoder, protocol, "Cheater");
            Assert.True(await WaitUntilAsync(
                    () => connections.Get(1)?.State == ConnectionState.Playing, TimeSpan.FromSeconds(5)),
                "客户端未进入 Playing");

            // Act：连发 2 个必然被权威层拒绝的包（阈值 = 2）
            await SendPacketAsync(stream, encoder, PacketId.PlayerPosition,
                new PlayerControlsPacket(1, new Vector2(0f, 0f)));
            await SendPacketAsync(stream, encoder, PacketId.PlayerPosition,
                new PlayerControlsPacket(1, new Vector2(0f, 0f)));

            // Assert：违规累计达阈值 → 客户端收到包 2，连接被移除
            var kick = await ReadPacketAsync(stream, decoder, PacketId.Disconnect, TimeSpan.FromSeconds(5));
            Assert.NotNull(kick);
            Assert.Contains("Too many violations", Assert.IsType<DisconnectPacket>(kick).Reason);
            Assert.True(await WaitUntilAsync(() => connections.Get(1) is null, TimeSpan.FromSeconds(5)),
                "达阈值后连接未从管理器移除");
        }
        finally
        {
            await network.DisposeAsync();
        }
    }

    [Fact]
    public async Task Correction_IsSent_WithItsOwnPacketType()
    {
        // Arrange：管线对任意包都返回"纠正"，纠正包为背包槽同步（包 5，非健康包 16）
        var world = new WorldState();
        var commands = new CommandQueue();
        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager();

        var correction = new InventorySlotPacket(Slot: 3, ItemId: 42, Stack: 1) { PlayerId = 1 };
        using var workers = new WorkerPool(2);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            decoder, encoder, protocol, connections, new AlwaysCorrectPipeline(correction),
            commands, workers, world);
        network.Start();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, network.BoundPort);
            var stream = client.GetStream();

            await HandshakeAsync(stream, encoder, protocol, "Correctee");
            Assert.True(await WaitUntilAsync(
                    () => connections.Get(1)?.State == ConnectionState.Playing, TimeSpan.FromSeconds(5)),
                "客户端未进入 Playing");

            // Act：发任意包 → 管线返回 Correct
            await SendPacketAsync(stream, encoder, PacketId.PlayerPosition,
                new PlayerControlsPacket(1, new Vector2(0f, 0f)));

            // Assert：纠正包按自身类型（包 5 SyncEquipment）下发，而不是硬编码的包 16
            var corrected = await ReadPacketAsync(
                stream, decoder, PacketId.InventorySlot, TimeSpan.FromSeconds(5));
            Assert.NotNull(corrected);
            var slot = Assert.IsType<InventorySlotPacket>(corrected);
            Assert.Equal(3, slot.Slot);
            Assert.Equal(42, slot.ItemId);
        }
        finally
        {
            await network.DisposeAsync();
        }
    }

    [Fact]
    public async Task ServerApi_KickAndBan_TakeEffectOnLiveConnection()
    {
        // Arrange：真实 TCP + 插件服务端 API（此前 KickPlayer / BanPlayer 仅写审计、不生效）
        var world = new WorldState();
        var commands = new CommandQueue();
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new FrameStage(),
            new TerminalStage(),
        });

        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(ProtocolVersion.Current);
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager();
        var bans = new RecordingBanManager();

        using var workers = new WorkerPool(2);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            decoder, encoder, protocol, connections, pipeline,
            commands, workers, world);
        network.Start();

        var server = new ServerApi(
            new NoOpAuditLogger(), new PrometheusMetrics(), network, connections, bans, world);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, network.BoundPort);
            var stream = client.GetStream();

            await HandshakeAsync(stream, encoder, protocol, "Target");
            Assert.True(await WaitUntilAsync(
                    () => connections.Get(1)?.State == ConnectionState.Playing, TimeSpan.FromSeconds(5)),
                "客户端未进入 Playing");

            // Assert 0：插件能读到真实玩家信息（名称来自包 4）
            var online = Assert.Single(server.GetOnlinePlayers());
            Assert.Equal(1, online.PlayerId);
            Assert.Equal("Target", online.Name);
            Assert.True(online.IsConnected);

            // Act 1：插件踢出玩家 → 客户端收到包 2，原因来自插件
            server.KickPlayer(1, "plugin kick");
            var kick = await ReadPacketAsync(stream, decoder, PacketId.Disconnect, TimeSpan.FromSeconds(5));
            Assert.NotNull(kick);
            Assert.Equal("plugin kick", Assert.IsType<DisconnectPacket>(kick).Reason);

            // Act 2：插件封禁玩家 → 封禁身份须与审计链路一致（否则封禁写入的是另一个身份）
            server.BanPlayer(1, TimeSpan.FromHours(2), "cheating");
            Assert.Equal(PlayerIdentity.ToGuid(1), bans.LastPlayerId);
            Assert.Equal("cheating", bans.LastReason);
            Assert.Equal(TimeSpan.FromHours(2), bans.LastDuration);
        }
        finally
        {
            await network.DisposeAsync();
        }
    }

    /// <summary>测试替身：记录最近一次封禁请求（验证 ServerApi 的参数与身份映射）。</summary>
    private sealed class RecordingBanManager : IBanManager
    {
        public Guid LastPlayerId { get; private set; }
        public string? LastReason { get; private set; }
        public TimeSpan? LastDuration { get; private set; }

        public Task<bool> ReportViolationAsync(Guid playerId, string reason) => Task.FromResult(false);

        public Task<bool> IsBannedAsync(Guid playerId, string ipAddress) => Task.FromResult(false);

        public Task BanAsync(Guid playerId, string reason, TimeSpan? duration)
        {
            LastPlayerId = playerId;
            LastReason = reason;
            LastDuration = duration;
            return Task.CompletedTask;
        }

        public Task UnbanAsync(Guid playerId, string reason) => Task.CompletedTask;
    }

    /// <summary>测试替身：一律拒绝的入站管线（验证"违规累计 → 踢出"处置闭环）。</summary>
    private sealed class AlwaysRejectPipeline : IInboundPipeline
    {
        public Task<AuthorityResult> ProcessAsync(
            INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct = default)
            => Task.FromResult(AuthorityResult.Reject("test_reject"));
    }

    /// <summary>测试替身：一律返回"纠正"的入站管线（验证纠正包按自身类型下发）。</summary>
    private sealed class AlwaysCorrectPipeline : IInboundPipeline
    {
        private readonly INetworkPacket _correction;
        public AlwaysCorrectPipeline(INetworkPacket correction) => _correction = correction;

        public Task<AuthorityResult> ProcessAsync(
            INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct = default)
            => Task.FromResult(AuthorityResult.Correct(_correction, "test_correct"));
    }

    // ---------- 测试辅助：真实 TCP 收发 ----------

    /// <summary>真实 TCP 完成握手至 Playing（包 1 → 4 → 6 → 8 → 12）。</summary>
    private static async Task HandshakeAsync(
        NetworkStream stream, PacketEncoder encoder, TerrariaProtocol protocol, string name)
    {
        await SendRawFrameAsync(stream, PacketId.ConnectionRequest,
            EncodeVersionPayload(protocol.Version.ConnectVersion));
        await SendPacketAsync(stream, encoder, PacketId.PlayerInfo,
            new PlayerInfoPacket(0, name));
        await SendRawFrameAsync(stream, PacketId.RequestWorldInfo,
            ReadOnlyMemory<byte>.Empty);
        await SendPacketAsync(stream, encoder, PacketId.TileGetSection,
            new SpawnTileDataPacket(-1, -1, 0));
        await SendPacketAsync(stream, encoder, PacketId.PlayerSpawn,
            new PlayerSpawnPacket(0, 0, 0, 0, 0, 0, 0, 0));
    }

    /// <summary>
    /// 从 TCP 流读取直到解析出指定类型的包；其余握手/状态包跳过。
    /// 返回 null 表示超时或连接关闭。
    /// </summary>
    private static async Task<INetworkPacket?> ReadPacketAsync(
        NetworkStream stream, PacketDecoder decoder, PacketId wanted, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var pending = new List<byte>();
        var temp = new byte[8192];

        try
        {
            while (true)
            {
                var buffer = new ReadOnlySequence<byte>(pending.ToArray());
                if (Framing.TryReadFrame(ref buffer, out var type, out var payload))
                {
                    pending.RemoveRange(0, pending.Count - (int)buffer.Length);

                    if (type == wanted)
                        return decoder.Decode(type, payload.ToArray(), new DecodeContext());
                    continue;
                }

                int read = await stream.ReadAsync(temp, cts.Token).ConfigureAwait(false);
                if (read == 0)
                    return null; // 服务端关闭连接
                pending.AddRange(temp[..read]);
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>用编码器把出站包编码成帧并写入 TCP 流。</summary>
    private static async Task SendPacketAsync(
        NetworkStream stream, IPacketEncoder encoder, PacketId type, INetworkPacket packet)
    {
        var writer = new ArrayBufferWriter<byte>();
        encoder.Encode(writer, type, packet);
        await stream.WriteAsync(writer.WrittenMemory).ConfigureAwait(false);
    }

    /// <summary>写入一条手工构造的帧（用于编码器未覆盖的客户端包，如包 1 / 6）。</summary>
    private static async Task SendRawFrameAsync(
        NetworkStream stream, PacketId type, ReadOnlyMemory<byte> payload)
    {
        var writer = new ArrayBufferWriter<byte>();
        Framing.WriteFrame(writer, type, payload.Span);
        await stream.WriteAsync(writer.WrittenMemory).ConfigureAwait(false);
    }

    /// <summary>包 1 的 payload：7-bit 长度前缀 + UTF-8 版本串（如 "Terraria326"）。</summary>
    private static byte[] EncodeVersionPayload(string version)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            PacketEncoder.WritePrefixedString(bw, version);
        return ms.ToArray();
    }

    /// <summary>轮询等待条件成立（服务端异步处理的同步点）。</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(10).ConfigureAwait(false);
        }
        return condition();
    }

    /// <summary>
    /// 从 TCP 流读取直到解析出包 15（Snapshot）；其余握手/状态包跳过。
    /// 返回 null 表示超时或连接关闭。
    /// </summary>
    private static async Task<SnapshotFrame?> ReadSnapshotAsync(
        NetworkStream stream, PacketDecoder decoder, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var pending = new List<byte>();
        var temp = new byte[8192];

        try
        {
            while (true)
            {
                var buffer = new ReadOnlySequence<byte>(pending.ToArray());
                if (Framing.TryReadFrame(ref buffer, out var type, out var payload))
                {
                    pending.RemoveRange(0, pending.Count - (int)buffer.Length);

                    if (type == PacketId.Snapshot)
                        return decoder.DecodeSnapshot(payload.ToArray());
                    continue;
                }

                int read = await stream.ReadAsync(temp, cts.Token).ConfigureAwait(false);
                if (read == 0)
                    return null; // 服务端关闭连接
                pending.AddRange(temp[..read]);
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    [Fact]
    public async Task ConfigThresholds_AreApplied_ByBootstrap()
    {
        // 验证"改 server.json 即生效"：先前仅 MovementLimits 接入配置，其余走代码默认值
        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-cfg-{Guid.NewGuid():N}.db");
        var configPath = Path.Combine(Path.GetTempPath(), $"terraauth-cfg-{Guid.NewGuid():N}.json");
        var dbPathDefault = Path.Combine(Path.GetTempPath(), $"terraauth-cfg-{Guid.NewGuid():N}.db");
        var configPathDefault = Path.Combine(Path.GetTempPath(), $"terraauth-cfg-{Guid.NewGuid():N}.json");

        // 自定义配置：单次伤害上限压到 1
        await File.WriteAllTextAsync(configPath, "{ \"MaxSingleDamage\": 1 }");

        try
        {
            // Act 1：自定义阈值 → 500 点伤害必须被拒
            using (var host = GameHost.Bootstrap(dbPath, configPath, metricsPort: 0, port: 0))
            {
                var rejected = await host.Pipeline.ProcessAsync(
                    new NpcStrikePacket(1, 500), playerId: 1, new CommandQueue(), default);
                Assert.Equal(AuthorityDecision.Reject, rejected.Decision);
                Assert.Equal("damage_exceeded", rejected.Reason);
            }

            // Act 2：默认阈值（配置缺失 → Bootstrap 生成默认）→ 同一包放行，证明差异确实来自配置
            using (var host = GameHost.Bootstrap(dbPathDefault, configPathDefault, metricsPort: 0, port: 0))
            {
                var accepted = await host.Pipeline.ProcessAsync(
                    new NpcStrikePacket(1, 500), playerId: 1, new CommandQueue(), default);
                Assert.Equal(AuthorityDecision.Accept, accepted.Decision);
            }
        }
        finally
        {
            foreach (var path in new[] { dbPath, configPath, dbPathDefault, configPathDefault })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Config_Rejects_MaxSingleDamage_Above_Int16()
    {
        // 包 28 的伤害线格式为 Int16：上限超过 32767 将永不触发 → 启动即拒绝并给出明确提示
        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-dmg-{Guid.NewGuid():N}.db");
        var configPath = Path.Combine(Path.GetTempPath(), $"terraauth-dmg-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, "{ \"MaxSingleDamage\": 40000 }");

        try
        {
            var ex = Assert.Throws<System.IO.InvalidDataException>(
                () => GameHost.Bootstrap(dbPath, configPath, metricsPort: 0, port: 0));
            Assert.Contains("Int16", ex.Message);
        }
        finally
        {
            foreach (var path in new[] { dbPath, configPath })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Metrics_SetGauge_IsExportedInPrometheusText()
    {
        using var metrics = new Monitoring.PrometheusMetrics();
        metrics.SetGauge("terraauth_test_gauge", 1.5, ("plugin", "demo"));

        var text = metrics.ExportAsText();
        Assert.Contains("terraauth_test_gauge{plugin=\"demo\"} 1.5", text);
    }

    [Fact]
    public async Task EventStore_Query_Returns_RecentAudit()
    {
        static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
        {
            var list = new List<T>();
            await foreach (var item in source) list.Add(item);
            return list;
        }

        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-ev-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new Persistence.SqlitePersistence(dbPath);
            await db.AppendAsync(new Persistence.AuditEntry(
                DateTime.UtcNow, Security.PlayerIdentity.ToGuid(1),
                "position_rejected", "authority:speed_exceeded {\"dt\":10}", null));

            var store = new Plugins.CoreEventStore(db);

            // 审计是异步批量落盘（250ms 一轮），轮询等待可见
            var events = new List<Plugins.EventRecord>();
            for (int i = 0; i < 30 && events.Count == 0; i++)
            {
                await Task.Delay(100);
                events = await CollectAsync(store.QueryAsync(playerId: 1));
            }

            var single = Assert.Single(events);
            Assert.Equal(1, single.PlayerId);
            Assert.Equal("authority", single.Category);
            Assert.Equal("position_rejected", single.Action);
            Assert.Equal("speed_exceeded", single.Reason);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task ConfigHotReload_UpdatesThresholds_WithoutRestart()
    {
        // 验证运行中改配置 → 阈值立即生效（此前 OnConfigurationChanged 只打印日志，改配置等于没用）
        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-hot-{Guid.NewGuid():N}.db");
        var configPath = Path.Combine(Path.GetTempPath(), $"terraauth-hot-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, "{ \"MaxSingleDamage\": 30000 }");

        try
        {
            using var host = GameHost.Bootstrap(dbPath, configPath, metricsPort: 0, port: 0);

            // 改动前：500 点伤害放行
            var before = await host.Pipeline.ProcessAsync(
                new NpcStrikePacket(1, 500), playerId: 1, new CommandQueue(), default);
            Assert.Equal(AuthorityDecision.Accept, before.Decision);

            // 运行中收紧阈值 → 重载（等价于 FileSystemWatcher 检测到文件变更后调用 Reload）
            await File.WriteAllTextAsync(configPath, "{ \"MaxSingleDamage\": 1 }");
            host.Config.Reload();

            // 改动后：同一包被拒，无需重启
            var after = await host.Pipeline.ProcessAsync(
                new NpcStrikePacket(1, 500), playerId: 1, new CommandQueue(), default);
            Assert.Equal(AuthorityDecision.Reject, after.Decision);
            Assert.Equal("damage_exceeded", after.Reason);
        }
        finally
        {
            foreach (var path in new[] { dbPath, configPath })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public void GameHost_CanBuild_AllPhases_Wired()
    {
        // 验证 GameHost 能构造（所有 Phase 依赖注入正确）
        // 真实启动需 TCP 端口，此处仅验证组装不抛异常
        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-test-{Guid.NewGuid():N}.db");
        var configPath = Path.Combine(Path.GetTempPath(), $"terraauth-test-{Guid.NewGuid():N}.json");
        try
        {
            using var host = GameHost.Bootstrap(dbPath: dbPath, configPath: configPath, metricsPort: 0, port: 0);
            Assert.NotNull(host);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    // ========================================================================
    // Phase 6：持久化往返（"进程重启"后数据仍在）
    // ========================================================================

    [Fact]
    public async Task Persistence_Player_And_Audit_SurviveReopen()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-persist-{Guid.NewGuid():N}.db");
        var playerId = Guid.NewGuid();
        try
        {
            // Act 1：写入玩家档案 + 审计，随后关闭（模拟进程退出）
            using (var db = new SqlitePersistence(dbPath))
            {
                await db.CreateIfNotExistsAsync(playerId, "Alice");
                await db.SaveAsync(new PlayerData(playerId, "Alice", new byte[] { 1, 2, 3 }, 250, 120));
                await db.AppendAsync(new AuditEntry(DateTime.UtcNow, playerId, "authority", "speed_exceeded", "1.2.3.4"));
            }

            // Act 2：重新打开（模拟进程重启）→ 数据必须仍在
            using (var db = new SqlitePersistence(dbPath))
            {
                var loaded = await db.GetAsync(playerId);
                Assert.NotNull(loaded);
                Assert.Equal("Alice", loaded!.Name);
                Assert.Equal(250, loaded.MaxHp);
                Assert.Equal(120, loaded.MaxMp);
                Assert.Equal(new byte[] { 1, 2, 3 }, loaded.InventoryBlob);

                var events = await db.QueryByPlayerAsync(playerId, DateTime.UtcNow.AddMinutes(-5));
                var only = Assert.Single(events);
                Assert.Equal("authority", only.EventType);
                Assert.Equal("speed_exceeded", only.Detail);
                Assert.Equal("1.2.3.4", only.IpAddress);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task Persistence_Ban_SurvivesReopen()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-ban-{Guid.NewGuid():N}.db");
        var playerId = Guid.NewGuid();
        try
        {
            // Act 1：落一条封禁，关闭
            using (var db = new SqlitePersistence(dbPath))
            {
                await new SqliteBanStore(db).AddAsync(
                    new BanRecord(playerId, "10.0.0.7", "auto:cheat", DateTime.UtcNow.AddHours(24)));
            }

            // Act 2：重启后按玩家 ID 与按 IP 都应命中
            using (var db = new SqlitePersistence(dbPath))
            {
                var store = new SqliteBanStore(db);
                Assert.True(await store.ContainsAsync(playerId, ""));
                Assert.True(await store.ContainsAsync(Guid.NewGuid(), "10.0.0.7"));
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task Persistence_ExpiredBan_IsNotActive()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"terraauth-expban-{Guid.NewGuid():N}.db");
        var playerId = Guid.NewGuid();
        try
        {
            using (var db = new SqlitePersistence(dbPath))
                await new SqliteBanStore(db).AddAsync(
                    new BanRecord(playerId, "", "old", DateTime.UtcNow.AddMinutes(-1))); // 已过期

            using (var db = new SqlitePersistence(dbPath))
                Assert.False(await new SqliteBanStore(db).ContainsAsync(playerId, ""));
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    /// <summary>清理 DB 及其附属文件（SQLite 的 -wal/-shm、内嵌 LiteDb 的 .tmp）。</summary>
    private static void CleanupDb(string dbPath)
    {
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".tmp" })
            if (File.Exists(path)) File.Delete(path);
    }
}
