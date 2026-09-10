// TerraAuth — Mod 兼容层（需求 2：为 Mod 服务器支持预留接口）
// 支持原版 / TModLoader / 自定义 Mod 客户端，通过 ModPolicy 控制白/黑名单

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

/// <summary>Mod 策略配置（对应 config.json 的 ModPolicy 节）。</summary>
public sealed class ModPolicy
{
    public ModPolicyMode Mode { get; set; } = ModPolicyMode.VanillaOnly;
    /// <summary>遇到未列出的 Mod 是否阻止（Whitelist 模式下有效）。</summary>
    public bool BlockOnUnlistedMod { get; set; } = true;
    /// <summary>允许仅客户端 Mod（不影响服务端，默认可用）。</summary>
    public bool AllowClientSideMods { get; set; } = true;
    public IReadOnlyList<ModEntry> AllowedMods { get; set; } = Array.Empty<ModEntry>();
    public IReadOnlyList<ModEntry> BlockedMods { get; set; } = Array.Empty<ModEntry>();
    public IReadOnlyList<ModEntry> RequiredMods { get; set; } = Array.Empty<ModEntry>();
}

/// <summary>Mod 条目（版本区间 + 可选哈希校验）。</summary>
public sealed class ModEntry
{
    public string Name { get; set; } = "";
    public string? MinVersion { get; set; }
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
    private readonly IModDetector _detector;
    private readonly ILogger _logger;
    private readonly ICustomPacketHandler _packets;

    // TModLoader 自定义包 ID 区间（参考 tModLoader 协议）
    private const int PACKET_START = 250;
    private const int PACKET_END = 255;

    public TModLoaderCompat(IModDetector detector, ILogger logger, ICustomPacketHandler packets)
    {
        _detector = detector; _logger = logger; _packets = packets;
        _packets.RegisterPacketRange(PACKET_START, PACKET_END, "tModLoader");
    }

    /// <summary>处理 TModLoader 握手（解析 Mod 列表 + 策略校验）。</summary>
    public Task<HandshakeResult> HandleHandshakeAsync(ConnectionRequest request, byte[]? modListData)
    {
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

    /// <summary>转发 Mod 包给目标玩家。</summary>
    public Task ForwardModPacketAsync(int fromPlayerId, int toPlayerId, int packetId, byte[] data)
    {
        if (!_packets.IsPacketAllowed(packetId, "tModLoader"))
        {
            _logger.Warn("Blocked unauthorized mod packet {PacketId} from player {PlayerId}", packetId, fromPlayerId);
            return Task.CompletedTask;
        }
        // TODO: 通过 NetworkHost 发送给 toPlayerId
        return Task.CompletedTask;
    }

    private static IReadOnlyList<Plugins.LoadedMod> ParseModList(byte[] data)
    {
        // TODO: 解析 tModLoader ModNet 序列化格式
        return Array.Empty<Plugins.LoadedMod>();
    }

    public sealed class HandshakeResult
    {
        public bool Success { get; set; }
        public string RejectReason { get; set; } = "";
        public ClientCapabilities? Capabilities { get; set; }
    }
}
#endregion
