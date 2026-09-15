// TerraAuth — Phase 5: 单连接状态机
// 管理单个客户端的生命周期：Handshake → Playing → Disconnected

using System.Buffers;              // ArrayBufferWriter<byte>
using System.IO.Pipelines;         // PipeReader, ReadOnlySequence<byte>
using System.Threading.Channels;   // Channel<OutboundFrame>
using TerraAuth.Concurrency;       // WorkerPool
using TerraAuth.Protocol;          // INetworkPacket

namespace TerraAuth.Net.Transport;

/// <summary>
/// 连接状态。
/// </summary>
public enum ConnectionState
{
    Handshaking,    // 等待 ConnectionRequest + 版本校验
    Authenticating, // 等待 PlayerInfo / Slot
    Playing,        // 正常游戏：入站走管线，出站走快照
    Disconnected,
}

/// <summary>
/// 出站消息（由 SnapshotBroadcaster / 控制逻辑投递）。
/// <paramref name="Flushed"/> 非空时，写循环在真正写入 socket 后置位（供"先发包再断开"等待）。
/// </summary>
public sealed record OutboundFrame(PacketId Type, byte[] Bytes, TaskCompletionSource? Flushed = null);

/// <summary>
/// 单个客户端连接。
/// 职责：
///   - 持有 NetworkStream / Pipe
///   - 运行 ReadLoop（解码 → 投递管线）
///   - 运行 WriteLoop（从 Channel 取帧 → 编码发送）
///   - 暴露 SendAsync 供 ISnapshotSender 调用
/// </summary>
public sealed class Connection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly PipeReader _reader;
    private readonly Channel<OutboundFrame> _outbound = Channel.CreateBounded<OutboundFrame>(
        new BoundedChannelOptions(2048)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    private readonly IPacketDecoder _decoder;
    private readonly IPacketEncoder _encoder;
    private readonly DecodeContext _decodeContext;
    private readonly WorkerPool _workers;
    private readonly CancellationTokenSource _lifetimeCts = new();
    // 握手阶段超时：在进入 Playing 前保持链接若超时则拒绝，防「占住连接/槽位」的握手停滞攻击。
    // 由 ConnectionManager 据 ServerConfig.HandshakeTimeoutSeconds 注入（默认 10s = 原默认值）。
    internal readonly TimeSpan HandshakeTimeout;
    private readonly TaskCompletionSource _runCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeStarted;
    private int _closeRequested;

    /// <summary>写循环退出信号：供"等待落盘"的调用方判断"已不可能落盘"，避免依赖固定超时。</summary>
    private readonly TaskCompletionSource _writeLoopExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static long _nextSessionId;
    public long SessionId { get; } = Interlocked.Increment(ref _nextSessionId);
    public int PlayerId { get; internal set; }
    public ConnectionState State { get; internal set; } = ConnectionState.Handshaking;
    public bool IsConnected => State != ConnectionState.Disconnected;

    /// <summary>
    /// 已下发过的图格区块（sectionX, sectionY）。区块编码（Deflate）成本高，同一区块不重复下发。
    /// 仅由该连接自身的读循环 / 流送路径访问（同一玩家串行），无需加锁。
    /// </summary>
    public HashSet<(int X, int Y)> SyncedSections { get; } = new();

    /// <summary>上次按玩家位置流送区块时所在的区块坐标（用于「跨区块才补发」判定）。</summary>
    public (int X, int Y)? LastStreamSection { get; set; }

    /// <summary>对端地址（用于连接生命周期日志）。</summary>
    public string RemoteEndPoint { get; internal set; } = "unknown";

    internal Connection(
        Stream stream,
        IPacketDecoder decoder,
        IPacketEncoder encoder,
        ProtocolVersion version,
        WorkerPool workers,
        TimeSpan? handshakeTimeout = null)
    {
        _stream = stream;
        _reader = PipeReader.Create(stream);
        _decoder = decoder;
        _encoder = encoder;
        _decodeContext = new DecodeContext { Version = version };
        _workers = workers;
        HandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(10); // 默认与原行为一致
    }

    /// <summary>
    /// 启动读写循环。返回当连接结束时的 Task。
    /// </summary>
    internal async Task RunAsync(
        Func<INetworkPacket, Connection, CancellationToken, Task> onPacket,
        CancellationToken ct)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
        var loopCt = connectionCts.Token;
        try
        {
            // 握手超时看门狗：进入 Playing 前停留超时即主动断开（防占住连接/槽位的握手停滞攻击）。
            // 进入 Playing 后该任务到期检查 State 判定无需断开，仅随连接关闭而取消。
            if (HandshakeTimeout > TimeSpan.Zero)
            {
                _ = HandshakeWatchdogAsync(connectionCts);
            }

            var readTask = ReadLoopAsync(onPacket, loopCt);
            var writeTask = WriteLoopAsync(loopCt);

            // 任一循环结束即取消另一循环，并观察两个任务的最终异常。
            await Task.WhenAny(readTask, writeTask).ConfigureAwait(false);
            if (Volatile.Read(ref _closeRequested) == 0)
            {
                connectionCts.Cancel();
                _outbound.Writer.TryComplete();
            }

            await Task.WhenAll(readTask, writeTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || connectionCts.IsCancellationRequested)
        {
            // 正常关闭
        }
        catch (IOException)
        {
            // 客户端断开
        }
        finally
        {
            State = ConnectionState.Disconnected;
            _outbound.Writer.TryComplete();
            _runCompleted.TrySetResult();
            Console.WriteLine($"[Net] 断开 {RemoteEndPoint} 玩家 #{PlayerId}");
        }
    }

    /// <summary>
    /// 握手看门狗：若在 <see cref="HandshakeTimeout"/> 内未进入 <see cref="ConnectionState.Playing"/>，
    /// 则主动取消连接（关闭 RunAsync 读写循环并回收槽位），防止「占住连接不放」的握手停滞攻击。
    /// 已进入 Playing 时到期判定无需断开，仅随连接关闭而随 <paramref name="connectionCts"/> 一并取消。
    /// </summary>
    private async Task HandshakeWatchdogAsync(CancellationTokenSource connectionCts)
    {
        var ct = connectionCts.Token;
        try
        {
            await Task.Delay(HandshakeTimeout, ct).ConfigureAwait(false);
            // 到期时仍未请求关闭且尚未完成握手 → 主动断开
            if (Volatile.Read(ref _closeRequested) == 0 && State != ConnectionState.Playing)
            {
                connectionCts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            // 连接已关闭 / Watchdog 被取消：无需处理
        }
    }

    // ---------- 入站：分帧（I/O）→ 解码 + 处理（Worker 池） ----------

    private async Task ReadLoopAsync(
        Func<INetworkPacket, Connection, CancellationToken, Task> onPacket,
        CancellationToken ct)
    {
        var buffer = default(ReadOnlySequence<byte>);

        while (!ct.IsCancellationRequested && IsConnected)
        {
            // 读更多数据
            var result = await _reader.ReadAsync(ct).ConfigureAwait(false);
            buffer = result.Buffer;

            // 分帧留在 I/O 线程（半包/粘包处理），解码与管线处理移交 Worker 池
            while (Framing.TryReadFrame(ref buffer, out var type, out var payload))
            {
                // 复制帧体，使其生命周期脱离 Pipe 缓冲区
                var frameBytes = payload.ToArray();

                try
                {
                    // 逐包 await：同一连接的包仍按序处理，但 CPU 工作不在 I/O 线程
                    await _workers.EnqueueAndWaitAsync(async wct =>
                    {
                        var packet = _decoder.Decode(type, frameBytes, _decodeContext);
                        await onPacket(packet, this, wct).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 单包异常 → 关闭连接（异常隔离）
                    Console.WriteLine($"[Connection] Packet handling failed: {ex.Message}");
                    break; // 退出 read loop → 触发清理
                }
            }

            // 通知 PipeReader 已消费多少
            _reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted) break;
        }

        await _reader.CompleteAsync().ConfigureAwait(false);
    }

    // ---------- 出站：Channel → 编码发送 ----------

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await _stream.WriteAsync(frame.Bytes, ct).ConfigureAwait(false);
                    await _stream.FlushAsync(ct).ConfigureAwait(false);
                    frame.Flushed?.TrySetResult(); // 该帧已落盘
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException)
                {
                    frame.Flushed?.TrySetException(ex);
                    break; // 断开
                }
            }
        }
        finally
        {
            // 写循环退出（正常关闭 / 断开 / 异常）：唤醒所有等待落盘的调用方，
            // 使它们无需依赖"魔法超时"判断"已不可能落盘"
            _writeLoopExited.TrySetResult();
        }
    }

    // ---------- 供 ISnapshotSender / 控制逻辑调用 ----------

    /// <summary>投递一条出站帧（线程安全）。</summary>
    internal ValueTask SendAsync(OutboundFrame frame, CancellationToken ct)
    {
        return _outbound.Writer.WriteAsync(frame, ct);
    }

    /// <summary>编码并投递（供 SnapshotBroadcaster 使用）。</summary>
    public ValueTask SendEncodedAsync(PacketId type, INetworkPacket packet, CancellationToken ct)
        => SendEncodedAsync(type, packet, ct, flushed: null);

    /// <summary>
    /// 编码投递并等待该帧**真正写入 socket**。
    /// 用于「必须先发出某包再断开」的场景（如踢出发包 2）：投递到出站通道不等于已发出，
    /// 立即关闭 socket 会丢掉尚未落盘的帧。
    /// </summary>
    /// <remarks>
    /// 等待是**事件驱动**的：要么该帧落盘，要么写循环退出（此时已不可能再落盘）。
    /// 不使用固定超时 —— 机器负载高时固定超时会误判为"发不出去"并丢掉包（实测已发生）。
    /// </remarks>
    public async Task SendEncodedAndFlushedAsync(PacketId type, INetworkPacket packet, CancellationToken ct)
    {
        if (type == PacketId.Disconnect)
            Volatile.Write(ref _closeRequested, 1);

        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendEncodedAsync(type, packet, ct, flushed).ConfigureAwait(false);

        var finished = await Task.WhenAny(flushed.Task, _writeLoopExited.Task).WaitAsync(ct).ConfigureAwait(false);
        if (finished == flushed.Task)
        {
            await flushed.Task.ConfigureAwait(false); // 展开写入侧异常（若有）
            if (type == PacketId.Disconnect)
            {
                // 当前帧已写入后才关闭通道和读写循环；否则 RemoveAsync/DisposeAsync
                // 会在写循环取出包 2 前关闭 socket，或让 RunAsync 永久等待未完成的通道。
                _outbound.Writer.TryComplete();
                _lifetimeCts.Cancel();
            }

            return;
        }

        throw new IOException("连接写循环已退出，Disconnect 包未写入");
    }

    private ValueTask SendEncodedAsync(
        PacketId type, INetworkPacket packet, CancellationToken ct, TaskCompletionSource? flushed)
    {
        var writer = new ArrayBufferWriter<byte>();
        _encoder.Encode(writer, type, packet);
        return SendAsync(new OutboundFrame(type, writer.WrittenSpan.ToArray(), flushed), ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            await _runCompleted.Task.ConfigureAwait(false);
            return;
        }

        _lifetimeCts.Cancel();
        _outbound.Writer.TryComplete();

        // 先等待读写循环退出，再释放底层流和生命周期 CTS，避免循环继续访问已释放资源。
        await _runCompleted.Task.ConfigureAwait(false);
        State = ConnectionState.Disconnected;
        await _stream.DisposeAsync().ConfigureAwait(false);
        _lifetimeCts.Dispose();
    }
}
