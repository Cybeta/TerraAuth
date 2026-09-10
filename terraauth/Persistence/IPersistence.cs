// Phase 6 - 持久化接口
// 对应架构文档 §4.5：SQLite 存档 + 审计日志

namespace TerraAuth.Persistence;

/// <summary>
/// 玩家持久化：SSC 模式下背包/属性/进度存服务端数据库。
/// 本地客户端改了内存也不会同步过来（架构 §6 L4 核心）。
/// </summary>
public interface IPlayerRepository
{
    Task<PlayerData?> GetAsync(Guid playerId);
    Task SaveAsync(PlayerData data);
    Task CreateIfNotExistsAsync(Guid playerId, string name);
}

/// <summary>
/// 审计日志仓储：所有 AuthorityDecision.Reject/ManualBan 必须落库，
/// 供 Phase 7 对抗测试分析与运营复核。
/// </summary>
public interface IAuditRepository
{
    Task AppendAsync(AuditEntry entry);
    Task<IReadOnlyList<AuditEntry>> QueryByPlayerAsync(Guid playerId, DateTime since);
}

public record PlayerData(Guid Id, string Name, byte[] InventoryBlob, int MaxHp, int MaxMp);
public record AuditEntry(DateTime Timestamp, Guid PlayerId, string EventType, string Detail, string? IpAddress);
