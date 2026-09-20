// TerraAuth — 图格物件（家具类）几何表：图格 ID → 尺寸 / 锚点偏移 / 样式与贴图坐标参数
// 数据源：Terraria 1.4.5.8 原版 Terraria.ObjectData.TileObjectData.Initialize()
//   （按 addTile(N) / addBaseTile(out StyleNxM) / newTile.CopyFrom(StyleNxM) / addSubTile 的线性顺序逐条核对求值）。
// 生成脚本：仓库外的一次性生成脚本（未纳入版本库）。
//
// 当前条目数：389 项。
//
// 为什么需要：包 79（PlaceObject）只带「点击的那一格」+ 图格 ID + style，而原版放置（Terraria.TileObject.Place）
// 是按图格物件整体写入的：锚点 = 点击格 - Origin，随后把 Width × Height 每格都写上 active/type 与**帧**。
// 只写单格、不写帧（帧停在 Tile.Empty 的 -1,-1）时，客户端本地已按正确帧显示整件家具，服务端世界却只有
// 一格且无帧 —— 真机表现即「放下的工作台看起来碎了 / 没了」，且在相邻那格再放会被 tile_already_exists 拒绝。
//
// 帧算法口径（逐行照抄原版 Terraria.TileObject.Place(TileObject)，第 60-108 行）：
//   num4 = tileData.CalculatePlacementStyle(style, alternate, random)
//   num5 = 0
//   if (StyleWrapLimit > 0) { num5 = num4 / StyleWrapLimit * StyleLineSkip; num4 %= StyleWrapLimit; }
//   if (StyleHorizontal) { baseX = CoordinateFullWidth * num4;  baseY = CoordinateFullHeight * num5; }
//   else                 { baseX = CoordinateFullWidth * num5;  baseY = CoordinateFullHeight * num4; }
//   frameX = baseX + dx * (CoordinateWidth + CoordinatePadding)
//   frameY = baseY + Σ_{l<dy} (CoordinateHeights[l] + CoordinatePadding)
//   本表把 style 直接当作 placement style（即 num4 = style）：原版 CalculatePlacementStyle 还会叠加
//   Style * StyleMultiplier + random，但那些只落在「多风格家具的样式序号」上，不影响物件几何与锚点；
//   故 StyleMultiplier ≠ 1 的少数图格（梳妆台 / 床 / 椅子类，placement style 会 ×2）样式序号可能差一格。
// CoordinateFullWidth / CoordinateFullHeight 取自原版 TileObjectData.Calculate() 的 styleWidth / styleHeight：
//   CoordinateFullWidth  = (CoordinateWidth + CoordinatePadding) * Width + CoordinatePaddingFixX
//   CoordinateFullHeight = Σ (CoordinateHeights[l] + CoordinatePadding) + CoordinatePaddingFixY
//   CoordinatePaddingFix 是 vanilla 对椅子 / 床等样式的整块微调（椅子 frameY 网格 = 40 = (16+2)+(18+2)+2），
//   故随表带出；缺了它 style ≥ StyleWrapLimit 的样式整块偏移会少 2 px。
//
// 锚点（原版 Terraria.TileObject.CanPlace，第 210-211 行）：anchor = 点击格 (x,y) - Origin；
//   Style / Direction / Alternate 只影响帧与放置合法性，不影响锚点公式。
//
// 未收录 = 该图格不是图格物件（泥土 0 / 石头 1 这类非 frame-important 图格本来就不在 TileObjectData 里）
//   → 按普通方块单格放置：只写点击那一格、不写帧（帧保持 0 / 新格仍是 Tile.Empty 的 -1,-1），
//     也不套用本表的锚点 / 尺寸逻辑。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>单个图格物件的几何定义（原版 <c>TileObjectData</c> 中该图格类型的字段快照）。</summary>
public readonly record struct TileObjectInfo(
    int Width,
    int Height,
    int OriginX,
    int OriginY,
    bool StyleHorizontal,
    int StyleWrapLimit,
    int StyleLineSkip,
    int CoordinateWidth,
    int CoordinatePadding,
    int[] CoordinateHeights,
    int CoordinatePaddingFixX,
    int CoordinatePaddingFixY)
{
    /// <summary>一个样式（style）在贴图上占用的整块宽度：原版 <c>TileObjectData.Calculate</c> 的 styleWidth。</summary>
    public int CoordinateFullWidth => (CoordinateWidth + CoordinatePadding) * Width + CoordinatePaddingFixX;

    /// <summary>一个样式在贴图上占用的整块高度：原版 <c>TileObjectData.Calculate</c> 的 styleHeight。</summary>
    public int CoordinateFullHeight
    {
        get
        {
            int height = 0;
            for (int i = 0; i < CoordinateHeights.Length; i++) height += CoordinateHeights[i] + CoordinatePadding;
            return height + CoordinatePaddingFixY;
        }
    }
}

/// <summary>图格物件（家具类）几何表：图格 ID → 尺寸 / 锚点偏移 / 帧参数；未收录即非图格物件。</summary>
public static class TileObjectTable
{
    /// <summary>图格 ID → 物件几何（原版 <c>TileObjectData.Initialize</c> 的 addTile 顺序快照）。</summary>
    public static readonly IReadOnlyDictionary<int, TileObjectInfo> Objects = new Dictionary<int, TileObjectInfo>
    {
        [3] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [4] = new(1, 1, 0, 0, true, 6, 1, 20, 2, new[] { 20 }, 0, 0),  // StyleTorch
        [10] = new(1, 3, 0, 0, false, 36, 3, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // defaults
        [11] = new(2, 3, 0, 0, false, 36, 2, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // defaults
        [12] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [13] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // StyleOnTable1x1
        [14] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [15] = new(1, 2, 0, 1, true, 2, 1, 16, 2, new[] { 16, 18 }, 0, 2),  // Style1x2
        [16] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 18 }, 0, 0),  // Style2x1
        [17] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [18] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 18 }, 0, 0),  // Style2x1
        [19] = new(1, 1, 0, 0, true, 27, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [20] = new(1, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // defaults
        [21] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [24] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [26] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [27] = new(2, 4, 0, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 18 }, 0, 0),  // defaults
        [28] = new(2, 2, 0, 1, true, 3, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [29] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style2x1
        [31] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [33] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // StyleOnTable1x1
        [34] = new(3, 3, 1, 0, false, 37, 2, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [35] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [36] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [42] = new(1, 2, 0, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2Top
        [49] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // StyleOnTable1x1
        [50] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // StyleOnTable1x1
        [55] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [61] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [71] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [73] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height34
        [74] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height34
        [77] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [78] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // StyleOnTable1x1
        [79] = new(4, 2, 1, 1, true, 2, 1, 16, 2, new[] { 16, 18 }, 0, -2),  // Style4x2
        [81] = new(1, 1, 0, 0, true, 0, 1, 24, 2, new[] { 26 }, 0, 0),  // Style1x1
        [82] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // StyleAlch
        [83] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // FullCopyFrom(82)
        [84] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // FullCopyFrom(83)
        [85] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [86] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [87] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [88] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [89] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [90] = new(4, 2, 1, 1, true, 2, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style4x2
        [91] = new(1, 3, 0, 0, true, 111, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style1x2Top
        [92] = new(1, 6, 0, 5, false, 0, 1, 16, 2, new[] { 16, 16, 16, 16, 16, 16 }, 0, 0),  // Style1xX
        [93] = new(1, 3, 0, 2, false, 0, 2, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style1xX
        [94] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [95] = new(2, 2, 1, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [96] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [97] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [98] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [99] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [100] = new(2, 2, 0, 1, false, 0, 2, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [101] = new(3, 4, 1, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style3x4
        [102] = new(3, 4, 1, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style3x4
        [103] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style2x1
        [104] = new(2, 5, 0, 4, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16, 16 }, 0, 0),  // Style2xX
        [105] = new(2, 3, 1, 2, true, 55, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [106] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [110] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [113] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height34
        [114] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [125] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [126] = new(2, 2, 1, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [128] = new(2, 3, 0, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [132] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [133] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [134] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style2x1
        [135] = new(1, 1, 0, 0, false, 0, 1, 16, 0, new[] { 18 }, 0, 0),  // Style1x1
        [136] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // StyleSwitch
        [138] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [139] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [141] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [142] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [143] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [144] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [149] = new(1, 1, 0, 0, true, 6, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [165] = new(1, 2, 0, 0, true, 39, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2
        [171] = new(4, 8, 1, 7, false, 0, 1, 16, 0, new[] { 16, 16, 16, 16, 16, 16, 16, 16 }, 0, 0),  // defaults
        [172] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [173] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [174] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // StyleOnTable1x1
        [178] = new(1, 1, 0, 0, true, 7, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [184] = new(1, 1, 0, 0, true, 11, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [185] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [186] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [187] = new(3, 2, 1, 1, true, 35, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [201] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [207] = new(2, 4, 1, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style2xX
        [209] = new(4, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // defaults
        [210] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [212] = new(3, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [215] = new(3, 2, 1, 1, true, 16, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [216] = new(1, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 20 }, 0, 0),  // Style1x2
        [217] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [218] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [219] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [220] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [227] = new(1, 1, 0, 0, true, 0, 1, 32, 2, new[] { 38 }, 0, 0),  // StyleDye
        [228] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [231] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [233] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [235] = new(3, 1, 1, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [236] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [237] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [238] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [239] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [240] = new(3, 3, 1, 1, true, 36, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3Wall
        [241] = new(4, 3, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3Wall
        [242] = new(6, 4, 2, 2, false, 27, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style3x3Wall
        [243] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [244] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [245] = new(2, 3, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3Wall
        [246] = new(3, 2, 1, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x3Wall
        [247] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [254] = new(2, 2, 0, 1, false, 6, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [269] = new(2, 3, 0, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [270] = new(1, 2, 0, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2Top
        [271] = new(1, 2, 0, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2Top
        [275] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [276] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [277] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [278] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [279] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [280] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [281] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [282] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [283] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [285] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [286] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [287] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [288] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [289] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [290] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [291] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [292] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [293] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [294] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [295] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [296] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [297] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [298] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [299] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [300] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [301] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [302] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [303] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [304] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [305] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [306] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [307] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [308] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [309] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [310] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [316] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [317] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [318] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [319] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [320] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [324] = new(1, 1, 0, 0, true, 3, 1, 20, 2, new[] { 20 }, 0, 0),  // Style1x1
        [334] = new(3, 3, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3Wall
        [335] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [337] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [338] = new(1, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2
        [339] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [349] = new(2, 3, 1, 2, true, 7, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [354] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [355] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [356] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [358] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [359] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [360] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [361] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [362] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [363] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [364] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [372] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // StyleOnTable1x1
        [373] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Drip
        [374] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Drip
        [375] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Drip
        [376] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [377] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [378] = new(2, 3, 1, 2, true, 2, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [386] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [387] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style2x1
        [388] = new(1, 5, 0, 0, false, 2, 1, 16, 2, new[] { 18, 16, 16, 16, 18 }, 0, 0),  // defaults
        [389] = new(1, 5, 0, 0, false, 2, 1, 16, 2, new[] { 18, 16, 16, 16, 18 }, 0, 0),  // FullCopyFrom(388)
        [390] = new(1, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2
        [391] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [392] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [393] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [394] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [395] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [405] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [406] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [410] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 18 }, 0, 0),  // Style2xX
        [411] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [412] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [413] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [414] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [419] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [420] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [423] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [424] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [425] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [427] = new(1, 1, 0, 0, true, 27, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [428] = new(1, 1, 0, 0, false, 0, 1, 16, 0, new[] { 18 }, 0, 0),  // Style1x1
        [429] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [435] = new(1, 1, 0, 0, true, 27, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [436] = new(1, 1, 0, 0, true, 27, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [437] = new(1, 1, 0, 0, true, 27, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [438] = new(1, 1, 0, 0, true, 27, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [439] = new(1, 1, 0, 0, true, 27, 1, 16, 2, new[] { 16 }, 0, 0),  // defaults
        [440] = new(3, 3, 1, 1, true, 36, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3Wall
        [441] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [442] = new(1, 1, 0, 0, true, 4, 1, 20, 2, new[] { 20 }, 0, 0),  // defaults
        [443] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style2x1
        [444] = new(2, 2, 1, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [445] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [452] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [453] = new(1, 3, 0, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style1xX
        [454] = new(4, 3, 2, 0, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [455] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [456] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [457] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [461] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Drip
        [462] = new(2, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style2x1
        [463] = new(3, 4, 1, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style3x4
        [464] = new(5, 4, 2, 3, false, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style5x4
        [465] = new(2, 3, 0, 0, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [466] = new(5, 4, 2, 3, false, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style5x4
        [467] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [468] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [469] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [470] = new(2, 3, 0, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [471] = new(3, 3, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3Wall
        [475] = new(3, 4, 1, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style3x4
        [476] = new(1, 1, 0, 0, false, 0, 1, 20, 2, new[] { 18 }, 0, 0),  // Style1x1
        [480] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [484] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [485] = new(2, 2, 0, 1, true, 4, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [486] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [487] = new(4, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // defaults
        [488] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [489] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [490] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [491] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [493] = new(1, 2, 0, 1, true, 6, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2
        [494] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // StyleOnTable1x1
        [497] = new(1, 2, 0, 1, true, 2, 1, 16, 2, new[] { 16, 18 }, 0, 2),  // Style1x2
        [499] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [505] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [506] = new(2, 3, 0, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [509] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [510] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [511] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [520] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // StyleOnTable1x1
        [521] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [522] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [523] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [524] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [525] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [526] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [527] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [530] = new(3, 2, 1, 1, true, 9, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [531] = new(2, 3, 0, 0, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [532] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [533] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [538] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [542] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [543] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [544] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [545] = new(2, 3, 0, 2, true, 2, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [547] = new(2, 5, 1, 4, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16, 16 }, 0, 0),  // Style2xX
        [548] = new(3, 6, 1, 5, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16, 16, 16 }, 0, 0),  // Style3x3
        [550] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [551] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [552] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [553] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [554] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [555] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [556] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [558] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [559] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [560] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [564] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [565] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [567] = new(1, 2, 0, 1, true, 0, 1, 26, 2, new[] { 18, 18 }, 0, 0),  // Style1x2
        [568] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [569] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [570] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [572] = new(1, 2, 0, 0, false, 6, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2Top
        [573] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [579] = new(1, 1, 0, 0, true, 0, 1, 20, 2, new[] { 20 }, 0, 0),  // StyleDye
        [580] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [581] = new(1, 2, 0, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2Top
        [582] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [590] = new(1, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // defaults
        [591] = new(2, 3, 0, 0, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [592] = new(2, 3, 0, 0, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [593] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [594] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [595] = new(1, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // defaults
        [597] = new(3, 4, 1, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style3x4
        [598] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [599] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [600] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [601] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [602] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [603] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [604] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [605] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [606] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [607] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [608] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [609] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [610] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [611] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [612] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [613] = new(3, 5, 1, 4, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16, 16 }, 0, 0),  // Style3x3
        [614] = new(3, 6, 1, 5, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16, 16, 16 }, 0, 0),  // Style3x3
        [615] = new(1, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // defaults
        [617] = new(3, 4, 1, 3, false, 2, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style3x4
        [619] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [620] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [621] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [622] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [623] = new(2, 5, 1, 4, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16, 16 }, 0, 0),  // Style2xX
        [624] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [629] = new(3, 2, 1, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // StyleSmallCage
        [630] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [631] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [632] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [637] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [639] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [640] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [642] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [643] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [644] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [645] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [646] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 20 }, 0, 0),  // StyleOnTable1x1
        [647] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [648] = new(3, 2, 1, 1, true, 35, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [649] = new(2, 1, 0, 0, true, 53, 1, 16, 2, new[] { 16 }, 0, 0),  // Style2x1
        [650] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [651] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [652] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [653] = new(2, 2, 0, 1, true, 3, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [654] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [656] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [657] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [658] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [660] = new(1, 2, 0, 0, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2Top
        [663] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [664] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [665] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [693] = new(1, 1, 0, 0, true, 39, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [694] = new(1, 2, 0, 0, true, 39, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2
        [695] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style3x2
        [696] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [698] = new(1, 2, 0, 0, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style1x2Top
        [699] = new(4, 4, 1, 3, true, 0, 1, 16, 2, new[] { 16, 16, 16, 16 }, 0, 0),  // Style4x4
        [700] = new(1, 1, 0, 0, false, 0, 1, 20, 2, new[] { 16 }, 0, 0),  // Style1x1
        [701] = new(1, 1, 0, 0, false, 0, 1, 24, 2, new[] { 34 }, 0, 0),  // Style1x1
        [702] = new(2, 2, 0, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [703] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Plant_Height22
        [704] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [705] = new(3, 2, 1, 1, true, 9, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [706] = new(3, 2, 1, 1, true, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style3x2
        [707] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // StyleOnTable1x1
        [709] = new(1, 1, 0, 0, false, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1Drip
        [710] = new(6, 3, 3, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style6x3
        [711] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [712] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 18 }, 0, 0),  // Style2x2
        [713] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [714] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [715] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [716] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [720] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [721] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [723] = new(1, 1, 0, 0, true, 0, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [724] = new(1, 1, 0, 0, true, 5, 1, 16, 2, new[] { 16 }, 0, 0),  // Style1x1
        [725] = new(2, 3, 1, 2, true, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style2xX
        [733] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
        [751] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [752] = new(2, 2, 0, 1, false, 0, 1, 16, 2, new[] { 16, 16 }, 0, 0),  // Style2x2
        [753] = new(3, 3, 1, 2, false, 0, 1, 16, 2, new[] { 16, 16, 16 }, 0, 0),  // Style3x3
    };

    /// <summary>表内条目数（与文件头注释一致，供自检测试）。</summary>
    public static int EntryCount => Objects.Count;

    /// <summary>取该图格的物件几何；未收录 → false（= 不是图格物件，按普通方块单格处理）。</summary>
    public static bool TryGet(int tileType, out TileObjectInfo info) => Objects.TryGetValue(tileType, out info);

    /// <summary>
    /// 按原版 <c>Terraria.TileObject.Place</c> 的公式，把「相对锚点的 (dx,dy)」换算成 (frameX, frameY)。
    /// <paramref name="dx"/>/<paramref name="dy"/> 必须落在 footprint 内，越界或非物件类型返回 false。
    /// style 直接作为 placement style 使用（口径见文件头）。
    /// </summary>
    public static bool TryGetFrame(int tileType, int style, int dx, int dy, out short frameX, out short frameY)
    {
        frameX = 0;
        frameY = 0;
        if (!Objects.TryGetValue(tileType, out var info)) return false;
        if (style < 0 || dx < 0 || dx >= info.Width || dy < 0 || dy >= info.Height) return false;

        int num4 = style;
        int num5 = 0;
        if (info.StyleWrapLimit > 0)
        {
            num5 = num4 / info.StyleWrapLimit * info.StyleLineSkip;
            num4 %= info.StyleWrapLimit;
        }

        int baseX;
        int baseY;
        if (info.StyleHorizontal)
        {
            baseX = info.CoordinateFullWidth * num4;
            baseY = info.CoordinateFullHeight * num5;
        }
        else
        {
            baseX = info.CoordinateFullWidth * num5;
            baseY = info.CoordinateFullHeight * num4;
        }

        int y = baseY;
        for (int l = 0; l < dy; l++) y += info.CoordinateHeights[l] + info.CoordinatePadding;

        frameX = (short)(baseX + dx * (info.CoordinateWidth + info.CoordinatePadding));
        frameY = (short)y;
        return true;
    }
}
