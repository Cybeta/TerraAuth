// TerraAuth — 插件上下文：插件可调用的服务端能力（适配核心内部类型）
// 需求 1：为插件编写预留稳定 API 边界

using System;
using System.Collections.Generic;

namespace TerraAuth.Plugins;

/// <summary>插件上下文：插件通过此接口访问服务端能力。这是插件唯一的依赖入口。</summary>
public interface IPluginContext
{
    IHookRegistry Hooks { get; }
    ILogger Logger { get; }
    IConfigurationAdapter Configuration { get; }
    IMetricsAdapter Metrics { get; }
    IServerApi Server { get; }
    IEventStoreAdapter EventStore { get; }
}

/// <summary>插件上下文默认实现。</summary>
public sealed class PluginContext : IPluginContext
{
    public IHookRegistry Hooks { get; }
    public ILogger Logger { get; }
    public IConfigurationAdapter Configuration { get; }
    public IMetricsAdapter Metrics { get; }
    public IServerApi Server { get; }
    public IEventStoreAdapter EventStore { get; }

    public PluginContext(
        IHookRegistry hooks, ILogger logger,
        IConfigurationAdapter configuration, IMetricsAdapter metrics,
        IServerApi server, IEventStoreAdapter eventStore)
    {
        Hooks = hooks; Logger = logger;
        Configuration = configuration; Metrics = metrics;
        Server = server; EventStore = eventStore;
    }
}

// ============================================================================
// 以下为面向插件的精简适配器接口（隔离核心内部类型，保持 API 稳定）
// ============================================================================

/// <summary>日志适配器。</summary>
public interface ILogger
{
    void Debug(string message, params object[] args);
    void Info(string message, params object[] args);
    void Warn(string message, params object[] args);
    void Error(Exception? ex, string message, params object[] args);
}

/// <summary>配置适配器：支持按路径读取 + 热更新通知。</summary>
public interface IConfigurationAdapter
{
    T Get<T>(string key, T defaultValue = default!);
    void OnChanged(Action onChange);
}

/// <summary>指标适配器：插件上报自定义指标。</summary>
public interface IMetricsAdapter
{
    void Counter(string name, string help, params (string, string)[] labels);
    void Gauge(string name, double value, params (string, string)[] labels);
    void Histogram(string name, double value, params (string, string)[] labels);
}

/// <summary>事件存储适配器（只读）：插件可查询历史事件（用于行为分析类插件）。</summary>
public interface IEventStoreAdapter
{
    IAsyncEnumerable<EventRecord> QueryAsync(int? playerId = null, string? category = null, int limit = 100);
}

/// <summary>事件记录（插件可见的只读视图）。</summary>
public sealed record EventRecord(
    DateTimeOffset Timestamp, int PlayerId, string Category, string Action, string Reason);

/// <summary>玩家状态快照（插件可见的只读视图，避免暴露内部 WorldState）。</summary>
public sealed record PlayerStateSnapshot(
    int PlayerId, string Name, int Hp, int MaxHp, int Mp, int MaxMp,
    float X, float Y, bool IsConnected);

/// <summary>服务端 API：插件可调用的服务端操作。</summary>
public interface IServerApi
{
    void Broadcast(string message, string color = "White");
    void SendMessage(int playerId, string message, string color = "White");
    PlayerStateSnapshot? GetPlayer(int playerId);
    IReadOnlyList<PlayerStateSnapshot> GetOnlinePlayers();
    void KickPlayer(int playerId, string reason);
    void BanPlayer(int playerId, TimeSpan? duration = null, string reason = "");
    void ExecuteCommand(string command);
    ServerInfoSnapshot GetServerInfo();
}

/// <summary>服务器信息快照。</summary>
public sealed record ServerInfoSnapshot(
    int OnlinePlayers, int MaxPlayers, long TotalTicks, TimeSpan Uptime, string WorldName);
