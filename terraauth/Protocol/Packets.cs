// TerraAuth — Phase 5: 协议层包契约补充
// 定义 Connection / SyncPlayer / WorldData / Spawn 等握手与同步包
// 布局权威来源：本地客户端 本地客户端 原版（1.4.5.8 / Protocol 326）
//   - 出站：Terraria.NetMessage.SendData
//   - 入站：Terraria.MessageBuffer.GetData

namespace TerraAuth.Protocol;

/// <summary>客户端连接请求（ConnectRequest，包 1）：仅携带版本串。</summary>
public sealed record ConnectionRequestPacket(string Version) : INetworkPacket
{
    public PacketId Type => PacketId.ConnectionRequest;

    /// <summary>版本串格式："Terraria" + ProtocolId，例如 "Terraria326"。</summary>
    public static string BuildVersion(int protocolId) => $"Terraria{protocolId}";
}

/// <summary>
/// 服务端分配 PlayerId（PlayerInfo，包 3）：payload = Byte PlayerId + Boolean ServerSpecialFlag。
/// 原版：<c>writer.Write((byte)remoteClient); writer.Write(false);</c>
/// </summary>
public sealed record ContinueConnectingPacket(byte PlayerId, bool ServerSpecialFlag = false) : INetworkPacket
{
    public PacketId Type => PacketId.ContinueConnecting;
}

/// <summary>客户端请求世界信息（RequestWorldData，包 6）：无 payload。</summary>
public sealed record RequestWorldInfoPacket : INetworkPacket
{
    public PacketId Type => PacketId.RequestWorldInfo;
}

/// <summary>
/// 世界信息（WorldData，包 7）。
/// 前 14 个字段为构造参数（核心世界标识）；其余尾部字段为 init 属性，默认值构成可被原版客户端接受的最小合法包。
/// 字段顺序严格对应 <c>NetMessage.SendData</c> 的 case 7。
/// </summary>
public sealed record WorldInfoPacket(
    int Time,
    bool DayTime,
    bool BloodMoon,
    bool Eclipse,
    byte MoonPhase,
    short MaxTilesX,
    short MaxTilesY,
    short SpawnTileX,
    short SpawnTileY,
    short WorldSurface,
    short RockLayer,
    int WorldId,
    string WorldName,
    byte GameMode) : INetworkPacket
{
    public PacketId Type => PacketId.WorldInfo;

    // ---- 尾部字段（包 7 后半段） ----

    /// <summary>世界唯一 ID（Guid，16 字节）。</summary>
    public Guid UniqueId { get; init; } = Guid.Empty;

    /// <summary>世界生成器版本。</summary>
    public ulong WorldGeneratorVersion { get; init; }

    /// <summary>月相类型（moonType）。</summary>
    public byte MoonType { get; init; }

    /// <summary>13 个背景 ID：treeBG1-4、corrupt、jungle、snow、hallow、crimson、desert、ocean、mushroom、underworld。</summary>
    public byte[] Backgrounds { get; init; } = new byte[13];

    public byte IceBackStyle { get; init; }
    public byte JungleBackStyle { get; init; }
    public byte HellBackStyle { get; init; }

    /// <summary>目标风速。</summary>
    public float WindSpeedTarget { get; init; }

    /// <summary>云量。</summary>
    public byte NumClouds { get; init; }

    /// <summary>3 个树背景 X 坐标。</summary>
    public int[] TreeX { get; init; } = new int[3];

    /// <summary>4 个树样式。</summary>
    public byte[] TreeStyle { get; init; } = new byte[4];

    /// <summary>3 个洞穴背景 X 坐标。</summary>
    public int[] CaveBackX { get; init; } = new int[3];

    /// <summary>4 个洞穴背景样式。</summary>
    public byte[] CaveBackStyle { get; init; } = new byte[4];

    /// <summary>TreeTops 13 个区域变化值（WorldGen.TreeTops.SyncSend）。</summary>
    public byte[] TreeTops { get; init; } = new byte[13];

    /// <summary>最大降雨强度。</summary>
    public float MaxRaining { get; init; }

    /// <summary>11 个进度位（bitsByte4..14）：Boss 击杀、事件、世界种子等。</summary>
    public byte[] ProgressFlags { get; init; } = new byte[11];

    public byte SundialCooldown { get; init; }
    public byte MoondialCooldown { get; init; }

    /// <summary>7 个矿石层级：Copper/Iron/Silver/Gold/Cobalt/Mythril/Adamantite。</summary>
    public short[] OreTiers { get; init; } = new short[7];

    /// <summary>入侵类型（-1 表示无入侵）。</summary>
    public sbyte InvasionType { get; init; }

    /// <summary>社交网络大厅 ID（无社交时为 0）。</summary>
    public ulong LobbyId { get; init; }

    /// <summary>沙尘暴目标强度。</summary>
    public float SandstormIntensity { get; init; }

    /// <summary>额外出生点（ExtraSpawnPointManager.Write：Byte count + count×(Int16 X, Int16 Y)）。</summary>
    public (short X, short Y)[] ExtraSpawnPoints { get; init; } = System.Array.Empty<(short, short)>();

    public short DungeonX { get; init; }
    public short DungeonY { get; init; }
}

/// <summary>
/// 客户端发送玩家信息（SyncPlayer，包 4）。
/// 字段顺序严格对应 <c>MessageBuffer</c> case 4 / <c>NetMessage.SendData</c> case 4。
/// </summary>
public sealed record PlayerInfoPacket(byte Slot, string Name) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerInfo;

    // ---- 外观（包 4 剩余字段） ----

    public byte SkinVariant { get; init; }
    public byte VoiceVariant { get; init; }
    public float VoicePitchOffset { get; init; }
    public byte Hair { get; init; }
    public byte HairDye { get; init; }

    /// <summary>隐藏的可见饰品（WriteAccessoryVisibility：UInt16 位掩码）。</summary>
    public ushort HideVisibleAccessory { get; init; }

    public byte HideMisc { get; init; }

    public RgbColor HairColor { get; init; }
    public RgbColor SkinColor { get; init; }
    public RgbColor EyeColor { get; init; }
    public RgbColor ShirtColor { get; init; }
    public RgbColor UnderShirtColor { get; init; }
    public RgbColor PantsColor { get; init; }
    public RgbColor ShoeColor { get; init; }

    /// <summary>0=软核、1=中核、2=硬核、3=旅途。</summary>
    public byte Difficulty { get; init; }

    public bool ExtraAccessory { get; init; }

    /// <summary>BitsByte：生物火把 / 超级矿车等。</summary>
    public byte TorchFlags { get; init; }

    /// <summary>BitsByte：永久增益消耗品（AegisCrystal 等）。</summary>
    public byte UnlockFlags { get; init; }
}

/// <summary>
/// 客户端请求加载出生区块（SpawnTileData，包 8）：Int32 SpawnX + Int32 SpawnY + Byte Team。
/// </summary>
public sealed record SpawnTileDataPacket(int SpawnX, int SpawnY, byte Team) : INetworkPacket
{
    public PacketId Type => PacketId.TileGetSection;
}

/// <summary>
/// 状态文本（StatusTextSize，包 9）：Int32 StatusMax + NetworkText + BitsByte。
/// </summary>
public sealed record StatusTextPacket(int StatusMax, string StatusText, byte Flags = 0) : INetworkPacket
{
    public PacketId Type => PacketId.StatusText;
}

/// <summary>
/// 玩家出生（PlayerSpawn，包 12）：Byte PlayerId + Int16 SpawnX/Y + Int32 RespawnTimer
/// + Int16 DeathsPve/Pvp + Byte Team + Byte SpawnContext。
/// </summary>
public sealed record PlayerSpawnPacket(
    byte PlayerId,
    short SpawnX,
    short SpawnY,
    int RespawnTimer,
    short DeathsPve,
    short DeathsPvp,
    byte Team,
    byte SpawnContext) : INetworkPacket
{
    public PacketId Type => PacketId.PlayerSpawn;
}

/// <summary>初始出生（InitialSpawn，包 49）：无 payload。客户端据此完成进入世界。</summary>
public sealed record InitialSpawnPacket : INetworkPacket
{
    public PacketId Type => PacketId.InitialSpawn;
}

/// <summary>连接完成（FinishedConnectingToServer，包 129）：无 payload。</summary>
public sealed record FinishedConnectingPacket : INetworkPacket
{
    public PacketId Type => PacketId.FinishedConnecting;
}

/// <summary>断开 / 踢出（Kick，包 2）：可携带原因（NetworkText 字面量）。</summary>
public sealed record DisconnectPacket(string? Reason = null) : INetworkPacket
{
    public PacketId Type => PacketId.Disconnect;

    public static readonly DisconnectPacket Instance = new();
    public static DisconnectPacket WithReason(string reason) => new(reason);
}
