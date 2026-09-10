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
}
