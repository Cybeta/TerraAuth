// TerraAuth — Phase 2: 六个权威子系统实现
// 架构 §4.2 / §5：所有 Validate 方法返回 Accept/Reject/Correct，绝不默默信任客户端

using System.Collections.Concurrent;
using TerraAuth.Protocol;
using TerraAuth.Simulation;

namespace TerraAuth.Authority;

// ---------- 阈值配置 ----------
// 由 ServerConfig 映射注入（架构 §4.5 唯一来源），使 Authority 层不反向依赖 Config 层。

public readonly record struct PlayerLimits(int MaxHp, int MaxMana)
{
    public static PlayerLimits Default => new(MaxHp: 500, MaxMana: 200);
}

/// <summary>移动校验阈值。</summary>
public readonly record struct MovementLimits(float MaxSpeed, float TeleportTolerance, int MaxTeleportsPerSecond = 5)
{
    /// <summary>兜底默认值（与 ServerConfig 默认一致）。</summary>
    public static MovementLimits Default => new(MaxSpeed: 8.0f, TeleportTolerance: 4.0f, MaxTeleportsPerSecond: 5);
}

/// <summary>战斗校验阈值：单次伤害上限 + 统计窗口内的伤害总量上限。</summary>
public readonly record struct CombatLimits(int MaxSingleDamage, int MaxDpsWindowSeconds, int MaxDps)
{
    public static CombatLimits Default => new(MaxSingleDamage: 30000, MaxDpsWindowSeconds: 5, MaxDps: 50000);
}

/// <summary>库存校验阈值。SscEnabled=true 时服务端持有唯一真相源。</summary>
public readonly record struct InventoryLimits(bool SscEnabled, int MaxStackSize)
{
    /// <summary>Terraria 玩家背包槽位数（0..58，含装备/饰品/坐骑槽）。</summary>
    public const int MaxSlots = 59;

    public static InventoryLimits Default => new(SscEnabled: true, MaxStackSize: 999);
}

/// <summary>世界交互阈值：每秒挖砖/放砖上限。</summary>
public readonly record struct WorldLimits(int MaxTileBreakPerSecond, int MaxTilePlacePerSecond)
{
    public static WorldLimits Default => new(MaxTileBreakPerSecond: 60, MaxTilePlacePerSecond: 40);
}

// ---------- 玩家属性权威 ----------

internal sealed class PlayerAuthority : IPlayerAuthority
{
    private readonly IAuditLogger _audit;
    private readonly PlayerLimits _limits;
    private readonly ConcurrentDictionary<int, PlayerStats> _stats = new();

    public PlayerAuthority(IAuditLogger audit, PlayerLimits limits) => (_audit, _limits) = (audit, limits);

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands)
    {
        if (packet is not PlayerHealthPacket hp)
            return AuthorityResult.Accept(packet);

        // 基础合法性：负血量 / 非正上限直接拒绝
        if (hp.Hp < 0 || hp.MaxHp <= 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "health_rejected", "invalid_health",
                new { hp.Hp, hp.MaxHp }));
            return AuthorityResult.Reject("invalid_health");
        }

        var state = _stats.GetOrAdd(playerId, _ => new PlayerStats(_limits.MaxHp, _limits.MaxMana));
        lock (state)
        {
            // 上限由服务端持有：客户端不得抬高，超出即纠正为服务端值
            if (hp.MaxHp > state.MaxHp)
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "health_corrected", "max_hp_exceeded",
                    new { ClientMaxHp = hp.MaxHp, ServerMaxHp = state.MaxHp }));
                return AuthorityResult.Correct(
                    new PlayerHealthPacket(playerId, Math.Min(hp.Hp, state.MaxHp), state.MaxHp),
                    "max_hp_exceeded");
            }

            // 客户端不得上报高于服务端上限的血量（CE 改血）
            if (hp.Hp > state.MaxHp)
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "health_corrected", "hp_exceeded",
                    new { ClientHp = hp.Hp, ServerMaxHp = state.MaxHp }));
                return AuthorityResult.Correct(
                    new PlayerHealthPacket(playerId, state.MaxHp, state.MaxHp),
                    "hp_exceeded");
            }

            // 客户端可下调上限（卸装备），但不得高于服务端记录
            state.Hp = hp.Hp;
            state.MaxHp = hp.MaxHp;
            return AuthorityResult.Accept(packet);
        }
    }

    public int GetMaxHp(int playerId) => _stats.TryGetValue(playerId, out var s) ? s.MaxHp : _limits.MaxHp;
    public int GetMaxMana(int playerId) => _stats.TryGetValue(playerId, out var s) ? s.MaxMana : _limits.MaxMana;

    private sealed class PlayerStats
    {
        public int Hp;
        public int MaxHp;
        public readonly int MaxMana;

        public PlayerStats(int maxHp, int maxMana)
        {
            Hp = maxHp;
            MaxHp = maxHp;
            MaxMana = maxMana;
        }
    }
}

// ---------- 移动权威 ----------

internal sealed class MovementAuthority : IMovementAuthority
{
    // Δt 钳制区间：下限取客户端最快发包间隔（60Hz），避免服务端批量处理积压包时
    // dt 被低估、把正常移动误判为超速。
    private const double MinDtSeconds = 1.0 / 60.0;

    // 上限：客户端失焦 / 卡顿时位置包间隔实测可达 4~7s，旧值 1.0s 会把合法移动
    // 误判为超速（实测首帧 distance=776 本应通过），故放宽到 10s 覆盖稀疏发包。
    // 注意：超过 10s 的间隔一律按 10s 计，单包允许位移上限 8.0×60×10+4=4804px。
    // 这是防作弊与可用性的折中：长时静默后的大位移仍会被拒绝，不做无条件放行
    // （见「后续事项：失焦误杀」）。
    private const double MaxDtSeconds = 10.0;

    // MaxSpeed 沿用 Terraria 原版量纲（像素/帧，60 FPS），而 dt 单位为秒，需乘帧率换算
    private const float FramesPerSecond = 60f;

    // 世界尺寸与实体索引上限（与 Main.player[255] / Main.npc[200] / 大型世界一致）
    private const int WorldWidthTiles = 8400;
    private const int WorldHeightTiles = 2400;
    private const int TileSizePixels = 16;
    private const int MaxPlayerIndex = 254;
    private const int MaxNpcIndex = 199;

    private readonly IPlayerAuthority _players;
    private readonly IAuditLogger _audit;
    private readonly MovementLimits _limits;
    private readonly ConcurrentDictionary<int, PlayerMotion> _motion = new();

    public MovementAuthority(IPlayerAuthority players, IAuditLogger audit, MovementLimits limits)
        => (_players, _audit, _limits) = (players, audit, limits);

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands)
    {
        // 传送类包：客户端只声明意图，落点 / 目标 / 频率由服务端判定
        if (packet is TeleportEntityPacket teleport)
            return ValidateTeleport(teleport, playerId);
        if (packet is RequestTeleportationByServerPacket request)
            return ValidateTeleportRequest(request, playerId);

        // 位置来源：包 13（真实线格式 PlayerControls）与内部简化的 PlayerPositionPacket 共用同一套超速判定
        var position = packet switch
        {
            PlayerControlsPacket controls => controls.Position,
            PlayerPositionPacket pos => pos.Position,
            _ => (Vector2?)null,
        };
        if (position is null)
            return AuthorityResult.Accept(packet);

        var reported = position.Value;
        var now = DateTimeOffset.UtcNow;
        var state = _motion.GetOrAdd(playerId, _ => new PlayerMotion());

        lock (state)
        {
            // 首个位置包：以客户端上报值建立权威基准
            if (!state.HasBaseline)
            {
                state.LastPosition = reported;
                state.LastSeenAt = now;
                state.HasBaseline = true;
                return AuthorityResult.Accept(packet);
            }

            var rawDt = (now - state.LastSeenAt).TotalSeconds;

            // 不区分静默时长，统一按钳制后的 Δt 限距：长时静默（失焦 / 卡顿 / 重连）
            // 后的位置包同样受上限约束，避免「静默超时无条件放行」成为穿墙 / 瞬移
            // 缺口。代价是静默期间位移超过 4804px 的合法玩家会被误拒（见「后续
            // 事项：失焦误杀」）。
            var dt = ClampDt(rawDt);
            var maxSpeed = GetMaxSpeedFor(playerId);
            var dx = reported.X - state.LastPosition.X;
            var dy = reported.Y - state.LastPosition.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            var allowed = maxSpeed * FramesPerSecond * (float)dt + _limits.TeleportTolerance;

            if (distance > allowed)
            {
                // 拒绝时保持权威基准不变：瞬移包不得污染服务端位置
                _audit.Log(AuditEvent.Now(playerId, "authority", "position_rejected", "speed_exceeded",
                    new { Distance = distance, Allowed = allowed, MaxSpeed = maxSpeed, Dt = dt }));
                return AuthorityResult.Reject("speed_exceeded");
            }

            state.LastPosition = reported;
            state.LastSeenAt = now;
            return AuthorityResult.Accept(packet);
        }
    }

    public float GetMaxSpeedFor(int playerId) => _limits.MaxSpeed;

    /// <summary>
    /// 校验 TeleportEntity（包 65）：目标种类 / 实体索引 / 落点越界 / 频率。
    /// 通过后同步权威位置基准到落点，避免紧随其后的位置包被误判为超速瞬移。
    /// </summary>
    private AuthorityResult ValidateTeleport(TeleportEntityPacket teleport, int playerId)
    {
        var now = DateTimeOffset.UtcNow;
        var state = _motion.GetOrAdd(playerId, _ => new PlayerMotion());

        lock (state)
        {
            // 实体索引范围：玩家 0..254（Main.player[255]），NPC 0..199（Main.npc[200]）
            var maxEntityId = teleport.Kind == TeleportEntityKind.Npc ? MaxNpcIndex : MaxPlayerIndex;
            if (teleport.EntityId < 0 || teleport.EntityId > maxEntityId)
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "teleport_rejected", "invalid_teleport_target",
                    new { teleport.EntityId, Max = maxEntityId }));
                return AuthorityResult.Reject("invalid_teleport_target");
            }

            // 落点越界：标志 bit2 表示服务端以自身记录覆盖坐标，客户端坐标不参与判定
            if (!teleport.NoPosition && !IsWithinWorld(teleport.Position))
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "teleport_rejected", "teleport_out_of_bounds",
                    new { teleport.Position.X, teleport.Position.Y }));
                return AuthorityResult.Reject("teleport_out_of_bounds");
            }

            // 频率：与包 73 共用同一窗口，防止瞬移刷屏
            if (!state.Teleports.TryConsume(now, _limits.MaxTeleportsPerSecond))
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "teleport_rejected", "teleport_rate_exceeded",
                    new { Max = _limits.MaxTeleportsPerSecond }));
                return AuthorityResult.Reject("teleport_rate_exceeded");
            }

            // 玩家传送成立 → 权威基准随之迁移（NPC / 确认包不影响玩家位置基准）
            if (!teleport.NoPosition
                && (teleport.Kind == TeleportEntityKind.Player || teleport.Kind == TeleportEntityKind.PlayerToPlayer))
            {
                state.LastPosition = teleport.Position;
                state.LastSeenAt = now;
                state.HasBaseline = true;
            }

            return AuthorityResult.Accept(teleport);
        }
    }

    /// <summary>校验 RequestTeleportationByServer（包 73）：种类合法性 + 频率。</summary>
    private AuthorityResult ValidateTeleportRequest(RequestTeleportationByServerPacket request, int playerId)
    {
        var now = DateTimeOffset.UtcNow;
        var state = _motion.GetOrAdd(playerId, _ => new PlayerMotion());

        lock (state)
        {
            if ((byte)request.Kind > (byte)TeleportRequestKind.PlayerNoSpaceTeleport)
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "teleport_request_rejected", "invalid_teleport_request",
                    new { Kind = (byte)request.Kind }));
                return AuthorityResult.Reject("invalid_teleport_request");
            }

            if (!state.Teleports.TryConsume(now, _limits.MaxTeleportsPerSecond))
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "teleport_request_rejected", "teleport_rate_exceeded",
                    new { Max = _limits.MaxTeleportsPerSecond }));
                return AuthorityResult.Reject("teleport_rate_exceeded");
            }

            return AuthorityResult.Accept(request);
        }
    }

    /// <summary>世界像素坐标是否落在世界范围内。</summary>
    private static bool IsWithinWorld(Vector2 p) =>
        p.X >= 0 && p.X < WorldWidthTiles * TileSizePixels &&
        p.Y >= 0 && p.Y < WorldHeightTiles * TileSizePixels;

    private static double ClampDt(double dt)
    {
        if (double.IsNaN(dt) || dt <= 0) return MinDtSeconds;
        if (dt < MinDtSeconds) return MinDtSeconds;
        if (dt > MaxDtSeconds) return MaxDtSeconds;
        return dt;
    }

    private sealed class PlayerMotion
    {
        public Vector2 LastPosition;
        public DateTimeOffset LastSeenAt;
        public bool HasBaseline;

        /// <summary>传送频率窗口（包 65 / 73 共用）。</summary>
        public readonly TeleportWindow Teleports = new();
    }

    /// <summary>固定窗口计数器：窗口过期即重置。仅在持有玩家状态锁时调用。</summary>
    private sealed class TeleportWindow
    {
        private DateTimeOffset _windowStart;
        private int _count;

        public bool TryConsume(DateTimeOffset now, int limit)
        {
            if (_windowStart == default || now - _windowStart >= TimeSpan.FromSeconds(1))
            {
                _windowStart = now;
                _count = 0;
            }
            if (_count >= limit) return false;
            _count++;
            return true;
        }
    }
}

// ---------- 战斗权威 ----------

internal sealed class CombatAuthority : ICombatAuthority
{
    /// <summary>服务端基础伤害。武器/护甲/暴击修正由仿真层的装备数据叠加（本层不持有武器模型）。</summary>
    private const int DefaultBaseDamage = 10;

    private readonly IPlayerAuthority _players;
    private readonly IAuditLogger _audit;
    private readonly CombatLimits _limits;
    private readonly ConcurrentDictionary<int, DamageWindow> _windows = new();

    public CombatAuthority(IPlayerAuthority players, IAuditLogger audit, CombatLimits limits)
        => (_players, _audit, _limits) = (players, audit, limits);

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands)
    {
        if (packet is not NpcStrikePacket strike)
            return AuthorityResult.Accept(packet);

        if (strike.NpcId < 0 || strike.Damage < 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "strike_rejected", "invalid_strike",
                new { strike.NpcId, strike.Damage }));
            return AuthorityResult.Reject("invalid_strike");
        }

        // 单次伤害上限：客户端上报的伤害绝不采信
        if (strike.Damage > _limits.MaxSingleDamage)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "strike_rejected", "damage_exceeded",
                new { strike.Damage, Max = _limits.MaxSingleDamage }));
            return AuthorityResult.Reject("damage_exceeded");
        }

        // 统计窗口内伤害总量上限（DPS 约束）
        var window = _windows.GetOrAdd(playerId, _ => new DamageWindow());
        lock (window)
        {
            window.Prune(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_limits.MaxDpsWindowSeconds));
            window.Add(strike.Damage);
            if (window.Total > _limits.MaxDps)
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "strike_rejected", "dps_exceeded",
                    new { window.Total, Max = _limits.MaxDps, WindowSeconds = _limits.MaxDpsWindowSeconds }));
                return AuthorityResult.Reject("dps_exceeded");
            }
        }

        return AuthorityResult.Accept(packet);
    }

    public int ComputeDamage(int playerId, int targetId) => DefaultBaseDamage;

    private sealed class DamageWindow
    {
        private readonly Queue<(DateTimeOffset At, int Damage)> _hits = new();

        public int Total { get; private set; }

        public void Add(int damage)
        {
            _hits.Enqueue((DateTimeOffset.UtcNow, damage));
            Total += damage;
        }

        public void Prune(DateTimeOffset now, TimeSpan window)
        {
            while (_hits.Count > 0 && now - _hits.Peek().At > window)
                Total -= _hits.Dequeue().Damage;
        }
    }
}

// ---------- 库存权威 ----------

internal sealed class InventoryAuthority : IInventoryAuthority
{
    /// <summary>Terraria 物品表规模上限（1.4 约 5000+，留余量）。</summary>
    private const int MaxItemId = 6000;

    /// <summary>世界图格宽高（与 TileAuthority 保持一致）。</summary>
    private const int WorldWidthTiles = 8400;
    private const int WorldHeightTiles = 2400;

    private readonly IAuditLogger _audit;
    private readonly InventoryLimits _limits;
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, SlotState>> _inventories = new();

    public InventoryAuthority(IAuditLogger audit, InventoryLimits limits) => (_audit, _limits) = (audit, limits);

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands) => packet switch
    {
        ItemDropPacket drop => ValidateDrop(drop, playerId),
        ChestPacket chest => ValidateChest(chest, playerId),
        InventorySlotPacket slot => ValidateSlot(slot, playerId),
        _ => AuthorityResult.Accept(packet),
    };

    private AuthorityResult ValidateDrop(ItemDropPacket drop, int playerId)
    {
        if (!IsValidItem(drop.ItemId))
            return Deny(playerId, "drop_rejected", "unknown_item", new { drop.ItemId });
        if (drop.Stack <= 0 || drop.Stack > _limits.MaxStackSize)
            return Deny(playerId, "drop_rejected", "invalid_stack", new { drop.Stack, Max = _limits.MaxStackSize });
        return AuthorityResult.Accept(drop);
    }

    private AuthorityResult ValidateChest(ChestPacket chest, int playerId)
    {
        if (chest.X < 0 || chest.X >= WorldWidthTiles || chest.Y < 0 || chest.Y >= WorldHeightTiles)
            return Deny(playerId, "chest_rejected", "out_of_bounds", new { chest.X, chest.Y });
        return AuthorityResult.Accept(chest);
    }

    private AuthorityResult ValidateSlot(InventorySlotPacket slot, int playerId)
    {
        if (slot.Slot < 0 || slot.Slot >= InventoryLimits.MaxSlots)
            return Deny(playerId, "slot_rejected", "invalid_slot", new { slot.Slot });
        if (slot.Stack < 0 || slot.Stack > _limits.MaxStackSize)
            return Deny(playerId, "slot_rejected", "invalid_stack", new { slot.Stack, Max = _limits.MaxStackSize });
        // 空槽（Stack==0）允许任意 ItemId（用于清空），非空槽必须是已知物品
        if (slot.Stack > 0 && !IsValidItem(slot.ItemId))
            return Deny(playerId, "slot_rejected", "unknown_item", new { slot.ItemId });

        // SSC：服务端为唯一真相源，客户端上报仅用于对账
        if (_limits.SscEnabled)
        {
            var inv = _inventories.GetOrAdd(playerId, _ => new ConcurrentDictionary<int, SlotState>());
            inv[slot.Slot] = new SlotState(slot.ItemId, slot.Stack);
        }
        return AuthorityResult.Accept(slot);
    }

    private AuthorityResult Deny(int playerId, string action, string reason, object details)
    {
        _audit.Log(AuditEvent.Now(playerId, "authority", action, reason, details));
        return AuthorityResult.Reject(reason);
    }

    public bool IsValidItem(int itemId) => itemId >= 0 && itemId < MaxItemId;

    public int GetStackCount(int playerId, int slot)
        => _inventories.TryGetValue(playerId, out var inv) && inv.TryGetValue(slot, out var s) ? s.Stack : 0;

    public bool HasItem(int playerId, int itemId)
    {
        if (!_inventories.TryGetValue(playerId, out var inv)) return false;
        // 遍历所有槽位找 itemId 匹配且 stack > 0
        foreach (var kv in inv)
            if (kv.Value.ItemId == itemId && kv.Value.Stack > 0)
                return true;
        return false;
    }

    public void ApplyAuthorizedChange(int playerId, int slot, int delta)
    {
        if (slot < 0 || slot >= InventoryLimits.MaxSlots) return;
        var inv = _inventories.GetOrAdd(playerId, _ => new ConcurrentDictionary<int, SlotState>());
        inv.AddOrUpdate(
            slot,
            _ => new SlotState(ItemId: 0, Stack: Math.Clamp(delta, 0, _limits.MaxStackSize)),
            (_, cur) => new SlotState(cur.ItemId, Math.Clamp(cur.Stack + delta, 0, _limits.MaxStackSize)));
    }

    private readonly record struct SlotState(int ItemId, int Stack);
}

// ---------- 世界权威 ----------

internal sealed class WorldAuthority : IWorldAuthority
{
    // Terraria 原版玩家可挖半径（像素）。tile 坐标到玩家像素坐标的直线距离，
    // 超过此值客户端也无法操作，但 CE 可以发任意坐标。
    private const int DigReachPx = 160;
    private const int PlaceReachPx = 180;

    private readonly IPlayerAuthority _players;
    private readonly IAuditLogger _audit;
    private readonly WorldLimits _limits;
    private readonly WorldState _world;
    private readonly IInventoryAuthority _inv;

    public WorldAuthority(IPlayerAuthority players, IAuditLogger audit, WorldLimits limits, WorldState world, IInventoryAuthority inv)
        => (_players, _audit, _limits, _world, _inv) = (players, audit, limits, world, inv);

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands) => packet switch
    {
        TileBreakPacket brk => ValidateBreak(brk, playerId),
        TilePlacePacket place => ValidatePlace(place, playerId),
        _ => AuthorityResult.Accept(packet),
    };

    private AuthorityResult ValidateBreak(TileBreakPacket brk, int playerId)
    {
        // 越界检查：坐标必须落在世界尺寸内
        if (!IsInWorld(brk.X, brk.Y))
            return Deny(playerId, "tile_rejected", "out_of_bounds", new { brk.X, brk.Y });

        // 玩家可达距离：CE 伪造远距离坐标 → 直接拒绝
        if (!IsWithinReach(playerId, brk.X, brk.Y, DigReachPx))
            return Deny(playerId, "tile_rejected", "out_of_reach", new { brk.X, brk.Y });

        // 目标 tile 必须存在且为实心（Action=0 挖实心砖；2/3 挖墙；>=5 电线/斜坡类跳过实体检查）
        // brk.Action=0 → 实心砖；TileType=客户端声称的类型，服务端需对账
        var tile = _world.Tiles[brk.X, brk.Y];
        if (brk.Action == 0)
        {
            if (!tile.Active)
                return Deny(playerId, "tile_rejected", "tile_not_found", new { brk.X, brk.Y });
            // 类型对账：客户端声称挖 TileType，服务端实际是 tile.Type，不一致视为篡改
            if (brk.TileType >= 0 && brk.TileType != tile.Type)
                return Deny(playerId, "tile_rejected", "tile_type_mismatch",
                    new { Client = brk.TileType, Server = tile.Type });
        }

        return AuthorityResult.Accept(brk);
    }

    private AuthorityResult ValidatePlace(TilePlacePacket place, int playerId)
    {
        // 越界检查
        if (!IsInWorld(place.X, place.Y))
            return Deny(playerId, "tile_rejected", "out_of_bounds", new { place.X, place.Y });

        // 玩家可达距离
        if (!IsWithinReach(playerId, place.X, place.Y, PlaceReachPx))
            return Deny(playerId, "tile_rejected", "out_of_reach", new { place.X, place.Y });

        // tile 类型合法性：Terraria 有效砖类型 0..556（1.4.5.8），负数或超上限拒
        if (place.TileType < 0 || place.TileType > 556)
            return Deny(playerId, "tile_rejected", "invalid_tile_type", new { place.TileType });

        // 背包物品校验：Terraria tile/item 同 ID，放砖需背包至少有 1 个对应物品。
        // 客户端正常流程会自动扣 stack；CE 空放（背包无此物品）或放非背包物品 → 拒绝。
        // 如果 InventoryAuthority 尚未收到 InventorySlotPacket（玩家没同步过背包），
        // _inventories 里查不到 playerId → HasItem 返回 false → 拒绝。这种情况需要客户端先同步背包。
        if (!_inv.HasItem(playerId, place.TileType))
            return Deny(playerId, "tile_rejected", "item_not_in_inventory",
                new { place.TileType, Reason = "backpack missing item or not synced yet" });

        // 放置目标必须为空：已有 Active 砖 → 客户端正常流程不会发，CE 伪造直接拒
        var tile = _world.Tiles[place.X, place.Y];
        if (tile.Active)
            return Deny(playerId, "tile_rejected", "tile_already_exists", new { place.X, place.Y });

        return AuthorityResult.Accept(place);
    }

    private AuthorityResult Deny(int playerId, string action, string reason, object details)
    {
        _audit.Log(AuditEvent.Now(playerId, "authority", action, reason, details));
        return AuthorityResult.Reject(reason);
    }

    private bool IsInWorld(int x, int y)
        => x >= 0 && x < _world.MaxTilesX && y >= 0 && y < _world.MaxTilesY;

    private bool IsWithinReach(int playerId, int tileX, int tileY, int reachPx)
    {
        if (!_world.Players.TryGetValue(playerId, out var player))
            return false; // 玩家未在权威状态 → 不允许操作
        var tileCenterX = tileX * 16 + 8;
        var tileCenterY = tileY * 16 + 8;
        var dx = player.Position.X - tileCenterX;
        var dy = player.Position.Y - tileCenterY;
        return dx * dx + dy * dy <= reachPx * reachPx;
    }

    /// <summary>公开接口：坐标落在世界范围内。</summary>
    public bool CanPlayerModifyTile(int playerId, int x, int y) => IsInWorld(x, y);

    public int GetTileBreakThreshold(int playerId) => _limits.MaxTileBreakPerSecond;
}

// ---------- 限流权威 ----------

internal sealed class RateAuthority : IRateAuthority
{
    private readonly RateLimits _limits;
    private readonly IAuditLogger _audit;
    private readonly ConcurrentDictionary<int, PlayerRateState> _states = new();

    public RateAuthority(RateLimits limits, IAuditLogger audit) => (_limits, _audit) = (limits, audit);

    public AuthorityResult Check(IPacketContext context, PacketId packetType)
    {
        var state = _states.GetOrAdd(context.PlayerId, _ => new PlayerRateState());
        var now = context.ReceivedAt == default ? DateTimeOffset.UtcNow : context.ReceivedAt;

        lock (state)
        {
            // 全局包速率：packet flood / DoS 防护
            if (!state.Global.TryConsume(now, _limits.MaxPacketsPerSecond, TimeSpan.FromSeconds(1)))
                return Deny(context, packetType, "packet_rate_exceeded", _limits.MaxPacketsPerSecond);

            switch (packetType)
            {
                case PacketId.TileBreak:
                    if (!state.TileBreak.TryConsume(now, _limits.MaxTileBreakPerSecond, TimeSpan.FromSeconds(1)))
                        return Deny(context, packetType, "tile_break_rate_exceeded", _limits.MaxTileBreakPerSecond);
                    break;

                case PacketId.TilePlace:
                    if (!state.TilePlace.TryConsume(now, _limits.MaxTilePlacePerSecond, TimeSpan.FromSeconds(1)))
                        return Deny(context, packetType, "tile_place_rate_exceeded", _limits.MaxTilePlacePerSecond);
                    break;

                case PacketId.ProjectileNew:
                    if (!state.Projectile.TryConsume(now, _limits.MaxProjectilesPerSecond, TimeSpan.FromSeconds(1)))
                        return Deny(context, packetType, "projectile_rate_exceeded", _limits.MaxProjectilesPerSecond);
                    break;

                case PacketId.ChatText:
                    if (!state.Chat.TryConsume(now, _limits.MaxChatPerMinute, TimeSpan.FromMinutes(1)))
                        return Deny(context, packetType, "chat_rate_exceeded", _limits.MaxChatPerMinute);
                    break;
            }
        }

        return AuthorityResult.Accept(null);
    }

    private AuthorityResult Deny(IPacketContext context, PacketId packetType, string reason, int limit)
    {
        _audit.Log(AuditEvent.Now(context.PlayerId, "rate", "packet_dropped", reason,
            new { Packet = packetType.ToString(), Limit = limit }));
        return AuthorityResult.Reject(reason);
    }

    /// <summary>固定窗口计数器：窗口过期即重置。仅在持锁状态下调用。</summary>
    private struct WindowCounter
    {
        private DateTimeOffset _windowStart;
        private int _count;

        public bool TryConsume(DateTimeOffset now, int limit, TimeSpan window)
        {
            if (_windowStart == default || now - _windowStart >= window)
            {
                _windowStart = now;
                _count = 0;
            }
            if (_count >= limit) return false;
            _count++;
            return true;
        }
    }

    private sealed class PlayerRateState
    {
        public WindowCounter Global;
        public WindowCounter TileBreak;
        public WindowCounter TilePlace;
        public WindowCounter Projectile;
        public WindowCounter Chat;
    }
}
