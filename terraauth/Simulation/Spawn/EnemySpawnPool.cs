// TerraAuth — 原版 NPC.Spawner.SpawnAnNPC 的刷怪池（服务端权威）
//
// 权威来源：原版 <c>NPC.Spawner.SpawnAnNPC</c> 的 if/else 链（Terraria 1.4.5.8 / Protocol 326）。
// 分支顺序、随机门限（<c>Main.rand.Next(n) == 0</c>）、权重数组（<c>Utils.SelectRandom</c> 的重复项）
// 均按原版逐条对照；负 netID 变体（如 -3 绿史莱姆）原样保留。
//
// 与本文件相关的原版量：
//   surfaceSpawn      = spawnTileY <= worldSurface
//   underGround       = spawnTileY <= rockLayer（经 surfaceSpawn 判定后实际只剩泥土层）
//   deeperThanRockLayer = spawnTileY >= rockLayer
//   waterTile         = 落点上方两格均有水
//
// 刻意未纳入的分支（均为**小动物（critter）**，服务端尚无对应 aiStyle，刷出来会以敌怪 AI 行动）：
//   蝴蝶 / 鸟 / 松鼠 / 兔 / 企鹅 / 海鸥 / 蜻蜓 / 萤火虫 / 圣甲虫 / 松露虫 / 熔岩钓饵 / 金小动物，
//   以及万圣节 / 圣诞节的季节性换皮（我们尚未跟踪节日）。这些分支原样跳过，流程落到同层的敌怪兜底。
// 其余未建模项：remixWorld / drunkWorld / dualDungeons / Skyblock 种子变体、四柱区域、撒旦军队、
//   南瓜月 / 霜月、花岗岩与大理石环境怪（世界暂无这两类图格）、骷髅商人与旅商等 NPC 解锁位。

using System;
using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary><see cref="EnemySpawnPool.Pick"/> 的输入。</summary>
public struct EnemySpawnContext
{
    /// <summary>玩家中心扫描得到的生物群系标志（原版 <c>player.Zone*</c>）。</summary>
    public SceneZones Zones;

    /// <summary>Boss 进度（原版 <c>downedXxx</c> 系列）。</summary>
    public WorldProgress Progress;

    /// <summary>现有 NPC 列表（<c>AnyNPCs</c> / <c>CountNPCS</c> 用）。</summary>
    public IReadOnlyList<WorldNpc> Npcs;

    /// <summary>洞穴杂物怪表（原版 <c>NPC.cavernMonsterType[2,3]</c>，按世界 ID 生成）。</summary>
    public int[] CavernMonsterTypes;

    public bool DayTime;
    public bool BloodMoon;
    public bool Eclipse;
    public bool HardMode;

    /// <summary>专家及以上难度（原版 <c>Main.expertMode</c>）。</summary>
    public bool ExpertMode;

    public bool Raining;

    /// <summary>入侵中（原版 <c>invaders</c>）。</summary>
    public bool Invaders;

    /// <summary>入侵类型（原版 <c>Main.invasionType</c>）。</summary>
    public int InvasionType;

    /// <summary>落点上方两格均为水（原版 <c>waterTile</c>）。</summary>
    public bool WaterTile;

    /// <summary>落点处于房屋墙内 → 不刷蠕虫（原版 <c>noWorms</c>）。</summary>
    public bool NoWorms;

    /// <summary>高空刷怪（原版 <c>skyMob</c>，天空层随机命中）。</summary>
    public bool SkyMob;

    public int MoonPhase;

    /// <summary>当日时间 0..54000（原版 <c>Main.time</c>；清晨 / 白天的临界值用于鸟的出现）。</summary>
    public double TimeOfDay;

    /// <summary>目标风速（原版 <c>Main.windSpeedTarget</c>；|值| ≥ 0.4 时原版认为"风太大，没有蝴蝶"）。</summary>
    public float WindSpeedTarget;

    /// <summary>落点图格 X / Y。</summary>
    public int SpawnTileX;
    public int SpawnTileY;

    /// <summary>落点图格类型（原版 <c>tileType</c> = 脚下实地那一格的类型）。</summary>
    public ushort GroundTileType;

    /// <summary>落点图格墙类型。</summary>
    public ushort WallType;

    public int MaxTilesX;
    public int MaxTilesY;

    /// <summary>世界出生点图格 X（原版 <c>Main.spawnTileX</c>，用于离出生点距离判定）。</summary>
    public int WorldSpawnTileX;

    public double WorldSurface;
    public double RockLayer;

    /// <summary>在线玩家数（原版 <c>numberOfActivePlayers</c>，仅用于火把僵尸概率）。</summary>
    public int ActivePlayers;

    /// <summary>目标玩家最大生命 ≤ 100（原版 <c>playerHasStartingHealth</c>）。</summary>
    public bool PlayerHasStartingHealth;
}

/// <summary>原版 <c>SpawnAnNPC</c> 的落点类型选择。</summary>
public static class EnemySpawnPool
{
    /// <summary>
    /// 按原版分支顺序挑一个 netID（可能为负，表示体型 / 配色变体）。
    /// 返回 0 表示本次不刷（原版没有这个出口；仅在我们主动略过某个分支时出现）。
    /// </summary>
    public static int Pick(IRng rng, in EnemySpawnContext c)
    {
        bool underGround = c.SpawnTileY <= c.RockLayer;
        bool surfaceSpawn = c.SpawnTileY <= c.WorldSurface;

        if (c.SkyMob) return PickSky(rng, in c);
        if (c.Invaders) return PickInvasion(rng, in c);
        if (PickUndergroundDesert(rng, in c, out int desert)) return desert;

        if (surfaceSpawn && c.DayTime && c.Eclipse) return PickEclipse(rng, in c);
        if (surfaceSpawn) return PickSurface(rng, in c);
        if (underGround) return PickDirtLayer(rng, in c);
        if (c.SpawnTileY > c.MaxTilesY - 190) return PickUnderworld(rng, in c);
        return PickRockLayer(rng, in c);
    }

    // ---------------------------------------------------------------- 高空

    /// <summary>天空层（原版 1420-1462）：火星探测器 / 飞龙 / 哈比。</summary>
    private static int PickSky(IRng rng, in EnemySpawnContext c)
    {
        bool sideBand = Math.Abs(c.SpawnTileX - c.MaxTilesX / 2) / (float)(c.MaxTilesX / 2) > 0.33f;

        if (sideBand && c.HardMode && c.Progress.DownedGolemBoss
            && ((!c.Progress.DownedMartians && Roll(rng, 8)) || Roll(rng, 30)) && !AnyNpc(c, 399))
        {
            return 399; // MartianProbe
        }
        if (c.HardMode && !AnyNpc(c, 87) && !c.NoWorms && Roll(rng, 10)) return 87; // WyvernHead
        if (!c.Progress.UnlockedSlimePurpleSpawn && Roll(rng, 25) && !AnyNpc(c, 686)) return 686; // BoundTownSlimePurple
        return 48; // Harpy
    }

    // ---------------------------------------------------------------- 入侵

    /// <summary>入侵（原版 1463-1620）：按 <c>Main.invasionType</c> 分派。</summary>
    private static int PickInvasion(IRng rng, in EnemySpawnContext c)
    {
        switch (c.InvasionType)
        {
            case 1: // 哥布林
                if (c.HardMode && !AnyNpc(c, 471) && Roll(rng, 30)) return 471; // GoblinSummoner
                if (Roll(rng, 9)) return 29;   // GoblinSorcerer
                if (Roll(rng, 5)) return 26;   // GoblinPeon
                if (Roll(rng, 3)) return 111;  // GoblinArcher
                if (Roll(rng, 3)) return 27;   // GoblinThief
                return 28;                     // GoblinWarrior

            case 2: // 雪人军团
                if (Roll(rng, 7)) return 145;  // SnowBalla
                if (Roll(rng, 3)) return 143;  // SnowmanGangsta
                return 144;                    // MisterStabby

            case 3: // 海盗
                if (Roll(rng, 20) && !AnyNpc(c, 491)) return 491; // PirateShip
                if (Roll(rng, 5)) return 216;  // PirateCaptain
                if (Roll(rng, 3)) return 213;  // PirateCorsair
                if (Roll(rng, 2)) return 212;  // PirateDeckhand
                return 214;                    // PirateDeadeye

            case 4: // 火星暴乱
                if (Roll(rng, 20) && !AnyNpc(c, 520)) return 520; // MartianWalker
                if (Roll(rng, 4)) return 391;  // Scutlix
                if (Roll(rng, 3)) return 389;  // GigaZapper
                if (Roll(rng, 2)) return 385;  // GrayGrunt
                return 386;                    // MartianEngineer

            default:
                return 0;
        }
    }

    // ------------------------------------------------------------ 地下沙漠

    /// <summary>
    /// 地下沙漠（原版 1719-1800）。触发条件是落点附近存在「允许地下沙漠敌怪生成的墙」，世界暂无该图格，
    /// 但保留完整实现以便后续地形专项接入。
    /// </summary>
    private static bool PickUndergroundDesert(IRng rng, in EnemySpawnContext c, out int netId)
    {
        netId = 0;
        bool inDesert = c.Zones.UndergroundDesert || BiomeScanner.AllowsUndergroundDesertEnemies(c.WallType);
        if (!inDesert || c.SpawnTileY <= c.WorldSurface) return false;

        float factor = 1.3f;
        if (c.SpawnTileY > (c.RockLayer * 2.0 + c.MaxTilesY) / 3.0) factor *= 0.5f;
        else if (c.SpawnTileY > c.RockLayer) factor *= 0.85f;

        if (Roll(rng, 20) && !c.WaterTile && !AnyNpc(c, 589)) { netId = 589; return true; } // GolferRescue
        if (c.HardMode && Roll(rng, (int)(50f * factor)) && !c.NoWorms
            && c.SpawnTileY > c.WorldSurface + 100) { netId = 510; return true; }          // DuneSplicerHead
        if (Roll(rng, (int)(50f * factor)) && !c.NoWorms
            && c.SpawnTileY > c.WorldSurface + 100 && CountNpc(c, 513) == 0) { netId = 513; return true; } // TombCrawlerHead

        if (c.HardMode && rng.NextInt32(5) != 0)
        {
            var list = new List<int>(8);
            if (c.Zones.Corrupt) { list.Add(525); list.Add(525); }
            if (c.Zones.Crimson) { list.Add(526); list.Add(526); }
            if (c.Zones.Hallow) { list.Add(527); list.Add(527); }
            if (list.Count == 0) { list.Add(524); list.Add(524); }
            if (c.Zones.Corrupt || c.Zones.Crimson) { list.Add(533); list.Add(529); }
            else { list.Add(530); list.Add(528); }
            list.Add(532);
            netId = list[rng.NextInt32(list.Count)];
            return true;
        }

        int type = AnyOf(rng, 69, 580, 580, 580, 581);
        if (Roll(rng, 15)) type = 537;                      // SandSlime
        else if (Roll(rng, 10))
        {
            if (type == 580) type = 508;                    // GiantWalkingAntlion
            else if (type == 581) type = 509;               // GiantFlyingAntlion
        }
        netId = type;
        return true;
    }

    // ---------------------------------------------------------------- 日食

    /// <summary>日食（原版 3589-3652）：地表 + 白天。</summary>
    private static int PickEclipse(IRng rng, in EnemySpawnContext c)
    {
        bool mechAll = c.Progress.DownedMechBoss1 && c.Progress.DownedMechBoss2 && c.Progress.DownedMechBoss3;

        if (c.Progress.DownedPlantBoss && Roll(rng, 80) && !AnyNpc(c, 477)) return 477; // Mothron
        if (Roll(rng, 50) && !AnyNpc(c, 251)) return 251;                               // Eyezor
        if (c.Progress.DownedPlantBoss && Roll(rng, 5) && !AnyNpc(c, 466)) return 466;   // Psycho
        if (c.Progress.DownedPlantBoss && Roll(rng, 20) && !AnyNpc(c, 463)) return 463;  // Nailhead
        if (c.Progress.DownedPlantBoss && Roll(rng, 20) && CountNpc(c, 467) < 2) return 467; // DeadlySphere
        if (Roll(rng, 15)) return 159;                       // Vampire
        if (mechAll && Roll(rng, 13)) return 253;            // Reaper
        if (Roll(rng, 8)) return 469;                        // ThePossessed
        if (c.Progress.DownedPlantBoss && Roll(rng, 7)) return 468; // DrManFly
        if (c.Progress.DownedPlantBoss && Roll(rng, 5)) return 460; // Butcher
        if (Roll(rng, 4)) return 162;                        // Frankenstein
        if (Roll(rng, 3)) return 461;                        // CreatureFromTheDeep
        if (Roll(rng, 2)) return 462;                        // Fritz
        return 166;                                          // SwampThing
    }

    // ---------------------------------------------------------------- 地表

    /// <summary>地表（原版 4205-4853）：白天→史莱姆兜底；夜晚→僵尸 / 恶魔眼 / 血月。</summary>
    private static int PickSurface(IRng rng, in EnemySpawnContext c)
    {
        // 墓地 + 泥土/草 → 蛆 / 鼠（小动物，跳过）；雪原下雨 → 冰魔像；雨天 → 愤怒雨云
        if (c.Zones.Snow && c.HardMode && c.Raining && !AnyNpc(c, 243) && Roll(rng, 20)) return 243; // IceGolem
        if (!c.Zones.Snow && c.HardMode && c.Raining && CountNpc(c, 250) < 2 && Roll(rng, 10)) return 250; // AngryNimbus
        if (c.HardMode && c.Progress.DownedGolemBoss
            && ((!c.Progress.DownedMartians && Roll(rng, 100)) || Roll(rng, 400)) && !AnyNpc(c, 399))
        {
            return 399; // MartianProbe
        }

        // ---- 白天：原版这一段以「小动物」为主，敌怪只有史莱姆兜底 ----
        if (!c.Zones.Graveyard && c.DayTime)
        {
            int distFromSpawn = Math.Abs(c.SpawnTileX - c.WorldSpawnTileX);
            bool critterGround = c.GroundTileType is 2 or 477 or 109 or 492 or 147 or 161;
            bool tooWindy = Math.Abs(c.WindSpeedTarget) >= 0.4f; // 原版 NPC.TooWindyForButterflies

            // 1/15：地表小动物（原版 4242-4345）。蝴蝶 / 椿象由原版 setFireFlyChance 的周期决定，
            // 同一周期二者只开一个；此处按「每次尝试重掷周期」近似（分布一致，见 RollCritterChances）。
            if (!c.WaterTile && critterGround && distFromSpawn < c.MaxTilesX / 2 && Roll(rng, 15))
            {
                if (c.GroundTileType is 147 or 161)
                    return rng.NextInt32(2) == 0 ? 148 : 149; // Penguin

                (_, int butterfly, int stinkBug) = RollCritterChances(rng);
                if (!tooWindy && !c.Raining && stinkBug > 0 && Roll(rng, stinkBug)) return 669;   // Stinkbug
                if (!tooWindy && !c.Raining && butterfly > 0 && Roll(rng, butterfly))
                    return Roll(rng, 400) ? 444 : 356;                                             // GoldButterfly / Butterfly
                if (tooWindy && !c.Raining && butterfly > 0 && Roll(rng, butterfly * 2))
                    return Roll(rng, 400) ? 605 : 604;                                             // GoldLadyBug / LadyBug
                if (Roll(rng, 400)) return 443;                                                    // GoldBunny
                if (Roll(rng, 400) && c.SpawnTileY <= c.WorldSurface) return 539;                   // GoldSquirrel
                if (Roll(rng, 3) && c.SpawnTileY <= c.WorldSurface)
                    return rng.NextInt32(2) == 0 ? 299 : 538;                                      // Squirrel / SquirrelRed
                return 46;                                                                          // Bunny
            }

            // 清晨（time < 18000）的鸟群：需同屏鸟 < 6（原版 4365-4406）
            if (!c.WaterTile && distFromSpawn < c.MaxTilesX / 3 && c.TimeOfDay < 18000
                && c.GroundTileType is 2 or 477 or 109 or 492 && c.SpawnTileY <= c.WorldSurface
                && CountBirds(in c) < 6 && (Roll(rng, 4) || Roll(rng, 15)))
            {
                if (Roll(rng, 400)) return 442;                         // GoldBird
                return rng.NextInt32(4) switch { 0 => 297, 1 => 298, _ => 74 }; // BirdBlue / BirdRed / Bird
            }

            if (c.WaterTile) return 0; // 水中生物（鱼 / 海龟等）未建模
            return GetBasicSlimeToSpawn(rng, surface: true, c.GroundTileType, distFromSpawn, c.ExpertMode);
        }

        // ---- 夜晚 / 墓地 ----
        bool tooWindyAtNight = Math.Abs(c.WindSpeedTarget) >= 0.4f;

        // 萤火虫（原版 4550）：夜晚 + 无风 + 无雨 + 地表
        if (!c.Zones.Graveyard && !tooWindyAtNight && !c.Raining && c.SpawnTileY <= c.WorldSurface
            && c.GroundTileType is 2 or 477 or 109 or 492)
        {
            (int firefly, _, _) = RollCritterChances(rng);
            if (firefly > 0 && Roll(rng, firefly)) return c.GroundTileType == 109 ? 358 : 355; // LightningBug / Firefly
        }

        // 恶魔眼家族（原版 4591）：1/6（月相 4 时 1/2）进入判定，进入后再 1/2 才真的出眼
        if (Roll(rng, 6) || (c.MoonPhase == 4 && Roll(rng, 2)))
        {
            if (c.HardMode && Roll(rng, 3)) return 133;                 // WanderingEye
            if (Roll(rng, 2)) return Roll(rng, 4) ? -43 : 2;             // DemonEye（含变体）
        }

        if (c.HardMode && Roll(rng, 50) && c.BloodMoon && !AnyNpc(c, 109)) return 109;  // Clown
        if (Roll(rng, 300) && (c.BloodMoon || c.Zones.Graveyard)) return 53;            // TheGroom
        if (Roll(rng, 300) && (c.BloodMoon || c.Zones.Graveyard)) return 536;           // TheBride
        if (!c.DayTime && c.MoonPhase == 0 && c.HardMode && rng.NextInt32(3) != 0) return 104; // Werewolf
        if (!c.DayTime && c.HardMode && Roll(rng, 3)) return 140;                       // PossessedArmor
        if (c.BloodMoon && rng.NextInt32(5) < 2) return rng.NextInt32(2) == 0 ? 489 : 490; // BloodZombie / Drippler

        if (IsSnowTile(c.GroundTileType) || c.GroundTileType == 162)
        {
            if (!c.Zones.Graveyard && c.HardMode && Roll(rng, 4)) return 169;  // IceElemental
            if (!c.Zones.Graveyard && c.HardMode && Roll(rng, 3)) return 155;  // Wolf
            if (c.ExpertMode && Roll(rng, 2)) return 431;                      // ArmedZombieEskimo
            return 161;                                                        // ZombieEskimo
        }

        if (c.Raining && rng.NextInt32(2) == 0)
        {
            if (rng.NextInt32(3) != 0) return 223;                             // ZombieRaincoat
            return rng.NextInt32(2) == 0 ? -54 : -55;                          // 小 / 大 雨衣僵尸
        }

        if (c.Zones.Graveyard)
        {
            if (Roll(rng, 30)) return 691;                                     // MossZombie
            if (Roll(rng, 20)) return 632;                                     // MaggotZombie
        }

        int torchZombieChance = 12;
        if (c.PlayerHasStartingHealth)
            torchZombieChance = Math.Max(2, 5 - c.ActivePlayers / 2);
        if (Roll(rng, torchZombieChance)) return c.ExpertMode && Roll(rng, 2) ? 591 : 590; // ArmedTorchZombie / TorchZombie

        return PickZombie(rng, in c);
    }

    /// <summary>
    /// 僵尸兜底（原版 4781-4853）：<c>zombieStyle = rand.Next(7)</c> 决定基础型与大小变体
    /// （<c>-26..-45</c> 为原版「小 / 大」负 netID）。
    /// </summary>
    private static int PickZombie(IRng rng, in EnemySpawnContext c)
    {
        int style = rng.NextInt32(7);

        // 专家及以上：1/3 概率换成持械僵尸（style 1 无持械变体）
        if (c.ExpertMode && style != 1 && Roll(rng, 3))
        {
            return style switch
            {
                2 => 432,
                3 => 433,
                4 => 434,
                5 => 435,
                6 => 436,
                _ => 430,
            };
        }

        (int Base, int Small, int Big) row = style switch
        {
            1 => (132, -28, -29), // BaldZombie
            2 => (186, -30, -31), // PincushionZombie
            3 => (187, -32, -33), // SlimedZombie
            4 => (188, -34, -35), // SwampZombie
            5 => (189, -36, -37), // TwiggyZombie
            6 => (200, -44, -45), // FemaleZombie
            _ => (3, -26, -27),   // Zombie
        };

        return Roll(rng, 3) ? (rng.NextInt32(2) == 0 ? row.Big : row.Small) : row.Base;
    }

    // -------------------------------------------------------------- 泥土层

    /// <summary>泥土层（原版 4855-4907）：蠕虫 / 盔甲僵尸 / 毒泥 / 史莱姆。</summary>
    private static int PickDirtLayer(IRng rng, in EnemySpawnContext c)
    {
        if (!c.NoWorms && Roll(rng, 50) && !c.Zones.Snow)
        {
            if (!c.HardMode) return 10;                       // GiantWormHead
            return rng.NextInt32(3) != 0 ? 95 : 10;           // DiggerHead / GiantWormHead
        }
        if (c.HardMode && Roll(rng, 3)) return 140;           // PossessedArmor
        if (c.HardMode && Roll(rng, 2)) return 141;           // ToxicSludge
        if (c.GroundTileType is 147 or 161 || c.Zones.Snow) return 147; // IceSlime
        return GetBasicSlimeToSpawn(rng, surface: false, c.GroundTileType, -1, c.ExpertMode);
    }

    // ---------------------------------------------------------------- 地狱

    /// <summary>地狱层（原版 4908-4957）：熔岩史莱姆 / 恶魔 / 骨蛇 / 火焰小鬼。</summary>
    private static int PickUnderworld(IRng rng, in EnemySpawnContext c)
    {
        if (c.HardMode && Roll(rng, 20) && !AnyNpc(c, 534)) return 534;  // DemonTaxCollector
        if (Roll(rng, 40) && !AnyNpc(c, 39)) return 39;                 // BoneSerpentHead
        if (Roll(rng, 14)) return 24;                                   // FireImp
        if (Roll(rng, 7))
        {
            if (Roll(rng, 10)) return 66;                               // VoodooDemon
            if (c.HardMode && c.Progress.DownedMechBossAny && rng.NextInt32(5) != 0) return 156; // RedDevil
            return 62;                                                  // Demon
        }
        if (Roll(rng, 3)) return 59;                                    // LavaSlime
        if (c.HardMode && c.Progress.DownedMechBossAny && rng.NextInt32(5) != 0) return 151; // Lavabat
        return 60;                                                       // Hellbat
    }

    // ---------------------------------------------------------------- 岩层

    /// <summary>岩层（原版 4958-5279）：洞穴怪主力分支。</summary>
    private static int PickRockLayer(IRng rng, in EnemySpawnContext c)
    {
        // 洞穴甲虫（原版 4962）：217 / 218 不在 NPCID.Sets.CountsAsCritter 内，是独立小怪
        if (Roll(rng, 60)) return c.Zones.Snow ? 218 : 217;

        if ((c.GroundTileType is 116 or 117 or 164) && c.HardMode && !c.NoWorms && Roll(rng, 8))
            return 120; // ChaosElemental

        bool snowTile = c.GroundTileType is 147 or 161 or 162 or 163 or 164 or 200;
        // 原版在本分支之后改用更窄的冰雪图格集合（147 / 161 / 162）判定冰系换皮
        bool narrowSnow = c.GroundTileType is 147 or 161 or 162;
        if (snowTile && !c.NoWorms && c.HardMode && c.Zones.Corrupt && Roll(rng, 30)) return 170; // PigronCorruption
        if (snowTile && !c.NoWorms && c.HardMode && c.Zones.Hallow && Roll(rng, 30)) return 171;  // PigronHallow
        if (snowTile && !c.NoWorms && c.HardMode && c.Zones.Crimson && Roll(rng, 30)) return 180; // PigronCrimson
        if (c.HardMode && c.Zones.Snow && Roll(rng, 10)) return 154;                              // IceTortoise

        if (!c.NoWorms && Roll(rng, 100) && !c.Zones.Hallow)
        {
            if (c.HardMode) return 95;                       // DiggerHead
            return c.Zones.Snow ? 185 : 10;                  // SnowFlinx / GiantWormHead
        }

        // 雪原小动物：雪貂（原版 5008）
        if (c.Zones.Snow && Roll(rng, 20)) return 185;

        if (((!c.HardMode && Roll(rng, 10)) || (c.HardMode && Roll(rng, 20))))
        {
            if (c.Zones.Snow || c.GroundTileType is 161 or 147) return 184; // SpikedIceSlime
            return rng.NextInt32(3) == 0 ? -6 : 16;                         // 黑史莱姆 / 史莱姆之母
        }

        if (!c.HardMode && rng.NextInt32(4) == 0)
        {
            if (c.Zones.Jungle) return -10;                  // JungleSlime
            if (c.Zones.Snow || c.GroundTileType is 161 or 147) return 184;
            return -6;                                        // BlackSlime
        }

        if (rng.NextInt32(2) == 0) return PickRockLayerCave(rng, in c, snowTile);

        if (c.HardMode && c.Zones.Hallow && rng.NextInt32(2) == 0) return 138; // IlluminantSlime
        if (c.Zones.Jungle) return 51;                                        // JungleBat
        if (c.Zones.Glowshroom && c.GroundTileType is 70 or 190) return 634;   // SporeBat
        if (c.HardMode && c.Zones.Hallow) return 137;                          // IlluminantBat
        if (c.HardMode && rng.NextInt32(6) > 0)
            return rng.NextInt32(3) == 0 && narrowSnow ? 150 : 93;                // IceBat / GiantBat
        if (narrowSnow) return c.HardMode ? 169 : 150;                            // IceElemental / IceBat
        return 49;                                                                // CaveBat
    }

    /// <summary>岩层「大分支」（原版 5042-5236）：稀有 NPC + 骷髅家族。</summary>
    private static int PickRockLayerCave(IRng rng, in EnemySpawnContext c, bool snowTile)
    {
        if (Roll(rng, 35) && !c.WaterTile && CountNpc(c, 453) == 0) return 453; // SkeletonMerchant
        if (Roll(rng, 80)) return 195;                                          // LostGirl
        if (c.HardMode && Roll(rng, 200)) return 172;                           // RuneWizard

        if (c.HardMode && rng.NextInt32(10) != 0)
        {
            return rng.NextInt32(2) == 0
                ? (c.Zones.Snow ? 197 : 77)    // ArmoredViking / ArmoredSkeleton
                : (c.Zones.Snow ? 206 : 110);  // IcyMerman / SkeletonArcher
        }

        if (!c.NoWorms && c.Zones.Graveyard && Roll(rng, 30)) return 316;        // Ghost
        if (Roll(rng, 20)) return 44;                                           // UndeadMiner

        if (c.GroundTileType is 147 or 161 or 162) return Roll(rng, 15) ? 185 : 167; // SnowFlinx / UndeadViking
        if (c.Zones.Snow) return 185;                                           // SnowFlinx

        if (Roll(rng, 3)) return CavernMonster(rng, in c);

        if (c.Zones.Glowshroom && c.GroundTileType is 70 or 190) return 635;     // SporeSkeleton

        // 专家：1/3 → 投骨骷髅。原版此处是 <c>num56 == 0</c> 的连续判断（后两个分支不可达），
        // 实际行为：num56 == 0 → 449，其余 → 452，这里按实际行为实现。
        if (c.ExpertMode && Roll(rng, 3)) return rng.NextInt32(4) == 0 ? 449 : 452;

        switch (rng.NextInt32(4))
        {
            case 0: return rng.NextInt32(3) != 0 ? 21 : (rng.NextInt32(2) == 0 ? -47 : -46);   // Skeleton
            case 1: return rng.NextInt32(3) != 0 ? 201 : (rng.NextInt32(2) == 0 ? -49 : -48);  // HeadacheSkeleton
            case 2: return rng.NextInt32(3) != 0 ? 202 : (rng.NextInt32(2) == 0 ? -51 : -50);  // MisassembledSkeleton
            default: return rng.NextInt32(3) != 0 ? 203 : (rng.NextInt32(2) == 0 ? -53 : -52); // PantlessSkeleton
        }
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>原版 <c>Main.rand.Next(range) == 0</c>。</summary>
    private static bool Roll(IRng rng, int range) => rng.NextInt32(range) == 0;

    /// <summary>原版 <c>Utils.SelectRandom</c>（等概率，重复项即权重）。</summary>
    private static int AnyOf(IRng rng, params int[] types) => types[rng.NextInt32(types.Length)];

    /// <summary>原版 <c>setFireFlyChance</c> 的一次周期：萤火虫 / 蝴蝶 / 椿象的概率（≤ 0 表示本周期禁用）。
    /// 原版每个周期只开「蝴蝶」或「椿象」中的一个（另一个置为永不触发），这里同样保持该互斥关系。</summary>
    private static (int Firefly, int Butterfly, int StinkBug) RollCritterChances(IRng rng)
    {
        int firefly;
        if (Roll(rng, 9)) firefly = 5 + rng.NextInt32(5);
        else if (Roll(rng, 3)) firefly = 0;
        else firefly = 10 + rng.NextInt32(50);

        int butterfly, stinkBug;
        if (Roll(rng, 3))
        {
            butterfly = 0;
            stinkBug = Roll(rng, 5) ? 0 : 1 + rng.NextInt32(13);
        }
        else
        {
            stinkBug = 0;
            butterfly = Roll(rng, 5) ? 0 : 1 + rng.NextInt32(20);
        }
        return (firefly, butterfly, stinkBug);
    }

    /// <summary>原版鸟群上限：同屏鸟（74 / 297 / 298）少于 6 才会继续刷。</summary>
    private static int CountBirds(in EnemySpawnContext c)
        => CountNpc(in c, 74) + CountNpc(in c, 297) + CountNpc(in c, 298);

    private static int CountNpc(in EnemySpawnContext c, int type)
    {
        int n = 0;
        var list = c.Npcs;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Active && list[i].Type == type) n++;
        return n;
    }

    private static bool AnyNpc(in EnemySpawnContext c, int type) => CountNpc(in c, type) > 0;

    /// <summary>原版 <c>TileID.Sets.IcesSnow</c> 与 <c>tileType == 162</c>。</summary>
    private static bool IsSnowTile(ushort type) => type is 161 or 200 or 163 or 164 or 147;

    /// <summary>
    /// 原版 <c>NPC.GetBasicSlimeToSpawn</c>：地表按落点图格选变体（丛林 → 丛林史莱姆，冰雪 → 冰史莱姆），
    /// 否则绿 / 紫 / 蓝按距离与难度加权；地下为红 / 黄 / 蓝。
    /// </summary>
    private static int GetBasicSlimeToSpawn(IRng rng, bool surface, ushort tileType, int spawnDist, bool expert)
    {
        if (!surface)
        {
            // 原版嵌套：Next(5)==0 → 黄；否则 Next(2)==0 → 蓝，否则红
            return rng.NextInt32(5) == 0 ? -9 : (rng.NextInt32(2) == 0 ? 1 : -8);
        }

        switch (tileType)
        {
            case 60: return -10;                    // JungleSlime
            case 147 or 161: return 147;            // IceSlime
            default:
                if (rng.NextInt32(3) == 0 || (spawnDist < 200 && !expert)) return -3;          // GreenSlime
                if (rng.NextInt32(10) == 0 && (spawnDist > 400 || expert)) return -7;          // PurpleSlime
                return 1;                                                                      // BlueSlime
        }
    }

    /// <summary>
    /// 原版 <c>cavernMonsterType[rand.Next(2), rand.Next(3)]</c>：按世界 ID 固定的 2×3 洞穴杂物怪表。
    /// 原版用 <c>new UnifiedRandom(Main.worldID)</c> 选两类（互不相同）；我们用世界 ID 播种的 Xoshiro，
    /// 分布一致但序列不等价。
    /// </summary>
    public static int[] BuildCavernMonsterTypes(int worldId)
    {
        var rng = new XoshiroRng((ulong)worldId * 0x9E3779B97F4A7C15UL + 1);
        int a = rng.NextInt32(3);
        int b = rng.NextInt32(3);
        while (b == a) b = rng.NextInt32(3);

        var table = new int[6];
        for (int i = 0; i < 2; i++)
        {
            int category = i == 0 ? a : b;
            for (int j = 0; j < 3; j++)
                table[i * 3 + j] = category switch
                {
                    0 => rng.NextInt32(2) + 494,   // Crawdad
                    1 => rng.NextInt32(2) + 496,   // GiantShelly
                    _ => rng.NextInt32(9) + 498,   // Salamander
                };
        }
        return table;
    }

    private static int CavernMonster(IRng rng, in EnemySpawnContext c)
    {
        var table = c.CavernMonsterTypes;
        if (table is null || table.Length < 6) return 494;
        return table[rng.NextInt32(2) * 3 + rng.NextInt32(3)];
    }
}
