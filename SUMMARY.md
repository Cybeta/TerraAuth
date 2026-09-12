# TerraAuth 交付汇总

> **单一汇总文件**：本文把本轮全部交付事项（功能 / 修复 / 测试 / 文档）与完整变更文件清单合并为一处，
> 作为本次推送的对照说明。更细的模块状态见 [`terraauth/README.md`](terraauth/README.md)，
> 原版功能覆盖见 [`terraauth/VANILLA_COVERAGE.md`](terraauth/VANILLA_COVERAGE.md)，
> 逐轮回溯见 [`terraauth/OPTIMIZATION_BACKLOG.md`](terraauth/OPTIMIZATION_BACKLOG.md)。
>
> 生成日期：2026-09-12 ｜ 当前测试：**257 用例**（默认 SQLite 后端与 `-p:NoSqlite=true` 兜底后端均全绿）

---

## 一、项目定位

TerraAuth 是 **Terraria 协议（协议 326）的服务端权威代理 / 反作弊框架**。

原版 Terraria 为纯客户端权威架构——位置、血量、伤害全部由客户端上报，服务端基本只做转发，
内存修改工具可直接篡改坐标 / 血量 / 伤害且几乎无拦截。TerraAuth 的目标是在**协议层**把关键状态收回服务端：
**客户端只能声明意图，所有状态变更必须经过服务端权威校验后才生效**。

- 技术栈：C# / .NET 10（`net10.0`）
- 解决方案与源码：`terraauth/terraauth/`（`TerraAuth.sln` 与 `TerraAuth.csproj` 同目录）
- 已实测：原版 Terraria 客户端（协议 326）可完成 握手 → 进入世界 → 正常断开，双客户端互相可见

---

## 二、交付汇总（按主题）

### 1. 战斗 / 生存权威（服务端结算）

- **受伤 / 死亡 / 复活全链路收归服务端**：包 117 非负校验 → `DamagePlayerCommand` 结算服务端生命；
  归零置死亡态 → 世界同步补发包 118；Playing 阶段包 12 视为复活请求，**复活点由服务端固定为世界出生点**（忽略客户端坐标）。
  拆分 `Dead`（死亡态）与 `Active`（在线），修掉「下落伤害致死把玩家置为离线态导致永久冻结」的缺陷。
- **服务端伤害来源（接触）**：仿真新增敌怪 / Boss 接触伤害判定（32px + 60tick 免伤帧 + 简化伤害表），
  统一经同一入口结算；下发包 117（表现，广播）+ 包 16（单发权威血量）——此前下落伤害只改服务端血量、客户端毫不知情。
- **掉落物拾取（包 22）**：服务端做槽位对账（真实存活实体）+ 拾取半径校验 + 服务端背包入库（SSC），
  成功后移除世界实体并下发包 21（stack=0）；拒绝远程拾取 / 重复拾取。
- **弹幕命中判定（包 27）**：仿真按弹幕 / 敌怪距离判定命中并扣血，不再采信客户端声明。
- **法力（包 42）**：服务端跟踪法力 / 上限；非负校验，上限由服务端持有（超出即下发权威纠正包）；原版不向他人转发。
- **治疗（包 35）**：非负校验 → 回血上限钳制到服务端 `HpMax`（客户端超额治疗不会让服务端生命越界）。
- **增益（包 50）**：条目数 ≤ 44、ID ∈ [1,400] 校验通过后，由服务端持有列表（唯一真相）。
- **弹幕生成（包 27）**：类型须在 [1,1135]，伤害超单次上限即判为作弊拒绝（与包 28 共用阈值）——关闭「声明 32767 一击秒杀」通道。

### 2. 世界内容权威

- **箱子内容服务端持有（包 31 / 32 / 34）**：开箱校验（存在 / 距离）后由服务端逐槽下发权威内容；
  包 32 校验箱子 / 槽位 / 堆叠 / 物品 / 距离后落盘——服务端是唯一真相。
- **液体（包 82 模块 0，NetLiquid）**：客户端液体编辑经校验（越界 / 类型 / 距离 / 条目数）后由 `LiquidEditCommand` 落盘；
  仿真为脏格驱动的简化流动（下落优先、受阻后侧向均衡），下发按**视口裁剪**（每玩家只收到视野内的变更）。
- **液体混合反应**：异种液体相邻且累计 ≥ 24 单位时消耗异种液体并生成混合图格
  （水 + 岩浆 → 黑曜石、水 + 蜂蜜 → 蜂蜜块、岩浆 + 蜂蜜 → 松脆蜂蜜块、微光 + 任意 → 微光块），
  随后经图格 / 液体变更通道下发。
- **电路（包 17）**：修正 action 0..19 语义映射（方块 / 墙 / 4 色线网 / 执行器放置与拆除）；
  action 19 触发服务端沿电线受限 BFS（≤2000 格）翻转连通执行器；翻转结果按行合并为「宽 × 1」小矩形逐格下发视口内玩家。

### 3. Boss / 事件（简化模型 + 进度权威）

- **进度位按已核对 ID 映射**：击杀眼魔 / 世界吞噬者 / 骷髅王 / 史莱姆王 / 蜂后 / 石巨人 / 世纪之花 /
  猪龙鱼公爵 / 邪教徒 / 月亮领主 / 三机械 Boss / 小丑 → 置对应世界进度位，进度变化重新下发包 7。
- **Boss 掉落**：按已核对掉落表生成服务端掉落物（眼魔 / 世界吞噬者 → 恶魔矿、史莱姆王 → 凝胶、蜂后 → 蜂蜡），
  由世界同步按视口补发包 21。
- **事件状态机**：血月 / 日食按昼夜概率触发；入侵按配额刷怪、耗尽即结束；Boss 为简化直线追击 AI。

### 4. 健壮性兜底

- **区块超帧上限**：包 10 编码超过 `UInt16`（65535）时按较长轴二分拆分再发（递归），
  修掉高熵区块直接抛异常中断登录 / 出生点下载的隐患。
- **配置量纲校验**：`MaxSingleDamage > 32767`（Int16 线格式上限）启动即拒绝并提示，避免阈值静默失效。
- **权威层兜底**：`ConnectionStateStage` 对 `PlayerId <= 0` 的无身份上下文显式丢弃（网络层保证仅 Playing 入管线）。

### 5. 基础设施补全

- `IMetrics.SetGauge`（插件自定义仪表盘，含标签）；`/metrics` 导出同步支持。
- `IAuditRepository.QueryRecentAsync` 双后端实现；`CoreEventStore.QueryAsync` 接持久化审计查询（读审计可用）。
- `PlayerIdentity.ToPlayerId`（Guid → 连接槽位，与 `ToGuid` 互逆）。
- **命令子系统**：`Authority/CommandService.cs`（注册 / 解析 / 分发，命令名不区分大小写、处理器异常转失败结果），
  内置 `say` / `who` / `kick` / `help`；`IServerApi.ExecuteCommand` 由「仅落审计」改为真实分发。

### 6. Mod 兼容

- `ModPolicy` 从 `server.json` 读取（枚举以字符串读写）；`TModLoaderCompat` 装配：
  Mod 名称清单解析 + 自定义包 250-255 转发（绑定网络层单播）。

### 7. 测试与对抗自动化

- 套件 **257 用例**，默认后端与 `-p:NoSqlite=true` 兜底后端均全绿。
- `VanillaFeatureTests` 以**真实权威管线 + 真实 TCP** 逐项验证原版功能；
  `AntiCheat_*` 覆盖 Phase 7 可自动化部分：DPS 窗口、非法堆叠 / 箱内未知物品、无身份包丢弃、
  恶意包重放不推进权威、洪水限流 → 违规累计踢出、高熵区块拆分下的登录完整性。

### 8. 持久化与可靠性

- **世界改动持久化**：玩家挖 / 放方块、墙、液体、电线、执行器的改动按 1Hz **增量落盘**（`WorldTiles`），
  启动时叠加回确定性基准世界 —— **玩家建筑在服务端重启后不再丢失**。落盘失败会重新排队重试，停机时冲刷一次。
- **箱子内容持久化**：真实 `.wld` 世界自带的箱子被开箱取放（包 32）后，内容按 1Hz 增量落盘（`WorldChests`），
  重启时按「索引 + 坐标」双校验回放 —— **箱子内容在服务端重启后不再丢失**（W-1 落地后解除的阻塞项）。
- **既有持久化**：玩家存档 / 审计 / 封禁（SQLite 或 `-p:NoSqlite=true` 的内嵌兜底后端）。
- **偶发测试加固**：客户端会话读取补 `IOException` / `SocketException` 处理，消除「踢出瞬间仍在读 socket」导致的偶发失败。
- **世界文件（`.wld`）加载 / 导出**：`ServerConfig.WorldPath` 指定 `.wld` 即**用真实世界开服**；
  未指定时按 `ServerConfig.WorldSize` **程序化生成三档尺寸**（`Small` 4200×1200 / `Medium` 6400×1800 / `Large` 8400×2400，与原版三档一致）；
  自研写出器（与自身读取器布局严格对称，覆盖全部图格特征位）配合 `WorldExportPath`：**停机导出 + 空服周期导出**，
  带**写后读回校验 + 原子替换 + `.bak` 滚动**，不会用读不回来的文件覆盖已有世界。
- **增量落盘加固**：单批上限 2048 → 8192；待落盘集合加上限（溢出降级为「全图游标扫描」，内存有界且最终一致）；
  停机时循环冲刷到排空。

### 9. 断线会话保留与连接健壮性

- **断线宽限期会话保留**：连接断开不再直接销毁玩家运行时 —— 运行时按**玩家名**保留位置 / 血量 / 增益，
  宽限期（`SessionResumeGraceSeconds`，默认 60s）内**同身份重连认回原运行时**；出生包（12）改发**恢复后的坐标**，
  客户端落回原位；超期或被顶号由仿真循环回收。
- **连接槽位回收（修真实缺陷）**：此前正常断开的连接**永不从连接池移除**，槽位与并发容量持续泄漏（累计 64 次连接后新玩家被拒）。
  现在连接结束时释放槽位，新连接复用**最小空闲 ID**（与原版一致）；移除按「键 + 实例」双匹配，避免误杀复用同槽位的新连接。
- **移动基线清理**：断线时经全部管线（含分片）清掉该槽位的移动校验基线（投递到同一分片 / 队列，保证在途包先处理完），
  否则槽位复用会把上次会话的位置当作基准，重连玩家首个位置包被判超速。
- **明确限制**：原版客户端**断线即回主菜单**，故这是「手动重进的会话接管」而非自动重连。

### 10. 原版可玩性核对与修复（三项硬伤）

以「原版客户端能否正常游玩」为线索做了一次**代码级核对**，修掉三项此前文档未列的硬伤：

- **出生点以外没有地形**（最严重）：包 8（区块请求）原先只在握手期处理，进入游戏后请求被丢弃。
  现在 **Playing 阶段也处理**，并由快照循环按玩家位置流送周边区块（跨区块才补发、已发过的区块不重复编码）——
  玩家走出出生点后远处地形正常出现。
- **正常坠落会被判超速并被踢**：移动权威原先只用水平上限（8 px/帧）判定，而原版下落终速约 20 px/帧，
  必然超标 → 服务端位置停在原处（后续挖 / 放 / 开箱全部 out_of_reach）+ 违规累计到 10 次即踢。
  现改为**分轴判定**：水平用 `MaxSpeed`，垂直用 `max(MaxSpeed, MaxFallSpeed)`（`MaxFallSpeed` 由配置注入）。
- **未建模的客户端包被静默丢弃**：`RelayToOthersAsync` 无 default 分支，表情 / 告示牌 / 家具 / NetModule 其他模块等
  「他人可见性」全部丢失。现在**未建模包默认中继**（握手 / 世界请求 / 自身属性 / 服务端自持等例外不中继）。

**随后按原版读写两侧核对，把三项「待确认」落实到位**：

- **图格改动改用包 20（TileSquare）**：原版对少量图格改动走包 20（未压缩小矩形 + 逐格位标志/可选段），
  只有区块级地形下载才走包 10。现在执行器翻转 / 液体混合等走**包 20**（矩形宽度超 255 自动切分），区块流送仍走包 10。
- **区块流送参数对齐原版**：流送矩形改为 **3×3**（以玩家所在区块为中心），下发前先发**包 9（进度）**，
  并沿用**逐连接去重**（原版同样按客户端记录已发区块）。
- **NPC 同步细节**：核对客户端读取侧 —— **省略 ai 与发送 ai=0 等价**（无需补发）；
  补齐**同步锚点**（史莱姆王：位置 + 体型×锚点，修正客户端画偏）；目标字段由 `0` 改为 **255（显式无目标）**。

### 11. 文档同步

根 `README.md`、`terraauth/README.md`、`PROJECT_STRUCTURE.md`、`VANILLA_COVERAGE.md`、
`OPTIMIZATION_BACKLOG.md`、`Phase6/Phase7` 与 `ModCompat` README 均已按上述实现同步更新。

---

## 三、变更文件清单

### 上一轮推送（`3eddcf6`）

共 **41 个文件**（38 修改 + 3 新增），约 +4056 / −185 行。

### 新增

| 文件 | 说明 |
|---|---|
| `SUMMARY.md` | 本文（交付汇总，单一合并文件） |
| `terraauth/Authority/CommandService.cs` | 服务器命令子系统（注册 / 解析 / 分发） |
| `terraauth/Tests/WorldFileTests.cs` | `.wld` 解析测试（最小合法世界 + 版本 / 魔数 / footer 拒绝路径） |

### 源码（`terraauth/`）

| 文件 | 行数 | 说明 |
|---|---|---|
| `Authority/AuthoritySubsystems.cs` | +254 | 权威子系统补全（拾取 / 箱子 / 电路 / 液体校验等） |
| `Authority/IAuthorityLayer.cs` | +7 | 权威层接口扩展 |
| `Authority/InboundPipeline.cs` | +29 | 管线阶段（含 ConnectionStateStage 显式防御） |
| `Config/ConfigurationService.cs` | +18 | 配置量纲校验（Int16 上限） |
| `Config/ServerConfig.cs` | +10 | `ModPolicy` 配置节 |
| `CoreAdapter.cs` | +67 | `ExecuteCommand` 分发、`QueryAsync` 接审计查询 |
| `GameHost.cs` | +293 | 世界同步补发（图格 / 液体 / 受伤 / 新掉落物）、开箱内容下发 |
| `ModCompat/ModPolicy.cs` | +78 | Mod 策略配置化 |
| `Monitoring/IMetrics.cs` | +2 | `SetGauge` |
| `Monitoring/PrometheusMetrics.cs` | +13 | Gauge 导出 |
| `Net/Phase5/NetworkHost.cs` | +119 | 包 10 超帧二分拆分、按玩家定制广播、单播 / 原始包发送 |
| `Net/Phase5/PacketDecoder.cs` | +46 | 新增入站包解析（拾取 / 箱子 / 伤害 / 死亡 / 传送 / 包 82 模块） |
| `Net/Phase5/PacketEncoder.cs` | +26 | 新增出站包编码 |
| `Persistence/IPersistence.cs` | +2 | 审计近期查询接口 |
| `Persistence/SqlitePersistence.cs` | +37 | `QueryRecentAsync` 实现 |
| `Protocol/PacketId.cs` | +2 | 新增包常量 |
| `Protocol/Types.cs` | +38 | 新增包契约 / 领域类型 |
| `Security/PlayerIdentity.cs` | +8 | `ToPlayerId`（与 `ToGuid` 互逆） |
| `Simulation/CommandQueue.cs` | +312 | 拾取 / 箱子 / 液体 / 电路 / 伤害 / 复活等命令 + `Apply` 语义 |
| `Simulation/World/WorldFileReader.cs` | +2 | `.wld` 解析运算符优先级缺陷修复 |
| `Simulation/World/WorldState.cs` | +254 | 进度位 / 掉落表 / 图格与液体待推送集 / 箱子锁 |
| `Simulation/WorldEntities.cs` | +9 | 掉落物 `NewNotified`（区分客户端上报与服务端生成） |
| `Simulation/WorldSimulator.cs` | +534 | 液体流动与混合、电路 BFS、接触伤害、Boss / 事件、实体物理 |

### 测试（`terraauth/Tests/`）

| 文件 | 行数 | 说明 |
|---|---|---|
| `VanillaFeatureTests.cs` | +1029 | 原版功能端到端 + Phase 7 对抗自动化 |
| `PluginModTests.cs` | +182 | 插件 / Mod 兼容（含 TModLoader 转发通道） |
| `IntegrationTests.cs` | +71 | 集成与去重 / 校验 |
| `NetworkTests.cs` | +28 | 网络编解码回归 |
| `TerraAuth.Tests.csproj` | +1 | 新增测试文件纳入编译 |

### 文档 / 配置

| 文件 | 行数 | 说明 |
|---|---|---|
| `README.md`（仓库根） | +11 | 阶段状态表 + 已实测能力 |
| `terraauth/README.md` | +28 | 模块状态表 + 当前状态摘要 |
| `terraauth/PROJECT_STRUCTURE.md` | +32 | 结构 / 模块 / 测试说明 |
| `terraauth/VANILLA_COVERAGE.md` | +52 | 覆盖矩阵 + 已知隐患 |
| `terraauth/OPTIMIZATION_BACKLOG.md` | +99 | 第五~七轮回溯 + 阻塞项解除 |
| `terraauth/ModCompat/README.md` | +15 | Mod 兼容层说明 |
| `terraauth/Phase6-Infrastructure/README.md` | +2 | 基础设施说明同步 |
| `terraauth/Phase7-RedTeam/README.md` | +19 | 对抗清单自动化落地说明 |
| `terraauth/server.json` | +8 | `ModPolicy` 默认配置 |
| `.gitignore` | — | 移除本机绝对路径与来源描述，守卫规则保留 |

### 本轮（第八~十一轮，待推送）

| 文件 | 说明 |
|---|---|
| `terraauth/Protocol/PacketId.cs` | 新增 `PlayerMana = 42` |
| `terraauth/Protocol/Types.cs` | 新增 `PlayerManaPacket` |
| `terraauth/Simulation/World/WorldState.cs` | `PlayerRuntime` 新增 `Mp` / `MpMax` / `Buffs` |
| `terraauth/Simulation/CommandQueue.cs` | `SetManaCommand` / `HealPlayerCommand` / `SetBuffsCommand` |
| `terraauth/Authority/AuthoritySubsystems.cs` | `ValidateMana` / `ValidateHeal` / `ValidateBuffs` / `ValidateProjectile`（含类型 / 伤害上界） |
| `terraauth/Authority/InboundPipeline.cs` | 包 35 / 42 / 50 → Command 映射 |
| `terraauth/Net/Phase5/PacketDecoder.cs`、`PacketEncoder.cs` | 包 42 编解码 |
| `terraauth/Net/Phase5/ITerrariaProtocol.cs` | 权威白名单新增 35 / 42 / 50 |
| `terraauth/CoreAdapter.cs` | 玩家快照回报真实 `Mp` / `MaxMp`（原恒为 0） |
| `terraauth/Simulation/World/Tile.cs` | 图格 15 字节定长序列化（`Serialize` / `Deserialize`） |
| `terraauth/Simulation/World/WorldState.cs` | 待落盘图格集合 + `MarkPersistTile` / `DrainPersistTiles` |
| `terraauth/Simulation/CommandQueue.cs` | 挖砖 / 放砖命令登记持久化 |
| `terraauth/Persistence/IPersistence.cs` | 新增 `IWorldRepository` + `WorldTileRecord` |
| `terraauth/Persistence/SqlitePersistence.cs` | `WorldTiles` 表（SQLite）与内嵌后端落盘 / 读取 |
| `terraauth/GameHost.cs` | 启动回放 + 1Hz 落盘 + 停机冲刷（失败重新排队）；`LoadBaseWorld` 按 `ServerConfig.WorldSize` 程序化生成；快照循环驱动区块流送 |
| `terraauth/Simulation/World/WorldFileWriter.cs` | 新增：`.wld` 写出器（写后读回校验 + 原子替换 + `.bak`） |
| `terraauth/Config/ServerConfig.cs` | 新增 `WorldPath` / `WorldSize` / `WorldExportPath` / `WorldExportIntervalSeconds` / `SessionResumeGraceSeconds` |
| `terraauth/Simulation/World/WorldState.cs` | 待落盘集合上限 + 溢出降级全图扫描 + `HasPendingPersist`；离线会话表 + `MarkPlayerOffline` / `TryResumePlayer` / `ReapOfflineSessions`；`PlayerRuntime` 新增 `ResumeKey` / `Resumed` |
| `terraauth/Tests/WorldFileTests.cs` | +3 用例（特征世界逐格 round-trip / 世界旗标 round-trip / 按原版顺序的严格分段走查） |
| `terraauth/Tests/VanillaFeatureTests.cs` | +18 用例（法力 / 治疗 / 增益 / 弹幕生成 / 世界改动重启回放 / 世界文件加载 / 世界导出 / 世界尺寸配置（中世界生成）/ 会话恢复（位置·血量续回、宽限期 0 关闭、越期回收）/ 箱子内容重启存活 / 区块流送 / 未建模包中继）；偶发用例等待窗口 5s → 15s |
| `terraauth/Simulation/World/WorldGenerator.cs` | 新增 `WorldSize`（Small/Medium/Large）与 `Generate(size)`；地表 / 岩层按高度比例缩放 |
| `terraauth/Tests/SimulationTests.cs` | +3 用例（三档世界尺寸生成：尺寸 / 区块数 / Int16 范围 / 出生点贴地 / 包 7 字段） |
| `terraauth/Simulation/World/WorldState.cs` | 箱子待落盘集合（`MarkPersistChest` / `DrainPersistChests`）+ `Chest.SerializeItems` / `DeserializeItems` |
| `terraauth/Simulation/CommandQueue.cs` | `SyncChestItemCommand.Apply`（包 32）写入后登记箱子落盘 |
| `terraauth/Persistence/IPersistence.cs` | `IWorldRepository` 新增 `SaveChestChangesAsync` / `LoadChestChangesAsync` + `WorldChestRecord` |
| `terraauth/Persistence/SqlitePersistence.cs` | 新增 `WorldChests` 表（SQLite）与内嵌后端箱子段 |
| `terraauth/GameHost.cs` | 箱子内容启动回放（索引 + 坐标双校验）+ 1Hz 落盘（失败重新排队） |
| `terraauth/Net/Phase5/NetworkHost.cs` | 断线走会话保留（`MarkPlayerOffline` + `ResetPlayer`）；登录时按玩家名 `TryResumePlayer`；连接结束回收槽位 |
| `terraauth/Net/Phase5/ConnectionManager.cs` | `RemoveAsync` 支持「键 + 实例」双匹配（槽位复用时防误杀新连接） |
| `terraauth/Authority/IAuthorityLayer.cs` | `IMovementAuthority.ResetPlayer` + `IInboundPipeline.ResetPlayer` 默认实现 |
| `terraauth/Authority/AuthoritySubsystems.cs` | `MovementAuthority.ResetPlayer`（清理移动校验基线） |
| `terraauth/Authority/InboundPipeline.cs` | `IResettableStage` + 阶段转发 |
| `terraauth/Authority/ShardedInboundPipeline.cs` | `InboundWork.IsReset` + 重置投递到同一分片 / 队列（保证在途包先处理完） |
| `terraauth/Plugins/HookIntegration.cs` | `HookedPipeline.ResetPlayer` 转发 |
| `terraauth/Net/Phase5/Connection.cs` | 新增 `SyncedSections` / `LastStreamSection`（区块流送去重 + 跨区块判定） |
| `terraauth/Net/Phase5/NetworkHost.cs` | 包 8 在 **Playing 阶段也处理**；`StreamSectionsForPlayersAsync`（跨区块补发周边 **3×3** + 先发包 9 进度）；未建模包**默认中继**（`IsSelfOnlyPacket` 例外表） |
| `terraauth/Authority/AuthoritySubsystems.cs` | 移动校验改**分轴**（垂直用 `max(MaxSpeed, MaxFallSpeed)`）；`MovementLimits` 新增 `MaxFallSpeed` |
| `terraauth/Protocol/PacketId.cs` | 新增 `TileSquare = 20` |
| `terraauth/Simulation/World/Tile.cs` | 新增 `TileSquarePacket`（包 20 契约：未压缩小矩形 + 逐格位标志/可选段） |
| `terraauth/Net/Phase5/PacketEncoder.cs` | 新增**包 20 编码**（按写入/读取两侧核对逐字段实现）；NPC 同步**锚点偏移**（史莱姆王） |
| `terraauth/GameHost.cs` | 图格改动由包 10 改为**包 20**（宽度超 255 切分）；NPC 目标字段改 **255（无目标）** |
| `terraauth/Tests/NetworkTests.cs` | +1 用例（包 20 线格式逐字段断言） |
| `terraauth/Tests/AuthorityTests.cs` | +1 用例（快速坠落接受 / 水平瞬移仍拒） |
| `README.md`、`terraauth/README.md`、`PROJECT_STRUCTURE.md`、`VANILLA_COVERAGE.md`、`OPTIMIZATION_BACKLOG.md`、`SUMMARY.md` | 文档同步（含 §二「无接触伤害」矛盾修正、W-1 立项、第十一 ~ 十五轮） |

---

## 四、验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 默认后端 | `dotnet test TerraAuth.sln -c Release` | **257 / 257 通过** |
| 兜底后端 | `dotnet test TerraAuth.sln -c Release -p:NoSqlite=true` | **257 / 257 通过** |

> 说明：解决方案文件位于 `terraauth/terraauth/TerraAuth.sln`（与源码同目录），不在仓库根。

---

## 五、已知限制（简化模型，非原版全量）

- **程序化世界生成**：支持原版三档尺寸（小 / 中 / 大），但地形只是「可加载地形」——
  正弦地表 + 草/土/石 + 背景墙 + 一名向导 NPC，**无矿石 / 洞穴 / 生物群系 / 树木 / 地牢 / 生命水晶**；
  需要完整地形请用 `WorldPath` 指定真实 `.wld`。大世界（8400×2400 ≈ 2000 万图格）内存约 0.5 GB。
- **Boss / 事件**：AI 仅直线追击、生命值为简化表；血月 / 日食为昼夜概率、入侵为配额刷怪；
  掉落为**简化表**（仅收录眼魔 / 世界吞噬者 / 史莱姆王 / 蜂后），未复刻原版掉落数据库。
- **液体**：混合反应仅在本格为空时生成；无液体压力模型。
- **电路**：无门电路 / 定时器 / 压力板（action 18 未建模）。
- **物理 / AI**：弹幕无图格碰撞与追踪 / 反弹行为；掉落物无拾取动画与合并；
  **NPC 同步省略 ai**（客户端在 ai 位未置位时置 0，故与发送 0 等价）—— NPC 动作由**客户端自身 AI** 驱动（与原版一致），
  已补齐同步锚点（史莱姆王）与目标字段语义；遗留：NPC 增益（buff）未同步。
- **图格推送**：**少量图格改动**（执行器翻转 / 液体混合）走**包 20（TileSquare）**，**区块级地形流送**走**包 10（TileSection）**
  —— 与原版一致。图格流送矩形为 3×3（跨区块才补发、逐连接去重）。
- **世界持久化**：在线走**图格 + 箱子内容增量**（1Hz，崩溃最多丢 1 秒）；整份 `.wld` 导出为**停机 / 空服**动作
  （全量 O(世界大小)，避开在线时段）。
- **导出的 `.wld` 验证程度**：已通过**逐格 round-trip** + **按原版加载器顺序的严格分段走查**（11 段含 5..9 段的合法空编码、
  段起点递增、逐段位置断言、footer 用尽文件）。**尚未在原版二进制上实测加载** —— 本沙箱无法运行 `TerrariaServer.exe`
  （连其 `-autocreate` 自建世界也崩溃、零输出，判定为环境限制而非文件格式问题）。
- **对端一致性**：原版客户端不支持预测协议，延迟只能靠快照频率缓解；Phase 7 的「内存修改类」条目仍需手工实验。
- **断线会话保留范围**：仅保留**运行时状态**（位置 / 血量 / 法力 / 增益 / 速度清零），不含「在线期间尚未落盘的临时实体归属」；
  且因原版客户端断线即回主菜单，属**手动重进的会话接管**（宽限期内同身份重连），非自动重连。
