// TerraAuth 示例插件 1：进服欢迎
// 演示服务器生命周期 / 玩家生命周期 Hook（ServerStarted / PlayerJoined / PlayerLeft）

using System;
using System.Threading.Tasks;
using TerraAuth.Plugins;

namespace TerraAuth.ExamplePlugins;

public sealed class WelcomePlugin : PluginBase
{
    public override string Id => "welcome";
    public override string Name => "Welcome Plugin";
    public override Version Version => new(1, 0, 0);
    public override string Author => "TerraAuth";
    public override string Description => "进服欢迎 + 生命周期日志（示例）";

    protected override Task OnInitializeAsync()
    {
        On<ServerStartedArgs>(OnServerStarted);
        On<PlayerJoinedArgs>(OnPlayerJoined);
        On<PlayerLeftArgs>(OnPlayerLeft);
        return Task.CompletedTask;
    }

    private HookResult OnServerStarted(ServerStartedArgs args)
    {
        Logger.Info("WelcomePlugin 已激活");
        return HookResult.Allow();
    }

    private HookResult OnPlayerJoined(PlayerJoinedArgs args)
    {
        Logger.Info("玩家 {0}（#{1}）加入游戏", args.PlayerName, args.PlayerId);
        SendToPlayer(args.PlayerId, $"欢迎来到服务器，{args.PlayerName}！", "Green");
        Broadcast($"{args.PlayerName} 加入了服务器", "Yellow");
        return HookResult.Allow();
    }

    private HookResult OnPlayerLeft(PlayerLeftArgs args)
    {
        Logger.Info("玩家 {0}（#{1}）离开，在线 {2:F0}s",
            args.PlayerName, args.PlayerId, args.SessionDuration.TotalSeconds);
        return HookResult.Allow();
    }
}
