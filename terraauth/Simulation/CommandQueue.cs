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

        PlayerRuntime player;
        lock (world.PlayersLock)
        {
            if (!world.Players.TryGetValue(id, out var existing))
            {
                existing = new PlayerRuntime { Id = id };
                world.Players[id] = existing;
            }
            player = existing;
        }

        // 死亡期间不接受移动：复活点由服务端在复活命令中划定，避免"死后瞬移"
        if (player.Dead)
            return;

        // 权威位置赋值：服务端校验通过后直接生效，并清零速度（避免与物理阶段积分叠加）
        player.Position = Position;
        player.Velocity = new Vector2(0, 0);
        player.Active = true;
    }
}

/// <summary>玩家受伤命令：包 117 权威通过后生成，由仿真结算服务端生命。</summary>
public sealed record DamagePlayerCommand(long Tick, int? PlayerId, int Damage)
    : Command(Tick, PlayerId, "damage_player")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id || Damage <= 0)
            return;

        PlayerRuntime? player;
        lock (world.PlayersLock)
            world.Players.TryGetValue(id, out player);
        if (player is null || player.Dead)
            return;

        player.Hp -= Damage;
        if (player.Hp <= 0)
        {
            player.Hp = 0;
            player.Dead = true;
            player.FallDistance = 0f;
            player.Velocity = new Vector2(0, 0);
            player.DeathNotified = false;  // 世界同步线程据此补发死亡包 118
        }
    }
}

/// <summary>玩家死亡命令：包 118（客户端声明死亡）权威通过后生成。</summary>
public sealed record KillPlayerCommand(long Tick, int? PlayerId)
    : Command(Tick, PlayerId, "kill_player")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return;

        PlayerRuntime? player;
        lock (world.PlayersLock)
            world.Players.TryGetValue(id, out player);
        if (player is null)
            return;

        player.Hp = 0;
        player.FallDistance = 0f;
        player.Velocity = new Vector2(0, 0);

        if (!player.Dead)
        {
            player.Dead = true;
            player.DeathNotified = false; // 首次死亡才下发死亡包，重复声明不重复广播
        }
    }
}

/// <summary>
/// 复活命令：包 12（Playing 阶段的出生包 = 复活请求）权威通过后生成。
/// 服务端权威：复活点固定为世界出生点，客户端上报坐标不采信（防复活瞬移）。
/// </summary>
public sealed record RespawnCommand(long Tick, int? PlayerId)
    : Command(Tick, PlayerId, "respawn")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return;

        PlayerRuntime? player;
        lock (world.PlayersLock)
            world.Players.TryGetValue(id, out player);
        if (player is null || !player.Dead)
            return; // 未死亡 → 非复活请求，忽略

        player.Hp = player.HpMax;
        player.Dead = false;
        player.FallDistance = 0f;
        player.Position = new Vector2((world.SpawnTileX + 0.5f) * 16f, world.SpawnTileY * 16f);
        player.Velocity = new Vector2(0, 0);
        player.DeathNotified = true;
        player.RespawnNotified = false; // 世界同步线程据此补发复活包 12 / 16
    }
}

/// <summary>物品拾取命令：包 22 权威通过后生成，由仿真移除世界掉落物实体。</summary>
public sealed record PickupItemCommand(long Tick, int? PlayerId, int ItemSlotIndex)
    : Command(Tick, PlayerId, "pickup_item")
{
    public override void Apply(WorldState world, IRng rng)
    {
        lock (world.ItemsLock)
        {
            foreach (var item in world.Items)
            {
                if (item.Slot != ItemSlotIndex || !item.Active) continue;

                item.Active = false;
                item.OwnedBy = PlayerId ?? -1;
                item.DeadTick = Tick;
                item.RemovalNotified = false; // 由世界同步下发包 21（stack=0）通知其他客户端移除
                return;
            }
        }
    }
}

/// <summary>
/// 箱子物品写入命令：包 32 权威通过后生成，由仿真把客户端上报的槽位内容写入服务端箱子。
/// 服务端持有箱子内容唯一真相（客户端上报经校验后才落盘）。
/// </summary>
public sealed record SyncChestItemCommand(
    long Tick, int? PlayerId, int ChestIndex, int Slot, int Stack, byte Prefix, int ItemType)
    : Command(Tick, PlayerId, "sync_chest_item")
{
    public override void Apply(WorldState world, IRng rng)
    {
        lock (world.ChestsLock)
        {
            var chest = world.FindChestByIndex(ChestIndex);
            if (chest is null || Slot < 0 || Slot >= chest.Items.Length) return;

            chest.Items[Slot] = new ChestItem
            {
                Type = ItemType,
                Stack = (short)Stack,
                Prefix = Prefix,
            };
        }
    }
}

/// <summary>
/// 液体编辑命令：客户端液体上报（包 82 模块 0）权威通过后生成，由仿真写入权威图格并触发流动仿真。
/// </summary>
public sealed record LiquidEditCommand(long Tick, int? PlayerId, IReadOnlyList<LiquidChange> Changes)
    : Command(Tick, PlayerId, "liquid_edit")
{
    public override void Apply(WorldState world, IRng rng)
    {
        foreach (var change in Changes)
        {
            if (change.X < 0 || change.X >= world.MaxTilesX ||
                change.Y < 0 || change.Y >= world.MaxTilesY) continue;

            world.Sections.EnterWrite(change.X, change.Y);
            try
            {
                ref var tile = ref world.Tiles[change.X, change.Y];

                // 实心格（含未通电的执行器方块）不允许存液
                if (tile.Active && TileIdSets.IsTileSolid(tile.Type) && !tile.InActive) continue;

                tile.Liquid = change.Amount;
                tile.LiquidType = change.Amount == 0 ? (byte)0 : change.Type;
            }
            finally
            {
                world.Sections.ExitWrite(change.X, change.Y);
            }

            world.MarkLiquidChanged(change.X, change.Y);
        }
    }
}

/// <summary>挖砖 / 改砖命令：世界权威接受 TileBreak(17) 后生成，由仿真应用到 WorldState.Tiles。</summary>
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

            // 包 17 的 action 语义（0..19）：
            //   0 KillTile / 1 PlaceTile / 2 KillWall / 3 PlaceWall / 4 KillTileNoItem
            //   5 PlaceWire / 6 KillWire / 7 PoundTile / 8 PlaceActuator / 9 KillActuator
            //   10 PlaceWire2 / 11 KillWire2 / 12 PlaceWire3 / 13 KillWire3 / 14 SlopeTile
            //   15 FrameTrack / 16 PlaceWire4 / 17 KillWire4 / 18 PokeLogicGate / 19 Actuate
            switch (Action)
            {
                case 0:  // KillTile
                case 4:  // KillTileNoItem
                    tile.Active = false;
                    tile.Type = 0;
                    tile.Wall = 0;
                    break;

                case 1:  // PlaceTile
                    if (TileType <= 0) break;
                    tile.Active = true;
                    tile.Type = (ushort)TileType;
                    break;

                case 2:  // KillWall
                    tile.Wall = 0;
                    break;

                case 3:  // PlaceWall
                    if (TileType <= 0) break;
                    tile.Wall = (ushort)TileType;
                    break;

                case 5: tile.Wire = true; break;
                case 6: tile.Wire = false; break;
                case 10: tile.Wire2 = true; break;
                case 11: tile.Wire2 = false; break;
                case 12: tile.Wire3 = true; break;
                case 13: tile.Wire3 = false; break;
                case 16: tile.Wire4 = true; break;
                case 17: tile.Wire4 = false; break;

                case 8:  // PlaceActuator
                    tile.Actuator = true;
                    break;

                case 9:  // KillActuator
                    tile.Actuator = false;
                    tile.InActive = false;
                    break;

                // 7 PoundTile / 14 SlopeTile / 15 FrameTrack / 18 PokeLogicGate：
                // 语义依赖更多上下文字段（斜坡/半砖编码、逻辑门），暂不建模——按无操作处理，避免误改世界。
                default:
                    break;
            }
        }
        finally
        {
            world.Sections.ExitWrite(X, Y);
        }

        world.MarkPersistTile(X, Y); // 客户端发起的图格改动同样要落盘（重启后回放）
    }
}

/// <summary>
/// 电路触发命令：包 17 action 19（Actuate）权威通过后生成。
/// 服务端沿电线（4 种线色任一）从触发点做受限 BFS，翻转连通路径上所有执行器方块的 <c>InActive</c>。
/// 属简化信号传播：不做门电路 / 定时器 / 压力板等元件逻辑，只保证执行器状态由服务端权威决定。
/// </summary>
public sealed record ActuateCommand(long Tick, int? PlayerId, int X, int Y)
    : Command(Tick, PlayerId, "actuate")
{
    /// <summary>坐标打包步长（大于最大世界宽度 8400）。</summary>
    private const int Stride = 16384;

    /// <summary>单次触发的最大传播格数（防大面积线网拖慢 tick）。</summary>
    private const int MaxCircuitTiles = 2000;

    public override void Apply(WorldState world, IRng rng)
    {
        if (X < 0 || X >= world.MaxTilesX || Y < 0 || Y >= world.MaxTilesY)
            return;

        var visited = new HashSet<int> { Y * Stride + X };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((X, Y));

        while (queue.Count > 0 && visited.Count <= MaxCircuitTiles)
        {
            var (cx, cy) = queue.Dequeue();

            bool hasWire;
            bool toggled = false;
            world.Sections.EnterWrite(cx, cy);
            try
            {
                ref var tile = ref world.Tiles[cx, cy];
                if (tile.Actuator)
                {
                    tile.InActive = !tile.InActive;
                    toggled = true;
                }
                hasWire = tile.Wire || tile.Wire2 || tile.Wire3 || tile.Wire4;
            }
            finally
            {
                world.Sections.ExitWrite(cx, cy);
            }

            // 服务端驱动的图格变更 → 排队推送客户端（包 10 小矩形）
            if (toggled) world.MarkTileChanged(cx, cy);

            if (!hasWire) continue;

            TryEnqueue(cx - 1, cy);
            TryEnqueue(cx + 1, cy);
            TryEnqueue(cx, cy - 1);
            TryEnqueue(cx, cy + 1);

            void TryEnqueue(int nx, int ny)
            {
                if (nx < 0 || nx >= world.MaxTilesX || ny < 0 || ny >= world.MaxTilesY) return;
                if (!visited.Add(ny * Stride + nx)) return;

                Tile neighbor;
                using (world.Sections.EnterRead(nx, ny, nx, ny))
                    neighbor = world.Tiles[nx, ny];

                if (neighbor.Wire || neighbor.Wire2 || neighbor.Wire3 || neighbor.Wire4)
                    queue.Enqueue((nx, ny));
            }
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

        world.MarkPersistTile(X, Y); // 客户端发起的图格改动同样要落盘（重启后回放）
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
                world.NotifyNpcKilled(npc.Type, npc.X, npc.Y); // Boss 击杀 → 世界进度 + 掉落
            }
        }
    }
}

/// <summary>掉落物生成命令：包 21 权威通过后生成；槽位由服务端分配（不采信客户端上报值）。</summary>
public sealed record SpawnItemCommand(
    long Tick, int? PlayerId, int ItemId, int Stack, Vector2 Position, Vector2 Velocity, byte Prefix)
    : Command(Tick, PlayerId, "spawn_item")
{
    /// <summary>世界掉落物槽位上限（与原版 <c>Main.item[400]</c> 一致）。</summary>
    private const int MaxSlots = 400;

    public override void Apply(WorldState world, IRng rng)
    {
        lock (world.ItemsLock)
        {
            if (world.Items.Count >= MaxSlots)
                return;

            int slot = 0;
            while (world.Items.Any(i => i.Slot == slot)) slot++;

            world.Items.Add(new WorldItemEntity
            {
                Slot = slot,
                ItemId = ItemId,
                Stack = Stack,
                Position = Position,
                Velocity = Velocity,
                Prefix = Prefix,
                OwnedBy = PlayerId ?? -1,
            });
        }
    }
}

/// <summary>弹幕生成 / 更新命令：包 27 权威通过后生成，服务端登记并推进其生命周期。</summary>
public sealed record SpawnProjectileCommand(
    long Tick, int? PlayerId, int Key, int Type, Vector2 Position, Vector2 Velocity, int Damage)
    : Command(Tick, PlayerId, "spawn_projectile")
{
    public override void Apply(WorldState world, IRng rng)
    {
        lock (world.ProjectilesLock)
        {
            // 同一 Key 视为同一弹幕的更新（原版 projectile 索引由归属者选定）
            var existing = world.Projectiles.FirstOrDefault(p => p.Key == Key);
            if (existing is not null)
            {
                existing.Position = Position;
                existing.Velocity = Velocity;
                existing.Active = true;
                existing.RemovalNotified = false;
                return;
            }

            world.Projectiles.Add(new ProjectileEntity
            {
                Key = Key,
                Owner = PlayerId ?? -1,
                Type = Type,
                Position = Position,
                Velocity = Velocity,
                Damage = Damage,
            });
        }
    }
}

/// <summary>弹幕销毁命令：包 29 权威通过后生成（仅归属者可销毁）。</summary>
public sealed record KillProjectileCommand(long Tick, int? PlayerId, int Key, Vector2 Position)
    : Command(Tick, PlayerId, "kill_projectile")
{
    public override void Apply(WorldState world, IRng rng)
    {
        lock (world.ProjectilesLock)
        {
            foreach (var p in world.Projectiles)
            {
                if (p.Key != Key || !p.Active) continue;

                // 服务端权威：只有归属者能销毁自己的弹幕（防伪造他人弹幕消失）
                if (PlayerId is int owner && p.Owner != owner) return;

                p.Position = Position;
                p.Active = false;
                p.DeadTick = Tick;
                p.RemovalNotified = true; // 客户端已发起销毁，无需服务端再补发
                return;
            }
        }
    }
}

/// <summary>法力更新命令：包 42 权威通过后生成（服务端跟踪法力；原版不对他人转发）。</summary>
public sealed record SetManaCommand(long Tick, int? PlayerId, int Mana, int MaxMana)
    : Command(Tick, PlayerId, "set_mana")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id || MaxMana <= 0)
            return;

        PlayerRuntime? player;
        lock (world.PlayersLock)
            world.Players.TryGetValue(id, out player);
        if (player is null)
            return;

        player.MpMax = MaxMana;
        player.Mp = Math.Clamp(Mana, 0, MaxMana);
    }
}

/// <summary>
/// 治疗命令：包 35 权威通过后生成。服务端为唯一真相源——回血上限钳制到服务端 HpMax，
/// 客户端上报的超额治疗不会让服务端生命越界。
/// </summary>
public sealed record HealPlayerCommand(long Tick, int? PlayerId, int Amount)
    : Command(Tick, PlayerId, "heal_player")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id || Amount <= 0)
            return;

        PlayerRuntime? player;
        lock (world.PlayersLock)
            world.Players.TryGetValue(id, out player);
        if (player is null || player.Dead)
            return;

        player.Hp = Math.Min(player.Hp + Amount, player.HpMax);
    }
}

/// <summary>增益列表命令：包 50 权威通过后生成（服务端持有增益列表唯一真相）。</summary>
public sealed record SetBuffsCommand(long Tick, int? PlayerId, IReadOnlyList<int> Buffs)
    : Command(Tick, PlayerId, "set_buffs")
{
    public override void Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return;

        PlayerRuntime? player;
        lock (world.PlayersLock)
            world.Players.TryGetValue(id, out player);
        if (player is null)
            return;

        player.Buffs.Clear();
        player.Buffs.AddRange(Buffs);
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
