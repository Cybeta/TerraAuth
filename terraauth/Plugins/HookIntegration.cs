// TerraAuth — Hook 接入：在权威管线各阶段插入 Hook 触发点
// 采用装饰器模式包装 IInboundPipeline，无需修改 Phase 2 内部代码
// 对应需求 1：为后续插件编写预留 hook

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraAuth.Authority;
using TerraAuth.Protocol;
using TerraAuth.Simulation;

namespace TerraAuth.Plugins;

/// <summary>带 Hook 的管线装饰器：在权威校验前后触发插件 Hook。</summary>
/// <remarks>
/// 触发点（对应架构 §3.2 管线 + Phase 2 六种权威子系统）：
///   - PlayerConnectingArgs  → ConnectionStateStage 之后
///   - PlayerMovingArgs      → MovementAuthorityStage
///   - PlayerStatChangedArgs → PlayerAuthorityStage
///   - NpcStrikeArgs         → CombatAuthorityStage
///   - ItemPickupArgs/Drop   → InventoryAuthorityStage
///   - TilePlaceArgs/Break   → WorldAuthorityStage
///   - ProjectileSpawnArgs   → CombatAuthorityStage
///   - ServerTickArgs        → GameLoop 每帧
/// Hook 返回 Deny 则短路拒绝；返回 Modify 则用修改后数据继续。
/// </remarks>
public sealed class HookedPipeline : IInboundPipeline
{
    private readonly IInboundPipeline _inner;
    private readonly IHookRegistry _hooks;
    private readonly ILogger _logger;

    public HookedPipeline(IInboundPipeline inner, IHookRegistry hooks, ILogger logger)
    {
        _inner = inner; _hooks = hooks; _logger = logger;
    }

    public Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct = default)
    {
        // 根据包类型触发对应前置 Hook（可取消 / 可修改）
        var args = BuildHookArgs(packet, playerId);
        if (args != null)
        {
            var result = _hooks.Trigger(args);
            if (result.Type == HookResultType.Deny)
            {
                _logger.Debug("Plugin denied packet {Type} for player {PlayerId}: {Reason}",
                    packet.Type, playerId, result.Reason);
                return Task.FromResult(AuthorityResult.Reject(result.Reason ?? "Denied by plugin"));
            }
            // Modified：后续可从 args 读取被修改的字段（此处为扩展点）
        }

        // 调用原始管线（Phase 2 权威校验）
        return _inner.ProcessAsync(packet, playerId, commands, ct);
    }

    /// <summary>把网络包映射到对应 Hook 参数（扩展点：新增包类型在此注册）。</summary>
    private HookArgs? BuildHookArgs(INetworkPacket packet, int playerId)
    {
        // 注意：packet.Type 为 PacketId 枚举（Protocol 层定义）
        var type = packet.Type;
        HookArgs? args = type switch
        {
            // 玩家移动
            PacketId.PlayerPosition => new PlayerMovingArgs { PlayerId = playerId },
            // 战斗
            PacketId.NpcStrike => new NpcStrikeArgs { PlayerId = playerId },
            PacketId.ProjectileNew => new ProjectileSpawnArgs { PlayerId = playerId },
            // 物品
            PacketId.ItemDrop or PacketId.Chest => new ItemDropArgs { PlayerId = playerId },
            // 世界
            PacketId.TilePlace => new TilePlaceArgs { PlayerId = playerId },
            PacketId.TileBreak => new TileBreakArgs { PlayerId = playerId },
            _ => null
        };
        if (args != null) args.PlayerName = ""; // TODO: 从 ConnectionManager 读取
        return args;
    }
}
