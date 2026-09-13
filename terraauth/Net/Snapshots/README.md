# Snapshots — 快照广播 + 客户端预测/协调

> **依赖**：Phase 2（权威层）、Phase 3（仿真层）
> **对应架构文档**：§4.4 快照层

---

## 职责

- **`SnapshotFrame`**：强类型快照（对接 terraria-protocol 布局）
- **`SnapshotBuilder`**：从 `WorldState` 产出增量快照
- **`SnapshotBroadcaster`**：仿真 → 网络 的桥梁
- **`IClientPredictor` / `ShadowPredictor`**：客户端预测 + **影子反作弊**

---

## 设计要点

### 1. 增量快照（Delta Snapshot）

```
BaseTick = 上一已确认 tick → 只发差异
Checksum = xxHash32（丢包容错）
```

`SnapshotFrame.BuildDelta` 从 `WorldState.Players` 提取实体（离线玩家映射为 `Hidden`），
与上一帧对比：只发位置/速度/状态发生变化的实体，消失的实体进 `Removed`（`Despawn`）；
无基线时退化为全量（`BaseTick = 0`）。`Checksum` 为对实体与移除项的规范字节流求得的真实 xxHash32。

每个玩家独立 `_lastAcked` → 各自收到针对自己的增量：
`SnapshotBroadcaster.BuildFrameFor(playerId)` 以该玩家的 `lastAcked` 为 `BaseTick`，
把历史帧归并成「自其确认点起」的净变化（同 Id 后写覆盖，`Removed` 与重现有撤销语义）。

**历史窗口不足回退**：store 是连续 delta 后缀，若该玩家的 `lastAcked` 已早于窗口首帧
（历史被 `CompactHistory` 裁剪 / 容量淘汰 / 玩家从未确认且首帧非 1），`CanServeDeltaFrom` 判定不可
重建连续增量，此时 `BuildFrameFor`、`GetFullSnapshotFor`、追赶路径一律回退**单帧真全量**
（`BaseTick = 0`），避免在错误基线上发残缺 delta 导致客户端缺失未变化实体。
`OnAck` 拒绝 `ackedTick > 当前 tick` 的越界确认（防伪造 ack 抬高基址 / 误裁剪历史）。

### 2. 视野裁剪（Viewport Culling）

以玩家权威位置为中心，超出 `SnapshotConfig.ViewportRadius`（像素）的实体不下发，
并转为 `RemovedEntity(OutOfRange)` 通知客户端移除；`ViewportRadius <= 0` 或玩家尚未进入世界时旁路（不裁剪）。
半径由 `ServerConfig.ViewportRadius`（`server.json`）配置，`GameHost` 组合根负责映射。
裁剪输出按 Id 去重：视野外实体只记一次 `OutOfRange`，帧自带的 `Removed` 重复条目也会合并，避免重复编码。

### 3. 客户端预测 / 协调

- 客户端本地预测移动，服务端权威纠正
- 丢包 → `OnAck` 触发追赶（`GetFullSnapshotFor`）

### 4. 影子预测反作弊 ★

服务端用客户端输入序列**重放**其视角：

```
deviation = |reported - predicted|
if deviation > ShadowPredictionMaxDeviation → 可疑
```

> 这是用「预测一致性」检测「合法包作弊」的核心手段，弥补自瞄/脚本盲区。
> 详见 `ISnapshotSender` 与 `ShadowPredictor`。

### 5. 并行下发（`ParallelSnapshotBroadcaster`）

`SnapshotBroadcaster.FlushAsync` 把「快照生成 + 编码 + 下发」投递 `ParallelSnapshotBroadcaster`：

- 每玩家先 `BuildFrameFor` 生成自己的分桶 + 裁剪后的快照，再独立编码下发（`SendEncodedAsync`）
- 每玩家下发在 `WorkerPool` 线程池并行，`SemaphoreSlim` 限流（默认 `ProcessorCount`，可由 `ParallelConfig.BackgroundThreads` 覆盖）

### 6. 快照存储线程安全（`SnapshotStore`）

仿真线程写入（`Add` / `TrimBefore`），网络线程读取（`Snapshot` / `Latest` / `Count`），二者并发。
`SnapshotStore` 内部 `List<SnapshotFrame>` 全部经 `lock (_gate)` 保护，且**不暴露内部列表**：
读取一律走 `Snapshot()` 返回的**一致视图副本**（`ToArray()`）。窗口判定、增量归并、追赶遍历都在同一份
副本上完成，避免仿真线程插入其间导致漏帧、索引越界或枚举期「集合被修改」异常。
`LatestOrDefault` 在空 store 时返回 `null`（不抛异常），供仿真循环安全取上一帧。

> **后续可能优化（暂不实施）**：`Snapshot()` 每次调用都 `ToArray()` 复制，而 `BuildFrameFor` 每玩家每轮
> `FlushAsync` 触发一次，复杂度 O(玩家 × 窗口帧数)。若实测成为瓶颈，可考虑按 tick 缓存一致视图，
> 或引入不可变/持久化列表以消除复制（帧本身为不可变 record，需保证写入端不原地修改）。

---

## 约束（必须认知）

> ⚠️ **原版 Terraria 客户端不支持预测协议**（硬约束）。
> 因此：
> - ✅ 服务端权威快照下发（防作弊核心）
> - ⚠️ 客户端预测/协调 → 仅自建客户端或 TModLoader 可实现
> - ⚠️ 原版客户端仍走标准包 → 延迟感只能通过快照频率（默认 20Hz）缓解
>
> **这不影响防作弊目标（L4），只影响操作手感。**

---

## 下一步

**P0**：
1. ✅ `SnapshotBuilder.Build` 的 Diff 逻辑（实体提取 + 增量 + `Removed`，见 `SnapshotFrame.BuildDelta`）
2. ✅ `SnapshotBroadcaster.SubmitInputs` 的 Command 生成（影子预测重放 → `MoveCommand`，含速度钳制）；⚠️ 未接线：生产路径统一走管线 `TerminalStage.CreateCommand`（包 13 → `MoveCommand`），`SubmitInputs` 保留给自建客户端的影子预测
3. ✅ `Encode` 的每玩家分桶（`BuildFrameFor` 以每玩家 `lastAcked` 为 `BaseTick` 归并增量）
4. ✅ `SendAsync` 的视野裁剪（`ViewportRadius` 超出转 `OutOfRange`）
5. ✅ `ShadowPredictor` 实装（输入重放 + 单 tick 速度钳制 + 偏差阈值走 `SnapshotConfig`）

---

## 测试（`Tests/SnapshotTests.cs`）

- ✅ 快照 tick 单调递增
- ✅ 实体提取（多玩家 / 离线 → `Hidden`）
- ✅ 增量生成（`BaseTick` 正确；未变实体省略；消失实体进 `Removed`）
- ✅ 输入裁剪（已确认历史回收）
- ✅ 每玩家分桶（不同 `lastAcked` → 不同 `BaseTick`）
- ✅ 历史窗口不足回退全量（`BuildFrameFor` / `GetFullSnapshotFor`；历史被裁剪后不发明残缺 delta）
- ✅ 空 store 回退全量（`BuildFrameFor` / `GetFullSnapshotFor` 不抛异常、不返回空帧）
- ✅ store 线程安全（仿真线程并发 `Add`/`TrimBefore` 与网络线程 `Snapshot`/`Count`/`LatestOrDefault` 无异常）
- ✅ ack 越界忽略（`ackedTick > 当前 tick` 不抬高基址、不误裁剪历史）
- ✅ 视野裁剪（范围内保留、范围外转 `OutOfRange`、`ViewportRadius = 0` 旁路）
- ✅ 裁剪 `Removed` 去重（视野外只记一次 `OutOfRange`；帧内重复移除条目合并）
- ✅ `BaseTick = 0` 归并包含 `Tick = 0` 基线帧（全量语义不漏帧）
- ✅ NPC 纳入实体提取（负 Id 命名空间、`Velocity = 0`、静态 NPC 增量省略）
- ✅ 追赶（落后者获取快照序列）
- ✅ `SubmitInputs` Command 生成（影子预测重放 → `MoveCommand`；超速位移被钳制）
- ✅ 影子预测一致性（报告 vs 预测偏差检测）
