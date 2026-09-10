// TerraAuth — Phase 6: 世界数据模型（Tile / TileMap）
// 字段布局权威来源：本地客户端原版 Terraria.Tile（1.4.5.8 / Protocol 326）
//   - 读写：WorldFile.LoadWorldTiles / NetMessage.CompressTileBlock_Inner

using System;
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>
/// 单个图格。字段对应原版 <c>Terraria.Tile</c> 中被存档 / 网络同步的部分。
/// 默认值（零初始化）等价于"空图格"；frameX/frameY 使用 <see cref="Empty"/> 置 -1。
/// </summary>
public struct Tile
{
    public bool Active;
    public ushort Type;
    public ushort Wall;

    /// <summary>液体量 0..255。</summary>
    public byte Liquid;

    /// <summary>液体类型：0=水、1=岩浆、2=蜂蜜、3=微光。</summary>
    public byte LiquidType;

    /// <summary>斜坡 0=无、1..3（原版 slope 值）。</summary>
    public byte Slope;
    public bool HalfBrick;

    public bool Wire;
    public bool Wire2;
    public bool Wire3;
    public bool Wire4;

    public bool Actuator;
    public bool InActive;

    public bool InvisibleBlock;
    public bool InvisibleWall;
    public bool FullbrightBlock;
    public bool FullbrightWall;

    /// <summary>方块油漆颜色（0 = 无）。</summary>
    public byte TileColor;

    /// <summary>墙壁油漆颜色（0 = 无）。</summary>
    public byte WallColor;

    public short FrameX;
    public short FrameY;

    /// <summary>空图格：frameX/frameY = -1（与原版非重要图格一致）。</summary>
    public static readonly Tile Empty = new() { FrameX = -1, FrameY = -1 };

    public void CopyFrom(in Tile other) => this = other;
}

/// <summary>
/// 图格矩阵。索引方式与原版一致：<c>tiles[x, y]</c>，内部按列优先（x * Height + y）扁平存储。
/// </summary>
public sealed class TileMap
{
    private readonly Tile[] _tiles;

    public int Width { get; }
    public int Height { get; }

    public TileMap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        _tiles = new Tile[checked(width * height)];
        Array.Fill(_tiles, Tile.Empty);
    }

    /// <summary>按坐标访问图格（越界不检查，调用方保证在范围内）。</summary>
    public ref Tile this[int x, int y] => ref _tiles[x * Height + y];

    /// <summary>区块坐标：原版 <c>GetSectionX</c> = x / 200。</summary>
    public static int GetSectionX(int x) => x / 200;

    /// <summary>区块坐标：原版 <c>GetSectionY</c> = y / 150。</summary>
    public static int GetSectionY(int y) => y / 150;
}

/// <summary>
/// 区块图格数据（TileSection，包 10）。
/// payload 为 Deflate 压缩的矩形区块：Int32 xStart/yStart + Int16 width/height + 逐格位标志/RLE + 尾部宝箱/牌子/实体列表。
/// 布局权威来源：<c>NetMessage.CompressTileBlock / CompressTileBlock_Inner</c>（1.4.5.8 / Protocol 326）。
/// 编码由 <c>PacketEncoder</c> 完成。
/// </summary>
public sealed record TileSectionPacket(WorldState World, int XStart, int YStart, int Width, int Height) : INetworkPacket
{
    public PacketId Type => PacketId.TileSendSection;
}
