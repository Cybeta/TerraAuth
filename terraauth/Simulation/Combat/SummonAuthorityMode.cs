// TerraAuth — 召唤 / 哨兵权威档位（backlog W-2）
//
// 档位**逐级递进**：每一档都包含前一档的行为，默认 ClientDriven = 现状、零风险。
// 由 ServerConfig.SummonAuthority（server.json 字符串枚举）注入 WorldState.SummonAuthority，可热重载。
//
// 各档的裁决边界（写代码前务必按这里对齐）：
//   · ClientDriven（默认）＝现状：本体位置 / 朝向 / 攻击节奏全由客户端 AI 决定，
//     命中经包 28 上报，服务端只做「归属 + 召唤类型 + 伤害上界 + 冷却」校验后按**上报值**结算。
//   · ServerDamage：本体命中的**伤害数值由服务端裁定**——包 28 只当作「发生了命中」的触发，
//     上报的 Damage 不参与结算（改为服务端按本体登记伤害掷 ±15% 浮动 × 暴击），且**本体不再作为
//     包 28 的伤害凭据**（否则玩家可以手持任意武器用高伤本体当"上界"）。派生弹幕仍走既有校验口径。
//     **本档不做服务端重叠判定**：本体位置由客户端包 27 驱动，原版 minion 的 27 上报频率不足以保证
//     命中帧坐标新鲜，服务端自行求交会**漏伤**——位置权威要等 ServerAi 档（服务端接管本体位置）。
//   · ServerAi（未实现）：服务端接管本体的跟随 / 驻守 / 索敌 / 节奏，包 27 只用于创建登记。
//   · ServerShots（未实现）：本体的攻击由服务端 NewProjectile 生成派生弹幕（用 SummonShotTable）。

namespace TerraAuth.Simulation;

/// <summary>召唤 / 哨兵的服务端权威档位（逐级递进，默认 <see cref="ClientDriven"/>）。</summary>
public enum SummonAuthorityMode
{
    /// <summary>客户端驱动（默认）：位置 / 节奏由客户端 AI，命中经包 28 上报并按上报值结算。</summary>
    ClientDriven = 0,

    /// <summary>服务端裁定本体命中的伤害数值（上报值仅作命中触发），且本体不再作为包 28 的伤害凭据。</summary>
    ServerDamage = 1,

    /// <summary>服务端接管本体位置与 AI（未实现）。</summary>
    ServerAi = 2,

    /// <summary>服务端生成本体的派生弹幕（未实现）。</summary>
    ServerShots = 3,
}
