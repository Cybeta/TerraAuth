// TerraAuth — 召唤 / 哨兵弹幕**身份**表（阶段 G；W-2 第二步已按数据表重写）
//
// 服务端用「这枚弹幕是否属于该玩家的召唤体系」回答三件事：
//   1) 生命周期：召唤物由召唤 AI 驱动（跟随 / 驻守），位置与朝向仍由客户端包 27 上报，
//      服务端不做直线积分，也不按默认 300 tick 超时销毁（否则合法召唤物命中会失去弹幕基准），
//      存活由客户端包 29 + 召唤 Buff（包 50）/ 断线清场驱动，哨兵另有 timeLeft = 36000 递减。
//   2) 命中归因：包 28 的召唤通道必须能找到「这名玩家的一枚召唤体系弹幕」作凭据
//      （FindOwnedSummonProjectile）。
//   3) 伤害上界：上界 = ceil(最高召唤弹幕伤害 × 1.15) × (crit ? 2 : 1)（见 CombatResolver.SummonDamageBound）。
//
// **集合构成（权威来源是数据表，本类不再手写类型列表）**：
//   · `Bodies` = `SummonEntityTable.Of.Keys`（62 条本体）；
//   · `Shots`  = `SummonShotTable.Of` 里全部派生弹幕（25 条）；
//   · `Of`     = 二者并集（87 条）= 身份判定集合。
//
// **本轮修掉的缺陷**：旧版把本体与派生弹幕混在一张手写列表里，且只收了 389 / 676 / 687 三个派生类型。
// 于是 374 HornetStinger / 376 / 378 / 408 / 433 / 614 / 642 / 644 / 664 / 666 / 668 / 680 / 694-696 /
// 818 / 967 / 1026 / 1097 / 1106 / 1120 这些派生弹幕 **既不在身份集合里、`KindOf` 又返回 `None`**
// → `IsSummon = false` → 玩家手持召唤法杖时包 28 找不到凭据 → 直接 `ProjectileRequired`
// → **派生弹幕的伤害全部丢失**。修法是让身份集合由数据表算出（本体 ∪ 派生），
// `KindOf` 也按数据表分档（顺手修掉旧 `SentryTypes` 的 4 处误标）。
//
// 注意区分（**不要**再混用）：
//   · 判断「是不是本体」→ `SummonEntityTable.Of.ContainsKey(type)` / `Bodies`；
//   · 判断「是不是召唤体系的弹幕」→ `Of`（本体 ∪ 派生）。

using System.Collections.Generic;
using System.Linq;

namespace TerraAuth.Simulation;

/// <summary>
/// 原版 1.4.5.8 全部召唤物 / 哨兵弹幕类型（<c>minion = true</c> / <c>sentry = true</c>）。
/// 用于识别「该玩家拥有的召唤弹幕」并据此计算包 28 的合法伤害上界。
/// </summary>
public enum SummonKind
{
    None = 0,
    Minion = 1,
    Sentry = 2,
}

public static class SummonProjectileTable
{
    /// <summary>
    /// 召唤 / 哨兵的**本体**类型集合（62 条）。权威来源是 <see cref="SummonEntityTable"/>——
    /// 本类**不再本地手写列表**（旧版那份手写列表漏了 19 条本体、又把派生弹幕混了进来）。
    /// </summary>
    public static readonly IReadOnlySet<int> Bodies = SummonEntityTable.Of.Keys.ToHashSet();

    /// <summary>
    /// 本体 AI 发射的**派生弹幕**类型集合（25 条，来源 <see cref="SummonShotTable"/>）。
    /// 它们不是本体，但**仍属于玩家的召唤体系**：归属校验、生命周期、命中归因都要认它们。
    /// 旧版把派生弹幕排除在身份集合之外，于是「玩家手持召唤法杖 → 包 28 必须由召唤弹幕背书」时，
    /// 派生弹幕既进不了 <c>FindOwnedSummonProjectile</c> 又被判成非召唤物 → 直接 <c>ProjectileRequired</c>
    /// → **派生弹幕的伤害全部丢失**（这是本次修复的核心缺陷）。
    /// </summary>
    public static readonly IReadOnlySet<int> Shots = SummonShotTable.Of.Values
        .SelectMany(static shots => shots)
        .Select(static shot => shot.ProjectileType)
        .ToHashSet();

    /// <summary>
    /// 身份判定集合 = <see cref="Bodies"/> ∪ <see cref="Shots"/>（87 条，两者不相交）。
    /// 只回答「这枚弹幕是否属于该玩家的召唤体系」，用于归属 / 生命周期 / 命中归因。
    /// **不要**用它区分本体与派生弹幕（那是 <see cref="Bodies"/> / <see cref="Shots"/> 的职责），
    /// 也**不要**用它做本体专属的伤害上界校验（派生弹幕的伤害倍率逐弹幕不同，见 <c>SpawnProjectileCommand</c>）。
    /// </summary>
    public static readonly IReadOnlySet<int> Of = Bodies.Concat(Shots).ToHashSet();

    /// <summary>派生弹幕类型 → 其所属档位（由其**发射者本体**的 <see cref="SummonKind"/> 决定）。</summary>
    private static readonly Dictionary<int, SummonKind> ShotKinds = BuildShotKinds();

    private static Dictionary<int, SummonKind> BuildShotKinds()
    {
        var map = new Dictionary<int, SummonKind>();
        foreach (var (bodyType, shots) in SummonShotTable.Of)
        {
            // 已核实没有任何派生弹幕被「仆从 + 哨兵」共同发射，故不存在档位冲突。
            SummonKind kind = SummonEntityTable.InfoOf(bodyType)?.Kind ?? SummonKind.Minion;
            foreach (var shot in shots)
                map[shot.ProjectileType] = kind;
        }

        return map;
    }

    /// <summary>
    /// 召唤武器（物品 ID）→ 其召唤 Buff（原版 <c>Item.SetDefaults</c> 的 <c>buffType</c> 字段，逐条核对原版实现）。
    /// 用于「移除召唤武器即销毁」（<see cref="WorldState.DestroySummonsOnWeaponRemoval"/>）时
    /// 同步移除客户端对应召唤 Buff：原版仆从由该 Buff 驱动存活，**只销毁服务端弹幕（包 29）不够**——
    /// 客户端 Buff 未移除时仆从不消失，仍持续发射弹幕并造成伤害（用户实测：必须手动点掉 Buff 提示）。
    /// 覆盖全部召唤武器（含 1.4.5+/交叉内容）。下列**哨兵炮台**在 <c>Item.SetDefaults</c> 里只设
    /// <c>sentry = true</c>、无 <c>buffType</c>，即无玩家召唤 Buff——无需清，故刻意不入表：
    /// 1572 FrostHydra / 3569 MoonlordTurret / 3571 RainbowCrystal / 3834 DD2Trap /
    /// 5119 HoundiusShootius / 5463 DeadCellsBarnacle。
    /// </summary>
    public static readonly IReadOnlyDictionary<int, int> SummonWeaponBuff = new Dictionary<int, int>
    {
        [1157] = 49,   // Pygmy Staff → Pygmies                (Item.cs:14284)
        [1309] = 64,   // Slime Staff → BabySlime              (Item.cs:16240)
        [1802] = 83,   // Raven Staff → Ravens                 (Item.cs:20046)
        [2364] = 125,  // Hornet Staff → Hornet                (Item.cs:23862)
        [2365] = 126,  // Imp Staff → FlyingImp                (Item.cs:23881)
        [2535] = 134,  // Optic Staff → Retanimini/Spazmamini  (Item.cs:24694)
        [2551] = 133,  // Spider Staff → VenomSpider           (Item.cs:24960)
        [2584] = 135,  // Pirate Staff → OneEyedPirate         (Item.cs:25393)
        [2621] = 139,  // Tempest Staff → Tempest              (Item.cs:25623)
        [2749] = 140,  // Xeno Staff → UFOMinion               (Item.cs:26320)
        [3249] = 161,  // Deadly Sphere Staff → DeadlySphere  (Item.cs:29965)
        [3474] = 182,  // Stardust Cell Staff → StardustMinion(Item.cs:31197)
        [3531] = 188,  // Stardust Dragon Staff → Dragon      (Item.cs:31766)
        [4269] = 213,  // Sanguine Staff → Sanguine Bat        (Item.cs:36769)
        [4273] = 214,  // Vampire Frog Staff → VampireFrog    (Item.cs:36840)
        [4281] = 216,  // Finch Staff → BabyBird               (Item.cs:36898)
        [4607] = 263,  // Desert Tiger Staff → StormTiger     (Item.cs:38190)
        [4758] = 271,  // Blade Staff → Smolstar               (Item.cs:39167)
        [5005] = 322,  // Empress' Blade → EmpressBlade       (Item.cs:40337)
        [5069] = 325,  // Flinx Staff → FlinxMinion            (Item.cs:40690)
        [5114] = 335,  // Abigail's Flower → AbigailMinion    (Item.cs:41025)
        [5456] = 355,  // Dead Cells Mushroom Boi             (Item.cs:43062)
        [5663] = 385,  // Palworld Cattiva                     (Item.cs:44532)
        [5664] = 386,  // Palworld Foxsparks                   (Item.cs:44551)
        [6148] = 389,  // Palworld Trusty Cattiva              (Item.cs:47405)
        [6149] = 390,  // Palworld Trusty Foxsparks            (Item.cs:47425)
        [6161] = 393,  // Clay Pot Minion                      (Item.cs:47548)
        [6164] = 394,  // Forbidden Minion                     (Item.cs:47567)
    };

    private static readonly HashSet<int> SummonBuffSet = new(SummonWeaponBuff.Values);

    /// <summary>
    /// 返回服务端用于生命周期和命中归因的召唤类别。
    /// 本体取 <see cref="SummonEntityTable"/> 的 <see cref="SummonKind"/>（**不含**旧版那份手写
    /// <c>SentryTypes</c>，它把 831 / 946 / 951 / 970 四个仆从误标成哨兵，又漏掉了真哨兵 308 / 377 /
    /// 641 / 643 / 663-693 / 966 / 1025）；派生弹幕取其**发射者本体**的档位。
    /// </summary>
    public static SummonKind KindOf(int projectileType)
    {
        if (SummonEntityTable.Of.TryGetValue(projectileType, out var body))
            return body.Kind;

        return ShotKinds.TryGetValue(projectileType, out var shotKind) ? shotKind : SummonKind.None;
    }

    /// <summary>判断 Buff 是否为召唤 Buff（供 <see cref="WorldState.KillSummonedProjectiles"/> 移除）。</summary>
    public static bool IsSummonBuff(int buffId) => SummonBuffSet.Contains(buffId);
}
