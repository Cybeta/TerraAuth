// TerraAuth — Phase 6: 世界状态（权威数据模型）
// 字段来源：本地客户端原版 WorldFile.LoadHeader / LoadWorldFlags / LoadChests / LoadSigns / LoadNPCs
//   以及 NetMessage.SendData case 7（WorldData 包）的字段顺序（1.4.5.8 / Protocol 326）
// 该类型是服务端权威世界的唯一真相来源：.wld 解析 / 程序化生成均产出它，NetworkHost 由它构造出站包。

using System;
using System.Collections.Generic;
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>世界全局状态：元数据 + 图格矩阵 + 实体列表。</summary>
public sealed class WorldState
{
    // ---- Phase 3 兼容字段 ----
    public long Tick { get; set; }

    /// <summary>玩家运行时状态（服务端权威唯一真相）。</summary>
    public Dictionary<int, PlayerRuntime> Players { get; } = new();

    // ---- 图格 ----
    public TileMap Tiles { get; set; } = new(1, 1);

    /// <summary>
    /// 区块分区锁：仿真线程写图格 / 其他线程读图格（包 10 编码、权威校验）时使用。
    /// 详见 <see cref="SectionLocks"/>。
    /// </summary>
    public SectionLocks Sections { get; } = new();

    public int MaxTilesX { get; set; } = 1;
    public int MaxTilesY { get; set; } = 1;

    public int SpawnTileX { get; set; }
    public int SpawnTileY { get; set; }

    /// <summary>地表高度（图格坐标，double）。</summary>
    public double WorldSurface { get; set; }

    /// <summary>岩层高度（图格坐标，double）。</summary>
    public double RockLayer { get; set; }

    public int WorldId { get; set; }
    public string WorldName { get; set; } = "";
    public Guid UniqueId { get; set; } = Guid.Empty;
    public ulong WorldGeneratorVersion { get; set; }
    public string Seed { get; set; } = "";

    /// <summary>0=普通、1=专家、2=大师、3=旅途。</summary>
    public int GameMode { get; set; }

    // ---- 时间 / 天气 ----
    /// <summary>当日时间 0..54000。</summary>
    public double Time { get; set; }
    public bool DayTime { get; set; }
    public bool BloodMoon { get; set; }
    public bool Eclipse { get; set; }

    /// <summary>月相 0..8。</summary>
    public int MoonPhase { get; set; }
    public byte MoonType { get; set; }

    public bool Raining { get; set; }
    public int RainTime { get; set; }
    public float MaxRain { get; set; }

    public float WindSpeedTarget { get; set; }
    public int NumClouds { get; set; }
    public int CloudBgActive { get; set; }

    // ---- 背景 / 样式 ----
    /// <summary>13 个背景：treeBG1-4、corrupt、jungle、snow、hallow、crimson、desert、ocean、mushroom、underworld。</summary>
    public byte[] Backgrounds { get; set; } = new byte[13];

    public int IceBackStyle { get; set; }
    public int JungleBackStyle { get; set; }
    public int HellBackStyle { get; set; }

    public int[] TreeX { get; set; } = new int[3];
    public int[] TreeStyle { get; set; } = new int[4];
    public int[] CaveBackX { get; set; } = new int[3];
    public int[] CaveBackStyle { get; set; } = new int[4];

    /// <summary>TreeTops 13 个区域变化值（包 7 以 byte 下发）。</summary>
    public int[] TreeTops { get; set; } = new int[13];

    // ---- 进度 / 世界种子 ----
    public WorldProgress Progress { get; } = new();

    // ---- 入侵 / 沙尘暴 / 冷却 ----
    public int InvasionDelay { get; set; }
    public int InvasionSize { get; set; }
    public int InvasionSizeStart { get; set; }
    public int InvasionType { get; set; }
    public double InvasionX { get; set; }

    public float SandstormIntensity { get; set; }

    public byte SundialCooldown { get; set; }
    public byte MoondialCooldown { get; set; }

    /// <summary>7 个矿石层级：0 Copper、1 Iron、2 Silver、3 Gold、4 Cobalt、5 Mythril、6 Adamantite。</summary>
    public short[] OreTiers { get; set; } = new short[7];

    public ulong LobbyId { get; set; }

    public List<(short X, short Y)> ExtraSpawnPoints { get; } = new();

    public int DungeonX { get; set; }
    public int DungeonY { get; set; }

    // ---- 实体 ----
    public List<Chest> Chests { get; } = new();
    public List<Sign> Signs { get; } = new();
    public List<WorldNpc> Npcs { get; } = new();

    /// <summary>NPC 列表的跨线程保护：仿真线程负责增删，世界同步线程负责遍历下发。</summary>
    public object NpcsLock { get; } = new();

    // ---- 掉落物 / 弹幕（服务端权威实体）----

    /// <summary>世界掉落物（原版 <c>Main.item[]</c>）。</summary>
    public List<WorldItemEntity> Items { get; } = new();

    /// <summary>弹幕（原版 <c>Main.projectile[]</c>）。</summary>
    public List<ProjectileEntity> Projectiles { get; } = new();

    /// <summary>掉落物列表的跨线程保护。</summary>
    public object ItemsLock { get; } = new();

    /// <summary>弹幕列表的跨线程保护。</summary>
    public object ProjectilesLock { get; } = new();

    /// <summary>按包 7 布局构造 <see cref="WorldInfoPacket"/>。</summary>
    public WorldInfoPacket ToWorldInfoPacket()
    {
        var p = Progress;
        var flags = new byte[11];

        byte b4 = 0;
        if (p.ShadowOrbSmashed) b4 |= 1 << 0;
        if (p.DownedBoss1) b4 |= 1 << 1;
        if (p.DownedBoss2) b4 |= 1 << 2;
        if (p.DownedBoss3) b4 |= 1 << 3;
        if (p.HardMode) b4 |= 1 << 4;
        if (p.DownedClown) b4 |= 1 << 5;
        if (p.DownedPlantBoss) b4 |= 1 << 7;
        flags[0] = b4;

        byte b5 = 0;
        if (p.DownedMechBoss1) b5 |= 1 << 0;
        if (p.DownedMechBoss2) b5 |= 1 << 1;
        if (p.DownedMechBoss3) b5 |= 1 << 2;
        if (p.DownedMechBossAny) b5 |= 1 << 3;
        if (CloudBgActive >= 1) b5 |= 1 << 4;
        if (p.Crimson) b5 |= 1 << 5;
        if (p.PumpkinMoon) b5 |= 1 << 6;
        if (p.SnowMoon) b5 |= 1 << 7;
        flags[1] = b5;

        byte b6 = 0;
        if (p.FastForwardTimeToDawn) b6 |= 1 << 1;
        if (p.SlimeRain) b6 |= 1 << 2;
        if (p.DownedSlimeKing) b6 |= 1 << 3;
        if (p.DownedQueenBee) b6 |= 1 << 4;
        if (p.DownedFishron) b6 |= 1 << 5;
        if (p.DownedMartians) b6 |= 1 << 6;
        if (p.DownedAncientCultist) b6 |= 1 << 7;
        flags[2] = b6;

        byte b7 = 0;
        if (p.DownedMoonlord) b7 |= 1 << 0;
        if (p.DownedHalloweenKing) b7 |= 1 << 1;
        if (p.DownedHalloweenTree) b7 |= 1 << 2;
        if (p.DownedChristmasIceQueen) b7 |= 1 << 3;
        if (p.DownedChristmasSantank) b7 |= 1 << 4;
        if (p.DownedChristmasTree) b7 |= 1 << 5;
        if (p.DownedGolemBoss) b7 |= 1 << 6;
        if (p.PartyIsUp) b7 |= 1 << 7;
        flags[3] = b7;

        byte b8 = 0;
        if (p.DownedPirates) b8 |= 1 << 0;
        if (p.DownedFrost) b8 |= 1 << 1;
        if (p.DownedGoblins) b8 |= 1 << 2;
        if (p.SandstormHappening) b8 |= 1 << 3;
        if (p.Dd2Ongoing) b8 |= 1 << 4;
        if (p.Dd2DownedInvasionT1) b8 |= 1 << 5;
        if (p.Dd2DownedInvasionT2) b8 |= 1 << 6;
        if (p.Dd2DownedInvasionT3) b8 |= 1 << 7;
        flags[4] = b8;

        byte b9 = 0;
        if (p.CombatBookWasUsed) b9 |= 1 << 0;
        if (p.LanternsUp) b9 |= 1 << 1;
        if (p.DownedTowerSolar) b9 |= 1 << 2;
        if (p.DownedTowerVortex) b9 |= 1 << 3;
        if (p.DownedTowerNebula) b9 |= 1 << 4;
        if (p.DownedTowerStardust) b9 |= 1 << 5;
        if (p.ForceHalloweenForToday) b9 |= 1 << 6;
        if (p.ForceXMasForToday) b9 |= 1 << 7;
        flags[5] = b9;

        byte b10 = 0;
        if (p.BoughtCat) b10 |= 1 << 0;
        if (p.BoughtDog) b10 |= 1 << 1;
        if (p.BoughtBunny) b10 |= 1 << 2;
        if (p.FreeCake) b10 |= 1 << 3;
        if (p.DrunkWorld) b10 |= 1 << 4;
        if (p.DownedEmpressOfLight) b10 |= 1 << 5;
        if (p.DownedQueenSlime) b10 |= 1 << 6;
        if (p.GetGoodWorld) b10 |= 1 << 7;
        flags[6] = b10;

        byte b11 = 0;
        if (p.TenthAnniversaryWorld) b11 |= 1 << 0;
        if (p.DontStarveWorld) b11 |= 1 << 1;
        if (p.DownedDeerclops) b11 |= 1 << 2;
        if (p.NotTheBeesWorld) b11 |= 1 << 3;
        if (p.RemixWorld) b11 |= 1 << 4;
        if (p.UnlockedSlimeBlueSpawn) b11 |= 1 << 5;
        if (p.CombatBookVolumeTwoWasUsed) b11 |= 1 << 6;
        if (p.PeddlersSatchelWasUsed) b11 |= 1 << 7;
        flags[7] = b11;

        byte b12 = 0;
        if (p.UnlockedSlimeGreenSpawn) b12 |= 1 << 0;
        if (p.UnlockedSlimeOldSpawn) b12 |= 1 << 1;
        if (p.UnlockedSlimePurpleSpawn) b12 |= 1 << 2;
        if (p.UnlockedSlimeRainbowSpawn) b12 |= 1 << 3;
        if (p.UnlockedSlimeRedSpawn) b12 |= 1 << 4;
        if (p.UnlockedSlimeYellowSpawn) b12 |= 1 << 5;
        if (p.UnlockedSlimeCopperSpawn) b12 |= 1 << 6;
        if (p.FastForwardTimeToDusk) b12 |= 1 << 7;
        flags[8] = b12;

        byte b13 = 0;
        if (p.NoTrapsWorld) b13 |= 1 << 0;
        if (p.ZenithWorld) b13 |= 1 << 1;
        if (p.UnlockedTruffleSpawn) b13 |= 1 << 2;
        if (p.VampireSeed) b13 |= 1 << 3;
        if (p.InfectedSeed) b13 |= 1 << 4;
        if (p.TeamBasedSpawnsSeed) b13 |= 1 << 5;
        if (p.SkyblockWorld) b13 |= 1 << 6;
        if (p.DualDungeonsSeed) b13 |= 1 << 7;
        flags[9] = b13;

        byte b14 = 0;
        if (p.SkyblockLowTiles) b14 |= 1 << 0;
        if (p.ForceHalloweenForever) b14 |= 1 << 1;
        if (p.ForceXMasForever) b14 |= 1 << 2;
        if (p.MoreLightningSeed) b14 |= 1 << 3;
        if (p.NoLightningSeed) b14 |= 1 << 4;
        flags[10] = b14;

        var spawns = new (short X, short Y)[ExtraSpawnPoints.Count];
        for (int i = 0; i < spawns.Length; i++) spawns[i] = ExtraSpawnPoints[i];

        var backgrounds = new byte[13];
        for (int i = 0; i < 13; i++) backgrounds[i] = i < Backgrounds.Length ? Backgrounds[i] : (byte)0;

        var treeTops = new byte[13];
        for (int i = 0; i < 13; i++) treeTops[i] = i < TreeTops.Length ? (byte)TreeTops[i] : (byte)0;

        return new WorldInfoPacket(
            Time: (int)Time,
            DayTime: DayTime,
            BloodMoon: BloodMoon,
            Eclipse: Eclipse,
            MoonPhase: (byte)MoonPhase,
            MaxTilesX: (short)MaxTilesX,
            MaxTilesY: (short)MaxTilesY,
            SpawnTileX: (short)SpawnTileX,
            SpawnTileY: (short)SpawnTileY,
            WorldSurface: (short)WorldSurface,
            RockLayer: (short)RockLayer,
            WorldId: WorldId,
            WorldName: WorldName,
            GameMode: (byte)GameMode)
        {
            UniqueId = UniqueId,
            WorldGeneratorVersion = WorldGeneratorVersion,
            MoonType = MoonType,
            Backgrounds = backgrounds,
            IceBackStyle = (byte)IceBackStyle,
            JungleBackStyle = (byte)JungleBackStyle,
            HellBackStyle = (byte)HellBackStyle,
            WindSpeedTarget = WindSpeedTarget,
            NumClouds = (byte)NumClouds,
            TreeX = TreeX,
            TreeStyle = new byte[] { (byte)TreeStyle[0], (byte)TreeStyle[1], (byte)TreeStyle[2], (byte)TreeStyle[3] },
            CaveBackX = CaveBackX,
            CaveBackStyle = new byte[] { (byte)CaveBackStyle[0], (byte)CaveBackStyle[1], (byte)CaveBackStyle[2], (byte)CaveBackStyle[3] },
            TreeTops = treeTops,
            MaxRaining = Raining ? MaxRain : 0f,
            ProgressFlags = flags,
            SundialCooldown = SundialCooldown,
            MoondialCooldown = MoondialCooldown,
            OreTiers = OreTiers,
            InvasionType = (sbyte)InvasionType,
            LobbyId = LobbyId,
            SandstormIntensity = SandstormIntensity,
            ExtraSpawnPoints = spawns,
            DungeonX = (short)DungeonX,
            DungeonY = (short)DungeonY
        };
    }
}

/// <summary>
/// 玩家运行时状态（服务端权威唯一真相）：位置 / 速度由仿真推进，生命由战斗阶段结算。
/// </summary>
public sealed class PlayerRuntime
{
    public int Id;

    /// <summary>世界坐标（像素）。</summary>
    public Vector2 Position;

    /// <summary>每 tick 速度（像素 / tick）。</summary>
    public Vector2 Velocity;

    /// <summary>是否在线：断线 / 死亡后置 false，不再参与仿真与快照。</summary>
    public bool Active = true;

    public int Hp = 100;
    public int HpMax = 100;

    /// <summary>连续下落距离（像素），落地时用于结算下落伤害。</summary>
    public float FallDistance;
}

/// <summary>
/// 世界进度位。字段与包 7 的 11 个 BitsByte 一一对应（权威来源：<c>NetMessage.SendData</c> case 7）。
/// </summary>
public sealed class WorldProgress
{
    // bitsByte4
    public bool ShadowOrbSmashed;
    public bool DownedBoss1;
    public bool DownedBoss2;
    public bool DownedBoss3;
    public bool HardMode;
    public bool DownedClown;
    public bool DownedPlantBoss;

    // bitsByte5
    public bool DownedMechBoss1;
    public bool DownedMechBoss2;
    public bool DownedMechBoss3;
    public bool DownedMechBossAny;
    public bool Crimson;
    public bool PumpkinMoon;
    public bool SnowMoon;

    // bitsByte6
    public bool FastForwardTimeToDawn;
    public bool SlimeRain;
    public bool DownedSlimeKing;
    public bool DownedQueenBee;
    public bool DownedFishron;
    public bool DownedMartians;
    public bool DownedAncientCultist;

    // bitsByte7
    public bool DownedMoonlord;
    public bool DownedHalloweenKing;
    public bool DownedHalloweenTree;
    public bool DownedChristmasIceQueen;
    public bool DownedChristmasSantank;
    public bool DownedChristmasTree;
    public bool DownedGolemBoss;
    public bool PartyIsUp;

    // bitsByte8
    public bool DownedPirates;
    public bool DownedFrost;
    public bool DownedGoblins;
    public bool SandstormHappening;
    public bool Dd2Ongoing;
    public bool Dd2DownedInvasionT1;
    public bool Dd2DownedInvasionT2;
    public bool Dd2DownedInvasionT3;

    // bitsByte9
    public bool CombatBookWasUsed;
    public bool LanternsUp;
    public bool DownedTowerSolar;
    public bool DownedTowerVortex;
    public bool DownedTowerNebula;
    public bool DownedTowerStardust;
    public bool ForceHalloweenForToday;
    public bool ForceXMasForToday;

    // bitsByte10
    public bool BoughtCat;
    public bool BoughtDog;
    public bool BoughtBunny;
    public bool FreeCake;
    public bool DrunkWorld;
    public bool DownedEmpressOfLight;
    public bool DownedQueenSlime;
    public bool GetGoodWorld;

    // bitsByte11
    public bool TenthAnniversaryWorld;
    public bool DontStarveWorld;
    public bool DownedDeerclops;
    public bool NotTheBeesWorld;
    public bool RemixWorld;
    public bool UnlockedSlimeBlueSpawn;
    public bool CombatBookVolumeTwoWasUsed;
    public bool PeddlersSatchelWasUsed;

    // bitsByte12
    public bool UnlockedSlimeGreenSpawn;
    public bool UnlockedSlimeOldSpawn;
    public bool UnlockedSlimePurpleSpawn;
    public bool UnlockedSlimeRainbowSpawn;
    public bool UnlockedSlimeRedSpawn;
    public bool UnlockedSlimeYellowSpawn;
    public bool UnlockedSlimeCopperSpawn;
    public bool FastForwardTimeToDusk;

    // bitsByte13
    public bool NoTrapsWorld;
    public bool ZenithWorld;
    public bool UnlockedTruffleSpawn;
    public bool VampireSeed;
    public bool InfectedSeed;
    public bool TeamBasedSpawnsSeed;
    public bool SkyblockWorld;
    public bool DualDungeonsSeed;

    // bitsByte14
    public bool SkyblockLowTiles;
    public bool ForceHalloweenForever;
    public bool ForceXMasForever;
    public bool MoreLightningSeed;
    public bool NoLightningSeed;
}

/// <summary>宝箱物品格。</summary>
public struct ChestItem
{
    public int Type;
    public short Stack;
    public byte Prefix;
}

/// <summary>世界宝箱（存档 section 3）。</summary>
public sealed class Chest
{
    public int Index;
    public int X;
    public int Y;
    public string Name = "";
    public ChestItem[] Items = Array.Empty<ChestItem>();
}

/// <summary>世界牌子（存档 section 4）。</summary>
public sealed class Sign
{
    public int Index;
    public int X;
    public int Y;
    public string Text = "";
}

/// <summary>世界 NPC（存档 section 5；<see cref="IsTownNpc"/> 区分城镇 NPC 与仅位置记录）。</summary>
public sealed class WorldNpc
{
    public int Type;
    public string GivenName = "";
    public float X;
    public float Y;
    public bool IsTownNpc;
    public bool Homeless;
    public int HomeTileX;
    public int HomeTileY;

    // ---- 运行时（网络同步 / 战斗）----

    /// <summary>网络 ID（客户端据此 <c>SetDefaults</c> 生成对应 NPC）；默认与 <see cref="Type"/> 相同。</summary>
    public short NetId;

    /// <summary>生成代数（包 23 用于校验命中的目标未过期）。</summary>
    public byte Generation;

    public int Life = 100;
    public int LifeMax = 100;

    public float VelocityX;
    public float VelocityY;

    /// <summary>是否存活；false 时同步 <c>life=0</c> 让客户端移除。</summary>
    public bool Active = true;

    /// <summary>死亡发生的 tick（用于延后清理，确保 life=0 已下发到客户端）。</summary>
    public long DeadTick;
}
