// TerraAuth — Phase 3/4: 快照数据结构
// 定义在 Simulation 层：仿真产出快照，Net 层负责编码/下发。
// 这样避免下层（Simulation）反向依赖上层（Net）造成依赖倒置。

using System.Buffers.Binary;
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>实体状态类型。</summary>
public enum EntityStateType : byte
{
    Active = 0,
    Hidden = 1,
    Removed = 2,
}

/// <summary>单个实体的快照状态。</summary>
public sealed record EntityState(
    int Id,
    Vector2 Position,
    Vector2 Velocity,
    EntityStateType State);

/// <summary>被移除的实体（差值编码）。</summary>
public sealed record RemovedEntity(int Id, RemoveReason Reason);

public enum RemoveReason : byte
{
    Despawn = 0,
    OutOfRange = 1,
    Destroyed = 2,
}

/// <summary>强类型快照帧（对接 terraria-protocol 的快照布局）。</summary>
public sealed record SnapshotFrame(
    uint Tick,
    uint BaseTick,       // 增量基址（0 = 全量）
    uint Checksum,       // xxHash32（占位）
    IReadOnlyList<EntityState> Entities,
    IReadOnlyList<RemovedEntity> Removed)
{
    public static SnapshotFrame Create(
        uint tick,
        IEnumerable<EntityState> entities,
        IEnumerable<RemovedEntity> removed,
        uint? baseTick = null,
        uint? checksum = null)
    {
        var list = entities.ToList();
        var rem = removed.ToList();
        return new SnapshotFrame(
            tick,
            baseTick ?? 0,
            checksum ?? ComputeChecksum(list, rem),
            list,
            rem);
    }

    /// <summary>
    /// NPC 实体 Id 命名空间：NPC 用负 Id（<c>-1 - 索引</c>），与玩家正整数 Id 不冲突。
    /// 客户端/解码侧用 <c>Id &lt; 0</c> 判定为 NPC，索引 = <c>-Id - 1</c>。
    /// </summary>
    public static int NpcEntityId(int npcIndex) => -1 - npcIndex;

    /// <summary>
    /// 从 <see cref="WorldState"/> 提取玩家与 NPC 实体，
    /// 并按 <paramref name="previous"/> 做增量：只保留变化实体 + 消失实体列表，BaseTick = 上一帧。
    /// </summary>
    public static SnapshotFrame BuildDelta(WorldState world, SnapshotFrame? previous)
    {
        var current = new List<EntityState>(world.Players.Count + world.Npcs.Count);

        foreach (var kvp in world.Players)
        {
            var p = kvp.Value;
            current.Add(new EntityState(
                kvp.Key, p.Position, p.Velocity,
                p.Active ? EntityStateType.Active : EntityStateType.Hidden));
        }

        // NPC 无速度字段 → Velocity 恒为 0；静态 NPC 会在增量中被 SameState 判为未变化而省略
        for (int i = 0; i < world.Npcs.Count; i++)
        {
            var npc = world.Npcs[i];
            current.Add(new EntityState(
                NpcEntityId(i), new Vector2(npc.X, npc.Y), new Vector2(0, 0),
                EntityStateType.Active));
        }

        List<EntityState> entities;
        List<RemovedEntity> removed;

        if (previous is null)
        {
            // 无基线 → 全量
            entities = current;
            removed = new List<RemovedEntity>();
        }
        else
        {
            var previousById = new Dictionary<int, EntityState>(previous.Entities.Count);
            foreach (var e in previous.Entities)
                previousById[e.Id] = e;

            entities = new List<EntityState>(current.Count);
            var currentIds = new HashSet<int>(current.Count);
            foreach (var e in current)
            {
                currentIds.Add(e.Id);
                if (!previousById.TryGetValue(e.Id, out var old) || !SameState(old, e))
                    entities.Add(e);
            }

            removed = new List<RemovedEntity>();
            foreach (var e in previous.Entities)
                if (!currentIds.Contains(e.Id))
                    removed.Add(new RemovedEntity(e.Id, RemoveReason.Despawn));
        }

        return Create(
            tick: (uint)world.Tick,
            entities: entities,
            removed: removed,
            baseTick: previous?.Tick,
            checksum: ComputeChecksum(entities, removed));
    }

    private static bool SameState(EntityState a, EntityState b)
        => a.State == b.State
           && a.Position.X == b.Position.X
           && a.Position.Y == b.Position.Y
           && a.Velocity.X == b.Velocity.X
           && a.Velocity.Y == b.Velocity.Y;

    /// <summary>
    /// 真实 xxHash32 校验和：对实体（Id/位置/速度/状态）与移除项的规范字节流求哈希。
    /// 确定性：固定字段顺序、固定小端编码，不依赖平台差异。
    /// </summary>
    internal static uint ComputeChecksum(
        IReadOnlyList<EntityState> entities,
        IReadOnlyList<RemovedEntity>? removed = null)
    {
        const int entitySize = 21;   // Id(4) + PosX/PosY/VelX/VelY(16) + State(1)
        const int removedSize = 5;   // Id(4) + Reason(1)

        int size = entities.Count * entitySize + (removed?.Count ?? 0) * removedSize;
        if (size == 0)
            return XxHash32.Hash(ReadOnlySpan<byte>.Empty);

        var buffer = new byte[size];
        var span = buffer.AsSpan();
        int offset = 0;

        foreach (var e in entities)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), e.Id); offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), BitConverter.SingleToUInt32Bits(e.Position.X)); offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), BitConverter.SingleToUInt32Bits(e.Position.Y)); offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), BitConverter.SingleToUInt32Bits(e.Velocity.X)); offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), BitConverter.SingleToUInt32Bits(e.Velocity.Y)); offset += 4;
            span[offset++] = (byte)e.State;
        }

        if (removed is not null)
        {
            foreach (var r in removed)
            {
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), r.Id); offset += 4;
                span[offset++] = (byte)r.Reason;
            }
        }

        return XxHash32.Hash(span);
    }
}

/// <summary>
/// xxHash32（seed = 0）。用于快照丢包容错校验；标准算法实现，保证跨平台/跨进程一致。
/// </summary>
internal static class XxHash32
{
    private const uint Prime1 = 2654435761u;
    private const uint Prime2 = 2246822519u;
    private const uint Prime3 = 3266489917u;
    private const uint Prime4 = 668265263u;
    private const uint Prime5 = 374761393u;

    public static uint Hash(ReadOnlySpan<byte> data, uint seed = 0)
    {
        int length = data.Length;
        int index = 0;
        uint hash;

        if (length >= 16)
        {
            uint v1 = seed + Prime1 + Prime2;
            uint v2 = seed + Prime2;
            uint v3 = seed;
            uint v4 = seed - Prime1;

            int limit = length - 16;
            do
            {
                v1 = Round(v1, ReadUInt32(data, index)); index += 4;
                v2 = Round(v2, ReadUInt32(data, index)); index += 4;
                v3 = Round(v3, ReadUInt32(data, index)); index += 4;
                v4 = Round(v4, ReadUInt32(data, index)); index += 4;
            }
            while (index <= limit);

            hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
        }
        else
        {
            hash = seed + Prime5;
        }

        hash += (uint)length;

        while (index + 4 <= length)
        {
            hash += ReadUInt32(data, index) * Prime3;
            hash = RotateLeft(hash, 17) * Prime4;
            index += 4;
        }

        while (index < length)
        {
            hash += data[index] * Prime5;
            hash = RotateLeft(hash, 11) * Prime1;
            index++;
        }

        hash ^= hash >> 15;
        hash *= Prime2;
        hash ^= hash >> 13;
        hash *= Prime3;
        hash ^= hash >> 16;
        return hash;
    }

    private static uint Round(uint accumulator, uint input)
    {
        accumulator += input * Prime2;
        accumulator = RotateLeft(accumulator, 13);
        accumulator *= Prime1;
        return accumulator;
    }

    private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
}
