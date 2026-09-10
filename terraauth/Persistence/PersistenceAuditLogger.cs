// TerraAuth — Phase 6: 审计 → 持久化 + 监控 + 违规累计桥接
// 归属组合根（Core）：权威层（Authority）不得反向依赖 Persistence/Monitoring，
// 因此该桥接器放在基础设施层，由 GameHost 注入给 AuthorityEnforcers。
// 架构 §4.5 数据流：
//   权威层 Reject → PersistenceAuditLogger.Log()
//     → IAuditRepository.AppendAsync（落库）
//     → IMetrics.IncrementBlockedCheat
//     → OnViolation 回调 → IBanManager.ReportViolationAsync（达阈值封禁）

using System;
using System.Threading.Tasks;
using TerraAuth.Authority;
using TerraAuth.Monitoring;

namespace TerraAuth.Persistence;

public sealed class PersistenceAuditLogger : IAuditLogger
{
    private readonly IAuditRepository _audit;
    private readonly IMetrics? _metrics;
    private static readonly Guid _hostId = Guid.Empty; // 服务端事件占位

    /// <summary>违规回调：GameHost 订阅后实现"拦截→封禁"闭环。</summary>
    public Action<Guid, string>? OnViolation { get; set; }

    public PersistenceAuditLogger(IAuditRepository audit, IMetrics? metrics = null)
        => (_audit, _metrics) = (audit, metrics);

    public void Log(AuditEvent e)
    {
        // 1. 落库（异步入队，不阻塞管线）
        _audit.AppendAsync(new AuditEntry(
            Timestamp: e.Timestamp.UtcDateTime,
            PlayerId: GetPlayerGuid(e.PlayerId),
            EventType: e.Action,
            Detail: $"{e.Category}:{e.Reason} {(e.Details is null ? "" : System.Text.Json.JsonSerializer.Serialize(e.Details))}",
            IpAddress: null));

        // 2. 指标计数
        if (_metrics is not null)
        {
            _metrics.IncrementBlockedCheat(e.Reason);
            if (e.Category == "false_positive") _metrics.IncrementFalsePositives();
        }

        // 3. 触发违规累计（GameHost 订阅 → BanManager）
        if (e.Category is "authority" or "rate" or "security")
            OnViolation?.Invoke(GetPlayerGuid(e.PlayerId), e.Reason);
    }

    public Task FlushAsync() => Task.CompletedTask;

    // PlayerId 在 AuditEvent 是 int（连接槽位），AuditEntry 用 Guid；此处做映射
    // 生产应维护 ConnectionId → Guid 表；此处简化用确定性哈希
    private static Guid GetPlayerGuid(int playerId)
        => playerId == 0 ? _hostId : new Guid(playerId, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)playerId);
}
