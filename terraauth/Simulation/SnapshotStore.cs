// TerraAuth — Phase 3/4: 快照存储
// 仿真层每 tick 产出快照，供 Phase 4 编码下发

using System.Threading; // Lock（.NET 9+ 专用同步原语）

namespace TerraAuth.Simulation;

/// <summary>
/// 快照环形存储（供 SnapshotBroadcaster 读取 + 追赶）。
/// 线程安全：仿真线程写入（<see cref="Add"/> / <see cref="TrimBefore"/>），
/// 网络线程读取（<see cref="Snapshot"/> / <see cref="Latest"/>）。
/// 内部为定长环形数组：Add 满时覆写最旧、TrimBefore 前移 head，均无元素搬移。
/// </summary>
public sealed class SnapshotStore
{
    private readonly SnapshotFrame?[] _buf;
    private readonly Lock _gate = new();
    private int _head;   // 最旧有效帧下标（逻辑起始）
    private int _count;  // 有效帧数

    public SnapshotStore(int capacity = 300) // 默认保留 15 秒 @ 20Hz
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buf = new SnapshotFrame?[capacity];
    }

    /// <summary>当前快照数量。</summary>
    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public SnapshotFrame Latest
    {
        get
        {
            lock (_gate)
            {
                if (_count == 0)
                    throw new InvalidOperationException("No snapshot yet");
                return _buf[Index(_count - 1)]!;
            }
        }
    }

    /// <summary>最新快照；尚无快照时返回 <c>null</c>（避免抛异常）。</summary>
    public SnapshotFrame? LatestOrDefault
    {
        get
        {
            lock (_gate)
                return _count > 0 ? _buf[Index(_count - 1)] : null;
        }
    }

    /// <summary>
    /// 获取窗口的一致快照副本（按 tick 升序，最旧在前）。调用方可在副本上做多步读取
    /// （窗口判定 + 增量归并 + 追赶），不会与仿真线程的 <see cref="Add"/> / <see cref="TrimBefore"/> 竞争。
    /// </summary>
    public SnapshotFrame[] Snapshot()
    {
        lock (_gate)
        {
            if (_count == 0)
                return Array.Empty<SnapshotFrame>();

            var result = new SnapshotFrame[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _buf[Index(i)]!;
            return result;
        }
    }

    public void Add(SnapshotFrame frame)
    {
        lock (_gate)
        {
            if (_count < _buf.Length)
            {
                // 仍有空位 → 写入下一个逻辑槽
                _buf[Index(_count)] = frame;
                _count++;
            }
            else
            {
                // 已满 → 覆写最旧槽位，head 前移
                _buf[_head] = frame;
                _head = (_head + 1) % _buf.Length;
            }
        }
    }

    /// <summary>移除指定 tick 之前的快照（追赶窗口之外的历史）。</summary>
    public void TrimBefore(uint tick)
    {
        lock (_gate)
        {
            while (_count > 0 && _buf[_head]!.Tick < tick)
            {
                _buf[_head] = null;
                _head = (_head + 1) % _buf.Length;
                _count--;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            System.Array.Clear(_buf, 0, _buf.Length);
            _head = 0;
            _count = 0;
        }
    }

    /// <summary>逻辑下标 → 物理槽位（以 head 为起点绕环）。</summary>
    private int Index(int logical) => (_head + logical) % _buf.Length;
}
