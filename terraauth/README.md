# TerraAuth — 服务端权威反作弊框架

> **目标**：在不修改原版 Terraria 客户端的前提下，用重写的服务端实现服务端权威（Server Authority），
> 将"CE 改内存"类作弊做到**架构性失效**（L4 天花板，详见 [`architecture.md`](architecture.md)）。
>
> **当前状态**：Phase 1-7 骨架 + Phase 6 完整实装 + 插件系统与多线程优化；当前生产版本仅支持原版 Terraria 客户端（协议 326）。MOD / TModLoader / 自定义包暂不支持，未来单独立项。
> + **Tile（挖 / 放方块）服务端权威全链路**（解码 → 权威校验 → Command → 仿真 → 增量广播 → SSC 背包扣减）
> + **战斗 / 生存权威**（受伤→死亡→复活的 `DamagePlayerCommand` / `KillPlayerCommand` / `RespawnCommand`、
>   弹幕命中判定、掉落物拾取（包 151 拾取通知 + 包 22 归属同步）+ SSC 入库、复活点由服务端划定；
>   法力（包 42）服务端跟踪 + 纠正、治疗（包 35）上限钳制、增益（包 50）服务端持有、弹幕生成（包 27）类型 / 伤害校验）
> + **世界内容权威**（箱子内容服务端持有（包 31/32/34）、液体 NetLiquid 编辑与逐格流动 + 混合反应、电路线网 / 执行器编辑
>   + 受限 BFS 信号传播、简化 Boss / 事件状态机（血月 · 日食 · 入侵 · Boss 击杀进度 + 掉落 → 包 7 广播 / 包 21 掉落））
> + **违规处置闭环**（权威拒绝累计达阈值 → 发包 2 踢出连接）
> + **断线宽限期会话保留**（断开不销毁运行时，宽限期内同身份重连续回位置 / 血量 / 增益；连接槽位随即回收并复用最小空闲 ID）
> + **热路径优化**（Hook 触发写时复制 · 每包零分配；`System.Threading.Lock`；`FrozenSet`）；
> 运行时 **.NET 10**（`net10.0`），原版 Terraria 客户端已实测连接成功（协议协商 → 进入世界）。

---

## 一、项目结构（实际目录）

```
terraauth/
├── architecture.md          # 架构文档（分层、模块、协议映射、验收 KPI）
├── PROJECT_STRUCTURE.md     # 合并后项目结构文档（目录树 / 工程配置 / 模块状态）
├── OPTIMIZATION_BACKLOG.md  # 优化待办（独立立项 / 后续可能优化 / 本轮回溯）
├── VANILLA_COVERAGE.md      # 原版功能覆盖与验证矩阵（原版可玩性现状）
├── TerraAuth.sln            # 解决方案（1 主工程 + 1 测试 + 1 示例插件）
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
│   └── World/               #   Tile / TileMap / TileIdSets / WorldState / WorldGenerator
│                            #   / WorldFileReader / WorldFileWriter(.wld 读写) / SectionLocks
├── Net/                      # Snapshots（快照） + Transport（网络宿主）
│   ├── Snapshots/               #   Snapshot / SnapshotBroadcaster / ClientPredictor
│   └── Transport/               #   NetworkHost / Framing / PacketDecoder / PacketEncoder
├── Config/                   # 配置（JSON + 热重载）
├── Persistence/              # 持久化（SQLite / 内嵌 LiteDb 双模式）
├── Monitoring/               # 监控（Prometheus + /metrics）
├── Security/                 # 封禁（滑动窗口 + 持久化）
│
├── Plugins/                  # ★ 插件系统（需求 1：预留 Hook + 说明文档）
├── ModCompat/                # 未来 MOD 兼容层（当前生产路径禁用）
├── Concurrency/              # ★ 多线程优化（多 CPU 线程）
│
├── Examples/                 # ★ 示例插件工程（WelcomePlugin / AntiCheatLitePlugin）
│   └── TerraAuth.ExamplePlugins/
├── Tests/                    # ★ 验收测试（xUnit，9 组覆盖全链路）
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
TerraAuth.ExamplePlugins ──▶ TerraAuth.csproj（编译期引用 Private=false，构建后 DLL 复制到服务端 plugins/）
```

### 模块实现状态

> 图例：**已实现** = 可用且有测试覆盖；**部分** = 主流程可用，仍有 TODO；**未实现** = 仅接口 / 文档 / 占位；**文档** = 仅设计说明，无对应代码目录。

| 模块 | 状态 | 说明 / 待办 |
|------|------|-------------|
| `Protocol/` | 部分 | 已定义 41 个 `PacketId` 常量与对应包契约（握手链 + 权威白名单 + 快照包 15 等）；原版 200+ 包按需补充 |
| `Authority/` | 已实现 | 六子系统（Inventory/Movement/Combat/Player/World/Rate）+ `InboundPipeline` 阶段链 |
| `Simulation/`（核心） | 已实现 | `GameLoop` 固定步长 / `CommandQueue` / `SnapshotStore` / `EventRecorder` / 确定性 RNG |
| `Simulation/World/` | 部分 | `Tile`/`TileMap`、`TileIdSets`（含 `tileFrameImportant`）、`WorldState`、`.wld` **解析器 + 写出器**（写出器与读取器布局严格对称，带写后读回校验 / 原子替换 / `.bak` 滚动；解析含最小合法世界 / 版本 / 魔数 / footer 测试）、包 10 `TileSection` 与包 20 `TileSquare` 编码均已实现；**程序化生成支持原版三档尺寸**（小 4200×1200 / 中 6400×1800 / 大 8400×2400，`ServerConfig.WorldSize`）并生成**分层地形**（噪声地表 / 洞穴 / 按深度分带矿脉 / 海滩与海水 / 地狱层 / 2×2 宝箱+战利品），层高与图格 ID 经协议行为验证；仍为简化的分层生成（无树木 / 生命水晶 / 生物群系 / 结构体） |
| `Simulation/WorldSimulator` | 已实现 | 六阶段 tick + 扩展阶段全部落地：AI（城镇 NPC 游走 / 敌怪 / 入侵怪 / Boss 追击）/ 物理（重力 + 图格碰撞 + 边界钳制）/ 战斗（下落伤害 + 弹幕命中判定 → 敌怪扣血、Boss 击杀记进度 + 生成掉落、玩家死亡置死亡态）/ 世界（昼夜 + 月相 + 简化事件：血月 · 日食）/ 实体（掉落物重力落地、弹幕积分与生存期）/ 液体（逐格简化流动 + 混合反应，下发按视口裁剪）/ 电路（`ActuateCommand` 沿电线受限 BFS 翻转执行器 + 图格变更推送）；为简化模型，非原版全量物理 |
| `Net/Snapshots` | 部分 | 快照广播框架 + `BuildDelta`（实体提取 / 增量 / `Removed` / xxHash32 校验和）+ `SubmitInputs` Command 生成 + `ShadowPredictor` 影子预测（输入重放/速度钳制/偏差阈值）+ 每玩家分桶（`BuildFrameFor`）+ 视野裁剪（`ViewportRadius`）已实现 |
| `Net/Transport`（协议） | 部分 | `Framing` / `Connection` / 握手链（1→3、6→7、8→9/10/49、12→129）已实现 |
| `Net/Transport` `PacketEncoder` | 部分 | 已实现 **37 类出站包**（握手链 2/3/4/7/8/9/10/12/49/129 + 权威与状态 5/13/14/16/17/18/20/21/22/23/27/28/29/31/32/34/35/36/42/50/65/73/79/117/118 + 包 82 的 NetText / NetLiquid 模块 + 包 15 `Snapshot`）；包 10 `TileSection`（Deflate + 位标志 + RLE + 尾部列表）、**包 20 `TileSquare`（未压缩小矩形 + 逐格位标志/可选段）**、包 13 可选尾随段（挂载 / 回城 / 相机）已实现 |
| `Net/Transport` `PacketDecoder` | 部分 | 已解析 **35 个入站包**（握手链 + 权威白名单 13 包 + 拾取 / 箱子 / 伤害 / 死亡 / 治疗 / 法力 / 增益 / 传送 / 时间 / NPC / 聊天与液体模块等）+ 包 15 `Snapshot`；其余统一 `UnknownPacket`，由 Vanilla-only Authority 默认拒绝 |
| `Net/Transport` `NetworkHost` | 部分 | 握手已实现；包 8 请求按出生点矩形逐块下发包 10，且**在 Playing 阶段也处理**；**按玩家位置流送区块**（3×3，跨区块才补发、下发前先发包 9）；包 7 下发真实世界元数据；纠正包按自身类型下发；权威拒绝在窗口内累计达阈值 → 踢出连接（先发包 2 再关闭；**未建模包等「行为噪声」拒绝不计入**）；**未建模包按 `PacketId` 统计**，供真机测试后决定中继 / 建模优先级；**断线走宽限期会话保留并在连接结束时回收槽位**；未知包默认拒绝，状态包不走即时中继 |
| `Config/` | 已实现 | `ServerConfig`（反作弊阈值唯一来源 + `ModPolicy` 节 + `WorldPath` / `WorldSize`（小 / 中 / 大三档）/ `WorldExportPath`）+ `FileSystemWatcher` 热重载，阈值热更新直接推送至已构造的权威子系统（无需重启）；枚举以字符串读写 |
| `Persistence/` | 已实现 | 真实 SQLite（`SqliteImpl`，默认后端）五表落盘：玩家 / 审计 / 封禁 / **WorldTiles（世界改动）** / **WorldChests（箱子内容）**；审计按批单事务写入，停机时冲刷通道残留。`-p:NoSqlite=true` 可降级到内嵌 `LiteDbPersistence`（JSON，同样五类数据落盘） |
| `Monitoring/` | 已实现 | Prometheus Counter/Gauge/Histogram + `HttpListener` `/metrics`（`SetGauge` 支持插件自定义指标名与标签） |
| `Security/` | 已实现 | `BanManager` 滑动窗口 + `SqliteBanStore`（封禁落盘，重启后仍生效）+ `PlayerIdentity`（连接槽位 ↔ 封禁 Guid 的统一映射） |
| `Plugins/` | 已实现 | `HookRegistry` / `PluginLoader` / `HookedPipeline` 全链路（Hook 参数已填充包数据，插件可按 Damage / 方块坐标等真实值决策）；注册表采用**写时复制快照**，触发路径**零锁零分配**（无订阅者时不构造 `HookArgs`）；`IServerApi` 已实装踢出 / 封禁 / 在线玩家查询 / 服务器信息 / `ExecuteCommand`（经 `Authority/CommandService.cs` 分发，内置 say / who / kick / help；`Broadcast` / `SendMessage` 经包 82（NetTextModule）真实下发）；`EventStore.QueryAsync` 已接持久化审计查询 |
| `ModCompat/` | 暂停 | 未来兼容层代码保留但默认禁用；当前生产不注册 250-255、不接受 TModLoader 握手、不透传自定义或未知包，MOD 后续单独立项 |
| `Concurrency/` | 部分 | `WorkerPool` / `ShardedAuthorityProcessor` / `ParallelSnapshotBroadcaster` 已接入管线与快照广播；`DoubleBufferedWorldState` 已接入仿真→快照（发布不可变 `WorldEntityView`）；`SectionLocks` 区块分区锁已接入图格读写；并行区块仿真待 P4（前提见模块 README） |
| `Tests/` | 部分 | 9 组验收测试（329 用例通过）；其中 `VanillaFeatureTests` 用**真实权威管线 + 真实 TCP** 逐项验证原版功能（登录链 / 外观广播 / 移动 / 挖放砖 / 背包 / 战斗（含服务端接触伤害与免伤帧）/ 传送（65·73）/ 血量纠正 / 法力跟踪与纠正 / 治疗上限钳制 / 增益服务端持有 / 弹幕生成校验 / 受伤→死亡→复活 / 掉落物拾取（含背包满保留 · 并发只成功一次）/ 弹幕命中 / 箱子内容（含持久化重启存活 · 会话代数隔离 · 关箱与离开距离关闭会话） / 液体（含视口裁剪与混合反应）/ 电路（含图格推送）/ Boss·事件（含掉落与已核对 ID 映射）/ 高熵区块拆分 / **Phase 7 对抗自动化** / 断线广播 / **断线会话保留（宽限期内同身份重连续回位置 / 血量，且换发新会话标识）** / **区块流送（离开出生点后地形）** / **未建模包拒绝** / 他人可见性中继 / 时间与 NPC 同步 / 聊天），覆盖矩阵见 [`VANILLA_COVERAGE.md`](VANILLA_COVERAGE.md)；`WorldFileTests` 覆盖 `.wld` 解析（最小合法世界 + 版本 / 魔数 / footer 拒绝路径）；另有真实 TCP 往返集成测试（含**旧连接实例踢出 / 移除不得影响复用槽位的新连接**）、配置阈值启动映射与热重载（含 `ModPolicy` 字符串枚举与 Int16 量纲校验）、命令子系统、指标导出、插件事件查询、实体视图发布、区块分区锁与包 10 编码并发安全、Hook 参数填充包数据、持久化往返（玩家 / 审计 / 封禁重启读回）、包 10 / 包 15 / 新增包编解码回归、`WorldGenerator` 确定性测试 |
| `Phase6-Infrastructure/` | 文档 | 仅设计说明，实现见 `Config/Persistence/Monitoring/Security` |
| `Phase7-RedTeam/` | 部分 | 对抗测试手册（M/P/R 清单）+ 服务端可自动化部分已落为测试（见其 README §零） |

---

## 二、需求 1：插件系统（预留 Hook + 说明文档）

| 文件 | 内容 |
|------|------|
| `Plugins/IPlugin.cs` | `IPlugin` / `PluginBase` / `HookResult` / `HookResultType` |
| `Plugins/IPluginContext.cs` | `IPluginContext` / `IServerApi` / `ILogger` 适配器 |
| `Plugins/HookArgs.cs` | 全部 Hook 参数类型（Player/Combat/World/Server/Economy） |
| `Plugins/HookRegistry.cs` | 线程安全注册表（**写时复制不可变快照**，触发端零锁零分配）、优先级排序、Deny 短路、Modified 传递 |
| `Plugins/PluginLoader.cs` | DLL 反射加载、依赖拓扑排序、热重载 |
| `Plugins/HookIntegration.cs` | `HookedPipeline` 装饰器 —— 权威管线插入 Hook 的接入点；**Hook 参数已填充包数据**（NpcId/Damage、方块坐标、抛射物、物品）与玩家名；**无订阅者时提前短路**（不构造 HookArgs，每包零分配） |
| `CoreAdapter.cs`（根目录） | 核心类型 ↔ 插件接口桥接；`ServerApi` 实装踢出 / 封禁 / 在线玩家查询 / 服务器信息 |
| `Plugins/README.md` | **插件开发指南**（Hook 列表 / 示例 / 生命周期 / 最佳实践） |
| `Tests/PluginModTests.cs` | Hook 注册/触发/Deny/Modify/卸载 验证 |

**接入方式**：`GameHost.Bootstrap()` 自动初始化 `HookRegistry` → `PluginLoader`，并用
`HookedPipeline` 装饰原始管线。新增插件只需：
1. 创建类库项目实现 `IPlugin`
2. 编译 DLL 放入 `plugins/` 目录
3. 重启服务端（或调用热重载）

---

## 三、未来 MOD 兼容层（当前生产禁用）

`ModCompat` 代码仅作为未来独立立项的兼容层保留。当前生产版本为 Vanilla-only：

- 仅支持原版 Terraria 客户端（协议 326）；
- 不接受 TModLoader 握手；
- 不注册 250-255 自定义包；
- 不转发自定义或未知包；
- 未知包默认由 Authority 拒绝。

未来 MOD 兼容工作必须单独完成协议、权限、限流、审计和测试设计，不能绕过当前的权威提交与服务端最终状态同步链路。

---

## 四、多线程优化

| 文件 | 内容 |
|------|------|
| `Concurrency/ParallelConfig.cs` | `ParallelConfig` / `AtomicCounter` / `MpscQueue` / 读写锁原语 |
| `Concurrency/ParallelWorkers.cs` | `WorkerPool` / `DoubleBufferedWorldState` / `ShardedAuthorityProcessor` / `ParallelSnapshotBroadcaster` |
| `Simulation/WorldEntityView.cs` | 每 tick 不可变实体视图（仿真线程发布 → 快照线程只读） |
| `Concurrency/README.md` | **并行优化分析**（并行边界 / 线程模型 / 收益预估 / 风险 / 执行优先级） |
| `Tests/ConcurrencyTests.cs` | 各并行组件 + 确定性校验 |

**已接入**：`GameHost` 实例化 `WorkerPool` + `ParallelConfig`；`HookedPipeline → ShardedInboundPipeline → InboundPipeline`（按玩家分片并行、同玩家保序）；`ParallelSnapshotBroadcaster` 接入 `SnapshotBroadcaster`；插件 Hook 触发通过 `HookRegistry`（写时复制快照，**读端零锁零分配**）；`DoubleBufferedWorldState` 接入 `WorldSimulator` → `SnapshotBroadcaster`（快照线程只读已发布视图，不再触碰活动 `WorldState`）；`SectionLocks` 区块分区锁接入图格读写（仿真写 vs 包 10 编码/权威校验读）。
**P4 进展**：已实装「区块分区锁」；「并行区块仿真」暂缓（`MaxConnections=64` 使收益场景不成立、仿真非瓶颈、且并行前须先做每实体确定性 RNG 流）—— 详见 [`Concurrency/README.md`](Concurrency/README.md) §六。

---

## 五、快速开始

### 构建

> 需要 **.NET 10 SDK**（三个工程统一目标框架 `net10.0`）。

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
| **P2** | ✅ 双缓冲 WorldState（发布不可变实体视图，快照线程不再读活动 WorldState） | 高 | 中 |
| **P2** | ⏳ MOD 兼容层暂缓（当前 Vanilla-only；未来单独立项） | 中 | 中 |
| **P4** | 空间分区世界仿真（区块分区锁 ✅ 已实装 / 并行区块仿真暂缓，见 `Concurrency/README.md` §六） | 极高 | 高 |

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
