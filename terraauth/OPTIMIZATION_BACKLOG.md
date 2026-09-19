# TerraAuth — 优化待办（Backlog）

> 记录**尚未实施**的优化 / 补全事项，供后续排期取舍。已实施项见文末「本轮回溯」。
> 最后更新：2026-09-19（第三十九轮：合成 / 消耗配方表补齐 —— SSC 守恒最后一个硬缺口；795/795 通过）

---

## 真机联调缺陷汇总（第二十二 ~ 三十二轮）

> 本段汇总**原版客户端真机进图**后暴露并已修复的缺陷，供追溯；逐轮细节见文末「本轮回溯」。

| # | 真机症状 | 根因 | 修复 | 轮次 |
|---|---|---|---|---|
| 1 | 怪物移动大面积卡顿 | NPC 同步只有 1Hz；AI 每 4 tick 才步进 0.25px | 同步提到 20Hz（后为 60Hz）；AI 每 tick 步进 | 22 |
| 2 | 向导卡在土里 | 生成 Y 语义错；城镇 NPC 不走重力/落地 | 城镇 NPC 走共用物理步（`StepNpcPhysics`） | 22 / 24 |
| 3 | 史莱姆不跳（贴地滑行） | 用 `VelocityY == 0` 当"贴地"判据（悬空生成的敌怪不落体）；贴地仍积分重力把等待期清零 | 改用碰撞结果 `WorldNpc.Grounded`；贴地等待期直接 return | 23 |
| 4 | 未建模包导致误踢 | 未建模包被计入违规窗口 | 未建模包只统计、不计违规 | 21 |
| 5 | 向导陷地 + 怪物抖动/"像加速" | 位置口径与客户端不一致（原版 `position` = 碰撞盒左上角、脚底 = `+height`，我们按 21 高建模）；落地时**连水平速度一起清零** | 逐类型碰撞盒表 `NpcSizes`（按原版 `SetDefaults` 核对）+ 脚底 = `Y + height` + 落地不清零水平速度 | 27 |
| 6 | 站着/走路莫名掉血；被史莱姆打刷 `speed_exceeded` | 玩家脚底按 `Y + 21` 判 → 永不判定落地 → `FallDistance` 持续累积；匀速上限过紧，击退/被挤出方块被拒后级联 | 玩家脚底 = `Y + 42`；落地判左右两列；移动权威加 ×4「可疑带」；上报 Y 未变即清空下落累计 | 27 / 28 |
| 7 | 向导一直跳 | 障碍探针探了"脚下的支撑行"（平地前方本来就有地面 → 每 tick 都算障碍） | 改探身体所在行（`supportRow - 1`） | 28 |
| 8 | 没碰到却扣血（第二次） | NPC 重力/终速与原版不一致（`0.4/16` vs 原版 `0.3/10`）→ 跳跃/下落弧线与客户端发散 | 物理常数对齐原版 `UpdateNPC_UpdateGravity` | 29 |
| 9 | 没碰到却扣血（第三次） | 2px 擦边被判成接触；客户端上报包 117 与服务端接触**各自结算**（双倍伤害） | 接触最小重叠；包 117 纳入统一免伤帧 | 30 |
| 10 | "一次都没碰到"却扣血 | 原版客户端**只在操作变化时**发位置包，而原版服务端会自行模拟玩家；我们不自模拟 → NPC 追/打 1 秒前的旧坐标 | 观测速度 + 位置外推（`AimPosition`）；NPC 同步提到 **60Hz** | 31 |
| 11 | 史莱姆一直朝一个方向走 | NPC 物理**没有水平碰撞** → 服务端 NPC 穿墙直线前进，与客户端发散 | 加 `NpcBlockedHorizontally` 水平阻挡 | 32 |
| 12 | 擦着走仍扣血 | 3~4px 贴边算接触 | 接触阈值收到 **8px**（半格） | 32 |

> 后续修正：第 12 条的 8px 阈值、第 9 条的免伤帧都已在 **第三十五轮**回归原版 —— 8px 移除（改整型 AABB、无最小重叠）；
> 接触免伤帧由 30 改为 40（30 是盾牌弹反分支的误取）；NPC 同步由 60Hz 改为分档（普通 20Hz / Boss 与近身 60Hz）。

---

## 一、独立立项

### W-1 支持加载真实 `.wld` 世界 —— ✅ 已实施（第十轮）

`ServerConfig.WorldPath` 指定世界文件即可开服（为空仍程序化生成）；并新增 `.wld` **写出器**与导出开关
（`WorldExportPath` / `WorldExportIntervalSeconds`，停机 + 空服导出）。详见第十轮回溯。

**遗留**：写出文件已通过**逐格 round-trip** + **严格分段走查** + **原版服务端实测加载**（第二十轮）；
**双向 `.wld` 格式互操作性均已确认**（导出 → 原版；原版 → TerraAuth）；原版客户端已完成连接、进图及 `/give` 掉落物拾取验证，完整游玩回归仍待验证。

---

## 二、后续可能优化（暂不实施）

> 以下均为**性能向**优化，收益需实测支撑；未确认瓶颈前不建议实施。

### B-5 原版物理补全（弹幕 / 掉落物）

- **位置**：`Simulation/WorldSimulator.cs`（`SimulateEntities`）
- **现状**：弹幕为直线积分 + 生存期到期；掉落物为重力 + 简单落地；命中判定为「与敌怪中心距离 < 32px」。
- **缺口**：无图格碰撞（弹幕穿墙）、无穿透 / 反弹 / 追踪等 ai 行为、无掉落物拾取动画与合并。
- **说明**：原版对应数据量极大，属**玩法补全**而非性能优化；当前实现已满足「服务端权威判定」目标。

### B-2 出站帧缓冲复用（`Connection.SendEncodedAsync`）

- **位置**：`Net/Transport/Connection.cs`
- **现状**：每次发送都 `new ArrayBufferWriter<byte>()` 再 `WrittenSpan.ToArray()` → 每帧两次分配；
  快照下发为 20Hz × 在线玩家数。
- **方向**：改用 `ArrayPool<byte>` 租借 + 精确长度写入（或复用单个 writer）。
- **风险（中）**：缓冲经 `Channel` 跨线程移交给写循环，归还时机与生命周期必须严格配对，
  否则易出现「已归还缓冲仍被读」类缺陷。
- **触发条件**：GC / 分配采样显示 `ArrayBufferWriter` 为热点。

  **结论（2026-09，GC 采样基线已跑）**：非热点，**不实施**。以快照高频主路径建模（20Hz × 20 玩家 × 4 分包 = 1600 包/秒，见测试 `Snapshot_EncodePath_Allocation_Sample`）测分配 ≈ **1.45 MB/s**（每包 ~950 B 分配，包体仅 82 B）。相对现代 .NET gen0 GC 吞吐量级可忽略（<1%），且 ArrayPool 跨线程归还属中风险、收益极小。留测试作可复现基线；若未来快照包体显著增大 / 更高在线数再复核。

### B-3 SnapshotStore 环形缓冲（消除 O(n) 移除）

- **位置**：`Simulation/SnapshotStore.cs`
- **现状**：容量 300；`Add` 超容量时走 `List.RemoveAt(0)`，`TrimBefore` 走 `RemoveRange(0, n)`，均为 O(n) 搬移。
- **量级**：每 tick 约 2.4KB memmove，60Hz ≈ 144KB/s —— **可忽略**，故暂不实施。
- **方向**：以「写入下标 + 逻辑长度」的环形缓冲替代 `List`，把移除降为 O(1)。
- **关联**：`Net/Snapshots/README.md` 已记录 `Snapshot()` 每轮 `ToArray()` 复制的问题，可与本项合并评估。

### B-4 区块锁原语评估（`SectionLocks`）

- **位置**：`Simulation/World/SectionLocks.cs`
- **现状**：按 64 条带使用 `ReaderWriterLockSlim`（仿真写 vs 包 10 编码 / 权威校验读，读写分离）。
- **结论**：.NET 10 框架**未提供**更优的读写锁替代（`System.Threading.Lock` 仅适用于互斥场景），故**保持现状**。
- **触发条件**：若实测条带竞争成为瓶颈，再评估条带数调优或按 section 细化粒度。

---

## 三、第十七轮：架构与权威链路检查（现状：部分已修正）

本轮为静态架构检查记录。**第十八 ~ 十九轮已核对代码并修正其中若干项**，下表按当前代码实际状态重新标注：

### 已修正（第十八轮，已与代码核对）

1. **命令顺序**：`CommandQueue` 已改为 `(Tick, Sequence)` **优先队列**；`DrainThrough(tick)` 只取到期命令，未来 Tick 不再阻塞已到期命令。
2. **连接失败传播**：出站改为**有界** `Channel`（容量 2048，`FullMode = Wait`）；读写循环联动取消并观察双方异常；写入 / `Flush` 失败经 `OutboundFrame.Flushed` 传递给等待者，不再被标记为成功。
3. **液体输入限额**：液体模块在**分配列表之前**校验条目上限（128）与 payload 剩余长度（`PacketDecoder.DecodeNetModule`）。
4. **未知包边界**：生产为 Vanilla-only 白名单，`UnknownPacket` 默认**拒绝**，不做即时中继 / 原样透传（Mod 兼容层不得绕过）；
   该拒绝**不计入违规窗口**并按 `PacketId` 统计（第二十一轮，见 §附），供真机测试后决定「中继 / 建模」优先级。

### 部分修正

5. **背包与世界状态原子提交**：放砖库存扣减、拾取（同一提交内入库 + 失效掉落物）、箱子写入（打开会话 + 在线 / 距离复核）均已在**仿真提交阶段**结算；仍需扩展到更多实体转移与提交事件。

### 仍待修正（风险保留）

6. **提交前广播**：客户端原包仍可能在仿真提交前广播，其他客户端会先观察到未被权威状态确认的结果；广播应绑定已提交状态 → **本会话核实：当前实现无「客户端原包即时中继」路径**。`WorldSimulator.Tick` 先 `ApplyCommandsForTick` 提交命令，再产出 Snapshot 并 `Publish` 实体视图（L163-164）；`NetworkHost` 标记「从服务端最终状态生成原版同步包，不存在客户端原包即时中继路径」（L358）；`GameHost` 备注「客户端上报位置不直接中继，避免在 Apply 前看到未提交状态」（L656）。出站帧均源自已提交态，本项不再构成当前缺陷。
7. **背压**：出站已有界；**入站 `CommandQueue`、分片入站队列与审计 `Channel` 的容量与过载策略** → **本项已落地**：出入站 / 分片入站 / 审计 channel 均已改有界；`CommandQueue` 新增可配置 `MaxCount`，超限入队返回 false 由管线拒绝该操作（防恶意超大未来 tick 堆积），生产以 8192 接线。
8. **锁契约不统一**：`WorldState` 的锁契约（持锁范围 / 可重入性 / 快照一致性）与调用方假设不一致，存在竞态与死锁风险 → **本会话核实：当前无锁顺序死锁**。`KillSummonedProjectiles` 对 `ProjectilesLock` 与 `PlayersLock` 为**顺序获取**（两个独立 lock 块，非嵌套）；`MarkPlayerOffline`/命令路径均在锁外调用它；全仓无「先 PlayersLock 再 ProjectilesLock」的反向嵌套。契约仍以 public 锁对象暴露，属可加固点（收敛为私有 + 封装方法），非当前已触发缺陷。
9. **WorkerPool 设计分裂**：两套 WorkerPool 的调度、生命周期与错误处理语义不一致，应收敛为单一模型 → **本会话收敛**：`WorkerPool` 统一为「节流 + Task.Run」单一执行契约——`EnqueueAsync` 返回的 Task 现在等待工作真正完成并传播异常（此前立即返回，仅靠测试里的 500ms 延时掩盖，属语义缺陷）；移除从未被进队的 `_queue`/从未使用的 `_workers` 死字段。快照广播 / 分片权威校验与 WorkerPool 沿用同一并发纪律。
10. **配置链路不完整**：缺少配置项到消费者的完整映射表；部分运行时参数可能无法进入实际组件，或热重载后不生效 → **本会话已核实并修掉一处死配置**：逐字段对照生产消费者（`GameHost` 组合根 + `AuthorityThresholds.From`）。除 `MaxWalkSpeed` 外均被消费——其从未进入 `MovementLimits`（生产用 `MaxFlightSpeed` 单一覆盖步行/冲刺以降低误判，见 `AuthorityThresholds.From` 注释），属死配置字段，已从 `ServerConfig`/`Validate` 移除。另已接线 `MetricsEnabled/MetricsPort`（条件创建 `MetricsHttpServer`）与 `HandshakeTimeoutSeconds`（握手看门狗）。
11. **快照缓冲与批量限制未落地**：`SnapshotConfig.MaxEntitiesPerPacket` 分包与 `SnapshotStore` 环形缓冲**均已落地**（见 §附「下一步建议」第 3 项）；本项已完成。
12. **协议元数据与分层约束分散**：包元数据分散多处，单程序集结构无法有效约束分层依赖与包契约边界 → **本会话评估后决定搁置**：协议侧元数据已在 Protocol 程序集内收敛（`PacketId` 唯一来源 / `Packets.cs` 集中 / `Types.cs` 共享）。当前 `Simulation↔Net↔Authority` 依赖有环、尚非干净单向分层，直接切程序集需先破环、破坏性大且无当前功能缺陷；单作者 + 命名空间分层已够表达意图。**暂不拆**；若日后依赖真正单向化，再按 v1.0 设计基线回退拆分。

> 结论：第 1~5、7、11 项已落地；第 6、8~10 项经本会话核实/收敛/修活；第 12 项评估为专项架构迁移（仍保留）。综上当前已无未决的功能性配置/广播/并发缺陷，仅余 §12 单程序集分层强制这一架构取舍。

---

## 附：本轮回溯

### 第三十九轮（2026-09-19）：合成 / 消耗配方表 —— SSC 守恒最后一个硬缺口

**一、根因**

- 守恒判据只要求 **(物品, 前缀) 总堆叠数不变**：拖拽 / 整理 / 拆分 / 交换满足，但**合成**（材料减少 + 产物增加）
  与**消耗**（药水 / 投掷物 / 丢弃）必然改变总量 → 被当成「凭空造物」整窗回滚，真机表现为**合成后物品被吃掉**；
  「丢弃物品」同理（包 21 已生成世界掉落物，清空槽位的包 5 被回滚会留下复制缺口）。

**二、配方表（新增 `Simulation/Crafting/RecipeTable.cs`，自动生成）**

- **数据源**：Terraria 1.4.5.8 原版 `Recipe.SetupRecipes` 及其家具 / 雕像辅助方法
  （`decompiled/src/Terraria/Recipe.cs`）；生成脚本 `decompiled-tmp/gen-recipes.ps1`。
- **产出**：**3193 条配方 + 34 个配方组**（产物 / 产物堆叠 / 材料需求，含配方组需求）。
- **抽取覆盖面**：`SetupRecipes` 逐条语句（含 `for` 字面量循环、`SetIngredients`、局部变量表达式、
  `ItemID.Sets.TextureCopyLoad[i]` 与内联二维数组）；字面量家具表直接扫体；
  `AddStandardFurnitureSetRecipes` / `AddCritterStatueRecipe` 两个模板按调用点实参在脚本内复刻。
- **不猜数值**：无法静态求值的语句使该条配方**整体作废**（绝不落半条 —— 缺材料的配方等于凭空造物漏洞），
  共 13 条作废；反向墙 / 平台配方（依赖 `Item.createWall` / `TileID.Sets.Platforms` 运行时图格数据）整体未收录。

**三、判据升级（`Simulation/Crafting/CraftingConservation.cs`）**

- 快路径：逐 (物品, 前缀) 完全一致 → 守恒（与原实现一致）。
- 否则把差异拆成「减少」与「增加」：
  - 带**非 0 前缀**的「增加」一律拒绝（原版合成产物恒为 0 前缀，防止把普通物品「洗」成传奇前缀）；
  - **没有增加**（纯减少）→ **背包侧放行**（= 消耗 / 丢弃），**箱子侧不放行**
    （否则客户端可用包 32 静默清空他人箱子内容）；
  - **有增加** → 必须由配方解释：净增量须能被单次产出堆叠整除，材料从「减少」池按配方扣减
    （配方组按组内任意成员合计），箱子侧还要求「减少」全部被解释（防顺手销毁箱内物品）。
- **消耗上限 = 窗口开始时的权威总量**（新增 `PlayerRuntime.InventoryTransactionBaseline`，窗口开启时取基准）：
  窗口期内服务端外部塞入的物品（`/give`、拾取、开袋）不在基准内，客户端「清空该槽」的暂存意图会超出上限
  → 保留既有「与外部变更冲突 → 回滚」语义（`InventoryTransaction_Conflicting_With_External_Change_RollsBack`）。
- 开箱期间的包 5 归入箱子事务，故「取用已打开箱子里的材料合成」同样被解释（箱侧与背包侧一起结算）。

**四、已知边界（均落在安全侧：宁回滚不放过）**

- 只校验「材料 → 产物」守恒，**不校验合成站 / 环境条件**（工作台、砧、水 / 岩浆等）。
- 同一窗口内的**多级合成**（先合成中间物、再立刻用它合成成品，中间物净变化为 0）不被解释；窗口仅 15 tick。

**测试**：**795 / 795 通过**（新增 16 例：背包合成提交 / 纯消耗放行 / 材料不足回滚 / 带前缀造物回滚、
箱子侧「取箱内材料合成」提交 / 箱子侧纯消耗回滚，以及判据纯函数 6 例 + 配方表抽取自检 1 例；
真机链路 3 例经 TCP → 管线 → 事务窗口验证）。

### 第三十八轮（2026-09-19）：SSC 背包 / 箱子守恒事务 + 宝袋解耦 + 包 40 + 召唤命中凭据

**一、SSC 背包从「逐包回正」改为「守恒事务」（客户端能正常整理背包了）**

- **根因**：SSC 下客户端拖拽 / 整理 / 拆分 / 合并 / 交换会在一个窗口内发出多个包 5（源槽清空 + 目标槽填入），
  逐包与权威值比对回正会把合法操作**全部撤销** —— 表现为玩家无法整理背包；而反过来若全盘接受，
  又无法区分「合法整理」与「凭空造物」。
- **修复**：包 5 与服务端权威值不一致时不即时回正，而是暂存进 **15 tick（≈250ms）事务窗口**
  （`PlayerRuntime.PendingInventoryChanges` + `StageInventorySlotCommand`）；窗口到期由
  `WorldState.TryCommitInventoryTransaction` 做**守恒校验**：对每个 **(物品, 前缀)** 叠加暂存值后的**总堆叠数**
  必须与当前权威总量一致 —— 拖拽 / 整理 / 拆分 / 合并 / 交换都保持总量不变 → **提交**（接受客户端整理结果）；
  凭空造物或销毁必然破坏总量 → **回滚**并回写权威值。外部变更（`/give`、拾取、开袋、箱子转移）天然使总量对不上，
  故与外部冲突的暂存意图自然回滚，无需版本号或冲突表。
- 完全一致的包 5（客户端回显确认）走 `RejectSilent()`，不生成写入命令。
- 该分流属客户端行为噪声，**不计违规**（否则正常游玩会被误踢）；对应用例
  `Vanilla_ClientIsNotKicked_After_NormalInventoryOps`、`Vanilla_InventoryDrag_Is_Accepted_And_Conserved`、
  `AntiCheat_Ssc_Forged_InventorySnapshot_Is_Rolled_Back`。
- **注意**：守恒判据按 (物品, 前缀) 的**总堆叠数**，不能用 (物品, 前缀, 堆叠) 多重集 —— 后者会让「拆分」被判不守恒。

**二、箱子守恒事务 + 背包 ↔ 箱子转移原子化 + 包 85 入站**

- 包 32（SyncChestItem）同样改为窗口聚合（`StageChestItemCommand` + `WorldState.TryCommitChestTransaction`），
  守恒基准是**「玩家权威背包 ∪ 该箱子」**的 (物品, 前缀) 总堆叠：箱内整理 / 交换守恒即提交，凭空造物即回滚。
- **背包 ↔ 箱子单槽转移必须两侧同一事务**：原版同时发包 5（背包侧清空）+ 包 32（箱子侧填入），若各走各的事务，
  背包侧会因总量减少被判不守恒回滚、箱子侧因总量增加被判不守恒回滚 → **存入箱子永远不生效**。
  修复：开箱期间包 5 归入箱子事务（`PendingChestInventoryChanges` + `WorldState.EnsureChestTransaction`），
  提交 / 回滚时背包侧与箱子侧一起落盘并一起回写。
- **包 85（QuickStackChests）入站**：解码 `Int32 槽位数 + Int16×N 槽位 + Boolean smartStack`
  （`QuickStackChestsPacket`）→ 权威层解析玩家当前打开的箱子 → 终端阶段生成 `BulkInventoryChestCommand`，
  由服务端**自行规划**装箱（不接受客户端最终快照），`SourceSlots` 限定只处理客户端指定的来源槽位。
- 对应用例：`Vanilla_ChestItem_Rearrange_Is_Conserved_And_Committed`、
  `Vanilla_ChestDeposit_FromInventory_Is_Accepted_And_Conserved`、`Vanilla_ChestQuickStack_IsAccepted`、
  `AntiCheat_Ssc_Forged_ChestSnapshot_Is_Rolled_Back`。

**三、宝藏袋解耦（移除全背包 pending 静默屏障）**

- **根因**：早期为「开袋后客户端回显本地奖励快照」建立了一层全背包静默屏障，导致正常玩家的其它槽位操作被静默丢弃
  （背包看起来「卡住」），且开袋与背包权威强耦合。
- **修复**：客户端清空袋槽（包 5 且该槽原为宝袋 3319）直接转换为权威开袋命令
  （`OpenEyeOfCthulhuTreasureBagCommand`，服务端随机奖励并**原子**写入权威背包；重复触发由命令内再校验幂等）；
  不再建立任何全背包屏障 —— 客户端随后上报的奖励快照按常规守恒事务收敛。
  对应用例 `Vanilla_TreasureBag_DoesNotSilenceOtherSlots`。

**四、包 40（SyncTalkNPC）补齐**

- 前序版本只把包 40 当作未建模包拒绝，导致「玩家与城镇 NPC 对话」状态不会中继给其他玩家（他人看不到对话气泡指向）。
- 修复：协议 `SyncTalkNpcPacket`（`Byte PlayerId + Int16 talkNPC`，-1 = 未对话）+ 编解码 + 权威边界校验
  （越界按 `sync` 类审计、**不计违规**）+ `SetTalkNpcCommand`（目标非活跃 / 非城镇 NPC 一律回落 -1，状态变化才标记）
  + `GameHost.FlushPlayerTalkNpcAsync` 向**其他玩家**广播。
  测试：`SetTalkNpc_Tracks_State_And_Marks_Only_On_Change`、`SyncTalkNpc_RoundTrips_In_Codec`、
  `Vanilla_TalkNpc_Is_Tracked_Server_Side_And_Relayed_To_Others`、`Vanilla_Invalid_TalkNpc_Is_Rejected_Without_Violation`。

**五、召唤物命中凭据修复（收尾 `debug-summon-hp-resync` 调试记录）**

- **症状**：召唤物撞击史莱姆后服务端日志显示 NPC 生命下降，但客户端血条瞬间回到 25（血条回满）。
- **证据链**：客户端创建 BabySlime（弹幕类型 266）只发一次包 27，之后持续发包 28 而无同 Key 的位置更新；
  旧实现要求「召唤物缓存位置与 NPC 当前碰撞盒重叠」，而弹幕位置冻结在创建点 → **所有命中都记 `summon-miss`**，
  服务端生命从未下降，客户端先显示预测伤害、随后收到服务端权威生命 25 → 表现为「瞬间回满」。
- **修复**：命中凭据改为**服务端已登记且存活的召唤实体**（`FindOwnedSummonProjectile`），不再要求缓存坐标当前时刻重叠；
  归属 / 类型 / 伤害上限（≤ ceil(武器伤害 × 1.15)）/ NPC generation / 命中冷却校验全部保留。
  回归用例 `Summon_Attack_Uses_ServerRegisteredEntity_Without_CurrentPositionOverlap`。

**测试**：**779 / 779 通过**（新增：背包守恒事务 4 例 + 集成 3 例、箱子守恒 / 转移 / 快速堆叠 4 例、
宝袋不静默、包 40 编解码与状态同步 4 例、召唤命中凭据；并改写 4 个仍期待「直接拒绝」旧语义的箱子用例）。

### 第三十七轮（2026-09-16）：真机联调收口 —— 生命上限口径 / 挖掘权威 / 挖砖掉落 / SSC 档案持久化

**一、NPC 生命上限与客户端口径对齐（血条「掉一半又回到满」）**

- **根因**：包 23 不下发 `lifeMax`，客户端血条恒为「服务端下发的当前生命 ÷ 客户端按 netID 自算的 lifeMax」；
  客户端对**负 netID 变体**走 `NPC.SetDefaultsFromNetId`（末尾 `lifeMax = life`，变体有独立上限）。
  服务端按 `FromNetId` 解析出的基础类型上限下发当前生命 → 「当前生命 > 客户端上限」→ 比值截断、血条画满。
- **修复**：新增 `NpcVariantLifeTable`（65 项变体上限，逐 case 复算 `life` / `× scale`，含 float32 与 floor 口径）
  + `NpcStatsTable.OfNetId`；`SpawnPickedNpc` 改用它（绿史莱姆 25→14、黑 45、紫 40、丛林 60）。
- **同类缺陷**：`SpawnNpcNear` 硬编码 `Life=LifeMax=20`，且 `AddNpc` 只回填伤害 / 防御 → 克苏鲁之仆（客户端 8）
  显示「10/8」、小蜜蜂（客户端 10）同理；改为按 `NpcStatsTable` 取上限。
- 范围说明：变体只对齐**生命上限**（血条口径），伤害 / 防御仍沿用基础类型（未建模，与改动前一致）。

**二、包 17 第 5 字段语义修正（挖不动地表的砖块）**

- **根因**：该字段在「挖」（action 0/2/4）时是 `KillTile` 的 **fail 标志**（1 = 仅命中特效、0 = 真正破坏；
  `Player.PickTile` 未挖穿发 `SendData(17, …, 0, x, y, 1f)`、挖穿时省略该参数），只有 action 1/3（放砖 / 放墙）
  才是图格类型。早期实现恒按图格类型对账 → 草(2) 等一切非 0/1 的图格永远被判 `tile_type_mismatch` 拒绝
  （真机日志刷 `ClientType=1 ServerType=2`）。
- **修复**：挖只校验越界 / 距离 / 图格存在；放砖 / 放墙才按类型校验（0..556）。命令层对 fail≠0 的包
  不再改动世界（对齐原版 `KillTile(fail: true)`），action 4（`KillTileNoItem`）保持「挖掉但不掉落」。

**三、挖砖掉落 + 整棵树倒下 + 拾取聊天提示（挖什么都不掉）**

- **根因**：原版在服务端 `KillTile` 内调 `WorldGen.KillTile_DropItems` → `Item.NewItem` 生成掉落物再经包 21/22 同步；
  客户端本地 `Item.NewItem` 只写 400 号预测槽、不落世界。服务端从未实现该步 → 挖矿 / 砍树零掉落。
- **修复**：新增 `TileDropTable`（361 项图格→物品映射，逐格提取，含树木特例）+ `WorldState.SpawnItemDrop`
  （分配槽位 + 标记待下发，复用既有包 21/22 与就近归属链路；在区块锁**外**生成以免与 `ItemsLock` 形成新锁序）；
  砍掉任意一格树干即**整棵倒下**（`FellTreeAt`：4 邻接连通树干 + 枝条全部清除），木材数量 = 清掉的格数。
- **拾取提示**：SSC 下拾取是服务端行为，客户端不弹原生提示 → 拾取成功排队一条聊天行（`WorldState.NotifyPlayer`
  → `GameHost.FlushPlayerNoticesAsync` 经包 82 单独下发，每轮上限 8 条防刷屏），名称取新增 `ItemNameTable`
  （6196 项原版标识名；中文显示名在客户端本地化表内，服务端不提供）。

**四、SSC 玩家档案持久化（重进背包清空）**

- **根因**：SSC 下背包 / 生命 / 法力由服务端持有，但运行时档案**从未落盘**（`IPlayerRepository` 仅被测试使用），
  断线即丢，重进从出生默认值开始。
- **修复**：新增 `PlayerProfileCodec`（版本化：生命 / 法力 + 59 槽物品与前缀）+ `IWorldRepository.ClearWorldChangesAsync`
  配套；断线时按**玩家名**（槽位会复用、会话接管换槽位，故不能用槽位身份）写档，新会话进服时回读后再做全量包 5 下发。
  边界：仅在断线时落盘（不做周期保存），增益 / 位置不落盘（位置由宽限期内的会话接管覆盖）。

**五、眼魔掉落表 ID 修正（掉「星辰头盔」）**

- 逐条核对原版掉落库 `ItemDropDatabase.RegisterBoss_EOC`：宝袋 `3381`（实为星辰头盔）→ **`3319`**；
  腐化种子 `37`（护目镜）→ `59`；眼面具 `1991`（捕虫网）→ `2112`（1/7）；望远镜 `1990` → `1299`（1/40）；
  并补上猩红种子 `2171`。专家模式语义确认为「只掉宝袋」（经典掉落均带 `NotExpert` 条件）。

**六、调试与运维向补充**

- 新增游戏内 `/boss <npcId> [x y]`（省略坐标则在发起者上方 8 格，生命取 `NpcStatsTable`）；
- `ServerConfig` 新增 `WorldSeed`（改种子即换图，重启生效）与 `ResetWorldChangesOnStart`（换图时清空旧图坐标上的改动，
  一次性开关，用完置回 false）；启动日志打印本次种子。

**测试**：**464 / 464 通过**（新增：变体上限 10 例、包 17 fail 标志回归、挖砖掉落 6 例 + 端到端包 21/82、
整棵树倒下 + 木材计数、拾取提示队列、SSC 档案编解码往返与「/give 后断线重进仍持有」、`/boss` 召唤、
清空世界改动落盘；并修正两处旧用例判据：会话恢复改判「位置未接管」、宝袋 ID 改 `3319`）。

### 第三十六轮（2026-09-16）：NPC 同步心跳真正生效 + 热路径诊断日志收敛为开关

**一、NPC 同步心跳此前「只写在文档里」**

- **缺陷**：`WorldNpc.SyncedTick` 只写不读 —— 广播循环每轮无条件推进它，且「变化才发」判据里**没有**心跳项。
  后果：NPC 状态长时间不变（静止的城镇 NPC、埋伏不动的敌怪）时，只要玩家离开过视野、客户端基线仍在，
  该玩家回到视野内就**再也收不到**这个 NPC 的新状态（客户端停在旧位置，实测表现为「走远再回来 NPC 不动」）。
- **修复**：`changed` 判据加入 `heartbeatDue = world.Tick - npc.SyncedTick >= NpcSyncHeartbeatTicks`（60 tick ≈ 1s）；
  且 `SyncedTick` / `SyncForced` 都只在**该轮确实发出**（`sent > 0`）后推进 —— 否则 NPC 不在任何玩家视野内时会被白清，
  换型标记与心跳基线同时失效。顺带回收离线玩家的同步基线（`_npcBaselines`：玩家 id 不复用 + 会话接管换新 id，旧条目再无读者）。
- **测试**：`Vanilla_NpcSync_Resends_AfterHeartbeat_Interval`（首次下发 → 未变且未到心跳不发 → 越过心跳周期补发）。

**二、诊断日志收敛（原「调试打印未回收」项）**

- **问题**：真机联调期加的 `[DIAG]` / `[Pkt28]` / `[HP]` / `[Strike]` / `[Hurt]` / `[Damage]` / `[Near]` 输出
  **无开关且落在热路径**：入站包诊断逐包打印（含包 5 背包同步、包 22 拾取、未知包），包 28 每命中一次打印，
  且部分打印发生在 `NpcsLock` 持锁区间内。正常游玩时是纯噪声，也白费字符串构造。
- **方案**：保留全部诊断代码（它们正是「没碰到却掉血」「掉落物拾不到」的定位依据），改为**开关驱动**：
  新增 `DiagnosticLog.Enabled`（根命名空间，见 `Diagnostics.cs`，避免各层反向依赖）+ `ServerConfig.VerboseDiagnostics`
  （**默认关闭**），由 `GameHost.Bootstrap` 接线、`OnConfigurationChanged` 热重载 —— 排障时改 `server.json` 即时生效、无需重启。
  调用约定 `if (DiagnosticLog.Enabled) Console.WriteLine(...)`：关闭时连字符串都不构造。
  入站包诊断整体抽为 `NetworkHost.LogInboundPacket`（由开关守卫一次，替代原来 5 个 case 分支）。
- **保持不变**：启动 / 错误 / 审计 / 世界落盘等低频管理性输出照旧；`[Authority] 拒绝` 本就限频（首次 50 次 + 每 1000 次）。
- **顺带**：`server.json` 移除 `MaxWalkSpeed`（第七轮已从 `ServerConfig` 删除的失效键，JSON 反序列化会静默忽略）；
  `Phase6-Infrastructure/README.md` 同步「ServerConfig 唯一真相源」示例字段。

**测试**：**438 / 438 通过**（新增心跳用例；诊断开关为纯输出门控，不新增断言）。

### 第三十五轮（2026-09-12）：接触判定回归原版 + 免伤帧取错分支的修正 + NPC 同步分档

**一、上一轮（第三十四轮）取错了分支 —— 接触免伤帧不是 30，而是 40**

`GiveImmuneTimeForCollisionAttack(longInvince ? 60 : 30)` 位于 `Player.Update_NPCCollision` 的 **`if (num)`** 分支，
而 `num = CanParryAgainst(...)` —— **那是盾牌弹反的免伤帧**。普通 NPC 接触的免伤帧来自 `Hurt()`：

```csharp
int num10 = pvp ? 8 : ((num2 != 1.0) ? (longInvince ? 80 : 40) : (longInvince ? 40 : 20));
if (cooldownCounter == ImmunityCooldownID.General) { immune = true; immuneTime = num10; }
```

接触的 `cooldownCounter` 默认即 `ImmunityCooldownID.General` → **40**（被防御压到 1 → 20）。
影响：30 tick 就能再次受伤 → 比原版多挨约 1/3 伤害；而且同类事件走两条路径时不一致（包 117 用 40/20、服务端接触用 30）。

**改动**：删除 `PlayerRuntime.ContactImmunityTicks`；接触改用 `GeneralImmunityTicks(damage)`（与包 117 / 下落 / 弹幕同档）。

**二、接触判定去掉 8px 最小重叠，回归原版整型 AABB**

原版 `Player.Update_NPCCollision`：玩家盒 `new Rectangle((int)position.X, (int)position.Y, width, height)`，
NPC 盒同法取整后 `Rectangle.Intersects` —— **取整后再比、无最小重叠**（两轴各 1px 即命中）。

8px 阈值（第三十二轮加的）实际治不了「擦着走过却掉血」：客户端按原版 0px 判定，会把 1~8px 的擦边
用包 117 报上来（`DamagePlayerCommand` 只查免伤帧、不查几何）→ 照样扣血；8px 只是让**服务端自己的判定**
比原版严 8 倍，与客户端不一致。位置偏差要靠对齐位置解决（逐类型尺寸 / 控制位物理 / 高频同步都已就位），
而不是放大阈值。

**改动**：新增 `WorldSimulator.PlayerTouchesNpc`（两端同口径取整 + 无最小重叠）替代接触判定里的 8px 调用；
`BoxesOverlap` 去掉 `minOverlap` 参数；删除 `ContactMinOverlap` 与「擦边未计入」诊断（阈值归零后该诊断不再有意义）。

**三、NPC 同步（包 23）分档：普通 20Hz / Boss 与近身 60Hz**

原版 `NPC.UpdateNetworkCode` 用令牌桶：`num = boss ? 5 : 30`，`netSpam` 每 tick 减 1，
状态变化时要求 `netSpam <= 3 * num` 才发包并 `netSpam += num`
→ 持续 **普通 ≈2 包/秒、Boss ≈12 包/秒**（另有 `StreamUpdatesToNearbyPlayers` 给近距玩家补充包）。

**改动**：60Hz 循环保留，`BroadcastNpcUpdatesAsync(ct, fullRate)` 增加分档 ——
`IsNpcHighPriority`（Boss，或与任一存活玩家中心距 ≤ 3 格）逐 tick 发，其余每 3 次调用（20Hz）发一次；
降频轮**不更新** `Synced*`，故下一个 20Hz 窗口必然补发。仍保持「变化才发 + 1s 心跳 + 视口裁剪」，静止 NPC 不占带宽。

**测试**：**306 / 306 通过**（无新增用例）。`ContactDamage_Requires_MinOverlap` 重写为
`ContactDamage_Aligns_With_Vanilla_Intersect`（边缘相切 0px 不结算 / 压进 1px 必结算）；
`Vanilla_ContactDamage_Has_ImmunityWindow` 期望从 ≈30 改为 ≈40。

**遗留（下一轮候选）**：

1. **玩家物理的其余分支**：可变跳跃高度、冲刺 / 坐骑 / 翅膀 / 水中 / 蜂蜜 / 斜坡与台阶自动上抬、抓钩与传送。
2. **NPC 同步进一步对齐原版**：令牌桶（普通 2Hz / Boss 12Hz）+ 近距流送 —— 前提是客户端 aiStyle 移植足够忠实，
   否则「看着很远却掉血」会回归；当前 20Hz / 60Hz 分档是折中。
3. **框架**：buff 表（施加 debuff 通道 + 剩余时间 / 到期移除）、粉尘 / 音效、外观包 40 建模、弹幕逐类型碰撞盒。

### 第三十四轮（2026-09-12）：免伤帧按原版**分来源**取值

> ⚠️ 本轮的「接触 = 30」取错分支（那是盾牌弹反的免伤帧），已在**第三十五轮**修正为 40。

**原版依据**（此前统一 60 tick，偏长）：

| 来源 | 原版 | 我们 |
|---|---|---|
| **接触攻击**（NPC 撞击） | `GiveImmuneTimeForCollisionAttack(longInvince ? 60 : 30)` → ❌ 见第三十五轮：那是盾牌弹反分支，实际走 `Hurt` → **40** | ❌ 第三十五轮已改为 `GeneralImmunityTicks` = 40 |
| **通用受击**（包 117 / 下落 / 敌对弹幕） | `Player.Hurt`：`immuneTime = pvp ? 8 : (伤害 ≠ 1 ? (longInvince ? 80 : 40) : (longInvince ? 40 : 20))` | 伤害 > 1 → **40**；伤害被压到 1 → **20**（`GeneralImmunityTicks(damage)`） |
| PvP | 8 | 未建模 PvP，暂不需 |

**改动**：`PlayerRuntime` 用 `ContactImmunityTicks` / `HurtImmunityTicks` / `WeakHurtImmunityTicks` +
`GeneralImmunityTicks(damage)` 取代原来的单一 `HurtImmunityTicks = 60`；
`ApplyPlayerDamage` 增加 `immunityTicks` 参数，接触 / 下落 / 敌对弹幕各自传入；
`DamagePlayerCommand`（包 117）改用 `GeneralImmunityTicks(Damage)`。
跨来源仍共用同一个 `HurtCooldown` 窗口（原版 `Player.immune` 亦是统一窗口）→ 双倍伤害防护不变。

**测试**：**306 / 306 通过**（无新增；`Vanilla_ContactDamage_Has_ImmunityWindow` 改为按「距首次受伤的总 tick 数」
断言 ≈30 tick，并把首次受伤后的静止观察窗从 30 收到 20）。

**遗留（下一轮候选）**：

1. **玩家物理的其余分支**：可变跳跃高度、冲刺 / 坐骑 / 翅膀 / 水中 / 蜂蜜 / 斜坡与台阶自动上抬、抓钩与传送。
2. ~~**NPC 同步节奏**~~ —— ✅ **已在第三十五轮部分完成**：改为分档（普通 20Hz / Boss 与近身 60Hz）；
   若要完全对齐原版令牌桶（2Hz / 12Hz）+ 近距流送，需等客户端 aiStyle 移植足够忠实。
3. ~~**接触判定**（8px 半格阈值）~~ —— ✅ **已在第三十五轮完成**：改为原版整型 AABB、无最小重叠（`PlayerTouchesNpc`）。
4. **框架**：buff 表（施加 debuff 通道 + 剩余时间 / 到期移除）、粉尘 / 音效、外观包 40 建模、弹幕逐类型碰撞盒。

### 第三十三轮（2026-09-12）：玩家移动改为「服务端按控制位模拟」（对齐原版做法）

**原版依据**：`Main.Update` 对**所有 active 玩家（含远端）**调用 `player[i].Update(i)`；而客户端只在
**操作变化**时发位置包（`SendData(13)` 的触发条件就是控制位变化）—— 所以原版服务端能靠**自己模拟**与客户端保持一致。
第三十一轮的「观测速度外推」只是近似，本轮换成真正的控制位模拟。

原版数值（`Player` 的 `ResetEffects` 与移动/跳跃分支，逐条核对）：

| 项 | 值 |
|---|---|
| `originalRunSpeed`（→ `maxRunSpeed`） | **3** |
| `runAcceleration` | **0.08** |
| `runSlowdown` | **0.2** |
| `defaultGravity` / `maxFallSpeed` | **0.4** / **10** |
| `jumpSpeed` / `jumpHeight` | **5.01** / 15 |

```csharp
// 水平（无装备时 accRunSpeed == maxRunSpeed）
if (controlLeft  && vx > -maxRunSpeed) { if (vx >  runSlowdown) vx -= runSlowdown; vx -= runAcceleration; }
else if (controlRight && vx < maxRunSpeed) { if (vx < -runSlowdown) vx += runSlowdown; vx += runAcceleration; }
else if (贴地 && 无方向输入) { 按 runSlowdown 收敛到 0 }
// 起跳：releaseJump 保证「松开后再按」才算一次（按住不连跳）
if (controlJump && releaseJump && 贴地 && !controlDown) velocity.Y = -jumpSpeed;
```

**实现**：

- `PlayerRuntime` 新增 `ControlBits`（与原版 `control*` 同一位序）/ `JumpHeld` / `Grounded` / `Direction`；
  `MoveCommand` 携带控制位与（可选的）上报速度，`InboundPipeline` 从包 13 填充。
- 新增 `WorldSimulator.StepPlayerPhysics`（原 `SimulatePhysics` 的玩家分支）：水平加速 / 摩擦 / 起跳 / 重力 /
  **水平阻挡** / 落地（脚底 = `Y + 42`、左右两列），并把结果写入 `AimPosition`
  （NPC 追击 / 接触伤害 / 敌对弹幕 / 刷怪点都读它）。
- **删除**外推近似（`ObservedSpeedX` / `LastMoveTick` / `Extrapolate` / `MaxObservedSpeedX`）与 `MoveCommand.Moving`。
- 位置仍以客户端上报为权威（原版服务端同样直接赋值）；速度若随包上报则采纳（`StateBits` bit2），
  使服务端状态与客户端对齐 —— 这也是本机测试里"转发位置比上报值大 1~2px"的原因（服务端在包间继续推进）。

**测试**：**306 / 306 通过**。新增 `PlayerPhysics_Accelerates_WithControlBits_AndStops_OnRelease`、
`PlayerPhysics_Jumps_OnFreshPress_Only`；`Vanilla_PlayerControls_Relay_Preserves_Mount_And_Camera` 改为容差断言。

**遗留（下一轮候选）**：

1. ~~**免伤帧对齐原版**~~ —— ✅ **已在第三十四轮完成**（接触档位在**第三十五轮**修正为 40）：接触 / 通用 / 弱伤害。
2. **玩家物理的其余分支**：可变跳跃高度（按住跳更高）、冲刺 / 坐骑 / 翅膀 / 水中 / 蜂蜜 / 斜坡与台阶自动上抬、
   抓钩与传送 —— 当前只实现了平地行走 / 跳跃 / 落地 / 水平阻挡。
3. ~~**NPC 同步节奏**~~ —— ✅ **已在第三十五轮部分完成**：改为分档（普通 20Hz / Boss 与近身 60Hz）。
4. ~~**接触判定**（8px 半格阈值）~~ —— ✅ **已在第三十五轮完成**：改为原版整型 AABB、无最小重叠。
5. **框架**：buff 表（施加 debuff 通道）、粉尘 / 音效、外观包 40 建模、弹幕逐类型碰撞盒。

### 第三十二轮（2026-09-12）：接触阈值收到 8px + NPC 水平阻挡（不再穿墙）

**真机日志**（第三十一轮修复后重测，第一段 15s / 第二段 39.7s）：

```
[Damage] -7（client_reported/包117）HP=93 位置=33598,4950
[Damage] -7（contact_damage）HP=86 NPC 1@33524,4974 玩家判定=33507,4950（上报=33507,4950）
[Damage] -7（contact_damage）HP=79 NPC 1@33350,4974 玩家判定=33334,4950（上报=33321,4950）
[Damage] -7（contact_damage）HP=72 NPC 1@33290,4974 玩家判定=33311,4950（上报=33326,4950）
```
- **第二段 39.7 秒零受伤** ✓（外推 + 60Hz NPC 同步起效）。
- 第一段 3 次 `contact_damage` 全是**水平只重叠 3~4px 的"擦着走"**（史莱姆 Y=4974 → 脚底正好在地面，
  即它站在你旁边贴着走，而不是撞上来）。

**修复 1：接触阈值 3px → 8px**。真正走进玩家身上的重叠是十几~二十像素（史莱姆盒 24 宽 / 玩家盒 20 宽，
走穿时重叠可达 20px），所以取半格 8px 只丢"擦着走"。同时加 `[Contact] 擦边未计入 …` 限频诊断（最多 30 条），
便于继续确认阈值是否合适。

**修复 2（用户反馈"史莱姆一直往左走、我在右边"）**：NPC 物理此前**只做垂直落地、水平完全不阻挡** ——
服务端 NPC 会穿过地形直线前进，而客户端有完整碰撞，两边位置持续发散（既解释"一直朝一个方向走"，
也是历次"看着离得很远却掉血"的发散来源之一）。给 `StepNpcPhysics` 加**水平阻挡**
（`NpcBlockedHorizontally`：前进方向边缘格实心即停在原地并清零水平速度），顺带让原版「卡住翻向」
判定（`ai[3] == position.X`）能真正生效。

**测试**：+1（**305 / 305 通过**）—— `Enemy_Stops_At_Wall`（朝墙冲不得穿过）；
`ContactDamage_Requires_MinOverlap` 改为 4px 擦边不扣血 / 16px 走进必扣。

**剩余清单（后续各轮）**：

1. ~~**玩家移动的服务端模拟**~~ —— ✅ **已在第三十三轮完成**：`StepPlayerPhysics` 按包 13 的控制位跑
   水平加速 / 摩擦 / 跳跃 / 落地 / 水平阻挡，外推近似已删除。
2. **框架：buff 表**（施加 debuff 通道 + 剩余时间）。
3. **框架：粉尘 / 音效**（纯客户端表现，最后补齐）。
4. **NPC 逐类型物理覆盖**（`gravity` / `maxFallSpeed` 的少数类型覆盖，如 258、576/577）。
5. **未建模包**：`#40`(SyncPlayer) / `#41`(SyncEquipment) / `#56` / `#152` / `#154` 与 NetModule 子模块一律被拒（不计违规）；
   外观类包（40）值得建模。
6. **弹幕碰撞盒逐类型**：命中判定现统一按 16×16 近似。
7. 其余 aiStyle：服务端不可达（不刷），移植无收益 —— 按需登记 `NpcAiStyleOf` 即可扩展。

### 第三十一轮（2026-09-12）：真机「一次都没碰到却在扣血」—— 服务端在追玩家早已离开的坐标

**真机日志**（第三十轮修复后重测，35 秒 7 次扣血，均为 `contact_damage` —— 客户端上报那条已被免伤帧挡掉 ✓）：

```
[Damage] 玩家 #1 -7（contact_damage）HP=93 NPC 1@33809,4937 玩家=33800,4950
[Damage] 玩家 #1 -7（contact_damage）HP=86 NPC 1@33985,4937 玩家=33998,4950
... 玩家坐标每次 +130~210px（正好是走速 3.3px/tick × 1s）
```

**根因（协议行为）**：原版客户端**只在操作变化时**才发位置包 —— `Player.cs` 里 `SendData(13)` 的触发条件是
`controlLeft/controlRight/... 与上一帧不同`（见 `Player.cs` 7421-7451）。原版服务端之所以没问题，是因为
**它自己也在跑玩家的 `Update`（用同步来的控制位继续模拟玩家移动）**，所以两边的玩家坐标始终一致。

TerraAuth **不做玩家操作模拟**：`MoveCommand` 只是把上报坐标赋值过去，不发包期间玩家在服务端"站住不动"。
于是——
- NPC 的 `NearestPlayer` 追的是一个**已经过期的玩家坐标**（记录里玩家每 1s 才被"更新"一次）；
- 服务端的接触判定也用这个过期坐标 → 史莱姆打的是"1 秒前的你" → 你看着它从没碰到你，血却在掉。

**修复**：给玩家加「观测速度 + 位置外推」，NPC 追击 / 接触 / 弹幕命中 / 刷怪点全部改读这个外推位置：

- `PlayerRuntime.ObservedSpeedX` / `LastMoveTick` / `AimPosition`（[WorldState.cs](Simulation/World/WorldState.cs)）：
  `Extrapolate(nowTick)` = 上报坐标 + 观测速度 × min(间隔, **15 tick**)。
- `MoveCommand`（[CommandQueue.cs](Simulation/CommandQueue.cs)）：
  用「本包与上一包的位移 ÷ 间隔」算观测速度，**只在按着方向键**（包 13 控制位 bit2/bit3）且平均速度合理（≤20px/tick）时更新 ——
  松开按键的那一包会立刻把外推速度归零，首次进服 / 传送也不会算飞。
- `SimulateAi` 每 tick 刷新所有玩家的 `AimPosition`；`NearestPlayer` / 各 aiStyle 的 `target.Position`、
  `SimulateBossStep`、接触与敌对弹幕命中、刷怪点全部改用 `AimPosition`。
  注：`player.Position`（上报值）不变，仍是同步 / 挖放砖距离 / 落盘的权威坐标，故对既有行为零影响。
- 接触日志同时打出「判定位置」与「上报位置」，便于继续核对。

**测试**：+1（**304 / 304 通过**）—— `PlayerAimPosition_Extrapolates_Between_PositionPackets`
（观测速度 = 位移 ÷ 间隔；无新包时外推；松开方向键立即停止外推）。

**剩余清单**：后续项（玩家移动的服务端模拟 / buff 表 / 粉尘音效 / NPC 逐类型物理 / 未建模包 / 弹幕碰撞盒）见**第三十二轮**「剩余清单」。

### 第三十轮（2026-09-12）：真机日志定案「没碰到却扣血」—— 2px 擦边 + 双倍伤害

**做法**：先给受伤路径补上诊断日志（此前服务端**不打印任何受伤记录**，无法判断血是怎么掉的），
再让用户实战一轮，用日志定案。实测日志（50 秒内死亡）：

```
[Damage] 玩家 #1 -7（contact_damage）HP=93  NPC 1@33617,4934  玩家=33605,4950   ← 垂直只重叠 2px
[Damage] 玩家 #1 -7（client_reported/包117）HP=86  位置=33402,4950             ← 同一秒的第二次
[Damage] 玩家 #1 -7（contact_damage）HP=79  NPC 1@33382,4974  玩家=33402,4950
...
```

**两个真问题**：

1. **2px 擦边被判成接触**：史莱姆跳到头顶高度掠过时，其碰撞盒（24×18）与玩家盒（20×42）**垂直只重叠 2px**，
   视觉上"没碰到"却结算了伤害。根因是服务端 NPC 位置与客户端画面存在几像素偏差，边界处就会来回翻转。
2. **同一次接触被结算两次**：服务端自己的接触判定与客户端上报的包 117 各自扣一次血
   （日志里 `contact_damage` 与 `client_reported` 交替出现，每秒 7+7=14）。
   原版 `Player.immune` 是**跨来源的统一窗口**，而 `DamagePlayerCommand` 当时完全没看免伤帧。

**修复**：

- 接触判定加**最小重叠**：两轴重叠都要 ≥ `ContactMinOverlap = 3px` 才算接触
  （实测擦边 2px、正常贴身 4px~18px，取 3 即「只丢 ≤3px 的擦边」）。`BoxesOverlap` 增加可选 `minOverlap`。
- `DamagePlayerCommand`（客户端上报的包 117）**纳入统一免伤帧**：`HurtCooldown > 0` 时忽略，
  结算后置 `HurtCooldown = PlayerRuntime.HurtImmunityTicks`（60）。免伤帧常量上移到 `PlayerRuntime` 供两处共用。
- 保留诊断日志（`[Damage]` + 来源 + 双方坐标），便于后续继续定位。

**测试**：+2（**303 / 303 通过**）—— `ContactDamage_Requires_MinOverlap`（2px 擦边不扣血、4px 正常接触必结算）、
`ClientReportedDamage_Respects_ImmunityWindow`（免伤帧内忽略客户端上报伤害）。

**剩余清单**：后续项（玩家移动的服务端模拟 / buff 表 / 粉尘音效 / NPC 逐类型物理 / 未建模包 / 弹幕碰撞盒 / NPC 水平碰撞）见**第三十一轮**「剩余清单」。

### 第二十九轮（2026-09-12）：史莱姆「没碰到却扣血」—— NPC 重力 / 终速必须与原版一致

**真机症状**（用户反馈）：有时候史莱姆并没有碰到我，我仍在扣血。
**服务端日志核对**：本轮**没有** `speed_exceeded`（只有不建模包的 `unknown_packet`），
所以不是上一轮的「位置被拒 → 服务端坐标变陈旧」那条路径。

**根因**：服务端 NPC 物理常数与原版不一致。原版 `NPC.UpdateNPC_UpdateGravity` 的默认值是
**`gravity = 0.3`、`maxFallSpeed = 10`**（蓝史莱姆等我们模拟的类型都没有覆盖），而 TerraAuth 用的是
`0.4 / 16` → **同一只史莱姆，服务端算出的跳跃 / 下落弧线与客户端按原版算出的不同**。

这条发散为什么表现为「没碰到却扣血」：原版客户端收到包 23 后把 NPC 位置**硬赋值**，之后自己按原版跑 AI，
并把「本地位置 − 服务端位置」记进 `netOffset` **只用于渲染**（`NPC.Update` 里 `position += netOffset` 的括号块）。
于是玩家**看到**的是客户端本地算出的位置，而**碰撞判定用的是服务端下发的那个位置**（客户端与服务端都用它）——
双方弧线不一致时，就会出现「看它没碰到，判定说碰到了」。

**修复**（[WorldSimulator.cs](Simulation/WorldSimulator.cs)）：

- 拆出明确的原版常数：`NpcGravity = 0.3` / `NpcMaxFallSpeed = 10`（NPC 物理步），
  `PlayerGravity = 0.4` / `PlayerMaxFallSpeed = 10`（原版 `Player.maxFallSpeed = 10`）。
- 掉落物继续用原值，不动。
- 顺带验证：`AI_001_Slimes` 的起跳档位 / `ai[0]` 复位值（`-120 + num54(×2)`、`-200`）已通过协议行为验证，无已知发散。

**测试**：+1（**301 / 301 通过**）—— `Enemy_Fall_Uses_Vanilla_Gravity_And_TerminalSpeed`
（每 tick +0.3、终速 10）。

**剩余清单**：后续项（buff 表 / 粉尘音效 / NPC 逐类型物理 / 未建模包 / 弹幕碰撞盒 / NPC 水平碰撞）见**第三十轮**「剩余清单」。

### 第二十八轮（2026-09-12）：向导一直跳 / 移动误判超速 / 平地误判下落伤害

**真机症状**（用户反馈）：①向导一直在跳；②移动时没有碰撞也提示扣血；③站着不动被史莱姆打也提示 `speed exceeded`。

**根因 1（向导一直跳）—— 上一轮我引入的回归**：障碍起跳探测用了「脚下的支撑行」，
而平地前方本来就有地面 → 每 tick 都判为障碍 → `velocity.Y = -4.4 / -5 / -6` 一直触发。
原版（`NPC.cs` 64412-64474）只在**朝向方向正在移动**时才有条件起跳，且探的是**身体所在行**（支撑行上方的
`tileSafely4/5`）与「台阶高度差」条件，不是支撑行。→ 改为 `supportRow = (Y + height + 1) / 16`、
`bodyRow = supportRow - 1`，只探 `bodyRow` / `bodyRow - 1`（向导 -6/-5，战士 -8/-6），并去掉恒真的第三档。

**根因 2（平地/移动时误判下落伤害）**：玩家碰撞盒宽 20、站立时可能跨两列，而落地判定只探**左列** →
站在台阶边缘 / 地形拐角时漏判「落地」→ `FallDistance` 持续累积 → 回到平地时被结算下落伤害。
→ 落地判定改为**左右两列任一实心即落地**（与 NPC 物理一致）。

**根因 3（正常移动被判超速）**：`speed_exceeded` 用的是匀速上限（`maxSpeed × 60 × Δt + 容差`，默认 8×1 帧 + 4 = 12px），
但原版客户端在**受击击退 / 被方块挤出 / 斜坡校正**时会有十几~几十像素的合法单帧位移 →
被拒后权威基准不更新 → 后续包位移更大 → 连续拒绝并累计违规（达阈值即踢）。
→ 引入「可疑带」：超上限在 **×4** 以内一律放行，只有**真瞬移量级**（> 上限 ×4）才拒绝；
拒绝原因行同时输出量测值 `dx/dy/允许/dt/上限`（`AuthorityResult.Detail`），便于继续定位。

**测试**：+2（**300 / 300 通过**）—— `Guide_DoesNotJump_OnFlatGround`（平地 120 tick 不起跳）、
`MovementAuthority_Accepts_KnockbackScale_Step`（20px 击退级单帧位移放行）；
`StandingPlayer_DoesNotAccumulate_FallDistance` 继续覆盖根因 2 的落地判定。

**剩余清单**：后续项（buff 表 / 粉尘音效 / 客户端 117 伤害声明 / NPC 逐类型物理 / 弹幕碰撞盒 / NPC 水平碰撞）见**第二十九轮**「剩余清单」。

### 第二十七轮（2026-09-12）：实体碰撞盒按原版口径对齐（真机抖动 / 向导陷地 / 莫名掉血）

**真机症状**（用户反馈）：①怪物异常抖动、像是加速；②向导仍卡在地里。

**核对原版后的根因**：服务端下发的位置语义与客户端不一致。原版约定（`MessageBuffer` 包 23 接收侧 + `NPC.SetDefaults`）：

- 实体 `position` = **碰撞盒左上角**，脚底 = `position.Y + height`；客户端收到包 23 后 `npc.position = 收到的位置 − 同步锚点`，
  随后按 `velocity` / `ai[]` **自行推进 + 自行做图格碰撞**（`netOffset` 只是渲染插值，见 `NPC.Update` 里 `position += netOffset` 的括号）。
- 玩家碰撞盒 = **20×42**（`Player.cs`）；NPC 逐类型（Guide 18×40、Blue Slime 24×18、Goblin Peon 18×38、
  Eye of Cthulhu 100×110、Spazmatism 100×110、Queen Bee 66×66、Hornet 12×12 / 8×8、King Slime 98×92、Skeletron 80×102）。

TerraAuth 此前把 `PlayerHalfHeight = 21` 当成**全高**用，NPC 也统一按 21 高建模，于是：

1. **向导陷地**：我们上报的 Y 比客户端尺寸应有的位置高 19px（40 高的碰撞盒只留了 21）→ 客户端把向导画进地里，
   且其自身 AI 碰撞把向导往上推、我们下一次同步又把它按回去 → **陷地 + 抖动**。
2. **怪物抖动、像加速**：`StepNpcPhysics` 落地时**同时清零水平速度**，贴地 NPC 每 tick 只能拿到 AI 的那一点瞬时加速度
   （城镇 NPC 0.07 px/tick），而客户端按自己的 AI 以 1 px/tick 走 → 每个同步周期都要把客户端拉回来 → **抖动 / 忽快忽慢**。
3. **站着莫名掉血**：玩家脚底按 `Y + 21` 判 → 站在地上永远检测不到「落地」→ `FallDistance` 持续累积
   （重力不断加速，最高 16 px/tick），而客户端每个位置包都会把 Velocity 清零（被当成「刚落地」）→ 凭空结算下落伤害。

**修复**：

- 新增 `Simulation/NpcAI/NpcSizes.cs`：玩家 20×42 + 上述 NPC 逐类型尺寸（**逐个类型按原版 `SetDefaults` 核对**，
  不是估的；写进去之前逐条 grep 过 `width/height`）。
- `SimulatePhysics`：玩家脚底改为 `Position.Y + 42`（落地判定与吸附都用全高）→ 站着不再累积下落距离 / 不再凭空掉血。
- `StepNpcPhysics`：改用该 NPC 的 `width/height`（脚底 = `Y + height`，左右任一侧脚底实心即落地），
  且**落地不再清零水平速度**（地面摩擦交回各 aiStyle：史莱姆 `×0.8`、城镇 NPC 收敛到 ±1）。
- 生成 / 落位 / 瞄准 / 命中全部改到原版口径：向导与刷怪点按「脚底贴地表上沿」摆碰撞盒；
  `SpawnBoss` / `SpawnNpcNear` 以「碰撞盒中心 = 调用方给的点」换算左上角；服务端弹幕从**碰撞盒中心**射出。
- 命中判定改为 **AABB 求交**（原版 `Collision.CheckAABBvAABBCollision`）：接触伤害用玩家 20×42 与 NPC 实际尺寸；
  弹幕命中用 16×16 近似（原版弹幕宽 6~16）；敌对弹幕打玩家同理。移除了「点 + 32px 半径」的近似。

**测试**：+2（**298 / 298 通过**）—— `StandingPlayer_DoesNotAccumulate_FallDistance`（站着不掉血 / 不下沉）、
`GroundedEnemy_Keeps_Horizontal_Velocity`（落地不清零水平速度）；`Guide_Spawns_WithFeet_On_Ground` 改为断言原版尺寸 18×40 与脚底贴地。

**剩余清单**：后续项（buff 表 / 粉尘音效 / 客户端 117 伤害声明 / 弹幕碰撞盒 / NPC 水平碰撞）见**第二十八轮**「剩余清单」。

### 第二十六轮（2026-09-12）：失效实体回收 + NPC 落位修正

**缺陷 1：失效实体只标记不回收（列表只增不减）**

- 现象：弹幕 / 掉落物失效后由世界同步补发销毁包（包 29 / 21）并置 `RemovalNotified`，
  但**从不从 `WorldState.Projectiles` / `Items` 移除** —— 长跑下每 tick 遍历、20Hz 快照过滤都按整个列表长度做，
  内存与耗时随游戏时间线性增长（Boss 每秒发弹、掉落物不断产生）。
- 修复：`SimulateEntities` 在遍历结束后回收 —— `!Active && (RemovalNotified || Tick - DeadTick > 600)`。
  - 以 `RemovalNotified` 为准 → **销毁包已成功下发才移除**，不会让客户端留下幽灵实体；
  - `600 tick（10s）` 为兜底：下发持续失败（无连接 / 瞬时错误）时也不至于永久泄漏。
  - **不在失效的同一 tick 移除**（`Tick > DeadTick`）：留一 tick 让世界同步 / 事件 / 测试观察到失效态。
- 附带修正：`KillProjectileCommand` / 拾取命令的 `DeadTick` 改用 `world.Tick`（命令的 `Tick` 可能落后于当前世界 tick，
  导致「同一 tick 即回收」而观察方看不到失效态）。

**缺陷 2：NPC 生成落位悬空（向导 / 史莱姆 / 哥布林）**

- 现象：仿真物理的落地不变量是「脚底 = Y + 21」（`PlayerHalfHeight`），但生成时用的是别的口径 ——
  向导 `(spawnGroundY - 2) × 16` 比正确位置**高 11px**（生成后先自由落体），史莱姆 / 哥布林 `(y - 1) × 16` 则**陷地 5px**。
- 修复：统一为 `Y = 地表图格上沿 − 21`（`WorldGenerator.NpcFeetOffset` / `WorldSimulator.PlayerHalfHeight`），
  脚底正好落在地表上沿，生成当帧即静止，无双帧抖动。

**测试**：+2（**296 / 296 通过**）—— `Guide_Spawns_WithFeet_On_Ground`（向导脚底贴地）、
`Expired_Projectiles_Are_Reclaimed_After_Removal_Notified`（失效弹幕在销毁包下发后回收）。

**剩余清单**：「接触 / 命中判定口径」已在**第二十七轮**按原版碰撞盒对齐；其余项（buff 表 / 粉尘音效 /
弹幕行为表扩展）见第二十七轮「剩余清单」。

### 第二十五轮（2026-09-12）：按原版 aiStyle 重建 NPC AI（二）—— aiStyle 31/43 + 服务端弹幕推送框架

**继续实现 Boss aiStyle**（承第二十四轮的「剩余清单」第 1、2 项，按协议行为验证）：

- **aiStyle 31（Spazmatism，type 126）** —— `Ai031Spazmatism`：
  - 一阶段 `ai[1] == 0`：绕到「玩家中心 ±400px」的侧面（加速 0.4 / 限速 12），每 **60 帧**发 1 枚 type 96 魔焰弹
    （初速 12 / 伤害 25 / 生存 300）；600 帧后转冲刺。
  - `ai[1] == 1`：朝玩家 13f → `ai[1] = 2`；`ai[1] == 2`：8 帧后 `velocity *= 0.9`，42 帧计一次冲刺，连冲 **10** 次回落。
  - 二阶段：`life < lifeMax * 0.4` → `ai[0] = 1` 自旋 100 帧 → `ai[0] = 2`；贴身 180px 悬停（加速 0.1 / 限速 4）
    + 每 **>8 帧**发 1 枚 type 101 火球（初速 6 / 伤害 30，用 `localAI[1]` 节流）；400 帧后冲刺 14f、50 帧后 `*= 0.93`、
    80 帧计一次、连冲 **6** 次（原版 32227-32867）。
  - 未移植：专家模式数值、二阶段 `rotation` 自旋（纯客户端表现）、白天脱战（简化为无目标上浮）。
- **aiStyle 43（Queen Bee，type 222）** —— `Ai043QueenBee`：`ai[0]` 攻击选择状态机（原版 35530-36226）
  - `-1`：随机挑下一招（只从 0/2/3 里选，`localAI[0]` 记上一招避免连续重复）。
  - `0` 悬停 / 对齐：高度差 `|py - (npc.Y + 33)| < 20` 时对齐后以 12f 冲刺（`num754 = 2` 轮后重选）；
    否则纵向 0.15 趋近（限速 12）、横向按 |dx| 与 600 / 300 的关系 ±0.15（限速 16）。
  - `1` 黄蜂突进：14f 冲玩家，每 40 帧召 1 只 210/211（初速 5），5 次后重选。
  - `2` 重新接近：加速 0.07 / 限速 12（目标点 = 玩家上方 200px），距 < 200 转突进。
  - `3` 毒刺齐射：玩家在下方时每 40 帧发 1 枚 type 719（初速 8 / 伤害 11），20 个周期后重选。
  - `4` 拉开距离：速度朝「远离玩家 14f」收敛（`(v*14 + e)/15`），距 < 2000 重选。
  - `5` 离场：减速上浮，出世界上边界置 `Active = false`。距离 > 3000 强制 `ai[0] = 4`。
  - 未移植：`num750` 难度修正（水下 / 非丛林 / 古德世界）、专家数值。

**服务端弹幕推送框架（包 27）** —— 此前 **Boss / NPC 发射的弹幕在服务端生成后从不外发**，其他玩家看不到：

- `ProjectileEntity` 新增 `NewNotified`（生成是否已下发，同掉落物的 `NewNotified` 语义）。
- AI 发射走 `SpawnNpcProjectileToward` → 暂存 `_pendingProjectiles`，**遍历结束后统一入队**（避免遍历中改集合），
  键取**负键段** `_nextServerProjectileKey`（客户端弹幕用非负键，二者不冲突）。
- `GameHost.FlushNewProjectilesAsync`（快照循环内，与 NPC 同步同频）：对 `!NewNotified` 的弹幕按 `Owner` 分流 ——
  客户端弹幕发给**除归属者外**的玩家（`BroadcastWhereAsync`），服务端弹幕（`Owner < 0`）发给所有人；
  **发送成功才置位**，瞬时失败记 `broadcast_failed`（下轮重试）。

**弹幕行为表（原版字段驱动）** —— 新增 `Simulation/Projectiles/WorldSimulator.Projectiles.cs`：

- 按原版 `Projectile.SetDefaults` 的字段建立行为表 `ProjectileBehaviorOf(type)`（仅收录**服务端会发射**的三种）：
  **96** CursedFlameHostile（aiStyle 8）直线 / 图格碰撞 / `timeLeft = 3600`；
  **101** EyeFire（aiStyle 23）`extraUpdates = 3`（每 tick 积分 4 次）/ `timeLeft` 钳到 60；
  **719** QueenBeeStinger（aiStyle 1）直线 / 图格碰撞。未登记类型保持简化直线积分（与客户端上报弹幕解耦）。
- `StepProjectile`：按 `UpdatesPerTick` 次积分位置，`TileCollide` 类型撞到实心图格 / 出界即失效。
- **命中归属修正**：玩家弹幕（`Owner >= 0`）才结算敌怪伤害；敌对弹幕（`Owner = -1`）改由
  `SimulateCombat` 结算**玩家伤害**（`projectile_damage`，与接触伤害共用免伤帧）。三类弹幕原版 `penetrate = -1`
  （不因命中销毁），故不消耗弹幕、靠免伤帧限频。

**测试**：+5（**294 / 294 通过**）—— `Vanilla_ServerProjectile_IsBroadcastToOtherPlayers`（服务端弹幕外发）、
`Vanilla_Spazmatism_EmitsFireball_And_EntersCharge`（31 的 `ai` 状态机 + 弹幕生成）、
`Vanilla_QueenBee_CyclesAttackChoice`（43 的 `ai[0]` 攻击选择推进）、
`Vanilla_HostileProjectile_Damages_Player`（敌对弹幕命中玩家）、`Vanilla_Projectile_Stops_At_SolidTile`（图格碰撞）。

**剩余清单**：「向导落位」与「失效实体回收」已在**第二十六轮**完成；其余项（buff 表 / 粉尘音效 /
接触判定口径 / 弹幕行为表扩展）见第二十六轮「剩余清单」。

### 第二十四轮（2026-09-12）：按原版 aiStyle 重建 NPC AI（一）—— 地基 + aiStyle 1 + ai 下发

**目标**（用户要求）：怪物 AI 完成原版客户端兼容的服务端行为建模；含框架、并把 `ai[0..3]` 下发。
**本轮交付地基 + 第一个 aiStyle**（其余 aiStyle 与框架按同法逐轮补齐，见文末「剩余清单」）。

**结构调整（对齐原版）**：

- `WorldNpc` 新增 `AiStyle`（原版 `NPC.aiStyle`）、`Ai[0..3]`（原版 `NPC.ai[]`）、`Direction`（原版 `NPC.direction`）。
- 新增 `Simulation/NpcAI/WorldSimulator.NpcAi.cs`：`NpcAiStyleOf(type)` 分发表 + `RunNpcAi(npc)` 分派 +
  各 aiStyle 实现 + 共用物理步 `StepNpcPhysics`。顺序与原版一致：**AI 只设速度/ai → 物理步走重力与图格碰撞**。
- 未移植的 aiStyle 走 `AiFallback`（简化追击），Boss 仍走 `SimulateBossStep`（自移动）。

**已实现：aiStyle 1（Slimes，原版 `AI_001_Slimes`）** —— 已通过协议行为验证：

- 初始化：`ai[2] == 0` → `ai[0] = -100`、`ai[2] = 1`、选定目标方向。
- 贴地：`ai[2]` 递减；`ai[3] == position.X` → 卡住 → `direction *= -1`、`ai[2] = 200`；
  地面摩擦 `velocity.X *= 0.8`（|vx| < 0.1 归零）；`ai[0]++`（有目标额外 +1）；
  按 `ai[0]` 与门限 `num54 = -1000` 的三档窗口得 `phase = 1/2/3`。
- 起跳：1/2 档 `velocity.Y = -6`、`velocity.X += 2 * direction`、`ai[0] = -120 + num54(×2)`；
  3 档（强跳）`velocity.Y = -8`、`velocity.X += 3 * direction`、`ai[0] = -200`、`ai[3] = position.X`。
- 空中：朝 `direction` 水平加速 0.2、上限 ±3（原版 `0.93` 阻尼分支）。

**包 23 下发 `ai[0..3]`**：`bitsA bit2..5` 置位并写出 4 个 float（位于 `bitsB` 之后、`netId` 之前，
已按客户端 `MessageBuffer` 读取顺序核对）；解码侧读取并保留；同步的「变化才发」判定纳入 ai 与 direction。
理由：客户端对未置位的 ai 位会**显式置 0**，不下发则依赖 ai 的 aiStyle 表现必然与服务端不一致。

**测试**：+1（**289 / 289 通过**）—— `Vanilla_NpcSync_Carries_Vanilla_Ai_State`（包 23 携带 4 个 ai）；
既有的史莱姆跳跃用例（`Enemy_Hops_WithGroundedWait_InsteadOfGroundGliding`）在**原版节拍**下继续通过。

**本轮继续移植（同一轮内完成）**：

- **aiStyle 3（Fighters，type 26 哥布林工兵）**：贴地加速 0.07 / 上限 1.5；`velocity.X == 0 && 贴地` → `ai[0]++`，累计 2 掉头；
  前方 2~3 格实心 → 起跳 `-8`、1 格 → `-6`、台阶 → `-5`（原版 68784-69139 / 71518-71575）。type 26 无弹幕（攻击 = 接触伤害）。
- **aiStyle 7（TownEntities，type 22 向导）**：走速上限 1 / 加速 0.07；离家 >25 格朝家走、家附近每 tick 1/80 概率掉头
  （原版 64092-64278）；前方障碍起跳 `-6 / -5 / -4.4`（原版 64416-64463）。
  重力改由**共用物理步**承担（原版 gravity = 0.3 属引擎侧）→ **向导不再卡在土里**（脚下图格实心即贴地站立）。
  未移植：住房判定 / 坐下（`ai[0]=5`）/ 传送回家 / 远程攻击状态 / 微光状态机。
- **aiStyle 4（Eye of Cthulhu，type 4）**：新增**飞行体物理**（`WorldNpc.NoGravity`：无重力、无图格碰撞，仅边界钳制）。
  按原版节奏实现：`life < lifeMax*0.5` → 二阶段；`ai[1]=0` 追「玩家上方 200px」（加速 0.04 / 限速 5，600 帧转冲刺，
  玩家在下方且距离 <500 时每 110 帧生成仆从 type 5）；`ai[1]=1` 蓄力 6.8；`ai[1]=2` 冲刺 40 帧后 `*=0.97`、130 帧计一次、连冲 3 次回落
  （原版 24948-25480）。未移植：二阶段自旋（纯客户端 rotation）、专家预判冲刺、白天脱战上浮（简化为无目标上浮）。
- **AI 内刷怪安全化**：新增 `_pendingNpcSpawns`，AI 期间产生的刷怪（如眼魔仆从）在遍历结束后统一入队，避免遍历中修改集合。

**剩余清单**：本轮登记的 aiStyle 31 / 43 与「服务端弹幕推送框架」已在**第二十五轮**完成；
后续项（弹幕行为表 / buff 表 / 粉尘音效 / 向导落位 / 接触判定圆心）见第二十五轮「剩余清单」。

### 第二十三轮（2026-09-12）：敌怪改为**跳跃式移动**（恢复史莱姆的「跳」）

**问题**（真机实测反馈）：原版史莱姆是**跳着走**的，改完同步频率后卡顿缓解，但**跳跃动作没了** —— 看着像贴地滑行。

**根因（两侧证据）**：

1. **服务端**：`SimulateEnemyStep` 每 tick `VelocityX = ±1` + 重力，**从不给 `VelocityY` 向上初速度**
   → 权威运动本身就是「贴地滑行」，没有滞空阶段。
2. **客户端**：`MessageBuffer.cs`（包 23 解码）处理包 23 时
   `npc.position = ...; npc.velocity = velocity; for (i<NPC.maxAI) npc.ai[i] = array2[i];`
   —— **速度与 ai[] 都被覆盖**（未置位的 ai 位即 0）。所以 20Hz 的「地面速度 + ai 全零」
   会把客户端本地史莱姆的跳跃状态每 50ms 冲掉一次，即使客户端自己起跳也会被立刻抹平。

**已实施**：

- **服务端跳跃式移动**：贴地时静止等待 `SlimeHopWaitMin(20) + rand(SlimeHopWaitSpan(25))` tick，
  然后起跳 `VelocityY = -SlimeHopSpeed(6)`（配合 `Gravity = 0.4` ≈ 45px 高 / 30 tick 滞空）
  并给朝玩家的水平速度 `SlimeHopHorizontalSpeed(2)`；空中只受重力，落地归零并重新计时。
- **贴地判定用碰撞结果而非速度**：新增 `WorldNpc.Grounded`（由图格碰撞维护）。
  用 `VelocityY == 0` 当「贴地」会让**悬空生成（速度为 0）的敌怪不落体**；同时贴地时必须**直接返回**
  （不做重力积分），否则每 tick 都「重新落地」把等待计数清零，永远等不到起跳（实现过程中踩到并修正）。
- **接触免伤用例解耦 AI**：`Vanilla_ContactDamage_Has_ImmunityWindow` 改为每 tick 把敌怪钉在玩家碰撞盒中心
  （敌怪现在会跳离 32px 接触圈），用例只验证免伤窗口本身，不再隐含「敌怪必须贴地不动」。

**测试**：+1（**288 / 288 通过**）—— `Enemy_Hops_WithGroundedWait_InsteadOfGroundGliding`
（300 tick 内必须出现过 `VelocityY < 0` 的起跳帧 + 存在 ≥2 tick 的地面静止等待期；
旧实现「贴地滑行」永远满足不了第一条）。

### 第二十二轮（2026-09-12）：真机实测修复（一）—— NPC 同步 20Hz + AI 每 tick 步进

**背景**：原版客户端真机进图后报告三条症状：①怪物移动卡顿；②向导卡在土里；③未碰怪却掉血。
本轮只修 ①（NPC 同步与 AI 步进），并顺带修一个启动崩溃路径；②③ 见文末「仍未修」。

**问题（代码依据）**：

1. **NPC 同步只有 1Hz**：包 23 原先只在 1Hz 的世界同步循环里下发（`GameHost` 的 `BroadcastWorldStateAsync`），
   客户端表现为「每秒被拽一次」。原版客户端自身也跑 NPC AI（`NPC.UpdateNPC` 无 netMode==1 提前返回），
   于是「客户端 AI 走一段 ↔ 服务端位置每秒覆盖」互相打架 → 卡顿。
2. **服务端 AI 每 4 tick 才步进**：`SimulateAi` 开头 `if ((_world.Tick & 3) != 0) return;`，
   而步进量固定为 `npc.X += VelocityX`（±1px）→ **等效速度仅 0.25 px/tick**（原版史莱姆约 1–2 px/tick），
   服务端位置本身就是「一跳一跳」，进一步放大卡顿与「服务端判定位置 vs 客户端画面」偏差。
3. **启动崩溃掩盖真实原因**：端口被占用时 `MetricsHttpServer` 绑定失败，随后 `Dispose()` 抛
   `ObjectDisposedException` 成为未处理异常 → 把「端口被占用」的真实错误盖掉（本轮实测踩到）。

**已实施**：

- **NPC 同步改到快照频率（默认 20Hz）**：新增 `GameHost.BroadcastNpcUpdatesAsync` 并由快照循环调用；
  **状态变化才发**（X / Y / 速度 / 生命 / 存活），未变化按 `NpcSyncHeartbeatTicks = 60`（≈1s）**心跳补发**，
  保证中途入服玩家也能看到静止 NPC。`BroadcastWorldStateAsync` 保留一次调用以兼容既有调用方
  （变化检测会抑制该重复包）。
- **AI 改为每 tick 步进**：去掉 `& 3` 门控；敌怪 1 px/tick + 每 tick 重力，Boss `BossSpeed = 2` px/tick
  （语义即「像素 / tick」，与速度常量口径一致）。
- **城镇 NPC 平滑化**：新增 `SimulateTownNpcStep` —— **方向持久 + 撞到住所 ±4 格边界才折返**
  （原实现每步随机改向 + 随机步长，在 20Hz 同步下会变成原地抖动）；`WorldNpc.WanderDirection` 记录方向。
- **启动崩溃修复**：`MetricsHttpServer.Dispose` 吞掉未成功启动时的 `ObjectDisposedException` /
  `InvalidOperationException`，不再掩盖真实启动错误。

**测试**：+2（**287 / 287 通过**）——
`TownNpc_WalksSmoothly_WithoutPerTickDirectionFlip`（240 tick 内方向翻转 ≤ 4 次、不越出住所 ±4 格、单 tick 位移 ≤ 0.75px）、
`Vanilla_NpcSync_SendsOnChange_AndSkipsUnchanged`（首次必发 / 未变化不重发 / 位置变化立刻发）。

**仍未修（下一轮）**：

- **向导落位**：生成时 `Y = (spawnGroundY - 2) * 16` 按碰撞盒左上角语义算，脚底比地面低 8px（陷进地表半格）；
  且城镇 NPC 游走**只有 X、没有重力 / 图格落地**，出不来。
- **未碰怪却掉血**：服务端用自己 1Hz / 断续的 NPC 位置判 32px 接触，与客户端画面不一致；
  且史莱姆 `X`（格中心）与 `Y`（格顶边）语义混用，判定圆心偏移约 (8,8) px。另有 `fall_damage` 通路待复现区分。

### 第二十一轮（2026-09-12）：未建模包「拒绝但不计违规」+ 按 PacketId 统计（真机测试前置）

**问题**（为「原版客户端真机进图游玩」做的前置修正）：踢出判定对**所有**权威拒绝一视同仁 ——
`NetworkHost` 在 `Reject/RejectSilent` 分支无条件累计违规窗口（默认 **10 次 / 60 分钟** → 踢出）。
而 [AuthoritySubsystems.cs](../terraauth/Authority/AuthoritySubsystems.cs) 对**未建模包**统一 `Reject("unknown_packet")`，
正常原版客户端会持续发这类包（表情 / 家具 / 告示牌 / 部分 NetModule / 物品使用…）→ **正常玩家累计 10 次即被误踢**。
这不只是测试障碍，更是线上误判风险。

**已实施**：

- **`AuthorityResult.CountsAsViolation`（默认 true）**：显式标注该拒绝是否参与违规累计；
  `Reject(reason, countsAsViolation: false)` 供「客户端行为噪声」使用。
  `UnknownPacket` → `Reject("unknown_packet", countsAsViolation: false)`。
  语义边界：作弊语义（超速 / 超伤 / 洪水 / 非法堆叠…）**必须保持 true**，未建模包才置 false。
- **`NetworkHost` 提按 `PacketId` 统计**：`UnmodeledPacketCounts`（并发字典）+ 首次出现必打印 / 每 100 次打印 / **停机汇总**（按次数倒序），
  用于真机测试后拿到「客户端实际发了哪些未建模包」的清单，据此决定哪些登记为中继、哪些需要权威建模。

**测试**：+1（**285 / 285 通过**）—— `Vanilla_UnmodeledPackets_Are_Counted_But_DoNotCause_Kick`
（25 个未建模包：被统计 25 次、运行时仍在线、后续合法移动仍被权威应用）。
**反向验证过**：临时把 `countsAsViolation` 改回 true 重跑，用例如预期失败（计数停在 10 —— 第 10 个包即踢出），
证明用例确实守住该边界；`AntiCheat_PacketFlood_IsRateLimited_AndEventuallyKicked` 仍通过（真实违规照旧踢出）。

### 第二十轮（2026-09-12）：服务端兼容性测试 —— 修掉 `.wld` 加载失败的缺陷

**背景**：此前 `.wld` 的互操作性验证尚未完成。本轮补充服务端兼容性测试，验证**导出 → 加载**流程。

**发现并修复的缺陷**：兼容性测试加载本服务端导出的世界时报

```
System.FormatException: Found invalid file type.
   at Terraria.IO.WorldFileData.SetAsActive()
```

根因：`.wld` 的 20 字节文件元数据实际是 **`UInt64`（低 56 位 = 魔数 `"relogic"`，**最高字节 = 文件类型**）+ `UInt32` Revision + `UInt64` 旗标**；
格式要求最高字节为 `FileType.World = 2`。写出器此前把整个 `UInt64` 写成低 56 位魔数（最高字节 = 0 → `FileType.None`），
而本服务端读取器只校验 `& 0x00FFFFFFFFFFFFFF`（只比较低 56 位）→ **自测 round-trip 永远发现不了**。

**已实施**：`WorldFileWriter` 写出 `MetadataMagicLow56 | ((ulong)FileType.World << 56)`；
新增回归用例 `Wld_Written_Metadata_Carries_WorldFileType`（钉住最高字节 = 2）。

**测试结果**：修正后服务端兼容性测试可加载本服务端导出的世界（11,175,496 字节，Small 4200×1200）：

```
Resetting game objects 100%
Loading world data: 100%     ← 11 段指针全部通过（LoadWorld_Version2 逐段位置断言）
Settling liquids 50%
Listening on port 7778
: Server started
```

即 **「导出 → 服务端加载」方向的 `.wld` 互操作性已确认**（此前仅有 round-trip + 分段走查）。

**反向验证（兼容 `.wld` → TerraAuth）**：把兼容性测试生成的世界（`autotest.wld`，Small 4200×1200，3,003,995 字节）配到
`ServerConfig.WorldPath` 开服：

```
[World] 已加载世界文件 .../autotest.wld：autotest 4200×1200，出生点 (2102,283)
```

无异常。**且这是强校验**：本服务端读取器对每段做**位置指针断言**
（`Expect(reader, positions[i])`：header → tiles → chests → signs → npcs → tile entities），并在 footer 校验标记 / 世界名 / WorldId。
全部通过意味着：**图格段（含原版 RLE 行程压缩）消耗的字节数与分段指针精确一致**，箱子 / 告示牌 / NPC 段也对齐 ——
若图格解码错一个字节，后续段的指针断言必然失败。

**据此确认：`.wld` 双向格式互操作性均已通过。**

**保真限制（新记录）**：读取器**跳过段 6..10**（图格实体 / 加权压力板 / 城镇管理 / 图鉴 / 创造之力），直接跳到 footer。
因此「原版世界 → TerraAuth → 再导出」会把这些段写成**合法空编码** —— 新生成世界这些段通常本就是空，但**玩过的世界会丢该数据**。

**仍未验证**：原版**客户端**进入本服务端并正常游玩。

**测试**：+1（**284 / 284 通过**）—— `Wld_Written_Metadata_Carries_WorldFileType`。

### 第十九轮（2026-09-12）：文档与代码一致性核对（进度修正）

**做法**：逐份核对全部文档中的「数字 / 状态 / 行为」声明与当前代码，修正过期项；**不改动运行代码**。

**文档偏差（已修正）**：

- **测试数量**：`PROJECT_STRUCTURE.md` 的「263 用例」→ **283**（实跑 `dotnet test TerraAuth.sln -c Release` = 283/283 通过，42s）。
- **测试文件数**：多处「7 个测试文件 / 7 组」→ **9**（`Tests/TerraAuth.Tests.csproj` 显式 `Compile` 列表实为 9 项）。
- **`PacketId` 常量数**：39 → **41**（`Protocol/PacketId.cs` 实际成员数）。
- **编解码覆盖**：入站「31 / 28 个」→ **35 个**；出站「32 类」→ **37 类**（按 `PacketDecoder` / `PacketEncoder` 实际 case 统计）。
- **持久化后端表述**：多处「默认内嵌 LiteDb / 可选 SQLite」→ **默认 SQLite（`USE_SQLITE`），`-p:NoSqlite=true` 降级 LiteDb**；删除 `SqliteImpl`「骨架」表述（第二轮已完整实装）。
- **Vanilla-only 边界**：`Net/Transport/README.md`（原同目录 `DELIVERY.md` 已并入该 README 并删除）中「未建模包透明透传 / 编码侧原样写回 / 编解码无缺口」→ 改为与代码一致（**未建模包默认拒绝**）。
- **背压表述（第十九轮历史快照）**：「`CommandQueue` 满 → 丢弃最旧」「`Channel` 满 → 跳过增量快照」→ 当时记录为出站有界 `Channel(2048, FullMode = Wait)`、入站无界且上限待办；**后续已落地入站 `CommandQueue.MaxCount`、分片入站与审计队列有界策略，当前状态见本文 §三第 7 项。**
- **文件树**：补 `WorldGenerator.cs` / `WorldFileWriter.cs` / `WorldEntities.cs` / `Authority/CommandService.cs`。
- **`OPTIMIZATION_BACKLOG.md` §三**：把第十七轮列出的 12 项按「已修正 / 部分修正 / 仍待修正」重新标注（此前全部标为待办，与代码不符）。

**代码侧核对结论（未改动，作为上述标注的依据）**：

- `CommandQueue` 在第十九轮核对时确为 `PriorityQueue<Command, (long Tick, long Sequence)>`（当时无界）；**当前已增加可配置 `MaxCount`，生产上限为 8192。**
- `Connection._outbound` 确为 `CreateBounded(2048)` + `FullMode = Wait`。
- `PacketDecoder.DecodeNetModule` 确在 `new List<LiquidChange>(count)` **之前**校验 `maxChanges = 128` 与剩余长度。
- `SnapshotConfig.MaxEntitiesPerPacket` 仅有定义，**全仓无引用**（仍为待办）→ **已落地**（见 §三 第 11 项标注）；`SplitFrame` 按上限拆分子帧，编码层逐包发送。
- `ShardedInboundPipeline._queue` 与 `SqlitePersistence._auditChannel` 在第十九轮核对时均为 `CreateUnbounded`（历史快照）；**当前已改为有界队列，容量与过载策略见本文 §三第 7 项。**

**下一步建议**（按收益 / 风险排序，均属 §三「仍待修正」）：

1. **提交后广播一致性**（§三 第 6 项）：让实体 / 图格 / 箱子广播绑定「已提交状态」，避免客户端先看到未确认结果——与既有「发送成功后才置位」重试机制衔接。
2. **入站 / 审计队列背压（第十九轮历史建议）**：为分片入站队列与审计 `Channel` 加容量上限 + 过载策略；**已完成，当前状态见本文 §三第 7 项。**
3. **快照实体分包**（§三 第 11 项）：**已完成**。`SplitFrame` 按 `MaxEntitiesPerPacket` 拆分实体为多子帧、移除项并入首份，编码层逐包发送；`SnapshotStore` 改为定长环形数组（Add 满时覆写最旧 / TrimBefore 前移 head，无元素搬移），含绕环 + Trim 顺序测试。
4. **玩法向补全**（`VANILLA_COVERAGE.md` §二）：树木 / 生命水晶 / 生物群系 / 结构体、液体压力模型、电路门·定时器·压力板、敌怪远程弹幕与更完整的掉落库。
5. **`.wld` 双向互操作已通过**（第二十轮：导出 → 原版；原版 → TerraAuth），剩余为「原版**客户端**真的进图游玩」；
   以及可选补全：读取段 6..10（图格实体 / 图鉴等）以避免「原版世界 → 再导出」丢段。

### 第十八轮（2026-09-12）：广播失败重试 · 箱子会话生命周期 · 拾取并发 · 事件模型

**问题**（以「状态变更会不会丢」为线索）：

1. **实体广播失败即丢更新**：掉落物新增 / 移除、弹幕销毁、玩家死亡 / 复活都是**发送前**就把
   `NewNotified` / `RemovalNotified` / `DeathNotified` / `RespawnNotified` 置位；发送抛异常（连接断开、
   出站队列已关闭）后这些状态变更**永久丢失**——客户端再也收不到最终结果（只有箱子路径做了重新入队）。
2. **箱子会话只会「换」和「断线关」**：缺少「关箱」与「离开范围」两条收尾路径，会话可能长期驻留。
   （协议核对结论：原版客户端关闭箱子时只清本地 `player.chest`，**不发任何包**；包 31 只用于开箱，
   服务端以包 80 告知 `player.chest` 值。）
3. **拾取并发无回归测试**：实现本身是原子的（同一把 `ItemsLock` 内入库 + 失效），但无断言保护。
4. **事件模型单一**：`GameEvent` 只有 `(Tick, PlayerId, Kind, object? Payload)`，状态提交 / 持久化 / 广播 /
   失败四类语义混在 `Kind` 字符串里，失败原因散落为字面量。

**已实施**：

- **实体广播改为「成功后才置位」**：`FlushNewItemsAsync` / 弹幕销毁 / 掉落物移除 / 玩家死亡复活
  全部改为 `await 广播` 成功后再置位标记；`IsTransientSendFailure`（`IOException` /
  `ObjectDisposedException` / `ChannelClosedException`）时仅记录 `broadcast_failed` 事件并在下一轮重试；
  `OperationCanceledException` 仍向上传播；箱子路径保持「重新入队」语义不变。
- **箱子会话生命周期补全**：包 31 负坐标 → `CloseChestCommand`（在仿真提交阶段按会话代数关闭，
  旧连接关不掉复用同槽位的新连接会话）；`WorldSimulator` 每 tick 做**距离复核**
  （`SnapshotChestSessions` + 箱子中心 160px），离开交互距离即关闭；玩家已离线或箱子不存在时兜底关闭。
- **拾取并发回归**：新增「背包满 → 掉落物保留且不进入待移除」「越界拾取不改状态」
  「两玩家并发抢同一掉落物只成功一次」三项断言。
- **事件模型分层**：`GameEventCategory`（`StateCommit` / `Persistence` / `Broadcast` / `Failure`）+
  `GameEventKinds` + `CommandFailures` 常量；命令失败原因常量值与既有字符串**逐字一致**
  （`stale_session` / `inventory_full` / `chest_not_open` …），指标与测试断言不受影响；
  图格 / 箱子落盘失败、实体 / 箱子广播失败均记录事件。

**协议核对结论（只读，不含工具链 / 路径）**：包 31 为 `Int16 x + Int16 y`；服务端仅在坐标命中箱子时
下发内容并回包 80；客户端关闭箱子不发包；包 34 是「放置 / 破坏箱子等世界结构变更」，不是「玩家当前打开的箱子」。

**测试**：+7（关箱会话 / 旧会话关不掉新会话 / 离开距离关闭会话 / 背包满保留 / 越界拾取 /
并发拾取只成功一次 / 包 31 负坐标为关箱请求）；越界开箱用例改用正数越界坐标。全量 **283 / 283 通过**。

### 第十七轮（2026-09-12）：连接会话隔离（SessionId）+ 箱子会话绑定

**问题**：连接槽位（`PlayerId`）按「最小空闲 ID」复用后，**旧连接的异步收尾会与新连接共享同一个 `PlayerId`**：

1. 旧连接的命令已入队但尚未提交，此时断开 → 新连接复用槽位 → 旧命令在下一次 `Tick` 仍按 `PlayerId` 通过校验，
   改的是新玩家的状态（位置 / 拾取 / 箱子）。
2. 旧连接的断线清理（移动基线重置、离线会话回收、箱子会话关闭、离开广播、违规踢出）同样只按 `PlayerId`，
   会把**新连接**的状态当成自己的清掉或踢掉。
3. 箱子打开会话此前是 `PlayerId → ChestIndex`，A 打开、A 断线、B 复用槽位后，B 的箱子写入会被 A 的会话放行。

**已实施**：

- **内部会话标识 `SessionId`（不进协议、不入存档）**：每个 `Connection` 实例一个唯一 `long`，
  贯穿 `PacketContext.SessionId` → `Command.SessionId` → `PlayerRuntime.SessionId`，
  命令在 `Apply` 阶段比对，不一致即返回 **`stale_session`** 拒绝（`TryGetPlayer` 统一入口）。
- **玩家运行时在认证完成时显式创建**（带 `SessionId`）：此前依赖首个移动命令惰性创建，
  与「命令必带会话标识」冲突 —— 认证后首个命令因运行时不存在而无法应用（曾导致大批端到端用例失败）。
- **会话恢复同时换代**：`TryResumePlayer(newPlayerId, newSessionId, resumeKey)` 认回离线运行时的同时
  **覆盖为新连接的 `SessionId`**，旧连接的在途命令随即失效。
- **断线清理按代数收窄**（`expectedSessionId` 参数）：移动管线 `ResetPlayer`、`MarkPlayerOffline`、
  `CloseChestSession`、外观 / 会话时长 / 违规窗口缓存、离开广播（按 **Connection 实例**双匹配）
  均只在「槽位仍属于本连接」时生效。
- **箱子会话升级为 `(PlayerId, SessionId) → ChestIndex`**；包 31 不再在校验阶段直接改世界，
  而是生成 **`OpenChestCommand`**，由 `WorldSimulator.Tick()` → `Command.Apply()` 在提交阶段建立会话，
  与「客户端包 → 权威校验 → 命令 → 仿真提交」链路一致；箱子广播按 `Connection.SessionId` 筛选接收者。
- **移除重复判定**：`BroadcastChestUpdateAsync` 的状态 / 会话检查由两遍收敛为一遍。

**测试**：新增 4 项 —— `StaleSessionOfflineCleanup_PreservesReplacementChestSession`、
`MatchingSessionOfflineCleanup_ClosesOwnChestSession`、`ChestSession_RejectsStaleConnectionAndPreservesReplacement`、
`StaleConnectionKick_DoesNotAffectSlotReplacement`（真实 TCP 下用旧 `Connection` 实例踢出 / 移除复用同槽位的新连接，
须被双匹配拦住且新连接收不到包 2）；会话恢复用例补断言「重连后 `SessionId` 已更换」。
全量 **276 / 276 通过**。

### 第十六轮（2026-09-12）：地形生成器重写 + 包 13 尾随字段 + 箱子索引修复

**问题**（用户指定两项）：

1. **地形生成器过于简陋**：原实现只有「正弦地表 + 草/土/石 + 背景墙 + 一名向导」，
   没有洞穴 / 矿脉 / 海滩 / 地狱层 / 宝箱 —— 玩家进服后无处可探索、无矿可挖。
2. **包 13 转发丢失可选尾随字段**：解码时把挂载类型 / 回城双坐标 / 相机目标**读后即丢**，
   编码时又强制清掉对应标志位 → 他人看不到坐骑 / 相机 / 回城表现。

**已实施**：

- **地形重写**（层高比例 / 图格 ID / 摆放约定均经协议行为验证）：
  - **层高**：地表基准 = 高 × 0.30 × (0.90~1.10)；岩层 = (地表 + 高 × 0.20) × (0.90~1.10)；
    地表钳制在 `[高 × 0.17（小世界 +0.02）, 高 × 0.26]`；`worldSurface` = 最高列 + 25；
    `rockLayer` 与 `worldSurface` 的差对齐到 6 的倍数；海平面 = (地表 + 岩层) / 2 + 40；地狱层 = 底部 200 格。
  - **地表起伏**：改用**确定性哈希值噪声**（多倍频 fBm + 平滑插值），不再用固定正弦叠加，且不依赖 `System.Random`。
  - **地层**：草皮 / 泥土（含黏土与沙斑）/ 岩层（石头 + 石墙）/ 地狱层（灰烬 + 狱石）。
  - **洞穴**：脊状噪声挖隧道 + 深层空腔；**出生点半径 16 格内不挖洞**（防出生即坠落）。
  - **矿脉**：按深度分带（浅 铜/铁 → 中 铁/银 → 深 银/金），随机游走成脉，只替换泥土 / 黏土 / 沙 / 石。
  - **海滩 + 海水**：两端海滩，越靠地图边缘越深，海平面到沙面之间灌满水。
  - **宝箱**：**2×2 摆放 + 原版帧约定**（`frameX ∈ {0,18}`、`frameY ∈ {0,18}`，实体登记在左上格，
    下方两格须实心），并带随机战利品（硬币 / 火把 / 木材 / 治疗药水 / 手里剑，物品 ID 按协议定义）。
- **包 13 尾随字段保真**：解码侧**保留**挂载类型 / 回城双坐标 / 相机目标；编码侧**按字段是否存在**校准
  三个可选位并写出尾随字段（标志位与负载严格自洽，避免接收端多读 / 少读字节导致整连接错位）。
  → 他人可见坐骑、相机与回城表现。
- **顺带修复**：`.wld` 读取的箱子去重（原版 RemoveChest 语义）会移动列表位置，
  但 `Chest.Index` 未重新对齐 → `FindChestByIndex` 会错位；现在去重后按列表下标重排 Index。

**未包含（明确边界）**：树木（需要 tree 的 frame-important 图集帧映射）、生命水晶、
生物群系（雪原 / 沙漠 / 丛林 / 腐化）、地牢 / 神庙等结构体。

**历史测试记录**：+2（当时 **259 通过**）—— `Generate_Produces_Layered_Terrain_With_Ores_Caves_Ocean_And_Chests`
（断言矿脉 / 洞穴 / 草皮 / 地狱层 / 海水 / 宝箱数量与战利品）、
`Vanilla_PlayerControls_Relay_Preserves_Mount_And_Camera`（包 13 中继保留挂载与相机）；
另把两个受「世界生成变重」影响的既有用例等待窗口放宽（`KickAsync_...` 与 `AntiCheat_PacketFlood_...`）。
该历史阶段默认后端与 `-p:NoSqlite=true` 兜底后端均 259/259 通过；当前全量测试为 **306 / 306** 通过（第十九轮 283 + 第二十轮元数据回归 + 第二十一轮未建模包边界 + 第二十二轮 NPC 同步 / AI 步进 + 第二十三轮敌怪跳跃 + 第二十四轮原版 aiStyle 重建地基与 ai 下发 + 第二十五轮 aiStyle 31/43 / 服务端弹幕推送 / 弹幕行为表 + 第二十六轮失效实体回收与 NPC 落位修正 + 第二十七轮实体碰撞盒按原版口径对齐 + 第二十八轮跳探针 / 落地判定 / 可疑带 + 第二十九轮 NPC 重力与终速对齐原版 + 第三十轮接触最小重叠与统一免伤帧 + 第三十一轮玩家位置外推 + 第三十二轮接触阈值 8px 与 NPC 水平阻挡 + 第三十三轮玩家移动改为控制位模拟）。

### 第十五轮（2026-09-12）：协议字段核对后落地（包 20 图格方阵 + 区块流送对齐 + NPC 同步细节）

**做法**：以上一轮列出的「未确认项」为清单，逐项进行**协议字段布局核对与客户端互操作性验证**，
不再靠推测；验证结论直接落到实现里。

**1) 服务端驱动的图格改动改用包 20（TileSquare）** —— 落实上一轮标注「需实测再定」的协议改动：

- **核对结果**：协议行为表明「少量图格改动」走**包 20**（未压缩的小矩形，逐格位标志 + 可选段），
  **只有区块级地形下载**才走包 10；包 20 的字段顺序已通过互操作性验证。
- **线格式**（已完成协议字段核对）：`Int16 X + Int16 Y + Byte 宽 + Byte 高 + Byte 变更类型 + 逐格(3 个标志字节 + 可选段)`；
  逐格：b1 = 存在方块 / 存在墙 / 存在液体 / 线 / 半砖 / 执行器 / 未激活；b2 = 线2 / 线3 / 方块油漆存在 / 墙油漆存在 / 斜坡(bits4-6) / 线4；
  b3 = 全亮方块 / 全亮墙 / 隐形方块 / 隐形墙；随后按需写 方块油漆、墙油漆、类型（frame-important 再写 FrameX/Y）、墙、液体量+类型。
- **实现**：新增 `TileSquarePacket` 与编码器（含左上角钳制到世界内）；`FlushTileUpdatesAsync` 由「包 10 小矩形」改为**包 20**，
  并按 Byte 宽度上限（255）切分超宽的行矩形。

**2) 区块流送参数对齐原版**：

- 原版是**服务端按客户端位置主动补发**（每 tick 检查），以玩家所在区块为中心取 **(2×fluff+1)²**（fluff=1 → 3×3）方块，
  **逐客户端记录已发区块**（避免重复下发），并在有新块时先发**包 9（进度）**再逐块发包 10。
- 实现据此调整：流送矩形由 5×3 改为 **3×3**；下发前先统计「尚未下发」的块数并发包 9；去重沿用 `SyncedSections`。
- 顺带通过协议行为验证：登录期的「世界出生点 5×3 + 请求点 6×4」与包 8 的服务端处理完全一致（无需改动）。

**3) 包 23（NPC 同步）细节**：

- **ai 字段无需补发**：协议行为验证表明 —— ai 位未置位时客户端**显式把该 ai 置 0**，
  因此「省略 ai」与「发送 ai=0」等价；当前服务端无真实 NPC ai，保持省略即可（原判断成立）。
- **同步锚点补齐**：原版对「锚点非零」的 NPC 上报 `位置 + 体型 × 锚点`，客户端接收后再减去同一偏移。
  当前仅**史莱姆王（类型 50）**锚点为 (0.5,1)、体型 98×92 → 现在编码时补 (49,92)，修掉客户端画偏。
- **目标字段语义修正**：原版 NPC AI 以 `target == 255`（部分 AI 还含 `<=0`）判为「无目标」并自行选最近玩家。
  原先固定发 `0` 会被当成「目标 = 玩家槽位 0」→ 现改为发 **255（无目标）**。

**测试**：+1（**257 通过**）—— `Encode_TileSquare_Writes_Vanilla_Layout`（单格覆盖全部可选段：油漆 / 帧 / 墙 / 液体 /
线 / 执行器 / 斜坡 / 全亮 / 隐形，逐字段断言线格式）；`Vanilla_Actuate_Pushes_Tile_Update_To_Client` 改为断言包 20。
默认后端与 `-p:NoSqlite=true` 兜底后端均 257/257 通过。

**仍未落地**：世界内容（程序化生成仍是「可加载地形」，完整地形需真实 `.wld`）。

### 第十四轮（2026-09-12）：Play 阶段区块流送 + 移动权威垂直轴 + 未建模包中继

**问题**（本轮以「原版客户端能否正常游玩」为线索做代码核对时发现，此前文档均未列）：

1. **出生点以外没有地形**：包 8（`SpawnTileData`）只在握手期处理（`when State == Authenticating`），
   进入 Playing 后落到 `default: return State == Playing` → 进管线 → 无命令 → **丢弃**。
   而原版客户端是**边走边请求周边区块**的。结果：玩家离开登录时下发的出生区块后，远处地形在客户端为空。
2. **正常坠落被判超速**：移动权威只用水平上限算允许位移（`MaxSpeed = MaxFlightSpeed = 8` px/帧），
   而原版下落终速 `MaxFallSpeed ≈ 20 px/帧` 必然超出。拒绝时又**不更新基准、也不生成 MoveCommand**
   → 服务端位置停在原处（后续挖 / 放 / 开箱 / 拾取的 `IsWithinReach` 全部判 out_of_reach），
   且每次 Reject 计入违规窗口 → 默认 **10 次 / 60s 直接踢出** ⇒ **从高处掉落即被踢**。
3. **未建模包按 Vanilla-only 边界拒绝**：解码器无法结构化的包统一进入 `UnknownPacket`，由 Authority 拒绝；当前不开放未知包中继。

**已实施**：

- **区块流送**：`Connection` 记录 `SyncedSections`（已下发区块，避免重复 Deflate 编码）与 `LastStreamSection`；
  `NetworkHost.SendSectionOnceAsync` 统一「未发过才下发」；新增 `StreamSectionsForPlayersAsync`
  （由快照循环按 20Hz 调用，**仅当玩家跨越区块边界**时补发其周边 5×3 区块）；
  包 8 在 Playing 阶段也被处理（按请求点补发）。登录期出生区块路径复用同一去重逻辑。
- **移动权威分轴判定**：`MovementLimits` 新增 `MaxFallSpeed`（默认 20，由 `ServerConfig.MaxFallSpeed` 注入）；
  水平用 `MaxSpeed`，**垂直用 `max(MaxSpeed, MaxFallSpeed)`**；拒绝时仍保持基准不变（防瞬移污染）。
- **未建模包不进入生产中继**：已删除 `RelayToOthersAsync` 即时中继路径；未知包默认由 Authority 拒绝，状态包统一等待仿真提交后生成服务端同步包。

**历史测试记录**：+3（当时 **256 通过**）——`Vanilla_TileSections_Stream_As_Player_Moves`（移动后补发区块、位置未变不重复下发）、
`MovementAuthority_Accepts_FastFall_But_Still_Rejects_HorizontalTeleport`（垂直放宽但水平瞬移仍拒）、
`Vanilla_UnmodeledPacket_Is_Relayed_To_OtherPlayers`（历史中继行为，已被当前 Vanilla-only 默认拒绝策略取代）。
顺带把既有偶发用例 `AntiCheat_PacketFlood_...` 的等待窗口 5s → 15s（全量并行跑时踢出会变慢）。
默认后端与 `-p:NoSqlite=true` 兜底后端均 256/256 通过。

**未纳入**：**世界内容** —— 程序化生成仍是「可加载地形」（三档尺寸已支持），完整地形需 `WorldPath` 指定真实 `.wld`。
其余原「未纳入」项（NPC 同步细节、图格推送包号）已通过**协议字段核对与客户端互操作性验证**后于第十五轮落地。

### 第十三轮（2026-09-12）：程序化生成支持原版三档世界尺寸

**问题**：程序化生成器只有一档尺寸（小世界 4200×1200），且地表 / 岩层是**写死的行号常量**（360 / 720）。
要让原版客户端在「中等 / 大世界」下正常游玩（或按需选尺寸），既缺尺寸开关，也没有按高度缩放的地层。

**已实施**：

- **尺寸枚举 + 尺寸感知生成**：`WorldSize`（`Small` / `Medium` / `Large`）与 `WorldGenerator.Dimensions`，
  对应原版三档：小 4200×1200、中 6400×1800、大 8400×2400；
  `WorldGenerator.Generate(WorldSize, worldName, seed)` 统一入口，`GenerateSmall(...)` 保留为 `Small` 的便捷入口。
- **地层按高度比例缩放**：地表 / 岩层由写死行号改为 `高度 × 0.30 / 0.60` —— 小世界仍为 360 / 720（**行为不变、确定性不变**），
  中 / 大世界自动得 540/1080、720/1440，避免大世界地表偏上、岩层过浅。
- **配置化**：`ServerConfig.WorldSize`（server.json 用字符串枚举，沿用 `JsonStringEnumConverter`）；
  `GameHost.LoadBaseWorld` 按配置选择尺寸；仅当 `WorldPath` 为空 / 文件不存在时生效（指定真实 `.wld` 时以文件尺寸为准）。
- **协议量纲核实**：包 7（WorldInfo）的 `MaxTilesX/Y`、`WorldSurface`、`RockLayer`、`SpawnTileX/Y` 均为 **Int16**，
  大世界 8400 / 2400 / 1440 全部落在 `±32767` 内，无需改协议。

**边界说明**：本次只是把尺寸与布局打通，**地形内容仍是「可加载地形」**（正弦地表 + 草/土/石 + 背景墙 + 一名向导 NPC），
**不含矿石 / 洞穴 / 生物群系 / 树木 / 地牢 / 生命水晶** —— 完整地形请用 `WorldPath` 指定真实 `.wld`。
大世界约 2000 万图格，`Tile` 约 26 字节 → 内存约 0.5 GB（中世界约 0.3 GB、小世界约 0.13 GB）。

**测试**：+4（**253 通过**）——`Generate_Supports_All_Vanilla_Sizes`（Theory 三档：尺寸 / 区块数 / Int16 范围 / 出生点贴地 / 包 7 字段）、
`Vanilla_WorldSize_Config_Generates_Medium_World`（配置字符串枚举 → 中世界生成 → 登录链仍完成）。
默认后端与 `-p:NoSqlite=true` 兜底后端均 253/253 通过。

### 第十二轮（2026-09-12）：箱子内容持久化（W-1 解除阻塞项）

**问题**：第九轮评估箱子内容持久化时，因「基准世界恒为程序化生成 → 世界里没有箱子」而判定落盘路径不可达、未留死代码，
记为「待 W-1（加载真实 `.wld`）落地后一并补」。第十轮已落地 `.wld` 加载 → **阻塞解除**：
真实世界自带箱子，玩家开箱取放（包 32）会改服务端箱子内容，但此前该改动只存在内存，**重启即丢**（与修复前的「重启丢建筑」同性质）。

**已实施**（沿用图格增量落盘的同构设计）：

- **脏集登记**：`WorldState` 新增箱子待落盘集合（`_pendingPersistChests`，受 `WorldPersistLock` 保护）与
  `MarkPersistChest` / `DrainPersistChests`；`HasPendingPersist` 一并纳入箱子。
  `SyncChestItemCommand.Apply`（包 32 权威通过后）在写入服务端箱子后登记该索引。
- **序列化**：`Chest.SerializeItems` / `DeserializeItems` —— 「Int32 格数 + 每格定长 7 字节（Type/Stack/Prefix）」，
  与自身严格对称；反序列化对非法格数做防御（返回空数组，避免损坏数据导致崩溃）。
- **持久化层**：`IWorldRepository` 新增 `SaveChestChangesAsync` / `LoadChestChangesAsync`（`WorldChestRecord`：索引 + 坐标 + 字节串）；
  SQLite 新增 `WorldChests` 表（单事务批量 upsert），内嵌兜底后端同步 `_worldChests` + JSON 段。
- **回放**：`GameHost.ApplyPersistedWorldChanges` 在回放图格后回放箱子 —— 按**索引定位 + 坐标校验**
  （基准世界被替换时坐标不符则跳过，避免错位套用）。
- **落盘**：`FlushWorldChangesAsync` 追加 `FlushChestChangesAsync`（同一 1Hz 循环与停机冲刷路径）；
  失败**重新排队**（同图格语义，避免「取出即丢」）。

**测试**：+1（**249 通过**）——`Vanilla_ChestContent_Survives_ServerRestart`：
造一份含箱子的 `.wld` 作为基准世界 → 经真实包 32 权威链路写入物品 → 落盘 → **复用同一 DB 重启** → 箱子内容被回放。
默认后端与 `-p:NoSqlite=true` 兜底后端均 249/249 通过。

### 第十一轮（2026-09-12）：断线宽限期会话保留 + 连接槽位回收

**问题**（两项，均为本轮回溯自查发现）：

1. **断线即销毁**：连接断开时玩家运行时被直接释放，位置 / 血量 / 增益全部丢失，
   重连只能从世界出生点以满血重新开始 —— 对「短暂掉线后重进」的体验是硬伤。
2. **连接槽位从不回收（真实缺陷）**：`ConnectionManager` 只在 `KickAsync` 里移除连接，
   **正常断开的连接会永久留在 `_connections`** —— 槽位（`PlayerId`）与并发容量都不释放。
   后果：玩家 ID 只增不减（与原版「复用最小空闲槽位」不一致），且累计达 `MaxConnections` 次连接后新玩家被拒之门外。
   该缺陷由本轮新增测试暴露（重连拿到的是新槽位，而非原槽位）。

**已实施**：

- **会话保留（宽限期）**：`WorldState` 新增离线会话表（`_offlineSessions`，受 `PlayersLock` 保护）。
  断线时 `MarkPlayerOffline` 把运行时移出在线集合、置 `Active=false`、清零速度，并按
  `ServerConfig.SessionResumeGraceSeconds`（默认 60s）记录回收截止 tick；宽限期为 0 即直接回收（同时也修掉「运行时不释放」的泄漏）。
- **同身份接管**：`TryResumePlayer` 在宽限期内按**恢复键（玩家名）**认回运行时 ——
  位置 / 血量 / 增益原样交还，改挂到新连接分配到的槽位；出生包（12）改发**恢复后的坐标**（而非世界出生点），客户端落回原位。
- **过期回收**：仿真循环每 tick 调 `ReapOfflineSessions` 回收超期会话（含被顶号场景），不留幽灵运行时。
- **移动基线清理**：断线时经 `IInboundPipeline.ResetPlayer` 清掉该槽位的移动校验基线。
  分片管线把「按玩家重置」投递到**同一队列 / 同一分片**，保证该玩家在途包先处理完再清状态 ——
  否则在途的旧位置包会把基线重新写回，重连玩家首个位置包仍被判超速。
- **连接槽位回收**：`NetworkHost.RunConnectionAsync` 在连接结束时调用 `_connections.RemoveAsync`（释放槽位 + 并发容量）；
  回收先于退出清理，使重连能拿到同一槽位。`RemoveAsync` 增加「键 + 实例」双匹配（`ICollection.Remove`），
  避免槽位已被新连接复用时**误杀新连接**（被踢连接在 `RunAsync` 结束后会再次走到该路径）。

**与原版的差异（明确限制）**：原版客户端**断线即回主菜单**（`Netplay.Disconnect=true`），服务端无任何自动重连机制；
故本实现是「**手动重进的会话接管**」（宽限期内以同身份重连 → 继承状态），而非自动重连 / 状态续传。

**测试**：+3（**248 通过**）——`Vanilla_SessionResume_Restores_Position_And_Hp`（断线后同身份重连续回位置 / 血量，出生包携带恢复坐标）、
`Vanilla_SessionResume_Off_When_Grace_Is_Zero`（宽限期 0 → 不保留，重连为全新满血运行时）、
`SessionResume_Expires_After_Grace`（越期回收 + 不再认回）。默认后端与 `-p:NoSqlite=true` 兜底后端均 248/248 通过。

### 第十轮（2026-09-11）：世界文件（`.wld`）加载 / 导出 + 增量落盘加固（含性能实测）

**问题**（三项）：

1. 基准世界恒为程序化生成 —— `.wld` 解析器虽已实现却从未被使用，**无法在真实世界上开服**（W-1）。
2. 只有增量落盘、没有可携带的整份产物：备份 / 迁移 / 换基准世界都缺一环。
3. 增量落盘的**待处理集合无上限**，单批上限写死 2048，超大范围改动会排队过久。

**已实施**：

- **基准世界可配置**：`ServerConfig.WorldPath` 指定 `.wld` 即加载（为空 / 文件不存在则回退程序化生成并打印提示）；
  `Bootstrap` 抽出 `LoadBaseWorld` 统一入口。世界改动持久化对新基准同样生效（增量叠加）。
- **`.wld` 写出器**（`Simulation/World/WorldFileWriter.cs`）：与读取器**布局严格对称**的编码器 ——
  文件元数据 + 11 个分段指针（先占位、写完回填）+ 图格重要度位图 + 头部（含全部世界旗标）+ 图格 + 箱子 + 告示牌 + NPC + footer。
  图格编码支持全部特征位（方块 / 墙（含高位）/ 液体四类 / 四色线 / 执行器 / 斜坡 / 半砖 / 油漆 / 隐形 / 全亮 / 双字节类型）。
- **写后校验 + 原子替换**：`Write()` = 序列化到内存 → 写临时文件 → **用自身读取器读回校验** → 通过才替换目标，
  替换前把旧文件滚动为 `.bak`；校验失败保留原文件并抛错，避免写出「读不回来」的世界。
- **导出触发**：`WorldExportPath` 非空时，停机导出一次（`force`），并在**无人在线**时按
  `WorldExportIntervalSeconds`（默认 600s）周期导出 —— 全量序列化是 O(世界大小)，故有意避开在线时段；
  在线可靠性仍由增量日志负责。
- **增量落盘加固**：单批上限 2048 → **8192**（`WorldState.PersistBatchSize`）；
  待处理集合加上限（262144，约 1 MB），**溢出时降级为「全图游标扫描」模式**（内存有界、最终一致，走完一遍自动回到正常模式）；
  新增 `HasPendingPersist`，停机时**循环冲刷到排空**（轮数有上界，不会挂死）。
- **性能实测**（小世界 4200×1200，见下表）用于选型：增量成本 ∝ 改动量，全量成本 ∝ 世界大小。
- **测试**：+4（**244 通过**）——`Wld_Write_Then_Read_RoundTrips_All_Tile_Features`（64×32 特征世界逐格往返，
  覆盖全部标志位 / 液体 / 油漆 / 高位墙 / 双字节类型）、`..._Header_Flags`（世界旗标 / 模式 / 时间 / 沙尘强度）、
  `Vanilla_WorldFile_Is_Used_As_Base_World`（配置 `.wld` → 开服基准世界来自文件且登录链正常）、
  `Vanilla_World_Is_Exported_To_Wld_When_Configured`（导出落地并可被读回；未配置则不导出）。
  默认后端与 `-p:NoSqlite=true` 兜底后端均 244/244 通过。

**性能实测数据**（用于方案选型）：

| 场景 | 实测 |
|---|---|
| 增量：无改动批次 | 0.00 ms |
| 增量：满批落盘（8192 格，SQLite） | ≈ 4×23.5 ms（线性于批大小） |
| 增量：登记一格 | ≈ 45 ns |
| 全量：遍历 504 万格 + 序列化 | ≈ 178 ms（含每格分配；真实写出器可复用缓冲更快） |
| 全量：Deflate 压缩（该世界 72 MB → 1 MB） | ≈ 28 ms |

**本轮补正（接续第十轮）**：

- **修掉一处会让原版拒绝加载的缺陷**：原先段 5..9（图格实体 / 加权压力板 / 城镇房间管理 / 图鉴 / 创造之力）
  的位置全部指向 footer（即「空段」）。本服务端读取器会跳过这些段，但**协议加载流程逐段读取并要求
  「读完正好落在下一段起点」**，空段会被判 `BadSectionPointer`。现按各段语义写出**合法空编码**：
  三段各一个 `Int32 0`、图鉴三个 `Int32 0`、创造之力一个 `false`。
- **更强的验证**：新增 `Wld_Written_File_Passes_Strict_Section_Walk` —— 按**协议加载顺序与位置断言**逐段走查
  （段起点严格递增、每段读完正好落在下一段起点、footer 读完正好用尽文件），补上 round-trip 覆盖不到的 5..9 段。
- **客户端互操作性验证未完成**：当前沙箱环境无法运行目标服务端 ——
  用本服务端导出的世界启动会崩溃，**用原版 `-autocreate` 自建世界启动同样崩溃、且零输出**，
  故判定为**环境限制而非文件格式问题**；完整互操作性验证仍待在可运行目标服务端的环境执行。

**测试**：245 通过（含上述严格走查）。默认后端与 `-p:NoSqlite=true` 兜底后端均 245/245。

### 第九轮（2026-09-11）：世界改动持久化（重启不丢建筑）+ 偶发测试加固

**问题**（本轮自查发现，此前文档未列）：服务端只有 `.wld` **读取器**、没有写回，且 `Bootstrap` 恒走程序化生成 ——
**玩家挖 / 放 / 灌液体 / 铺线在服务端重启后会全部丢失**，这对任何长期开服都是硬伤。

**已实施**：

- **世界改动增量落盘**：新增 `IWorldRepository`（`SaveTileChangesAsync` / `LoadTileChangesAsync`）与
  `WorldTiles` 表（SQLite）/ `WorldTiles` 段（内嵌兜底），随 `IDbExecutor` 双后端实现。
  图格以 **15 字节定长编码**（`Tile.Serialize` / `Deserialize`）存储，一份记录含方块 / 墙 / 液体 / 电线 / 执行器 / 油漆 / frame。
- **登记点**：`WorldState.MarkPersistTile` 挂在**所有真实写入点** —— 客户端发起的 `TileBreakCommand`（挖 / 放砖 / 墙 / 线网 / 执行器）、
  `TilePlaceCommand`，服务端驱动的执行器翻转（`MarkTileChanged`）、液体流动与混合反应（`MarkLiquidChanged`）。
  待落盘集合按坐标去重（`HashSet`），**取出即移除**；落盘失败会**重新排队**重试，避免「取出即丢」。
- **回放**：`Bootstrap` 生成基准世界后调用 `ApplyPersistedWorldChanges`，把上次运行落盘的图格叠加回去
  （基准世界确定性 → 只需存增量，无需写回 `.wld`）。
- **落盘时机**：1Hz 世界同步循环（单批上限 2048 格，超限留待下轮）+ `RunAsync` 退出时冲刷一次。
- **偶发测试加固**：`VanillaSession.ReadUntilAsync` 补 `IOException` / `SocketException` 处理
  （服务端踢出连接的瞬间还在读 socket 会抛异常，导致 `AntiCheat_PacketFlood_...` 偶发失败）。
- **测试**：+1（**240 通过**）——`Vanilla_WorldEdits_Survive_ServerRestart`（挖一格 → 落盘 → **复用同一 DB 重启** → 该格仍为空）。
  默认后端与 `-p:NoSqlite=true` 兜底后端均 240/240 通过。

**已评估但未纳入**：箱子内容持久化 —— 当前基准世界**不含箱子**且包 34（放置箱子）未建模，落盘路径不可达，
故不留死代码；待 W-1（加载真实 `.wld`）落地后一并补。

**新增立项**：W-1「支持加载真实 `.wld` 世界」——`WorldFileReader` 已实现却从未被 `Bootstrap` 使用，目前无法在真实世界上开服。

### 第八轮（2026-09-11）：反作弊闭环补全 —— 法力 / 治疗 / 增益 / 弹幕生成权威化

**问题**：项目定位是「把客户端可伪造的状态收回服务端」，但仍有四个包**只做限流、不做语义校验**：
包 42（法力）、包 35（治疗）、包 50（增益）、包 27（弹幕生成）。其中包 27 的伤害直接来自客户端声明，
可被用来一击秒杀任意敌怪；法力则完全未在服务端跟踪（玩家快照里 `Mp / MaxMp` 恒为 0）。

**数值核对**（按协议 326 逐项核对，仅记录结论）：
增益槽位数 44；有效增益 ID 区间 [1, 400]；有效弹幕类型区间 [1, 1135]；
包 16 线格式仅含生命（不含法力），法力单独走包 42（`Byte 玩家 + Int16 法力 + Int16 上限`）。

**已实施**：

- **法力服务端跟踪（包 42）**：新增 `PlayerManaPacket` + 编解码 + `SetManaCommand`；权威层 `ValidateMana`
  非负校验、上限由服务端持有（超出即「纠正」下发权威包）、当前法力不得高于上限。按协议语义**不向他人转发**。
  玩家快照（`IServerApi`）的 `Mp / MaxMp` 由恒 0 改为真实值。
- **治疗上限钳制（包 35）**：权威层非负校验（负治疗拒绝）→ `HealPlayerCommand` 把回血上限钳制到服务端 `HpMax`，
  客户端声明的超额治疗不会让服务端生命越界。
- **增益服务端持有（包 50）**：权威层校验条目数 ≤ 44 且 ID ∈ [1,400]（越界拒绝）→ `SetBuffsCommand`
  把列表写入服务端玩家状态（唯一真相）。
- **弹幕生成校验（包 27）**：权威层新增 `ValidateProjectile` —— 弹幕类型须在 [1,1135]，伤害超配置单次伤害上限
  （与包 28 共用阈值）即判为作弊**拒绝**，关闭「声明 32767 一击秒杀」的通道；通过后仍由服务端登记实体并推进生命周期。
- **文档一致性**：修正 `VANILLA_COVERAGE.md` §二「无 NPC 专属 AI 与接触伤害」与 §一 已实装接触伤害的自相矛盾。
- **测试**：+8（**239 通过**）——法力跟踪 / 法力上限纠正 / 治疗钳制 / 负治疗拒绝 / 增益持有 / 越界增益拒绝 /
  超上限弹幕伤害拒绝 / 越界弹幕类型拒绝。默认后端与 `-p:NoSqlite=true` 兜底后端均 239/239 通过。

**仍为简化模型**：包 27 伤害仍由客户端声明，服务端只做**上界校验**（原版伤害由武器 / 装备推导，未建模武器表）；
增益的**效果**仍由客户端计算（服务端只维护列表）；治疗来源未做「是否有合法治疗来源」的溯源。

### 第七轮（2026-09-11）：Boss 掉落 + 液体混合反应（阻塞项解除）

**问题**：第六轮遗留两个「因缺原版数值 ID 而不敢写」的阻塞项 —— Boss 掉落表与液体混合反应。

**核对**：按客户端协议 326 的字段与数值逐项核对（仅记录结论）：
NPC 类型 ID（眼魔 4 / 世界吞噬者 13·14·15 / 骷髅王 35 / 史莱姆王 50 / 小丑 109 / 血肉墙 113 / 双子 125·126 /
机械骷髅王 127 / 毁灭者 134 / 蜂后 222 / 石巨人 245 / 世纪之花 262 / 猪龙鱼公爵 370 / 月亮领主 398 / 邪教徒 439）、
物品 ID（凝胶 23 / 恶魔矿 56 / 蜂蜡 2431）、图格 ID（黑曜石 56 / 蜂蜜块 229 / 松脆蜂蜜块 230 / 微光块 659）、
混合阈值 24 单位 —— 全部与实现一致。

**已实施**：

- **Boss 击杀进度 + 掉落**：`WorldState.NotifyNpcKilled` 扩展为**已核对**的进度位映射（修掉两处旧错：126 原误记为蜂后→实为双子、113 原误记为石巨人→实为血肉墙）；
  新增 `BossLoot` 表（眼魔 / 世界吞噬者 → 恶魔矿，史莱姆王 → 凝胶，蜂后 → 蜂蜡）与 `DropBossLoot`；
  `WorldItemEntity.NewNotified` 区分「客户端上报生成」与「服务端主动生成」，后者由 `GameHost.FlushNewItemsAsync` 按视口补发包 21。
- **液体混合反应**：`WorldSimulator.TryLiquidMerge` —— 本格液体与相邻异种液体累计 ≥ 24 单位时消耗异种液体并生成混合图格
  （水 + 岩浆 → 黑曜石、水 + 蜂蜜 → 蜂蜜块、岩浆 + 蜂蜜 → 松脆蜂蜜块、微光 + 任意 → 微光块），随后经 `MarkLiquidChanged` / `MarkTileChanged` 下发。
- **测试**：+3（**231 通过**）——`Vanilla_BossKill_Drops_Loot_And_Pushes_Packet21`、`Vanilla_Progress_Uses_Verified_BossIds`（回归旧错映射）、`Vanilla_LiquidMerge_Water_Plus_Lava_Creates_Obsidian`。
  默认后端与 `-p:NoSqlite=true` 兜底后端均 231/231 通过。

**仍为简化模型**：Boss 掉落仅收录 4 项（原版掉落规则在掉落数据库中，不逐条复刻）；液体内压 / 压力模型未实现；混合仅在本格为空时生成（原版还允许覆盖可被黑曜石破坏的图格）。

### 第六轮（2026-09-11）：健壮性兜底 + 服务端伤害来源 + 对抗测试自动化

**已实施**：

- **区块超帧上限兜底**：`NetworkHost.SendTileSectionAsync` 在编码超过 `UInt16`（65535）时按较长轴二分拆分再发（递归），
  修掉「高熵区块直接抛 `Frame too large` 中断登录 / 出生点下载」的既有隐患（入口：包 8 → `HandleSpawnTileDataAsync`）。
- **配置量纲校验**：`MaxSingleDamage > 32767`（Int16 线格式上限）启动即拒绝并提示，避免「阈值永不触发」的静默失效。
- **权威层兜底防御**：`ConnectionStateStage` 关闭 TODO —— 对 `PlayerId <= 0` 的无身份上下文静默丢弃（网络层保证仅 Playing 入管线）。
- **服务端伤害来源（接触）**：`SimulateCombat` 新增敌怪 / Boss 接触伤害（32px 判定 + 60tick 免伤帧 + 简化伤害表），
  统一经 `ApplyPlayerDamage` 结算；受击经包 117（广播）+ 包 16（单发权威血量）下发 —— 此前下落伤害只改服务端血量、客户端毫不知情。
- **基础设施补全**：`IMetrics.SetGauge`（插件自定义仪表盘，含标签）、`IAuditRepository.QueryRecentAsync` 双后端实现、
  `PlayerIdentity.ToPlayerId`（Guid → 槽位，与 `ToGuid` 互逆）、`CoreEventStore.QueryAsync` 接持久化审计查询。
- **命令子系统**：新增 `Authority/CommandService.cs`（注册 / 解析 / 分发，命令名不区分大小写、处理器异常转失败结果），
  组合根注册内置 `say` / `who` / `kick` / `help`，`IServerApi.ExecuteCommand` 由「仅落审计」改为真实分发。
- **Phase 7 对抗清单自动化**：`VanillaFeatureTests.AntiCheat_*` 覆盖 P1（DPS 窗口）/ P3（非法堆叠、箱内未知物品）/
  P4（无身份包丢弃、恶意包重放不推进权威）/ P5+M6（洪水限流 → 违规累计踢出）以及高熵区块拆分下的登录完整性。
- **测试**：+14（228 通过）。

**未实施（阻塞项，不猜数值）**：~~Boss 掉落 / 更多 Boss 与事件~~、~~液体混合反应~~ —— 阻塞已解除，见第七轮。

### 第五轮（2026-09-11）：遗留项闭环 —— 执行器逐格下发 + 液体视口裁剪

**问题**（第四轮遗留，见其回溯）：服务端电路翻转执行器只落在服务端图格、未推送客户端；液体同步把变化**全量广播**给所有玩家。

**已实施**：

- **执行器翻转逐格下发**：`WorldState` 新增待推送图格集合（去重 + 上限 8192）与 `MarkTileChanged`；`ActuateCommand` 每翻转一格即入队；
  `GameHost.FlushTileUpdatesAsync` 按行把待推送图格合并为「宽 × 1」矩形，以**包 10 小矩形**推送**视口内**玩家（256 格 / 64 矩形每批，超限重新排队不丢更新），随快照频率（20Hz）下发。
- **液体同步视口裁剪**：`GameHost.FlushLiquidAsync` 改为按玩家裁剪——每玩家只收到其视野半径内的液体变更（`NetworkHost.BroadcastPerPlayerAsync` 新增「按玩家定制内容」的广播通道），无变更的玩家不下发。
- **测试**：+2（214 通过）——`Vanilla_Actuate_Pushes_Tile_Update_To_Client`（客户端收到包 10 小矩形）、`Vanilla_Liquid_Sync_Is_Viewport_Culled`（视口外玩家收不到液体同步）。

**遗留**（见 §二与 `VANILLA_COVERAGE.md` §三）：图格推送用包 10 小矩形而非原版包 20（需实测校对）；液体裁剪为「每玩家全量过滤」（O(玩家 × 变更)）。

### 第四轮（2026-09-11）：世界内容权威 —— 箱子 / 液体 / 电路 / Boss·事件

**问题**：上一轮结束后剩余三块「未实现」：箱子内容仅中继（服务端不持有）、液体 / 电路完全未实现、Boss / 事件只有静态进度位。
另发现包 17 的 action 语义映射有误（把 5..11 当作「清线网 / 斜坡 / 执行器」，实际是 5=PlaceWire…19=Actuate），会误改世界。

**已实施**（口径：简化模型但服务端权威）：

- **箱子内容服务端持有**：`WorldState.ChestsLock` + `SyncChestItemCommand`；包 31 校验箱子存在 / 距离后，由网络层下发包 34（当前箱子索引）+ 逐槽包 32（权威内容）；包 32 校验箱子 / 槽位 / 堆叠 / 物品 / 距离后落盘（服务端唯一真相）。
- **液体（NetLiquid，包 82 模块 0）**：`LiquidModulePacket` 编解码（UInt16 模块号 + UInt16 条目数 + 6 字节条目）；客户端编辑经 `ValidateLiquid`（越界 / 类型 / 距离 / 条目数）后由 `LiquidEditCommand` 落盘；仿真新增脏格驱动的简化流动（下落优先、受阻后侧向均衡，限 2000 格/tick）；按快照频率批量下发（限 512 条/批）。包 82 的限流按模块拆分（聊天 / 液体各自令牌桶，液体不再占用聊天额度）。
- **电路**：修正包 17 action 0..19 映射（方块 / 墙 / 4 色线网 / 执行器放置与拆除）；新增 `ActuateCommand`（action 19）——沿电线受限 BFS（≤2000 格）翻转连通执行器的 `InActive`。
- **Boss / 事件**：`WorldState.ProgressDirty` + `NotifyNpcKilled`（Boss 击杀记进度）；`WorldSimulator` 新增 `StartBloodMoon` / `StartEclipse` / `StartInvasion` / `SpawnBoss`；入侵按配额刷哥布林，耗尽即结束；Boss（眼魔 / 骷髅王 / 史莱姆王）直线追击；昼夜切换按概率触发血月 / 日食；进度变化由世界同步重新下发包 7。
- **测试**：+26（212 通过）——箱子开箱内容 / 权威落盘 / 非法槽位 / 超距 / 不存在；液体编解码回归 + 落盘流动 / 同步下发 / 超距 / 非法类型；线网与执行器编辑 / 信号传播 / 无线网不传播；血月广播 / 昼夜切换 / Boss 刷新与击杀进度 / 入侵起止 / 日食。

**遗留**（见 §二 B-6 / B-7 与 `VANILLA_COVERAGE.md` §三）：液体同步未按视口裁剪；执行器翻转未逐格下发；液体无压力模型与混合反应；电路无门电路 / 定时器 / 压力板；Boss AI 与事件触发规则为简化模型。

### 第三轮（2026-09-11）：战斗 / 生存权威补全 + Mod 策略配置化 + `.wld` 解析修复

**问题**：

1. 受伤 / 死亡 / 复活全链路仅中继（117 / 118），无服务端结算与复活；下落伤害致死还会把玩家置为 `Active=false`（离线态）导致永远冻结。
2. 掉落物拾取（包 22）与弹幕命中判定缺失：客户端可远程拾取 / 重复拾取，弹幕命中完全由客户端声明。
3. `ModPolicy` 未从 `server.json` 读取，`TModLoaderCompat` 未装配，`ParseModList` / `ForwardModPacketAsync` 为 TODO。
4. `.wld` 解析器从未有测试覆盖 —— 补充测试后发现**真实缺陷**。

**已实施**：

- **战斗 / 生存权威**：新增 `DamagePlayerCommand` / `KillPlayerCommand` / `RespawnCommand`；`PlayerHurtV2`（包 117）非负校验后结算服务端生命，归零置死亡态；`PlayerDeathV2`（118）结算死亡；Playing 阶段的包 12 视为复活请求，**复活点由服务端固定为世界出生点**（忽略客户端坐标）；死亡 / 复活由世界同步补发包 118 / 12+16。`PlayerRuntime` 拆出 `Dead`（死亡态）与 `Active`（在线），死亡不再冻结在线状态。
- **掉落物拾取（包 22）**：`ItemPickupPacket` + 解码 / 编码；`WorldAuthority.ValidatePickup` 做槽位对账（真实存活实体）+ 拾取半径（64px）+ SSC 入库（`IInventoryAuthority.TryAddItem`），成功后 `PickupItemCommand` 移除世界实体并由世界同步下发包 21（stack=0）。
- **弹幕命中判定**：仿真层按弹幕 / 敌怪中心距离（32px）判定命中并扣血，不再采信客户端。
- **MOD 兼容层暂缓**：`ModCompat` 代码保留但当前生产显式 `enabled: false`；不接受 TModLoader 握手、不注册 250-255、不转发自定义或未知包，未来单独立项。
- **`.wld` 解析修复**：定位并修复 `LoadWorldTiles` 中 `int run = (b4 & 0xC0) >> 6 switch { ... }` 的**运算符优先级缺陷**——`switch` 实际约束 `6`，导致每个图格无条件执行 `_ => reader.ReadInt16()` 多读 2 字节，**任何 `.wld` 都无法解析**；改为显式括号后恢复正常。
- **测试**：+20（192 通过）——受伤 / 致死 / 负数伤害、死亡广播、复活点权威、拾取成功 / 超距拒绝、弹幕命中、包 73 接受与限频、ModPolicy 配置反序列化与组合根注入、TModLoader 握手 / 截断容忍 / 转发通道、`.wld` 最小合法世界 + 版本 / 魔数 / footer 拒绝路径。

### 第二轮（2026-09-11）：真实 SQLite 持久化落地

**问题**：`SqliteImpl` 全为空方法体（`GetPlayer` 返回 `null`、`SavePlayer`/`CreatePlayer`/`AppendAudit` 空实现、
`QueryAudit` 返回空数组），且全仓从未定义 `USE_SQLITE` → 实际恒走 `LiteDbPersistence`；
而它只序列化 `Players` → **封禁与审计重启即丢**（`Microsoft.Data.Sqlite` 被引用却从未生效）。

**已实施**：

- `TerraAuth.csproj` 新增 `DefineConstants=USE_SQLITE`（条件 `'$(NoSqlite)' != 'true'`），与 SQLite 包引用同开关。
- `Persistence/SqlitePersistence.cs` 实装 `SqliteImpl`：`Players` / `AuditLogs` / `Bans` 三表 + 索引，
  UPSERT、单事务批量审计插入、封禁过期判定（时间戳统一 ISO-8601 UTC 文本，字符串比较即时间比较）。
  连接策略 `Pooling=false`：句柄随 `using` 立即释放，避免 Windows 下 DB 文件被占用（备份 / 迁移 / 测试清理）。
- `IDbExecutor.AppendAudit(AuditEntry)` → `AppendAuditBatch(IReadOnlyList<AuditEntry>)`：审计按批单事务落盘
  （原先注释声称「事务包裹」但实际逐条写）。
- 停机时把审计通道残留条目一并落盘（原先 `finally` 只 flush 已取出的 buffer，通道内条目会丢）。
- `LiteDbPersistence` 兜底路径同步补齐：玩家 / 审计 / 封禁三类数据均落盘。
- 测试 +3（141 通过）：玩家+审计重启读回、封禁重启读回、过期封禁不生效；
  且两种后端（默认 SQLite 与 `-p:NoSqlite=true`）均全绿。

### 第一轮（2026-09-11）：.NET 8 → .NET 10 升级 + Hook 热路径优化

**已实施**：

- 目标框架 → `net10.0`（3 个工程）；`Microsoft.Data.Sqlite` → 10.0.12；移除 `System.IO.Pipelines`
  显式引用（.NET 10 起已内置于共享框架）。
- 锁原语：`object` / Monitor → `System.Threading.Lock`（11 处）。
- `Simulation/World/TileIdSets.cs` 静态查找集 → `FrozenSet<ushort>`。
- `Plugins/HookRegistry.cs` 触发路径：改为**写时复制不可变快照数组**，消除每包一次 `List` 分配与取锁。
- `Plugins/HookIntegration.cs`：该类 Hook 无订阅者时**不再构造 `HookArgs`**（无插件场景每包零分配）。

**已评估但未采纳**：

- 日志 `params object[]` 装箱改造：实测 `Debug/Info/Warn/Error` 仅出现在 Deny / 插件加载 / Mod 包拦截等
  低频路径，**不在每包稳态路径**，装箱开销可忽略；且 `ILogger` 属插件公共 API，改造有兼容成本，故不做。
  - 若后续需要可运维性改进，可单独增加日志级别开关（当前 `CoreLogger.Debug` 无条件写 `Console`）。
- `JsonSerializerContext` 源生成：调用点均为低频（配置变更 / 审计批量落盘）；
  唯一较高频点 `PersistenceAuditLogger.Log` 序列化的是**匿名类型** `details`，源生成无法覆盖，
  需先定义具体 DTO 才有意义。
