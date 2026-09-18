// TerraAuth — Phase 6: 世界数据模型（Tile / TileMap）
// 字段布局依据：原版客户端兼容的图格字段布局（Terraria 1.4.5.8 / Protocol 326）
//   - 读写：世界文件的图格段与包 10（TileSection）的压缩块

using System;
using System.Buffers.Binary;
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

    /// <summary>序列化字节数（定长，供世界改动持久化使用）。</summary>
    public const int SerializedSize = 15;

    /// <summary>把图格序列化为定长字节（字段顺序与 <see cref="Deserialize"/> 严格对应）。</summary>
    public static byte[] Serialize(in Tile t)
    {
        var b = new byte[SerializedSize];
        b[0] = (byte)((t.Active ? 1 : 0) | (t.HalfBrick ? 2 : 0) | (t.Wire ? 4 : 0) | (t.Wire2 ? 8 : 0)
                    | (t.Wire3 ? 16 : 0) | (t.Wire4 ? 32 : 0) | (t.Actuator ? 64 : 0) | (t.InActive ? 128 : 0));
        b[1] = (byte)((t.InvisibleBlock ? 1 : 0) | (t.InvisibleWall ? 2 : 0)
                    | (t.FullbrightBlock ? 4 : 0) | (t.FullbrightWall ? 8 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), t.Type);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), t.Wall);
        b[6] = t.Liquid;
        b[7] = t.LiquidType;
        b[8] = t.Slope;
        b[9] = t.TileColor;
        b[10] = t.WallColor;
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(11), t.FrameX);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(13), t.FrameY);
        return b;
    }

    /// <summary>从定长字节还原图格；长度不足返回 <see cref="Empty"/>。</summary>
    public static Tile Deserialize(ReadOnlySpan<byte> b)
    {
        if (b.Length < SerializedSize) return Empty;

        var t = Empty;
        byte f1 = b[0];
        t.Active = (f1 & 1) != 0;
        t.HalfBrick = (f1 & 2) != 0;
        t.Wire = (f1 & 4) != 0;
        t.Wire2 = (f1 & 8) != 0;
        t.Wire3 = (f1 & 16) != 0;
        t.Wire4 = (f1 & 32) != 0;
        t.Actuator = (f1 & 64) != 0;
        t.InActive = (f1 & 128) != 0;

        byte f2 = b[1];
        t.InvisibleBlock = (f2 & 1) != 0;
        t.InvisibleWall = (f2 & 2) != 0;
        t.FullbrightBlock = (f2 & 4) != 0;
        t.FullbrightWall = (f2 & 8) != 0;

        t.Type = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        t.Wall = BinaryPrimitives.ReadUInt16LittleEndian(b[4..]);
        t.Liquid = b[6];
        t.LiquidType = b[7];
        t.Slope = b[8];
        t.TileColor = b[9];
        t.WallColor = b[10];
        t.FrameX = BinaryPrimitives.ReadInt16LittleEndian(b[11..]);
        t.FrameY = BinaryPrimitives.ReadInt16LittleEndian(b[13..]);
        return t;
    }

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
/// 布局依据：经兼容性测试的图格压缩字段（1.4.5.8 / Protocol 326）。
/// 编码由 <c>PacketEncoder</c> 完成。
/// </summary>
public sealed record TileSectionPacket(WorldState World, int XStart, int YStart, int Width, int Height) : INetworkPacket
{
    public PacketId Type => PacketId.TileSendSection;
}

/// <summary>
/// 图格方阵变更（TileSquare，包 20）。
/// 用于**服务端驱动的图格改动**（执行器翻转、液体混合等）的小矩形下发 —— 原版对「少量图格改动」走此包，
/// 只有区块级地形下载才走包 10（TileSection）。
/// 线格式：Int16 X + Int16 Y + Byte 宽 + Byte 高 + Byte 变更类型 + 逐格位标志与可选段（未压缩）。
/// </summary>
public sealed record TileSquarePacket(
    WorldState World, int X, int Y, int Width, int Height, byte ChangeType = 0) : INetworkPacket
{
    public PacketId Type => PacketId.TileSquare;
}
