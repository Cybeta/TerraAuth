# TerraAuth 项目结构文档（合并后）

> 本文档描述 **8 个源码工程合并为单一工程** 之后的实际结构。
> 分层保留在目录与命名空间层面，工程层面只剩「1 个主工程 + 1 个测试工程 + 1 个示例插件工程」。

---

## 一、工程概览

| 工程 | 路径 | 类型 | 说明 |
|------|------|------|------|
| `TerraAuth` | `TerraAuth.csproj` | Exe（net8.0） | **唯一主工程**：收拢全部分层源码 |
| `TerraAuth.Tests` | `Tests/TerraAuth.Tests.csproj` | xUnit 测试库 | 独立保留，经 `ProjectReference` 引用主工程 |
| `TerraAuth.ExamplePlugins` | `Examples/TerraAuth.ExamplePlugins/TerraAuth.ExamplePlugins.csproj` | 插件类库 | 示例插件（`WelcomePlugin` / `AntiCheatLitePlugin`），构建后自动复制到主工程 `bin/<Config>/net8.0/plugins/` |

解决方案 [TerraAuth.sln](TerraAuth.sln) 包含上述 3 个工程。

**合并前**（9 工程）：`TerraAuth.Protocol` · `TerraAuth.Authority` · `TerraAuth.Simulation` · `TerraAuth.Net` · `TerraAuth.Plugins` · `TerraAuth.ModCompat` · `TerraAuth.Concurrency` · `TerraAuth.Core` + `TerraAuth.Tests`
**合并后**：源码工程全部并入根目录 `TerraAuth.csproj`，目录结构不变，分层改由命名空间隔离。

---

## 二、主工程配置（`TerraAuth.csproj`）

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `TargetFramework` | `net8.0` | 统一目标框架 |
| `OutputType` | `Exe` | 可执行（入口 `Program.cs`） |
| `RootNamespace` | `TerraAuth` | 根命名空间 |
| `Nullable` | `enable` | 可空引用类型 |
| `ImplicitUsings` | `enable` | 隐式 using |
| `EnableDefaultCompileItems` | `false` | 关闭默认 glob，改用显式 glob |
| `Compile` | `**\*.cs`（`Exclude="Tests\**;bin\**;obj\**"`） | 递归收拢所有源码，排除测试与构建产物 |
| `PackageReference` | `Microsoft.Data.Sqlite 8.0.0` | 条件：`'$(NoSqlite)' != 'true'`，离线时用内嵌 LiteDb |
| `PackageReference` | `System.IO.Pipelines 8.0.0` | `PipeReader` 分帧 |
| `InternalsVisibleTo` | `TerraAuth.Tests` | 测试可访问 `internal`（原分散在 Simulation/Net，现集中一处） |

**测试工程配置**：`Microsoft.NET.Test.Sdk 17.8.0` + `xunit 2.9.2` + `xunit.runner.visualstudio 2.8.2`，显式 `Compile` 列表（7 个测试文件），单一 `ProjectReference` → `..\TerraAuth.csproj`。

---

## 三、完整目录树

```
terraauth/
├── TerraAuth.sln                 # 解决方案（1 主工程 + 1 测试 + 1 示例插件）
├── TerraAuth.csproj              # ★ 单一主工程（Exe / net8.0）
├── Program.cs                    # 可执行入口：参数解析 → GameHost.Bootstrap → RunAsync
├── GameHost.cs                   # ★ 组装根：初始化全部模块 + 仿真/网络/快照三循环
├── CoreAdapter.cs                # 核心类型 ↔ 插件/Mod 接口桥接（namespace TerraAuth.Plugins）
├── server.json                   # 运行配置（端口 / DB / ModPolicy 等）
├── verify.py                     # 无 SDK 环境静态校验脚本
├── README.md                     # 项目说明 + 进度
├── architecture.md               # 架构文档（分层 / 模块 / 协议映射 / 验收 KPI）
├── .gitignore
│
├── Protocol/                     # 协议层          ── namespace TerraAuth.Protocol
│   ├── PacketId.cs               #   包 ID 常量（真实协议 ID = 326）
│   ├── Packets.cs                #   包契约（连接 / 玩家信息等）
│   └── Types.cs                  #   INetworkPacket / Vector2 等基础类型
│
├── Authority/                    # Phase 2 权威层   ── namespace TerraAuth.Authority
│   ├── IAuthorityLayer.cs        #   决策模型 + 6 接口
│   ├── InboundPipeline.cs        #   入站管线（阶段链）
│   ├── ShardedInboundPipeline.cs #   分片装饰器（单包→按玩家分片并行）
│   ├── AuthorityEnforcers.cs     #   校验执行器
│   ├── AuthoritySubsystems.cs    #   六子系统默认实现（Inventory/Movement/Combat/Player/World/Rate）
│   ├── AuditLogger.cs            #   审计实现
│   └── IAuditLogger.cs
│
├── Simulation/                   # Phase 3 仿真层   ── namespace TerraAuth.Simulation
│   ├── GameLoop.cs               #   固定步长循环
│   ├── WorldSimulator.cs         #   六阶段 tick（partial）
│   ├── WorldSimulator.Snapshot.cs#   快照桥接
│   ├── WorldEntityView.cs        #   每 tick 不可变实体视图（仿真→快照跨线程发布）
│   ├── CommandQueue.cs           #   命令模型
│   ├── SnapshotStore.cs          #   快照环形存储
│   ├── SnapshotFrame.cs          #   快照帧
│   ├── EventRecorder.cs          #   事件溯源
│   ├── Determinism.cs            #   IRng + 确定性
│   └── World/                    #   世界数据模型
│       ├── Tile.cs               #     Tile / TileMap
│       ├── TileIdSets.cs         #     瓦片 ID 集合（FrameImportant 等）
│       ├── WorldState.cs         #     世界状态
│       ├── SectionLocks.cs       #     区块分区锁（图格读写互斥）
│       └── WorldFileReader.cs    #     .wld 解析器
│
├── Net/                          # Phase 4/5 网络层 ── namespace TerraAuth.Net.Phase4 / Phase5
│   ├── Phase4/
│   │   ├── Snapshot.cs           #   快照广播 + 客户端影子预测
│   │   └── README.md
│   └── Phase5/
│       ├── ITerrariaProtocol.cs  #   协议抽象
│       ├── Framing.cs            #   PipeReader 分帧
│       ├── PacketEncoder.cs      #   出站编码
│       ├── PacketDecoder.cs      #   入站解码
│       ├── Connection.cs         #   单连接状态机（Handshake→Playing→Disconnected）
│       ├── ConnectionManager.cs  #   连接池 / 超时 / 并发上限
│       ├── ISnapshotSender.cs    #   出站快照抽象
│       ├── NetworkHost.cs        # ★ TcpListener + Accept + 管线调度
│       ├── README.md
│       └── DELIVERY.md
│
├── Config/                       # 配置            ── namespace TerraAuth.Config
│   ├── IConfigurationService.cs
│   ├── ConfigurationService.cs   #   JSON + FileSystemWatcher 热重载
│   └── ServerConfig.cs
│
├── Persistence/                  # 持久化          ── namespace TerraAuth.Persistence
│   ├── IPersistence.cs
│   ├── SqlitePersistence.cs      #   SqliteImpl（骨架）/ LiteDbPersistence（内嵌可用）
│   └── PersistenceAuditLogger.cs
│
├── Monitoring/                   # 监控            ── namespace TerraAuth.Monitoring
│   ├── IMetrics.cs
│   └── PrometheusMetrics.cs      #   Counter/Gauge/Histogram + HttpListener /metrics
│
├── Security/                     # 封禁            ── namespace TerraAuth.Security
│   ├── IBanManager.cs
│   ├── BanManager.cs             #   滑动窗口封禁
│   ├── PlayerIdentity.cs         #   连接槽位 ↔ 封禁 Guid 的统一映射
│   └── SqliteBanStore.cs
│
├── Plugins/                      # 插件系统         ── namespace TerraAuth.Plugins
│   ├── IPlugin.cs                #   IPlugin / PluginBase / HookResult
│   ├── IPluginContext.cs         #   IPluginContext / IServerApi / ILogger
│   ├── HookArgs.cs               #   全部 Hook 参数类型
│   ├── HookRegistry.cs           #   线程安全注册表 / 优先级 / Deny 短路
│   ├── PluginLoader.cs           #   DLL 反射加载 / 拓扑排序 / 热重载
│   ├── HookIntegration.cs        #   HookedPipeline 装饰器
│   └── README.md                 #   插件开发指南
│
├── ModCompat/                    # Mod 兼容层       ── namespace TerraAuth.ModCompat
│   ├── ModPolicy.cs              #   ModPolicy / IModDetector / ICustomPacketHandler
│   └── README.md
│
├── Concurrency/                  # 并行优化         ── namespace TerraAuth.Concurrency
│   ├── ParallelConfig.cs         #   ParallelConfig / AtomicCounter / MpscQueue / 读写锁
│   ├── ParallelWorkers.cs        #   WorkerPool / DoubleBufferedWorldState / ShardedAuthorityProcessor / ParallelSnapshotBroadcaster
│   └── README.md
│
├── Tests/                        # 独立测试工程      ── namespace TerraAuth.Tests
│   ├── TerraAuth.Tests.csproj    #   xUnit 工程（引用主工程）
│   ├── AuthorityTests.cs         #   Phase 2
│   ├── SimulationTests.cs        #   Phase 3
│   ├── SnapshotTests.cs          #   Phase 4
│   ├── NetworkTests.cs           #   Phase 5
│   ├── IntegrationTests.cs       #   端到端
│   ├── PluginModTests.cs         #   插件 / Mod
│   └── ConcurrencyTests.cs       #   并行组件
│
├── Examples/                     # 示例插件工程      ── namespace TerraAuth.ExamplePlugins
│   └── TerraAuth.ExamplePlugins/
│       ├── TerraAuth.ExamplePlugins.csproj
│       ├── WelcomePlugin.cs      #   生命周期 Hook（ServerStarted / PlayerJoined / PlayerLeft）
│       └── AntiCheatLitePlugin.cs#   NpcStrike Deny 短路 + Metrics 上报
│
├── Phase6-Infrastructure/        # 设计文档（实现落在 Config/Persistence/Monitoring/Security）
│   └── README.md
└── Phase7-RedTeam/               # 红队对抗测试手册（M/P/R 清单）
    └── README.md
```

---

## 四、命名空间分层

合并后各层不再由工程边界约束，改由 **命名空间** 隔离（无跨层类型名冲突）：

```
TerraAuth                     ← 组合根（Program / GameHost）
├── TerraAuth.Protocol        ← 协议契约与基础类型
├── TerraAuth.Authority       ← 权威层
├── TerraAuth.Simulation      ← 仿真层（含 World/ 世界模型）
├── TerraAuth.Net.Phase4      ← 快照广播 / 客户端预测
├── TerraAuth.Net.Phase5      ← 网络宿主 / 编解码 / 分帧
├── TerraAuth.Plugins         ← 插件系统（含根目录 CoreAdapter.cs）
├── TerraAuth.ModCompat       ← Mod 兼容层
├── TerraAuth.Concurrency     ← 并行基础设施
├── TerraAuth.Config          ← 配置
├── TerraAuth.Persistence     ← 持久化
├── TerraAuth.Monitoring      ← 监控
├── TerraAuth.Security        ← 封禁
└── TerraAuth.Tests           ← 验收测试（独立程序集）
```

依赖方向（逻辑上）：`Protocol → Simulation → Authority → Net → GameHost`；扩展层 `Plugins / ModCompat / Concurrency` 横切；`Config / Persistence / Monitoring / Security` 为基础设施。

---

## 五、模块实现状态

> 图例：**已实现** = 可用且有测试覆盖；**部分** = 主流程可用，仍有 TODO；**未实现** = 仅接口 / 文档 / 占位；**文档** = 仅设计说明。

| 模块 | 状态 | 说明 / 待办 |
|------|------|-------------|
| `Protocol/` | 部分 | 已定义 33 个 `PacketId` 常量与对应包契约（握手链 + 权威白名单 + 快照包 15 等）；原版 200+ 包按需补充 |
| `Authority/` | 已实现 | 六子系统 + `InboundPipeline` 阶段链 |
| `Simulation/`（核心） | 已实现 | `GameLoop` / `CommandQueue` / `SnapshotStore` / `EventRecorder` / 确定性 RNG |
| `Simulation/World/` | 部分 | `Tile`/`TileMap`、`TileIdSets`（含 `tileFrameImportant`）、`WorldState`、`.wld` 解析、包 10 `TileSection` 编码均已实现 |
| `Simulation/WorldSimulator` | 已实现 | 六阶段 tick 全部落地：AI（城镇 NPC 确定性游走）/ 物理（重力 + 图格碰撞 + 边界钳制）/ 战斗（下落伤害结算）/ 世界（昼夜 + 月相推进）；为简化模型，非原版全量物理 |
| `Net/Phase4` | 部分 | 快照广播框架 + `BuildDelta`（实体提取 / 增量 / `Removed` / xxHash32 校验和）+ `SubmitInputs` Command 生成 + `ShadowPredictor` 影子预测（输入重放/速度钳制/偏差阈值）+ 每玩家分桶（`BuildFrameFor`）+ 视野裁剪（`ViewportRadius`）已实现 |
| `Net/Phase5`（协议） | 部分 | `Framing` / `Connection` / 握手链已实现 |
| `Net/Phase5` `PacketEncoder` | 部分 | 已实现 28 类出站包（握手链 3/7/9/8/10/12/49/129 + 2/4/5/13/14/16/17/21/27/28/31/32/35/36/50/65/73/79/117/118）；包 10 `TileSection`（Deflate + 位标志 + RLE + 尾部列表）、包 15 `Snapshot`（BaseTick/Tick/Checksum/实体/Removed）已实现 |
| `Net/Phase5` `PacketDecoder` | 部分 | 已解析 28 个入站包（握手链 + 权威白名单 10 包 + 伤害/死亡/传送等）+ 包 15 `Snapshot`；其余统一 `UnknownPacket` 透传 |
| `Net/Phase5` `NetworkHost` | 部分 | 握手已实现；包 8 请求按出生点矩形逐块下发包 10；包 7 下发真实世界元数据（`WorldState.ToWorldInfoPacket`）；纠正包按自身类型下发；权威拒绝在窗口内累计达阈值 → 踢出连接（先发包 2 再关闭） |
| `Config/` | 已实现 | `ServerConfig` + `FileSystemWatcher` 热重载 |
| `Persistence/` | 部分 | 内嵌 `LiteDbPersistence` 可用；真实 `SqliteImpl` 为骨架 |
| `Monitoring/` | 已实现 | Prometheus Counter/Gauge/Histogram + `/metrics` |
| `Security/` | 已实现 | `BanManager` 滑动窗口 + `SqliteBanStore` + `PlayerIdentity`（连接槽位 ↔ 封禁 Guid 的统一映射） |
| `Plugins/` | 已实现 | `HookRegistry` / `PluginLoader` / `HookedPipeline` 全链路（Hook 参数已填充包数据）；`IServerApi` 已实装踢出 / 封禁 / 在线玩家查询 / 服务器信息（`Broadcast` / `SendMessage` / `ExecuteCommand` 待文本包与命令子系统，当前仅落审计）；示例插件见 `Examples/TerraAuth.ExamplePlugins/` |
| `ModCompat/` | 部分 | 策略 / 检测框架已实现；TModLoader 握手与 ModNet 解析为 TODO |
| `Concurrency/` | 部分 | `WorkerPool` / `ShardedAuthorityProcessor` / `ParallelSnapshotBroadcaster` 已接入管线与快照广播；`DoubleBufferedWorldState` 已接入仿真→快照（发布不可变 `WorldEntityView`）；`SectionLocks` 区块分区锁已接入图格读写；并行区块仿真待 P4（前提见模块 README） |
| `Tests/` | 部分 | 7 组验收测试（138 用例通过）；包 10 / 包 15 编解码回归、`WorldGenerator` 确定性测试、踢出与违规阈值触发、纠正包类型、插件 API 踢出与封禁、Hook 参数填充包数据、配置阈值启动映射与热重载、实体视图发布、区块分区锁并发安全（真实 TCP）已补，`.wld` 解析测试待补 |
| `Phase6-Infrastructure/` | 文档 | 仅设计说明，实现见 `Config/Persistence/Monitoring/Security` |
| `Phase7-RedTeam/` | 文档 | 对抗测试手册（M/P/R 清单），尚未执行 |

---

## 六、构建 / 运行 / 测试

```bash
# 构建（主工程）
dotnet build TerraAuth.csproj

# 离线 / 无 SQLite 环境（降级到内嵌 LiteDb）
dotnet build TerraAuth.csproj -p:NoSqlite=true

# 运行
dotnet run --project TerraAuth.csproj -- --config server.json --port 7777

# 测试（138 用例）
dotnet test Tests/TerraAuth.Tests.csproj

# 无 SDK 环境静态校验（大括号平衡 / ProjectReference 路径 / 接口实现 / TODO 统计）
python3 verify.py
```
