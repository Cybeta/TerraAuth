// TerraAuth — 可执行入口（服务端进程）
// 用法：dotnet run --project TerraAuth.csproj -- [--config server.json] [--port 7777]
//                                          [--db terraauth.db] [--metrics-port 9090]

namespace TerraAuth;

/// <summary>服务端进程入口：解析参数 → GameHost.Bootstrap 组装 → 运行至 Ctrl+C。</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        ServerOptions options;
        try
        {
            options = ServerOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[TerraAuth] 参数错误：{ex.Message}");
            Console.Error.WriteLine(ServerOptions.Usage);
            return 1;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ServerOptions.Usage);
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // 阻止进程被直接终止，走优雅停机
            Console.WriteLine("\n[TerraAuth] 收到关闭信号，正在停止……");
            cts.Cancel();
        };

        // 后台线程 / 未观察任务的异常统一记录，避免无输出直接崩溃
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"[TerraAuth] 未处理异常：{e.ExceptionObject}");
            Console.Error.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"[TerraAuth] 未观察的任务异常：{e.Exception}");
            Console.Error.Flush();
            e.SetObserved();
        };

        Console.WriteLine($"[TerraAuth] 配置={options.ConfigPath} 数据库={options.DbPath}");
        Console.WriteLine($"[TerraAuth] 监听端口={options.Port} 指标端口={options.MetricsPort}");

        if (!Console.IsInputRedirected)
            WorldManagementConsole.Run(options.ConfigPath, Console.In, Console.Out);

        GameHost host;
        try
        {
            host = GameHost.Bootstrap(
                dbPath: options.DbPath,
                configPath: options.ConfigPath,
                metricsPort: options.MetricsPort,
                port: options.Port);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TerraAuth] 启动失败：{ex}");
            return 3;
        }

        using (host)
        {
            try
            {
                await host.RunAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常关闭路径
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TerraAuth] 运行失败：{ex}");
                return 2;
            }
        }

        Console.WriteLine("[TerraAuth] 已停止。");
        return 0;
    }
}

/// <summary>命令行参数（README §五 快速开始：--config / --port）。</summary>
internal sealed record ServerOptions(
    string ConfigPath,
    string DbPath,
    int Port,
    int MetricsPort,
    bool ShowHelp)
{
    public const string Usage =
        "用法: TerraAuth [--config <path>] [--db <path>] [--port <1-65535>] " +
        "[--metrics-port <0-65535>] [--help]";

    public static ServerOptions Parse(string[] args)
    {
        var config = "server.json";
        var db = "terraauth.db";
        var port = 7777;
        var metricsPort = 9090;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config":
                    config = RequireValue(args, ref i);
                    break;
                case "--db":
                    db = RequireValue(args, ref i);
                    break;
                case "--port":
                    port = RequireInt(args, ref i);
                    break;
                case "--metrics-port":
                    metricsPort = RequireInt(args, ref i);
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"未知参数：{args[i]}");
            }
        }

        if (port is < 1 or > 65535)
            throw new ArgumentException($"端口非法：{port}");
        if (metricsPort is < 0 or > 65535)
            throw new ArgumentException($"指标端口非法：{metricsPort}");

        return new ServerOptions(config, db, port, metricsPort, help);
    }

    private static string RequireValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"参数 {args[i]} 缺少取值");
        return args[++i];
    }

    private static int RequireInt(string[] args, ref int i)
    {
        var value = RequireValue(args, ref i);
        if (!int.TryParse(value, out var parsed))
            throw new ArgumentException($"参数 {args[i - 1]} 需要整数，实际为 {value}");
        return parsed;
    }
}
