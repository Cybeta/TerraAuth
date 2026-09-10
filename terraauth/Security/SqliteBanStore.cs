// Phase 6 - 真实 IBanStore 实现
// 1) SqliteBanStore：基于 SqlitePersistence 的 IDbExecutor（存档 + 封禁共用 DB）
// 2) InMemoryBanStore：进程内（测试 / 单实例快速启动）
// 设计：依赖 IDbExecutor 抽象，不持有 SqlitePersistence 引用 → 无循环依赖

using System.Collections.Concurrent;
using TerraAuth.Persistence;

namespace TerraAuth.Security;

/// <summary>基于 SQLite 的封禁存储（生产级）。</summary>
public sealed class SqliteBanStore : IBanStore, IDisposable
{
    private readonly IDbExecutor _db;

    public SqliteBanStore(SqlitePersistence db)
    {
        // 通过内部访问器拿到 Executor（IDbExecutor），避免类型强耦合
        _db = db.GetExecutor();
    }

    // 注：Add/Contains/Remove 的 SQL 逻辑在 IDbExecutor 层面统一（见下方扩展）
    public async Task AddAsync(BanRecord record)
        => await Task.Run(() => _db.SaveBan(record)).ConfigureAwait(false);

    public async Task<bool> ContainsAsync(Guid playerId, string ipAddress)
        => await Task.Run(() => _db.IsBanned(playerId, ipAddress)).ConfigureAwait(false);

    public async Task RemoveAsync(Guid playerId)
        => await Task.Run(() => _db.RemoveBan(playerId)).ConfigureAwait(false);

    public void Dispose() { /* IDbExecutor 生命周期由 SqlitePersistence 管理 */ }
}

/// <summary>进程内封禁存储（测试 / 单实例快速启动，零依赖）。</summary>
public sealed class InMemoryBanStore : IBanStore
{
    private readonly ConcurrentDictionary<Guid, BanRecord> _records = new();

    public Task AddAsync(BanRecord record)
    {
        _records[record.PlayerId] = record;
        return Task.CompletedTask;
    }

    public Task<bool> ContainsAsync(Guid playerId, string ipAddress)
    {
        if (_records.TryGetValue(playerId, out var r) && r.ExpiresAt > DateTime.UtcNow) return Task.FromResult(true);
        if (!string.IsNullOrEmpty(ipAddress))
            return Task.FromResult(_records.Values.Any(r => r.IpAddress == ipAddress && r.ExpiresAt > DateTime.UtcNow));
        return Task.FromResult(false);
    }

    public Task RemoveAsync(Guid playerId)
    {
        _records.TryRemove(playerId, out _);
        return Task.CompletedTask;
    }
}
