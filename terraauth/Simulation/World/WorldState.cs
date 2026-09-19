// TerraAuth — Phase 6: 世界状态（权威数据模型）
// 字段来源：原版客户端世界文件的头部与各分段（世界旗标 / 箱子 / 告示牌 / NPC），
//   以及包 7（WorldData）的字段顺序（Terraria 1.4.5.8 / Protocol 326）
// 该类型是服务端权威世界的唯一真相来源：.wld 解析 / 程序化生成均产出它，NetworkHost 由它构造出站包。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>世界全局状态：元数据 + 图格矩阵 + 实体列表。</summary>
public readonly record struct InventoryReward(int ItemId, int Stack);

public enum InventoryBagOpenResult
{
    Success,
    InvalidBag,
    InventoryFull,
}

public interface IInventoryLedger
{
    bool ConsumeItem(int playerId, int itemId);
    bool TryAddItem(int playerId, int itemId, int stack);
    bool TryAddItemExactly(int playerId, int itemId, int stack);
    InventoryBagOpenResult TryOpenEyeOfCthulhuTreasureBag(int playerId, int slot, IReadOnlyList<InventoryReward> rewards);
}

public sealed class WorldState
{
    public IInventoryLedger? InventoryLedger { get; set; }

    /// <summary>
    /// 阶段 C「弹幕伤害匹配」开关：开启后包 28（NPC 受击）的伤害数值必须落在归属玩家的
    /// **最近存活弹幕**的权威伤害区间内（<c>ceil(p.Damage × 1.15) × (crit ? 2 : 1)</c>），
    /// 超界记 <see cref="CommandFailures.StrikeDamageMismatch"/> 拒绝。
    /// 默认关：近战挥砍无弹幕，全量开启会把近战攻击全拒（武器数据阶段 D 建模后再默认开）。
    /// 测试路径按用例开启，实例级开关（非静态）避免并行测试互相污染。
    /// </summary>
    public bool StrikeProjectileMatch { get; set; }

    /// <summary>
    /// 阶段 E「近战武器伤害校验」开关：开启后无弹幕的包 28（近战挥砍）按**手持武器权威伤害**区间校验
    /// （<see cref="CombatResolver.WeaponDamageBound"/>：base × 前缀 × 修饰（Buff/药水/套装/饰品实时）±15% 浮动上界 × 暴击）。
    /// 阶段 E-4 起默认开启：前缀数据流（包 5 → ItemPrefixes）已补齐，带 +伤害前缀武器不会误拒；
    /// 未收录武器 / 空手失败放行。测试可实例级覆盖（并行测试互不污染）。
    /// </summary>
    public bool StrikeWeaponCheck { get; set; } = true;

    /// <summary>
    /// 全局 SSC（Server Side Characters）开关，对应 <c>ServerConfig.SscEnabled</c>（组合根注入）。
    /// 开启：WorldInfo 置 SSC 位、进世界全量下发背包、服务端背包权威（/give 等）生效；
    /// 关闭：走原版非 SSC 流程，客户端本地背包为准，服务端不参与背包权威。
    /// </summary>
    public bool SscEnabled { get; set; } = true;

    /// <summary>
    /// 移除背包召唤武器即销毁对应召唤弹幕，对应 <c>ServerConfig.DestroySummonsOnWeaponRemoval</c>（组合根注入）。
    /// false（默认）= 原版行为：召唤物不随武器移除而消失；true = 武器移除即置召唤弹幕 Active=false。
    /// </summary>
    public bool DestroySummonsOnWeaponRemoval { get; set; } = false;

    // ---- Phase 3 兼容字段 ----
    public long Tick { get; set; }

    private long _nextProjectileSourceTransactionId;
    private long _nextSummonEntityId;

    internal long AllocateProjectileSourceTransactionId()
    {
        lock (ProjectilesLock)
            return ++_nextProjectileSourceTransactionId;
    }

    /// <summary>为新建召唤实体分配单调递增的服务端内部标识。</summary>
    internal long AllocateSummonEntityId()
    {
        lock (ProjectilesLock)
            return ++_nextSummonEntityId;
    }

    /// <summary>玩家运行时状态（服务端权威唯一真相）。</summary>
    public Dictionary<int, PlayerRuntime> Players { get; } = new();

    /// <summary>
    /// 玩家表的跨线程保护：仿真线程可能新增玩家（首个移动包），
    /// 网络线程（世界同步 / 死亡广播）需枚举其当前值，加锁避免枚举期结构变更异常。
    /// </summary>
    public object PlayersLock { get; } = new();

    /// <summary>离线会话（宽限期内可被同身份重连认回）：恢复键 → 运行时 + 截止 tick。受 <see cref="PlayersLock"/> 保护。</summary>
    private readonly Dictionary<string, OfflineSession> _offlineSessions = new(StringComparer.Ordinal);

    private sealed record OfflineSession(PlayerRuntime Runtime, long DeadlineTick);

    /// <summary>
    /// 销毁指定玩家的全部存活召唤 / 哨兵弹幕（置 <c>Active=false</c>）。
    /// <c>RemovalNotified</c> 保持 false，由世界同步循环补发包 29（ProjectileDestroy）广播销毁；
    /// 用于断线清场（对齐原版——玩家掉线其召唤物立即消失）与可配置的「移除召唤武器即销毁」。
    /// 同时移除该玩家的召唤 Buff（原版仆从由召唤 Buff 驱动存活，仅销毁弹幕不够：
    /// 客户端 Buff 未移除时仆从不消失、仍发射弹幕造成伤害），并标记下发包 50。
    /// </summary>
    public void KillSummonedProjectiles(int playerId)
    {
        lock (ProjectilesLock)
        {
            int destroyed = 0;
            for (int i = 0; i < Projectiles.Count; i++)
            {
                var p = Projectiles[i];
                if (p.Active && p.Owner == playerId &&
                    (p.IsSummon || SummonProjectileTable.Of.Contains(p.Type)))
                {
                    p.Active = false;
                    p.Destroyed = true;   // 永久销毁：拒绝被后续包 27 更新复活
                    destroyed++;
                }
            }
            if (DiagnosticLog.Enabled)
                Console.WriteLine($"[DIAG] KillSummonedProjectiles pid={playerId} destroyed={destroyed}");
        }

        // 召唤 Buff 移除：原版仆从由 Buff 驱动（客户端仆从 AI 每帧检查、Buff 消失则仆从自杀），
        // 只有服务端销毁弹幕 + 包 29 时客户端仆从仍存活并继续攻击（命中校验双通道上界均 null → 放行）。
        // 与用户实测「手动点掉 Buff 提示召唤物才消失」一致：此处服务端主动移除并回写客户端。
        lock (PlayersLock)
        {
            if (!Players.TryGetValue(playerId, out var player) || player is null) return;

            int removed = 0;
            for (int i = player.Buffs.Count - 1; i >= 0; i--)
            {
                if (SummonProjectileTable.IsSummonBuff(player.Buffs[i]))
                {
                    player.Buffs.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                player.RecalculateDefense();   // 防御型 Buff 移除后即时并入 statDefense
                MarkPlayerBuffsChanged(playerId);
                if (DiagnosticLog.Enabled)
                    Console.WriteLine($"[DIAG] KillSummonedProjectiles pid={playerId} removed_buffs={removed}");
            }
        }
    }

    /// <summary>销毁指定玩家由某个召唤 Buff 维持的 Minion；Sentry 不受 Buff 变化影响。</summary>
    public void KillSummonedProjectilesForBuff(int playerId, int buffId)
    {
        lock (ProjectilesLock)
        {
            foreach (var projectile in Projectiles)
            {
                if (!projectile.Active || projectile.Owner != playerId ||
                    projectile.SummonKind != SummonKind.Minion ||
                    projectile.SourceSummonBuffId != buffId)
                {
                    continue;
                }

                projectile.Active = false;
                projectile.Destroyed = true;
                projectile.DeadTick = Tick;
            }
        }
    }

    /// <summary>
    /// 玩家断线：把运行时移出在线集合（**这同时修掉「断线运行时永不释放」的泄漏**）。
    /// <paramref name="graceTicks"/> &gt; 0 且恢复键非空时挂入离线会话，供同身份重连认回；否则直接丢弃。
    /// </summary>
    public void MarkPlayerOffline(int playerId, string resumeKey, long graceTicks)
        => MarkPlayerOffline(playerId, 0, resumeKey, graceTicks);

    public void MarkPlayerOffline(int playerId, long expectedSessionId, string resumeKey, long graceTicks)
    {
        PlayerRuntime? runtime;
        lock (PlayersLock)
        {
            if (!Players.TryGetValue(playerId, out runtime)) return;
            if (expectedSessionId != 0 && runtime.SessionId != expectedSessionId) return;
            Players.Remove(playerId);
        }

        runtime.Active = false;
        runtime.Velocity = new Vector2(0, 0);
        CloseChestSession(playerId, expectedSessionId);

        // 阶段 H：断线清空该玩家的召唤弹幕（对齐原版——玩家掉线其召唤物立即消失）。
        // 置 Active=false（RemovalNotified 保持 false）交由世界同步循环补发包 29 广播销毁；
        // 此后槽位复用 / 会话接管时，旧召唤物不会残留为幽灵上界基准。
        KillSummonedProjectiles(playerId);

        if (graceTicks <= 0 || string.IsNullOrEmpty(resumeKey)) return;

        runtime.ResumeKey = resumeKey;
        lock (PlayersLock)
            _offlineSessions[resumeKey] = new OfflineSession(runtime, Tick + graceTicks);
    }

    /// <summary>
    /// 尝试把离线会话认回到新槽位（同身份重连）：保留位置 / 血量 / 增益等运行时状态，
    /// 并置 <see cref="PlayerRuntime.Resumed"/>（世界同步据此下发携原坐标的出生包，跳过出生点重定位）。
    /// </summary>
    public bool TryResumePlayer(int newPlayerId, string resumeKey)
        => TryResumePlayer(newPlayerId, 0, resumeKey);

    public bool TryResumePlayer(int newPlayerId, long expectedSessionId, string resumeKey)
    {
        if (string.IsNullOrEmpty(resumeKey)) return false;

        lock (PlayersLock)
        {
            if (!_offlineSessions.TryGetValue(resumeKey, out var session)) return false;
            _offlineSessions.Remove(resumeKey);
            if (session.DeadlineTick <= Tick) return false;   // 已超宽限期 → 按新玩家处理

            var runtime = session.Runtime;
            if (expectedSessionId != 0)
                runtime.SessionId = expectedSessionId;
            runtime.Id = newPlayerId;
            runtime.Active = true;
            runtime.HasReceivedManaSync = false;
            runtime.AimPosition = runtime.Position;
            runtime.Resumed = true;
            runtime.RespawnNotified = false;   // 世界同步据此下发包 12（携恢复后的坐标）
            Players[newPlayerId] = runtime;
            return true;
        }
    }

    /// <summary>回收超过宽限期的离线会话（由世界同步循环调用）；返回回收数量。</summary>
    public bool TryGetCurrentPlayer(int playerId, long expectedSessionId, out PlayerRuntime? player)
    {
        lock (PlayersLock)
        {
            if (Players.TryGetValue(playerId, out player)
                && (expectedSessionId == 0 || player.SessionId == expectedSessionId)) return true;
            player = null;
            return false;
        }
    }

    public int ReapOfflineSessions()
    {
        lock (PlayersLock)
        {
            if (_offlineSessions.Count == 0) return 0;

            List<string>? expired = null;
            foreach (var kv in _offlineSessions)
            {
                if (kv.Value.DeadlineTick > Tick) continue;
                (expired ??= new List<string>()).Add(kv.Key);
            }

            if (expired is null) return 0;
            foreach (var key in expired) _offlineSessions.Remove(key);
            return expired.Count;
        }
    }

    // ---- 图格 ----
    public TileMap Tiles { get; set; } = new(1, 1);

    /// <summary>
    /// 区块分区锁：仿真线程写图格 / 其他线程读图格（包 10 编码、权威校验）时使用。
    /// 详见 <see cref="SectionLocks"/>。
    /// </summary>
    public SectionLocks Sections { get; } = new();

    public int MaxTilesX { get; set; } = 1;
    public int MaxTilesY { get; set; } = 1;

    public int SpawnTileX { get; set; }
    public int SpawnTileY { get; set; }

    /// <summary>地表高度（图格坐标，double）。</summary>
    public double WorldSurface { get; set; }

    /// <summary>岩层高度（图格坐标，double）。</summary>
    public double RockLayer { get; set; }

    public int WorldId { get; set; }
    public string WorldName { get; set; } = "";
    public Guid UniqueId { get; set; } = Guid.Empty;
    public ulong WorldGeneratorVersion { get; set; }
    public string Seed { get; set; } = "";

    /// <summary>
    /// 世界难度（0=普通、1=专家、2=大师、3=旅途；启动时由 <c>ServerConfig.GameMode</c> 写入，见 <c>GameHost</c>）。
    /// 玩家受击区间校验（<see cref="DamagePlayerCommand"/>）与接触兜底（<c>WorldSimulator.SimulateCombat</c>）
    /// 经 <see cref="CombatResolver.FromWorldDifficulty"/> 取原版 <c>Main.CalculateDamagePlayersTake</c> 对应分支。
    /// </summary>
    public int GameMode { get; set; }

    // ---- 时间 / 天气 ----
    /// <summary>当日时间 0..54000。</summary>
    public double Time { get; set; }
    public bool DayTime { get; set; }
    public bool BloodMoon { get; set; }
    public bool Eclipse { get; set; }

    /// <summary>月相 0..8。</summary>
    public int MoonPhase { get; set; }
    public byte MoonType { get; set; }

    public bool Raining { get; set; }
    public int RainTime { get; set; }
    public float MaxRain { get; set; }

    public float WindSpeedTarget { get; set; }
    public int NumClouds { get; set; }
    public int CloudBgActive { get; set; }

    // ---- 背景 / 样式 ----
    /// <summary>13 个背景：treeBG1-4、corrupt、jungle、snow、hallow、crimson、desert、ocean、mushroom、underworld。</summary>
    public byte[] Backgrounds { get; set; } = new byte[13];

    public int IceBackStyle { get; set; }
    public int JungleBackStyle { get; set; }
    public int HellBackStyle { get; set; }

    public int[] TreeX { get; set; } = new int[3];
    public int[] TreeStyle { get; set; } = new int[4];
    public int[] CaveBackX { get; set; } = new int[3];
    public int[] CaveBackStyle { get; set; } = new int[4];

    /// <summary>TreeTops 13 个区域变化值（包 7 以 byte 下发）。</summary>
    public int[] TreeTops { get; set; } = new int[13];

    // ---- 进度 / 世界种子 ----
    public WorldProgress Progress { get; } = new();

    /// <summary>
    /// 进度（Boss 击杀 / 事件开始结束）发生变化 → 需要重新下发包 7（WorldData）。
    /// 由仿真线程置位、世界同步线程清除，均为单字节读写，故用 volatile。
    /// </summary>
    public volatile bool ProgressDirty;

    /// <summary>
    /// 记录 Boss 击杀进度 + 掉落（服务端权威）。未收录的 NPC 类型不做处理。
    /// 进度位映射与 NPC 类型 ID 均经原版客户端协议行为验证。
    /// 由仿真线程调用（NPC 死亡处）。
    /// </summary>
    public void NotifyNpcKilled(int npcType, float x, float y, IRng rng)
    {
        switch (npcType)
        {
            case 4: Progress.DownedBoss1 = true; break;                  // Eye of Cthulhu
            case 13 or 14 or 15 or 266: Progress.DownedBoss2 = true; break; // Eater of Worlds（含分段）
            case 35: Progress.DownedBoss3 = true; break;                 // Skeletron
            case 50: Progress.DownedSlimeKing = true; break;             // King Slime
            case 222: Progress.DownedQueenBee = true; break;             // Queen Bee
            case 245: Progress.DownedGolemBoss = true; break;            // Golem
            case 262: Progress.DownedPlantBoss = true; break;            // Plantera
            case 370: Progress.DownedFishron = true; break;              // Duke Fishron
            case 439: Progress.DownedAncientCultist = true; break;       // Lunatic Cultist
            case 398: Progress.DownedMoonlord = true; break;             // Moon Lord
            case 134: Progress.DownedMechBoss1 = true; Progress.DownedMechBossAny = true; break; // The Destroyer
            case 125 or 126: Progress.DownedMechBoss2 = true; Progress.DownedMechBossAny = true; break; // The Twins
            case 127: Progress.DownedMechBoss3 = true; Progress.DownedMechBossAny = true; break; // Skeletron Prime
            case 657: Progress.DownedQueenSlime = true; break;           // Queen Slime
            case 636: Progress.DownedEmpressOfLight = true; break;       // Empress of Light
            case 668: Progress.DownedDeerclops = true; break;            // Deerclops
            case 109: Progress.DownedClown = true; break;                // Clown
            // 血肉墙（113）：原版 WorldGen.StartHardmode() —— 世界进入困难模式，
            // 地形转换（神圣带 + 邪恶带刷新）由仿真层在检测到该进度位时执行一次。
            case 113: Progress.HardMode = true; break;                    // Wall of Flesh
            default: return;
        }

        DropBossLoot(npcType, x, y, rng);
        ProgressDirty = true;
    }

    /// <summary>Boss 掉落数据库（**原版模型**：概率 + 堆叠范围 + 专家/大师袋）。
    /// <see cref="DropEntry.Chance"/> 为命中概率（1.0 = 必掉）；堆叠在 [MinStack, MaxStack] 间均匀随机。
    /// 眼魔（4）按腐化 / 猩红 / 专家三套完整建模；其余 Boss 暂以「必掉固定数量」包装（等价旧行为），
    /// 未逐条复刻概率与专家袋，留待后续逐项核对扩表。
    /// <paramref name="ClassicCorrupt"/> / <see cref="BossLootSpec.ClassicCrimson"/> 依 <see cref="Crimson"/> 选择；
    /// 专家 / 大师模式优先取 <see cref="BossLootSpec.ExpertOrMaster"/>（非空时）。
    /// </summary>
    private readonly record struct DropEntry(int ItemId, int MinStack, int MaxStack, double Chance);

    /// <summary>单个 Boss 的掉落规范：腐化世界 / 猩红世界 / 专家或大师三条通道。</summary>
    private sealed record BossLootSpec(DropEntry[] ClassicCorrupt, DropEntry[] ClassicCrimson, DropEntry[] ExpertOrMaster);

    /// <summary>把「必掉固定数量」包装为等价的数据库条目（Chance=1、堆叠固定），用于尚未逐项核对的 Boss。</summary>
    private static BossLootSpec Fixed(params (int ItemId, int Stack)[] drops)
    {
        var entries = drops.Select(d => new DropEntry(d.ItemId, d.Stack, d.Stack, 1.0)).ToArray();
        return new BossLootSpec(entries, entries, System.Array.Empty<DropEntry>());
    }

    private static readonly Dictionary<int, BossLootSpec> BossLoot = new()
    {
        // 眼魔 4 —— 原版掉落全表（腐化 / 猩红 / 专家袋），逐条核对 <c>ItemDropDatabase.RegisterBoss_EOC</c>：
        //   经典腐化（NotExpert + IsCorruption）：恶魔矿(56) 30-90、邪箭(47) 20-50、腐化种子(59) 1-3（均 100%）。
        //   经典猩红（NotExpert + IsCrimson）：猩红矿(880) 30-90、猩红种子(2171) 1-3（均 100%）。
        //   两套通用：眼面具(2112) 1/7、望远镜(1299) 1/40。
        //   专家/大师：眼魔宝袋(3319) 必掉（原版 BossBag 为 DropBasedOnExpertMode(无,  宝袋)，
        //   且上述经典掉落均带 NotExpert 条件 → 专家模式**只**掉宝袋，内容由服务端权威开袋命令生成）。
        [4] = new BossLootSpec(
            new[]
            {
                new DropEntry(56, 30, 90, 1.0),
                new DropEntry(47, 20, 50, 1.0),
                new DropEntry(59, 1, 3, 1.0),
                new DropEntry(2112, 1, 1, 1.0 / 7),
                new DropEntry(1299, 1, 1, 1.0 / 40),
            },
            new[]
            {
                new DropEntry(880, 30, 90, 1.0),
                new DropEntry(2171, 1, 3, 1.0),
                new DropEntry(2112, 1, 1, 1.0 / 7),
                new DropEntry(1299, 1, 1, 1.0 / 40),
            },
            new[] { new DropEntry(3319, 1, 1, 1.0) }),

        [13]  = Fixed((56, 30), (86, 5)),        // Eater of Worlds → Demonite + Shadow Scale
        [266] = Fixed((880, 30)),                // Brain of Cthulhu → Crimtane
        [50]  = Fixed((23, 50), (998, 1)),       // King Slime → Gel + Solidifier
        [222] = Fixed((2431, 10), (1121, 1)),    // Queen Bee → Bee Wax + Beegun
        [35]  = Fixed((1313, 1)),                // Skeletron → Book of Skulls
        [113] = Fixed((367, 1), (490, 1)),       // Wall of Flesh → Pwnhammer + Warrior Emblem
        [125] = Fixed((549, 25), (1225, 15)),    // The Twins → Soul of Sight + Hallowed Bar
        [126] = Fixed((549, 25), (1225, 15)),    // The Twins(Spazmatism)
        [134] = Fixed((548, 25), (1225, 15)),    // The Destroyer → Soul of Might + Hallowed Bar
        [127] = Fixed((547, 25), (1225, 15)),    // Skeletron Prime → Soul of Fright + Hallowed Bar
        [262] = Fixed((1141, 1), (1157, 1)),     // Plantera → Temple Key + Pygmy Staff
        [245] = Fixed((1294, 1), (2218, 18)),    // Golem → Picksaw + Beetle Husk
        [370] = Fixed((2624, 1), (2609, 1)),     // Duke Fishron → Tsunami + Fishron Wings
        [657] = Fixed((4986, 50), (4980, 1)),    // Queen Slime → Gel Balloon + Hook of Dissonance
        [636] = Fixed((4923, 1)),                // Empress of Light → 武器(4选一)
        [668] = Fixed((5098, 1)),                // Deerclops → Chester 宠物(1/3)
        [439] = Fixed((3549, 1)),                // Lunatic Cultist → Lunar Crafting Station
        [398] = Fixed((3460, 90), (3384, 1)),    // Moon Lord → Lunar Ore + Portal Gun
    };

    /// <summary>世界掉落物槽位上限（与原版 <c>Main.item[400]</c> 一致）。</summary>
    private const int MaxItemSlots = 400;

    /// <summary>服务端主动生成掉落物（Boss 掉落）：按概率 / 堆叠范围 / 模式抛掷，标记待下发（包 21 补发）。</summary>
    private void DropBossLoot(int npcType, float x, float y, IRng rng)
    {
        if (!BossLoot.TryGetValue(npcType, out var spec)) return;

        // 世界难度（0=经典、1=专家、2=大师、3=旅途）：专家/大师掉落走宝袋分支。
        var pool = GameMode >= 1 && spec.ExpertOrMaster.Length > 0
            ? spec.ExpertOrMaster
            : (Progress.Crimson ? spec.ClassicCrimson : spec.ClassicCorrupt);

        lock (ItemsLock)
        {
            foreach (var drop in pool)
            {
                if (rng.NextDouble() >= drop.Chance) continue;                 // 概率抛掷
                if (Items.Count >= MaxItemSlots) return;

                int slot = 0;
                while (Items.Any(i => i.Slot == slot)) slot++;

                int stack = drop.MinStack == drop.MaxStack
                    ? drop.MinStack
                    : drop.MinStack + rng.NextInt32(drop.MaxStack - drop.MinStack + 1); // [Min, Max] 均匀

                Items.Add(new WorldItemEntity
                {
                    Slot = slot,
                    ItemId = drop.ItemId,
                    Stack = stack,
                    Position = new Vector2(x, y),
                    Velocity = new Vector2(0f, 0f),
                    Prefix = 0,
                    OwnedBy = -1,
                    NewNotified = false, // 服务端生成 → 客户端尚不知情，需补发包 21
                });
            }
        }
    }

    /// <summary>
    /// 生成一个掉落物（原版 <c>Item.NewItem</c> 的服务端侧）：分配槽位 + 标记待下发
    /// （包 21 / 22 由世界同步循环的 <c>FlushNewItemsAsync</c> 补发）。
    /// 供挖砖掉落（<see cref="TileDropTable"/>）使用；<paramref name="itemId"/> ≤ 0 或槽位已满则不做任何事。
    /// </summary>
    public void SpawnItemDrop(int itemId, int stack, int tileX, int tileY, IRng rng, byte prefix = 0)
    {
        if (itemId <= 0 || stack <= 0) return;

        lock (ItemsLock)
        {
            if (Items.Count >= MaxItemSlots) return;

            int slot = 0;
            while (Items.Any(i => i.Slot == slot)) slot++;

            Items.Add(new WorldItemEntity
            {
                Slot = slot,
                ItemId = itemId,
                Stack = stack,
                Position = new Vector2(tileX * 16 + 8, tileY * 16 + 8),   // 图格中心（原版 Item.NewItem 的碰撞盒中心）
                // 原版初速：X ±3.0、Y -4.0..-1.5（px/tick），落地前有轻微抛物线
                Velocity = new Vector2((rng.NextInt32(61) - 30) * 0.1f, -(rng.NextInt32(25) + 15) * 0.1f),
                Prefix = prefix,
                OwnedBy = -1,          // 无归属 → 刷新循环按原版 FindOwner 就近分配（见 FlushItemOwnersAsync）
                NewNotified = false,   // 服务端生成 → 客户端尚不知情，需补发包 21
            });
        }
    }

    // ---- 玩家提示（聊天框）----

    private readonly object _noticesLock = new();
    private readonly Queue<(int PlayerId, string Text)> _pendingNotices = new();

    /// <summary>待发提示上限（防刷屏拖内存，超出丢最旧）。</summary>
    private const int MaxPendingNotices = 256;

    /// <summary>
    /// 排队一条发给单个玩家的聊天提示（如拾取物品）。由世界同步循环
    /// <c>GameHost.FlushPlayerNoticesAsync</c> 取走并经包 82（NetTextModule）下发。
    /// </summary>
    public void NotifyPlayer(int playerId, string text)
    {
        if (playerId <= 0 || string.IsNullOrEmpty(text)) return;

        lock (_noticesLock)
        {
            if (_pendingNotices.Count >= MaxPendingNotices) _pendingNotices.Dequeue();
            _pendingNotices.Enqueue((playerId, text));
        }
    }

    /// <summary>取出至多 <paramref name="max"/> 条待发提示（FIFO）。</summary>
    public List<(int PlayerId, string Text)> DrainPlayerNotices(int max)
    {
        var result = new List<(int PlayerId, string Text)>();
        if (max <= 0) return result;

        lock (_noticesLock)
            while (result.Count < max && _pendingNotices.Count > 0)
                result.Add(_pendingNotices.Dequeue());
        return result;
    }

    /// <summary>单棵树最多清除的树干格数（防异常图格造成大面积破坏）。</summary>
    private const int MaxTreeTiles = 400;

    /// <summary>
    /// 让一棵树整棵倒下：从 (x, y) 的 4 邻接出发收集**同类型树干**图格并全部清除，返回额外清掉的格数
    /// （不含起点 (x, y)——它已由挖砖命令清除）。
    /// <para>
    /// 原版一棵树由多格树干（含枝条）组成，砍掉任意一格即整棵倒下并**逐格**产出木材；
    /// 4 邻接不会跨到邻树：生成器保证树间距 ≥ 4 列、枝条最远只伸到 ±1 列。
    /// </para>
    /// </summary>
    public int FellTreeAt(int x, int y, int treeType)
    {
        var pending = new Stack<(int X, int Y)>();
        var seen = new HashSet<(int X, int Y)>();

        void PushTreeNeighbor(int px, int py)
        {
            if (px < 0 || py < 0 || px >= MaxTilesX || py >= MaxTilesY) return;
            if (Tiles[px, py].Active && Tiles[px, py].Type == treeType && seen.Add((px, py)))
                pending.Push((px, py));
        }

        PushTreeNeighbor(x - 1, y);
        PushTreeNeighbor(x + 1, y);
        PushTreeNeighbor(x, y - 1);
        PushTreeNeighbor(x, y + 1);

        int removed = 0;
        while (pending.Count > 0 && removed < MaxTreeTiles)
        {
            var (px, py) = pending.Pop();

            bool isTree;
            Sections.EnterWrite(px, py);
            try
            {
                ref var tile = ref Tiles[px, py];
                isTree = tile.Active && tile.Type == treeType;
                if (isTree)
                {
                    tile.Active = false;
                    tile.Type = 0;
                    tile.Wall = 0;
                    removed++;
                }
            }
            finally
            {
                Sections.ExitWrite(px, py);
            }

            if (!isTree) continue;

            MarkTileChanged(px, py);
            PushTreeNeighbor(px - 1, py);
            PushTreeNeighbor(px + 1, py);
            PushTreeNeighbor(px, py - 1);
            PushTreeNeighbor(px, py + 1);
        }

        return removed;
    }

    // ---- 入侵 / 沙尘暴 / 冷却 ----
    public int InvasionDelay { get; set; }
    public int InvasionSize { get; set; }
    public int InvasionSizeStart { get; set; }
    public int InvasionType { get; set; }
    public double InvasionX { get; set; }

    public float SandstormIntensity { get; set; }

    public byte SundialCooldown { get; set; }
    public byte MoondialCooldown { get; set; }

    /// <summary>7 个矿石层级：0 Copper、1 Iron、2 Silver、3 Gold、4 Cobalt、5 Mythril、6 Adamantite。</summary>
    public short[] OreTiers { get; set; } = new short[7];

    public ulong LobbyId { get; set; }

    public List<(short X, short Y)> ExtraSpawnPoints { get; } = new();

    public int DungeonX { get; set; }
    public int DungeonY { get; set; }

    // ---- 实体 ----
    public List<Chest> Chests { get; } = new();
    public List<Sign> Signs { get; } = new();
    public List<WorldNpc> Npcs { get; } = new();

    // 图格实体使用独立锁；调用方不得在持有 ChestsLock 时进入此锁，避免锁顺序交错。
    public object TileEntitiesLock { get; } = new();
    private readonly Dictionary<int, TileEntity> _tileEntitiesById = new();
    private readonly Dictionary<(short X, short Y), TileEntity> _tileEntitiesByPosition = new();
    private readonly HashSet<int> _dirtyTileEntityIds = new();
    private readonly Dictionary<int, TileEntityDeletion> _deletedTileEntities = new();
    private int _nextTileEntityId;

    public readonly record struct TileEntityDeletion(int RuntimeId, int FileId, byte Type, short X, short Y);

    public int NextTileEntityId
    {
        get { lock (TileEntitiesLock) return _nextTileEntityId; }
    }

    public bool TryGetTileEntity(int id, out TileEntity? entity)
    {
        lock (TileEntitiesLock)
            return _tileEntitiesById.TryGetValue(id, out entity);
    }

    public bool TryGetTileEntityAt(short x, short y, out TileEntity? entity)
    {
        lock (TileEntitiesLock)
            return _tileEntitiesByPosition.TryGetValue((x, y), out entity);
    }

    public List<TileEntity> SnapshotTileEntities()
    {
        lock (TileEntitiesLock)
            return _tileEntitiesById.Values.OrderBy(static entity => entity.Id).ToList();
    }

    public TileEntity InsertTileEntity(TileEntity entity, bool markDirty = true)
    {
        lock (TileEntitiesLock)
        {
            if (_tileEntitiesByPosition.TryGetValue((entity.X, entity.Y), out var atPosition))
            {
                _tileEntitiesById.Remove(atPosition.Id);
                _tileEntitiesByPosition.Remove((entity.X, entity.Y));
                _dirtyTileEntityIds.Remove(atPosition.Id);
                if (markDirty) _deletedTileEntities[atPosition.Id] = ToTileEntityDeletion(atPosition);
            }

            entity.Id = _nextTileEntityId++;
            if (entity.FileId < 0) entity.FileId = entity.Id;
            _tileEntitiesById.Add(entity.Id, entity);
            _tileEntitiesByPosition.Add((entity.X, entity.Y), entity);
            if (markDirty) _dirtyTileEntityIds.Add(entity.Id);
            return entity;
        }
    }

    public bool RemoveTileEntity(int id)
    {
        lock (TileEntitiesLock)
        {
            if (!_tileEntitiesById.Remove(id, out var entity)) return false;
            _tileEntitiesByPosition.Remove((entity.X, entity.Y));
            _dirtyTileEntityIds.Remove(id);
            _deletedTileEntities[id] = ToTileEntityDeletion(entity);
            return true;
        }
    }

    public bool RemoveTileEntityAt(short x, short y)
    {
        lock (TileEntitiesLock)
            return _tileEntitiesByPosition.TryGetValue((x, y), out var entity) && RemoveTileEntity(entity.Id);
    }

    public void MarkTileEntityDirty(int id)
    {
        lock (TileEntitiesLock)
            if (_tileEntitiesById.ContainsKey(id)) _dirtyTileEntityIds.Add(id);
    }

    public List<int> DrainDirtyTileEntities(int max) => DrainTileEntityQueue(_dirtyTileEntityIds, max);

    public List<TileEntityDeletion> DrainDeletedTileEntities(int max)
    {
        if (max <= 0) return new List<TileEntityDeletion>();
        lock (TileEntitiesLock)
        {
            var result = _deletedTileEntities.Values.OrderBy(static deletion => deletion.RuntimeId).Take(max).ToList();
            foreach (var deletion in result) _deletedTileEntities.Remove(deletion.RuntimeId);
            return result;
        }
    }

    public void RequeueDirtyTileEntities(IEnumerable<int> ids) => RequeueTileEntityQueue(_dirtyTileEntityIds, ids);
    public void RequeueDeletedTileEntities(IEnumerable<TileEntityDeletion> entities)
    {
        lock (TileEntitiesLock)
            foreach (var entity in entities) _deletedTileEntities[entity.RuntimeId] = entity;
    }

    private static TileEntityDeletion ToTileEntityDeletion(TileEntity entity)
        => new(entity.Id, entity.FileId, entity.Type, entity.X, entity.Y);

    private List<int> DrainTileEntityQueue(HashSet<int> queue, int max)
    {
        if (max <= 0) return new List<int>();
        lock (TileEntitiesLock)
        {
            var result = queue.OrderBy(static id => id).Take(max).ToList();
            foreach (int id in result) queue.Remove(id);
            return result;
        }
    }

    private void RequeueTileEntityQueue(HashSet<int> queue, IEnumerable<int> ids)
    {
        lock (TileEntitiesLock)
            foreach (int id in ids) queue.Add(id);
    }

    /// <summary>
    /// 箱子列表的跨线程保护：仿真线程按命令写入箱内物品，权威校验 / 网络线程读取内容。
    /// </summary>
    public object ChestsLock { get; } = new();

    /// <summary>玩家当前打开的箱子会话；会话状态由权威层读写。</summary>
    private readonly Dictionary<int, OpenChestSessionState> _openChests = new();

    private readonly record struct OpenChestSessionState(
        long SessionId,
        int ChestIndex);

    public void OpenChestSession(int playerId, int chestIndex)
        => OpenChestSession(playerId, 0, chestIndex);

    public void OpenChestSession(int playerId, long sessionId, int chestIndex)
    {
        lock (ChestsLock)
            _openChests[playerId] = new OpenChestSessionState(sessionId, chestIndex);
    }

    public bool HasChestSession(int playerId, int chestIndex)
        => HasChestSession(playerId, 0, chestIndex);

    public bool HasChestSession(int playerId, long sessionId, int chestIndex)
    {
        lock (ChestsLock)
            return _openChests.TryGetValue(playerId, out var current)
                && (sessionId == 0 || current.SessionId == sessionId)
                && current.ChestIndex == chestIndex;
    }

    public void CloseChestSession(int playerId)
        => CloseChestSession(playerId, 0);

    /// <summary>当前所有箱子打开会话的快照（玩家 / 会话 / 箱子索引），供仿真做距离复核。</summary>
    public List<(int PlayerId, long SessionId, int ChestIndex)> SnapshotChestSessions()
    {
        lock (ChestsLock)
        {
            var result = new List<(int, long, int)>(_openChests.Count);
            foreach (var kv in _openChests)
                result.Add((kv.Key, kv.Value.SessionId, kv.Value.ChestIndex));
            return result;
        }
    }

    public void CloseChestSession(int playerId, long expectedSessionId)
    {
        lock (ChestsLock)
        {
            if (!_openChests.TryGetValue(playerId, out var current)
                || (expectedSessionId != 0 && current.SessionId != expectedSessionId))
                return;

            _openChests.Remove(playerId);
        }
    }

    public object ChestUpdatesLock { get; } = new();
    private readonly HashSet<(int ChestIndex, int Slot)> _pendingChestUpdates = new();
    private readonly HashSet<(int PlayerId, long SessionId, int Slot)> _pendingInventoryUpdates = new();
    private readonly Dictionary<(int PlayerId, long SessionId), HashSet<long>> _appliedInventoryChestOperations = new();
    private readonly Dictionary<(int PlayerId, long SessionId), PendingBagOpen> _pendingBagOpens = new();
    private readonly Dictionary<(int PlayerId, long SessionId, int Slot), PendingInventoryConfirmation> _pendingInventoryConfirmations = new();

    private sealed record PendingBagOpen(int Slot, DateTimeOffset ExpiresAt);
    private sealed record PendingInventoryConfirmation(
        int ItemId,
        int Stack,
        byte Prefix,
        DateTimeOffset ExpiresAt);

    public void MarkInventoryConfirmation(
        int playerId,
        long sessionId,
        int slot,
        int itemId,
        int stack,
        byte prefix)
    {
        lock (ChestUpdatesLock)
        {
            _pendingInventoryConfirmations[(playerId, sessionId, slot)] =
                new PendingInventoryConfirmation(
                    stack > 0 ? itemId : 0,
                    stack > 0 ? stack : 0,
                    stack > 0 ? prefix : (byte)0,
                    DateTimeOffset.UtcNow.AddSeconds(2));
        }
    }

    public bool IsPendingInventoryConfirmation(
        int playerId,
        long sessionId,
        int slot,
        int itemId,
        int stack,
        byte prefix)
    {
        lock (ChestUpdatesLock)
        {
            var key = (playerId, sessionId, slot);
            if (!_pendingInventoryConfirmations.TryGetValue(key, out var pending))
                return false;

            if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _pendingInventoryConfirmations.Remove(key);
                return false;
            }

            return pending.ItemId == (stack > 0 ? itemId : 0) &&
                   pending.Stack == (stack > 0 ? stack : 0) &&
                   pending.Prefix == (stack > 0 ? prefix : (byte)0);
        }
    }

    public bool BeginPendingBagOpen(int playerId, long sessionId, int slot)
    {
        lock (ChestUpdatesLock)
        {
            var key = (playerId, sessionId);
            if (_pendingBagOpens.TryGetValue(key, out var current) &&
                current.ExpiresAt > DateTimeOffset.UtcNow)
                return false;

            _pendingBagOpens[key] = new PendingBagOpen(slot, DateTimeOffset.UtcNow.AddSeconds(2));
            return true;
        }
    }

    public bool IsPendingBagOpen(int playerId, long sessionId, int slot)
    {
        lock (ChestUpdatesLock)
        {
            if (!_pendingBagOpens.TryGetValue((playerId, sessionId), out var pending))
                return false;
            if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _pendingBagOpens.Remove((playerId, sessionId));
                return false;
            }
            return pending.Slot == -1 || pending.Slot == slot;
        }
    }

    public bool HasPendingBagOpen(int playerId, long sessionId)
    {
        lock (ChestUpdatesLock)
        {
            if (!_pendingBagOpens.TryGetValue((playerId, sessionId), out var pending))
                return false;
            if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _pendingBagOpens.Remove((playerId, sessionId));
                return false;
            }
            return true;
        }
    }

    public void PromotePendingBagOpen(int playerId, long sessionId)
    {
        lock (ChestUpdatesLock)
        {
            var key = (playerId, sessionId);
            if (_pendingBagOpens.TryGetValue(key, out var pending))
                _pendingBagOpens[key] = pending with { Slot = -1 };
        }
    }

    public void CompletePendingBagOpen(int playerId, long sessionId)
    {
        lock (ChestUpdatesLock)
            _pendingBagOpens.Remove((playerId, sessionId));
    }

    public void MarkInventoryChanged(int playerId, long sessionId, int slot)
    {
        lock (ChestUpdatesLock)
            _pendingInventoryUpdates.Add((playerId, sessionId, slot));
    }

    public List<(int PlayerId, long SessionId, int Slot)> DrainInventoryUpdates(int max)
    {
        if (max <= 0) return new List<(int PlayerId, long SessionId, int Slot)>();
        lock (ChestUpdatesLock)
        {
            var result = new List<(int PlayerId, long SessionId, int Slot)>(Math.Min(max, _pendingInventoryUpdates.Count));
            foreach (var update in _pendingInventoryUpdates)
            {
                result.Add(update);
                if (result.Count >= max) break;
            }
            foreach (var update in result) _pendingInventoryUpdates.Remove(update);
            return result;
        }
    }

    public bool HasAppliedInventoryChestOperation(int playerId, long sessionId, long operationId)
    {
        lock (ChestUpdatesLock)
            return _appliedInventoryChestOperations.TryGetValue((playerId, sessionId), out var operations)
                && operations.Contains(operationId);
    }

    public void MarkInventoryChestOperationApplied(int playerId, long sessionId, long operationId)
    {
        lock (ChestUpdatesLock)
        {
            if (!_appliedInventoryChestOperations.TryGetValue((playerId, sessionId), out var operations))
                _appliedInventoryChestOperations[(playerId, sessionId)] = operations = new HashSet<long>();
            operations.Add(operationId);
        }
    }

    public void MarkChestChanged(int chestIndex, int slot)
    {
        lock (ChestUpdatesLock)
            _pendingChestUpdates.Add((chestIndex, slot));
    }

    public List<(int ChestIndex, int Slot)> DrainChestUpdates(int max)
    {
        if (max <= 0)
            return new List<(int ChestIndex, int Slot)>();

        lock (ChestUpdatesLock)
        {
            var result = new List<(int ChestIndex, int Slot)>(
                Math.Min(max, _pendingChestUpdates.Count));
            foreach (var update in _pendingChestUpdates)
            {
                result.Add(update);
                if (result.Count >= max)
                    break;
            }

            foreach (var update in result)
                _pendingChestUpdates.Remove(update);

            return result;
        }
    }

    /// <summary>按图格坐标查找箱子（不存在返回 null）。调用方需持 <see cref="ChestsLock"/>。</summary>
    public Chest? FindChestAt(int x, int y)
    {
        foreach (var chest in Chests)
            if (!chest.Deleted && chest.X == x && chest.Y == y) return chest;
        return null;
    }

    /// <summary>按索引查找箱子（越界 / 已删除 / 不存在返回 null）。调用方需持 <see cref="ChestsLock"/>。</summary>
    public Chest? FindChestByIndex(int index)
        => index >= 0 && index < Chests.Count && !Chests[index].Deleted ? Chests[index] : null;

    /// <summary>删除指定位置的空容器，保留列表槽位以维持网络箱子索引稳定。</summary>
    public bool TryDeleteEmptyChestAt(int x, int y, out int deletedIndex)
    {
        lock (ChestsLock)
        {
            var chest = FindChestAt(x, y);
            if (chest is null)
            {
                deletedIndex = -1;
                return true;
            }

            if (chest.Items.Any(static item => item.Stack > 0))
            {
                deletedIndex = -1;
                return false;
            }

            chest.Deleted = true;
            int index = chest.Index;
            foreach (var playerId in _openChests.Where(pair => pair.Value.ChestIndex == index)
                         .Select(static pair => pair.Key).ToArray())
                _openChests.Remove(playerId);
            deletedIndex = index;
            return true;
        }
    }

    /// <summary>NPC 列表的跨线程保护：仿真线程负责增删，世界同步线程负责遍历下发。</summary>
    public object NpcsLock { get; } = new();

    // ---- 液体仿真 / 同步（服务端权威）----

    /// <summary>液体待处理集合的跨线程保护：命令 / 权威校验标记，仿真线程消费。</summary>
    public object LiquidsLock { get; } = new();

    /// <summary>坐标打包步长：大于最大世界宽度（8400），保证 (x, y) 打包唯一。</summary>
    private const int CoordPackStride = 16384;

    private readonly HashSet<int> _liquidDirty = new();
    private readonly List<(int X, int Y)> _liquidPendingSync = new();

    /// <summary>待下发客户端的液体变更上限（防止液体洪水撑爆发送队列）。</summary>
    private const int MaxPendingLiquidSync = 16384;

    /// <summary>标记一格液体需要仿真（不触发下发）。</summary>
    public void MarkLiquidDirty(int x, int y)
    {
        lock (LiquidsLock) _liquidDirty.Add(y * CoordPackStride + x);
    }

    /// <summary>标记一格液体已变化：既需要继续仿真，也需要下发客户端。</summary>
    public void MarkLiquidChanged(int x, int y)
    {
        lock (LiquidsLock)
        {
            _liquidDirty.Add(y * CoordPackStride + x);
            if (_liquidPendingSync.Count < MaxPendingLiquidSync)
                _liquidPendingSync.Add((x, y));
        }

        MarkPersistTile(x, y); // 液体属于图格字段，一并纳入持久化
    }

    /// <summary>取出至多 <paramref name="max"/> 格待仿真液体（并从待处理集合移除）。</summary>
    public List<(int X, int Y)> TakeLiquidDirty(int max)
    {
        if (max <= 0)
            return new List<(int X, int Y)>();

        lock (LiquidsLock)
        {
            var result = new List<(int X, int Y)>(Math.Min(max, _liquidDirty.Count));
            if (_liquidDirty.Count == 0) return result;

            foreach (var packed in _liquidDirty)
            {
                result.Add((packed % CoordPackStride, packed / CoordPackStride));
                if (result.Count >= max) break;
            }

            foreach (var (x, y) in result) _liquidDirty.Remove(y * CoordPackStride + x);
            return result;
        }
    }

    /// <summary>取出至多 <paramref name="max"/> 条待下发的液体变更。</summary>
    public List<(int X, int Y)> DrainLiquidSync(int max)
    {
        if (max <= 0)
            return new List<(int X, int Y)>();

        lock (LiquidsLock)
        {
            if (_liquidPendingSync.Count == 0) return new List<(int X, int Y)>();

            int take = Math.Min(max, _liquidPendingSync.Count);
            var result = _liquidPendingSync.GetRange(0, take);
            _liquidPendingSync.RemoveRange(0, take);
            return result;
        }
    }

    // ---- 玩家状态变更推送（仿真提交后生成原版包 13）----

    public object PlayerUpdatesLock { get; } = new();
    private readonly HashSet<int> _pendingPlayerUpdates = new();

    public void MarkPlayerChanged(int playerId)
    {
        lock (PlayerUpdatesLock) _pendingPlayerUpdates.Add(playerId);
    }

    public List<int> DrainPlayerUpdates(int max)
    {
        if (max <= 0)
            return new List<int>();

        lock (PlayerUpdatesLock)
        {
            var result = new List<int>(Math.Min(max, _pendingPlayerUpdates.Count));
            foreach (var playerId in _pendingPlayerUpdates)
            {
                result.Add(playerId);
                if (result.Count >= max) break;
            }
            foreach (var playerId in result) _pendingPlayerUpdates.Remove(playerId);
            return result;
        }
    }

    // ---- 法力变更推送（服务端扣除法力后生成原版包 42）----

    public object PlayerManaLock { get; } = new();
    private readonly HashSet<int> _pendingPlayerMana = new();

    /// <summary>标记玩家法力已由服务端权威修改，向本人回写包 42。</summary>
    public void MarkPlayerManaChanged(int playerId)
    {
        lock (PlayerManaLock) _pendingPlayerMana.Add(playerId);
    }

    public List<int> DrainPlayerManaChanged(int max)
    {
        if (max <= 0)
            return new List<int>();

        lock (PlayerManaLock)
        {
            var result = new List<int>(Math.Min(max, _pendingPlayerMana.Count));
            foreach (var playerId in _pendingPlayerMana)
            {
                result.Add(playerId);
                if (result.Count >= max) break;
            }
            foreach (var playerId in result) _pendingPlayerMana.Remove(playerId);
            return result;
        }
    }

    // ---- 增益列表变更推送（仿真移除增益后生成原版包 50）----

    public object PlayerBuffsLock { get; } = new();
    private readonly HashSet<int> _pendingPlayerBuffs = new();

    /// <summary>
    /// 标记玩家增益列表已由服务端权威修改（如「移除召唤武器即销毁」时移除召唤 Buff），
    /// 世界同步线程据此向该玩家下发包 50（PlayerBuffs）回写更新后的列表。
    /// </summary>
    public void MarkPlayerBuffsChanged(int playerId)
    {
        lock (PlayerBuffsLock) _pendingPlayerBuffs.Add(playerId);
    }

    public List<int> DrainPlayerBuffsChanged(int max)
    {
        if (max <= 0)
            return new List<int>();

        lock (PlayerBuffsLock)
        {
            var result = new List<int>(Math.Min(max, _pendingPlayerBuffs.Count));
            foreach (var playerId in _pendingPlayerBuffs)
            {
                result.Add(playerId);
                if (result.Count >= max) break;
            }
            foreach (var playerId in result) _pendingPlayerBuffs.Remove(playerId);
            return result;
        }
    }

    // ---- NPC 增益列表变更推送（仿真修改 NPC 增益后生成原版包 54）----

    public object NpcBuffsLock { get; } = new();
    private readonly HashSet<int> _pendingNpcBuffs = new();

    /// <summary>
    /// 标记某 NPC 的增益列表已由服务端权威修改（如服务端施加减益 / 移除增益），
    /// 世界同步线程据此向全体玩家下发包 54（NpcBuffSync）回写该 NPC 的全量列表。
    /// </summary>
    public void MarkNpcBuffsChanged(int npcId)
    {
        lock (NpcBuffsLock) _pendingNpcBuffs.Add(npcId);
    }

    public List<int> DrainNpcBuffsChanged(int max)
    {
        if (max <= 0)
            return new List<int>();

        lock (NpcBuffsLock)
        {
            var result = new List<int>(Math.Min(max, _pendingNpcBuffs.Count));
            foreach (var npcId in _pendingNpcBuffs)
            {
                result.Add(npcId);
                if (result.Count >= max) break;
            }
            foreach (var npcId in result) _pendingNpcBuffs.Remove(npcId);
            return result;
        }
    }

    // ---- 图格变更推送（服务端驱动的图格修改，如电路翻转执行器）----

    /// <summary>待推送图格集合的跨线程保护。</summary>
    public object TileUpdatesLock { get; } = new();

    private readonly HashSet<int> _pendingTileUpdates = new();

    /// <summary>待推送图格上限（防止大范围改动撑爆发送队列）。</summary>
    private const int MaxPendingTileUpdates = 8192;

    /// <summary>标记一格图格已由服务端修改，需要推送客户端（小矩形包 10 区块）。</summary>
    public void MarkTileChanged(int x, int y)
    {
        lock (TileUpdatesLock)
        {
            if (_pendingTileUpdates.Count < MaxPendingTileUpdates)
                _pendingTileUpdates.Add(y * CoordPackStride + x);
        }

        MarkPersistTile(x, y); // 同时登记持久化（服务端重启后回放）
    }

    /// <summary>取出至多 <paramref name="max"/> 格待推送图格（并从待推送集合移除）。</summary>
    public List<(int X, int Y)> DrainTileUpdates(int max)
    {
        if (max <= 0)
            return new List<(int X, int Y)>();

        lock (TileUpdatesLock)
        {
            if (_pendingTileUpdates.Count == 0) return new List<(int X, int Y)>();

            var result = new List<(int X, int Y)>(Math.Min(max, _pendingTileUpdates.Count));
            foreach (var packed in _pendingTileUpdates)
            {
                result.Add((packed % CoordPackStride, packed / CoordPackStride));
                if (result.Count >= max) break;
            }

            foreach (var (x, y) in result) _pendingTileUpdates.Remove(y * CoordPackStride + x);
            return result;
        }
    }

    // ---- 世界改动持久化（服务端重启后回放，避免玩家建筑 / 箱子丢失）----

    /// <summary>待落盘集合的跨线程保护：仿真线程登记，持久化线程消费。</summary>
    public object WorldPersistLock { get; } = new();

    /// <summary>单批落盘的图格数上限（同时决定最大排空速率：<c>PersistBatchSize × 落盘频率</c>）。</summary>
    public const int PersistBatchSize = 8192;

    /// <summary>待落盘坐标集合上限（约 1 MB）：超过则不再增长，降级为「全图扫描」模式。</summary>
    private const int MaxPendingPersistTiles = 262_144;

    private readonly HashSet<int> _pendingPersistTiles = new();

    /// <summary>待落盘箱子索引上限（普通世界箱子数量远小于此，仅作无界增长防御）。</summary>
    private const int MaxPendingPersistChests = 65_536;

    private readonly HashSet<int> _pendingPersistChests = new();
    private readonly HashSet<int> _pendingDeletedPersistChests = new();

    /// <summary>全图扫描模式：待处理集合曾溢出，改用游标遍历全图保证最终一致（内存有界）。</summary>
    private bool _persistFullScan;
    private int _fullScanX;
    private int _fullScanY;

    /// <summary>是否仍有改动未落盘（停机冲刷用）。</summary>
    public bool HasPendingPersist
    {
        get
        {
            lock (WorldPersistLock)
            lock (TileEntitiesLock)
                return _persistFullScan || _pendingPersistTiles.Count > 0 || _pendingPersistChests.Count > 0 ||
                       _pendingDeletedPersistChests.Count > 0 || _dirtyTileEntityIds.Count > 0 || _deletedTileEntities.Count > 0;
        }
    }

    /// <summary>登记一格需要落盘的图格改动（挖 / 放 / 墙 / 液体 / 电线 / 执行器 / 混合反应）。</summary>
    public void MarkPersistTile(int x, int y)
    {
        if (x < 0 || x >= MaxTilesX || y < 0 || y >= MaxTilesY) return;

        lock (WorldPersistLock)
        {
            // 改动量极端大（如大范围液体流动）：内存有界优先，降级为全图扫描兜底
            if (_pendingPersistTiles.Count >= MaxPendingPersistTiles)
            {
                _persistFullScan = true;
                return;
            }

            _pendingPersistTiles.Add(y * CoordPackStride + x);
        }
    }

    /// <summary>
    /// 取出本批待落盘的图格坐标（取出的会从待处理集合移除）。
    /// 若曾因改动量过大降级为全图扫描，则按游标遍历全图；走完一整遍后自动回到正常模式。
    /// </summary>
    public List<(int X, int Y)> DrainPersistTiles(int max)
    {
        lock (WorldPersistLock)
        {
            var result = new List<(int X, int Y)>(Math.Min(max, _pendingPersistTiles.Count + 1));

            if (_persistFullScan)
            {
                while (result.Count < max)
                {
                    result.Add((_fullScanX, _fullScanY));

                    if (++_fullScanY < MaxTilesY) continue;

                    _fullScanY = 0;
                    if (++_fullScanX < MaxTilesX) continue;

                    // 一整遍走完 → 全图均已落盘，回到正常模式
                    _fullScanX = 0;
                    _persistFullScan = false;
                    break;
                }
                return result;
            }

            if (_pendingPersistTiles.Count == 0) return result;

            foreach (var packed in _pendingPersistTiles)
            {
                result.Add((packed % CoordPackStride, packed / CoordPackStride));
                if (result.Count >= max) break;
            }

            foreach (var (x, y) in result) _pendingPersistTiles.Remove(y * CoordPackStride + x);
            return result;
        }
    }

    /// <summary>登记一个需要落盘的箱子（内容被客户端修改，包 32 权威通过后）。</summary>
    public void MarkPersistChest(int chestIndex)
    {
        if (chestIndex < 0) return;
        lock (WorldPersistLock)
        {
            if (_pendingPersistChests.Count >= MaxPendingPersistChests) return;
            _pendingPersistChests.Add(chestIndex);
        }
    }

    /// <summary>取出本批待落盘的箱子索引（取出的会从待处理集合移除；失败重排见调用方）。</summary>
    public List<int> DrainPersistChests(int max)
    {
        lock (WorldPersistLock)
        {
            var result = new List<int>(Math.Min(max, _pendingPersistChests.Count));
            foreach (var index in _pendingPersistChests)
            {
                result.Add(index);
                if (result.Count >= max) break;
            }
            foreach (var index in result) _pendingPersistChests.Remove(index);
            return result;
        }
    }

    public void MarkPersistChestDeleted(int chestIndex)
    {
        if (chestIndex < 0) return;
        lock (WorldPersistLock)
        {
            _pendingPersistChests.Remove(chestIndex);
            _pendingDeletedPersistChests.Add(chestIndex);
        }
    }

    public List<int> DrainDeletedPersistChests(int max)
    {
        lock (WorldPersistLock)
        {
            var result = new List<int>(Math.Min(max, _pendingDeletedPersistChests.Count));
            foreach (var index in _pendingDeletedPersistChests)
            {
                result.Add(index);
                if (result.Count >= max) break;
            }
            foreach (var index in result) _pendingDeletedPersistChests.Remove(index);
            return result;
        }
    }

    // ---- 服务端判定的玩家受击通知（包 117 / 16 下发）----

    /// <summary>待下发受击通知的跨线程保护：仿真线程入队，网络线程消费。</summary>
    public object PlayerHurtLock { get; } = new();

    private readonly List<(int PlayerId, int Damage)> _pendingPlayerHurt = new();

    /// <summary>待下发受击通知上限（防止极端情况下无界增长）。</summary>
    private const int MaxPendingPlayerHurt = 1024;

    /// <summary>登记一次「服务端判定」的玩家受击（供网络层发包 117 表现 + 包 16 权威血量）。</summary>
    public void MarkPlayerHurt(int playerId, int damage)
    {
        lock (PlayerHurtLock)
        {
            if (_pendingPlayerHurt.Count < MaxPendingPlayerHurt)
                _pendingPlayerHurt.Add((playerId, damage));
        }
    }

    /// <summary>
    /// **玩家生命的唯一权威写入路径**（原 <c>WorldSimulator.ApplyPlayerDamage</c> 的核心抽入）：
    /// 扣血 → 置免伤帧 → 必要时置死亡态 → 登记受击通知（包 117 表现 + 包 16 权威血量）。
    /// 接触兜底（SimulateCombat）与包 117 区间校验（DamagePlayerCommand）共用，
    /// 从而保证「服务端扣的血」在两条路径下都以同一入口生效。
    /// </summary>
    public void ApplyDamageToPlayer(PlayerRuntime player, int damage, int immunityTicks)
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

        MarkPlayerHurt(player.Id, damage);
    }

    /// <summary>取出至多 <paramref name="max"/> 条待下发受击通知。</summary>
    public List<(int PlayerId, int Damage)> DrainPlayerHurt(int max)
    {
        lock (PlayerHurtLock)
        {
            if (_pendingPlayerHurt.Count == 0) return new List<(int, int)>();

            int take = Math.Min(max, _pendingPlayerHurt.Count);
            var result = _pendingPlayerHurt.GetRange(0, take);
            _pendingPlayerHurt.RemoveRange(0, take);
            return result;
        }
    }

    // ---- NPC 命中确认（包 162 下发）----

    /// <summary>待下发命中确认的跨线程保护：仿真线程入队，网络线程消费。</summary>
    public object NpcDamageAckLock { get; } = new();

    private readonly List<int> _pendingNpcDamageAck = new();

    /// <summary>待下发命中确认上限（防止极端情况下无界增长）。</summary>
    private const int MaxPendingNpcDamageAck = 512;

    /// <summary>
    /// 登记一次待下发的 NPC 命中确认（包 162）。服务端每收到一次包 28 都应登记一条：
    /// 客户端按 FIFO 出队（<c>NPC.AckDamage()</c>），漏发会让其待确认伤害无限累积，
    /// 最终把本地 NPC 血量算成负数（贴图消失）而服务端该怪仍存活 —— 幽灵碰撞。
    /// </summary>
    public void MarkNpcDamageAck(int playerId)
    {
        lock (NpcDamageAckLock)
        {
            if (_pendingNpcDamageAck.Count < MaxPendingNpcDamageAck)
                _pendingNpcDamageAck.Add(playerId);
        }
    }

    /// <summary>取出至多 <paramref name="max"/> 条待下发命中确认；**必须保持 FIFO**（与客户端入队顺序一致）。</summary>
    public List<int> DrainNpcDamageAck(int max)
    {
        lock (NpcDamageAckLock)
        {
            if (_pendingNpcDamageAck.Count == 0) return new List<int>();

            int take = Math.Min(max, _pendingNpcDamageAck.Count);
            var result = _pendingNpcDamageAck.GetRange(0, take);
            _pendingNpcDamageAck.RemoveRange(0, take);
            return result;
        }
    }

    // ---- 掉落物 / 弹幕（服务端权威实体）----

    /// <summary>世界掉落物（原版 <c>Main.item[]</c>）。</summary>
    public List<WorldItemEntity> Items { get; } = new();

    /// <summary>弹幕（原版 <c>Main.projectile[]</c>）。</summary>
    public List<ProjectileEntity> Projectiles { get; } = new();

    /// <summary>掉落物列表的跨线程保护。</summary>
    public object ItemsLock { get; } = new();

    /// <summary>弹幕列表的跨线程保护。</summary>
    public object ProjectilesLock { get; } = new();

    /// <summary>按包 7 布局构造 <see cref="WorldInfoPacket"/>。</summary>
    public WorldInfoPacket ToWorldInfoPacket()
    {
        var p = Progress;
        var flags = new byte[11];

        byte b4 = 0;
        if (p.ShadowOrbSmashed) b4 |= 1 << 0;
        if (p.DownedBoss1) b4 |= 1 << 1;
        if (p.DownedBoss2) b4 |= 1 << 2;
        if (p.DownedBoss3) b4 |= 1 << 3;
        if (p.HardMode) b4 |= 1 << 4;
        if (p.DownedClown) b4 |= 1 << 5;
        // 原版 bitsByte7[6] = ServerSideCharacter：开启 SSC 时服务端持有背包唯一真相，
        // 客户端据此才会接受服务端下发的背包槽（包 5）；否则客户端忽略对自身的包 5（give 无效）。
        // 由全局配置 <see cref="SscEnabled"/> 控制：关闭时走原版非 SSC 流程（客户端本地背包为准）。
        if (SscEnabled) b4 |= 1 << 6;
        if (p.DownedPlantBoss) b4 |= 1 << 7;
        flags[0] = b4;

        byte b5 = 0;
        if (p.DownedMechBoss1) b5 |= 1 << 0;
        if (p.DownedMechBoss2) b5 |= 1 << 1;
        if (p.DownedMechBoss3) b5 |= 1 << 2;
        if (p.DownedMechBossAny) b5 |= 1 << 3;
        if (CloudBgActive >= 1) b5 |= 1 << 4;
        if (p.Crimson) b5 |= 1 << 5;
        if (p.PumpkinMoon) b5 |= 1 << 6;
        if (p.SnowMoon) b5 |= 1 << 7;
        flags[1] = b5;

        byte b6 = 0;
        if (p.FastForwardTimeToDawn) b6 |= 1 << 1;
        if (p.SlimeRain) b6 |= 1 << 2;
        if (p.DownedSlimeKing) b6 |= 1 << 3;
        if (p.DownedQueenBee) b6 |= 1 << 4;
        if (p.DownedFishron) b6 |= 1 << 5;
        if (p.DownedMartians) b6 |= 1 << 6;
        if (p.DownedAncientCultist) b6 |= 1 << 7;
        flags[2] = b6;

        byte b7 = 0;
        if (p.DownedMoonlord) b7 |= 1 << 0;
        if (p.DownedHalloweenKing) b7 |= 1 << 1;
        if (p.DownedHalloweenTree) b7 |= 1 << 2;
        if (p.DownedChristmasIceQueen) b7 |= 1 << 3;
        if (p.DownedChristmasSantank) b7 |= 1 << 4;
        if (p.DownedChristmasTree) b7 |= 1 << 5;
        if (p.DownedGolemBoss) b7 |= 1 << 6;
        if (p.PartyIsUp) b7 |= 1 << 7;
        flags[3] = b7;

        byte b8 = 0;
        if (p.DownedPirates) b8 |= 1 << 0;
        if (p.DownedFrost) b8 |= 1 << 1;
        if (p.DownedGoblins) b8 |= 1 << 2;
        if (p.SandstormHappening) b8 |= 1 << 3;
        if (p.Dd2Ongoing) b8 |= 1 << 4;
        if (p.Dd2DownedInvasionT1) b8 |= 1 << 5;
        if (p.Dd2DownedInvasionT2) b8 |= 1 << 6;
        if (p.Dd2DownedInvasionT3) b8 |= 1 << 7;
        flags[4] = b8;

        byte b9 = 0;
        if (p.CombatBookWasUsed) b9 |= 1 << 0;
        if (p.LanternsUp) b9 |= 1 << 1;
        if (p.DownedTowerSolar) b9 |= 1 << 2;
        if (p.DownedTowerVortex) b9 |= 1 << 3;
        if (p.DownedTowerNebula) b9 |= 1 << 4;
        if (p.DownedTowerStardust) b9 |= 1 << 5;
        if (p.ForceHalloweenForToday) b9 |= 1 << 6;
        if (p.ForceXMasForToday) b9 |= 1 << 7;
        flags[5] = b9;

        byte b10 = 0;
        if (p.BoughtCat) b10 |= 1 << 0;
        if (p.BoughtDog) b10 |= 1 << 1;
        if (p.BoughtBunny) b10 |= 1 << 2;
        if (p.FreeCake) b10 |= 1 << 3;
        if (p.DrunkWorld) b10 |= 1 << 4;
        if (p.DownedEmpressOfLight) b10 |= 1 << 5;
        if (p.DownedQueenSlime) b10 |= 1 << 6;
        if (p.GetGoodWorld) b10 |= 1 << 7;
        flags[6] = b10;

        byte b11 = 0;
        if (p.TenthAnniversaryWorld) b11 |= 1 << 0;
        if (p.DontStarveWorld) b11 |= 1 << 1;
        if (p.DownedDeerclops) b11 |= 1 << 2;
        if (p.NotTheBeesWorld) b11 |= 1 << 3;
        if (p.RemixWorld) b11 |= 1 << 4;
        if (p.UnlockedSlimeBlueSpawn) b11 |= 1 << 5;
        if (p.CombatBookVolumeTwoWasUsed) b11 |= 1 << 6;
        if (p.PeddlersSatchelWasUsed) b11 |= 1 << 7;
        flags[7] = b11;

        byte b12 = 0;
        if (p.UnlockedSlimeGreenSpawn) b12 |= 1 << 0;
        if (p.UnlockedSlimeOldSpawn) b12 |= 1 << 1;
        if (p.UnlockedSlimePurpleSpawn) b12 |= 1 << 2;
        if (p.UnlockedSlimeRainbowSpawn) b12 |= 1 << 3;
        if (p.UnlockedSlimeRedSpawn) b12 |= 1 << 4;
        if (p.UnlockedSlimeYellowSpawn) b12 |= 1 << 5;
        if (p.UnlockedSlimeCopperSpawn) b12 |= 1 << 6;
        if (p.FastForwardTimeToDusk) b12 |= 1 << 7;
        flags[8] = b12;

        byte b13 = 0;
        if (p.NoTrapsWorld) b13 |= 1 << 0;
        if (p.ZenithWorld) b13 |= 1 << 1;
        if (p.UnlockedTruffleSpawn) b13 |= 1 << 2;
        if (p.VampireSeed) b13 |= 1 << 3;
        if (p.InfectedSeed) b13 |= 1 << 4;
        if (p.TeamBasedSpawnsSeed) b13 |= 1 << 5;
        if (p.SkyblockWorld) b13 |= 1 << 6;
        if (p.DualDungeonsSeed) b13 |= 1 << 7;
        flags[9] = b13;

        byte b14 = 0;
        if (p.SkyblockLowTiles) b14 |= 1 << 0;
        if (p.ForceHalloweenForever) b14 |= 1 << 1;
        if (p.ForceXMasForever) b14 |= 1 << 2;
        if (p.MoreLightningSeed) b14 |= 1 << 3;
        if (p.NoLightningSeed) b14 |= 1 << 4;
        flags[10] = b14;

        var spawns = new (short X, short Y)[ExtraSpawnPoints.Count];
        for (int i = 0; i < spawns.Length; i++) spawns[i] = ExtraSpawnPoints[i];

        var backgrounds = new byte[13];
        for (int i = 0; i < 13; i++) backgrounds[i] = i < Backgrounds.Length ? Backgrounds[i] : (byte)0;

        var treeTops = new byte[13];
        for (int i = 0; i < 13; i++) treeTops[i] = i < TreeTops.Length ? (byte)TreeTops[i] : (byte)0;

        return new WorldInfoPacket(
            Time: (int)Time,
            DayTime: DayTime,
            BloodMoon: BloodMoon,
            Eclipse: Eclipse,
            MoonPhase: (byte)MoonPhase,
            MaxTilesX: (short)MaxTilesX,
            MaxTilesY: (short)MaxTilesY,
            SpawnTileX: (short)SpawnTileX,
            SpawnTileY: (short)SpawnTileY,
            WorldSurface: (short)WorldSurface,
            RockLayer: (short)RockLayer,
            WorldId: WorldId,
            WorldName: WorldName,
            GameMode: (byte)GameMode)
        {
            UniqueId = UniqueId,
            WorldGeneratorVersion = WorldGeneratorVersion,
            MoonType = MoonType,
            Backgrounds = backgrounds,
            IceBackStyle = (byte)IceBackStyle,
            JungleBackStyle = (byte)JungleBackStyle,
            HellBackStyle = (byte)HellBackStyle,
            WindSpeedTarget = WindSpeedTarget,
            NumClouds = (byte)NumClouds,
            TreeX = TreeX,
            TreeStyle = new byte[] { (byte)TreeStyle[0], (byte)TreeStyle[1], (byte)TreeStyle[2], (byte)TreeStyle[3] },
            CaveBackX = CaveBackX,
            CaveBackStyle = new byte[] { (byte)CaveBackStyle[0], (byte)CaveBackStyle[1], (byte)CaveBackStyle[2], (byte)CaveBackStyle[3] },
            TreeTops = treeTops,
            MaxRaining = Raining ? MaxRain : 0f,
            ProgressFlags = flags,
            SundialCooldown = SundialCooldown,
            MoondialCooldown = MoondialCooldown,
            OreTiers = OreTiers,
            InvasionType = (sbyte)InvasionType,
            LobbyId = LobbyId,
            SandstormIntensity = SandstormIntensity,
            ExtraSpawnPoints = spawns,
            DungeonX = (short)DungeonX,
            DungeonY = (short)DungeonY
        };
    }
}

/// <summary>
/// 玩家运行时状态（服务端权威唯一真相）：位置 / 速度由仿真推进，生命由战斗阶段结算。
/// </summary>
public sealed class PlayerRuntime
{
    public long SessionId;
    public int Id;

    /// <summary>世界坐标（像素）。</summary>
    public Vector2 Position;

    /// <summary>每 tick 速度（像素 / tick）。</summary>
    public Vector2 Velocity;

    /// <summary>是否在线：断线后置 false，不再参与仿真与快照。</summary>
    public bool Active = true;

    public int Hp = 100;
    public int HpMax = 100;

    /// <summary>法力 / 法力上限（包 42 权威跟踪；原版不对他人转发法力）。</summary>
    public int Mp = 20;
    public int MpMax = 20;

    /// <summary>是否已接受本次会话的首个合法法力同步；之后客户端只能报告减少。</summary>
    public bool HasReceivedManaSync;

    /// <summary>服务端持有的增益 / 减益列表（包 50 权威；上限与原版增益槽位数一致，44）。</summary>
    public readonly List<int> Buffs = new();

    /// <summary>是否处于死亡状态：死亡后不再参与物理 / 战斗，直到复活命令复位。</summary>
    public bool Dead;

    /// <summary>死亡是否已广播（包 118），由世界同步线程置位，避免重复下发。</summary>
    public bool DeathNotified;

    /// <summary>复活是否已广播（包 12 / 16），由世界同步线程置位。</summary>
    public bool RespawnNotified = true;

    /// <summary>受击免伤帧剩余 tick：&gt;0 时不再结算任何来源的伤害（原版 <c>Player.immune</c> 跨来源统一）。</summary>
    public int HurtCooldown;

    /// <summary>
    /// 玩家防御值（原版 <c>Player.statDefense</c>，装备 / buff 合计）。
    /// 由 <see cref="RecalculateDefense"/> 在物品栏变更（包 5）时重算；
    /// 用于 117 区间校验的减防上界与扣血计算。
    /// </summary>
    public int Defense;

    /// <summary>物品栏槽位数量（原版 59：装备区 0-8 / 物品区 / 钱币 / 弹药 / 材料）。</summary>
    public const int InventorySlotCount = 59;

    /// <summary>物品栏（槽位 → 物品 ID；0 = 空）。由服务器受控写入（服务端 SSC 唯一真相）。</summary>
    public readonly int[] Items = new int[InventorySlotCount];

    /// <summary>物品栏槽位 → 堆叠数量；0 表示空，必须与 <see cref="Items"/> 一致。</summary>
    public readonly int[] ItemStacks = new int[InventorySlotCount];

    /// <summary>物品栏槽位 → 物品前缀（服务器受控写入；近战武器校验按此前缀修正基础伤害）。</summary>
    public readonly byte[] ItemPrefixes = new byte[InventorySlotCount];

    /// <summary>
    /// 手持热键槽（原版 <c>Player.selectedItem</c>，包 13 权威更新）：手持武器 = <see cref="Items"/>[SelectedSlot]。
    /// 阶段 E 近战武器校验据此定位玩家当前武器。
    /// </summary>
    public int SelectedSlot;

    /// <summary>装备区槽位闭区间 [0, 8]：0-2 头盔/胸甲/护腿、3-7 饰品、8 盾牌（原版给防御的装备区）。</summary>
    public const int EquipmentSlotStart = 0;
    public const int EquipmentSlotEnd = 8;

    /// <summary>护甲槽闭区间 [0, 2]：头盔/胸甲/护腿（供护甲单件职业伤害修饰扫描）。</summary>
    public const int ArmorSlotEnd = 2;

    /// <summary>饰品槽起点（原版 3-8：3-7 饰品 + 8 盾牌/坐骑专属），供饰品伤害/防御修饰扫描。</summary>
    public const int AccessorySlotStart = 3;

    /// <summary>
    /// 重算防御：装备区物品防御 + Buff 防御 + 套装防御加成（原版 <c>Player.statDefense</c> = 装备 + 增益 + 套装）。
    /// 物品栏变更（包 5）与 Buff 变更（包 50）后调用；117 区间上界 / 接触兜底据此实时减防。
    /// 未知物品 / 空槽经 <see cref="ItemDefenseTable.DefenseOf"/> 记为 0。
    /// </summary>
    public void RecalculateDefense()
    {
        int sum = 0;
        for (int i = EquipmentSlotStart; i <= EquipmentSlotEnd && i < InventorySlotCount; i++)
            sum += ItemDefenseTable.DefenseOf(Items[i]);
        sum += BuffTable.DefenseOf(Buffs);
        sum += ArmorSetBonusTable.BonusForEquipment(Items)?.DefenseBonus ?? 0;
        Defense = sum;
    }

    /// <summary>
    /// **受击免伤帧**（接触攻击 / 客户端上报的包 117 / 下落伤害 / 敌对弹幕共用一个窗口）。
    /// 原版 <c>Player.Hurt</c>：<c>immuneTime = pvp ? 8 : (伤害 ≠ 1 ? (longInvince ? 80 : 40) : (longInvince ? 40 : 20))</c>，
    /// 且 NPC 接触走的也是 <c>Hurt</c>（<c>cooldownCounter == ImmunityCooldownID.General</c>）。
    /// 注意原版 <c>GiveImmuneTimeForCollisionAttack(60 / 30)</c> 只用于**盾牌弹反**分支，不是普通接触。
    /// 我们未建模十字项链、非 PvP → 伤害 &gt; 1 取 <see cref="HurtImmunityTicks"/>，伤害被防御压到 1 取 <see cref="WeakHurtImmunityTicks"/>。
    /// </summary>
    public const int HurtImmunityTicks = 40;

    /// <summary>受击里「伤害 ≤ 1」时的较短窗口（原版 20 tick）。</summary>
    public const int WeakHurtImmunityTicks = 20;

    /// <summary>按原版 <c>Hurt</c> 规则取免伤帧长（无十字项链、非 PvP）；接触 / 弹幕 / 下落 / 包 117 共用。</summary>
    public static int GeneralImmunityTicks(int damage) => damage > 1 ? HurtImmunityTicks : WeakHurtImmunityTicks;

    /// <summary>
    /// **重生无敌帧**：原版 <c>Player.Spawn</c> 的 <c>ReviveFromDeath</c> 分支 <c>immuneTime = 180</c>（3 秒），
    /// PvP 死亡复活为 300（5 秒）。服务端在复活时同样置免伤帧，接触兜底 / 包 117 在此期间跳过，与客户端重生闪烁对齐。
    /// 进世界（SpawningIntoWorld）的 60 由进服流程自然覆盖（出生点通常无怪）。
    /// </summary>
    public const int RespawnImmunityTicks = 180;

    /// <summary>连续下落距离（像素），落地时用于结算下落伤害。</summary>
    public float FallDistance;

    /// <summary>会话恢复键（玩家名）：断线后用于在宽限期内认回同一运行时。</summary>
    public string ResumeKey = "";

    /// <summary>本次进服是否由「会话恢复」接管：跳过出生点重定位、保留原坐标与状态。</summary>
    public bool Resumed;

    /// <summary>
    /// 包 13 上报的控制位（与原版 <c>Player.control*</c> 的位序一致）：
    /// bit0 上 / bit1 下 / bit2 左 / bit3 右 / bit4 跳 / bit5 用物品 …
    /// 服务端据此**自己推进玩家物理**（原版做法），无需依赖连续的位置包。
    /// </summary>
    public byte ControlBits;

    /// <summary>是否已收到过包 13；旧的直接命令测试路径在此之前保持兼容。</summary>
    public bool HasReceivedPlayerControls;

    /// <summary>当前按住 UseItem 时包 13 关联的手持槽位；松开后清除。</summary>
    public int UseItemSelectedSlot = -1;

    /// <summary>上次获准创建受 UseItem 状态机约束弹幕的服务端 tick。</summary>
    public long LastUseItemProjectileSpawnTick = long.MinValue;

    /// <summary>当前 UseItem 动画周期剩余 tick；松开或换槽位时清零。</summary>
    public int UseAnimationTicksRemaining;

    /// <summary>服务器分配的下一次开火事务序号，绑定当前 UseItem 周期。</summary>
    public long NextFireTransactionId = 1;

    /// <summary>已提交的开火事务；用于网络重试幂等，键为服务端事务号。</summary>
    public readonly HashSet<long> AppliedFireTransactions = new();

    private readonly Queue<long> _appliedFireTransactionOrder = new();

    /// <summary>每位玩家保留的最近开火事务数，避免重试去重状态无限增长。</summary>
    public const int MaxAppliedFireTransactions = 256;

    public void RecordAppliedFireTransaction(long transactionId)
    {
        if (!AppliedFireTransactions.Add(transactionId))
            return;

        _appliedFireTransactionOrder.Enqueue(transactionId);
        while (_appliedFireTransactionOrder.Count > MaxAppliedFireTransactions)
            AppliedFireTransactions.Remove(_appliedFireTransactionOrder.Dequeue());
    }

    /// <summary>上一 tick 是否按住跳跃键。原版用 <c>releaseJump</c> 要求「松开后再按」才算一次起跳。</summary>
    public bool JumpHeld;

    /// <summary>本 tick 是否贴地（由玩家物理的落地方程维护）。</summary>
    public bool Grounded;

    /// <summary>水平朝向（1 = 右 / -1 = 左）。</summary>
    public int Direction = 1;

    /// <summary>
    /// 「游戏判定用」的玩家位置（NPC 追击 / 接触伤害 / 敌对弹幕 / 刷怪点都用它）。
    /// 原版服务端对远端玩家同样执行 <c>Player.Update</c>（按同步来的控制位继续模拟移动），
    /// 所以服务端坐标与客户端始终一致；TerraAuth 现在同样按控制位跑玩家物理，
    /// 该值就等于模拟后的 <see cref="Position"/>（见 <c>WorldSimulator.StepPlayerPhysics</c>）。
    /// </summary>
    public Vector2 AimPosition;

    public const byte ControlUp = 0x01;
    public const byte ControlDown = 0x02;
    public const byte ControlLeft = 0x04;
    public const byte ControlRight = 0x08;
    public const byte ControlJump = 0x10;
    public const byte ControlUseItem = 0x20;

    public bool PressingLeft => (ControlBits & ControlLeft) != 0;
    public bool PressingRight => (ControlBits & ControlRight) != 0;
    public bool PressingJump => (ControlBits & ControlJump) != 0;
    public bool PressingDown => (ControlBits & ControlDown) != 0;
    public bool PressingUseItem => (ControlBits & ControlUseItem) != 0;
}

/// <summary>
/// 世界进度位。字段与包 7 的 11 个 BitsByte 一一对应，并经协议行为验证。
/// </summary>
public sealed class WorldProgress
{
    // bitsByte4
    public bool ShadowOrbSmashed;
    public bool DownedBoss1;
    public bool DownedBoss2;
    public bool DownedBoss3;
    public bool HardMode;
    public bool DownedClown;
    public bool DownedPlantBoss;

    // bitsByte5
    public bool DownedMechBoss1;
    public bool DownedMechBoss2;
    public bool DownedMechBoss3;
    public bool DownedMechBossAny;
    public bool Crimson;
    public bool PumpkinMoon;
    public bool SnowMoon;

    // bitsByte6
    public bool FastForwardTimeToDawn;
    public bool SlimeRain;
    public bool DownedSlimeKing;
    public bool DownedQueenBee;
    public bool DownedFishron;
    public bool DownedMartians;
    public bool DownedAncientCultist;

    // bitsByte7
    public bool DownedMoonlord;
    public bool DownedHalloweenKing;
    public bool DownedHalloweenTree;
    public bool DownedChristmasIceQueen;
    public bool DownedChristmasSantank;
    public bool DownedChristmasTree;
    public bool DownedGolemBoss;
    public bool PartyIsUp;

    // bitsByte8
    public bool DownedPirates;
    public bool DownedFrost;
    public bool DownedGoblins;
    public bool SandstormHappening;
    public bool Dd2Ongoing;
    public bool Dd2DownedInvasionT1;
    public bool Dd2DownedInvasionT2;
    public bool Dd2DownedInvasionT3;

    // bitsByte9
    public bool CombatBookWasUsed;
    public bool LanternsUp;
    public bool DownedTowerSolar;
    public bool DownedTowerVortex;
    public bool DownedTowerNebula;
    public bool DownedTowerStardust;
    public bool ForceHalloweenForToday;
    public bool ForceXMasForToday;

    // bitsByte10
    public bool BoughtCat;
    public bool BoughtDog;
    public bool BoughtBunny;
    public bool FreeCake;
    public bool DrunkWorld;
    public bool DownedEmpressOfLight;
    public bool DownedQueenSlime;
    public bool GetGoodWorld;

    // bitsByte11
    public bool TenthAnniversaryWorld;
    public bool DontStarveWorld;
    public bool DownedDeerclops;
    public bool NotTheBeesWorld;
    public bool RemixWorld;
    public bool UnlockedSlimeBlueSpawn;
    public bool CombatBookVolumeTwoWasUsed;
    public bool PeddlersSatchelWasUsed;

    // bitsByte12
    public bool UnlockedSlimeGreenSpawn;
    public bool UnlockedSlimeOldSpawn;
    public bool UnlockedSlimePurpleSpawn;
    public bool UnlockedSlimeRainbowSpawn;
    public bool UnlockedSlimeRedSpawn;
    public bool UnlockedSlimeYellowSpawn;
    public bool UnlockedSlimeCopperSpawn;
    public bool FastForwardTimeToDusk;

    // bitsByte13
    public bool NoTrapsWorld;
    public bool ZenithWorld;
    public bool UnlockedTruffleSpawn;
    public bool VampireSeed;
    public bool InfectedSeed;
    public bool TeamBasedSpawnsSeed;
    public bool SkyblockWorld;
    public bool DualDungeonsSeed;

    // bitsByte14
    public bool SkyblockLowTiles;
    public bool ForceHalloweenForever;
    public bool ForceXMasForever;
    public bool MoreLightningSeed;
    public bool NoLightningSeed;
}

/// <summary>宝箱物品格。</summary>
public struct ChestItem
{
    public int Type;
    public short Stack;
    public byte Prefix;
}

/// <summary>世界宝箱（存档 section 3）。</summary>
public sealed class Chest
{
    public int Index;
    public bool Deleted;
    public int X;
    public int Y;
    public string Name = "";
    public ChestItem[] Items = Array.Empty<ChestItem>();

    /// <summary>单格物品的定长字节数：Int32 Type + Int16 Stack + Byte Prefix。</summary>
    private const int ItemSize = 7;

    /// <summary>物品格上限（防御非法长度导致的超大分配）。</summary>
    private const int MaxItems = 1024;

    /// <summary>
    /// 把物品格整体序列化为字节串（供箱子内容持久化）。
    /// 编码为「Int32 格数 + 每格定长 7 字节」，与 <see cref="DeserializeItems"/> 严格对称。
    /// </summary>
    public static byte[] SerializeItems(ChestItem[] items)
    {
        using var ms = new MemoryStream(sizeof(int) + items.Length * ItemSize);
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(items.Length);
            foreach (var item in items)
            {
                w.Write(item.Type);
                w.Write(item.Stack);
                w.Write(item.Prefix);
            }
        }
        return ms.ToArray();
    }

    /// <summary>反序列化物品格（长度非法时返回空数组，避免损坏数据导致崩溃）。</summary>
    public static ChestItem[] DeserializeItems(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms);
        var count = r.ReadInt32();
        if (count < 0 || count > MaxItems) return Array.Empty<ChestItem>();

        var items = new ChestItem[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = new ChestItem
            {
                Type = r.ReadInt32(),
                Stack = r.ReadInt16(),
                Prefix = r.ReadByte(),
            };
        }
        return items;
    }
}

/// <summary>世界牌子（存档 section 4）。</summary>
public sealed class Sign
{
    public int Index;
    public int X;
    public int Y;
    public string Text = "";
}

/// <summary>世界 NPC（存档 section 5；<see cref="IsTownNpc"/> 区分城镇 NPC 与仅位置记录）。</summary>
public sealed class WorldNpc
{
    public int Type;
    public string GivenName = "";
    public float X;
    public float Y;
    public bool IsTownNpc;
    public bool Homeless;
    public int HomeTileX;
    public int HomeTileY;

    // ---- 运行时（网络同步 / 战斗）----

    /// <summary>网络 ID（客户端据此 <c>SetDefaults</c> 生成对应 NPC）；默认与 <see cref="Type"/> 相同。</summary>
    public short NetId;

    /// <summary>生成代数（包 23 用于校验命中的目标未过期）。</summary>
    public byte Generation;

    public int Life = 100;
    public int LifeMax = 100;

    /// <summary>基础伤害（原版 <c>NPC.SetDefaults.damage</c>，经典难度基准；生成时由 <see cref="NpcStatsTable"/> 回填）。</summary>
    public int Damage;

    /// <summary>基础防御（原版 <c>NPC.SetDefaults.defense</c>）。</summary>
    public int Defense;

    public float VelocityX;
    public float VelocityY;

    // ---- 增益 / 减益（原版 NPC.buffType / NPC.buffTime，最多 5 槽）----

    /// <summary>
    /// NPC 当前增益 / 减益：buffType → 剩余时长（tick）。原版上限 5 槽（包 54 下发全量）。
    /// 服务端权威增减，改动后须调用 <see cref="WorldState.MarkNpcBuffsChanged"/> 触发包 54 回写。
    /// </summary>
    public readonly Dictionary<int, int> Buffs = new();

    /// <summary>是否存活；false 时同步 <c>life=0</c> 让客户端移除。</summary>
    public bool Active = true;

    /// <summary>原版 <c>noGravity</c> / <c>noTileCollide</c>：飞行体（Boss 等）不受重力、不做图格落地。</summary>
    public bool NoGravity;

    /// <summary>是否为 Boss（服务端权威：击杀后记录世界进度，AI 为简化追击）。</summary>
    public bool IsBoss;

    /// <summary>死亡发生的 tick（用于延后清理，确保 life=0 已下发到客户端）。</summary>
    public long DeadTick;

    // ---- 网络同步状态（不落盘）----

    /// <summary>
    /// 原版 <c>NPC.aiStyle</c>：决定走哪套 AI 实现（见 <c>WorldSimulator.RunNpcAi</c> 的分发表）。
    /// 0 = 未指定（走简化兜底）。
    /// </summary>
    public int AiStyle;

    /// <summary>
    /// 原版 <c>NPC.ai[0..3]</c> 状态槽（与包 23 下发/接收的语义一致）。
    /// 各 aiStyle 自行解释其含义；服务端权威持有，客户端据此对齐动画与状态。
    /// </summary>
    public readonly float[] Ai = new float[4];

    /// <summary>原版 <c>NPC.localAI[0..3]</c>：不参与网络同步的本地计时/暂存槽（如弹幕发射节流 / 小动物卡住判定）。</summary>
    public readonly float[] LocalAi = new float[4];

    /// <summary>朝向（原版 <c>NPC.direction</c>，1 = 右 / -1 = 左）。</summary>
    public int Direction = 1;

    /// <summary>垂直朝向（原版 <c>NPC.directionY</c>，1 = 下 / -1 = 上；蜗牛爬墙状态机使用）。</summary>
    public int DirectionY = 1;

    /// <summary>上一帧是否贴地（由图格碰撞结果维护）：决定是否进入「等待 → 起跳」。</summary>
    public bool Grounded;

    // ---- 帧内瞬态（由图格碰撞 / 液体探测填写，AI 下一帧读取；对应原版 collideX/collideY/wet）----

    /// <summary>上一帧水平速度是否被图格碰撞清零（原版 <c>NPC.collideX</c>）。</summary>
    public bool CollideX;

    /// <summary>上一帧垂直速度是否被图格碰撞清零（原版 <c>NPC.collideY</c>；落地 / 撞顶都会置位）。</summary>
    public bool CollideY;

    /// <summary>上一帧是否处于液体中（原版 <c>NPC.wet</c>，物理步用新位置探测）。</summary>
    public bool Wet;

    /// <summary>原版 <c>NPC.oldVelocity</c>：碰撞清零之前的水平速度（AI 反弹计算用）。</summary>
    public float PrevVelocityX;

    /// <summary>原版 <c>NPC.oldVelocity.Y</c>。</summary>
    public float PrevVelocityY;

    /// <summary>原版 <c>noTileCollide</c>：跳过图格碰撞（与 <see cref="NoGravity"/> 独立）。</summary>
    public bool NoTileCollide;

    /// <summary>上一次已下发的状态；用于「变化才发」（未变化时按心跳周期补发）。</summary>
    public float SyncedX;
    public float SyncedY;
    public float SyncedVelocityX;
    public float SyncedVelocityY;

    /// <summary>上一次已下发的生命（<see cref="int.MinValue"/> = 从未下发 → 首次必然下发）。</summary>
    public int SyncedLife = int.MinValue;

    /// <summary>上一次已下发的存活状态。</summary>
    public bool SyncedActive;

    /// <summary>上一次已下发的朝向。</summary>
    public int SyncedDirection = int.MinValue;

    /// <summary>上一次已下发的 ai[0..3]（供「变化才发」比较）。</summary>
    public readonly float[] SyncedAi = new float[4];

    /// <summary>上一次下发所在 tick（心跳判定：未变化也每 1s 补发一次，保证新入服玩家能看到静止 NPC）。</summary>
    public long SyncedTick;

    /// <summary>
    /// 强制下一轮同步下发（包 23）。用于「不在状态变化判据里」的字段改动 —— 目前是
    /// <see cref="Type"/> / <see cref="NetId"/>（换型：<c>NPC.Transform</c>）。
    /// 客户端只在包 23 的 netID 与本地不一致时重建外观（<c>MessageBuffer</c> 包 23 分支），
    /// 所以换型必须无条件重发一次，否则客户端会一直画旧形态。
    /// </summary>
    public bool SyncForced;
}
