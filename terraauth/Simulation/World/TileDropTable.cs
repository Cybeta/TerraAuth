// TerraAuth — 图格 → 掉落物表（原版 WorldGen.KillTile_GetItemDrops 的**静态映射**部分）
//
// 为什么需要：原版服务端在 KillTile 里调用 WorldGen.KillTile_DropItems（内部 KillTile_GetItemDrops
// + Item.NewItem）生成掉落物，再由包 21/22 同步给客户端；客户端本地的 Item.NewItem 只写 400 号
// 「本地预测槽」，不落世界（见 Item.cs 的 `Main.netMode == 1 ? 400 : ...`）。服务端若不生成，
// 真机表现就是「挖掉树木 / 地块后没有掉落木块与泥土」。
//
// 数据来源：Terraria 1.4.5.8 原版 WorldGen.cs → KillTile_GetItemDrops 逐格提取的
// 「case → dropItem = 字面量」分支（源提取 361 项；**当前表内 373 项** —— v0.4.0 起
// 新增 20 项宝箱 / 家具样式类映射（102/106/212/219/220/228/243/247/283/300-308/354/355），
// 并移除 8 项改由专用逻辑处理的图格（3/24/50/110/129/149/201/707））。**未收录**的图格表示其掉落依赖
// 帧 / 随机数 / 玩家工具（树木按斧力追加木材、草药按 frameX 选种子、宝箱掉落内含物…）：
// 其中树木在下方显式处理，其余未收录即「不掉落」。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>图格 → 掉落物（物品 ID）映射；不含「按帧 / 概率」的复杂分支。</summary>
public static class TileDropTable
{
    /// <summary>树木类图格（原版 case 5/596/616/634 → KillTile_GetTreeDrops，默认掉落木材）。</summary>
    private static readonly int[] TreeTiles = { 5, 596, 616, 634 };

    /// <summary>木材（ItemID.Wood）。</summary>
    public const int Wood = 9;

    /// <summary>是否为树木类图格（供「整棵倒下」判断）。</summary>
    public static bool IsTreeTile(int tileType) => System.Array.IndexOf(TreeTiles, tileType) >= 0;

    /// <summary>
    /// 取成熟草药（83）或开花草药（84）的掉落。开花草药额外掉落 1..3 颗种子；
    /// 环境成熟条件和再生法杖状态尚未建模，故只按图格类型区分。
    /// </summary>
    public static bool TryGetMatureHerbDrop(int tileType, short frameX, out int herbItem, out int seedItem, out bool flowering)
    {
        flowering = tileType == 84;
        if (tileType != 83 && !flowering)
        {
            herbItem = 0;
            seedItem = 0;
            return false;
        }

        int style = frameX / 18;
        if (style < 0 || style > 6)
        {
            herbItem = 0;
            seedItem = 0;
            return false;
        }

        herbItem = style == 6 ? 2358 : 313 + style;
        seedItem = style == 6 ? 2357 : 307 + style;
        return true;
    }

    private static readonly Dictionary<int, int> Of = new()
    {
        [0] = 2,
        [1] = 3,
        [2] = 2,
        [6] = 11,
        [7] = 12,
        [8] = 13,
        [9] = 14,
        [22] = 56,
        [23] = 2,
        [25] = 61,
        [30] = 9,
        [36] = 1869,
        [37] = 116,
        [38] = 129,
        [39] = 131,
        [40] = 133,
        [41] = 134,
        [43] = 137,
        [44] = 139,
        [45] = 141,
        [46] = 143,
        [47] = 145,
        [48] = 147,
        [49] = 148,
        [51] = 150,
        [52] = 2996,
        [53] = 169,
        [54] = 170,
        [56] = 173,
        [57] = 172,
        [58] = 174,
        [59] = 176,
        [60] = 176,
        [61] = 331,
        [62] = 2996,
        [70] = 176,
        [71] = 194,
        [72] = 194,
        [73] = 283,
        [74] = 331,
        [75] = 192,
        [76] = 214,
        [78] = 222,
        [80] = 276,
        [81] = 275,
        [83] = 2358,
        [84] = 2358,
        [102] = 355,
        [106] = 363,
        [107] = 364,
        [108] = 365,
        [109] = 2,
        [111] = 366,
        [112] = 370,
        [116] = 408,
        [117] = 409,
        [118] = 412,
        [119] = 413,
        [120] = 414,
        [121] = 415,
        [122] = 416,
        [123] = 424,
        [124] = 480,
        [130] = 511,
        [131] = 512,
        [135] = 529,
        [136] = 538,
        [137] = 539,
        [140] = 577,
        [141] = 580,
        [144] = 583,
        [145] = 586,
        [146] = 591,
        [147] = 593,
        [148] = 594,
        [150] = 604,
        [151] = 607,
        [152] = 609,
        [153] = 611,
        [154] = 612,
        [155] = 613,
        [156] = 614,
        [157] = 619,
        [158] = 620,
        [159] = 621,
        [160] = 662,
        [161] = 664,
        [163] = 833,
        [164] = 834,
        [166] = 699,
        [167] = 700,
        [168] = 701,
        [169] = 702,
        [170] = 1872,
        [174] = 713,
        [175] = 717,
        [176] = 718,
        [177] = 719,
        [179] = 3,
        [180] = 3,
        [181] = 3,
        [182] = 3,
        [183] = 3,
        [188] = 276,
        [189] = 751,
        [190] = 183,
        [191] = 9,
        [193] = 762,
        [194] = 154,
        [195] = 763,
        [196] = 765,
        [197] = 767,
        [198] = 775,
        [199] = 2,
        [200] = 835,
        [202] = 824,
        [203] = 836,
        [204] = 880,
        [206] = 883,
        [208] = 911,
        [210] = 937,
        [211] = 947,
        [212] = 951,
        [213] = 965,
        [214] = 85,
        [219] = 997,
        [220] = 998,
        [221] = 1104,
        [222] = 1105,
        [223] = 1106,
        [224] = 1103,
        [226] = 1101,
        [228] = 1120,
        [229] = 1125,
        [230] = 1127,
        [232] = 1150,
        [234] = 1246,
        [239] = 20,
        [243] = 1430,
        [247] = 1551,
        [248] = 1589,
        [249] = 1591,
        [250] = 1593,
        [251] = 1725,
        [252] = 1727,
        [253] = 1729,
        [272] = 1344,
        [273] = 2119,
        [274] = 2120,
        [283] = 2172,
        [284] = 2173,
        [300] = 2192,
        [301] = 2193,
        [302] = 2194,
        [303] = 2195,
        [304] = 2196,
        [305] = 2197,
        [306] = 2198,
        [307] = 2203,
        [308] = 2204,
        [311] = 2260,
        [312] = 2261,
        [313] = 2262,
        [315] = 2435,
        [321] = 2503,
        [322] = 2504,
        [323] = 2504,
        [325] = 2692,
        [326] = 2693,
        [327] = 2694,
        [328] = 2695,
        [329] = 2697,
        [330] = 71,
        [331] = 72,
        [332] = 73,
        [333] = 74,
        [336] = 2701,
        [340] = 2751,
        [341] = 2752,
        [342] = 2753,
        [343] = 2754,
        [344] = 2755,
        [345] = 2787,
        [346] = 2792,
        [347] = 2793,
        [348] = 2794,
        [350] = 2860,
        [351] = 2868,
        [353] = 2996,
        [354] = 2999,
        [355] = 3000,
        [357] = 3066,
        [365] = 3077,
        [366] = 3078,
        [367] = 3081,
        [368] = 3086,
        [369] = 3087,
        [370] = 3100,
        [371] = 3113,
        [372] = 3117,
        [379] = 3214,
        [381] = 3,
        [382] = 2996,
        [383] = 620,
        [385] = 3234,
        [396] = 3271,
        [397] = 3272,
        [398] = 3274,
        [399] = 3275,
        [400] = 3276,
        [401] = 3277,
        [402] = 3338,
        [403] = 3339,
        [404] = 3347,
        [407] = 3380,
        [408] = 3460,
        [409] = 3461,
        [415] = 3573,
        [416] = 3574,
        [417] = 3575,
        [418] = 3576,
        [421] = 3609,
        [422] = 3610,
        [424] = 3616,
        [426] = 3621,
        [427] = 3622,
        [429] = 3629,
        [430] = 3633,
        [431] = 3634,
        [432] = 3635,
        [433] = 3636,
        [434] = 3637,
        [435] = 3638,
        [436] = 3639,
        [437] = 3640,
        [438] = 3641,
        [439] = 3642,
        [442] = 3707,
        [445] = 3725,
        [446] = 3736,
        [447] = 3737,
        [448] = 3738,
        [449] = 3739,
        [450] = 3740,
        [451] = 3741,
        [458] = 3754,
        [459] = 3755,
        [460] = 3756,
        [472] = 3951,
        [473] = 3953,
        [474] = 3955,
        [476] = 4040,
        [477] = 2,
        [478] = 4050,
        [479] = 4051,
        [492] = 2,
        [494] = 4089,
        [495] = 4090,
        [496] = 4091,
        [498] = 4139,
        [500] = 4229,
        [501] = 4230,
        [502] = 4231,
        [503] = 4232,
        [507] = 4277,
        [508] = 4278,
        [512] = 129,
        [513] = 129,
        [514] = 129,
        [515] = 129,
        [516] = 129,
        [517] = 129,
        [519] = 183,
        [520] = 4326,
        [528] = 183,
        [534] = 3,
        [535] = 129,
        [536] = 3,
        [537] = 129,
        [539] = 3,
        [540] = 129,
        [541] = 4392,
        [546] = 4422,
        [557] = 4422,
        [561] = 4554,
        [562] = 4564,
        [563] = 4547,
        [566] = 999,
        [571] = 4564,
        [574] = 4717,
        [575] = 4718,
        [576] = 4719,
        [577] = 4720,
        [578] = 4721,
        [579] = 4761,
        [593] = 4868,
        [618] = 4962,
        [624] = 5114,
        [625] = 3,
        [626] = 129,
        [627] = 3,
        [628] = 129,
        [630] = 5137,
        [631] = 5138,
        [633] = 172,
        [635] = 5215,
        [637] = 5214,
        [641] = 5306,
        [646] = 5322,
        [650] = 3,
        [656] = 5333,
        [659] = 5349,
        [661] = 176,
        [662] = 176,
        [666] = 5395,
        [667] = 5398,
        [668] = 5400,
        [669] = 5401,
        [670] = 5402,
        [671] = 5403,
        [672] = 5404,
        [673] = 5405,
        [674] = 5406,
        [675] = 5407,
        [676] = 5408,
        [677] = 5417,
        [678] = 5419,
        [679] = 5421,
        [680] = 5423,
        [681] = 5425,
        [682] = 5427,
        [683] = 5433,
        [684] = 5435,
        [685] = 5429,
        [686] = 5431,
        [687] = 5439,
        [688] = 5440,
        [689] = 5441,
        [690] = 5442,
        [691] = 5443,
        [692] = 5444,
        [697] = 5471,
        [700] = 5114,
        [701] = 5333,
        [708] = 5493,
        [717] = 5569,
        [718] = 5570,
        [719] = 5571,
        [722] = 5622,
        [726] = 929,
        [727] = 5674,
        [728] = 5675,
        [729] = 5676,
        [730] = 5677,
        [731] = 5678,
        [732] = 5679,
        [734] = 5710,
        [735] = 5733,
        [736] = 5920,
        [737] = 5922,
        [738] = 5924,
        [739] = 5926,
        [740] = 5928,
        [741] = 5930,
        [742] = 5953,
        [743] = 5996,
        [744] = 6019,
        [745] = 6042,
        [746] = 6065,
        [747] = 6088,
        [748] = 6109,
        [749] = 6132,
        [750] = 6134,
    };

    /// <summary>
    /// 取某图格的掉落物：<paramref name="itemId"/> = 0 表示不掉落。
    /// <paramref name="frameX"/> 和 <paramref name="frameY"/> 用于少数样式掉落；其余帧敏感规则由专用分支处理。
    /// <paramref name="stack"/> 恒为 1（原版的多种子 / 多木材依赖帧与玩家工具，暂未建模）。
    /// </summary>
    public static bool TryGet(int tileType, short frameX, short frameY, out int itemId, out int stack)
    {
        stack = 1;
        switch (tileType)
        {
            case 3:
            case 110:
                itemId = frameX == 144 ? 5 : 0;
                return itemId != 0;
            case 24:
                itemId = frameX == 144 ? 60 : 0;
                return itemId != 0;
            case 201:
                itemId = frameX == 270 ? 2887 : 0;
                return itemId != 0;
            case 14:
                itemId = GetTablesDrop(frameX / 54, secondType: false);
                return true;
            case 469:
                itemId = GetTablesDrop(frameX / 54, secondType: true);
                return true;
            case 441:
                itemId = GetFakeChestsDrop(frameX / 36, secondType: false);
                return itemId != 0;
            case 468:
                itemId = GetFakeChestsDrop(frameX / 36, secondType: true);
                return itemId != 0;
            case 15:
                itemId = GetChairDrop(frameY / 40);
                return true;
            case 18:
                itemId = GetWorkbenchesDrop(frameX / 36);
                return true;
            case 34:
                itemId = GetChandeliersDrop(frameY / 54 + 37 * (frameX / 108));
                return true;
            case 42:
                itemId = GetLanternsDrop(frameY / 36);
                return true;
            case 79:
                itemId = GetBedsDrop(frameY / 36);
                return true;
            case 87:
                itemId = GetPianosDrop(frameX / 54);
                return true;
            case 89:
                itemId = GetBenchesDrop(frameX / 54);
                return true;
            case 90:
                itemId = GetBathtubsDrop(frameY / 36);
                return true;
            case 93:
                itemId = GetLampsDrop(frameY / 54);
                return true;
            case 100:
                itemId = GetCandelabrasDrop(frameY / 36);
                return true;
            case 101:
                itemId = GetBookcasesDrop(frameX / 54);
                return true;
            case 104:
                itemId = GetClocksDrop(frameX / 36);
                return true;
            case 139:
                itemId = GetMusicBoxesDrop(frameY / 36);
                return true;
            case 172:
                itemId = GetSinksDrop(frameY / 38);
                return true;
            case 497:
                itemId = GetToiletDrop(frameY / 40);
                return true;
            case 487:
                itemId = (frameX / 72) switch
                {
                    _ => 4064,
                };
                return true;
            case 493:
                itemId = (frameX / 18) switch
                {
                    >= 0 and <= 5 => 4083 + frameX / 18,
                    _ => 0,
                };
                return itemId != 0;
            case 33:
                itemId = (frameY / 22) switch
                {
                    1 => 1405, 2 => 1406, 3 => 1407,
                    >= 4 and <= 13 => 2045 + frameY / 22 - 4,
                    >= 14 and <= 16 => 2153 + frameY / 22 - 14,
                    17 => 2236, 18 => 2523, 19 => 2542, 20 => 2556, 21 => 2571,
                    22 => 2648, 23 => 2649, 24 => 2650, 25 => 2651, 26 => 2818,
                    27 => 3171, 28 => 3173, 29 => 3172, 30 => 3890, 31 => 3936,
                    32 => 3962, 33 => 4150, 34 => 4171, 35 => 4192, 36 => 4213,
                    37 => 4303, 38 => 4571, 39 => 5153, 40 => 5174, 41 => 5195,
                    42 => 5553, 43 => 5606, 44 => 5694, 45 => 5717, 46 => 5743,
                    47 => 5760, 48 => 5781, 49 => 5802, 50 => 5823, 51 => 5844,
                    52 => 5862, 53 => 5883, 54 => 5902, 55 => 5936, 56 => 5959,
                    57 => 5979, 58 => 6002, 59 => 6025, 60 => 6048, 61 => 6071,
                    62 => 6094, 63 => 6115,
                    _ => 105,
                };
                return true;
            case 19:
                itemId = (frameY / 18) switch
                {
                    1 => 631, 2 => 632, 3 => 633, 4 => 634, 5 => 913,
                    6 => 1384, 7 => 1385, 8 => 1386, 9 => 1387, 10 => 1388,
                    11 => 1389, 12 => 1418, 13 => 1457, 14 => 1702, 15 => 1796,
                    16 => 1818, 17 => 2518, 18 => 2549, 19 => 2566, 20 => 2581,
                    21 => 2627, 22 => 2628, 23 => 2629, 24 => 2630, 25 => 2744,
                    26 => 2822, 27 => 3144, 28 => 3146, 29 => 3145,
                    >= 30 and <= 35 => 3903 + frameY / 18 - 30,
                    36 => 3945, 37 => 3957, 38 => 4159, 39 => 4180, 40 => 4201,
                    41 => 4222, 42 => 4311, 43 => 4416, 44 => 4580, 45 => 5162,
                    46 => 5183, 47 => 5204, 48 => 5292, 49 => 5544, 50 => 5562,
                    51 => 5615, 52 => 5703, 53 => 5726, 54 => 5751, 55 => 5770,
                    56 => 5791, 57 => 5812, 58 => 5833, 59 => 5852, 60 => 5872,
                    61 => 5912, 62 => 5946, 63 => 5989, 64 => 6012, 65 => 6035,
                    66 => 6058, 67 => 6081, 68 => 6103, 69 => 6125,
                    _ => 94,
                };
                return true;
            case 13:
                itemId = (frameX / 18) switch
                {
                    1 => 28,
                    2 => 110,
                    3 => 350,
                    4 => 351,
                    5 => 2234,
                    6 => 2244,
                    7 => 2257,
                    8 => 2258,
                    _ => 31,
                };
                return true;
            case 227:
            {
                int style = frameX / 34;
                itemId = style is >= 8 and <= 11 ? 3385 + style - 8 : 1107 + style;
                return true;
            }
            case 178:
                itemId = (frameX / 18) switch
                {
                    0 => 181,
                    1 => 180,
                    2 => 177,
                    3 => 179,
                    4 => 178,
                    5 => 182,
                    6 => 999,
                    _ => 0,
                };
                return itemId != 0;
            case 703:
                itemId = (frameX / 18) switch
                {
                    6 or 7 => 208,
                    8 => 331,
                    9 => 223,
                    _ => 195,
                };
                return true;
            // 原版 WorldGen.KillTile_GetItemDrops：这些图格仅由帧决定摆放样式。
            case 4:
                itemId = (frameY / 22) switch
                {
                    0 => 8,
                    8 => 523,
                    9 => 974,
                    10 => 1245,
                    11 => 1333,
                    12 => 2274,
                    13 => 3004,
                    14 => 3045,
                    15 => 3114,
                    16 => 4383,
                    17 => 4384,
                    18 => 4385,
                    19 => 4386,
                    20 => 4387,
                    21 => 4388,
                    22 => 5293,
                    23 => 5353,
                    int style => 426 + style,
                };
                return true;
            // 图格 239 的摆放样式编码在横向帧中。
            case 135:
                itemId = (frameY / 18) switch
                {
                    0 => 529,
                    1 => 541,
                    2 => 542,
                    3 => 543,
                    4 => 852,
                    5 => 853,
                    6 => 1151,
                    _ => 0,
                };
                return itemId != 0;
            case 137:
                itemId = (frameY / 18) switch
                {
                    0 => 539,
                    1 => 1146,
                    2 => 1147,
                    3 => 1148,
                    4 => 1149,
                    5 => 5135,
                    _ => 0,
                };
                return itemId != 0;
            case 144:
                itemId = frameX switch
                {
                    0 => 583,
                    18 => 584,
                    36 => 585,
                    54 => 4484,
                    72 => 4485,
                    _ => 0,
                };
                return itemId != 0;
            case 239:
                itemId = (frameX / 18) switch
                {
                    0 => 20, 1 => 703, 2 => 22, 3 => 704, 4 => 21, 5 => 705,
                    6 => 19, 7 => 706, 8 => 57, 9 => 117, 10 => 175, 11 => 381,
                    12 => 1184, 13 => 382, 14 => 1191, 15 => 391, 16 => 1198,
                    17 => 1006, 18 => 1225, 19 => 1257, 20 => 1552, 21 => 3261,
                    22 => 3467, _ => 0,
                };
                return itemId != 0;
            // 大堆物体的放置样式编码在纵向帧中。
            case 324:
                itemId = (frameY / 22) switch
                {
                    0 => 2625,
                    1 => 2626,
                    2 => 4072,
                    3 => 4073,
                    4 => 4071,
                    _ => 0,
                };
                return itemId != 0;
            case 380:
                itemId = 3215 + frameY / 18;
                return true;
            case 419:
                itemId = (frameX / 18) switch
                {
                    0 => 3602,
                    1 => 3618,
                    2 => 3663,
                    _ => 0,
                };
                return itemId != 0;
            case 420:
                itemId = (frameY / 18) switch
                {
                    0 => 3603,
                    1 => 3604,
                    2 => 3605,
                    3 => 3606,
                    4 => 3607,
                    5 => 3608,
                    _ => 0,
                };
                return itemId != 0;
            case 423:
                itemId = (frameY / 18) switch
                {
                    0 => 3613,
                    1 => 3614,
                    2 => 3615,
                    3 => 3726,
                    4 => 3727,
                    5 => 3728,
                    6 => 3729,
                    _ => 0,
                };
                return itemId != 0;
            case 428:
                itemId = (frameY / 18) switch
                {
                    0 => 3630,
                    1 => 3632,
                    2 => 3631,
                    3 => 3626,
                    _ => 0,
                };
                return itemId != 0;
            case 650:
            {
                int style = frameX / 18;
                itemId = style switch
                {
                    < 6 => 3,
                    < 12 => 2,
                    < 28 => 154,
                    < 36 => 9,
                    < 42 => 593,
                    < 48 => 664,
                    < 54 => 150,
                    < 60 => 3271,
                    < 66 => 3086,
                    < 72 => 3081,
                    < 73 => 62,
                    73 or 74 or 76 or 78 or 79 or 80 or 81 => 169,
                    75 or 77 => 276,
                    _ => 0,
                };
                return itemId != 0;
            }
            case 50:
            case 707:
                itemId = frameX == 90 ? 165 : 149;
                return true;
            case 129:
                itemId = frameX >= 324 ? 4988 : 502;
                return true;
            case 149:
                itemId = frameX switch
                {
                    0 or 54 => 596,
                    18 or 72 => 597,
                    36 or 90 => 598,
                    _ => 0,
                };
                return itemId != 0;
        }

        if (Of.TryGetValue(tileType, out itemId)) return true;

        if (IsTreeTile(tileType))
        {
            itemId = Wood;
            return true;
        }

        itemId = 0;
        return false;
    }

    private static int GetTablesDrop(int style, bool secondType)
    {
        if (secondType)
        {
            return style switch
            {
                1 => 3948, 2 => 3974, 3 => 4162, 4 => 4183, 5 => 4204, 6 => 4225,
                7 => 4314, 8 => 4583, 9 => 5165, 10 => 5186, 11 => 5207, 12 => 5565,
                13 => 5618, 14 => 5706, 15 => 5729, 16 => 5773, 17 => 5794, 18 => 5815,
                19 => 5836, 20 => 5875, 21 => 5894, 22 => 5915, 23 => 5949, 24 => 5971,
                25 => 5992, 26 => 6015, 27 => 6038, 28 => 6061, 29 => 6084, 30 => 6106,
                31 => 6128,
                _ => 3920,
            };
        }

        if (style is >= 1 and <= 3) return 637 + style;
        if (style is >= 15 and <= 20) return 1698 + style;
        if (style is >= 4 and <= 7) return 823 + style;

        return style switch
        {
            8 => 917, 9 => 1144, 10 => 1397, 11 => 1400, 12 => 1403, 13 => 1460,
            14 => 1510, 21 => 1794, 22 => 1816, 23 => 1926, 24 => 2248, 25 => 2259,
            26 => 2532, 27 => 2550, 28 => 677, 29 => 2583, 30 => 2743, 31 => 2824,
            32 => 3153, 33 => 3155, 34 => 3154,
            _ => 32,
        };
    }

    private static int GetFakeChestsDrop(int style, bool secondType)
    {
        if (secondType)
        {
            return style switch
            {
                1 => 3887, 2 => 3950, 3 => 3976, 4 or 13 => 0, 5 => 4164, 6 => 4185,
                7 => 4206, 8 => 4227, 9 => 4266, 10 => 4268, 11 => 4585, 12 => 4713,
                14 => 5167, 15 => 5188, 16 => 5209, 17 => 5567, 18 => 5620, 19 => 5708,
                20 => 5731, 21 => 5754, 22 => 5776, 23 => 5797, 24 => 5818, 25 => 5839,
                26 => 5857, 27 => 5878, 28 => 5897, 29 => 5918, 30 => 5952, 31 => 5974,
                32 => 5995, 33 => 6018, 34 => 6041, 35 => 6064, 36 => 6087, 37 => 6131,
                _ => 3886,
            };
        }

        return style switch
        {
            1 => 3666, 3 => 3667, 7 => 3668, 8 => 3669, 9 => 3670, 10 => 3671,
            11 => 3672, 12 => 3673, 13 => 3674, 14 => 3675, 15 => 3676, 16 => 3677,
            17 => 3678, 18 => 3679, 19 => 3680, 20 => 3681, 21 => 3682, 22 => 3683,
            28 => 3684, 29 => 3685, 30 => 3686, 31 => 3687, 32 => 3688, 33 => 3689,
            34 => 3690, 35 => 3691, 37 => 3692, 39 => 3693, 41 => 3694, 42 => 3695,
            43 => 3696, 44 => 3697, 45 => 3698, 46 => 3699, 47 => 3700, 48 => 3701,
            49 => 3702, 50 => 3703, 51 => 3704,
            _ => 3665,
        };
    }

    private static int GetChairDrop(int style)
	{
		switch (style)
		{
		default:
			return 34;
		case 1:
			return 358;
		case 2:
			return 628;
		case 3:
			return 629;
		case 4:
			return 630;
		case 5:
			return 806;
		case 6:
			return 807;
		case 7:
			return 808;
		case 8:
			return 809;
		case 9:
			return 810;
		case 10:
			return 826;
		case 11:
			return 915;
		case 12:
			return 1143;
		case 13:
			return 1396;
		case 14:
			return 1399;
		case 15:
			return 1402;
		case 16:
			return 1459;
		case 17:
			return 1509;
		case 18:
		case 19:
		case 20:
		case 21:
		case 22:
		case 23:
			return 1703 + style - 18;
		case 24:
			return 1792;
		case 25:
			return 1814;
		case 26:
			return 1925;
		case 27:
			return 2228;
		case 28:
			return 2288;
		case 29:
			return 2524;
		case 30:
			return 2557;
		case 31:
			return 2572;
		case 32:
			return 2812;
		case 33:
			return 3174;
		case 34:
			return 3176;
		case 35:
			return 3175;
		case 36:
			return 3889;
		case 37:
			return 3937;
		case 38:
			return 3963;
		case 39:
			return 4151;
		case 40:
			return 4172;
		case 41:
			return 4193;
		case 42:
			return 4214;
		case 43:
			return 4304;
		case 44:
			return 4572;
		case 45:
			return 5154;
		case 46:
			return 5175;
		case 47:
			return 5196;
		case 48:
			return 5554;
		case 49:
			return 5607;
		case 50:
			return 5695;
		case 51:
			return 5718;
		case 52:
			return 5761;
		case 53:
			return 5782;
		case 54:
			return 5803;
		case 55:
			return 5824;
		case 56:
			return 5863;
		case 57:
			return 5884;
		case 58:
			return 5903;
		case 59:
			return 5937;
		case 60:
			return 5960;
		case 61:
			return 5980;
		case 62:
			return 6003;
		case 63:
			return 6026;
		case 64:
			return 6049;
		case 65:
			return 6072;
		case 66:
			return 6095;
		case 67:
			return 6116;
		}
	}

    private static int GetWorkbenchesDrop(int style)
	{
		int result = 36;
		if (style >= 1 && style <= 3)
		{
			result = 634 + style;
		}
		else if (style >= 4 && style <= 8)
		{
			result = 807 + style;
		}
		else
		{
			switch (style)
			{
			case 9:
				result = 916;
				break;
			case 10:
				result = 1145;
				break;
			case 11:
				result = 1398;
				break;
			case 12:
				result = 1401;
				break;
			case 13:
				result = 1404;
				break;
			case 14:
				result = 1461;
				break;
			case 15:
				result = 1511;
				break;
			case 16:
				result = 1795;
				break;
			case 17:
				result = 1817;
				break;
			case 18:
				result = 2229;
				break;
			case 19:
				result = 2251;
				break;
			case 20:
				result = 2252;
				break;
			case 21:
				result = 2253;
				break;
			case 22:
				result = 2534;
				break;
			case 23:
				result = 673;
				break;
			case 24:
				result = 2631;
				break;
			case 25:
				result = 2632;
				break;
			case 26:
				result = 2633;
				break;
			case 27:
				result = 2826;
				break;
			case 28:
				result = 3156;
				break;
			case 29:
				result = 3158;
				break;
			case 30:
				result = 3157;
				break;
			case 31:
				result = 3909;
				break;
			case 32:
				result = 3910;
				break;
			case 33:
				result = 3949;
				break;
			case 34:
				result = 3975;
				break;
			case 35:
				result = 4163;
				break;
			case 36:
				result = 4184;
				break;
			case 37:
				result = 4205;
				break;
			case 38:
				result = 4226;
				break;
			case 39:
				result = 4315;
				break;
			case 40:
				result = 4584;
				break;
			case 41:
				result = 5166;
				break;
			case 42:
				result = 5187;
				break;
			case 43:
				result = 5208;
				break;
			case 44:
				result = 5566;
				break;
			case 45:
				result = 5619;
				break;
			case 46:
				result = 5707;
				break;
			case 47:
				result = 5730;
				break;
			case 48:
				result = 5775;
				break;
			case 49:
				result = 5796;
				break;
			case 50:
				result = 5817;
				break;
			case 51:
				result = 5838;
				break;
			case 52:
				result = 5856;
				break;
			case 53:
				result = 5877;
				break;
			case 54:
				result = 5896;
				break;
			case 55:
				result = 5917;
				break;
			case 56:
				result = 5951;
				break;
			case 57:
				result = 5973;
				break;
			case 58:
				result = 5994;
				break;
			case 59:
				result = 6017;
				break;
			case 60:
				result = 6040;
				break;
			case 61:
				result = 6063;
				break;
			case 62:
				result = 6086;
				break;
			case 63:
				result = 6108;
				break;
			case 64:
				result = 6130;
				break;
			}
		}
		return result;
	}

    private static int GetChandeliersDrop(int style)
	{
		int result = 106;
		switch (style)
		{
		case 1:
			result = 107;
			break;
		case 2:
			result = 108;
			break;
		case 3:
			result = 710;
			break;
		case 4:
			result = 711;
			break;
		case 5:
			result = 712;
			break;
		case 6:
			result = 1812;
			break;
		case 7:
		case 8:
		case 9:
		case 10:
		case 11:
		case 12:
		case 13:
		case 14:
		case 15:
		case 16:
		case 17:
			result = 2055 + style - 7;
			break;
		default:
			if (style >= 18 && style <= 21)
			{
				result = 2141 + style - 18;
				break;
			}
			switch (style)
			{
			case 22:
				result = 2224;
				break;
			case 23:
				result = 2525;
				break;
			case 24:
				result = 2543;
				break;
			case 25:
				result = 2558;
				break;
			case 26:
				result = 2573;
				break;
			case 27:
				result = 2652;
				break;
			case 28:
				result = 2653;
				break;
			case 29:
				result = 2654;
				break;
			case 30:
				result = 2655;
				break;
			case 31:
				result = 2656;
				break;
			case 32:
				result = 2657;
				break;
			case 33:
				result = 2813;
				break;
			case 34:
				result = 3177;
				break;
			case 35:
				result = 3179;
				break;
			case 36:
				result = 3178;
				break;
			case 37:
				result = 3894;
				break;
			case 38:
				result = 3938;
				break;
			case 39:
				result = 3964;
				break;
			case 40:
				result = 4152;
				break;
			case 41:
				result = 4173;
				break;
			case 42:
				result = 4194;
				break;
			case 43:
				result = 4215;
				break;
			case 44:
				result = 4305;
				break;
			case 45:
				result = 4573;
				break;
			case 46:
				result = 5155;
				break;
			case 47:
				result = 5176;
				break;
			case 48:
				result = 5197;
				break;
			case 49:
				result = 5555;
				break;
			case 50:
				result = 5608;
				break;
			case 51:
				result = 5696;
				break;
			case 52:
				result = 5719;
				break;
			case 53:
				result = 5744;
				break;
			case 54:
				result = 5762;
				break;
			case 55:
				result = 5783;
				break;
			case 56:
				result = 5804;
				break;
			case 57:
				result = 5825;
				break;
			case 58:
				result = 5845;
				break;
			case 59:
				result = 5864;
				break;
			case 60:
				result = 5885;
				break;
			case 61:
				result = 5904;
				break;
			case 62:
				result = 5938;
				break;
			case 63:
				result = 5961;
				break;
			case 64:
				result = 5981;
				break;
			case 65:
				result = 6004;
				break;
			case 66:
				result = 6027;
				break;
			case 67:
				result = 6050;
				break;
			case 68:
				result = 6073;
				break;
			case 69:
				result = 6096;
				break;
			case 70:
				result = 6117;
				break;
			}
			break;
		}
		return result;
	}

    private static int GetLanternsDrop(int style)
	{
		int result = 136;
		if (style == 0)
		{
			result = 136;
		}
		else if (style == 7)
		{
			result = 1431;
		}
		else if (style == 8)
		{
			result = 1808;
		}
		else if (style == 9)
		{
			result = 1859;
		}
		else if (style < 10)
		{
			result = 1389 + style;
		}
		else
		{
			switch (style)
			{
			case 10:
				result = 2032;
				break;
			case 11:
				result = 2033;
				break;
			case 12:
				result = 2034;
				break;
			case 13:
				result = 2035;
				break;
			case 14:
				result = 2036;
				break;
			case 15:
				result = 2037;
				break;
			case 16:
				result = 2038;
				break;
			case 17:
				result = 2039;
				break;
			case 18:
				result = 2040;
				break;
			case 19:
				result = 2041;
				break;
			case 20:
				result = 2042;
				break;
			case 21:
				result = 2043;
				break;
			case 22:
			case 23:
			case 24:
			case 25:
				result = 2145 + style - 22;
				break;
			default:
				switch (style)
				{
				case 26:
					result = 2226;
					break;
				case 27:
					result = 2530;
					break;
				case 28:
					result = 2546;
					break;
				case 29:
					result = 2564;
					break;
				case 30:
					result = 2579;
					break;
				case 31:
					result = 2641;
					break;
				case 32:
					result = 2642;
					break;
				case 33:
					result = 2820;
					break;
				case 34:
					result = 3138;
					break;
				case 35:
					result = 3140;
					break;
				case 36:
					result = 3139;
					break;
				case 37:
					result = 3891;
					break;
				case 38:
					result = 3943;
					break;
				case 39:
					result = 3970;
					break;
				case 40:
					result = 4157;
					break;
				case 41:
					result = 4178;
					break;
				case 42:
					result = 4199;
					break;
				case 43:
					result = 4220;
					break;
				case 44:
					result = 4309;
					break;
				case 45:
					result = 4578;
					break;
				case 46:
					result = 5160;
					break;
				case 47:
					result = 5181;
					break;
				case 48:
					result = 5202;
					break;
				case 49:
					result = 5560;
					break;
				case 50:
					result = 5613;
					break;
				case 51:
					result = 5701;
					break;
				case 52:
					result = 5724;
					break;
				case 53:
					result = 5749;
					break;
				case 54:
					result = 5768;
					break;
				case 55:
					result = 5789;
					break;
				case 56:
					result = 5810;
					break;
				case 57:
					result = 5831;
					break;
				case 58:
					result = 5850;
					break;
				case 59:
					result = 5870;
					break;
				case 60:
					result = 5890;
					break;
				case 61:
					result = 5910;
					break;
				case 62:
					result = 5944;
					break;
				case 63:
					result = 5967;
					break;
				case 64:
					result = 5987;
					break;
				case 65:
					result = 6010;
					break;
				case 66:
					result = 6033;
					break;
				case 67:
					result = 6056;
					break;
				case 68:
					result = 6079;
					break;
				case 69:
					result = 6101;
					break;
				case 70:
					result = 6123;
					break;
				}
				break;
			}
		}
		return result;
	}

    private static int GetBedsDrop(int style)
	{
		int result = 224;
		switch (style)
		{
		case 0:
			result = 224;
			break;
		case 1:
		case 2:
		case 3:
			result = style + 643;
			break;
		default:
			switch (style)
			{
			case 4:
				result = 920;
				break;
			case 5:
			case 6:
			case 7:
			case 8:
				result = 1465 + style;
				break;
			default:
				if (style >= 9 && style <= 12)
				{
					result = 1710 + style;
					break;
				}
				if (style >= 13 && style <= 18)
				{
					result = 2066 + style - 13;
					break;
				}
				switch (style)
				{
				case 19:
					result = 2139;
					break;
				case 20:
					result = 2140;
					break;
				case 21:
					result = 2231;
					break;
				case 22:
					result = 2520;
					break;
				case 23:
					result = 2538;
					break;
				case 24:
					result = 2553;
					break;
				case 25:
					result = 2568;
					break;
				case 26:
					result = 2669;
					break;
				case 27:
					result = 2811;
					break;
				case 28:
					result = 3162;
					break;
				case 29:
					result = 3164;
					break;
				case 30:
					result = 3163;
					break;
				case 31:
					result = 3897;
					break;
				case 32:
					result = 3932;
					break;
				case 33:
					result = 3959;
					break;
				case 34:
					result = 4146;
					break;
				case 35:
					result = 4167;
					break;
				case 36:
					result = 4188;
					break;
				case 37:
					result = 4209;
					break;
				case 38:
					result = 4299;
					break;
				case 39:
					result = 4567;
					break;
				case 40:
					result = 5149;
					break;
				case 41:
					result = 5170;
					break;
				case 42:
					result = 5191;
					break;
				case 43:
					result = 5549;
					break;
				case 44:
					result = 5602;
					break;
				case 45:
					result = 5690;
					break;
				case 46:
					result = 5713;
					break;
				case 47:
					result = 5740;
					break;
				case 48:
					result = 5757;
					break;
				case 49:
					result = 5778;
					break;
				case 50:
					result = 5799;
					break;
				case 51:
					result = 5820;
					break;
				case 52:
					result = 5841;
					break;
				case 53:
					result = 5859;
					break;
				case 54:
					result = 5880;
					break;
				case 55:
					result = 5899;
					break;
				case 56:
					result = 5933;
					break;
				case 57:
					result = 5956;
					break;
				case 58:
					result = 5976;
					break;
				case 59:
					result = 5999;
					break;
				case 60:
					result = 6022;
					break;
				case 61:
					result = 6045;
					break;
				case 62:
					result = 6068;
					break;
				case 63:
					result = 6091;
					break;
				case 64:
					result = 6112;
					break;
				}
				break;
			}
			break;
		}
		return result;
	}

    private static int GetPianosDrop(int style)
	{
		int result = 333;
		if (style >= 1 && style <= 3)
		{
			result = 640 + style;
		}
		else
		{
			switch (style)
			{
			case 4:
				result = 919;
				break;
			case 5:
			case 6:
			case 7:
				result = 2245 + style - 5;
				break;
			default:
				if (style >= 8 && style <= 10)
				{
					result = 2254 + style - 8;
					break;
				}
				if (style >= 11 && style <= 20)
				{
					result = 2376 + style - 11;
					break;
				}
				switch (style)
				{
				case 21:
					result = 2531;
					break;
				case 22:
					result = 2548;
					break;
				case 23:
					result = 2565;
					break;
				case 24:
					result = 2580;
					break;
				case 25:
					result = 2671;
					break;
				case 26:
					result = 2821;
					break;
				case 27:
					result = 3141;
					break;
				case 28:
					result = 3143;
					break;
				case 29:
					result = 3142;
					break;
				case 30:
					result = 3915;
					break;
				case 31:
					result = 3916;
					break;
				case 32:
					result = 3944;
					break;
				case 33:
					result = 3971;
					break;
				case 34:
					result = 4158;
					break;
				case 35:
					result = 4179;
					break;
				case 36:
					result = 4200;
					break;
				case 37:
					result = 4221;
					break;
				case 38:
					result = 4310;
					break;
				case 39:
					result = 4579;
					break;
				case 40:
					result = 5161;
					break;
				case 41:
					result = 5182;
					break;
				case 42:
					result = 5203;
					break;
				case 43:
					result = 5561;
					break;
				case 44:
					result = 5614;
					break;
				case 45:
					result = 5702;
					break;
				case 46:
					result = 5725;
					break;
				case 47:
					result = 5750;
					break;
				case 48:
					result = 5769;
					break;
				case 49:
					result = 5790;
					break;
				case 50:
					result = 5811;
					break;
				case 51:
					result = 5832;
					break;
				case 52:
					result = 5851;
					break;
				case 53:
					result = 5871;
					break;
				case 54:
					result = 5891;
					break;
				case 55:
					result = 5911;
					break;
				case 56:
					result = 5945;
					break;
				case 57:
					result = 5968;
					break;
				case 58:
					result = 5988;
					break;
				case 59:
					result = 6011;
					break;
				case 60:
					result = 6034;
					break;
				case 61:
					result = 6057;
					break;
				case 62:
					result = 6080;
					break;
				case 63:
					result = 6102;
					break;
				case 64:
					result = 6124;
					break;
				}
				break;
			}
		}
		return result;
	}

    private static int GetBenchesDrop(int style)
	{
		return style switch
		{
			1 => 2397,
			2 => 2398,
			3 => 2399,
			4 => 2400,
			5 => 2401,
			6 => 2402,
			7 => 2403,
			8 => 2404,
			9 => 2405,
			10 => 2406,
			11 => 2407,
			12 => 2408,
			13 => 2409,
			14 => 2410,
			15 => 2411,
			16 => 2412,
			17 => 2413,
			18 => 2414,
			19 => 2415,
			20 => 2416,
			21 => 2521,
			22 => 2527,
			23 => 2539,
			24 => 858,
			25 => 2582,
			26 => 2634,
			27 => 2635,
			28 => 2636,
			29 => 2823,
			30 => 3150,
			31 => 3152,
			32 => 3151,
			33 => 3918,
			34 => 3919,
			35 => 3947,
			36 => 3973,
			37 => 4161,
			38 => 4182,
			39 => 4203,
			40 => 4224,
			41 => 4313,
			42 => 4582,
			43 => 4993,
			44 => 5164,
			45 => 5185,
			46 => 5206,
			47 => 5564,
			48 => 5617,
			49 => 5705,
			50 => 5728,
			51 => 5753,
			52 => 5772,
			53 => 5793,
			54 => 5814,
			55 => 5835,
			56 => 5854,
			57 => 5874,
			58 => 5893,
			59 => 5914,
			60 => 5948,
			61 => 5970,
			62 => 5991,
			63 => 6014,
			64 => 6037,
			65 => 6060,
			66 => 6083,
			67 => 6105,
			68 => 6127,
			_ => 335,
		};
	}

    private static int GetBathtubsDrop(int style)
	{
		int result = 336;
		if (style >= 1 && style <= 10)
		{
			result = 2072 + style - 1;
		}
		else if (style >= 11 && style <= 15)
		{
			result = 2124 + style - 11;
		}
		switch (style)
		{
		case 0:
			result = 336;
			break;
		case 16:
			result = 2232;
			break;
		case 17:
			result = 2519;
			break;
		case 18:
			result = 2537;
			break;
		case 19:
			result = 2552;
			break;
		case 20:
			result = 2567;
			break;
		case 21:
			result = 2658;
			break;
		case 22:
			result = 2659;
			break;
		case 23:
			result = 2660;
			break;
		case 24:
			result = 2661;
			break;
		case 25:
			result = 2662;
			break;
		case 26:
			result = 2663;
			break;
		case 27:
			result = 2810;
			break;
		case 28:
			result = 3159;
			break;
		case 29:
			result = 3161;
			break;
		case 30:
			result = 3160;
			break;
		case 31:
			result = 3895;
			break;
		case 32:
			result = 3931;
			break;
		case 33:
			result = 3958;
			break;
		case 34:
			result = 4145;
			break;
		case 35:
			result = 4166;
			break;
		case 36:
			result = 4187;
			break;
		case 37:
			result = 4208;
			break;
		case 38:
			result = 4298;
			break;
		case 39:
			result = 4566;
			break;
		case 40:
			result = 5148;
			break;
		case 41:
			result = 5169;
			break;
		case 42:
			result = 5190;
			break;
		case 43:
			result = 5548;
			break;
		case 44:
			result = 5601;
			break;
		case 45:
			result = 5689;
			break;
		case 46:
			result = 5712;
			break;
		case 47:
			result = 5739;
			break;
		case 48:
			result = 5756;
			break;
		case 49:
			result = 5777;
			break;
		case 50:
			result = 5798;
			break;
		case 51:
			result = 5819;
			break;
		case 52:
			result = 5840;
			break;
		case 53:
			result = 5858;
			break;
		case 54:
			result = 5879;
			break;
		case 55:
			result = 5898;
			break;
		case 56:
			result = 5932;
			break;
		case 57:
			result = 5955;
			break;
		case 58:
			result = 5975;
			break;
		case 59:
			result = 5998;
			break;
		case 60:
			result = 6021;
			break;
		case 61:
			result = 6044;
			break;
		case 62:
			result = 6067;
			break;
		case 63:
			result = 6090;
			break;
		case 64:
			result = 6111;
			break;
		}
		return result;
	}

    private static int GetLampsDrop(int style)
	{
		int result = 342;
		switch (style)
		{
		case 0:
			result = 342;
			break;
		case 1:
		case 2:
		case 3:
		case 4:
		case 5:
		case 6:
		case 7:
		case 8:
		case 9:
		case 10:
			result = 2082 + style - 1;
			break;
		default:
			if (style >= 11 && style <= 16)
			{
				result = 2129 + style - 11;
				break;
			}
			switch (style)
			{
			case 17:
				result = 2225;
				break;
			case 18:
				result = 2533;
				break;
			case 19:
				result = 2547;
				break;
			case 20:
				result = 2563;
				break;
			case 21:
				result = 2578;
				break;
			case 22:
				result = 2643;
				break;
			case 23:
				result = 2644;
				break;
			case 24:
				result = 2645;
				break;
			case 25:
				result = 2646;
				break;
			case 26:
				result = 2647;
				break;
			case 27:
				result = 2819;
				break;
			case 28:
				result = 3135;
				break;
			case 29:
				result = 3137;
				break;
			case 30:
				result = 3136;
				break;
			case 31:
				result = 3892;
				break;
			case 32:
				result = 3942;
				break;
			case 33:
				result = 3969;
				break;
			case 34:
				result = 4156;
				break;
			case 35:
				result = 4177;
				break;
			case 36:
				result = 4198;
				break;
			case 37:
				result = 4219;
				break;
			case 38:
				result = 4308;
				break;
			case 39:
				result = 4577;
				break;
			case 40:
				result = 5159;
				break;
			case 41:
				result = 5180;
				break;
			case 42:
				result = 5201;
				break;
			case 43:
				result = 5559;
				break;
			case 44:
				result = 5612;
				break;
			case 45:
				result = 5700;
				break;
			case 46:
				result = 5723;
				break;
			case 47:
				result = 5748;
				break;
			case 48:
				result = 5767;
				break;
			case 49:
				result = 5788;
				break;
			case 50:
				result = 5809;
				break;
			case 51:
				result = 5830;
				break;
			case 52:
				result = 5849;
				break;
			case 53:
				result = 5869;
				break;
			case 54:
				result = 5889;
				break;
			case 55:
				result = 5909;
				break;
			case 56:
				result = 5943;
				break;
			case 57:
				result = 5966;
				break;
			case 58:
				result = 5986;
				break;
			case 59:
				result = 6009;
				break;
			case 60:
				result = 6032;
				break;
			case 61:
				result = 6055;
				break;
			case 62:
				result = 6078;
				break;
			case 63:
				result = 6100;
				break;
			case 64:
				result = 6122;
				break;
			}
			break;
		}
		return result;
	}

    private static int GetCandelabrasDrop(int style)
	{
		int result = 349;
		switch (style)
		{
		case 0:
			result = 349;
			break;
		case 1:
		case 2:
		case 3:
		case 4:
		case 5:
		case 6:
		case 7:
		case 8:
		case 9:
		case 10:
		case 11:
		case 12:
			result = 2092 + style - 1;
			break;
		default:
			if (style >= 13 && style <= 16)
			{
				result = 2149 + style - 13;
				break;
			}
			switch (style)
			{
			case 17:
				result = 2227;
				break;
			case 18:
				result = 2522;
				break;
			case 19:
				result = 2541;
				break;
			case 20:
				result = 2555;
				break;
			case 21:
				result = 2570;
				break;
			case 22:
				result = 2664;
				break;
			case 23:
				result = 2665;
				break;
			case 24:
				result = 2666;
				break;
			case 25:
				result = 2667;
				break;
			case 26:
				result = 2668;
				break;
			case 27:
				result = 2825;
				break;
			case 28:
				result = 3168;
				break;
			case 29:
				result = 3170;
				break;
			case 30:
				result = 3169;
				break;
			case 31:
				result = 3893;
				break;
			case 32:
				result = 3935;
				break;
			case 33:
				result = 3961;
				break;
			case 34:
				result = 4149;
				break;
			case 35:
				result = 4170;
				break;
			case 36:
				result = 4191;
				break;
			case 37:
				result = 4212;
				break;
			case 38:
				result = 4302;
				break;
			case 39:
				result = 4570;
				break;
			case 40:
				result = 5152;
				break;
			case 41:
				result = 5173;
				break;
			case 42:
				result = 5194;
				break;
			case 43:
				result = 5552;
				break;
			case 44:
				result = 5605;
				break;
			case 45:
				result = 5693;
				break;
			case 46:
				result = 5716;
				break;
			case 47:
				result = 5742;
				break;
			case 48:
				result = 5759;
				break;
			case 49:
				result = 5780;
				break;
			case 50:
				result = 5801;
				break;
			case 51:
				result = 5822;
				break;
			case 52:
				result = 5843;
				break;
			case 53:
				result = 5861;
				break;
			case 54:
				result = 5882;
				break;
			case 55:
				result = 5901;
				break;
			case 56:
				result = 5935;
				break;
			case 57:
				result = 5958;
				break;
			case 58:
				result = 5978;
				break;
			case 59:
				result = 6001;
				break;
			case 60:
				result = 6024;
				break;
			case 61:
				result = 6047;
				break;
			case 62:
				result = 6070;
				break;
			case 63:
				result = 6093;
				break;
			case 64:
				result = 6114;
				break;
			}
			break;
		}
		return result;
	}

    private static int GetBookcasesDrop(int style)
	{
		int result = 354;
		switch (style)
		{
		case 1:
			result = 1414;
			break;
		case 2:
			result = 1415;
			break;
		case 3:
			result = 1416;
			break;
		case 4:
			result = 1463;
			break;
		case 5:
			result = 1512;
			break;
		case 6:
			result = 2020;
			break;
		case 7:
			result = 2021;
			break;
		case 8:
			result = 2022;
			break;
		case 9:
			result = 2023;
			break;
		case 10:
			result = 2024;
			break;
		case 11:
			result = 2025;
			break;
		case 12:
			result = 2026;
			break;
		case 13:
			result = 2027;
			break;
		case 14:
			result = 2028;
			break;
		case 15:
			result = 2029;
			break;
		case 16:
			result = 2030;
			break;
		case 17:
			result = 2031;
			break;
		case 18:
		case 19:
		case 20:
		case 21:
			result = 2135 + style - 18;
			break;
		default:
			switch (style)
			{
			case 22:
				result = 2233;
				break;
			case 23:
				result = 2536;
				break;
			case 24:
				result = 2540;
				break;
			case 25:
				result = 2554;
				break;
			case 26:
				result = 2569;
				break;
			case 27:
				result = 2670;
				break;
			case 28:
				result = 2817;
				break;
			case 29:
				result = 3165;
				break;
			case 30:
				result = 3167;
				break;
			case 31:
				result = 3166;
				break;
			case 32:
				result = 3917;
				break;
			case 33:
				result = 3933;
				break;
			case 34:
				result = 3960;
				break;
			case 35:
				result = 4147;
				break;
			case 36:
				result = 4168;
				break;
			case 37:
				result = 4189;
				break;
			case 38:
				result = 4210;
				break;
			case 39:
				result = 4300;
				break;
			case 40:
				result = 4568;
				break;
			case 41:
				result = 5150;
				break;
			case 42:
				result = 5171;
				break;
			case 43:
				result = 5192;
				break;
			case 44:
				result = 5550;
				break;
			case 45:
				result = 5603;
				break;
			case 46:
				result = 5691;
				break;
			case 47:
				result = 5714;
				break;
			case 48:
				result = 5758;
				break;
			case 49:
				result = 5779;
				break;
			case 50:
				result = 5800;
				break;
			case 51:
				result = 5821;
				break;
			case 52:
				result = 5842;
				break;
			case 53:
				result = 5860;
				break;
			case 54:
				result = 5881;
				break;
			case 55:
				result = 5900;
				break;
			case 56:
				result = 5934;
				break;
			case 57:
				result = 5957;
				break;
			case 58:
				result = 5977;
				break;
			case 59:
				result = 6000;
				break;
			case 60:
				result = 6023;
				break;
			case 61:
				result = 6046;
				break;
			case 62:
				result = 6069;
				break;
			case 63:
				result = 6092;
				break;
			case 64:
				result = 6113;
				break;
			}
			break;
		}
		return result;
	}

    private static int GetClocksDrop(int style)
	{
		if (style >= 1 && style <= 5)
		{
			return 2237 + style - 1;
		}
		switch (style)
		{
		case 6:
			return 2560;
		case 7:
			return 2575;
		case 8:
		case 9:
		case 10:
		case 11:
		case 12:
		case 13:
		case 14:
		case 15:
		case 16:
		case 17:
		case 18:
		case 19:
		case 20:
		case 21:
		case 22:
		case 23:
			return 2591 + style - 8;
		default:
			return style switch
			{
				24 => 2809,
				25 => 3126,
				26 => 3128,
				27 => 3127,
				28 => 3898,
				29 => 3899,
				30 => 3900,
				31 => 3901,
				32 => 3902,
				33 => 3940,
				34 => 3966,
				35 => 4154,
				36 => 4175,
				37 => 4196,
				38 => 4217,
				39 => 4306,
				40 => 4575,
				41 => 5157,
				42 => 5178,
				43 => 5199,
				44 => 5557,
				45 => 5610,
				46 => 5698,
				47 => 5721,
				48 => 5746,
				49 => 5764,
				50 => 5785,
				51 => 5806,
				52 => 5827,
				53 => 5847,
				54 => 5866,
				55 => 5887,
				56 => 5906,
				57 => 5940,
				58 => 5963,
				59 => 5983,
				60 => 6006,
				61 => 6029,
				62 => 6052,
				63 => 6075,
				64 => 6097,
				65 => 6119,
				_ => 359,
			};
		}
	}

    private static int GetMusicBoxesDrop(int style)
	{
		int result = 576;
		if (style <= 12)
		{
			result = 562 + style;
		}
		else if (style >= 13 && style <= 27)
		{
			result = 1596 + style - 13;
		}
		else
		{
			switch (style)
			{
			case 28:
				result = 1963;
				break;
			case 29:
				result = 1964;
				break;
			case 30:
				result = 1965;
				break;
			case 31:
				result = 2742;
				break;
			case 32:
				result = 3044;
				break;
			case 33:
				result = 3235;
				break;
			case 34:
				result = 3236;
				break;
			case 35:
				result = 3237;
				break;
			case 36:
				result = 3370;
				break;
			case 37:
				result = 3371;
				break;
			case 38:
				result = 3796;
				break;
			case 39:
				result = 3869;
				break;
			case 40:
				result = 4082;
				break;
			case 41:
				result = 4078;
				break;
			case 42:
				result = 4079;
				break;
			case 43:
				result = 4077;
				break;
			case 44:
				result = 4080;
				break;
			case 45:
				result = 4081;
				break;
			case 46:
				result = 4237;
				break;
			case 47:
				result = 4356;
				break;
			case 48:
				result = 4357;
				break;
			case 49:
				result = 4358;
				break;
			case 50:
				result = 4421;
				break;
			case 51:
				result = 4606;
				break;
			case 52:
				result = 4979;
				break;
			case 53:
				result = 4985;
				break;
			case 54:
				result = 4990;
				break;
			case 55:
				result = 4991;
				break;
			case 56:
				result = 4992;
				break;
			case 57:
				result = 5006;
				break;
			case 58:
				result = 5014;
				break;
			case 59:
				result = 5015;
				break;
			case 60:
				result = 5016;
				break;
			case 61:
				result = 5017;
				break;
			case 62:
				result = 5018;
				break;
			case 63:
				result = 5019;
				break;
			case 64:
				result = 5020;
				break;
			case 65:
				result = 5021;
				break;
			case 66:
				result = 5022;
				break;
			case 67:
				result = 5023;
				break;
			case 68:
				result = 5024;
				break;
			case 69:
				result = 5025;
				break;
			case 70:
				result = 5026;
				break;
			case 71:
				result = 5027;
				break;
			case 72:
				result = 5028;
				break;
			case 73:
				result = 5029;
				break;
			case 74:
				result = 5030;
				break;
			case 75:
				result = 5031;
				break;
			case 76:
				result = 5032;
				break;
			case 77:
				result = 5033;
				break;
			case 78:
				result = 5034;
				break;
			case 79:
				result = 5035;
				break;
			case 80:
				result = 5036;
				break;
			case 81:
				result = 5037;
				break;
			case 82:
				result = 5038;
				break;
			case 83:
				result = 5039;
				break;
			case 84:
				result = 5040;
				break;
			case 85:
				result = 5044;
				break;
			case 86:
				result = 5112;
				break;
			case 87:
				result = 5362;
				break;
			case 88:
				result = 5578;
				break;
			case 89:
				result = 5538;
				break;
			case 90:
				result = 5579;
				break;
			case 91:
				result = 5580;
				break;
			case 92:
				result = 5539;
				break;
			case 93:
				result = 5581;
				break;
			case 94:
				result = 5582;
				break;
			case 95:
				result = 5637;
				break;
			case 96:
				result = 5638;
				break;
			case 97:
				result = 5639;
				break;
			case 98:
				result = 6144;
				break;
			case 99:
				result = 6145;
				break;
			case 100:
				result = 6146;
				break;
			}
		}
		return result;
	}

    private static int GetSinksDrop(int style)
	{
		int result = 2827;
		if (style >= 0 && style <= 28)
		{
			result = 2827 + style;
		}
		else
		{
			switch (style)
			{
			case 29:
				result = 3147;
				break;
			case 30:
				result = 3149;
				break;
			case 31:
				result = 3148;
				break;
			case 32:
				result = 3896;
				break;
			case 33:
				result = 3946;
				break;
			case 34:
				result = 3972;
				break;
			case 35:
				result = 4160;
				break;
			case 36:
				result = 4181;
				break;
			case 37:
				result = 4202;
				break;
			case 38:
				result = 4223;
				break;
			case 39:
				result = 4312;
				break;
			case 40:
				result = 4581;
				break;
			case 41:
				result = 5163;
				break;
			case 42:
				result = 5184;
				break;
			case 43:
				result = 5205;
				break;
			case 44:
				result = 5563;
				break;
			case 45:
				result = 5616;
				break;
			case 46:
				result = 5704;
				break;
			case 47:
				result = 5727;
				break;
			case 48:
				result = 5752;
				break;
			case 49:
				result = 5771;
				break;
			case 50:
				result = 5792;
				break;
			case 51:
				result = 5813;
				break;
			case 52:
				result = 5834;
				break;
			case 53:
				result = 5853;
				break;
			case 54:
				result = 5873;
				break;
			case 55:
				result = 5892;
				break;
			case 56:
				result = 5913;
				break;
			case 57:
				result = 5947;
				break;
			case 58:
				result = 5969;
				break;
			case 59:
				result = 5990;
				break;
			case 60:
				result = 6013;
				break;
			case 61:
				result = 6036;
				break;
			case 62:
				result = 6059;
				break;
			case 63:
				result = 6082;
				break;
			case 64:
				result = 6104;
				break;
			case 65:
				result = 6126;
				break;
			}
		}
		return result;
	}

    private static int GetToiletDrop(int style)
	{
		int result = 4096;
		if (style >= 0 && style <= 31)
		{
			result = 4096 + style;
		}
		switch (style)
		{
		case 32:
			result = 4141;
			break;
		case 33:
			result = 4165;
			break;
		case 34:
			result = 4186;
			break;
		case 35:
			result = 4207;
			break;
		case 36:
			result = 4228;
			break;
		case 37:
			result = 4316;
			break;
		case 38:
			result = 4586;
			break;
		case 39:
			result = 4731;
			break;
		case 40:
			result = 5168;
			break;
		case 41:
			result = 5189;
			break;
		case 42:
			result = 5210;
			break;
		case 43:
			result = 5568;
			break;
		case 44:
			result = 5621;
			break;
		case 45:
			result = 5709;
			break;
		case 46:
			result = 5732;
			break;
		case 47:
			result = 5755;
			break;
		case 48:
			result = 5774;
			break;
		case 49:
			result = 5795;
			break;
		case 50:
			result = 5816;
			break;
		case 51:
			result = 5837;
			break;
		case 52:
			result = 5855;
			break;
		case 53:
			result = 5876;
			break;
		case 54:
			result = 5895;
			break;
		case 55:
			result = 5916;
			break;
		case 56:
			result = 5950;
			break;
		case 57:
			result = 5972;
			break;
		case 58:
			result = 5993;
			break;
		case 59:
			result = 6016;
			break;
		case 60:
			result = 6039;
			break;
		case 61:
			result = 6062;
			break;
		case 62:
			result = 6085;
			break;
		case 63:
			result = 6107;
			break;
		case 64:
			result = 6129;
			break;
		}
		return result;
	}
}
