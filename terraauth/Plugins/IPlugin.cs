// TerraAuth — 插件系统：核心接口
// 为后续插件编写预留 Hook 点（需求 1）
// 对应架构文档 §4.6 插件扩展层

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TerraAuth.Plugins;

/// <summary>Hook 执行结果类型。</summary>
public enum HookResultType
{
    /// <summary>继续传播（其他插件可继续处理）。</summary>
    Continue,
    /// <summary>阻止操作（取消当前行为）。</summary>
    Deny,
    /// <summary>修改数据后继续。</summary>
    Modified
}

/// <summary>Hook 执行结果。统一管线用：Allow = 继续，Deny = 拒绝，Modify = 带修改后的数据继续。</summary>
public sealed class HookResult
{
    public HookResultType Type { get; set; } = HookResultType.Continue;
    public string? Reason { get; set; }
    public object? ModifiedData { get; set; }

    public static HookResult Allow() => new() { Type = HookResultType.Continue };
    public static HookResult Deny(string reason) => new() { Type = HookResultType.Deny, Reason = reason };
    public static HookResult Modify(object data) => new() { Type = HookResultType.Modified, ModifiedData = data };
}

/// <summary>Hook 参数基类。所有 Hook 参数继承此类，携带玩家/时间戳上下文。</summary>
public abstract class HookArgs
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
}

/// <summary>插件元数据接口。所有插件必须实现。</summary>
public interface IPlugin
{
    /// <summary>插件唯一 ID（用于依赖解析、配置节、日志）。</summary>
    string Id { get; }
    /// <summary>插件显示名称。</summary>
    string Name { get; }
    /// <summary>插件版本。</summary>
    Version Version { get; }
    /// <summary>作者。</summary>
    string Author { get; }
    /// <summary>描述。</summary>
    string Description { get; }
    /// <summary>依赖的其他插件 ID 列表（按 ID 拓扑排序加载）。</summary>
    IReadOnlyList<string> Dependencies { get; }
    /// <summary>执行优先级，数字越小越先执行。</summary>
    int Priority { get; }

    /// <summary>初始化（只执行一次，在此注册 Hook）。</summary>
    Task InitializeAsync(IPluginContext context);
    /// <summary>启动（可启动后台任务）。</summary>
    Task StartAsync();
    /// <summary>停止。</summary>
    Task StopAsync();
    /// <summary>卸载前清理（注销 Hook、释放资源）。</summary>
    Task ShutdownAsync();
}

/// <summary>插件基类：提供便捷访问（Hooks/Logger/Config/Metrics/Server）。</summary>
public abstract class PluginBase : IPlugin
{
    protected IPluginContext Context { get; private set; } = null!;
    protected IHookRegistry Hooks => Context.Hooks;
    protected ILogger Logger => Context.Logger;
    protected IConfigurationAdapter Config => Context.Configuration;
    protected IMetricsAdapter Metrics => Context.Metrics;
    protected IServerApi Server => Context.Server;

    // ---- 便捷注册方法 ----
    protected void On<TArgs>(Func<TArgs, HookResult> handler) where TArgs : HookArgs
        => Hooks.Register(this, handler);
    protected void OnAsync<TArgs>(Func<TArgs, Task<HookResult>> handler) where TArgs : HookArgs
        => Hooks.RegisterAsync(this, handler);

    // 便捷消息方法
    protected void Broadcast(string message, string color = "White")
        => Server.Broadcast(message, color);
    protected void SendToPlayer(int playerId, string message, string color = "White")
        => Server.SendMessage(playerId, message, color);
    protected PlayerStateSnapshot? GetPlayer(int playerId) => Server.GetPlayer(playerId);

    // ---- 子类必须 override 的元数据 ----
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract Version Version { get; }
    public virtual string Author => "Unknown";
    public virtual string Description => "";
    public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();
    public virtual int Priority { get; } = 100;

    public async Task InitializeAsync(IPluginContext context)
    {
        Context = context;
        await OnInitializeAsync();
    }

    /// <summary>子类重写：注册所有 Hook。</summary>
    protected virtual Task OnInitializeAsync() => Task.CompletedTask;
    public virtual Task StartAsync() => Task.CompletedTask;
    public virtual Task StopAsync() => Task.CompletedTask;
    public virtual Task ShutdownAsync() => Task.CompletedTask;
}
