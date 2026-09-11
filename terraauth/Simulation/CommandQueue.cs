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

/// <summary>挖砖命令：世界权威接受 TileBreak(17) 后生成，由仿真在目标 tick 应用到 WorldState.Tiles。</summary>
public sealed record TileBreakCommand(long Tick, int? PlayerId, int X, int Y, byte Action, int TileType)
    : Command(Tick, PlayerId, "tile_break")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (X < 0 || X >= world.MaxTilesX || Y < 0 || Y >= world.MaxTilesY)
            return;

        // 区块分区锁：与包 10 编码 / 权威校验的跨线程读互斥（详见 SectionLocks）
        world.Sections.EnterWrite(X, Y);
        try
        {
            ref var tile = ref world.Tiles[X, Y];
            // Action=0：挖实心砖 → Active=false，Type=0
            // Action=2/3：挖墙 → Wall=0
            // >=5：电线/斜坡类 → 清对应 flag
            switch (Action)
            {
                case 0:
                    tile.Active = false;
                    tile.Type = 0;
                    tile.Wall = 0;
                    break;
                case 2: // 挖墙
                case 3:
                    tile.Wall = 0;
                    break;
                default: // 电线、斜坡、激活器等 → 清对应位
                    if (Action >= 5)
                    {
                        if (Action == 5) tile.Wire = false;
                        else if (Action == 6) tile.Wire2 = false;
                        else if (Action == 7) tile.Wire3 = false;
                        else if (Action == 8) tile.Wire4 = false;
                        else if (Action == 9) tile.Slope = 0;
                        else if (Action == 10) tile.HalfBrick = false;
                        else if (Action == 11) tile.Actuator = false;
                    }
                    break;
            }
        }
        finally
        {
            world.Sections.ExitWrite(X, Y);
        }
    }
}

/// <summary>放砖命令：世界权威接受 TilePlace(79) 后生成，由仿真在目标 tick 应用到 WorldState.Tiles。</summary>
public sealed record TilePlaceCommand(long Tick, int? PlayerId, int X, int Y, int TileType, int Style)
    : Command(Tick, PlayerId, "tile_place")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (X < 0 || X >= world.MaxTilesX || Y < 0 || Y >= world.MaxTilesY)
            return;

        // 区块分区锁：与包 10 编码 / 权威校验的跨线程读互斥（详见 SectionLocks）
        world.Sections.EnterWrite(X, Y);
        try
        {
            ref var tile = ref world.Tiles[X, Y];
            tile.Active = true;
            tile.Type = (ushort)TileType;
            tile.Wall = 0;
        }
        finally
        {
            world.Sections.ExitWrite(X, Y);
        }
    }
}

/// <summary>NPC 受击命令：包 28 权威通过后生成，由仿真扣减 NPC 生命（生命归零即死亡）。</summary>
public sealed record NpcStrikeCommand(long Tick, int? PlayerId, int NpcIndex, int Damage)
    : Command(Tick, PlayerId, "npc_strike")
{
    public override void Apply(WorldState world, IRng rng)
    {
        lock (world.NpcsLock)
        {
            if (NpcIndex < 0 || NpcIndex >= world.Npcs.Count)
                return;

            var npc = world.Npcs[NpcIndex];
            if (!npc.Active)
                return;

            npc.Life -= Damage;
            if (npc.Life <= 0)
            {
                npc.Life = 0;
                npc.Active = false; // 由世界同步下发 life=0，客户端据此移除
                npc.DeadTick = Tick;
            }
        }
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
