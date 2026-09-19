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

namespace TerraAuth.Net.Transport;

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
    private readonly ConcurrentDictionary<int, SessionAppearance> _playerAppearances = new();

    /// <summary>进服时间（用于 PlayerLeftArgs.SessionDuration）。</summary>
    private readonly ConcurrentDictionary<int, SessionStart> _sessionStart = new();

    private sealed record SessionAppearance(long SessionId, PlayerInfoPacket Packet);
    private sealed record SessionStart(long SessionId, DateTimeOffset StartedAt);

    /// <summary>诊断：权威层拒绝计数器（PersistenceAuditLogger 只落库不打印，拒绝原因需在控制台可见）。</summary>
    private long _rejectCount;

    /// <summary>
    /// 未建模包统计：PacketId → 收到次数。用于「真机客户端实际发了哪些包」的数据驱动决策
    /// （决定哪些包需要登记为中继 / 需要权威建模）。这些包被拒绝但**不计违规**。
    /// </summary>
    private readonly ConcurrentDictionary<PacketId, long> _unmodeledPackets = new();

    /// <summary>未建模包统计快照（按 PacketId）。</summary>
    public IReadOnlyDictionary<PacketId, long> UnmodeledPacketCounts => _unmodeledPackets;

    /// <summary>违规处置阈值（滑动窗口 + 阈值 → 踢出）。</summary>
    private readonly ViolationKickLimits _violationKick;

    /// <summary>仿真固定步长（与 GameLoop 一致）：用于把「秒」换算成 tick。</summary>
    private const int TicksPerSecond = 60;

    /// <summary>会话恢复宽限期（tick）；0 = 断线即回收。</summary>
    private readonly long _sessionResumeGraceTicks;

    /// <summary>进程内违规窗口：PlayerId → 窗口起点 + 窗口内拒绝计数。</summary>
    private readonly ConcurrentDictionary<int, ViolationWindow> _violations = new();

    private Task? _acceptLoop;

    public ISnapshotSender SnapshotSender { get; }

    /// <summary>服务端命令子系统（游戏内 / 前缀命令分发）；null = 关闭。</summary>
    private readonly Authority.CommandService? _commandService;

    /// <summary>SSC 玩家档案仓储（背包 / 生命 / 法力落盘）；null = 不持久化。</summary>
    private readonly TerraAuth.Persistence.IPlayerRepository? _playerProfiles;

    /// <summary>实际监听端口（endpoint 端口传 0 时由 OS 分配）；<see cref="Start"/> 之后有效。</summary>
    public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// 查询玩家名（包 4 SyncPlayer 记录的外观）。未认证 / 未知返回 false。
    /// 供插件 API（IServerApi）读取玩家信息用，避免暴露内部外观缓存。
    /// </summary>
    public bool TryGetPlayerName(int playerId, out string name)
    {
        if (_playerAppearances.TryGetValue(playerId, out var appearance)
            && !string.IsNullOrEmpty(appearance.Packet.Name))
        {
            name = appearance.Packet.Name;
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
        ViolationKickLimits? violationKick = null,
        int sessionResumeGraceSeconds = 0,
        Authority.CommandService? commandService = null,
        TerraAuth.Persistence.IPlayerRepository? playerProfiles = null)
    {
        _sessionResumeGraceTicks = Math.Max(0, sessionResumeGraceSeconds) * TicksPerSecond;
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
        _commandService = commandService;
        _playerProfiles = playerProfiles;

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

        // 真机测试收尾：把「客户端实际发了哪些未建模包」打出来（决定中继 / 建模优先级）
        LogUnmodeledPacketSummary();
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
        try
        {
            await connection.RunAsync(OnPacketAsync, ct).ConfigureAwait(false);
        }
        finally
        {
            // 先释放槽位再走退出清理：槽位按「最小可用 ID」复用（与原版一致），
            // 会话恢复依赖「旧槽位已释放」后重连才能拿到同一 ID；顺序颠倒会让重连拿到新 ID。
            await _connections.RemoveAsync(connection.PlayerId, connection).ConfigureAwait(false);
            OnConnectionClosed(connection);
        }
    }

    /// <summary>连接结束：清理外观缓存并触发 PlayerLeft Hook。</summary>
    private void OnConnectionClosed(Connection connection)
    {
        var hasAppearance = _playerAppearances.TryGetValue(connection.PlayerId, out var appearance)
            && appearance.SessionId == connection.SessionId;
        if (hasAppearance)
            _playerAppearances.TryRemove(
                new KeyValuePair<int, SessionAppearance>(connection.PlayerId, appearance!));

        if (_violations.TryGetValue(connection.PlayerId, out var violation)
            && violation.SessionId == connection.SessionId)
            _violations.TryRemove(connection.PlayerId, out _);

        var name = hasAppearance ? appearance!.Packet.Name ?? "" : "";

        // 通知其他玩家该玩家已离线（包 14 置为未激活），否则原版客户端会残留幽灵玩家
        if (connection.PlayerId > 0)
        {
            // SSC 档案落盘：必须在 MarkPlayerOffline 之前取运行时（之后它会被移出在线集合）。
            // 不落盘的话 SSC（服务端持有背包唯一真相）下每次重进背包都会归零。
            SavePlayerProfile(connection, name);

            // 权威侧：清掉该槽位上的按玩家状态（移动基线等）。否则槽位复用时，
            // 上一次会话的位置会被当作基准，使重连玩家的首个位置包被判超速。
            _pipeline.ResetPlayer(connection.PlayerId, connection.SessionId);

            // 世界侧：移出在线集合并按宽限期保留会话（宽限期 0 即直接回收；同时修掉运行时不释放的泄漏）
            _world.MarkPlayerOffline(connection.PlayerId, connection.SessionId, name, _sessionResumeGraceTicks);
            _ = BroadcastLeaveAsync(connection);
        }

        if (_hooks is null) return;

        var duration = _sessionStart.TryGetValue(connection.PlayerId, out var start)
            && start.SessionId == connection.SessionId
            && _sessionStart.TryRemove(
                new KeyValuePair<int, SessionStart>(connection.PlayerId, start))
            ? DateTimeOffset.UtcNow - start.StartedAt
            : TimeSpan.Zero;

        _hooks.Trigger(new PlayerLeftArgs
        {
            PlayerId = connection.PlayerId,
            PlayerName = name,
            Reason = "Disconnected",
            SessionDuration = duration,
        });
    }

    /// <summary>
    /// SSC 玩家档案落盘（背包 + 生命 / 法力），按玩家名作为档案身份。
    /// 同步等待写入完成：断线清理紧随其后，异步写会让「立刻重进」读到旧档案。
    /// </summary>
    private void SavePlayerProfile(Connection connection, string name)
    {
        if (_playerProfiles is null || string.IsNullOrEmpty(name)) return;

        PlayerRuntime? runtime;
        lock (_world.PlayersLock)
            _world.Players.TryGetValue(connection.PlayerId, out runtime);
        if (runtime is null || runtime.SessionId != connection.SessionId) return;

        try
        {
            _playerProfiles.SaveAsync(new TerraAuth.Persistence.PlayerData(
                TerraAuth.Security.PlayerIdentity.FromName(name),
                name,
                PlayerProfileCodec.Encode(runtime),
                runtime.HpMax,
                runtime.MpMax)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SSC] 玩家档案写入失败（{name}）：{ex.Message}");
        }
    }

    /// <summary>广播玩家离线（包 14 Active=false）给其余玩家。</summary>
    private async Task BroadcastLeaveAsync(Connection expected)
    {
        try
        {
            var current = _connections.Get(expected.PlayerId);
            if (current is not null && !ReferenceEquals(current, expected))
                return;

            await _connections.BroadcastExceptAsync(
                expected.PlayerId,
                PacketId.PlayerActive,
                new PlayerActivePacket((byte)expected.PlayerId, Active: false),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NetworkHost] 广播玩家 #{expected.PlayerId} 离线失败: {ex.Message}");
        }
    }

    // ---------- 入站包处理：管线 → 仿真 ----------

    private async Task OnPacketAsync(INetworkPacket packet, Connection connection, CancellationToken ct)
    {
        // 状态流转：握手 / 认证
        if (!await HandleConnectionStateAsync(packet, connection, ct).ConfigureAwait(false))
            return;

        // [DIAG] 入站关键包诊断（真机排障用，默认关闭）：确认客户端丢弃 / 拾取实际发的包与内容
        if (DiagnosticLog.Enabled) LogInboundPacket(packet, connection);

        // 走 Phase 2 权威管线
        var result = await _pipeline.ProcessAsync(
            packet,
            connection.PlayerId,
            _commands,
            ct,
            connection.SessionId).ConfigureAwait(false);

        // 未建模包统计（真机测试数据来源）：这些包会被拒绝，但不计入违规窗口。
        if (packet is UnknownPacket unmodeled)
            RecordUnmodeledPacket(unmodeled.Type);

        switch (result.Decision)
        {
            case AuthorityDecision.Accept:
                // Command 已由管线写入 _commands（Phase 3 仿真消费）
                if (packet is NetTextPacket { IsClientMessage: true } chat)
                {
                    // 客户端聊天（命令名 + 文本）→ 转服务端下行形态广播给**所有人**（含发送者，与原版一致）
                    if (_commandService is not null && chat.Text.StartsWith("/", StringComparison.Ordinal))
                    {
                        // 游戏内命令：/give /who 等 → CommandService 分发，结果仅回发给发起者
                        var cmdResult = _commandService.Execute(connection.PlayerId, chat.Text[1..]);
                        await SendChatAsync(connection.PlayerId, cmdResult.Output, cmdResult.Success ? "Yellow" : "Red", ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await BroadcastChatAsync(PlayerChatLine(connection.PlayerId, chat.Text), ct: ct)
                            .ConfigureAwait(false);
                    }
                }
                else if (packet is ChestPacket chestOpen)
                {
                    // 打开箱子（包 31）权威通过 → 把服务端持有的箱子内容逐槽下发（包 34 + 32）。
                    await SendChestContentsAsync(connection, chestOpen, ct).ConfigureAwait(false);
                }
                // 其他已接受的状态包只进入命令队列；仿真提交后由 GameHost
                // 从服务端最终状态生成原版同步包，不存在客户端原包即时中继路径。
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
                    Console.WriteLine($"[Authority] 拒绝 #{rejectNo} 玩家 #{connection.PlayerId}: {result.Reason}"
                        + (result.Detail is { Length: > 0 } d ? $"（{d}）" : ""));

                // 客户端图格缓存与服务端不一致时，拒绝修改但立即补发权威单格，
                // 让客户端停止重试旧 TileType；该类同步差异不计入违规。
                if (packet is TileBreakPacket tileBreak
                    && result.Reason == "tile_type_mismatch")
                {
                    await connection.SendEncodedAsync(
                        PacketId.TileSquare,
                        new TileSquarePacket(_world, tileBreak.X, tileBreak.Y, 1, 1),
                        ct).ConfigureAwait(false);
                }

                // 处置：窗口内拒绝累计达阈值 → 踢出（先发包 2 说明原因，再关闭连接）
                // 仅「计入违规」的拒绝参与累计；未建模包等客户端行为噪声只统计不惩罚。
                if (result.CountsAsViolation
                    && RecordViolation(connection.PlayerId, connection.SessionId))
                {
                    if (_violations.TryGetValue(connection.PlayerId, out var violation)
                        && violation.SessionId == connection.SessionId)
                        _violations.TryRemove(connection.PlayerId, out _);
                    Console.WriteLine(
                        $"[Authority] 玩家 #{connection.PlayerId} 违规累计达 " +
                        $"{_violationKick.MaxViolations}/{_violationKick.WindowSeconds}s，踢出：{result.Reason}");
                    await _connections.KickAsync(
                        connection.PlayerId,
                        $"Too many violations: {result.Reason}",
                        CancellationToken.None,
                        connection).ConfigureAwait(false);
                }
                break;
        }
    }

    /// <summary>
    /// 入站关键包明细（真机排障用，由 <see cref="DiagnosticLog.Enabled"/> 守卫）：
    /// 确认客户端「丢弃 / 拾取 / 背包写入」实际发的是哪种包、内容是什么，以及未建模包的包号分布。
    /// </summary>
    private static void LogInboundPacket(INetworkPacket packet, Connection connection)
    {
        switch (packet)
        {
            case ItemDropPacket drop:
                Console.WriteLine($"[DIAG] 21 ItemDrop pid={connection.PlayerId} slot={drop.ItemSlotIndex} id={drop.ItemId} stack={drop.Stack} pos=({drop.Position.X:0},{drop.Position.Y:0}) vel=({drop.Velocity.X:0},{drop.Velocity.Y:0})");
                break;
            case ItemDestroyPacket des:
                Console.WriteLine($"[DIAG] 151 ItemDestroy pid={connection.PlayerId} slot={des.ItemSlotIndex}");
                break;
            case ItemPickupPacket pick:
                Console.WriteLine($"[DIAG] 22 ItemPickup pid={connection.PlayerId} slot={pick.ItemSlotIndex} owner={pick.PlayerId}");
                break;
            case InventorySlotPacket slot:
                Console.WriteLine($"[DIAG] 5 InventorySlot pid={connection.PlayerId} slot={slot.Slot} id={slot.ItemId} stack={slot.Stack}");
                break;
            case ProjectileNewPacket projectile:
                Console.WriteLine($"[DIAG] 27 ProjectileNew pid={connection.PlayerId} key={projectile.ProjectileKey} type={projectile.ProjectileType} pos=({projectile.Position.X:0.0},{projectile.Position.Y:0.0}) vel=({projectile.Velocity.X:0.0},{projectile.Velocity.Y:0.0}) dmg={projectile.Damage} ai=({projectile.Ai0:0.0},{projectile.Ai1:0.0},{projectile.Ai2:0.0})");
                break;
            case NpcStrikePacket strike:
                Console.WriteLine($"[DIAG] 28 NpcStrike pid={connection.PlayerId} idx={strike.NpcId} gen={strike.Generation} dmg={strike.Damage} crit={(strike.Crit ? 1 : 0)}");
                break;
            case UnknownPacket unk:
                Console.WriteLine($"[DIAG] UNKNOWN pid={connection.PlayerId} type={unk.Type}");
                break;
        }
    }

    /// <summary>
    /// 记录一次权威拒绝，返回 true 表示该玩家在窗口内已达阈值（调用方应立即处置）。
    /// 窗口滚动：超出窗口则重置起点与计数；阈值触发后由调用方移除条目。
    /// </summary>
    private bool RecordViolation(int playerId, long sessionId)
    {
        var now = DateTime.UtcNow;
        var window = _violations.GetOrAdd(playerId, _ => new ViolationWindow
        {
            SessionId = sessionId,
            StartUtc = now,
        });

        if (window.SessionId != sessionId)
        {
            _violations[playerId] = window = new ViolationWindow
            {
                SessionId = sessionId,
                StartUtc = now,
            };
        }

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
        public long SessionId;
        public DateTime StartUtc;
        public int Count;
    }

    /// <summary>
    /// 记录一个未建模包（PacketId → 次数）。首次出现必打印（用于发现「客户端到底发了什么」），
    /// 之后每 100 次打印一次（用于观察量级）；停机时再打印完整汇总。
    /// </summary>
    private void RecordUnmodeledPacket(PacketId type)
    {
        var count = _unmodeledPackets.AddOrUpdate(type, 1, static (_, c) => c + 1);
        if (count == 1)
            Console.WriteLine($"[Unmodeled] 首次收到未建模包 {DescribePacketId(type)}：已拒绝（不计违规，仅统计）");
        else if (count % 100 == 0)
            Console.WriteLine($"[Unmodeled] {DescribePacketId(type)} 累计 {count} 次");
    }

    /// <summary>停机汇总未建模包统计（按次数倒序），供真机测试后决定「中继 / 建模」优先级。</summary>
    private void LogUnmodeledPacketSummary()
    {
        if (_unmodeledPackets.IsEmpty) return;

        Console.WriteLine($"[Unmodeled] 未建模包汇总（{_unmodeledPackets.Count} 种，按次数倒序）：");
        foreach (var (type, count) in _unmodeledPackets.OrderByDescending(static kv => kv.Value))
            Console.WriteLine($"[Unmodeled]   {DescribePacketId(type)} × {count}");
    }

    /// <summary>包号显示：已登记常量显示「名字(号)」，未登记（枚举未定义）只显示号。</summary>
    private static string DescribePacketId(PacketId type)
        => Enum.IsDefined(type) ? $"{type}({(byte)type})" : $"#{(byte)type}";

    /// <summary>
    /// 打开箱子（包 31）权威通过后，把服务端持有的箱子内容逐槽下发：
    /// 先发包 34 告知玩家当前箱子索引，再对每个槽位发包 32（空槽 stack=0）。
    /// </summary>
    private async Task SendChestContentsAsync(Connection connection, ChestPacket request, CancellationToken ct)
    {
        int index;
        ChestItem[] items;
        lock (_world.ChestsLock)
        {
            var chest = _world.FindChestAt(request.X, request.Y);
            if (chest is null) return;

            index = chest.Index;
            items = (ChestItem[])chest.Items.Clone();
        }

        await connection.SendEncodedAsync(
            PacketId.SyncPlayerChestIndex,
            new PlayerChestIndexPacket((byte)connection.PlayerId, (short)index),
            ct).ConfigureAwait(false);

        for (int slot = 0; slot < items.Length; slot++)
        {
            var item = items[slot];
            await connection.SendEncodedAsync(
                PacketId.SyncChestItem,
                new SyncChestItemPacket(index, slot, item.Stack, item.Prefix, item.Type),
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>向所有 Playing 连接广播一个包（供世界状态同步 / 聊天使用）。</summary>
    public Task BroadcastAsync(PacketId type, INetworkPacket packet, CancellationToken ct = default)
        => _connections.BroadcastAsync(type, packet, ct);

    /// <summary>
    /// 只发给满足条件的玩家（用于按视口裁剪的世界同步，避免把全世界 NPC / 掉落物推给所有人）。
    /// </summary>
    public async Task BroadcastWhereAsync(
        PacketId type, INetworkPacket packet, Func<int, bool> shouldSend, CancellationToken ct = default)
    {
        foreach (var conn in _connections.All())
        {
            if (conn.State != ConnectionState.Playing || !shouldSend(conn.PlayerId)) continue;
            await conn.SendEncodedAsync(type, packet, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 广播箱子槽位更新；会话在筛选后再次确认，避免关箱 / 断线后仍发送已失效会话的更新。
    /// </summary>
    public async Task BroadcastChestUpdateAsync(
        INetworkPacket packet,
        Func<Connection, bool> shouldSend,
        CancellationToken ct = default)
    {
        foreach (var conn in _connections.All())
        {
            if (conn.State != ConnectionState.Playing || !shouldSend(conn))
                continue;

            await conn.SendEncodedAsync(
                PacketId.SyncChestItem,
                packet,
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>向单个 Playing 玩家发送一个包（玩家不存在 / 未进入 Playing 时忽略）。</summary>
    public async Task SendToPlayerAsync(int playerId, PacketId type, INetworkPacket packet, CancellationToken ct = default)
    {
        var conn = _connections.Get(playerId);
        if (conn is null || conn.State != ConnectionState.Playing) return;

        await conn.SendEncodedAsync(type, packet, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 按玩家定制内容的广播：<paramref name="packetFactory"/> 返回 null 表示跳过该玩家。
    /// 用于「每个玩家需要收到不同子集」的下发（如按视口裁剪的液体变更）。
    /// </summary>
    public async Task BroadcastPerPlayerAsync(
        Func<int, INetworkPacket?> packetFactory, CancellationToken ct = default)
    {
        foreach (var conn in _connections.All())
        {
            if (conn.State != ConnectionState.Playing) continue;

            var packet = packetFactory(conn.PlayerId);
            if (packet is null) continue;

            await conn.SendEncodedAsync(packet.Type, packet, ct).ConfigureAwait(false);
        }
    }

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

            case SpawnTileDataPacket spawn when connection.State == ConnectionState.Playing:
                // 游戏内请求周边区块（原版客户端边走边请求）→ 补发该点周边**尚未下发过**的区块。
                // 仅在登录时发一次会让玩家离开出生点后看不到地形。
                await StreamSectionsAroundAsync(spawn.SpawnX, spawn.SpawnY, connection, ct).ConfigureAwait(false);
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
    /// 区块矩形与顺序对应原版客户端包 8 的读取顺序：
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
            await SendSectionOnceAsync(connection, sx, sy, ct).ConfigureAwait(false);

        await connection.SendEncodedAsync(
            PacketId.InitialSpawn,
            new InitialSpawnPacket(),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 流送半径（区块数）：与原版一致 —— 以玩家所在区块为中心取 (2×fluff+1)² 的方块（fluff=1 → 3×3）。
    /// </summary>
    private const int StreamSectionFluff = 1;

    /// <summary>
    /// 按玩家当前所在区块流送周边区块：**仅当该玩家跨越区块边界时**触发，且跳过已下发过的区块。
    /// 原版会在玩家移动时持续补发附近区块；若只在登录时发一次，离开出生点后客户端地形为空。
    /// 由快照循环按 20Hz 调用（未跨区块时只做一次坐标比较，开销可忽略）。
    /// </summary>
    public async Task StreamSectionsForPlayersAsync(CancellationToken ct = default)
    {
        foreach (var connection in _connections.All())
        {
            if (connection.State != ConnectionState.Playing) continue;
            if (!TryGetPlayerPosition(connection.PlayerId, out var position)) continue;

            var current = (TileMap.GetSectionX((int)(position.X / 16f)),
                           TileMap.GetSectionY((int)(position.Y / 16f)));
            if (connection.LastStreamSection == current) continue;

            connection.LastStreamSection = current;
            await StreamSectionsAroundSectionAsync(current.Item1, current.Item2, connection, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>按图格坐标流送周边区块（包 8 请求路径）。</summary>
    private async Task StreamSectionsAroundAsync(int tileX, int tileY, Connection connection, CancellationToken ct)
    {
        // 请求坐标无效（-1 / 越界）时退回服务端记录的位置
        if (tileX <= 0 || tileY <= 0 || tileX >= _world.MaxTilesX || tileY >= _world.MaxTilesY)
        {
            if (!TryGetPlayerPosition(connection.PlayerId, out var position)) return;
            tileX = (int)(position.X / 16f);
            tileY = (int)(position.Y / 16f);
        }

        await StreamSectionsAroundSectionAsync(
            TileMap.GetSectionX(tileX), TileMap.GetSectionY(tileY), connection, ct).ConfigureAwait(false);
    }

    private async Task StreamSectionsAroundSectionAsync(
        int sectionX, int sectionY, Connection connection, CancellationToken ct)
    {
        int maxSectionsX = _world.MaxTilesX / 200;
        int maxSectionsY = _world.MaxTilesY / 150;

        // 先收集「尚未下发」的区块（原版会先发包 9 告知进度，再逐块下发；重复请求不重复编码）
        var pending = new List<(int X, int Y)>();
        for (int sx = sectionX - StreamSectionFluff; sx <= sectionX + StreamSectionFluff; sx++)
            for (int sy = sectionY - StreamSectionFluff; sy <= sectionY + StreamSectionFluff; sy++)
            {
                if (sx < 0 || sy < 0 || sx >= maxSectionsX || sy >= maxSectionsY) continue;
                if (connection.SyncedSections.Contains((sx, sy))) continue;
                pending.Add((sx, sy));
            }

        if (pending.Count == 0) return;

        await connection.SendEncodedAsync(
            PacketId.StatusText,
            new StatusTextPacket(StatusMax: pending.Count, StatusText: "Receiving tile data"),
            ct).ConfigureAwait(false);

        foreach (var (sx, sy) in pending)
            await SendSectionOnceAsync(connection, sx, sy, ct).ConfigureAwait(false);
    }

    /// <summary>下发一个区块（已下发过的跳过；编码超帧上限时自动拆分）。</summary>
    private async Task SendSectionOnceAsync(Connection connection, int sx, int sy, CancellationToken ct)
    {
        if (!connection.SyncedSections.Add((sx, sy))) return; // 已发过 → 不重复编码

        int xStart = sx * 200;
        int yStart = sy * 150;
        int width = Math.Min(200, _world.MaxTilesX - xStart);
        int height = Math.Min(150, _world.MaxTilesY - yStart);
        if (width <= 0 || height <= 0) return;

        await SendTileSectionAsync(connection, xStart, yStart, width, height, ct).ConfigureAwait(false);
    }

    /// <summary>取服务端权威的玩家位置（无运行时 / 未在线返回 false）。</summary>
    private bool TryGetPlayerPosition(int playerId, out Vector2 position)
    {
        position = default;
        lock (_world.PlayersLock)
        {
            if (!_world.Players.TryGetValue(playerId, out var player)) return false;
            position = player.Position;
            return true;
        }
    }

    /// <summary>
    /// 发送一个图格区块；若编码后超过帧上限（UInt16 65535），沿较长轴二分拆分后分别发送（递归）。
    /// 原版对「压缩不划算的区块」有降级路径；此处用拆分替代，避免高熵区块
    /// 直接抛 <see cref="InvalidOperationException"/> 中断登录 / 出生点下载。
    /// </summary>
    private async Task SendTileSectionAsync(
        Connection connection, int xStart, int yStart, int width, int height, CancellationToken ct)
    {
        try
        {
            await connection.SendEncodedAsync(
                PacketId.TileSendSection,
                new TileSectionPacket(_world, xStart, yStart, width, height),
                ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (width > 1 || height > 1)
        {
            // 帧过大且仍可拆分 → 优先切分较长轴
            if (width >= height)
            {
                int half = width / 2;
                await SendTileSectionAsync(connection, xStart, yStart, half, height, ct).ConfigureAwait(false);
                await SendTileSectionAsync(connection, xStart + half, yStart, width - half, height, ct).ConfigureAwait(false);
            }
            else
            {
                int half = height / 2;
                await SendTileSectionAsync(connection, xStart, yStart, width, half, ct).ConfigureAwait(false);
                await SendTileSectionAsync(connection, xStart, yStart + half, width, height - half, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>包 12：客户端完成出生 → 进入 Playing，回包 129（FinishedConnecting），并主动下发玩家状态。</summary>
    private async Task HandlePlayerSpawnAsync(Connection connection, CancellationToken ct)
    {
        connection.State = ConnectionState.Playing;
        Console.WriteLine(
            $"[Net] 玩家 #{connection.PlayerId} 进入世界（出生点 {_world.SpawnTileX},{_world.SpawnTileY}）");

        TriggerPlayerJoined(connection);

        // SSC 全量背包同步：仅 SSC 模式下服务端持有背包唯一真相（WorldInfo SSC 标志已下发），
        // 进世界即把 59 个槽位逐一用包 5 下发，客户端据此初始化背包显示
        // （原版 SyncOnePlayer 在 State==10 时同样下发全部背包槽；否则 SSC 下客户端背包保持未初始化）。
        // 关闭 SSC 时跳过：客户端以本地档案背包为准（原版非 SSC 流程）。
        if (_world.SscEnabled && _world.Players.TryGetValue(connection.PlayerId, out var runtime))
        {
            for (int i = 0; i < PlayerRuntime.InventorySlotCount; i++)
            {
                var itemId = runtime.Items[i];
                var stack = runtime.ItemStacks[i];
                await connection.SendEncodedAsync(PacketId.InventorySlot,
                    new InventorySlotPacket(i, itemId, stack)
                    {
                        PlayerId = connection.PlayerId,
                        Prefix = runtime.ItemPrefixes[i],
                    }, ct).ConfigureAwait(false);
            }
            Console.WriteLine(
                $"[Give] 进世界全量下发 #{connection.PlayerId} 背包 {PlayerRuntime.InventorySlotCount} 槽");
        }

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

        _playerAppearances.TryGetValue(connection.PlayerId, out var appearance);
        var name = appearance?.Packet.Name ?? "";
        _sessionStart[connection.PlayerId] = new SessionStart(
            connection.SessionId,
            DateTimeOffset.UtcNow);

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
                PacketId.PlayerInfo, kvp.Value.Packet, ct).ConfigureAwait(false);

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
                    PacketId.PlayerInfo, selfInfo.Packet, ct).ConfigureAwait(false);
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

        // 会话恢复：同身份（玩家名）在宽限期内重连 → 接管原运行时（位置 / 血量 / 增益一并交还），
        // 而不是当作新玩家从出生点重新开始。超出宽限期 / 未开启则走常规新玩家流程。
        bool resumed = _world.TryResumePlayer(
            connection.PlayerId,
            connection.SessionId,
            name);

        if (!resumed)
        {
            var created = new PlayerRuntime
            {
                Id = connection.PlayerId,
                SessionId = connection.SessionId,
                Active = true,
                Position = new Vector2(
                    _world.SpawnTileX * 16f,
                    _world.SpawnTileY * 16f),
                AimPosition = new Vector2(
                    _world.SpawnTileX * 16f,
                    _world.SpawnTileY * 16f),
            };
            lock (_world.PlayersLock)
                _world.Players[connection.PlayerId] = created;

            // SSC 档案回读：新会话（非接管）按**玩家名**取回背包 / 生命 / 法力。
            // 锁外做 I/O；读失败按出生默认值继续（不阻断进服）。
            if (_playerProfiles is not null)
            {
                try
                {
                    var profile = await _playerProfiles
                        .GetAsync(TerraAuth.Security.PlayerIdentity.FromName(name)).ConfigureAwait(false);
                    if (profile is not null && PlayerProfileCodec.TryApply(profile.InventoryBlob, created))
                        Console.WriteLine($"[SSC] 已恢复玩家 \"{name}\"（#{connection.PlayerId}）的背包与状态");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SSC] 玩家档案读取失败（按新玩家继续）：{ex.Message}");
                }
            }
        }

        // Slot 以服务端分配的 PlayerId 覆盖：客户端上行的包 4 槽位是其本地索引（通常 0），
        // 直接透传会导致其他客户端把该外观画到自己的槽位上。
        _playerAppearances[connection.PlayerId] = new SessionAppearance(
            connection.SessionId,
            info with { Name = name, Slot = (byte)connection.PlayerId });
        Console.WriteLine(resumed
            ? $"[Net] 玩家 #{connection.PlayerId} 名称 \"{name}\"（会话已恢复）"
            : $"[Net] 玩家 #{connection.PlayerId} 名称 \"{name}\"");
    }

    private static async Task KickAsync(Connection connection, string reason, CancellationToken ct)
    {
        try
        {
            // 先等待包 2 写入 socket，再切换状态，避免认证阶段踢出丢失原因。
            await connection.SendEncodedAndFlushedAsync(
                PacketId.Disconnect,
                DisconnectPacket.WithReason(reason),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 连接已取消时直接进入关闭流程。
        }

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
