// TerraAuth — Phase 2: 审计日志接口
// 架构 §7.1 结构化安全审计日志

using System;
using System.Threading.Tasks;

namespace TerraAuth.Authority;

/// <summary>审计日志接口。</summary>
public interface IAuditLogger
{
    void Log(AuditEvent e);
    Task FlushAsync();
}

/// <summary>结构化审计事件。</summary>
public sealed record AuditEvent(
    DateTimeOffset Timestamp,
    int PlayerId,
    string Category,   // "authority" / "rate" / "connection" / ...
    string Action,     // "position_rejected" / "item_denied" / ...
    string Reason,     // "speed_exceeded" / "unknown_item" / ...
    object? Details = null)
{
    public static AuditEvent Now(int playerId, string category, string action, string reason, object? details = null)
        => new(DateTimeOffset.UtcNow, playerId, category, action, reason, details);
}
