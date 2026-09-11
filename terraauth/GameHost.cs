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
        // 世界数据：优先加载配置指定的 .wld；未配置则程序化生成小世界（见 WorldGenerator 注释）
        var world = LoadBaseWorld(config.Current.WorldPath);

        // 世界改动回放：基准世界是确定性的（程序化生成 / .wld 解析），只需叠加上次运行落盘的增量，
        // 否则玩家挖 / 放 / 箱内物品在服务端重启后会全部丢失。
        ApplyPersistedWorldChanges(world, db);
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
        // ModPolicy 取自 server.json 的 ModPolicy 节（未配置时默认 VanillaOnly = 仅原版客户端）
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
                config.Current.ViolationWindowMinutes * 60),
            sessionResumeGraceSeconds: config.Current.SessionResumeGraceSeconds);
        networkForNames = network; // HookedPipeline 的玩家名解析延迟绑定到此

        // 6.5 TModLoader 兼容层：Mod 列表解析 + 自定义包转发
        // 转发通道绑定到网络层单播发送（包号 250-255 原样透传，其余被 CustomPackets 拦截）
        var tmodLoader = new TModLoaderCompat(
            modDetector, logger, customPackets,
            forward: (_, toPlayerId, packetId, data) =>
                network.SendRawAsync(toPlayerId, (PacketId)packetId, data));

        // 7. 插件上下文 + 加载器
        // 必须在网络层之后：ServerApi 需要连接管理（踢出/在线查询）与封禁管理器才能真实生效
        // 命令子系统：内置 say / who / kick / help；插件可经 IServerApi.ExecuteCommand 调用
        var commandService = new CommandService();
        var serverApi = new ServerApi(auditLogger, metrics, network, connections, bans, world, commandService);
        RegisterBuiltinCommands(commandService, serverApi);

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
                // 服务端判定的玩家受击（接触 / 下落伤害）→ 包 117 + 包 16
                await FlushPlayerHurtAsync(ct).ConfigureAwait(false);
                // 服务端主动生成的掉落物（Boss 掉落等）→ 包 21
                await FlushNewItemsAsync(ct).ConfigureAwait(false);
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
                // 世界改动按 1Hz 落盘：够快（崩溃最多丢 1 秒改动）且写放大可控
                await FlushWorldChangesAsync(ct).ConfigureAwait(false);
                // 回收超过宽限期的离线会话（会话恢复的时间边界）
                Simulator.State.ReapOfflineSessions();
                // 空服时的全量 .wld 导出（配置了 WorldExportPath 才生效；内部自带间隔与在线判定）
                TryExportWorld();
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }, ct);

        await Task.WhenAll(simTask, netTask, snapTask, worldSyncTask).ConfigureAwait(false);

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
    /// 否则程序化生成小世界（保持既有默认行为）。
    /// </summary>
    private static WorldState LoadBaseWorld(string worldPath)
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

        var generated = WorldGenerator.GenerateSmall();
        Console.WriteLine(
            $"[World] 程序化生成 {generated.WorldName} {generated.MaxTilesX}×{generated.MaxTilesY}，" +
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

    /// <summary>单批受击通知上限。</summary>
    private const int MaxHurtNotifiesPerFlush = 64;

    /// <summary>
    /// 下发服务端判定的玩家受击：包 117（受击表现，广播给所有玩家）+
    /// 包 16（权威生命，单发给受击者，客户端据此更新血条）。由快照循环按快照频率调用。
    /// </summary>
    public async Task FlushPlayerHurtAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var hurts = world.DrainPlayerHurt(MaxHurtNotifiesPerFlush);
        if (hurts.Count == 0) return;

        foreach (var (playerId, damage) in hurts)
        {
            await Network.BroadcastAsync(PacketId.PlayerHurtV2,
                new PlayerHurtV2Packet(playerId, damage), ct).ConfigureAwait(false);

            PlayerRuntime? player;
            lock (world.PlayersLock)
                world.Players.TryGetValue(playerId, out player);

            if (player is not null)
            {
                await Network.SendToPlayerAsync(playerId, PacketId.PlayerHealth,
                    new PlayerHealthPacket(playerId, player.Hp, player.HpMax), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 下发服务端主动生成的掉落物（如 Boss 掉落）：包 21（含服务端分配的槽位 / 位置 / 速度 / 堆叠），按视口裁剪。
    /// 客户端上报生成的掉落物由客户端自行广播，不在此列（<c>NewNotified</c> 默认 true）。由快照循环调用。
    /// </summary>
    public async Task FlushNewItemsAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;

        WorldItemEntity[] fresh;
        lock (world.ItemsLock)
        {
            fresh = world.Items.Where(i => i.Active && !i.NewNotified).ToArray();
            foreach (var item in fresh) item.NewNotified = true;
        }

        if (fresh.Length == 0) return;

        var radius = Math.Max(1, Config.Current.ViewportRadius);
        var radiusSq = (float)radius * radius;

        foreach (var item in fresh)
        {
            await Network.BroadcastWhereAsync(PacketId.ItemDrop,
                new ItemDropPacket(item.ItemId, item.Stack)
                {
                    ItemSlotIndex = item.Slot,
                    Position = item.Position,
                    Velocity = item.Velocity,
                    Prefix = item.Prefix,
                },
                playerId => IsPlayerWithin(world, playerId, item.Position.X, item.Position.Y, radiusSq),
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>单次推送的图格数上限。</summary>
    private const int MaxTileUpdatesPerFlush = 256;

    /// <summary>单次推送的矩形区块数上限（其余重新排队，下次 flush 继续）。</summary>
    private const int MaxTileUpdateRectsPerFlush = 64;

    /// <summary>
    /// 推送服务端驱动的图格变更（如电路翻转执行器）：把待推送图格按行合并为连续矩形，
    /// 以包 10（TileSection）小矩形下发给**视口内**的玩家。由快照循环按快照频率调用。
    /// </summary>
    public async Task FlushTileUpdatesAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;
        var cells = world.DrainTileUpdates(MaxTileUpdatesPerFlush);
        if (cells.Count == 0) return;

        // 先把待推送图格按行合并为「宽 × 1」矩形
        var rects = new List<(int X, int Y, int Width)>();
        foreach (var row in cells.GroupBy(c => c.Y))
        {
            var xs = row.Select(c => c.X).Distinct().OrderBy(x => x).ToArray();
            int start = xs[0];
            int prev = xs[0];

            for (int i = 1; i < xs.Length; i++)
            {
                if (xs[i] == prev + 1) { prev = xs[i]; continue; }
                rects.Add((start, row.Key, prev - start + 1));
                start = prev = xs[i];
            }
            rects.Add((start, row.Key, prev - start + 1));
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
                PacketId.TileSendSection,
                new TileSectionPacket(world, x, y, width, 1),
                playerId => IsPlayerWithin(world, playerId, centerX, centerY, radiusSq),
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 把世界时间与 NPC 状态同步给所有在线玩家（包 18 / 23）。
    /// 布局依据：原版客户端（协议 326）包 18 / 包 23 的字段顺序。
    /// </summary>
    public async Task BroadcastWorldStateAsync(CancellationToken ct = default)
    {
        var world = Simulator.State;

        // 世界进度 / 事件变化（Boss 击杀、入侵起止、昼夜切换）→ 重新下发包 7（WorldData）
        if (world.ProgressDirty)
        {
            world.ProgressDirty = false;
            await Network.BroadcastAsync(PacketId.WorldInfo,
                world.ToWorldInfoPacket(), ct).ConfigureAwait(false);
        }

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

        // 弹幕到期：服务端补发销毁包 29（客户端掉线 / 未发 29 时也避免幽灵弹幕）
        ProjectileEntity[] expired;
        lock (world.ProjectilesLock)
        {
            expired = world.Projectiles.Where(p => !p.Active && !p.RemovalNotified).ToArray();
            foreach (var p in expired) p.RemovalNotified = true;
        }

        foreach (var p in expired)
        {
            await Network.BroadcastAsync(PacketId.ProjectileDestroy,
                new ProjectileDestroyPacket(p.Key, p.Position), ct).ConfigureAwait(false);
        }

        // 玩家死亡 / 复活：服务端结算后由本线程补发权威包（118 死亡 / 12+16 复活）
        PlayerRuntime[] players;
        lock (world.PlayersLock)
            players = world.Players.Values.ToArray();

        foreach (var p in players)
        {
            if (p.Dead && !p.DeathNotified)
            {
                p.DeathNotified = true;
                await Network.BroadcastAsync(PacketId.PlayerDeathV2,
                    new PlayerDeathV2Packet(p.Id, 0), ct).ConfigureAwait(false);
            }
            else if (!p.Dead && !p.RespawnNotified)
            {
                p.RespawnNotified = true;

                // 出生点由服务端权威划定：常规为世界出生点；「会话恢复」则把客户端放回**恢复后的原坐标**，
                // 送完即清标记（此后死亡复活仍回世界出生点）。
                short spawnX = (short)world.SpawnTileX;
                short spawnY = (short)world.SpawnTileY;
                if (p.Resumed)
                {
                    p.Resumed = false;
                    spawnX = (short)Math.Clamp((int)MathF.Floor(p.Position.X / TileSizePx), 0, world.MaxTilesX - 1);
                    spawnY = (short)Math.Clamp((int)MathF.Floor(p.Position.Y / TileSizePx), 0, world.MaxTilesY - 1);
                }

                await Network.BroadcastAsync(PacketId.PlayerSpawn,
                    new PlayerSpawnPacket((byte)p.Id, spawnX, spawnY,
                        0, 0, 0, 0, 0), ct).ConfigureAwait(false);
                await Network.BroadcastAsync(PacketId.PlayerHealth,
                    new PlayerHealthPacket(p.Id, p.Hp, p.HpMax), ct).ConfigureAwait(false);
            }
        }

        // 掉落物被拾取 / 失效：服务端补发包 21（stack=0）通知客户端移除
        WorldItemEntity[] removedItems;
        lock (world.ItemsLock)
        {
            removedItems = world.Items.Where(i => !i.Active && !i.RemovalNotified).ToArray();
            foreach (var i in removedItems) i.RemovalNotified = true;
        }

        foreach (var item in removedItems)
        {
            await Network.BroadcastAsync(PacketId.ItemDrop,
                new ItemDropPacket(item.ItemId, 0)
                {
                    ItemSlotIndex = item.Slot,
                    Position = item.Position,
                    Velocity = new Vector2(0, 0),
                }, ct).ConfigureAwait(false);
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
            new ServerConfig(), ConfigurationService.JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>注册内置命令（say / who / kick / help）；插件可另注册自己的命令。</summary>
    private static void RegisterBuiltinCommands(CommandService commands, ServerApi server)
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
            Movement: new MovementLimits(c.MaxFlightSpeed, c.TeleportTolerance),
            Combat: new CombatLimits(c.MaxSingleDamage, c.MaxDpsWindowSeconds, c.MaxDps),
            Inventory: new InventoryLimits(c.SscEnabled, c.MaxStackSize),
            World: new WorldLimits(c.MaxTileBreakPerSecond, c.MaxTilePlacePerSecond));
    }
}
