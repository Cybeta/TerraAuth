// TerraAuth — Phase 2: 六个权威子系统实现
// 架构 §4.2 / §5：所有 Validate 方法返回 Accept/Reject/Correct，绝不默默信任客户端

using System.Collections.Concurrent;
using TerraAuth.Protocol;
using TerraAuth.Simulation;

namespace TerraAuth.Authority;

// ---------- 阈值配置 ----------
// 由 ServerConfig 映射注入（架构 §4.5 唯一来源），使 Authority 层不反向依赖 Config 层。
// 全部为不可变 record（引用类型）：热更新时由 AuthorityEnforcers.UpdateThresholds 整体替换实例，
// 子系统以 volatile 字段持有 —— 读取端无锁、无撕裂，故此处不能用 record struct。

public sealed record PlayerLimits(int MaxHp, int MaxMana)
{
    public static PlayerLimits Default => new(MaxHp: 500, MaxMana: 200);
}

/// <summary>移动校验阈值。</summary>
/// <param name="MaxSpeed">水平 / 上行的最大速度（像素/帧）。</param>
/// <param name="TeleportTolerance">单帧允许的额外容差（像素）。</param>
/// <param name="MaxTeleportsPerSecond">每秒传送次数上限。</param>
/// <param name="MaxFallSpeed">
/// 垂直**向下**的最大速度（像素/帧）。原版下落终速（<c>MaxFallSpeed</c> ≈ 20）远高于水平速度，
/// 若按水平上限判定，任何一次正常坠落都会被判超速 → 服务端位置不再更新且累计违规踢人。
/// </param>
public sealed record MovementLimits(
    float MaxSpeed,
    float TeleportTolerance,
    int MaxTeleportsPerSecond = 5,
    float MaxFallSpeed = 20.0f)
{
    /// <summary>兜底默认值（与 ServerConfig 默认一致）。</summary>
    public static MovementLimits Default => new(MaxSpeed: 8.0f, TeleportTolerance: 4.0f, MaxTeleportsPerSecond: 5);
}

/// <summary>战斗校验阈值：单次伤害上限 + 统计窗口内的伤害总量上限。</summary>
public sealed record CombatLimits(int MaxSingleDamage, int MaxDpsWindowSeconds, int MaxDps)
{
    public static CombatLimits Default => new(MaxSingleDamage: 30000, MaxDpsWindowSeconds: 5, MaxDps: 50000);
}

/// <summary>库存校验阈值。SscEnabled=true 时服务端持有唯一真相源。</summary>
public sealed record InventoryLimits(bool SscEnabled, int MaxStackSize)
{
    /// <summary>Terraria 玩家背包槽位数（0..58，含装备/饰品/坐骑槽）。</summary>
    public const int MaxSlots = 59;

    public static InventoryLimits Default => new(SscEnabled: true, MaxStackSize: 999);
}

/// <summary>世界交互阈值：每秒挖砖/放砖上限。</summary>
public sealed record WorldLimits(int MaxTileBreakPerSecond, int MaxTilePlacePerSecond)
{
    public static WorldLimits Default => new(MaxTileBreakPerSecond: 60, MaxTilePlacePerSecond: 40);
}

// ---------- 玩家属性权威 ----------

internal sealed class PlayerAuthority : IPlayerAuthority
{
    /// <summary>增益槽位上限（与原版玩家增益数组长度一致）。</summary>
    private const int MaxBuffSlots = 44;

    /// <summary>有效增益 ID 区间（0 表示无增益；上限为原版增益总数 - 1）。</summary>
    private const int MinBuffId = 1;
    private const int MaxBuffId = 400;

    private readonly IAuditLogger _audit;
    private volatile PlayerLimits _limits;
    private readonly ConcurrentDictionary<int, PlayerStats> _stats = new();

    public PlayerAuthority(IAuditLogger audit, PlayerLimits limits) => (_audit, _limits) = (audit, limits);

    /// <summary>热更新阈值（引用整体替换，读取端无锁）。</summary>
    internal void UpdateLimits(PlayerLimits limits) => _limits = limits;

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands)
    {
        return packet switch
        {
            PlayerHealthPacket hp => ValidateHealth(hp, playerId),
            PlayerManaPacket mana => ValidateMana(mana, playerId),
            PlayerHealPacket heal => ValidateHeal(heal, playerId),
            PlayerBuffsPacket buffs => ValidateBuffs(buffs, playerId),
            PlayerHurtV2Packet hurt => ValidateHurt(hurt, playerId),
            PlayerDeathV2Packet death => ValidateDeath(death, playerId),
            _ => AuthorityResult.Accept(packet),
        };
    }

    private AuthorityResult ValidateHealth(PlayerHealthPacket hp, int playerId)
    {
        // 基础合法性：负血量 / 非正上限直接拒绝
        if (hp.Hp < 0 || hp.MaxHp <= 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "health_rejected", "invalid_health",
                new { hp.Hp, hp.MaxHp }));
            return AuthorityResult.Reject("invalid_health");
        }

        var state = _stats.GetOrAdd(playerId, _ => new PlayerStats(_limits.MaxHp));
        lock (state.Gate)
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
            return AuthorityResult.Accept(hp);
        }
    }

    /// <summary>
    /// 玩家法力（包 42）：此层只做协议边界校验。会话内最终法力由仿真层
    /// <see cref="PlayerRuntime"/> 持有，避免 Authority 与 WorldState 维护两份可偏离的状态。
    /// </summary>
    private AuthorityResult ValidateMana(PlayerManaPacket mana, int playerId)
    {
        if (mana.Mana < 0 || mana.MaxMana <= 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "mana_rejected", "invalid_mana",
                new { mana.Mana, mana.MaxMana }));
            return AuthorityResult.Reject("invalid_mana");
        }

        var limits = _limits;
        if (mana.MaxMana > limits.MaxMana)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "mana_corrected", "max_mana_exceeded",
                new { ClientMaxMana = mana.MaxMana, ServerMaxMana = limits.MaxMana }));
            return AuthorityResult.Correct(
                new PlayerManaPacket(playerId, Math.Min(mana.Mana, limits.MaxMana), limits.MaxMana),
                "max_mana_exceeded");
        }

        if (mana.Mana > mana.MaxMana)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "mana_corrected", "mana_exceeded",
                new { ClientMana = mana.Mana, mana.MaxMana }));
            return AuthorityResult.Correct(
                new PlayerManaPacket(playerId, mana.MaxMana, mana.MaxMana),
                "mana_exceeded");
        }

        return AuthorityResult.Accept(mana);
    }

    /// <summary>
    /// 玩家治疗（包 35）：原版由客户端声明治疗量，此处只做非负校验；
    /// 真正的回血上限由仿真层 HealPlayerCommand 钳制到服务端 HpMax（服务端生命不越界）。
    /// </summary>
    private AuthorityResult ValidateHeal(PlayerHealPacket heal, int playerId)
    {
        if (heal.Amount < 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "heal_rejected", "invalid_heal",
                new { heal.Amount }));
            return AuthorityResult.Reject("invalid_heal");
        }
        return AuthorityResult.Accept(heal);
    }

    /// <summary>
    /// 玩家增益（包 50）：服务端持有增益列表唯一真相。
    /// 校验条目数不超过原版增益槽位数（44）且每个增益 ID 落在有效区间（1..400）。
    /// </summary>
    private AuthorityResult ValidateBuffs(PlayerBuffsPacket buffs, int playerId)
    {
        if (buffs.BuffTypes.Count > MaxBuffSlots)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "buffs_rejected", "too_many_buffs",
                new { Count = buffs.BuffTypes.Count, Max = MaxBuffSlots }));
            return AuthorityResult.Reject("too_many_buffs");
        }

        foreach (var buffId in buffs.BuffTypes)
        {
            if (buffId < MinBuffId || buffId > MaxBuffId)
            {
                _audit.Log(AuditEvent.Now(playerId, "authority", "buffs_rejected", "invalid_buff",
                    new { BuffId = buffId }));
                return AuthorityResult.Reject("invalid_buff");
            }
        }

        return AuthorityResult.Accept(buffs);
    }

    /// <summary>
    /// 玩家受伤（包 117）：客户端伤害报告仅做非负值校验，服务端不据此扣血。
    /// 负伤害等价于治疗（CE 改血的方向之一），直接拒绝。
    /// </summary>
    private AuthorityResult ValidateHurt(PlayerHurtV2Packet hurt, int playerId)
    {
        if (hurt.Damage < 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "hurt_rejected", "invalid_damage",
                new { hurt.Damage }));
            return AuthorityResult.Reject("invalid_damage");
        }
        return AuthorityResult.Accept(hurt);
    }

    /// <summary>玩家死亡（包 118）：伤害非负即可，死亡状态由仿真层结算。</summary>
    private AuthorityResult ValidateDeath(PlayerDeathV2Packet death, int playerId)
    {
        if (death.Damage < 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "death_rejected", "invalid_damage",
                new { death.Damage }));
            return AuthorityResult.Reject("invalid_damage");
        }
        return AuthorityResult.Accept(death);
    }

    public int GetMaxHp(int playerId) => _stats.TryGetValue(playerId, out var s) ? s.MaxHp : _limits.MaxHp;
    public int GetMaxMana(int playerId) => _limits.MaxMana;

    private sealed class PlayerStats
    {
        /// <summary>每玩家独占的轻量锁（.NET 9+ Lock）。</summary>
        public readonly Lock Gate = new();
        public int Hp;
        public int MaxHp;

        public PlayerStats(int maxHp)
        {
            Hp = maxHp;
            MaxHp = maxHp;
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

    // 「可疑带」倍数：见 Validate 中的说明 —— 匀速度上限 × 该倍数以内一律放行（原版击退 / 挤出方块等合法大位移）。
    private const float SuspiciousBandMultiplier = 4f;

    // 世界尺寸与实体索引上限（与 Main.player[255] / Main.npc[200] / 大型世界一致）
    private const int WorldWidthTiles = 8400;
    private const int WorldHeightTiles = 2400;
    private const int TileSizePixels = 16;
    private const int MaxPlayerIndex = 254;
    private const int MaxNpcIndex = 199;

    private readonly IPlayerAuthority _players;
    private readonly IAuditLogger _audit;
    private volatile MovementLimits _limits;
    private readonly ConcurrentDictionary<int, PlayerMotion> _motion = new();

    public MovementAuthority(IPlayerAuthority players, IAuditLogger audit, MovementLimits limits)
        => (_players, _audit, _limits) = (players, audit, limits);

    /// <summary>热更新阈值（引用整体替换，读取端无锁）。</summary>
    internal void UpdateLimits(MovementLimits limits) => _limits = limits;

    /// <summary>连接结束：丢弃该玩家的移动基线，使重连后的首个位置包重新建立基准。</summary>
    public void ResetPlayer(int playerId) => _motion.TryRemove(playerId, out _);

    public void BindSession(int playerId, long sessionId)
    {
        if (sessionId == 0) return;
        var state = _motion.GetOrAdd(playerId, _ => new PlayerMotion());
        lock (state.Gate)
        {
            if (state.SessionId == 0)
                state.SessionId = sessionId;
        }
    }

    public void ResetPlayer(int playerId, long sessionId)
    {
        if (!_motion.TryGetValue(playerId, out var state)) return;
        if (sessionId == 0)
        {
            _motion.TryRemove(playerId, out _);
            return;
        }

        lock (state.Gate)
        {
            if (state.SessionId == sessionId)
                _motion.TryRemove(playerId, out _);
        }
    }

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

        lock (state.Gate)
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

            // 分轴判定：水平用 MaxSpeed，**垂直用 max(MaxSpeed, MaxFallSpeed)**（上行 / 下落都算）。
            // 若垂直也按水平上限，原版正常坠落（终速 ≈20 px/帧）会被误判超速 → 服务端位置
            // 不再更新（后续挖 / 放 / 交互全部 out_of_reach）且累计违规被踢。
            var verticalSpeed = MathF.Max(maxSpeed, _limits.MaxFallSpeed);
            var horizontal = maxSpeed * FramesPerSecond * (float)dt + _limits.TeleportTolerance;
            var vertical = verticalSpeed * FramesPerSecond * (float)dt + _limits.TeleportTolerance;

            // 「可疑带」：匀速度上限不是硬边界 —— 原版客户端在**受击击退 / 被挤出方块 / 斜坡校正**
            // 时会出现短促的大位移（一帧十几到几十像素），这类位移合法但超过匀速上限。
            // 因此超过上限在 `SuspiciousBandMultiplier` 倍以内一律放行（只记审计），
            // 只有远超量级的才是真瞬移 → 拒绝。宁可漏判一点，也不要误杀正常玩家
            // （真机症状：站着不动被史莱姆打一下、或走下台阶就刷 speed_exceeded）。
            if (MathF.Abs(dx) > horizontal * SuspiciousBandMultiplier ||
                MathF.Abs(dy) > vertical * SuspiciousBandMultiplier)
            {
                // 拒绝时保持权威基准不变：瞬移包不得污染服务端位置
                var detail =
                    $"dx={dx:F1} dy={dy:F1} 允许=({horizontal:F1},{vertical:F1}) dt={dt:F3}s 上限=({maxSpeed},{verticalSpeed})";
                _audit.Log(AuditEvent.Now(playerId, "authority", "position_rejected", "speed_exceeded",
                    new { Dx = dx, Dy = dy, AllowedX = horizontal, AllowedY = vertical, MaxSpeed = maxSpeed, Dt = dt }));
                return AuthorityResult.Reject("speed_exceeded", detail: detail);
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

        lock (state.Gate)
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

        lock (state.Gate)
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
        /// <summary>每玩家独占的轻量锁（.NET 9+ Lock）。</summary>
        public readonly Lock Gate = new();
        public Vector2 LastPosition;
        public DateTimeOffset LastSeenAt;
        public bool HasBaseline;
        public long SessionId;

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
    private volatile CombatLimits _limits;
    private readonly ConcurrentDictionary<int, DamageWindow> _windows = new();

    public CombatAuthority(IPlayerAuthority players, IAuditLogger audit, CombatLimits limits)
        => (_players, _audit, _limits) = (players, audit, limits);

    /// <summary>热更新阈值（引用整体替换，读取端无锁）。</summary>
    internal void UpdateLimits(CombatLimits limits) => _limits = limits;

    /// <summary>有效弹幕类型区间（0 表示无弹幕；上限为原版弹幕总数 - 1）。</summary>
    private const int MinProjectileType = 1;
    private const int MaxProjectileType = 1135;

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands)
    {
        return packet switch
        {
            ProjectileNewPacket proj => ValidateProjectile(proj, playerId),
            NpcStrikePacket strike => ValidateStrike(strike, playerId),
            _ => AuthorityResult.Accept(packet),
        };
    }

    /// <summary>
    /// 弹幕生成（包 27）：服务端登记实体并做基础校验。
    /// 类型须落在有效区间；伤害由客户端声明，超过配置的单次伤害上限即判为作弊并拒绝
    /// （与包 28 共用同一阈值；原版伤害由武器 / 装备推导，此处为简化上界模型）。
    /// </summary>
    private AuthorityResult ValidateProjectile(ProjectileNewPacket proj, int playerId)
    {
        if (proj.ProjectileKey < 0)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "projectile_rejected", "invalid_projectile_key",
                new { proj.ProjectileKey }));
            return AuthorityResult.Reject("invalid_projectile_key");
        }

        if (proj.ProjectileType < MinProjectileType || proj.ProjectileType > MaxProjectileType)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "projectile_rejected", "invalid_projectile_type",
                new { proj.ProjectileType }));
            return AuthorityResult.Reject("invalid_projectile_type");
        }

        if (proj.Damage < 0 || proj.Damage > _limits.MaxSingleDamage)
        {
            _audit.Log(AuditEvent.Now(playerId, "authority", "projectile_rejected", "projectile_damage_exceeded",
                new { proj.Damage, Max = _limits.MaxSingleDamage }));
            return AuthorityResult.Reject("projectile_damage_exceeded");
        }

        return AuthorityResult.Accept(proj);
    }

    /// <summary>NPC 受击（包 28）：单次伤害上限 + 统计窗口内 DPS 上限。</summary>
    private AuthorityResult ValidateStrike(NpcStrikePacket strike, int playerId)
    {
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
        lock (window.Gate)
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

        return AuthorityResult.Accept(strike);
    }

    public int ComputeDamage(int playerId, int targetId) => DefaultBaseDamage;

    private sealed class DamageWindow
    {
        /// <summary>每玩家独占的轻量锁（.NET 9+ Lock）。</summary>
        public readonly Lock Gate = new();
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

internal sealed class InventoryAuthority : IInventoryAuthority, IInventoryLedger
{
    /// <summary>Terraria 物品表规模上限（1.4 约 5000+，留余量）。</summary>
    private const int MaxItemId = 6000;

    private readonly IAuditLogger _audit;
    private readonly WorldState _world;
    private volatile InventoryLimits _limits;

    public InventoryAuthority(IAuditLogger audit, InventoryLimits limits, WorldState world)
        => (_audit, _limits, _world) = (audit, limits, world);

    /// <summary>热更新阈值（引用整体替换，读取端无锁）。</summary>
    internal void UpdateLimits(InventoryLimits limits) => _limits = limits;

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands) => packet switch
    {
        ItemDropPacket drop => ValidateDrop(drop, playerId),
        InventorySlotPacket slot => ValidateSlot(slot, playerId),
        SyncChestItemPacket chestItem => ValidateChestItem(chestItem, playerId),
        _ => AuthorityResult.Accept(packet),   // 箱子会话、距离由 WorldAuthority 校验（需世界数据）
    };

    public int MaxStackSize => _limits.MaxStackSize;

    private AuthorityResult ValidateDrop(ItemDropPacket drop, int playerId)
    {
        if (!IsValidItem(drop.ItemId))
            return Deny(playerId, "drop_rejected", "unknown_item", new { drop.ItemId });
        if (drop.Stack <= 0 || drop.Stack > _limits.MaxStackSize)
            return Deny(playerId, "drop_rejected", "invalid_stack", new { drop.Stack, Max = _limits.MaxStackSize });
        return AuthorityResult.Accept(drop);
    }

    private AuthorityResult ValidateChestItem(SyncChestItemPacket chestItem, int playerId)
    {
        if (chestItem.Stack < 0 || chestItem.Stack > _limits.MaxStackSize)
            return Deny(playerId, "chest_item_rejected", "invalid_stack", new { chestItem.Stack, Max = _limits.MaxStackSize });
        if (chestItem.Stack > 0 && !IsValidItem(chestItem.ItemType))
            return Deny(playerId, "chest_item_rejected", "unknown_item", new { chestItem.ItemType });
        if (_limits.SscEnabled)
            return Deny(playerId, "chest_item_rejected", "chest_snapshot_forbidden", new { chestItem.ChestIndex, chestItem.ItemSlot });

        return AuthorityResult.Accept(chestItem);
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

        // SSC：客户端槽位快照不能覆盖服务端权威背包；物品变更只能来自服务端受控事件。
        if (_limits.SscEnabled)
            return Deny(playerId, "slot_rejected", "inventory_snapshot_forbidden", new { slot.Slot });

        return AuthorityResult.Accept(slot);
    }

    private AuthorityResult Deny(int playerId, string action, string reason, object details)
    {
        _audit.Log(AuditEvent.Now(playerId, "authority", action, reason, details));
        return AuthorityResult.Reject(reason);
    }

    public bool IsValidItem(int itemId) => itemId >= 0 && itemId < MaxItemId;

    public int GetStackCount(int playerId, int slot)
    {
        if (slot < 0 || slot >= InventoryLimits.MaxSlots) return 0;
        lock (_world.PlayersLock)
            return _world.Players.TryGetValue(playerId, out var player) ? player.ItemStacks[slot] : 0;
    }

    public bool HasItem(int playerId, int itemId)
    {
        lock (_world.PlayersLock)
        {
            if (!_world.Players.TryGetValue(playerId, out var player)) return false;
            for (int slot = 0; slot < PlayerRuntime.InventorySlotCount; slot++)
                if (player.Items[slot] == itemId && player.ItemStacks[slot] > 0)
                    return true;
            return false;
        }
    }

    public bool ConsumeItem(int playerId, int itemId)
    {
        lock (_world.PlayersLock)
        {
            if (!_world.Players.TryGetValue(playerId, out var player)) return false;
            for (int slot = 0; slot < PlayerRuntime.InventorySlotCount; slot++)
            {
                if (player.Items[slot] != itemId || player.ItemStacks[slot] <= 0) continue;
                if (--player.ItemStacks[slot] == 0)
                {
                    player.Items[slot] = 0;
                    player.ItemPrefixes[slot] = 0;
                }
                player.RecalculateDefense();
                _world.MarkInventoryChanged(playerId, player.SessionId, slot);
                return true;
            }
            return false;
        }
    }

    public bool TryAddItem(int playerId, int itemId, int stack)
        => TryAddItemInternal(playerId, itemId, stack, requireExact: false);

    public bool TryAddItemExactly(int playerId, int itemId, int stack)
        => TryAddItemInternal(playerId, itemId, stack, requireExact: true);

    private bool TryAddItemInternal(int playerId, int itemId, int stack, bool requireExact)
    {
        if (!IsValidItem(itemId) || stack <= 0) return false;

        lock (_world.PlayersLock)
        {
            if (!_world.Players.TryGetValue(playerId, out var player)) return false;

            var remaining = stack;
            var changes = new List<(int Slot, int Stack)>();
            for (int slot = 0; slot < PlayerRuntime.InventorySlotCount && remaining > 0; slot++)
            {
                if (player.Items[slot] != itemId || player.ItemStacks[slot] <= 0 || player.ItemStacks[slot] >= _limits.MaxStackSize)
                    continue;

                var add = Math.Min(remaining, _limits.MaxStackSize - player.ItemStacks[slot]);
                changes.Add((slot, player.ItemStacks[slot] + add));
                remaining -= add;
            }

            for (int slot = 0; remaining > 0 && slot < PlayerRuntime.InventorySlotCount; slot++)
            {
                if (player.ItemStacks[slot] > 0) continue;
                var add = Math.Min(remaining, _limits.MaxStackSize);
                changes.Add((slot, add));
                remaining -= add;
            }

            if (requireExact && remaining > 0) return false;
            if (changes.Count == 0) return false;

            foreach (var (slot, newStack) in changes)
            {
                player.Items[slot] = itemId;
                player.ItemStacks[slot] = newStack;
                player.ItemPrefixes[slot] = 0;
                _world.MarkInventoryChanged(playerId, player.SessionId, slot);
            }
            player.RecalculateDefense();
            return true;
        }
    }

    public void ApplyAuthorizedChange(int playerId, int slot, int delta)
    {
        if (slot < 0 || slot >= InventoryLimits.MaxSlots) return;
        lock (_world.PlayersLock)
        {
            if (!_world.Players.TryGetValue(playerId, out var player)) return;
            var stack = Math.Clamp(player.ItemStacks[slot] + delta, 0, _limits.MaxStackSize);
            player.ItemStacks[slot] = stack;
            if (stack == 0)
            {
                player.Items[slot] = 0;
                player.ItemPrefixes[slot] = 0;
            }
            player.RecalculateDefense();
            _world.MarkInventoryChanged(playerId, player.SessionId, slot);
        }
    }
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
    private volatile WorldLimits _limits;
    private readonly WorldState _world;
    private readonly IInventoryAuthority _inv;

    public WorldAuthority(IPlayerAuthority players, IAuditLogger audit, WorldLimits limits, WorldState world, IInventoryAuthority inv)
        => (_players, _audit, _limits, _world, _inv) = (players, audit, limits, world, inv);

    /// <summary>热更新阈值（引用整体替换，读取端无锁）。</summary>
    internal void UpdateLimits(WorldLimits limits) => _limits = limits;

    public AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands)
        => Validate(packet, playerId, commands, 0);

    public AuthorityResult Validate(
        INetworkPacket packet,
        int playerId,
        CommandQueue commands,
        long sessionId) => packet switch
    {
        TileBreakPacket brk => ValidateBreak(brk, playerId),
        TilePlacePacket place => ValidatePlace(place, playerId),
        ItemPickupPacket pickup => ValidatePickup(pickup.ItemSlotIndex, playerId),
        ItemDestroyPacket destroy => ValidatePickup(destroy.ItemSlotIndex, playerId),
        ChestPacket chest => ValidateChestOpen(chest, playerId, sessionId),
        SyncChestItemPacket chestItem => ValidateChestItem(chestItem, playerId, sessionId),
        LiquidModulePacket liquid => ValidateLiquid(liquid, playerId),
        // 未建模包：拒绝（不进权威链路），但**不计违规** —— 正常原版客户端会发不少未建模包
        // （表情 / 家具 / 告示牌 / 部分 NetModule…），计入违规会让正常玩家被误踢。
        UnknownPacket => AuthorityResult.Reject("unknown_packet", countsAsViolation: false),
        _ => AuthorityResult.Accept(packet),
    };

    /// <summary>箱子交互最大距离（像素）。</summary>
    private const int ChestReachPx = 160;

    /// <summary>液体编辑最大距离（像素）。</summary>
    private const int LiquidReachPx = 160;

    /// <summary>单个液体帧允许的最大变更条目数（防超大帧）。</summary>
    private const int MaxLiquidChangesPerPacket = 128;

    /// <summary>
    /// 客户端液体编辑（包 82 模块 0）：逐条校验坐标越界 / 液体类型 / 玩家距离；
    /// 全部通过才接受（失败整帧拒绝，避免部分生效导致的状态漂移）。
    /// </summary>
    private AuthorityResult ValidateLiquid(LiquidModulePacket liquid, int playerId)
    {
        // 服务端下发形态不会进入入站管线；此处仅处理客户端上行
        if (!liquid.IsClientMessage || liquid.Changes.Count == 0)
            return AuthorityResult.Accept(liquid);

        if (liquid.Changes.Count > MaxLiquidChangesPerPacket)
            return Deny(playerId, "liquid_rejected", "too_many_changes",
                new { liquid.Changes.Count, Max = MaxLiquidChangesPerPacket });

        PlayerRuntime? player;
        lock (_world.PlayersLock)
            _world.Players.TryGetValue(playerId, out player);
        if (player is null || !player.Active || player.Dead)
            return Deny(playerId, "liquid_rejected", "player_not_active", new { playerId });

        foreach (var change in liquid.Changes)
        {
            if (!IsInWorld(change.X, change.Y))
                return Deny(playerId, "liquid_rejected", "out_of_bounds", new { change.X, change.Y });

            if (change.Type > 3)
                return Deny(playerId, "liquid_rejected", "invalid_liquid_type", new { change.Type });

            if (!IsWithinReach(playerId, change.X, change.Y, LiquidReachPx))
                return Deny(playerId, "liquid_rejected", "out_of_reach", new { change.X, change.Y });
        }

        return AuthorityResult.Accept(liquid);
    }

    /// <summary>
    /// 打开箱子（包 31）：坐标必须落在世界内、存在箱子、且玩家在交互距离内。
    /// 通过后由网络层把服务端持有的箱子内容逐槽下发（包 32）。
    /// </summary>
    private AuthorityResult ValidateChestOpen(
        ChestPacket chest,
        int playerId,
        long sessionId)
    {
        // 负坐标 = 关闭箱子请求（见 InboundPipeline 的命令映射）：只读校验阶段直接放行，
        // 会话关闭由 CloseChestCommand 在仿真提交阶段执行。
        if (chest.X < 0 || chest.Y < 0)
            return AuthorityResult.Accept(chest);

        if (!IsInWorld(chest.X, chest.Y))
            return Deny(playerId, "chest_rejected", "out_of_bounds", new { chest.X, chest.Y });

        if (!IsWithinReach(playerId, chest.X, chest.Y, ChestReachPx))
            return Deny(playerId, "chest_rejected", "out_of_reach", new { chest.X, chest.Y });

        int chestIndex;
        lock (_world.ChestsLock)
        {
            var existing = _world.FindChestAt(chest.X, chest.Y);
            if (existing is null)
                return Deny(playerId, "chest_rejected", "chest_not_found", new { chest.X, chest.Y });

            chestIndex = existing.Index;
        }

        return AuthorityResult.Accept(chest);
    }

    /// <summary>
    /// 箱子内物品写入（包 32）：箱子索引 / 槽位 / 堆叠 / 物品合法性 + 玩家在交互距离内。
    /// 通过后由 <see cref="SyncChestItemCommand"/> 落盘到服务端箱子（服务端持有唯一真相）。
    /// </summary>
    private AuthorityResult ValidateChestItem(
        SyncChestItemPacket item,
        int playerId,
        long sessionId)
    {
        if (item.Stack < 0 || item.Stack > _inv.MaxStackSize)
            return Deny(playerId, "chest_rejected", "invalid_stack",
                new { item.Stack, Max = _inv.MaxStackSize });

        // 空槽（Stack==0）允许任意物品 ID（用于清空），非空槽必须是已知物品
        if (item.Stack > 0 && !_inv.IsValidItem(item.ItemType))
            return Deny(playerId, "chest_rejected", "unknown_item", new { item.ItemType });

        Chest? chest;
        lock (_world.ChestsLock)
            chest = _world.FindChestByIndex(item.ChestIndex);
        if (chest is null)
            return Deny(playerId, "chest_rejected", "chest_not_found", new { item.ChestIndex });

        if (item.ItemSlot < 0 || item.ItemSlot >= chest.Items.Length)
            return Deny(playerId, "chest_rejected", "invalid_slot", new { item.ItemSlot });

        if (!IsWithinReach(playerId, chest.X, chest.Y, ChestReachPx))
            return Deny(playerId, "chest_rejected", "out_of_reach", new { item.ChestIndex });

        // 箱子会话由 OpenChestCommand 在仿真提交阶段建立；此处只做只读边界校验，
        // 最终会话一致性由 SyncChestItemCommand.Apply 再次确认，避免同一批包因提交尚未发生而被提前拒绝。
        return AuthorityResult.Accept(item);
    }

    /// <summary>玩家拾取掉落物的最大距离（像素）。原版拾取盒约 1 格 + 玩家半宽，此处放宽到 4 格。</summary>
    private const int PickupReachPx = 64;

    /// <summary>
    /// 掉落物拾取（包 22 / 包 151）：槽位必须对应真实存活的世界掉落物、玩家在线且在拾取半径内，
    /// 入库成功后才允许移除世界实体（背包已满则拒绝，世界实体保留）。
    /// </summary>
    private AuthorityResult ValidatePickup(int itemSlotIndex, int playerId)
    {
        PlayerRuntime? player;
        lock (_world.PlayersLock)
            _world.Players.TryGetValue(playerId, out player);
        if (player is null || !player.Active || player.Dead)
            return Deny(playerId, "pickup_rejected", "player_not_active", new { playerId });

        WorldItemEntity? item;
        lock (_world.ItemsLock)
            item = _world.Items.FirstOrDefault(i => i.Slot == itemSlotIndex && i.Active);
        if (item is null)
            return Deny(playerId, "pickup_rejected", "item_not_found", new { itemSlotIndex });

        // 拾取半径：CE 远程拾取 → 拒绝
        var dx = player.Position.X - item.Position.X;
        var dy = player.Position.Y - item.Position.Y;
        if (dx * dx + dy * dy > (float)PickupReachPx * PickupReachPx)
            return Deny(playerId, "pickup_rejected", "out_of_reach", new { itemSlotIndex });

        // 专属掉落物（OwnedBy ≥ 0，如 /give SSC 关闭时的 GiveItemByDrop 方案）：只准归属玩家拾取
        if (item.OwnedBy >= 0 && item.OwnedBy != playerId)
            return Deny(playerId, "pickup_rejected", "owned_by_other", new { itemSlotIndex });

        // 这里只做只读校验；库存入库与实体移除必须在仿真提交阶段原子完成。
        return AuthorityResult.Accept(new ItemPickupPacket(itemSlotIndex) { PlayerId = playerId });
    }

    private AuthorityResult ValidateBreak(TileBreakPacket brk, int playerId)
    {
        // 越界检查：坐标必须落在世界尺寸内
        if (!IsInWorld(brk.X, brk.Y))
            return Deny(playerId, "tile_rejected", "out_of_bounds", new { brk.X, brk.Y });

        // 玩家可达距离：CE 伪造远距离坐标 → 直接拒绝
        if (!IsWithinReach(playerId, brk.X, brk.Y, DigReachPx))
            return Deny(playerId, "tile_rejected", "out_of_reach", new { brk.X, brk.Y });

        // 目标 tile 必须存在（Action=0/4 挖实心砖；2 挖墙；>=5 电线/斜坡类跳过实体检查）
        // 区块读锁内取一份图格副本：仿真线程可能正在改同一格（详见 SectionLocks）
        Tile tile;
        using (_world.Sections.EnterRead(brk.X, brk.Y, brk.X, brk.Y))
            tile = _world.Tiles[brk.X, brk.Y];

        // 包 17 第 5 字段的语义**随 Action 变化**（逐条核对原版调用点 Player/DelegateMethods）：
        //   Action 0/2/4（KillTile / KillWall / KillTileNoItem）：**fail 标志**——1 = 仅命中特效（尚未挖穿），
        //     0 = 真正破坏。原版 Player.PickTile 未挖穿时发 `SendData(17, …, 0, x, y, 1f)`、挖穿时省略该参数（=0）；
        //     服务端原版读作 `bool flag14 = num163 == 1` 并传给 `KillTile(x, y, fail: flag14)`。
        //   Action 1/3（PlaceTile / PlaceWall）：图格 / 墙的**类型**（原版把它交给 WorldGen.PlaceTile/PlaceWall）。
        // 故「挖」绝不能拿该字段与 tile.Type 对账：曾经的 tile_type_mismatch 会把草(2)等一切非 0/1 类型
        // 判成篡改而拒绝——真机表现即「挖不动地表的砖块」（地表多为草/土/石）。
        if (brk.Action is 0 or 4)
        {
            if (!tile.Active)
                return Deny(playerId, "tile_rejected", "tile_not_found", new { brk.X, brk.Y });
        }
        else if (brk.Action is 1 or 3 && (brk.TileType < 0 || brk.TileType > MaxTileType))
        {
            return Deny(playerId, "tile_rejected", "invalid_tile_type", new { brk.TileType });
        }

        return AuthorityResult.Accept(brk);
    }

    /// <summary>原版有效图格 / 墙类型上限（1.4.5.8：图格 0..556）。</summary>
    private const int MaxTileType = 556;

    private AuthorityResult ValidatePlace(TilePlacePacket place, int playerId)
    {
        // 越界检查
        if (!IsInWorld(place.X, place.Y))
            return Deny(playerId, "tile_rejected", "out_of_bounds", new { place.X, place.Y });

        // 玩家可达距离
        if (!IsWithinReach(playerId, place.X, place.Y, PlaceReachPx))
            return Deny(playerId, "tile_rejected", "out_of_reach", new { place.X, place.Y });

        // tile 类型合法性：Terraria 有效砖类型 0..556（1.4.5.8），负数或超上限拒
        if (place.TileType < 0 || place.TileType > MaxTileType)
            return Deny(playerId, "tile_rejected", "invalid_tile_type", new { place.TileType });

        // 先检查放置目标，再检查背包。验证阶段不得产生副作用，避免目标格已占用时扣除物品。
        Tile tile;
        using (_world.Sections.EnterRead(place.X, place.Y, place.X, place.Y))
            tile = _world.Tiles[place.X, place.Y];
        if (tile.Active)
            return Deny(playerId, "tile_rejected", "tile_already_exists", new { place.X, place.Y });

        // 背包物品校验只读；实际扣除必须随放置命令一起提交。
        if (!_inv.HasItem(playerId, place.TileType))
            return Deny(playerId, "tile_rejected", "item_not_in_inventory",
                new { place.TileType, Reason = "backpack missing item or not synced yet" });

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
    private volatile RateLimits _limits;
    private readonly IAuditLogger _audit;
    private readonly ConcurrentDictionary<int, PlayerRateState> _states = new();

    public RateAuthority(RateLimits limits, IAuditLogger audit) => (_limits, _audit) = (limits, audit);

    /// <summary>热更新阈值（引用整体替换，读取端无锁）。</summary>
    internal void UpdateLimits(RateLimits limits) => _limits = limits;

    public AuthorityResult Check(IPacketContext context, INetworkPacket packet)
    {
        var state = _states.GetOrAdd(context.PlayerId, _ => new PlayerRateState());
        var now = context.ReceivedAt == default ? DateTimeOffset.UtcNow : context.ReceivedAt;

        lock (state.Gate)
        {
            // 全局包速率：packet flood / DoS 防护
            if (!state.Global.TryConsume(now, _limits.MaxPacketsPerSecond, TimeSpan.FromSeconds(1)))
                return Deny(context, packet.Type, "packet_rate_exceeded", _limits.MaxPacketsPerSecond);

            switch (packet)
            {
                case TileBreakPacket:
                    if (!state.TileBreak.TryConsume(now, _limits.MaxTileBreakPerSecond, TimeSpan.FromSeconds(1)))
                        return Deny(context, packet.Type, "tile_break_rate_exceeded", _limits.MaxTileBreakPerSecond);
                    break;

                case TilePlacePacket:
                    if (!state.TilePlace.TryConsume(now, _limits.MaxTilePlacePerSecond, TimeSpan.FromSeconds(1)))
                        return Deny(context, packet.Type, "tile_place_rate_exceeded", _limits.MaxTilePlacePerSecond);
                    break;

                case ProjectileNewPacket:
                    if (!state.Projectile.TryConsume(now, _limits.MaxProjectilesPerSecond, TimeSpan.FromSeconds(1)))
                        return Deny(context, packet.Type, "projectile_rate_exceeded", _limits.MaxProjectilesPerSecond);
                    break;

                // 聊天：包 82 的 NetTextModule（含已弃用的包 25）共用同一聊天令牌桶
                case NetTextPacket { IsClientMessage: true }:
                case UnknownPacket { Type: PacketId.ChatText }:
                    if (!state.Chat.TryConsume(now, _limits.MaxChatPerMinute, TimeSpan.FromMinutes(1)))
                        return Deny(context, packet.Type, "chat_rate_exceeded", _limits.MaxChatPerMinute);
                    break;

                // 液体：包 82 的 NetLiquidModule，独立令牌桶（不占用聊天额度）
                case LiquidModulePacket:
                    if (!state.Liquid.TryConsume(now, _limits.MaxLiquidPerSecond, TimeSpan.FromSeconds(1)))
                        return Deny(context, packet.Type, "liquid_rate_exceeded", _limits.MaxLiquidPerSecond);
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
        /// <summary>每玩家独占的轻量锁（.NET 9+ Lock）。</summary>
        public readonly Lock Gate = new();
        public WindowCounter Global;
        public WindowCounter TileBreak;
        public WindowCounter TilePlace;
        public WindowCounter Projectile;
        public WindowCounter Chat;
        public WindowCounter Liquid;
    }
}
