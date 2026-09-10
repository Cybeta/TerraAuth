// TerraAuth — 端到端集成测试
// 验证架构闭环：包 → 管线(Phase2) → Command(Phase3) → 仿真 → 快照(Phase4) → 编码(Phase5)

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using TerraAuth.Authority;
using TerraAuth.Concurrency;
using TerraAuth.Net.Phase4;
using TerraAuth.Net.Phase5;
using TerraAuth.Protocol;
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
}
