// TerraAuth — Phase 2: 权威层接口
// 架构 §4.2：决策模型 + 管线契约

using TerraAuth.Protocol;
using TerraAuth.Simulation;

namespace TerraAuth.Authority;

/// <summary>权威决策结果。</summary>
public enum AuthorityDecision
{
    /// <summary>接受，生成 Command 入队。</summary>
    Accept,

    /// <summary>拒绝（明确告知，调试用）。</summary>
    Reject,

    /// <summary>拒绝（静默，避免泄露服务端状态）。</summary>
    RejectSilent,

    /// <summary>纠正：服务端权威值覆盖客户端，需下发纠正包。</summary>
    Correct,
}

/// <summary>权威处理结果。</summary>
public sealed record AuthorityResult(
    AuthorityDecision Decision,
    INetworkPacket? Packet,
    Command? Command = null,
    INetworkPacket? CorrectionPacket = null,
    string? Reason = null)
{
    public static AuthorityResult Accept(INetworkPacket? packet, Command? command = null)
        => new(AuthorityDecision.Accept, packet, command);

    public static AuthorityResult Reject(string reason)
        => new(AuthorityDecision.Reject, null, Reason: reason);

    public static AuthorityResult RejectSilent()
        => new(AuthorityDecision.RejectSilent, null);

    public static AuthorityResult Correct(INetworkPacket correction, string reason)
        => new(AuthorityDecision.Correct, null, CorrectionPacket: correction, Reason: reason);
}

/// <summary>包处理上下文。</summary>
public interface IPacketContext
{
    int PlayerId { get; }
    long Tick { get; }
    DateTimeOffset ReceivedAt { get; }
}

/// <summary>入站管线契约（架构 §3.2）。</summary>
public interface IInboundPipeline
{
    Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet,
        int playerId,
        CommandQueue commands,
        CancellationToken ct = default);

    /// <summary>
    /// 连接结束：清理按玩家索引的权威状态（如移动基线），避免槽位复用串号。
    /// 默认无操作；包装型管线（Hook / 分片）需转发到内层。
    /// </summary>
    void ResetPlayer(int playerId) { }
}

// ---------- 六个权威子系统接口 ----------

public interface IPlayerAuthority
{
    AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands);
    int GetMaxHp(int playerId);
    int GetMaxMana(int playerId);
}

public interface IMovementAuthority
{
    AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands);
    float GetMaxSpeedFor(int playerId);

    /// <summary>
    /// 连接结束：清除该玩家索引上的权威状态（移动基线 / 传送频率窗口）。
    /// 必须做，否则槽位复用（含「断线重连拿到同一槽位」）会把上一次会话的位置当作基准，
    /// 使重连玩家的首个位置包被判超速而拒绝，甚至累计违规被踢。
    /// </summary>
    void ResetPlayer(int playerId);
}

public interface ICombatAuthority
{
    AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands);
    int ComputeDamage(int playerId, int targetId);
}

public interface IInventoryAuthority
{
    AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands);
    bool IsValidItem(int itemId);
    /// <summary>单格最大堆叠（阈值唯一来源在 ServerConfig）。</summary>
    int MaxStackSize { get; }
    int GetStackCount(int playerId, int slot);
    void ApplyAuthorizedChange(int playerId, int slot, int delta);
    /// <summary>检查玩家背包（含装备槽）中是否至少有 1 个指定物品。用于 TilePlace 等需要消耗物品的操作。</summary>
    bool HasItem(int playerId, int itemId);
    /// <summary>消耗玩家背包中 1 个指定物品：找到第一个匹配槽位 stack-1，stack 归零则置 ItemId=0。返回 false 表示背包中无此物品。</summary>
    bool ConsumeItem(int playerId, int itemId);
    /// <summary>把物品加入服务端权威背包（优先并入同物品未满堆叠，其次占用空槽）。返回 false 表示背包已满。</summary>
    bool TryAddItem(int playerId, int itemId, int stack);
}

public interface IWorldAuthority
{
    AuthorityResult Validate(INetworkPacket packet, int playerId, CommandQueue commands);
    bool CanPlayerModifyTile(int playerId, int x, int y);
    int GetTileBreakThreshold(int playerId);
}

public interface IRateAuthority
{
    /// <summary>按包类型 / 内容执行限流（需要包内容区分包 82 的不同模块）。</summary>
    AuthorityResult Check(IPacketContext context, INetworkPacket packet);
}

/// <summary>事件存储（事件溯源）。</summary>
public interface IEventStore
{
    void Append(GameEvent e);
    IReadOnlyList<GameEvent> Since(long tick);
    void CompactBefore(long tick);
}
