# TerraAuth — 服务端权威反作弊框架

> **目标**：在不修改原版 Terraria 客户端的前提下，用重写的服务端实现服务端权威（Server Authority），
> 将"CE 改内存"类作弊做到**架构性失效**（L4 天花板，详见 [`architecture.md`](architecture.md)）。
>
> **当前状态**：Phase 1-7 骨架 + Phase 6 完整实装 + **插件系统 / Mod 兼容层 / 多线程优化**（已整合）；
> 原版 Terraria 客户端已实测连接成功（协议协商 → 进入世界）。

---

## 一、项目结构（实际目录）

```
terraauth/
├── architecture.md          # 架构文档（分层、模块、协议映射、验收 KPI）
├── PROJECT_STRUCTURE.md     # 合并后项目结构文档（目录树 / 工程配置 / 模块状态）
├── TerraAuth.sln            # 解决方案（1 主工程 + 1 测试）
├── TerraAuth.csproj         # ★ 单一工程：收拢全部分层源码（Protocol/Authority/Simulation/Net/...）
├── GameHost.cs              # ★ 组装根：Bootstrap() 一键初始化全部模块
├── Program.cs               # 可执行入口（--config / --db / --port / --metrics-port）
├── CoreAdapter.cs           # 核心类型 ↔ 插件/Mod 接口桥接
├── server.json              # 运行配置（端口 / DB / ModPolicy）
├── verify.py                # 静态校验脚本（无 SDK 降级方案）
│
├── Protocol/                # 协议层：PacketId、包契约、类型
├── Authority/               # Phase 2：权威层（六子系统 + 管线）
├── Simulation/              # Phase 3：仿真层（GameLoop 确定性 + Command + WorldSimulator）
│   └── World/               #   Tile / TileMap / TileIdSets / WorldState / WorldFileReader(.wld)
├── Net/                      # Phase 4(快照) + Phase 5(网络宿主)
│   ├── Phase4/               #   Snapshot / SnapshotBroadcaster / ClientPredictor
│   └── Phase5/               #   NetworkHost / Framing / PacketDecoder / PacketEncoder
├── Config/                   # 配置（JSON + 热重载）
├── Persistence/              # 持久化（SQLite / 内嵌 LiteDb 双模式）
├── Monitoring/               # 监控（Prometheus + /metrics）
├── Security/                 # 封禁（滑动窗口 + 持久化）
│
├── Plugins/                  # ★ 插件系统（需求 1：预留 Hook + 说明文档）
├── ModCompat/                # ★ Mod 兼容层（需求 2：Mod 服务器支持）
├── Concurrency/              # ★ 多线程优化（多 CPU 线程）
│
├── Tests/                    # ★ 验收测试（xUnit，7 组覆盖全链路）
│   └── TerraAuth.Tests.csproj
├── Phase6-Infrastructure/    # 基础设施层设计文档（实现落在 Config/Persistence/Monitoring/Security）
└── Phase7-RedTeam/           # 红队对抗测试手册（M/P/R 清单）
```

### 项目依赖关系

```
TerraAuth.csproj（单工程 / 单程序集，OutputType=Exe）
  └── 分层以命名空间隔离（不再拆分子工程）：
        Protocol · Authority · Simulation · Net · Plugins · ModCompat
        · Concurrency · Config / Persistence / Monitoring / Security

TerraAuth.Tests ──▶ TerraAuth.csproj（同程序集，经 InternalsVisibleTo 访问 internal）
```

### 模块实现状态

> 图例：**已实现** = 可用且有测试覆盖；**部分** = 主流程可用，仍有 TODO；**未实现** = 仅接口 / 文档 / 占位；**文档** = 仅设计说明，无对应代码目录。

| 模块 | 状态 | 说明 / 待办 |
|------|------|-------------|
| `Protocol/` | 部分 | 已定义 `PacketId` 与 1/2/3/4/6/7/8/9/12/49/129 等包契约；其余 200+ 包未定义 |
| `Authority/` | 已实现 | 六子系统（Inventory/Movement/Combat/Player/World/Rate）+ `InboundPipeline` 阶段链 |
| `Simulation/`（核心） | 已实现 | `GameLoop` 固定步长 / `CommandQueue` / `SnapshotStore` / `EventRecorder` / 确定性 RNG |
| `Simulation/World/` | 部分 | `Tile`/`TileMap`、`TileIdSets`（含 `tileFrameImportant`）、`WorldState`、`.wld` 解析器、包 10 `TileSection` 编码均已实现 |
| `Simulation/WorldSimulator` | 部分 | 主循环 / 快照已实现；`SimulateAi/Physics/Combat/World` 为空 TODO |
| `Net/Phase4` | 部分 | 快照广播框架 + `BuildDelta`（实体提取 / 增量 / `Removed` / xxHash32 校验和）+ `SubmitInputs` Command 生成 + `ShadowPredictor` 影子预测（输入重放/速度钳制/偏差阈值）+ 每玩家分桶（`BuildFrameFor`）+ 视野裁剪（`ViewportRadius`）已实现 |
| `Net/Phase5`（协议） | 部分 | `Framing` / `Connection` / 握手链（1→3、6→7、8→9/10/49、12→129）已实现 |
| `Net/Phase5` `PacketEncoder` | 部分 | 已实现 3/7/9/8/10/12/49/129/13/2；包 10 `TileSection`（Deflate + 位标志 + RLE + 尾部列表）、包 15 `Snapshot`（BaseTick/Tick/Checksum/实体/Removed）已实现 |
| `Net/Phase5` `PacketDecoder` | 部分 | 已解析 28 个入站包（握手链 + 权威白名单 10 包 + 伤害/死亡/传送等）+ 包 15 `Snapshot`；其余统一 `UnknownPacket` 透传 |
| `Net/Phase5` `NetworkHost` | 部分 | 握手已实现；包 8 请求按出生点矩形逐块下发包 10；包 7 仍用 `DefaultWorldInfo` 占位 |
| `Config/` | 已实现 | `ServerConfig` + `FileSystemWatcher` 热重载 |
| `Persistence/` | 部分 | 内嵌 `LiteDbPersistence` 可用（默认路径：`TerraAuth.csproj` 未定义 `USE_SQLITE`）；真实 `SqliteImpl` 为骨架（SQL 省略） |
| `Monitoring/` | 已实现 | Prometheus Counter/Gauge/Histogram + `HttpListener` `/metrics` |
| `Security/` | 已实现 | `BanManager` 滑动窗口 + `SqliteBanStore` |
| `Plugins/` | 已实现 | `HookRegistry` / `PluginLoader` / `HookedPipeline` 全链路 |
| `ModCompat/` | 部分 | 策略 / 检测框架已实现；TModLoader 握手与 ModNet 解析为 TODO |
| `Concurrency/` | 部分 | `WorkerPool` / `ShardedAuthorityProcessor` / `ParallelSnapshotBroadcaster` 已接入管线与快照广播；`DoubleBufferedWorldState` 骨架待接入仿真 |
| `Tests/` | 部分 | 7 组验收测试（122 用例通过）；真实 TCP 往返集成测试（包 13 → 快照包 15 / 双客户端包 13 转发）、包 10 / 包 15 编解码回归已补，`.wld` 解析测试待补 |
| `Phase6-Infrastructure/` | 文档 | 仅设计说明，实现见 `Config/Persistence/Monitoring/Security` |
| `Phase7-RedTeam/` | 文档 | 对抗测试手册（M/P/R 清单），尚未执行 |

---

## 二、需求 1：插件系统（预留 Hook + 说明文档）

| 文件 | 内容 |
|------|------|
| `Plugins/IPlugin.cs` | `IPlugin` / `PluginBase` / `HookResult` / `HookResultType` |
| `Plugins/IPluginContext.cs` | `IPluginContext` / `IServerApi` / `ILogger` 适配器 |
| `Plugins/HookArgs.cs` | 全部 Hook 参数类型（Player/Combat/World/Server/Economy） |
| `Plugins/HookRegistry.cs` | 线程安全注册表、优先级排序、Deny 短路、Modified 传递 |
| `Plugins/PluginLoader.cs` | DLL 反射加载、依赖拓扑排序、热重载 |
| `Plugins/HookIntegration.cs` | `HookedPipeline` 装饰器 —— 权威管线插入 Hook 的接入点 |
| `CoreAdapter.cs`（根目录） | 核心类型 ↔ 插件接口桥接 |
| `Plugins/README.md` | **插件开发指南**（Hook 列表 / 示例 / 生命周期 / 最佳实践） |
| `Tests/PluginModTests.cs` | Hook 注册/触发/Deny/Modify/卸载 验证 |

**接入方式**：`GameHost.Bootstrap()` 自动初始化 `HookRegistry` → `PluginLoader`，并用
`HookedPipeline` 装饰原始管线。新增插件只需：
1. 创建类库项目实现 `IPlugin`
2. 编译 DLL 放入 `plugins/` 目录
3. 重启服务端（或调用热重载）

---

## 三、需求 2：Mod 兼容层

| 文件 | 内容 |
|------|------|
| `ModCompat/ModPolicy.cs` | `ModPolicy` / `IModDetector` + 默认实现 / `ICustomPacketHandler` |
| `ModCompat/README.md` | **Mod 兼容指南**（策略配置 / TModLoader 握手 / 插件中的 Mod 支持） |
| （集成于 `GameHost.Bootstrap`） | `ModDetector` / `CustomPackets` 注入 |

**支持矩阵**：
- ✅ 原版客户端（`VanillaOnly`）
- ✅ TModLoader（自动检测 + Mod 白/黑名单 + 自定义包 250-255）
- ✅ 自定义 Mod 客户端（扩展点 `IModDetector`）
- ⚠️ 原版客户端不实现预测协议 → 延迟感只能通过快照频率缓解（**不影响防作弊 L4**）

**策略模式**：`VanillaOnly` / `Whitelist` / `Blacklist` / `AllowAll`，配置于 `server.json` 的 `ModPolicy` 节。

---

## 四、多线程优化

| 文件 | 内容 |
|------|------|
| `Concurrency/ParallelConfig.cs` | `ParallelConfig` / `AtomicCounter` / `MpscQueue` / 读写锁原语 |
| `Concurrency/ParallelWorkers.cs` | `WorkerPool` / `DoubleBufferedWorldState` / `ShardedAuthorityProcessor` / `ParallelSnapshotBroadcaster` |
| `Concurrency/README.md` | **并行优化分析**（并行边界 / 线程模型 / 收益预估 / 风险 / 执行优先级） |
| `Tests/ConcurrencyTests.cs` | 各并行组件 + 确定性校验 |

**已接入**：`GameHost` 实例化 `WorkerPool` + `ParallelConfig`；`HookedPipeline → ShardedInboundPipeline → InboundPipeline`（按玩家分片并行、同玩家保序）；`ParallelSnapshotBroadcaster` 接入 `SnapshotBroadcaster`；插件 Hook 触发通过 `HookRegistry`（线程安全）。
**待推进（P3+）**：把 `DoubleBufferedWorldState` 替换 `WorldSimulator` 状态引用。

---

## 五、快速开始

### 构建

```bash
cd terraauth
dotnet restore
dotnet build -c Release
```

> 沙盒环境若无 dotnet SDK / 无法联网，用 `python3 verify.py` 做静态校验。

### 运行

```bash
dotnet run --project TerraAuth.csproj -- --config server.json --port 7777
```

### 测试

```bash
dotnet test -c Release
```

### 静态校验（无 SDK 降级方案）

```bash
python3 verify.py
```

---

## 六、agent 执行优先级

| 优先级 | 任务 | 收益 | 风险 |
|--------|------|------|------|
| **P0** | ✅ 网络 I/O 与解码分离（`WorkerPool` 接入 `NetworkHost`） | 高 | 低 |
| **P0** | ✅ 快照序列化并行（`ParallelSnapshotBroadcaster` 接入 `SnapshotBroadcaster`） | 高 | 低 |
| **P0** | ✅ 持久化异步化（`SqlitePersistence` 审计走无界 `Channel` + 批量落盘） | 中 | 低 |
| **P1** | ✅ 插件系统联调（`Examples/TerraAuth.ExamplePlugins` 示例插件 + `HookedPipeline` 端到端链路） | 中 | 低 |
| **P2** | ✅ 权威校验分片（`ShardedInboundPipeline` 接入管线，按玩家分片并行、同玩家保序） | 高 | 中 |
| **P2** | 双缓冲 WorldState | 高 | 中 |
| **P2** | Mod 握手实装（TModLoader Mod 列表解析） | 中 | 中 |
| **P4** | 空间分区世界仿真 | 极高 | 高 |

每个 Phase 结束须跑 **`Tests/` 验收测试** + **`Phase7-RedTeam/` 对抗清单**，详见各模块 README。

---

## 七、验收标准（架构 §8）

| 指标 | 目标 |
|------|------|
| 内存修改类阻断率 | ≥ 99% |
| 合法操作误判率 | < 0.1% |
| 洪水攻击下 CPU | < 200% |
| 确定性（同输入 → 同状态） | 100% 一致 |

---

## 八、防作弊成熟度

```
L1 裸奔 → L2 阈值检测(TShock) → L3 SSC → L4 深度服务端重算 ← 本项目目标
                                        ↘ L5 L4+ML 行为分析
L6 客户端+服务端联合（需改客户端，违反本项目约束，不可达）
```

> **终极目标**：让作弊成本远高于收益，而非追求 100%。详见 `architecture.md` 与 `Phase7-RedTeam/`。
