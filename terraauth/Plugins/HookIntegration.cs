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
    private readonly Func<int, string?>? _playerNameResolver;

    /// <param name="playerNameResolver">
    /// 玩家名解析器（PlayerId → 名称）。传输层（NetworkHost）持有玩家名，故由组合根以延迟绑定的
    /// 闭包注入，避免 Plugins 层反向依赖 Net 层；为 null 时 PlayerName 留空。
    /// </param>
    public HookedPipeline(
        IInboundPipeline inner,
        IHookRegistry hooks,
        ILogger logger,
        Func<int, string?>? playerNameResolver = null)
    {
        _inner = inner; _hooks = hooks; _logger = logger;
        _playerNameResolver = playerNameResolver;
    }

    public Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct = default)
    {
        // 零分配短路：该类 Hook 无订阅者时**不构造 HookArgs**（无插件场景下每包省一次对象分配）
        if (HasSubscriberFor(packet))
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
        }

        // 调用原始管线（Phase 2 权威校验）
        return _inner.ProcessAsync(packet, playerId, commands, ct);
    }

    /// <summary>
    /// 包类型 → 对应 HookArgs 类型是否有订阅者。<b>只做类型判断，不实例化</b>，
    /// 使无插件（或该 Hook 无插件）时每包零分配；与 <see cref="BuildHookArgs"/> 的映射须保持一致。
    /// </summary>
    private bool HasSubscriberFor(INetworkPacket packet) => packet switch
    {
        PlayerControlsPacket => _hooks.HasSubscribers(typeof(PlayerMovingArgs)),      // 包 13
        NpcStrikePacket => _hooks.HasSubscribers(typeof(NpcStrikeArgs)),             // 包 28
        ProjectileNewPacket => _hooks.HasSubscribers(typeof(ProjectileSpawnArgs)),   // 包 27
        ItemDropPacket => _hooks.HasSubscribers(typeof(ItemDropArgs)),               // 包 21
        ChestPacket => _hooks.HasSubscribers(typeof(ItemDropArgs)),                  // 包 31
        TilePlacePacket => _hooks.HasSubscribers(typeof(TilePlaceArgs)),             // 包 79
        TileBreakPacket => _hooks.HasSubscribers(typeof(TileBreakArgs)),             // 包 17
        _ => false,
    };

    /// <summary>
    /// 把网络包映射到对应 Hook 参数并**填充包数据**（扩展点：新增包类型在此注册）。
    /// 填充规则：只填解码器**已提取**的字段；未解码字段保持默认值（各 HookArgs 类型上有说明）。
    /// 注意：不填包数据会让插件基于恒为 0 的字段做判断（如 Damage&gt;上限），静默失效。
    /// </summary>
    private HookArgs? BuildHookArgs(INetworkPacket packet, int playerId)
    {
        // 注意：packet.Type 为 PacketId 枚举（Protocol 层定义）
        HookArgs? args = packet switch
        {
            // 玩家移动（包 13）
            PlayerControlsPacket mv => new PlayerMovingArgs
            {
                PlayerId = playerId,
                ToX = mv.Position.X,
                ToY = mv.Position.Y,
                VelocityX = mv.Velocity.X,
                VelocityY = mv.Velocity.Y,
            },
            // 战斗（包 28）
            NpcStrikePacket strike => new NpcStrikeArgs
            {
                PlayerId = playerId,
                NpcId = strike.NpcId,
                Damage = strike.Damage,
            },
            // 抛射物（包 27）
            ProjectileNewPacket proj => new ProjectileSpawnArgs
            {
                PlayerId = playerId,
                ProjectileId = proj.ProjectileKey,
                Type = proj.ProjectileType,
                X = proj.Position.X,
                Y = proj.Position.Y,
                VelocityX = proj.Velocity.X,
                VelocityY = proj.Velocity.Y,
                Damage = proj.Damage,
            },
            // 物品（包 21 丢弃 / 包 31 开箱）
            ItemDropPacket drop => new ItemDropArgs
            {
                PlayerId = playerId,
                ItemId = drop.ItemId,
                Stack = drop.Stack,
                X = drop.Position.X,
                Y = drop.Position.Y,
            },
            ChestPacket chest => new ItemDropArgs
            {
                PlayerId = playerId,
                X = chest.X,
                Y = chest.Y,
            },
            // 世界（包 79 / 17）
            TilePlacePacket place => new TilePlaceArgs
            {
                PlayerId = playerId,
                X = place.X,
                Y = place.Y,
                TileType = place.TileType,
            },
            TileBreakPacket brk => new TileBreakArgs
            {
                PlayerId = playerId,
                X = brk.X,
                Y = brk.Y,
                TileType = brk.TileType,
            },
            _ => null
        };

        if (args is not null)
            args.PlayerName = _playerNameResolver?.Invoke(playerId) ?? "";

        return args;
    }
}
