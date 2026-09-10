// TerraAuth — 核心适配层：把核心内部类型桥接到插件/Mod 兼容接口
// 这是核心与插件系统之间的解耦边界，确保插件不直接依赖内部实现
// 归属组合根（Core）：适配器需同时看到 Core 的 Config/Monitoring 与 Plugins 的接口，
// 若放在 Plugins 会导致 Plugins 反向依赖 Core（循环引用）。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TerraAuth.Authority;       // IAuditLogger, AuditEvent
using TerraAuth.Monitoring;      // IMetrics
using TerraAuth.Plugins;         // 插件适配器接口
using TerraAuth.Simulation;      // WorldState

namespace TerraAuth.Plugins;

#region 日志适配器（桥接核心 ILogger → 插件 ILogger）
/// <summary>桥接核心日志到插件 ILogger（此处核心用 Console，可替换为 Serilog 等）。</summary>
internal sealed class CoreLogger : ILogger
{
    public void Debug(string message, params object[] args) => Write("DEBUG", message, args);
    public void Info(string message, params object[] args) => Write("INFO", message, args);
    public void Warn(string message, params object[] args) => Write("WARN", message, args);
    public void Error(Exception? ex, string message, params object[] args)
        => Console.WriteLine($"[ERROR] {Format(message, args)}{(ex != null ? $"\n{ex}" : "")}");

    private static void Write(string level, string message, object[] args)
        => Console.WriteLine($"[{level}] {Format(message, args)}");

    /// <summary>
    /// 兼容 Serilog 风格命名占位符（<c>{Count}</c>）与标准数字索引（<c>{0}</c>）的格式化。
    /// 数字索引直接取 <paramref name="args"/>[index]；命名占位符按首次出现顺序绑定参数，重复名称复用同一参数。
    /// 同时支持 <c>{{</c>/<c>}}</c> 转义与 <c>{Name:format}</c> 格式说明符（忽略格式说明符）。
    /// </summary>
    private static string Format(string message, object[] args)
    {
        if (string.IsNullOrEmpty(message) || args.Length == 0) return message;

        var sb = new StringBuilder(message.Length + 16);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;

        for (var i = 0; i < message.Length; i++)
        {
            var c = message[i];
            if (c == '{')
            {
                if (i + 1 < message.Length && message[i + 1] == '{') { sb.Append('{'); i++; continue; }
                var end = message.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(c); continue; }

                var token = message.Substring(i + 1, end - i - 1);
                var colon = token.IndexOf(':');
                var name = colon >= 0 ? token.Substring(0, colon) : token;

                object? value = null;
                if (int.TryParse(name, out var index) && index >= 0 && index < args.Length)
                {
                    value = args[index];
                }
                else
                {
                    if (!names.TryGetValue(name, out var bound))
                    {
                        bound = next;
                        if (bound < args.Length) next++;
                        names[name] = bound;
                    }
                    if (bound >= 0 && bound < args.Length) value = args[bound];
                }

                sb.Append(value?.ToString());
                i = end;
            }
            else if (c == '}' && i + 1 < message.Length && message[i + 1] == '}') { sb.Append('}'); i++; }
            else sb.Append(c);
        }

        return sb.ToString();
    }
}

/// <summary>桥接核心配置到插件 IConfigurationAdapter。</summary>
internal sealed class CoreConfiguration : IConfigurationAdapter
{
    private readonly Config.IConfigurationService _config;
    public CoreConfiguration(Config.IConfigurationService config) => _config = config;
    public T Get<T>(string key, T defaultValue = default!)
    {
        // 简单实现：直接读取 ServerConfig 上的属性（生产可改用 JsonPath）
        var prop = _config.Current?.GetType().GetProperty(key);
        if (prop == null) return defaultValue;
        return (T)(prop.GetValue(_config.Current) ?? defaultValue)!;
    }
    public void OnChanged(Action onChange) => _config.OnChanged += _ => onChange();
}

/// <summary>桥接核心 IMetrics 到插件指标（自动加前缀，便于 Prometheus 分组）。</summary>
internal sealed class CoreMetrics : IMetricsAdapter
{
    private readonly Monitoring.IMetrics _metrics;
    public CoreMetrics(Monitoring.IMetrics metrics) => _metrics = metrics;
    public void Counter(string name, string help, params (string, string)[] labels)
        => _metrics.IncrementBlockedCheat(name); // 映射到现有计数器
    public void Gauge(string name, double value, params (string, string)[] labels) { /* TODO: 暴露 SetGauge */ }
    public void Histogram(string name, double value, params (string, string)[] labels)
        => _metrics.ObservePacketProcessingTime(value);
}

/// <summary>桥接核心事件存储（复用 IAuditLogger）。</summary>
internal sealed class CoreEventStore : IEventStoreAdapter
{
    private readonly IAuditLogger _audit;
    public CoreEventStore(IAuditLogger audit) => _audit = audit;
    public async IAsyncEnumerable<EventRecord> QueryAsync(int? playerId = null, string? category = null, int limit = 100)
    {
        // TODO: 从持久化层按条件查询（此处为占位）
        await Task.CompletedTask;
        yield break;
    }
}
#endregion

#region 服务端 API 实现（IServerApi）
/// <summary>服务端 API 默认实现：桥接 GameHost 持有的核心服务。</summary>
public sealed class ServerApi : IServerApi
{
    private readonly IAuditLogger _audit;
    private readonly Monitoring.IMetrics _metrics;
    // TODO: 注入 NetworkHost / ConnectionManager / BanManager 实现真实操作

    public ServerApi(IAuditLogger audit, Monitoring.IMetrics metrics)
    {
        _audit = audit; _metrics = metrics;
    }

    public void Broadcast(string message, string color = "White")
        => _audit.Log(AuditEvent.Now(0, "server", "broadcast", $"color={color}", message));

    public void SendMessage(int playerId, string message, string color = "White")
        => _audit.Log(AuditEvent.Now(playerId, "server", "message", $"color={color}", message));

    public PlayerStateSnapshot? GetPlayer(int playerId)
    {
        // TODO: 从 WorldState / ConnectionManager 读取真实状态
        return null;
    }

    public IReadOnlyList<PlayerStateSnapshot> GetOnlinePlayers()
        => Array.Empty<PlayerStateSnapshot>();

    public void KickPlayer(int playerId, string reason)
        => _audit.Log(AuditEvent.Now(playerId, "server", "kick", reason));

    public void BanPlayer(int playerId, TimeSpan? duration = null, string reason = "")
        => _audit.Log(AuditEvent.Now(playerId, "security", "ban", reason, duration?.ToString() ?? "permanent"));

    public void ExecuteCommand(string command)
        => _audit.Log(AuditEvent.Now(0, "server", "command", command));

    public ServerInfoSnapshot GetServerInfo()
        => new(0, 0, 0, TimeSpan.Zero, "");
}
#endregion
