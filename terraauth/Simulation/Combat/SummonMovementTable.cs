// TerraAuth — 召唤本体「移动 / 拴绳」表（数据表；阶段 1：只落数据，不改行为）
//
// 作用：给 backlog W-2 的 `ServerAi` 档（服务端接管本体位置与 AI）提供**位置权威**所需的参数：
// 移动模式、召回（拴绳）距离、超远归位阈值、速度上限。
//
// 覆盖全部 62 个本体（=`SummonEntityTable.Of` 的键），来源为 Terraria 1.4.5.8 反编译源码逐条人工读 AI。
//
// ==== 三条必须先知道的通用事实（本轮逐条核实，写实现前务必遵守）====
//
// 1. **通用世界边界处理**（Projectile.cs L18391-18406）对两类本体语义**不同**：
//    · `minion` 越界 → `base.Center = player.Center`（传回主人）并 return；
//    · `sentry` 越界 → **直接 `active = false` + 发包 29 消亡**，**不是归位**。
//    故「哨兵没有超远归位」是原版行为，`TeleportDistance = null` 表示本 AI 内**没有自己的**阈值。
//
// 2. **`Main.player[owner].MinionRestTargetPoint` 全原版只有 623 StardustGuardian 使用**
//    （AI_120 内 L58923/L59094 两处）。其余本体的待命点都在各自 AI 内现算
//    （多为 `player.Center + (0, -60)` 一类固定偏移，或按 `minionPos` / 组内序号横向排队）。
//    服务端接管时**不要**假设存在统一的「待命点」字段。
//
// 3. **并非所有哨兵都静止**：只有 641 MoonlordTurret / 643 RainbowCrystal 每帧 `velocity = Vector2.Zero`
//    真正固定；其余 16 个哨兵都有 AI 自写的**垂直**速度积分（落体 `velocity.Y += 0.2f` 贴地，
//    或 1025 的贴顶调节）。服务端冻结哨兵会造成「客户端落地、服务端悬空」。
//
// ==== 字段口径（null 的含义务必看清）====
//   · `Mode`：移动模式，见 <see cref="SummonMoveMode"/>。
//   · `RecallDistance` / `RecallWithTargetDistance`：**召回（拴绳）**阈值——超过它本体进入「回位」态。
//     `null` = 本 AI **没有**这种机制（不是"距离为 0"）。随仆从位线性增长的本体记**基础值**，
//     公式写在各自注释里（例：AI_026 系 = `500 + 40 × minionPos`，刚攻击过再 +500）。
//   · `TeleportDistance`：本 AI **自带**的「离主人太远 → 直接传送/吸附」阈值（多数为 2000，864 为 3000）。
//     `null` = 本 AI 内没有该阈值（此时只剩上面的通用世界边界处理）。
//   · `SpeedLimit` / `RecallSpeedLimit`：常态 / 召回态的**速度上限**（px/tick）。
//     `null` = 源码中**未找到**明确上限（例如 DD2 炮台只做落体，没有钳位）。
//
// 边界（抽不到的不猜）：
//   · **逐类型的待命点公式**（`40 × minionPos` 排队、759 的头顶堆叠、755/946 的环绕半径、
//     864 的公转环、831/970 的公转环、1119 的椭圆环…）只写在注释里，**未入表**——
//     它们是「实现某个 aiStyle 时才需要」的细节，等该族实现时按注释逐条落地，避免此处编造数值。
//   · 加速度 / 惯性插值系数（如 `velocity = (velocity×20 + 目标)/21`）同样未入表，见注释。
//   · 本表是**只读数据**，当前无消费者。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>本体的移动模式（决定服务端接管位置时要复刻什么）。</summary>
public enum SummonMoveMode
{
    /// <summary>飞行 / 悬浮：AI 直接写 <c>velocity</c>，**不**自加重力。</summary>
    Fly = 0,

    /// <summary>贴地：AI 自加重力（多为 0.4/tick）+ 落地转向 / 跳跃。</summary>
    Ground = 1,

    /// <summary>贴顶 / 垂直调节 + 图块碰撞（1025 DeadCellsBarnacle）。</summary>
    Ceiling = 2,

    /// <summary>位置完全固定：AI 每帧把 <c>velocity</c> 清零，无任何位移（641 / 643）。</summary>
    Static = 3,

    /// <summary>位置**硬绑定**：每帧直接赋 <c>Center</c>（831 / 970 绑到主人身边的家点；625-628 节段绑父节）。</summary>
    PositionBound = 4,
}

/// <summary>本体的移动 / 拴绳参数（原版 1.4.5.8 基准值；null 的口径见文件头）。</summary>
public sealed record SummonMovementInfo(
    SummonMoveMode Mode,
    int? RecallDistance = null,
    int? RecallWithTargetDistance = null,
    int? TeleportDistance = null,
    float? SpeedLimit = null,
    float? RecallSpeedLimit = null);

/// <summary>
/// 62 个本体的移动模式 + 召回 / 归位阈值 + 速度上限（键与 <see cref="SummonEntityTable.Of"/> 相同）。
/// </summary>
public static class SummonMovementTable
{
    /// <summary>本体弹幕类型 → 移动参数。</summary>
    public static readonly IReadOnlyDictionary<int, SummonMovementInfo> Of =
        new Dictionary<int, SummonMovementInfo>
        {
            // ==== aiStyle 26（AI_026）====
            // 贴地行走 + 召回时飞行；待命横向站位 = 40 × (minionPos + 1)，容差 10（L79917-79929）；
            // 召回距离 = 500 + 40 × minionPos（刚攻击过再 +500，L81287-81302）；AI 自带 >2000 吸附主人（L81311）
            [191] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 12f),
            [192] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 12f),
            [193] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 12f),
            [194] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 12f),
            // BabySlime：同一骨架，召回距离额外 +100（L81294）；贴地弹跳（移动时 velocity.Y -= 6f，L83304）
            [266] = new(SummonMoveMode.Ground, RecallDistance: 600, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 10f),
            // 蜘蛛：召回距离额外 +400（L81298）；贴墙爬行（爬速固定 9）+ 非贴墙跳跃（L85135-85218）
            [390] = new(SummonMoveMode.Ground, RecallDistance: 900, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 10f),
            [391] = new(SummonMoveMode.Ground, RecallDistance: 900, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 10f),
            [392] = new(SummonMoveMode.Ground, RecallDistance: 900, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 10f),
            // Foxsparks：同 flag10 骨架（召回 500+40×minionPos+500）；回收状态直接吸附主人（L81639）
            [1094] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 12f),
            [1113] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 12f),

            // ==== aiStyle 54（内联块 L38231-38441）Raven ====
            // 飞行；待命点 = 主人中心上方 60（L38331）；拴绳 500，ai[1]!=0 或 friendly 时 1400（L38270-38274）；
            // 自带 >2000 吸附主人（L38338）；速度 8（召回态 12，远距 15）
            [317] = new(SummonMoveMode.Fly, RecallDistance: 500, RecallWithTargetDistance: 1400, TeleportDistance: 2000, SpeedLimit: 8f, RecallSpeedLimit: 12f),

            // ==== aiStyle 62（AI_062）====
            // 飞行；待命点 = 主人中心上方 60（L87227，375/407/963 另有横向偏移）；
            // 拴绳 num21 = 500（963 为 800；有目标 1000；423 为 1200；613 为 1350，L86969-86985）；
            // 自带 >2000 吸附主人（L87271）；速度 6（召回 15；375 为 ×0.75、407 固定 9）
            [373] = new(SummonMoveMode.Fly, RecallDistance: 500, RecallWithTargetDistance: 1000, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 15f),
            [375] = new(SummonMoveMode.Fly, RecallDistance: 500, RecallWithTargetDistance: 1000, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 15f),
            [407] = new(SummonMoveMode.Fly, RecallDistance: 500, RecallWithTargetDistance: 1000, TeleportDistance: 2000, SpeedLimit: 9f, RecallSpeedLimit: 15f),
            [423] = new(SummonMoveMode.Fly, RecallDistance: 500, RecallWithTargetDistance: 1200, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 15f),
            [613] = new(SummonMoveMode.Fly, RecallDistance: 500, RecallWithTargetDistance: 1350, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 15f),
            // AbigailMinion：无目标档 800、有目标档 1000（L86972/L86976）；速度 ×0.8（L87222）
            [963] = new(SummonMoveMode.Fly, RecallDistance: 800, RecallWithTargetDistance: 1000, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 15f),

            // ==== aiStyle 66（内联块 L39361-39942）====
            // 387/388：待命点 = 主人上方 60（L39692）；召回 800（有目标 1200，L39371-39372）；
            //   解除距离 150（L39373 `num726`）；自带 >2000 改 position（L39730）；速度 6（533 为 12）/ 召回 15
            [387] = new(SummonMoveMode.Fly, RecallDistance: 800, RecallWithTargetDistance: 1200, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 15f),
            [388] = new(SummonMoveMode.Fly, RecallDistance: 800, RecallWithTargetDistance: 1200, TeleportDistance: 2000, SpeedLimit: 6f, RecallSpeedLimit: 15f),
            // DeadlySphere：召回 900（有目标 1500）、解除 450（L39385-39388）；ai[0] < 9 时参与图格碰撞（L39591）
            [533] = new(SummonMoveMode.Ground, RecallDistance: 900, RecallWithTargetDistance: 1500, TeleportDistance: 2000, SpeedLimit: 12f, RecallSpeedLimit: 15f),

            // ==== aiStyle 120（AI_120_StardustGuardian）====
            // **全原版唯一使用 MinionRestTargetPoint 的本体**（L58923/L59094）；
            // 待命点 = 主人朝向前方 (5 + player.width/2)px、上方 25px（L58826-58838）；
            // 本 AI 内**没有**超远归位阈值（只有 ai[0]==3 的 Lerp 回位 + 通用世界边界）；无显式速度上限
            [623] = new(SummonMoveMode.Fly),

            // ==== aiStyle 121（AI_121_StardustDragon）====
            // 头节 625：追击判定 700（L56232）、主人到目标最大 1000（L56233）、自带 >2000 吸附（L56235）；
            //   速度上限：追击 30、跟随 15（L56301/56338）；无目标死区 100（L56319）
            [625] = new(SummonMoveMode.Fly, RecallDistance: 700, TeleportDistance: 2000, SpeedLimit: 15f, RecallSpeedLimit: 30f),
            // 节段 626-628：**位置硬绑定父节** —— 每帧 velocity 清零，`Center = 父节.Center - 单位向量 × 16 × 父节scale`（L56438-56456）
            [626] = new(SummonMoveMode.PositionBound),
            [627] = new(SummonMoveMode.PositionBound),
            [628] = new(SummonMoveMode.PositionBound),

            // ==== aiStyle 67（AI_067_FreakingPirates）====
            // 贴地行走 + 跳跃；待命横向站位 = (基础 + player.width/2) + minionPos × 步长（逐类型不同，见各行注释）；
            // 拴绳：水平 num3 = 500 / 垂直 num4 = 300（L66000-66001、L67174）；自带 >2000 改 position（L66631/L67170）
            [393] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 8f),   // 站位 (15 + w/2) + minionPos×20（L66267）
            [394] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 8f),
            [395] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 8f),
            [758] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f),   // VampireFrog：站位 (35 + w/2) + minionPos×40；有目标时站位再推 50（L66985）
            [833] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 8f),   // StormTiger：站位 (15 + w/2) + minionPos×40
            [834] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 8f),
            [835] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 8f),
            [951] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 5.5f), // FlinxMinion：站位 (45 + w/2) + minionPos×30
            [1022] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f),  // MushroomBoi：站位 (45 + w/2) + minionPos×30；有目标时站到身前 27px（L66990）
            [1093] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 6f),  // Cattiva：站位 (30 + w/2) + minionPos×20；召回态硬吸附主人（L66768）
            [1112] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 12f), // TrustyCattiva：速度专属覆盖 9/12/9（L67289）
            [1118] = new(SummonMoveMode.Ground, RecallDistance: 500, TeleportDistance: 2000, SpeedLimit: 3f),  // ClayPotMinion：站位 (24 + w/2) + minionPos×20

            // ==== aiStyle 164（AI_164_StormTigerGem）====
            // 位置硬绑定：每帧 `base.Center = AI_164_GetHomeLocation(...)`，**无 speed/惯性/召回**（L61608-61610）；
            // 家点 = 主人 MountedCenter 上方约 61px 的公转环点（半径 8 + 12×排号，L61683-61689）
            [831] = new(SummonMoveMode.PositionBound),
            [970] = new(SummonMoveMode.PositionBound),

            // ==== aiStyle 169（AI_169_Smolstars）====
            // 864 Smolstar：悬浮飞行；待命 = 主人头顶上方 30 + 随 GlobalTimeWrappedHourly 公转的环（L60351-60372）；
            // 速度随距离增长：10 + lerp(200,600,距离)×30（上限 40，L60377-60379）；自带 >=3000 归位（L60380）
            [864] = new(SummonMoveMode.Fly, TeleportDistance: 3000, SpeedLimit: 40f),

            // ==== aiStyle 156（AI_156_BatOfLight）====
            // 755/946：悬浮飞行；待命为环绕/偏移点（755 半径 40 环、946 摆动偏移，L69183-69198）；
            // 自带 Vector2.Distance(主人) > 2000 → 复位（L68819）；攻击位移速度 10（L69039）
            [755] = new(SummonMoveMode.Fly, TeleportDistance: 2000, SpeedLimit: 10f),
            [946] = new(SummonMoveMode.Fly, TeleportDistance: 2000, SpeedLimit: 10f),

            // ==== aiStyle 158（AI_158_BabyBird）====
            // 悬浮飞行；待命 = 主人头顶堆叠点（AI_158_GetHomeLocation L65175-65279，每 6 只上移 16）；
            // 速度随距离增长：6 + 距离×0.006（L65116）；自带 >2000 归位（L65105）；**未找到**拴绳阈值
            [759] = new(SummonMoveMode.Fly, TeleportDistance: 2000, SpeedLimit: 6f),

            // ==== aiStyle 206（AI_206_ForbiddenMinion）====
            // 飞行/悬浮；待命 = 主人头顶偏后 (-direction×16, -25) + 组内椭圆环（L48452/L48463/L48482）；
            // 有目标时与目标保持 200px（L48494）；自带 >2000 吸附主人 MountedCenter（L48453）；
            // 速度上限 16、加速度 2（L48543-48549）
            [1119] = new(SummonMoveMode.Fly, TeleportDistance: 2000, SpeedLimit: 16f),

            // ==== 哨兵：均**没有**召回 / 超远归位（越界 = 消亡，见文件头第 1 条）====
            // AI_053：只有**垂直**落体 `velocity.Y += 0.2f`、上限 16（L48198-48206）；未背包时无水平位移
            [308] = new(SummonMoveMode.Ground, SpeedLimit: 16f),
            [377] = new(SummonMoveMode.Ground, SpeedLimit: 16f),
            [966] = new(SummonMoveMode.Ground, SpeedLimit: 16f),
            // AI_123：**每帧 velocity = Vector2.Zero**，位置完全固定，无任何位移（L47873）
            [641] = new(SummonMoveMode.Static),
            [643] = new(SummonMoveMode.Static),
            // DD2 四系：落体 `velocity.Y += 0.2f` + 图格碰撞贴地（L91251/91655/92301/92786）；未找到速度钳位
            [663] = new(SummonMoveMode.Ground),
            [665] = new(SummonMoveMode.Ground),
            [667] = new(SummonMoveMode.Ground),
            [677] = new(SummonMoveMode.Ground),
            [678] = new(SummonMoveMode.Ground),
            [679] = new(SummonMoveMode.Ground),
            // AI_137：首帧向下 500 找地面（找不到则下移 16px 重试），再向上 10 格找天花板并重写碰撞盒（L92187-92226）
            [688] = new(SummonMoveMode.Ground),
            [689] = new(SummonMoveMode.Ground),
            [690] = new(SummonMoveMode.Ground),
            [691] = new(SummonMoveMode.Ground),
            [692] = new(SummonMoveMode.Ground),
            [693] = new(SummonMoveMode.Ground),
            // AI_197：贴顶/悬浮 —— 向上 15 格探测实体块（L52336），有块则 `velocity.Y -= 0.1f`（下限 -12），
            // 无块则 `velocity *= 0.9f`（L52371-52393）
            [1025] = new(SummonMoveMode.Ceiling, SpeedLimit: 12f),
        };
}
