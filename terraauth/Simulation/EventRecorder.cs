// TerraAuth — Phase 3: 事件记录器（事件溯源）
// 架构 §4.3：所有状态变更以 Command + Event 形式记录

using System.Collections.Concurrent;

namespace TerraAuth.Simulation;

/// <summary>游戏事件（状态变更的结果）。</summary>
public sealed record GameEvent(
    long Tick,
    int? PlayerId,
    string Kind,
    object? Payload);

/// <summary>事件记录器：追加式，供审计 / 回放 / 调试。</summary>
public sealed class EventRecorder
{
    private readonly ConcurrentBag<GameEvent> _events = new();

    public void Record(GameEvent e) => _events.Add(e);
    public IReadOnlyCollection<GameEvent> All => _events;
    public void Clear() => _events.Clear();
}
