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
    string WorldId { get; }

    /// <summary>批量写入图格改动（同坐标覆盖写）。</summary>
    Task SaveTileChangesAsync(IReadOnlyList<WorldTileRecord> tiles);
    /// <summary>读取全部图格改动（启动时回放）。</summary>
    Task<IReadOnlyList<WorldTileRecord>> LoadTileChangesAsync();
    /// <summary>批量写入箱子内容（同索引覆盖写）。</summary>
    Task SaveChestChangesAsync(IReadOnlyList<WorldChestRecord> chests);
    /// <summary>读取全部箱子内容改动（启动时回放）。</summary>
    Task<IReadOnlyList<WorldChestRecord>> LoadChestChangesAsync();
    /// <summary>删除已销毁箱子的内容覆盖记录，避免重启时基准世界容器复活。</summary>
    Task DeleteChestChangesAsync(IReadOnlyList<int> chestIndices);
    Task DeleteChestChangesAsync(IReadOnlyList<(int Index, long Version)> chests);
    /// <summary>按锚点写入图格实体覆盖或删除墓碑。</summary>
    Task SaveTileEntityChangesAsync(IReadOnlyList<WorldTileEntityRecord> entities);
    /// <summary>读取已持久化世界版本，用于重启后继续生成单调版本。</summary>
    Task<long> GetMaxWorldPersistVersionAsync();
    /// <summary>读取全部图格实体覆盖与删除墓碑，供启动时按锚点回放。</summary>
    Task<IReadOnlyList<WorldTileEntityRecord>> LoadTileEntityChangesAsync();
    /// <summary>
    /// 写入世界进度（单行覆盖写：同一世界只有一条进度记录）。
    /// 进度是「世界状态」而非「玩家数据」，故按 <see cref="WorldId"/> 落库而非按玩家名。
    /// </summary>
    Task SaveWorldProgressAsync(WorldProgressRecord progress);
    /// <summary>读取世界进度（启动时回放）；从未落盘过则返回 null（按全新世界启动）。</summary>
    Task<WorldProgressRecord?> LoadWorldProgressAsync();
    /// <summary>
    /// 清空全部世界改动（图格 + 箱子 + 图格实体 + 世界进度）。基准世界换掉（改 <c>WorldSeed</c> / 换 .wld）时必须调用，
    /// 否则按旧地图坐标记录的改动会落到新地形上。进度必须一起清：否则新地图会带着旧地图的
    /// 「已击败 Boss / 困难模式」跑起来 —— 这正是本改动前「换图即重置进度」行为的等价保留。
    /// </summary>
    Task ClearWorldChangesAsync();
}

/// <summary>单格图格改动：坐标 + 定长序列化图格（见 <c>Tile.Serialize</c>）。</summary>
public record WorldTileRecord(int X, int Y, byte[] Data, string WorldId = "default", long Version = 0);

/// <summary>
/// 单个箱子的内容改动：索引 + 坐标 + 物品格序列化字节（见 <c>Chest.SerializeItems</c>）。
/// 回放时按索引定位并校验坐标，避免基准世界被替换后错位套用。
/// </summary>
public record WorldChestRecord(int Index, int X, int Y, byte[] Data, string WorldId = "default", long Version = 0);

/// <summary>
/// 图格实体覆盖：以锚点为稳定键，保存完整 section-5 单实体文件负载；删除时保留墓碑，
/// 防止同锚点的基准世界实体在重启回放时复活。
/// </summary>
public record WorldTileEntityRecord(int RuntimeId, int FileId, byte Type, short X, short Y, byte[]? Data, bool IsDeleted, string WorldId = "default", long Version = 0);

/// <summary>
/// 世界进度存档：<see cref="Data"/> 是 <c>WorldProgressCodec</c> 产出的版本化字节串
/// （当前为 1 字节版本 + 11 字节进度位图）。存字节而不再拆列：进度位会随原版版本增删，
/// 拆成 82 个列会每加一位就要改表结构，而版本化字节串只需升 codec 版本。
/// </summary>
public record WorldProgressRecord(byte[] Data, string WorldId = "default");

public record PlayerData(Guid Id, string Name, byte[] InventoryBlob, int MaxHp, int MaxMp);
public record AuditEntry(DateTime Timestamp, Guid PlayerId, string EventType, string Detail, string? IpAddress);
