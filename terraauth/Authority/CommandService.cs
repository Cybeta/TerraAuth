// TerraAuth — 服务端命令子系统（供插件 API ExecuteCommand / 控制台接管使用）
// 只负责「注册 + 解析 + 分发」；具体命令由组合根注册，避免本层依赖网络 / 封禁等实现。

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TerraAuth.Authority;

/// <summary>命令执行结果。</summary>
public sealed record CommandResult(bool Success, string Output)
{
    public static CommandResult Ok(string output = "") => new(true, output);
    public static CommandResult Fail(string output) => new(false, output);
}

/// <summary>命令处理器：入参为发起者 PlayerId（0 = 服务端控制台）与命令参数。</summary>
public delegate CommandResult CommandHandler(int playerId, string[] args);

/// <summary>
/// 服务端命令子系统：命令名不区分大小写，首个空格切分命令与参数。
/// 未注册的命令返回失败（不抛异常），处理器内部异常被捕获并转为失败结果。
/// </summary>
public sealed class CommandService
{
    private readonly ConcurrentDictionary<string, Entry> _commands = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Entry(string Help, CommandHandler Handler);

    /// <summary>注册（或覆盖）一条命令。</summary>
    public void Register(string name, string help, CommandHandler handler)
        => _commands[name] = new Entry(help, handler);

    /// <summary>已注册命令（按名称排序）。</summary>
    public IReadOnlyList<(string Name, string Help)> Commands
        => _commands.Select(kv => (kv.Key, kv.Value.Help)).OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

    /// <summary>执行一条命令（不含前缀，如 <c>say hello</c>）。</summary>
    public CommandResult Execute(int playerId, string commandLine)
    {
        var line = (commandLine ?? "").Trim();
        if (line.Length == 0) return CommandResult.Fail("空命令");

        int space = line.IndexOf(' ');
        var name = space < 0 ? line : line[..space];
        var args = space < 0
            ? Array.Empty<string>()
            : line[(space + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (!_commands.TryGetValue(name, out var entry))
            return CommandResult.Fail($"未知命令：{name}");

        try
        {
            return entry.Handler(playerId, args);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"命令执行异常：{ex.Message}");
        }
    }
}
