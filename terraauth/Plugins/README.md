# 插件开发指南

> 为后续插件编写预留的 Hook 与稳定 API。完整类型定义见 `IPlugin.cs`、`HookArgs.cs`、`IPluginContext.cs`。

## 一、快速开始

> 可直接参考可运行示例工程 **`Examples/TerraAuth.ExamplePlugins/`**（`WelcomePlugin` 演示生命周期 Hook，`AntiCheatLitePlugin` 演示 Deny 短路 + 指标上报）。

### 1. 创建插件项目

```bash
dotnet new classlib -n MyPlugin
cd MyPlugin
dotnet add reference ../TerraAuth.csproj
```

### 2. 编写插件

```csharp
using System;
using System.Threading.Tasks;
using TerraAuth.Plugins;

namespace MyPlugin;

public class WelcomePlugin : PluginBase
{
    public override string Id => "welcome";
    public override string Name => "Welcome Plugin";
    public override Version Version => new(1, 0, 0);
    public override string Author => "YourName";
    public override string Description => "欢迎消息 + 经济示例";

    protected override async Task OnInitializeAsync()
    {
        // 注册 Hook（玩家加入时发送欢迎）
        On<PlayerJoinedArgs>(OnPlayerJoined);
        On<NpcStrikeArgs>(OnNpcKill); // NPC 击杀奖励
    }

    private HookResult OnPlayerJoined(PlayerJoinedArgs args)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            SendToPlayer(args.PlayerId, "Welcome!", "Green");
            Broadcast($"{args.PlayerName} joined!", "Yellow");
        });
        return HookResult.Allow();
    }

    private HookResult OnNpcKill(NpcStrikeArgs args)
    {
        // 击杀奖励逻辑（对接经济系统）
        return HookResult.Allow();
    }
}
```

### 3. 部署

```bash
dotnet build -c Release
cp bin/Release/net10.0/MyPlugin.dll <server>/plugins/
```

服务端启动时自动加载（详见 `GameHost.Bootstrap`）。

---

## 二、Hook 列表（对接 Phase 2 六种权威子系统）

| Hook | 触发阶段 | 可取消 | 可修改 |
|------|---------|--------|--------|
| `PlayerConnectingArgs` | ConnectionState 之后 | ✅ | ❌ |
| `PlayerJoinedArgs` | PlayerAuthority | ❌ | ❌ |
| `PlayerLeftArgs` | — | ❌ | ❌ |
| `PlayerMovingArgs` | MovementAuthority | ✅ | ✅ |
| `PlayerStatChangedArgs` | PlayerAuthority | ✅ | ✅ |
| `PlayerDiedArgs` | — | ❌ | ❌ |
| `ItemPickupArgs` | InventoryAuthority | ✅ | ❌ |
| `ItemDropArgs` | InventoryAuthority | ✅ | ❌ |
| `NpcStrikeArgs` | CombatAuthority | ✅ | ✅ |
| `ProjectileSpawnArgs` | CombatAuthority | ✅ | ✅ |
| `TilePlaceArgs` | WorldAuthority | ✅ | ❌ |
| `TileBreakArgs` | WorldAuthority | ✅ | ❌ |
| `NpcSpawnArgs` | WorldAuthority | ✅ | ❌ |
| `ServerStartedArgs` | — | ❌ | ❌ |
| `ServerStoppingArgs` | — | ❌ | ❌ |
| `ServerTickArgs` | GameLoop 每帧 | ❌ | ❌ |
| `CommandExecutingArgs` | — | ✅ | ❌ |

### Hook 返回值

```csharp
HookResult.Allow()              // 继续
HookResult.Deny("reason")       // 阻止（短路，后续插件不执行）
HookResult.Modify(changedArgs)  // 修改后继续（传递新数据）
```

### 优先级

```csharp
public override int Priority => 50; // 数字越小越先执行
```

---

## 三、插件 API

```csharp
// 消息
Broadcast("server msg", "Red");
SendToPlayer(playerId, "private", "Blue");

// 玩家
var player = GetPlayer(playerId);
var online = Context.Server.GetOnlinePlayers();

// 命令
Context.Server.ExecuteCommand("kick PlayerName");

// 配置（config.json 中加自己的节）
var threshold = Config.Get<int>("MyPlugin:Threshold", 100);

// 指标
Metrics.Counter("my_actions_total", "Total actions");
Metrics.Histogram("my_duration", 12.5);
```

---

## 四、示例插件

### 反作弊增强（行为分析）

```csharp
public class BehaviorAntiCheat : PluginBase
{
    public override string Id => "behavior_ac";
    public override Version Version => new(1, 0, 0);
    private readonly Dictionary<int, Profile> _profiles = new();

    protected override async Task OnInitializeAsync()
    {
        On<PlayerMovingArgs>(OnMove);
        On<NpcStrikeArgs>(OnStrike);
    }

    private HookResult OnMove(PlayerMovingArgs args)
    {
        var p = GetOrCreate(args.PlayerId);
        var dist = Math.Sqrt((args.ToX - args.FromX) * (args.ToX - args.FromX) +
                              (args.ToY - args.FromY) * (args.ToY - args.FromY));
        p.Samples.Add(dist);
        if (p.Samples.Count > 100)
        {
            var variance = p.Samples.Select(s => (s - p.Samples.Average()) * (s - p.Samples.Average())).Sum() / p.Samples.Count;
            if (variance < 0.01) // 几乎无变化 = 可能脚本
            {
                Logger.Warn("Suspicious movement: player {0}", args.PlayerId);
                Metrics.Counter("suspicious_movement", "Suspicious");
            }
        }
        return HookResult.Allow();
    }

    private HookResult OnStrike(NpcStrikeArgs args)
    {
        var p = GetOrCreate(args.PlayerId);
        p.TotalDamage += args.Damage;
        var dps = p.TotalDamage / Math.Max(1, (DateTimeOffset.UtcNow - p.Start).TotalSeconds);
        if (dps > 10000) return HookResult.Deny("Suspicious DPS");
        return HookResult.Allow();
    }

    private Profile GetOrCreate(int id)
    {
        if (!_profiles.TryGetValue(id, out var p))
            _profiles[id] = p = new Profile { Start = DateTimeOffset.UtcNow };
        return p;
    }

    private sealed class Profile
    {
        public DateTimeOffset Start;
        public List<double> Samples = new();
        public int TotalDamage;
    }
}
```

---

## 五、生命周期

```
Assembly.LoadFrom → Activator.CreateInstance → InitializeAsync (注册 Hook)
  → StartAsync (启动后台任务)
  → ... 处理 Hook ...
  → StopAsync → ShutdownAsync → Hooks.UnregisterPlugin
```

---

## 六、最佳实践

1. **Hook 中不要做耗时操作**（同步执行，会阻塞管线）→ 用 `Task.Run`
2. **异常隔离**：插件异常不会崩溃服务端，但会记录日志
3. **用配置**：硬编码值写在 config，不用魔法数字
4. **加指标**：行为类插件务必上报 `Metrics`，便于运营监控

---

## 七、接入点说明（供维护者）

`HookIntegration.cs` 的 `HookedPipeline` 用**装饰器模式**包装 `IInboundPipeline`，在权威校验前后触发 Hook。
新增包类型时，在 `BuildHookArgs` 的 `switch` 中注册映射即可，**无需修改 Phase 2 内部代码**。

接线现状（`GameHost.Bootstrap`）：
- `NetworkHost` 收到的是 `HookedPipeline`（而非原始 `IInboundPipeline`），并注入 `IHookRegistry` —— 真实网络包会先过插件 Hook；
- 生命周期 Hook 触发点：`ServerStartedArgs`（插件启动后）、`PlayerJoinedArgs`（进服 Playing 时）、`PlayerLeftArgs`（连接关闭，含 `SessionDuration`）。

> 注意：`HookRegistry.Trigger` 按**运行时类型**查找订阅者，因此以基类静态类型 `HookArgs` 传入也能命中具体注册项。

> 这是需求 1 的核心设计：核心层零修改，插件通过稳定 Hook 接口扩展。
