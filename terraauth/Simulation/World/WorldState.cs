// TerraAuth — Phase 6: 世界状态（权威数据模型）
// 字段来源：原版客户端世界文件的头部与各分段（世界旗标 / 箱子 / 告示牌 / NPC），
//   以及包 7（WorldData）的字段顺序（Terraria 1.4.5.8 / Protocol 326）
// 该类型是服务端权威世界的唯一真相来源：.wld 解析 / 程序化生成均产出它，NetworkHost 由它构造出站包。

using System;
using System.Collections.Generic;
using System.IO;
using TerraAuth.Protocol;

namespace TerraAuth.Simulation;

/// <summary>世界全局状态：元数据 + 图格矩阵 + 实体列表。</summary>
public interface IInventoryLedger
{
    bool ConsumeItem(int playerId, int itemId);
    bool TryAddItem(int playerId, int itemId, int stack);
    bool TryAddItemExactly(int playerId, int itemId, int stack);
}

public sealed class WorldState
{
    public IInventoryLedger? InventoryLedger { get; set; }

    // ---- Phase 3 兼容字段 ----
    public long Tick { get; set; }

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
    /// 玩家断线：把运行时移出在线集合（**这同时修掉「断线运行时永不释放」的泄漏**）。
    /// <paramref name="graceTicks"/> &gt; 0 且恢复键非空时挂入离线会话，供同身份重连认回；否则直接丢弃。
    /// </summary>
    public void MarkPlayerOffline(int playerId, string resumeKey, long graceTicks)
    {
        PlayerRuntime? runtime;
        lock (PlayersLock)
        {
            if (!Players.TryGetValue(playerId, out runtime)) return;
            Players.Remove(playerId);
        }

        runtime.Active = false;
        runtime.Velocity = new Vector2(0, 0);
        CloseChestSession(playerId);

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
    {
        if (string.IsNullOrEmpty(resumeKey)) return false;

        lock (PlayersLock)
        {
            if (!_offlineSessions.TryGetValue(resumeKey, out var session)) return false;
            _offlineSessions.Remove(resumeKey);
            if (session.DeadlineTick <= Tick) return false;   // 已超宽限期 → 按新玩家处理

            var runtime = session.Runtime;
            runtime.Id = newPlayerId;
            runtime.Active = true;
            runtime.Resumed = true;
            runtime.RespawnNotified = false;   // 世界同步据此下发包 12（携恢复后的坐标）
            Players[newPlayerId] = runtime;
            return true;
        }
    }

    /// <summary>回收超过宽限期的离线会话（由世界同步循环调用）；返回回收数量。</summary>
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

    /// <summary>0=普通、1=专家、2=大师、3=旅途。</summary>
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
    /// 进度位映射与 NPC 类型 ID 均按**原版客户端行为**逐项核对（协议字段比对，不含第三方源码）。
    /// 由仿真线程调用（NPC 死亡处）。
    /// </summary>
    public void NotifyNpcKilled(int npcType, float x = 0f, float y = 0f)
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
            case 109: Progress.DownedClown = true; break;                // Clown
            default: return;
        }

        DropBossLoot(npcType, x, y);
        ProgressDirty = true;
    }

    /// <summary>
    /// Boss 掉落表（**简化模型**：物品 ID 按原版 <c>ItemID</c> 核对，数量为简化值）。
    /// 未收录的 Boss 不掉落 —— 原版掉落规则在掉落数据库中，此处不逐条复刻。
    /// </summary>
    private static readonly Dictionary<int, (int ItemId, int Stack)> BossLoot = new()
    {
        [4] = (56, 30),      // Eye of Cthulhu → Demonite Ore
        [13] = (56, 30),     // Eater of Worlds → Demonite Ore
        [50] = (23, 50),     // King Slime → Gel
        [222] = (2431, 10),  // Queen Bee → Bee Wax
    };

    /// <summary>世界掉落物槽位上限（与原版 <c>Main.item[400]</c> 一致）。</summary>
    private const int MaxItemSlots = 400;

    /// <summary>服务端主动生成掉落物（Boss 掉落）：标记待下发，由世界同步补发包 21。</summary>
    private void DropBossLoot(int npcType, float x, float y)
    {
        if (!BossLoot.TryGetValue(npcType, out var loot)) return;

        lock (ItemsLock)
        {
            if (Items.Count >= MaxItemSlots) return;

            int slot = 0;
            while (Items.Any(i => i.Slot == slot)) slot++;

            Items.Add(new WorldItemEntity
            {
                Slot = slot,
                ItemId = loot.ItemId,
                Stack = loot.Stack,
                Position = new Vector2(x, y),
                Velocity = new Vector2(0f, 0f),
                Prefix = 0,
                OwnedBy = -1,
                NewNotified = false, // 服务端生成 → 客户端尚不知情，需补发包 21
            });
        }
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

    /// <summary>
    /// 箱子列表的跨线程保护：仿真线程按命令写入箱内物品，权威校验 / 网络线程读取内容。
    /// </summary>
    public object ChestsLock { get; } = new();

    /// <summary>玩家当前打开的箱子会话；会话状态由权威层读写。</summary>
    private readonly Dictionary<int, int> _openChests = new();

    public void OpenChestSession(int playerId, int chestIndex)
    {
        lock (ChestsLock)
            _openChests[playerId] = chestIndex;
    }

    public bool HasChestSession(int playerId, int chestIndex)
    {
        lock (ChestsLock)
            return _openChests.TryGetValue(playerId, out var current)
                && current == chestIndex;
    }

    public void CloseChestSession(int playerId)
    {
        lock (ChestsLock)
            _openChests.Remove(playerId);
    }

    public object ChestUpdatesLock { get; } = new();
    private readonly HashSet<(int ChestIndex, int Slot)> _pendingChestUpdates = new();

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
            if (chest.X == x && chest.Y == y) return chest;
        return null;
    }

    /// <summary>按索引查找箱子（越界 / 不存在返回 null）。调用方需持 <see cref="ChestsLock"/>。</summary>
    public Chest? FindChestByIndex(int index)
        => index >= 0 && index < Chests.Count ? Chests[index] : null;

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
                return _persistFullScan || _pendingPersistTiles.Count > 0 || _pendingPersistChests.Count > 0;
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

    /// <summary>服务端持有的增益 / 减益列表（包 50 权威；上限与原版增益槽位数一致，44）。</summary>
    public readonly List<int> Buffs = new();

    /// <summary>是否处于死亡状态：死亡后不再参与物理 / 战斗，直到复活命令复位。</summary>
    public bool Dead;

    /// <summary>死亡是否已广播（包 118），由世界同步线程置位，避免重复下发。</summary>
    public bool DeathNotified;

    /// <summary>复活是否已广播（包 12 / 16），由世界同步线程置位。</summary>
    public bool RespawnNotified = true;

    /// <summary>受击免伤帧剩余 tick：&gt;0 时不再结算接触伤害（约 1 秒）。</summary>
    public int HurtCooldown;

    /// <summary>连续下落距离（像素），落地时用于结算下落伤害。</summary>
    public float FallDistance;

    /// <summary>会话恢复键（玩家名）：断线后用于在宽限期内认回同一运行时。</summary>
    public string ResumeKey = "";

    /// <summary>本次进服是否由「会话恢复」接管：跳过出生点重定位、保留原坐标与状态。</summary>
    public bool Resumed;
}

/// <summary>
/// 世界进度位。字段与包 7 的 11 个 BitsByte 一一对应（权威来源：<c>NetMessage.SendData</c> case 7）。
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

    public float VelocityX;
    public float VelocityY;

    /// <summary>是否存活；false 时同步 <c>life=0</c> 让客户端移除。</summary>
    public bool Active = true;

    /// <summary>是否为 Boss（服务端权威：击杀后记录世界进度，AI 为简化追击）。</summary>
    public bool IsBoss;

    /// <summary>死亡发生的 tick（用于延后清理，确保 life=0 已下发到客户端）。</summary>
    public long DeadTick;
}
