// TerraAuth — Phase 5: 数据包编码器
// ISnapshotSender 输出 → 字节流
// 布局权威来源：原版客户端发包实现的字段顺序（Terraria 1.4.5.8 / Protocol 326）

using System.Buffers;
using System.IO;
using System.IO.Compression;
using TerraAuth.Protocol;    // PacketId, INetworkPacket, ProtocolVersion, Vector2
using TerraAuth.Simulation; // SnapshotFrame, TileSectionPacket

namespace TerraAuth.Net.Phase5;

/// <summary>
/// 编码器：将出站对象序列化为帧字节。
/// 核心职责：
///   1. SnapshotFrame → 二进制帧（供 ISnapshotSender 下发）
///   2. 通用 INetworkPacket → 二进制帧（供握手/控制消息）
/// </summary>
public interface IPacketEncoder
{
    /// <summary>编码一条快照为可写缓冲。</summary>
    void EncodeSnapshot(IBufferWriter<byte> writer, SnapshotFrame frame);

    /// <summary>编码任意出站包。</summary>
    void Encode(IBufferWriter<byte> writer, PacketId type, INetworkPacket packet);
}

public sealed class PacketEncoder : IPacketEncoder
{
    private readonly ProtocolVersion _version;

    public PacketEncoder(ProtocolVersion version)
    {
        _version = version;
    }

    // ---------- 快照编码（Phase 4 → bytes） ----------

    public void EncodeSnapshot(IBufferWriter<byte> writer, SnapshotFrame frame)
    {
        // 帧类型 = Snapshot（TerraAuth 专用；原版 case 15 为 no-op，客户端会忽略）
        // 布局：BaseTick(UInt32) + Tick(UInt32) + Checksum(UInt32)
        //       + EntityCount(UInt16) + [Id(Int32) + PosX/PosY/VelX/VelY(Float32) + State(Byte)] × N
        //       + RemovedCount(UInt16) + [Id(Int32) + Reason(Byte)] × M
        // 字段顺序与 SnapshotFrame.ComputeChecksum 的规范字节流一致（实体 21B / 移除项 5B，小端）。
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(frame.BaseTick);          // 增量基址（0 = 全量，客户端应以此重建）
            bw.Write(frame.Tick);              // 本帧 tick
            bw.Write(frame.Checksum);
            bw.Write((ushort)frame.Entities.Count);

            foreach (var e in frame.Entities)
            {
                bw.Write(e.Id);
                bw.Write(e.Position.X);
                bw.Write(e.Position.Y);
                bw.Write(e.Velocity.X);
                bw.Write(e.Velocity.Y);
                bw.Write((byte)e.State);
            }

            bw.Write((ushort)frame.Removed.Count);

            foreach (var r in frame.Removed)
            {
                bw.Write(r.Id);
                bw.Write((byte)r.Reason);
            }
        }

        var bytes = ms.ToArray();
        Framing.WriteFrame(writer, PacketId.Snapshot, bytes);
    }

    // ---------- 通用包编码 ----------

    public void Encode(IBufferWriter<byte> writer, PacketId type, INetworkPacket packet)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            switch (packet)
            {
                case ContinueConnectingPacket cont:
                    // PlayerInfo（包 3）：Byte PlayerId + Boolean ServerSpecialFlag
                    bw.Write(cont.PlayerId);
                    bw.Write(cont.ServerSpecialFlag);
                    break;

                case WorldInfoPacket world:
                    WriteWorldInfo(bw, world);
                    break;

                case StatusTextPacket status:
                    // StatusTextSize（包 9）：Int32 StatusMax + NetworkText + BitsByte
                    bw.Write(status.StatusMax);
                    WriteNetworkText(bw, status.StatusText);
                    bw.Write(status.Flags);
                    break;

                case SpawnTileDataPacket spawn:
                    // SpawnTileData（包 8）：Int32 SpawnX + Int32 SpawnY + Byte Team
                    bw.Write(spawn.SpawnX);
                    bw.Write(spawn.SpawnY);
                    bw.Write(spawn.Team);
                    break;

                case TileSectionPacket section:
                    // TileSection（包 10）：Deflate 压缩区块，直接写入 ms（见 WriteTileSection）
                    WriteTileSection(ms, section);
                    break;

                case TileSquarePacket square:
                    // TileSquare（包 20）：未压缩的小矩形图格变更（服务端驱动的图格改动）
                    WriteTileSquare(bw, square);
                    break;

                case PlayerSpawnPacket playerSpawn:
                    WritePlayerSpawn(bw, playerSpawn);
                    break;

                case InitialSpawnPacket:
                case FinishedConnectingPacket:
                    // 无 payload
                    break;

                case PlayerInfoPacket info:
                    WritePlayerInfo(bw, info);
                    break;

                case InventorySlotPacket slot:
                    WriteInventorySlot(bw, slot);
                    break;

                case PlayerControlsPacket controls:
                    WritePlayerControls(bw, controls);
                    break;

                case PlayerActivePacket active:
                    // PlayerActive（包 14）：Byte PlayerId + Byte Active
                    bw.Write(active.PlayerId);
                    bw.Write((byte)(active.Active ? 1 : 0));
                    break;

                case PlayerHealthPacket health:
                    // PlayerLifeMana（包 16）：Byte PlayerId + Int16 statLife + Int16 statLifeMax
                    bw.Write((byte)health.PlayerId);
                    bw.Write((short)health.Hp);
                    bw.Write((short)health.MaxHp);
                    break;

                case PlayerManaPacket mana:
                    // PlayerMana（包 42）：Byte PlayerId + Int16 statMana + Int16 statManaMax
                    bw.Write((byte)mana.PlayerId);
                    bw.Write((short)mana.Mana);
                    bw.Write((short)mana.MaxMana);
                    break;

                case TileBreakPacket tileBreak:
                    // TileManipulation（包 17）：Byte Action + Int16 X + Int16 Y + Int16 TileType + Byte Style
                    bw.Write(tileBreak.Action);
                    bw.Write((short)tileBreak.X);
                    bw.Write((short)tileBreak.Y);
                    bw.Write((short)tileBreak.TileType);
                    bw.Write(tileBreak.Style);
                    break;

                case TilePlacePacket place:
                    // PlaceObject（包 79）：Int16 X + Int16 Y + Int16 TileType + Int16 Style
                    //   + Byte Alternate + SByte Random + Boolean Direction（1 / -1）
                    bw.Write((short)place.X);
                    bw.Write((short)place.Y);
                    bw.Write((short)place.TileType);
                    bw.Write((short)place.Style);
                    bw.Write(place.Alternate);
                    bw.Write(place.Random);
                    bw.Write(place.Direction == 1);
                    break;

                case NpcStrikePacket npcStrike:
                    // DamageNPC（包 28）：Byte NpcId + Byte Generation + Int16 Damage
                    //   + Single Knockback + Byte Direction(+1) + Byte Crit
                    bw.Write((byte)npcStrike.NpcId);
                    bw.Write((byte)npcStrike.Generation);
                    bw.Write((short)npcStrike.Damage);
                    bw.Write(npcStrike.Knockback);
                    bw.Write((byte)(npcStrike.Direction + 1));
                    bw.Write((byte)(npcStrike.Crit ? 1 : 0));
                    break;

                case ProjectileNewPacket proj:
                    WriteProjectileNew(bw, proj);
                    break;

                case ChestPacket chest:
                    // RequestChestOpen（包 31）：Int16 X + Int16 Y
                    bw.Write((short)chest.X);
                    bw.Write((short)chest.Y);
                    break;

                case ItemDropPacket item:
                    WriteSyncItem(bw, item);
                    break;

                case ItemPickupPacket pickup:
                    // SyncItemOwner（包 22）：Int16 世界物品槽位 + Byte 归属玩家
                    bw.Write((short)pickup.ItemSlotIndex);
                    bw.Write((byte)pickup.PlayerId);
                    break;

                case SyncChestItemPacket chestItem:
                    // SyncChestItem（包 32）：Int16 ChestIndex + Byte Slot + Int16 Stack + Byte Prefix + Int16 Type
                    bw.Write((short)chestItem.ChestIndex);
                    bw.Write((byte)chestItem.ItemSlot);
                    bw.Write((short)chestItem.Stack);
                    bw.Write(chestItem.Prefix);
                    bw.Write((short)chestItem.ItemType);
                    break;

                case PlayerChestIndexPacket chestIndex:
                    // SyncPlayerChestIndex（包 34）：Byte PlayerId + Int16 ChestIndex
                    bw.Write(chestIndex.PlayerId);
                    bw.Write(chestIndex.ChestIndex);
                    break;

                case PlayerHealPacket heal:
                    // PlayerHeal（包 35）：Byte PlayerId + Int16 Amount
                    bw.Write((byte)heal.PlayerId);
                    bw.Write((short)heal.Amount);
                    break;

                case SyncPlayerZonePacket zone:
                    // SyncPlayerZone（包 36）：Byte PlayerId + Byte zone1..zone5 + Byte TownNPCs
                    bw.Write(zone.PlayerId);
                    bw.Write(zone.Zone1);
                    bw.Write(zone.Zone2);
                    bw.Write(zone.Zone3);
                    bw.Write(zone.Zone4);
                    bw.Write(zone.Zone5);
                    bw.Write(zone.TownNpcs);
                    break;

                case PlayerBuffsPacket buffs:
                    // PlayerBuffs（包 50）：Byte PlayerId + UInt16 增益序列 + UInt16 0 结束
                    bw.Write((byte)buffs.PlayerId);
                    foreach (var buffType in buffs.BuffTypes)
                        bw.Write((ushort)buffType);
                    bw.Write((ushort)0);
                    break;

                case TeleportEntityPacket teleport:
                    WriteTeleportEntity(bw, teleport);
                    break;

                case RequestTeleportationByServerPacket teleportRequest:
                    // RequestTeleportationByServer（包 73）：Byte 传送种类（0..4）
                    bw.Write((byte)teleportRequest.Kind);
                    break;

                case PlayerHurtV2Packet hurt:
                    // PlayerHurtV2（包 117）：Byte PlayerId + PlayerDeathReason + Int16 Damage
                    //   + Byte（击退方向+1）+ BitsByte（bit0=暴击、bit1=PvP）+ SByte 免伤冷却计数
                    bw.Write((byte)hurt.PlayerId);
                    WriteDeathReason(bw, hurt.DeathReason);
                    bw.Write((short)hurt.Damage);
                    bw.Write((byte)(hurt.HitDirection + 1));
                    bw.Write((byte)((hurt.Crit ? 0x01 : 0) | (hurt.Pvp ? 0x02 : 0)));
                    bw.Write(hurt.CooldownCounter);
                    break;

                case PlayerDeathV2Packet death:
                    // PlayerDeathV2（包 118）：Byte PlayerId + PlayerDeathReason + Int16 Damage
                    //   + Byte（击退方向+1）+ BitsByte（bit0=PvP）
                    bw.Write((byte)death.PlayerId);
                    WriteDeathReason(bw, death.DeathReason);
                    bw.Write((short)death.Damage);
                    bw.Write((byte)(death.HitDirection + 1));
                    bw.Write((byte)(death.Pvp ? 0x01 : 0));
                    break;

                case ProjectileDestroyPacket destroy:
                    // KillProjectile（包 29）：Int32 弹幕键 + Vector2 位置
                    bw.Write(destroy.ProjectileKey);
                    WriteVector2(bw, destroy.Position);
                    break;

                case TimePacket time:
                    // Time（包 18）：Byte dayTime + Int32 time + Int16 sunModY + Int16 moonModY
                    bw.Write((byte)(time.DayTime ? 1 : 0));
                    bw.Write(time.Time);
                    bw.Write(time.SunModY);
                    bw.Write(time.MoonModY);
                    break;

                case NpcUpdatePacket npc:
                    WriteNpcUpdate(bw, npc);
                    break;

                case NetTextPacket netText:
                    WriteNetText(bw, netText);
                    break;

                case LiquidModulePacket liquid:
                    // NetLiquidModule（模块 0）：UInt16 模块号 + UInt16 条目数
                    //   + 条目 ×（Int16 X + Int16 Y + Byte 液体量 + Byte 液体类型）
                    bw.Write((ushort)0);
                    bw.Write((ushort)liquid.Changes.Count);
                    foreach (var c in liquid.Changes)
                    {
                        bw.Write((short)c.X);
                        bw.Write((short)c.Y);
                        bw.Write(c.Amount);
                        bw.Write(c.Type);
                    }
                    break;

                case DisconnectPacket disconnect:
                    // Kick：NetworkText（mode=0 字面量 + 7-bit 长度前缀字符串）
                    if (disconnect.Reason is not null)
                        WriteNetworkText(bw, disconnect.Reason);
                    break;

                case UnknownPacket unknown:
                    // 未结构化建模的包：原样透传 payload，保证转发 / 回放不丢字节。
                    // 解码侧对未识别包统一产出 UnknownPacket，故编解码对本版本其余包
                    // 保持「透明可转发」，无需逐包建模；仅需权威校验的包才结构化。
                    bw.Write(unknown.Payload);
                    break;

                default:
                    throw new NotSupportedException(
                        $"Encode not implemented for {packet.GetType().Name} ({type})");
            }
        }

        var bytes = ms.ToArray();
        Framing.WriteFrame(writer, type, bytes);
    }

    private static void WriteVector2(BinaryWriter bw, Vector2 v)
    {
        bw.Write(v.X);
        bw.Write(v.Y);
    }

    /// <summary>NetTextModule 的模块号（权威：NetworkInitializer 注册顺序，NetLiquidModule=0 → NetTextModule=1）。</summary>
    private const ushort NetTextModuleId = 1;

    /// <summary>
    /// NPC 生成 / 更新（包 23）：只写「满血 + 无 ai + 非雕像 / 无难度覆盖」的最小形态。
    /// bitsA.bit7 置位 → 客户端跳过生命段，故无需服务端维护 NPC 生命。
    /// </summary>
    private static void WriteNpcUpdate(BinaryWriter bw, NpcUpdatePacket npc)
    {
        bw.Write(npc.Index);            // Byte 索引（0..199）
        bw.Write(npc.Generation);       // Byte generation
        // 同步锚点：对「锚点非零」的 NPC，上报的是 position + 体型 × 锚点，客户端接收后再减去同一偏移。
        // 不补偿会让客户端把该 NPC 画偏（当前只有史莱姆王 50：锚点 (0.5,1)、体型 98×92 → 偏移 (49,92)）。
        var anchor = NpcSyncAnchorOffset(npc.NetId);
        WriteVector2(bw, new Vector2(npc.Position.X + anchor.X, npc.Position.Y + anchor.Y));
        WriteVector2(bw, npc.Velocity);
        bw.Write(npc.Target);           // UInt16 target

        var lifeFull = npc.Life >= npc.LifeMax;
        byte bitsA = 0;
        if (npc.DirectionPositive) bitsA |= 0x01;
        if (npc.DirectionYPositive) bitsA |= 0x02;
        if (npc.SpriteDirectionPositive) bitsA |= 0x40;
        if (lifeFull) bitsA |= 0x80;    // bit7=1：生命为满 → 省略生命段
        bw.Write(bitsA);
        bw.Write((byte)0);              // BitsByte B：无玩家数缩放 / 非雕像 / 无难度覆盖 / 非需同步生成

        bw.Write(npc.NetId);            // Int16 netID（客户端据此 SetDefaults 生成 NPC）

        if (lifeFull) return;

        // 生命段：Byte 宽度标记（1=sbyte / 2=short / 4=int）+ 对应宽度的生命值。
        // 生命为 0 表示该 NPC 已死亡，客户端据此移除。
        byte width = npc.LifeMax > 32767 ? (byte)4 : npc.LifeMax > 127 ? (byte)2 : (byte)1;
        bw.Write(width);
        switch (width)
        {
            case 2: bw.Write((short)npc.Life); break;
            case 4: bw.Write(npc.Life); break;
            default: bw.Write((sbyte)npc.Life); break;
        }
    }

    /// <summary>
    /// NPC 同步锚点偏移（体型 × 锚点，像素）。锚点默认 (0,0)，只有极少数 NPC 非零
    /// （当前仅史莱姆王 50：锚点 (0.5,1)，体型 98×92 → 偏移 (49,92)）。
    /// </summary>
    private static Vector2 NpcSyncAnchorOffset(short netId) => netId switch
    {
        50 => new Vector2(49f, 92f),
        _ => default,
    };

    /// <summary>
    /// 聊天（包 82 = LoadNetModule → NetTextModule）。
    /// 下行：UInt16 模块号 + Byte 作者 + Byte 模式(0=Literal) + String 文本 + RGB；
    /// 上行：UInt16 模块号 + String 命令名 + String 文本。
    /// </summary>
    private static void WriteNetText(BinaryWriter bw, NetTextPacket netText)
    {
        bw.Write(NetTextModuleId);

        if (netText.IsClientMessage)
        {
            WritePrefixedString(bw, netText.CommandName);
            WritePrefixedString(bw, netText.Text);
            return;
        }

        bw.Write(netText.AuthorId);
        bw.Write((byte)0);              // NetworkText 模式：0 = Literal
        WritePrefixedString(bw, netText.Text);
        bw.Write(netText.Color.R);
        bw.Write(netText.Color.G);
        bw.Write(netText.Color.B);
    }

    /// <summary>
    /// 编码 WorldData（包 7）全量字段。
    /// 顺序严格对应 <c>NetMessage.SendData</c> case 7；缺失的数组按声明长度补 0。
    /// </summary>
    private static void WriteWorldInfo(BinaryWriter bw, WorldInfoPacket world)
    {
        bw.Write(world.Time);

        byte flags = 0;
        if (world.DayTime) flags |= 1 << 0;
        if (world.BloodMoon) flags |= 1 << 1;
        if (world.Eclipse) flags |= 1 << 2;
        bw.Write(flags);

        bw.Write(world.MoonPhase);
        bw.Write(world.MaxTilesX);
        bw.Write(world.MaxTilesY);
        bw.Write(world.SpawnTileX);
        bw.Write(world.SpawnTileY);
        bw.Write(world.WorldSurface);
        bw.Write(world.RockLayer);
        bw.Write(world.WorldId);
        WritePrefixedString(bw, world.WorldName);
        bw.Write(world.GameMode);

        bw.Write(world.UniqueId.ToByteArray());
        bw.Write(world.WorldGeneratorVersion);
        bw.Write(world.MoonType);

        WriteBytes(bw, world.Backgrounds, 13);
        bw.Write(world.IceBackStyle);
        bw.Write(world.JungleBackStyle);
        bw.Write(world.HellBackStyle);
        bw.Write(world.WindSpeedTarget);
        bw.Write(world.NumClouds);

        WriteInt32s(bw, world.TreeX, 3);
        WriteBytes(bw, world.TreeStyle, 4);
        WriteInt32s(bw, world.CaveBackX, 3);
        WriteBytes(bw, world.CaveBackStyle, 4);

        WriteBytes(bw, world.TreeTops, 13);

        bw.Write(world.MaxRaining);

        WriteBytes(bw, world.ProgressFlags, 11);

        bw.Write(world.SundialCooldown);
        bw.Write(world.MoondialCooldown);

        WriteInt16s(bw, world.OreTiers, 7);

        bw.Write(world.InvasionType);
        bw.Write(world.LobbyId);
        bw.Write(world.SandstormIntensity);

        // ExtraSpawnPointManager.Write：Byte count + count×(Int16 X, Int16 Y)
        var spawns = world.ExtraSpawnPoints;
        bw.Write((byte)spawns.Length);
        foreach (var (x, y) in spawns)
        {
            bw.Write(x);
            bw.Write(y);
        }

        bw.Write(world.DungeonX);
        bw.Write(world.DungeonY);
    }

    /// <summary>
    /// PlayerSpawn（包 12）：Byte + Int16 + Int16 + Int32 + Int16 + Int16 + Byte + Byte。
    /// </summary>
    private static void WritePlayerSpawn(BinaryWriter bw, PlayerSpawnPacket p)
    {
        bw.Write(p.PlayerId);
        bw.Write(p.SpawnX);
        bw.Write(p.SpawnY);
        bw.Write(p.RespawnTimer);
        bw.Write(p.DeathsPve);
        bw.Write(p.DeathsPvp);
        bw.Write(p.Team);
        bw.Write(p.SpawnContext);
    }

    /// <summary>
    /// SyncPlayer（包 4）：Byte id + Byte skin + Byte voice + Single pitch + Byte hair + String name
    /// + Byte hairDye + UInt16 hideVisibleAccessory + Byte hideMisc + RGB×7 + BitsByte×3。
    /// </summary>
    private static void WritePlayerInfo(BinaryWriter bw, PlayerInfoPacket p)
    {
        bw.Write(p.Slot);
        bw.Write(p.SkinVariant);
        bw.Write(p.VoiceVariant);
        bw.Write(p.VoicePitchOffset);
        bw.Write(p.Hair);
        WritePrefixedString(bw, p.Name);
        bw.Write(p.HairDye);
        bw.Write(p.HideVisibleAccessory);   // WriteAccessoryVisibility：每位对应一个可见饰品
        bw.Write(p.HideMisc);

        WriteRgb(bw, p.HairColor);
        WriteRgb(bw, p.SkinColor);
        WriteRgb(bw, p.EyeColor);
        WriteRgb(bw, p.ShirtColor);
        WriteRgb(bw, p.UnderShirtColor);
        WriteRgb(bw, p.PantsColor);
        WriteRgb(bw, p.ShoeColor);

        byte difficulty = 0;
        if (p.Difficulty == 1) difficulty |= 1 << 0;
        else if (p.Difficulty == 2) difficulty |= 1 << 1;
        else if (p.Difficulty == 3) difficulty |= 1 << 3;
        if (p.ExtraAccessory) difficulty |= 1 << 2;
        bw.Write(difficulty);

        bw.Write(p.TorchFlags);
        bw.Write(p.UnlockFlags);
    }

    /// <summary>SyncEquipment（包 5）：Byte id + Int16 slot + Int16 stack + Byte prefix + Int16 type + BitsByte。</summary>
    private static void WriteInventorySlot(BinaryWriter bw, InventorySlotPacket s)
    {
        bw.Write((byte)s.PlayerId);
        bw.Write((short)s.Slot);
        bw.Write((short)s.Stack);
        bw.Write(s.Prefix);
        bw.Write((short)s.ItemId);
        bw.Write((byte)(s.Favorited ? 1 : 0));
    }

    /// <summary>
    /// SyncItem（包 21）：Int16 槽位 + Vector2 位置 + Vector2 速度 + Int16 堆叠 + Byte 词缀
    /// + BitsByte 标志 + Int16 类型 + 可选微光 / 抓取延迟字段。
    /// </summary>
    private static void WriteSyncItem(BinaryWriter bw, ItemDropPacket item)
    {
        bw.Write((short)item.ItemSlotIndex);
        WriteVector2(bw, item.Position);
        WriteVector2(bw, item.Velocity);
        bw.Write((short)item.Stack);
        bw.Write(item.Prefix);

        byte flags = 0;
        if (item.Shimmered || item.ShimmerTime > 0f) flags |= 0x04;
        if (item.EnemyGrabDelayTime > 0) flags |= 0x08;
        bw.Write(flags);
        bw.Write((short)item.ItemId);

        if ((flags & 0x04) != 0)
        {
            bw.Write(item.Shimmered);
            bw.Write(item.ShimmerTime);
        }
        if ((flags & 0x08) != 0)
            bw.Write(item.EnemyGrabDelayTime);
    }

    /// <summary>
    /// SyncProjectile（包 27）：Int32 键 + Vector2 位置 + Vector2 速度 + Int16 类型
    /// + BitsByte 标志1 + 可选 BitsByte 标志2 + 依标志按序的可选字段（含末尾 ai[2]）。
    /// </summary>
    private static void WriteProjectileNew(BinaryWriter bw, ProjectileNewPacket proj)
    {
        bw.Write(proj.ProjectileKey);
        WriteVector2(bw, proj.Position);
        WriteVector2(bw, proj.Velocity);
        bw.Write((short)proj.ProjectileType);

        byte flags1 = 0;
        byte flags2 = 0;
        if (proj.Ai0 != 0f) flags1 |= 0x01;
        if (proj.Ai1 != 0f) flags1 |= 0x02;
        if (proj.Ai2 != 0f) flags2 |= 0x01;
        if (proj.BannerIdToRespondTo != 0) flags1 |= 0x08;
        if (proj.Damage != 0) flags1 |= 0x10;
        if (proj.Knockback != 0f) flags1 |= 0x20;
        if (proj.OriginalDamage != 0) flags1 |= 0x40;
        if (flags2 != 0) flags1 |= 0x04;

        bw.Write(flags1);
        if ((flags1 & 0x04) != 0) bw.Write(flags2);
        if ((flags1 & 0x01) != 0) bw.Write(proj.Ai0);
        if ((flags1 & 0x02) != 0) bw.Write(proj.Ai1);
        if ((flags1 & 0x08) != 0) bw.Write((ushort)proj.BannerIdToRespondTo);
        if ((flags1 & 0x10) != 0) bw.Write((short)proj.Damage);
        if ((flags1 & 0x20) != 0) bw.Write(proj.Knockback);
        if ((flags1 & 0x40) != 0) bw.Write((short)proj.OriginalDamage);
        if ((flags2 & 0x01) != 0) bw.Write(proj.Ai2);
    }

    /// <summary>
    /// 写入死亡原因（等价原版 <c>PlayerDeathReason.WriteSelfTo</c>）：
    /// 8 位标志 + 依标志存在的条件字段（Int16/Byte/字符串）。
    /// </summary>
    private static void WriteDeathReason(BinaryWriter bw, PlayerDeathReasonData reason)
    {
        byte bits = 0;
        if (reason.SourcePlayerIndex != -1) bits |= 0x01;
        if (reason.SourceNpcIndex != -1) bits |= 0x02;
        if (reason.SourceProjectileLocalIndex != -1) bits |= 0x04;
        if (reason.SourceOtherIndex != -1) bits |= 0x08;
        if (reason.SourceProjectileType != 0) bits |= 0x10;
        if (reason.SourceItemType != 0) bits |= 0x20;
        if (reason.SourceItemPrefix != 0) bits |= 0x40;
        if (reason.CustomReason is not null) bits |= 0x80;
        bw.Write(bits);

        if ((bits & 0x01) != 0) bw.Write((short)reason.SourcePlayerIndex);
        if ((bits & 0x02) != 0) bw.Write((short)reason.SourceNpcIndex);
        if ((bits & 0x04) != 0) bw.Write((short)reason.SourceProjectileLocalIndex);
        if ((bits & 0x08) != 0) bw.Write((byte)reason.SourceOtherIndex);
        if ((bits & 0x10) != 0) bw.Write((short)reason.SourceProjectileType);
        if ((bits & 0x20) != 0) bw.Write((short)reason.SourceItemType);
        if ((bits & 0x40) != 0) bw.Write((byte)reason.SourceItemPrefix);
        if ((bits & 0x80) != 0) WritePrefixedString(bw, reason.CustomReason!);
    }

    /// <summary>
    /// PlayerControls（包 13）：Byte id + BitsByte×4 + Byte selectedItem + Vector2 position
    /// + 可选 Vector2 velocity（StateBits bit2）。挂载 / 回城 / 相机等可选字段当前不发出。
    /// </summary>
    private static void WritePlayerControls(BinaryWriter bw, PlayerControlsPacket p)
    {
        // 标志位与尾随字段必须自洽：解码端会按 StateBits bit7（挂载）、StateBits2 bit6（回城）、
        // StateBits3 bit5（相机）读取尾随字段，而本编码器不发出这些字段。若原样透传标志位，
        // 接收端会多读字节，整条连接的后续包全部错位（流式协议无法自愈）。
        var stateBits = (byte)(p.StateBits & ~0x80);
        var stateBits2 = (byte)(p.StateBits2 & ~0x40);
        var stateBits3 = (byte)(p.StateBits3 & ~0x20);

        bw.Write(p.PlayerId);
        bw.Write(p.ControlBits);
        bw.Write(stateBits);
        bw.Write(stateBits2);
        bw.Write(stateBits3);
        bw.Write(p.SelectedItem);
        WriteVector2(bw, p.Position);

        if ((stateBits & PlayerControlsPacket.StateBitHasVelocity) != 0)
            WriteVector2(bw, p.Velocity);
    }

    /// <summary>
    /// TeleportEntity（包 65）：BitsByte 标志 + Int16 实体索引 + Vector2 位置 + Byte 样式
    /// + 可选 Int32 额外信息。标志：bit0/bit1=种类、bit2=不带位置、bit3=携带额外 Int32。
    /// </summary>
    private static void WriteTeleportEntity(BinaryWriter bw, TeleportEntityPacket p)
    {
        byte flags = 0;
        if (((byte)p.Kind & 0x01) != 0) flags |= 0x01;
        if (((byte)p.Kind & 0x02) != 0) flags |= 0x02;
        if (p.NoPosition) flags |= 0x04;
        if (p.ExtraInfo != 0) flags |= 0x08;

        bw.Write(flags);
        bw.Write((short)p.EntityId);
        WriteVector2(bw, p.Position);
        bw.Write(p.Style);
        if ((flags & 0x08) != 0)
            bw.Write(p.ExtraInfo);
    }

    private static void WriteRgb(BinaryWriter bw, RgbColor c)
    {
        bw.Write(c.R);
        bw.Write(c.G);
        bw.Write(c.B);
    }

    // ---------- 包 10：TileSection（Deflate + 位标志 + RLE） ----------

    /// <summary>
    /// 编码 TileSection（包 10）：Deflate 压缩的矩形区块。
    /// 布局严格对应 <c>NetMessage.CompressTileBlock</c>：
    ///   Deflate[ Int32 xStart + Int32 yStart + Int16 width + Int16 height + CompressTileBlock_Inner ]。
    /// </summary>
    private static void WriteTileSection(Stream output, TileSectionPacket section)
    {
        // 先在区块读锁内把图格拷成快照，再压缩：拷贝是 memcpy 级，
        // 避免把 Deflate 的耗时压进锁内阻塞仿真线程的图格写入（详见 SectionLocks）。
        var tiles = SnapshotTiles(section);

        using var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true);
        using var bw = new BinaryWriter(deflate, System.Text.Encoding.UTF8, leaveOpen: true);

        bw.Write(section.XStart);
        bw.Write(section.YStart);
        bw.Write((short)section.Width);
        bw.Write((short)section.Height);

        CompressTileBlockInner(
            bw, tiles, section.World, section.XStart, section.YStart, section.Width, section.Height);
    }

    /// <summary>在区块读锁内拷贝图格矩形（拷贝的是值，释放锁后即可安全使用）。</summary>
    private static Tile[] SnapshotTiles(TileSectionPacket section)
    {
        var world = section.World;
        int width = section.Width;
        int height = section.Height;
        var snapshot = new Tile[width * height];

        using (world.Sections.EnterRead(
                   section.XStart, section.YStart,
                   section.XStart + width - 1, section.YStart + height - 1))
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    snapshot[x * height + y] = world.Tiles[section.XStart + x, section.YStart + y];
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 图格方阵（包 20）：Int16 X/Y + Byte 宽/高 + Byte 变更类型 + 逐格（3 个位标志字节 + 可选段），**未压缩**。
    /// 逐格顺序与客户端读取侧严格一致：三个标志字节 → 方块/墙油漆 → 类型（可含 frameX/Y）→ 墙 → 液体。
    /// </summary>
    private static void WriteTileSquare(BinaryWriter bw, TileSquarePacket square)
    {
        var world = square.World;
        int width = Math.Clamp(square.Width, 1, byte.MaxValue);
        int height = Math.Clamp(square.Height, 1, byte.MaxValue);

        // 坐标钳制：保证矩形完全落在世界内（原版同样把左上角钳到 [size, max - size]）
        int x = Math.Clamp(square.X, Math.Min(width, world.MaxTilesX - 1),
            Math.Max(0, world.MaxTilesX - width - 1));
        int y = Math.Clamp(square.Y, Math.Min(height, world.MaxTilesY - 1),
            Math.Max(0, world.MaxTilesY - height - 1));

        bw.Write((short)x);
        bw.Write((short)y);
        bw.Write((byte)width);
        bw.Write((byte)height);
        bw.Write(square.ChangeType);

        using (world.Sections.EnterRead(x, y, x + width - 1, y + height - 1))
        {
            for (int tx = x; tx < x + width; tx++)
                for (int ty = y; ty < y + height; ty++)
                    WriteSquareTile(bw, in world.Tiles[tx, ty]);
        }
    }

    /// <summary>单格编码（包 20 的逐格负载；字段顺序与客户端读取侧对称）。</summary>
    private static void WriteSquareTile(BinaryWriter bw, in Tile tile)
    {
        byte b1 = 0;
        if (tile.Active) b1 |= 1 << 0;
        if (tile.Wall > 0) b1 |= 1 << 2;
        if (tile.Liquid > 0) b1 |= 1 << 3;
        if (tile.Wire) b1 |= 1 << 4;
        if (tile.HalfBrick) b1 |= 1 << 5;
        if (tile.Actuator) b1 |= 1 << 6;
        if (tile.InActive) b1 |= 1 << 7;

        byte b2 = 0;
        if (tile.Wire2) b2 |= 1 << 0;
        if (tile.Wire3) b2 |= 1 << 1;
        bool hasTileColor = tile.Active && tile.TileColor > 0;
        bool hasWallColor = tile.Wall > 0 && tile.WallColor > 0;
        if (hasTileColor) b2 |= 1 << 2;
        if (hasWallColor) b2 |= 1 << 3;
        b2 |= (byte)((tile.Slope & 0x07) << 4); // bits4-6 = 斜坡
        if (tile.Wire4) b2 |= 1 << 7;

        byte b3 = 0;
        if (tile.FullbrightBlock) b3 |= 1 << 0;
        if (tile.FullbrightWall) b3 |= 1 << 1;
        if (tile.InvisibleBlock) b3 |= 1 << 2;
        if (tile.InvisibleWall) b3 |= 1 << 3;

        bw.Write(b1);
        bw.Write(b2);
        bw.Write(b3);

        if (hasTileColor) bw.Write(tile.TileColor);
        if (hasWallColor) bw.Write(tile.WallColor);

        if (tile.Active)
        {
            bw.Write(tile.Type);
            if (TileIdSets.IsTileFrameImportant(tile.Type))
            {
                bw.Write(tile.FrameX);
                bw.Write(tile.FrameY);
            }
        }

        if (tile.Wall > 0) bw.Write(tile.Wall);

        if (tile.Liquid > 0)
        {
            bw.Write(tile.Liquid);
            bw.Write(tile.LiquidType);
        }
    }

    /// <summary>
    /// 逐图格编码区块（等价 <c>NetMessage.CompressTileBlock_Inner</c>）。
    /// 每格写入 [可选 b2][可选 b3][可选 b4][主标志 b][payload][可选 RLE 计数]，主标志决定后续字节存在性。
    /// <paramref name="tiles"/> 为区块图格的快照（列优先，索引 = (x-xStart)*height + (y-yStart)）。
    /// </summary>
    private static void CompressTileBlockInner(
        BinaryWriter writer, Tile[] tiles, WorldState world,
        int xStart, int yStart, int width, int height)
    {
        // 尾部宝箱 / 牌子按坐标索引（原版通过 Chest.FindChest / Sign.ReadSign 查找）
        var chestByPos = new Dictionary<(int X, int Y), Chest>(world.Chests.Count);
        foreach (var c in world.Chests) chestByPos[(c.X, c.Y)] = c;
        var signByPos = new Dictionary<(int X, int Y), Sign>(world.Signs.Count);
        foreach (var s in world.Signs) signByPos[(s.X, s.Y)] = s;

        var chests = new List<Chest>();
        var signs = new List<Sign>();

        int run = 0;    // 连续相同图格计数（不含首格）
        int pos = 0;    // payload 写入位置（原版 num5）
        int start = 0;  // 头部起始索引（原版 num6）
        byte main = 0;  // 主标志（原版 b）
        var buf = new byte[24];
        bool hasPrev = false;
        Tile prev = default;

        for (int y = yStart; y < yStart + height; y++)
        {
            for (int x = xStart; x < xStart + width; x++)
            {
                ref var tile = ref tiles[(x - xStart) * height + (y - yStart)];

                if (hasPrev && IsSameAs(in tile, in prev) && TileIdSets.AllowsSaveCompressionBatching(tile.Type))
                {
                    run++;
                    continue;
                }

                if (hasPrev)
                {
                    FlushTile(writer, buf, main, pos, start, run);
                    run = 0;
                }

                pos = 4;
                byte b2 = 0, b3 = 0, b4 = 0;
                main = 0;

                if (tile.Active)
                {
                    main |= 0x02;
                    buf[pos++] = (byte)tile.Type;
                    if (tile.Type > 255)
                    {
                        buf[pos++] = (byte)(tile.Type >> 8);
                        main |= 0x20;
                    }

                    // 宝箱：BasicChest（frame 36 对齐）与 88 号图格（frame 54/36 对齐）
                    if ((TileIdSets.IsBasicChest(tile.Type) && tile.FrameX % 36 == 0 && tile.FrameY % 36 == 0)
                        || (tile.Type == 88 && tile.FrameX % 54 == 0 && tile.FrameY % 36 == 0))
                    {
                        if (chestByPos.TryGetValue((x, y), out var chest))
                            chests.Add(chest);
                    }

                    if (TileIdSets.IsSign(tile.Type) && tile.FrameX % 36 == 0 && tile.FrameY % 36 == 0)
                    {
                        if (signByPos.TryGetValue((x, y), out var sign))
                            signs.Add(sign);
                    }

                    if (TileIdSets.IsTileFrameImportant(tile.Type))
                    {
                        buf[pos++] = (byte)(tile.FrameX & 0xFF);
                        buf[pos++] = (byte)((tile.FrameX & 0xFF00) >> 8);
                        buf[pos++] = (byte)(tile.FrameY & 0xFF);
                        buf[pos++] = (byte)((tile.FrameY & 0xFF00) >> 8);
                    }

                    if (tile.TileColor != 0)
                    {
                        b3 |= 0x08;
                        buf[pos++] = tile.TileColor;
                    }
                }

                if (tile.Wall != 0)
                {
                    main |= 0x04;
                    buf[pos++] = (byte)tile.Wall;
                    if (tile.WallColor != 0)
                    {
                        b3 |= 0x10;
                        buf[pos++] = tile.WallColor;
                    }
                }

                if (tile.Liquid != 0)
                {
                    if (tile.LiquidType == 3) // shimmer
                    {
                        b3 |= 0x80;
                        main |= 0x08;
                    }
                    else
                    {
                        main = tile.LiquidType == 1 ? (byte)(main | 0x10)
                             : tile.LiquidType == 2 ? (byte)(main | 0x18)
                             : (byte)(main | 0x08);
                    }
                    buf[pos++] = tile.Liquid;
                }

                if (tile.Wire) b4 |= 0x02;
                if (tile.Wire2) b4 |= 0x04;
                if (tile.Wire3) b4 |= 0x08;
                b4 |= (byte)(tile.HalfBrick ? 16 : (tile.Slope != 0 ? (tile.Slope + 1) << 4 : 0));

                if (tile.Actuator) b3 |= 0x02;
                if (tile.InActive) b3 |= 0x04;
                if (tile.Wire4) b3 |= 0x20;

                if (tile.Wall > 255)
                {
                    buf[pos++] = (byte)(tile.Wall >> 8);
                    b3 |= 0x40;
                }

                if (tile.InvisibleBlock) b2 |= 0x02;
                if (tile.InvisibleWall) b2 |= 0x04;
                if (tile.FullbrightBlock) b2 |= 0x08;
                if (tile.FullbrightWall) b2 |= 0x10;

                // 头部逆序回填：b2 → b3 → b4 → main（写出的字节顺序为 main, b4, b3, b2）
                start = 3;
                if (b2 != 0) { b3 |= 0x01; buf[start--] = b2; }
                if (b3 != 0) { b4 |= 0x01; buf[start--] = b3; }
                if (b4 != 0) { main |= 0x01; buf[start--] = b4; }

                hasPrev = true;
                prev = tile;
            }
        }

        if (hasPrev)
            FlushTile(writer, buf, main, pos, start, run);

        // ---- 尾部：宝箱（Int16 count + 每箱 Int16 index/x/y + 名称串） ----
        writer.Write((short)chests.Count);
        foreach (var c in chests)
        {
            writer.Write((short)c.Index);
            writer.Write((short)c.X);
            writer.Write((short)c.Y);
            writer.Write(c.Name);
        }

        // ---- 尾部：牌子 ----
        writer.Write((short)signs.Count);
        foreach (var s in signs)
        {
            writer.Write((short)s.Index);
            writer.Write((short)s.X);
            writer.Write((short)s.Y);
            writer.Write(s.Text);
        }

        // ---- 尾部：图格实体（本模型暂不支持） ----
        writer.Write((short)0);
    }

    /// <summary>写出一个图格（含可选 RLE 游程计数）：<c>[start..pos)</c> 为头部+payload，RLE 计数追加在 payload 之后。</summary>
    private static void FlushTile(BinaryWriter writer, byte[] buf, byte main, int pos, int start, int run)
    {
        if (run > 0)
        {
            buf[pos++] = (byte)(run & 0xFF);
            if (run > 255)
            {
                main |= 0x80;
                buf[pos++] = (byte)((run & 0xFF00) >> 8);
            }
            else
            {
                main |= 0x40;
            }
        }

        buf[start] = main;
        writer.Write(buf, start, pos - start);
    }

    /// <summary>图格语义相等（用于 RLE 合并）；比原版 <c>Tile.isTheSameAs</c> 更严格，仅保证不误合并。</summary>
    private static bool IsSameAs(in Tile a, in Tile b) =>
        a.Active == b.Active &&
        a.Type == b.Type &&
        a.Wall == b.Wall &&
        a.Liquid == b.Liquid &&
        a.LiquidType == b.LiquidType &&
        a.Slope == b.Slope &&
        a.HalfBrick == b.HalfBrick &&
        a.Wire == b.Wire &&
        a.Wire2 == b.Wire2 &&
        a.Wire3 == b.Wire3 &&
        a.Wire4 == b.Wire4 &&
        a.Actuator == b.Actuator &&
        a.InActive == b.InActive &&
        a.InvisibleBlock == b.InvisibleBlock &&
        a.InvisibleWall == b.InvisibleWall &&
        a.FullbrightBlock == b.FullbrightBlock &&
        a.FullbrightWall == b.FullbrightWall &&
        a.TileColor == b.TileColor &&
        a.WallColor == b.WallColor &&
        a.FrameX == b.FrameX &&
        a.FrameY == b.FrameY;

    /// <summary>NetworkText 字面量：Byte mode(0) + 7-bit 长度前缀字符串。</summary>
    private static void WriteNetworkText(BinaryWriter bw, string text)
    {
        bw.Write((byte)0); // NetworkText.Mode.Literal
        WritePrefixedString(bw, text);
    }

    private static void WriteBytes(BinaryWriter bw, byte[] values, int count)
    {
        for (int i = 0; i < count; i++)
            bw.Write(i < values.Length ? values[i] : (byte)0);
    }

    private static void WriteInt32s(BinaryWriter bw, int[] values, int count)
    {
        for (int i = 0; i < count; i++)
            bw.Write(i < values.Length ? values[i] : 0);
    }

    private static void WriteInt16s(BinaryWriter bw, short[] values, int count)
    {
        for (int i = 0; i < count; i++)
            bw.Write(i < values.Length ? values[i] : (short)0);
    }

    // ---------- 字符串辅助（与 PacketDecoder.ReadPrefixedString 对称） ----------

    /// <summary>
    /// 写入 Terraria 字符串：7-bit 变长编码长度前缀 + UTF-8 字节。
    /// </summary>
    internal static void WritePrefixedString(BinaryWriter bw, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Write7BitEncodedInt(bw, bytes.Length);
        bw.Write(bytes);
    }

    /// <summary>
    /// 写入 7-bit 变长编码整数（每字节最高位 0x80 表示续字节）。
    /// </summary>
    internal static void Write7BitEncodedInt(BinaryWriter bw, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            bw.Write((byte)(v | 0x80));
            v >>= 7;
        }
        bw.Write((byte)v);
    }
}
