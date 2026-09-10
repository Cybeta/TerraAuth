// TerraAuth — Phase 5: 网络主机（监听 + Accept 循环）
// 串联：TCP 监听 → Connection 生命周期 → 入站管线 → 仿真 / 出站快照

using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using TerraAuth.Authority; // IInboundPipeline, AuthorityResult
using TerraAuth.Concurrency; // WorkerPool
using TerraAuth.Plugins;   // IHookRegistry / PlayerJoinedArgs / PlayerLeftArgs
using TerraAuth.Protocol;  // INetworkPacket
using TerraAuth.Simulation; // CommandQueue

namespace TerraAuth.Net.Phase5;

/// <summary>
/// 服务端网络主机。
/// 职责：
///   1. TcpListener 接受新连接
///   2. 为每个连接创建 Connection + 运行读写循环
///   3. 入站包 → IInboundPipeline（Phase 2）→ CommandQueue（Phase 3）
///   4. 出站快照 → ISnapshotSender（Phase 4）
/// </summary>
public sealed class NetworkHost : IAsyncDisposable
{
    /// <summary>玩家名长度上限（与原版角色命名一致）。</summary>
    private const int MaxPlayerNameLength = 20;

    private readonly TcpListener _listener;
    private readonly IPacketDecoder _decoder;
    private readonly IPacketEncoder _encoder;
    private readonly ITerrariaProtocol _protocol;
    private readonly ConnectionManager _connections;
    private readonly IInboundPipeline _pipeline;
    private readonly CommandQueue _commands;
    private readonly WorkerPool _workers;
    private readonly WorldState _world;
    private readonly IReadOnlyCollection<string>? _playerWhitelist;
    private readonly IHookRegistry? _hooks;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>玩家外观（包 4）：PlayerId → SyncPlayer，进服后用于向其他玩家广播。</summary>
    private readonly ConcurrentDictionary<int, PlayerInfoPacket> _playerAppearances = new();

    /// <summary>进服时间（用于 PlayerLeftArgs.SessionDuration）。</summary>
    private readonly ConcurrentDictionary<int, DateTimeOffset> _sessionStart = new();

    /// <summary>诊断：权威层拒绝计数（PersistenceAuditLogger 只落库不打印，拒绝原因需在控制台可见）。</summary>
    private long _rejectCount;

    private Task? _acceptLoop;

    public ISnapshotSender SnapshotSender { get; }

    /// <summary>实际监听端口（endpoint 端口传 0 时由 OS 分配）；<see cref="Start"/> 之后有效。</summary>
    public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public NetworkHost(
        IPEndPoint endpoint,
        IPacketDecoder decoder,
        IPacketEncoder encoder,
        ITerrariaProtocol protocol,
        ConnectionManager connections,
        IInboundPipeline pipeline,
        CommandQueue commands,
        WorkerPool workers,
        WorldState world,
        IReadOnlyCollection<string>? playerWhitelist = null,
        IHookRegistry? hooks = null)
    {
        _listener = new TcpListener(endpoint);
        _decoder = decoder;
        _encoder = encoder;
        _protocol = protocol;
        _connections = connections;
        _pipeline = pipeline;
        _commands = commands;
        _workers = workers;
        _world = world;
        _playerWhitelist = playerWhitelist;
        _hooks = hooks;

        SnapshotSender = new ConnectionSnapshotSender(connections, encoder);
    }

    /// <summary>启动监听 + Accept 循环。</summary>
    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>优雅关闭。</summary>
    public async Task StopAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        if (_acceptLoop is not null)
            await _acceptLoop.ConfigureAwait(false);

        foreach (var conn in _connections.All())
            await conn.DisposeAsync().ConfigureAwait(false);
    }

    // ---------- Accept 循环 ----------

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var socket = await _listener.AcceptSocketAsync(ct).ConfigureAwait(false);
                // 位置包（包 13）仅 ~30 字节且 60Hz 高频：若不禁用 Nagle，小包会被 TCP 栈
                // 攒到对端 delayed ACK（~40ms）才成批发，接收端看到的就是一顿一顿的抖动。
                socket.NoDelay = true;
                var remote = socket.RemoteEndPoint?.ToString() ?? "unknown";
                var stream = new NetworkStream(socket, ownsSocket: true);
                var connection = new Connection(stream, _decoder, _encoder, _protocol.Version, _workers)
                {
                    RemoteEndPoint = remote,
                };

                if (!_connections.TryAdd(connection, out var playerId))
                {
                    stream.Close();
                    continue;
                }

                Console.WriteLine($"[Net] 新连接 {remote} → 玩家 #{playerId}");

                // 启动该连接的读写循环（不 await，后台运行）；结束后触发 PlayerLeft Hook
                _ = RunConnectionAsync(connection, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkHost] Accept error: {ex.Message}");
            }
        }
    }

    private async Task RunConnectionAsync(Connection connection, CancellationToken ct)
    {
        await connection.RunAsync(OnPacketAsync, ct).ConfigureAwait(false);
        OnConnectionClosed(connection);
    }

    /// <summary>连接结束：清理外观缓存并触发 PlayerLeft Hook。</summary>
    private void OnConnectionClosed(Connection connection)
    {
        _playerAppearances.TryRemove(connection.PlayerId, out var info);
        var name = info?.Name ?? "";

        // 通知其他玩家该玩家已离线（包 14 置为未激活），否则原版客户端会残留幽灵玩家
        if (connection.PlayerId > 0)
            _ = BroadcastLeaveAsync(connection.PlayerId);

        if (_hooks is null) return;

        var duration = _sessionStart.TryRemove(connection.PlayerId, out var start)
            ? DateTimeOffset.UtcNow - start
            : TimeSpan.Zero;

        _hooks.Trigger(new PlayerLeftArgs
        {
            PlayerId = connection.PlayerId,
            PlayerName = name,
            Reason = "Disconnected",
            SessionDuration = duration,
        });
    }

    /// <summary>广播玩家离线（包 14 Active=false）给其余玩家。</summary>
    private async Task BroadcastLeaveAsync(int playerId)
    {
        try
        {
            await _connections.BroadcastExceptAsync(
                playerId,
                PacketId.PlayerActive,
                new PlayerActivePacket((byte)playerId, Active: false),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NetworkHost] 广播玩家 #{playerId} 离线失败: {ex.Message}");
        }
    }

    // ---------- 入站包处理：管线 → 仿真 ----------

    private async Task OnPacketAsync(INetworkPacket packet, Connection connection, CancellationToken ct)
    {
        // 状态流转：握手 / 认证
        if (!await HandleConnectionStateAsync(packet, connection, ct).ConfigureAwait(false))
            return;

        // 走 Phase 2 权威管线
        var result = await _pipeline.ProcessAsync(
            packet,
            connection.PlayerId,
            _commands,
            ct).ConfigureAwait(false);

        switch (result.Decision)
        {
            case AuthorityDecision.Accept:
                // Command 已由管线写入 _commands（Phase 3 仿真消费）
                // 包 13：转发给其他玩家（原版客户端会忽略 TerraAuth 专用快照包 15，玩家间可见性依赖包 13）
                if (packet is PlayerControlsPacket controls)
                    await BroadcastControlsAsync(connection, controls, ct).ConfigureAwait(false);
                // 包 17 / 79：挖砖/放砖权威通过 → 广播给其他玩家，原版客户端会自动更新 tile 显示
                else if (packet is TileBreakPacket brk)
                    await _connections.BroadcastExceptAsync(connection.PlayerId, PacketId.TileBreak, brk, ct).ConfigureAwait(false);
                else if (packet is TilePlacePacket place)
                    await _connections.BroadcastExceptAsync(connection.PlayerId, PacketId.TilePlace, place, ct).ConfigureAwait(false);
                break;

            case AuthorityDecision.Correct:
                // 服务端权威纠正（如 HP/MP snap back）→ 下发纠正包
                if (result.CorrectionPacket is not null)
                    await connection.SendEncodedAsync(
                        PacketId.PlayerHealth, // TODO: 按包类型映射
                        result.CorrectionPacket,
                        ct).ConfigureAwait(false);
                break;

            case AuthorityDecision.Reject:
            case AuthorityDecision.RejectSilent:
                // 违规：记录审计（Phase 2 IAuditLogger；PersistenceAuditLogger 不输出控制台）
                // 诊断：限频打印拒绝原因，否则"移动包是否被权威层丢弃"在服务端完全不可见
                var rejectNo = Interlocked.Increment(ref _rejectCount);
                if (rejectNo <= 50 || rejectNo % 1000 == 0)
                    Console.WriteLine($"[Authority] 拒绝 #{rejectNo} 玩家 #{connection.PlayerId}: {result.Reason}");
                // TODO: 严重违规累计 → KickAsync
                break;
        }
    }

    /// <summary>
    /// 包 13 转发：把玩家控制状态发给其他 Playing 连接。
    /// 原版客户端不解析 TerraAuth 专用快照包 15，玩家间可见性依赖原版包 13。
    /// PlayerId 以服务端分配值覆盖，防止客户端伪造他人身份驱动其移动。
    /// </summary>
    private async Task BroadcastControlsAsync(
        Connection sender, PlayerControlsPacket controls, CancellationToken ct)
    {
        var forwarded = controls with { PlayerId = (byte)sender.PlayerId };
        await _connections.BroadcastExceptAsync(
            sender.PlayerId, PacketId.PlayerPosition, forwarded, ct).ConfigureAwait(false);
    }

    // ---------- 连接状态管理 ----------

    /// <summary>返回 true 表示该包应继续走权威管线。</summary>
    private async Task<bool> HandleConnectionStateAsync(
        INetworkPacket packet,
        Connection connection,
        CancellationToken ct)
    {
        switch (packet)
        {
            case ConnectionRequestPacket req when connection.State == ConnectionState.Handshaking:
                await HandleConnectionRequest(req, connection, ct).ConfigureAwait(false);
                return false; // 握手包已消费

            case PlayerInfoPacket info when connection.State == ConnectionState.Authenticating:
                // 包 4 SyncPlayer：校验玩家名 + 白名单，并记录外观
                await HandlePlayerInfoAsync(info, connection, ct).ConfigureAwait(false);
                return false;

            case RequestWorldInfoPacket when connection.State == ConnectionState.Authenticating:
                // 包 6 RequestWorldData：服务端回包 7（原版 State 仍停留认证阶段）
                await SendWorldInfoAsync(connection, ct).ConfigureAwait(false);
                return false;

            case SpawnTileDataPacket spawn when connection.State == ConnectionState.Authenticating:
                // 包 8 SpawnTileData：服务端回包 9（进度）→ 逐块 10（TileSection）→ 49（InitialSpawn）
                await HandleSpawnTileDataAsync(spawn, connection, ct).ConfigureAwait(false);
                return false;

            case PlayerSpawnPacket when connection.State == ConnectionState.Authenticating:
                // 包 12 PlayerSpawn：服务端置 Playing 并回包 129（连接完成）
                await HandlePlayerSpawnAsync(connection, ct).ConfigureAwait(false);
                return false;

            default:
                return connection.State == ConnectionState.Playing;
        }
    }

    /// <summary>
    /// 包 8：客户端请求出生区块。回 9（状态文本，携带区块总数）→ 逐块 10（TileSection）→ 49（InitialSpawn）。
    /// 区块矩形与顺序对应原版 <c>MessageBuffer</c> case 8：
    ///   世界出生点 5×3 矩形 + 请求出生点 6×4 矩形（去重）。
    /// </summary>
    private async Task HandleSpawnTileDataAsync(
        SpawnTileDataPacket spawn, Connection connection, CancellationToken ct)
    {
        int maxSectionsX = _world.MaxTilesX / 200;
        int maxSectionsY = _world.MaxTilesY / 150;

        var sections = new List<(int X, int Y)>();
        var seen = new HashSet<(int X, int Y)>();

        void AddSection(int sx, int sy)
        {
            if (sx < 0 || sy < 0 || sx >= maxSectionsX || sy >= maxSectionsY) return;
            if (seen.Add((sx, sy))) sections.Add((sx, sy));
        }

        // 世界出生点矩形：5 宽 × 3 高（半开区间）
        int sx0 = TileMap.GetSectionX(_world.SpawnTileX) - 2;
        int sy0 = TileMap.GetSectionY(_world.SpawnTileY) - 1;
        int sx1 = sx0 + 5;
        int sy1 = sy0 + 3;
        if (sx0 < 0) sx0 = 0;
        if (sx1 > maxSectionsX) sx1 = maxSectionsX;
        if (sy0 < 0) sy0 = 0;
        if (sy1 > maxSectionsY) sy1 = maxSectionsY;
        for (int sx = sx0; sx < sx1; sx++)
            for (int sy = sy0; sy < sy1; sy++)
                AddSection(sx, sy);

        // 请求出生点矩形：6 宽 × 4 高（含端点），仅当请求坐标有效
        bool requestedValid =
            spawn.SpawnX != -1 && spawn.SpawnY != -1 &&
            spawn.SpawnX >= 10 && spawn.SpawnX <= _world.MaxTilesX - 10 &&
            spawn.SpawnY >= 10 && spawn.SpawnY <= _world.MaxTilesY - 10;
        if (requestedValid)
        {
            int rx0 = TileMap.GetSectionX(spawn.SpawnX) - 2;
            int ry0 = TileMap.GetSectionY(spawn.SpawnY) - 1;
            int rx1 = rx0 + 5;
            int ry1 = ry0 + 3;
            if (rx0 < 0) rx0 = 0;
            if (rx1 >= maxSectionsX) rx1 = maxSectionsX - 1;
            if (ry0 < 0) ry0 = 0;
            if (ry1 >= maxSectionsY) ry1 = maxSectionsY - 1;
            for (int sx = rx0; sx <= rx1; sx++)
                for (int sy = ry0; sy <= ry1; sy++)
                    AddSection(sx, sy);
        }

        await connection.SendEncodedAsync(
            PacketId.StatusText,
            new StatusTextPacket(StatusMax: sections.Count, StatusText: "Receiving tile data"),
            ct).ConfigureAwait(false);

        foreach (var (sx, sy) in sections)
        {
            int xStart = sx * 200;
            int yStart = sy * 150;
            int width = Math.Min(200, _world.MaxTilesX - xStart);
            int height = Math.Min(150, _world.MaxTilesY - yStart);

            await connection.SendEncodedAsync(
                PacketId.TileSendSection,
                new TileSectionPacket(_world, xStart, yStart, width, height),
                ct).ConfigureAwait(false);
        }

        await connection.SendEncodedAsync(
            PacketId.InitialSpawn,
            new InitialSpawnPacket(),
            ct).ConfigureAwait(false);
    }

    /// <summary>包 12：客户端完成出生 → 进入 Playing，回包 129（FinishedConnecting），并主动下发玩家状态。</summary>
    private async Task HandlePlayerSpawnAsync(Connection connection, CancellationToken ct)
    {
        connection.State = ConnectionState.Playing;
        Console.WriteLine(
            $"[Net] 玩家 #{connection.PlayerId} 进入世界（出生点 {_world.SpawnTileX},{_world.SpawnTileY}）");

        TriggerPlayerJoined(connection);

        await connection.SendEncodedAsync(
            PacketId.FinishedConnecting,
            new FinishedConnectingPacket(),
            ct).ConfigureAwait(false);

        await BroadcastJoinAsync(connection, ct).ConfigureAwait(false);
    }

    /// <summary>触发 PlayerJoined Hook（进服快照；HP/MP 用原版出生默认值）。</summary>
    private void TriggerPlayerJoined(Connection connection)
    {
        if (_hooks is null) return;

        _playerAppearances.TryGetValue(connection.PlayerId, out var info);
        var name = info?.Name ?? "";
        _sessionStart[connection.PlayerId] = DateTimeOffset.UtcNow;

        _hooks.Trigger(new PlayerJoinedArgs
        {
            PlayerId = connection.PlayerId,
            PlayerName = name,
            State = new PlayerStateSnapshot(
                PlayerId: connection.PlayerId,
                Name: name,
                Hp: 100, MaxHp: 100,
                Mp: 20, MaxMp: 20,
                X: _world.SpawnTileX * 16f,
                Y: _world.SpawnTileY * 16f,
                IsConnected: true),
        });
    }

    /// <summary>
    /// 玩家进入 Playing 后主动下发玩家状态，避免原版客户端空角色：
    ///   1. 向新玩家下发自身 + 已在线玩家的外观（包 4），并为既有玩家下发激活（包 14）
    ///   2. 向其他在线玩家广播新玩家外观（包 4）与激活（包 14）
    ///   3. 标记自身为激活（包 14）
    /// 原版客户端仅当 <c>Main.player[i].active</c> 为真时才绘制该玩家，故包 14 必须发给所有玩家（含新玩家视角下的既有玩家）。
    /// </summary>
    private async Task BroadcastJoinAsync(Connection connection, CancellationToken ct)
    {
        int selfId = connection.PlayerId;

        // 1) 新玩家可见的既有玩家：外观 + 激活（自身的激活由步骤 3 单独下发）
        foreach (var kvp in _playerAppearances)
        {
            await connection.SendEncodedAsync(
                PacketId.PlayerInfo, kvp.Value, ct).ConfigureAwait(false);

            if (kvp.Key != selfId)
            {
                await connection.SendEncodedAsync(
                    PacketId.PlayerActive,
                    new PlayerActivePacket((byte)kvp.Key, Active: true),
                    ct).ConfigureAwait(false);
            }
        }

        // 2) 既有玩家可见新玩家：外观 + 激活（外观缓存中的 Slot 已是服务端分配的 PlayerId）
        if (_playerAppearances.TryGetValue(selfId, out var selfInfo))
        {
            foreach (var other in _connections.All())
            {
                if (other.PlayerId == selfId || other.State != ConnectionState.Playing)
                    continue;

                await other.SendEncodedAsync(
                    PacketId.PlayerInfo, selfInfo, ct).ConfigureAwait(false);
                await other.SendEncodedAsync(
                    PacketId.PlayerActive,
                    new PlayerActivePacket((byte)selfId, Active: true),
                    ct).ConfigureAwait(false);
            }
        }

        // 3) 自身激活
        await connection.SendEncodedAsync(
            PacketId.PlayerActive,
            new PlayerActivePacket((byte)selfId, Active: true),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 包 4 SyncPlayer：校验玩家名（合法性 + 白名单）并记录外观，供进服后广播。
    /// Steam 票据校验走 NetModule（包 82）+ Steam Web API，当前版本未接入（离线环境无法验证）。
    /// </summary>
    private async Task HandlePlayerInfoAsync(
        PlayerInfoPacket info, Connection connection, CancellationToken ct)
    {
        var name = info.Name?.Trim() ?? string.Empty;

        // 名称合法性（系统边界校验）
        if (name.Length == 0 || name.Length > MaxPlayerNameLength)
        {
            await KickAsync(connection, "Invalid player name", ct).ConfigureAwait(false);
            return;
        }

        // 白名单：未配置（空）表示不限制
        if (_playerWhitelist is { Count: > 0 } &&
            !_playerWhitelist.Contains(name, StringComparer.Ordinal))
        {
            await KickAsync(connection, "Not in whitelist", ct).ConfigureAwait(false);
            return;
        }

        // Slot 以服务端分配的 PlayerId 覆盖：客户端上行的包 4 槽位是其本地索引（通常 0），
        // 直接透传会导致其他客户端把该外观画到自己的槽位上。
        _playerAppearances[connection.PlayerId] =
            info with { Name = name, Slot = (byte)connection.PlayerId };
        Console.WriteLine($"[Net] 玩家 #{connection.PlayerId} 名称 \"{name}\"");
    }

    private static async Task KickAsync(Connection connection, string reason, CancellationToken ct)
    {
        await connection.SendEncodedAsync(
            PacketId.Disconnect, DisconnectPacket.WithReason(reason), ct).ConfigureAwait(false);

        connection.State = ConnectionState.Disconnected;
    }

    private async Task HandleConnectionRequest(
        ConnectionRequestPacket req,
        Connection connection,
        CancellationToken ct)
    {
        // 版本校验：客户端版本串须为 "Terraria<ProtocolId>"
        var expected = _protocol.Version.ConnectVersion;
        if (!string.Equals(req.Version, expected, StringComparison.Ordinal))
        {
            await connection.SendEncodedAsync(
                PacketId.Disconnect,
                DisconnectPacket.WithReason($"Protocol mismatch. Server: {expected}"),
                ct).ConfigureAwait(false);

            connection.State = ConnectionState.Disconnected;
            return;
        }

        // 校验通过 → 分配 PlayerId 并进入认证阶段
        await connection.SendEncodedAsync(
            PacketId.ContinueConnecting,
            new ContinueConnectingPacket((byte)connection.PlayerId),
            ct).ConfigureAwait(false);

        connection.State = ConnectionState.Authenticating;
        Console.WriteLine($"[Net] 握手成功 {connection.RemoteEndPoint} 玩家 #{connection.PlayerId} 版本 {req.Version}");
    }

    private Task SendWorldInfoAsync(Connection connection, CancellationToken ct)
    {
        // 包 7 使用真实世界元数据（尺寸 / 出生点 / 进度位），与包 10 区块网格保持一致
        return connection.SendEncodedAsync(
            PacketId.WorldInfo, _world.ToWorldInfoPacket(), ct).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
