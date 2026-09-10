// TerraAuth — Phase 5: 连接管理器
// 连接池、超时、踢出、并发上限

using System.Collections.Concurrent;
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

    /// <summary>移除并释放连接，释放容量槽位。</summary>
    public async Task RemoveAsync(int playerId)
    {
        if (_connections.TryRemove(playerId, out var conn))
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            _capacity.Release();
        }
    }

    /// <summary>踢出指定玩家。</summary>
    public async Task KickAsync(int playerId, string reason)
    {
        var conn = Get(playerId);
        if (conn is null) return;

        // TODO: 先发 DisconnectPacket(reason)，再关闭
        await RemoveAsync(playerId).ConfigureAwait(false);
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
