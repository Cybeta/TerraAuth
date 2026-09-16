// TerraAuth — Phase 6: 图格 ID 静态集合
// 权威来源：原版客户端的图格 ID 集合定义（Terraria 1.4.5.8 / Protocol 326）
//   - 实心图格集合（298 个 true，已剔除被显式置 false 的 110/3/4/5/11/634/379）
//   - 可保存斜坡 = 实心图格 ∪ 非实心可保存斜坡
//   - 基础箱子 / 告示牌集合

using System.Collections.Frozen;
using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>原版 <c>TileID.Sets</c> 与 <c>Main.tileSolid/tileSign</c> 中与存档 / 网络相关的静态集合。</summary>
public static class TileIdSets
{
    /// <summary>原版 <c>Main.tileSolid</c> 为 true 的图格 ID（影响斜坡是否存档）。</summary>
    private static readonly FrozenSet<ushort> Solid = new HashSet<ushort>
    {
        0, 1, 2, 6, 7, 8, 9, 10, 19, 22, 23, 25, 30, 37, 38, 39, 40, 41, 43, 44, 45, 46, 47, 48,
        53, 54, 56, 57, 58, 59, 60, 63, 64, 65, 66, 67, 68, 70, 75, 76, 107, 108, 109, 111, 112,
        116, 117, 118, 119, 120, 121, 122, 123, 127, 130, 137, 138, 140, 145, 146, 147, 148, 150,
        151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 166, 167, 168, 169,
        170, 175, 176, 177, 179, 180, 181, 182, 183, 188, 189, 190, 191, 192, 193, 194, 195, 196,
        197, 198, 199, 200, 202, 203, 204, 206, 208, 211, 221, 222, 223, 224, 225, 226, 229, 230,
        232, 234, 235, 239, 248, 249, 250, 251, 252, 253, 272, 273, 274, 284, 311, 312, 313, 315,
        321, 322, 325, 326, 327, 328, 329, 345, 346, 347, 348, 350, 357, 367, 368, 369, 370, 371,
        380, 381, 383, 384, 385, 387, 388, 396, 397, 398, 399, 400, 401, 402, 403, 404, 407, 408,
        409, 415, 416, 417, 418, 421, 422, 426, 427, 430, 431, 432, 433, 434, 446, 447, 448, 458,
        459, 460, 472, 473, 474, 476, 477, 478, 479, 481, 482, 483, 484, 492, 495, 496, 498, 500,
        501, 502, 503, 507, 508, 512, 513, 514, 515, 516, 517, 534, 535, 536, 537, 539, 540, 541,
        546, 557, 562, 563, 566, 618, 625, 626, 627, 628, 633, 635, 641, 659, 661, 662, 664, 666,
        667, 668, 669, 670, 671, 672, 673, 674, 675, 676, 677, 678, 679, 680, 681, 682, 683, 684,
        685, 686, 687, 688, 689, 690, 691, 692, 708, 711, 712, 713, 714, 715, 716, 717, 718, 719,
        722, 726, 734, 735, 736, 737, 738, 739, 740, 741, 742, 743, 744, 745, 746, 747, 748, 749, 750
    }.ToFrozenSet();

    /// <summary>原版 <c>TileID.Sets.NonSolidSaveSlopes</c>。</summary>
    private static readonly FrozenSet<ushort> NonSolidSaveSlopes =
        new HashSet<ushort> { 131, 351, 336, 340, 342, 341, 343, 344 }.ToFrozenSet();

    /// <summary>原版 <c>TileID.Sets.BasicChest</c>。</summary>
    private static readonly FrozenSet<ushort> BasicChest = new HashSet<ushort> { 21, 467 }.ToFrozenSet();

    /// <summary>原版 <c>Main.tileSign</c>。</summary>
    private static readonly FrozenSet<ushort> Sign = new HashSet<ushort> { 55, 85, 425, 573 }.ToFrozenSet();

    /// <summary>
    /// 原版 <c>Main.tileFrameImportant</c> 为 true 的图格 ID（398 项）。
    /// 包 10 编码时对这些图格额外写入 FrameX/FrameY（各 Int16）。
    /// </summary>
    private static readonly FrozenSet<ushort> FrameImportant = new HashSet<ushort>
    {
        3, 4, 5, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 24, 26, 27, 28, 29, 31, 33, 34,
        35, 36, 42, 49, 50, 55, 61, 71, 72, 73, 74, 77, 78, 79, 81, 82, 83, 84, 85, 86, 87, 88,
        89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 110, 113,
        114, 125, 126, 128, 129, 132, 133, 134, 135, 136, 137, 138, 139, 141, 142, 143, 144, 149,
        165, 171, 172, 173, 174, 178, 184, 185, 186, 187, 201, 207, 209, 210, 212, 215, 216, 217,
        218, 219, 220, 227, 228, 231, 233, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245,
        246, 247, 254, 269, 270, 271, 275, 276, 277, 278, 279, 280, 281, 282, 283, 285, 286, 287,
        288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305,
        306, 307, 308, 309, 310, 314, 316, 317, 318, 319, 320, 323, 324, 334, 335, 337, 338, 339,
        349, 354, 355, 356, 358, 359, 360, 361, 362, 363, 364, 372, 373, 374, 375, 376, 377, 378,
        380, 386, 387, 388, 389, 390, 391, 392, 393, 394, 395, 405, 406, 410, 411, 412, 413, 414,
        419, 420, 423, 424, 425, 427, 428, 429, 440, 441, 442, 443, 444, 445, 452, 453, 454, 455,
        456, 457, 461, 462, 463, 464, 465, 466, 467, 468, 469, 470, 471, 475, 476, 480, 484, 485,
        486, 487, 488, 489, 490, 491, 493, 494, 497, 499, 505, 506, 509, 510, 511, 518, 519, 520,
        521, 522, 523, 524, 525, 526, 527, 529, 530, 531, 532, 533, 538, 542, 543, 544, 545, 547,
        548, 549, 550, 551, 552, 553, 554, 555, 556, 558, 559, 560, 564, 565, 567, 568, 569, 570,
        571, 572, 573, 579, 580, 581, 582, 583, 584, 585, 586, 587, 588, 589, 590, 591, 592, 593,
        594, 595, 596, 597, 598, 599, 600, 601, 602, 603, 604, 605, 606, 607, 608, 609, 610, 611,
        612, 613, 614, 615, 616, 617, 619, 620, 621, 622, 623, 624, 629, 630, 631, 632, 634, 637,
        639, 640, 642, 643, 644, 645, 646, 653, 654, 656, 657, 658, 660, 663, 664, 665, 695, 696,
        698, 699, 700, 701, 702, 703, 704, 705, 707, 709, 710, 711, 712, 713, 714, 715, 716, 720,
        721, 723, 724, 725, 726, 733, 751, 752, 753
    }.ToFrozenSet();

    public static bool IsTileSolid(ushort type) => Solid.Contains(type);
    /// <summary>原版 <c>TileID.Sets.SaveSlopes[type]</c>：仅这些图格会存档斜坡 / 半砖。</summary>
    public static bool SaveSlopes(ushort type) => Solid.Contains(type) || NonSolidSaveSlopes.Contains(type);

    /// <summary>原版 <c>TileID.Sets.AllowsSaveCompressionBatching</c>（默认全类型允许）。</summary>
    public static bool AllowsSaveCompressionBatching(ushort type) => true;

    /// <summary>原版 <c>TileID.Sets.BasicChest</c>（包 10 尾部宝箱收集用）。</summary>
    public static bool IsBasicChest(ushort type) => BasicChest.Contains(type);

    /// <summary>原版 <c>Main.tileDungeon</c>（地牢砖，陨石落点与地牢判定用）。</summary>
    public static bool IsDungeonBrick(ushort type) => DungeonBrick.Contains(type);

    /// <summary>原版 <c>Main.tileDungeon</c> 中我们建模的地牢砖集合（与 <c>SceneMetrics.DungeonTileCount</c> 同口径）。</summary>
    private static readonly FrozenSet<ushort> DungeonBrick = new HashSet<ushort> { 41, 43, 44, 481, 482, 483 }.ToFrozenSet();

    /// <summary>原版 <c>Main.tileSign</c>（牌子收集 / 校验用）。</summary>
    public static bool IsSign(ushort type) => Sign.Contains(type);

    /// <summary>原版 <c>Main.tileFrameImportant[type]</c>（包 10 需写 FrameX/FrameY）。</summary>
    public static bool IsTileFrameImportant(ushort type) => FrameImportant.Contains(type);
}
