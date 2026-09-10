// TerraAuth — 并行组件实装
// 1. WorkerPool        — 网络 I/O 与包解码并行
// 2. DoubleBufferedWorldState — 仿真写 / 快照读 读写分离
// 3. ShardedAuthorityProcessor — 按玩家分片的权威校验并行
// 4. ParallelSnapshotBroadcaster — 每玩家快照序列化并行

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TerraAuth.Concurrency;

#region 1. WorkerPool：网络 I/O + 解码并行
/// <summary>Worker 池：把 CPU 密集的解码/校验从网络 I/O 线程剥离。</summary>
public sealed class WorkerPool : IDisposable
{
    private readonly SemaphoreSlim _throttle;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _workers = new();
    private readonly ConcurrentQueue<Func<CancellationToken, Task>> _queue = new();
    private readonly int _size;

    public int Pending => _queue.Count;

    public WorkerPool(int size)
    {
        _size = size;
        _throttle = new SemaphoreSlim(size, size);
    }

    /// <summary>提交工作项（异步，受信号量限流）。</summary>
    public async Task EnqueueAsync(Func<CancellationToken, Task> work)
    {
        await _throttle.WaitAsync(_cts.Token).ConfigureAwait(false);
        _ = Task.Run(async () =>
        {
            try { await work(_cts.Token).ConfigureAwait(false); }
            finally { _throttle.Release(); }
        }, _cts.Token);
    }

    /// <summary>提交工作项并等待其完成（调用方据此保持逐包顺序）。</summary>
    public async Task EnqueueAndWaitAsync(Func<CancellationToken, Task> work)
    {
        await _throttle.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            await Task.Run(() => work(_cts.Token), _cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>提交同步工作项。</summary>
    public Task Enqueue(Action work) => EnqueueAsync(_ => { work(); return Task.CompletedTask; });

    /// <summary>并行执行一批独立工作（如批量解码）。</summary>
    public Task WhenAllAsync(IEnumerable<Func<CancellationToken, Task>> items)
        => Task.WhenAll(items.Select(EnqueueAsync));

    public void Dispose() => _cts.Cancel();
}
#endregion

#region 2. 双缓冲 WorldState：仿真写 / 快照读 分离
/// <summary>双缓冲状态容器：避免快照序列化与仿真推进互相阻塞。</summary>
/// <remarks>
/// 约定：
///   Logic 线程：GetWriteState() 写，Swap() 交换
///   Snapshot 线程：GetReadonlyState() 读（短暂持锁仅取引用）
/// </remarks>
public sealed class DoubleBufferedWorldState<T> where T : class, new()
{
    private T _front = new();  // 仿真线程写
    private T _back = new();   // 快照线程读
    private volatile int _flip = 0;
    private readonly object _swapLock = new();

    /// <summary>仿真线程获取可写状态。</summary>
    public T GetWriteState() => _flip == 0 ? _front : _back;

    /// <summary>快照线程获取只读状态（短暂持锁取引用）。</summary>
    public T GetReadonlyState()
    {
        lock (_swapLock) { return _flip == 0 ? _back : _front; }
    }

    /// <summary>交换前后缓冲（仿真线程在帧末调用）。</summary>
    public void Swap()
    {
        lock (_swapLock)
        {
            _flip = 1 - _flip;
            // TODO: 增量复制变更（仅复制脏区块，非全量）
            // CopyChanges(_flip == 0 ? _back : _front, _flip == 0 ? _front : _back);
        }
    }
}
#endregion

#region 3. 按玩家分片的权威校验并行
/// <summary>按玩家 ID 分片的权威处理器：不同玩家的校验可并行。</summary>
public sealed class ShardedAuthorityProcessor<TInput, TOutput>
{
    private readonly int _shardCount;
    private readonly Func<int, TInput, TOutput> _process; // (shardIndex, batch) => result

    public ShardedAuthorityProcessor(int shardCount, Func<int, TInput, TOutput> process)
    {
        _shardCount = Math.Max(1, shardCount);
        _process = process;
    }

    /// <summary>按 PlayerId % shardCount 分组后并行处理；返回值与输入下标一一对应（保序）。</summary>
    public async Task<IReadOnlyList<TOutput>> ProcessBatchAsync(IEnumerable<(int PlayerId, TInput Input)> batch)
    {
        var items = batch as IList<(int PlayerId, TInput Input)> ?? batch.ToList();
        var results = new TOutput[items.Count];

        // 按分片分组（同一玩家的多个包必须顺序处理）；保留输入下标以回填保序结果
        var groups = items
            .Select((item, index) => (Item: item, Index: index))
            .GroupBy(x => x.Item.PlayerId % _shardCount);

        // 并行处理每个分片
        var tasks = groups.Select(async group =>
        {
            // 同一分片内顺序处理
            foreach (var (item, index) in group)
            {
                results[index] = await Task.Run(() => _process(group.Key, item.Input)).ConfigureAwait(false);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
#endregion

#region 4. 并行快照广播
/// <summary>并行快照广播：每个玩家的快照生成/编码相互独立。</summary>
public sealed class ParallelSnapshotBroadcaster<TSnapshot>
{
    private readonly Func<int, TSnapshot> _buildSnapshot; // playerId => snapshot
    private readonly Func<TSnapshot, byte[]> _encode;     // snapshot => bytes
    private readonly Func<int, byte[], Task> _send;        // playerId, bytes => send
    private readonly SemaphoreSlim _throttle;

    public ParallelSnapshotBroadcaster(
        Func<int, TSnapshot> buildSnapshot,
        Func<TSnapshot, byte[]> encode,
        Func<int, byte[], Task> send,
        int maxConcurrency)
    {
        _buildSnapshot = buildSnapshot;
        _encode = encode;
        _send = send;
        _throttle = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>对所有在线玩家并行生成+编码+发送快照。</summary>
    public async Task FlushAllAsync(IEnumerable<int> playerIds, CancellationToken ct)
    {
        // 生成 + 编码属 CPU 密集工作：投递线程池执行，避免阻塞调用方（网络/仿真线程）
        var tasks = playerIds.Select(playerId => Task.Run(async () =>
        {
            await _throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var snap = _buildSnapshot(playerId);
                var bytes = _encode(snap);
                await _send(playerId, bytes).ConfigureAwait(false);
            }
            finally { _throttle.Release(); }
        }, ct));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
#endregion
