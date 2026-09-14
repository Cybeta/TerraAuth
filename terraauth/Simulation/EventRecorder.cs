// TerraAuth — Phase 3: 事件记录器（事件溯源）
// 架构 §4.3：所有状态变更以 Command + Event 形式记录

using System.Collections.Concurrent;

namespace TerraAuth.Simulation;

/// <summary>
/// 事件类别：区分「状态提交 / 持久化 / 广播 / 失败」，避免四类语义混在同一个 <c>Kind</c> 字符串里。
/// </summary>
public enum GameEventCategory
{
    /// <summary>权威状态已提交（<c>Command.Apply</c> 成功）。</summary>
    StateCommit,
    /// <summary>持久化结果（图格 / 箱子等增量落盘）。</summary>
    Persistence,
    /// <summary>出站广播结果（失败表示已重新排队，等待下次重试）。</summary>
    Broadcast,
    /// <summary>命令 / 权威校验失败。</summary>
    Failure,
}

/// <summary>事件种类常量（服务端内部事件名，非协议字段）。</summary>
public static class GameEventKinds
{
    /// <summary>命令在提交阶段被拒绝。</summary>
    public const string CommandFailed = "command_failed";
    /// <summary>出站广播失败（实体 / 箱子更新，已重新排队）。</summary>
    public const string BroadcastFailed = "broadcast_failed";
    /// <summary>增量落盘失败（已重新排队）。</summary>
    public const string PersistFailed = "persist_failed";
    /// <summary>弹幕命中结算。</summary>
    public const string ProjectileHit = "projectile_hit";
    /// <summary>液体混合反应。</summary>
    public const string LiquidMerge = "liquid_merge";
}

/// <summary>
/// 命令失败原因（<see cref="CommandApplyResult.Reason"/> 的唯一来源）。
/// 这些字符串同时出现在仿真事件与指标里，故集中定义，避免各命令各写一份字面量。
/// </summary>
public static class CommandFailures
{
    /// <summary>命令未携带玩家（服务端命令不应走玩家路径）。</summary>
    public const string MissingPlayer = "missing_player";
    /// <summary>槽位不存在或玩家已离线 / 死亡。</summary>
    public const string PlayerNotActive = "player_not_active";
    /// <summary>命令携带的会话标识与当前占用该槽位的连接不一致。</summary>
    public const string StaleSession = "stale_session";
    /// <summary>位置非法（非有限数）。</summary>
    public const string InvalidPosition = "invalid_position";
    /// <summary>通用「未生效」（前置状态不满足）。</summary>
    public const string NotApplied = "not_applied";
    /// <summary>前置状态未发生变化（幂等命令重复提交）。</summary>
    public const string NoChange = "no_change";
    /// <summary>背包服务不可用。</summary>
    public const string InventoryUnavailable = "inventory_unavailable";
    /// <summary>背包空间不足。</summary>
    public const string InventoryFull = "inventory_full";
    /// <summary>目标超出交互距离。</summary>
    public const string OutOfReach = "out_of_reach";
    /// <summary>世界掉落物槽位不存在或已失效。</summary>
    public const string ItemNotFound = "item_not_found";
    /// <summary>箱子不存在。</summary>
    public const string ChestNotFound = "chest_not_found";
    /// <summary>玩家尚未建立该箱子的打开会话。</summary>
    public const string ChestNotOpen = "chest_not_open";
    /// <summary>箱子槽位越界。</summary>
    public const string InvalidSlot = "invalid_slot";
    /// <summary>目标弹幕不存在。</summary>
    public const string ProjectileNotFound = "projectile_not_found";
    /// <summary>目标实体不属于该玩家。</summary>
    public const string NotOwner = "not_owner";
    /// <summary>包 117 上报伤害超出服务端权威上界（接触者基础伤害 × 浮动 × 减防）。</summary>
    public const string HurtDamageAboveLimit = "hurt_damage_above_limit";
}

/// <summary>游戏事件（状态变更的结果）。</summary>
public sealed record GameEvent(
    long Tick,
    int? PlayerId,
    string Kind,
    object? Payload,
    GameEventCategory Category = GameEventCategory.StateCommit);

/// <summary>事件记录器：追加式，供审计 / 回放 / 调试。</summary>
public sealed class EventRecorder
{
    private readonly ConcurrentBag<GameEvent> _events = new();

    public void Record(GameEvent e) => _events.Add(e);
    public IReadOnlyCollection<GameEvent> All => _events;
    public void Clear() => _events.Clear();
}
