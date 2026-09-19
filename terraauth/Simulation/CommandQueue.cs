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

    /// <summary>
    /// 重取玩家合成环境快照（可达区域图格 + 相邻液体）。
    /// **必须在取 <see cref="WorldState.PlayersLock"/> 之前调用**：内部要取区块读锁，
    /// 而全局锁序是 SectionLocks → PlayersLock → ChestsLock / ItemsLock …
    /// 玩家不存在时静默跳过（后续命令自身会失败）。
    /// </summary>
    protected static void RefreshCraftingEnvironment(WorldState world, int playerId)
    {
        PlayerRuntime? player;
        lock (world.PlayersLock)
            world.Players.TryGetValue(playerId, out player);
        if (player is not null) CraftingEnvironmentSampler.Refresh(world, player);
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
        bool wasPressingUseItem = player.PressingUseItem;
        int previousUseSlot = player.UseItemSelectedSlot;
        player.ControlBits = ControlBits;
        if (SelectedItem < PlayerRuntime.InventorySlotCount)
            player.SelectedSlot = SelectedItem;
        player.HasReceivedPlayerControls = true;
        if (!player.PressingUseItem)
        {
            player.UseItemSelectedSlot = -1;
            player.UseAnimationTicksRemaining = 0;
        }
        else if (!wasPressingUseItem || player.UseItemSelectedSlot < 0 || previousUseSlot != player.SelectedSlot)
        {
            player.UseItemSelectedSlot = player.SelectedSlot;
            player.UseAnimationTicksRemaining = WeaponUseBehaviorTable.For(
                player.Items[player.SelectedSlot]).UseAnimation;
        }
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
            if (DiagnosticLog.Enabled)
                Console.WriteLine($"[Hurt] 玩家 #{id} 上报伤害 {Damage}（包117）但免伤帧中，忽略");
            return new(false, CommandFailures.NotApplied);
        }

        // 前置：此刻必须确实接触着敌怪（防伪造远程受伤；判定口径与服务端接触兜底一致）
        if (!CombatResolver.IsPlayerInContact(world, player))
        {
            if (DiagnosticLog.Enabled)
                Console.WriteLine($"[Hurt] 玩家 #{id} 上报伤害 {Damage}（包117）但未接触敌怪，忽略");
            return new(false, CommandFailures.NotApplied);
        }

        // 伤害上界：接触者最大基础伤害 × ±15% 浮动上界 → 减玩家防御（原版 CalculateDamagePlayersTake，按难度取分支）
        int contact = CombatResolver.FindContactDamage(world, player, out _, out _);
        int upper = CombatResolver.CalculateDamagePlayersTake(
            (int)Math.Ceiling(contact * 1.15f), player.Defense, CombatResolver.FromWorldDifficulty(world.GameMode));

        if (Damage > upper)
        {
            if (DiagnosticLog.Enabled)
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
        player.ItemStacks[Slot] = Stack > 0 ? Stack : 0;
        player.ItemPrefixes[Slot] = Stack > 0 ? Prefix : (byte)0;
        player.RecalculateDefense();
        return new(true);
    }
}

/// <summary>SSC 眼魔宝袋开袋命令：奖励由服务端随机并原子写入权威背包。</summary>
public sealed record OpenEyeOfCthulhuTreasureBagCommand(long Tick, int? PlayerId, int Slot)
    : Command(Tick, PlayerId, "open_eye_of_cthulhu_treasure_bag")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (world.InventoryLedger is null)
            return new(false, CommandFailures.InventoryUnavailable);

        bool crimson;
        lock (world.PlayersLock)
        {
            if (!world.Players.TryGetValue(playerId, out var player) ||
                (SessionId != 0 && player.SessionId != SessionId) || !player.Active || player.Dead)
            {
                return new(false, CommandFailures.PlayerNotActive);
            }
            crimson = world.Progress.Crimson;
        }

        var rewards = new List<InventoryReward>
        {
            crimson
                ? new InventoryReward(880, 30 + rng.NextInt32(61))
                : new InventoryReward(56, 30 + rng.NextInt32(61)),
        };
        if (crimson)
            rewards.Add(new InventoryReward(2171, 1 + rng.NextInt32(3)));
        else
        {
            rewards.Add(new InventoryReward(47, 20 + rng.NextInt32(31)));
            rewards.Add(new InventoryReward(59, 1 + rng.NextInt32(3)));
        }
        if (rng.NextInt32(7) == 0) rewards.Add(new InventoryReward(2112, 1));
        if (rng.NextInt32(40) == 0) rewards.Add(new InventoryReward(1299, 1));

        var result = world.InventoryLedger.TryOpenEyeOfCthulhuTreasureBag(playerId, Slot, rewards);
        // 奖励由服务端原子生成；校验失败（袋已被消费 / 背包满）不产出任何物品。
        // 客户端随后上报的奖励快照由权威层回正（Correct），无需 pending 屏障。
        return result switch
        {
            InventoryBagOpenResult.Success => new(true),
            InventoryBagOpenResult.InventoryFull => new(false, CommandFailures.InventoryFull),
            _ => new(false, CommandFailures.NotApplied),
        };
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

                    // 聊天框提示（「获取 Wood ×1」）：SSC 下拾取是服务端行为，客户端不会弹原生拾取提示，
                    // 故由服务端经包 82 给该玩家一条提示（世界同步循环统一取走下发）。
                    world.NotifyPlayer(playerId,
                        $"获取 {ItemDisplayNameTable.NameOf(item.ItemId)} ×{item.Stack}");
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
/// 箱子物品暂存命令（SSC 箱子守恒事务）：包 32 通过权威校验后不立即写入箱子，
/// 而是把客户端意图暂存进该玩家的箱子事务窗口；窗口到期后按「玩家背包 ∪ 该箱子」守恒校验
/// 决定整体提交或回滚（见 <see cref="WorldState.TryCommitChestTransaction"/>）。
/// 原版拖拽会在一个窗口内发出多个包（源槽清空 + 目标槽填入），逐包写入无法区分
/// 「合法的箱内整理 / 背包↔箱子移动」与「凭空造物」，故必须窗口聚合后再判。
/// </summary>
public sealed record StageChestItemCommand(
    long Tick, int? PlayerId, int ChestIndex, int Slot, int Stack, byte Prefix, int ItemType)
    : Command(Tick, PlayerId, "stage_chest_item")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);

        // 合成环境快照：先于 PlayersLock（锁序 SectionLocks → PlayersLock → ChestsLock）。
        RefreshCraftingEnvironment(world, playerId);

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

                // 会话切换 / 切换到别的箱子 / 上一窗口已结算 → 重开事务并重取守恒基准
                world.EnsureChestTransaction(player, ChestIndex, world.Tick);

                player.PendingChestChanges[(ChestIndex, Slot)] = (ItemType, Stack, Prefix);
                world.MarkChestTransactionOpen(playerId);
            }
        }

        return new(true);
    }
}

/// <summary>背包与当前打开箱子间的服务端权威原子物品转移。</summary>
public sealed record TransferInventoryChestItemCommand(
    long Tick, int? PlayerId, bool FromChest, int ChestIndex, int InventorySlot, int ChestSlot,
    int Amount, long OperationId, int ExpectedSourceItemId, byte ExpectedSourcePrefix, int ExpectedSourceStack)
    : Command(Tick, PlayerId, "transfer_inventory_chest_item")
{
    private const int MaxStackSize = 999;

    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId) return new(false, CommandFailures.MissingPlayer);
        if (OperationId == 0 || Amount <= 0) return new(false, CommandFailures.NotApplied);
        long appliedSessionId = 0;

        lock (world.PlayersLock)
        {
            if (!world.Players.TryGetValue(playerId, out var player) || !player.Active || player.Dead)
                return new(false, CommandFailures.PlayerNotActive);
            if (SessionId != 0 && player.SessionId != SessionId)
                return new(false, CommandFailures.StaleSession);

            long sessionId = SessionId != 0 ? SessionId : player.SessionId;
            lock (world.ChestsLock)
            {
                if (!world.HasChestSession(playerId, sessionId, ChestIndex))
                    return new(false, CommandFailures.ChestNotOpen);
                if (world.HasAppliedInventoryChestOperation(playerId, sessionId, OperationId))
                    return new(false, "replayed_operation");

                var chest = world.FindChestByIndex(ChestIndex);
                if (chest is null) return new(false, CommandFailures.ChestNotFound);
                if (InventorySlot < 0 || InventorySlot >= PlayerRuntime.InventorySlotCount ||
                    ChestSlot < 0 || ChestSlot >= chest.Items.Length)
                    return new(false, CommandFailures.InvalidSlot);

                float dx = player.Position.X - chest.X * 16f - 8f;
                float dy = player.Position.Y - chest.Y * 16f - 8f;
                if (dx * dx + dy * dy > 160f * 160f) return new(false, CommandFailures.OutOfReach);

                int sourceId = FromChest ? chest.Items[ChestSlot].Type : player.Items[InventorySlot];
                int sourceStack = FromChest ? chest.Items[ChestSlot].Stack : player.ItemStacks[InventorySlot];
                byte sourcePrefix = FromChest ? chest.Items[ChestSlot].Prefix : player.ItemPrefixes[InventorySlot];
                int targetId = FromChest ? player.Items[InventorySlot] : chest.Items[ChestSlot].Type;
                int targetStack = FromChest ? player.ItemStacks[InventorySlot] : chest.Items[ChestSlot].Stack;
                byte targetPrefix = FromChest ? player.ItemPrefixes[InventorySlot] : chest.Items[ChestSlot].Prefix;

                if (sourceId != ExpectedSourceItemId || sourcePrefix != ExpectedSourcePrefix || sourceStack != ExpectedSourceStack ||
                    sourceId == 0 || sourceStack <= 0 || Amount > sourceStack ||
                    (targetStack == 0 ? targetId != 0 || targetPrefix != 0 : targetId == 0) ||
                    (targetStack > 0 && (targetId != sourceId || targetPrefix != sourcePrefix)) ||
                    targetStack + Amount > MaxStackSize)
                    return new(false, CommandFailures.NoChange);

                int remaining = sourceStack - Amount;
                int combined = targetStack + Amount;
                if (FromChest)
                {
                    chest.Items[ChestSlot] = remaining == 0 ? new ChestItem() : new ChestItem { Type = sourceId, Stack = (short)remaining, Prefix = sourcePrefix };
                    player.Items[InventorySlot] = sourceId;
                    player.ItemStacks[InventorySlot] = combined;
                    player.ItemPrefixes[InventorySlot] = sourcePrefix;
                }
                else
                {
                    player.Items[InventorySlot] = remaining == 0 ? 0 : sourceId;
                    player.ItemStacks[InventorySlot] = remaining;
                    player.ItemPrefixes[InventorySlot] = remaining == 0 ? (byte)0 : sourcePrefix;
                    chest.Items[ChestSlot] = new ChestItem { Type = sourceId, Stack = (short)combined, Prefix = sourcePrefix };
                }

                player.RecalculateDefense();
                appliedSessionId = sessionId;
                world.MarkInventoryChestOperationApplied(playerId, sessionId, OperationId);
            }
        }

        world.MarkPersistChest(ChestIndex);
        world.MarkChestChanged(ChestIndex, ChestSlot);
        world.MarkInventoryChanged(playerId, appliedSessionId, InventorySlot);
        return new(true);
    }
}

public enum ChestBulkOperation
{
    LootAll,
    DepositAll,
    QuickStack,
}

/// <summary>箱子批量物品操作：服务端规划后一次性提交，客户端不能提交最终快照。</summary>
public sealed record BulkInventoryChestCommand(
    long Tick, int? PlayerId, int ChestIndex, ChestBulkOperation Operation, long OperationId)
    : Command(Tick, PlayerId, "bulk_inventory_chest")
{
    private const int MaxStackSize = 999;
    private const int InventoryStart = 9;
    private const int InventoryEnd = 49;

    /// <summary>
    /// 可选：仅处理这些背包来源槽位（包 85 QuickStackChests 指定的槽位列表）。
    /// null 或空 → 走全量逻辑（遍历 <see cref="InventoryStart"/>..<see cref="InventoryEnd"/>）。
    /// </summary>
    public IReadOnlyList<int>? SourceSlots { get; init; }

    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId) return new(false, CommandFailures.MissingPlayer);
        if (OperationId == 0) return new(false, CommandFailures.NotApplied);
        long appliedSessionId;
        var changedInventory = new HashSet<int>();
        var changedChest = new HashSet<int>();

        lock (world.PlayersLock)
        {
            if (!world.Players.TryGetValue(playerId, out var player) || !player.Active || player.Dead)
                return new(false, CommandFailures.PlayerNotActive);
            if (SessionId != 0 && player.SessionId != SessionId)
                return new(false, CommandFailures.StaleSession);

            appliedSessionId = SessionId != 0 ? SessionId : player.SessionId;
            lock (world.ChestsLock)
            {
                if (!world.HasChestSession(playerId, appliedSessionId, ChestIndex))
                    return new(false, CommandFailures.ChestNotOpen);
                if (world.HasAppliedInventoryChestOperation(playerId, appliedSessionId, OperationId))
                    return new(false, "replayed_operation");

                var chest = world.FindChestByIndex(ChestIndex);
                if (chest is null) return new(false, CommandFailures.ChestNotFound);

                float dx = player.Position.X - chest.X * 16f - 8f;
                float dy = player.Position.Y - chest.Y * 16f - 8f;
                if (dx * dx + dy * dy > 160f * 160f)
                    return new(false, CommandFailures.OutOfReach);

                var inventory = new ChestItem[PlayerRuntime.InventorySlotCount];
                for (int slot = 0; slot < inventory.Length; slot++)
                    inventory[slot] = new ChestItem
                    {
                        Type = player.Items[slot],
                        Stack = (short)Math.Clamp(player.ItemStacks[slot], 0, MaxStackSize),
                        Prefix = player.ItemPrefixes[slot],
                    };
                var chestItems = (ChestItem[])chest.Items.Clone();

                if (Operation is ChestBulkOperation.LootAll)
                {
                    for (int chestSlot = 0; chestSlot < chestItems.Length; chestSlot++)
                        MoveStack(chestItems, chestSlot, inventory, changedChest, changedInventory, false,
                            InventoryStart, InventoryEnd);
                }
                else
                {
                    bool existingOnly = Operation == ChestBulkOperation.QuickStack;
                    if (SourceSlots is { Count: > 0 })
                    {
                        // 包 85：仅处理客户端指定的来源槽位（越界槽位忽略）
                        foreach (int inventorySlot in SourceSlots)
                        {
                            if (inventorySlot < 0 || inventorySlot >= inventory.Length) continue;
                            MoveStack(inventory, inventorySlot, chestItems, changedInventory, changedChest,
                                existingOnly, 0, chestItems.Length - 1);
                        }
                    }
                    else
                    {
                        for (int inventorySlot = InventoryStart; inventorySlot <= InventoryEnd; inventorySlot++)
                            MoveStack(inventory, inventorySlot, chestItems, changedInventory, changedChest, existingOnly,
                                0, chestItems.Length - 1);
                    }
                }

                if (changedInventory.Count == 0 && changedChest.Count == 0)
                    return new(false, CommandFailures.NoChange);

                for (int slot = 0; slot < inventory.Length; slot++)
                {
                    player.Items[slot] = inventory[slot].Type;
                    player.ItemStacks[slot] = inventory[slot].Stack;
                    player.ItemPrefixes[slot] = inventory[slot].Prefix;
                }
                chest.Items = chestItems;
                player.RecalculateDefense();
                world.MarkInventoryChestOperationApplied(playerId, appliedSessionId, OperationId);
            }
        }

        world.MarkPersistChest(ChestIndex);
        foreach (int slot in changedChest)
            world.MarkChestChanged(ChestIndex, slot);
        foreach (int slot in changedInventory)
            world.MarkInventoryChanged(playerId, appliedSessionId, slot);
        return new(true);
    }

    private static void MoveStack(
        ChestItem[] source, int sourceSlot, ChestItem[] target,
        HashSet<int> changedSource, HashSet<int> changedTarget, bool existingOnly,
        int targetStart, int targetEnd)
    {
        var item = source[sourceSlot];
        if (item.Type == 0 || item.Stack <= 0) return;

        int remaining = item.Stack;
        for (int targetSlot = targetStart; targetSlot <= targetEnd && targetSlot < target.Length && remaining > 0; targetSlot++)
        {
            var destination = target[targetSlot];
            if (destination.Type != item.Type || destination.Prefix != item.Prefix ||
                destination.Stack >= MaxStackSize)
                continue;

            int amount = Math.Min(remaining, MaxStackSize - destination.Stack);
            target[targetSlot] = new ChestItem
            {
                Type = item.Type,
                Stack = (short)(destination.Stack + amount),
                Prefix = item.Prefix,
            };
            remaining -= amount;
            changedTarget.Add(targetSlot);
        }

        if (!existingOnly && remaining > 0)
        {
            for (int targetSlot = targetStart; targetSlot <= targetEnd && targetSlot < target.Length && remaining > 0; targetSlot++)
            {
                if (target[targetSlot].Type != 0 || target[targetSlot].Stack != 0 || target[targetSlot].Prefix != 0)
                    continue;

                int amount = Math.Min(remaining, MaxStackSize);
                target[targetSlot] = new ChestItem
                {
                    Type = item.Type,
                    Stack = (short)amount,
                    Prefix = item.Prefix,
                };
                remaining -= amount;
                changedTarget.Add(targetSlot);
            }
        }

        if (remaining != item.Stack)
        {
            source[sourceSlot] = remaining == 0
                ? new ChestItem()
                : new ChestItem { Type = item.Type, Stack = (short)remaining, Prefix = item.Prefix };
            changedSource.Add(sourceSlot);
        }
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

        if (Action == 0 && TileType == 0 && TryBreakDisplayTileEntityObject(world, rng, out var displayBroken))
            return displayBroken ? new(true) : new(false, CommandFailures.NoChange);
        if (Action == 0 && TileType == 0 && TryBreakTileEntityObject(world, rng, out var tileEntityBroken))
            return tileEntityBroken ? new(true) : new(false, CommandFailures.NoChange);
        if (Action == 0 && TileType == 0 && TryBreakContainerObject(world, rng, out var containerBroken))
            return containerBroken ? new(true) : new(false, CommandFailures.NoChange);
        if (Action == 0 && TileType == 0 && TryBreakStatelessMultiTileObject(world, rng, out var objectBroken))
            return objectBroken ? new(true) : new(false, CommandFailures.NoChange);

        // 区块分区锁：与包 10 编码 / 权威校验的跨线程读互斥（详见 SectionLocks）
        bool changed = false;
        // 挖掉的图格（用于掉落查表；泥土类型就是 0，故用独立标志而非「类型 > 0」）
        bool killedTile = false;
        ushort killedType = 0;
        short killedFrameX = 0;
        short killedFrameY = 0;
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
                case 0:  // KillTile（掉落地图格对应的物品）
                    // 包 17 第 5 字段在「挖」时不是图格类型，而是 fail 标志（1 = 仅命中特效，尚未挖穿）。
                    // 原版此时调 KillTile(x, y, fail: true)：只播击打效果、**不改动世界**（详见 ValidateBreak）。
                    if (TileType != 0) break;
                    killedType = before.Type;
                    killedFrameX = before.FrameX;
                    killedFrameY = before.FrameY;
                    killedTile = true;
                    tile.Active = false;
                    tile.Type = 0;
                    tile.Wall = 0;
                    break;

                case 4:  // KillTileNoItem：原版语义即「挖掉但**不掉落**」（如雕像破坏 / 系统清理）
                    if (TileType != 0) break;
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

        // 掉落物（原版 WorldGen.KillTile_DropItems → Item.NewItem）：
        // 放在**区块锁之外**生成，避免与 ItemsLock 形成新的锁序（拾取路径是 PlayersLock → ItemsLock）。
        if (killedTile && TileDropTable.IsTreeTile(killedType))
        {
            int heldItem = 0;
            lock (world.PlayersLock)
            {
                if (world.Players.TryGetValue(playerId, out var player)
                    && player.SelectedSlot >= 0 && player.SelectedSlot < PlayerRuntime.InventorySlotCount)
                {
                    heldItem = player.Items[player.SelectedSlot];
                }
            }

            // 树：砍掉任意一格即**整棵倒下**（原版一棵树由多格树干组成，逐格产木材），
            // 故木材数量 = 本次清掉的树干格数（含刚挖掉的这一格），整棵树只掷一次额外木材。
            int extra = world.FellTreeAt(X, Y, killedType);
            int woodStack = 1 + extra;
            int axePower = AxePowerTable.AxePowerOf(heldItem);
            if (rng.NextInt32(35) <= axePower || rng.NextInt32(3) == 0)
                woodStack++;
            world.SpawnItemDrop(TileDropTable.Wood, woodStack, X, Y, rng);
        }
        else if (killedTile && TileDropTable.TryGetMatureHerbDrop(killedType, killedFrameX,
                     out int herbItem, out int seedItem, out bool flowering))
        {
            world.SpawnItemDrop(herbItem, 1, X, Y, rng);
            if (flowering)
                world.SpawnItemDrop(seedItem, 1 + rng.NextInt32(3), X, Y, rng);
        }
        else if (killedTile && TileDropTable.TryGet(killedType, killedFrameX, killedFrameY,
                     out int dropItem, out int dropStack))
        {
            world.SpawnItemDrop(dropItem, dropStack, X, Y, rng);
        }

        return changed ? new(true) : new(false, CommandFailures.NoChange);
    }

    private readonly record struct DisplayTileEntityObject(int Width, int Height, byte EntityType, int ItemId, bool ReturnsItem);

    private static readonly Dictionary<ushort, DisplayTileEntityObject> DisplayTileEntityObjects = new()
    {
        [395] = new(2, 2, 1, 3270, true),
        [471] = new(3, 3, 4, 2699, true),
        [520] = new(1, 1, 6, 4326, true),
        [698] = new(1, 2, 8, 5472, true),
        [470] = new(2, 3, 3, 498, false),
        [475] = new(3, 4, 5, 3977, false),
    };

    private readonly record struct StatelessMultiTileObject(int Width, int Height, int ItemId);

    private static readonly Dictionary<ushort, StatelessMultiTileObject> StatelessMultiTileObjects = new()
    {
        [406] = new(3, 3, 3365), [412] = new(3, 3, 3549), [452] = new(3, 3, 3742),
        [455] = new(3, 3, 3747), [491] = new(3, 3, 4076), [499] = new(3, 3, 4142),
        [642] = new(3, 3, 5296), [733] = new(3, 3, 5113), [753] = new(3, 3, 6147),
        [102] = new(3, 4, 355), [463] = new(3, 4, 3813),
        [106] = new(3, 2, 363), [212] = new(3, 2, 951), [219] = new(3, 2, 997),
        [220] = new(3, 2, 998), [228] = new(3, 2, 1120), [243] = new(3, 2, 1430),
        [247] = new(3, 2, 1551), [283] = new(3, 2, 2172), [300] = new(3, 2, 2192),
        [301] = new(3, 2, 2193), [302] = new(3, 2, 2194), [303] = new(3, 2, 2195),
        [304] = new(3, 2, 2196), [305] = new(3, 2, 2197), [306] = new(3, 2, 2198),
        [307] = new(3, 2, 2203), [308] = new(3, 2, 2204), [354] = new(3, 2, 2999),
        [355] = new(3, 2, 3000), [114] = new(3, 2, 398), [217] = new(3, 2, 995),
        [218] = new(3, 2, 996), [377] = new(3, 2, 3198), [405] = new(3, 2, 3364),
        [486] = new(3, 2, 4063), [704] = new(3, 2, 501), [706] = new(3, 2, 4144),
    };

    private bool TryBreakDisplayTileEntityObject(WorldState world, IRng rng, out bool broken)
    {
        broken = false;
        Tile hit;
        using (world.Sections.EnterRead(X, Y, X, Y))
            hit = world.Tiles[X, Y];
        if (!hit.Active || !DisplayTileEntityObjects.TryGetValue(hit.Type, out var descriptor))
            return false;
        if (hit.FrameX < 0 || hit.FrameY < 0)
            return true;

        int localX = hit.FrameX / 18 % descriptor.Width;
        int localY = hit.FrameY / 18 % descriptor.Height;
        int anchorX = X - localX;
        int anchorY = Y - localY;
        if (anchorX < 0 || anchorY < 0 || anchorX + descriptor.Width > world.MaxTilesX ||
            anchorY + descriptor.Height > world.MaxTilesY)
            return true;

        int styleX = hit.FrameX / (descriptor.Width * 18);
        int styleY = hit.FrameY / (descriptor.Height * 18);
        var payloadDrops = new List<TileEntityItem>();
        bool payloadReturned = false;
        bool removeEntity = false;
        int shellItem = 0;
        TileEntity? entity;
        lock (world.TileEntitiesLock)
        {
            if (!world.TryGetTileEntityAt((short)anchorX, (short)anchorY, out entity) || entity!.Type != descriptor.EntityType)
                return true;

            using (world.Sections.EnterWrite(anchorX, anchorY, anchorX + descriptor.Width - 1,
                       anchorY + descriptor.Height - 1))
            {
                for (int x = 0; x < descriptor.Width; x++)
                for (int y = 0; y < descriptor.Height; y++)
                {
                    ref var tile = ref world.Tiles[anchorX + x, anchorY + y];
                    if (!tile.Active || tile.Type != hit.Type || tile.FrameX % 18 != 0 || tile.FrameY % 18 != 0 ||
                        tile.FrameX / (descriptor.Width * 18) != styleX ||
                        tile.FrameY / (descriptor.Height * 18) != styleY ||
                        tile.FrameX / 18 % descriptor.Width != x ||
                        tile.FrameY / 18 % descriptor.Height != y)
                        return true;
                }

                if (!TryTakeDisplayPayload(entity, descriptor.ReturnsItem, payloadDrops))
                    return true;
                if (payloadDrops.Count > 0)
                {
                    world.MarkTileEntityDirty(entity.Id);
                    payloadReturned = true;
                }
                else
                {
                    if (!TryGetDisplayTileEntityObjectDrop(hit.Type, hit.FrameX - localX * 18, out shellItem))
                        return true;

                    for (int x = 0; x < descriptor.Width; x++)
                    for (int y = 0; y < descriptor.Height; y++)
                    {
                        ref var tile = ref world.Tiles[anchorX + x, anchorY + y];
                        tile.Active = false;
                        tile.Type = 0;
                        tile.Wall = 0;
                    }
                    removeEntity = true;
                }
            }

            if (removeEntity)
                world.RemoveTileEntity(entity.Id);
        }

        foreach (var item in payloadDrops)
            world.SpawnItemDrop(item.Type, item.Stack, anchorX, anchorY, rng, item.Prefix);
        if (payloadReturned)
        {
            broken = true;
            return true;
        }

        for (int x = 0; x < descriptor.Width; x++)
        for (int y = 0; y < descriptor.Height; y++)
            world.MarkTileChanged(anchorX + x, anchorY + y);
        world.SpawnItemDrop(shellItem, 1, anchorX, anchorY, rng);
        broken = true;
        return true;
    }

    private static bool TryTakeDisplayPayload(TileEntity entity, bool returnsItem, List<TileEntityItem> drops)
    {
        if (returnsItem)
        {
            if (IsValidPayload(entity.Item))
            {
                drops.Add(entity.Item);
                entity.Item = default;
                return true;
            }
            return IsEmptyPayload(entity.Item);
        }

        TileEntityItem[] first;
        TileEntityItem[] second;
        if (entity.IsDisplayDoll)
        {
            first = entity.DisplayDollItems;
            second = entity.DisplayDollDyes;
            if (!IsValidOrEmpty(entity.DisplayDollMisc))
                return false;
        }
        else if (entity.IsHatRack)
        {
            first = entity.HatRackHats;
            second = entity.HatRackDyes;
        }
        else
        {
            return false;
        }

        if (!first.All(IsValidOrEmpty) || !second.All(IsValidOrEmpty))
            return false;

        TakePayloads(first, drops);
        TakePayloads(second, drops);
        if (entity.IsDisplayDoll && IsValidPayload(entity.DisplayDollMisc))
        {
            drops.Add(entity.DisplayDollMisc);
            entity.DisplayDollMisc = default;
        }
        return true;
    }

    private static void TakePayloads(TileEntityItem[] items, List<TileEntityItem> drops)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (IsValidPayload(items[i]))
                drops.Add(items[i]);
            items[i] = default;
        }
    }

    private static readonly Dictionary<ushort, TileEntityObject> TileEntityObjects = new()
    {
        [423] = new(1, 1, 2),
        [378] = new(2, 3, 0),
        [597] = new(3, 4, 7),
        [723] = new(1, 1, 9),
        [724] = new(1, 1, 10),
    };

    private readonly record struct TileEntityObject(int Width, int Height, byte EntityType);

    private bool TryBreakTileEntityObject(WorldState world, IRng rng, out bool broken)
    {
        broken = false;
        Tile hit;
        using (world.Sections.EnterRead(X, Y, X, Y))
            hit = world.Tiles[X, Y];
        if (!hit.Active || !TileEntityObjects.TryGetValue(hit.Type, out var descriptor))
            return false;
        if (hit.FrameX < 0 || hit.FrameY < 0 || hit.FrameX % 18 != 0 || hit.FrameY % 18 != 0)
            return true;

        int localX = hit.FrameX / 18 % descriptor.Width;
        int localY = hit.FrameY / 18 % descriptor.Height;
        int anchorX = X - localX;
        int anchorY = Y - localY;
        if (anchorX < 0 || anchorY < 0 || anchorX + descriptor.Width > world.MaxTilesX ||
            anchorY + descriptor.Height > world.MaxTilesY)
            return true;

        int styleX = hit.FrameX / (descriptor.Width * 18);
        int styleY = hit.FrameY / (descriptor.Height * 18);
        int shellItem = 0;
        int payloadItem = 0;
        lock (world.TileEntitiesLock)
        {
            if (!world.TryGetTileEntityAt((short)anchorX, (short)anchorY, out var entity) ||
                entity!.Type != descriptor.EntityType)
                return true;

            using (world.Sections.EnterWrite(anchorX, anchorY, anchorX + descriptor.Width - 1,
                       anchorY + descriptor.Height - 1))
            {
                for (int x = 0; x < descriptor.Width; x++)
                for (int y = 0; y < descriptor.Height; y++)
                {
                    ref var tile = ref world.Tiles[anchorX + x, anchorY + y];
                    if (!tile.Active || tile.Type != hit.Type || tile.FrameX % 18 != 0 || tile.FrameY % 18 != 0 ||
                        tile.FrameX / (descriptor.Width * 18) != styleX ||
                        tile.FrameY / (descriptor.Height * 18) != styleY ||
                        tile.FrameX / 18 % descriptor.Width != x ||
                        tile.FrameY / 18 % descriptor.Height != y)
                        return true;
                }

                if (hit.Type is 723 or 724)
                {
                    // Vanilla anchor data persists only an item type, so a present type returns one plain item.
                    payloadItem = entity.Item.Type;
                }
                else if (!TryGetTileEntityObjectShellDrop(hit, out shellItem))
                {
                    return true;
                }

                for (int x = 0; x < descriptor.Width; x++)
                for (int y = 0; y < descriptor.Height; y++)
                {
                    ref var tile = ref world.Tiles[anchorX + x, anchorY + y];
                    tile.Active = false;
                    tile.Type = 0;
                    tile.Wall = 0;
                }
            }

            world.RemoveTileEntity(entity.Id);
        }

        for (int x = 0; x < descriptor.Width; x++)
        for (int y = 0; y < descriptor.Height; y++)
            world.MarkTileChanged(anchorX + x, anchorY + y);
        if (payloadItem > 0)
            world.SpawnItemDrop(payloadItem, 1, anchorX, anchorY, rng);
        if (shellItem > 0)
            world.SpawnItemDrop(shellItem, 1, anchorX, anchorY, rng);
        broken = true;
        return true;
    }

    private static bool TryGetTileEntityObjectShellDrop(Tile hit, out int itemId)
    {
        switch (hit.Type)
        {
            case 423:
                itemId = GetLogicSensorDrop(hit.FrameY / 18);
                return itemId > 0;
            case 378:
                itemId = 3202;
                return true;
            case 597:
                itemId = GetTeleportationPylonDrop(hit.FrameX / 54);
                return true;
            default:
                itemId = 0;
                return false;
        }
    }

    private static int GetLogicSensorDrop(int style) => style switch
    {
        0 => 3613,
        1 => 3614,
        2 => 3615,
        3 => 3726,
        4 => 3727,
        5 => 3728,
        6 => 3729,
        _ => 0,
    };

    private static int GetTeleportationPylonDrop(int style) => style switch
    {
        1 => 4875,
        2 => 4916,
        3 => 4917,
        4 => 4918,
        5 => 4919,
        6 => 4920,
        7 => 4921,
        8 => 4951,
        9 => 5652,
        10 => 5653,
        _ => 4876,
    };

    private static bool IsValidPayload(TileEntityItem item) => item.Type > 0 && item.Stack > 0;
    private static bool IsEmptyPayload(TileEntityItem item) => item.Type == 0 && item.Stack == 0;
    private static bool IsValidOrEmpty(TileEntityItem item) => IsValidPayload(item) || IsEmptyPayload(item);

    private static bool TryGetDisplayTileEntityObjectDrop(ushort tileType, int anchorFrameX, out int itemId)
    {
        switch (tileType)
        {
            case 395: itemId = 3270; return true;
            case 471: itemId = 2699; return true;
            case 520: itemId = 4326; return true;
            case 698: itemId = 5472; return true;
            case 470: itemId = anchorFrameX / 72 == 1 ? 1989 : 498; return true;
            case 475: itemId = 3977; return true;
            default: itemId = 0; return false;
        }
    }

    private bool TryBreakStatelessMultiTileObject(WorldState world, IRng rng, out bool broken)
    {
        broken = false;
        Tile hit;
        using (world.Sections.EnterRead(X, Y, X, Y))
            hit = world.Tiles[X, Y];
        if (!hit.Active || !StatelessMultiTileObjects.TryGetValue(hit.Type, out var descriptor))
            return false;

        int localX = hit.FrameX / 18 % descriptor.Width;
        int localY = hit.FrameY / 18 % descriptor.Height;
        int anchorX = X - localX;
        int anchorY = Y - localY;
        if (anchorX < 0 || anchorY < 0 || anchorX + descriptor.Width > world.MaxTilesX ||
            anchorY + descriptor.Height > world.MaxTilesY)
            return true;

        int styleX = hit.FrameX / (descriptor.Width * 18);
        int styleY = hit.FrameY / (descriptor.Height * 18);
        using (world.Sections.EnterWrite(anchorX, anchorY, anchorX + descriptor.Width - 1,
                   anchorY + descriptor.Height - 1))
        {
            for (int x = 0; x < descriptor.Width; x++)
            for (int y = 0; y < descriptor.Height; y++)
            {
                ref var tile = ref world.Tiles[anchorX + x, anchorY + y];
                if (!tile.Active || tile.Type != hit.Type ||
                    tile.FrameX / (descriptor.Width * 18) != styleX ||
                    tile.FrameY / (descriptor.Height * 18) != styleY ||
                    tile.FrameX / 18 % descriptor.Width != x ||
                    tile.FrameY / 18 % descriptor.Height != y)
                    return true;
            }

            for (int x = 0; x < descriptor.Width; x++)
            for (int y = 0; y < descriptor.Height; y++)
            {
                ref var tile = ref world.Tiles[anchorX + x, anchorY + y];
                tile.Active = false;
                tile.Type = 0;
                tile.Wall = 0;
            }
        }

        broken = true;
        for (int x = 0; x < descriptor.Width; x++)
        for (int y = 0; y < descriptor.Height; y++)
            world.MarkTileChanged(anchorX + x, anchorY + y);
        world.SpawnItemDrop(descriptor.ItemId, 1, anchorX, anchorY, rng);
        return true;
    }

    private bool TryBreakContainerObject(WorldState world, IRng rng, out bool broken)
    {
        broken = false;
        Tile hit;
        using (world.Sections.EnterRead(X, Y, X, Y))
            hit = world.Tiles[X, Y];
        if (!hit.Active || (hit.Type != 21 && hit.Type != 467 && hit.Type != 88))
            return false;

        int width = hit.Type == 88 ? 3 : 2;
        int anchorX = X - (hit.FrameX / 18 % width);
        int anchorY = Y - hit.FrameY / 18;
        if (anchorX < 0 || anchorY < 0 || anchorX + width > world.MaxTilesX || anchorY + 2 > world.MaxTilesY)
            return true;

        ushort type = hit.Type;
        short frameX = hit.FrameX;
        int deletedChestIndex;
        using (world.Sections.EnterWrite(anchorX, anchorY, anchorX + width - 1, anchorY + 1))
        {
            for (int localX = 0; localX < width; localX++)
            for (int localY = 0; localY < 2; localY++)
            {
                ref var tile = ref world.Tiles[anchorX + localX, anchorY + localY];
                if (!tile.Active || tile.Type != type || tile.FrameX / 18 % width != localX || tile.FrameY / 18 != localY)
                    return true;
            }

            if (!world.TryDeleteEmptyChestAt(anchorX, anchorY, out deletedChestIndex))
                return true;

            for (int localX = 0; localX < width; localX++)
            for (int localY = 0; localY < 2; localY++)
            {
                ref var tile = ref world.Tiles[anchorX + localX, anchorY + localY];
                tile.Active = false;
                tile.Type = 0;
                tile.Wall = 0;
            }
        }

        if (deletedChestIndex >= 0)
            world.MarkPersistChestDeleted(deletedChestIndex);
        broken = true;

        for (int localX = 0; localX < width; localX++)
        for (int localY = 0; localY < 2; localY++)
            world.MarkTileChanged(anchorX + localX, anchorY + localY);

        int style = type == 88 ? frameX / 54 : frameX / 36;
        int itemId = type switch
        {
            21 => GetChestDrop(style, false),
            467 => GetChestDrop(style, true),
            _ => GetDresserDrop(style),
        };
        world.SpawnItemDrop(itemId, 1, anchorX, anchorY, rng);
        return true;
    }

    private static int GetChestDrop(int style, bool secondType)
    {
        if (secondType)
        {
            int[] drops = { 3884, 3885, 3939, 3965, 3988, 4153, 4174, 4195, 4216, 4265, 4267, 4574,
                4712, 4712, 5156, 5177, 5198, 5556, 5609, 5697, 5720, 5745, 5763, 5784, 5805, 5826,
                5846, 5865, 5886, 5905, 5939, 5962, 5982, 6005, 6028, 6051, 6074, 6118 };
            return style >= 0 && style < drops.Length ? drops[style] : drops[0];
        }

        int[] normalDrops = { 48, 306, 306, 328, 328, 343, 348, 625, 626, 627, 680, 681, 831, 838,
            914, 952, 1142, 1298, 1528, 1529, 1530, 1531, 1532, 1528, 1529, 1530, 1531, 1532, 2230,
            2249, 2250, 2526, 2544, 2559, 2574, 2612, 2612, 2613, 2613, 2614, 2614, 2615, 2616, 2617,
            2618, 2619, 2620, 2748, 2814, 3180, 3125, 3181 };
        return style >= 0 && style < normalDrops.Length ? normalDrops[style] : normalDrops[0];
    }

    private static int GetDresserDrop(int style)
    {
        if (style is >= 1 and <= 3) return 646 + style;
        if (style is >= 5 and <= 15) return 2386 + style - 5;
        int[] drops = { 334, 0, 0, 0, 918, 2386, 2387, 2388, 2389, 2390, 2391, 2392, 2393, 2394, 2395,
            2396, 2529, 2545, 2562, 2577, 2637, 2638, 2639, 2640, 2816, 3132, 3134, 3133, 3911, 3912,
            3913, 3914, 3934, 3968, 4148, 4169, 4190, 4211, 4301, 4569, 5151, 5172, 5193, 5551, 5604,
            5692, 5715, 5741, 5766, 5787, 5808, 5829, 5848, 5868, 5888, 5908, 5942, 5965, 5985, 6008,
            6031, 6054, 6077, 6099, 6121 };
        return style >= 0 && style < drops.Length && drops[style] != 0 ? drops[style] : 334;
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

        if (DiagnosticLog.Enabled)
            Console.WriteLine($"[DIAG] Apply28 pid={playerId} npc={NpcIndex} gen={Generation} dmg={Damage} crit={(Crit ? 1 : 0)} tick={Tick}");

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
                if (DiagnosticLog.Enabled)
                    Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 客户端gen={Generation} 服务端已死(type={npc.Type}) dmg={Damage}");
                return new(false, CommandFailures.NotApplied);
            }
            if ((byte)Generation != npc.Generation)
            {
                if (DiagnosticLog.Enabled)
                    Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 客户端gen={Generation} 服务端gen={npc.Generation} dmg={Damage}");
                return new(false, CommandFailures.NotApplied);
            }

            // 已收录远程武器，以及有固定弹幕映射的已收录魔法武器，必须以服务端登记且实际
            // 接触目标的自有弹幕为依据。其余武器保留既有近战 / 召唤兼容路径。
            bool projectileRequired = RequiresProjectileForStrike(player!);
            bool summonWeaponHeld = IsSummonWeaponHeld(player!);
            bool summonAttackExpected = summonWeaponHeld ||
                IsSelectedSlotEmpty(player!) &&
                (HasOwnedSummonProjectile(world, playerId) || HasSummonWeaponInInventory(player!));
            bool projectileMatched = false;
            bool summonProjectileMatched = false;
            // W-2 档位：ServerDamage 起本体命中的**伤害数值由服务端裁定**（见 SummonAuthorityMode 文件头）。
            bool serverSettlesSummonDamage = world.ServerSettlesSummonDamage;
            int? serverSettledDamage = null;
            if (world.StrikeProjectileMatch || projectileRequired)
            {
                var proj = FindPlayerProjectileNearNpc(world, playerId, npc, out bool hasOwnedProjectile);
                if (proj is null)
                {
                    if (projectileRequired)
                        return new(false, hasOwnedProjectile
                            ? CommandFailures.ProjectileNotColliding
                            : CommandFailures.ProjectileRequired);

                    if (DiagnosticLog.Enabled)
                        Console.WriteLine($"[Strike] slot={NpcIndex} 未找到归属玩家 #{playerId} 的存活碰撞弹幕，交棒武器校验 dmg={Damage}");
                }
                else
                {
                    if (proj.NpcHitCooldownUntil.TryGetValue(NpcIndex, out long cooldownUntil) &&
                        Tick < cooldownUntil)
                        return new(false, CommandFailures.ProjectileHitCooldown);

                    int bound = (int)Math.Ceiling(proj.Damage * 1.15f) * (Crit ? 2 : 1);
                    if (Damage > bound)
                    {
                        if (DiagnosticLog.Enabled)
                            Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 上报伤害 {Damage} 超弹幕上界 {bound}（弹幕key={proj.Key} dmg={proj.Damage} crit={Crit}）");
                        return new(false, CommandFailures.StrikeDamageMismatch);
                    }
                    projectileMatched = true;
                    proj.NpcHitCooldownUntil[NpcIndex] = Tick + 10;
                    if (proj.Penetrate > 0)
                    {
                        proj.Penetrate--;
                        if (proj.Penetrate == 0)
                        {
                            proj.Active = false;
                            proj.DeadTick = Tick;
                        }
                    }
                }
            }

            if (summonAttackExpected)
            {
                // 原版召唤物的包 27 通常只在生成时上报位置；后续包 28 是客户端
                // 依据仆从自身 AI 产生的命中确认，不能要求服务端缓存坐标仍与 NPC 重叠。
                // 归属、活动状态、召唤类型、伤害上限、generation 和冷却仍在此处校验。
                var summonProjectile = FindOwnedSummonProjectile(world, playerId, npc);
                if (summonProjectile is null)
                {
                    if (DiagnosticLog.Enabled)
                        Console.WriteLine($"[DIAG] Apply28 summon-miss pid={playerId} npc={NpcIndex} npcPos=({npc.X:0.0},{npc.Y:0.0})");
                    return new(false, CommandFailures.ProjectileRequired);
                }

                if (DiagnosticLog.Enabled)
                    Console.WriteLine($"[DIAG] Apply28 summon-match pid={playerId} npc={NpcIndex} key={summonProjectile.Key} type={summonProjectile.Type} pos=({summonProjectile.Position.X:0.0},{summonProjectile.Position.Y:0.0}) dmg={summonProjectile.Damage} summon={summonProjectile.IsSummon} kind={summonProjectile.SummonKind} entity={summonProjectile.SummonEntityId} sourceItem={summonProjectile.SourceWeaponItem} buff={summonProjectile.SourceSummonBuffId}");

                if (IsSummonHitOnCooldown(world, summonProjectile, playerId, NpcIndex, Tick))
                {
                    if (DiagnosticLog.Enabled)
                        Console.WriteLine($"[DIAG] Apply28 summon-cooldown pid={playerId} npc={NpcIndex} type={summonProjectile.Type} tick={Tick}");
                    return new(false, CommandFailures.ProjectileHitCooldown);
                }

                if (serverSettlesSummonDamage && IsSummonBody(summonProjectile))
                {
                    // ServerDamage 档：包 28 对**本体**只作「命中触发」，上报的 Damage 不参与结算——
                    // 客户端可选 ≤ 上界的任意数值，采信它等于把伤害数值的决定权留在客户端。
                    // 改由服务端按本体登记伤害掷 ±15% 浮动（原版 Projectile.Damage 的口径），暴击倍率沿用上报 Crit 位
                    // （服务端当前没有暴击率来源，属本档已知边界，见 backlog W-2）。
                    serverSettledDamage = CombatResolver.DamageVar(summonProjectile.Damage, rng);
                }
                else
                {
                    int summonBound = (int)Math.Ceiling(summonProjectile.Damage * 1.15f)
                        * (Crit ? 2 : 1);
                    if (Damage > summonBound)
                    {
                        if (DiagnosticLog.Enabled)
                            Console.WriteLine($"[DIAG] Apply28 summon-damage-mismatch pid={playerId} npc={NpcIndex} reported={Damage} bound={summonBound} projectileDamage={summonProjectile.Damage}");
                        return new(false, CommandFailures.StrikeDamageMismatch);
                    }
                }
                summonProjectileMatched = true;
                MarkSummonHitCooldown(world, summonProjectile, playerId, NpcIndex, Tick);
                if (summonProjectile.Penetrate > 0)
                {
                    summonProjectile.Penetrate--;
                    if (summonProjectile.Penetrate == 0)
                    {
                        summonProjectile.Active = false;
                        summonProjectile.DeadTick = Tick;
                    }
                }
            }

            // 阶段 E「近战武器伤害校验」/ 阶段 F「远程武器校验」/ 阶段 G「召唤弹幕校验」:
            // 无弹幕匹配的命中按**多通道取最大上界**校验——任一合法来源（手持武器或召唤物）
            // 的权威伤害都构成合法上界，上报值超过**所有**通道的上界才拒绝。
            //   · 近战 / 魔法：武器伤害即弹幕伤害（无弹药合并），直接按武器上界校验；
            //   · 远程：弹幕伤害 = 武器 + 弹药（原版 PickAmmo 合并，弹药乘修饰倍率），
            //     未收录弹药类型 / 无弹药合并武器（投掷、鱼叉、gunProj 四件）按武器伤害校验；
            //   · 召唤：仆从伤害 ≠ 手持武器（召唤后切换武器仍沿用创建时伤害），不进手持通道，
            //     改按玩家拥有的存活召唤弹幕最高伤害上界校验（SummonProjectileTable）。
            // 未收录武器 / 空手 / 无召唤弹幕 → 无通道上界，失败放行，绝不误拒未知物品。
            if (!projectileMatched && !summonProjectileMatched && world.StrikeWeaponCheck)
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

                // 通道 2：召唤 / 哨兵上界（阶段 G）。只使用服务端仍存活的召唤弹幕，
                // 不以当前背包内容替代召唤物实体作为命中凭据。
                // ServerDamage 档起排除**本体**：本体命中已由服务端裁定，不能再充当"上界"凭据。
                if (CombatResolver.SummonDamageBound(world, playerId, Crit, excludeBodies: serverSettlesSummonDamage) is int sb)
                    upperBound = Math.Max(upperBound ?? 0, sb);

                if (upperBound is int ub && Damage > ub)
                {
                    if (DiagnosticLog.Enabled)
                        Console.WriteLine($"[Strike] 拒绝 slot={NpcIndex} 上报 {Damage} 超上界 {ub}（crit={Crit}）");
                    return new(false, CommandFailures.StrikeDamageMismatch);
                }
            }

            // 原版服务端收包 28 后的结算（对齐客户端显示口径）：
            //   applied = Main.CalculateDamageNPCsTake(Damage, npc.defense) × (crit ? 2 : 1)
            // 即**先按 NPC 防御减伤**（dmg − def×0.5，最低 1），**再应用暴击倍率**。
            // 漏掉 ×2 会造成「客户端按暴击打死、服务端还差一半血」——客户端贴图消失，服务端该怪仍存活
            // 并继续造成接触伤害（幽灵碰撞）；漏掉防御减伤则服务端扣血多于客户端显示，血量口径不一致。
            // ServerDamage 档：本体命中的结算值由服务端裁定（serverSettledDamage），客户端上报值只作触发。
            int settledDamage = serverSettledDamage ?? Damage;
            int applied = CombatResolver.CalculateDamageNPCsTake(settledDamage, npc.Defense) * (Crit ? 2 : 1);
            npc.Life -= applied;
            if (npc.Life <= 0)
            {
                npc.Life = 0;
                npc.Active = false; // 由世界同步下发 life=0，客户端据此移除
                npc.DeadTick = Tick;
                if (DiagnosticLog.Enabled)
                    Console.WriteLine($"[Kill] slot={NpcIndex} gen={npc.Generation} type={npc.Type} dmg={Damage} def={npc.Defense} applied={applied} @{npc.X:F0},{npc.Y:F0}");
                world.NotifyNpcKilled(npc.Type, npc.X, npc.Y, rng); // Boss 击杀 → 世界进度 + 掉落
            }
            else
            {
                if (DiagnosticLog.Enabled)
                    Console.WriteLine($"[Strike] slot={NpcIndex} gen={npc.Generation} dmg={Damage} def={npc.Defense} applied={applied} → life={npc.Life}");
            }
        }

        return new(true);
    }

    private static bool RequiresProjectileForStrike(PlayerRuntime player)
    {
        if (player.SelectedSlot < 0 || player.SelectedSlot >= PlayerRuntime.InventorySlotCount)
            return false;

        int heldItem = player.Items[player.SelectedSlot];
        return ItemDamageTable.Of.TryGetValue(heldItem, out var stats)
            && (stats.Class == WeaponClass.Ranged
                || stats.Class == WeaponClass.Magic && WeaponProjectileTypeOf.Direct.ContainsKey(heldItem));
    }

    private static bool IsSummonWeaponHeld(PlayerRuntime player)
    {
        if (player.SelectedSlot < 0 || player.SelectedSlot >= PlayerRuntime.InventorySlotCount)
            return false;

        int heldItem = player.Items[player.SelectedSlot];
        return ItemDamageTable.Of.TryGetValue(heldItem, out var stats)
            && stats.Class == WeaponClass.Summon;
    }

    private static bool IsSelectedSlotEmpty(PlayerRuntime player)
    {
        return player.SelectedSlot < 0 || player.SelectedSlot >= PlayerRuntime.InventorySlotCount ||
            player.ItemStacks[player.SelectedSlot] <= 0 || player.Items[player.SelectedSlot] <= 0;
    }

    private static bool HasOwnedSummonProjectile(WorldState world, int playerId)
    {
        lock (world.ProjectilesLock)
            return world.Projectiles.Any(p => p.Active && p.Owner == playerId && p.Damage > 0 &&
                IsSummonProjectile(p));
    }

    private static bool HasSummonWeaponInInventory(PlayerRuntime player)
    {
        for (int slot = 0; slot < PlayerRuntime.InventorySlotCount; slot++)
        {
            if (player.ItemStacks[slot] > 0 &&
                ItemDamageTable.Of.TryGetValue(player.Items[slot], out var stats) &&
                stats.Class == WeaponClass.Summon)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 是否为召唤体系的弹幕（本体 ∪ 派生，见 <see cref="SummonProjectileTable.Of"/>）。
    /// 用于归属 / 生命周期 / 命中归因；**不要**用它判断"是否本体"——那是
    /// <see cref="IsSummonBody"/>（<see cref="SummonEntityTable"/>）的职责。
    /// </summary>
    private static bool IsSummonProjectile(ProjectileEntity projectile)
        => projectile.IsSummon || SummonProjectileTable.Of.Contains(projectile.Type);

    /// <summary>
    /// 是否为召唤 / 哨兵的**本体**（<see cref="SummonEntityTable"/> 的 62 条），而非本体发射的派生弹幕
    /// （如 374 HornetStinger / 389 MiniRetinaLaser）。派生弹幕见 <see cref="SummonShotTable"/>。
    /// </summary>
    private static bool IsSummonBody(ProjectileEntity projectile)
        => SummonEntityTable.Of.ContainsKey(projectile.Type);

    /// <summary>星尘龙**头节**类型：节段 626/627/628 与它共用命中免疫数组（原版 Projectile.cs L12732-12739）。</summary>
    private const int StardustDragonHeadType = 625;

    /// <summary>
    /// 本体命中是否仍在冷却中。口径取自 <see cref="SummonEntityTable"/> 的**三档**（原版 `Projectile.Damage`）：
    /// <list type="bullet">
    /// <item><see cref="SummonHitImmunity.LocalPerTarget"/>：冷却记在**该弹幕实例**上（626/627/628 记在头节 625 上）；</item>
    /// <item><see cref="SummonHitImmunity.IdStaticShared"/>：冷却按 <c>(type, npc)</c> 记，**同 type 所有实例共享**；</item>
    /// <item>默认档：冷却按 <c>(playerId, npc)</c> 记，**该玩家的任意本体**在同一 NPC 上都受约束。</item>
    /// </list>
    /// </summary>
    private static bool IsSummonHitOnCooldown(
        WorldState world, ProjectileEntity projectile, int playerId, int npcIndex, long tick)
    {
        long until;
        switch (SummonEntityTable.ImmunityOf(projectile.Type))
        {
            case SummonHitImmunity.LocalPerTarget:
                return SummonImmunityStoreOf(world, projectile).TryGetValue(npcIndex, out until) && tick < until;
            case SummonHitImmunity.IdStaticShared:
                return world.SummonTypeHitCooldownUntil.TryGetValue((projectile.Type, npcIndex), out until) &&
                    tick < until;
            default:
                return world.SummonPlayerHitCooldownUntil.TryGetValue((playerId, npcIndex), out until) &&
                    tick < until;
        }
    }

    /// <summary>
    /// 记下本体命中冷却（冷却值取自 <see cref="SummonEntityTable"/>；`-1` = 同一弹幕对同一目标终身一次）。
    /// 落点与 <see cref="IsSummonHitOnCooldown"/> 的分档一致。
    /// </summary>
    private static void MarkSummonHitCooldown(
        WorldState world, ProjectileEntity projectile, int playerId, int npcIndex, long tick)
    {
        int cooldown = SummonEntityTable.HitCooldownOf(projectile.Type);
        long until = cooldown < 0 ? long.MaxValue : tick + cooldown;
        switch (SummonEntityTable.ImmunityOf(projectile.Type))
        {
            case SummonHitImmunity.LocalPerTarget:
                SummonImmunityStoreOf(world, projectile)[npcIndex] = until;
                break;
            case SummonHitImmunity.IdStaticShared:
                world.SummonTypeHitCooldownUntil[(projectile.Type, npcIndex)] = until;
                break;
            default:
                world.SummonPlayerHitCooldownUntil[(playerId, npcIndex)] = until;
                break;
        }
    }

    /// <summary>
    /// <see cref="SummonHitImmunity.LocalPerTarget"/> 档的冷却落点：默认是**该弹幕实例自己的**数组；
    /// 但星尘龙节段 626/627/628 在原版里共用**头节 625** 的 <c>localNPCImmunity</c>，
    /// 故先找同属主的存活 625 头节，找不到再退回自己（无头节的独立节段按实例记）。
    /// </summary>
    private static Dictionary<int, long> SummonImmunityStoreOf(WorldState world, ProjectileEntity projectile)
    {
        if (projectile.Type is 626 or 627 or 628)
        {
            lock (world.ProjectilesLock)
            {
                foreach (var p in world.Projectiles)
                {
                    if (p.Active && p.Owner == projectile.Owner && p.Type == StardustDragonHeadType)
                        return p.SummonNpcHitCooldownUntil;
                }
            }
        }

        return projectile.SummonNpcHitCooldownUntil;
    }

    private static ProjectileEntity? FindOwnedSummonProjectile(
        WorldState world, int playerId, WorldNpc npc)
    {
        ProjectileEntity? best = null;
        lock (world.ProjectilesLock)
        {
            foreach (var p in world.Projectiles)
            {
                if (!p.Active || p.Destroyed || p.Owner != playerId || p.Damage <= 0 ||
                    !IsSummonProjectile(p))
                    continue;

                if (best is null)
                {
                    best = p;
                    continue;
                }

                long entityId = p.SummonEntityId > 0 ? p.SummonEntityId : long.MaxValue;
                long bestEntityId = best.SummonEntityId > 0 ? best.SummonEntityId : long.MaxValue;
                if (entityId < bestEntityId ||
                    entityId == bestEntityId && p.Key < best.Key)
                    best = p;
            }
        }

        return best;
    }

    private static ProjectileEntity? FindPlayerProjectileNearNpc(
        WorldState world, int playerId, WorldNpc npc, out bool hasOwnedProjectile,
        bool summonOnly = false)
    {
        const float syncTolerance = 2f;
        var (npcWidth, npcHeight) = NpcSizes.Of(npc.Type);
        float npcLeft = npc.X - syncTolerance;
        float npcTop = npc.Y - syncTolerance;
        float npcRight = npc.X + npcWidth + syncTolerance;
        float npcBottom = npc.Y + npcHeight + syncTolerance;
        ProjectileEntity? best = null;
        hasOwnedProjectile = false;

        lock (world.ProjectilesLock)
        {
            foreach (var p in world.Projectiles)
            {
                if (!p.Active || p.Owner != playerId || p.Damage <= 0 ||
                    summonOnly && !IsSummonProjectile(p))
                    continue;

                hasOwnedProjectile = true;
                if (p.Position.X + p.Width < npcLeft || p.Position.X > npcRight ||
                    p.Position.Y + p.Height < npcTop || p.Position.Y > npcBottom)
                    continue;

                if (best is null)
                {
                    best = p;
                    continue;
                }

                if (!summonOnly)
                {
                    if (p.Damage > best.Damage)
                        best = p;
                    continue;
                }

                float npcCenterX = npc.X + npcWidth / 2f;
                float npcCenterY = npc.Y + npcHeight / 2f;
                float px = p.Position.X + p.Width / 2f - npcCenterX;
                float py = p.Position.Y + p.Height / 2f - npcCenterY;
                float bestX = best.Position.X + best.Width / 2f - npcCenterX;
                float bestY = best.Position.Y + best.Height / 2f - npcCenterY;
                float distanceSquared = px * px + py * py;
                float bestDistanceSquared = bestX * bestX + bestY * bestY;
                long entityId = p.SummonEntityId > 0 ? p.SummonEntityId : long.MaxValue;
                long bestEntityId = best.SummonEntityId > 0 ? best.SummonEntityId : long.MaxValue;
                if (distanceSquared < bestDistanceSquared ||
                    distanceSquared == bestDistanceSquared &&
                    (entityId < bestEntityId || entityId == bestEntityId && p.Key < best.Key))
                {
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
    long Tick, int? PlayerId, int Key, int Type, Vector2 Position, Vector2 Velocity, int Damage,
    long FireTransactionId = 0)
    : Command(Tick, PlayerId, "spawn_projectile")
{
    /// <summary>服务端兼容未知武器的默认开火间隔。</summary>
    private const long MinimumUseItemProjectileIntervalTicks =
        WeaponUseBehaviorTable.CompatibilityUseTime;

    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int playerId)
            return new(false, CommandFailures.MissingPlayer);
        if (!TryGetPlayer(world, playerId, out var player, out var failure))
            return failure;

        lock (world.ProjectilesLock)
        {
            // 客户端 projectile 索引仅在归属者范围内唯一；不同玩家可同时使用同一 Key。
            var existing = world.Projectiles.FirstOrDefault(p => p.Owner == playerId && p.Key == Key);
            if (existing is not null)
            {
                // 召唤 / 哨兵的位置和速度由客户端 AI 持续上报；只接受经过边界校验的运动状态，
                // 不接受客户端对伤害、类型、归属或生命周期字段的修改。
                if (existing.IsSummon || SummonProjectileTable.Of.Contains(existing.Type))
                {
                    if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) ||
                        !float.IsFinite(Velocity.X) || !float.IsFinite(Velocity.Y))
                        return new(false, CommandFailures.ProjectileSpawnInvalid);

                    float existingWorldWidth = world.MaxTilesX * 16f;
                    float existingWorldHeight = world.MaxTilesY * 16f;
                    if (Position.X < 0f || Position.Y < 0f ||
                        Position.X + existing.Width > existingWorldWidth ||
                        Position.Y + existing.Height > existingWorldHeight)
                        return new(false, CommandFailures.ProjectileSpawnOutOfWorld);

                    if (Velocity.X * Velocity.X + Velocity.Y * Velocity.Y > 512f * 512f)
                        return new(false, CommandFailures.ProjectileSpeedExceeded);

                    existing.Position = Position;
                    existing.Velocity = Velocity;
                }

                // 普通弹幕由服务端仿真推进；包 27 更新不采信客户端的位置、速度或生命周期状态。
                return new(true);
            }

            // 已收到过包 13 的玩家必须以当前 UseItem 按住状态及其关联槽位创建已映射普通武器弹幕。
            // 未收到包 13 时保留直接构造 WorldState/Command 的既有测试与服务端路径兼容性。
            bool requiresUseItem = RequiresUseItemState(player!);
            bool hasRegisteredUseBehavior = player!.SelectedSlot >= 0 &&
                player.SelectedSlot < PlayerRuntime.InventorySlotCount &&
                WeaponUseBehaviorTable.Of.ContainsKey(player.Items[player.SelectedSlot]);
            bool controlledFire = (requiresUseItem || hasRegisteredUseBehavior) &&
                player.HasReceivedPlayerControls;
            int firingSlot = player.SelectedSlot;
            WeaponUseBehavior useBehavior = WeaponUseBehaviorTable.For(0);
            if (controlledFire)
            {
                if (!player.PressingUseItem || player.UseItemSelectedSlot != player.SelectedSlot)
                    return new(false, CommandFailures.ProjectileUseItemNotHeld);

                firingSlot = player.UseItemSelectedSlot;
                int firingItem = player.Items[firingSlot];
                useBehavior = WeaponUseBehaviorTable.For(firingItem);
                if (FireTransactionId > 0 && player.AppliedFireTransactions.Contains(FireTransactionId))
                    return new(true, CommandFailures.FireTransactionDuplicate);
                if (player.LastUseItemProjectileSpawnTick != long.MinValue &&
                    world.Tick - player.LastUseItemProjectileSpawnTick < useBehavior.UseTime)
                    return new(false, CommandFailures.ProjectileUseItemCooldown);
            }

            // 碰撞盒按原版逐类型尺寸（Sizes），未登记类型沿用 16×16 近似。
            var size = ProjectileCapabilityTable.Sizes.TryGetValue(Type, out var s)
                ? s : (Width: 16f, Height: 16f);

            if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) ||
                !float.IsFinite(Velocity.X) || !float.IsFinite(Velocity.Y))
                return new(false, CommandFailures.ProjectileSpawnInvalid);

            float worldWidth = world.MaxTilesX * 16f;
            float worldHeight = world.MaxTilesY * 16f;
            if (Position.X < 0f || Position.Y < 0f || Position.X + size.Width > worldWidth ||
                Position.Y + size.Height > worldHeight)
                return new(false, CommandFailures.ProjectileSpawnOutOfWorld);

            if (Velocity.X * Velocity.X + Velocity.Y * Velocity.Y > 512f * 512f)
                return new(false, CommandFailures.ProjectileSpeedExceeded);

            // 弹幕伤害的服务端权威推导结果（默认沿用客户端上报；近战 / 魔法等可精确推导的通道则覆盖为
            // 服务端推导值，使阶段 C 命中匹配以服务端权威值而非客户端上报值为基准）。
            int derivedDamage = Damage;

            // 对已收录的武器，包 27 的弹幕类型必须能由服务端权威手持物品和背包弹药推出。
            // 未收录武器或弹药保持兼容性放行；它们无法被安全地映射到固定弹幕类型。
            if (player!.SelectedSlot >= 0 && player.SelectedSlot < PlayerRuntime.InventorySlotCount)
            {
                int heldItem = player.ItemStacks[player.SelectedSlot] > 0 ? player.Items[player.SelectedSlot] : 0;
                if (WeaponProjectileTypeOf.Direct.TryGetValue(heldItem, out int directType))
                {
                    if (Type != directType)
                        return new(false, CommandFailures.ProjectileTypeNotAllowed);
                }
                else if (WeaponAmmoTypeOf.Of.TryGetValue(heldItem, out int ammoType))
                {
                    bool allowsType = false;
                    for (int slot = 0; slot < PlayerRuntime.InventorySlotCount; slot++)
                    {
                        int ammoItem = player.Items[slot];
                        if (player.ItemStacks[slot] <= 0 || !AmmoTypeOf.Of.TryGetValue(ammoItem, out int actualAmmoType) || actualAmmoType != ammoType)
                            continue;

                        if (!WeaponProjectileTypeOf.Ammo.TryGetValue(ammoItem, out int ammoProjectileType))
                            continue;

                        if (Type == ammoProjectileType)
                        {
                            allowsType = true;
                            break;
                        }
                    }

                    if (!allowsType)
                        return new(false, CommandFailures.ProjectileTypeNotAllowed);
                }
            }

            if (player!.SelectedSlot >= 0 && player.SelectedSlot < PlayerRuntime.InventorySlotCount)
            {
                int heldItem = player.ItemStacks[player.SelectedSlot] > 0 ? player.Items[player.SelectedSlot] : 0;
                if (WeaponProjectileTypeOf.Direct.ContainsKey(heldItem) || WeaponAmmoTypeOf.Of.ContainsKey(heldItem))
                {
                    // 普通手持发射的起点应接近玩家中心偏移，避免远程凭空生成。
                    float dx = Position.X + size.Width / 2f - (player.Position.X + 10f);
                    float dy = Position.Y + size.Height / 2f - (player.Position.Y + 21f);
                    if (dx * dx + dy * dy > 160f * 160f)
                        return new(false, CommandFailures.ProjectileSpawnTooFar);
                }
            }

            // 阶段 H：召唤 / 哨兵弹幕 spawn 伤害权威校验——堵住「虚报弹幕伤害 → 命中上界随之上抬」漏洞。
            // 原版仆从伤害 = **召唤时**武器伤害（GetWeaponDamage 含前缀 / Buff / 饰品 / 套装），创建时一次确定，
            // 之后不随背包 / 手持变化；阶段 G 依此用弹幕 Damage 计算命中上界（ceil(p.Damage × 1.15) × crit）。
            // 规则：
            //   · 手持已收录**召唤武器**：弹幕 Damage 必须 ≤ ceil(权威武器伤害 × 1.15)（容差对齐命中浮动），超限拒绝；
            //   · 空手 / 手持明确非召唤武器（近战 / 远程 / 魔法）：拒绝（原版只有召唤武器能 spawn 召唤弹幕，
            //     空手 spawn 只存在于客户端本地，登记会造成幽灵弹幕）；
            //   · 手持未收录武器（mod 等）：放行（绝不误拒未知物品）。
            // **只对本体（SummonEntityTable）做这条上界校验**：派生弹幕的伤害倍率逐弹幕不同
            // （例如 1044 是 `damage × 1.33`、389 是 `damage × 1.15`），用「≤ 武器伤害 ×1.15」去卡会误拒，
            // 故派生弹幕刻意**放行**——它们的伤害基准仍由包 28 的召唤通道上界把关
            // （该通道由弹幕自身的 Damage 反推，与倍率无关）。
            if (SummonEntityTable.Of.ContainsKey(Type))
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
            else
            {
                // 阶段 H2：非召唤 / 非哨兵弹幕的创建时伤害权威接入。
                // 普通弹幕伤害 = 发射时刻武器伤害（原版 ItemCheck_Shoot 把武器权威伤害传给 NewProjectile），
                // 服务端在创建时用「手持已收录武器」推导权威伤害，并对客户端上报值做上界校验（±15% 命中浮动容差）——
                // 堵住「虚报高伤害普通弹幕 → 阶段 C 用其 Damage 抬命中上界」的漏洞。
                //   · 近战 / 魔法：弹幕伤害 == 武器伤害（无弹药合并），可精确推导 → 校验 + 覆盖 derivedDamage；
                //   · 远程：弹幕伤害含弹药合并（原版 PickAmmo），RangedDamageBound 已取最高弹药合并上界，
                //     仅作校验（更宽容，不覆盖，避免写低值误拒）；
                //   · 手持未知 / 空手 / Buff 弹幕：无推导来源 → 放行（绝不误拒未知物品）。
                if (player!.SelectedSlot >= 0 && player.SelectedSlot < PlayerRuntime.InventorySlotCount)
            {
                int heldItem = player.Items[player.SelectedSlot];
                byte heldPrefix = player.ItemPrefixes[player.SelectedSlot];
                if (heldItem > 0 && ItemDamageTable.Of.TryGetValue(heldItem, out var heldStats)
                    && heldStats.Class is WeaponClass.Melee or WeaponClass.Magic or WeaponClass.Ranged)
                {
                    if (heldStats.Class is WeaponClass.Melee or WeaponClass.Magic)
                    {
                        int wd = CombatResolver.GetWeaponDamage(player, heldItem, heldPrefix);
                        if (Damage > (int)Math.Ceiling(wd * 1.15f))
                            return new(false, CommandFailures.ProjectileDamageAboveBound);
                        derivedDamage = wd;   // 精确推导 → 服务端权威覆盖
                    }
                    else if (CombatResolver.RangedDamageBound(player, heldItem, false, heldPrefix) is int rb
                             && Damage > (int)Math.Ceiling(rb * 1.15f))
                    {
                        return new(false, CommandFailures.ProjectileDamageAboveBound);
                    }
                    // 远程仅校验（上界已含弹药合并），不覆盖 storeDamage。
                }
            }
            }

            int sourceWeaponItem = firingSlot >= 0 && firingSlot < PlayerRuntime.InventorySlotCount
                ? player.Items[firingSlot] : 0;
            byte sourceWeaponPrefix = firingSlot >= 0 && firingSlot < PlayerRuntime.InventorySlotCount
                ? player.ItemPrefixes[firingSlot] : (byte)0;
            SummonKind summonKind = SummonProjectileTable.KindOf(Type);

            // 全部校验完成后才预检资源。任一资源不足时不修改另一项，保证开火要么整体提交、要么完全不变。
            int manaCost = controlledFire ? useBehavior.ManaCost : 0;
            if (player.Mp < manaCost)
                return new(false, CommandFailures.ProjectileManaInsufficient);

            int ammoSlot = -1;
            bool infiniteAmmo = false;
            if (firingSlot >= 0 && firingSlot < PlayerRuntime.InventorySlotCount &&
                WeaponAmmoTypeOf.Of.TryGetValue(player.Items[firingSlot], out int requiredAmmoType))
            {
                for (int slot = 0; slot < PlayerRuntime.InventorySlotCount; slot++)
                {
                    int ammoItem = player.Items[slot];
                    if (player.ItemStacks[slot] <= 0 || !AmmoTypeOf.Of.TryGetValue(ammoItem, out int actualAmmoType) ||
                        actualAmmoType != requiredAmmoType || !WeaponProjectileTypeOf.Ammo.TryGetValue(ammoItem, out int ammoProjectileType) ||
                        ammoProjectileType != Type)
                        continue;

                    if (ammoItem is 3103 or 3104)
                    {
                        infiniteAmmo = true;
                        break;
                    }

                    ammoSlot = slot;
                    break;
                }

                if (!infiniteAmmo && ammoSlot < 0)
                    return new(false, CommandFailures.ProjectileTypeNotAllowed);
            }

            long committedFireTransactionId = FireTransactionId > 0
                ? FireTransactionId
                : player.NextFireTransactionId++;
            player.Mp -= manaCost;
            if (manaCost > 0)
                world.MarkPlayerManaChanged(playerId);

            if (!infiniteAmmo && ammoSlot >= 0)
            {
                player.ItemStacks[ammoSlot]--;
                if (player.ItemStacks[ammoSlot] == 0)
                {
                    player.Items[ammoSlot] = 0;
                    player.ItemPrefixes[ammoSlot] = 0;
                }
                world.MarkInventoryChanged(playerId, player.SessionId, ammoSlot);
            }

            var projectile = new ProjectileEntity
            {
                Key = Key,
                Owner = playerId,
                Type = Type,
                Position = Position,
                Velocity = Velocity,
                Damage = derivedDamage,
                SourceItem = sourceWeaponItem,
                SourceWeaponItem = sourceWeaponItem,
                SourceWeaponPrefix = sourceWeaponPrefix,
                SourceTransactionId = summonKind != SummonKind.None
                    ? world.AllocateProjectileSourceTransactionId() : 0,
                FireTransactionId = committedFireTransactionId,
                SpawnTick = world.Tick,
                IsSummon = summonKind != SummonKind.None,
                SummonEntityId = summonKind != SummonKind.None
                    ? world.AllocateSummonEntityId() : 0,
                SummonKind = summonKind,
                // 召唤 Buff 归属只记在**本体**上：Buff 消失时原版杀的是仆从本体，
                // 派生弹幕（在飞的弹幕）不受 Buff 移除影响（见 WorldState.KillSummonedProjectilesForBuff）。
                SourceSummonBuffId = summonKind == SummonKind.Minion &&
                    SummonEntityTable.Of.ContainsKey(Type) &&
                    SummonProjectileTable.SummonWeaponBuff.TryGetValue(sourceWeaponItem, out int summonBuffId)
                        ? summonBuffId : 0,
                Penetrate = -1,
                Width = size.Width,
                Height = size.Height,
                NewNotified = false,
            };
            // 哨兵按原版 timeLeft = 36000 起算（值取自本体表），由仿真逐 tick 递减、到期自毁（见 SimulateEntities）。
            // 仆从表值为 0 = 生命由召唤 Buff 驱动、不按计时销毁，故**不动**它的默认 timeLeft。
            if (SummonEntityTable.InfoOf(Type) is { TimeLeft: > 0 } summonInfo)
                projectile.TimeLeft = summonInfo.TimeLeft;
            world.Projectiles.Add(projectile);
            if (DiagnosticLog.Enabled && Type == 266)
                Console.WriteLine($"[DIAG] Apply27 summon-created pid={playerId} key={projectile.Key} type={projectile.Type} pos=({projectile.Position.X:0.0},{projectile.Position.Y:0.0}) dmg={projectile.Damage} summon={projectile.IsSummon} kind={projectile.SummonKind} entity={projectile.SummonEntityId} sourceItem={projectile.SourceWeaponItem} prefix={projectile.SourceWeaponPrefix} buff={projectile.SourceSummonBuffId} tick={projectile.SpawnTick}");
            if (controlledFire)
            {
                player.LastUseItemProjectileSpawnTick = world.Tick;
                player.RecordAppliedFireTransaction(committedFireTransactionId);
            }
        }

        return new(true);
    }

    private static bool RequiresUseItemState(PlayerRuntime player)
    {
        if (player.SelectedSlot < 0 || player.SelectedSlot >= PlayerRuntime.InventorySlotCount)
            return false;

        int heldItem = player.ItemStacks[player.SelectedSlot] > 0 ? player.Items[player.SelectedSlot] : 0;
        return WeaponProjectileTypeOf.Direct.ContainsKey(heldItem)
            || WeaponAmmoTypeOf.Of.ContainsKey(heldItem);
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
            if (PlayerId is not int owner)
                return new(false, CommandFailures.MissingPlayer);

            var projectile = world.Projectiles.FirstOrDefault(p => p.Owner == owner && p.Key == Key && p.Active);
            if (projectile is null)
                return new(false, CommandFailures.ProjectileNotFound);

            projectile.Active = false;
            projectile.DeadTick = world.Tick;   // 用仿真 tick（命令的 Tick 可能落后于当前世界 tick）
            projectile.RemovalNotified = true; // 客户端已发起销毁，无需服务端再补发
            return new(true);
        }
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

        if (player!.HasReceivedManaSync && (MaxMana > player.MpMax || Mana > player.Mp))
        {
            world.MarkPlayerManaChanged(id);
            return new(false, CommandFailures.NotApplied);
        }

        player.MpMax = MaxMana;
        player.Mp = Math.Clamp(Mana, 0, player.MpMax);
        player.HasReceivedManaSync = true;
        return new(true);
    }
}

/// <summary>
/// 背包槽位暂存命令（SSC 背包守恒事务）：包 5 与服务端权威值不一致时不再立即回正，
/// 而是把客户端意图暂存进该玩家的背包事务窗口，窗口到期后由守恒校验决定整体提交或回滚。
/// 原版客户端拖拽 / 整理 / 拆分 / 合并 / 交换都会在一个窗口内发出多个槽位包，
/// 逐包校验无法区分「合法的多槽联动」与「凭空造物」，故必须窗口聚合后再判。
/// </summary>
public sealed record StageInventorySlotCommand(long Tick, int? PlayerId, int Slot, int ItemId, int Stack, byte Prefix = 0)
    : Command(Tick, PlayerId, "stage_inventory_slot")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return new(false, CommandFailures.NotApplied);

        if (Slot < 0 || Slot >= PlayerRuntime.InventorySlotCount)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;

        // 合成环境快照（可达区域图格 + 相邻液体）：等价原版暂存时刻的 AdjTiles()。
        // 必须先于 PlayersLock（锁序 SectionLocks → PlayersLock → ChestsLock），提交时据此校验配方前置条件。
        CraftingEnvironmentSampler.Refresh(world, player);

        // 开箱期间的包 5 属于「背包 ↔ 箱子」转移的一半：必须与同一窗口的包 32 合并结算。
        // 若按背包事务单独校验，存入箱子会让背包总量减少而被判不守恒回滚（取出则相反）。
        // 锁序与 StageChestItemCommand / TryCommitChestTransaction 一致：PlayersLock → ChestsLock。
        lock (world.PlayersLock)
        {
            lock (world.ChestsLock)
            {
                if (world.TryGetOpenedChestIndex(id, out int chestIndex))
                {
                    world.EnsureChestTransaction(player!, chestIndex, world.Tick);
                    player!.PendingChestInventoryChanges[Slot] = (ItemId, Stack, Prefix);
                    world.MarkChestTransactionOpen(id);
                    return new(true);
                }
            }

            // 会话切换后不得沿用上一会话的暂存意图（重连复用同一槽位时会串号）
            if (player!.InventoryTransactionSessionId != player.SessionId)
            {
                player.PendingInventoryChanges.Clear();
                player.InventoryTransactionBaseline.Clear();
                player.InventoryTransactionSessionId = player.SessionId;
            }

            if (player.PendingInventoryChanges.Count == 0)
            {
                player.InventoryTransactionStartTick = world.Tick;
                // 窗口开始时取基准：约束本窗口内可消耗 / 可作合成材料的物品上限（见 InventoryTransactionBaseline）。
                PlayerRuntime.CaptureInventoryBaseline(player);
            }

            player.PendingInventoryChanges[Slot] = (ItemId, Stack, Prefix);
        }

        world.MarkInventoryTransactionOpen(id);
        return new(true);
    }
}

/// <summary>
/// 对话 NPC 命令：包 40 SyncTalkNPC 权威通过后生成。
/// 服务端持有该表现状态唯一真相：写入玩家当前对话的城镇 NPC 槽位（-1 = 未对话），
/// 并在真正发生变化时标记中继，由 GameHost 向其他玩家广播包 40。
/// 索引越界或指向非活跃 / 非城镇 NPC 一律视为「未对话」，避免把无效目标中继给其他客户端。
/// </summary>
public sealed record SetTalkNpcCommand(long Tick, int? PlayerId, int TalkNpc)
    : Command(Tick, PlayerId, "set_talk_npc")
{
    public override CommandApplyResult Apply(WorldState world, IRng rng)
    {
        if (PlayerId is not int id)
            return new(false, CommandFailures.NotApplied);

        if (!TryGetPlayer(world, id, out var player, out var failure))
            return failure;

        int target = -1;
        if (TalkNpc >= 0)
        {
            lock (world.NpcsLock)
            {
                if (TalkNpc < world.Npcs.Count && world.Npcs[TalkNpc].Active
                    && world.Npcs[TalkNpc].IsTownNpc)
                    target = TalkNpc;
            }
        }

        // 幂等：状态未变不重复广播（客户端会高频重发同一对话目标）
        if (player!.TalkNpc == target)
            return new(true);

        player.TalkNpc = target;
        world.MarkPlayerTalkNpcChanged(id);
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

        var removedSummonBuffs = player!.Buffs
            .Where(SummonProjectileTable.IsSummonBuff)
            .Except(Buffs)
            .ToArray();
        player.Buffs.Clear();
        player.Buffs.AddRange(Buffs);
        foreach (int buffId in removedSummonBuffs)
            world.KillSummonedProjectilesForBuff(id, buffId);
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
