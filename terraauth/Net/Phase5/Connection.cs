// TerraAuth — Phase 5: 单连接状态机
// 管理单个客户端的生命周期：Handshake → Playing → Disconnected

using System.Buffers;              // ArrayBufferWriter<byte>
using System.IO.Pipelines;         // PipeReader, ReadOnlySequence<byte>
using System.Threading.Channels;   // Channel<OutboundFrame>
using TerraAuth.Concurrency;       // WorkerPool
using TerraAuth.Protocol;          // INetworkPacket

namespace TerraAuth.Net.Phase5;

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
/// </summary>
public sealed record OutboundFrame(PacketId Type, byte[] Bytes);

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
    private readonly Channel<OutboundFrame> _outbound = Channel.CreateUnbounded<OutboundFrame>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly IPacketDecoder _decoder;
    private readonly IPacketEncoder _encoder;
    private readonly DecodeContext _decodeContext;
    private readonly WorkerPool _workers;

    public int PlayerId { get; internal set; }
    public ConnectionState State { get; internal set; } = ConnectionState.Handshaking;
    public bool IsConnected => State != ConnectionState.Disconnected;

    /// <summary>对端地址（用于连接生命周期日志）。</summary>
    public string RemoteEndPoint { get; internal set; } = "unknown";

    internal Connection(
        Stream stream,
        IPacketDecoder decoder,
        IPacketEncoder encoder,
        ProtocolVersion version,
        WorkerPool workers)
    {
        _stream = stream;
        _reader = PipeReader.Create(stream);
        _decoder = decoder;
        _encoder = encoder;
        _decodeContext = new DecodeContext { Version = version };
        _workers = workers;
    }

    /// <summary>
    /// 启动读写循环。返回当连接结束时的 Task。
    /// </summary>
    internal async Task RunAsync(
        Func<INetworkPacket, Connection, CancellationToken, Task> onPacket,
        CancellationToken ct)
    {
        try
        {
            var readTask = ReadLoopAsync(onPacket, ct);
            var writeTask = WriteLoopAsync(ct);

            // 任一结束即整体结束
            await Task.WhenAny(readTask, writeTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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
            Console.WriteLine($"[Net] 断开 {RemoteEndPoint} 玩家 #{PlayerId}");
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
        await foreach (var frame in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await _stream.WriteAsync(frame.Bytes, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break; // 断开
            }
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
    {
        var writer = new ArrayBufferWriter<byte>();
        _encoder.Encode(writer, type, packet);
        return SendAsync(new OutboundFrame(type, writer.WrittenSpan.ToArray()), ct);
    }

    public async ValueTask DisposeAsync()
    {
        State = ConnectionState.Disconnected;
        _outbound.Writer.TryComplete();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
