# 多线程优化分析（Concurrency 模块）

> 给 agent 的执行参考：哪些能并行、哪些不能、怎么并行、收益预估。
> 对应实装代码：`Concurrency/ParallelConfig.cs`、`Concurrency/ParallelWorkers.cs`

## 一、线程安全边界

### ✅ 可安全并行（无共享状态 / 只读）

| 模块 | 并行度 | 实装位置 |
|------|--------|---------|
| 网络 I/O（收/发包） | 每连接独立 | `Net/Phase5/NetworkHost.cs` |
| 包解码（Decode） | 每包独立（纯函数） | `ParallelWorkers.WorkerPool` |
| 快照序列化（Encode） | 每玩家独立 | `ParallelWorkers.ParallelSnapshotBroadcaster` |
| 持久化落盘 | 后台线程 | `Persistence/SqlitePersistence.cs` |
| 无状态插件 Hook | 可并行 | `Plugins/HookRegistry.cs` |
| 监控指标采集 | 独立线程 | `Monitoring/PrometheusMetrics.cs` |

### ⚠️ 需架构改造（部分已实装）

| 模块 | 挑战 | 方案 | 实装类 |
|------|------|------|--------|
| 权威校验（Phase 2） | 需读玩家状态 | 按玩家分片 | `ShardedAuthorityProcessor` |
| 世界仿真（Phase 3） | 确定性 + NPC 交互 | 空间分区（Chunk） | `WorldSimulator` 扩展点 |
| 图格并发访问 | 仿真写 / 包 10 编码与权威校验读 | 区块分区锁 | `Simulation/World/SectionLocks` |
| 快照生成（Phase 4） | 需读 WorldState | 发布不可变实体视图 | `DoubleBufferedWorldState` + `WorldEntityView` |

### ❌ 不可并行

| 模块 | 原因 |
|------|------|
| CommandQueue 排序 | 必须按 (tick, playerId) 稳定排序 |
| Tick 推进（GameLoop） | 固定 timestep 时序锚点 |

## 二、推荐线程模型

```
┌────────────────────────────────────────────┐
│ Logic Thread（单线程，GameLoop 确定性循环） │
│  Tick = Input → AI → Physics → Combat →…  │
└────────────┬───────────────┬───────────────┘
             ▼               ▼
   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
   │ Network Pool │   │ Worker Pool  │   │ Background   │
   │ (I/O Bound)  │   │ (CPU Bound) │   │ (Async Ops)  │
   │              │   │              │   │              │
   │ • Accept     │   │ • Decode     │   │ • SQLite     │
   │ • Read/Write │   │ • Hook 执行  │   │ • 审计日志    │
   │ • Framing    │   │ • 空间查询    │   │ • 快照序列化  │
   └──────────────┘   └──────────────┘   └──────────────┘
```

线程数（`ParallelConfig`）：
- `NetworkThreads = max(4, logicalCores)`
- `WorkerThreads = max(1, logicalCores - 2)`（留 2 核给 Logic + OS）
- `BackgroundThreads = 2`

## 三、实装状态

| 组件 | 文件 | 状态 |
|------|------|------|
| `ParallelConfig` | `ParallelConfig.cs` | ✅ 完整 |
| `AtomicCounter` / `MpscQueue` / `SingleWriterMultiReaderLock` | `ParallelConfig.cs` | ✅ 完整 |
| `WorkerPool` | `ParallelWorkers.cs` | ✅ 完整（已接入 `GameHost.Workers`） |
| `DoubleBufferedWorldState<T>` | `ParallelWorkers.cs` | ✅ 完整（发布语义：`Publish` / `Current`，供 `WorldSimulator` 发布 `WorldEntityView`；见下方契约说明） |
| `ShardedAuthorityProcessor` | `ParallelWorkers.cs` | ✅ 完整（已接入 `Authority/ShardedInboundPipeline`，按玩家分片并行、同玩家保序） |
| `ParallelSnapshotBroadcaster` | `ParallelWorkers.cs` | ✅ 完整（已接入 `SnapshotBroadcaster.FlushAsync`，同快照编码一次后并行下发） |
| `SectionLocks`（区块分区锁） | `Simulation/World/SectionLocks.cs` | ✅ 完整（仿真写图格 / 包 10 编码与权威校验读互斥；读侧先在锁内拷贝快照再压缩，锁内不做长耗时工作） |
| 并行区块仿真 | `WorldSimulator` 扩展 | ⏳ 待实装（前提见 §六 P4） |

## 四、性能收益预估（64 玩家 / 中世界）

| 优化项 | 单线程 | 多线程 | 提升 |
|--------|--------|--------|------|
| 网络 I/O + 解码 | 2ms/tick | 0.5ms/tick | 4x |
| 权威校验（分片） | 3ms/tick | 1ms/tick | 3x |
| 快照生成 + 编码 | 4ms/tick | 1ms/tick | 4x |
| **总 Tick** | **~17ms (59 FPS)** | **~5.5ms (181 FPS)** | **~3x** |

## 五、风险与对策

1. **确定性破坏** → `Determinism.AssertConsistent` + Debug 断言 + 回放测试
2. **假共享** → 按玩家/Chunk 独立内存，对齐 64 字节
3. **锁竞争** → 无锁数据结构 + 消息传递 + `ReaderWriterLockSlim`
4. **GC 压力** → Object Pool + `ArrayPool<T>` + 低峰期 GC

## 六、agent 执行优先级

- ✅ **P0**：网络 I/O 与解码分离（已实装：`Connection` 在 I/O 线程分帧后，把解码 + 处理投递 `WorkerPool`）
- ✅ **P0**：快照序列化并行（已实装：`SnapshotBroadcaster.FlushAsync` 走 `ParallelSnapshotBroadcaster`，每帧编码一次后并行下发）
- ✅ **P1**：持久化异步化（已实装：`SqlitePersistence.AppendAsync` 写入无界 `Channel` 零等待，后台 `DrainAuditLoop` 每 250ms / 100 条批量落盘）
- ✅ **P2**：权威校验分片（已实装：`ShardedInboundPipeline` 把单包契约桥接到 `ShardedAuthorityProcessor`，按 `PlayerId % ShardCount` 分片并行、同玩家保序）
- ✅ **P2**：双缓冲 WorldState（已实装：`WorldSimulator.Tick` 末把玩家 + NPC 提取为**不可变** `WorldEntityView` 并发布；`SnapshotBroadcaster` 只读该视图构建全量帧与判定裁剪中心，广播线程不再触碰活动 `WorldState`）
- ✅ **P4（可验证子集）**：区块分区锁 —— 仿真线程写图格与包 10 编码 / 权威校验的读互斥，消除 `Tile`（约 20B）撕裂读导致的客户端错乱图格（确定性测试：持写锁时编码被阻塞）
- **P4（并行部分）**：并行区块仿真 —— **暂不实装**，理由与前提如下
  1. 服务器上限 `MaxConnections = 64`，而本项收益场景是「100+ 玩家」（§四 预估表的前提）；
  2. `Tick()` 只遍历实体（players + town NPCs），不遍历图格，每实体仅数次浮点运算 + 1 次图格查询 —— 仿真不是每 tick 瓶颈（大头是快照生成/裁剪/编码，已并行；入站校验已分片）；
  3. 并行化会威胁验收 KPI「确定性 100% 一致」：当前 `SimulateAi` 依赖**全局 RNG 调用顺序**，并行前必须先做**每实体确定性 RNG 流**，并补「并行结果 == 串行结果」的等价性测试；
  4. 启用前提：先补实体规模实测（证明仿真确为瓶颈）+ 每实体 RNG 流 + 等价性测试。

> 务实建议：先做 P0/P1/P2，解决 80% 瓶颈且风险低。空间分区留给 100+ 玩家场景。

### 契约变更说明（P2 实装时）

原骨架提供「就地填充 + `Swap`」，并留了「增量复制脏区块」的 TODO。实装时改为**发布不可变对象**：

- **为何不能用就地填充**：只有两块缓冲时，读数慢于写帧的读者（仿真 60Hz vs 快照 20Hz 必然发生）会读到正被覆写的缓冲，且一次覆写跨过整块缓冲 —— 不安全。
- **为何不再需要增量复制**：发布对象不可变，写端每帧构建完整视图后原子替换引用，读者持有的旧视图永不被改动，故 TODO 随契约取消。
- **未双缓冲整个世界**：快照路径只读实体（players / Npcs），不涉及 `Tiles`；图格并发面属 P4 空间分区范畴。

---

## 参考：优化待办

- `SectionLocks` 锁原语评估（结论：.NET 10 无更优读写锁替代，**保持 `ReaderWriterLockSlim`**）→
  见 [`OPTIMIZATION_BACKLOG.md`](../OPTIMIZATION_BACKLOG.md) §B-4。
- 出站帧缓冲复用 / SnapshotStore 环形缓冲等同属待评估项 → 见同一文档 §B-2、§B-3。
