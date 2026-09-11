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
}

// ============================================================================
// 公开入口：根据条件编译选择实现
// ============================================================================
public sealed class SqlitePersistence : IPlayerRepository, IAuditRepository, IDisposable
{
    private readonly IDbExecutor _db;
    private readonly Channel<AuditEntry> _auditChannel = Channel.CreateUnbounded<AuditEntry>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _batchCts = new();
    private readonly Task _batchTask;

    public SqlitePersistence(string dbPath, bool runMigrations = true)
    {
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
        // synchronous / busy_timeout 为连接级设置，每次连接都需施加
        Exec(conn, "PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;");
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

    public void Migrate()
    {
        using var conn = Open();
        // journal_mode 持久化在 DB 头部，仅在建库时设置一次
        Exec(conn, "PRAGMA journal_mode=WAL;");
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

    public void Dispose() { /* 连接池已禁用：连接随 using 释放，无跨连接资源需清理 */ }
}
#endif
