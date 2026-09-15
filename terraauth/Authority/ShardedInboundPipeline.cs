// TerraAuth — P2: 权威校验分片接入管线
// 把单包 IInboundPipeline 契约桥接到 ShardedAuthorityProcessor 的批量 API：
//   - 入站包按 PlayerId % shardCount 分片
//   - 同一玩家串行（保持包序与 Command 入队顺序），不同玩家并行
// 每个 ProcessAsync 调用挂一个 TaskCompletionSource，由分片批量处理完成后回填结果。

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TerraAuth.Concurrency;
using TerraAuth.Protocol;
using TerraAuth.Simulation;

namespace TerraAuth.Authority;

/// <summary>分片入站管线装饰器：包装原始管线，按玩家分片并行执行权威校验。</summary>
/// <remarks>
/// 设计取舍：
///   - 单调度协程按到达顺序批量取出待处理包，交给 <see cref="ShardedAuthorityProcessor{TInput,TOutput}"/>；
///   - 处理器内部按分片并行、片内串行，因此同玩家包序被保持（Command 入队顺序可重现）；
///   - 批量等待全部完成后再取下一批，跨批次的到达顺序同样保持。
/// 权威阶段为同步 CPU 工作（<see cref="InboundPipeline"/> 的 Task 同步完成），
/// 处理器用 Task.Run 把每包投递到线程池以获得真正的跨分片并行。
/// </remarks>
public sealed class ShardedInboundPipeline : IInboundPipeline, IAsyncDisposable
{
    private readonly ShardedAuthorityProcessor<InboundWork, AuthorityResult> _processor;
    private readonly Channel<InboundWork> _queue;
    private readonly IInboundPipeline _inner;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _dispatcher;
    private readonly int _maxBatchSize;
    private readonly int _queueCapacity;

    public ShardedInboundPipeline(IInboundPipeline inner, int shardCount, int maxBatchSize = 64,
        int queueCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _maxBatchSize = Math.Max(1, maxBatchSize);
        _queueCapacity = Math.Max(1, queueCapacity);
        _processor = new ShardedAuthorityProcessor<InboundWork, AuthorityResult>(
            shardCount,
            (_, work) => work.IsReset
                ? ResetPlayerCore(work.PlayerId, work.SessionId)
                : inner.ProcessAsync(work.Packet!, work.PlayerId, work.Commands!, work.Ct, work.SessionId)
                    .GetAwaiter().GetResult());
        // 入站有界：容量上限 + FullMode.Wait，满时阻塞写入（背压传导）。
        // 见 OPTIMIZATION_BACKLOG §三 第 7 项（原为无界，剔除无界内存风险）。
        _queue = Channel.CreateBounded<InboundWork>(new BoundedChannelOptions(_queueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _dispatcher = Task.Run(() => DispatchLoopAsync(_cts.Token));
    }

    public Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct = default)
        => ProcessAsync(packet, playerId, commands, ct, 0);

    public async Task<AuthorityResult> ProcessAsync(
        INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct, long sessionId)
    {
        if (ct.IsCancellationRequested)
            return await Task.FromCanceled<AuthorityResult>(ct).ConfigureAwait(false);

        var completion = new TaskCompletionSource<AuthorityResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // 满则阻塞写入（FullMode.Wait）：把背压传导到网络读取端——权威校验跟不上时
        // 不再无界堆积入站包，而是让慢消费者反过来拖慢供应商（真实服务端标准行为）。
        try
        {
            await _queue.Writer.WriteAsync(
                new InboundWork(packet, playerId, commands, ct, completion, SessionId: sessionId),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(ct);
            throw;
        }
        catch (Exception ex) when (ex is ChannelClosedException or ObjectDisposedException)
        {
            var disposed = new ObjectDisposedException(nameof(ShardedInboundPipeline));
            completion.TrySetException(disposed);
            throw disposed;
        }
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 连接结束：把「按玩家重置」投递到**同一队列 / 同一分片**，保证该玩家在途包先处理完再清状态
    /// （否则在途的旧位置包会把基线重新写回，重连后仍被判超速）。
    /// </summary>
    public void ResetPlayer(int playerId) => ResetPlayer(playerId, 0);
    public void ResetPlayer(int playerId, long sessionId)
    {
        var work = InboundWork.Reset(playerId, sessionId);
        // 有界队列若已满：重置**必须送达**（丢弃会使重连后旧基线未清 → 误判超速），
        // 故短自旋等待腾位。重置极低频，仅极端过载时才可能短暂占用网络线程——正是有界背压的预期。
        while (!_queue.Writer.TryWrite(work))
        {
            if (_cts.IsCancellationRequested) return;
            Thread.SpinWait(256);
        }
    }

    private AuthorityResult ResetPlayerCore(int playerId, long sessionId)
    {
        _inner.ResetPlayer(playerId, sessionId);
        return AuthorityResult.Accept(null);
    }

    /// <summary>单调度协程：按到达顺序批量取出，交给分片处理器，再把结果回填到各自的 TCS。</summary>
    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        var batch = new List<InboundWork>(_maxBatchSize);
        var inputs = new List<(int PlayerId, InboundWork Input)>(_maxBatchSize);
        try
        {
            await foreach (var first in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();
                inputs.Clear();
                batch.Add(first);
                while (batch.Count < _maxBatchSize && _queue.Reader.TryRead(out var more))
                    batch.Add(more);

                foreach (var work in batch)
                    inputs.Add((work.PlayerId, work));

                IReadOnlyList<AuthorityResult> results;
                try
                {
                    results = await _processor.ProcessBatchAsync(inputs).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    foreach (var work in batch)
                        work.Completion?.TrySetException(ex);
                    continue;
                }

                // ProcessBatchAsync 保序返回 → 按下标回填，保证每个调用方拿到自己的结果
                // （「按玩家重置」工作项无等待方，Completion 为 null）
                for (var i = 0; i < batch.Count; i++)
                    batch[i].Completion?.TrySetResult(results[i]);
            }
        }
        catch (OperationCanceledException)
        {
            // 停机
        }
        finally
        {
            // 停机时唤醒所有仍等待的调用方，避免悬挂
            while (_queue.Reader.TryRead(out var pending))
                pending.Completion?.TrySetCanceled();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        try
        {
            await _dispatcher.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 停机期间忽略异常
        }
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>
    /// 一件待处理工作：普通包处理（<see cref="IsReset"/> = false）或「按玩家重置」（无包、无等待方）。
    /// </summary>
    private sealed record InboundWork(
        INetworkPacket? Packet,
        int PlayerId,
        CommandQueue? Commands,
        CancellationToken Ct,
        TaskCompletionSource<AuthorityResult>? Completion,
        long SessionId = 0,
        bool IsReset = false)
    {
        public static InboundWork Reset(int playerId, long sessionId = 0)
            => new(null, playerId, null, default, null, SessionId: sessionId, IsReset: true);
    }
}
