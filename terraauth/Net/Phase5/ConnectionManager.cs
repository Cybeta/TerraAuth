// TerraAuth — Phase 5: 连接管理器
// 连接池、超时、踢出、并发上限

using System.Collections.Concurrent;
using System.Collections.Generic; // ICollection<KeyValuePair<,>>（槽位回收的原子双匹配）
using TerraAuth.Protocol; // PacketId, INetworkPacket

namespace TerraAuth.Net.Phase5;

/// <summary>
/// 连接管理器：追踪所有活跃连接，提供踢出 / 广播 / 容量控制。
/// </summary>
public sealed class ConnectionManager
{
    private readonly ConcurrentDictionary<int, Connection> _connections = new();
    private readonly SemaphoreSlim _capacity;
    private readonly TimeSpan _handshakeTimeout = TimeSpan.FromSeconds(10);

    public int MaxConnections { get; }
    public int ActiveCount => _connections.Count;

    public ConnectionManager(int maxConnections = 64)
    {
        MaxConnections = maxConnections;
        _capacity = new SemaphoreSlim(maxConnections, maxConnections);
    }

    /// <summary>尝试注册新连接。返回 false 表示已达上限。</summary>
    public bool TryAdd(Connection connection, out int playerId)
    {
        playerId = 0;
        if (!_capacity.Wait(0)) // 非阻塞
            return false;

        // 分配最小可用 ID
        for (int i = 1; i <= MaxConnections; i++)
        {
            if (_connections.TryAdd(i, connection))
            {
                playerId = i;
                connection.PlayerId = i;
                return true;
            }
        }

        _capacity.Release();
        return false;
    }

    public Connection? Get(int playerId) =>
        _connections.TryGetValue(playerId, out var c) ? c : null;

    /// <summary>
    /// 移除并释放连接，释放容量槽位。
    /// <paramref name="expected"/> 非空时按「键 + 实例」双匹配移除：槽位被回收后可能已被新连接复用，
    /// 若仅按 PlayerId 移除会误杀新连接（被踢连接在 <c>RunAsync</c> 结束后会再次走到这里）。
    /// </summary>
    public async Task RemoveAsync(int playerId, Connection? expected = null)
    {
        if (!_connections.TryGetValue(playerId, out var conn)) return;
        if (expected is not null && !ReferenceEquals(conn, expected)) return;
        // ICollection.Remove 按「键 + 值」原子匹配：避免在槽位被新连接复用时误杀新连接
        if (!((ICollection<KeyValuePair<int, Connection>>)_connections)
                .Remove(new KeyValuePair<int, Connection>(playerId, conn))) return;

        await conn.DisposeAsync().ConfigureAwait(false);
        _capacity.Release();
    }

    /// <summary>
    /// 踢出指定玩家：先下发包 2（Disconnect，携带原因，客户端据此提示并主动断开），再关闭连接释放槽位。
    /// 关闭走 dispose 路径：读循环随 socket 释放退出，由 NetworkHost 触发 PlayerLeft 清理。
    /// </summary>
    public async Task KickAsync(int playerId, string reason, CancellationToken ct = default)
    {
        var conn = Get(playerId);
        if (conn is null) return;

        // 先置为断开：该玩家后续入站包不再进入权威管线
        conn.State = ConnectionState.Disconnected;

        try
        {
            // 等待包 2 真正写入 socket 后再关闭：投递到出站通道不等于已发出，
            // 直接关闭会丢帧（客户端看不到踢出原因）
            await conn.SendEncodedAndFlushedAsync(PacketId.Disconnect, DisconnectPacket.WithReason(reason), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消即直接关闭，不再等待落盘
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Net] 踢出玩家 #{playerId} 时下发包 2 失败: {ex.Message}");
        }

        await RemoveAsync(playerId).ConfigureAwait(false);
        Console.WriteLine($"[Net] 已踢出玩家 #{playerId}：{reason}");
    }

    /// <summary>广播到所有活跃连接（供快照下发）。</summary>
    public async Task BroadcastAsync(
        PacketId type,
        INetworkPacket packet,
        CancellationToken ct)
    {
        foreach (var kvp in _connections)
        {
            var conn = kvp.Value;
            if (conn.State == ConnectionState.Playing)
            {
                await conn.SendEncodedAsync(type, packet, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>广播到除指定玩家外的所有活跃连接（用于把某玩家的状态转发给其他人）。</summary>
    public async Task BroadcastExceptAsync(
        int excludePlayerId,
        PacketId type,
        INetworkPacket packet,
        CancellationToken ct)
    {
        foreach (var kvp in _connections)
        {
            if (kvp.Key == excludePlayerId)
                continue;

            var conn = kvp.Value;
            if (conn.State == ConnectionState.Playing)
            {
                await conn.SendEncodedAsync(type, packet, ct).ConfigureAwait(false);
            }
        }
    }

    public IEnumerable<Connection> All() => _connections.Values;
}
