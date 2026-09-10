// TerraAuth — Phase 4: 快照广播 + 客户端预测
// 架构 §4.4：强类型快照、增量编码、影子预测反作弊

using System.Buffers;           // ArrayBufferWriter<byte>
using System.Collections.Concurrent;
using System.Collections.Generic;
using TerraAuth.Authority;
using TerraAuth.Concurrency;   // ParallelSnapshotBroadcaster
using TerraAuth.Net.Phase5;    // ISnapshotSender / IPacketEncoder（出站发送 + 编码抽象）
using TerraAuth.Protocol;
using TerraAuth.Simulation;

namespace TerraAuth.Net.Phase4;

/// <summary>快照配置。</summary>
public sealed record SnapshotConfig
{
    public int TickHz { get; init; } = 20;           // 下发频率
    public int MaxEntitiesPerPacket { get; init; } = 256;
    public int FullSnapshotIntervalTicks { get; init; } = 600; // 每 30s 发一次全量
    public float ShadowPredictionMaxDeviation { get; init; } = 8f; // 像素

    /// <summary>快照视野半径（像素）：实体超出该范围则不下发；0 = 不裁剪。</summary>
    public float ViewportRadius { get; init; } = 2000f;

    // ---- 影子预测重放模型（服务端权威约束）----
    /// <summary>重放时单 tick 允许的最大位移（像素）；客户端声明的位移超过即被钳制。</summary>
    public float ShadowPredictionMaxSpeedPerTick { get; init; } = 24f;
    /// <summary>每玩家待重放输入队列上限（超出丢弃最旧），防止恶意客户端撑爆内存。</summary>
    public int ShadowPredictionMaxPendingInputs { get; init; } = 600;
    /// <summary>单个输入重放的 tick 数上限，防止 DeltaTicks 被伪造为极大值。</summary>
    public int ShadowPredictionMaxReplayTicks { get; init; } = 600;

    // 注：ServerConfig → SnapshotConfig 的映射属组合根（Core/GameHost）职责，
    // 不在此处提供 FromServerConfig，避免 Net 层反向依赖 Core 的 Config。
}

// 快照数据结构（EntityStateType / EntityState / RemovedEntity / RemoveReason / SnapshotFrame）
// 已下沉至 TerraAuth.Simulation（见 Simulation/SnapshotFrame.cs），此处仅保留网络侧编解码与广播。

/// <summary>快照构建器：从 WorldState 产出 SnapshotFrame（差值编码）。</summary>
public sealed class SnapshotBuilder
{
    private readonly WorldState _world;
    private readonly SnapshotFrame? _previous;

    public SnapshotBuilder(WorldState world, SnapshotFrame? previous = null)
        => (_world, _previous) = (world, previous);

    public SnapshotFrame Build()
    {
        // 从 WorldState 提取玩家实体，并与上一帧做增量（BaseTick + 真实 xxHash32）
        return SnapshotFrame.BuildDelta(_world, _previous);
    }
}

/// <summary>快照广播器：仿真层每 tick 提交 → 网络层下发。</summary>
public sealed class SnapshotBroadcaster
{
    private readonly WorldState _world;
    private readonly CommandQueue _commands;
    private readonly SnapshotConfig _config;
    private readonly ISnapshotSender _sender;
    private readonly IPacketEncoder _encoder;
    private readonly ParallelSnapshotBroadcaster<SnapshotFrame> _parallel;
    private readonly SnapshotStore _store;
    private readonly ConcurrentDictionary<int, uint> _lastAcked = new();
    private readonly IClientPredictor _predictor;
    private uint _tick;

    /// <summary>
    /// 仿真线程发布的实体视图。接上后，广播线程（独立于仿真线程）构建/裁剪快照时
    /// **不再读活动 WorldState**，从而消除数据竞争；为 null 时回退直读（测试 / 独立场景）。
    /// </summary>
    private readonly IWorldViewProvider? _views;

    /// <summary>
    /// 当前快照 tick：优先取共享 <see cref="SnapshotStore"/> 的最新帧（由仿真线程写入），
    /// 否则回退到 <see cref="Enqueue"/> 记录值（独立 store 的测试场景）。
    /// </summary>
    private uint CurrentTick => _store.LatestOrDefault?.Tick ?? _tick;

    public SnapshotBroadcaster(
        WorldState world,
        CommandQueue commands,
        SnapshotConfig config,
        ISnapshotSender sender,
        IPacketEncoder encoder,
        int maxParallelism = 0,
        SnapshotStore? store = null,
        IWorldViewProvider? views = null)
    {
        _world = world;
        _commands = commands;
        _config = config;
        _sender = sender;
        _encoder = encoder;
        _views = views;
        _predictor = new ShadowPredictor(config);
        // 共享仿真层 store 时，仿真每 tick 写入即成为可下发帧；未传入则自持（测试/独立场景）
        _store = store ?? new SnapshotStore();

        // 每玩家独立生成 + 编码（CPU 密集），并行下发
        // buildSnapshot 按玩家分桶：BaseTick 取该玩家的 lastAcked，并做视野裁剪
        _parallel = new ParallelSnapshotBroadcaster<SnapshotFrame>(
            buildSnapshot: BuildFrameFor,
            encode: Encode,
            send: (playerId, bytes) => _sender.SendEncodedAsync(playerId, bytes).AsTask(),
            maxConcurrency: maxParallelism > 0 ? maxParallelism : Environment.ProcessorCount);
    }

    /// <summary>仿真线程调用：提交本 tick 快照。</summary>
    public void Enqueue(uint tick, SnapshotFrame frame)
    {
        _tick = tick;
        _store.Add(frame);
        // 裁剪已确认的历史（保留追赶窗口）
        CompactHistory();
    }

    /// <summary>网络线程调用：并行生成/编码并下发快照给所有在线玩家。</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_store.Count == 0)
            return; // 尚无快照可发（仿真尚未产出）

        await _parallel.FlushAllAsync(_sender.ActivePlayers, ct).ConfigureAwait(false);
    }

    private byte[] Encode(SnapshotFrame frame)
    {
        var writer = new ArrayBufferWriter<byte>();
        _encoder.EncodeSnapshot(writer, frame);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// 为指定玩家构建快照（P0 #3/#4）：
    /// BaseTick 取该玩家自己的 lastAcked（每玩家分桶），再按玩家视野裁剪实体。
    /// </summary>
    internal SnapshotFrame BuildFrameFor(int playerId)
    {
        uint baseTick = _lastAcked.TryGetValue(playerId, out var acked) ? acked : 0u;

        // 取一致视图：窗口判定与增量归并必须在同一份快照副本上进行，
        // 否则仿真线程的 Add/TrimBefore 会插入其间，导致漏帧或索引越界。
        var frames = _store.Snapshot();

        // 历史窗口不足（裁剪 / 容量淘汰 / 从未确认的落后玩家）→ 回退真全量，
        // 否则会在错误基线上发残缺 delta，客户端将缺失未变化实体。
        if (!CanServeDeltaFrom(frames, baseTick))
            return Cull(BuildFullFrame(), playerId);

        return Cull(MergeSince(frames, baseTick), playerId);
    }

    /// <summary>
    /// 构建全量快照帧：优先取仿真线程发布的不可变实体视图（广播线程不读活动 WorldState）；
    /// 未接入视图提供方时回退直读（测试 / 独立场景）。
    /// </summary>
    private SnapshotFrame BuildFullFrame()
    {
        var view = _views?.CurrentEntityView;
        if (view is not null && !ReferenceEquals(view, WorldEntityView.Empty))
            return SnapshotFrame.Create(view.Tick, view.Entities, Array.Empty<RemovedEntity>(), baseTick: 0);

        return SnapshotFrame.BuildDelta(_world, previous: null);
    }

    /// <summary>
    /// 判断能否从 <paramref name="baseTick"/> 起用连续帧增量重建状态。
    /// 前提：<paramref name="frames"/> 是按 tick 升序的连续 delta 后缀；
    /// 需 baseTick 不超前于最新帧，且 earliest &lt;= baseTick + 1。
    /// </summary>
    private static bool CanServeDeltaFrom(IReadOnlyList<SnapshotFrame> frames, uint baseTick)
    {
        if (frames.Count == 0)
            return false;

        // 用 long 比较，避免 baseTick == uint.MaxValue 时 +1 溢出
        return (long)baseTick <= frames[^1].Tick
            && (long)frames[0].Tick <= (long)baseTick + 1;
    }

    /// <summary>合并 baseTick 之后的所有增量帧，得到「自 baseTick 起」的净变化（Id 维度后写覆盖）。</summary>
    private static SnapshotFrame MergeSince(IReadOnlyList<SnapshotFrame> frames, uint baseTick)
    {
        // 前置条件：调用方已由 CanServeDeltaFrom 确认 frames 非空（空 store 走全量回退）
        uint latestTick = frames[^1].Tick;
        var entities = new Dictionary<int, EntityState>();
        var removed = new Dictionary<int, RemoveReason>();

        foreach (var frame in frames)
        {
            // BaseTick = 0 为「全量」语义（客户端无基线），窗口内所有帧都要归并（含 Tick == 0）；
            // 否则跳过 <= baseTick 的帧（其状态已含在客户端基线中）。
            // 用 long 比较避免 baseTick == uint.MaxValue 时的边界歧义。
            if (baseTick != 0u && (long)frame.Tick <= (long)baseTick)
                continue;

            foreach (var e in frame.Entities)
            {
                entities[e.Id] = e;      // 后写覆盖：取最新状态
                removed.Remove(e.Id);    // 重新出现 → 撤销移除
            }

            foreach (var r in frame.Removed)
            {
                entities.Remove(r.Id);
                removed[r.Id] = r.Reason;
            }
        }

        return SnapshotFrame.Create(
            latestTick,
            entities.Values,
            removed.Select(static kv => new RemovedEntity(kv.Key, kv.Value)),
            baseTick: baseTick);
    }

    /// <summary>视野裁剪：以玩家当前权威位置为中心，超出 ViewportRadius 的实体转为 OutOfRange 移除。</summary>
    private SnapshotFrame Cull(SnapshotFrame frame, int playerId)
    {
        float radius = _config.ViewportRadius;
        if (radius <= 0f || !TryGetViewportCenter(playerId, out var center))
            return frame; // 关闭裁剪 / 玩家尚未进入世界 → 原样下发

        float radiusSquared = radius * radius;

        var kept = new List<EntityState>(frame.Entities.Count);
        var removed = new List<RemovedEntity>(frame.Removed.Count + frame.Entities.Count);
        var removedIds = new HashSet<int>(frame.Removed.Count);

        // 先处理帧内实体：视野内保留，视野外转 OutOfRange；同一 Id 只记一次移除。
        foreach (var e in frame.Entities)
        {
            if (InRange(e.Position, center, radiusSquared))
                kept.Add(e);
            else if (removedIds.Add(e.Id))
                removed.Add(new RemovedEntity(e.Id, RemoveReason.OutOfRange)); // 离开视野 → 通知客户端移除
        }

        // 再合并帧自带的移除项（按 Id 去重，避免重复条目与重复编码）
        foreach (var r in frame.Removed)
        {
            if (removedIds.Add(r.Id))
                removed.Add(r);
        }

        return SnapshotFrame.Create(frame.Tick, kept, removed, baseTick: frame.BaseTick);
    }

    /// <summary>
    /// 取视野裁剪中心（玩家权威坐标）。优先用已发布的实体视图，广播线程因此不触活动 WorldState；
    /// 未接入视图时回退直读（测试 / 独立场景）。
    /// </summary>
    private bool TryGetViewportCenter(int playerId, out Vector2 center)
    {
        var view = _views?.CurrentEntityView;
        if (view is not null && !ReferenceEquals(view, WorldEntityView.Empty))
        {
            // 视图内查不到该玩家 → 不裁剪也不改读活动状态（该玩家尚无 tick 数据）
            if (view.ById.TryGetValue(playerId, out var entity))
            {
                center = entity.Position;
                return true;
            }

            center = default;
            return false;
        }

        if (_world.Players.TryGetValue(playerId, out var player))
        {
            center = player.Position;
            return true;
        }

        center = default;
        return false;
    }

    private static bool InRange(Vector2 position, Vector2 center, float radiusSquared)
    {
        var dx = position.X - center.X;
        var dy = position.Y - center.Y;
        return dx * dx + dy * dy <= radiusSquared;
    }

    /// <summary>客户端输入 → Command（对接 Phase 2/3）。</summary>
    public void SubmitInputs(int playerId, IReadOnlyList<ClientInput> inputs)
    {
        var ordered = inputs.OrderBy(static i => i.Sequence).ToList();
        if (ordered.Count == 0)
            return;

        // 1. 记录输入供影子预测重放
        foreach (var input in ordered)
            _predictor.RecordInput(playerId, input);

        // 2. 以当前权威位置为基准重放，得到服务端认可的目标位置
        //    （Predict 内部按单 tick 速度上限钳制，客户端声明的超速位移会被削平）
        var basePosition = _world.Players.TryGetValue(playerId, out var player)
            ? player.Position
            : new Vector2(0, 0);
        var target = _predictor.Predict(playerId, basePosition);

        // 3. 生成移动命令，交给仿真在下一 tick 应用
        _commands.Enqueue(new MoveCommand((long)CurrentTick + 1, playerId, target));
    }

    /// <summary>客户端确认 → 裁剪历史 + 检测丢包。</summary>
    public void OnAck(int playerId, uint ackedTick)
    {
        var current = CurrentTick;

        // 越界校验：不得确认尚未下发的 tick（防伪造 ack 抬高基址 / 触发历史误裁剪）
        if (ackedTick > current)
            return;

        _lastAcked[playerId] = ackedTick;

        // 落后超过阈值 → 触发追赶：补发自 ackedTick 起的快照序列
        if (_sender is not null && current > ackedTick &&
            current - ackedTick > (uint)_config.FullSnapshotIntervalTicks)
        {
            _ = SendCatchUpAsync(playerId, ackedTick);
        }

        CompactHistory();
    }

    /// <summary>补发落后玩家的快照序列（按 tick 升序）。</summary>
    private async Task SendCatchUpAsync(int playerId, uint fromTick)
    {
        var frames = _store.Snapshot(); // 一致视图，避免补发途中被 TrimBefore 抽帧

        // 历史窗口不足（容量淘汰 / 被裁剪）→ 无法补出连续 delta，改发一帧真全量
        if (!CanServeDeltaFrom(frames, fromTick))
        {
            try
            {
                var full = Cull(SnapshotFrame.BuildDelta(_world, previous: null), playerId);
                await _sender.SendEncodedAsync(playerId, Encode(full)).ConfigureAwait(false);
            }
            catch
            {
                // 追赶失败不阻塞仿真：下一轮 Flush 会重发最新快照
            }
            return;
        }

        foreach (var frame in frames)
        {
            if (frame.Tick < fromTick)
                continue;

            try
            {
                await _sender.SendEncodedAsync(playerId, Encode(frame)).ConfigureAwait(false);
            }
            catch
            {
                // 追赶失败不阻塞仿真：下一轮 Flush 会重发最新快照
                return;
            }
        }
    }

    /// <summary>落后玩家追赶：返回自 lastAcked 起的快照序列。</summary>
    public IEnumerable<SnapshotFrame> GetFullSnapshotFor(int playerId)
    {
        var from = _lastAcked.TryGetValue(playerId, out var acked) ? acked : 0u;
        var frames = _store.Snapshot(); // 一致视图，返回值不随仿真线程写入变化

        // 历史窗口不足 → 返回单帧真全量（BaseTick=0 语义为全量），避免下发残缺 delta
        if (!CanServeDeltaFrom(frames, from))
            return new[] { Cull(SnapshotFrame.BuildDelta(_world, previous: null), playerId) };

        return frames.Where(f => f.Tick >= from);
    }

    private void CompactHistory()
    {
        if (_lastAcked.IsEmpty)
            return;

        // 保留所有已确认玩家中最早 tick（含）之后的历史
        uint oldest = _lastAcked.Values.Min();
        _store.TrimBefore(oldest);
    }

    /// <summary>运行循环：定时 Flush。</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _config.TickHz);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct).ConfigureAwait(false);
            await FlushAsync(ct).ConfigureAwait(false);
        }
    }
}

/// <summary>客户端输入（预测用）。</summary>
public sealed record ClientInput(
    uint Sequence,
    Vector2 MoveDelta,
    bool Jump,
    bool Attack,
    uint DeltaTicks);

/// <summary>客户端预测器接口（客户端预测 + 影子反作弊）。</summary>
public interface IClientPredictor
{
    void RecordInput(int playerId, ClientInput input);
    Vector2 Predict(int playerId, Vector2 authoritativePosition);
    bool IsDeviationSignificant(int playerId, Vector2 reported, Vector2 predicted, out float deviation);
}

/// <summary>影子预测器：服务端重放客户端输入，检测偏离（反作弊）。</summary>
internal sealed class ShadowPredictor : IClientPredictor
{
    private readonly ConcurrentDictionary<int, ConcurrentQueue<ClientInput>> _inputs = new();
    private readonly float _maxDeviation;
    private readonly float _maxSpeedPerTick;
    private readonly int _maxPendingInputs;
    private readonly int _maxReplayTicks;

    public ShadowPredictor() : this(new SnapshotConfig()) { }

    public ShadowPredictor(SnapshotConfig config)
    {
        _maxDeviation = config.ShadowPredictionMaxDeviation;
        _maxSpeedPerTick = config.ShadowPredictionMaxSpeedPerTick;
        _maxPendingInputs = config.ShadowPredictionMaxPendingInputs;
        _maxReplayTicks = config.ShadowPredictionMaxReplayTicks;
    }

    public void RecordInput(int playerId, ClientInput input)
    {
        var queue = _inputs.GetOrAdd(playerId, _ => new ConcurrentQueue<ClientInput>());
        queue.Enqueue(input);

        // 限长：丢弃最旧输入，避免恶意客户端无限堆积
        while (queue.Count > _maxPendingInputs && queue.TryDequeue(out _))
        {
        }
    }

    /// <summary>用客户端输入序列重放，预测其应处位置；本次重放会消费已处理的输入。</summary>
    public Vector2 Predict(int playerId, Vector2 authoritativePosition)
    {
        if (!_inputs.TryGetValue(playerId, out var queue))
            return authoritativePosition;

        var pending = new List<ClientInput>();
        while (queue.TryDequeue(out var input))
            pending.Add(input);

        if (pending.Count == 0)
            return authoritativePosition;

        // 按序号重放，保证乱序到达时的确定性
        pending.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

        var position = authoritativePosition;
        foreach (var input in pending)
        {
            var ticks = Math.Clamp((int)input.DeltaTicks, 1, _maxReplayTicks);
            var step = ClampSpeed(input.MoveDelta, _maxSpeedPerTick);
            position = new Vector2(
                position.X + step.X * ticks,
                position.Y + step.Y * ticks);
        }
        return position;
    }

    /// <summary>偏差检测：报告位置 vs 预测位置。</summary>
    public bool IsDeviationSignificant(
        int playerId,
        Vector2 reported,
        Vector2 predicted,
        out float deviation)
    {
        var dx = reported.X - predicted.X;
        var dy = reported.Y - predicted.Y;
        deviation = MathF.Sqrt(dx * dx + dy * dy);
        return deviation > _maxDeviation;
    }

    /// <summary>把单 tick 位移钳制到服务端允许的最大速度（保持方向）。</summary>
    private static Vector2 ClampSpeed(Vector2 delta, float maxSpeed)
    {
        var length = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (length <= maxSpeed || length == 0f)
            return delta;

        var scale = maxSpeed / length;
        return new Vector2(delta.X * scale, delta.Y * scale);
    }
}
