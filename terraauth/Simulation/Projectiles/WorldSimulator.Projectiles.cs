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
    /// <para>服务端权威仿真能力（3C 方案 A）：<c>Homing</c>（追踪敌人）+ <c>HomingRange/HomingTurn</c>（追踪范围 / 每子步最大转角）+ <c>Bounces</c>（撞图格反弹预算）。</para>
    /// </summary>
    private readonly record struct ProjectileBehavior(
        bool TileCollide, float Gravity, int UpdatesPerTick, int MaxLifetime,
        bool Homing = false, float HomingRange = 0f, float HomingTurn = 0f, int Bounces = 0)
    {
        /// <summary>默认：图格碰撞 = 原版默认 true；每 tick 积分 1 次；无追踪 / 反弹 / 重力。</summary>
        public static readonly ProjectileBehavior Default = new(TileCollide: true, Gravity: 0f, UpdatesPerTick: 1, MaxLifetime: 0);
    }

    // 追踪 / 反弹属特殊定义，SetDefaults 不含，按原版知名弹幕显式收录（默认归零）。
    private static ProjectileBehavior HomingOrBounceBehaviorOf(int type) => type switch
    {
        // 20 Demon Scythe：图格碰撞 + 强追踪（全屏锁敌转向），life 480。
        20 => new(TileCollide: true, Gravity: 0f, UpdatesPerTick: 1, MaxLifetime: 480, Homing: true, HomingRange: 1100f, HomingTurn: 0.10f),
        // 433 Death Sickle：图格碰撞 + 追踪，life 180。
        433 => new(TileCollide: true, Gravity: 0f, UpdatesPerTick: 1, MaxLifetime: 180, Homing: true, HomingRange: 900f, HomingTurn: 0.07f),
        // 81 Water Bolt：图格碰撞 + 反弹（近似预算 20，原版为穿行 + 弹射）。
        81 => new(TileCollide: true, Gravity: 0f, UpdatesPerTick: 2, MaxLifetime: 180, Bounces: 20),
        _ => default,
    };

    /// <summary>
    /// 已知弹幕类型的行为表。追踪 / 反弹条目见 <see cref="HomingOrBounceBehaviorOf"/>；
    /// 其余类型由 <see cref="ProjectileCapabilityTable"/>（据原版 <c>Projectile.SetDefaults</c> 逐类提取）驱动：
    /// 图格碰撞 = 非 <c>NoTileCollide</c> 例外；每 tick 积分次数 = extraUpdates + 1（原版 <c>extraUpdates</c>）。
    /// </summary>
    private static ProjectileBehavior ProjectileBehaviorOf(int type)
    {
        var curated = HomingOrBounceBehaviorOf(type);
        if (curated != default)
            return curated;

        bool collide = !ProjectileCapabilityTable.NoTileCollide.Contains(type);
        int updates = ProjectileCapabilityTable.ExtraUpdates.TryGetValue(type, out var extraUpdates) ? extraUpdates + 1 : 1;
        return new ProjectileBehavior(collide, Gravity: 0f, updates, MaxLifetime: 0);
    }

    /// <summary>追踪目标快照（每 tick 在 ProjectilesLock 之外刷新，避免锁序反向死锁）。</summary>
    private Vector2[] _homingTargets = Array.Empty<Vector2>();

    /// <summary>
    /// 收集敌对 NPC 中心点快照，供追踪弹幕选择目标。必须在 <c>ProjectilesLock</c> 之外调用
    /// （内含 <c>NpcsLock</c>；而 <c>SimulateAi</c> 已是 NpcsLock→ProjectilesLock 顺序，反向会死锁）。
    /// </summary>
    private void RefreshProjectileTargets()
    {
        lock (_world.NpcsLock)
        {
            if (_world.Npcs.Count == 0)
            {
                _homingTargets = Array.Empty<Vector2>();
                return;
            }

            var list = new List<Vector2>(_world.Npcs.Count);
            foreach (var n in _world.Npcs)
            {
                if (!n.Active || n.IsTownNpc) continue;
                var (w, h) = NpcSizes.Of(n.Type);
                list.Add(new Vector2(n.X + w / 2f, n.Y + h / 2f));
            }
            _homingTargets = list.ToArray();
        }
    }

    /// <summary>追踪：把当前速度向量向最近敌怪旋转（每子步最多 <see cref="ProjectileBehavior.HomingTurn"/> 弧度）。</summary>
    private void ApplyHoming(ProjectileEntity p, in ProjectileBehavior b)
    {
        if (!b.Homing || _homingTargets.Length == 0)
            return;

        float half = p.Width / 2f;
        float pcx = p.Position.X + half, pcy = p.Position.Y + half;

        float bestSq = b.HomingRange * b.HomingRange;
        float bestX = 0f, bestY = 0f;
        bool found = false;
        foreach (var t in _homingTargets)
        {
            float dx = t.X - pcx, dy = t.Y - pcy;
            float sq = dx * dx + dy * dy;
            if (sq <= bestSq) { bestSq = sq; bestX = dx; bestY = dy; found = true; }
        }
        if (!found) return;

        float speed = MathF.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y);
        if (speed <= 0.0001f) return;

        float curX = p.Velocity.X / speed, curY = p.Velocity.Y / speed;
        float bestLen = MathF.Sqrt(bestX * bestX + bestY * bestY);
        if (bestLen <= 0.0001f) return;
        float desX = bestX / bestLen, desY = bestY / bestLen;

        float desiredAngle = MathF.Atan2(desY, desX);
        float curAngle = MathF.Atan2(curY, curX);
        float diff = MathF.Atan2(MathF.Sin(desiredAngle - curAngle), MathF.Cos(desiredAngle - curAngle));
        float step = Math.Clamp(diff, -b.HomingTurn, b.HomingTurn);
        float newAngle = curAngle + step;
        p.Velocity = new Vector2(MathF.Cos(newAngle) * speed, MathF.Sin(newAngle) * speed);
    }

    /// <summary>弹幕碰撞盒左上角 (x,y) 是否触到实心图格 / 出界。</summary>
    private bool BoxTouchesSolid(float x, float y, float w, float h)
    {
        int x0 = (int)(x / TileSize);
        int y0 = (int)(y / TileSize);
        int x1 = (int)((x + w - 0.001f) / TileSize);
        int y1 = (int)((y + h - 0.001f) / TileSize);

        if (x0 < 0 || y0 < 0 || x0 >= _world.Tiles.Width || y0 >= _world.Tiles.Height) return true;
        if (x1 < 0 || y1 < 0 || x1 >= _world.Tiles.Width || y1 >= _world.Tiles.Height) return true;

        for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
                if (NpcTileSolid(tx, ty))
                    return true;
        return false;
    }

    /// <summary>
    /// 按行为表推进一枚弹幕：按 <c>UpdatesPerTick</c> 次积分位置（原版 <c>extraUpdates</c>），
    /// 期间做图格碰撞（<c>TileCollide</c>）——轴向分离检测，命中实心图格时若有反弹预算则反射速度，
    /// 否则失效；随后引力 / 追踪（<c>Homing</c>）。返回 <c>true</c> 表示撞图格 / 出界 / 反弹预算耗尽，应失效。
    /// </summary>
    private bool StepProjectile(ProjectileEntity p, in ProjectileBehavior behavior)
    {
        // 首次遇到反弹型：填充原版弹射预算（存活期间只消耗一次）。
        if (behavior.Bounces > 0 && p.BouncesLeft < 0)
            p.BouncesLeft = behavior.Bounces;

        float w = p.Width, h = p.Height;

        for (int i = 0; i < behavior.UpdatesPerTick; i++)
        {
            if (behavior.Gravity != 0f)
                p.Velocity = new Vector2(p.Velocity.X, p.Velocity.Y + behavior.Gravity);

            if (behavior.TileCollide)
            {
                // X 轴：水平移动，保持 Y。
                float nx = p.Position.X + p.Velocity.X;
                if (BoxTouchesSolid(nx, p.Position.Y, w, h))
                {
                    if (p.BouncesLeft > 0) { p.Velocity = new Vector2(-p.Velocity.X, p.Velocity.Y); nx = p.Position.X + p.Velocity.X; p.BouncesLeft--; }
                    else return true;
                }

                // Y 轴：在反射后的 X 基础上竖直移动。
                float ny = p.Position.Y + p.Velocity.Y;
                if (BoxTouchesSolid(nx, ny, w, h))
                {
                    if (p.BouncesLeft > 0) { p.Velocity = new Vector2(p.Velocity.X, -p.Velocity.Y); ny = p.Position.Y + p.Velocity.Y; p.BouncesLeft--; }
                    else return true;
                }

                p.Position = new Vector2(nx, ny);
            }
            else
            {
                p.Position = new Vector2(p.Position.X + p.Velocity.X, p.Position.Y + p.Velocity.Y);
            }

            if (behavior.Homing)
                ApplyHoming(p, in behavior);
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

                if (!BoxesOverlap(p.Position.X, p.Position.Y, p.Width, p.Height,
                                  px, py, NpcSizes.PlayerWidth, NpcSizes.PlayerHeight))
                    continue;

                best = Math.Max(best, p.Damage);
            }
        }

        return best;
    }
}
