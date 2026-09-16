// Phase 6 - 组装根：把 Phase 2/3/4/5 与基础设施（配置/持久化/监控/封禁）串联
// 对应架构文档 §4.5 基础设施层

using System.Net;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Channels;
using TerraAuth.Config;
using TerraAuth.Persistence;
using TerraAuth.Monitoring;
using TerraAuth.Security;
using TerraAuth.Authority;
using TerraAuth.Simulation;
using TerraAuth.Net;
using TerraAuth.Net.Snapshots;
using TerraAuth.Net.Transport;
using TerraAuth.Plugins;
using TerraAuth.ModCompat;
using TerraAuth.Concurrency;
using TerraAuth.Protocol; // Vector2 / PacketId

namespace TerraAuth;

public sealed class GameHost : IDisposable
{
    // ---- 基础设施（Phase 6）----
    public IConfigurationService Config { get; }
    public IPlayerRepository Players { get; }
    public IAuditRepository Audit { get; }
    public IMetrics Metrics { get; }
    public IBanManager Bans { get; }

    /// <summary>世界改动仓储（图格 / 箱子增量落盘；启动时回放）。</summary>
    public IWorldRepository? WorldRepo { get; }

    // ---- 核心管线（Phase 2/3/4/5）----
    public IInboundPipeline Pipeline { get; }
    public WorldSimulator Simulator { get; }
    public SnapshotBroadcaster Broadcaster { get; }
    public NetworkHost Network { get; }

    // ---- 扩展层（插件 / Mod 兼容 / 并行）----
    /// <summary>Hook 注册表：供插件注册回调，也可在运行时由运维查询。</summary>
    public IHookRegistry Hooks { get; }
    /// <summary>插件加载器：支持运行时热重载。</summary>
    public PluginLoader Plugins { get; }
    /// <summary>Mod 兼容层：客户端能力检测 + 策略校验。</summary>
    public IModDetector ModDetector { get; }
    /// <summary>自定义包处理器：Mod 自定义网络包 ID 区间管理。</summary>
    public ICustomPacketHandler CustomPackets { get; }
    /// <summary>TModLoader 兼容层：Mod 列表解析 + 自定义包转发。</summary>
    public TModLoaderCompat TModLoader { get; }
    /// <summary>服务端命令子系统：内置命令 + 插件注册的命令。</summary>
    public CommandService Commands { get; }
    /// <summary>并行配置：线程池大小、Chunk 大小等。</summary>
    public ParallelConfig Parallel { get; }
    /// <summary>Worker 池：网络 I/O 与解码并行。</summary>
    public WorkerPool Workers { get; }

    private readonly List<IDisposable> _disposables = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<(int Index, byte Generation), byte>> _npcBaselines = new();
    private readonly MetricsHttpServer? _metricsServer;
    private readonly IAsyncDisposable? _pipelineDisposable;
    private readonly WorldState _world; // 供热重载同步全局开关（SSC 等）

    /// <summary>权威子系统聚合：配置热重载时用于推送新阈值。</summary>
    private readonly AuthorityEnforcers _enforcers;

    // ★ 已填充：注入真实实现（含扩展层）
    public GameHost(
        IConfigurationService config,
        IPlayerRepository players,
        IAuditRepository audit,
        IMetrics metrics,
        IBanManager bans,
        NetworkHost network,
        IInboundPipeline pipeline,
        WorldSimulator simulator,
        SnapshotBroadcaster broadcaster,
        MetricsHttpServer? metricsServer,
        IHookRegistry hooks,
        PluginLoader pluginLoader,
        IModDetector modDetector,
        ICustomPacketHandler customPackets,
        TModLoaderCompat tmodLoader,
        ParallelConfig parallel,
        WorkerPool workers,
        AuthorityEnforcers enforcers,
        CommandService commands,
        IAsyncDisposable? pipelineDisposable = null,
        IWorldRepository? worldRepo = null)
    {
        Config = config;
        Players = players;
        Audit = audit;
        Metrics = metrics;
        Bans = bans;
        WorldRepo = worldRepo;
        Network = network;
        Pipeline = pipeline;
        Simulator = simulator;
        Broadcaster = broadcaster;
        _metricsServer = metricsServer;

        // 扩展层
        Hooks = hooks;
        Plugins = pluginLoader;
        ModDetector = modDetector;
        CustomPackets = customPackets;
        TModLoader = tmodLoader;
        Commands = commands;
        Parallel = parallel;
        Workers = workers;
        _enforcers = enforcers;
        _world = simulator.State; // 供热重载同步全局开关（SSC 等）
        _pipelineDisposable = pipelineDisposable;

        // 配置热更新 → 动态调整阈值（如 MaxFlightSpeed / MaxSingleDamage）
        config.OnChanged += OnConfigurationChanged;
    }

    /// <summary>工厂方法：一键组装完整服务端（推荐入口）。</summary>
    public static GameHost Bootstrap(
        string dbPath = "terraauth.db",
        string configPath = "server.json",
        int metricsPort = 9090,
        int port = 7777)
    {
        // 1. 配置（JSON + 热重载）
        var config = new ConfigurationService();
        if (File.Exists(configPath)) config.Load(configPath);
        else config.Load(CreateDefaultConfig(configPath)); // 首次生成默认配置

        // 热路径诊断开关（逐包 / 逐次受伤等明细）：默认关闭，由配置驱动，见 DiagnosticLog.cs
        DiagnosticLog.Enabled = config.Current.VerboseDiagnostics;

        // 2. 持久化（SQLite；无 NuGet 时走内嵌 LiteDb）
        var db = new SqlitePersistence(dbPath);

        // 3. 监控（Prometheus 风格 + /metrics HTTP 端点）；由 ServerConfig.MetricsEnabled 控制开关
        var metrics = new PrometheusMetrics();
        var metricsServer = config.Current.MetricsEnabled
            ? new MetricsHttpServer(metrics, config.Current.MetricsPort)
            : null;

        // 4. 封禁（SQLite 持久化 + 进程内兜底）
        var banStore = new SqliteBanStore(db);
        var bans = new BanManager(banStore, config, db);

        // 5. 权威层（Phase 2）+ 审计桥接
        // 世界数据：优先加载配置指定的 .wld；未配置则按 WorldSize + WorldSeed 程序化生成
        // 换图（改种子 / 换 .wld）时必须先清掉旧地图的改动，否则按旧坐标记录的图格 / 箱子会落到新地形上
        if (config.Current.ResetWorldChangesOnStart)
        {
            db.ClearWorldChangesAsync().GetAwaiter().GetResult();
            Console.WriteLine(
                "[World] 已按 ResetWorldChangesOnStart 清空持久化的世界改动（图格 + 箱子）——" +
                "请把 server.json 的该项改回 false，否则每次重启都会丢弃玩家改动");
        }

        var world = LoadBaseWorld(config.Current.WorldPath, config.Current.WorldSize, config.Current.WorldSeed);
        world.GameMode = (int)config.Current.GameMode; // 阶段 D：玩家受击公式按难度取分支（经典/专家/大师），117 上界与接触兜底共用
        world.SscEnabled = config.Current.SscEnabled;   // 全局 SSC 开关：开=服务器背包权威，关=原版客户端本地背包
        world.DestroySummonsOnWeaponRemoval =
            config.Current.DestroySummonsOnWeaponRemoval; // 移除召唤武器即销毁对应召唤弹幕（可热重载）

        // 世界改动回放：基准世界是确定性的（程序化生成 / .wld 解析），只需叠加上次运行落盘的增量，
        // 否则玩家挖 / 放 / 箱内物品在服务端重启后会全部丢失。
        ApplyPersistedWorldChanges(world, db);
        var commands = new CommandQueue(maxCount: CommandQueueCapacity);
        var recorder = new EventRecorder();
        var snapshots = new SnapshotStore();
        var auditLogger = new PersistenceAuditLogger(db, metrics);

        // 权威阈值统一取自 ServerConfig（架构 §4.5 唯一来源）：限流 + 六个子系统全部显式映射，
        // 杜绝"改了 server.json 却不生效"——先前仅 MovementLimits 接入，其余走代码默认值，
        // 而默认值恰与 ServerConfig 默认值相同（除挖砖上限），问题被完全掩盖。
        // 移动限速用飞行上限覆盖步行/冲刺，降低误判。
        var thresholds = AuthorityThresholds.From(config.Current);
        var enforcers = new AuthorityEnforcers(thresholds.Rate, auditLogger, world,
            thresholds.Movement, thresholds.Player, thresholds.Combat,
            thresholds.Inventory, thresholds.World);
        world.InventoryLedger = enforcers.Inventory as IInventoryLedger;

        // 管线阶段顺序（越早拒绝成本越低）
        // 管线阶段按“先解析、再校验、后执行业务”的顺序组装，确保非法帧尽早拒绝
        var pipeline = new InboundPipeline(new IPipelineStage[]
        {
            new FrameStage(),
            new ConnectionStateStage(),
            new RateLimitStage(enforcers.Rate),
            new InventoryAuthorityStage(enforcers.Inventory, auditLogger),
            new MovementAuthorityStage(enforcers.Movement, auditLogger),
            new CombatAuthorityStage(enforcers.Combat, auditLogger),
            new PlayerAuthorityStage(enforcers.Player, auditLogger),
            new WorldAuthorityStage(enforcers.World, auditLogger),
            new TerminalStage(),
        }, currentTick: () => world.Tick);

        // ---- 扩展层初始化（插件 / Mod 兼容 / 并行）----
        // 1. 并行基础设施（需求：多 CPU 线程优化）
        var parallelConfig = new ParallelConfig();
        var workers = new WorkerPool(parallelConfig.WorkerThreads);

        // 2. Hook 注册表 + 插件上下文（需求 1：预留 hook）
        var logger = new CoreLogger();
        var hooks = new HookRegistry(logger);

        // 3. 当前生产版本仅支持 Vanilla；Mod 兼容层保留为后续独立立项，不注册 250-255，
        // 不开放 TModLoader 握手、自定义包或未知包转发。
        var modPolicy = config.Current.ModPolicy;
        var modDetector = new ModDetector(modPolicy, logger);
        var customPackets = new CustomPacketHandler(logger);

        // 4. 权威管线分片装饰（P2：按玩家分片并行，同玩家保序）+ 叠加 Hook 触发
        var shardedPipeline = new ShardedInboundPipeline(pipeline, parallelConfig.ShardCount);
        // 玩家名解析用延迟绑定：HookedPipeline 必须先于 NetworkHost 构造（NetworkHost 依赖它），
        // 故以闭包捕获局部变量；实际调用发生在运行期，届时 networkForNames 已赋值。
        NetworkHost? networkForNames = null;
        var hookedPipeline = new HookedPipeline(shardedPipeline, hooks, logger,
            playerNameResolver: id =>
                networkForNames is not null && networkForNames.TryGetPlayerName(id, out var n) ? n : null);

        // 5. 仿真层（Phase 3）
        var simulator = new WorldSimulator(world, commands, recorder, snapshots, config);

        // 6. 网络层（Phase 5）——先建，以便把 SnapshotSender 注入快照广播
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager(
            config.Current.MaxConnections,
            TimeSpan.FromSeconds(config.Current.HandshakeTimeoutSeconds));
        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(protocol.Version);
        var commandService = new CommandService();
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Any, port),
            decoder, encoder, protocol, connections, hookedPipeline, commands, workers, world,
            config.Current.PlayerWhitelist, hooks,
            new ViolationKickLimits(
                config.Current.MaxViolationsBeforeBan,
                config.Current.ViolationWindowMinutes * 60),
            sessionResumeGraceSeconds: config.Current.SessionResumeGraceSeconds,
            commandService: commandService,
            playerProfiles: db);   // SSC 玩家档案（背包 / 生命 / 法力）跨重进持久化
        networkForNames = network; // HookedPipeline 的玩家名解析延迟绑定到此

        // 6.5 保留未来兼容层对象，但不进入当前 Vanilla-only 生产能力。
        var tmodLoader = new TModLoaderCompat(
            modDetector, logger, customPackets,
            forward: null,
            enabled: false);

        // 7. 插件上下文 + 加载器
        // 必须在网络层之后：ServerApi 需要连接管理（踢出/在线查询）与封禁管理器才能真实生效
        // 命令子系统：内置 say / who / kick / help / give / boss；插件可经 IServerApi.ExecuteCommand 调用
        var serverApi = new ServerApi(auditLogger, metrics, network, connections, bans, world, commandService);
        RegisterBuiltinCommands(commandService, serverApi, world, network, simulator);

        var pluginContext = new PluginContext(
            hooks: hooks,
            logger: logger,
            configuration: new CoreConfiguration(config),
            metrics: new CoreMetrics(metrics),
            server: serverApi,
            eventStore: new CoreEventStore(db));
        var pluginLoader = new PluginLoader(
            pluginDirectory: System.IO.Path.Combine(System.AppContext.BaseDirectory, "plugins"),
            logger: logger,
            context: pluginContext);

        // 8. 快照广播（Phase 4）——ServerConfig → SnapshotConfig 的映射属组合根职责
        var snapshotConfig = new SnapshotConfig
        {
            TickHz = config.Current.SnapshotRateHz,
            ViewportRadius = config.Current.ViewportRadius,
            ShadowPredictionMaxDeviation = config.Current.ShadowPredictionMaxDeviation,
        };
        var broadcaster = new SnapshotBroadcaster(
            world, commands, snapshotConfig, network.SnapshotSender, encoder,
            maxParallelism: parallelConfig.BackgroundThreads,
            store: snapshots,      // 与仿真层共享同一 store：仿真每 tick 写入即成为可下发帧
            views: simulator);     // 广播线程只读仿真发布的实体视图，不触活动 WorldState

        var host = new GameHost(
            config, db, db, metrics, bans, network, hookedPipeline, simulator, broadcaster, metricsServer,
            hooks, pluginLoader, modDetector, customPackets, tmodLoader, parallelConfig, workers, enforcers,
            commandService, shardedPipeline, worldRepo: db);

        // 订阅审计：权威层 Reject → Metrics + Ban 累计（架构 §4.5 数据流）
        auditLogger.OnViolation += (playerId, reason) =>
        {
            metrics.IncrementBlockedCheat(reason);
            _ = bans.ReportViolationAsync(playerId, reason);
        };

        return host;
    }

    /// <summary>启动完整服务端：网络监听 + 仿真循环 + 快照广播 + 指标端点。</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // 启动指标端点（Phase 6 验收 6.6）
        _metricsServer?.Start();

        // 加载并启动插件（扩展层：Hook 在管线中已生效）
        await Plugins.LoadAllAsync().ConfigureAwait(false);
        await Plugins.StartAllAsync().ConfigureAwait(false);

        // 服务器生命周期 Hook：插件可在此做启动后初始化（如预热缓存、订阅外部事件）
        Hooks.Trigger(new ServerStartedArgs());

        // 1. 启动仿真循环（Phase 3，固定 timestep）
        var gameLoop = new GameLoop(Simulator, Simulator.State);
        var simTask = gameLoop.RunAsync(ct);

        // 2. 启动网络宿主（Phase 5）；监听持续到 ct 取消后再优雅停机
        Network.Start();
        var netTask = StopNetworkOnShutdownAsync(ct);

        // 3. 启动快照广播刷新循环（Phase 4，独立网络线程）
        var snapTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Broadcaster.FlushAsync(ct);
                // 液体变化随快照频率（20Hz）下发：1Hz 的液体流动观感过差（按视口裁剪）
                await FlushLiquidAsync(ct).ConfigureAwait(false);
                // 服务端驱动的图格变更（电路翻转执行器等）同样按快照频率推送
                await FlushTileUpdatesAsync(ct).ConfigureAwait(false);
                // 客户端状态变更仅在仿真 Apply 成功后，按服务端最终状态生成包 13
                await FlushPlayerUpdatesAsync(ct).ConfigureAwait(false);
                // 服务端权威修改的增益列表（如移除召唤 Buff）→ 包 50 回写本人
                await FlushPlayerBuffsAsync(ct).ConfigureAwait(false);
                // 服务端权威修改的 NPC 增益列表 → 包 54 向全体玩家回写
                await FlushNpcBuffsAsync(ct).ConfigureAwait(false);
                // 箱子改动只在仿真提交后同步给当前打开该箱子的玩家
                await FlushChestUpdatesAsync(ct).ConfigureAwait(false);
                // 服务端判定的玩家受击（接触 / 下落伤害）→ 包 117 + 包 16
                await FlushPlayerHurtAsync(ct).ConfigureAwait(false);
                // NPC 命中确认 → 包 162（客户端据此出队一条待确认伤害；漏发会导致幽灵碰撞）
                await FlushNpcDamageAcksAsync(ct).ConfigureAwait(false);
                // 服务端主动生成的掉落物（Boss 掉落等）→ 包 21
                await FlushNewItemsAsync(ct).ConfigureAwait(false);
                // 玩家提示（拾取物品等）→ 包 82 单独发给该玩家
                await FlushPlayerNoticesAsync(ct).ConfigureAwait(false);
                // 周期刷新掉落物归属（原版 FindOwner 循环）→ 包 22（归属变更才发）
                await FlushItemOwnersAsync(ct).ConfigureAwait(false);
                // 新增弹幕（客户端上报 / Boss AI 发射）→ 包 27
                await FlushNewProjectilesAsync(ct).ConfigureAwait(false);
                // 按玩家位置流送其周边图格区块（仅跨区块时补发，跳过已发过的区块）
                await Network.StreamSectionsForPlayersAsync(ct).ConfigureAwait(false);
                await Task.Delay(1000 / Math.Max(1, Config.Current.SnapshotRateHz), ct);
            }
        }, ct);

        // 3.5 NPC 同步（包 23）：**挂到仿真 tick** 上（轮询 State.Tick，每前进 1 tick 处理一次），
        //     频率 = 仿真频率（GameLoop 高精度定时后 ≈60Hz），不再有独立 Task.Delay 循环的频率漂移
        //     （此前用 Task.Delay(1000/60) 驱动，Windows 时钟粒度下两套循环互相漂移，
        //      实测同一史莱姆两次包 23 间隔 4~6 tick，近身 60Hz 从未成立）。
        //     逐 NPC 分档仍保留（见 BroadcastNpcUpdatesAsync）：
        //     Boss / 近身（3 格内）NPC 逐 tick 发，其余每 3 次调用（20Hz）发一次。
        //     客户端收到包 23 后会按原版自行推进 NPC，同步越稀疏、双方位置差越大；
        //     而**接触判定用的是服务端位置**，差值一大就会出现「看着离史莱姆很远却在掉血」，
        //     故接触相关的 NPC 必须保持高频对齐，远处的按 20Hz 即可。
        //     原版用令牌桶把普通 NPC 压到 ≈2 包/秒（Boss ≈12Hz），这里取 20Hz 折中：约省 3 倍带宽。
        //     仍是「变化才发 + 心跳补发」，静止 NPC 不占额外带宽。
        var npcSyncTask = Task.Run(async () =>
        {
            long lastTick = -1;
            while (!ct.IsCancellationRequested)
            {
                var tick = Simulator.State.Tick;
                if (tick == lastTick)
                {
                    await Task.Delay(1, ct).ConfigureAwait(false);   // 等下一 tick（timeBeginPeriod 后 1ms 精度）
                    continue;
                }

                lastTick = tick;
                // 逐 NPC 分档：非高优先级（远处）NPC 只在每 N 次调用下发一次
                var fullRate = tick % NpcSyncRateDivisor == 0;
                await BroadcastNpcUpdatesAsync(ct, fullRate).ConfigureAwait(false);
            }
        }, ct);

        // 4. 世界状态同步循环：时间（包 18）与进度（包 7）定期下发
        //    1Hz 足够：客户端自身按 tick 推进时间，这里只做周期性对账
        var worldSyncTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await BroadcastWorldStateAsync(ct).ConfigureAwait(false);
                // 世界改动按 1Hz 落盘：够快（崩溃最多丢 1 秒改动）且写放大可控
                await FlushWorldChangesAsync(ct).ConfigureAwait(false);
                // 回收超过宽限期的离线会话（会话恢复的时间边界）
                Simulator.State.ReapOfflineSessions();
                // 空服时的全量 .wld 导出（配置了 WorldExportPath 才生效；内部自带间隔与在线判定）
                TryExportWorld();
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }, ct);

        await Task.WhenAll(simTask, netTask, snapTask, npcSyncTask, worldSyncTask).ConfigureAwait(false);

        // 停机：把待落盘的世界改动冲刷干净（单批上限决定每轮吞吐，故循环到排空；
        // 极端情况下（曾降级为全图扫描）最多多跑一遍全图，轮数有上限，不会挂死）
        try
        {
            var world = Simulator.State;
            int maxRounds = world.MaxTilesX * world.MaxTilesY / WorldState.PersistBatchSize + 4;
            int rounds = 0;
            while (world.HasPendingPersist && rounds++ < maxRounds)
                await FlushWorldChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) { Console.WriteLine($"[World] 停机落盘失败：{ex.Message}"); }

        // 停机导出：此时增量已全部落盘，导出的 .wld 反映最终状态（可被再次作为基准世界加载）
        TryExportWorld(force: true);
    }

    /// <summary>
    /// 载入基准世界：配置了 <see cref="ServerConfig.WorldPath"/> 且文件存在 → 解析该 `.wld`；
    /// 否则按 <see cref="ServerConfig.WorldSize"/> + <see cref="ServerConfig.WorldSeed"/> 程序化生成（小 / 中 / 大三档）。
    /// </summary>
    private static WorldState LoadBaseWorld(string worldPath, WorldSize size, int seed)
    {
        if (!string.IsNullOrWhiteSpace(worldPath) && File.Exists(worldPath))
        {
            var loaded = WorldFileReader.Read(worldPath);
            Console.WriteLine(
                $"[World] 已加载世界文件 {worldPath}：{loaded.WorldName} " +
                $"{loaded.MaxTilesX}×{loaded.MaxTilesY}，出生点 ({loaded.SpawnTileX},{loaded.SpawnTileY})");
            return loaded;
        }

        if (!string.IsNullOrWhiteSpace(worldPath))
            Console.WriteLine($"[World] 世界文件不存在，回退为程序化生成：{worldPath}");

        var generated = WorldGenerator.Generate(size, seed: seed);
        Console.WriteLine(
            $"[World] 程序化生成（{size}，种子 {seed}）{generated.WorldName} {generated.MaxTilesX}×{generated.MaxTilesY}，" +
            $"出生点 ({generated.SpawnTileX},{generated.SpawnTileY})，区块 {generated.MaxTilesX / 200}×{generated.MaxTilesY / 150}");
        return generated;
    }

    /// <summary>
    /// 启动时回放世界改动：基准世界（程序化生成 / .wld 解析）是确定性的，
    /// 因此只需把上次运行落盘的图格 / 箱子内容增量叠加回去即可复原玩家建筑与箱子。
    /// </summary>
    private static void ApplyPersistedWorldChanges(WorldState world, IWorldRepository repo)
    {
        IReadOnlyList<WorldTileRecord> tiles;
        try
        {
            tiles = repo.LoadTileChangesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[World] 世界改动回放失败（按基准世界启动）：{ex.Message}");
            return;
        }

        int applied = 0;
        foreach (var record in tiles)
        {
            if (record.X < 0 || record.X >= world.MaxTilesX ||
                record.Y < 0 || record.Y >= world.MaxTilesY) continue;

            world.Tiles[record.X, record.Y] = Tile.Deserialize(record.Data);
            applied++;
        }

        if (applied > 0)
            Console.WriteLine($"[World] 已回放上次运行的世界改动：图格 {applied} 格");

        ApplyPersistedChests(world, repo);
    }

    /// <summary>
    /// 回放落盘的箱子内容：按索引定位并校验坐标（基准世界被替换时坐标不符则跳过，避免错位套用）。
    /// </summary>
    private static void ApplyPersistedChests(WorldState world, IWorldRepository repo)
    {
        IReadOnlyList<WorldChestRecord> chests;
        try
        {
            chests = repo.LoadChestChangesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[World] 箱子内容回放失败（保留基准世界内容）：{ex.Message}");
            return;
        }

        int applied = 0;
        lock (world.ChestsLock)
        {
            foreach (var record in chests)
            {
                var chest = world.FindChestByIndex(record.Index);
                if (chest is null || chest.X != record.X || chest.Y != record.Y) continue;

                chest.Items = Chest.DeserializeItems(record.Data);
                applied++;
            }
        }

        if (applied > 0)
            Console.WriteLine($"[World] 已回放上次运行的箱子内容：{applied} 个");
    }

    /// <summary>
    /// 把服务端的图格改动落盘。由 1Hz 世界同步循环与停机时调用。
    /// 落盘失败会把本批改动**重新排队**，避免「取出即丢」造成建筑丢失。
    /// </summary>
    public async Task FlushWorldChangesAsync(CancellationToken ct = default)
    {
        if (WorldRepo is null) return;

        var world = Simulator.State;

        var cells = world.DrainPersistTiles(WorldState.PersistBatchSize);
        if (cells.Count > 0)
        {
            var records = new List<WorldTileRecord>(cells.Count);
            foreach (var (x, y) in cells)
            {
                Tile tile;
                using (world.Sections.EnterRead(x, y, x, y))
                    tile = world.Tiles[x, y];
                records.Add(new WorldTileRecord(x, y, Tile.Serialize(in tile)));
            }

            try
            {
                await WorldRepo.SaveTileChangesAsync(records).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                foreach (var (x, y) in cells) world.MarkPersistTile(x, y);
                Simulator.Recorder.Record(new GameEvent(world.Tick, null,
                    GameEventKinds.PersistFailed, cells.Count, GameEventCategory.Persistence));
                Console.WriteLine($"[World] 图格落盘失败（已重新排队 {cells.Count} 格）：{ex.Message}");
            }
        }

        await FlushChestChangesAsync(world).ConfigureAwait(false);
    }

    /// <summary>把变更过的箱子内容落盘；失败重新排队（同图格语义）。</summary>
    private async Task FlushChestChangesAsync(WorldState world)
    {
        if (WorldRepo is null) return;

        var indices = world.DrainPersistChests(WorldState.PersistBatchSize);
        if (indices.Count == 0) return;

        var records = new List<WorldChestRecord>(indices.Count);
        lock (world.ChestsLock)
        {
            foreach (var index in indices)
            {
                var chest = world.FindChestByIndex(index);
                if (chest is null) continue;
                records.Add(new WorldChestRecord(index, chest.X, chest.Y, Chest.SerializeItems(chest.Items)));
            }
        }

        try
        {
            await WorldRepo.SaveChestChangesAsync(records).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            foreach (var index in indices) world.MarkPersistChest(index);
            Simulator.Recorder.Record(new GameEvent(world.Tick, null,
                GameEventKinds.PersistFailed, indices.Count, GameEventCategory.Persistence));
            Console.WriteLine($"[World] 箱子内容落盘失败（已重新排队 {indices.Count} 个）：{ex.Message}");
        }
    }

    private DateTimeOffset _lastWorldExport = DateTimeOffset.MinValue;

    /// <summary>
    /// 把当前世界导出为 `.wld`（取 <see cref="ServerConfig.WorldExportPath"/>）。
    /// <paramref name="force"/> = true（停机）无条件导出；否则仅当「无人在线且距上次导出超过配置间隔」时导出 ——
    /// 全量序列化是 O(世界大小) 的，故有意避开在线时段（在线可靠性由增量日志负责）。
    /// 导出走「临时文件 → 读回校验 → 原子替换」，不会用读不回来的文件覆盖已有世界。
    /// </summary>
    public bool TryExportWorld(bool force = false)
    {
        var path = Config.Current.WorldExportPath;
        if (string.IsNullOrWhiteSpace(path)) return false;

        var world = Simulator.State;

        if (!force)
        {
            if ((DateTimeOffset.UtcNow - _lastWorldExport).TotalSeconds
                < Math.Max(30, Config.Current.WorldExportIntervalSeconds)) return false;

            bool online;
            lock (world.PlayersLock) online = world.Players.Values.Any(p => p.Active);
            if (online) return false;
        }

        try
        {
            WorldFileWriter.Write(path, world);
            _lastWorldExport = DateTimeOffset.UtcNow;
            Console.WriteLine($"[World] 已导出世界文件：{path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[World] 世界导出失败（保留原文件）：{ex.Message}");
            return false;
        }
    }

    /// <summary>单批液体同步的最大条目数。</summary>
    private const int MaxLiquidChangesPerBatch = 512;

    /// <summary>服务端命令队列容量上限：入站管线写入的 Command 超限即拒绝该操作，防恶意超大未来 tick 堆积。</summary>
    private const int CommandQueueCapacity = 8192;

    /// <summary>图格边长（像素）。</summary>
    private const float TileSizePx = 16f;

    /// <summary>
    /// 批量下发服务端仿真的液体变化（包 82 模块 0），**按视口裁剪**：
    /// 每个玩家只收到其视野半径内的变更；无变更的玩家不下发。由快照循环按快照频率调用。
    /// </summary>
    public async Task FlushLiquidAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var cells = world.DrainLiquidSync(MaxLiquidChangesPerBatch);
        if (cells.Count == 0) return;

        // 先采样一次当前液体状态（随后按玩家裁剪，避免每个玩家重复读图格）
        var changes = new List<LiquidChange>(cells.Count);
        foreach (var (x, y) in cells)
        {
            Tile tile;
            using (world.Sections.EnterRead(x, y, x, y))
                tile = world.Tiles[x, y];
            changes.Add(new LiquidChange(x, y, tile.Liquid, tile.LiquidType));
        }

        var radius = Math.Max(1, Config.Current.ViewportRadius);
        var radiusSq = (float)radius * radius;

        await Network.BroadcastPerPlayerAsync(playerId =>
        {
            var subset = new List<LiquidChange>();
            foreach (var change in changes)
            {
                float px = (change.X + 0.5f) * TileSizePx;
                float py = (change.Y + 0.5f) * TileSizePx;
                if (IsPlayerWithin(world, playerId, px, py, radiusSq)) subset.Add(change);
            }
            return subset.Count == 0 ? null : new LiquidModulePacket(subset);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>单批玩家状态通知上限。</summary>
    private const int MaxPlayerUpdatesPerFlush = 256;

    /// <summary>
    /// 下发仿真已提交的玩家最终状态。客户端上报的位置不直接中继，避免在 Apply 前看到未提交状态。
    /// </summary>
    public async Task FlushPlayerUpdatesAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var playerIds = world.DrainPlayerUpdates(MaxPlayerUpdatesPerFlush);
        foreach (var playerId in playerIds)
        {
            PlayerRuntime? player;
            lock (world.PlayersLock)
                world.Players.TryGetValue(playerId, out player);
            if (player is null || !player.Active) continue;

            // 位置广播只发给**其他**玩家（原版 SendData(PlayerControls) 用 ignoreClient=whoAmI 排除本人）：
            // 客户端本地物理是权威的，把服务端持有的位置回显给本人会覆盖其本地物理状态
            // （起跳被拉回地面、移动被旧位置覆盖 → 卡顿 + 跳跃几乎不可用）。
            await Network.BroadcastWhereAsync(PacketId.PlayerPosition,
                new PlayerControlsPacket((byte)playerId, player.Position, player.Velocity),
                pid => pid != playerId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>单批增益变更通知上限。</summary>
    private const int MaxPlayerBuffsPerFlush = 64;

    /// <summary>
    /// 下发服务端权威修改后的增益列表（包 50）：如「移除召唤武器即销毁」时移除召唤 Buff。
    /// 原版仆从由召唤 Buff 驱动存活，仅销毁服务端弹幕（包 29）不够——客户端 Buff 未移除时
    /// 仆从不消失、仍持续发射弹幕造成伤害（用户实测：必须手动点掉 Buff 提示召唤物才消失）。
    /// 回写包 50 后客户端 Buff 消失 → 仆从自毁，与用户手动点掉 Buff 的效果一致。
    /// 只发给本人（包 50 玩家增益为本人私有的权威状态，原版不对他人转发）。
    /// </summary>
    public async Task FlushPlayerBuffsAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var playerIds = world.DrainPlayerBuffsChanged(MaxPlayerBuffsPerFlush);
        foreach (var playerId in playerIds)
        {
            PlayerRuntime? player;
            lock (world.PlayersLock)
                world.Players.TryGetValue(playerId, out player);
            if (player is null || !player.Active) continue;

            try
            {
                await Network.SendToPlayerAsync(playerId, PacketId.PlayerBuffs,
                    new PlayerBuffsPacket(playerId, new List<int>(player.Buffs)), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                // 发送失败（连接已断开等）：该玩家已不在线，直接丢弃；重连后按全量状态重新下发。
            }
        }
    }

    /// <summary>单批 NPC 增益变更通知上限。</summary>
    private const int MaxNpcBuffsPerFlush = 64;

    /// <summary>
    /// 服务端权威修改某 NPC 增益列表（包 53 上报并入 / 服务端施加）后，向全体玩家下发包 54（NpcBuffSync）
    /// 回写该 NPC 的**全量**增益列表。原版 <c>NetMessage.SendData(54)</c> 广播给所有玩家；
    /// 增益状态相对图格/位置不敏感，故不做视口裁剪，统一广播。
    /// </summary>
    public async Task FlushNpcBuffsAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var npcIds = world.DrainNpcBuffsChanged(MaxNpcBuffsPerFlush);
        foreach (var npcId in npcIds)
        {
            NpcBuffEntry[] buffs;
            lock (world.NpcsLock)
            {
                if (npcId < 0 || npcId >= world.Npcs.Count || !world.Npcs[npcId].Active)
                    continue;
                var npc = world.Npcs[npcId];
                buffs = npc.Buffs
                    .Select(kv => new NpcBuffEntry(kv.Key, kv.Value))
                    .ToArray();
            }

            try
            {
                await Network.BroadcastWhereAsync(PacketId.NpcBuffSync,
                    new NpcBuffSyncPacket(npcId, buffs), _ => true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                // 广播失败（无连接/断开）：已不在线，直接丢弃；重连后按全量状态重新下发。
            }
        }
    }

    /// <summary>单批受击通知上限。</summary>
    private const int MaxHurtNotifiesPerFlush = 64;

    /// <summary>
    /// 下发服务端判定的玩家受击：包 117（受击表现，**转发给其他玩家**）+ 包 16（权威生命，单发给受击者）。
    /// 原版 <c>NetMessage.SendPlayerHurt</c> 用 <c>ignoreClient = whoAmI</c> 排除本人：原版客户端
    /// <c>Player.Hurt</c> 的 <c>quiet</c> 只挡发包、不挡扣血与伤害数字显示，把 117 回给本人会造成二次扣血显示（双结算）。
    /// 本人只收包 16 权威血量，血条以服务端为准。由快照循环按快照频率调用。
    /// </summary>
    public async Task FlushPlayerHurtAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var hurts = world.DrainPlayerHurt(MaxHurtNotifiesPerFlush);
        if (hurts.Count == 0) return;

        foreach (var (playerId, damage) in hurts)
        {
            await Network.BroadcastWhereAsync(PacketId.PlayerHurtV2,
                new PlayerHurtV2Packet(playerId, damage),
                pid => pid != playerId, ct).ConfigureAwait(false);

            PlayerRuntime? player;
            lock (world.PlayersLock)
                world.Players.TryGetValue(playerId, out player);

            if (player is not null)
            {
                if (DiagnosticLog.Enabled)
                    Console.WriteLine($"[HP] 玩家 #{playerId} 下发包16 hp={player.Hp}/{player.HpMax}");
                await Network.SendToPlayerAsync(playerId, PacketId.PlayerHealth,
                    new PlayerHealthPacket(playerId, player.Hp, player.HpMax), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 下发 NPC 命中确认（包 162，无 payload）：服务端每收到一次 NPC 命中（包 28）都要回一个，
    /// 客户端收到即 <c>NPC.AckDamage()</c> 把**最早入队**的待确认伤害出队。
    /// <para>**必须 FIFO 且不可漏发**：客户端在收到包 23 时会以
    /// <c>npc.life = 服务端血量 - 待确认伤害总和</c> 修正本地血量；漏发会让该总和持续累积，
    /// 本地血量被算成负数 → 客户端抹掉贴图（本地判死）而服务端该怪仍存活并继续造成接触伤害，
    /// 即「击杀后出现幽灵碰撞」。</para>
    /// </summary>
    public async Task FlushNpcDamageAcksAsync(CancellationToken ct = default)
    {
        var acks = Simulator.State.DrainNpcDamageAck(MaxNpcDamageAckPerFlush);
        foreach (var playerId in acks)
        {
            await Network.SendToPlayerAsync(playerId, PacketId.NpcDamageAck,
                new NpcDamageAckPacket(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>单轮下发的命中确认条数上限（FIFO，逐条对应客户端的一次入队）。</summary>
    private const int MaxNpcDamageAckPerFlush = 128;

    /// <summary>
    /// 下发服务端主动生成的掉落物（如 Boss 掉落）：包 21（含服务端分配的槽位 / 位置 / 速度 / 堆叠），按视口裁剪。
    /// 客户端上报生成的掉落物由客户端自行广播，不在此列（<c>NewNotified</c> 默认 true）。由快照循环调用。
    /// </summary>
    public async Task FlushNewItemsAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var recorder = Simulator.Recorder;

        WorldItemEntity[] fresh;
        lock (world.ItemsLock)
            fresh = world.Items.Where(i => i.Active && !i.NewNotified).ToArray();

        if (fresh.Length == 0) return;

        var radius = Math.Max(1, Config.Current.ViewportRadius);
        var radiusSq = (float)radius * radius;

        foreach (var item in fresh)
        {
            try
            {
                await Network.BroadcastWhereAsync(PacketId.ItemDrop,
                    new ItemDropPacket(item.ItemId, item.Stack)
                    {
                        ItemSlotIndex = item.Slot,
                        Position = item.Position,
                        Velocity = item.Velocity,
                        Prefix = item.Prefix,
                    },
                    // 丢弃的掉落物广播给所有玩家（含丢弃者本人）：客户端丢弃发包 21 时 index=400
                    // （NEW_ITEM_INDEX）即本地未持有，由服务器分配槽位并统一下发。
                    playerId => IsPlayerWithin(world, playerId, item.Position.X, item.Position.Y, radiusSq),
                    ct).ConfigureAwait(false);

                // 归属同步（包 22）：**所有**掉落物都需要，不只专属掉落物——
                // 原版 1.4.5.8 客户端 Player.GrabItems 只拾取 playerIndexTheItemIsReservedFor == 自己 的物品，
                // 无主（255）物品反而不可拾取。包 21 不含归属字段，客户端收到后本地归属为 255，
                // 因此丢弃物 / 专属物都必须广播包 22（原版 ApplySpawnOwnership → FindOwner → ReserveFor 流程）。
                // 专属物（OwnedBy ≥ 0）固定归属目标玩家；丢弃物按 FindOwner 就近分配（最近在线玩家）。
                await ReserveItemForAsync(world, item, ct).ConfigureAwait(false);

                item.NewNotified = true;   // 只有真正发出才记「已通知」，否则下次循环重试
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                recorder.Record(new GameEvent(world.Tick, null,
                    GameEventKinds.BroadcastFailed, "item_drop", GameEventCategory.Broadcast));
            }
        }
    }

    /// <summary>
    /// 下发玩家提示（拾取物品等）为聊天行（包 82，仅发给该玩家）。
    /// 单轮上限 <see cref="MaxPlayerNoticesPerFlush"/>：拾取可能每秒多次，避免一次刷屏。
    /// </summary>
    public async Task FlushPlayerNoticesAsync(CancellationToken ct = default)
    {
        var notices = Simulator.State.DrainPlayerNotices(MaxPlayerNoticesPerFlush);
        foreach (var (playerId, text) in notices)
        {
            try
            {
                await Network.SendChatAsync(playerId, text, "White", ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                // 提示属 best-effort：发送失败直接丢弃（不重排队，避免刷屏放大）
            }
        }
    }

    /// <summary>单轮下发的玩家提示上限。</summary>
    private const int MaxPlayerNoticesPerFlush = 8;

    /// <summary>无主掉落物归属搜索间隔（tick）：原版 Main.UpdateServer 对无主物品每 5 tick 重跑 FindOwner。</summary>
    private const int ItemOwnerUnreservedRefreshTicks = 5;

    /// <summary>已归属掉落物归属搜索间隔（tick）：原版对已归属物品每 300 tick（5 秒）重跑 FindOwner。</summary>
    private const int ItemOwnerReservedRefreshTicks = 300;

    /// <summary>
    /// 归属搜索半径（像素，平方比较）：与 <see cref="PickupItemCommand"/> 的拾取接受半径（160px）一致，
    /// 保证「服务端会接受拾取」时客户端必然已收到归属（≤5 tick 内），从而能发起拾取。
    /// </summary>
    private const float ItemOwnerSearchRangeSq = 160f * 160f;

    /// <summary>
    /// 服务端 FindOwner：为掉落物选定归属玩家并广播包 22。
    /// 专属物（OwnedBy ≥ 0）固定归属目标玩家；无主物就近分配（范围内最近在线玩家，无则 255）。
    /// 首次搜索（ReservedFor = -1）**必须**广播——即便目标为 255 也要把拾取延迟字段
    /// （grabDelayPlayer / grabDelayTime）带给客户端，否则丢弃后无延迟 → 原地立即重新拾取（与原版不符）。
    /// </summary>
    private async Task ReserveItemForAsync(WorldState world, WorldItemEntity item, CancellationToken ct)
    {
        int target = item.OwnedBy >= 0
            ? item.OwnedBy
            : FindOwnerTarget(world, item);

        // 首次搜索也必须广播（携带 grabDelay 字段）；其后仅在归属变更时广播。
        if (item.ReservedFor != -1 && target == item.ReservedFor)
            return;

        item.ReservedFor = target;
        item.OwnerSearchAge = 0;

        // 原版丢弃：grabDelayPlayer=丢弃者、grabDelayTime=100（DefaultGrabDelay），由包 22 带给客户端强制执行。
        // 延迟过期后 grabDelay 字段归零（255 / 0），否则新归属玩家也会被错误地套上延迟。
        var remainingDelay = item.RemainingGrabDelayTicks(world.Tick);
        var grabDelayPlayer = remainingDelay > 0 && item.DroppedBy >= 0 ? item.DroppedBy : 255;

        await Network.BroadcastAsync(PacketId.ItemPickup,
            new ItemOwnerPacket(target, item.Position)
            {
                ItemSlotIndex = item.Slot,
                TimeToKeepReservation = 15,
                GrabDelayPlayer = (byte)grabDelayPlayer,
                GrabDelayTime = remainingDelay,
            }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 周期刷新掉落物归属（原版 Main.UpdateServer 的 FindOwner 循环）：
    /// 无主物每 5 tick、已归属物每 300 tick 重跑就近分配，并把变更广播包 22。
    /// 覆盖「归属玩家离开 / 下线后物品永久不可拾取」与「更近玩家靠近后转移归属」两种场景。
    /// </summary>
    public async Task FlushItemOwnersAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;

        WorldItemEntity[] items;
        lock (world.ItemsLock)
            items = world.Items.Where(i => i.Active).ToArray();
        if (items.Length == 0) return;

        foreach (var item in items)
        {
            // 专属物（OwnedBy ≥ 0）不参与动态就近分配：归属由服务器权威固定。
            if (item.OwnedBy >= 0) continue;

            var interval = item.ReservedFor == 255
                ? ItemOwnerUnreservedRefreshTicks
                : ItemOwnerReservedRefreshTicks;
            if (++item.OwnerSearchAge < interval) continue;

            try
            {
                await ReserveItemForAsync(world, item, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                // 广播失败：ReservedFor 保持旧值，下次按间隔重试。
            }
        }
    }

    /// <summary>
    /// 就地选择最近在线（非死亡）玩家；超出拾取半径返回 255（无主）。
    /// 原版 FindOwner 在拾取延迟（grabDelayTime &gt; 0）期间**跳过丢弃者**（grabDelayPlayer），
    /// 使丢弃后延迟期内归属先让给其他玩家 / 保持无主，杜绝「丢→立刻捡回」。
    /// </summary>
    private static int FindOwnerTarget(WorldState world, WorldItemEntity item)
    {
        int best = 255;
        float bestDistSq = ItemOwnerSearchRangeSq;
        var skipDropper = item.RemainingGrabDelayTicks(world.Tick) > 0;
        lock (world.PlayersLock)
        {
            foreach (var kv in world.Players)
            {
                var p = kv.Value;
                if (!p.Active || p.Dead) continue;
                if (skipDropper && kv.Key == item.DroppedBy) continue;

                var dx = p.Position.X - item.Position.X;
                var dy = p.Position.Y - item.Position.Y;
                var distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = kv.Key;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// 瞬时发送失败：连接已断开 / 出站队列已关闭。此时**不得**把「已通知」标记置位，
    /// 否则该状态变更会永久丢失（客户端再也收不到最终结果）。
    /// </summary>
    private static bool IsTransientSendFailure(Exception ex)
        => ex is IOException or ObjectDisposedException or ChannelClosedException;

    private const int MaxChestUpdatesPerFlush = 256;

    public async Task FlushChestUpdatesAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var updates = world.DrainChestUpdates(MaxChestUpdatesPerFlush);

        for (int i = 0; i < updates.Count; i++)
        {
            var (chestIndex, slot) = updates[i];
            ChestItem item;
            lock (world.ChestsLock)
            {
                var chest = world.FindChestByIndex(chestIndex);
                if (chest is null || slot < 0 || slot >= chest.Items.Length)
                    continue;
                item = chest.Items[slot];
            }

            try
            {
                await Network.BroadcastChestUpdateAsync(
                    new SyncChestItemPacket(
                        chestIndex,
                        slot,
                        item.Stack,
                        item.Prefix,
                        item.Type),
                    conn => world.HasChestSession(
                        conn.PlayerId,
                        conn.SessionId,
                        chestIndex),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                for (int retry = i; retry < updates.Count; retry++)
                    world.MarkChestChanged(
                        updates[retry].ChestIndex,
                        updates[retry].Slot);
                throw;
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                // 发送失败 → 重新登记该槽位，由下一轮 flush 重试（否则客户端永远收不到最终内容）
                world.MarkChestChanged(chestIndex, slot);
                Simulator.Recorder.Record(new GameEvent(world.Tick, null,
                    GameEventKinds.BroadcastFailed, "chest_item", GameEventCategory.Broadcast));
            }
        }
    }

    /// <summary>单次推送的图格数上限。</summary>
    private const int MaxTileUpdatesPerFlush = 256;

    /// <summary>单次推送的矩形区块数上限（其余重新排队，下次 flush 继续）。</summary>
    private const int MaxTileUpdateRectsPerFlush = 64;

    /// <summary>包 20 的矩形宽/高为 Byte：单个矩形宽度上限。</summary>
    private const int MaxTileSquareWidth = 255;

    /// <summary>
    /// 推送服务端驱动的图格变更（如电路翻转执行器）：把待推送图格按行合并为连续矩形，
    /// 以**包 20（TileSquare）**下发给**视口内**的玩家 —— 原版对「少量图格改动」走包 20，
    /// 只有区块级地形下载才走包 10。由快照循环按快照频率调用。
    /// </summary>
    public async Task FlushTileUpdatesAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var cells = world.DrainTileUpdates(MaxTileUpdatesPerFlush);
        if (cells.Count == 0) return;

        // 先把待推送图格按行合并为「宽 × 1」矩形；宽度超 Byte 上限则再切分
        var rects = new List<(int X, int Y, int Width)>();
        foreach (var row in cells.GroupBy(c => c.Y))
        {
            var xs = row.Select(c => c.X).Distinct().OrderBy(x => x).ToArray();
            int start = xs[0];
            int prev = xs[0];

            for (int i = 1; i < xs.Length; i++)
            {
                if (xs[i] == prev + 1) { prev = xs[i]; continue; }
                AddRowRects(rects, start, prev, row.Key);
                start = prev = xs[i];
            }
            AddRowRects(rects, start, prev, row.Key);
        }

        var radius = Math.Max(1, Config.Current.ViewportRadius);
        var radiusSq = (float)radius * radius;

        for (int i = 0; i < rects.Count; i++)
        {
            var (x, y, width) = rects[i];

            // 超出本次上限 → 重新排队，保证不丢更新
            if (i >= MaxTileUpdateRectsPerFlush)
            {
                for (int cx = x; cx < x + width; cx++)
                    world.MarkTileChanged(cx, y);
                continue;
            }

            float centerX = (x + width / 2f) * TileSizePx;
            float centerY = (y + 0.5f) * TileSizePx;

            await Network.BroadcastWhereAsync(
                PacketId.TileSquare,
                new TileSquarePacket(world, x, y, width, 1),
                playerId => IsPlayerWithin(world, playerId, centerX, centerY, radiusSq),
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>把一行内的连续区间按包 20 的宽度上限切成若干矩形。</summary>
    private static void AddRowRects(List<(int X, int Y, int Width)> rects, int start, int end, int y)
    {
        while (end - start + 1 > MaxTileSquareWidth)
        {
            rects.Add((start, y, MaxTileSquareWidth));
            start += MaxTileSquareWidth;
        }
        rects.Add((start, y, end - start + 1));
    }

    /// <summary>
    /// 把世界时间与世界进度同步给所有在线玩家（包 18 / 7），并顺带下发一次 NPC 状态（包 23）。
    /// 布局依据：原版客户端（协议 326）包 18 / 包 23 的字段顺序。
    /// 生产路径下 NPC 由快照循环（<see cref="BroadcastNpcUpdatesAsync"/>，20Hz）高频下发；
    /// 这里保留一次调用仅为兼容既有调用方（变化检测会抑制重复包）。
    /// </summary>
    public async Task BroadcastWorldStateAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var recorder = Simulator.Recorder;

        // 世界进度 / 事件变化（Boss 击杀、入侵起止、昼夜切换）→ 重新下发包 7（WorldData）
        if (world.ProgressDirty)
        {
            world.ProgressDirty = false;
            await Network.BroadcastAsync(PacketId.WorldInfo,
                world.ToWorldInfoPacket(), ct).ConfigureAwait(false);
        }

        await Network.BroadcastAsync(PacketId.Time,
            new TimePacket(world.DayTime, (int)world.Time, SunModY: 0, MoonModY: 0), ct).ConfigureAwait(false);

        await BroadcastNpcUpdatesAsync(ct).ConfigureAwait(false);

        // 弹幕到期：服务端补发销毁包 29（客户端掉线 / 未发 29 时也避免幽灵弹幕）
        ProjectileEntity[] expired;
        lock (world.ProjectilesLock)
            expired = world.Projectiles.Where(p => !p.Active && !p.RemovalNotified).ToArray();

        foreach (var p in expired)
        {
            try
            {
                if (DiagnosticLog.Enabled && p.Destroyed)
                    Console.WriteLine($"[DIAG] Pkt29 destroy key={p.Key} (spawner={p.Key & 0xFF}, idx={(p.Key >> 8) & 0x3FF}, gen={(p.Key >> 18) & 0x3FFF})");
                await Network.BroadcastAsync(PacketId.ProjectileDestroy,
                    new ProjectileDestroyPacket(p.Key, p.Position), ct).ConfigureAwait(false);

                p.RemovalNotified = true;   // 发出后才记「已通知」，失败则留待下一轮重试
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                recorder.Record(new GameEvent(world.Tick, null,
                    GameEventKinds.BroadcastFailed, "projectile_destroy", GameEventCategory.Broadcast));
            }
        }

        // 玩家死亡 / 复活：服务端结算后由本线程补发权威包（118 死亡 / 12+16 复活）
        PlayerRuntime[] players;
        lock (world.PlayersLock)
            players = world.Players.Values.ToArray();

        foreach (var p in players)
        {
            if (p.Dead && !p.DeathNotified)
            {
                try
                {
                    await Network.BroadcastAsync(PacketId.PlayerDeathV2,
                        new PlayerDeathV2Packet(p.Id, 0), ct).ConfigureAwait(false);

                    p.DeathNotified = true;
                }
                catch (Exception ex) when (IsTransientSendFailure(ex))
                {
                    recorder.Record(new GameEvent(world.Tick, p.Id,
                        GameEventKinds.BroadcastFailed, "player_death", GameEventCategory.Broadcast));
                }
            }
            else if (!p.Dead && !p.RespawnNotified)
            {
                // 出生点由服务端权威划定：常规为世界出生点；「会话恢复」则把客户端放回**恢复后的原坐标**，
                // 送完即清标记（此后死亡复活仍回世界出生点）。
                short spawnX = (short)world.SpawnTileX;
                short spawnY = (short)world.SpawnTileY;
                if (p.Resumed)
                {
                    spawnX = (short)Math.Clamp((int)MathF.Floor(p.Position.X / TileSizePx), 0, world.MaxTilesX - 1);
                    spawnY = (short)Math.Clamp((int)MathF.Floor(p.Position.Y / TileSizePx), 0, world.MaxTilesY - 1);
                }

                try
                {
                    await Network.BroadcastAsync(PacketId.PlayerSpawn,
                        new PlayerSpawnPacket((byte)p.Id, spawnX, spawnY,
                            0, 0, 0, 0, 0), ct).ConfigureAwait(false);
                    await Network.BroadcastAsync(PacketId.PlayerHealth,
                        new PlayerHealthPacket(p.Id, p.Hp, p.HpMax), ct).ConfigureAwait(false);

                    p.RespawnNotified = true;
                    p.Resumed = false;   // 恢复坐标已送达（失败则保留标记，下一轮仍按恢复坐标下发）
                }
                catch (Exception ex) when (IsTransientSendFailure(ex))
                {
                    recorder.Record(new GameEvent(world.Tick, p.Id,
                        GameEventKinds.BroadcastFailed, "player_respawn", GameEventCategory.Broadcast));
                }
            }
        }

        // 掉落物被拾取 / 失效：服务端补发包 21（stack=0）通知客户端移除
        WorldItemEntity[] removedItems;
        lock (world.ItemsLock)
            removedItems = world.Items.Where(i => !i.Active && !i.RemovalNotified).ToArray();

        foreach (var item in removedItems)
        {
            try
            {
                await Network.BroadcastAsync(PacketId.ItemDrop,
                    new ItemDropPacket(item.ItemId, 0)
                    {
                        ItemSlotIndex = item.Slot,
                        Position = item.Position,
                        Velocity = new Vector2(0, 0),
                    }, ct).ConfigureAwait(false);

                item.RemovalNotified = true;
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                recorder.Record(new GameEvent(world.Tick, null,
                    GameEventKinds.BroadcastFailed, "item_remove", GameEventCategory.Broadcast));
            }
        }
    }

    /// <summary>
    /// NPC 状态同步（包 23）：位置 / 速度 / 生命。
    /// 由 60Hz 循环调用，但**逐 NPC 分档**：Boss 与「近身」NPC 每次都发，其余仅当
    /// <paramref name="fullRate"/> 为真时才发（调用方按 <see cref="NpcSyncRateDivisor"/> 分频 → 20Hz）。
    /// 原版用令牌桶把普通 NPC 压到 ≈2 包/秒（Boss ≈12Hz），这里取 20Hz 折中：约省 3 倍带宽，
    /// 同时让接触判定相关的 NPC 保持逐 tick 对齐（接触判定用服务端位置，同步越稀疏、位置差越大）。
    /// 带宽控制：**状态变化才发**（X/Y/速度/生命/存活）；未变化时按 <see cref="NpcSyncHeartbeatTicks"/>
    /// 补发一次心跳。心跳覆盖的是「客户端仍持有旧基线」的情形（如走远再回来、或该 NPC 在其他玩家
    /// 视野内移动过），这类客户端不会因为基线存在而永远收不到新状态；中途入服的玩家靠
    /// 「无基线即全量下发」即可看到静止 NPC。
    /// </summary>
    public async Task BroadcastNpcUpdatesAsync(CancellationToken ct = default, bool fullRate = true)
    {
        var world = Simulator.State;

        // 视口裁剪：只发给视野半径内的玩家，避免把全世界 NPC 推给所有人
        var radius = Math.Max(1, Config.Current.ViewportRadius);
        var radiusSq = (float)radius * radius;

        // 近身判定用的玩家快照：仅在降频轮需要，锁内取一次（避免在 NPC 循环里反复加锁）
        PlayerRuntime[] players = Array.Empty<PlayerRuntime>();
        if (!fullRate)
        {
            lock (world.PlayersLock)
            {
                players = world.Players.Values.Where(p => p.Active && !p.Dead).ToArray();

                // 基线自清理：玩家 id 不复用，会话接管还会换新 id，离线玩家的条目再无读者。
                // 不清理则每名玩家每见过一代 NPC 都会留下一条永不释放的记录（20Hz 扫一次足够）。
                foreach (var id in _npcBaselines.Keys)
                    if (!world.Players.ContainsKey(id)) _npcBaselines.TryRemove(id, out _);
            }
        }

        // 先在锁内取一致快照（仿真线程会增删 NPC），再在锁外逐个下发（不在持锁期间做 I/O）
        // 索引上限 199（原版 Main.npc[200]）；下标即客户端认的 npcIndex
        WorldNpc[] npcs;
        lock (world.NpcsLock)
        {
            npcs = world.Npcs.Count <= byte.MaxValue + 1
                ? world.Npcs.ToArray()
                : world.Npcs.GetRange(0, byte.MaxValue + 1).ToArray();
        }

        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            var life = npc.Active ? npc.Life : 0;   // 已死亡 → life=0，客户端据此移除
            // 心跳：距上次**实际下发**已超过 NpcSyncHeartbeatTicks（≈1s）时，即使状态未变化也补发一次。
            // 必要性：Synced* 每轮都会推进（与「是否真的发给了谁」无关），故玩家离开视野期间发生的位移
            // 不会留下「未发送的差值」；该玩家回到视野内时，若没有心跳就再也收不到这个 NPC 的新位置。
            var heartbeatDue = world.Tick - npc.SyncedTick >= NpcSyncHeartbeatTicks;
            var changed = npc.SyncForced || heartbeatDue || npc.X != npc.SyncedX || npc.Y != npc.SyncedY
                          || npc.VelocityX != npc.SyncedVelocityX || npc.VelocityY != npc.SyncedVelocityY
                          || life != npc.SyncedLife || npc.Active != npc.SyncedActive
                          || npc.Direction != npc.SyncedDirection
                          || !npc.Ai.AsSpan().SequenceEqual(npc.SyncedAi);
            if (!fullRate && !IsNpcHighPriority(npc, players)) continue;

            npc.SyncedX = npc.X;
            npc.SyncedY = npc.Y;
            npc.SyncedVelocityX = npc.VelocityX;
            npc.SyncedVelocityY = npc.VelocityY;
            npc.SyncedLife = life;
            npc.SyncedActive = npc.Active;
            npc.SyncedDirection = npc.Direction;
            npc.Ai.CopyTo(npc.SyncedAi, 0);

            var packet = new NpcUpdatePacket(
                Index: (byte)i,
                Generation: npc.Generation,
                Position: new Vector2(npc.X, npc.Y),
                Velocity: new Vector2(npc.VelocityX, npc.VelocityY),
                // 255 = 显式「无目标」：客户端侧 NPC AI 以 target==255（部分 AI 还含 <=0）判为无目标并自行 TargetClosest。
                // 若发 0，会被当作「目标 = 玩家槽位 0」，客户端 AI 会去追那个玩家（通常是错的）。
                Target: 255,
                NetId: npc.NetId == 0 ? (short)npc.Type : npc.NetId,
                Life: life,
                LifeMax: Math.Max(1, npc.LifeMax),
                DirectionPositive: npc.Direction >= 0,
                // 原版 ai[0..3]：依赖 ai 的 aiStyle（如史莱姆跳跃状态）必须下发，
                // 否则客户端会把 ai 全置 0，表现与服务端不一致。
                Ai: npc.Ai);

            var baselineKey = (Index: i, Generation: npc.Generation);
            var sent = 0;
            await Network.BroadcastWhereAsync(PacketId.NpcUpdate, packet, playerId =>
            {
                if (!IsPlayerWithin(world, playerId, npc.X, npc.Y, radiusSq))
                    return false;

                var baselines = _npcBaselines.GetOrAdd(playerId,
                    static _ => new ConcurrentDictionary<(int Index, byte Generation), byte>());
                if (!changed && baselines.ContainsKey(baselineKey))
                    return false;

                baselines[baselineKey] = 0;
                sent++;
                return true;
            }, ct).ConfigureAwait(false);

            // 换型（netID 变化）的强制同步 + 心跳基线：都只在**确实发出**后才清除 / 推进，
            // 否则该 NPC 不在任何玩家视野内时会被白清，客户端会一直停在旧形态、心跳也随之失效。
            if (sent > 0)
            {
                npc.SyncForced = false;
                npc.SyncedTick = world.Tick;
            }
        }
    }

    /// <summary>非高优先级 NPC 的降频倍数：60Hz / 3 = 20Hz。</summary>
    private const int NpcSyncRateDivisor = 3;

    /// <summary>「近身」判定半径的平方（6 格 = 96px）：该范围内 NPC 可能与玩家接触，需逐 tick 对齐。</summary>
    private const float NpcSyncNearRangeSq = 96f * 96f;

    /// <summary>是否需要逐 tick 下发：Boss，或与任一存活玩家近身（可能发生接触伤害）。</summary>
    private static bool IsNpcHighPriority(WorldNpc npc, PlayerRuntime[] players)
    {
        if (npc.IsBoss) return true;

        foreach (var p in players)
        {
            var dx = p.AimPosition.X - npc.X;
            var dy = p.AimPosition.Y - npc.Y;
            if (dx * dx + dy * dy <= NpcSyncNearRangeSq) return true;
        }
        return false;
    }

    /// <summary>NPC 同步心跳（tick）：状态未变化也按该周期补发一次（≈1s @ 60Hz）。</summary>
    private const long NpcSyncHeartbeatTicks = 60;

    /// <summary>
    /// 新增弹幕（包 27 / SyncProjectile）：客户端上报的弹幕转发给**其他**玩家；
    /// Boss AI 发射的服务端弹幕（<see cref="ProjectileEntity.Owner"/> = -1）广播给所有人。
    /// 发出成功后才置 <see cref="ProjectileEntity.NewNotified"/>，失败留待下一轮重试。
    /// </summary>
    public async Task FlushNewProjectilesAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;

        ProjectileEntity[] pending;
        lock (world.ProjectilesLock)
            pending = world.Projectiles.Where(p => p.Active && !p.NewNotified).ToArray();

        foreach (var p in pending)
        {
            var packet = new ProjectileNewPacket(p.Key, p.Position, p.Velocity, p.Type)
            {
                Damage = 0,   // 伤害由服务端权威结算，不下发客户端声明值
            };

            try
            {
                if (p.Owner >= 0)
                {
                    await Network.BroadcastWhereAsync(PacketId.ProjectileNew, packet,
                        playerId => playerId != p.Owner, ct).ConfigureAwait(false);
                }
                else
                {
                    await Network.BroadcastAsync(PacketId.ProjectileNew, packet, ct).ConfigureAwait(false);
                }

                p.NewNotified = true;
            }
            catch (Exception ex) when (IsTransientSendFailure(ex))
            {
                Simulator.Recorder.Record(new GameEvent(world.Tick, null,
                    GameEventKinds.BroadcastFailed, "projectile_new", GameEventCategory.Broadcast));
            }
        }
    }

    /// <summary>玩家瞄准位置是否落在 (x, y) 的视口半径内。</summary>
    private static bool IsPlayerWithin(WorldState world, int playerId, float x, float y, float radiusSq)
    {
        if (!world.Players.TryGetValue(playerId, out var player)) return true; // 位置未知 → 不裁剪，避免 NPC 不可见
        var dx = player.AimPosition.X - x;
        var dy = player.AimPosition.Y - y;
        return dx * dx + dy * dy <= radiusSq;
    }

    /// <summary>等待关闭信号后再停止网络宿主（避免启动即停机）。</summary>
    private async Task StopNetworkOnShutdownAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 收到关闭信号，进入优雅停机
        }

        await Network.StopAsync().ConfigureAwait(false);
    }

    // ========================================================================
    // 配置热更新 → 权威子系统
    // ========================================================================
    private void OnConfigurationChanged(ServerConfig cfg)
    {
        // 把全部新阈值推送给已构造的权威子系统：子系统以引用整体替换阈值，无需重启即生效
        var t = AuthorityThresholds.From(cfg);
        _enforcers.UpdateThresholds(t.Rate, t.Player, t.Movement, t.Combat, t.Inventory, t.World);
        _world.SscEnabled = cfg.SscEnabled; // 全局 SSC 开关热重载（新连接 / 下次 WorldInfo 生效）
        _world.DestroySummonsOnWeaponRemoval = cfg.DestroySummonsOnWeaponRemoval; // 移除召唤武器即销毁（即时生效）
        DiagnosticLog.Enabled = cfg.VerboseDiagnostics; // 诊断日志开关热重载（排障时无需重启）

        Console.WriteLine(
            $"[Config] 热重载已生效：MaxSingleDamage={cfg.MaxSingleDamage}, " +
            $"MaxTileBreakPerSecond={cfg.MaxTileBreakPerSecond}, MaxPlayerHp={cfg.MaxPlayerHp}, " +
            $"VerboseDiagnostics={cfg.VerboseDiagnostics}");
    }

    public void Dispose()
    {
        Config.OnChanged -= OnConfigurationChanged;
        _metricsServer?.Dispose();
        foreach (var d in _disposables) d.Dispose();
        Network.DisposeAsync().AsTask().GetAwaiter().GetResult();
        // 网络停止后再释放分片管线，确保在途 ProcessAsync 已完成
        _pipelineDisposable?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Players is IDisposable pd) pd.Dispose();

        // 扩展层释放：卸载插件 → 停止 WorkerPool
        if (Plugins != null!) Plugins.StopAllAsync(Hooks).GetAwaiter().GetResult();
        Workers?.Dispose();
    }

    // ---- 首次启动生成默认配置（含全部阈值，运维可直接改）----
    private static string CreateDefaultConfig(string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new ServerConfig(), ConfigurationService.JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>注册内置命令（say / who / kick / help / give / boss）；插件可另注册自己的命令。</summary>
    private static void RegisterBuiltinCommands(CommandService commands, ServerApi server,
        WorldState world, NetworkHost network, WorldSimulator simulator)
    {
        commands.Register("say", "广播一条消息：say <text>", (_, args) =>
        {
            if (args.Length == 0) return CommandResult.Fail("用法：say <text>");
            server.Broadcast(string.Join(' ', args));
            return CommandResult.Ok("已广播");
        });

        commands.Register("who", "列出在线玩家", (_, _) =>
        {
            var players = server.GetOnlinePlayers();
            return CommandResult.Ok(players.Count == 0
                ? "当前无在线玩家"
                : string.Join(", ", players.Select(p => $"#{p.PlayerId} {p.Name}")));
        });

        commands.Register("kick", "踢出玩家：kick <playerId> [reason]", (_, args) =>
        {
            if (args.Length == 0 || !int.TryParse(args[0], out var playerId))
                return CommandResult.Fail("用法：kick <playerId> [reason]");

            var reason = args.Length > 1 ? string.Join(' ', args[1..]) : "Kicked by command";
            server.KickPlayer(playerId, reason);
            return CommandResult.Ok($"已踢出 #{playerId}");
        });

        commands.Register("help", "列出全部命令", (_, _) =>
            CommandResult.Ok(string.Join("\n", commands.Commands.Select(c => $"{c.Name} - {c.Help}"))));

        commands.Register("give", "给在线玩家物品并同步背包：give <playerId> <itemId> [slot]", (_, args) =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out var pid) || !int.TryParse(args[1], out var itemId))
                return CommandResult.Fail("用法：give <playerId> <itemId> [slot]");

            int slot;
            lock (world.PlayersLock)
            {
                if (!world.Players.TryGetValue(pid, out var p))
                    return CommandResult.Fail($"玩家 #{pid} 不在线");

                // 全局 SSC 开关：关闭时走 TShock「GiveItemByDrop」方案 —— 在玩家脚下生成**专属掉落物**
                // （包 21 由快照循环 FlushNewItemsAsync 下发），玩家拾取（包 22 → PickupItemCommand）入背包。
                // 拾取是客户端驱动的正常流程（客户端把物品加入本地背包并显示），不依赖 SSC 背包权威。
                if (!world.SscEnabled)
                {
                    int itemSlot;
                    lock (world.ItemsLock)
                    {
                        itemSlot = 0;
                        while (world.Items.Any(i => i.Slot == itemSlot)) itemSlot++;
                        world.Items.Add(new WorldItemEntity
                        {
                            Slot = itemSlot,
                            ItemId = itemId,
                            Stack = 1,
                            Position = p.Position,
                            Velocity = new Vector2(0, -2f),   // 轻微上抛，便于玩家看见
                            Prefix = 0,
                            OwnedBy = pid,                    // 专属：只准目标玩家拾取
                            NewNotified = false,              // 服务端主动生成 → 快照循环补发包 21
                        });
                    }
                    Console.WriteLine($"[Give] 玩家 #{pid} 脚下生成掉落物 槽 {itemSlot} 物品 {itemId}（SSC 关闭：拾取入包）");
                    return CommandResult.Ok($"已在 #{pid} 脚下生成物品 {itemId}（SSC 关闭，请拾取）");
                }

                // SSC 开启：服务端背包唯一真相 → 直接写背包 + 包 5 下发
                slot = args.Length > 2 && int.TryParse(args[2], out var s) ? s : -1;
                if (slot < 0)
                {
                    slot = -1;
                    for (int i = 0; i < p.Items.Length; i++)
                        if (p.Items[i] == 0) { slot = i; break; }
                    if (slot < 0) return CommandResult.Fail("背包已满");
                }
                else if (slot >= p.Items.Length)
                    return CommandResult.Fail("槽位越界");
                p.Items[slot] = itemId;
                p.ItemPrefixes[slot] = 0;
                Console.WriteLine($"[Give] 玩家 #{pid} 槽 {slot} 物品 {itemId}（背包 Items[{slot}]={p.Items[slot]}）");
            }
            network.SendToPlayerAsync(pid, PacketId.InventorySlot,
                new InventorySlotPacket(Slot: (short)slot, ItemId: itemId, Stack: 1) { PlayerId = pid });
            Console.WriteLine($"[Give] 已向 #{pid} 下发包5 slot={slot} type={itemId}");
            return CommandResult.Ok($"已给 #{pid} 槽 {slot} 放置物品 {itemId}");
        });

        // 召唤 NPC / Boss（真机调试用）：npcId 为原版 NPC 类型 ID，生命 / 伤害 / 防御取 NpcStatsTable
        // （未收录类型按 1000 生命兜底，见 WorldSimulator.SpawnBoss），出生点对齐包 23 的同步口径。
        commands.Register("boss", "召唤 NPC / Boss：boss <npcId> [x y]（省略坐标则在发起者上方 8 格）", (playerId, args) =>
        {
            if (args.Length == 0 || !int.TryParse(args[0], out var npcType) || npcType <= 0)
                return CommandResult.Fail("用法：boss <npcId> [x y]（4=眼魔 35=骷髅王 50=史莱姆王 222=蜂后 113=血肉墙）");

            float x, y;
            if (args.Length >= 3 && float.TryParse(args[1], out var px) && float.TryParse(args[2], out var py))
            {
                (x, y) = (px, py);                       // 显式像素坐标（左上角锚点由 SpawnBoss 换算）
            }
            else
            {
                lock (world.PlayersLock)
                {
                    if (!world.Players.TryGetValue(playerId, out var p))
                        return CommandResult.Fail("发起者不在线，请显式给出坐标：boss <npcId> <x> <y>");

                    x = p.AimPosition.X;
                    y = p.AimPosition.Y - 8f * 16f;      // 发起者上方 8 格（与夜晚自然刷眼魔同口径）
                }
            }

            var boss = simulator.SpawnBoss(npcType, x, y);
            int slot;
            lock (world.NpcsLock) slot = world.Npcs.IndexOf(boss);
            return CommandResult.Ok($"已召唤 NPC {npcType}（槽位 {slot}，生命 {boss.Life}/{boss.LifeMax}）");
        });
    }

    /// <summary>
    /// ServerConfig → 权威阈值集合。启动注入与热重载共用同一映射，保证阈值来源唯一（架构 §4.5），
    /// 避免两处各写一份导致"启动值与热更新值不一致"。
    /// </summary>
    private readonly record struct AuthorityThresholds(
        RateLimits Rate,
        PlayerLimits Player,
        MovementLimits Movement,
        CombatLimits Combat,
        InventoryLimits Inventory,
        WorldLimits World)
    {
        public static AuthorityThresholds From(ServerConfig c) => new(
            Rate: new RateLimits
            {
                MaxPacketsPerSecond = c.MaxPacketsPerSecond,
                MaxTileBreakPerSecond = c.MaxTileBreakPerSecond,
                MaxTilePlacePerSecond = c.MaxTilePlacePerSecond,
                MaxProjectilesPerSecond = c.MaxProjectilesPerSecond,
                MaxChatPerMinute = c.MaxChatPerMinute,
                MaxLiquidPerSecond = c.MaxLiquidPerSecond,
            },
            Player: new PlayerLimits(c.MaxPlayerHp, c.MaxPlayerMana),
            // 移动限速用飞行上限覆盖步行/冲刺，降低误判
            Movement: new MovementLimits(c.MaxFlightSpeed, c.TeleportTolerance, MaxFallSpeed: c.MaxFallSpeed),
            Combat: new CombatLimits(c.MaxSingleDamage, c.MaxDpsWindowSeconds, c.MaxDps),
            Inventory: new InventoryLimits(c.SscEnabled, c.MaxStackSize),
            World: new WorldLimits(c.MaxTileBreakPerSecond, c.MaxTilePlacePerSecond));
    }
}
