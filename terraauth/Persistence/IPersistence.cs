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
    /// <summary>按时间倒序取最近 <paramref name="limit"/> 条审计（供插件事件查询）。</summary>
    Task<IReadOnlyList<AuditEntry>> QueryRecentAsync(int limit);
}

/// <summary>
/// 世界改动仓储：把玩家对世界的修改（挖 / 放方块、墙、液体、电线、执行器、箱子内容）落盘，
/// 服务端重启后按「基准世界 + 改动日志」回放，避免玩家建筑 / 箱子在重启后丢失。
/// 基准世界由 .wld 解析或程序化生成得到（确定性），因此只需存增量。
/// </summary>
public interface IWorldRepository
{
    /// <summary>批量写入图格改动（同坐标覆盖写）。</summary>
    Task SaveTileChangesAsync(IReadOnlyList<WorldTileRecord> tiles);
    /// <summary>读取全部图格改动（启动时回放）。</summary>
    Task<IReadOnlyList<WorldTileRecord>> LoadTileChangesAsync();
    /// <summary>批量写入箱子内容（同索引覆盖写）。</summary>
    Task SaveChestChangesAsync(IReadOnlyList<WorldChestRecord> chests);
    /// <summary>读取全部箱子内容改动（启动时回放）。</summary>
    Task<IReadOnlyList<WorldChestRecord>> LoadChestChangesAsync();
}

/// <summary>单格图格改动：坐标 + 定长序列化图格（见 <c>Tile.Serialize</c>）。</summary>
public record WorldTileRecord(int X, int Y, byte[] Data);

/// <summary>
/// 单个箱子的内容改动：索引 + 坐标 + 物品格序列化字节（见 <c>Chest.SerializeItems</c>）。
/// 回放时按索引定位并校验坐标，避免基准世界被替换后错位套用。
/// </summary>
public record WorldChestRecord(int Index, int X, int Y, byte[] Data);

public record PlayerData(Guid Id, string Name, byte[] InventoryBlob, int MaxHp, int MaxMp);
public record AuditEntry(DateTime Timestamp, Guid PlayerId, string EventType, string Detail, string? IpAddress);
