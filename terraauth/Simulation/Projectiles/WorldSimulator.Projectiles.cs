// TerraAuth — 服务端弹幕行为表（按原版 Projectile 字段 / aiStyle 移植）
//
// 原版弹幕的「行为」由两部分决定：
//   1) SetDefaults 的字段：tileCollide / gravity / extraUpdates（每 tick 额外更新次数）/ timeLeft；
//   2) Update 里的 aiStyle 分支：多数火焰类弹幕只做粉尘 / 朝向，运动完全由字段驱动。
// 本表只收录**服务端会发射**的弹幕类型（Boss AI 用到的 96 / 101 / 719），
// 其余类型（含客户端上报）保持简化直线积分，避免引入未核对的行为。

using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

public partial class WorldSimulator
{
    /// <summary>
    /// 弹幕行为（按原版字段）：是否图格碰撞 / 每帧重力 / 每 tick 积分次数 / 生存期上限（0 = 不覆盖）。
    /// </summary>
    private readonly record struct ProjectileBehavior(
        bool TileCollide, float Gravity, int UpdatesPerTick, int MaxLifetime);

    /// <summary>
    /// 已知弹幕类型的行为表（数值取自原版 <c>Projectile.SetDefaults</c> / aiStyle 分支）：
    /// <list type="bullet">
    ///   <item><b>96</b> CursedFlameHostile（aiStyle 8）：16×16、直线、图格碰撞、<c>timeLeft = 3600</c>；aiStyle 只生成粉尘。</item>
    ///   <item><b>101</b> EyeFire（aiStyle 23）：6×6、<c>extraUpdates = 3</c>（每 tick 积分 4 次）、<c>timeLeft</c> 钳到 60；aiStyle 只生成粉尘 / 自旋。</item>
    ///   <item><b>719</b> QueenBeeStinger（aiStyle 1）：10×10、直线、图格碰撞、<c>timeLeft = 3600</c>。</item>
    ///   <item>未登记类型：不做图格碰撞、不加重力、单次积分、不覆盖生存期（与原版未知类型解耦）。</item>
    /// </list>
    /// 三者默认 <c>penetrate = -1</c>（不因命中销毁），与「命中后不失效、靠免伤帧限频」的实现口径一致。
    /// </summary>
    private static ProjectileBehavior ProjectileBehaviorOf(int type) => type switch
    {
        96 => new(TileCollide: true, Gravity: 0f, UpdatesPerTick: 1, MaxLifetime: 3600),
        101 => new(TileCollide: true, Gravity: 0f, UpdatesPerTick: 4, MaxLifetime: 60),
        719 => new(TileCollide: true, Gravity: 0f, UpdatesPerTick: 1, MaxLifetime: 3600),
        _ => new(TileCollide: false, Gravity: 0f, UpdatesPerTick: 1, MaxLifetime: 0),
    };

    /// <summary>
    /// 按行为表推进一枚弹幕：按 <c>UpdatesPerTick</c> 次积分位置（原版 <c>extraUpdates</c>），
    /// 期间做图格碰撞（仅 <c>TileCollide</c> 类型）。返回 <c>true</c> 表示撞到图格 / 出界，弹幕应失效。
    /// </summary>
    private bool StepProjectile(ProjectileEntity p, in ProjectileBehavior behavior)
    {
        for (int i = 0; i < behavior.UpdatesPerTick; i++)
        {
            if (behavior.Gravity != 0f)
                p.Velocity = new Vector2(p.Velocity.X, p.Velocity.Y + behavior.Gravity);

            float nx = p.Position.X + p.Velocity.X;
            float ny = p.Position.Y + p.Velocity.Y;

            if (behavior.TileCollide)
            {
                int tileX = (int)(nx / TileSize);
                int tileY = (int)(ny / TileSize);
                if (tileX < 0 || tileY < 0 || tileX >= _world.Tiles.Width || tileY >= _world.Tiles.Height)
                    return true;
                if (NpcTileSolid(tileX, tileY))
                    return true;
            }

            p.Position = new Vector2(nx, ny);
        }

        return false;
    }

    /// <summary>
    /// 敌对弹幕（服务端发射，<see cref="ProjectileEntity.Owner"/> = -1）对玩家的命中伤害：
    /// 弹幕碰撞盒与玩家碰撞盒（原版 20×42）求交，取伤害最高者。
    /// 原版这三类弹幕 <c>penetrate = -1</c>（不因命中销毁），故此处**不消耗弹幕**，伤害频率由免伤帧约束。
    /// </summary>
    private int FindHostileProjectileDamage(PlayerRuntime player)
    {
        float px = player.AimPosition.X, py = player.AimPosition.Y;
        int best = 0;

        lock (_world.ProjectilesLock)
        {
            foreach (var p in _world.Projectiles)
            {
                if (!p.Active || p.Owner >= 0 || p.Damage <= 0) continue;

                if (!BoxesOverlap(p.Position.X, p.Position.Y, ProjectileHitBoxSize, ProjectileHitBoxSize,
                                  px, py, NpcSizes.PlayerWidth, NpcSizes.PlayerHeight))
                    continue;

                best = Math.Max(best, p.Damage);
            }
        }

        return best;
    }
}
