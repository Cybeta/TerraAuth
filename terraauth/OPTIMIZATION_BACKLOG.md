# TerraAuth — 优化待办（Backlog）

> 记录**尚未实施**的优化 / 补全事项，供后续排期取舍。已实施项见文末「本轮回溯」。
> 最后更新：2026-09-11（第二轮：真实 SQLite 持久化落地）

---

## 一、独立立项

> 暂无。（原 B-1「真实 SQLite 持久化」已完成，见文末回溯。）

---

## 二、后续可能优化（暂不实施）

> 以下均为**性能向**优化，收益需实测支撑；未确认瓶颈前不建议实施。

### B-2 出站帧缓冲复用（`Connection.SendEncodedAsync`）

- **位置**：`Net/Phase5/Connection.cs`
- **现状**：每次发送都 `new ArrayBufferWriter<byte>()` 再 `WrittenSpan.ToArray()` → 每帧两次分配；
  快照下发为 20Hz × 在线玩家数。
- **方向**：改用 `ArrayPool<byte>` 租借 + 精确长度写入（或复用单个 writer）。
- **风险（中）**：缓冲经 `Channel` 跨线程移交给写循环，归还时机与生命周期必须严格配对，
  否则易出现「已归还缓冲仍被读」类缺陷。
- **触发条件**：GC / 分配采样显示 `ArrayBufferWriter` 为热点。

### B-3 SnapshotStore 环形缓冲（消除 O(n) 移除）

- **位置**：`Simulation/SnapshotStore.cs`
- **现状**：容量 300；`Add` 超容量时走 `List.RemoveAt(0)`，`TrimBefore` 走 `RemoveRange(0, n)`，均为 O(n) 搬移。
- **量级**：每 tick 约 2.4KB memmove，60Hz ≈ 144KB/s —— **可忽略**，故暂不实施。
- **方向**：以「写入下标 + 逻辑长度」的环形缓冲替代 `List`，把移除降为 O(1)。
- **关联**：`Net/Phase4/README.md` 已记录 `Snapshot()` 每轮 `ToArray()` 复制的问题，可与本项合并评估。

### B-4 区块锁原语评估（`SectionLocks`）

- **位置**：`Simulation/World/SectionLocks.cs`
- **现状**：按 64 条带使用 `ReaderWriterLockSlim`（仿真写 vs 包 10 编码 / 权威校验读，读写分离）。
- **结论**：.NET 10 框架**未提供**更优的读写锁替代（`System.Threading.Lock` 仅适用于互斥场景），故**保持现状**。
- **触发条件**：若实测条带竞争成为瓶颈，再评估条带数调优或按 section 细化粒度。

---

## 附：本轮回溯

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
