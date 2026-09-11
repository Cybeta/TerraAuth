// TerraAuth — Hook 注册表
// 线程安全：支持并行触发（对接 Concurrency 模块的 WorkerPool）
// 优先级：数字越小越先执行；Deny 短路；Modified 传递修改后数据
// 并发策略：注册 / 注销罕见 → 写端加锁重建**不可变快照数组**（写时复制）；
//          触发在每包热路径上 → 读取端 Volatile.Read 取快照，零锁、零分配。

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;      // Lock / Volatile
using System.Threading.Tasks;

namespace TerraAuth.Plugins;

/// <summary>Hook 注册表：管理所有插件注册的回调，负责触发与分发。</summary>
public interface IHookRegistry
{
    void Register<TArgs>(IPlugin plugin, Func<TArgs, HookResult> handler) where TArgs : HookArgs;
    void RegisterAsync<TArgs>(IPlugin plugin, Func<TArgs, Task<HookResult>> handler) where TArgs : HookArgs;
    HookResult Trigger<TArgs>(TArgs args) where TArgs : HookArgs;
    Task<HookResult> TriggerAsync<TArgs>(TArgs args) where TArgs : HookArgs;
    void UnregisterPlugin(IPlugin plugin);
    /// <summary>是否存在某类 Hook 的订阅者（用于核心层提前短路判断）。</summary>
    bool HasSubscribers<TArgs>() where TArgs : HookArgs;
    /// <summary>
    /// 按运行期 <see cref="HookArgs"/> 类型查询订阅者。供核心层在**构造 HookArgs 之前**做零分配短路。
    /// </summary>
    bool HasSubscribers(Type argsType);
}

/// <summary>Hook 注册表默认实现。</summary>
public sealed class HookRegistry : IHookRegistry
{
    // Type = HookArgs 的具体类型；Snapshot 为按 Priority 排序的不可变数组
    private readonly ConcurrentDictionary<Type, HookEntryList> _hooks = new();
    private readonly ILogger _logger;

    public HookRegistry(ILogger logger) => _logger = logger;

    public void Register<TArgs>(IPlugin plugin, Func<TArgs, HookResult> handler) where TArgs : HookArgs
    {
        var entry = new HookEntry(plugin, args => handler((TArgs)args), null);
        Add(typeof(TArgs), entry);
    }

    public void RegisterAsync<TArgs>(IPlugin plugin, Func<TArgs, Task<HookResult>> handler) where TArgs : HookArgs
    {
        var entry = new HookEntry(plugin, null, args => handler((TArgs)args));
        Add(typeof(TArgs), entry);
    }

    /// <summary>新增订阅者：复制当前快照 + 排序后原子发布（写时复制，读端不受影响）。</summary>
    private void Add(Type type, HookEntry entry)
    {
        var list = _hooks.GetOrAdd(type, _ => new HookEntryList());
        lock (list.Gate) // 仅写端串行；读端（Trigger）不加锁
        {
            var current = list.Snapshot;
            var next = new HookEntry[current.Length + 1];
            current.CopyTo(next, 0);
            next[^1] = entry;
            Array.Sort(next, static (a, b) => a.Plugin.Priority.CompareTo(b.Plugin.Priority));
            Volatile.Write(ref list.Snapshot, next);
        }
    }

    /// <summary>同步触发：按优先级顺序执行，Deny 短路。</summary>
    public HookResult Trigger<TArgs>(TArgs args) where TArgs : HookArgs
    {
        // 按运行时类型查找：调用方可能以基类静态类型（HookArgs）传入，
        // 此时 typeof(TArgs) 恒为 HookArgs，会漏掉具体注册项（HookedPipeline 即此场景）。
        if (args is null) return HookResult.Allow();
        if (!_hooks.TryGetValue(args.GetType(), out var entries)) return HookResult.Allow();
        // 读取不可变快照：零锁、零分配（插件可能耗时，更不能持锁执行）
        var snapshot = Volatile.Read(ref entries.Snapshot);
        foreach (var entry in snapshot)
        {
            try
            {
                var result = entry.SyncHandler!(args);
                if (result.Type == HookResultType.Deny)
                {
                    _logger.Debug("Hook {Type} denied by '{Plugin}': {Reason}",
                        typeof(TArgs).Name, entry.Plugin.Id, result.Reason);
                    return result;
                }
                if (result.Type == HookResultType.Modified && result.ModifiedData is TArgs modified)
                {
                    args = modified; // 传递修改后的参数给后续插件
                }
            }
            catch (Exception ex)
            {
                // 单个插件异常不影响其他插件（隔离性）
                _logger.Error(ex, "Plugin '{Plugin}' threw in hook {Type}", entry.Plugin.Id, typeof(TArgs).Name);
            }
        }
        return HookResult.Allow();
    }

    /// <summary>异步触发：并行执行无依赖的 Hook，但 Deny 仍需短路（此处简化为顺序 await）。</summary>
    public async Task<HookResult> TriggerAsync<TArgs>(TArgs args) where TArgs : HookArgs
    {
        if (args is null) return HookResult.Allow();
        if (!_hooks.TryGetValue(args.GetType(), out var entries)) return HookResult.Allow();
        var snapshot = Volatile.Read(ref entries.Snapshot);
        foreach (var entry in snapshot)
        {
            try
            {
                if (entry.AsyncHandler != null)
                {
                    var result = await entry.AsyncHandler(args).ConfigureAwait(false);
                    if (result.Type == HookResultType.Deny) return result;
                }
                else if (entry.SyncHandler != null)
                {
                    var result = entry.SyncHandler(args);
                    if (result.Type == HookResultType.Deny) return result;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Plugin '{Plugin}' threw in async hook", entry.Plugin.Id);
            }
        }
        return HookResult.Allow();
    }

    /// <summary>注销插件的全部订阅：命中才重建快照（写时复制，读端不受影响）。</summary>
    public void UnregisterPlugin(IPlugin plugin)
    {
        foreach (var kvp in _hooks)
        {
            var list = kvp.Value;
            lock (list.Gate)
            {
                var current = list.Snapshot;
                var kept = Array.FindAll(current, e => e.Plugin != plugin);
                if (kept.Length != current.Length)
                    Volatile.Write(ref list.Snapshot, kept);
            }
        }
    }

    public bool HasSubscribers<TArgs>() where TArgs : HookArgs
        => HasSubscribers(typeof(TArgs));

    public bool HasSubscribers(Type argsType)
        => _hooks.TryGetValue(argsType, out var list) && Volatile.Read(ref list.Snapshot).Length > 0;

    /// <summary>
    /// 某类 Hook 的订阅者容器：<see cref="Snapshot"/> 为按优先级排序的不可变数组。
    /// 写端（注册 / 注销）持 <see cref="Lock"/> 重建并原子替换；读端（触发）仅 Volatile.Read。
    /// </summary>
    private sealed class HookEntryList
    {
        public readonly Lock Gate = new();
        public HookEntry[] Snapshot = Array.Empty<HookEntry>();
    }

    private sealed record HookEntry(
        IPlugin Plugin,
        Func<HookArgs, HookResult>? SyncHandler,
        Func<HookArgs, Task<HookResult>>? AsyncHandler);
}

// ============================================================================
// Mod 兼容层共用：客户端能力声明（需求 2）
// ============================================================================

/// <summary>客户端类型。</summary>
public enum ClientType { Vanilla, TModLoader, TModLoaderFork, CustomModded, Unknown }

/// <summary>客户端能力声明（连接握手时由 ModDetector 填充）。</summary>
public sealed class ClientCapabilities
{
    public ClientType Type { get; set; } = ClientType.Unknown;
    public string Version { get; set; } = "";
    public int ProtocolVersion { get; set; }
    /// <summary>加载的 Mod 列表（TModLoader 客户端通过自定义包上报）。</summary>
    public IReadOnlyList<LoadedMod> Mods { get; set; } = Array.Empty<LoadedMod>();
    public bool SupportsDirectSnapshots { get; set; }
    public bool SupportsShadowPrediction { get; set; }
    /// <summary>是否为仅客户端 Mod（不影响服务端，默认可用）。</summary>
    public bool IsClientSideOnly => Mods.All(m => m.IsClientSideOnly);
}

/// <summary>已加载的 Mod 信息。</summary>
public sealed record LoadedMod(
    string Name, string Version, string Author, bool IsClientSideOnly, bool RequiresServerSide);
