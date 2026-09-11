// Phase 6 - 组装根：把 Phase 2/3/4/5 与基础设施（配置/持久化/监控/封禁）串联
// 对应架构文档 §4.5 基础设施层

using System.Net;
using System.Collections.Generic;
using System.Linq;
using TerraAuth.Config;
using TerraAuth.Persistence;
using TerraAuth.Monitoring;
using TerraAuth.Security;
using TerraAuth.Authority;
using TerraAuth.Simulation;
using TerraAuth.Net;
using TerraAuth.Net.Phase4;
using TerraAuth.Net.Phase5;
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
    /// <summary>并行配置：线程池大小、Chunk 大小等。</summary>
    public ParallelConfig Parallel { get; }
    /// <summary>Worker 池：网络 I/O 与解码并行。</summary>
    public WorkerPool Workers { get; }

    private readonly List<IDisposable> _disposables = new();
    private readonly MetricsHttpServer? _metricsServer;
    private readonly IAsyncDisposable? _pipelineDisposable;

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
        ParallelConfig parallel,
        WorkerPool workers,
        AuthorityEnforcers enforcers,
        IAsyncDisposable? pipelineDisposable = null)
    {
        Config = config;
        Players = players;
        Audit = audit;
        Metrics = metrics;
        Bans = bans;
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
        Parallel = parallel;
        Workers = workers;
        _enforcers = enforcers;
        _pipelineDisposable = pipelineDisposable;

        // 配置热更新 → 动态调整阈值（如 MaxWalkSpeed / MaxSingleDamage）
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

        // 2. 持久化（SQLite；无 NuGet 时走内嵌 LiteDb）
        var db = new SqlitePersistence(dbPath);

        // 3. 监控（Prometheus 风格 + /metrics HTTP 端点）
        var metrics = new PrometheusMetrics();
        var metricsServer = new MetricsHttpServer(metrics, metricsPort);

        // 4. 封禁（SQLite 持久化 + 进程内兜底）
        var banStore = new SqliteBanStore(db);
        var bans = new BanManager(banStore, config, db);

        // 5. 权威层（Phase 2）+ 审计桥接
        // 世界数据：程序化生成小世界（解除 1×1 默认世界导致的进服阻塞，见 WorldGenerator 注释）
        var world = WorldGenerator.GenerateSmall();
        Console.WriteLine(
            $"[World] 程序化生成 {world.WorldName} {world.MaxTilesX}×{world.MaxTilesY}，" +
            $"出生点 ({world.SpawnTileX},{world.SpawnTileY})，区块 {world.MaxTilesX / 200}×{world.MaxTilesY / 150}");
        var commands = new CommandQueue();
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

        // 管线阶段顺序（越早拒绝成本越低）
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
        });

        // ---- 扩展层初始化（插件 / Mod 兼容 / 并行）----
        // 1. 并行基础设施（需求：多 CPU 线程优化）
        var parallelConfig = new ParallelConfig();
        var workers = new WorkerPool(parallelConfig.WorkerThreads);

        // 2. Hook 注册表 + 插件上下文（需求 1：预留 hook）
        var logger = new CoreLogger();
        var hooks = new HookRegistry(logger);

        // 3. Mod 兼容层（需求 2：Mod 服务器支持）
        // ModPolicy 不属于 ServerConfig（配置文件当前仅含阈值），默认仅允许原版客户端
        var modPolicy = new ModPolicy { Mode = ModPolicyMode.VanillaOnly };
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
        var simulator = new WorldSimulator(world, commands, recorder, snapshots);

        // 6. 网络层（Phase 5）——先建，以便把 SnapshotSender 注入快照广播
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager(config.Current.MaxConnections);
        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(protocol.Version);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Any, port),
            decoder, encoder, protocol, connections, hookedPipeline, commands, workers, world,
            config.Current.PlayerWhitelist, hooks,
            new ViolationKickLimits(
                config.Current.MaxViolationsBeforeBan,
                config.Current.ViolationWindowMinutes * 60));
        networkForNames = network; // HookedPipeline 的玩家名解析延迟绑定到此

        // 7. 插件上下文 + 加载器
        // 必须在网络层之后：ServerApi 需要连接管理（踢出/在线查询）与封禁管理器才能真实生效
        var pluginContext = new PluginContext(
            hooks: hooks,
            logger: logger,
            configuration: new CoreConfiguration(config),
            metrics: new CoreMetrics(metrics),
            server: new ServerApi(auditLogger, metrics, network, connections, bans, world),
            eventStore: new CoreEventStore(auditLogger));
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
            hooks, pluginLoader, modDetector, customPackets, parallelConfig, workers, enforcers,
            shardedPipeline);

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
                await Task.Delay(1000 / Math.Max(1, Config.Current.SnapshotRateHz), ct);
            }
        }, ct);

        // 4. 世界状态同步循环：时间（包 18）与 NPC（包 23）定期下发
        //    1Hz 足够：客户端自身按 tick 推进时间，这里只做周期性对账；NPC 位置变化平缓
        var worldSyncTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await BroadcastWorldStateAsync(ct).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }, ct);

        await Task.WhenAll(simTask, netTask, snapTask, worldSyncTask).ConfigureAwait(false);
    }

    /// <summary>
    /// 把世界时间与 NPC 状态同步给所有在线玩家（包 18 / 23）。
    /// 布局权威：原版 <c>NetMessage.SendData</c> case 18 / case 23。
    /// </summary>
    public async Task BroadcastWorldStateAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;

        await Network.BroadcastAsync(PacketId.Time,
            new TimePacket(world.DayTime, (int)world.Time, SunModY: 0, MoonModY: 0), ct).ConfigureAwait(false);

        // NPC 索引上限 199（原版 Main.npc[200]）
        // 视口裁剪：只发给视野半径内的玩家，避免把全世界 NPC 推给所有人
        var radius = Math.Max(1, Config.Current.ViewportRadius);
        var radiusSq = (float)radius * radius;

        // 先在锁内取一致快照（仿真线程会增删 NPC），再在锁外逐个下发（不在持锁期间做 I/O）
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
            var packet = new NpcUpdatePacket(
                Index: (byte)i,
                Generation: npc.Generation,
                Position: new Vector2(npc.X, npc.Y),
                Velocity: new Vector2(npc.VelocityX, npc.VelocityY),
                Target: 0,
                NetId: npc.NetId == 0 ? (short)npc.Type : npc.NetId,
                Life: npc.Active ? npc.Life : 0,   // 已死亡 → life=0，客户端据此移除
                LifeMax: Math.Max(1, npc.LifeMax));

            await Network.BroadcastWhereAsync(PacketId.NpcUpdate, packet,
                playerId => IsPlayerWithin(world, playerId, npc.X, npc.Y, radiusSq), ct).ConfigureAwait(false);
        }
    }

    /// <summary>玩家当前位置是否落在 (x, y) 的视口半径内。</summary>
    private static bool IsPlayerWithin(WorldState world, int playerId, float x, float y, float radiusSq)
    {
        if (!world.Players.TryGetValue(playerId, out var player)) return true; // 位置未知 → 不裁剪，避免 NPC 不可见
        var dx = player.Position.X - x;
        var dy = player.Position.Y - y;
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

        Console.WriteLine(
            $"[Config] 热重载已生效：MaxSingleDamage={cfg.MaxSingleDamage}, " +
            $"MaxTileBreakPerSecond={cfg.MaxTileBreakPerSecond}, MaxPlayerHp={cfg.MaxPlayerHp}");
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
            new ServerConfig(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
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
            },
            Player: new PlayerLimits(c.MaxPlayerHp, c.MaxPlayerMana),
            // 移动限速用飞行上限覆盖步行/冲刺，降低误判
            Movement: new MovementLimits(c.MaxFlightSpeed, c.TeleportTolerance),
            Combat: new CombatLimits(c.MaxSingleDamage, c.MaxDpsWindowSeconds, c.MaxDps),
            Inventory: new InventoryLimits(c.SscEnabled, c.MaxStackSize),
            World: new WorldLimits(c.MaxTileBreakPerSecond, c.MaxTilePlacePerSecond));
    }
}
