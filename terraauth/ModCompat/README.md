# Mod 兼容指南

## 概述

`ModCompat` 提供客户端能力检测、Mod 策略校验、TModLoader 握手与自定义包转发，
让 TerraAuth 在不修改原版客户端的前提下，支持多种客户端接入。

## 客户端类型

| 类型 | 检测方式 |
|------|---------|
| `Vanilla` | 标准协议版本，无 Mod 标识 |
| `TModLoader` | ClientVersion 含 "tModLoader" |
| `CustomModded` | 自定义标识 / 携带 Mod 列表 |

检测结果封装为 `ClientCapabilities`：

```csharp
public class ClientCapabilities
{
    public ClientType Type { get; set; }
    public string Version { get; set; } = "";
    public int ProtocolVersion { get; set; }
    public List<LoadedMod> Mods { get; set; } = new();
    public bool SupportsDirectSnapshots { get; set; }
    public bool SupportsShadowPrediction { get; set; }
}
```

## 策略配置

`ModPolicy` 支持以下 JSON 结构（`AllowedMods` / `BlockedMods` / `RequiredMods`）：

```json
{
  "ModPolicy": {
    "Mode": "Whitelist",
    "BlockOnUnlistedMod": true,
    "AllowClientSideMods": true,
    "AllowedMods": [
      { "Name": "MagicStorage", "MinVersion": "1.5.0" },
      { "Name": "RecipeBrowser" },
      { "Name": "BossChecklist", "MinVersion": "2.0.0" }
    ],
    "BlockedMods": [
      { "Name": "CheatMod" }
    ],
    "RequiredMods": [
      { "Name": "ServerSideMod", "MinVersion": "1.0.0" }
    ]
  }
}
```

模式说明：

| 模式 | 行为 |
|------|------|
| `VanillaOnly` | 仅允许原版客户端 |
| `Whitelist` | 仅放行白名单中的 Mod |
| `Blacklist` | 仅封禁黑名单中的 Mod |
| `AllowAll` | 放行所有，仅记录 |

> ✅ `GameHost.Bootstrap` 会从 `server.json` 的 `ModPolicy` 节读取策略（未配置时为默认 `VanillaOnly`）；
> `Mode` 支持字符串枚举写法（`"Whitelist"` 等）。启用白 / 黑名单无需改代码，改配置即可。

## 服务端接入

`GameHost.Bootstrap()` 已完成以下装配（见 `GameHost.cs`）：

```csharp
// ModPolicy 取自 server.json（未配置 → 默认 VanillaOnly = 仅允许原版客户端）
var modPolicy = config.Current.ModPolicy;
var modDetector = new ModDetector(modPolicy, logger);
var customPackets = new CustomPacketHandler(logger);

// TModLoader 兼容层：Mod 列表解析 + 自定义包转发（转发通道绑定网络层单播发送）
var tmodLoader = new TModLoaderCompat(
    modDetector, logger, customPackets,
    forward: (_, toPlayerId, packetId, data) =>
        network.SendRawAsync(toPlayerId, (PacketId)packetId, data));
```

`GameHost` 通过 `ModDetector` / `CustomPackets` / `TModLoader` 暴露上述组件，供插件与运维查询。

在连接处理中调用：

```csharp
var capabilities = modDetector.Detect(connectionRequest);
var result = modDetector.Validate(capabilities);
if (!result.IsAllowed)
{
    // 拒绝连接，返回 result.RejectReason
}
```

## TModLoader 握手

```
Client → Server : ConnectionRequest (tModLoader 标识)
Server → Client : ModListRequest
Client → Server : ModListResponse
Server          : Validate → Accept / Deny
```

自定义包区间（默认 250-255）：

```csharp
customPackets.RegisterPacketRange(250, 255, "tModLoader");
```

## 插件中获取客户端信息

```csharp
public class MyPlugin : PluginBase
{
    protected override Task OnInitializeAsync()
    {
        On<PlayerConnectingArgs>(OnConnecting);
        return Task.CompletedTask;
    }

    private HookResult OnConnecting(PlayerConnectingArgs args)
    {
        if (args.Capabilities.Type == ClientType.TModLoader)
        {
            Logger.Info("TModLoader 客户端: {Mods}",
                string.Join(", ", args.Capabilities.Mods.Select(m => m.Name)));
        }
        return HookResult.Allow();
    }
}
```

## 常见问题

**Q: 原版客户端能连吗？**
A: 取决于 `Mode`。`VanillaOnly` 仅原版；`Whitelist` 下原版默认可连（除非显式禁止）。

**Q: 客户端 Mod 会触发误判吗？**
A: 不会。反作弊基于服务端权威。但修改移动速度等的 Mod 可能需要加入白名单或调整阈值。

**Q: 如何调试 Mod 兼容问题？**
A: 查看日志中的 `Detected client:` 与 `Mod validation` 记录。
