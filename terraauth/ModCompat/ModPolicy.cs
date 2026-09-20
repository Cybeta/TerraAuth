// TerraAuth — Mod 兼容层（需求 2：为 Mod 服务器支持预留接口）
// 未来 MOD 兼容层策略；当前生产路径由 TModLoaderCompat(enabled: false) 禁用

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TerraAuth.Plugins; // ClientCapabilities / ILogger / LoadedMod

namespace TerraAuth.ModCompat;

#region 策略配置
/// <summary>Mod 策略模式。</summary>
public enum ModPolicyMode
{
    /// <summary>仅允许原版客户端。</summary>
    VanillaOnly,
    /// <summary>白名单：只允许列出的 Mod。</summary>
    Whitelist,
    /// <summary>黑名单：禁止列出的 Mod。</summary>
    Blacklist,
    /// <summary>允许所有（仅记录）。</summary>
    AllowAll
}

/// <summary>Mod 策略配置（对应 server.json 的 ModPolicy 节）。</summary>
public sealed class ModPolicy
{
    /// <summary>策略模式：VanillaOnly = 仅原版客户端；Whitelist = 只允许 AllowedMods 列出的 Mod；
    /// Blacklist = 禁止 BlockedMods 列出的 Mod；AllowAll = 全部允许（仅记录日志）。</summary>
    public ModPolicyMode Mode { get; set; } = ModPolicyMode.VanillaOnly;
    /// <summary>遇到未列出的 Mod 是否阻止（Whitelist 模式下有效）。</summary>
    public bool BlockOnUnlistedMod { get; set; } = true;
    /// <summary>允许仅客户端 Mod（不影响服务端，默认可用）。</summary>
    public bool AllowClientSideMods { get; set; } = true;
    /// <summary>白名单条目（Whitelist 模式下只有列出的 Mod 可进服）。</summary>
    public IReadOnlyList<ModEntry> AllowedMods { get; set; } = Array.Empty<ModEntry>();
    /// <summary>黑名单条目（Blacklist 模式下列出的 Mod 一律拒绝）。</summary>
    public IReadOnlyList<ModEntry> BlockedMods { get; set; } = Array.Empty<ModEntry>();
    /// <summary>强制要求的 Mod（客户端未安装即拒绝连接）。</summary>
    public IReadOnlyList<ModEntry> RequiredMods { get; set; } = Array.Empty<ModEntry>();
}

/// <summary>Mod 条目（版本区间 + 可选哈希校验）。</summary>
public sealed class ModEntry
{
    /// <summary>Mod 名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>允许的最低版本（含）；null = 不限。</summary>
    public string? MinVersion { get; set; }
    /// <summary>允许的最高版本（含）；null = 不限。</summary>
    public string? MaxVersion { get; set; }
    /// <summary>Mod 文件哈希（用于完整性校验，可选）。</summary>
    public string? Hash { get; set; }
}

/// <summary>Mod 校验结果。</summary>
public sealed class ModValidationResult
{
    public bool IsAllowed { get; set; } = true;
    public string? RejectReason { get; set; }
    public IReadOnlyList<Plugins.LoadedMod> AllowedMods { get; set; } = Array.Empty<Plugins.LoadedMod>();
    public IReadOnlyList<Plugins.LoadedMod> BlockedMods { get; set; } = Array.Empty<Plugins.LoadedMod>();
}
#endregion

#region 检测器接口
/// <summary>Mod 检测器：从连接请求识别客户端类型 + 校验 Mod 策略。</summary>
public interface IModDetector
{
    /// <summary>从连接请求检测客户端能力。</summary>
    ClientCapabilities Detect(ConnectionRequest request);
    /// <summary>校验 Mod 列表是否符合策略。</summary>
    ModValidationResult Validate(ClientCapabilities capabilities);
    /// <summary>请求客户端上报 Mod 列表（TModLoader 支持）。</summary>
    Task<IReadOnlyList<Plugins.LoadedMod>> RequestModListAsync(object connection);
}

/// <summary>默认检测器实现。</summary>
public sealed class ModDetector : IModDetector
{
    private readonly ModPolicy _policy;
    private readonly ILogger _logger;

    public ModDetector(ModPolicy policy, ILogger logger) { _policy = policy; _logger = logger; }

    public ClientCapabilities Detect(ConnectionRequest request)
    {
        var caps = new ClientCapabilities { ProtocolVersion = request.ProtocolVersion };
        var v = request.ClientVersion ?? "";

        if (v.Contains("tModLoader", StringComparison.OrdinalIgnoreCase) || v.Contains("TML", StringComparison.OrdinalIgnoreCase))
        {
            caps.Type = ClientType.TModLoader;
            caps.Version = ExtractVersion(v);
            caps.SupportsDirectSnapshots = request.Extensions?.Contains("direct_snapshots") == true;
            caps.SupportsShadowPrediction = request.Extensions?.Contains("shadow_prediction") == true;
        }
        else if (request.Extensions?.Contains("modded") == true)
        {
            caps.Type = ClientType.CustomModded;
            caps.Version = v;
        }
        else
        {
            caps.Type = ClientType.Vanilla;
            caps.Version = v;
        }

        _logger.Debug("Detected client: {Type} v{Version} (Protocol={Protocol})",
            caps.Type, caps.Version, caps.ProtocolVersion);
        return caps;
    }

    public ModValidationResult Validate(ClientCapabilities caps)
    {
        var result = new ModValidationResult { IsAllowed = true, AllowedMods = caps.Mods };

        switch (_policy.Mode)
        {
            case ModPolicyMode.VanillaOnly:
                if (caps.Type != ClientType.Vanilla)
                {
                    result.IsAllowed = false;
                    result.RejectReason = "This server only allows vanilla clients.";
                }
                break;

            case ModPolicyMode.Whitelist:
                var allowed = new List<Plugins.LoadedMod>();
                foreach (var mod in caps.Mods)
                {
                    var entry = _policy.AllowedMods.FirstOrDefault(a =>
                        a.Name == mod.Name &&
                        (a.MinVersion == null || CompareVersion(mod.Version, a.MinVersion) >= 0) &&
                        (a.MaxVersion == null || CompareVersion(mod.Version, a.MaxVersion) <= 0));
                    if (entry != null) allowed.Add(mod);
                    else result.BlockedMods = result.BlockedMods.Append(mod).ToList();
                }
                result.AllowedMods = allowed;
                if (_policy.BlockOnUnlistedMod && result.BlockedMods.Count > 0)
                {
                    result.IsAllowed = false;
                    result.RejectReason = $"Mod(s) not whitelisted: {string.Join(", ", result.BlockedMods.Select(m => m.Name))}";
                }
                break;

            case ModPolicyMode.Blacklist:
                var blocked = new List<Plugins.LoadedMod>();
                foreach (var mod in caps.Mods)
                {
                    if (_policy.BlockedMods.Any(b => b.Name == mod.Name)) blocked.Add(mod);
                }
                result.BlockedMods = blocked;
                if (blocked.Count > 0)
                {
                    result.IsAllowed = false;
                    result.RejectReason = $"Mod(s) blacklisted: {string.Join(", ", blocked.Select(m => m.Name))}";
                }
                break;

            case ModPolicyMode.AllowAll:
                _logger.Info("Modded client allowed (AllowAll mode): {Count} mods", caps.Mods.Count);
                break;
        }

        // 校验强制要求的 Mod
        foreach (var req in _policy.RequiredMods)
        {
            if (!caps.Mods.Any(m => m.Name == req.Name))
            {
                result.IsAllowed = false;
                result.RejectReason = $"Required mod missing: {req.Name}";
                break;
            }
        }

        return result;
    }

    public Task<IReadOnlyList<Plugins.LoadedMod>> RequestModListAsync(object connection)
    {
        // TODO: 通过自定义包向客户端请求 Mod 列表（TModLoader 协议支持）
        return Task.FromResult<IReadOnlyList<Plugins.LoadedMod>>(Array.Empty<Plugins.LoadedMod>());
    }

    private static string ExtractVersion(string clientVersion)
    {
        var parts = clientVersion.Split(' ');
        return parts.Length > 1 ? parts[1] : clientVersion;
    }

    private static int CompareVersion(string a, string b)
    {
        var va = ParseVersion(a);
        var vb = ParseVersion(b);
        for (int i = 0; i < Math.Max(va.Length, vb.Length); i++)
        {
            var x = i < va.Length ? va[i] : 0;
            var y = i < vb.Length ? vb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] ParseVersion(string v)
    {
        return (v ?? "").Split('.').Select(p =>
        {
            int n; int.TryParse(p, out n); return n;
        }).ToArray();
    }
}

/// <summary>连接请求（握手数据，由网络层填充）。</summary>
public sealed class ConnectionRequest
{
    public string? ClientVersion { get; set; }
    public int ProtocolVersion { get; set; }
    public IReadOnlyList<string>? Extensions { get; set; }
}
#endregion

#region 自定义包处理（TModLoader 兼容）
/// <summary>自定义包处理器：管理 Mod 自定义网络包的 ID 区间注册与转发。</summary>
public interface ICustomPacketHandler
{
    /// <summary>处理自定义包，返回是否允许通过。</summary>
    Task<bool> HandleAsync(int playerId, int packetId, byte[] data);
    /// <summary>注册某 Mod 的包 ID 区间。</summary>
    void RegisterPacketRange(int startId, int endId, string modName);
    /// <summary>检查某 Mod 是否允许使用某包 ID。</summary>
    bool IsPacketAllowed(int packetId, string modName);
}

/// <summary>自定义包处理器默认实现。</summary>
public sealed class CustomPacketHandler : ICustomPacketHandler
{
    // 包 ID → 所属 Mod
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _owners = new();
    private readonly ILogger _logger;

    public CustomPacketHandler(ILogger logger) => _logger = logger;

    public Task<bool> HandleAsync(int playerId, int packetId, byte[] data)
    {
        if (!_owners.ContainsKey(packetId))
        {
            _logger.Warn("Unknown custom packet {PacketId} from player {PlayerId}", packetId, playerId);
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public void RegisterPacketRange(int startId, int endId, string modName)
    {
        for (int i = startId; i <= endId; i++) _owners[i] = modName;
        _logger.Info("Registered packet range [{Start}-{End}] for mod '{Mod}'", startId, endId, modName);
    }

    public bool IsPacketAllowed(int packetId, string modName)
        => _owners.TryGetValue(packetId, out var owner) && owner == modName;
}

/// <summary>TModLoader 兼容层：握手 + Mod 列表解析 + 包转发。</summary>
public sealed class TModLoaderCompat
{
    private readonly bool _enabled;
    private readonly IModDetector _detector;
    private readonly ILogger _logger;
    private readonly ICustomPacketHandler _packets;

    /// <summary>自定义包转发回调（由组合根注入 NetworkHost 的单播发送）；null 表示未装配发送通道。</summary>
    private readonly Func<int, int, int, byte[], Task>? _forward;

    // TModLoader 自定义包 ID 区间（ModPacket 使用 250 号消息 ID）
    private const int PACKET_START = 250;
    private const int PACKET_END = 255;

    public TModLoaderCompat(
        IModDetector detector,
        ILogger logger,
        ICustomPacketHandler packets,
        Func<int, int, int, byte[], Task>? forward = null,
        bool enabled = false)
    {
        _enabled = enabled;
        _detector = detector; _logger = logger; _packets = packets; _forward = forward;
        if (_enabled)
            _packets.RegisterPacketRange(PACKET_START, PACKET_END, "tModLoader");
    }

    /// <summary>处理 TModLoader 握手（解析 Mod 列表 + 策略校验）。</summary>
    public Task<HandshakeResult> HandleHandshakeAsync(ConnectionRequest request, byte[]? modListData)
    {
        if (!_enabled)
            return Task.FromResult(new HandshakeResult
            {
                Success = false,
                RejectReason = "mod_support_disabled",
            });

        var caps = _detector.Detect(request);
        if (modListData != null && caps.Type == ClientType.TModLoader)
        {
            caps.Mods = ParseModList(modListData);
        }

        var validation = _detector.Validate(caps);
        if (!validation.IsAllowed)
        {
            return Task.FromResult(new HandshakeResult { Success = false, RejectReason = validation.RejectReason! });
        }
        return Task.FromResult(new HandshakeResult { Success = true, Capabilities = caps });
    }

    /// <summary>转发 Mod 包给目标玩家（仅放行已注册区间，其余丢弃并告警）。</summary>
    public async Task ForwardModPacketAsync(int fromPlayerId, int toPlayerId, int packetId, byte[] data)
    {
        if (!_enabled)
        {
            _logger.Warn("Blocked Mod packet {PacketId} while Mod support is disabled", packetId);
            return;
        }

        if (!_packets.IsPacketAllowed(packetId, "tModLoader"))
        {
            _logger.Warn("Blocked unauthorized mod packet {PacketId} from player {PlayerId}", packetId, fromPlayerId);
            return;
        }

        if (_forward is null)
        {
            _logger.Debug("Mod packet {PacketId} accepted but no forward channel configured", packetId);
            return;
        }

        await _forward(fromPlayerId, toPlayerId, packetId, data).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析 Mod 列表载荷：Int32 数量 + 数量 ×（7-bit 长度前缀 + UTF-8 名称）。
    /// 该布局对应 ModPacket 中「Mod 名称清单」段；载荷不含版本 / 哈希，
    /// 故解析出的 Mod 版本为空串 —— 白名单中带 MinVersion/MaxVersion 的条目无法据此校验。
    /// 载荷截断 / 数量异常时返回已解析部分（不抛异常，避免握手崩溃）。
    /// </summary>
    internal static IReadOnlyList<Plugins.LoadedMod> ParseModList(byte[] data)
    {
        var mods = new List<Plugins.LoadedMod>();
        if (data is null || data.Length < sizeof(int)) return mods;

        using var ms = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(ms, System.Text.Encoding.UTF8);
        try
        {
            int count = reader.ReadInt32();
            if (count <= 0 || count > 4096) return Array.Empty<Plugins.LoadedMod>();

            for (int i = 0; i < count; i++)
            {
                var name = ReadPrefixedString(reader);
                if (string.IsNullOrWhiteSpace(name)) continue;
                mods.Add(new Plugins.LoadedMod(name, Version: "", Author: "", IsClientSideOnly: false, RequiresServerSide: false));
            }
        }
        catch (EndOfStreamException)
        {
            // 载荷截断：保留已解析部分
        }
        return mods;
    }

    /// <summary>读取 7-bit 变长长度前缀 + UTF-8 字符串（与 .NET BinaryReader.ReadString 线格式一致）。</summary>
    private static string ReadPrefixedString(BinaryReader reader)
    {
        int len = 0, shift = 0;
        while (true)
        {
            byte b = reader.ReadByte();
            len |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 28) throw new FormatException("非法 7-bit 长度前缀");
        }
        var bytes = reader.ReadBytes(len);
        if (bytes.Length < len) throw new EndOfStreamException("Mod 名称载荷截断");
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public sealed class HandshakeResult
    {
        public bool Success { get; set; }
        public string RejectReason { get; set; } = "";
        public ClientCapabilities? Capabilities { get; set; }
    }
}
#endregion
