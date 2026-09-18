// TerraAuth — 原版 NPC.GetSpawnRate 的服务端移植
//
// 行为依据：Terraria 1.4.5.8 / 协议 326 的刷怪频率兼容性测试
//   基准 <c>defaultSpawnRate = 600</c> / <c>defaultMaxSpawns = 5</c>（NPC.cs 静态字段）
//
// 语义：每 tick 对每个在线玩家掷一次 <c>rand.Next(spawnRate) == 0</c>，成功才进入「挑落点 + 挑类型」；
// 玩家附近敌怪槽位数达到 maxSpawns 则本 tick 不再尝试。spawnRate 越小刷得越密。
//
// 与原版的差异（刻意保留）：
//   - 种子变体（remixWorld / drunkWorld / dualDungeons / Skyblock / getGoodWorld / Journey）不建模；
//   - 玩家侧增益与装饰（隐身 / 镇静 / 向日葵 / 渔夫套 / 水蜡烛 / 和平蜡烛 / 仙灵 / 战场buff）不建模，
//     故这几段速率修正不参与计算；
//   - 南瓜月 / 霜月 / 撒旦军队 / 入侵的「固定 spawnRate=20 + 配额上限」分支不在此处 —— 入侵走独立的配额路径
//     （见 <c>WorldSimulator.TrySpawnInvasionEnemy</c>）；
//   - 雪原云量修正（<c>Main.cloudAlpha</c>）不建模：我们尚未跟踪云量。

namespace TerraAuth.Simulation;

/// <summary><see cref="SpawnRate.Compute"/> 的输入（字段与原版同名量一一对应）。</summary>
public readonly record struct SpawnRateContext
{
    /// <summary>是否困难模式（原版 <c>Main.hardMode</c>）。</summary>
    public required bool HardMode { get; init; }

    /// <summary>玩家碰撞盒左上角 Y（像素）；原版用 <c>player.position.Y</c>（同样是碰撞盒左上角）。</summary>
    public required float PlayerCenterY { get; init; }

    /// <summary>地表高度（图格）。</summary>
    public required double WorldSurface { get; init; }

    /// <summary>岩层高度（图格）。</summary>
    public required double RockLayer { get; init; }

    /// <summary>地狱层起始（图格）；原版 <c>Main.UnderworldLayer</c>。</summary>
    public required int UnderworldLayer { get; init; }

    public required bool DayTime { get; init; }
    public required bool BloodMoon { get; init; }
    public required bool Eclipse { get; init; }

    /// <summary>玩家中心扫描得到的生物群系标志。</summary>
    public required SceneZones Zones { get; init; }

    /// <summary>玩家附近的城镇 NPC 数（原版 <c>player.townNPCs</c>）。</summary>
    public required int TownNpcs { get; init; }

    /// <summary>玩家附近存活敌怪槽位总数（原版 <c>player.nearbyActiveNPCs</c>）。</summary>
    public required int NearbyActiveNpcs { get; init; }

    /// <summary>入侵进行中（原版 <c>invaders</c>）：固定 <c>spawnRate = 20</c> 并放宽上限。</summary>
    public required bool Invaders { get; init; }

    /// <summary>在线玩家数（原版 <c>numberOfActivePlayers</c>）。</summary>
    public required int ActivePlayers { get; init; }

    /// <summary>同屏敌怪上限的外部上限（server.json 的 <c>MaxEnemies</c>）；≤ 0 表示不额外限制。</summary>
    public int MaxSpawnsCap { get; init; }
}

/// <summary>原版 <c>NPC.Spawner.GetSpawnRate</c> 的确定性实现。</summary>
public static class SpawnRate
{
    /// <summary>原版 <c>defaultSpawnRate</c>。</summary>
    public const int DefaultSpawnRate = 600;

    /// <summary>原版 <c>defaultMaxSpawns</c>。</summary>
    public const int DefaultMaxSpawns = 5;

    /// <summary>原版 <c>sHeight</c>：判定「地表以下」时在层高之上额外加一个屏幕高度。</summary>
    private const int ScreenHeight = 1200;

    /// <summary>按原版顺序计算本 tick 的 <c>spawnRate</c>（越小越密）与 <c>maxSpawns</c>（附近敌怪上限）。</summary>
    public static (int SpawnRate, int MaxSpawns) Compute(in SpawnRateContext c)
    {
        int spawnRate = DefaultSpawnRate;
        int maxSpawns = DefaultMaxSpawns;

        // ---- 困难模式基准 ----
        if (c.HardMode)
        {
            spawnRate = (int)(DefaultSpawnRate * 0.9);
            maxSpawns = DefaultMaxSpawns + 1;
        }

        // ---- 深度：地狱 > 岩层 > 泥土层 > 地表（昼夜）----
        float py = c.PlayerCenterY;
        if (py > c.UnderworldLayer * 16f)
        {
            maxSpawns = (int)(maxSpawns * 2f);
        }
        else if (py > c.RockLayer * 16 + ScreenHeight)
        {
            spawnRate = (int)(spawnRate * 0.4);
            maxSpawns = (int)(maxSpawns * 1.9f);
        }
        else if (py > c.WorldSurface * 16 + ScreenHeight)
        {
            if (c.HardMode)
            {
                spawnRate = (int)(spawnRate * 0.45);
                maxSpawns = (int)(maxSpawns * 1.8f);
            }
            else
            {
                spawnRate = (int)(spawnRate * 0.5);
                maxSpawns = (int)(maxSpawns * 1.7f);
            }
        }
        else if (!c.DayTime)
        {
            spawnRate = (int)(spawnRate * 0.6);
            maxSpawns = (int)(maxSpawns * 1.3f);
            if (c.BloodMoon)
            {
                spawnRate = (int)(spawnRate * 0.3);
                maxSpawns = (int)(maxSpawns * 1.8f);
            }
        }
        else if (c.DayTime && c.Eclipse)
        {
            spawnRate = (int)(spawnRate * 0.2);
            maxSpawns = (int)(maxSpawns * 1.9f);
        }

        // ---- 生物群系（互斥链，与原版一致）----
        if (c.Zones.Dungeon)
        {
            spawnRate = (int)(spawnRate * 0.3);
            maxSpawns = (int)(maxSpawns * 1.8f);
        }
        else if (c.Zones.UndergroundDesert)
        {
            spawnRate = (int)(spawnRate * 0.2f);
            maxSpawns = (int)(maxSpawns * 3f);
        }
        else if (c.Zones.Jungle)
        {
            if (c.TownNpcs == 0)
            {
                spawnRate = (int)(spawnRate * 0.4);
                maxSpawns = (int)(maxSpawns * 1.5f);
            }
            else if (c.TownNpcs == 1)
            {
                spawnRate = (int)(spawnRate * 0.55);
                maxSpawns = (int)(maxSpawns * 1.4);
            }
            else if (c.TownNpcs == 2)
            {
                spawnRate = (int)(spawnRate * 0.7);
                maxSpawns = (int)(maxSpawns * 1.3f);
            }
            else
            {
                spawnRate = (int)(spawnRate * 0.85);
                maxSpawns = (int)(maxSpawns * 1.2f);
            }
        }
        else if (c.Zones.Corrupt || c.Zones.Crimson)
        {
            spawnRate = (int)(spawnRate * 0.65);
            maxSpawns = (int)(maxSpawns * 1.3f);
        }
        else if (c.Zones.Meteor)
        {
            spawnRate = (int)(spawnRate * 0.4);
            maxSpawns = (int)(maxSpawns * 1.1f);
        }

        if (c.Zones.Hallow && py > c.RockLayer * 16 + ScreenHeight)
        {
            spawnRate = (int)(spawnRate * 0.65);
            maxSpawns = (int)(maxSpawns * 1.3f);
        }

        // ---- 附近敌怪越少刷得越快（原版两段阶梯）----
        if (c.NearbyActiveNpcs < maxSpawns * 0.2) spawnRate = (int)(spawnRate * 0.6f);
        else if (c.NearbyActiveNpcs < maxSpawns * 0.4) spawnRate = (int)(spawnRate * 0.7f);
        else if (c.NearbyActiveNpcs < maxSpawns * 0.6) spawnRate = (int)(spawnRate * 0.8f);
        else if (c.NearbyActiveNpcs < maxSpawns * 0.8) spawnRate = (int)(spawnRate * 0.9f);

        if (py / 16f > (c.WorldSurface + c.RockLayer) / 2 || c.Zones.Corrupt || c.Zones.Crimson)
        {
            if (c.NearbyActiveNpcs < maxSpawns * 0.2) spawnRate = (int)(spawnRate * 0.7f);
            else if (c.NearbyActiveNpcs < maxSpawns * 0.4) spawnRate = (int)(spawnRate * 0.9f);
        }

        // ---- 钳制（原版下限 / 上限）----
        if (spawnRate < DefaultSpawnRate * 0.1) spawnRate = (int)(DefaultSpawnRate * 0.1);
        if (maxSpawns > DefaultMaxSpawns * 3) maxSpawns = DefaultMaxSpawns * 3;

        // ---- 入侵 / 四柱：固定高频刷新与按人数放宽的上限 ----
        if (c.Invaders)
        {
            maxSpawns = (int)(DefaultMaxSpawns * (2.0 + 0.3 * c.ActivePlayers));
            spawnRate = 20;
        }

        // 项目侧配置上限（server.json 的 MaxEnemies）叠加在最终值上
        if (c.MaxSpawnsCap > 0 && maxSpawns > c.MaxSpawnsCap) maxSpawns = c.MaxSpawnsCap;

        return (spawnRate, maxSpawns);
    }
}
