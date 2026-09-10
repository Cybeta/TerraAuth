// TerraAuth — 协议层：包类型标识
// PacketId 属于最底层协议契约，定义在 Protocol 项目，避免下层反向依赖 Net 层。

namespace TerraAuth.Protocol;

/// <summary>
/// 包类型标识。数值对应 Terraria 网络协议中的真实包号。
/// 权威来源：本地客户端 <c>本地客户端</c> 原版的
/// <c>Terraria.ID.MessageID</c>（Terraria 1.4.5.8 / Protocol 326）。
/// 注释中的 "原版名" 为该常量在 MessageID 中的真实名称；TerraAuth 侧保留语义化命名。
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
    ItemDrop            = 21,   // 原版 SyncItem
    ChatText            = 25,   // 原版 Unused25（已弃用，1.4 起聊天走 NetTextModule）
    ProjectileNew       = 27,   // 原版 SyncProjectile
    NpcStrike           = 28,   // 原版 DamageNPC
    Chest               = 31,   // 原版 RequestChestOpen
    SyncChestItem       = 32,   // 原版 SyncChestItem：箱子内物品同步
    PlayerHeal          = 35,   // 原版 PlayerHeal：治疗 / 回血事件
    SyncPlayerZone      = 36,   // 原版 SyncPlayerZone：生物群系 / 城镇 NPC 状态
    InitialSpawn        = 49,   // 原版 InitialSpawn：无 payload
    PlayerBuffs         = 50,   // 原版 PlayerBuffs：增益 / 减益列表
    TeleportEntity      = 65,   // 原版 TeleportEntity：玩家 / NPC / 玩家间传送（含确认）
    RequestTeleportationByServer = 73, // 原版 RequestTeleportationByServer：回城药水 / 海螺等
    TilePlace           = 79,   // 原版 PlaceObject
    PlayerHurtV2        = 117,  // 原版 PlayerHurtV2：玩家受击（含死亡原因）
    PlayerDeathV2       = 118,  // 原版 PlayerDeathV2：玩家死亡（含死亡原因）
    FinishedConnecting  = 129,  // 原版 FinishedConnectingToServer：无 payload
}
