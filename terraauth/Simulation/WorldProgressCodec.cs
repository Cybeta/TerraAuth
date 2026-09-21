// 世界进度编解码：把「已击败 Boss / 事件开关 / 世界种子特性」等进度位编成存档字节串，
// 供持久化层落盘与回读，修掉「服务端重启后进度归零（已击败的 Boss 复活、困难模式丢失）」。
//
// ① 数据来源：字段分组与顺序取 <c>WorldProgress</c>（Simulation/World/WorldState.cs）里
//    「bitsByte4 … bitsByte14」的既有分组注释，本文件是其**逐位不重排**的镜像，
//    共 82 个 bool 位。
// ② 版本：当前 <see cref="Version"/> = 1。首字节存版本，其余 11 字节为位图（88 位，末 6 位保留）。
// ③ 为什么不复用 <c>WorldState.ToWorldInfoPacket()</c> 的 flags：那 11 个字节是**包 7 的线上布局**，
//    里面混入了非进度字段 —— flags[0] bit6 是 <c>SscEnabled</c>、flags[1] bit4 是 <c>CloudBgActive</c>。
//    直接拿它当存档会有两个错：一是读回时会把「当时的 SSC 开关 / 云背景状态」当成世界进度写进世界数据；
//    二是 SSC 开关属于**进程配置**（server.json，可热重载），不该跟着世界落库，
//    否则改配置重开会得到一个被旧配置污染的世界。故另立纯进度编解码器。
// ④ 演进规则：新增进度字段必须**追加到位图末尾的空位**（当前只有 b10 的高 3 位及末 6 位保留位可用，
//    不够就直接加字节）并同时升 <see cref="Version"/>；**不得改动既有位序** ——
//    位序一变，旧存档会被解成另一批字段（静默错位比报错更危险）。

using System;
using System.IO;

namespace TerraAuth.Simulation;

/// <summary>世界进度位图的版本化编解码（见文件头注释的布局与演进规则）。</summary>
public static class WorldProgressCodec
{
    /// <summary>当前存档版本。位序或字段集合变化时必须递增。</summary>
    public const byte Version = 1;

    /// <summary>编码后总长度：1 字节版本 + 11 字节位图。</summary>
    public const int EncodedLength = 12;

    /// <summary>位图布局：外层下标 = 位图字节序号（0..10），内层下标 = 该字节从 bit0 起的位序号。
    /// 表驱动而非手写位运算，保证编 / 解码共用同一份顺序，杜绝「编码改了、解码忘了改」。</summary>
    private static readonly (Func<WorldProgress, bool> Read, Action<WorldProgress, bool> Write)[][] Layout =
    [
        // b0 — WorldProgress.bitsByte4（7 位；注意 DownedPlantBoss 在包 7 里是 bit7，此处按纯进度紧凑排到 bit6）
        [
            (p => p.ShadowOrbSmashed, (p, v) => p.ShadowOrbSmashed = v),
            (p => p.DownedBoss1, (p, v) => p.DownedBoss1 = v),
            (p => p.DownedBoss2, (p, v) => p.DownedBoss2 = v),
            (p => p.DownedBoss3, (p, v) => p.DownedBoss3 = v),
            (p => p.HardMode, (p, v) => p.HardMode = v),
            (p => p.DownedClown, (p, v) => p.DownedClown = v),
            (p => p.DownedPlantBoss, (p, v) => p.DownedPlantBoss = v),
        ],
        // b1 — bitsByte5（7 位；包 7 的 bit4 是 CloudBgActive，此处不复用）
        [
            (p => p.DownedMechBoss1, (p, v) => p.DownedMechBoss1 = v),
            (p => p.DownedMechBoss2, (p, v) => p.DownedMechBoss2 = v),
            (p => p.DownedMechBoss3, (p, v) => p.DownedMechBoss3 = v),
            (p => p.DownedMechBossAny, (p, v) => p.DownedMechBossAny = v),
            (p => p.Crimson, (p, v) => p.Crimson = v),
            (p => p.PumpkinMoon, (p, v) => p.PumpkinMoon = v),
            (p => p.SnowMoon, (p, v) => p.SnowMoon = v),
        ],
        // b2 — bitsByte6（7 位）
        [
            (p => p.FastForwardTimeToDawn, (p, v) => p.FastForwardTimeToDawn = v),
            (p => p.SlimeRain, (p, v) => p.SlimeRain = v),
            (p => p.DownedSlimeKing, (p, v) => p.DownedSlimeKing = v),
            (p => p.DownedQueenBee, (p, v) => p.DownedQueenBee = v),
            (p => p.DownedFishron, (p, v) => p.DownedFishron = v),
            (p => p.DownedMartians, (p, v) => p.DownedMartians = v),
            (p => p.DownedAncientCultist, (p, v) => p.DownedAncientCultist = v),
        ],
        // b3 — bitsByte7（8 位）
        [
            (p => p.DownedMoonlord, (p, v) => p.DownedMoonlord = v),
            (p => p.DownedHalloweenKing, (p, v) => p.DownedHalloweenKing = v),
            (p => p.DownedHalloweenTree, (p, v) => p.DownedHalloweenTree = v),
            (p => p.DownedChristmasIceQueen, (p, v) => p.DownedChristmasIceQueen = v),
            (p => p.DownedChristmasSantank, (p, v) => p.DownedChristmasSantank = v),
            (p => p.DownedChristmasTree, (p, v) => p.DownedChristmasTree = v),
            (p => p.DownedGolemBoss, (p, v) => p.DownedGolemBoss = v),
            (p => p.PartyIsUp, (p, v) => p.PartyIsUp = v),
        ],
        // b4 — bitsByte8（8 位）
        [
            (p => p.DownedPirates, (p, v) => p.DownedPirates = v),
            (p => p.DownedFrost, (p, v) => p.DownedFrost = v),
            (p => p.DownedGoblins, (p, v) => p.DownedGoblins = v),
            (p => p.SandstormHappening, (p, v) => p.SandstormHappening = v),
            (p => p.Dd2Ongoing, (p, v) => p.Dd2Ongoing = v),
            (p => p.Dd2DownedInvasionT1, (p, v) => p.Dd2DownedInvasionT1 = v),
            (p => p.Dd2DownedInvasionT2, (p, v) => p.Dd2DownedInvasionT2 = v),
            (p => p.Dd2DownedInvasionT3, (p, v) => p.Dd2DownedInvasionT3 = v),
        ],
        // b5 — bitsByte9（8 位）
        [
            (p => p.CombatBookWasUsed, (p, v) => p.CombatBookWasUsed = v),
            (p => p.LanternsUp, (p, v) => p.LanternsUp = v),
            (p => p.DownedTowerSolar, (p, v) => p.DownedTowerSolar = v),
            (p => p.DownedTowerVortex, (p, v) => p.DownedTowerVortex = v),
            (p => p.DownedTowerNebula, (p, v) => p.DownedTowerNebula = v),
            (p => p.DownedTowerStardust, (p, v) => p.DownedTowerStardust = v),
            (p => p.ForceHalloweenForToday, (p, v) => p.ForceHalloweenForToday = v),
            (p => p.ForceXMasForToday, (p, v) => p.ForceXMasForToday = v),
        ],
        // b6 — bitsByte10（8 位）
        [
            (p => p.BoughtCat, (p, v) => p.BoughtCat = v),
            (p => p.BoughtDog, (p, v) => p.BoughtDog = v),
            (p => p.BoughtBunny, (p, v) => p.BoughtBunny = v),
            (p => p.FreeCake, (p, v) => p.FreeCake = v),
            (p => p.DrunkWorld, (p, v) => p.DrunkWorld = v),
            (p => p.DownedEmpressOfLight, (p, v) => p.DownedEmpressOfLight = v),
            (p => p.DownedQueenSlime, (p, v) => p.DownedQueenSlime = v),
            (p => p.GetGoodWorld, (p, v) => p.GetGoodWorld = v),
        ],
        // b7 — bitsByte11（8 位）
        [
            (p => p.TenthAnniversaryWorld, (p, v) => p.TenthAnniversaryWorld = v),
            (p => p.DontStarveWorld, (p, v) => p.DontStarveWorld = v),
            (p => p.DownedDeerclops, (p, v) => p.DownedDeerclops = v),
            (p => p.NotTheBeesWorld, (p, v) => p.NotTheBeesWorld = v),
            (p => p.RemixWorld, (p, v) => p.RemixWorld = v),
            (p => p.UnlockedSlimeBlueSpawn, (p, v) => p.UnlockedSlimeBlueSpawn = v),
            (p => p.CombatBookVolumeTwoWasUsed, (p, v) => p.CombatBookVolumeTwoWasUsed = v),
            (p => p.PeddlersSatchelWasUsed, (p, v) => p.PeddlersSatchelWasUsed = v),
        ],
        // b8 — bitsByte12（8 位）
        [
            (p => p.UnlockedSlimeGreenSpawn, (p, v) => p.UnlockedSlimeGreenSpawn = v),
            (p => p.UnlockedSlimeOldSpawn, (p, v) => p.UnlockedSlimeOldSpawn = v),
            (p => p.UnlockedSlimePurpleSpawn, (p, v) => p.UnlockedSlimePurpleSpawn = v),
            (p => p.UnlockedSlimeRainbowSpawn, (p, v) => p.UnlockedSlimeRainbowSpawn = v),
            (p => p.UnlockedSlimeRedSpawn, (p, v) => p.UnlockedSlimeRedSpawn = v),
            (p => p.UnlockedSlimeYellowSpawn, (p, v) => p.UnlockedSlimeYellowSpawn = v),
            (p => p.UnlockedSlimeCopperSpawn, (p, v) => p.UnlockedSlimeCopperSpawn = v),
            (p => p.FastForwardTimeToDusk, (p, v) => p.FastForwardTimeToDusk = v),
        ],
        // b9 — bitsByte13（8 位）
        [
            (p => p.NoTrapsWorld, (p, v) => p.NoTrapsWorld = v),
            (p => p.ZenithWorld, (p, v) => p.ZenithWorld = v),
            (p => p.UnlockedTruffleSpawn, (p, v) => p.UnlockedTruffleSpawn = v),
            (p => p.VampireSeed, (p, v) => p.VampireSeed = v),
            (p => p.InfectedSeed, (p, v) => p.InfectedSeed = v),
            (p => p.TeamBasedSpawnsSeed, (p, v) => p.TeamBasedSpawnsSeed = v),
            (p => p.SkyblockWorld, (p, v) => p.SkyblockWorld = v),
            (p => p.DualDungeonsSeed, (p, v) => p.DualDungeonsSeed = v),
        ],
        // b10 — bitsByte14（5 位；高 3 位保留，预留给后续追加字段）
        [
            (p => p.SkyblockLowTiles, (p, v) => p.SkyblockLowTiles = v),
            (p => p.ForceHalloweenForever, (p, v) => p.ForceHalloweenForever = v),
            (p => p.ForceXMasForever, (p, v) => p.ForceXMasForever = v),
            (p => p.MoreLightningSeed, (p, v) => p.MoreLightningSeed = v),
            (p => p.NoLightningSeed, (p, v) => p.NoLightningSeed = v),
        ],
    ];

    /// <summary>编码为 <c>[Version][11 字节位图]</c>（共 <see cref="EncodedLength"/> 字节）。</summary>
    public static byte[] Encode(WorldProgress progress)
    {
        var data = new byte[EncodedLength];
        data[0] = Version;

        for (int b = 0; b < Layout.Length; b++)
        {
            byte bits = 0;
            var group = Layout[b];
            for (int j = 0; j < group.Length; j++)
                if (group[j].Read(progress)) bits |= (byte)(1 << j);
            data[1 + b] = bits;
        }

        return data;
    }

    /// <summary>
    /// 把位图还原写进 <paramref name="progress"/>（就地改字段，调用方持有的对象引用不变）。
    /// 数据为空 / 长度不符 / 版本不符 → 抛 <see cref="InvalidDataException"/>，调用方按「无进度」处理。
    /// 保留位一律忽略（不报错），保证「旧代码写的新版本数据」在追加字段后仍可读。
    /// </summary>
    public static void Decode(byte[] data, WorldProgress progress)
    {
        if (data is null || data.Length != EncodedLength)
            throw new InvalidDataException(
                $"世界进度数据长度非法：期望 {EncodedLength} 字节，实际 {data?.Length ?? 0} 字节");
        if (data[0] != Version)
            throw new InvalidDataException($"世界进度数据版本不支持：期望 v{Version}，实际 v{data[0]}");

        for (int b = 0; b < Layout.Length; b++)
        {
            byte bits = data[1 + b];
            var group = Layout[b];
            for (int j = 0; j < group.Length; j++)
                group[j].Write(progress, (bits & (1 << j)) != 0);
        }
    }
}
