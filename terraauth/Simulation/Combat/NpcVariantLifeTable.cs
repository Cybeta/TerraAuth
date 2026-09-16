// TerraAuth — 原版「负 netID 变体」的生命上限表
//
// 为什么需要：包 23（NPC 同步）**不携带生命上限**，客户端血条恒为
//   「服务端下发的当前生命 ÷ 客户端自己按 netID 得到的 lifeMax」。
// 客户端的 lifeMax 来自 NPC.SetDefaults(netID)；对负 netID（同一 type 的配色 / 体型变体，
// 如 -3 = 绿史莱姆）走 SetDefaultsFromNetId，该分支末尾 `lifeMax = life` ——
// 即**变体有自己的生命上限**（绿史莱姆 14，而非基础类型蓝史莱姆的 25）。
// 服务端若按 FromNetId 解析出的基础类型上限下发当前生命，就会出现「当前生命 > 客户端上限」，
// 客户端血条按比值截断 → 画成满的；真机表现即「打掉一半血后血条又回到满」。
//
// 权威来源：原版 1.4.5.8 <c>NPC.SetDefaultsFromNetId</c> 每个 case 的 <c>life</c>：
//   字面量，或 `(int)(life * scale)`（scale = 该 case 传给 SetDefaults_ForNetId 的体型覆盖值；
//   -14 另有 ×1.1；flag=true 的分支随后走 ScaleStats 并 `life = lifeMax`，classic 难度下不变，
//   只保留 `lifeMax < 6 → 6` 下限）。基础类型生命上限取自 NpcStatsTable（同一来源）。
//
// 范围：本表只对齐**生命上限**（血条口径）。变体的伤害 / 防御仍沿用基础类型（与改动前一致）。

namespace TerraAuth.Simulation;

/// <summary>原版负 netID 变体的生命上限（索引 = <c>-netId - 1</c>，与 <see cref="NpcNetIdMap"/> 同序）。</summary>
public static class NpcVariantLifeTable
{
    /// <summary>变体生命上限（classic 基准），下标 = <c>-netId - 1</c>；注释为「基础类型 → 取值来源」。</summary>
    private static readonly int[] LifeMax =
    {
        90,  // -1  基础 81（CorruptSlime）字面量
        90,  // -2  基础 81（CorruptSlime）字面量
        14,  // -3  基础 1（BlueSlime）字面量 → 绿史莱姆
        150, // -4  基础 1 字面量 → Pinky
        30,  // -5  基础 1 字面量
        45,  // -6  基础 1 字面量 → 黑史莱姆
        40,  // -7  基础 1 字面量 → 紫史莱姆
        35,  // -8  基础 1 字面量
        45,  // -9  基础 1 字面量
        60,  // -10 基础 1 字面量 → 丛林史莱姆
        34,  // -11 基础 6（EaterofSouls）× 0.85
        46,  // -12 基础 6 × 1.15
        72,  // -13 基础 31（AngryBones）× 0.9
        101, // -14 基础 31 × 1.15 × 1.1
        400, // -15 基础 77（ArmoredSkeleton）字面量
        40,  // -16 基础 42（Hornet）× 0.85
        57,  // -17 基础 42 × 1.2
        176, // -18 基础 176（MossHornet）× 0.8
        198, // -19 基础 176 × 0.9
        242, // -20 基础 176 × 1.1
        264, // -21 基础 176 × 1.2
        34,  // -22 基础 173（Crimera）× 0.85
        46,  // -23 基础 173 × 1.15
        170, // -24 基础 183（Crimslime）× 0.85
        230, // -25 基础 183 × 1.15
        40,  // -26 基础 3（Zombie）× 0.9
        49,  // -27 基础 3 × 1.1
        34,  // -28 基础 132（BaldZombie）× 0.85
        46,  // -29 基础 132 × 1.15
        46,  // -30 基础 186（PincushionZombie）× 0.93
        56,  // -31 基础 186 × 1.13
        35,  // -32 基础 187（SlimedZombie）× 0.89
        44,  // -33 基础 187 × 1.11
        39,  // -34 基础 188（SwampZombie）× 0.87
        50,  // -35 基础 188 × 1.13
        41,  // -36 基础 189（TwiggyZombie）× 0.92
        48,  // -37 基础 189 × 1.08
        74,  // -38 基础 190 × 1.15
        66,  // -39 基础 191 × 1.1
        45,  // -40 基础 192 × 0.9
        51,  // -41 基础 193 × 0.85
        66,  // -42 基础 194 × 1.1
        69,  // -43 基础 2（DemonEye）× 1.15
        33,  // -44 基础 200（FemaleZombie）× 0.87
        39,  // -45 基础 200 × 1.05
        54,  // -46 基础 21（Skeleton）× 0.9
        66,  // -47 基础 21 × 1.1
        51,  // -48 基础 201 × 0.93
        58,  // -49 基础 201 × 1.07
        56,  // -50 基础 202 × 0.87
        73,  // -51 基础 202 × 1.13
        51,  // -52 基础 203 × 0.85
        69,  // -53 基础 203 × 1.15
        45,  // -54 基础 223（ZombieRaincoat）× 0.9
        55,  // -55 基础 223 × 1.1
        42,  // -56 基础 231（HornetFatty）× 0.85
        62,  // -57 基础 231 × 1.25
        33,  // -58 基础 232（HornetHoney）× 0.8
        48,  // -59 基础 232 × 1.15
        34,  // -60 基础 233（HornetLeafy）× 0.92
        41,  // -61 基础 233 × 1.1
        32,  // -62 基础 234（HornetSpikey）× 0.78
        48,  // -63 基础 234 × 1.16
        33,  // -64 基础 235（HornetStingy）× 0.87
        45,  // -65 基础 235 × 1.21
    };

    /// <summary>
    /// 取变体生命上限；<paramref name="netId"/> 非负（无变体）或越界时返回 false
    /// （调用方回退到基础类型上限）。
    /// </summary>
    public static bool TryLifeMax(int netId, out int lifeMax)
    {
        if (netId >= 0)
        {
            lifeMax = 0;
            return false;
        }

        int index = -netId - 1;
        if (index >= LifeMax.Length)
        {
            lifeMax = 0;
            return false;
        }

        lifeMax = LifeMax[index];
        return true;
    }
}
