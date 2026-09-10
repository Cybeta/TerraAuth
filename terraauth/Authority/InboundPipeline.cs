// TerraAuth — Phase 2: 管线阶段实现 + 限流配置
// 架构 §3.2 / §4.2：统一入站管线

using System.Collections.Concurrent;
using TerraAuth.Protocol;
using TerraAuth.Simulation;

namespace TerraAuth.Authority;

// ---------- 限流配置 ----------

public sealed class RateLimits
{
    public int MaxPacketsPerSecond { get; init; } = 120;
    public int MaxTileBreakPerSecond { get; init; } = 200;
    public int MaxTilePlacePerSecond { get; init; } = 40;
    public int MaxProjectilesPerSecond { get; init; } = 60;
    public int MaxChatPerMinute { get; init; } = 30;
}

// ---------- 包上下文 ----------

public sealed record PacketContext(int PlayerId, long Tick, DateTimeOffset ReceivedAt) : IPacketContext;

// ---------- 管线阶段 ----------

public interface IPipelineStage
{
    int Order { get; }
    Task<AuthorityResult> ExecuteAsync(
        INetworkPacket packet,
        IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next,
        CancellationToken ct);
}

public sealed class FrameStage : IPipelineStage
{
    public int Order => 0;
    public Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
        => next(packet); // 分帧已在 NetworkHost 完成
}

public sealed class ConnectionStateStage : IPipelineStage
{
    public int Order => 10;
    public Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        // TODO: 校验 context.PlayerId 已认证
        return next(packet);
    }
}

public sealed class RateLimitStage : IPipelineStage
{
    private readonly IRateAuthority _rate;
    public int Order => 20;
    public RateLimitStage(IRateAuthority rate) => _rate = rate;

    public async Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        var result = _rate.Check(context, packet.Type);
        if (result.Decision == AuthorityDecision.Reject)
            return result;
        return await next(packet).ConfigureAwait(false);
    }
}

public sealed class InventoryAuthorityStage : IPipelineStage
{
    private readonly IInventoryAuthority _inv;
    private readonly IAuditLogger _audit;
    public int Order => 30;
    public InventoryAuthorityStage(IInventoryAuthority inv, IAuditLogger audit)
        => (_inv, _audit) = (inv, audit);

    public async Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        var result = _inv.Validate(packet, context.PlayerId, null!);
        if (result.Decision != AuthorityDecision.Accept) return result;
        return await next(packet).ConfigureAwait(false);
    }
}

public sealed class MovementAuthorityStage : IPipelineStage
{
    private readonly IMovementAuthority _move;
    private readonly IAuditLogger _audit;
    public int Order => 40;
    public MovementAuthorityStage(IMovementAuthority move, IAuditLogger audit)
        => (_move, _audit) = (move, audit);

    public async Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        var result = _move.Validate(packet, context.PlayerId, null!);
        if (result.Decision != AuthorityDecision.Accept) return result;
        return await next(packet).ConfigureAwait(false);
    }
}

public sealed class CombatAuthorityStage : IPipelineStage
{
    private readonly ICombatAuthority _combat;
    private readonly IAuditLogger _audit;
    public int Order => 50;
    public CombatAuthorityStage(ICombatAuthority combat, IAuditLogger audit)
        => (_combat, _audit) = (combat, audit);

    public async Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        var result = _combat.Validate(packet, context.PlayerId, null!);
        if (result.Decision != AuthorityDecision.Accept) return result;
        return await next(packet).ConfigureAwait(false);
    }
}

public sealed class PlayerAuthorityStage : IPipelineStage
{
    private readonly IPlayerAuthority _player;
    private readonly IAuditLogger _audit;
    public int Order => 60;
    public PlayerAuthorityStage(IPlayerAuthority player, IAuditLogger audit)
        => (_player, _audit) = (player, audit);

    public async Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        var result = _player.Validate(packet, context.PlayerId, null!);
        if (result.Decision != AuthorityDecision.Accept) return result;
        return await next(packet).ConfigureAwait(false);
    }
}

public sealed class WorldAuthorityStage : IPipelineStage
{
    private readonly IWorldAuthority _world;
    private readonly IAuditLogger _audit;
    public int Order => 70;
    public WorldAuthorityStage(IWorldAuthority world, IAuditLogger audit)
        => (_world, _audit) = (world, audit);

    public async Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        var result = _world.Validate(packet, context.PlayerId, null!);
        if (result.Decision != AuthorityDecision.Accept) return result;
        return await next(packet).ConfigureAwait(false);
    }
}

/// <summary>终结阶段：把已通过校验的包翻译为 Command（架构 §4.2）。</summary>
public sealed class TerminalStage : IPipelineStage
{
    public int Order => 1000;
    public Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        // 管线通过 → 生成 Command（由 InboundPipeline 写入 CommandQueue）
        var command = CreateCommand(packet, context);
        return Task.FromResult(AuthorityResult.Accept(packet, command));
    }

    private static Command? CreateCommand(INetworkPacket packet, IPacketContext context) => packet switch
    {
        // 包 13 PlayerControls（真实线格式）→ 移动指令
        PlayerControlsPacket controls => new MoveCommand(context.Tick, context.PlayerId, controls.Position),
        // 包 65 TeleportEntity：本玩家带落点的传送 → 移动指令（bit2 无位置时由服务端自持位置，不生成）
        TeleportEntityPacket teleport when !teleport.NoPosition
            && teleport.EntityId == context.PlayerId
            && (teleport.Kind == TeleportEntityKind.Player || teleport.Kind == TeleportEntityKind.PlayerToPlayer)
            => new MoveCommand(context.Tick, context.PlayerId, teleport.Position),
        // 内部简化的位置包（权威层纠偏等内部构造，非线格式）
        PlayerPositionPacket pos => new MoveCommand(context.Tick, context.PlayerId, pos.Position),
        _ => null,
    };
}

// ---------- 管线执行器 ----------

public sealed class InboundPipeline : IInboundPipeline
{
    private readonly IPipelineStage[] _stages;

    public InboundPipeline(IEnumerable<IPipelineStage> stages)
    {
        _stages = stages.OrderBy(s => s.Order).ToArray();
    }

    public async Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet,
        int playerId,
        CommandQueue commands,
        CancellationToken ct = default)
    {
        var context = new PacketContext(playerId, Tick: 0, DateTimeOffset.UtcNow);
        var index = 0;

        async Task<AuthorityResult> Next(INetworkPacket p)
        {
            if (index >= _stages.Length)
                return AuthorityResult.Accept(packet);
            var stage = _stages[index++];
            return await stage.ExecuteAsync(p, context, Next, ct).ConfigureAwait(false);
        }

        var result = await Next(packet).ConfigureAwait(false);

        // 接受 → 将权威层生成的 Command 入队（Phase 3 仿真消费）
        if (result.Decision == AuthorityDecision.Accept && result.Command is not null)
            commands.Enqueue(result.Command);

        return result;
    }
}
