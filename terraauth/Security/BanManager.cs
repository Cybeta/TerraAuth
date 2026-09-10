// Phase 6 - 封禁管理器默认实现
// 滑动窗口累计违规 → 达阈值自动封禁（配置驱动）

using System.Collections.Concurrent;
using TerraAuth.Config;
using TerraAuth.Persistence;

namespace TerraAuth.Security;

public sealed class BanManager : IBanManager
{
    private readonly IBanStore _store;
    private readonly IConfigurationService _config;
    private readonly IAuditRepository _audit;
    // 进程内违规计数：PlayerId -> 有序时间戳列表
    private readonly ConcurrentDictionary<Guid, List<DateTime>> _violations = new();

    public BanManager(IBanStore store, IConfigurationService config, IAuditRepository audit)
    {
        _store = store;
        _config = config;
        _audit = audit;
    }

    public async Task<bool> ReportViolationAsync(Guid playerId, string reason)
    {
        var window = _config.Current.ViolationWindowMinutes;
        var threshold = _config.Current.MaxViolationsBeforeBan;
        var now = DateTime.UtcNow;

        var list = _violations.GetOrAdd(playerId, _ => new List<DateTime>());
        lock (list)
        {
            // 移除窗口外的旧记录（滑动窗口）
            list.RemoveAll(t => (now - t).TotalMinutes > window);
            list.Add(now);
            if (list.Count >= threshold)
            {
                // 达阈值 → 自动封禁（默认 24h，可配置）
                _ = BanAsync(playerId, $"auto:{reason} (x{list.Count})", TimeSpan.FromHours(24));
                list.Clear();
                return true;
            }
        }
        await _audit.AppendAsync(new AuditEntry(now, playerId, "violation", reason, null));
        return false;
    }

    public async Task<bool> IsBannedAsync(Guid playerId, string ipAddress)
        => await _store.ContainsAsync(playerId, ipAddress);

    public async Task BanAsync(Guid playerId, string reason, TimeSpan? duration)
    {
        var expires = DateTime.UtcNow.Add(duration ?? TimeSpan.FromHours(24));
        await _store.AddAsync(new BanRecord(playerId, "", reason, expires));
        await _audit.AppendAsync(new AuditEntry(DateTime.UtcNow, playerId, "ban", reason, null));
    }

    public async Task UnbanAsync(Guid playerId, string reason)
    {
        await _store.RemoveAsync(playerId);
        await _audit.AppendAsync(new AuditEntry(DateTime.UtcNow, playerId, "unban", reason, null));
    }
}
