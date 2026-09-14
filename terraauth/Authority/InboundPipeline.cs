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
    public int MaxLiquidPerSecond { get; init; } = 60;
}

// ---------- 包上下文 ----------

public sealed record PacketContext(int PlayerId, long Tick, DateTimeOffset ReceivedAt, long SessionId = 0) : IPacketContext;

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

/// <summary>阶段可选的「按玩家重置」能力：连接结束时清理该玩家的权威状态。</summary>
public interface IResettableStage
{
    void ResetPlayer(int playerId);
    void ResetPlayer(int playerId, long sessionId) => ResetPlayer(playerId);
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
        // 「仅 Playing 连接的包会进入管线」由网络层保证（NetworkHost.HandleConnectionStateAsync：
        // 握手包就地消费、非 Playing 的包直接丢弃）。此处只做权威层自身的兜底防御：
        // 未分配 PlayerId（≤0）的上下文一律静默丢弃，避免无身份包进入后续子系统。
        if (context.PlayerId <= 0)
            return Task.FromResult(AuthorityResult.RejectSilent());

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
        var result = _rate.Check(context, packet);
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

public sealed class MovementAuthorityStage : IPipelineStage, IResettableStage
{
    private readonly IMovementAuthority _move;
    private readonly IAuditLogger _audit;
    public int Order => 40;
    public MovementAuthorityStage(IMovementAuthority move, IAuditLogger audit)
        => (_move, _audit) = (move, audit);

    public void ResetPlayer(int playerId) => _move.ResetPlayer(playerId);
    public void ResetPlayer(int playerId, long sessionId) => _move.ResetPlayer(playerId, sessionId);

    public void BindSession(int playerId, long sessionId) => _move.BindSession(playerId, sessionId);

    public async Task<AuthorityResult> ExecuteAsync(INetworkPacket packet, IPacketContext context,
        Func<INetworkPacket, Task<AuthorityResult>> next, CancellationToken ct)
    {
        _move.BindSession(context.PlayerId, context.SessionId);
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
        var result = _world.Validate(packet, context.PlayerId, null!, context.SessionId);
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
        if (command is not null) command = command with { SessionId = context.SessionId };
        return Task.FromResult(AuthorityResult.Accept(packet, command));
    }

    private static Command? CreateCommand(INetworkPacket packet, IPacketContext context) => packet switch
    {
        // 包 13 PlayerControls（真实线格式）→ 移动指令
        //   ControlBits bit2 = 左 / bit3 = 右 / bit4 = 跳（与客户端 Player.cs 的 control* 位一致）；
        //   控制位随指令带给仿真，由服务端自己推进玩家物理（原版服务端对远端玩家也跑 Player.Update）。
        PlayerControlsPacket controls => new MoveCommand(context.Tick, context.PlayerId, controls.Position)
        {
            ControlBits = controls.ControlBits,
            ReportedVelocity = (controls.StateBits & PlayerControlsPacket.StateBitHasVelocity) != 0
                ? controls.Velocity
                : null,
            SelectedItem = controls.SelectedItem, // 手持热键槽（阶段 E 近战武器校验据此定位手持武器）
        },
        // 包 65 TeleportEntity：本玩家带落点的传送 → 移动指令（bit2 无位置时由服务端自持位置，不生成）
        TeleportEntityPacket teleport when !teleport.NoPosition
            && teleport.EntityId == context.PlayerId
            && (teleport.Kind == TeleportEntityKind.Player || teleport.Kind == TeleportEntityKind.PlayerToPlayer)
            => new MoveCommand(context.Tick, context.PlayerId, teleport.Position),
        // 内部简化的位置包（权威层纠偏等内部构造，非线格式）
        PlayerPositionPacket pos => new MoveCommand(context.Tick, context.PlayerId, pos.Position),
        // 包 17 TileManipulation（action 19 = Actuate）→ 电路触发（服务端沿电线传播并翻转执行器）
        TileBreakPacket { Action: 19 } actuate =>
            new ActuateCommand(context.Tick, context.PlayerId, actuate.X, actuate.Y),
        // 包 17 TileManipulation → 挖砖 / 改砖指令（action 语义见 TileBreakCommand.Apply）
        TileBreakPacket brk => new TileBreakCommand(context.Tick, context.PlayerId, brk.X, brk.Y, brk.Action, brk.TileType),
        // 包 79 PlaceObject → 放砖指令
        TilePlacePacket place => new TilePlaceCommand(context.Tick, context.PlayerId, place.X, place.Y, place.TileType, place.Style),
        // 包 28 DamageNPC → NPC 受击指令（服务端扣血，生命归零即死亡）
        NpcStrikePacket strike => new NpcStrikeCommand(
            context.Tick, context.PlayerId, strike.NpcId, strike.Damage, strike.Generation, strike.Crit),
        // 包 21 SyncItem → 掉落物生成（服务端分配槽位）
        ItemDropPacket drop => new SpawnItemCommand(context.Tick, context.PlayerId,
            drop.ItemId, drop.Stack, drop.Position, drop.Velocity, drop.Prefix),
        // 包 22 SyncItemOwner → 物品拾取（服务端移除世界实体并入库）
        ItemPickupPacket pickup => new PickupItemCommand(context.Tick, context.PlayerId, pickup.ItemSlotIndex),
        // 包 5 InventorySlot → 物品栏槽位写入（SSC 服务端唯一真相；装备区防御由此回填 Defense，前缀用于近战武器校验）
        InventorySlotPacket slot => new SetInventorySlotCommand(context.Tick, context.PlayerId,
            slot.Slot, slot.ItemId, slot.Stack, slot.Prefix),
        // 包 31 Chest → 建立服务端箱子会话；坐标为负视为「关闭箱子」（原版客户端关闭时本地清 chest，
        // 不发包，故这里兼容部分客户端 / 工具发出的负坐标关闭请求）
        ChestPacket { X: < 0 } => new CloseChestCommand(context.Tick, context.PlayerId),
        ChestPacket { Y: < 0 } => new CloseChestCommand(context.Tick, context.PlayerId),
        ChestPacket chest => new OpenChestCommand(context.Tick, context.PlayerId, chest.X, chest.Y),
        // 包 32 SyncChestItem → 箱子内物品写入（服务端持有箱子内容唯一真相）
        SyncChestItemPacket chestItem => new SyncChestItemCommand(context.Tick, context.PlayerId,
            chestItem.ChestIndex, chestItem.ItemSlot, chestItem.Stack, chestItem.Prefix, chestItem.ItemType),
        // 包 82 模块 0（NetLiquid）→ 客户端液体编辑（服务端权威落盘并触发流动）
        LiquidModulePacket { IsClientMessage: true } liquid =>
            new LiquidEditCommand(context.Tick, context.PlayerId, liquid.Changes),
        // 包 117 PlayerHurtV2 → 客户端伤害报告仅校验并记录诊断，服务端不据此扣血
        PlayerHurtV2Packet hurt => new DamagePlayerCommand(context.Tick, context.PlayerId, hurt.Damage),
        // 包 118 PlayerDeathV2 → 服务端死亡结算
        PlayerDeathV2Packet => new KillPlayerCommand(context.Tick, context.PlayerId),
        // 包 35 PlayerHeal → 服务端回血（上限由 HealPlayerCommand 钳制到服务端 HpMax）
        PlayerHealPacket heal => new HealPlayerCommand(context.Tick, context.PlayerId, heal.Amount),
        // 包 42 PlayerMana → 服务端法力跟踪（原版不向他人转发法力）
        PlayerManaPacket mana => new SetManaCommand(context.Tick, context.PlayerId, mana.Mana, mana.MaxMana),
        // 包 50 PlayerBuffs → 服务端持有增益列表
        PlayerBuffsPacket buffs => new SetBuffsCommand(context.Tick, context.PlayerId, buffs.BuffTypes),
        // 包 12 PlayerSpawn（Playing 阶段）→ 复活请求（服务端划定复活点）
        PlayerSpawnPacket => new RespawnCommand(context.Tick, context.PlayerId),
        // 包 27 SyncProjectile → 弹幕生成 / 更新（服务端登记生命周期）
        ProjectileNewPacket proj => new SpawnProjectileCommand(context.Tick, context.PlayerId,
            proj.ProjectileKey, proj.ProjectileType, proj.Position, proj.Velocity, proj.Damage),
        // 包 29 KillProjectile → 弹幕销毁（仅归属者可销毁）
        ProjectileDestroyPacket kill => new KillProjectileCommand(context.Tick, context.PlayerId,
            kill.ProjectileKey, kill.Position),
        _ => null,
    };
}

// ---------- 管线执行器 ----------

public sealed class InboundPipeline : IInboundPipeline
{
    private readonly IPipelineStage[] _stages;
    private readonly Func<long> _currentTick;

    public InboundPipeline(IEnumerable<IPipelineStage> stages, Func<long>? currentTick = null)
    {
        _stages = stages.OrderBy(s => s.Order).ToArray();
        _currentTick = currentTick ?? (() => 0);
    }

    /// <summary>连接结束：把「按玩家重置」转发给实现了 <see cref="IResettableStage"/> 的阶段。</summary>
    public void ResetPlayer(int playerId) => ResetPlayer(playerId, 0);

    public void ResetPlayer(int playerId, long sessionId)
    {
        foreach (var stage in _stages)
        {
            if (stage is IResettableStage resettable)
                resettable.ResetPlayer(playerId, sessionId);
        }
    }

    public Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet,
        int playerId,
        CommandQueue commands,
        CancellationToken ct = default)
        => ProcessAsync(packet, playerId, commands, ct, 0);

    public async Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet,
        int playerId,
        CommandQueue commands,
        CancellationToken ct,
        long sessionId)
    {
        var context = new PacketContext(playerId, _currentTick(), DateTimeOffset.UtcNow, sessionId);
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
