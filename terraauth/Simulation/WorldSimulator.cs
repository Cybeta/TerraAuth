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

    /// <summary>事件记录器：仿真提交事件由本类记录，世界同步线程也用它记录广播 / 持久化事件。</summary>
    public EventRecorder Recorder => _recorder;

    /// <summary>最近一次 tick 发布的实体视图（快照线程读取，见 <see cref="IWorldViewProvider"/>）。</summary>
    public WorldEntityView CurrentEntityView => _entityViews.Current ?? WorldEntityView.Empty;

    // ---------- 服务端事件 / Boss 控制（运维 / 插件 / 测试入口） ----------

    /// <summary>开始血月（持续到次日黎明；由世界同步下发包 7 告知客户端）。</summary>
    public void StartBloodMoon()
    {
        _world.BloodMoon = true;
        _world.ProgressDirty = true;
    }

    /// <summary>开始日食（持续到当天结束）。</summary>
    public void StartEclipse()
    {
        _world.Eclipse = true;
        _world.ProgressDirty = true;
    }

    /// <summary>开始入侵：<paramref name="type"/> 为入侵类型（0 = 无），<paramref name="size"/> 为待刷新配额。</summary>
    public void StartInvasion(int type, int size)
    {
        _world.InvasionType = type;
        _world.InvasionSize = Math.Max(0, size);
        _world.InvasionSizeStart = Math.Max(0, size);
        _world.ProgressDirty = true;
    }

    /// <summary>在指定位置生成一只 Boss（生命上限取自简化表）并返回该 NPC。</summary>
    public WorldNpc SpawnBoss(int npcType, float x, float y)
    {
        int life = BossLife.TryGetValue(npcType, out var hp) ? hp : 1000;

        // 调用方给的是「希望 Boss 出现的位置」→ 换算成原版口径的碰撞盒左上角（X/Y = 左上角，脚底 = Y + height）
        var (width, height) = NpcSizes.Of(npcType);

        var boss = new WorldNpc
        {
            Type = npcType,
            NetId = (short)npcType,
            X = x - width / 2f,
            Y = y - height / 2f,
            IsTownNpc = false,
            IsBoss = true,
            Life = life,
            LifeMax = life,
            Active = true,
            Generation = (byte)(_rng.NextUInt32() & 0xFF),
        };

        lock (_world.NpcsLock) AddNpc(boss);
        _world.ProgressDirty = true;
        return boss;
    }

    /// <summary>
    /// 分配稳定的 NPC 槽位并写入世界列表（对应原版 <c>Main.npc[200]</c> 的固定槽位 + whoAmI 语义）。
    /// 死亡槽位**原位保留、不再整表前移**；新刷怪优先复用已过死亡宽限期的空槽，
    /// 从而保证包 28 的 npcIndex 在 NPC 存活期间不因移除而错位。
    /// 调用方须持有 <see cref="WorldState.NpcsLock"/>。
    /// </summary>
    private void AddNpc(WorldNpc npc)
    {
        for (int i = 0; i < _world.Npcs.Count; i++)
        {
            var slot = _world.Npcs[i];
            if (!slot.IsTownNpc && !slot.Active && _world.Tick - slot.DeadTick > EnemyRemovalDelayTicks)
            {
                _world.Npcs[i] = npc;
                return;
            }
        }

        _world.Npcs.Add(npc);
    }

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

        // 5.5 世界实体：掉落物重力落地 / 弹幕运动与生命周期
        SimulateEntities();

        // 5.6 液体：按脏格集合推进简化流动（下落优先，受阻后向两侧均衡）
        SimulateLiquids();

        // 5.7 箱子会话：玩家离开交互距离 → 关闭会话（协议无「关箱」包，以距离作为可观测等价物）
        ReapChestSessionsOutOfReach();

        // 6. Output：产出快照（Phase 4）
        //    同时发布本 tick 的不可变实体视图 —— 快照线程据此构建/裁剪快照，
        //    不再直接读正在被本线程改动的 WorldState（见 WorldEntityView）。
        var view = WorldEntityView.Extract(_world);
        _snapshots.Add(BuildSnapshot(view, _snapshots.LatestOrDefault));
        _entityViews.Publish(view);
    }

    // ---------- 各阶段（扩展点，逐步填充） ----------

    /// <summary>箱子交互最大距离（像素），与权威层 <c>ChestReachPx</c> 保持一致。</summary>
    private const float ChestReachPx = 160f;

    /// <summary>
    /// 箱子会话距离复核：原版协议没有「关闭箱子」包（客户端关闭时只清本地 <c>chest</c> 字段），
    /// 故以「离开交互距离」作为可观测等价物 —— 玩家走远后服务端主动关闭会话，
    /// 避免会话长期驻留（此后该玩家的箱子写入会被 <c>chest_not_open</c> 拒绝）。
    /// </summary>
    private void ReapChestSessionsOutOfReach()
    {
        var sessions = _world.SnapshotChestSessions();
        if (sessions.Count == 0) return;

        foreach (var (playerId, sessionId, chestIndex) in sessions)
        {
            Vector2 position;
            lock (_world.PlayersLock)
            {
                if (!_world.Players.TryGetValue(playerId, out var player))
                {
                    // 玩家已离线：断线路径本应关闭会话，这里兜底，避免槽位复用时残留
                    _world.CloseChestSession(playerId, sessionId);
                    continue;
                }

                position = player.Position;
            }

            Chest? chest;
            lock (_world.ChestsLock)
                chest = _world.FindChestByIndex(chestIndex);

            if (chest is null)
            {
                _world.CloseChestSession(playerId, sessionId);
                continue;
            }

            var dx = position.X - (chest.X * TileSize + TileSize / 2f);
            var dy = position.Y - (chest.Y * TileSize + TileSize / 2f);
            if (dx * dx + dy * dy > ChestReachPx * ChestReachPx)
                _world.CloseChestSession(playerId, sessionId);
        }
    }

    private void ApplyCommandsForTick(long tick)
    {
        // 取出不晚于当前 tick 的命令，Command.Apply 是唯一允许变更 WorldState 的地方。
        foreach (var cmd in _commands.DrainThrough(tick))
        {
            var result = cmd.Apply(_world, _rng);
            if (result.Applied)
                _recorder.Record(new GameEvent(tick, cmd.PlayerId, cmd.Kind, null));
            else
                _recorder.Record(new GameEvent(
                    tick, cmd.PlayerId, GameEventKinds.CommandFailed, result.Reason,
                    GameEventCategory.Failure));
        }
    }

    // ---------- 各阶段（确定性：固定常量 + 种子化 _rng，禁止读墙钟 / System.Random） ----------

    /// <summary>图格边长（像素）。</summary>
    private const float TileSize = 16f;
    /// <summary>掉落物重力加速度（像素 / tick²）。</summary>
    private const float Gravity = 0.4f;
    /// <summary>终端下落速度（像素 / tick）。</summary>
    private const float MaxFallSpeed = 16f;

    /// <summary>玩家重力（像素 / tick²）。原版 <c>Player.defaultGravity = 0.4</c>；服务端按控制位模拟玩家（见 StepPlayerPhysics）。</summary>
    private const float PlayerGravity = 0.4f;

    /// <summary>玩家终端下落速度（原版 <c>Player.maxFallSpeed = 10</c>）。</summary>
    private const float PlayerMaxFallSpeed = 10f;

    /// <summary>玩家水平最大速度（原版 <c>Player.originalRunSpeed = 3</c>，即 <c>maxRunSpeed</c>）。</summary>
    private const float PlayerRunSpeed = 3f;

    /// <summary>玩家水平加速度（原版 <c>Player.runAcceleration = 0.08</c>）。</summary>
    private const float PlayerRunAcceleration = 0.08f;

    /// <summary>玩家水平减速 / 反向刹车（原版 <c>Player.runSlowdown = 0.2</c>）。</summary>
    private const float PlayerRunSlowdown = 0.2f;

    /// <summary>玩家起跳初速（原版 <c>Player.jumpSpeed = 5.01</c>）。</summary>
    private const float PlayerJumpSpeed = 5.01f;

    /// <summary>
    /// NPC 重力 / 终端下落速度：原版 <c>NPC.UpdateNPC_UpdateGravity</c> 的默认值
    /// （<c>gravity = 0.3</c>、<c>maxFallSpeed = 10</c>；少数类型有覆盖，我们模拟的这些都没有）。
    /// **必须与原版一致** —— 客户端会按原版值自行推进 NPC，服务端用别的值会让双方位置持续发散：
    /// 客户端把 NPC 画在自己算出的位置（`netOffset` 平滑），而**碰撞判定用的是服务端位置**，
    /// 于是出现「史莱姆没碰到我却在扣血」。
    /// </summary>
    private const float NpcGravity = 0.3f;
    private const float NpcMaxFallSpeed = 10f;
    /// <summary>玩家碰撞盒半高（原版 42px 的一半）：用于取玩家碰撞盒**中心**（<c>Position.Y + 21</c>）。</summary>
    private const float PlayerHalfHeight = NpcSizes.PlayerHeight / 2f;
    /// <summary>玩家碰撞盒高度（原版 42px）。原版约定 <c>Position</c> 为碰撞盒左上角 → 脚底 = <c>Position.Y + 42</c>。</summary>
    private const float PlayerHeight = NpcSizes.PlayerHeight;
    /// <summary>超过该下落距离才结算下落伤害（像素）。</summary>
    private const float FallDamageThreshold = 25f * TileSize;
    /// <summary>一个白天的时间单位数（原版 15 分钟 @ 60Hz）。</summary>
    private const double DayLength = 54000.0;
    /// <summary>一个夜晚的时间单位数。</summary>
    private const double NightLength = 32400.0;

    /// <summary>同屏敌怪上限（达到后不再刷怪）。</summary>
    private const int MaxEnemies = 8;
    /// <summary>刷怪 / 死亡清理间隔（tick）。</summary>
    private const int SpawnIntervalTicks = 60;
    /// <summary>敌怪清理延迟（tick）：确保 life=0 已通过世界同步下发后再从列表移除。</summary>
    private const long EnemyRemovalDelayTicks = 120;
    /// <summary>史莱姆（原版 NPC 类型 ID 1）与其生命值。</summary>
    private const short BlueSlimeType = 1;
    private const int BlueSlimeLife = 25;

    /// <summary>眼魔（Boss，原版 NPC 类型 ID 4）。</summary>
    private const short EyeOfCthulhuType = 4;

    /// <summary>哥布林苦工（入侵怪，原版 NPC 类型 ID 26）与其生命值。</summary>
    private const short GoblinPeonType = 26;
    private const int GoblinPeonLife = 60;

    /// <summary>Boss 飞行速度（像素 / tick）。</summary>
    private const float BossSpeed = 2f;

    /// <summary>Boss 类型 → 生命上限（简化表；未收录类型按 1000 处理）。</summary>
    private static readonly Dictionary<int, int> BossLife = new()
    {
        [4] = 2800,   // Eye of Cthulhu
        [35] = 4400,  // Skeletron
        [50] = 2000,  // King Slime
    };

    /// <summary>
    /// 阶段 5.5：世界实体（服务端权威）。
    /// 掉落物：重力 + 图格落地；弹幕：直线积分 + 生存期耗尽即失效。
    /// </summary>
    private void SimulateEntities()
    {
        lock (_world.ItemsLock)
        {
            foreach (var item in _world.Items)
            {
                if (!item.Active) continue;

                item.Velocity = new Vector2(item.Velocity.X * 0.99f,
                    Math.Min(item.Velocity.Y + Gravity, MaxFallSpeed));

                var next = new Vector2(item.Position.X + item.Velocity.X, item.Position.Y + item.Velocity.Y);
                int tileX = (int)(next.X / TileSize);
                int tileY = (int)((next.Y + 8f) / TileSize); // 物品半高 8px
                if (tileX >= 0 && tileX < _world.Tiles.Width && tileY >= 0 && tileY < _world.Tiles.Height)
                {
                    ref var tile = ref _world.Tiles[tileX, tileY];
                    if (tile.Active && TileIdSets.IsTileSolid(tile.Type))
                    {
                        next = new Vector2(next.X, tileY * TileSize - 8f);
                        item.Velocity = new Vector2(item.Velocity.X * 0.8f, 0f);
                    }
                }
                item.Position = next;
            }

            // 回收：已失效且销毁包已下发（或超过兜底宽限）的掉落物从列表移除，
            // 否则列表只增不减，长跑下每 tick 遍历与快照过滤都会越来越慢。
            // 「不在失效的同一 tick 移除」——留一 tick 让观察方（世界同步 / 事件 / 测试）看到失效态。
            _world.Items.RemoveAll(item => !item.Active && _world.Tick > item.DeadTick
                && (item.RemovalNotified || _world.Tick - item.DeadTick > EntityRemovalGraceTicks));
        }

        lock (_world.ProjectilesLock)
        {
            foreach (var p in _world.Projectiles)
            {
                if (!p.Active) continue;

                // 原版字段驱动的行为（图格碰撞 / 重力 / extraUpdates / 生存期钳制）：
                // 仅登记过的类型（服务端发射的 Boss 弹幕）生效，其余保持简化直线积分。
                var behavior = ProjectileBehaviorOf(p.Type);
                if (behavior.MaxLifetime > 0 && p.TimeLeft > behavior.MaxLifetime)
                    p.TimeLeft = behavior.MaxLifetime;

                if (StepProjectile(p, in behavior))
                {
                    p.Active = false;
                    p.DeadTick = _world.Tick; // 撞图格 / 出界 → 由世界同步补发包 29
                    continue;
                }

                // 命中判定（服务端权威）：仅**玩家弹幕**结算敌怪伤害；敌对弹幕（Owner = -1）
                // 打玩家，见 SimulateCombat。
                var hit = p.Owner >= 0 ? FindHitEnemy(p) : null;
                if (hit is not null)
                {
                    int damage = Math.Max(1, p.Damage);
                    hit.Life -= damage;
                    if (hit.Life <= 0)
                    {
                        hit.Life = 0;
                        hit.Active = false;
                        hit.DeadTick = _world.Tick;
                        _world.NotifyNpcKilled(hit.Type, hit.X, hit.Y); // Boss 击杀 → 世界进度 + 掉落
                    }
                    p.Active = false;
                    p.DeadTick = _world.Tick;
                    _recorder.Record(new GameEvent(_world.Tick, p.Owner, GameEventKinds.ProjectileHit, damage));
                    continue;
                }

                if (--p.TimeLeft <= 0)
                {
                    p.Active = false;
                    p.DeadTick = _world.Tick; // 由世界同步补发包 29（客户端掉线时也能清理）
                }
            }

            // 回收：同掉落物 —— 销毁包下发成功后移除，避免弹幕列表只增不减。
            _world.Projectiles.RemoveAll(p => !p.Active && _world.Tick > p.DeadTick
                && (p.RemovalNotified || _world.Tick - p.DeadTick > EntityRemovalGraceTicks));
        }
    }

    /// <summary>
    /// 已失效掉落物 / 弹幕的回收宽限（tick）：销毁包下发成功（<c>RemovalNotified</c>）即可移除；
    /// 若下发持续失败（无连接 / 瞬时错误），最多保留该时长作为兜底，避免客户端留下幽灵实体。
    /// </summary>
    private const long EntityRemovalGraceTicks = 600;   // 10s @60Hz

    /// <summary>每 tick 处理的液体格数上限（限制大范围流动对 tick 的占用）。</summary>
    private const int MaxLiquidStepsPerTick = 2000;

    /// <summary>
    /// 阶段 5.6：液体简化仿真（服务端权威）。
    /// 只处理「脏格」集合（编辑 / 上次流动波及的格子），避免全图扫描：
    ///   1) 优先向下方流动（下方非实心且液体类型兼容）；
    ///   2) 下方受阻时，向两侧液面更低处均衡（每 tick 每侧最多 1 单位）。
    /// 与可玩性无关的原版压力模型不同，属简化模型；但液体量与类型均由服务端持有并同步。
    /// </summary>
    private void SimulateLiquids()
    {
        var cells = _world.TakeLiquidDirty(MaxLiquidStepsPerTick);
        if (cells.Count == 0) return;

        foreach (var (x, y) in cells)
            SimulateLiquidCell(x, y);
    }

    /// <summary>混合反应所需的最小异种液体量（原版规则为 24 单位）。</summary>
    private const int LiquidMergeThreshold = 24;

    /// <summary>
    /// 液体混合反应（按原版客户端行为核对，简化模型）：
    /// 本格液体与相邻（左右 / 上 / 下）异种液体接触，异种量累计 ≥ 24 → 消耗异种液体、清空本格并生成混合图格。
    /// 图格 ID 已核对：水 + 岩浆 → 黑曜石 56、水 + 蜂蜜 → 蜂蜜块 229、岩浆 + 蜂蜜 → 松脆蜂蜜块 230、微光 → 微光块 659。
    /// 简化点：仅在本格为空时生成（原版还允许覆盖可被黑曜石破坏的图格），且生成位置取本格。
    /// </summary>
    private bool TryLiquidMerge(int x, int y)
    {
        var self = ReadTile(x, y);
        if (self.Liquid <= 0 || self.Active) return false;

        byte otherType = 0;
        int otherAmount = ConsumeOtherLiquid(x - 1, y, self.LiquidType, ref otherType)
                        + ConsumeOtherLiquid(x + 1, y, self.LiquidType, ref otherType)
                        + ConsumeOtherLiquid(x, y - 1, self.LiquidType, ref otherType)
                        + ConsumeOtherLiquid(x, y + 1, self.LiquidType, ref otherType);

        if (otherAmount < LiquidMergeThreshold) return false;

        int mergeTile = MergeTileFor(self.LiquidType, otherType);
        if (mergeTile < 0) return false;

        self.Active = true;
        self.Type = (ushort)mergeTile;
        self.Liquid = 0;
        self.LiquidType = 0;
        WriteTile(x, y, in self);

        _world.MarkLiquidChanged(x, y);
        _world.MarkTileChanged(x, y); // 新方块推送客户端（包 10 小矩形）
        _recorder.Record(new GameEvent(_world.Tick, 0, GameEventKinds.LiquidMerge, mergeTile));
        return true;
    }

    /// <summary>消耗一格与 <paramref name="myType"/> 不同的液体并返回其数量；同时记录异种类型。</summary>
    private int ConsumeOtherLiquid(int x, int y, byte myType, ref byte otherType)
    {
        if (x < 0 || x >= _world.MaxTilesX || y < 0 || y >= _world.MaxTilesY) return 0;

        var tile = ReadTile(x, y);
        if (tile.Liquid == 0 || tile.LiquidType == myType) return 0;

        int amount = tile.Liquid;
        otherType = tile.LiquidType;
        tile.Liquid = 0;
        WriteTile(x, y, in tile);
        _world.MarkLiquidChanged(x, y);
        return amount;
    }

    /// <summary>两种液体混合产出的图格 ID（按原版客户端行为核对）；-1 表示无反应。</summary>
    private static int MergeTileFor(byte selfType, byte otherType) => (selfType, otherType) switch
    {
        (0, 1) or (1, 0) => 56,   // 水 + 岩浆 → 黑曜石
        (0, 2) or (2, 0) => 229,  // 水 + 蜂蜜 → 蜂蜜块
        (1, 2) or (2, 1) => 230,  // 岩浆 + 蜂蜜 → 松脆蜂蜜块
        (3, _) or (_, 3) => 659,  // 微光 + 任意 → 微光块
        _ => -1,
    };

    private void SimulateLiquidCell(int x, int y)
    {
        if (x < 0 || x >= _world.MaxTilesX || y < 0 || y >= _world.MaxTilesY) return;

        // 0) 混合反应：异种液体接触且累计 ≥ 24 单位 → 生成混合图格
        if (TryLiquidMerge(x, y)) return;

        var tile = ReadTile(x, y);
        if (tile.Liquid < 2) return;

        int amount = tile.Liquid;
        byte type = tile.LiquidType;

        // 1) 优先下落
        int belowY = y + 1;
        if (belowY < _world.MaxTilesY)
        {
            var below = ReadTile(x, belowY);
            if (!IsLiquidBlocking(in below) && (below.Liquid == 0 || below.LiquidType == type))
            {
                int move = Math.Min(amount, 255 - below.Liquid);
                if (move > 0)
                {
                    below.Liquid = (byte)(below.Liquid + move);
                    below.LiquidType = type;
                    WriteTile(x, belowY, in below);
                    _world.MarkLiquidChanged(x, belowY);

                    WriteLiquid(x, y, amount - move, type);
                    return; // 下落过程中不做水平扩散
                }
            }
        }

        // 2) 下方受阻 → 向两侧低位均衡
        for (int dir = -1; dir <= 1 && amount >= 2; dir += 2)
        {
            int nx = x + dir;
            if (nx < 0 || nx >= _world.MaxTilesX) continue;

            var side = ReadTile(nx, y);
            if (IsLiquidBlocking(in side)) continue;
            if (side.Liquid != 0 && side.LiquidType != type) continue;
            if (side.Liquid + 1 >= amount) continue; // 只向液面明显更低的一侧扩散

            side.Liquid++;
            side.LiquidType = type;
            WriteTile(nx, y, in side);
            _world.MarkLiquidChanged(nx, y);
            amount--;
        }

        if (amount != tile.Liquid)
            WriteLiquid(x, y, amount, type);
    }

    /// <summary>读取单格图格（区块读锁内取副本）。</summary>
    private Tile ReadTile(int x, int y)
    {
        using (_world.Sections.EnterRead(x, y, x, y))
            return _world.Tiles[x, y];
    }

    /// <summary>写入单格图格（区块写锁内）。</summary>
    private void WriteTile(int x, int y, in Tile tile)
    {
        _world.Sections.EnterWrite(x, y);
        try
        {
            _world.Tiles[x, y] = tile;
        }
        finally
        {
            _world.Sections.ExitWrite(x, y);
        }
    }

    /// <summary>写入单格液体量 / 类型并标记为「已变化」（触发继续仿真 + 下发）。</summary>
    private void WriteLiquid(int x, int y, int amount, byte type)
    {
        byte clamped = (byte)Math.Clamp(amount, 0, 255);

        _world.Sections.EnterWrite(x, y);
        try
        {
            ref var tile = ref _world.Tiles[x, y];
            tile.Liquid = clamped;
            tile.LiquidType = clamped == 0 ? (byte)0 : type;
        }
        finally
        {
            _world.Sections.ExitWrite(x, y);
        }

        _world.MarkLiquidChanged(x, y);
    }

    /// <summary>该格是否阻挡液体（实心方块；已通电 / 未通电的执行器方块按通电状态判定）。</summary>
    private static bool IsLiquidBlocking(in Tile tile)
        => tile.Active && TileIdSets.IsTileSolid(tile.Type) && !tile.InActive;

    /// <summary>
    /// 查找被弹幕命中的存活敌怪（不含城镇 NPC）：弹幕碰撞盒与 NPC 碰撞盒（原版逐类型尺寸）求交。
    /// </summary>
    private WorldNpc? FindHitEnemy(ProjectileEntity p)
    {
        lock (_world.NpcsLock)
        {
            foreach (var npc in _world.Npcs)
            {
                if (!npc.Active || npc.IsTownNpc) continue;

                var (width, height) = NpcSizes.Of(npc.Type);
                if (BoxesOverlap(p.Position.X, p.Position.Y, ProjectileHitBoxSize, ProjectileHitBoxSize,
                                 npc.X, npc.Y, width, height))
                    return npc;
            }
        }
        return null;
    }

    /// <summary>弹幕命中判定用的碰撞盒边长（像素）：原版弹幕宽度多为 6~16，统一取 16 作为宽容近似。</summary>
    private const int ProjectileHitBoxSize = 16;

    /// <summary>AI 执行期间产生的刷怪请求（遍历结束后统一入队，避免遍历中修改集合）。</summary>
    private readonly List<WorldNpc> _pendingNpcSpawns = new();

    /// <summary>AI 执行期间产生的弹幕请求（遍历结束后统一入队，避免遍历中修改集合）。</summary>
    private readonly List<ProjectileEntity> _pendingProjectiles = new();

    /// <summary>阶段 2：AI。城镇 NPC 在住所附近平滑游走；敌怪朝最近玩家水平移动并受重力。</summary>
    private void SimulateAi()
    {
        lock (_world.NpcsLock)
        {
            if (_world.Tick % SpawnIntervalTicks == 0)
            {
                TrySpawnEnemy();
                // 死亡 NPC 不再整表移除（会前移下标导致包 28 错位）；槽位原位保留，由 AddNpc 复用。
            }

            _pendingNpcSpawns.Clear();
            _pendingProjectiles.Clear();

            foreach (var npc in _world.Npcs)
            {
                if (!npc.Active) continue;

                // 全部 NPC（含城镇 NPC）统一走 aiStyle 分派 + 共用物理步；
                // Boss 例外：仍为自移动的简化追击（见 RunNpcAi）
                RunNpcAi(npc);
            }

            // AI 期间产生的刷怪 / 弹幕请求在遍历结束后入队，避免遍历中修改集合
            if (_pendingNpcSpawns.Count > 0)
            {
                foreach (var npc in _pendingNpcSpawns)
                    AddNpc(npc);
                _pendingNpcSpawns.Clear();
            }

            if (_pendingProjectiles.Count > 0)
            {
                lock (_world.ProjectilesLock)
                    _world.Projectiles.AddRange(_pendingProjectiles);

                _pendingProjectiles.Clear();
            }
        }
    }

    /// <summary>Boss 一帧：朝最近玩家飞行追击（简化为直线移动，不做专属 AI 阶段）。</summary>
    private void SimulateBossStep(WorldNpc npc)
    {
        var target = NearestPlayer(npc.X, npc.Y);
        if (target is null)
        {
            npc.VelocityX = 0f;
            npc.VelocityY = 0f;
            return;
        }

        float dx = target.AimPosition.X - npc.X;
        float dy = target.AimPosition.Y - npc.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > 1f)
        {
            npc.VelocityX = dx / len * BossSpeed;
            npc.VelocityY = dy / len * BossSpeed;
        }

        npc.X += npc.VelocityX;
        npc.Y += npc.VelocityY;
    }

    /// <summary>随机挑选一名在线且未死亡的玩家（无则返回 null）。</summary>
    private PlayerRuntime? PickPlayer()
    {
        var candidates = new List<PlayerRuntime>(_world.Players.Count);
        foreach (var p in _world.Players.Values)
            if (p.Active && !p.Dead) candidates.Add(p);

        return candidates.Count == 0
            ? null
            : candidates[(int)(_rng.NextUInt32() % (uint)candidates.Count)];
    }

    /// <summary>是否存在存活的 Boss。</summary>
    private bool AnyBossAlive()
    {
        foreach (var n in _world.Npcs)
            if (n.Active && n.IsBoss) return true;
        return false;
    }

    /// <summary>
    /// 确定性刷怪：入侵期内优先刷新入侵怪（消耗入侵配额）；夜晚且未击败 Boss 时小概率刷新 Boss；
    /// 否则在随机在线玩家附近的地表生成一只史莱姆。
    /// </summary>
    private void TrySpawnEnemy()
    {
        if (_world.InvasionType != 0 && _world.InvasionSize > 0 && TrySpawnInvasionEnemy())
            return;

        // 简化 Boss 触发条件：夜晚 + 未击败眼魔 + 无存活 Boss + 低概率
        if (!_world.DayTime && !_world.Progress.DownedBoss1 && !AnyBossAlive()
            && _rng.NextUInt32() % 120 == 0)
        {
            var bossTarget = PickPlayer();
            if (bossTarget is not null)
            {
                SpawnBoss(EyeOfCthulhuType, bossTarget.AimPosition.X, bossTarget.AimPosition.Y - 8f * TileSize);
                return;
            }
        }

        int enemies = 0;
        foreach (var n in _world.Npcs)
            if (n.Active && !n.IsTownNpc) enemies++;
        if (enemies >= MaxEnemies) return;

        var target = PickPlayer();
        if (target is null) return;

        int spawnTileX = (int)(target.AimPosition.X / TileSize) + ((_rng.NextUInt32() & 1) == 0 ? -12 : 12);
        if (spawnTileX < 1 || spawnTileX >= _world.MaxTilesX - 1) return;

        // 自上而下找第一个实心格作为落脚点
        for (int y = 1; y < _world.MaxTilesY - 1; y++)
        {
            ref var tile = ref _world.Tiles[spawnTileX, y];
            if (!tile.Active || !TileIdSets.IsTileSolid(tile.Type)) continue;

            var (slimeW, slimeH) = NpcSizes.Of(BlueSlimeType);
            AddNpc(new WorldNpc
            {
                Type = BlueSlimeType,
                NetId = BlueSlimeType,
                AiStyle = 1,            // 原版 aiStyle 1（Slimes）
                X = (spawnTileX + 0.5f) * TileSize - slimeW / 2f,   // 原版：X/Y = 碰撞盒左上角
                Y = y * TileSize - slimeH,                          // 脚底贴地表上沿（脚底 = Y + height）
                IsTownNpc = false,
                Life = BlueSlimeLife,
                LifeMax = BlueSlimeLife,
                Active = true,
                Generation = (byte)(_rng.NextUInt32() & 0xFF),
            });
            return;
        }
    }

    /// <summary>刷新一只入侵怪（哥布林）并消耗 1 个入侵配额；配额归零则结束入侵。</summary>
    private bool TrySpawnInvasionEnemy()
    {
        var target = PickPlayer();
        if (target is null) return false;

        int spawnTileX = (int)(target.AimPosition.X / TileSize) + ((_rng.NextUInt32() & 1) == 0 ? -12 : 12);
        if (spawnTileX < 1 || spawnTileX >= _world.MaxTilesX - 1) return false;

        for (int y = 1; y < _world.MaxTilesY - 1; y++)
        {
            ref var tile = ref _world.Tiles[spawnTileX, y];
            if (!tile.Active || !TileIdSets.IsTileSolid(tile.Type)) continue;

            var (goblinW, goblinH) = NpcSizes.Of(GoblinPeonType);
            AddNpc(new WorldNpc
            {
                Type = GoblinPeonType,
                NetId = GoblinPeonType,
                AiStyle = 3,            // 原版 aiStyle 3（Fighters）
                X = (spawnTileX + 0.5f) * TileSize - goblinW / 2f,   // 原版：X/Y = 碰撞盒左上角
                Y = y * TileSize - goblinH,                          // 脚底贴地表上沿
                IsTownNpc = false,
                Life = GoblinPeonLife,
                LifeMax = GoblinPeonLife,
                Active = true,
                Generation = (byte)(_rng.NextUInt32() & 0xFF),
            });

            if (--_world.InvasionSize <= 0)
            {
                _world.InvasionSize = 0;
                _world.InvasionType = 0;
                _world.ProgressDirty = true; // 入侵结束 → 包 7 重新下发
            }
            return true;
        }
        return false;
    }

    /// <summary>距 (x, y) 最近的在线玩家；无在线玩家时返回 <c>null</c>。</summary>
    private PlayerRuntime? NearestPlayer(float x, float y)
    {
        PlayerRuntime? best = null;
        var bestSq = float.MaxValue;
        foreach (var p in _world.Players.Values)
        {
            if (!p.Active || p.Dead) continue;
            var dx = p.AimPosition.X - x;
            var dy = p.AimPosition.Y - y;
            var d = dx * dx + dy * dy;
            if (d < bestSq)
            {
                bestSq = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>阶段 3：物理。玩家重力 + 速度积分 + 图格碰撞 + 世界边界钳制。</summary>
    private void SimulatePhysics()
    {
        foreach (var player in _world.Players.Values)
        {
            if (!player.Active || player.Dead)
                continue;

            StepPlayerPhysics(player);
        }
    }

    /// <summary>
    /// 阶段 3.1：玩家物理 —— **按包 13 的控制位在服务端模拟玩家**（对齐原版 <c>Player.Update</c>）。
    /// 原版服务端在 <c>Main.Update</c> 里对所有 active 玩家（含远端）执行 <c>Player.Update</c> —— 这才是
    /// 「服务端坐标与客户端一致」的根本原因；而客户端只在**操作变化**时发位置包，只信位置包会让服务端坐标
    /// 长时间停在原地，NPC 于是去追/打玩家早已离开的位置（真机症状：看着没被碰到却在扣血）。
    /// 常数取自原版 <c>Player</c>：maxRunSpeed 3 / runAcceleration 0.08 / runSlowdown 0.2 /
    /// gravity 0.4 / maxFallSpeed 10 / jumpSpeed 5.01。
    /// </summary>
    private void StepPlayerPhysics(PlayerRuntime player)
    {
        bool left = player.PressingLeft, right = player.PressingRight;

        // 起跳 / 地面摩擦都要看「当前是否贴地」：先用当前位置探一次脚底 ——
        // 首次 tick（Grounded 还是默认值）与从空中落地的那一 tick 都靠它。
        player.Grounded = PlayerFeetOnGround(player.Position);

        // ---- 水平：原版 runAcceleration / runSlowdown 分支（无装备时 accRunSpeed == maxRunSpeed）----
        float vx = player.Velocity.X;
        if (left && vx > -PlayerRunSpeed)
        {
            if (vx > PlayerRunSlowdown) vx -= PlayerRunSlowdown;   // 反向时先刹车
            vx -= PlayerRunAcceleration;
        }
        else if (right && vx < PlayerRunSpeed)
        {
            if (vx < -PlayerRunSlowdown) vx += PlayerRunSlowdown;
            vx += PlayerRunAcceleration;
        }
        else if (player.Grounded && !left && !right)
        {
            // 无输入且贴地：按 runSlowdown 收敛到 0（原版同一分支）
            if (vx > PlayerRunSlowdown) vx -= PlayerRunSlowdown;
            else if (vx < -PlayerRunSlowdown) vx += PlayerRunSlowdown;
            else vx = 0f;
        }
        vx = Math.Clamp(vx, -PlayerRunSpeed, PlayerRunSpeed);

        // ---- 垂直：起跳（要求「松开后再按」，原版 releaseJump）+ 重力 ----
        float vy = player.Velocity.Y;
        bool jumpPressed = player.PressingJump && !player.JumpHeld;
        player.JumpHeld = player.PressingJump;
        if (jumpPressed && player.Grounded && !player.PressingDown)
        {
            vy = -PlayerJumpSpeed;
            player.Grounded = false;
        }
        vy = Math.Min(vy + PlayerGravity, PlayerMaxFallSpeed);

        var next = new Vector2(player.Position.X + vx, player.Position.Y + vy);

        // ---- 水平阻挡：前进方向的边缘格是实心则停在原地 ----
        if (vx != 0f)
        {
            int col = (int)((vx > 0f ? next.X + NpcSizes.PlayerWidth : next.X) / TileSize);
            int midRow = (int)((next.Y + PlayerHalfHeight) / TileSize);
            int lowRow = (int)((next.Y + PlayerHeight - 1f) / TileSize);
            if (NpcTileSolid(col, midRow) || NpcTileSolid(col, lowRow))
            {
                next = new Vector2(player.Position.X, next.Y);
                vx = 0f;
            }
        }

        // ---- 落地：脚底（Y + 42）左右两列任一实心（玩家宽 20 会跨两列，只探左列会在台阶边缘漏判）----
        int feetRow = (int)((next.Y + PlayerHeight) / TileSize);
        bool landed = PlayerFeetOnGround(next);
        if (landed)
        {
            next = new Vector2(next.X, feetRow * TileSize - PlayerHeight);   // 脚底贴合图格上沿
            vy = 0f;
        }
        else if (vy > 0f)
        {
            player.FallDistance += vy;   // 下落伤害在战斗阶段按此结算
        }

        if (_world.MaxTilesX > 1)
            next = new Vector2(Math.Clamp(next.X, TileSize, (_world.MaxTilesX - 1) * TileSize), next.Y);

        player.Velocity = new Vector2(vx, vy);
        player.Grounded = landed;
        if (vx != 0f) player.Direction = vx > 0f ? 1 : -1;
        player.Position = next;
        player.AimPosition = next;   // 「游戏判定用」位置 = 模拟位置
    }

    /// <summary>给定位置的脚底（<c>Y + 42</c>）是否踩在实心格上（宽 20 跨两列，任一列实心即算）。</summary>
    private bool PlayerFeetOnGround(Vector2 position)
    {
        int leftCol = (int)(position.X / TileSize);
        int rightCol = (int)((position.X + NpcSizes.PlayerWidth - 1) / TileSize);
        int feetRow = (int)((position.Y + PlayerHeight) / TileSize);
        return NpcTileSolid(leftCol, feetRow) || NpcTileSolid(rightCol, feetRow);
    }

    /// <summary>
    /// 阶段 4：战斗结算（服务端权威）。下落伤害 + 敌怪 / Boss 接触伤害。
    /// 接触伤害由服务端判定并结算（客户端上报的受击仅作参考），避免「漏报伤害避免死亡」。
    /// </summary>
    private void SimulateCombat()
    {
        foreach (var player in _world.Players.Values)
        {
            if (!player.Active || player.Dead)
                continue;

            // 4.1 下落伤害：仅在落地（垂直速度归零）时结算，且下落距离超过阈值
            if (player.Velocity.Y == 0f)
            {
                if (player.FallDistance > FallDamageThreshold)
                {
                    int damage = (int)((player.FallDistance - FallDamageThreshold) / TileSize);
                    if (damage > 0)
                    {
                        ApplyPlayerDamage(player, damage, "fall_damage", PlayerRuntime.GeneralImmunityTicks(damage));
                        LogPlayerDamage(player, damage, "fall_damage", $"下落距离={player.FallDistance:F0}");
                    }
                }

                player.FallDistance = 0f;
            }

            if (player.Dead) continue;

            // 4.2 接触伤害：受免伤帧约束
            if (player.HurtCooldown > 0)
            {
                player.HurtCooldown--;
                continue;
            }

            int contact = FindContactDamage(player, out var contactNpc);
            if (contact > 0)
            {
                ApplyPlayerDamage(player, contact, "contact_damage", PlayerRuntime.GeneralImmunityTicks(contact));
                LogPlayerDamage(player, contact, "contact_damage",
                    $"NPC {contactNpc!.Type}@{contactNpc.X:F0},{contactNpc.Y:F0} " +
                    $"玩家判定={player.AimPosition.X:F0},{player.AimPosition.Y:F0}（上报={player.Position.X:F0},{player.Position.Y:F0}）");
                continue;
            }

            // 4.3 敌对弹幕伤害（Boss 弹幕）：同样受免伤帧约束
            int projectile = FindHostileProjectileDamage(player);
            if (projectile > 0)
                ApplyPlayerDamage(player, projectile, "projectile_damage", PlayerRuntime.GeneralImmunityTicks(projectile));
        }
    }

    /// <summary>诊断输出：玩家受伤来源（含接触方位置），用于定位「没碰到却掉血」。</summary>
    private void LogPlayerDamage(PlayerRuntime player, int damage, string kind, string detail)
    {
        if (Interlocked.Increment(ref _damageLogCount) > 500) return;
        Console.WriteLine($"[Damage] 玩家 #{player.Id} -{damage}（{kind}）HP={player.Hp} {detail}");
    }

    private int _damageLogCount;

    /// <summary>敌怪 / Boss 接触伤害表（简化：按类型固定值，未收录按普通敌怪计）。</summary>
    private static int ContactDamageOf(int npcType) => npcType switch
    {
        26 => 12,   // Goblin Peon
        4 => 20,    // Eye of Cthulhu
        35 => 30,   // Skeletron
        50 => 20,   // King Slime
        _ => 7,     // 史莱姆等普通敌怪
    };

    /// <summary>
    /// 查找与玩家碰撞盒重叠的敌怪伤害（取接触者中的最大值）；无接触返回 0。
    /// 判定口径与原版 <c>Player.Update_NPCCollision</c> 一致：玩家 / NPC 盒各自取整后做 AABB 求交，
    /// **不设最小重叠**（两轴各 1px 即命中），且用逐类型尺寸而非「点 + 半径」。
    /// <paramref name="contactNpc"/> 回传实际接触的 NPC（诊断输出用）。
    /// </summary>
    private int FindContactDamage(PlayerRuntime player, out WorldNpc? contactNpc)
    {
        float px = player.AimPosition.X, py = player.AimPosition.Y;
        int best = 0;
        contactNpc = null;

        lock (_world.NpcsLock)
        {
            foreach (var npc in _world.Npcs)
            {
                if (!npc.Active || npc.IsTownNpc) continue;

                var (width, height) = NpcSizes.Of(npc.Type);
                if (!PlayerTouchesNpc(px, py, npc.X, npc.Y, width, height))
                    continue;

                int damage = ContactDamageOf(npc.Type);
                if (damage > best)
                {
                    best = damage;
                    contactNpc = npc;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// 玩家盒（<see cref="NpcSizes.PlayerWidth"/> × <see cref="NpcSizes.PlayerHeight"/>）与 NPC 盒是否相交。
    /// 原版 <c>Player.Update_NPCCollision</c> 的做法是 <c>new Rectangle((int)position.X, (int)position.Y, width, height)</c>
    /// 对 NPC 同法取整后 <c>Rectangle.Intersects</c> —— **取整后再比、无最小重叠**。
    /// 两端同口径取整，位置一致时判定必然一致；位置不一致要靠对齐位置解决，而不是放大阈值。
    /// </summary>
    private static bool PlayerTouchesNpc(float px, float py, float nx, float ny, int nw, int nh)
    {
        int px0 = (int)px, py0 = (int)py;
        int nx0 = (int)nx, ny0 = (int)ny;
        return px0 < nx0 + nw && nx0 < px0 + NpcSizes.PlayerWidth
            && py0 < ny0 + nh && ny0 < py0 + NpcSizes.PlayerHeight;
    }

    /// <summary>两个轴对齐碰撞盒是否重叠（像素坐标，X/Y 为左上角；浮点精度，无最小重叠）。</summary>
    private static bool BoxesOverlap(
        float ax, float ay, int aw, int ah,
        float bx, float by, int bw, int bh)
        => ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;

    /// <summary>
    /// 服务端结算玩家伤害：扣血 → 置免伤帧 → 必要时置死亡态 → 登记受击通知（包 117 表现 + 包 16 权威血量）。
    /// 这是玩家生命的唯一权威入口（客户端上报的包 117 只做非负校验，不直接改血）。
    /// <paramref name="immunityTicks"/> 按原版 <c>Player.Hurt</c> 的 <c>immuneTime</c> 取值
    /// （接触 / 包 117 / 下落 / 弹幕共用 <see cref="PlayerRuntime.GeneralImmunityTicks"/>：40 / 20）。
    /// </summary>
    private void ApplyPlayerDamage(PlayerRuntime player, int damage, string kind, int immunityTicks)
    {
        if (damage <= 0 || player.Dead) return;

        player.Hp = Math.Max(0, player.Hp - damage);
        player.HurtCooldown = immunityTicks;
        player.FallDistance = 0f;

        if (player.Hp == 0)
        {
            // 死亡：置死亡态而非离线态（Active 表示在线），等待复活命令复位
            player.Dead = true;
            player.DeathNotified = false;
            player.Velocity = new Vector2(0, 0);
        }

        _world.MarkPlayerHurt(player.Id, damage);
        _recorder.Record(new GameEvent(_world.Tick, player.Id, kind, damage));
    }

    /// <summary>阶段 5：世界推进。时间 / 昼夜 / 月相 / 简化事件（血月 · 日食）。</summary>
    private void SimulateWorld()
    {
        // 原版每 tick 推进 1 个时间单位（60Hz 下一天 15 分钟）
        _world.Time += 1.0;

        double length = _world.DayTime ? DayLength : NightLength;
        if (_world.Time < length) return;

        _world.Time -= length;
        _world.DayTime = !_world.DayTime;
        _world.ProgressDirty = true; // 昼夜切换 → 包 7 重新下发

        if (_world.DayTime)
        {
            // 天亮：推进月相、结束血月、按概率开始日食
            _world.MoonPhase = (_world.MoonPhase + 1) % 8;
            _world.BloodMoon = false;
            _world.Eclipse = _rng.NextUInt32() % 20 == 0;
        }
        else
        {
            // 入夜：结束日食、按概率开始血月
            _world.Eclipse = false;
            _world.BloodMoon = _rng.NextUInt32() % 9 == 0;
        }
    }
}
