// TerraAuth 示例插件 2：轻量伤害上限
// 演示可取消的 NpcStrikeArgs Hook + Deny 短路（对接 CombatAuthority）

using System;
using System.Threading.Tasks;
using TerraAuth.Plugins;

namespace TerraAuth.ExamplePlugins;

public sealed class AntiCheatLitePlugin : PluginBase
{
    private const int MaxSingleHitDamage = 10000;

    public override string Id => "anticheat_lite";
    public override string Name => "AntiCheat Lite";
    public override Version Version => new(1, 0, 0);
    public override string Author => "TerraAuth";
    public override string Description => "拦截单次伤害异常（示例）";

    protected override Task OnInitializeAsync()
    {
        On<NpcStrikeArgs>(OnNpcStrike);
        return Task.CompletedTask;
    }

    private HookResult OnNpcStrike(NpcStrikeArgs args)
    {
        if (args.Damage > MaxSingleHitDamage)
        {
            Logger.Warn("玩家 #{0} 单次伤害异常 {1}，已拒绝", args.PlayerId, args.Damage);
            Metrics.Counter("anticheat_lite_blocked_total", "异常伤害拦截数");
            return HookResult.Deny($"damage {args.Damage} exceeds limit {MaxSingleHitDamage}");
        }
        return HookResult.Allow();
    }
}
