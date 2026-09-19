# TerraAuth

**Terraria 协议的服务端权威代理 / 反作弊框架**

原版 Terraria 是纯客户端权威架构——位置、血量、伤害全部由客户端上报，服务端只做转发。任何 CE / CheatEngine 用户可以直接写内存改坐标、改血量、改伤害，原版服务器几乎没有拦截能力。

TerraAuth 的目标是在**协议层**把关键状态收回服务端，提供可靠的反作弊能力：客户端只能声明意图，所有状态变更必须经过服务端权威校验后才生效。

## 当前进度

| 阶段 | 内容 | 状态 |
|---|---|---|
| Phase 0 | 协议兼容骨架（TCP Server + Framing + 握手） | ✅ 完成 |
| Phase 1 | 玩家权威（HP/MP/位置/速度 + 限速） | ✅ 位置 / 血量 / **法力（包 42，含上限纠正）** / 移动限速均已落地 |
| Phase 2 | 物品 & 战斗权威 | ◐ 物品 ID / 堆叠校验、SSC 背包与**箱子内容的守恒事务**（15 tick 窗口按 (物品, 前缀) 总堆叠守恒提交 / 回滚；开箱期间包 5 与包 32 合并结算）、包 85 快速堆叠由服务端规划；**合成 / 纯消耗已纳入判据**（产物净增量由原版配方表解释，并按合成站与环境校验；只减不增的消耗在背包侧放行）；单次伤害上限 + DPS 窗口；受伤→死亡→复活（`DamagePlayerCommand` / `KillPlayerCommand` / `RespawnCommand`）+ 弹幕命中判定 + 掉落物拾取（包 151 拾取通知 + 包 22 归属同步）均服务端结算；**武器→允许弹幕类型 / 弹药绑定已收紧**（见下「弹幕类型权威」） |
| Phase 3 | 世界权威（tile / 液体 / 抛射物 / 电路） | ◐ tile（挖 / 放）全链路已落地；抛射物 / 掉落物已跟踪并结算命中与拾取；**液体**逐格简化流动 + 混合反应（水 + 岩浆 → 黑曜石等）+ NetLiquid 同步；**电路**线网 / 执行器编辑权威 + 受限 BFS 信号传播；**简化 Boss / 事件**（血月 · 日食 · 入侵 · 击杀进度 + 掉落 → 包 7 广播 / 包 21 掉落） |
| Phase 4 | 一致性 & 性能（快照 + WAL + 断线重连） | ◐ 快照包 15 + 视野裁剪 + 影子预测已实现；**断线宽限期会话保留已落地**（同身份重连续回位置 / 血量 / 增益）；WAL 待定 |
| **Phase 5** | **TCP 传输层（NetworkHost + Connection + 编解码）** | **✅ 完成并实测** |
| Phase 6 | 基础设施（持久化 / 审计 / 监控） | ✅ 配置热重载 / 审计 / Prometheus / 封禁已实装；真实 SQLite 落库（玩家 / 审计 / 封禁）完成 |
| Phase 7 | 红队对抗测试 | ◐ 服务端可自动化部分已落为测试（P1/P2/P3/P4/P5·M6/M1/M3/M4/M9 见 `Phase7-RedTeam/README.md` §零），CE 改内存类仍需手工实验 |
| 扩展 | 插件系统 / Mod 兼容层 / 多线程优化 | ✅ 已整合（Hook 触发写时复制 · 每包零分配） |

> 更细的模块状态与待办：见 [`terraauth/README.md`](terraauth/README.md) §模块实现状态；
> **原版功能覆盖现状（哪些已实现 / 未实现）**：见 [`terraauth/VANILLA_COVERAGE.md`](terraauth/VANILLA_COVERAGE.md)；
> 未实施的优化 / 补全项：见 [`terraauth/OPTIMIZATION_BACKLOG.md`](terraauth/OPTIMIZATION_BACKLOG.md)。

图例：✅ 完成 / ◐ 部分实现 / ⏳ 未实施

## 已实测能力

- ✅ 原版 Terraria 客户端（协议 326）连接 → 握手 → 进入世界 → 正常断开
- ✅ 双客户端同时进服，互相可见（需正确下发包 14 `PlayerActive`）
- ✅ 位置包（包 13）→ 权威校验 → 仿真 → 快照（包 15）经 TCP 下发完整闭环
- ✅ 移动广播：A 发包 13 → B 经 TCP 收到转发，位置一致
- ✅ 移动速度校验：**分轴**判定 —— 水平 `maxSpeed × 60 × Δt + 容差`，垂直 `max(maxSpeed, MaxFallSpeed) × 60 × Δt + 容差`（Δt 钳制在 `[1/60s, 10s]`，不做静默超时无条件放行）
- ✅ 区块流送：包 8 在**游戏内也处理**，并由快照循环按玩家位置补发周边区块（**3×3**，跨区块才发、已发过的区块不重复编码）—— 离开出生点后地形正常
- ✅ 图格推送与原版一致：**少量图格改动**（执行器翻转 / 液体混合）走**包 20（TileSquare）**，**区块级地形**走**包 10（TileSection）**
- ✅ 未建模包边界：未结构化的客户端包默认拒绝，不绕过权威管线即时转发；状态包在仿真提交后由服务端生成同步包
- ✅ Tile（挖 / 放方块）服务端权威全链路：校验 → Command → 仿真 → 增量广播 → SSC 背包扣减
- ✅ 违规处置闭环：窗口内权威拒绝累计达阈值 → 下发包 2 后踢出连接
- ✅ 断线会话保留：断开不销毁运行时，宽限期（`SessionResumeGraceSeconds`，默认 60s）内**同身份重连续回位置 / 血量 / 增益**，出生包下发恢复坐标；连接槽位与并发容量随即释放并复用最小空闲 ID（原版客户端断线即回主菜单，故为「手动重进的会话接管」）
- ✅ 持久化落盘：玩家存档 / 审计 / 封禁写入 SQLite，重启后仍在（封禁不再重启即失效）；**世界改动（挖 / 放 / 液体 / 电线 / 执行器）与箱子内容增量落盘并在启动时回放，玩家建筑与箱子内容重启不丢**
- ✅ 世界文件（`.wld`）：`WorldPath` 指定即可**用真实世界开服**；未指定时按 `WorldSize` **程序化生成三档尺寸**（`Small` 4200×1200 / `Medium` 6400×1800 / `Large` 8400×2400，与原版三档一致），地形为**分层生成**（洞穴 / 矿脉 / 海滩与海水 / 地狱层 / 地下宝箱）；`WorldExportPath` 启用后停机 + 空服导出整份 `.wld`（写出器带写后读回校验 + 原子替换 + `.bak` 滚动）
- ✅ 世界同步与聊天：时间（包 18）/ NPC（包 23）定期下发；聊天（包 82 NetTextModule）可收发，`IServerApi.Broadcast/SendMessage` 真实生效
- ✅ 他人可见性：移动 / 挖放砖 / 受伤 / 增益 / 弹幕 / 掉落物按协议包中继（带玩家字段的包以服务端 ID 覆盖）
- ✅ 战斗 / 生存权威：受伤（117）非负校验 + 服务端生命结算 → 死亡广播（118）；**敌怪 / Boss 接触伤害由服务端判定**（原版整型 AABB、无最小重叠 + 免伤帧 40/20，下发 117 表现 + 16 权威血量）；复活（12）由服务端划定复活点；弹幕命中判定与掉落物拾取（151 拾取通知 + 22 归属同步）均在服务端结算；**法力（42）服务端跟踪 + 上限纠正、治疗（35）回血上限钳制到服务端 HpMax、增益（50）服务端持有列表、弹幕生成（27）类型与伤害上界校验**
- ✅ 世界内容权威：箱子内容服务端持有（开箱下发权威内容 + 包 32 校验落盘）；液体 NetLiquid 逐格流动仿真与编辑校验（同步按视口裁剪）+ 混合反应（异种液体累计 ≥ 24 → 黑曜石 / 蜂蜜块 / 松脆蜂蜜块 / 微光块）；电路线网 / 执行器编辑权威 + 受限 BFS 信号传播 + 图格变更推送；简化 Boss / 事件（血月 · 日食 · 入侵 · 击杀进度 + 按已核对 NPC ID 记录进度并生成掉落，经包 21 补发）
- ✅ 健壮性与运维：区块编码超 `UInt16` 帧上限时自动拆分（不中断登录）；阈值量纲启动校验；服务端命令子系统（say / who / kick / help，插件可扩展）；`/metrics` 支持自定义 Gauge；插件事件查询接持久化审计；Phase 7 对抗清单的服务端可自动化部分已落为测试

## 快速启动

前置：.NET 10 SDK

```bash
cd terraauth
dotnet run --project TerraAuth.csproj -- --config server.json
# 默认监听 127.0.0.1:7777
# Prometheus 指标在 http://127.0.0.1:9090/metrics
```

用原版 Terraria 客户端（协议 326）连接 `127.0.0.1:7777` 即可进入世界。

世界来源由 `server.json` 决定：`WorldPath` 指定 `.wld` 则加载真实世界；为空时按 `WorldSize`（`Small` / `Medium` / `Large`）程序化生成，默认 `Small`。

## 技术栈

| 层 | 技术 |
|---|---|
| 传输 | `System.IO.Pipelines` + `Channel<>` 异步管线，Worker 池处理 |
| 协议 | 原生 Terraria 1.4.5.8（协议 326），位置包 13 / 快照包 15 为核心 |
| 仿真 | 确定性 GameLoop，20Hz 快照频率，Command 模式应用变更 |
| 权威 | `MovementAuthority`（位置超速 + 传送频率）、`PlayerAuthority`（属性只读） |
| 监控 | Prometheus 指标 + 结构化审计日志（SQLite / 内嵌 LiteDb） |
| 语言 | C# 14 + .NET 10 |

## SSC 背包权威（已实施）

### 机制

SSC（`ServerConfig.SscEnabled`，默认开）下服务端持有背包唯一真相，客户端包 5 只被当作**整理意图**，不再无条件覆盖服务端槽位：

- 与服务端权威槽位完全一致 → 视为客户端回显确认，静默接受（不生成写入命令）。
- 不一致 → 不逐包回正，而是暂存进 **15 tick（≈250ms）事务窗口**，窗口到期按 **(物品, 前缀) 总堆叠数守恒**整体提交或回滚。
  - 拖拽 / 交换 / 合并 / 拆分 / 整理都保持总量不变 → 提交（否则玩家无法整理背包）。
  - 凭空造物或销毁必然破坏总量 → 回滚，并把权威值回写客户端。
- 该分流属客户端行为噪声处理，**不计入违规窗口**（避免正常游玩被误踢）。
- 开箱期间的包 5 归入**箱子事务**，与本窗口的包 32 合并按「玩家背包 ∪ 该箱子」守恒结算（否则背包 ↔ 箱子转移会被两侧分别判为不守恒）。
- SSC 关闭时不适用上述权威：客户端走本地背包，包 5 直接写入；`/give` 改为归属掉落物方案。

物品**获得与消耗**一律由服务端可验证事件驱动：世界掉落物拾取（包 151 / 22）、箱子会话内的取放（包 32 / 85）、放置图格与挖掘掉落、弹药消耗、战利品与宝袋奖励（`OpenEyeOfCthulhuTreasureBagCommand`）等均由服务端权威入口改背包，并在提交后落盘 + 下发槽位同步。

### 已知边界

- **合成与纯消耗已纳入守恒判据**：净增量由原版配方表（`terraauth/Simulation/Crafting/RecipeTable.cs`，3301 条，含派生的「墙 → 块 / 平台 → 材料」）解释，并按 `requiredTile` + 水 / 蜂蜜 / 岩浆**校验合成站与环境**（`CraftingEnvironmentSampler`）；「只减不增」的消耗在背包侧放行（箱子侧不放行）。**不校验**雪原 / 墓地 / 特殊种子 / 火把神恩（依赖客户端场景度量与玩家解锁状态）；同一窗口内的多级合成与 13 条静态不可解的 Lesion 家具配方仍会回滚（见 `terraauth/OPTIMIZATION_BACKLOG.md`）。
- 包 85（快速堆叠到附近箱子）**已实现**：客户端上报来源槽位 + smartStack，服务端按当前打开的箱子自行规划装箱，不接受客户端最终快照。
- 包 5 / 包 32 的跨背包-箱子操作已改为**绑定玩家会话 + 箱子索引的守恒事务**；跨玩家同箱并发、包 5/32 乱序或重放、关箱/重开边界、断线重连期间未提交槽位更新等组合仍是持续验证项。

## 弹幕类型权威（已实施）

客户端包 27 携带的 `ProjectileType` 不再被直接信任，服务端按权威上下文推导允许的弹幕：

- 数据表：`Simulation/Combat/WeaponProjectileTypeOf.cs`（武器 → 弹幕，含 `Ammo` 表：弹药 → 弹幕转换）、`WeaponAmmoTypeOf.cs`（武器 → 所需弹药类别）、`AmmoTypeOf.cs`（物品 → 弹药类别）、`WeaponUseBehaviorTable.cs`（`useTime` / `useAnimation` 节奏）。
- `SpawnProjectileCommand` 校验：类型须落在当前权威武器 / 合法弹药允许集合内，远程须消耗服务端背包中匹配 `useAmmo` 的弹药（原子扣减），空手、已知非发射武器、无匹配弹药不得生成普通攻击弹幕。
- 服务端主动开火：`WorldSimulator.SimulateHeldWeaponFire` 按持用状态与 `useTime` / `useAnimation` 节奏生成普通武器弹幕，松开即停；召唤物命中须绑定服务端已登记的召唤实体（归属 / 类型 / 伤害上限 / generation / 命中冷却）。
- 命中侧（包 28）另有 `WorldState.StrikeProjectileMatch` 开关（**生产默认关**）：近战等无弹幕攻击若强制要求弹幕关联会被全拒，故仅按已收录远程 / 固定弹幕魔法启用 `RequiresProjectileForStrike`。

### 已知边界

- 完整自动重复开火、多弹幕、特殊弹药，以及不依赖包 27 的全部服务端主动开火规则仍在补全。
- Minion / Sentry 尚未完成独立的服务端移动、目标选择、攻击与生命周期模拟（当前位置 / 朝向由客户端包 27 上报，销毁由包 29 驱动）。

## 代码结构

```
terraauth/
├─ Authority/           # 权威管线（限流 / 库存 / 移动 / 战斗 / 属性 / 世界 + 分片并行）
│  ├─ AuthoritySubsystems.cs   # 六子系统默认实现
│  ├─ InboundPipeline.cs       # 权威 → Command 转换
│  └─ AuditLogger.cs           # 结构化日志
├─ Simulation/          # 仿真 + GameLoop + Command 模型
│  ├─ WorldSimulator.cs        # 世界状态（六阶段 tick）
│  ├─ CommandQueue.cs          # Move / TileBreak / TilePlace 等
│  └─ World/                   # Tile / TileIdSets / WorldState / .wld 解析
├─ Net/
│  ├─ Snapshots/                  # 快照构造 + 视野裁剪 + 影子预测
│  └─ Transport/                  # TCP 传输 + Framing + 编解码
├─ Protocol/            # Terraria 包 ID 与类型定义
├─ Config/              # 阈值配置 + FileSystemWatcher 热重载
├─ Persistence/         # 持久化（默认 SQLite / 可降级内嵌 LiteDb）
├─ Monitoring/          # Prometheus 指标 + /metrics 端点
├─ Security/            # 封禁（滑动窗口 + 存储）
├─ Plugins/             # 插件系统（Hook / 加载器 / 管线装饰）
├─ ModCompat/           # 未来 MOD 兼容层（当前生产禁用）
├─ Concurrency/         # 并行优化（Worker 池 / 分片 / 快照并行）
├─ Tests/               # xUnit 验收测试（779 用例，以 dotnet test 实测为准）
└─ server.json          # 阈值配置
```

## 移动权威判定

```
allowedX = MaxSpeed × 60 × Δt + TeleportTolerance
allowedY = max(MaxSpeed, MaxFallSpeed) × 60 × Δt + TeleportTolerance
Δt = ClampDt(now - state.LastSeenAt)   // 钳制 [1/60, 10] 秒
if (|dx| > allowedX || |dy| > allowedY) → Reject("speed_exceeded")，拒绝时不更新权威基准
```

- **分轴判定**：水平用 `MaxSpeed = MaxFlightSpeed = 8.0`（像素/帧）；**垂直用 `max(MaxSpeed, MaxFallSpeed)`**
  （`MaxFallSpeed = 20.0`）。原版下落终速约 20 px/帧，若垂直也按 8 判定，**任何一次正常坠落都会被误判超速**
  → 服务端位置不再更新（后续挖 / 放 / 交互全部 out_of_reach）且累计违规被踢（默认 10 次 / 60 分钟）。
- `TeleportTolerance = 4` 像素；长时静默（>10s）一律按 10s 计（水平上限 `4804px`），**不做无条件放行**（防穿墙 / 瞬移）
- **已知限制**：客户端失焦时位置包间隔可达 4~7s（客户端降频），**水平**静默位移过大仍可能被误判；
  彻底解决需服务端权威移动 / 碰撞校验（Phase 3 世界权威落地后补齐）

## 许可

本项目仅为学习 / 研究用途，与 Re-Logic 官方无关。Terraria 协议实现参考社区规范。
