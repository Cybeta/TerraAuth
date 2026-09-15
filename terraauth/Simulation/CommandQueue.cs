// TerraAuth — Phase 3: Command 命令模型
// 架构 §4.3：所有状态变更经 Command → WorldSimulator.Tick 应用

using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>召唤武器判定 helper（ItemDamageTable 权威职业 = Summon）。</summary>
internal static class SummonWeaponLookup
{
    public static bool IsSummonWeapon(int itemId)
        => ItemDamageTable.Of.TryGetValue(itemId, out var stats) && stats.Class == WeaponClass.Summon;
}

public readonly record struct CommandApplyResult(bool Applied, string? Reason = null);

/// <summary>命令基类。客户端意图 → Authority 接受 → Command → 仿真。</summary>
public abstract record Command(
    long Tick,       // 目标 tick（由提交时刻的 GameLoop tick 决定）
    int? PlayerId,   // 发起玩家（服务端命令为 null）
    string Kind)     // "move" / "use_item" / "place_tile" / "attack"
{
    public long SessionId { get; init; }

    protected bool TryGetPlayer(
        WorldState world,
        int playerId,
        out PlayerRuntime? player,
        out CommandApplyResult failure)
    {
        lock (world.PlayersLock)
            world.Players.TryGetValue(playerId, out player);

        if (player is null)
        {
            failure = new(false, CommandFailures.PlayerNotActive);
            return false;
        }

        if (SessionId != 0 && player.SessionId == 0)
            player.SessionId = SessionId;
        else if (SessionId != 0 && player.SessionId != SessionId)
        {
            failure = new(false, CommandFailures.StaleSession);
            return false;
        }

        if (!player.Active)
        {
            failure = new(false, CommandFailures.PlayerNotActive);
            return false;
        }

        failure = default;
        return true;
    }

    /// <summary>应用命令到世界状态。这是唯一允许变更 WorldState 的地方。</summary>
    public abstract CommandApplyResult Apply(WorldState world, IRng rng);
}

/// <summary>移动命令：移动权威接受位置包后生成，由仿真在目标 tick 应用。</summary>
public sealed record MoveCommand(long Tick, int? PlayerId, Vector2 Position)
    : Command(Tick, PlayerId, "move")
{
    /// <summary>
    /// 包 13 携带的控制位（bit0 上 / bit1 下 / bit2 左 / bit3 右 / bit4 跳 …）。
    /// 服务端据此**自己推进玩家物理**（原版服务端对远端玩家也跑 <c>Player.Update</c>），
    /// 这样两次位置包之间玩家坐标不会停住（客户端只在操作变化时发包）。
    /// </summary>
    public byte ControlBits { get; init; }

    /// <summary>包 13 若携带速度（StateBits bit2）则一并采纳，使服务端状态与客户端对齐。</summary>
    public Vector2? ReportedVelocity { get; init; }

    /// <summary>包 13 携带的当前手持热键槽（原版 <c>Player.selectedItem</c>），阶段 E 近战武器校验据此定位手持武器。</summary>
    public byte SelectedItem { get; init; }

    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return new(false, CommandFailures.MissingPlayer);

        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y))
            return new(false, CommandFailures.InvalidPosition);

        PlayerRuntime player;
        lock (world.PlayersLock)
        {
            if (!world.Players.TryGetValue(id, out var existing))
            {
                if (SessionId != 0)
                    return new(false, CommandFailures.PlayerNotActive);
                existing = new PlayerRuntime { Id = id, SessionId = SessionId };
                world.Players[id] = existing;
            }
            player = existing;
        }

        if (SessionId != 0 && player.SessionId != SessionId)
            return new(false, CommandFailures.StaleSession);

        // 死亡期间不接受移动：复活点由服务端在复活命令中划定，避免"死后瞬移"
        if (player.Dead)
            return new(false, CommandFailures.NotApplied);

        // 客户端上报的 Y 与上一包持平 → 玩家处于站立 / 贴地状态，不可能在下落 → 清空下落累计。
        // 这是**客户端权威信号**，用于兜住服务端落地判定在台阶 / 边界处的偶发漏判。
        if (MathF.Abs(Position.Y - player.Position.Y) < 0.05f)
            player.FallDistance = 0f;

        // 权威赋值：位置以客户端上报为准（原版服务端同样直接赋值），
        // 速度若有上报则采纳、否则保留本地模拟值（不发包期间由控制位继续推进，见 StepPlayerPhysics）。
        player.Position = Position;
        player.AimPosition = Position;
        if (ReportedVelocity is { } reported)
            player.Velocity = reported;
        player.ControlBits = ControlBits;
        if (SelectedItem < PlayerRuntime.InventorySlotCount)
            player.SelectedSlot = SelectedItem;
        player.Active = true;
        world.MarkPlayerChanged(id);
        return new(true);
    }
}

/// <summary>
/// 玩家受伤命令：包 117（原版 PlayerHurtV2）权威通过后生成。
/// 服务端权威结算：前置「此刻确实接触敌怪」（防伪造远程受伤）→ 伤害上界
/// （接触者基础伤害 × ±15% 浮动上界 → 减玩家防御，原版 <c>CalculateDamagePlayersTake</c>）→
/// 区间内按客户端上报值扣血（客户端显示 = 服务端扣血，完全一致）。
/// </summary>
public sealed record DamagePlayerCommand(long Tick, int? PlayerId, int Damage)
    : Command(Tick, PlayerId, "damage_player")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id || Damage <= 0)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;
        if (player!.Dead)
            return new(false, CommandFailures.NotApplied);

        // 前置：免伤帧内忽略（与服务端 contact_damage 共享统一免伤帧，避免同一次接触双扣——
        // 客户端本地 Hurt 上报的包 117 与服务端接触兜底是「同一次伤害的两条表达」，只扣一次）
        if (player.HurtCooldown > 0)
        {
            Console.WriteLine($"[Hurt] 玩家 #{id} 上报伤害 {Damage}（包117）但免伤帧中，忽略");
            return new(false, CommandFailures.NotApplied);
        }

        // 前置：此刻必须确实接触着敌怪（防伪造远程受伤；判定口径与服务端接触兜底一致）
        if (!CombatResolver.IsPlayerInContact(world, player))
        {
            Console.WriteLine($"[Hurt] 玩家 #{id} 上报伤害 {Damage}（包117）但未接触敌怪，忽略");
            return new(false, CommandFailures.NotApplied);
        }

        // 伤害上界：接触者最大基础伤害 × ±15% 浮动上界 → 减玩家防御（原版 CalculateDamagePlayersTake，按难度取分支）
        int contact = CombatResolver.FindContactDamage(world, player, out _, out _);
        int upper = CombatResolver.CalculateDamagePlayersTake(
            (int)Math.Ceiling(contact * 1.15f), player.Defense, CombatResolver.FromWorldDifficulty(world.GameMode));

        if (Damage > upper)
        {
            Console.WriteLine($"[Hurt] 玩家 #{id} 上报伤害 {Damage} 超上界 {upper}（接触={contact} def={player.Defense}），拒绝");
            return new(false, CommandFailures.HurtDamageAboveLimit);
        }

        // 区间内：按客户端上报值（含 ±15% 浮动）扣血 → 客户端显示 = 服务端扣血，完全一致
        world.ApplyDamageToPlayer(player, Damage, PlayerRuntime.GeneralImmunityTicks(Damage));
        return new(true);
    }
}

/// <summary>
/// 物品栏槽位写入命令：包 5（InventorySlot）权威通过后生成。
/// 服务端持有物品栏唯一真相（SSC），装备区槽位的物品防御由此回填 <see cref="PlayerRuntime.Defense"/>，
/// 供包 117 区间上界与接触兜底结算按真实装备减防（阶段 D 第二部分）。
/// </summary>
public sealed record SetInventorySlotCommand(long Tick, int? PlayerId, int Slot, int ItemId, int Stack, byte Prefix = 0)
    : Command(Tick, PlayerId, "inventory_slot")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return new(false, CommandFailures.MissingPlayer);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;
        if (Slot < 0 || Slot >= PlayerRuntime.InventorySlotCount)
            return new(false, CommandFailures.NotApplied);

        // 可配置「移除召唤武器即销毁」：该槽位从召唤武器变为非召唤武器（清空 / 换出）时，
        // 销毁该玩家全部存活召唤弹幕（默认 false 保持原版行为——召唤物不随武器移除而消失）。
        var removedId = player!.Items[Slot];
        if (world.DestroySummonsOnWeaponRemoval && removedId != 0 &&
            removedId != (Stack > 0 ? ItemId : 0) &&
            SummonWeaponLookup.IsSummonWeapon(removedId))
        {
            world.KillSummonedProjectiles(id);
        }

        // 空槽（Stack == 0）允许任意 ItemId（清空语义），统一记为 0；前缀同步清零
        player.Items[Slot] = Stack > 0 ? ItemId : 0;
        player.ItemPrefixes[Slot] = Stack > 0 ? Prefix : (byte)0;
        player.RecalculateDefense();
        return new(true);
    }
}

/// <summary>玩家死亡命令：包 118（客户端声明死亡）权威通过后生成。</summary>
public sealed record KillPlayerCommand(long Tick, int? PlayerId)
    : Command(Tick, PlayerId, "kill_player")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;

        player!.Hp = 0;
        player.FallDistance = 0f;
        player.Velocity = new Vector2(0, 0);

        if (!player.Dead)
        {
            player.Dead = true;
            player.DeathNotified = false; // 首次死亡才下发死亡包，重复声明不重复广播
        }
        return new(true);
    }
}

/// <summary>
/// 复活命令：包 12（Playing 阶段的出生包 = 复活请求）权威通过后生成。
/// 服务端权威：复活点固定为世界出生点，客户端上报坐标不采信（防复活瞬移）。
/// </summary>
public sealed record RespawnCommand(long Tick, int? PlayerId)
    : Command(Tick, PlayerId, "respawn")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;
        if (!player!.Dead)
            return new(false, CommandFailures.NotApplied);

        player.Hp = player.HpMax;
        player.Dead = false;
        player.FallDistance = 0f;
        player.Position = new Vector2((world.SpawnTileX + 0.5f) * 16f, world.SpawnTileY * 16f);
        player.AimPosition = player.Position;
        player.Velocity = new Vector2(0, 0);
        // 重生无敌帧：原版 Player.Spawn（ReviveFromDeath）immuneTime = 180（3 秒），
        // 服务端同样生效（接触兜底在免伤帧内跳过），与客户端重生闪烁对齐
        player.HurtCooldown = PlayerRuntime.RespawnImmunityTicks;
        world.MarkPlayerChanged(id);
        player.DeathNotified = true;
        player.RespawnNotified = false; // 世界同步线程据此补发复活包 12 / 16
        return new(true);
    }
}

/// <summary>物品拾取命令：包 22 权威通过后生成，由仿真移除世界掉落物实体。</summary>
public sealed record PickupItemCommand(long Tick, int? PlayerId, int ItemSlotIndex)
    : Command(Tick, PlayerId, "pickup_item")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);

        if (world.InventoryLedger is null)
            return new(false, CommandFailures.InventoryUnavailable);

        lock (world.PlayersLock)
        {
            if (!world.Players.TryGetValue(playerId, out var player))
                return new(false, CommandFailures.PlayerNotActive);
            if (SessionId != 0 && player.SessionId != SessionId)
                return new(false, CommandFailures.StaleSession);
            if (!player.Active || player.Dead)
                return new(false, CommandFailures.PlayerNotActive);

            lock (world.ItemsLock)
            {
                foreach (var item in world.Items)
                {
                    if (item.Slot != ItemSlotIndex || !item.Active) continue;

                    // 专属掉落物（OwnedBy ≥ 0，如 /give SSC 关闭时的 GiveItemByDrop 方案）：
                    // 只准归属玩家拾取，其余玩家的包 22 拾取请求直接拒绝（物品保持不动）。
                    if (item.OwnedBy >= 0 && item.OwnedBy != playerId)
                        return new(false, CommandFailures.OutOfReach);

                    var dx = player.Position.X - item.Position.X;
                    var dy = player.Position.Y - item.Position.Y;
                    if (dx * dx + dy * dy > 160f * 160f)
                        return new(false, CommandFailures.OutOfReach);

                    if (!world.InventoryLedger.TryAddItemExactly(
                            playerId,
                            item.ItemId,
                            item.Stack))
                    {
                        return new(false, CommandFailures.InventoryFull);
                    }

                    item.Active = false;
                    item.OwnedBy = playerId;
                    item.DeadTick = world.Tick;   // 用仿真 tick（命令的 Tick 可能落后于当前世界 tick）
                    item.RemovalNotified = false; // 由世界同步下发包 21（stack=0）通知其他客户端移除
                    return new(true);
                }
            }
        }

        return new(false, CommandFailures.ItemNotFound);
    }
}

/// <summary>打开箱子命令：包 31 权威通过后生成，由仿真建立带连接会话的箱子会话。</summary>
public sealed record OpenChestCommand(long Tick, int? PlayerId, int X, int Y)
    : Command(Tick, PlayerId, "open_chest")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);

        if (!TryGetPlayer(world, playerId, out var player, out var failure))
            return failure;
        if (player!.Dead)
            return new(false, CommandFailures.PlayerNotActive);

        Chest? chest;
        lock (world.ChestsLock)
        {
            chest = world.FindChestAt(X, Y);
        }
        if (chest is null)
            return new(false, CommandFailures.ChestNotFound);

        world.OpenChestSession(playerId, SessionId, chest.Index);
        return new(true);
    }
}

/// <summary>关闭箱子命令：包 31 负坐标（客户端主动关箱）权威通过后生成，由仿真关闭带会话的打开状态。</summary>
public sealed record CloseChestCommand(long Tick, int? PlayerId)
    : Command(Tick, PlayerId, "close_chest")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);

        world.CloseChestSession(playerId, SessionId);
        return new(true);
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
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);

        lock (world.PlayersLock)
        {
            if (!world.Players.TryGetValue(playerId, out var player))
                return new(false, CommandFailures.PlayerNotActive);
            if (SessionId != 0 && player.SessionId != SessionId)
                return new(false, CommandFailures.StaleSession);
            if (!player.Active || player.Dead)
                return new(false, CommandFailures.PlayerNotActive);

            lock (world.ChestsLock)
            {
                if (!world.HasChestSession(playerId, SessionId, ChestIndex))
                    return new(false, CommandFailures.ChestNotOpen);

                var chest = world.FindChestByIndex(ChestIndex);
                if (chest is null)
                    return new(false, CommandFailures.ChestNotFound);

                if (Slot < 0 || Slot >= chest.Items.Length)
                    return new(false, CommandFailures.InvalidSlot);

                var dx = player.Position.X - chest.X * 16f - 8f;
                var dy = player.Position.Y - chest.Y * 16f - 8f;
                if (dx * dx + dy * dy > 160f * 160f)
                    return new(false, CommandFailures.OutOfReach);

                chest.Items[Slot] = new ChestItem
                {
                    Type = ItemType,
                    Stack = (short)Stack,
                    Prefix = Prefix,
                };
            }
        }

        // 服务端重启后回放：登记该箱子（内容已变更），并在提交后通知已打开该箱子的客户端。
        world.MarkPersistChest(ChestIndex);
        world.MarkChestChanged(ChestIndex, Slot);
        return new(true);
    }
}

/// <summary>
/// 液体编辑命令：客户端液体上报（包 82 模块 0）权威通过后生成，由仿真写入权威图格并触发流动仿真。
/// </summary>
public sealed record LiquidEditCommand(long Tick, int? PlayerId, IReadOnlyList<LiquidChange> Changes)
    : Command(Tick, PlayerId, "liquid_edit")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (!TryGetPlayer(world, playerId, out _, out var failure))
            return failure;

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

        return new(true);
    }
}

/// <summary>挖砖 / 改砖命令：世界权威接受 TileBreak(17) 后生成，由仿真应用到 WorldState.Tiles。</summary>
public sealed record TileBreakCommand(long Tick, int? PlayerId, int X, int Y, byte Action, int TileType)
    : Command(Tick, PlayerId, "tile_break")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (!TryGetPlayer(world, playerId, out _, out var failure))
            return failure;
        if (X < 0 || X >= world.MaxTilesX || Y < 0 || Y >= world.MaxTilesY)
return new(false, CommandFailures.NotApplied);

        // 区块分区锁：与包 10 编码 / 权威校验的跨线程读互斥（详见 SectionLocks）
        bool changed = false;
        world.Sections.EnterWrite(X, Y);
        try
        {
            ref var tile = ref world.Tiles[X, Y];
            var before = tile;

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
            changed = !tile.Equals(before);
        }
        finally
        {
            world.Sections.ExitWrite(X, Y);
        }

        if (changed) world.MarkTileChanged(X, Y);
        return changed ? new(true) : new(false, CommandFailures.NoChange);
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

    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (X < 0 || X >= world.MaxTilesX || Y < 0 || Y >= world.MaxTilesY)
return new(false, CommandFailures.NotApplied);

        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (!TryGetPlayer(world, playerId, out _, out var failure))
            return failure;

        var visited = new HashSet<int> { Y * Stride + X };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((X, Y));

        bool toggledAny = false;
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
            if (toggled) { toggledAny = true; world.MarkTileChanged(cx, cy); }

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

        return toggledAny ? new(true) : new(false, CommandFailures.NoChange);
    }
}

/// <summary>放砖命令：世界权威接受 TilePlace(79) 后生成，由仿真在目标 tick 应用到 WorldState.Tiles。</summary>
public sealed record TilePlaceCommand(long Tick, int? PlayerId, int X, int Y, int TileType, int Style)
    : Command(Tick, PlayerId, "tile_place")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (X < 0 || X >= world.MaxTilesX || Y < 0 || Y >= world.MaxTilesY)
return new(false, CommandFailures.NotApplied);

        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (!TryGetPlayer(world, playerId, out _, out var failure))
            return failure;
        if (world.InventoryLedger is null)
return new(false, CommandFailures.NotApplied);

        // 图格和背包必须在同一提交单元中处理。先占住图格写锁，再确认目标仍为空并扣除物品。
        world.Sections.EnterWrite(X, Y);
        try
        {
            ref var tile = ref world.Tiles[X, Y];
            if (tile.Active || !world.InventoryLedger.ConsumeItem(playerId, TileType))
                return new(false, CommandFailures.NotApplied);
            tile.Active = true;
            tile.Type = (ushort)TileType;
            tile.Wall = 0;
        }
        finally
        {
            world.Sections.ExitWrite(X, Y);
        }

        world.MarkTileChanged(X, Y);
        return new(true);
    }
}

/// <summary>NPC 受击命令：包 28 权威通过后生成，由仿真扣减 NPC 生命（生命归零即死亡）。</summary>
public sealed record NpcStrikeCommand(
    long Tick, int? PlayerId, int NpcIndex, int Damage, int Generation = 0, bool Crit = false)
    : Command(Tick, PlayerId, "npc_strike")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (!TryGetPlayer(world, playerId, out var player, out var failure))
            return failure;

        // 原版服务端在收到包 28 时**无条件**回一个包 162（且在校验之前），客户端据此出队一条待确认伤害。
        // 在此登记可保证后续因 generation / 存活校验而未生效的命中同样被确认，客户端的队列不会漏账。
        world.MarkNpcDamageAck(playerId);

        lock (world.NpcsLock)
        {
            if (NpcIndex < 0 || NpcIndex >= world.Npcs.Count)
                return new(false, CommandFailures.NotApplied);

            var npc = world.Npcs[NpcIndex];
            if (!npc.Active)
            {
                Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 客户端gen={Generation} 服务端已死(type={npc.Type}) dmg={Damage}");
                return new(false, CommandFailures.NotApplied);
            }
            if ((byte)Generation != npc.Generation)
            {
                Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 客户端gen={Generation} 服务端gen={npc.Generation} dmg={Damage}");
                return new(false, CommandFailures.NotApplied);
            }

            // 阶段 C「弹幕伤害匹配」：玩家攻击 NPC 的数值必须落在其**最近存活弹幕**的权威伤害区间内。
            // 原版客户端 Projectile.Damage()：Damage × DamageVar(±15%) × (crit ? 2 : 1)（均发生在防御减伤**之前**，
            // 包 28 上报的是减防御前数值；NPC 防御减伤在服务端结算时应用，见下方 applied）。
            // 上界 = ceil(p.Damage × 1.15) × (crit ? 2 : 1)。
            // 近战挥砍无弹幕（找不到匹配）→ 交棒阶段 E 武器校验。
            bool projectileMatched = false;
            if (world.StrikeProjectileMatch)
            {
                var proj = FindPlayerProjectileNearNpc(world, playerId, npc);
                if (proj is null)
                {
                    Console.WriteLine($"[Strike] slot={NpcIndex} 未找到归属玩家 #{playerId} 的存活弹幕（近战挥砍？），交棒武器校验 dmg={Damage}");
                }
                else
                {
                    int bound = (int)Math.Ceiling(proj.Damage * 1.15f) * (Crit ? 2 : 1);
                    if (Damage > bound)
                    {
                        Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 上报伤害 {Damage} 超弹幕上界 {bound}（弹幕key={proj.Key} dmg={proj.Damage} crit={Crit}）");
                        return new(false, CommandFailures.StrikeDamageMismatch);
                    }
                    projectileMatched = true;
                }
            }

            // 阶段 E「近战武器伤害校验」/ 阶段 F「远程武器校验」/ 阶段 G「召唤弹幕校验」：
            // 无弹幕匹配的命中按**多通道取最大上界**校验——任一合法来源（手持武器或召唤物）
            // 的权威伤害都构成合法上界，上报值超过**所有**通道的上界才拒绝。
            //   · 近战 / 魔法：武器伤害即弹幕伤害（无弹药合并），直接按武器上界校验；
            //   · 远程：弹幕伤害 = 武器 + 弹药（原版 PickAmmo 合并，弹药乘修饰倍率），
            //     未收录弹药类型 / 无弹药合并武器（投掷、鱼叉、gunProj 四件）按武器伤害校验；
            //   · 召唤：仆从伤害 ≠ 手持武器（召唤后切换武器仍沿用创建时伤害），不进手持通道，
            //     改按玩家拥有的存活召唤弹幕最高伤害上界校验（SummonProjectileTable）。
            // 未收录武器 / 空手 / 无召唤弹幕 → 无通道上界，失败放行，绝不误拒未知物品。
            if (!projectileMatched && world.StrikeWeaponCheck)
            {
                int? upperBound = null;

                // 通道 1：手持武器权威伤害上界（近战 / 魔法 / 远程）。
                if (player!.SelectedSlot >= 0 && player.SelectedSlot < PlayerRuntime.InventorySlotCount)
                {
                    int heldItem = player.Items[player.SelectedSlot];
                    byte heldPrefix = player.ItemPrefixes[player.SelectedSlot];
                    if (heldItem > 0
                        && ItemDamageTable.Of.TryGetValue(heldItem, out var heldStats))
                    {
                        if (heldStats.Class is WeaponClass.Melee or WeaponClass.Magic)
                        {
                            int bound = CombatResolver.WeaponDamageBound(player, heldItem, Crit, heldPrefix);
                            upperBound = Math.Max(upperBound ?? 0, bound);
                        }
                        else if (heldStats.Class == WeaponClass.Ranged)
                        {
                            if (CombatResolver.RangedDamageBound(player, heldItem, Crit, heldPrefix) is int rb)
                                upperBound = Math.Max(upperBound ?? 0, rb);
                        }
                        // 召唤武器：武器伤害 ≠ 仆从伤害，不进通道 1（由通道 2 覆盖）。
                    }
                }

                // 通道 2：召唤 / 哨兵上界（阶段 G）。无论手持武器，只要该玩家拥有存活召唤弹幕，
                // 其最高伤害即构成合法上界——召唤物命中不会因「手持弱武器」被误拒。
                // 弹幕基准 + 背包兜底取最大：弹幕未被跟踪时（类型未收录 / 包 27 丢失 / 掉线重连）
                // 由背包最高召唤武器伤害兜底，防作弊不失效；弹幕在时不受「召唤后武器移出背包」影响。
                if (CombatResolver.SummonDamageBound(world, playerId, Crit) is int sb)
                    upperBound = Math.Max(upperBound ?? 0, sb);
                if (CombatResolver.SummonBackpackBound(player, Crit) is int pb)
                    upperBound = Math.Max(upperBound ?? 0, pb);

                if (upperBound is int ub && Damage > ub)
                {
                    Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 上报 {Damage} 超上界 {ub}（crit={Crit}）");
                    return new(false, CommandFailures.StrikeDamageMismatch);
                }
            }

            // 原版服务端收包 28 后的结算（对齐客户端显示口径）：
            //   applied = Main.CalculateDamageNPCsTake(Damage, npc.defense) × (crit ? 2 : 1)
            // 即**先按 NPC 防御减伤**（dmg − def×0.5，最低 1），**再应用暴击倍率**。
            // 漏掉 ×2 会造成「客户端按暴击打死、服务端还差一半血」——客户端贴图消失，服务端该怪仍存活
            // 并继续造成接触伤害（幽灵碰撞）；漏掉防御减伤则服务端扣血多于客户端显示，血量口径不一致。
            int applied = CombatResolver.CalculateDamageNPCsTake(Damage, npc.Defense) * (Crit ? 2 : 1);
            npc.Life -= applied;
            if (npc.Life <= 0)
            {
                npc.Life = 0;
                npc.Active = false; // 由世界同步下发 life=0，客户端据此移除
                npc.DeadTick = Tick;
                Console.WriteLine($"[Kill] slot={NpcIndex} gen={npc.Generation} type={npc.Type} dmg={Damage} def={npc.Defense} applied={applied} @{npc.X:F0},{npc.Y:F0}");
                world.NotifyNpcKilled(npc.Type, npc.X, npc.Y); // Boss 击杀 → 世界进度与掉落 // Boss 击杀 → 世界进度 + 掉落
            }
            else
            {
                Console.WriteLine($"[Strike] slot={NpcIndex} gen={npc.Generation} dmg={Damage} def={npc.Defense} applied={applied} → life={npc.Life}");
            }
        }

        return new(true);
    }

    /// <summary>
    /// 查找归属该玩家、距离 NPC 最近的一枚存活弹幕（阶段 C 伤害匹配的数值来源）。
    /// 原版客户端只结算「自己发射」的弹幕（<c>Projectile.Damage</c> 断言 owner == myPlayer），
    /// 故匹配基准必须是 <c>Owner == playerId</c>；未找到（近战挥砍 / 弹幕已销毁）返回 null。
    /// </summary>
    private static ProjectileEntity? FindPlayerProjectileNearNpc(WorldState world, int playerId, WorldNpc npc)
    {
        ProjectileEntity? best = null;
        float bestDistSq = float.MaxValue;

        lock (world.ProjectilesLock)
        {
            foreach (var p in world.Projectiles)
            {
                if (!p.Active || p.Owner != playerId || p.Damage <= 0) continue;

                float dx = p.Position.X - npc.X;
                float dy = p.Position.Y - npc.Y;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = p;
                }
            }
        }

        return best;
    }
}

/// <summary>掉落物生成命令：包 21 权威通过后生成；槽位由服务端分配（不采信客户端上报值）。</summary>
public sealed record SpawnItemCommand(
    long Tick, int? PlayerId, int ItemId, int Stack, Vector2 Position, Vector2 Velocity, byte Prefix)
    : Command(Tick, PlayerId, "spawn_item")
{
    /// <summary>世界掉落物槽位上限（与原版 <c>Main.item[400]</c> 一致）。</summary>
    private const int MaxSlots = 400;

    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is int playerId && !TryGetPlayer(world, playerId, out _, out var failure))
            return failure;

        // 可配置「移除召唤武器即销毁」：玩家丢弃（包 21 上行）召唤武器时立即销毁其召唤弹幕。
        // 客户端丢弃物品只发包 21 创建掉落物、不必然发包 5 清槽，故在此路径补充销毁检测。
        if (world.DestroySummonsOnWeaponRemoval && PlayerId is int pid && SummonWeaponLookup.IsSummonWeapon(ItemId))
            world.KillSummonedProjectiles(pid);

        lock (world.ItemsLock)
        {
            if (world.Items.Count >= MaxSlots)
                return new(false, CommandFailures.NotApplied);

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
                OwnedBy = -1,              // 丢弃物品无归属（原版语义：谁都能拾取）
                DroppedBy = PlayerId ?? -1,  // 丢弃者：FindOwner 延迟期间跳过 + 包 22 下发拾取延迟
                GrabDelayExpireTick = PlayerId >= 0
                    ? world.Tick + WorldItemEntity.DefaultGrabDelay  // 原版 100 tick ≈ 1.67s
                    : 0,
                NewNotified = false,       // 由服务器统一广播（客户端丢弃 index=400，本地未持有，须服务端下发）
            });
        }

        return new(true);
    }
}

/// <summary>弹幕生成 / 更新命令：包 27 权威通过后生成，服务端登记并推进其生命周期。</summary>
public sealed record SpawnProjectileCommand(
    long Tick, int? PlayerId, int Key, int Type, Vector2 Position, Vector2 Velocity, int Damage)
    : Command(Tick, PlayerId, "spawn_projectile")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (!TryGetPlayer(world, playerId, out var player, out var failure))
            return failure;

        lock (world.ProjectilesLock)
        {
            // 同一 Key 视为同一弹幕的更新（原版 projectile 索引由归属者选定）
            var existing = world.Projectiles.FirstOrDefault(p => p.Key == Key);
            if (existing is not null)
            {
                // 服务端已永久销毁（如「移除召唤武器即销毁」）：忽略后续更新，拒绝复活。
                if (existing.Destroyed)
                    return new(true);

                existing.Position = Position;
                existing.Velocity = Velocity;
                existing.Active = true;
                existing.RemovalNotified = false;
                return new(true);
            }

            // 阶段 H：召唤 / 哨兵弹幕 spawn 伤害权威校验——堵住「虚报弹幕伤害 → 命中上界随之上抬」漏洞。
            // 原版仆从伤害 = **召唤时**武器伤害（GetWeaponDamage 含前缀 / Buff / 饰品 / 套装），创建时一次确定，
            // 之后不随背包 / 手持变化；阶段 G 依此用弹幕 Damage 计算命中上界（ceil(p.Damage × 1.15) × crit）。
            // 规则：
            //   · 手持已收录**召唤武器**：弹幕 Damage 必须 ≤ ceil(权威武器伤害 × 1.15)（容差对齐命中浮动），超限拒绝；
            //   · 空手 / 手持明确非召唤武器（近战 / 远程 / 魔法）：拒绝（原版只有召唤武器能 spawn 召唤弹幕，
            //     空手 spawn 只存在于客户端本地，登记会造成幽灵弹幕）；
            //   · 手持未收录武器（mod 等）：放行（绝不误拒未知物品）。
            if (SummonProjectileTable.Of.Contains(Type))
            {
                bool heldHasItem = false, heldIsKnown = false, heldIsSummon = false;
                int weaponDamage = 0;
                if (player!.SelectedSlot >= 0 && player.SelectedSlot < PlayerRuntime.InventorySlotCount)
                {
                    int heldItem = player.Items[player.SelectedSlot];
                    if (heldItem > 0)
                    {
                        heldHasItem = true;
                        if (ItemDamageTable.Of.TryGetValue(heldItem, out var heldStats))
                        {
                            heldIsKnown = true;
                            if (heldStats.Class == WeaponClass.Summon)
                            {
                                heldIsSummon = true;
                                weaponDamage = CombatResolver.GetWeaponDamage(player, heldItem,
                                    player.ItemPrefixes[player.SelectedSlot]);
                            }
                        }
                    }
                }

                if (heldIsSummon)
                {
                    if (Damage > (int)Math.Ceiling(weaponDamage * 1.15f))
                        return new(false, CommandFailures.ProjectileDamageAboveBound);
                }
                else if (!heldHasItem || heldIsKnown)
                {
                    return new(false, CommandFailures.SummonRequiresSummonWeapon);
                }
                // 未收录武器（heldHasItem && !heldIsKnown）→ 放行。
            }

            // 碰撞盒按原版逐类型尺寸（Sizes），未登记类型沿用 16×16 近似。
            var size = ProjectileCapabilityTable.Sizes.TryGetValue(Type, out var s)
                ? s : (Width: 16f, Height: 16f);
            world.Projectiles.Add(new ProjectileEntity
            {
                Key = Key,
                Owner = PlayerId ?? -1,
                Type = Type,
                Position = Position,
                Velocity = Velocity,
                Damage = Damage,
                Width = size.Width,
                Height = size.Height,
                NewNotified = false,   // 由世界同步循环推送给其他玩家（包 27）
            });
        }

        return new(true);
    }
}

/// <summary>弹幕销毁命令：包 29 权威通过后生成（仅归属者可销毁）。</summary>
public sealed record KillProjectileCommand(long Tick, int? PlayerId, int Key, Vector2 Position)
    : Command(Tick, PlayerId, "kill_projectile")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        lock (world.ProjectilesLock)
        {
            foreach (var p in world.Projectiles)
            {
                if (p.Key != Key || !p.Active) continue;

                // 服务端权威：只有归属者能销毁自己的弹幕（防伪造他人弹幕消失）
                if (PlayerId is int owner && p.Owner != owner) return new(false, CommandFailures.NotOwner);

                p.Position = Position;
                p.Active = false;
                p.DeadTick = world.Tick;   // 用仿真 tick（命令的 Tick 可能落后于当前世界 tick）
                p.RemovalNotified = true; // 客户端已发起销毁，无需服务端再补发
                return new(true);
            }
        }

        return new(false, CommandFailures.ProjectileNotFound);
    }
}

/// <summary>法力更新命令：包 42 权威通过后生成（服务端跟踪法力；原版不对他人转发）。</summary>
public sealed record SetManaCommand(long Tick, int? PlayerId, int Mana, int MaxMana)
    : Command(Tick, PlayerId, "set_mana")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id || MaxMana <= 0)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;

        player!.MpMax = MaxMana;
        player.Mp = Math.Clamp(Mana, 0, MaxMana);
        return new(true);
    }
}

/// <summary>
/// 治疗命令：包 35 权威通过后生成。服务端为唯一真相源——回血上限钳制到服务端 HpMax，
/// 客户端上报的超额治疗不会让服务端生命越界。
/// </summary>
public sealed record HealPlayerCommand(long Tick, int? PlayerId, int Amount)
    : Command(Tick, PlayerId, "heal_player")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id || Amount <= 0)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;
        if (player!.Dead)
            return new(false, CommandFailures.NotApplied);

        player.Hp = Math.Min(player.Hp + Amount, player.HpMax);
        return new(true);
    }
}

/// <summary>增益列表命令：包 50 权威通过后生成（服务端持有增益列表唯一真相）。</summary>
public sealed record SetBuffsCommand(long Tick, int? PlayerId, IReadOnlyList<int> Buffs)
    : Command(Tick, PlayerId, "set_buffs")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;

        player!.Buffs.Clear();
        player.Buffs.AddRange(Buffs);
        player.RecalculateDefense(); // Buff 防御（铁皮/吃饱…）实时并入 statDefense，驱动 117 上界
        world.MarkPlayerChanged(id);
        return new(true);
    }
}

/// <summary>
/// 客户端上报「命中给 NPC 施加单条减益」（包 53 AddNPCBuff）。
/// 服务端权威并入该 NPC 的增益列表，并触发包 54 全量下发回写；时长 ≤0 视为移除该减益。
/// 原版 NPC.buff 槽上限 5：超限按「替换首个迟到减益」的懒策略合并，避免客户端入队溢出。
/// </summary>
public sealed record ApplyNpcBuffCommand(long Tick, int? PlayerId, int NpcId, int BuffType, int Time)
    : Command(Tick, PlayerId, "apply_npc_buff")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        lock (world.NpcsLock)
        {
            if (NpcId < 0 || NpcId >= world.Npcs.Count)
                return new(false, CommandFailures.NotApplied);

            var npc = world.Npcs[NpcId];
            if (!npc.Active)
                return new(false, CommandFailures.NotApplied);

            if (Time <= 0)
            {
                npc.Buffs.Remove(BuffType);
            }
            else
            {
                if (npc.Buffs.Count >= 5 && !npc.Buffs.ContainsKey(BuffType))
                {
                    using (var e = npc.Buffs.Keys.GetEnumerator())
                    {
                        e.MoveNext();
                        npc.Buffs.Remove(e.Current); // 懒替换首个（近似原版扫空槽取负值）
                    }
                }
                npc.Buffs[BuffType] = Time;
            }

            world.MarkNpcBuffsChanged(NpcId);
        }
        return new(true);
    }
}

/// <summary>命令队列：按 (tick, sequence) 线程安全、稳定排序。</summary>
public sealed class CommandQueue
{
    private readonly object _gate = new();
    private readonly PriorityQueue<QueuedCommand, (long Tick, long Sequence)> _queue = new();

    /// <summary>
    /// 队列容量上限；0 表示无界。用于阻断「恶意构造超大未来 tick 命令」导致的无限堆积
    /// （<see cref="DrainThrough"/> 只消费当期命令，未来命令会滞留）。默认无界以保持兼容。
    /// </summary>
    public int MaxCount { get; }

    public CommandQueue(int maxCount = 0) => MaxCount = maxCount;

    private long _nextSequence;

    public int Count
    {
        get { lock (_gate) return _queue.Count; }
    }

    public bool IsEmpty
    {
        get { lock (_gate) return _queue.Count == 0; }
    }

    /// <summary>
    /// 入队命令。超出 <see cref="MaxCount"/>（且非 0）时**拒绝入队并返回 false**，
    /// 由调用方决定回退策略（通常拒绝该客户端操作）；队列不无限增长。无界（MaxCount==0）时恒为 true。
    /// </summary>
    public bool Enqueue(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            if (MaxCount > 0 && _queue.Count >= MaxCount)
                return false;

            var sequence = _nextSequence++;
            _queue.Enqueue(new QueuedCommand(command, sequence), (command.Tick, sequence));
            return true;
        }
    }

    public bool TryPeek(out Command? command)
    {
        lock (_gate)
        {
            if (_queue.TryPeek(out var queued, out _))
            {
                command = queued.Command;
                return true;
            }
        }

        command = null;
        return false;
    }

    public Command Dequeue()
    {
        lock (_gate)
        {
            if (_queue.TryDequeue(out var queued, out _))
                return queued.Command;
        }

        throw new InvalidOperationException("Queue empty");
    }

    /// <summary>取出不晚于指定 tick 的所有命令，按 (tick, sequence) 排序。</summary>
    public IReadOnlyList<Command> DrainThrough(long tick)
    {
        var result = new List<Command>();
        lock (_gate)
        {
            while (_queue.TryPeek(out var queued, out var priority) && priority.Tick <= tick)
            {
                _queue.Dequeue();
                result.Add(queued.Command);
            }
        }

        return result;
    }

    private sealed record QueuedCommand(Command Command, long Sequence);
}
