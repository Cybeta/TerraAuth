// Phase 6 - 封禁管理
// 对应架构文档 §6：违规累计 → 自动封禁 + IP 黑名单

namespace TerraAuth.Security;

public interface IBanManager
{
    /// <summary>记录一次违规，返回是否已达封禁阈值。</summary>
    Task<bool> ReportViolationAsync(Guid playerId, string reason);

    /// <summary>检查玩家是否被封（含 IP 黑名单）。</summary>
    Task<bool> IsBannedAsync(Guid playerId, string ipAddress);

    /// <summary>手动封禁/解封（运营操作，落入审计）。</summary>
    Task BanAsync(Guid playerId, string reason, TimeSpan? duration);
    Task UnbanAsync(Guid playerId, string reason);
}

/// <summary>
/// 封禁存储抽象（内存/Redis/DB）。
/// </summary>
public interface IBanStore
{
    Task AddAsync(BanRecord record);
    Task<bool> ContainsAsync(Guid playerId, string ipAddress);
    Task RemoveAsync(Guid playerId);
}

public record BanRecord(Guid PlayerId, string IpAddress, string Reason, DateTime ExpiresAt);
