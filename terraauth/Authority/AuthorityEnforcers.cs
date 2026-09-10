// TerraAuth — Phase 2: 权威子系统聚合
// 架构 §4.2：六个权威子系统 + 限流 + 审计

using TerraAuth.Simulation;

namespace TerraAuth.Authority;

public sealed class AuthorityEnforcers
{
    public IPlayerAuthority Player { get; }
    public IMovementAuthority Movement { get; }
    public ICombatAuthority Combat { get; }
    public IInventoryAuthority Inventory { get; }
    public IWorldAuthority World { get; }
    public IRateAuthority Rate { get; }

    public AuthorityEnforcers(
        RateLimits rate,
        IAuditLogger audit,
        WorldState world,
        MovementLimits? movementLimits = null,
        PlayerLimits? playerLimits = null,
        CombatLimits? combatLimits = null,
        InventoryLimits? inventoryLimits = null,
        WorldLimits? worldLimitsOpt = null)
    {
        // 依赖关系：World/Combat/Movement 依赖 Player（权限/上限），其余独立
        var movement = movementLimits ?? MovementLimits.Default;
        var player = playerLimits ?? PlayerLimits.Default;
        var combat = combatLimits ?? CombatLimits.Default;
        var inventory = inventoryLimits ?? InventoryLimits.Default;
        var worldLimits = worldLimitsOpt ?? WorldLimits.Default;

        Player = new PlayerAuthority(audit, player);
        Movement = new MovementAuthority(Player, audit, movement);
        Combat = new CombatAuthority(Player, audit, combat);
        Inventory = new InventoryAuthority(audit, inventory);
        World = new WorldAuthority(Player, audit, worldLimits, world);
        Rate = new RateAuthority(rate, audit);
    }
}
