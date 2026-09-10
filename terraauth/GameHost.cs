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
        var rate = new RateLimits();
        var auditLogger = new PersistenceAuditLogger(db, metrics);
        // 移动权威阈值取自 ServerConfig（架构 §4.5 唯一来源）；用飞行上限覆盖步行/冲刺，降低误判
        var enforcers = new AuthorityEnforcers(rate, auditLogger,
            new MovementLimits(config.Current.MaxFlightSpeed, config.Current.TeleportTolerance));

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
        var hookedPipeline = new HookedPipeline(shardedPipeline, hooks, logger);

        // 5. 插件上下文 + 加载器
        var pluginContext = new PluginContext(
            hooks: hooks,
            logger: logger,
            configuration: new CoreConfiguration(config),
            metrics: new CoreMetrics(metrics),
            server: new ServerApi(auditLogger, metrics),
            eventStore: new CoreEventStore(auditLogger));
        var pluginLoader = new PluginLoader(
            pluginDirectory: System.IO.Path.Combine(System.AppContext.BaseDirectory, "plugins"),
            logger: logger,
            context: pluginContext);

        // 6. 仿真层（Phase 3）
        var simulator = new WorldSimulator(world, commands, recorder, snapshots);

        // 7. 网络层（Phase 5）——先建，以便把 SnapshotSender 注入快照广播
        var protocol = new TerrariaProtocol();
        var connections = new ConnectionManager(config.Current.MaxConnections);
        var decoder = new PacketDecoder();
        var encoder = new PacketEncoder(protocol.Version);
        var network = new NetworkHost(
            new IPEndPoint(IPAddress.Any, port),
            decoder, encoder, protocol, connections, hookedPipeline, commands, workers, world,
            config.Current.PlayerWhitelist, hooks);

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
            store: snapshots); // 与仿真层共享同一 store：仿真每 tick 写入即成为可下发帧

        var host = new GameHost(
            config, db, db, metrics, bans, network, hookedPipeline, simulator, broadcaster, metricsServer,
            hooks, pluginLoader, modDetector, customPackets, parallelConfig, workers, shardedPipeline);

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

        await Task.WhenAll(simTask, netTask, snapTask).ConfigureAwait(false);
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
    // 配置热更新 → 权威子系统（IConfigurationObserver 模式）
    // ========================================================================
    private void OnConfigurationChanged(ServerConfig cfg)
    {
        // 把阈值推送给权威子系统（如运行时收紧 MaxWalkSpeed）
        // 各 Authority 持有 ServerConfig 引用即可自动读到新值（Current 是引用类型）
        Metrics.SetAuthorityOverhead(0); // placeholder：触发一次采样
        Console.WriteLine($"[Config] 热重载：MaxWalkSpeed={cfg.MaxWalkSpeed}, MaxSingleDamage={cfg.MaxSingleDamage}");
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
}
