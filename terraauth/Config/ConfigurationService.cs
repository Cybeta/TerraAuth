// Phase 6 - 配置服务默认实现：JSON 文件 + FileSystemWatcher 热重载

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading; // Lock（.NET 9+ 专用同步原语）

namespace TerraAuth.Config;

public sealed class ConfigurationService : IConfigurationService
{
    /// <summary>
    /// 配置序列化选项：枚举以字符串读写（如 ModPolicy.Mode = "Whitelist"），
    /// 属性名大小写不敏感，便于运维手写配置。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();
    private ServerConfig _current = new(); // 默认值
    private string? _path;
    private FileSystemWatcher? _watcher;

    public ServerConfig Current
    {
        get { lock (_lock) return _current; }
    }

    public event Action<ServerConfig>? OnChanged;

    public void Load(string path)
    {
        // 归一化为绝对路径：相对路径（如 "server.json"）经 GetDirectoryName 会得到空串，
        // 直接交给 FileSystemWatcher 会抛 ArgumentException。
        var fullPath = Path.GetFullPath(path);
        _path = fullPath;
        Reload();

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory)) return; // 理论上不会发生，防御性返回

        // 监听文件变更，自动热重载
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
        {
            NotifyFilter = NotifyFilters.LastWrite
        };
        _watcher.Changed += (_, _) => { try { Reload(); } catch { /* 忽略中间态错误 */ } };
        _watcher.EnableRaisingEvents = true;
    }

    public void Reload()
    {
        if (_path is null || !File.Exists(_path)) return;
        var json = File.ReadAllText(_path);
        var cfg = JsonSerializer.Deserialize<ServerConfig>(json, JsonOptions)
                  ?? throw new InvalidDataException("配置解析失败");
        Validate(cfg);
        lock (_lock) _current = cfg;
        OnChanged?.Invoke(cfg); // 通知观察者（如 RateAuthority 动态调整阈值）
    }

    // 校验配置合法性，防止误杀或漏判
    private static void Validate(ServerConfig c)
    {
        if (c.MaxConnections <= 0) throw new InvalidDataException("MaxConnections 必须 > 0");
        if (c.MaxSingleDamage < 0) throw new InvalidDataException("MaxSingleDamage 不能为负");
        // 包 28 的伤害线格式为 Int16（±32767）：上限超过该范围将永不触发（量纲陷阱）
        if (c.MaxSingleDamage > short.MaxValue)
            throw new InvalidDataException(
                $"MaxSingleDamage({c.MaxSingleDamage}) 超过 Int16 上限 {short.MaxValue}，该阈值永不触发；请设为 ≤ {short.MaxValue}");
        if (c.MaxViolationsBeforeBan < 1) throw new InvalidDataException("封禁阈值至少为 1");
        // 注：完整实现应校验所有阈值，此处仅示例
    }
}
