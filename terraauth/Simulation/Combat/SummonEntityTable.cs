// TerraAuth — 召唤本体表（数据表；阶段 1：只落数据，不改行为）
//
// 来源（Terraria 1.4.5.8 反编译源码逐条提取；抽取脚本在仓库外 decompiled-tmp/）：
//   · **判定本体的唯一权威** = `Projectile.SetDefaults` 里赋 `minion = true` / `sentry = true` 的弹幕类型
//     —— Projectile.cs L718..10699 的 `else if (type == N)` / `else if (type >= A && type <= B)` / `case N:` 三种形态
//     —— extract-summon-bodies3.ps1 + extract-summon-bodies-full.ps1 → **62 条**
//   · 物品归属 = `Item.shoot`（`summon = true` / `sentry = true` 的物品）
//     —— Item.cs 里同一个 id 会散落在**不同缩进深度**的多个 switch 中（Item.SetDefaults1..5 是 2 缩进，
//        另一处是 4 缩进），且 DD2 哨兵是**12 个物品共享一段 fallthrough case + 内层 `switch (type)` 逐 id 精化**，
//        `case N:` 在此**不是**「一个 case 一个物品」
//     —— extract-summon-items2.ps1（缩进无关 + 内层同 id 精化）→ **46 个物品**
//   · 名称取自 `ItemID.cs` / `ProjectileID.cs`（**核对过，不是按印象写的**）
//
// 上一版（27 条）为什么错：抽取脚本 `extract-summons.ps1` 只匹配 2 缩进的 `case N:`，且遇到
// fallthrough/last-wins 时把 DD2 内层 `case 3831: shoot = 690` 记到了 3834 头上。结果
// **漏了 19 个物品本体**（Hornet 373 / FlyingImp 375 / SpiderHiver 377 / Retanimini 387 /
// VenomSpider 390 / OneEyedPirate 393 / Tempest 407 / UFOMinion 423 + 11 个 DD2 哨兵），
// 并把 3834 的 shoot 记成 663（真值 693）。旧表注释里的「373/375/377 = 海盗法杖」等**全部是错的**。
//
// 命中节奏口径（原版 `Projectile.Damage` → `Damage_PVE_Inner`，Projectile.cs L12731-12754 / L14676-14684）：
//   · 可否命中：`usesLocalNPCImmunity && localNPCImmunity[npc] == 0`，或 `usesIDStaticNPCImmunity` 且未共享免疫，
//     或两者都不用的默认路径；
//   · `usesLocalNPCImmunity && localNPCHitCooldown != -2` → `localNPCImmunity[victim] = localNPCHitCooldown`
//     （`targetNPC.immune[owner] = 0`）——即 **本弹幕对每个目标各自冷却**；
//   · `usesIDStaticNPCImmunity` → 同 `immunityIdentity` 全服共享冷却；
//   · 其余（含 `localNPCHitCooldown = -2` 默认值）→ `targetNPC.immune[owner] = 10`，即 **默认 10 tick**。
//   · `localNPCHitCooldown = -1`（755 BatOfLight / 946 EmpressBlade）：命中后写 `localNPCImmunity[victim] = -1`，
//     而命中条件要求 `== 0`，故 **同一弹幕对同一目标终身只能命中一次**（不是"未知"，上一版记 null 是错的）。
//   · 626/627/628（StardustDragon2/3/4）**共用头节 625 的 `localNPCImmunity` 数组**（L12732-12739）。
//   服务端将来结算召唤伤害时**必须用这里的节奏**，不要另造冷却。
//
// 口径与边界（**抽不到的不猜**）：
//   · `ItemId = null` 的 16 条是**本体变体**（由另一个本体 / 增益生成，物品不直接生成），但它们仍是 `minion = true`
//     的本体，不是"派生弹幕"，不要按派生弹幕销毁。典型：Pygmy2/3/4 = 192/193/194、
//     Spazmamini = 388（OpticStaff 的 387 成对生成）、JumperSpider/DangerousSpider = 391/392、
//     StardustDragon2/3/4 = 626/627/628、StormTigerTier1/2/3 = 833/834/835、
//     StardustGuardian = 623（星尘套加成，`minionSlots = 0`）、AbigailMinion = 963（由 970 AbigailCounter 生成）。
//   · **派生弹幕**（本体 AI 发射、自行飞行的弹幕）**不在本表**，已单独定稿到
//     `Simulation/Combat/SummonShotTable.cs`：62 个本体全部读完 AI —— 32 个有派生（共 34 条关系、
//     25 个派生类型），30 个确认无派生（纯接触伤害 / 媒介 / 共享块无效调用）。两表互补，不存在"未解析"。
//   · **索敌射程 / 攻击间隔 / 攻击冷却仍未抽取**：这些在巨型共享 AI（`AI_026` 7600+ 行、`AI_062` 1600+ 行）
//     内部按 `type` 分支分派，需人工读代码，两表都不猜。未定稿前不得据此实现 `ServerShots`。
//   · `minionSlots`（占几个仆从位）已抽出但**未入表**（当前无消费者，避免造无用的字段）。
//   · `SummonProjectileTable.Of` 仍把本体与派生弹幕混在一张表里（`SentryTypes` 亦有多处误标）。
//     本表是**只读数据**：修正 `Of` / `SentryTypes` 属行为改动，留给 backlog W-2 的第二步。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>本体的命中免疫归属（决定"同一目标多久能再被打一次"）。</summary>
public enum SummonHitImmunity
{
    /// <summary>原版默认：<c>targetNPC.immune[owner] = 10</c>（每玩家 10 tick）。</summary>
    Default = 0,

    /// <summary><c>usesLocalNPCImmunity</c>：本弹幕对每个目标各自冷却，冷却见 <see cref="SummonEntityInfo.HitCooldownTicks"/>。</summary>
    LocalPerTarget = 1,

    /// <summary><c>usesIDStaticNPCImmunity</c>：同 <c>immunityIdentity</c> 全服共享冷却，冷却见 <see cref="SummonEntityInfo.HitCooldownTicks"/>。</summary>
    IdStaticShared = 2,
}

/// <summary>本体弹幕的静态数据（原版 1.4.5.8 基准值）。</summary>
/// <param name="ItemId">直接生成该本体的物品；null = 由别的本体 / 增益生成（本体变体）。</param>
/// <param name="Kind">本体类别（仆从 / 哨兵）。</param>
/// <param name="AiStyle">原版 <c>Projectile.aiStyle</c>（决定行为由哪段 AI 实现）。</param>
/// <param name="Width">原版 <c>Projectile.width</c>。</param>
/// <param name="Height">原版 <c>Projectile.height</c>。</param>
/// <param name="TimeLeft">服务端口径的存活上限（哨兵 = 原版 36000；其余 0 = 由召唤 Buff 驱动，不按计时销毁）。</param>
/// <param name="TileCollide">原版 <c>tileCollide</c>（false = 穿墙）。</param>
/// <param name="Immunity">命中免疫归属（默认每玩家 10 tick）。</param>
/// <param name="HitCooldownTicks">
/// 命中冷却 tick；-1 = **同一弹幕对同一目标终身只能命中一次**（原版 <c>localNPCHitCooldown = -1</c>，755 / 946）。
/// </param>
public sealed record SummonEntityInfo(
    int? ItemId,
    SummonKind Kind,
    int AiStyle,
    int Width,
    int Height,
    int TimeLeft,
    bool TileCollide = true,
    SummonHitImmunity Immunity = SummonHitImmunity.Default,
    int HitCooldownTicks = 10);

/// <summary>
/// 原版 1.4.5.8 全部召唤 / 哨兵**本体**弹幕（62 条 = 46 物品直生 + 16 本体变体），键为弹幕类型。
/// 用于把"本体"与"本体发射的派生弹幕"分开——这是服务端权威召唤（backlog W-2）的前置数据。
/// </summary>
public static class SummonEntityTable
{
    /// <summary>本体弹幕类型 → 静态数据。</summary>
    public static readonly IReadOnlyDictionary<int, SummonEntityInfo> Of = new Dictionary<int, SummonEntityInfo>
    {
        // ---- 仆从本体：物品直接生成（28 条）----
        [191] = new(1157, SummonKind.Minion, 26, 18, 18, 0),    // Pygmy ← PygmyStaff（2/3/4 = 192/193/194）
        [266] = new(1309, SummonKind.Minion, 26, 24, 16, 0, Immunity: SummonHitImmunity.IdStaticShared, HitCooldownTicks: 12),   // BabySlime ← SlimeStaff
        [317] = new(1802, SummonKind.Minion, 54, 28, 28, 0, Immunity: SummonHitImmunity.LocalPerTarget),                        // Raven ← RavenStaff
        [373] = new(2364, SummonKind.Minion, 62, 24, 26, 0, TileCollide: false),                                               // Hornet ← HornetStaff
        [375] = new(2365, SummonKind.Minion, 62, 34, 26, 0, TileCollide: false),                                               // FlyingImp ← ImpStaff
        [387] = new(2535, SummonKind.Minion, 66, 40, 20, 0, TileCollide: false, Immunity: SummonHitImmunity.IdStaticShared, HitCooldownTicks: 16),  // Retanimini ← OpticStaff（成对生成 388 Spazmamini）
        [390] = new(2551, SummonKind.Minion, 26, 30, 30, 0, Immunity: SummonHitImmunity.IdStaticShared, HitCooldownTicks: 15),  // VenomSpider ← SpiderStaff（变体 391/392）
        [393] = new(2584, SummonKind.Minion, 67, 20, 30, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 18),  // OneEyedPirate ← PirateStaff（变体 394/395）
        [407] = new(2621, SummonKind.Minion, 62, 28, 40, 0, TileCollide: false, Immunity: SummonHitImmunity.IdStaticShared),    // Tempest ← TempestStaff
        [423] = new(2749, SummonKind.Minion, 62, 28, 28, 0),                                                                   // UFOMinion ← XenoStaff
        [533] = new(3249, SummonKind.Minion, 66, 20, 20, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 12),  // DeadlySphere ← DeadlySphereStaff
        [613] = new(3474, SummonKind.Minion, 62, 24, 24, 0),   // StardustCellMinion ← StardustCellStaff
        [625] = new(3531, SummonKind.Minion, 121, 24, 24, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 7),  // StardustDragon1 ← StardustDragonStaff（2/3/4 = 626/627/628，共用头节免疫数组）
        [755] = new(4269, SummonKind.Minion, 156, 10, 10, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: -1), // BatOfLight ← SanguineStaff（对同一目标只命中一次）
        [758] = new(4273, SummonKind.Minion, 67, 20, 30, 0, Immunity: SummonHitImmunity.IdStaticShared),                        // VampireFrog ← VampireFrogStaff
        [759] = new(4281, SummonKind.Minion, 158, 10, 10, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 15), // BabyBird ← BabyBirdStaff
        [831] = new(4607, SummonKind.Minion, 164, 10, 10, 60, TileCollide: false),   // StormTigerGem ← StormTigerStaff（Tier1/2/3 = 833/834/835；短命本体）
        [864] = new(4758, SummonKind.Minion, 169, 10, 10, 60, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget),  // Smolstar ← Smolstar（短命本体）
        [946] = new(5005, SummonKind.Minion, 156, 10, 10, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: -1), // EmpressBlade ← EmpressBlade（对同一目标只命中一次）
        [951] = new(5069, SummonKind.Minion, 67, 26, 26, 0, Immunity: SummonHitImmunity.IdStaticShared),                        // FlinxMinion ← FlinxStaff
        [970] = new(5114, SummonKind.Minion, 164, 10, 10, 60, TileCollide: false),   // AbigailCounter ← AbigailsFlower（**媒介**：真实本体 963 AbigailMinion）
        [1022] = new(5456, SummonKind.Minion, 67, 20, 30, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 18),  // DeadCellsMushroomBoiMinion
        [1093] = new(5663, SummonKind.Minion, 67, 14, 22, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 9),   // PalworldMinionCattiva
        [1094] = new(5664, SummonKind.Minion, 26, 18, 18, 0),   // PalworldMinionFoxsparks
        [1112] = new(6148, SummonKind.Minion, 67, 14, 22, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 9),   // PalworldMinionTrustyCattiva
        [1113] = new(6149, SummonKind.Minion, 26, 18, 18, 0),   // PalworldMinionTrustyFoxsparks
        [1118] = new(6161, SummonKind.Minion, 67, 14, 22, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 30),  // ClayPotMinion
        [1119] = new(6164, SummonKind.Minion, 206, 14, 22, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 30),  // ForbiddenMinion

        // ---- 哨兵本体：物品直接生成（18 条；timeLeft = 36000 = 原版 10 分钟）----
        [308] = new(1572, SummonKind.Sentry, 53, 80, 74, 36000),     // FrostHydra ← StaffoftheFrostHydra
        [377] = new(2366, SummonKind.Sentry, 53, 66, 50, 36000),     // SpiderHiver ← QueenSpiderStaff
        [641] = new(3569, SummonKind.Sentry, 123, 32, 32, 36000, TileCollide: false),   // MoonlordTurret ← MoonlordTurretStaff
        [643] = new(3571, SummonKind.Sentry, 123, 32, 32, 36000, TileCollide: false),   // RainbowCrystal ← RainbowCrystalStaff
        [663] = new(3818, SummonKind.Sentry, 130, 30, 54, 36000, TileCollide: false),   // DD2FlameBurstTowerT1 ← DD2FlameburstTowerT1Popper
        [665] = new(3819, SummonKind.Sentry, 130, 28, 58, 36000, TileCollide: false),   // DD2FlameBurstTowerT2
        [667] = new(3820, SummonKind.Sentry, 130, 28, 60, 36000, TileCollide: false),   // DD2FlameBurstTowerT3
        [677] = new(3824, SummonKind.Sentry, 134, 26, 54, 36000, TileCollide: false),   // DD2BallistraTowerT1 ← DD2BallistraTowerT1Popper
        [678] = new(3825, SummonKind.Sentry, 134, 26, 54, 36000, TileCollide: false),   // DD2BallistraTowerT2
        [679] = new(3826, SummonKind.Sentry, 134, 26, 54, 36000, TileCollide: false),   // DD2BallistraTowerT3
        [688] = new(3829, SummonKind.Sentry, 137, 16, 16, 36000, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 3),  // DD2LightningAuraT1
        [689] = new(3830, SummonKind.Sentry, 137, 16, 16, 36000, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 3),  // DD2LightningAuraT2
        [690] = new(3831, SummonKind.Sentry, 137, 16, 16, 36000, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 3),  // DD2LightningAuraT3
        [691] = new(3832, SummonKind.Sentry, 138, 16, 16, 36000, TileCollide: false),   // DD2ExplosiveTrapT1
        [692] = new(3833, SummonKind.Sentry, 138, 16, 16, 36000, TileCollide: false),   // DD2ExplosiveTrapT2
        [693] = new(3834, SummonKind.Sentry, 138, 16, 16, 36000, TileCollide: false),   // DD2ExplosiveTrapT3
        [966] = new(5119, SummonKind.Sentry, 53, 18, 60, 36000),                        // HoundiusShootius ← HoundiusShootius
        [1025] = new(5463, SummonKind.Sentry, 197, 42, 38, 36000),                      // DeadCellsBarnacle ← DeadCellsBarnacleSummonItem

        // ---- 本体变体：由别的本体 / 增益生成，物品不直接生成（16 条，ItemId = null）----
        [192] = new(null, SummonKind.Minion, 26, 18, 18, 0),    // Pygmy2
        [193] = new(null, SummonKind.Minion, 26, 18, 18, 0),    // Pygmy3
        [194] = new(null, SummonKind.Minion, 26, 18, 18, 0),    // Pygmy4
        [388] = new(null, SummonKind.Minion, 66, 40, 20, 0, TileCollide: false, Immunity: SummonHitImmunity.IdStaticShared, HitCooldownTicks: 12),  // Spazmamini（OpticStaff 成对生成）
        [391] = new(null, SummonKind.Minion, 26, 30, 30, 0, Immunity: SummonHitImmunity.IdStaticShared, HitCooldownTicks: 15),  // JumperSpider
        [392] = new(null, SummonKind.Minion, 26, 30, 30, 0, Immunity: SummonHitImmunity.IdStaticShared, HitCooldownTicks: 15),  // DangerousSpider
        [394] = new(null, SummonKind.Minion, 67, 20, 30, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 18),  // SoulscourgePirate
        [395] = new(null, SummonKind.Minion, 67, 20, 30, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 18),  // PirateCaptain
        [623] = new(null, SummonKind.Minion, 120, 50, 80, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 5),  // StardustGuardian（星尘套加成，minionSlots = 0）
        [626] = new(null, SummonKind.Minion, 121, 24, 24, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 7),  // StardustDragon2
        [627] = new(null, SummonKind.Minion, 121, 24, 24, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 7),  // StardustDragon3
        [628] = new(null, SummonKind.Minion, 121, 24, 24, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 7),  // StardustDragon4
        [833] = new(null, SummonKind.Minion, 67, 26, 20, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 10),  // StormTigerTier1
        [834] = new(null, SummonKind.Minion, 67, 20, 30, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 10),  // StormTigerTier2
        [835] = new(null, SummonKind.Minion, 67, 20, 30, 0, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 10),  // StormTigerTier3
        [963] = new(null, SummonKind.Minion, 62, 30, 48, 0, TileCollide: false, Immunity: SummonHitImmunity.LocalPerTarget, HitCooldownTicks: 20),  // AbigailMinion（由 970 AbigailCounter 生成）
    };
}
