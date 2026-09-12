# Phase 6 — 基础设施层（Infrastructure）

> 对应架构文档 §4.5。本 Phase 不新增反作弊逻辑，而是把 **配置 / 持久化 / 监控 / 封禁** 四块基础设施补齐，让整个服务端可运维、可观测、可持续对抗。

## §1. 职责

| 模块 | 接口 | 说明 |
|---|---|---|
| 配置 | `IConfigurationService` / `ServerConfig` | 所有反作弊阈值的唯一来源（JSON + 热重载） |
| 持久化 | `IPlayerRepository` / `IAuditRepository` | SSC 存档 + 审计日志（默认 `SqliteImpl`；`-p:NoSqlite=true` 降级到内嵌 `LiteDbPersistence`） |
| 监控 | `IMetrics` | Prometheus 指标（包耗时/拦截数/在线人数） |
| 封禁 | `IBanManager` / `IBanStore` | 违规累计 → 自动封禁 + IP 黑名单 |

## §2. 数据流

```
客户端包 → Phase 2 权威层
   ├─ Accept → Command → Phase 3 → Phase 4 → 下发
   └─ Reject → IMetrics.IncrementBlockedCheat
             → IBanManager.ReportViolationAsync (达阈值→封禁记录)
             → IAuditRepository.AppendAsync (落库)
             → Phase 5 NetworkHost 处置 (达阈值→发包 2 踢出连接)
                  ↓
            运营复核 → 误判标记 → IMetrics.IncrementFalsePositives
```

## §3. 配置驱动

`ServerConfig` 是所有阈值的唯一真相源（MaxWalkSpeed / MaxSingleDamage / MaxTileBreakPerSecond / MaxViolationsBeforeBan…）。`GameHost` 把限流与六个子系统的阈值全部显式映射注入（`RateLimits` / `PlayerLimits` / `MovementLimits` / `CombatLimits` / `InventoryLimits` / `WorldLimits`），**启动注入与热重载共用同一映射**（`AuthorityThresholds.From`），避免两处各写一份而漂移。

`ConfigurationService` 用 `FileSystemWatcher` 重新加载并触发 `OnChanged`；`GameHost.OnConfigurationChanged` 随即调用 `AuthorityEnforcers.UpdateThresholds`，把新阈值推送给**已构造**的子系统 —— **改 `server.json` 无需重启即生效**。实现要点：阈值对象为不可变 record（引用类型），子系统以 `volatile` 字段持有，热更新时整体替换引用，故读取端无锁、无撕裂；更新瞬间正在执行的校验可能仍用旧值，属热重载的正常语义。

集成测试覆盖两条路径：启动映射（`ConfigThresholds_AreApplied_ByBootstrap`）与运行时热重载（`ConfigHotReload_UpdatesThresholds_WithoutRestart`）。

> ⚠️ 阈值过松=漏判，过紧=误杀（架构 §8.2 要求误判率<0.1%）。先用保守值再逐步收紧。

## §4. 封禁策略

`BanManager` 滑动窗口累计违规：达 `MaxViolationsBeforeBan` → 自动封禁（默认 24h）。生产级 `IBanStore` 可选 Redis（多服共享 IP 黑名单）或 SQLite。

同一阈值也驱动 **连接层即时处置**：`NetworkHost` 按 `ServerConfig`（`MaxViolationsBeforeBan` / `ViolationWindowMinutes`）维护每玩家滑动窗口，达阈值即调用 `ConnectionManager.KickAsync`（先下发包 2 告知原因，再关闭连接释放槽位）。封禁记录 + 踢出双管齐下，避免"只记录不处置"。

## §5. 验收清单

- [x] 6.1 违规达阈值自动封禁（测试通过）
- [x] 6.3 配置热重载触发 OnChanged
- [x] 6.4 真实 `SqliteImpl` 填充（玩家存档 + 审计 + 封禁落库）：默认后端为 SQLite（csproj 定义 `USE_SQLITE`）；`-p:NoSqlite=true` 降级到内嵌 `LiteDbPersistence`（三类数据同样落盘）。均有「重启后读回」测试覆盖
- [x] 6.5 审计可按玩家查询（`IAuditRepository.QueryByPlayerAsync` → 后台批量落盘）；另提供 `QueryRecentAsync`（按时间倒序取最近 N 条，SQLite / 内嵌后端均实现），并由 `CoreEventStore.QueryAsync` 适配为插件可见事件流
- [x] 6.6 /metrics 端点可被 Prometheus 抓取（`MetricsHttpServer` + `ExportAsText`）
- [x] 6.7 GameHost.RunAsync 启动三循环（网络 + 仿真 + 快照）
- [x] 6.8 权威层拒绝同步触发 指标 + 审计 + 违规累计（`auditLogger.OnViolation`）
- [x] 6.9 违规达阈值即时踢出在线连接（`NetworkHost` 滑动窗口 + `ConnectionManager.KickAsync` 发包 2 后关闭，真实 TCP 集成测试覆盖）
- [x] 6.10 插件 API 的踢出 / 封禁真实生效（`ServerApi` 桥接 `ConnectionManager` + `IBanManager`；封禁身份经 `PlayerIdentity` 与审计链路统一，真实 TCP 集成测试覆盖）

## §6. P0 优先级

1. ✅ SqlitePersistence 审计异步化（无界 `Channel` + `DrainAuditLoop` 每 250ms / 100 条批量落盘）；`SqliteImpl` 已完整实装（玩家 / 审计 / 封禁 / WorldTiles / WorldChests 五表）
2. ✅ 真实 IBanStore（`SqliteBanStore` 复用同一 `IDbExecutor`）
3. ✅ PrometheusMetrics.ExportAsText + `HttpListener` /metrics 端点
4. ✅ GameHost 注入真实实现（`Bootstrap` 组装 Phase 2/3/4/5 + 基础设施 + 扩展层）
5. ✅ Phase 2 子系统订阅配置热更新（`OnConfigurationChanged` → `AuthorityEnforcers.UpdateThresholds`；阈值对象引用整体替换，运行时改 `server.json` 无需重启）

## §7. 与 Phase 7 衔接

Phase 6 的审计+监控是 Phase 7（对抗测试）的数据来源：CE 改内存后观察 `blocked:*` 指标、检查 AuditLogs、`false_positives` 在合法操作下是否<0.1%。**务必先跑通本 Phase 再进入对抗测试。**
