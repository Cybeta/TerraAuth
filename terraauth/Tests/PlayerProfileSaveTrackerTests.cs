// PlayerProfileSaveTracker 的单元测试：落盘基线的「比对 / 记账 / 剪枝」三件事。
// 这层逻辑是「未变更不写库」的唯一判据，出错的两个方向都很贵：
//   判成「未变更」而实际变了 → 该玩家读档回退（丢背包）；
//   判成「变了」而实际没变 → 挂机玩家被每 500ms 白写一次（写放大）。

using TerraAuth.Net.Transport;
using Xunit;

namespace TerraAuth.Tests;

public class PlayerProfileSaveTrackerTests
{
    /// <summary>编码后档案的定长（1 字节版本 + 4×Int32 + 59 槽 × 7 字节，见 PlayerProfileCodec）。</summary>
    private const int BlobLength = 430;

    private static byte[] Blob(byte fill) => Enumerable.Repeat(fill, BlobLength).ToArray();

    [Fact]
    public void NeedsSave_WithoutBaseline_IsTrue()
    {
        var tracker = new PlayerProfileSaveTracker();

        // 本会话还没成功落盘过 → 必须写（新会话 / 首轮周期保存都走这条）
        Assert.True(tracker.NeedsSave((1, 100L), Blob(0x11)));
    }

    [Fact]
    public void MarkSaved_SameBytes_NeedsSaveIsFalse()
    {
        var tracker = new PlayerProfileSaveTracker();
        var blob = Blob(0x11);

        tracker.MarkSaved((1, 100L), blob);

        // 内容相同的**另一份**数组（每轮 Encode 都新建）也必须被判为未变更
        Assert.False(tracker.NeedsSave((1, 100L), Blob(0x11)));
    }

    [Fact]
    public void MarkSaved_OneByteChanged_NeedsSaveIsTrue()
    {
        var tracker = new PlayerProfileSaveTracker();
        tracker.MarkSaved((1, 100L), Blob(0x11));

        var changed = Blob(0x11);
        changed[17] = 0x12;   // 槽 0 的物品 id 所在字节

        Assert.True(tracker.NeedsSave((1, 100L), changed));
    }

    [Fact]
    public void SamePlayerId_DifferentSession_IsNotTreatedAsUnchanged()
    {
        var tracker = new PlayerProfileSaveTracker();
        var blob = Blob(0x11);

        tracker.MarkSaved((1, 100L), blob);

        // 槽位复用（最小可用 ID）：同一 PlayerId 的新会话字节恰好与上一会话相同时，
        // 也必须判为「需要写」—— 否则新会话会被当成已落盘，读档读到上一会话 / 别人的背包。
        Assert.True(tracker.NeedsSave((1, 101L), Blob(0x11)));
        Assert.False(tracker.NeedsSave((1, 100L), Blob(0x11)));   // 老会话自身仍按基线比对
    }

    [Fact]
    public void Prune_DropsKeysNotLive()
    {
        var tracker = new PlayerProfileSaveTracker();
        tracker.MarkSaved((1, 100L), Blob(0x11));
        tracker.MarkSaved((2, 101L), Blob(0x22));
        Assert.Equal(2, tracker.Count);

        // 只剩 #1 在线（#2 已断线）→ #2 的基线必须丢弃，否则字典随历史会话数无界增长
        tracker.Prune(new List<(int, long)> { (1, 100L) });

        Assert.Equal(1, tracker.Count);
        Assert.False(tracker.NeedsSave((1, 100L), Blob(0x11)));   // 在线者基线保留
        Assert.True(tracker.NeedsSave((2, 101L), Blob(0x22)));    // 被剪掉 → 需要重新落盘
    }

    [Fact]
    public void Prune_KeepsLiveKeys()
    {
        var tracker = new PlayerProfileSaveTracker();
        tracker.MarkSaved((1, 100L), Blob(0x11));

        // live 集合里有「还没有基线的会话」（如刚进服）也不能出错：只剪基线里多余的，不建新条目
        tracker.Prune(new List<(int, long)> { (1, 100L), (7, 999L) });

        Assert.Equal(1, tracker.Count);
        Assert.False(tracker.NeedsSave((1, 100L), Blob(0x11)));
        Assert.True(tracker.NeedsSave((7, 999L), Blob(0x33)));
    }
}
