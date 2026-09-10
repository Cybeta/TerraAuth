// TerraAuth — Phase 3/4: 每 tick 实体视图（跨线程只读发布）
// 快照线程（Phase 4 广播）需要"某一 tick 的一致实体集合"，但不得直接读正在被仿真线程
// 改动的 WorldState：Dictionary 结构变更会破坏并发读，PlayerRuntime.Position 等字段也可能撕裂。
// 故仿真线程在 tick 末把实体集合提取为**不可变视图**并发布，读取端只持有该视图。
//
// 作用域说明：快照路径只读实体（players / Npcs），不涉及图格数组（Tiles）。图格走包 10 / 10x
// 增量下发，其并发面属 P4 空间分区范畴，不在本视图覆盖内。

using System.Collections.Generic;
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>由仿真线程发布、供快照线程读取的实体视图提供方。</summary>
public interface IWorldViewProvider
{
    /// <summary>最近一次 tick 发布的实体视图；尚未发布过时为 <see cref="WorldEntityView.Empty"/>。</summary>
    WorldEntityView CurrentEntityView { get; }
}

/// <summary>
/// 单个 tick 的不可变实体视图（玩家 + NPC），与 <see cref="SnapshotFrame.BuildDelta"/> 共用提取逻辑，
/// 保证快照内容与视图一致。不可变 → 可安全跨线程发布，读取端无需加锁。
/// </summary>
public sealed record WorldEntityView(
    uint Tick,
    IReadOnlyList<EntityState> Entities,
    IReadOnlyDictionary<int, EntityState> ById)
{
    /// <summary>尚未产生任何 tick 时的空视图。</summary>
    public static readonly WorldEntityView Empty =
        new(0, Array.Empty<EntityState>(), new Dictionary<int, EntityState>());

    /// <summary>
    /// 从 <see cref="WorldState"/> 提取实体集合。仅允许仿真线程调用（要求无并发写入）。
    /// </summary>
    public static WorldEntityView Extract(WorldState world)
    {
        var entities = new List<EntityState>(world.Players.Count + world.Npcs.Count);

        foreach (var kvp in world.Players)
        {
            var p = kvp.Value;
            entities.Add(new EntityState(
                kvp.Key, p.Position, p.Velocity,
                p.Active ? EntityStateType.Active : EntityStateType.Hidden));
        }

        // NPC 无速度字段 → Velocity 恒为 0；Id 取负命名空间，与玩家正整数 Id 不冲突
        for (int i = 0; i < world.Npcs.Count; i++)
        {
            var npc = world.Npcs[i];
            entities.Add(new EntityState(
                SnapshotFrame.NpcEntityId(i), new Vector2(npc.X, npc.Y), new Vector2(0, 0),
                EntityStateType.Active));
        }

        var byId = new Dictionary<int, EntityState>(entities.Count);
        foreach (var e in entities)
            byId[e.Id] = e;

        return new WorldEntityView((uint)world.Tick, entities, byId);
    }
}
