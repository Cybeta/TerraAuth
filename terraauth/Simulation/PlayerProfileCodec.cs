// SSC 玩家档案编解码：把服务端权威的背包（59 槽 itemId + 前缀）与生命 / 法力编成存档字节串，
// 供 Players 表（PlayerData.InventoryBlob）落盘与回读。
//
// 为什么需要：SSC 模式下客户端背包由服务端下发（WorldInfo 的 SSC 位置位，客户端本地背包不参与），
// 是否持久化完全由服务端负责；不落盘就会出现「每次重进背包里的物品全部消失」（真机反馈）。
// 原版把玩家档案写 .plr，这里按同一语义存进 DB。
//
// 布局（小端，版本化，长度随槽位数固定）：
//   Byte Version=2 | Int32 Hp | Int32 HpMax | Int32 Mp | Int32 MpMax
//   | N × (Int16 ItemId, Int32 Stack, Byte Prefix)      // N = PlayerRuntime.InventorySlotCount

using System;
using System.IO;

namespace TerraAuth.Simulation;

/// <summary>SSC 玩家档案（背包 + 生命 / 法力）的编解码。</summary>
public static class PlayerProfileCodec
{
    private const byte Version = 2;
    private const byte LegacyVersion = 1;

    /// <summary>编码为存档字节串（槽位数与 <see cref="PlayerRuntime.InventorySlotCount"/> 绑定）。</summary>
    public static byte[] Encode(PlayerRuntime player)
    {
        using var ms = new MemoryStream(1 + 16 + PlayerRuntime.InventorySlotCount * 7);
        using var w = new BinaryWriter(ms);
        w.Write(Version);
        w.Write(player.Hp);
        w.Write(player.HpMax);
        w.Write(player.Mp);
        w.Write(player.MpMax);
        for (int i = 0; i < PlayerRuntime.InventorySlotCount; i++)
        {
            w.Write((short)player.Items[i]);
            w.Write(player.ItemStacks[i]);
            w.Write(player.ItemPrefixes[i]);
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// 把档案回填到 <paramref name="player"/>（背包 + 生命 / 法力 + 防御重算）。
    /// 存档为空 / 版本不符 / 截断时返回 false，调用方保留出生默认值。
    /// 存档里生命 ≤ 0（断线时已死亡）→ 按满血复活处理，避免重进即处于死亡态。
    /// </summary>
    public static bool TryApply(byte[]? blob, PlayerRuntime player)
    {
        if (blob is null || blob.Length < 1 + 16) return false;

        try
        {
            using var ms = new MemoryStream(blob, writable: false);
            using var r = new BinaryReader(ms);
            byte version = r.ReadByte();
            if (version != Version && version != LegacyVersion) return false;

            int hp = r.ReadInt32();
            int hpMax = r.ReadInt32();
            int mp = r.ReadInt32();
            int mpMax = r.ReadInt32();

            for (int i = 0; i < PlayerRuntime.InventorySlotCount; i++)
            {
                int itemId = r.ReadInt16();
                int stack = version == Version ? r.ReadInt32() : (itemId == 0 ? 0 : 1);
                byte prefix = r.ReadByte();
                player.Items[i] = stack > 0 ? itemId : 0;
                player.ItemStacks[i] = stack > 0 ? stack : 0;
                player.ItemPrefixes[i] = stack > 0 ? prefix : (byte)0;
            }

            player.HpMax = Math.Max(1, hpMax);
            player.Hp = hp <= 0 ? player.HpMax : Math.Min(hp, player.HpMax);
            player.MpMax = Math.Max(0, mpMax);
            player.Mp = Math.Clamp(mp, 0, player.MpMax);
            player.RecalculateDefense();   // 装备还原后重算防御（与包 5 路径同口径）
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;   // 截断的存档 → 视为无档案
        }
    }
}
