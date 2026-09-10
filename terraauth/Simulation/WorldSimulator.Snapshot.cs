// TerraAuth — Phase 3: WorldSimulator 快照桥接（Phase 4 调用）
// 架构 §4.3 Output 阶段：将 WorldState 转为强类型 SnapshotFrame

namespace TerraAuth.Simulation;

public partial class WorldSimulator
{
    /// <summary>
    /// 从当前 WorldState 产出强类型快照（供 SnapshotBroadcaster 下发）。
    /// 使用上一个快照做增量 Diff（BaseTick + 真实 xxHash32 校验和）。
    /// </summary>
    internal SnapshotFrame BuildSnapshot(SnapshotFrame? previous)
        => SnapshotFrame.BuildDelta(_world, previous);
}
