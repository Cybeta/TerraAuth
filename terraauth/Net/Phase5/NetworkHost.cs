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
/// 违规处置阈值：滑动窗口内权威拒绝次数达 <see cref="MaxViolations"/> → 踢出该连接。
/// 由组合根从 ServerConfig 映射注入，Net 层不反向依赖 Config 层（架构 §4.5 阈值唯一来源）。
/// </summary>
public readonly record struct ViolationKickLimits(int MaxViolations, int WindowSeconds)
{
    /// <summary>兜底默认值（与 ServerConfig 默认一致：10 次 / 60 分钟）。</summary>
    public static ViolationKickLimits Default => new(MaxViolations: 10, WindowSeconds: 60 * 60);
}

/// <summary>
/// 服务端网络主机。
/// 职责：
///   1. TcpListener 接受新连接
///   2. 为每个连接创建 Connection + 运行读写循环
///   3. 入站包 → IInboundPipeline（Phase 2）→ CommandQueue（Phase 3）
///   4. 出站快照 → ISnapshotSender（Phase 4）
///   5. 权威拒绝累计达阈值 → 踢出连接（处置闭环）
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

    /// <summary>违规处置阈值（滑动窗口 + 阈值 → 踢出）。</summary>
    private readonly ViolationKickLimits _violationKick;

    /// <summary>进程内违规窗口：PlayerId → 窗口起点 + 窗口内拒绝计数。</summary>
    private readonly ConcurrentDictionary<int, ViolationWindow> _violations = new();

    private Task? _acceptLoop;

    public ISnapshotSender SnapshotSender { get; }

    /// <summary>实际监听端口（endpoint 端口传 0 时由 OS 分配）；<see cref="Start"/> 之后有效。</summary>
    public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// 查询玩家名（包 4 SyncPlayer 记录的外观）。未认证 / 未知返回 false。
    /// 供插件 API（IServerApi）读取玩家信息用，避免暴露内部外观缓存。
    /// </summary>
    public bool TryGetPlayerName(int playerId, out string name)
    {
        if (_playerAppearances.TryGetValue(playerId, out var info) && !string.IsNullOrEmpty(info.Name))
        {
            name = info.Name;
            return true;
        }

        name = "";
        return false;
    }

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
        IHookRegistry? hooks = null,
        ViolationKickLimits? violationKick = null)
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
        _violationKick = violationKick ?? ViolationKickLimits.Default;

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
        _violations.TryRemove(connection.PlayerId, out _); // 断开即清违规窗口，避免 ID 复用串号
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
                if (packet is NetTextPacket { IsClientMessage: true } chat)
                {
                    // 客户端聊天（命令名 + 文本）→ 转服务端下行形态广播给**所有人**（含发送者，与原版一致）
                    await BroadcastChatAsync(PlayerChatLine(connection.PlayerId, chat.Text), ct: ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    // 权威通过 → 转发给其他玩家：原版客户端依赖这些原版包渲染他人状态
                    // （TerraAuth 专用快照包 15 会被原版客户端忽略，故玩家间可见性必须靠原版包）
                    await RelayToOthersAsync(packet, connection, ct).ConfigureAwait(false);
                }
                break;

            case AuthorityDecision.Correct:
                // 服务端权威纠正（如 HP/MP snap back）→ 下发纠正包
                // 包号取自纠正包自身（INetworkPacket.Type），避免新增纠正类型时下发错误包号
                if (result.CorrectionPacket is not null)
                    await connection.SendEncodedAsync(
                        result.CorrectionPacket.Type,
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

                // 处置：窗口内拒绝累计达阈值 → 踢出（先发包 2 说明原因，再关闭连接）
                if (RecordViolation(connection.PlayerId))
                {
                    _violations.TryRemove(connection.PlayerId, out _);
                    Console.WriteLine(
                        $"[Authority] 玩家 #{connection.PlayerId} 违规累计达 " +
                        $"{_violationKick.MaxViolations}/{_violationKick.WindowSeconds}s，踢出：{result.Reason}");
                    await _connections.KickAsync(
                        connection.PlayerId,
                        $"Too many violations: {result.Reason}",
                        ct).ConfigureAwait(false);
                }
                break;
        }
    }

    /// <summary>
    /// 记录一次权威拒绝，返回 true 表示该玩家在窗口内已达阈值（调用方应立即处置）。
    /// 窗口滚动：超出窗口则重置起点与计数；阈值触发后由调用方移除条目。
    /// </summary>
    private bool RecordViolation(int playerId)
    {
        var now = DateTime.UtcNow;
        var window = _violations.GetOrAdd(playerId, _ => new ViolationWindow { StartUtc = now });

        lock (window.Gate)
        {
            if ((now - window.StartUtc).TotalSeconds > _violationKick.WindowSeconds)
            {
                window.StartUtc = now;
                window.Count = 0;
            }

            window.Count++;
            return window.Count >= _violationKick.MaxViolations;
        }
    }

    /// <summary>进程内违规窗口状态（每玩家一条，锁内更新）。</summary>
    private sealed class ViolationWindow
    {
        /// <summary>每窗口独占的轻量锁（.NET 9+ Lock，替代 Monitor 对象锁）。</summary>
        public readonly Lock Gate = new();
        public DateTime StartUtc;
        public int Count;
    }

    /// <summary>
    /// 权威通过后把该包转发给其他 Playing 连接（原版语义：服务端中继客户端状态变更）。
    /// <para>
    /// 身份覆盖：凡携带玩家字段的包，一律以服务端分配的 PlayerId 覆盖客户端上报值，
    /// 防止伪造他人身份驱动其移动 / 受伤 / 增益。
    /// </para>
    /// <para>
    /// 原样转发：包 21（掉落物槽位）/ 27（抛射物索引）中的索引为全局量，原版即原样中继；
    /// 包 32（箱子内物品）无玩家字段。
    /// </para>
    /// </summary>
    private async Task RelayToOthersAsync(INetworkPacket packet, Connection sender, CancellationToken ct)
    {
        switch (packet)
        {
            case PlayerControlsPacket controls:      // 13 移动 / 控制（含速度等可选字段）
                await _connections.BroadcastExceptAsync(sender.PlayerId, PacketId.PlayerPosition,
                    controls with { PlayerId = (byte)sender.PlayerId }, ct).ConfigureAwait(false);
                break;

            case TileBreakPacket brk:                // 17 挖砖
                await _connections.BroadcastExceptAsync(
                    sender.PlayerId, PacketId.TileBreak, brk, ct).ConfigureAwait(false);
                break;

            case TilePlacePacket place:              // 79 放砖
                await _connections.BroadcastExceptAsync(
                    sender.PlayerId, PacketId.TilePlace, place, ct).ConfigureAwait(false);
                break;

            case ProjectileNewPacket proj:           // 27 抛射物
                await _connections.BroadcastExceptAsync(
                    sender.PlayerId, PacketId.ProjectileNew, proj, ct).ConfigureAwait(false);
                break;

            case ItemDropPacket drop:                // 21 世界掉落物
                await _connections.BroadcastExceptAsync(
                    sender.PlayerId, PacketId.ItemDrop, drop, ct).ConfigureAwait(false);
                break;

            case PlayerHurtV2Packet hurt:            // 117 受伤（他人可见受击表现）
                await _connections.BroadcastExceptAsync(sender.PlayerId, PacketId.PlayerHurtV2,
                    hurt with { PlayerId = sender.PlayerId }, ct).ConfigureAwait(false);
                break;

            case PlayerDeathV2Packet death:          // 118 死亡
                await _connections.BroadcastExceptAsync(sender.PlayerId, PacketId.PlayerDeathV2,
                    death with { PlayerId = sender.PlayerId }, ct).ConfigureAwait(false);
                break;

            case PlayerHealPacket heal:              // 35 治疗
                await _connections.BroadcastExceptAsync(sender.PlayerId, PacketId.PlayerHeal,
                    heal with { PlayerId = sender.PlayerId }, ct).ConfigureAwait(false);
                break;

            case SyncPlayerZonePacket zone:          // 36 生物群系 / 城镇状态
                await _connections.BroadcastExceptAsync(sender.PlayerId, PacketId.SyncPlayerZone,
                    zone with { PlayerId = (byte)sender.PlayerId }, ct).ConfigureAwait(false);
                break;

            case PlayerBuffsPacket buffs:            // 50 增益 / 减益列表
                await _connections.BroadcastExceptAsync(sender.PlayerId, PacketId.PlayerBuffs,
                    buffs with { PlayerId = sender.PlayerId }, ct).ConfigureAwait(false);
                break;

            case SyncChestItemPacket chestItem:      // 32 箱子内物品（无玩家字段）
                await _connections.BroadcastExceptAsync(
                    sender.PlayerId, PacketId.SyncChestItem, chestItem, ct).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>向所有 Playing 连接广播一个包（供世界状态同步 / 聊天使用）。</summary>
    public Task BroadcastAsync(PacketId type, INetworkPacket packet, CancellationToken ct = default)
        => _connections.BroadcastAsync(type, packet, ct);

    /// <summary>
    /// 广播一条聊天（包 82 / NetTextModule 下行形态：作者 = 服务端）。
    /// 原版客户端不会按 authorId 反查名字，故玩家发言需自行带上「名字: 文本」前缀。
    /// </summary>
    public Task BroadcastChatAsync(string text, string color = "White", CancellationToken ct = default)
        => BroadcastAsync(PacketId.NetModule,
            new NetTextPacket(text) { AuthorId = byte.MaxValue, Color = ParseColor(color) }, ct);

    /// <summary>向单个玩家发送聊天（供插件 API <c>IServerApi.SendMessage</c>）。</summary>
    public async Task SendChatAsync(int playerId, string text, string color = "White", CancellationToken ct = default)
    {
        var conn = _connections.Get(playerId);
        if (conn is null || conn.State != ConnectionState.Playing) return;

        await conn.SendEncodedAsync(PacketId.NetModule,
            new NetTextPacket(text) { AuthorId = byte.MaxValue, Color = ParseColor(color) }, ct).ConfigureAwait(false);
    }

    /// <summary>拼「名字: 文本」聊天行（服务端下行不带作者解析，需自行带名）。</summary>
    private string PlayerChatLine(int playerId, string text)
        => TryGetPlayerName(playerId, out var name) && name.Length > 0 ? $"{name}: {text}" : text;

    /// <summary>颜色名 → RGB（未知名回退白色）。</summary>
    private static RgbColor ParseColor(string? color) => color?.Trim().ToLowerInvariant() switch
    {
        "red" => new RgbColor(255, 0, 0),
        "green" => new RgbColor(0, 255, 0),
        "blue" => new RgbColor(0, 0, 255),
        "yellow" => new RgbColor(255, 255, 0),
        _ => new RgbColor(255, 255, 255),
    };

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
