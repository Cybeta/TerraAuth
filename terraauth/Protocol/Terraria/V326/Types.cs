// TerraAuth — 协议层：包契约 + 领域类型
// 架构 §3.2：所有客户端通信经此抽象

using System;

namespace TerraAuth.Protocol;

/// <summary>网络包契约。所有上行/下行消息实现此接口。</summary>
public interface INetworkPacket
{
    /// <summary>包类型（映射到 terraria-protocol 的 PacketId）。</summary>
    PacketId Type { get; }
}

/// <summary>未知/未实现包（占位，避免崩溃）。</summary>
public sealed record UnknownPacket(PacketId Type, byte[] Payload) : INetworkPacket;

/// <summary>向量（对接 terraria-protocol 的 Vector2 布局）。</summary>
public readonly record struct Vector2(float X, float Y);

/// <summary>RGB 颜色（对接 <c>BinaryWriter.WriteRGB</c>：R/G/B 各 1 字节）。</summary>
public readonly record struct RgbColor(byte R, byte G, byte B);

/// <summary>玩家激活状态（PlayerActive，包 14）：Byte PlayerId + Byte Active。</summary>
public sealed record PlayerActivePacket(byte PlayerId, bool Active) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerActive;
}

/// <summary>
/// 玩家控制状态（PlayerControls，包 13，下行广播）。
/// 4 个 BitsByte + 选中槽 + 位置；速度等可选字段由对应标志位决定是否写入。
/// </summary>
public sealed record PlayerControlsPacket(
    byte PlayerId,
    Vector2 Position,
    Vector2 Velocity = default,
    byte SelectedItem = 0,
    byte ControlBits = 0,
    byte StateBits = 0,
    byte StateBits2 = 0,
    byte StateBits3 = 0) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerPosition;

    /// <summary>StateBits 第 2 位（0x04）：速度非零，payload 尾部携带 Vector2 速度。</summary>
    public const byte StateBitHasVelocity = 0x04;

    /// <summary>StateBits 第 7 位（0x80）：尾部携带 UInt16 挂载类型。</summary>
    public const byte StateBitHasMount = 0x80;

    /// <summary>StateBits2 第 6 位（0x40）：尾部携带回城药水的两个 Vector2。</summary>
    public const byte StateBit2HasPotionReturn = 0x40;

    /// <summary>StateBits3 第 5 位（0x20）：尾部携带 Vector2 相机目标。</summary>
    public const byte StateBit3HasCamera = 0x20;

    /// <summary>挂载类型（仅 <see cref="StateBitHasMount"/> 置位时存在于线格式）。</summary>
    public ushort? MountType { get; init; }

    /// <summary>回城药水使用位置（仅 <see cref="StateBit2HasPotionReturn"/> 置位时存在）。</summary>
    public Vector2? PotionReturnOriginal { get; init; }

    /// <summary>回城药水的「家」位置（与 <see cref="PotionReturnOriginal"/> 成对出现）。</summary>
    public Vector2? PotionReturnHome { get; init; }

    /// <summary>相机目标（仅 <see cref="StateBit3HasCamera"/> 置位时存在）。</summary>
    public Vector2? CameraTarget { get; init; }
}

/// <summary>玩家位置同步包（上行）。</summary>
public sealed record PlayerPositionPacket(int PlayerId, Vector2 Position) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerPosition;
}

/// <summary>玩家血量包（上行）。</summary>
public sealed record PlayerHealthPacket(int PlayerId, int Hp, int MaxHp) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerHealth;
}

/// <summary>
/// 玩家法力包（PlayerMana，包 42，上行）。
/// 布局：Byte PlayerId + Int16 statMana + Int16 statManaMax。
/// 原版不向其他玩家转发法力，故服务端只做权威跟踪（不中继）。
/// </summary>
public sealed record PlayerManaPacket(int PlayerId, int Mana, int MaxMana) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerMana;
}

/// <summary>
/// 对话 NPC 同步包（SyncTalkNPC，包 40，上行 / 下行）。
/// 布局：Byte PlayerId + Int16 talkNPC（-1 = 未与任何城镇 NPC 对话）。
/// 原版服务端收到后以发送者 whoAmI 覆盖 PlayerId，调用 <c>Player.SetTalkNPC(talkNPC)</c>，
/// 再中继给其他玩家；本实现同样由服务端持有该状态并广播给他人（不回发本人）。
/// </summary>
public sealed record SyncTalkNpcPacket(int PlayerId, int TalkNpc) : INetworkPacket
{
    public PacketId Type => PacketId.SyncTalkNPC;
}

/// <summary>
/// NPC 受击包（DamageNPC，包 28，上行，客户端发起）。
/// 布局：Byte NpcId + Byte Generation + Int16 Damage + Single Knockback + Byte Direction + Byte Crit。
/// </summary>
public sealed record NpcStrikePacket(int NpcId, int Damage) : INetworkPacket
{
    public PacketId Type => PacketId.NpcStrike;

    /// <summary>NPC 生成代数（校验目标未过期）。</summary>
    public int Generation { get; init; }

    /// <summary>击退力度。</summary>
    public float Knockback { get; init; }

    /// <summary>攻击方向（线格式为 direction+1，解码后还原为 -1 / 1）。</summary>
    public int Direction { get; init; }

    /// <summary>是否暴击。</summary>
    public bool Crit { get; init; }
}

/// <summary>
/// Tile 操作包（TileManipulation，包 17，上行）。
/// 布局：Byte Action + Int16 X + Int16 Y + Int16 TileType + Byte Style。
/// Action 语义见原版 <c>MessageBuffer</c> case 17（0=挖、1=放、2/3=墙、5+=电线/斜坡等）。
/// <para>
/// ⚠️ <see cref="TileType"/> 这个 Int16 的语义**随 Action 两用**（原版同一字段）：
/// Action 0/2/4（挖砖 / 挖墙 / 无掉落挖砖）时它是 <c>KillTile</c> 的 **fail 标志**
/// （1 = 仅命中特效、尚未挖穿；0 = 真正破坏——原版 Player.PickTile 正是这样发的）；
/// Action 1/3（放砖 / 放墙）时它才是图格 / 墙的**类型**。
/// 早期实现把它恒当作图格类型并与服务端对账，导致草(2)等一切非 0/1 的图格挖不动。
/// </para>
/// </summary>
public sealed record TileBreakPacket(int X, int Y, byte Action) : INetworkPacket
{
    public PacketId Type => PacketId.TileBreak;

    /// <summary>
    /// 第 4 个 Int16：Action 0/2/4 = fail 标志（0 真正破坏 / 非 0 仅命中特效）；
    /// Action 1/3 = 图格 / 墙类型。见类型注释。
    /// </summary>
    public int TileType { get; init; }

    /// <summary>样式变体（依 Action 语义不同）。</summary>
    public byte Style { get; init; }
}

/// <summary>
/// Tile 放置包（PlaceObject，包 79，上行）。
/// 布局：Int16 X + Int16 Y + Int16 TileType + Int16 Style + Byte Alternate
///   + SByte Random + Boolean Direction（解码后还原为 1 / -1）。
/// </summary>
public sealed record TilePlacePacket(int X, int Y, int TileType) : INetworkPacket
{
    public PacketId Type => PacketId.TilePlace;

    /// <summary>样式变体（PlaceObject 的 style 参数）。</summary>
    public int Style { get; init; }

    /// <summary>放置备用变体（PlaceObject 的 alternate 参数，线格式为 Byte）。</summary>
    public byte Alternate { get; init; }

    /// <summary>随机种子（线格式为 SByte）。</summary>
    public sbyte Random { get; init; }

    /// <summary>朝向（线格式为 Boolean，解码后还原为 1 / -1）。</summary>
    public int Direction { get; init; }
}

/// <summary>
/// 世界物品同步包（SyncItem，包 21，双向）。
/// 布局：Int16 槽位索引 + Vector2 位置 + Vector2 速度 + Int16 堆叠 + Byte 词缀
///   + BitsByte 标志（bit2=微光、bit3=敌人抓取延迟）+ Int16 物品类型
///   + 可选 微光状态（bit2）+ 可选 敌人抓取延迟（bit3）。
/// 客户端上报此包用于声明掉落物；服务端权威下须校验来源与堆叠。
/// </summary>
public sealed record ItemDropPacket(int ItemId, int Stack) : INetworkPacket
{
    public PacketId Type => PacketId.ItemDrop;

    /// <summary>世界物品槽位索引（Main.item 下标）。</summary>
    public int ItemSlotIndex { get; init; }

    /// <summary>世界坐标位置。</summary>
    public Vector2 Position { get; init; }

    /// <summary>速度。</summary>
    public Vector2 Velocity { get; init; }

    /// <summary>词缀 ID。</summary>
    public byte Prefix { get; init; }

    /// <summary>是否处于微光状态（标志 bit2）。</summary>
    public bool Shimmered { get; init; }

    /// <summary>微光计时（标志 bit2）。</summary>
    public float ShimmerTime { get; init; }

    /// <summary>敌人抓取延迟（标志 bit3）。</summary>
    public byte EnemyGrabDelayTime { get; init; }
}

/// <summary>
/// 物品拾取包（SyncItemOwner，包 22，上行）。
/// 布局：Int16 世界物品槽位 + Byte 归属玩家。
/// 注意：原版 1.4.5 客户端拾取物品后发的是包 151（ItemDestroy），**不发本包**；本类型保留
///   用于入站兼容（旧客户端 / 工具 / 现有测试）与服务端侧校验的通用入参形态。
/// </summary>
public sealed record ItemPickupPacket(int ItemSlotIndex) : INetworkPacket
{
    public PacketId Type => PacketId.ItemPickup;

    /// <summary>归属玩家（上行解码时填充；服务端以连接身份覆盖，防止代他人拾取）。</summary>
    public int PlayerId { get; init; }
}

/// <summary>
/// 物品拾取包（SyncItemOwner，包 22，下行）。
/// 布局：Int16 世界物品槽位 + Byte 归属玩家 + 7Bit 保留时长 + Byte 抓取延迟玩家
///   + 7Bit 抓取延迟 + Vector2 位置。
/// 服务端权威：物品归属变化（如 /give 专属掉落物）时广播此包，客户端据此把本地
///   <c>playerIndexTheItemIsReservedFor</c> 更新为目标玩家，拾取条件（归属==自己）才满足。
/// 注意：原版客户端拾取物品后发送的是包 151（ItemDestroy）或包 21（SyncItem），**不是**本包。
/// </summary>
public sealed record ItemOwnerPacket(int OwnerPlayerId, Vector2 Position) : INetworkPacket
{
    public PacketId Type => PacketId.ItemPickup;

    /// <summary>世界物品槽位索引（Main.item 下标）。</summary>
    public int ItemSlotIndex { get; init; }

    /// <summary>归属保留时长（tick；对齐原版 ReserveFor 默认 15）。</summary>
    public int TimeToKeepReservation { get; init; } = 15;

    /// <summary>抓取延迟玩家（255 = 无）。</summary>
    public byte GrabDelayPlayer { get; init; } = 255;

    /// <summary>抓取延迟（tick）。</summary>
    public int GrabDelayTime { get; init; }
}

/// <summary>
/// 物品移除包（ItemDestroy，包 151，上行）。
/// 布局：Int16 世界物品槽位。
/// 原版客户端拾取物品后发送本包通知服务端（全部拿走时）；服务端权威校验归属与距离后
///   结算入库并移除世界实体。部分拾取（背包空间不足）时客户端改发包 21（SyncItem 带新堆叠）。
/// </summary>
public sealed record ItemDestroyPacket(int ItemSlotIndex) : INetworkPacket
{
    public PacketId Type => PacketId.ItemDestroy;

    /// <summary>归属玩家（上行解码时填充；服务端以连接身份覆盖，防止代他人拾取）。</summary>
    public int PlayerId { get; init; }
}

/// <summary>
/// 抛射物生成 / 同步包（ProjectileNew，包 27，双向；原版 SyncProjectile）。
/// 布局：Int32 抛射物键（ProjectileKey）+ Vector2 位置 + Vector2 速度 + Int16 类型
///   + BitsByte 标志1（bit0=ai[0]、bit1=ai[1]、bit2=标志2存在、bit3=banner、bit4=damage、
///     bit5=knockBack、bit6=originalDamage）
///   + 可选 BitsByte 标志2（bit0=ai[2]）
///   + 依标志按序：ai[0](Single) / ai[1](Single) / banner(UInt16) / damage(Int16)
///     / knockBack(Single) / originalDamage(Int16) / ai[2](Single)。
/// 服务端权威下抛射物由 ProjectileAuthority 生成与模拟，客户端仅上报意图。
/// </summary>
public sealed record ProjectileNewPacket(int ProjectileKey, Vector2 Position, Vector2 Velocity, int ProjectileType) : INetworkPacket
{
    public PacketId Type => PacketId.ProjectileNew;

    /// <summary>AI 槽位 0（标志1 bit0 存在）。</summary>
    public float Ai0 { get; init; }

    /// <summary>AI 槽位 1（标志1 bit1 存在）。</summary>
    public float Ai1 { get; init; }

    /// <summary>AI 槽位 2（标志2 bit0 存在，位于 payload 末尾）。</summary>
    public float Ai2 { get; init; }

    /// <summary>待响应的旗帜 ID（标志1 bit3，线格式 UInt16）。</summary>
    public int BannerIdToRespondTo { get; init; }

    /// <summary>伤害值（标志1 bit4，线格式 Int16）。</summary>
    public int Damage { get; init; }

    /// <summary>击退力（标志1 bit5，线格式 Single）。</summary>
    public float Knockback { get; init; }

    /// <summary>原始伤害值（标志1 bit6，线格式 Int16）。</summary>
    public int OriginalDamage { get; init; }
}

/// <summary>
/// 箱子物品同步包（SyncChestItem，包 32）。
/// 布局：Int16 箱子索引 + Byte 槽位 + Int16 堆叠 + Byte 词缀 + Int16 物品类型。
/// </summary>
public sealed record SyncChestItemPacket(int ChestIndex, int ItemSlot, int Stack, byte Prefix, int ItemType) : INetworkPacket
{
    public PacketId Type => PacketId.SyncChestItem;
}

/// <summary>
/// 快速堆叠到附近箱子（QuickStackChests，包 85，客户端 → 服务端）。
/// 布局：Int32 槽位数 + 槽位 × Int16 + Boolean smartStack。
/// 客户端上报「作为来源的背包槽位列表」与 smartStack 标志，由服务端执行 QuickStack
/// （原版用玩家当前打开的箱子作为目标；payload 长度为 0 表示空列表）。
/// </summary>
public sealed record QuickStackChestsPacket(IReadOnlyList<int> Slots, bool SmartStack) : INetworkPacket
{
    public PacketId Type => PacketId.QuickStackChests;
}

/// <summary>
/// 玩家当前打开的箱子索引（SyncPlayerChestIndex，包 34，服务端 → 客户端）。
/// 布局：Byte 玩家 + Int16 箱子索引（-1 表示关闭）。
/// </summary>
public sealed record PlayerChestIndexPacket(byte PlayerId, short ChestIndex) : INetworkPacket
{
    public PacketId Type => PacketId.SyncPlayerChestIndex;
}

/// <summary>治疗 / 回血事件包（PlayerHeal，包 35）：Byte 玩家 + Int16 治疗量。</summary>
public sealed record PlayerHealPacket(int PlayerId, int Amount) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerHeal;
}

/// <summary>单格液体变更（NetLiquid 条目，6 字节）。</summary>
public readonly record struct LiquidChange(int X, int Y, byte Amount, byte Type);

/// <summary>
/// 液体同步帧（包 82 = LoadNetModule → 模块 0 NetLiquidModule）。
/// 布局：UInt16 模块号(0) + UInt16 条目数 + 条目 ×（Int16 X + Int16 Y + Byte 液体量 + Byte 液体类型）。
/// 服务端权威：服务端下发自身仿真的液体变化；客户端上报浇灌 / 清液作为意图，经权威校验后落盘。
/// </summary>
public sealed record LiquidModulePacket(IReadOnlyList<LiquidChange> Changes) : INetworkPacket
{
    public PacketId Type => PacketId.NetModule;

    /// <summary>true = 客户端上报（上行）；false = 服务端下发（下行）。</summary>
    public bool IsClientMessage { get; init; }
}

/// <summary>
/// 玩家生物群系 / 城镇状态包（SyncPlayerZone，包 36）。
/// 布局：Byte 玩家 + Byte zone1..zone5 + Byte 城镇 NPC 计数。
/// </summary>
public sealed record SyncPlayerZonePacket(
    byte PlayerId,
    byte Zone1,
    byte Zone2,
    byte Zone3,
    byte Zone4,
    byte Zone5,
    byte TownNpcs) : INetworkPacket
{
    public PacketId Type => PacketId.SyncPlayerZone;
}

/// <summary>
/// 玩家增益 / 减益包（PlayerBuffs，包 50）。
/// 布局：Byte 玩家 + UInt16 增益类型序列（以 0 结束）。
/// </summary>
public sealed record PlayerBuffsPacket(int PlayerId, IReadOnlyList<int> BuffTypes) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerBuffs;
}

/// <summary>NPC 身上单条增益 / 减益（类型 + 剩余时长）。</summary>
public readonly record struct NpcBuffEntry(int Type, int Time);

/// <summary>
/// 服务端下发某 NPC 的全量增益列表（NpcBuffSync，包 54，服务端 → 客户端）。
/// 布局：Int16 npcId + [UInt16 type, UInt16 time]… + UInt16 0 结束。
/// </summary>
public sealed record NpcBuffSyncPacket(int NpcId, IReadOnlyList<NpcBuffEntry> Buffs) : INetworkPacket
{
    public PacketId Type => PacketId.NpcBuffSync;
}

/// <summary>
/// 客户端上报「命中给 NPC 施加单条减益」（AddNpcBuff，包 53，客户端 → 服务端）。
/// 布局：Int16 npcId + UInt16 type + Int16 time。
/// 服务端权威校验后并入该 NPC 的增益列表。
/// </summary>
public sealed record AddNpcBuffPacket(int NpcId, int BuffType, int Time) : INetworkPacket
{
    public PacketId Type => PacketId.AddNpcBuff;
}

/// <summary>传送目标种类（TeleportEntity，包 65）：由线格式标志位 bit0 / bit1 组合得到。</summary>
public enum TeleportEntityKind : byte
{
    /// <summary>0：传送玩家。</summary>
    Player = 0,

    /// <summary>1：传送 NPC。</summary>
    Npc = 1,

    /// <summary>2：玩家间传送（服务端强制 num82 = whoAmI）。</summary>
    PlayerToPlayer = 2,

    /// <summary>3：玩家对服务端发起传送的确认。</summary>
    Acknowledge = 3,
}

/// <summary>
/// 传送实体包（TeleportEntity，包 65，双向）。
/// 布局：BitsByte 标志（bit0/bit1=种类、bit2=不带位置/由服务端取当前位置、bit3=携带额外 Int32）
///   + Int16 实体索引 + Vector2 位置 + Byte 样式 + 可选 Int32 额外信息（bit3）。
/// 服务端权威下客户端只能声明意图，落点合法性 / 频率由 MovementAuthority 判定。
/// </summary>
public sealed record TeleportEntityPacket(int EntityId, Vector2 Position, byte Style = 0) : INetworkPacket
{
    public PacketId Type => PacketId.TeleportEntity;

    /// <summary>传送目标种类（标志 bit0 / bit1）。</summary>
    public TeleportEntityKind Kind { get; init; }

    /// <summary>标志 bit2：不带位置，服务端以自身记录的位置覆盖线格式中的坐标。</summary>
    public bool NoPosition { get; init; }

    /// <summary>标志 bit3：携带的额外信息（线格式 Int32）。</summary>
    public int ExtraInfo { get; init; }
}

/// <summary>服务端传送请求种类（RequestTeleportationByServer，包 73）。</summary>
public enum TeleportRequestKind : byte
{
    /// <summary>0：传送药水。</summary>
    TeleportationPotion = 0,

    /// <summary>1：魔法海螺。</summary>
    MagicConch = 1,

    /// <summary>2：恶魔海螺。</summary>
    DemonConch = 2,

    /// <summary>3：贝壳电话（回城）。</summary>
    ShellphoneSpawn = 3,

    /// <summary>4：无空位玩家传送。</summary>
    PlayerNoSpaceTeleport = 4,
}

/// <summary>
/// 服务端传送请求包（RequestTeleportationByServer，包 73，上行）。
/// 布局：Byte 传送种类（0..4）。服务端决定落点，客户端仅声明使用何种道具。
/// </summary>
public sealed record RequestTeleportationByServerPacket(TeleportRequestKind Kind) : INetworkPacket
{
    public PacketId Type => PacketId.RequestTeleportationByServer;
}

/// <summary>
/// 死亡原因数据（对接原版 <c>Terraria.DataStructures.PlayerDeathReason</c> 的线格式）。
/// 8 位标志决定后续条件字段是否存在；缺失字段保持默认值（索引 -1、类型/词缀 0、文本 null）。
/// </summary>
public sealed record PlayerDeathReasonData
{
    /// <summary>来源玩家索引（标志 bit0）。-1 表示缺失。</summary>
    public int SourcePlayerIndex { get; init; } = -1;

    /// <summary>来源 NPC 索引（标志 bit1）。-1 表示缺失。</summary>
    public int SourceNpcIndex { get; init; } = -1;

    /// <summary>来源弹幕本地索引（标志 bit2）。-1 表示缺失。</summary>
    public int SourceProjectileLocalIndex { get; init; } = -1;

    /// <summary>其他来源类型（标志 bit3）。-1 表示缺失。</summary>
    public int SourceOtherIndex { get; init; } = -1;

    /// <summary>来源弹幕类型（标志 bit4）。0 表示缺失。</summary>
    public int SourceProjectileType { get; init; }

    /// <summary>来源物品类型（标志 bit5）。0 表示缺失。</summary>
    public int SourceItemType { get; init; }

    /// <summary>来源物品词缀（标志 bit6）。0 表示缺失。</summary>
    public int SourceItemPrefix { get; init; }

    /// <summary>自定义死亡文本（标志 bit7）。null 表示缺失。</summary>
    public string? CustomReason { get; init; }
}

/// <summary>
/// 玩家受击包（PlayerHurtV2，包 117）。
/// 布局：Byte PlayerId + PlayerDeathReason + Int16 Damage + Byte（击退方向+1）
///   + BitsByte（bit0=暴击、bit1=PvP）+ SByte 免伤冷却计数。
/// </summary>
public sealed record PlayerHurtV2Packet(int PlayerId, int Damage) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerHurtV2;

    /// <summary>受击来源（死亡原因）。</summary>
    public PlayerDeathReasonData DeathReason { get; init; } = new();

    /// <summary>击退方向（线格式为 方向+1，解码后还原为 -1 / 1）。</summary>
    public int HitDirection { get; init; }

    /// <summary>是否暴击（BitsByte bit0）。</summary>
    public bool Crit { get; init; }

    /// <summary>是否 PvP（BitsByte bit1）。</summary>
    public bool Pvp { get; init; }

    /// <summary>免伤冷却计数。</summary>
    public sbyte CooldownCounter { get; init; }
}

/// <summary>
/// 玩家死亡包（PlayerDeathV2，包 118）。
/// 布局：Byte PlayerId + PlayerDeathReason + Int16 Damage + Byte（击退方向+1）
///   + BitsByte（bit0=PvP）。
/// </summary>
public sealed record PlayerDeathV2Packet(int PlayerId, int Damage) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerDeathV2;

    /// <summary>死亡原因。</summary>
    public PlayerDeathReasonData DeathReason { get; init; } = new();

    /// <summary>击退方向（线格式为 方向+1，解码后还原为 -1 / 1）。</summary>
    public int HitDirection { get; init; }

    /// <summary>是否 PvP（BitsByte bit0）。</summary>
    public bool Pvp { get; init; }
}

/// <summary>
/// 打开箱子请求包（RequestChestOpen，包 31，上行）。
/// 布局：Int16 X + Int16 Y（目标箱子所在图格坐标）。
/// 服务端权威下由服务端判定该坐标是否存在箱子、是否已被占用。
/// </summary>
public sealed record ChestPacket(int X, int Y) : INetworkPacket
{
    public PacketId Type => PacketId.Chest;
}

/// <summary>
/// 背包槽位变更包（SyncEquipment，包 5）。
/// 布局：Byte PlayerId + Int16 Slot + Int16 Stack + Byte Prefix + Int16 Type + BitsByte。
/// </summary>
public sealed record InventorySlotPacket(int Slot, int ItemId, int Stack) : INetworkPacket
{
    public PacketId Type => PacketId.InventorySlot;

    /// <summary>所属玩家（上行解码时填充；下行编码时用于填充包首字节）。</summary>
    public int PlayerId { get; init; }

    /// <summary>词缀 ID。</summary>
    public byte Prefix { get; init; }

    /// <summary>是否收藏（BitsByte bit0）。</summary>
    public bool Favorited { get; init; }
}

/// <summary>
/// 弹幕销毁包（KillProjectile，包 29，双向）。
/// 布局权威：<c>NetMessage.SendData</c> case 29 / <c>MessageBuffer.GetData</c> case 29：
/// <c>Int32 弹幕键 + Vector2 位置</c>。服务端权威：仅弹幕归属者可销毁。
/// </summary>
public sealed record ProjectileDestroyPacket(int ProjectileKey, Vector2 Position) : INetworkPacket
{
    public PacketId Type => PacketId.ProjectileDestroy;
}

/// <summary>
/// 世界时间包（Time，包 18，服务端 → 客户端）。
/// 布局权威：<c>NetMessage.SendData</c> case 18 / <c>MessageBuffer.GetData</c> case 18：
/// <c>Byte dayTime + Int32 time + Int16 sunModY + Int16 moonModY</c>。
/// </summary>
public sealed record TimePacket(bool DayTime, int Time, short SunModY, short MoonModY) : INetworkPacket
{
    public PacketId Type => PacketId.Time;
}

/// <summary>
/// NPC 生成 / 更新包（SyncNPC，包 23，服务端 → 客户端）。
/// 布局权威：<c>NetMessage.SendData</c> case 23 / <c>MessageBuffer.GetData</c> case 23：
/// <c>Byte 索引 + Byte generation + Vector2 位置 + Vector2 速度 + UInt16 target
/// + BitsByte A + BitsByte B + [各 ai 单精度] + Int16 netID + [可选段]</c>。
/// <para>BitsByte A：bit0 朝向 &gt;0、bit1 竖直朝向 &gt;0、bit2..5 第 i 个 ai 是否存在、
/// bit6 spriteDirection &gt;0、bit7 生命是否为满（置位则**省略生命段**）。</para>
/// <para>BitsByte B：bit0 玩家数缩放、bit1 雕像生成、bit2 难度覆盖、bit3 需同步生成。</para>
/// </summary>
/// <remarks>
/// TerraAuth 只发「满血 + 无 ai + 非雕像 / 无难度覆盖 / 不可捕捉」的最小形态：
/// bitsA.bit7 恒置位以省略生命段，因此服务端无需维护 NPC 生命即可让客户端正确生成该 NPC。
/// </remarks>
public sealed record NpcUpdatePacket(
    byte Index,
    byte Generation,
    Vector2 Position,
    Vector2 Velocity,
    ushort Target,
    short NetId,
    int Life = 100,
    int LifeMax = 100,
    bool DirectionPositive = true,
    bool DirectionYPositive = true,
    bool SpriteDirectionPositive = true,
    /// <summary>
    /// 原版 <c>ai[0..3]</c>（长度 4 时下发：bitsA bit2..5 置位并写出 4 个 float）。
    /// 为 null 表示不下发 —— 原版客户端收到未置位的 ai 位会**把该 ai 显式置 0**，故两者不等价，
    /// 依赖 ai 的 aiStyle（如史莱姆的跳跃状态）必须下发。
    /// </summary>
    float[]? Ai = null) : INetworkPacket
{
    public PacketId Type => PacketId.NpcUpdate;
}

/// <summary>
/// 聊天包（LoadNetModule → NetTextModule，包 82）。
/// 模块号权威：<c>Terraria.Initializers.NetworkInitializer</c> 的注册顺序（NetLiquidModule=0，**NetTextModule=1**）。
/// <para>服务端 → 客户端负载：<c>UInt16 模块号 + Byte authorId + Byte 模式(0=Literal) + String 文本 + RGB</c>；</para>
/// <para>客户端 → 服务端负载：<c>UInt16 模块号 + String 命令名（普通说话为空串）+ String 文本</c>。</para>
/// </summary>
public sealed record NetTextPacket(string Text) : INetworkPacket
{
    public PacketId Type => PacketId.NetModule;

    /// <summary>true = 客户端上行形态；false = 服务端下行形态。</summary>
    public bool IsClientMessage { get; init; }

    /// <summary>上行：命令名（普通发言为空串）。</summary>
    public string CommandName { get; init; } = "";

    /// <summary>下行：作者玩家 Id（<see cref="byte.MaxValue"/> 表示服务端 / 系统消息）。</summary>
    public byte AuthorId { get; init; } = byte.MaxValue;

    /// <summary>下行：颜色。</summary>
    public RgbColor Color { get; init; } = new RgbColor(255, 255, 255);
}
