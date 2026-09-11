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
    // 进程内违规计数：PlayerId -> 有序时间戳列表（附带 .NET 9+ 轻量锁）
    private readonly ConcurrentDictionary<Guid, ViolationWindow> _violations = new();

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

        var state = _violations.GetOrAdd(playerId, _ => new ViolationWindow());
        lock (state.Gate)
        {
            var list = state.Times;
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

    /// <summary>单玩家的违规时间戳窗口（锁与数据同置，替代对 List 实例加锁）。</summary>
    private sealed class ViolationWindow
    {
        public readonly Lock Gate = new();
        public readonly List<DateTime> Times = new();
    }
}
