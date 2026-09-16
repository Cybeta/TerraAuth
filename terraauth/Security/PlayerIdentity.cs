// Phase 6 - 玩家身份映射：连接槽位 PlayerId(int) ↔ 封禁 / 审计使用的稳定 Guid
// 生产应维护 ConnectionId → Guid 表；当前用确定性构造，保证同一玩家在
// 审计链路（PersistenceAuditLogger）与插件 API（IServerApi.BanPlayer）中得到同一身份，
// 否则"审计里封了 A、写库封的是 B"，封禁将失效。

using System;

namespace TerraAuth.Security;

public static class PlayerIdentity
{
    /// <summary>服务端事件（非玩家）使用的占位身份。</summary>
    public static readonly Guid HostId = Guid.Empty;

    /// <summary>连接槽位 PlayerId → 稳定 Guid（PlayerId 为 0 表示服务端自身，返回 <see cref="HostId"/>）。</summary>
    public static Guid ToGuid(int playerId)
        => playerId == 0 ? HostId : new Guid(playerId, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)playerId);

    /// <summary>稳定 Guid → 连接槽位 PlayerId（与 <see cref="ToGuid"/> 互逆；<see cref="HostId"/> 返回 0）。</summary>
    public static int ToPlayerId(Guid id)
    {
        if (id == HostId) return 0;
        var bytes = id.ToByteArray();
        return BitConverter.ToInt32(bytes, 0);
    }

    /// <summary>
    /// 玩家名 → **档案**身份 Guid（玩家档案：SSC 背包 / 生命 / 法力，跨会话与槽位复用）。
    /// 不能复用 <see cref="ToGuid"/>：那是按连接槽位构造的，槽位会被下一位玩家复用、
    /// 会话接管还会换槽位，用它会「A 的背包存到 B 名下」。玩家名是跨会话稳定的身份
    /// （会话恢复同样按玩家名认回），故取它的确定性哈希；仅作身份键，非安全用途。
    /// </summary>
    public static Guid FromName(string name)
    {
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name), hash);
        return new Guid(hash[..16]);
    }
}
