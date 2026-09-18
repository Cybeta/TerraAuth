using System;
using System.IO;

namespace TerraAuth.Simulation;

/// <summary>WorldFile section 5 的图格实体。</summary>
public sealed class TileEntity
{
    public int Id;
    public int FileId = -1;
    public byte Type;
    public short X;
    public short Y;
    public int NpcSlot;
    public TileEntityItem Item;
    public byte LogicCheck;
    public bool On;
    public TileEntityItem[] DisplayDollItems = new TileEntityItem[9];
    public TileEntityItem[] DisplayDollDyes = new TileEntityItem[9];
    public TileEntityItem DisplayDollMisc;
    public byte DisplayDollPose;
    public TileEntityItem[] HatRackHats = new TileEntityItem[2];
    public TileEntityItem[] HatRackDyes = new TileEntityItem[2];

    public bool IsDisplayDoll => Type == 3;
    public bool IsHatRack => Type == 5;

    public byte[] SerializeFilePayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Type);
        writer.Write(FileId);
        writer.Write(X);
        writer.Write(Y);
        switch (Type)
        {
            case 0: writer.Write((short)NpcSlot); break;
            case 1 or 4 or 6 or 8: WriteItem(writer, Item); break;
            case 2: writer.Write(LogicCheck); writer.Write(On); break;
            case 3: WriteDisplayDoll(writer, this); break;
            case 5: WriteHatRack(writer, this); break;
            case 9 or 10: writer.Write(Item.Type); break;
            case 7: break;
            default: throw new InvalidDataException($"不支持的图格实体类型：{Type}");
        }
        return stream.ToArray();
    }

    public static TileEntity DeserializeFilePayload(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        var entity = new TileEntity { Type = reader.ReadByte(), FileId = reader.ReadInt32(), X = reader.ReadInt16(), Y = reader.ReadInt16() };
        switch (entity.Type)
        {
            case 0: entity.NpcSlot = reader.ReadInt16(); break;
            case 1 or 4 or 6 or 8: entity.Item = ReadItem(reader); break;
            case 2: entity.LogicCheck = reader.ReadByte(); entity.On = reader.ReadBoolean(); break;
            case 3: ReadDisplayDoll(reader, entity); break;
            case 5: ReadHatRack(reader, entity); break;
            case 9 or 10: entity.Item.Type = reader.ReadInt16(); break;
            case 7: break;
            default: throw new InvalidDataException($"不支持的图格实体类型：{entity.Type}");
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("图格实体负载包含多余数据");
        return entity;
    }

    private static void WriteItem(BinaryWriter writer, TileEntityItem item) { writer.Write(item.Type); writer.Write(item.Prefix); writer.Write(item.Stack); }
    private static TileEntityItem ReadItem(BinaryReader reader) => new() { Type = reader.ReadInt16(), Prefix = reader.ReadByte(), Stack = reader.ReadInt16() };
    private static void WriteDisplayDoll(BinaryWriter writer, TileEntity entity)
    {
        byte items = 0, dyes = 0, extra = 0;
        for (int i = 0; i < 8; i++) { if (!entity.DisplayDollItems[i].IsAir) items |= (byte)(1 << i); if (!entity.DisplayDollDyes[i].IsAir) dyes |= (byte)(1 << i); }
        if (!entity.DisplayDollItems[8].IsAir) extra |= 2;
        if (!entity.DisplayDollDyes[8].IsAir) extra |= 4;
        if (!entity.DisplayDollMisc.IsAir) extra |= 1;
        writer.Write(items); writer.Write(dyes); writer.Write(entity.DisplayDollPose); writer.Write(extra);
        for (int i = 0; i < 9; i++) if (i < 8 ? (items & (1 << i)) != 0 : (extra & 2) != 0) WriteItem(writer, entity.DisplayDollItems[i]);
        for (int i = 0; i < 9; i++) if (i < 8 ? (dyes & (1 << i)) != 0 : (extra & 4) != 0) WriteItem(writer, entity.DisplayDollDyes[i]);
        if ((extra & 1) != 0) WriteItem(writer, entity.DisplayDollMisc);
    }
    private static void ReadDisplayDoll(BinaryReader reader, TileEntity entity)
    {
        byte items = reader.ReadByte(), dyes = reader.ReadByte(); entity.DisplayDollPose = reader.ReadByte(); byte extra = reader.ReadByte();
        for (int i = 0; i < 9; i++) if (i < 8 ? (items & (1 << i)) != 0 : (extra & 2) != 0) entity.DisplayDollItems[i] = ReadItem(reader);
        for (int i = 0; i < 9; i++) if (i < 8 ? (dyes & (1 << i)) != 0 : (extra & 4) != 0) entity.DisplayDollDyes[i] = ReadItem(reader);
        if ((extra & 1) != 0) entity.DisplayDollMisc = ReadItem(reader);
    }
    private static void WriteHatRack(BinaryWriter writer, TileEntity entity)
    {
        byte bits = 0;
        for (int i = 0; i < 2; i++) { if (!entity.HatRackHats[i].IsAir) bits |= (byte)(1 << i); if (!entity.HatRackDyes[i].IsAir) bits |= (byte)(1 << (i + 2)); }
        writer.Write(bits);
        for (int i = 0; i < 2; i++) if ((bits & (1 << i)) != 0) WriteItem(writer, entity.HatRackHats[i]);
        for (int i = 0; i < 2; i++) if ((bits & (1 << (i + 2))) != 0) WriteItem(writer, entity.HatRackDyes[i]);
    }
    private static void ReadHatRack(BinaryReader reader, TileEntity entity)
    {
        byte bits = reader.ReadByte();
        for (int i = 0; i < 2; i++) if ((bits & (1 << i)) != 0) entity.HatRackHats[i] = ReadItem(reader);
        for (int i = 0; i < 2; i++) if ((bits & (1 << (i + 2))) != 0) entity.HatRackDyes[i] = ReadItem(reader);
    }
}

/// <summary>图格实体物品负载：原版顺序为 Int16 Type、Byte Prefix、Int16 Stack。</summary>
public struct TileEntityItem
{
    public short Type;
    public byte Prefix;
    public short Stack;

    public bool IsAir => Type == 0 || Stack == 0;
}
