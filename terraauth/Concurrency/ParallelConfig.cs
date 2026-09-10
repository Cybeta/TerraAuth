// TerraAuth — 多线程优化（需求：多 CPU 线程优化）
// 分析见 docs/concurrency.md；此处提供可落地的并行基础设施骨架
//
// 并行边界（来自分析结论）：
//   ✅ 可并行：网络 I/O、包解码、快照序列化、持久化落盘、无状态 Hook、指标采集
//   ⚠️ 需改造：权威校验（按玩家分片）、世界仿真（空间分区）、快照生成（双缓冲）
//   ❌ 不可并行：CommandQueue 排序、Tick 推进（固定 timestep 锚点）

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraAuth.Concurrency;

/// <summary>并行配置：根据逻辑核心数自动推导各线程池大小。</summary>
public sealed class ParallelConfig
{
    public int LogicalCores { get; }
    /// <summary>网络 I/O 池（I/O 密集，可多于核心数）。</summary>
    public int NetworkThreads { get; set; }
    /// <summary>Worker 池（CPU 密集，≈ 核心数）。</summary>
    public int WorkerThreads { get; set; }
    /// <summary>后台池（持久化/指标，少量）。</summary>
    public int BackgroundThreads { get; set; }
    /// <summary>权威校验分片数（按 PlayerId % ShardCount 分片，片内串行、跨片并行）。</summary>
    public int ShardCount { get; set; }
    /// <summary>世界空间分区大小（Chunk 边长，单位 tile）。</summary>
    public int ChunkSize { get; set; } = 16;

    public ParallelConfig()
    {
        LogicalCores = Environment.ProcessorCount;
        NetworkThreads = Math.Max(4, LogicalCores);
        WorkerThreads = Math.Max(1, LogicalCores - 2); // 留 2 核给 Logic + OS
        BackgroundThreads = 2;
        ShardCount = Math.Max(1, WorkerThreads);       // 每分片对应一个 Worker 线程
    }
}

#region 线程安全原语
/// <summary>无锁计数器（Interlocked，用于指标/统计）。</summary>
public struct AtomicCounter
{
    private long _value;
    public long Value => Interlocked.Read(ref _value);
    public void Increment() => Interlocked.Increment(ref _value);
    public void Add(long n) => Interlocked.Add(ref _value, n);
    public long Reset() => Interlocked.Exchange(ref _value, 0);
}

/// <summary>多生产者单消费者队列（用于 Command 提交，无锁快路径）。</summary>
public sealed class MpscQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();
    public void Enqueue(T item) => _queue.Enqueue(item);
    public bool TryDequeue(out T? item) => _queue.TryDequeue(out item);
    public int Count => _queue.Count;
}

/// <summary>单写多读锁（快照读取用，降低读写竞争）。</summary>
public sealed class SingleWriterMultiReaderLock
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    public IDisposable ReadLock() { _lock.EnterReadLock(); return new Releaser(_lock.ExitReadLock); }
    public IDisposable WriteLock() { _lock.EnterWriteLock(); return new Releaser(_lock.ExitWriteLock); }
    private sealed class Releaser : IDisposable
    {
        private readonly Action _release;
        public Releaser(Action release) => _release = release;
        public void Dispose() => _release();
    }
}
#endregion

/// <summary>确定性校验辅助：并行结果必须按确定顺序合并，保证回放一致。</summary>
public static class Determinism
{
    /// <summary>计算状态哈希（开发模式开启，不一致即断言失败）。</summary>
    public static int ComputeStateHash<T>(T state) where T : notnull
    {
        // 值类型与字符串：GetHashCode 本身即基于内容
        if (state is string || state is ValueType)
            return state.GetHashCode();

        // 引用类型：按公共字段/属性内容计算，否则两个内容相同的实例会得到不同哈希，
        // 破坏并行结果的可重现性校验。
        var hash = 17;
        var type = state.GetType();
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            hash = unchecked(hash * 31 + (f.GetValue(state)?.GetHashCode() ?? 0));
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.GetIndexParameters().Length == 0)
                hash = unchecked(hash * 31 + (p.GetValue(state)?.GetHashCode() ?? 0));
        return hash;
    }

    /// <summary>断言确定性未被破坏（仅 Debug 生效）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AssertConsistent(int expected, int actual)
    {
#if DEBUG
        if (expected != 0 && expected != actual)
            throw new InvalidOperationException($"Determinism broken! Expected {expected}, got {actual}");
#endif
    }
}
