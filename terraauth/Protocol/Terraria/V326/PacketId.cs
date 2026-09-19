// TerraAuth — 协议层：包类型标识
// PacketId 属于最底层协议契约，定义在 Protocol 项目，避免下层反向依赖 Net 层。

namespace TerraAuth.Protocol;

/// <summary>
/// 包类型标识。数值对应 Terraria 网络协议中的真实包号。
/// 协议依据：原版客户端（Terraria 1.4.5.8 / Protocol 326）兼容的包号。
/// 注释中的 "原版名" 为该包号在原版协议常量中的名称；TerraAuth 侧保留语义化命名。
/// 仅收录握手与当前已实现管线所需的包；其余包按需补充。
/// </summary>
public enum PacketId : byte
{
    // ---- 连接 / 握手 ----
    ConnectionRequest   = 1,    // 原版 Hello：客户端版本串 "Terraria<ProtocolId>"
    Disconnect          = 2,    // 原版 Kick：断开 / 踢出（NetworkText 原因串）
    ContinueConnecting  = 3,    // 原版 PlayerInfo：服务端分配 PlayerId（Byte）+ ServerSpecialFlags（Boolean）
    PlayerInfo          = 4,    // 原版 SyncPlayer：玩家信息（外观 / 名称 / 难度等）
    InventorySlot       = 5,    // 原版 SyncEquipment
    RequestWorldInfo    = 6,    // 原版 RequestWorldData：无 payload
    WorldInfo           = 7,    // 原版 WorldData：完整世界元数据（见 PacketEncoder.WriteWorldInfo）
    TileGetSection      = 8,    // 原版 SpawnTileData：请求出生点区块（Int32 x / Int32 y / Byte）
    StatusText          = 9,    // 原版 StatusTextSize
    TileSendSection     = 10,   // 原版 TileSection
    TileFrameSection    = 11,   // 原版 TileFrameSection（已弃用）
    PlayerSpawn         = 12,   // 原版 PlayerSpawn：Byte playerId + Int16 spawnX/Y + Int32 respawnTimer + ...
    PlayerPosition      = 13,   // 原版 PlayerControls：移动 / 控制
    PlayerActive        = 14,   // 原版 PlayerActive
    Snapshot            = 15,   // TerraAuth 专用快照帧（原版 MessageBuffer case 15 为显式 no-op，客户端会忽略，安全复用）
    PlayerHealth        = 16,   // 原版 PlayerLifeMana
    TileBreak           = 17,   // 原版 TileManipulation：破块 / 改块
    Time                = 18,   // 原版 Time：世界时间（Byte dayTime + Int32 time + Int16 sunModY + Int16 moonModY）
    TileSquare          = 20,   // 原版 TileSquare：小矩形图格变更（服务端驱动的图格改动走这里）
    ItemDrop            = 21,   // 原版 SyncItem
    ItemPickup          = 22,   // 原版 SyncItemOwner：物品拾取（Int16 物品槽位 + Byte 归属玩家）
    NpcUpdate           = 23,   // 原版 SyncNPC：NPC 生成 / 更新（条件位 + 可选 ai / 生命段）
    ChatText            = 25,   // 原版 Unused25（已弃用，1.4 起聊天走 NetTextModule）
    ProjectileNew       = 27,   // 原版 SyncProjectile
    NpcStrike           = 28,   // 原版 DamageNPC（线格式 Int16 伤害）
    ProjectileDestroy   = 29,   // 原版 KillProjectile：弹幕销毁（Int32 弹幕键 + Vector2 位置）
    Chest               = 31,   // 原版 RequestChestOpen
    SyncChestItem       = 32,   // 原版 SyncChestItem：箱子内物品同步
    SyncPlayerChestIndex = 34,  // 原版 SyncPlayerChestIndex：告知玩家当前打开的箱子索引（Byte 玩家 + Int16 箱子）
    PlayerHeal          = 35,   // 原版 PlayerHeal：治疗 / 回血事件
    SyncPlayerZone      = 36,   // 原版 SyncPlayerZone：生物群系 / 城镇 NPC 状态
    SyncTalkNPC         = 40,   // 原版 SyncTalkNPC：玩家当前对话的城镇 NPC（Byte 玩家 + Int16 NPC 索引，-1 = 未对话）
    PlayerMana          = 42,   // 原版 PlayerMana：法力 / 法力上限
    InitialSpawn        = 49,   // 原版 InitialSpawn：无 payload
    PlayerBuffs         = 50,   // 原版 PlayerBuffs：增益 / 减益列表
    AddNpcBuff          = 53,   // 原版 AddNPCBuff：客户端向服务端上报「命中给 NPC 施加单条减益」（Int16 npcId + UInt16 type + Int16 time）
    NpcBuffSync         = 54,   // 原版 UpdateNPCBuff：服务端下发某 NPC 的**全量**增益列表（Int16 npcId + [UInt16 type, UInt16 time]… + UInt16 0）
    TeleportEntity      = 65,   // 原版 TeleportEntity：玩家 / NPC / 玩家间传送（含确认）
    RequestTeleportationByServer = 73, // 原版 RequestTeleportationByServer：回城药水 / 海螺等
    TilePlace           = 79,   // 原版 PlaceObject
    NetModule           = 82,   // 原版 LoadNetModule：模块帧（UInt16 moduleId + 模块负载；聊天走 NetTextModule）
    QuickStackChests    = 85,   // 原版 QuickStackChests：客户端上报要快速堆叠的背包槽位 + smartStack
    PlayerHurtV2        = 117,  // 原版 PlayerHurtV2：玩家受击（含死亡原因）
    PlayerDeathV2       = 118,  // 原版 PlayerDeathV2：玩家死亡（含死亡原因）
    FinishedConnecting  = 129,  // 原版 FinishedConnectingToServer：无 payload
    ItemDestroy         = 151,  // 原版 ItemDestroy：客户端拾取 / 移除物品通知（Int16 世界物品槽位）
    NpcDamageAck        = 162,  // 原版 NPC 命中确认（无 payload）：客户端收到即 NPC.AckDamage() 出队一条待确认伤害
}
