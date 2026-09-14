// TerraAuth — Phase 5: 协议版本与包 ID 映射
// 对应 terraria-protocol 的 MessageId 枚举
// https://github.com/CreativeTools/terraria-protocol

using System.Collections.Immutable;
using TerraAuth.Protocol;

namespace TerraAuth.Net.Transport;

/// <summary>
/// Terraria 协议版本声明。
/// 每次 Terraria 更新客户端，只需修改本文件 + Phase 2 对应包处理。
/// </summary>
public sealed record ProtocolVersion(
    int Major,
    int Minor,
    int Patch,
    int ProtocolId)
{
    /// <summary>
    /// 当前目标版本：Terraria 1.4.5.8 → Protocol 326。
    /// 权威来源：原版客户端（协议 326）—— 握手中发送/校验的版本串为 <c>"Terraria" + 326</c>。
    /// </summary>
    public static readonly ProtocolVersion Current = new(1, 4, 5, 326);

    public string DisplayName => $"{Major}.{Minor}.{Patch}.{ProtocolId}";

    /// <summary>ConnectRequest 版本串："Terraria" + ProtocolId，例如 "Terraria326"。</summary>
    public string ConnectVersion => ConnectionRequestPacket.BuildVersion(ProtocolId);
}

/// <summary>
/// 协议能力声明（用于版本协商 / 特性开关）。
/// </summary>
[Flags]
public enum ProtocolCapabilities : uint
{
    None                = 0,
    ServerSideInventory = 1 << 0,   // SSC：服务端权威库存
    ServerSideCombat    = 1 << 1,   // 服务端权威战斗
    DeltaSnapshots      = 1 << 2,   // 增量快照（Phase 4）
    ShadowPrediction    = 1 << 3,   // 影子预测（反作弊）
}

/// <summary>
/// 协议元数据查询接口。
/// </summary>
public interface ITerrariaProtocol
{
    ProtocolVersion Version { get; }
    ProtocolCapabilities Capabilities { get; }

    /// <summary>包 ID → 包名（调试用）</summary>
    string GetPacketName(PacketId id);

    /// <summary>该包是否需要走权威管线（Phase 2）</summary>
    bool RequiresAuthority(PacketId id);

    /// <summary>该包是否允许在未认证状态发送</summary>
    bool AllowedBeforeAuth(PacketId id);
}

/// <summary>
/// 默认协议实现：声明当前版本与能力，包分类供权威管线/握手阶段使用。
/// 每次 Terraria 更新时在此维护版本与包清单。
/// </summary>
public sealed class TerrariaProtocol : ITerrariaProtocol
{
    public ProtocolVersion Version { get; }
    public ProtocolCapabilities Capabilities { get; }

    public TerrariaProtocol(
        ProtocolVersion? version = null,
        ProtocolCapabilities capabilities = ProtocolCapabilities.ServerSideInventory
            | ProtocolCapabilities.ServerSideCombat
            | ProtocolCapabilities.DeltaSnapshots
            | ProtocolCapabilities.ShadowPrediction)
    {
        Version = version ?? ProtocolVersion.Current;
        Capabilities = capabilities;
    }

    public string GetPacketName(PacketId id) => id.ToString();

    /// <summary>客户端可伪造、必须经权威管线校验的包。</summary>
    public bool RequiresAuthority(PacketId id) => id switch
    {
        PacketId.PlayerPosition or PacketId.NpcStrike or PacketId.ProjectileNew
            or PacketId.TileBreak or PacketId.TilePlace or PacketId.ItemDrop
            or PacketId.ItemPickup or PacketId.ItemDestroy
            or PacketId.Chest or PacketId.InventorySlot
            or PacketId.TeleportEntity or PacketId.RequestTeleportationByServer
            or PacketId.PlayerHeal or PacketId.PlayerMana or PacketId.PlayerBuffs => true,
        _ => false,
    };

    /// <summary>握手/认证阶段允许发送的包（对应原版 State 0→10 的 gating）。</summary>
    public bool AllowedBeforeAuth(PacketId id) => id switch
    {
        PacketId.ConnectionRequest or PacketId.PlayerInfo
            or PacketId.RequestWorldInfo or PacketId.TileGetSection
            or PacketId.PlayerSpawn or PacketId.Disconnect => true,
        _ => false,
    };
}
