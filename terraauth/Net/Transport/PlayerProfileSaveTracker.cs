// SSC 玩家档案落盘基线（PlayerProfileSaveTracker）：记住「每个在线会话最后一次**成功**落盘的档案字节」，
// 让「按 500ms 轮询玩家档案」不必无条件重写全部在线玩家。
//
// ① 为什么需要：档案写库是「整行覆盖写 + fsync」，若每 500ms 把全部在线玩家（含挂机者）各写一次，
//    写放大随在线人数线性上涨而收益为零 —— 挂机玩家的背包一个字节都没变。
//    有了基线就能「变了才写」：挂机玩家 0 次写库，真正变更的玩家立刻落盘，
//    于是既做到崩服 / 被强杀最多丢 ~0.5 秒，又不至于把磁盘写爆。
// ② 为什么 key 必须带 SessionId：玩家槽位按「最小可用 ID」复用（与原版一致），
//    只用 PlayerId 会让**新会话**命中**上一会话**的基线字节；两者恰好相同时（同一玩家原地重进、
//    背包没动）就会被判成「未变更」→ 跳过落盘 → 之后读档读到的是别人 / 上一局的背包。
//    SessionId 是 <c>Connection</c> 里 Interlocked.Increment 的单调计数器，带上它就天然按会话隔离。
// ③ 为什么「只有写成功才记账」：账记早了（写库失败也记）就会把失败的那次当成已落盘，
//    此后同样的状态永远不再重试，丢档会被静默放大。只记成功是安全侧 —— 宁可多写一次。
//
// 线程安全：档案周期循环线程与断线清理线程都会碰它，故所有方法在同一把 <see cref="Lock"/> 内完成。

namespace TerraAuth.Net.Transport;

/// <summary>
/// 玩家档案落盘基线表：比对（要不要写）/ 记账（写成功了）/ 剪枝（会话结束就丢）。
/// 键是 <c>(PlayerId, SessionId)</c>；值时该会话最后一次成功落盘的档案字节（见文件头 ②③）。
/// </summary>
internal sealed class PlayerProfileSaveTracker
{
    private readonly Lock _gate = new();

    /// <summary>会话 → 最后成功落盘的档案字节。</summary>
    private readonly Dictionary<(int PlayerId, long SessionId), byte[]> _lastSaved = new();

    /// <summary>当前基线条数（供测试与诊断；每条基线约等于一份编码后的档案，定长 430 字节）。</summary>
    public int Count
    {
        get { lock (_gate) return _lastSaved.Count; }
    }

    /// <summary>
    /// 该会话的档案是否需要写库：没有基线（本会话还没成功写过）→ true，或与基线逐字节不同 → true；
    /// 与基线完全一致 → false（调用方直接跳过写库）。
    /// </summary>
    public bool NeedsSave((int PlayerId, long SessionId) key, byte[] blob)
    {
        lock (_gate)
            return !_lastSaved.TryGetValue(key, out var last) || !blob.AsSpan().SequenceEqual(last);
    }

    /// <summary>
    /// 记账：仅在**写库成功之后**调用（失败不记账 → 下一次轮询自动重试）。
    /// <paramref name="blob"/> 由编解码器每次新建、记账后调用方不再改写，故直接存引用。
    /// </summary>
    public void MarkSaved((int PlayerId, long SessionId) key, byte[] blob)
    {
        lock (_gate)
            _lastSaved[key] = blob;
    }

    /// <summary>
    /// 丢弃不在 <paramref name="liveKeys"/> 里的基线，防字典随「历史会话数」无界增长。
    /// 由落盘循环在每轮末尾用本轮实际的在线会话集合调用（断线 / 踢出 / 换会话的条目随之清掉）。
    /// </summary>
    public void Prune(IReadOnlyCollection<(int PlayerId, long SessionId)> liveKeys)
    {
        lock (_gate)
        {
            if (_lastSaved.Count == 0) return;

            var live = new HashSet<(int PlayerId, long SessionId)>(liveKeys);
            List<(int PlayerId, long SessionId)>? dead = null;
            foreach (var key in _lastSaved.Keys)
                if (!live.Contains(key)) (dead ??= new()).Add(key);
            if (dead is null) return;

            foreach (var key in dead)
                _lastSaved.Remove(key);
        }
    }
}
