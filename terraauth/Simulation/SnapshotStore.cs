// TerraAuth — Phase 3/4: 快照存储
// 仿真层每 tick 产出快照，供 Phase 4 编码下发

using System.Threading; // Lock（.NET 9+ 专用同步原语）

namespace TerraAuth.Simulation;

/// <summary>
/// 快照环形存储（供 SnapshotBroadcaster 读取 + 追赶）。
/// 线程安全：仿真线程写入（<see cref="Add"/> / <see cref="TrimBefore"/>），
/// 网络线程读取（<see cref="Snapshot"/> / <see cref="Latest"/>）。
/// </summary>
public sealed class SnapshotStore
{
    private readonly List<SnapshotFrame> _frames = new();
    private readonly Lock _gate = new();
    private readonly int _capacity;

    public SnapshotStore(int capacity = 300) // 默认保留 15 秒 @ 20Hz
    {
        _capacity = capacity;
    }

    /// <summary>当前快照数量。</summary>
    public int Count
    {
        get { lock (_gate) return _frames.Count; }
    }

    public SnapshotFrame Latest
    {
        get
        {
            lock (_gate)
                return _frames.Count > 0 ? _frames[^1] : throw new InvalidOperationException("No snapshot yet");
        }
    }

    /// <summary>最新快照；尚无快照时返回 <c>null</c>（避免抛异常）。</summary>
    public SnapshotFrame? LatestOrDefault
    {
        get { lock (_gate) return _frames.Count > 0 ? _frames[^1] : null; }
    }

    /// <summary>
    /// 获取窗口的一致快照副本。调用方可在副本上做多步读取（窗口判定 + 增量归并 + 追赶），
    /// 不会与仿真线程的 <see cref="Add"/> / <see cref="TrimBefore"/> 竞争。
    /// <para>注意：返回的是副本；直接暴露内部 <see cref="List{T}"/> 会因并发写入产生
    /// 索引越界或枚举期「集合被修改」异常。</para>
    /// </summary>
    public SnapshotFrame[] Snapshot()
    {
        lock (_gate) return _frames.ToArray();
    }

    public void Add(SnapshotFrame frame)
    {
        lock (_gate)
        {
            _frames.Add(frame);
            if (_frames.Count > _capacity)
                _frames.RemoveAt(0);
        }
    }

    /// <summary>移除指定 tick 之前的快照（追赶窗口之外的历史）。</summary>
    public void TrimBefore(uint tick)
    {
        lock (_gate)
        {
            int remove = 0;
            while (remove < _frames.Count && _frames[remove].Tick < tick)
                remove++;

            if (remove > 0)
                _frames.RemoveRange(0, remove);
        }
    }

    public void Clear()
    {
        lock (_gate) _frames.Clear();
    }
}
