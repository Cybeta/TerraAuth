# Phase 6 — 基础设施层（Infrastructure）

> 对应架构文档 §4.5。本 Phase 不新增反作弊逻辑，而是把 **配置 / 持久化 / 监控 / 封禁** 四块基础设施补齐，让整个服务端可运维、可观测、可持续对抗。

## §1. 职责

| 模块 | 接口 | 说明 |
|---|---|---|
| 配置 | `IConfigurationService` / `ServerConfig` | 所有反作弊阈值的唯一来源（JSON + 热重载） |
| 持久化 | `IPlayerRepository` / `IAuditRepository` | SSC 存档 + 审计日志（默认内嵌 `LiteDbPersistence`；定义 `USE_SQLITE` 后走 SQLite） |
| 监控 | `IMetrics` | Prometheus 指标（包耗时/拦截数/在线人数） |
| 封禁 | `IBanManager` / `IBanStore` | 违规累计 → 自动封禁 + IP 黑名单 |

## §2. 数据流

```
客户端包 → Phase 2 权威层
   ├─ Accept → Command → Phase 3 → Phase 4 → 下发
   └─ Reject → IMetrics.IncrementBlockedCheat
             → IBanManager.ReportViolationAsync (达阈值→封禁)
             → IAuditRepository.AppendAsync (落库)
                  ↓
            运营复核 → 误判标记 → IMetrics.IncrementFalsePositives
```

## §3. 配置驱动

`ServerConfig` 是所有阈值的唯一真相源（MaxWalkSpeed / MaxSingleDamage / MaxTileBreakPerSecond / MaxViolationsBeforeBan…）。`ConfigurationService` 用 `FileSystemWatcher` 热重载，权威子系统订阅 `OnChanged` 动态调整阈值，改阈值无需重启。

> ⚠️ 阈值过松=漏判，过紧=误杀（架构 §8.2 要求误判率<0.1%）。先用保守值再逐步收紧。

## §4. 封禁策略

`BanManager` 滑动窗口累计违规：达 `MaxViolationsBeforeBan` → 自动封禁（默认 24h）。生产级 `IBanStore` 可选 Redis（多服共享 IP 黑名单）或 SQLite。

## §5. 验收清单

- [x] 6.1 违规达阈值自动封禁（测试通过）
- [x] 6.3 配置热重载触发 OnChanged
- [ ] 6.4 真实 `SqliteImpl` 填充 TODO（存档 + 审计落库）：当前默认走内嵌 `LiteDbPersistence`（`TerraAuth.csproj` 未定义 `USE_SQLITE`），`SqliteImpl` 仍为空壳
- [x] 6.5 审计可按玩家查询（`IAuditRepository.QueryByPlayerAsync` → 后台批量落盘）
- [x] 6.6 /metrics 端点可被 Prometheus 抓取（`MetricsHttpServer` + `ExportAsText`）
- [x] 6.7 GameHost.RunAsync 启动三循环（网络 + 仿真 + 快照）
- [x] 6.8 权威层拒绝同步触发 指标 + 审计 + 违规累计（`auditLogger.OnViolation`）

## §6. P0 优先级

1. ✅ SqlitePersistence 审计异步化（无界 `Channel` + `DrainAuditLoop` 每 250ms / 100 条批量落盘）；真实 `SqliteImpl` 的 SQL 仍为骨架
2. ✅ 真实 IBanStore（`SqliteBanStore` 复用同一 `IDbExecutor`）
3. ✅ PrometheusMetrics.ExportAsText + `HttpListener` /metrics 端点
4. ✅ GameHost 注入真实实现（`Bootstrap` 组装 Phase 2/3/4/5 + 基础设施 + 扩展层）
5. ⏳ Phase 2 子系统订阅配置热更新（当前 `OnConfigurationChanged` 仅打印日志占位）

## §7. 与 Phase 7 衔接

Phase 6 的审计+监控是 Phase 7（对抗测试）的数据来源：CE 改内存后观察 `blocked:*` 指标、检查 AuditLogs、`false_positives` 在合法操作下是否<0.1%。**务必先跑通本 Phase 再进入对抗测试。**
