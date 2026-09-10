// TerraAuth — Phase 3: Command 命令模型
// 架构 §4.3：所有状态变更经 Command → WorldSimulator.Tick 应用

using System.Collections.Concurrent;
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>命令基类。客户端意图 → Authority 接受 → Command → 仿真。</summary>
public abstract record Command(
    long Tick,       // 目标 tick（由提交时刻的 GameLoop tick 决定）
    int? PlayerId,   // 发起玩家（服务端命令为 null）
    string Kind)     // "move" / "use_item" / "place_tile" / "attack"
{
    /// <summary>应用命令到世界状态。这是唯一允许变更 WorldState 的地方。</summary>
    public abstract void Apply(WorldState world, IRng rng);
}

/// <summary>移动命令：移动权威接受位置包后生成，由仿真在目标 tick 应用。</summary>
public sealed record MoveCommand(long Tick, int? PlayerId, Vector2 Position)
    : Command(Tick, PlayerId, "move")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return;

        if (!world.Players.TryGetValue(id, out var player))
        {
            player = new PlayerRuntime { Id = id };
            world.Players[id] = player;
        }

        // 权威位置赋值：服务端校验通过后直接生效，并清零速度（避免与物理阶段积分叠加）
        player.Position = Position;
        player.Velocity = new Vector2(0, 0);
        player.Active = true;
    }
}

/// <summary>命令队列：按 tick 分组、线程安全、稳定排序。</summary>
public sealed class CommandQueue
{
    // (tick, playerId) 稳定排序 → 确定性
    private readonly ConcurrentQueue<Command> _queue = new();

    public int Count => _queue.Count;
    public bool IsEmpty => _queue.IsEmpty;

    public void Enqueue(Command command) => _queue.Enqueue(command);

    public bool TryPeek(out Command? command)
    {
        if (_queue.TryPeek(out var cmd))
        {
            command = cmd;
            return true;
        }
        command = null;
        return false;
    }

    public Command Dequeue()
    {
        _queue.TryDequeue(out var cmd);
        return cmd ?? throw new InvalidOperationException("Queue empty");
    }

    /// <summary>取出指定 tick 的所有命令，按 (tick, playerId) 排序。</summary>
    public IReadOnlyList<Command> DrainThrough(long tick)
    {
        var result = new List<Command>();
        while (_queue.TryPeek(out var cmd) && cmd!.Tick <= tick)
        {
            _queue.TryDequeue(out _);
            result.Add(cmd!);
        }
        result.Sort((a, b) =>
        {
            var t = a.Tick.CompareTo(b.Tick);
            if (t != 0) return t;
            return (a.PlayerId ?? -1).CompareTo(b.PlayerId ?? -1);
        });
        return result;
    }
}
