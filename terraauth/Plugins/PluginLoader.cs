// TerraAuth — 插件加载器
// 从 plugins/ 目录加载 DLL → 反射查找 IPlugin → 按依赖拓扑排序 → 初始化
// 支持热重载（开发模式）

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace TerraAuth.Plugins;

/// <summary>已加载插件记录。</summary>
public sealed record LoadedPlugin(IPlugin Plugin, Assembly Assembly, Type PluginType, DateTimeOffset LoadTime);

/// <summary>插件加载器：负责发现、排序、初始化、热重载插件。</summary>
public sealed class PluginLoader
{
    private readonly string _pluginDirectory;
    private readonly ILogger _logger;
    private readonly IPluginContext _context;
    private readonly List<LoadedPlugin> _loaded = new();

    public IReadOnlyList<LoadedPlugin> LoadedPlugins => _loaded;

    public PluginLoader(string pluginDirectory, ILogger logger, IPluginContext context)
    {
        _pluginDirectory = pluginDirectory;
        _logger = logger;
        _context = context;
        Directory.CreateDirectory(pluginDirectory);
    }

    /// <summary>加载并初始化所有插件（按依赖顺序）。</summary>
    public async Task LoadAllAsync()
    {
        var dlls = Directory.GetFiles(_pluginDirectory, "*.dll");
        _logger.Info("Found {Count} plugin DLLs in {Dir}", dlls.Length, _pluginDirectory);

        var pluginTypes = new List<(Assembly Assembly, Type Type)>();
        foreach (var dll in dlls)
        {
            Assembly assembly;
            try { assembly = Assembly.LoadFrom(dll); }
            catch (Exception ex) { _logger.Error(ex, "Failed to load assembly: {File}", dll); continue; }

            var types = assembly.GetTypes()
                .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
            foreach (var t in types)
            {
                pluginTypes.Add((assembly, t));
                _logger.Debug("Discovered plugin type: {Type}", t.FullName);
            }
        }

        // 拓扑排序：被依赖的插件先初始化
        foreach (var (assembly, type) in TopologicalSort(pluginTypes))
        {
            try
            {
                var plugin = (IPlugin)Activator.CreateInstance(type)!;
                await plugin.InitializeAsync(_context);
                _loaded.Add(new LoadedPlugin(plugin, assembly, type, DateTimeOffset.UtcNow));
                _logger.Info("Loaded plugin: {Name} v{Version} by {Author} [{Id}]",
                    plugin.Name, plugin.Version, plugin.Author, plugin.Id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize plugin: {Type}", type.FullName);
            }
        }
    }

    /// <summary>启动所有插件。</summary>
    public async Task StartAllAsync()
    {
        foreach (var lp in _loaded)
        {
            try { await lp.Plugin.StartAsync(); }
            catch (Exception ex) { _logger.Error(ex, "Failed to start plugin: {Id}", lp.Plugin.Id); }
        }
    }

    /// <summary>停止并卸载所有插件（注销所有 Hook）。</summary>
    public async Task StopAllAsync(IHookRegistry hooks)
    {
        foreach (var lp in Enumerable.Reverse(_loaded)) // 逆序卸载
        {
            try
            {
                await lp.Plugin.StopAsync();
                await lp.Plugin.ShutdownAsync();
                hooks.UnregisterPlugin(lp.Plugin);
                _logger.Info("Unloaded plugin: {Id}", lp.Plugin.Id);
            }
            catch (Exception ex) { _logger.Error(ex, "Error stopping plugin: {Id}", lp.Plugin.Id); }
        }
        _loaded.Clear();
    }

    /// <summary>热重载指定插件（开发用）。</summary>
    public async Task HotReloadAsync(string pluginId, IHookRegistry hooks)
    {
        var lp = _loaded.FirstOrDefault(p => p.Plugin.Id == pluginId);
        if (lp == null) { _logger.Warn("Plugin not found for hot reload: {Id}", pluginId); return; }

        _logger.Info("Hot reloading plugin: {Id}", pluginId);
        await lp.Plugin.StopAsync();
        await lp.Plugin.ShutdownAsync();
        hooks.UnregisterPlugin(lp.Plugin);

        // 重新加载 DLL（注意：Assembly 无法卸载，生产环境建议 AppDomain/AssemblyLoadContext 隔离）
        var dll = lp.Assembly.Location;
        var assembly = Assembly.LoadFrom(dll);
        var type = assembly.GetTypes().First(t => t.FullName == lp.PluginType.FullName);
        var plugin = (IPlugin)Activator.CreateInstance(type)!;
        await plugin.InitializeAsync(_context);
        await plugin.StartAsync();

        lock (_loaded) { _loaded.Remove(lp); _loaded.Add(new LoadedPlugin(plugin, assembly, type, DateTimeOffset.UtcNow)); }
        _logger.Info("Hot reload complete: {Id}", pluginId);
    }

    // ---- 拓扑排序（Kahn 算法）----
    private IEnumerable<(Assembly, Type)> TopologicalSort(List<(Assembly Assembly, Type Type)> items)
    {
        var byId = new Dictionary<string, (Assembly, Type)>();
        foreach (var it in items)
        {
            var inst = (IPlugin?)Activator.CreateInstance(it.Type); // 临时实例读取元数据
            if (inst != null) byId[inst.Id] = it;
        }

        var indegree = new Dictionary<string, int>();
        foreach (var (id, _) in byId) indegree[id] = 0;
        foreach (var (id, (_, type)) in byId)
        {
            var inst = (IPlugin?)Activator.CreateInstance(type);
            if (inst == null) continue;
            foreach (var dep in inst.Dependencies)
            {
                if (byId.ContainsKey(dep)) indegree[dep] = indegree.GetValueOrDefault(dep, 0) + 1;
            }
        }

        var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<(Assembly, Type)>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            result.Add(byId[id]);
            foreach (var (otherId, (_, type)) in byId)
            {
                var inst = (IPlugin?)Activator.CreateInstance(type);
                if (inst?.Dependencies.Contains(id) == true)
                {
                    indegree[otherId]--;
                    if (indegree[otherId] == 0) queue.Enqueue(otherId);
                }
            }
        }
        if (result.Count != byId.Count) _logger.Warn("Plugin dependency cycle detected! Some plugins may not load.");
        return result;
    }
}
