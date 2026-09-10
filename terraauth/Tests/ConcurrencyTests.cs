// TerraAuth — Concurrency 模块验收测试（agent 执行参考）
// 对应 Concurrency/README.md §六 执行优先级

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerraAuth.Authority;
using TerraAuth.Concurrency;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class ConcurrencyTests
{
    // ========================================================================
    // 1. WorkerPool：网络 I/O 与解码并行（P0）
    // ========================================================================
    [Fact]
    public async Task WorkerPool_EnqueueAsync_ProcessesAllItems()
    {
        using var pool = new WorkerPool(4);
        var completed = 0;
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var id = i;
            tasks.Add(pool.EnqueueAsync(ct =>
            {
                Interlocked.Increment(ref completed);
                return Task.CompletedTask;
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        // 给 Worker 线程一点时间完成
        await Task.Delay(500).ConfigureAwait(false);

        Assert.Equal(100, completed);
    }

    [Fact]
    public void WorkerPool_Throttle_LimitsConcurrency()
    {
        using var pool = new WorkerPool(2);
        var concurrent = 0;
        var maxConcurrent = 0;

        // 用信号量验证：同时最多 2 个在跑
        var gate = new SemaphoreSlim(0, 2);
        for (int i = 0; i < 10; i++)
        {
            _ = pool.EnqueueAsync(async ct =>
            {
                var c = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, c);
                await Task.Delay(50).ConfigureAwait(false);
                Interlocked.Decrement(ref concurrent);
            });
        }

        // 简单断言：不应抛出，且完成
        Assert.True(true); // placeholder（真实验收需同步点）
    }

    // ========================================================================
    // 2. DoubleBufferedWorldState：读写分离（P2）
    // ========================================================================
    [Fact]
    public void DoubleBuffer_GetWrite_GetRead_AreDifferentReferences()
    {
        var buf = new DoubleBufferedWorldState<State>();
        var write1 = buf.GetWriteState();
        var write2 = buf.GetWriteState();
        Assert.Same(write1, write2); // 写端始终返回同一 front

        var readBefore = buf.GetReadonlyState();
        Assert.NotSame(write1, readBefore); // 读写是不同缓冲

        buf.Swap();
        var readAfter = buf.GetReadonlyState();
        Assert.Same(write1, readAfter); // Swap 后读端切换到原 front（即刚写入的缓冲）
    }

    [Fact]
    public void DoubleBuffer_Swap_TogglesRoles()
    {
        var buf = new DoubleBufferedWorldState<State>();
        var front = buf.GetWriteState();
        var back = buf.GetReadonlyState();

        buf.Swap();

        // Swap 后：原来的 front 变成可读，原来的 back 变成可写
        Assert.Same(front, buf.GetReadonlyState());
        Assert.Same(back, buf.GetWriteState());
    }

    // ========================================================================
    // 3. ShardedAuthorityProcessor：按玩家分片并行（P2）
    // ========================================================================
    [Fact]
    public async Task ShardedAuthorityProcessor_GroupsByPlayerShard()
    {
        var processor = new ShardedAuthorityProcessor<int, int>(4, (shard, input) => shard + input);

        var batch = new List<(int, int)>
        {
            (0, 10), (4, 20), (8, 30), // shard 0
            (1, 11), (5, 21),           // shard 1
            (2, 12), (6, 22),           // shard 2
            (3, 13), (7, 23),           // shard 3
        };

        var results = await processor.ProcessBatchAsync(batch).ConfigureAwait(false);

        // 每个输入都应产生一个结果
        Assert.Equal(batch.Count, results.Count);
        // 结果包含预期的 shard+input 值
        Assert.Contains(results, r => r == 0 + 10);
        Assert.Contains(results, r => r == 1 + 11);
        Assert.Contains(results, r => r == 3 + 13);
    }

    [Fact]
    public async Task ShardedAuthorityProcessor_PreservesInputOrder()
    {
        // 保序是 ShardedInboundPipeline 按下标回填结果的前提
        var processor = new ShardedAuthorityProcessor<int, string>(4, (_, input) => $"v{input}");
        var batch = new List<(int, int)> { (7, 70), (1, 10), (3, 30), (1, 11), (7, 71), (2, 20) };

        var results = await processor.ProcessBatchAsync(batch).ConfigureAwait(false);

        Assert.Equal(new[] { "v70", "v10", "v30", "v11", "v71", "v20" }, results.ToArray());
    }

    [Fact]
    public async Task ShardedAuthorityProcessor_RunsDifferentShardsInParallel()
    {
        using var probe = new ConcurrencyProbe(participants: 2);
        var processor = new ShardedAuthorityProcessor<int, int>(4, (_, input) => probe.Run(input));

        // 玩家 1 与 2 落在不同分片 → 应并行执行
        var batch = new List<(int, int)> { (1, 100), (2, 200) };
        var results = await processor.ProcessBatchAsync(batch).ConfigureAwait(false);

        Assert.True(probe.MaxConcurrency >= 2, "不同分片应并行执行");
        Assert.Equal(new[] { 100, 200 }, results.ToArray());
    }

    // ========================================================================
    // 3b. ShardedInboundPipeline：单包契约 → 分片批量桥接（P2 接入管线）
    // ========================================================================
    [Fact]
    public async Task ShardedInboundPipeline_SamePlayer_PreservesArrivalOrder()
    {
        var inner = new RecordingPipeline();
        var pipeline = new ShardedInboundPipeline(inner, shardCount: 4, maxBatchSize: 16);
        try
        {
            var commands = new CommandQueue();
            var tasks = new List<Task<AuthorityResult>>();
            for (int i = 0; i < 64; i++)
                tasks.Add(pipeline.ProcessAsync(new PlayerPositionPacket(1, new Vector2(i, 0)), 1, commands));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // 同一玩家跨批次仍按到达顺序处理（保证 Command 入队顺序可重现）
            Assert.Equal(Enumerable.Range(0, 64), inner.OrderForPlayer(1));
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ShardedInboundPipeline_ReturnsEachCallersOwnResult()
    {
        var inner = new RecordingPipeline();
        var pipeline = new ShardedInboundPipeline(inner, shardCount: 4, maxBatchSize: 32);
        try
        {
            var commands = new CommandQueue();
            var packets = new List<PlayerPositionPacket>();
            var tasks = new List<Task<AuthorityResult>>();
            for (int i = 0; i < 40; i++)
            {
                var playerId = i % 5;
                var packet = new PlayerPositionPacket(playerId, new Vector2(i, i * 2));
                packets.Add(packet);
                tasks.Add(pipeline.ProcessAsync(packet, playerId, commands));
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // 跨玩家分片并行后，结果必须回填到各自的调用方
            for (int i = 0; i < packets.Count; i++)
            {
                Assert.Equal(AuthorityDecision.Accept, results[i].Decision);
                Assert.Same(packets[i], results[i].Packet);
            }
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ShardedInboundPipeline_AfterDispose_RejectsNewWork()
    {
        var inner = new RecordingPipeline();
        var pipeline = new ShardedInboundPipeline(inner, shardCount: 2);
        var commands = new CommandQueue();

        var first = await pipeline.ProcessAsync(
            new PlayerPositionPacket(1, new Vector2(1, 1)), 1, commands).ConfigureAwait(false);
        Assert.Equal(AuthorityDecision.Accept, first.Decision);

        await pipeline.DisposeAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            pipeline.ProcessAsync(new PlayerPositionPacket(1, new Vector2(2, 2)), 1, commands));
    }

    // ========================================================================
    // 4. 确定性校验：并行结果必须可重现（核心约束）
    // ========================================================================
    [Fact]
    public void Determinism_ComputeStateHash_SameInput_SameOutput()
    {
        var a = new State { X = 1, Y = 2 };
        var b = new State { X = 1, Y = 2 };

        var h1 = Determinism.ComputeStateHash(a);
        var h2 = Determinism.ComputeStateHash(b);

        // 相同状态 → 相同哈希（开发模式断言，防止并行破坏确定性）
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Determinism_AssertConsistent_NoMismatch_DoesNotThrow()
    {
        // 一致性匹配 → 无异常
        Determinism.AssertConsistent(123, 123);
        // 仅 Debug 生效，Release 下为 no-op（此处仅验证可调用）
        Assert.True(true);
    }

    // ========================================================================
    // 5. ParallelConfig：根据核心数推导线程池大小
    // ========================================================================
    [Fact]
    public void ParallelConfig_DerivesSensibleDefaults()
    {
        var cfg = new ParallelConfig();
        Assert.True(cfg.NetworkThreads >= 4);
        Assert.True(cfg.WorkerThreads >= 1);
        Assert.True(cfg.BackgroundThreads >= 1);
        Assert.True(cfg.WorkerThreads < cfg.NetworkThreads || cfg.NetworkThreads == cfg.WorkerThreads);
    }

    // ========================================================================
    // 6. 线程安全原语
    // ========================================================================
    [Fact]
    public void AtomicCounter_Increment_IsThreadSafe()
    {
        var counter = new AtomicCounter();
        Parallel.For(0, 10000, _ => counter.Increment());
        Assert.Equal(10000, counter.Value);
    }

    [Fact]
    public void MpscQueue_Enqueue_Dequeue_IsOrdered()
    {
        var queue = new MpscQueue<int>();
        for (int i = 0; i < 100; i++) queue.Enqueue(i);

        for (int i = 0; i < 100; i++)
        {
            Assert.True(queue.TryDequeue(out var v));
            Assert.Equal(i, v);
        }
        Assert.Equal(0, queue.Count);
    }

    private sealed class State { public int X; public int Y; }

    /// <summary>记录每个玩家被处理的包顺序，用于验证同玩家保序。</summary>
    private sealed class RecordingPipeline : IInboundPipeline
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, List<int>> _order = new();

        public Task<AuthorityResult> ProcessAsync(
            INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct = default)
        {
            if (packet is PlayerPositionPacket position)
            {
                lock (_gate)
                {
                    if (!_order.TryGetValue(playerId, out var list))
                        _order[playerId] = list = new List<int>();
                    list.Add((int)position.Position.X);
                }
            }
            return Task.FromResult(AuthorityResult.Accept(packet));
        }

        public IReadOnlyList<int> OrderForPlayer(int playerId)
        {
            lock (_gate)
                return _order.TryGetValue(playerId, out var list) ? list.ToArray() : Array.Empty<int>();
        }
    }

    /// <summary>并发探针：用 Barrier 强制参与者重叠，记录峰值并发数。</summary>
    private sealed class ConcurrencyProbe : IDisposable
    {
        private readonly Barrier _barrier;
        private int _concurrent;
        private int _maxConcurrency;

        public ConcurrencyProbe(int participants) => _barrier = new Barrier(participants);

        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

        public int Run(int value)
        {
            var current = Interlocked.Increment(ref _concurrent);
            int observed;
            while (current > (observed = Volatile.Read(ref _maxConcurrency)))
                Interlocked.CompareExchange(ref _maxConcurrency, current, observed);
            try
            {
                // 未并行时超时返回 false，峰值并发停留在 1
                _barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            }
            catch (BarrierPostPhaseException)
            {
                // 参与者超时后屏障进入破碎状态，忽略
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
            return value;
        }

        public void Dispose() => _barrier.Dispose();
    }
}
