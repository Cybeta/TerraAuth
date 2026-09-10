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
| 快照生成（Phase 4） | 需读 WorldState | 双缓冲 | `DoubleBufferedWorldState` |

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
| `DoubleBufferedWorldState<T>` | `ParallelWorkers.cs` | ✅ 骨架（Swap 增量复制为 TODO） |
| `ShardedAuthorityProcessor` | `ParallelWorkers.cs` | ✅ 完整（已接入 `Authority/ShardedInboundPipeline`，按玩家分片并行、同玩家保序） |
| `ParallelSnapshotBroadcaster` | `ParallelWorkers.cs` | ✅ 完整（已接入 `SnapshotBroadcaster.FlushAsync`，同快照编码一次后并行下发） |
| 空间分区世界仿真 | `WorldSimulator` 扩展 | ⏳ TODO（P4，高风险高收益） |

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
- **P2**：双缓冲 WorldState（替换 WorldSimulator 的 State 引用）
- **P4**：空间分区世界仿真（需大量测试 + 确定性校验）

> 务实建议：先做 P0/P1/P2，解决 80% 瓶颈且风险低。空间分区留给 100+ 玩家场景。
