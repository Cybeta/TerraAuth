// Phase 6 - 持久化实现（已填充）
// ============================================================
// 设计：定义 IDbExecutor 抽象隔离具体 ADO.NET 依赖
//   - #if USE_SQLITE：真实 SQLite（Microsoft.Data.Sqlite，默认路径）
//   - 否则：内嵌 LiteDbPersistence（零依赖，沙盒 / CI 直接跑）
// 切换：dotnet build                    → 走 SQLite（csproj 定义 USE_SQLITE）
//       dotnet build -p:NoSqlite=true   → 走内嵌实现
// 时间戳统一以 ISO-8601 UTC 文本存储：定长同格式 → 字符串比较等价于时间比较。
// ============================================================

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using TerraAuth.Security; // BanRecord
#if USE_SQLITE
using Microsoft.Data.Sqlite;
#endif

namespace TerraAuth.Persistence;

/// <summary>数据库执行抽象：隔离 ADO.NET，便于零依赖测试。</summary>
internal interface IDbExecutor
{
    void Migrate();
    PlayerData? GetPlayer(Guid id);
    void SavePlayer(PlayerData data);
    void CreatePlayer(Guid id, string name);

    /// <summary>批量写入审计（单事务，减少 fsync 次数）。</summary>
    void AppendAuditBatch(IReadOnlyList<AuditEntry> batch);
    IReadOnlyList<AuditEntry> QueryAudit(Guid playerId, DateTime since);
    IReadOnlyList<AuditEntry> QueryRecentAudit(int limit);

    // ---- 封禁（IBanStore 复用同一 DB）----
    void SaveBan(BanRecord record);
    bool IsBanned(Guid playerId, string ipAddress);
    void RemoveBan(Guid playerId);

    // ---- 世界改动（IWorldRepository 复用同一 DB）----
    void SaveWorldTiles(IReadOnlyList<WorldTileRecord> tiles);
    IReadOnlyList<WorldTileRecord> LoadWorldTiles();
    void SaveWorldChests(IReadOnlyList<WorldChestRecord> chests);
    IReadOnlyList<WorldChestRecord> LoadWorldChests();
    void DeleteWorldChests(string worldId, IReadOnlyList<int> chestIndices);
    void SaveWorldTileEntities(IReadOnlyList<WorldTileEntityRecord> entities);
    IReadOnlyList<WorldTileEntityRecord> LoadWorldTileEntities();
    /// <summary>写入世界进度（单行覆盖写；<paramref name="progress"/>.WorldId 指定世界）。</summary>
    void SaveWorldProgress(WorldProgressRecord progress);
    /// <summary>读取世界进度（按世界）；无记录返回 null。</summary>
    WorldProgressRecord? LoadWorldProgress(string worldId);
    /// <summary>清空全部世界改动（图格 + 箱子 + 图格实体 + 世界进度）——换种子重开地图时使用（见 <c>ServerConfig.ResetWorldChangesOnStart</c>）。</summary>
    void ClearWorldChanges(string worldId);
}

// ============================================================================
// 公开入口：根据条件编译选择实现
// ============================================================================
public sealed class SqlitePersistence : IPlayerRepository, IAuditRepository, IWorldRepository, IDisposable
{
    private readonly IDbExecutor _db;
    public string WorldId { get; }

    private static string NormalizeWorldId(string? worldId)
        => string.IsNullOrWhiteSpace(worldId) ? "default" : worldId.Trim();
    /// <summary>审计队列容量上限。审计为 best-effort：落盘循环跟不上时丢弃新条，
    /// 记忆体有界、不阻塞关键路径（原为无界，见 OPTIMIZATION_BACKLOG §三 第 7 项）。</summary>
    private const int BoundedChannelCapacity = 4096;
    private readonly Channel<AuditEntry> _auditChannel = Channel.CreateBounded<AuditEntry>(
        new BoundedChannelOptions(BoundedChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    private readonly CancellationTokenSource _batchCts = new();
    private readonly Task _batchTask;

    public SqlitePersistence(string dbPath, bool runMigrations = true, string worldId = "default")
    {
        WorldId = NormalizeWorldId(worldId);
#if USE_SQLITE
        _db = new SqliteImpl(dbPath);
#else
        _db = new LiteDbPersistence(dbPath, runMigrations);
#endif
        if (runMigrations) _db.Migrate();
        _batchTask = Task.Run(() => DrainAuditLoop(_batchCts.Token));
    }

    // 供 SqliteBanStore 复用同一 DB（internal）
    internal IDbExecutor GetExecutor() => _db;

    // ========================================================================
    // IPlayerRepository
    // ========================================================================
    public Task<PlayerData?> GetAsync(Guid playerId)
        // 读走同步 API 包一层（SQLite 读极快，无阻塞风险）
        => Task.Run(() => _db.GetPlayer(playerId));

    public Task SaveAsync(PlayerData data)
        => Task.Run(() => _db.SavePlayer(data));

    public Task CreateIfNotExistsAsync(Guid playerId, string name)
        => Task.Run(() => _db.CreatePlayer(playerId, name));

    // ========================================================================
    // IAuditRepository（异步入队，关键路径零等待）
    // ========================================================================
    public Task AppendAsync(AuditEntry entry)
    {
        _auditChannel.Writer.TryWrite(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> QueryByPlayerAsync(Guid playerId, DateTime since)
        => Task.Run(() => _db.QueryAudit(playerId, since));

    public Task<IReadOnlyList<AuditEntry>> QueryRecentAsync(int limit)
        => Task.Run(() => _db.QueryRecentAudit(limit));

    // ========================================================================
    // IWorldRepository（世界改动增量落盘；空批次直接返回，避免无谓 Task）
    // ========================================================================
    public Task SaveTileChangesAsync(IReadOnlyList<WorldTileRecord> tiles)
    {
        var scoped = tiles.Select(t => t with { WorldId = WorldId }).ToArray();
        return scoped.Length == 0 ? Task.CompletedTask : Task.Run(() => _db.SaveWorldTiles(scoped));
    }

    public Task<IReadOnlyList<WorldTileRecord>> LoadTileChangesAsync()
        => Task.Run<IReadOnlyList<WorldTileRecord>>(() => _db.LoadWorldTiles().Where(t => t.WorldId == WorldId).ToArray());

    public Task SaveChestChangesAsync(IReadOnlyList<WorldChestRecord> chests)
    {
        var scoped = chests.Select(c => c with { WorldId = WorldId }).ToArray();
        return scoped.Length == 0 ? Task.CompletedTask : Task.Run(() => _db.SaveWorldChests(scoped));
    }

    public Task<IReadOnlyList<WorldChestRecord>> LoadChestChangesAsync()
        => Task.Run<IReadOnlyList<WorldChestRecord>>(() => _db.LoadWorldChests().Where(c => c.WorldId == WorldId).ToArray());

    public Task DeleteChestChangesAsync(IReadOnlyList<int> chestIndices)
        => chestIndices.Count == 0 ? Task.CompletedTask : Task.Run(() => _db.DeleteWorldChests(WorldId, chestIndices));

    public Task SaveTileEntityChangesAsync(IReadOnlyList<WorldTileEntityRecord> entities)
    {
        var scoped = entities.Select(e => e with { WorldId = WorldId }).ToArray();
        return scoped.Length == 0 ? Task.CompletedTask : Task.Run(() => _db.SaveWorldTileEntities(scoped));
    }

    public Task<IReadOnlyList<WorldTileEntityRecord>> LoadTileEntityChangesAsync()
        => Task.Run<IReadOnlyList<WorldTileEntityRecord>>(() => _db.LoadWorldTileEntities().Where(e => e.WorldId == WorldId).ToArray());

    /// <summary>
    /// 写入世界进度。进度是**单条**记录（不像图格那样是空批次常态），故不做「空批次直接返回」的判断：
    /// 每次调用都必须真写，否则进度永远停在首次落盘的状态。
    /// </summary>
    public Task SaveWorldProgressAsync(WorldProgressRecord progress)
        => Task.Run(() => _db.SaveWorldProgress(progress with { WorldId = WorldId }));

    /// <summary>读取本世界的进度；再校验一次世界归属（记录里带 WorldId，防止后端实现串到别的世界）。</summary>
    public Task<WorldProgressRecord?> LoadWorldProgressAsync()
        => Task.Run(() =>
        {
            var record = _db.LoadWorldProgress(WorldId);
            return record is null || record.WorldId != WorldId ? null : record;
        });

    public Task ClearWorldChangesAsync()
        => Task.Run(() => _db.ClearWorldChanges(WorldId));

    // ========================================================================
    // 后台批量落盘
    // ========================================================================
    private async Task DrainAuditLoop(CancellationToken ct)
    {
        var buffer = new List<AuditEntry>(capacity: 128);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await _auditChannel.Reader.WaitToReadAsync(ct); } catch { break; }
                while (_auditChannel.Reader.TryRead(out var entry))
                {
                    buffer.Add(entry);
                    if (buffer.Count >= 100) break; // 每批 100 条
                }
                if (buffer.Count > 0)
                    await Task.Run(() => _db.AppendAuditBatch(buffer)).ConfigureAwait(false);
                buffer.Clear();
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 停机：把通道中残留的条目一并落盘，避免丢审计
            while (_auditChannel.Reader.TryRead(out var pending)) buffer.Add(pending);
            if (buffer.Count > 0)
                await Task.Run(() => _db.AppendAuditBatch(buffer)).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _batchCts.Cancel();
        try { _batchTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _batchCts.Dispose();
        // 释放底层执行器（禁用了连接池，此处关闭后即可安全删除 / 备份 DB 文件）
        (_db as IDisposable)?.Dispose();
    }
}

// ============================================================================
// 内嵌轻量实现：零依赖，用 JSON 文件 + 内存索引模拟 SQLite 语义
// 足以让沙盒 / CI / 单机小服完整跑通 Phase 6。
// 玩家 / 审计 / 封禁三类数据均落盘（整份 JSON 重写，适合低频兜底场景）。
// ============================================================================
internal sealed class LiteDbPersistence : IDbExecutor
{
    private readonly string _dbPath;
    private readonly ConcurrentDictionary<Guid, PlayerData> _players = new();
    private readonly ConcurrentDictionary<Guid, List<AuditEntry>> _audit = new();
    private readonly ConcurrentDictionary<Guid, BanRecord> _bans = new();
    private readonly ConcurrentDictionary<(string WorldId, int X, int Y), WorldTileRecord> _worldTiles = new();
    private readonly ConcurrentDictionary<(string WorldId, int Index), WorldChestRecord> _worldChests = new();
    private readonly ConcurrentDictionary<(string WorldId, short X, short Y), WorldTileEntityRecord> _worldTileEntities = new();
    private readonly ConcurrentDictionary<string, WorldProgressRecord> _worldProgress = new();

    public LiteDbPersistence(string dbPath, bool runMigrations)
    {
        _dbPath = dbPath;
        // 从磁盘恢复（模拟持久化）
        LoadFromDisk();
    }

    public void Migrate() { /* 内嵌实现无需建表 */ }

    public PlayerData? GetPlayer(Guid id) => _players.TryGetValue(id, out var p) ? p : null;

    public void SavePlayer(PlayerData data) => _players[data.Id] = data;

    public void CreatePlayer(Guid id, string name)
    {
        _players.TryAdd(id, new PlayerData(
            id, name, JsonSerializer.SerializeToUtf8Bytes(new { Slots = new object[50] }), 100, 20));
    }

    public void AppendAuditBatch(IReadOnlyList<AuditEntry> batch)
    {
        if (batch.Count == 0) return;
        foreach (var entry in batch)
        {
            var list = _audit.GetOrAdd(entry.PlayerId, _ => new List<AuditEntry>());
            lock (list) list.Add(entry);
        }
        // 每批落盘一次（模拟 WAL 刷盘）
        SaveToDisk();
    }

    public IReadOnlyList<AuditEntry> QueryAudit(Guid playerId, DateTime since)
    {
        if (!_audit.TryGetValue(playerId, out var list)) return Array.Empty<AuditEntry>();
        lock (list) return list.Where(e => e.Timestamp >= since).OrderByDescending(e => e.Timestamp).ToList();
    }

    public IReadOnlyList<AuditEntry> QueryRecentAudit(int limit)
    {
        var all = new List<AuditEntry>();
        foreach (var kv in _audit)
            lock (kv.Value) all.AddRange(kv.Value);

        return all.OrderByDescending(e => e.Timestamp).Take(Math.Max(0, limit)).ToList();
    }

    // ---- 封禁（内存 + JSON 持久化）----
    public void SaveBan(BanRecord record)
    {
        _bans[record.PlayerId] = record;
        SaveToDisk(); // 封禁低频，直接落盘
    }

    public bool IsBanned(Guid playerId, string ipAddress)
    {
        if (_bans.TryGetValue(playerId, out var r) && r.ExpiresAt > DateTime.UtcNow) return true;
        if (!string.IsNullOrEmpty(ipAddress))
            return _bans.Values.Any(r => r.IpAddress == ipAddress && r.ExpiresAt > DateTime.UtcNow);
        return false;
    }

    public void RemoveBan(Guid playerId)
    {
        if (_bans.TryRemove(playerId, out _)) SaveToDisk();
    }

    // ---- 世界改动（内存 + JSON 持久化；整份重写，适合兜底场景）----
    public void SaveWorldTiles(IReadOnlyList<WorldTileRecord> tiles)
    {
        if (tiles.Count == 0) return;
        foreach (var t in tiles) _worldTiles[(t.WorldId, t.X, t.Y)] = t;
        SaveToDisk();
    }

    public IReadOnlyList<WorldTileRecord> LoadWorldTiles() => _worldTiles.Values.ToList();

    public void SaveWorldChests(IReadOnlyList<WorldChestRecord> chests)
    {
        if (chests.Count == 0) return;
        foreach (var c in chests) _worldChests[(c.WorldId, c.Index)] = c;
        SaveToDisk();
    }

    public IReadOnlyList<WorldChestRecord> LoadWorldChests() => _worldChests.Values.ToList();

    public void DeleteWorldChests(string worldId, IReadOnlyList<int> chestIndices)
    {
        if (chestIndices.Count == 0) return;
        foreach (var index in chestIndices) _worldChests.TryRemove((worldId, index), out _);
        SaveToDisk();
    }

    public void SaveWorldTileEntities(IReadOnlyList<WorldTileEntityRecord> entities)
    {
        if (entities.Count == 0) return;
        foreach (var entity in entities) _worldTileEntities[(entity.WorldId, entity.X, entity.Y)] = entity;
        SaveToDisk();
    }

    public IReadOnlyList<WorldTileEntityRecord> LoadWorldTileEntities()
        => _worldTileEntities.Values.OrderBy(static entity => entity.X).ThenBy(static entity => entity.Y).ToList();

    public void SaveWorldProgress(WorldProgressRecord progress)
    {
        _worldProgress[progress.WorldId] = progress;   // 单条覆盖写：同世界只留最新进度
        SaveToDisk();
    }

    public WorldProgressRecord? LoadWorldProgress(string worldId)
        => _worldProgress.TryGetValue(worldId, out var record) ? record : null;

    public void ClearWorldChanges(string worldId)
    {
        foreach (var key in _worldTiles.Keys.Where(k => k.WorldId == worldId).ToArray()) _worldTiles.TryRemove(key, out _);
        foreach (var key in _worldChests.Keys.Where(k => k.WorldId == worldId).ToArray()) _worldChests.TryRemove(key, out _);
        foreach (var key in _worldTileEntities.Keys.Where(k => k.WorldId == worldId).ToArray()) _worldTileEntities.TryRemove(key, out _);
        _worldProgress.TryRemove(worldId, out _);
        SaveToDisk();
    }

    // ---- 磁盘持久化（JSON，模拟 SQLite 文件）----
    private void LoadFromDisk()
    {
        if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath)) return;
        try
        {
            var dto = JsonSerializer.Deserialize<LiteDump>(File.ReadAllText(_dbPath));
            if (dto is null) return;
            if (dto.Players is not null)
                foreach (var p in dto.Players) _players[p.Id] = p;
            if (dto.Audit is not null)
                foreach (var e in dto.Audit)
                    _audit.GetOrAdd(e.PlayerId, _ => new List<AuditEntry>()).Add(e); // 启动期单线程
            if (dto.Bans is not null)
                foreach (var b in dto.Bans) _bans[b.PlayerId] = b;
            if (dto.WorldTiles is not null)
                foreach (var t in dto.WorldTiles) _worldTiles[(t.WorldId, t.X, t.Y)] = t;
            if (dto.WorldChests is not null)
                foreach (var c in dto.WorldChests) _worldChests[(c.WorldId, c.Index)] = c;
            if (dto.WorldTileEntities is not null)
                foreach (var entity in dto.WorldTileEntities) _worldTileEntities[(entity.WorldId, entity.X, entity.Y)] = entity;
            if (dto.WorldProgress is not null)
                foreach (var progress in dto.WorldProgress) _worldProgress[progress.WorldId] = progress;
        }
        catch { /* 首次启动无文件 / 解析失败，忽略 */ }
    }

    private void SaveToDisk()
    {
        if (string.IsNullOrEmpty(_dbPath)) return;
        var audit = new List<AuditEntry>();
        foreach (var list in _audit.Values)
            lock (list) audit.AddRange(list);

        var dump = new LiteDump
        {
            Players = _players.Values.ToList(),
            Audit = audit,
            Bans = _bans.Values.ToList(),
            WorldTiles = _worldTiles.Values.ToList(),
            WorldChests = _worldChests.Values.ToList(),
            WorldTileEntities = _worldTileEntities.Values.ToList(),
            WorldProgress = _worldProgress.Values.ToList(),
        };
        var tmp = _dbPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dump));
        File.Move(tmp, _dbPath, overwrite: true);
    }

    private sealed class LiteDump
    {
        public List<PlayerData>? Players { get; set; }
        public List<AuditEntry>? Audit { get; set; }
        public List<BanRecord>? Bans { get; set; }
        public List<WorldTileRecord>? WorldTiles { get; set; }
        public List<WorldChestRecord>? WorldChests { get; set; }
        public List<WorldTileEntityRecord>? WorldTileEntities { get; set; }
        public List<WorldProgressRecord>? WorldProgress { get; set; }
    }
}

// ============================================================================
// 真实 SQLite 实现（生产默认，需 Microsoft.Data.Sqlite）
// 每次操作独立连接：Pooling=false 让句柄随 using 立即释放，
// 避免 Windows 下 DB 文件被占用（备份 / 迁移 / 测试清理时需要删除文件）。
// ============================================================================
#if USE_SQLITE
internal sealed class SqliteImpl : IDbExecutor, IDisposable
{
    private readonly string _connectionString;

    public SqliteImpl(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // synchronous / busy_timeout 为连接级设置，每次连接都需施加。
        // synchronous=FULL 是**有意**选择（别当性能缺陷改回 NORMAL）：WAL 模式下 NORMAL 的提交不刷盘
        // （只在 checkpoint 刷），进程崩溃尚可恢复，但断电 / 系统崩会丢最近若干次提交；
        // FULL 让每次提交都 fsync WAL，把「崩服少丢存档」落到磁盘层面。
        // 代价可接受：本服务端写入很稀疏（玩家档案只在变更时写、图格类 1Hz 批量），
        // 单次 fsync 在 SSD 上约 0.1~1ms。
        Exec(conn, "PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;");
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql, Action<SqliteCommand>? bind = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        cmd.ExecuteNonQuery();
    }

    /// <summary>ISO-8601 UTC 文本：定长同格式，字符串比较即时间比较。</summary>
    private static string Iso(DateTime value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseIso(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void MigrateWorldTables(SqliteConnection conn)
    {
        MigrateWorldTable(conn, "WorldTiles", """
            CREATE TABLE WorldTiles (WorldId TEXT NOT NULL, X INTEGER NOT NULL, Y INTEGER NOT NULL,
            Data BLOB NOT NULL, PRIMARY KEY(WorldId, X, Y));
            """, "WorldId, X, Y, Data", "X, Y, Data");
        MigrateWorldTable(conn, "WorldChests", """
            CREATE TABLE WorldChests (WorldId TEXT NOT NULL, ChestIndex INTEGER NOT NULL,
            X INTEGER NOT NULL, Y INTEGER NOT NULL, Data BLOB NOT NULL, PRIMARY KEY(WorldId, ChestIndex));
            """, "WorldId, ChestIndex, X, Y, Data", "ChestIndex, X, Y, Data");
        MigrateWorldTable(conn, "WorldTileEntities", """
            CREATE TABLE WorldTileEntities (WorldId TEXT NOT NULL, X INTEGER NOT NULL, Y INTEGER NOT NULL,
            RuntimeId INTEGER NOT NULL, FileId INTEGER NOT NULL, Type INTEGER NOT NULL, Data BLOB,
            IsDeleted INTEGER NOT NULL, PRIMARY KEY(WorldId, X, Y));
            """, "WorldId, X, Y, RuntimeId, FileId, Type, Data, IsDeleted", "X, Y, RuntimeId, FileId, Type, Data, IsDeleted");

        // 世界进度：单行（每个世界一条）。新表，不需要上面那套「老表补 WorldId」的迁移逻辑。
        Exec(conn, "CREATE TABLE IF NOT EXISTS WorldProgress (WorldId TEXT PRIMARY KEY, Data BLOB NOT NULL);");
    }

    private static void MigrateWorldTable(SqliteConnection conn, string table, string createSql, string columns, string legacyColumns)
    {
        using var probe = conn.CreateCommand();
        probe.CommandText = $"PRAGMA table_info({table});";
        using var reader = probe.ExecuteReader();
        bool exists = false;
        bool hasWorldId = false;
        while (reader.Read())
        {
            exists = true;
            hasWorldId |= string.Equals(reader.GetString(1), "WorldId", StringComparison.OrdinalIgnoreCase);
        }
        if (!exists || hasWorldId) return;

        string legacy = table + "_LegacyWorldScope";
        Exec(conn, $"DROP TABLE IF EXISTS {legacy}; ALTER TABLE {table} RENAME TO {legacy};");
        Exec(conn, createSql);
        Exec(conn, $"INSERT INTO {table} ({columns}) SELECT 'default', {legacyColumns} FROM {legacy}; DROP TABLE {legacy};");
    }

    public void Migrate()
    {
        using var conn = Open();
        // journal_mode 持久化在 DB 头部，仅在建库时设置一次
        Exec(conn, "PRAGMA journal_mode=WAL;");
        MigrateWorldTables(conn);
        Exec(conn, """
            CREATE TABLE IF NOT EXISTS Players (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                InventoryBlob BLOB NOT NULL,
                MaxHp INTEGER NOT NULL DEFAULT 100,
                MaxMp INTEGER NOT NULL DEFAULT 20,
                UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                PlayerId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Detail TEXT NOT NULL DEFAULT '',
                IpAddress TEXT);
            CREATE INDEX IF NOT EXISTS idx_audit_player_time ON AuditLogs(PlayerId, Timestamp DESC);
            CREATE TABLE IF NOT EXISTS Bans (
                PlayerId TEXT PRIMARY KEY,
                IpAddress TEXT NOT NULL DEFAULT '',
                Reason TEXT NOT NULL DEFAULT '',
                ExpiresAt TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_bans_ip ON Bans(IpAddress);
            CREATE TABLE IF NOT EXISTS WorldTiles (
                WorldId TEXT NOT NULL DEFAULT 'default',
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                Data BLOB NOT NULL,
                PRIMARY KEY(WorldId, X, Y));
            CREATE TABLE IF NOT EXISTS WorldChests (
                WorldId TEXT NOT NULL DEFAULT 'default',
                ChestIndex INTEGER NOT NULL,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                Data BLOB NOT NULL,
                PRIMARY KEY(WorldId, ChestIndex));
            CREATE TABLE IF NOT EXISTS WorldTileEntities (
                WorldId TEXT NOT NULL DEFAULT 'default',
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                RuntimeId INTEGER NOT NULL,
                FileId INTEGER NOT NULL,
                Type INTEGER NOT NULL,
                Data BLOB,
                IsDeleted INTEGER NOT NULL,
                PRIMARY KEY(WorldId, X, Y));
            """);
    }

    // ---- 玩家存档 ----
    public PlayerData? GetPlayer(Guid id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name, InventoryBlob, MaxHp, MaxMp FROM Players WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PlayerData(id, r.GetString(0), (byte[])r[1], r.GetInt32(2), r.GetInt32(3));
    }

    public void SavePlayer(PlayerData data)
    {
        using var conn = Open();
        Exec(conn, """
            INSERT INTO Players (Id, Name, InventoryBlob, MaxHp, MaxMp, UpdatedAt)
            VALUES ($id, $name, $blob, $maxHp, $maxMp, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name          = excluded.Name,
                InventoryBlob = excluded.InventoryBlob,
                MaxHp         = excluded.MaxHp,
                MaxMp         = excluded.MaxMp,
                UpdatedAt     = excluded.UpdatedAt;
            """, cmd =>
        {
            cmd.Parameters.AddWithValue("$id", data.Id.ToString());
            cmd.Parameters.AddWithValue("$name", data.Name);
            cmd.Parameters.AddWithValue("$blob", data.InventoryBlob);
            cmd.Parameters.AddWithValue("$maxHp", data.MaxHp);
            cmd.Parameters.AddWithValue("$maxMp", data.MaxMp);
            cmd.Parameters.AddWithValue("$updatedAt", Iso(DateTime.UtcNow));
        });
    }

    public void CreatePlayer(Guid id, string name)
    {
        using var conn = Open();
        Exec(conn, """
            INSERT OR IGNORE INTO Players (Id, Name, InventoryBlob, MaxHp, MaxMp, UpdatedAt)
            VALUES ($id, $name, $blob, 100, 20, $updatedAt);
            """, cmd =>
        {
            cmd.Parameters.AddWithValue("$id", id.ToString());
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$blob", JsonSerializer.SerializeToUtf8Bytes(new { Slots = new object[50] }));
            cmd.Parameters.AddWithValue("$updatedAt", Iso(DateTime.UtcNow));
        });
    }

    // ---- 审计（单事务批量插入）----
    public void AppendAuditBatch(IReadOnlyList<AuditEntry> batch)
    {
        if (batch.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO AuditLogs (Timestamp, PlayerId, EventType, Detail, IpAddress)
            VALUES ($ts, $pid, $type, $detail, $ip);
            """;
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);
        var pPid = cmd.Parameters.Add("$pid", SqliteType.Text);
        var pType = cmd.Parameters.Add("$type", SqliteType.Text);
        var pDetail = cmd.Parameters.Add("$detail", SqliteType.Text);
        var pIp = cmd.Parameters.Add("$ip", SqliteType.Text);
        foreach (var e in batch)
        {
            pTs.Value = Iso(e.Timestamp);
            pPid.Value = e.PlayerId.ToString();
            pType.Value = e.EventType;
            pDetail.Value = e.Detail;
            pIp.Value = (object?)e.IpAddress ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<AuditEntry> QueryAudit(Guid playerId, DateTime since)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Timestamp, PlayerId, EventType, Detail, IpAddress FROM AuditLogs
            WHERE PlayerId = $pid AND Timestamp >= $since
            ORDER BY Timestamp DESC;
            """;
        cmd.Parameters.AddWithValue("$pid", playerId.ToString());
        cmd.Parameters.AddWithValue("$since", Iso(since));

        var list = new List<AuditEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AuditEntry(
                ParseIso(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }
        return list;
    }

    public IReadOnlyList<AuditEntry> QueryRecentAudit(int limit)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Timestamp, PlayerId, EventType, Detail, IpAddress FROM AuditLogs
            ORDER BY Timestamp DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Max(0, limit));

        var list = new List<AuditEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AuditEntry(
                ParseIso(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }
        return list;
    }

    // ---- 封禁 ----
    public void SaveBan(BanRecord record)
    {
        using var conn = Open();
        Exec(conn, """
            INSERT INTO Bans (PlayerId, IpAddress, Reason, ExpiresAt)
            VALUES ($pid, $ip, $reason, $expires)
            ON CONFLICT(PlayerId) DO UPDATE SET
                IpAddress = excluded.IpAddress,
                Reason    = excluded.Reason,
                ExpiresAt = excluded.ExpiresAt;
            """, cmd =>
        {
            cmd.Parameters.AddWithValue("$pid", record.PlayerId.ToString());
            cmd.Parameters.AddWithValue("$ip", record.IpAddress ?? "");
            cmd.Parameters.AddWithValue("$reason", record.Reason ?? "");
            cmd.Parameters.AddWithValue("$expires", Iso(record.ExpiresAt));
        });
    }

    public bool IsBanned(Guid playerId, string ipAddress)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        var hasIp = !string.IsNullOrEmpty(ipAddress);
        cmd.CommandText = hasIp
            ? "SELECT 1 FROM Bans WHERE ExpiresAt > $now AND (PlayerId = $pid OR IpAddress = $ip) LIMIT 1;"
            : "SELECT 1 FROM Bans WHERE ExpiresAt > $now AND PlayerId = $pid LIMIT 1;";
        cmd.Parameters.AddWithValue("$now", Iso(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("$pid", playerId.ToString());
        if (hasIp) cmd.Parameters.AddWithValue("$ip", ipAddress);
        return cmd.ExecuteScalar() is not null;
    }

    public void RemoveBan(Guid playerId)
    {
        using var conn = Open();
        Exec(conn, "DELETE FROM Bans WHERE PlayerId = $pid;",
            cmd => cmd.Parameters.AddWithValue("$pid", playerId.ToString()));
    }

    // ---- 世界改动（单事务批量 upsert，避免逐条 fsync）----
    public void SaveWorldTiles(IReadOnlyList<WorldTileRecord> tiles)
    {
        if (tiles.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO WorldTiles (WorldId, X, Y, Data) VALUES ($worldId, $x, $y, $data)
            ON CONFLICT(WorldId, X, Y) DO UPDATE SET Data = excluded.Data;
            """;
        var pworld = cmd.Parameters.Add("$worldId", SqliteType.Text);
        var px = cmd.Parameters.Add("$x", SqliteType.Integer);
        var py = cmd.Parameters.Add("$y", SqliteType.Integer);
        var pd = cmd.Parameters.Add("$data", SqliteType.Blob);
        foreach (var t in tiles)
        {
            pworld.Value = t.WorldId;
            px.Value = t.X;
            py.Value = t.Y;
            pd.Value = t.Data;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<WorldTileRecord> LoadWorldTiles()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT WorldId, X, Y, Data FROM WorldTiles;";
        var list = new List<WorldTileRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new WorldTileRecord(r.GetInt32(1), r.GetInt32(2), (byte[])r[3], r.GetString(0)));
        return list;
    }

    // ---- 箱子内容（单事务批量 upsert）----
    public void SaveWorldChests(IReadOnlyList<WorldChestRecord> chests)
    {
        if (chests.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO WorldChests (WorldId, ChestIndex, X, Y, Data) VALUES ($worldId, $i, $x, $y, $data)
            ON CONFLICT(WorldId, ChestIndex) DO UPDATE SET X = excluded.X, Y = excluded.Y, Data = excluded.Data;
            """;
        var pworld = cmd.Parameters.Add("$worldId", SqliteType.Text);
        var pi = cmd.Parameters.Add("$i", SqliteType.Integer);
        var px = cmd.Parameters.Add("$x", SqliteType.Integer);
        var py = cmd.Parameters.Add("$y", SqliteType.Integer);
        var pd = cmd.Parameters.Add("$data", SqliteType.Blob);
        foreach (var c in chests)
        {
            pworld.Value = c.WorldId;
            pi.Value = c.Index;
            px.Value = c.X;
            py.Value = c.Y;
            pd.Value = c.Data;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<WorldChestRecord> LoadWorldChests()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT WorldId, ChestIndex, X, Y, Data FROM WorldChests;";
        var list = new List<WorldChestRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WorldChestRecord(r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), (byte[])r[4], r.GetString(0)));
        return list;
    }

    public void DeleteWorldChests(string worldId, IReadOnlyList<int> chestIndices)
    {
        if (chestIndices.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM WorldChests WHERE WorldId = $worldId AND ChestIndex = $i;";
        var pworld = cmd.Parameters.Add("$worldId", SqliteType.Text);
        var pi = cmd.Parameters.Add("$i", SqliteType.Integer);
        pworld.Value = worldId;
        foreach (var index in chestIndices)
        {
            pi.Value = index;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void SaveWorldTileEntities(IReadOnlyList<WorldTileEntityRecord> entities)
    {
        if (entities.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO WorldTileEntities (WorldId, X, Y, RuntimeId, FileId, Type, Data, IsDeleted)
            VALUES ($worldId, $x, $y, $runtimeId, $fileId, $type, $data, $isDeleted)
            ON CONFLICT(WorldId, X, Y) DO UPDATE SET
                RuntimeId = excluded.RuntimeId, FileId = excluded.FileId, Type = excluded.Type,
                Data = excluded.Data, IsDeleted = excluded.IsDeleted;
            """;
        var pworld = cmd.Parameters.Add("$worldId", SqliteType.Text);
        var px = cmd.Parameters.Add("$x", SqliteType.Integer);
        var py = cmd.Parameters.Add("$y", SqliteType.Integer);
        var pr = cmd.Parameters.Add("$runtimeId", SqliteType.Integer);
        var pf = cmd.Parameters.Add("$fileId", SqliteType.Integer);
        var pt = cmd.Parameters.Add("$type", SqliteType.Integer);
        var pd = cmd.Parameters.Add("$data", SqliteType.Blob);
        var deleted = cmd.Parameters.Add("$isDeleted", SqliteType.Integer);
        foreach (var entity in entities)
        {
            pworld.Value = entity.WorldId;
            px.Value = entity.X; py.Value = entity.Y; pr.Value = entity.RuntimeId; pf.Value = entity.FileId;
            pt.Value = entity.Type; pd.Value = (object?)entity.Data ?? DBNull.Value; deleted.Value = entity.IsDeleted ? 1 : 0;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<WorldTileEntityRecord> LoadWorldTileEntities()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT WorldId, RuntimeId, FileId, Type, X, Y, Data, IsDeleted FROM WorldTileEntities ORDER BY X, Y;";
        var list = new List<WorldTileEntityRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new WorldTileEntityRecord(reader.GetInt32(1), reader.GetInt32(2), (byte)reader.GetInt32(3),
                (short)reader.GetInt32(4), (short)reader.GetInt32(5), reader.IsDBNull(6) ? null : (byte[])reader[6], reader.GetInt32(7) != 0, reader.GetString(0)));
        return list;
    }

    public void SaveWorldProgress(WorldProgressRecord progress)
    {
        using var conn = Open();
        Exec(conn, "INSERT INTO WorldProgress(WorldId, Data) VALUES($w,$d) ON CONFLICT(WorldId) DO UPDATE SET Data=$d;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$w", progress.WorldId);
                cmd.Parameters.AddWithValue("$d", progress.Data);
            });
    }

    public WorldProgressRecord? LoadWorldProgress(string worldId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Data FROM WorldProgress WHERE WorldId = $w;";
        cmd.Parameters.AddWithValue("$w", worldId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new WorldProgressRecord((byte[])reader[0], worldId);
    }

    public void ClearWorldChanges(string worldId)
    {
        using var conn = Open();
        Exec(conn, "DELETE FROM WorldTiles WHERE WorldId = $worldId; DELETE FROM WorldChests WHERE WorldId = $worldId; " +
                   "DELETE FROM WorldTileEntities WHERE WorldId = $worldId; DELETE FROM WorldProgress WHERE WorldId = $worldId;",
            cmd => cmd.Parameters.AddWithValue("$worldId", worldId));
    }

    public void Dispose() { /* 连接池已禁用：连接随 using 释放，无跨连接资源需清理 */ }
}
#endif
