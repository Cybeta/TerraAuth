// TerraAuth — Phase 3/4: 区块分区锁
// 架构（并发改造）：「支持区块分区锁，并行处理不同区域」。
//
// 解决的问题：Tile 结构约 20 字节（超过字长），而图格存在跨线程并发访问——
//   写：仿真线程 ApplyCommandsForTick → TileBreak/TilePlaceCommand.Apply
//   读：包 10 区块编码（CompressTileBlockInner，连接的工作线程）+ 世界权威校验（分片工作线程）
// 无保护时可能读出「撕裂」的图格（例如 Active=true 配 Type=0，或 Type 与 Frame 来自不同状态），
// 客户端据此渲染会得到错乱图格。
//
// 粒度：与包 10 一致的 section（200×150 tile）。锁按条带（stripe）组织，不依赖世界尺寸
// （WorldState 的尺寸可在构造后设置），条带冲突只是粒度变粗，不影响正确性。

using System;
using System.Collections.Generic;
using System.Threading;

namespace TerraAuth.Simulation;

/// <summary>图格访问的区块分区读写锁。</summary>
public sealed class SectionLocks
{
    /// <summary>与包 10 一致的区块边长（tile）。</summary>
    public const int SectionWidth = 200;
    public const int SectionHeight = 150;

    // 条带数取 2 的幂，便于位与取模
    private const int StripeCount = 64;

    private readonly ReaderWriterLockSlim[] _stripes;

    public SectionLocks()
    {
        _stripes = new ReaderWriterLockSlim[StripeCount];
        for (int i = 0; i < StripeCount; i++)
            _stripes[i] = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    }

    private static int SectionX(int tileX) => tileX / SectionWidth;
    private static int SectionY(int tileY) => tileY / SectionHeight;

    private static int StripeOf(int sectionX, int sectionY)
        => ((sectionX * 31) + sectionY) & (StripeCount - 1);

    /// <summary>取写锁（仿真线程修改图格前调用，配对 <see cref="ExitWrite"/>）。</summary>
    public void EnterWrite(int tileX, int tileY)
        => _stripes[StripeOf(SectionX(tileX), SectionY(tileY))].EnterWriteLock();

    public void ExitWrite(int tileX, int tileY)
        => _stripes[StripeOf(SectionX(tileX), SectionY(tileY))].ExitWriteLock();

    /// <summary>
    /// 取读锁，覆盖图格矩形 <c>[x0..x1] × [y0..y1]</c> 涉及的全部条带（单格读传 x0==x1、y0==y1）。
    /// 请用 <c>using</c> 包裹读区间；切勿在读锁内做压缩等长耗时工作（见 PacketEncoder 的快照拷贝策略）。
    /// </summary>
    public IDisposable EnterRead(int x0, int y0, int x1, int y1)
    {
        var indices = StripeIndicesFor(x0, y0, x1, y1);
        for (int i = 0; i < indices.Length; i++)
            _stripes[indices[i]].EnterReadLock();

        return new ReadScope(_stripes, indices);
    }

    private static int[] StripeIndicesFor(int x0, int y0, int x1, int y1)
    {
        // 规范化矩形（调用方可能传入反向坐标）
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);

        int sx0 = SectionX(x0), sx1 = SectionX(x1);
        int sy0 = SectionY(y0), sy1 = SectionY(y1);

        var set = new SortedSet<int>();
        for (int sx = sx0; sx <= sx1; sx++)
            for (int sy = sy0; sy <= sy1; sy++)
                set.Add(StripeOf(sx, sy));

        var indices = new int[set.Count];
        set.CopyTo(indices);
        return indices;
    }

    private sealed class ReadScope : IDisposable
    {
        private readonly ReaderWriterLockSlim[] _stripes;
        private readonly int[] _indices;
        private bool _released;

        public ReadScope(ReaderWriterLockSlim[] stripes, int[] indices)
            => (_stripes, _indices) = (stripes, indices);

        public void Dispose()
        {
            if (_released)
                return;

            _released = true;
            for (int i = _indices.Length - 1; i >= 0; i--)
                _stripes[_indices[i]].ExitReadLock();
        }
    }
}
