// TerraAuth — Phase 3: WorldSimulator 主文件（partial）
// 架构 §4.3：六阶段确定性 tick

using System.Collections.Generic;
using TerraAuth.Concurrency; // DoubleBufferedWorldState（无依赖的通用原语，同 Authority 层用法）
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

public partial class WorldSimulator : IWorldViewProvider
{
    private readonly WorldState _world;
    private readonly CommandQueue _commands;
    private readonly EventRecorder _recorder;
    private readonly SnapshotStore _snapshots;
    private readonly IRng _rng = new XoshiroRng(0x12345);

    /// <summary>双缓冲发布：仿真线程写 / 快照线程读，读取端无需加锁。</summary>
    private readonly DoubleBufferedWorldState<WorldEntityView> _entityViews = new();

    public WorldState State => _world;

    /// <summary>最近一次 tick 发布的实体视图（快照线程读取，见 <see cref="IWorldViewProvider"/>）。</summary>
    public WorldEntityView CurrentEntityView => _entityViews.Current ?? WorldEntityView.Empty;

    public WorldSimulator(
        WorldState world,
        CommandQueue commands,
        EventRecorder recorder,
        SnapshotStore snapshots)
    {
        _world = world;
        _commands = commands;
        _recorder = recorder;
        _snapshots = snapshots;
    }

    /// <summary>推进一个固定 timestep（架构 §4.3）。</summary>
    public void Tick()
    {
        // tick 从 1 开始编号：先推进，再仿真，快照标记当前 tick
        _world.Tick++;

        // 1. Input：应用本 tick 的 Command
        ApplyCommandsForTick(_world.Tick);

        // 2. AI：NPC / 敌怪逻辑（确定性，走 IRng）
        SimulateAi();

        // 3. Physics：移动、碰撞（走 IRng）
        SimulatePhysics();

        // 4. Combat：伤害结算（服务端权威，武器数据来自权威层）
        SimulateCombat();

        // 5. World：液体、Tile、抛射物
        SimulateWorld();

        // 6. Output：产出快照（Phase 4）
        //    同时发布本 tick 的不可变实体视图 —— 快照线程据此构建/裁剪快照，
        //    不再直接读正在被本线程改动的 WorldState（见 WorldEntityView）。
        var view = WorldEntityView.Extract(_world);
        _snapshots.Add(BuildSnapshot(view, _snapshots.LatestOrDefault));
        _entityViews.Publish(view);
    }

    // ---------- 各阶段（扩展点，逐步填充） ----------

    private void ApplyCommandsForTick(long tick)
    {
        // 从 CommandQueue 取出本 tick 的命令，按 (tick, playerId) 稳定排序后应用
        // Command.Apply 是唯一允许变更 WorldState 的地方
        while (_commands.TryPeek(out var cmd) && cmd is not null && cmd.Tick <= tick)
        {
            _commands.Dequeue();
            cmd.Apply(_world, _rng);
            _recorder.Record(new GameEvent(tick, cmd.PlayerId, cmd.Kind, null));
        }
    }

    // ---------- 各阶段（确定性：固定常量 + 种子化 _rng，禁止读墙钟 / System.Random） ----------

    /// <summary>图格边长（像素）。</summary>
    private const float TileSize = 16f;
    /// <summary>重力加速度（像素 / tick²）。</summary>
    private const float Gravity = 0.4f;
    /// <summary>终端下落速度（像素 / tick）。</summary>
    private const float MaxFallSpeed = 16f;
    /// <summary>玩家碰撞盒半高（原版 42px）。</summary>
    private const float PlayerHalfHeight = 21f;
    /// <summary>超过该下落距离才结算下落伤害（像素）。</summary>
    private const float FallDamageThreshold = 25f * TileSize;
    /// <summary>一个白天的时间单位数（原版 15 分钟 @ 60Hz）。</summary>
    private const double DayLength = 54000.0;
    /// <summary>一个夜晚的时间单位数。</summary>
    private const double NightLength = 32400.0;

    /// <summary>阶段 2：AI。城镇 NPC 在住所附近确定性游走。</summary>
    private void SimulateAi()
    {
        // 每 4 tick 决策一次：降低随机消耗，同时保持移动平滑
        if ((_world.Tick & 3) != 0)
            return;

        foreach (var npc in _world.Npcs)
        {
            // 仅位置记录（非城镇 NPC）不参与 AI
            if (!npc.IsTownNpc)
                continue;

            float direction = (_rng.NextUInt32() & 1) == 0 ? -1f : 1f;
            float step = 0.5f + (float)_rng.NextDouble() * 0.5f;

            // 夹在住所中心 ±4 格内
            float homeX = (npc.HomeTileX + 0.5f) * TileSize;
            npc.X = Math.Clamp(
                npc.X + direction * step,
                homeX - 4f * TileSize,
                homeX + 4f * TileSize);
        }
    }

    /// <summary>阶段 3：物理。玩家重力 + 速度积分 + 图格碰撞 + 世界边界钳制。</summary>
    private void SimulatePhysics()
    {
        foreach (var player in _world.Players.Values)
        {
            if (!player.Active)
                continue;

            // 重力积分（终端速度封顶）
            player.Velocity = new Vector2(
                player.Velocity.X,
                Math.Min(player.Velocity.Y + Gravity, MaxFallSpeed));

            var next = new Vector2(
                player.Position.X + player.Velocity.X,
                player.Position.Y + player.Velocity.Y);

            // 水平边界钳制（Vector2 为只读结构，需重建值）
            if (_world.MaxTilesX > 1)
                next = new Vector2(Math.Clamp(next.X, TileSize, (_world.MaxTilesX - 1) * TileSize), next.Y);

            // 垂直碰撞：检测脚底图格是否实心
            int tileX = (int)(next.X / TileSize);
            int tileY = (int)((next.Y + PlayerHalfHeight) / TileSize);
            bool landed = false;

            if (tileX >= 0 && tileX < _world.Tiles.Width &&
                tileY >= 0 && tileY < _world.Tiles.Height)
            {
                ref var tile = ref _world.Tiles[tileX, tileY];
                if (tile.Active && TileIdSets.IsTileSolid(tile.Type))
                {
                    landed = true;
                    next = new Vector2(next.X, tileY * TileSize - PlayerHalfHeight); // 脚底贴合图格上沿
                    player.Velocity = new Vector2(player.Velocity.X, 0f);
                }
            }

            // 未落地时累计下落距离（供战斗阶段结算下落伤害）
            if (!landed && player.Velocity.Y > 0f)
                player.FallDistance += player.Velocity.Y;

            player.Position = next;
        }
    }

    /// <summary>阶段 4：战斗结算（服务端权威）。当前结算下落伤害。</summary>
    private void SimulateCombat()
    {
        foreach (var player in _world.Players.Values)
        {
            if (!player.Active)
                continue;

            // 仅在落地（垂直速度归零）时结算，且下落距离超过阈值
            if (player.Velocity.Y == 0f)
            {
                if (player.FallDistance > FallDamageThreshold)
                {
                    int damage = (int)((player.FallDistance - FallDamageThreshold) / TileSize);
                    if (damage > 0)
                    {
                        player.Hp = Math.Max(0, player.Hp - damage);
                        if (player.Hp == 0)
                            player.Active = false;

                        _recorder.Record(new GameEvent(
                            _world.Tick, player.Id, "fall_damage", damage));
                    }
                }

                player.FallDistance = 0f;
            }
        }
    }

    /// <summary>阶段 5：世界推进。时间 / 昼夜 / 月相。</summary>
    private void SimulateWorld()
    {
        // 原版每 tick 推进 1 个时间单位（60Hz 下一天 15 分钟）
        _world.Time += 1.0;

        double length = _world.DayTime ? DayLength : NightLength;
        if (_world.Time >= length)
        {
            _world.Time -= length;
            _world.DayTime = !_world.DayTime;

            // 天亮时推进月相
            if (_world.DayTime)
                _world.MoonPhase = (_world.MoonPhase + 1) % 8;
        }
    }
}
