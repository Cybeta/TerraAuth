// TerraAuth — Phase 5: 出站发送抽象
// 由 SnapshotBroadcaster（Phase 4）调用，由本 Phase 的 Connection 实现
// https://github.com/CreativeTools/terraria-protocol

using System.Buffers;      // ArrayBufferWriter<byte>
using TerraAuth.Protocol;   // PacketId
using TerraAuth.Simulation; // SnapshotFrame

namespace TerraAuth.Net.Transport;

/// <summary>
/// 快照发送器接口。
/// Phase 4 的 <see cref="SnapshotBroadcaster"/> 依赖此抽象下发快照。
/// 本 Phase 提供基于 TCP Connection 的实现。
/// </summary>
public interface ISnapshotSender : IAsyncDisposable
{
    /// <summary>当前可接收快照的在线玩家。</summary>
    IReadOnlyList<int> ActivePlayers { get; }

    /// <summary>发送一条快照给指定玩家（内部编码）。</summary>
    ValueTask SendAsync(int playerId, SnapshotFrame frame, CancellationToken ct = default);

    /// <summary>发送一条已编码的快照帧给指定玩家（供并行编码后复用字节）。</summary>
    ValueTask SendEncodedAsync(int playerId, byte[] frameBytes, CancellationToken ct = default);

    /// <summary>广播一条快照给所有活跃玩家。</summary>
    ValueTask BroadcastAsync(SnapshotFrame frame, CancellationToken ct = default);
}

/// <summary>
/// 基于 TCP 连接的快照发送实现。
/// 将 SnapshotFrame → 编码 → 逐连接 Channel → 网络写出。
/// </summary>
public sealed class ConnectionSnapshotSender : ISnapshotSender
{
    private readonly ConnectionManager _connections;
    private readonly IPacketEncoder _encoder;

    public ConnectionSnapshotSender(ConnectionManager connections, IPacketEncoder encoder)
    {
        _connections = connections;
        _encoder = encoder;
    }

    public IReadOnlyList<int> ActivePlayers => _connections.All()
        .Where(c => c.State == ConnectionState.Playing)
        .Select(c => c.PlayerId)
        .ToArray();

    public ValueTask SendAsync(int playerId, SnapshotFrame frame, CancellationToken ct = default)
        => SendEncodedAsync(playerId, Encode(frame), ct);

    public ValueTask SendEncodedAsync(int playerId, byte[] frameBytes, CancellationToken ct = default)
    {
        var conn = _connections.Get(playerId);
        if (conn is null || !conn.IsConnected)
            return ValueTask.CompletedTask;

        return conn.SendAsync(new OutboundFrame(PacketId.Snapshot, frameBytes), ct);
    }

    public async ValueTask BroadcastAsync(SnapshotFrame frame, CancellationToken ct = default)
    {
        var bytes = Encode(frame); // 同一快照对所有玩家只编码一次
        foreach (var playerId in ActivePlayers)
        {
            await SendEncodedAsync(playerId, bytes, ct).ConfigureAwait(false);
        }
    }

    private byte[] Encode(SnapshotFrame frame)
    {
        var writer = new ArrayBufferWriter<byte>();
        _encoder.EncodeSnapshot(writer, frame);
        return writer.WrittenSpan.ToArray();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
