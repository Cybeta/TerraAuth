# TerraAuth — 优化待办（Backlog）

> 记录**尚未实施**的优化 / 补全事项，供后续排期取舍。已实施项见文末「本轮回溯」。
> 最后更新：2026-09-11（第七轮：Boss 掉落 + 液体混合反应）

---

## 一、独立立项

> 暂无。

---

## 二、后续可能优化（暂不实施）

> 以下均为**性能向**优化，收益需实测支撑；未确认瓶颈前不建议实施。

### B-5 原版物理补全（弹幕 / 掉落物）

- **位置**：`Simulation/WorldSimulator.cs`（`SimulateEntities`）
- **现状**：弹幕为直线积分 + 生存期到期；掉落物为重力 + 简单落地；命中判定为「与敌怪中心距离 < 32px」。
- **缺口**：无图格碰撞（弹幕穿墙）、无穿透 / 反弹 / 追踪等 ai 行为、无掉落物拾取动画与合并。
- **说明**：原版对应数据量极大，属**玩法补全**而非性能优化；当前实现已满足「服务端权威判定」目标。

### B-2 出站帧缓冲复用（`Connection.SendEncodedAsync`）

- **位置**：`Net/Phase5/Connection.cs`
- **现状**：每次发送都 `new ArrayBufferWriter<byte>()` 再 `WrittenSpan.ToArray()` → 每帧两次分配；
  快照下发为 20Hz × 在线玩家数。
- **方向**：改用 `ArrayPool<byte>` 租借 + 精确长度写入（或复用单个 writer）。
- **风险（中）**：缓冲经 `Channel` 跨线程移交给写循环，归还时机与生命周期必须严格配对，
  否则易出现「已归还缓冲仍被读」类缺陷。
- **触发条件**：GC / 分配采样显示 `ArrayBufferWriter` 为热点。

### B-3 SnapshotStore 环形缓冲（消除 O(n) 移除）

- **位置**：`Simulation/SnapshotStore.cs`
- **现状**：容量 300；`Add` 超容量时走 `List.RemoveAt(0)`，`TrimBefore` 走 `RemoveRange(0, n)`，均为 O(n) 搬移。
- **量级**：每 tick 约 2.4KB memmove，60Hz ≈ 144KB/s —— **可忽略**，故暂不实施。
- **方向**：以「写入下标 + 逻辑长度」的环形缓冲替代 `List`，把移除降为 O(1)。
- **关联**：`Net/Phase4/README.md` 已记录 `Snapshot()` 每轮 `ToArray()` 复制的问题，可与本项合并评估。

### B-4 区块锁原语评估（`SectionLocks`）

- **位置**：`Simulation/World/SectionLocks.cs`
- **现状**：按 64 条带使用 `ReaderWriterLockSlim`（仿真写 vs 包 10 编码 / 权威校验读，读写分离）。
- **结论**：.NET 10 框架**未提供**更优的读写锁替代（`System.Threading.Lock` 仅适用于互斥场景），故**保持现状**。
- **触发条件**：若实测条带竞争成为瓶颈，再评估条带数调优或按 section 细化粒度。

---

## 附：本轮回溯

### 第七轮（2026-09-11）：Boss 掉落 + 液体混合反应（阻塞项解除）

**问题**：第六轮遗留两个「因缺原版数值 ID 而不敢写」的阻塞项 —— Boss 掉落表与液体混合反应。

**核对**：按原版客户端（协议 326）的字段与数值逐项核对（仅记录结论，不含第三方资料）：
NPC 类型 ID（眼魔 4 / 世界吞噬者 13·14·15 / 骷髅王 35 / 史莱姆王 50 / 小丑 109 / 血肉墙 113 / 双子 125·126 /
机械骷髅王 127 / 毁灭者 134 / 蜂后 222 / 石巨人 245 / 世纪之花 262 / 猪龙鱼公爵 370 / 月亮领主 398 / 邪教徒 439）、
物品 ID（凝胶 23 / 恶魔矿 56 / 蜂蜡 2431）、图格 ID（黑曜石 56 / 蜂蜜块 229 / 松脆蜂蜜块 230 / 微光块 659）、
混合阈值 24 单位 —— 全部与实现一致。

**已实施**：

- **Boss 击杀进度 + 掉落**：`WorldState.NotifyNpcKilled` 扩展为**已核对**的进度位映射（修掉两处旧错：126 原误记为蜂后→实为双子、113 原误记为石巨人→实为血肉墙）；
  新增 `BossLoot` 表（眼魔 / 世界吞噬者 → 恶魔矿，史莱姆王 → 凝胶，蜂后 → 蜂蜡）与 `DropBossLoot`；
  `WorldItemEntity.NewNotified` 区分「客户端上报生成」与「服务端主动生成」，后者由 `GameHost.FlushNewItemsAsync` 按视口补发包 21。
- **液体混合反应**：`WorldSimulator.TryLiquidMerge` —— 本格液体与相邻异种液体累计 ≥ 24 单位时消耗异种液体并生成混合图格
  （水 + 岩浆 → 黑曜石、水 + 蜂蜜 → 蜂蜜块、岩浆 + 蜂蜜 → 松脆蜂蜜块、微光 + 任意 → 微光块），随后经 `MarkLiquidChanged` / `MarkTileChanged` 下发。
- **测试**：+3（**231 通过**）——`Vanilla_BossKill_Drops_Loot_And_Pushes_Packet21`、`Vanilla_Progress_Uses_Verified_BossIds`（回归旧错映射）、`Vanilla_LiquidMerge_Water_Plus_Lava_Creates_Obsidian`。
  默认后端与 `-p:NoSqlite=true` 兜底后端均 231/231 通过。

**仍为简化模型**：Boss 掉落仅收录 4 项（原版掉落规则在掉落数据库中，不逐条复刻）；液体内压 / 压力模型未实现；混合仅在本格为空时生成（原版还允许覆盖可被黑曜石破坏的图格）。

### 第六轮（2026-09-11）：健壮性兜底 + 服务端伤害来源 + 对抗测试自动化

**已实施**：

- **区块超帧上限兜底**：`NetworkHost.SendTileSectionAsync` 在编码超过 `UInt16`（65535）时按较长轴二分拆分再发（递归），
  修掉「高熵区块直接抛 `Frame too large` 中断登录 / 出生点下载」的既有隐患（入口：包 8 → `HandleSpawnTileDataAsync`）。
- **配置量纲校验**：`MaxSingleDamage > 32767`（Int16 线格式上限）启动即拒绝并提示，避免「阈值永不触发」的静默失效。
- **权威层兜底防御**：`ConnectionStateStage` 关闭 TODO —— 对 `PlayerId <= 0` 的无身份上下文静默丢弃（网络层保证仅 Playing 入管线）。
- **服务端伤害来源（接触）**：`SimulateCombat` 新增敌怪 / Boss 接触伤害（32px 判定 + 60tick 免伤帧 + 简化伤害表），
  统一经 `ApplyPlayerDamage` 结算；受击经包 117（广播）+ 包 16（单发权威血量）下发 —— 此前下落伤害只改服务端血量、客户端毫不知情。
- **基础设施补全**：`IMetrics.SetGauge`（插件自定义仪表盘，含标签）、`IAuditRepository.QueryRecentAsync` 双后端实现、
  `PlayerIdentity.ToPlayerId`（Guid → 槽位，与 `ToGuid` 互逆）、`CoreEventStore.QueryAsync` 接持久化审计查询。
- **命令子系统**：新增 `Authority/CommandService.cs`（注册 / 解析 / 分发，命令名不区分大小写、处理器异常转失败结果），
  组合根注册内置 `say` / `who` / `kick` / `help`，`IServerApi.ExecuteCommand` 由「仅落审计」改为真实分发。
- **Phase 7 对抗清单自动化**：`VanillaFeatureTests.AntiCheat_*` 覆盖 P1（DPS 窗口）/ P3（非法堆叠、箱内未知物品）/
  P4（无身份包丢弃、恶意包重放不推进权威）/ P5+M6（洪水限流 → 违规累计踢出）以及高熵区块拆分下的登录完整性。
- **测试**：+14（228 通过）。

**未实施（阻塞项，不猜数值）**：~~Boss 掉落 / 更多 Boss 与事件~~、~~液体混合反应~~ —— 阻塞已解除，见第七轮。

### 第五轮（2026-09-11）：遗留项闭环 —— 执行器逐格下发 + 液体视口裁剪

**问题**（第四轮遗留，见其回溯）：服务端电路翻转执行器只落在服务端图格、未推送客户端；液体同步把变化**全量广播**给所有玩家。

**已实施**：

- **执行器翻转逐格下发**：`WorldState` 新增待推送图格集合（去重 + 上限 8192）与 `MarkTileChanged`；`ActuateCommand` 每翻转一格即入队；
  `GameHost.FlushTileUpdatesAsync` 按行把待推送图格合并为「宽 × 1」矩形，以**包 10 小矩形**推送**视口内**玩家（256 格 / 64 矩形每批，超限重新排队不丢更新），随快照频率（20Hz）下发。
- **液体同步视口裁剪**：`GameHost.FlushLiquidAsync` 改为按玩家裁剪——每玩家只收到其视野半径内的液体变更（`NetworkHost.BroadcastPerPlayerAsync` 新增「按玩家定制内容」的广播通道），无变更的玩家不下发。
- **测试**：+2（214 通过）——`Vanilla_Actuate_Pushes_Tile_Update_To_Client`（客户端收到包 10 小矩形）、`Vanilla_Liquid_Sync_Is_Viewport_Culled`（视口外玩家收不到液体同步）。

**遗留**（见 §二与 `VANILLA_COVERAGE.md` §三）：图格推送用包 10 小矩形而非原版包 20（需实测校对）；液体裁剪为「每玩家全量过滤」（O(玩家 × 变更)）。

### 第四轮（2026-09-11）：世界内容权威 —— 箱子 / 液体 / 电路 / Boss·事件

**问题**：上一轮结束后剩余三块「未实现」：箱子内容仅中继（服务端不持有）、液体 / 电路完全未实现、Boss / 事件只有静态进度位。
另发现包 17 的 action 语义映射有误（把 5..11 当作「清线网 / 斜坡 / 执行器」，实际是 5=PlaceWire…19=Actuate），会误改世界。

**已实施**（口径：简化模型但服务端权威）：

- **箱子内容服务端持有**：`WorldState.ChestsLock` + `SyncChestItemCommand`；包 31 校验箱子存在 / 距离后，由网络层下发包 34（当前箱子索引）+ 逐槽包 32（权威内容）；包 32 校验箱子 / 槽位 / 堆叠 / 物品 / 距离后落盘（服务端唯一真相）。
- **液体（NetLiquid，包 82 模块 0）**：`LiquidModulePacket` 编解码（UInt16 模块号 + UInt16 条目数 + 6 字节条目）；客户端编辑经 `ValidateLiquid`（越界 / 类型 / 距离 / 条目数）后由 `LiquidEditCommand` 落盘；仿真新增脏格驱动的简化流动（下落优先、受阻后侧向均衡，限 2000 格/tick）；按快照频率批量下发（限 512 条/批）。包 82 的限流按模块拆分（聊天 / 液体各自令牌桶，液体不再占用聊天额度）。
- **电路**：修正包 17 action 0..19 映射（方块 / 墙 / 4 色线网 / 执行器放置与拆除）；新增 `ActuateCommand`（action 19）——沿电线受限 BFS（≤2000 格）翻转连通执行器的 `InActive`。
- **Boss / 事件**：`WorldState.ProgressDirty` + `NotifyNpcKilled`（Boss 击杀记进度）；`WorldSimulator` 新增 `StartBloodMoon` / `StartEclipse` / `StartInvasion` / `SpawnBoss`；入侵按配额刷哥布林，耗尽即结束；Boss（眼魔 / 骷髅王 / 史莱姆王）直线追击；昼夜切换按概率触发血月 / 日食；进度变化由世界同步重新下发包 7。
- **测试**：+26（212 通过）——箱子开箱内容 / 权威落盘 / 非法槽位 / 超距 / 不存在；液体编解码回归 + 落盘流动 / 同步下发 / 超距 / 非法类型；线网与执行器编辑 / 信号传播 / 无线网不传播；血月广播 / 昼夜切换 / Boss 刷新与击杀进度 / 入侵起止 / 日食。

**遗留**（见 §二 B-6 / B-7 与 `VANILLA_COVERAGE.md` §三）：液体同步未按视口裁剪；执行器翻转未逐格下发；液体无压力模型与混合反应；电路无门电路 / 定时器 / 压力板；Boss AI 与事件触发规则为简化模型。

### 第三轮（2026-09-11）：战斗 / 生存权威补全 + Mod 策略配置化 + `.wld` 解析修复

**问题**：

1. 受伤 / 死亡 / 复活全链路仅中继（117 / 118），无服务端结算与复活；下落伤害致死还会把玩家置为 `Active=false`（离线态）导致永远冻结。
2. 掉落物拾取（包 22）与弹幕命中判定缺失：客户端可远程拾取 / 重复拾取，弹幕命中完全由客户端声明。
3. `ModPolicy` 未从 `server.json` 读取，`TModLoaderCompat` 未装配，`ParseModList` / `ForwardModPacketAsync` 为 TODO。
4. `.wld` 解析器从未有测试覆盖 —— 补充测试后发现**真实缺陷**。

**已实施**：

- **战斗 / 生存权威**：新增 `DamagePlayerCommand` / `KillPlayerCommand` / `RespawnCommand`；`PlayerHurtV2`（包 117）非负校验后结算服务端生命，归零置死亡态；`PlayerDeathV2`（118）结算死亡；Playing 阶段的包 12 视为复活请求，**复活点由服务端固定为世界出生点**（忽略客户端坐标）；死亡 / 复活由世界同步补发包 118 / 12+16。`PlayerRuntime` 拆出 `Dead`（死亡态）与 `Active`（在线），死亡不再冻结在线状态。
- **掉落物拾取（包 22）**：`ItemPickupPacket` + 解码 / 编码；`WorldAuthority.ValidatePickup` 做槽位对账（真实存活实体）+ 拾取半径（64px）+ SSC 入库（`IInventoryAuthority.TryAddItem`），成功后 `PickupItemCommand` 移除世界实体并由世界同步下发包 21（stack=0）。
- **弹幕命中判定**：仿真层按弹幕 / 敌怪中心距离（32px）判定命中并扣血，不再采信客户端。
- **Mod 策略配置化**：`ServerConfig` 新增 `ModPolicy` 节（枚举以字符串读写）；`GameHost.Bootstrap` 从配置构造 `ModDetector` 并装配 `TModLoaderCompat`（Mod 名称清单解析 + 250-255 自定义包转发，转发通道绑定 `NetworkHost.SendRawAsync`）。
- **`.wld` 解析修复**：定位并修复 `LoadWorldTiles` 中 `int run = (b4 & 0xC0) >> 6 switch { ... }` 的**运算符优先级缺陷**——`switch` 实际约束 `6`，导致每个图格无条件执行 `_ => reader.ReadInt16()` 多读 2 字节，**任何 `.wld` 都无法解析**；改为显式括号后恢复正常。
- **测试**：+20（192 通过）——受伤 / 致死 / 负数伤害、死亡广播、复活点权威、拾取成功 / 超距拒绝、弹幕命中、包 73 接受与限频、ModPolicy 配置反序列化与组合根注入、TModLoader 握手 / 截断容忍 / 转发通道、`.wld` 最小合法世界 + 版本 / 魔数 / footer 拒绝路径。

### 第二轮（2026-09-11）：真实 SQLite 持久化落地

**问题**：`SqliteImpl` 全为空方法体（`GetPlayer` 返回 `null`、`SavePlayer`/`CreatePlayer`/`AppendAudit` 空实现、
`QueryAudit` 返回空数组），且全仓从未定义 `USE_SQLITE` → 实际恒走 `LiteDbPersistence`；
而它只序列化 `Players` → **封禁与审计重启即丢**（`Microsoft.Data.Sqlite` 被引用却从未生效）。

**已实施**：

- `TerraAuth.csproj` 新增 `DefineConstants=USE_SQLITE`（条件 `'$(NoSqlite)' != 'true'`），与 SQLite 包引用同开关。
- `Persistence/SqlitePersistence.cs` 实装 `SqliteImpl`：`Players` / `AuditLogs` / `Bans` 三表 + 索引，
  UPSERT、单事务批量审计插入、封禁过期判定（时间戳统一 ISO-8601 UTC 文本，字符串比较即时间比较）。
  连接策略 `Pooling=false`：句柄随 `using` 立即释放，避免 Windows 下 DB 文件被占用（备份 / 迁移 / 测试清理）。
- `IDbExecutor.AppendAudit(AuditEntry)` → `AppendAuditBatch(IReadOnlyList<AuditEntry>)`：审计按批单事务落盘
  （原先注释声称「事务包裹」但实际逐条写）。
- 停机时把审计通道残留条目一并落盘（原先 `finally` 只 flush 已取出的 buffer，通道内条目会丢）。
- `LiteDbPersistence` 兜底路径同步补齐：玩家 / 审计 / 封禁三类数据均落盘。
- 测试 +3（141 通过）：玩家+审计重启读回、封禁重启读回、过期封禁不生效；
  且两种后端（默认 SQLite 与 `-p:NoSqlite=true`）均全绿。

### 第一轮（2026-09-11）：.NET 8 → .NET 10 升级 + Hook 热路径优化

**已实施**：

- 目标框架 → `net10.0`（3 个工程）；`Microsoft.Data.Sqlite` → 10.0.12；移除 `System.IO.Pipelines`
  显式引用（.NET 10 起已内置于共享框架）。
- 锁原语：`object` / Monitor → `System.Threading.Lock`（11 处）。
- `Simulation/World/TileIdSets.cs` 静态查找集 → `FrozenSet<ushort>`。
- `Plugins/HookRegistry.cs` 触发路径：改为**写时复制不可变快照数组**，消除每包一次 `List` 分配与取锁。
- `Plugins/HookIntegration.cs`：该类 Hook 无订阅者时**不再构造 `HookArgs`**（无插件场景每包零分配）。

**已评估但未采纳**：

- 日志 `params object[]` 装箱改造：实测 `Debug/Info/Warn/Error` 仅出现在 Deny / 插件加载 / Mod 包拦截等
  低频路径，**不在每包稳态路径**，装箱开销可忽略；且 `ILogger` 属插件公共 API，改造有兼容成本，故不做。
  - 若后续需要可运维性改进，可单独增加日志级别开关（当前 `CoreLogger.Debug` 无条件写 `Console`）。
- `JsonSerializerContext` 源生成：调用点均为低频（配置变更 / 审计批量落盘）；
  唯一较高频点 `PersistenceAuditLogger.Log` 序列化的是**匿名类型** `details`，源生成无法覆盖，
  需先定义具体 DTO 才有意义。
