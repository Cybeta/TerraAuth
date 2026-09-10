// TerraAuth — Phase 2: 权威子系统聚合
// 架构 §4.2：六个权威子系统 + 限流 + 审计

using TerraAuth.Simulation;

namespace TerraAuth.Authority;

public sealed class AuthorityEnforcers
{
    // 具体类型（而非接口）持有：UpdateThresholds 需要调用各自的 internal UpdateLimits，
    // 而阈值热更新属实现细节，不进入 IAuthorityLayer 的公开契约。
    private readonly PlayerAuthority _player;
    private readonly MovementAuthority _movement;
    private readonly CombatAuthority _combat;
    private readonly InventoryAuthority _inventory;
    private readonly WorldAuthority _world;
    private readonly RateAuthority _rate;

    public IPlayerAuthority Player => _player;
    public IMovementAuthority Movement => _movement;
    public ICombatAuthority Combat => _combat;
    public IInventoryAuthority Inventory => _inventory;
    public IWorldAuthority World => _world;
    public IRateAuthority Rate => _rate;

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

        _player = new PlayerAuthority(audit, player);
        _movement = new MovementAuthority(_player, audit, movement);
        _combat = new CombatAuthority(_player, audit, combat);
        _inventory = new InventoryAuthority(audit, inventory);
        _world = new WorldAuthority(_player, audit, worldLimits, world, _inventory);
        _rate = new RateAuthority(rate, audit);
    }

    /// <summary>
    /// 热更新全部阈值（配置热重载调用）。各子系统以引用整体替换阈值对象，
    /// 读取端无锁、无撕裂；更新瞬间正在执行的校验可能仍用旧值，属热重载的正常语义。
    /// </summary>
    public void UpdateThresholds(
        RateLimits rate,
        PlayerLimits player,
        MovementLimits movement,
        CombatLimits combat,
        InventoryLimits inventory,
        WorldLimits world)
    {
        _rate.UpdateLimits(rate);
        _player.UpdateLimits(player);
        _movement.UpdateLimits(movement);
        _combat.UpdateLimits(combat);
        _inventory.UpdateLimits(inventory);
        _world.UpdateLimits(world);
    }
}
