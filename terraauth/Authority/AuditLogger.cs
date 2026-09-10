// TerraAuth — Phase 2/5: 审计日志占位实现
// 架构 §7.1 结构化安全审计日志

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TerraAuth.Simulation;

namespace TerraAuth.Authority;

/// <summary>控制台审计日志（生产环境应替换为持久化存储）。</summary>
public sealed class ConsoleAuditLogger : IAuditLogger
{
    public void Log(AuditEvent e)
    {
        Console.WriteLine($"[AUDIT] {e.Timestamp:O} | {e.PlayerId} | {e.Category} | {e.Action} | {e.Reason}");
    }

    public Task FlushAsync() => Task.CompletedTask;
}

/// <summary>空操作审计（测试用）。</summary>
public sealed class NoOpAuditLogger : IAuditLogger
{
    public void Log(AuditEvent e) { }
    public Task FlushAsync() => Task.CompletedTask;
}

/// <summary>内存事件存储（架构 §4.2 IEventStore）。</summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly List<GameEvent> _events = new();

    public void Append(GameEvent e) => _events.Add(e);
    public IReadOnlyList<GameEvent> Since(long tick) => _events.FindAll(x => x.Tick >= tick);
    public void CompactBefore(long tick) => _events.RemoveAll(x => x.Tick < tick);
}

// 注：PersistenceAuditLogger（审计 → 持久化/监控/封禁桥接）已移至
// TerraAuth.Persistence（组合根），避免 Authority 层反向依赖 Persistence/Monitoring。
