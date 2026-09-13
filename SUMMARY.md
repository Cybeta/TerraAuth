# TerraAuth 交付汇总

> **单一汇总文件**：本文把本轮全部交付事项（功能 / 修复 / 测试 / 文档）与完整变更文件清单合并为一处，
> 作为本次推送的对照说明。更细的模块状态见 [`terraauth/README.md`](terraauth/README.md)，
> 原版功能覆盖见 [`terraauth/VANILLA_COVERAGE.md`](terraauth/VANILLA_COVERAGE.md)，
> 逐轮回溯见 [`terraauth/OPTIMIZATION_BACKLOG.md`](terraauth/OPTIMIZATION_BACKLOG.md)。
>
> 生成日期：2026-09-13 ｜ 当前测试：**306 用例通过**（默认 SQLite 后端全绿；非 SQLite 兜底后端需单独执行验证）

---

## 近期工作汇总（2026-09-13）

### 目录与版本适配整理

- 将 `Net/Phase4` 重命名为 `Net/Snapshots`，将 `Net/Phase5` 重命名为 `Net/Transport`。
- 将 NPC AI 与尺寸数据归入 `Simulation/NpcAI`，将弹幕模拟归入 `Simulation/Projectiles`。
- 将协议定义归入 `Protocol/Terraria/V326`，为后续 Terraria 版本并行适配预留清晰边界。
- 同步更新 C# 命名空间、项目引用和相关 Markdown 路径；保留世界状态、Tile 和 `.wld` 文件处理的现有位置，降低重组风险。

### NPC 与战斗权威修复

- 包 28 的 NPC `Generation` 已从入站协议传递到服务端命令，并在扣血前校验，避免旧索引误伤当前 NPC。
- AI 生成的 NPC 补齐 `Generation`，不再依赖默认值 `0`。
- NPC 同步增加玩家级基线记录；玩家首次进入 NPC 视口时会收到完整 NPC 状态，减少“服务端已碰撞但客户端尚未显示”的视觉不同步。
- 包 117 保持仅上报语义，不直接修改服务端 HP、免伤帧或死亡状态；接触伤害继续由服务端权威结算。

### 客户端兼容与图格同步

- `tile_type_mismatch` 仍拒绝不一致的图格修改，但不再累计违规踢出正常客户端。
- 图格不一致时补发服务端权威图格，帮助客户端恢复本地缓存。
- 保持原版客户端协议 326 的无 GUI 服务端运行方式。

### 验证结果

- 自动化测试：306/306 通过，失败 0，跳过 0。
- Release 构建：成功，错误 0。
- 本轮重点验证：NPC Generation、玩家级 NPC 同步基线、原版客户端连接兼容和项目目录整理。

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
- **事件状态机**：血月 / 日食按昼夜概率触发；入侵按配额刷怪、耗尽即结束；Boss 中眼魔（4）/ 魔焰眼（31）/ 蜂后（43）已按原版 aiStyle 驱动运动与攻击，其余为简化直线追击 AI。

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

### 6. Vanilla-only 生产边界

- 当前生产版本仅支持原版 Terraria 客户端（协议 326）。
- MOD / TModLoader 握手、自定义包 250-255 注册与未知包透传均已关闭；未来 MOD 兼容层单独立项。

### 7. 测试与对抗自动化

- 套件 **306 用例**，默认后端 `dotnet test "terraauth\Tests\TerraAuth.Tests.csproj" --no-restore` 全部通过。
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
  写出器（与读取器布局严格对称，覆盖全部图格特征位）配合 `WorldExportPath`：**停机导出 + 空服周期导出**，
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

### 10. 客户端互操作性验证与修复（三项硬伤）

以「客户端能否正常游玩」为线索做了一次**协议行为验证**，修掉三项此前文档未列的硬伤：

- **出生点以外没有地形**（最严重）：包 8（区块请求）原先只在握手期处理，进入游戏后请求被丢弃。
  现在 **Playing 阶段也处理**，并由快照循环按玩家位置流送周边区块（跨区块才补发、已发过的区块不重复编码）——
  玩家走出出生点后远处地形正常出现。
- **正常坠落会被判超速并被踢**：移动权威原先只用水平上限（8 px/帧）判定，而原版下落终速约 20 px/帧，
  必然超标 → 服务端位置停在原处（后续挖 / 放 / 开箱全部 out_of_reach）+ 违规累计到 10 次即踢。
  现改为**分轴判定**：水平用 `MaxSpeed`，垂直用 `max(MaxSpeed, MaxFallSpeed)`（`MaxFallSpeed` 由配置注入）。
- **当前生产采用 Vanilla-only 白名单**：未知或未建模包默认拒绝，不进入即时中继；玩家、图格、实体、箱子等状态包必须经仿真提交后由服务端重新生成同步包。

**随后通过协议字段核对，把三项「待确认」落实到位**：

- **图格改动改用包 20（TileSquare）**：原版对少量图格改动走包 20（未压缩小矩形 + 逐格位标志/可选段），
  只有区块级地形下载才走包 10。现在执行器翻转 / 液体混合等走**包 20**（矩形宽度超 255 自动切分），区块流送仍走包 10。
- **区块流送参数对齐原版**：流送矩形改为 **3×3**（以玩家所在区块为中心），下发前先发**包 9（进度）**，
  并沿用**逐连接去重**（原版同样按客户端记录已发区块）。
- **NPC 同步细节**：协议字段核对表明 —— **省略 ai 与发送 ai=0 等价**（无需补发）；
  补齐**同步锚点**（史莱姆王：位置 + 体型×锚点，修正客户端画偏）；目标字段由 `0` 改为 **255（显式无目标）**。

### 11. 地形生成器重写 + 包 13 尾随字段保真

- **地形生成器重写**：原实现只有「正弦地表 + 草/土/石」，现在生成**分层地形**——地表起伏改用确定性哈希噪声，
  并含**洞穴、按深度分带的矿脉、两端海滩与海水、地狱层（灰烬/狱石）、地下宝箱（2×2 摆放 + 战利品）**；
  层高比例（地表 / 岩层 / 海平面 / 地狱层）、图格 ID、宝箱摆放与帧约定**均经协议行为验证**；
  出生点半径内不挖洞（防出生坠落）。
- **包 13 尾随字段保真**：解码时原先把**挂载类型 / 回城双坐标 / 相机目标**读后即丢、编码时强制清标志位 →
  现在**原样保留并写出**，他人可看到坐骑、相机与回城表现（标志位与负载严格自洽，避免整连接错位）。
- **顺带修复**：`.wld` 箱子去重后未重排 `Index`（会让 `FindChestByIndex` 错位）。

### 12. 文档同步

根 `README.md`、`terraauth/README.md`、`PROJECT_STRUCTURE.md`、`VANILLA_COVERAGE.md`、
`OPTIMIZATION_BACKLOG.md`、`Phase6/Phase7` 与 `ModCompat` README 均已按上述实现同步更新。

### 13. 连接会话隔离（SessionId）与箱子会话绑定

连接槽位（`PlayerId`）按「最小空闲 ID」复用后，旧连接的异步收尾会与新连接**共享同一个 `PlayerId`**，
仅按 `PlayerId` 校验会让旧连接的在途命令 / 清理动作作用到新玩家身上。本轮引入内部会话代数隔离：

- **`SessionId`（不进协议、不入存档）**：每个 `Connection` 实例一个唯一 `long`，
  贯穿 `PacketContext` → `Command` → `PlayerRuntime`，命令在 `Apply` 阶段比对，
  不一致返回 **`stale_session`**；认证完成即显式创建带 `SessionId` 的运行时（不再依赖首个移动命令惰性创建）。
- **会话恢复换代**：宽限期内同身份重连认回运行时，同时把 `SessionId` 覆盖为新连接的值，旧在途命令立即失效。
- **断线清理按代数收窄**：移动基线重置 / 离线会话回收 / 箱子会话关闭 / 外观与会话时长缓存 / 违规窗口 /
  离开广播（按 Connection 实例双匹配）只在「槽位仍属于本连接」时生效。
- **箱子会话改为 `(PlayerId, SessionId) → ChestIndex`**：包 31 生成 **`OpenChestCommand`**，
  由仿真提交阶段建立会话（权威只读校验不再直接改世界）；箱子广播按连接会话筛选接收者。
- **测试**：新增旧连接清理 / 踢出不影响复用槽位新连接的用例（含真实 TCP 下的槽位复用）；
  会话恢复用例补断言「重连后 `SessionId` 已更换」。

### 14. 广播可靠性收尾 · 箱子会话生命周期 · 拾取并发 · 事件模型

- **实体广播失败不再丢更新**：掉落物新增 / 移除、弹幕销毁、玩家死亡 / 复活此前都是**发送前**就把
  「已通知」标记置位，发送抛异常（连接断开、出站队列关闭）后该状态变更永久丢失。
  现在统一改为**发出成功后才置位**，失败仅记录 `broadcast_failed` 事件并留待下一轮重试
  （与箱子路径的重新入队语义一致），取消仍向上传播。
- **箱子会话生命周期补全**：①包 31 负坐标视为「关闭箱子」请求（原版客户端关闭时只清本地状态、不发包），
  权威只读校验直接放行、由 `CloseChestCommand` 在仿真提交阶段关闭会话（且校验会话代数，
  旧连接关不掉复用同槽位的新连接会话）；②仿真每 tick 做**距离复核**，玩家离开交互距离即关闭会话，
  避免会话长期驻留后被误用。
- **拾取原子性回归测试**：背包满时掉落物保留且不进入「待通知移除」；两个玩家并发抢同一掉落物**只成功一次**；
  越界拾取不改变实体状态。实现本就原子（同一把 `ItemsLock` 内完成入库 + 失效），本轮补齐断言。
- **事件模型分层**：`GameEvent` 增加 `Category`（`StateCommit` / `Persistence` / `Broadcast` / `Failure`），
  事件名收敛到 `GameEventKinds` 常量；命令失败原因收敛到 `CommandFailures` 常量
  （值与原字符串完全一致，指标与测试断言不受影响）；图格 / 箱子落盘失败、实体 / 箱子广播失败现在都会留下事件。

### 15. 未建模包边界：拒绝但不计违规 + 按 PacketId 统计（真机游玩前置）

- **问题**：踢出判定对所有权威拒绝一视同仁（默认 **10 次 / 60 分钟** → 踢出），而未建模包统一 `Reject("unknown_packet")`。
  正常原版客户端会持续发未建模包（表情 / 家具 / 告示牌 / 部分 NetModule…）→ **正常玩家被误踢**（线上误判风险）。
- **修复**：`AuthorityResult` 新增 `CountsAsViolation`（默认 true，作弊语义保持 true）；
  未建模包 `Reject(..., countsAsViolation: false)` → 仍被拒绝（不进权威链路），但不参与违规累计。
- **统计**：`NetworkHost.UnmodeledPacketCounts`（`PacketId → 次数`）+ 首次出现必打印 / 每 100 次打印 / **停机汇总**，
  用于真机测试后拿到「客户端实际发了哪些包」的清单，决定「登记为中继」还是「权威建模」，而非盲目构造全部包。
- **测试**：+1 → **285 / 285**；并反向验证（临时改回旧行为 → 用例如期失败：计数停在 10 即被踢出）。

### 16. 真机实测修复（一）：NPC 同步 20Hz + AI 每 tick 步进

原版客户端真机进图后报告「怪物移动卡顿」，定位到两处叠加原因并修复：

- **NPC 同步原先只有 1Hz**（包 23 只在 1Hz 的世界同步循环下发）→ 客户端每秒被拽一次；
  现有新增 `GameHost.BroadcastNpcUpdatesAsync` 由快照循环按 **20Hz** 调用，
  **状态变化才发**（X/Y/速度/生命/存活），未变化按 1s 心跳补发（保证新入服玩家能看到静止 NPC）。
- **服务端 AI 每 4 tick 才步进一步**（等效 0.25 px/tick）→ 改为**每 tick 步进**（敌怪 1 px/tick + 每 tick 重力）；
  城镇 NPC 另改为「方向持久 + 到住所 ±4 格边界折返」的平滑往返（原实现每步随机改向，高频同步下会抖动）。
- 顺带修：`MetricsHttpServer.Dispose` 在端口占用时抛 `ObjectDisposedException` 掩盖真实启动错误。
- **测试**：+2 → **287 / 287**（城镇 NPC 平滑步进 / NPC 同步变化检测）。

> 真机同批报告另两项（**向导卡在土里**、**未碰怪却掉血**）本轮未修，已记入 `OPTIMIZATION_BACKLOG.md` 第二十二轮「仍未修」。

### 17. 敌怪改为跳跃式移动（恢复史莱姆的「跳」）

真机反馈：卡顿缓解后**跳跃动作消失**（看着像贴地滑行）。两侧根因：

- **服务端**：`SimulateEnemyStep` 只做「每 tick 水平 ±1px + 重力」，从不给 `VelocityY` 向上初速度 → 权威运动没有滞空阶段。
- **客户端**：包 23 处理会 `npc.position = …; npc.velocity = …; npc.ai[i] = ai[i]` **覆盖速度与 ai**（未置位的 ai 位即 0），
  所以 20Hz 的「地面速度 + ai 全零」每 50ms 冲掉客户端本地史莱姆的跳跃状态。

**修复**：服务端改为跳跃式 —— 贴地静止等待 20~45 tick → 起跳（`VelocityY = -6`，≈45px 高 / 30 tick 滞空）
+ 朝玩家水平速度 2px/tick，空中只受重力，落地重新计时。贴地判定改用**碰撞结果** `WorldNpc.Grounded`
（用 `VelocityY == 0` 会让悬空生成的敌怪不落体；且贴地时必须直接返回，否则等待计数每 tick 被落地清零、永不起跳）。

**测试**：+1 → **288 / 288**（`Enemy_Hops_WithGroundedWait_InsteadOfGroundGliding`）；接触免伤用例改为钉住敌怪，只验证免伤窗口。

---

## 三、变更文件清单

### 本轮变更（广播重试 · 箱子会话生命周期 · 拾取并发 · 事件模型）

共 **16 个文件**（13 源码 / 3 文档 + 测试），+676 / −113 行；全量 **283 / 283 通过**。

| 文件 | 说明 |
|---|---|
| `terraauth/GameHost.cs` | 掉落物 / 弹幕 / 玩家死亡复活的「已通知」标记改为**发送成功后置位**，瞬时失败记录 `broadcast_failed` 并重试；箱子广播失败重排保留；落盘失败记录 `persist_failed` |
| `terraauth/Simulation/EventRecorder.cs` | 新增 `GameEventCategory`（状态提交 / 持久化 / 广播 / 失败）、`GameEventKinds`、`CommandFailures`；`GameEvent` 增加 `Category` |
| `terraauth/Simulation/CommandQueue.cs` | 失败原因改用 `CommandFailures` 常量；新增 `CloseChestCommand`（按会话代数关箱） |
| `terraauth/Simulation/WorldSimulator.cs` | 事件名 / 类别接入；新增每 tick 的**箱子会话距离复核**（离开交互距离即关闭）；暴露 `Recorder` |
| `terraauth/Simulation/World/WorldState.cs` | 新增 `SnapshotChestSessions()`（供距离复核遍历） |
| `terraauth/Authority/InboundPipeline.cs` | 包 31 负坐标 → `CloseChestCommand` |
| `terraauth/Authority/AuthoritySubsystems.cs` | 包 31 负坐标只读放行（关箱请求不按越界拒绝） |
| `terraauth/Tests/SimulationTests.cs` | +6 用例（关箱会话 / 旧会话关不掉新会话 / 离开距离关闭会话 / 背包满保留掉落物 / 越界拾取 / 并发拾取只成功一次） |
| `terraauth/Tests/VanillaFeatureTests.cs` | 越界开箱用例改用正数越界坐标；+1 用例（包 31 负坐标为关箱请求） |
| `terraauth/Tests/IntegrationTests.cs` | +1 用例（旧连接实例踢出 / 移除不得影响复用槽位的新连接） |
| `README.md`、`terraauth/README.md`、`terraauth/PROJECT_STRUCTURE.md`、`terraauth/VANILLA_COVERAGE.md`、`terraauth/OPTIMIZATION_BACKLOG.md`、`SUMMARY.md` | 测试数 283 与本轮说明同步 |

### 上一轮推送（`1efc999` Add connection session isolation）

共 **13 个文件**（全部修改），+627 / −117 行；另含本条汇总与文档同步。

| 文件 | 说明 |
|---|---|
| `terraauth/Net/Transport/Connection.cs` | 连接实例持有唯一 `SessionId` |
| `terraauth/Authority/IAuthorityLayer.cs` | 权威 `Validate` 增加带 `SessionId` 的重载；`ResetPlayer` 增加带代数的重载 |
| `terraauth/Authority/InboundPipeline.cs` | `PacketContext.SessionId` 贯通；包 31 → `OpenChestCommand`；命令落地前写入 `SessionId` |
| `terraauth/Authority/ShardedInboundPipeline.cs` | 分片工作项携带 `SessionId`，`Reset` 按代数投递到同一分片 |
| `terraauth/Authority/AuthoritySubsystems.cs` | 箱子打开 / 写入校验接入 `SessionId`；移动 `ResetPlayer` 按代数据收窄 |
| `terraauth/GameHost.cs` | 箱子广播按 `Connection.SessionId` 筛选；发送失败重排保持 |
| `terraauth/Net/Transport/ConnectionManager.cs` | 移除 / 踢出按「键 + 实例」双匹配，不再误杀复用槽位的新连接 |
| `terraauth/Net/Transport/NetworkHost.cs` | 认证完成显式创建带 `SessionId` 的运行时；外观 / 会话时长 / 违规窗口与会话代数绑定；离开广播按连接实例校验；箱子广播改 `Func<Connection, bool>` |
| `terraauth/Simulation/CommandQueue.cs` | 新增 `OpenChestCommand`；命令基类统一 `stale_session` 判定 |
| `terraauth/Simulation/World/WorldState.cs` | 箱子会话升级为 `(PlayerId, SessionId) → ChestIndex`；断线清理 / 恢复按代数收窄 |
| `terraauth/Tests/SimulationTests.cs` | +6 用例（会话命令拒绝 `stale_session` / 箱子会话代数 / 离线清理代数 / 会话恢复续期） |
| `terraauth/Tests/VanillaFeatureTests.cs` | 会话恢复用例补断言「重连后 `SessionId` 已更换」 |
| `terraauth/Tests/IntegrationTests.cs` | +1 用例（旧连接实例踢出 / 移除不得影响复用槽位的新连接） |

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
| `Net/Transport/NetworkHost.cs` | +119 | 包 10 超帧二分拆分、按玩家定制广播、单播 / 原始包发送 |
| `Net/Transport/PacketDecoder.cs` | +46 | 新增入站包解析（拾取 / 箱子 / 伤害 / 死亡 / 传送 / 包 82 模块） |
| `Net/Transport/PacketEncoder.cs` | +26 | 新增出站包编码 |
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
| `PluginModTests.cs` | +182 | 插件与未来 MOD 兼容层禁用边界测试（含 TModLoader 拒绝与包范围关闭） |
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
| `terraauth/Net/Transport/PacketDecoder.cs`、`PacketEncoder.cs` | 包 42 编解码 |
| `terraauth/Net/Transport/ITerrariaProtocol.cs` | 权威白名单新增 35 / 42 / 50 |
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
| `terraauth/Tests/WorldFileTests.cs` | +3 用例（特征世界逐格 round-trip / 世界旗标 round-trip / 按协议顺序的严格分段走查） |
| `terraauth/Tests/VanillaFeatureTests.cs` | +19 用例（法力 / 治疗 / 增益 / 弹幕生成 / 世界改动重启回放 / 世界文件加载 / 世界导出 / 世界尺寸配置（中世界生成）/ 会话恢复 / 箱子内容重启存活 / 区块流送 / 未建模包拒绝 / **包 13 字段保真**）；偶发用例等待窗口 5s → 15s |
| `terraauth/Simulation/World/WorldGenerator.cs` | **重写为分层地形生成**：噪声地表 / 洞穴 / 按深度分带矿脉 / 海滩+海水 / 地狱层 / 2×2 宝箱+战利品；层高比例与图格 ID 经协议行为验证（`WorldSize` 三档保留） |
| `terraauth/Tests/SimulationTests.cs` | +4 用例（三档世界尺寸生成 / **分层地形内容：矿脉·洞穴·草皮·地狱层·海水·宝箱**） |
| `terraauth/Protocol/Types.cs` | `PlayerControlsPacket` 新增 `MountType` / `PotionReturnOriginal` / `PotionReturnHome` / `CameraTarget`（包 13 可选尾随段） |
| `terraauth/Net/Transport/PacketDecoder.cs` | 包 13 尾随段**读取并保留**（不再读后即丢） |
| `terraauth/Net/Transport/PacketEncoder.cs` | 包 13 按字段存在性**校准可选位并写出尾随字段** |
| `terraauth/Simulation/World/WorldFileReader.cs` | 箱子去重后**按列表下标重排 `Index`**（修 `FindChestByIndex` 错位） |
| `terraauth/Tests/IntegrationTests.cs` | 踢出用例等待窗口 5s → 10s（世界生成变重后的偶发） |
| `terraauth/Simulation/World/WorldState.cs` | 箱子待落盘集合（`MarkPersistChest` / `DrainPersistChests`）+ `Chest.SerializeItems` / `DeserializeItems` |
| `terraauth/Simulation/CommandQueue.cs` | `SyncChestItemCommand.Apply`（包 32）写入后登记箱子落盘 |
| `terraauth/Persistence/IPersistence.cs` | `IWorldRepository` 新增 `SaveChestChangesAsync` / `LoadChestChangesAsync` + `WorldChestRecord` |
| `terraauth/Persistence/SqlitePersistence.cs` | 新增 `WorldChests` 表（SQLite）与内嵌后端箱子段 |
| `terraauth/GameHost.cs` | 箱子内容启动回放（索引 + 坐标双校验）+ 1Hz 落盘（失败重新排队） |
| `terraauth/Net/Transport/NetworkHost.cs` | 断线走会话保留（`MarkPlayerOffline` + `ResetPlayer`）；登录时按玩家名 `TryResumePlayer`；连接结束回收槽位 |
| `terraauth/Net/Transport/ConnectionManager.cs` | `RemoveAsync` 支持「键 + 实例」双匹配（槽位复用时防误杀新连接） |
| `terraauth/Authority/IAuthorityLayer.cs` | `IMovementAuthority.ResetPlayer` + `IInboundPipeline.ResetPlayer` 默认实现 |
| `terraauth/Authority/AuthoritySubsystems.cs` | `MovementAuthority.ResetPlayer`（清理移动校验基线） |
| `terraauth/Authority/InboundPipeline.cs` | `IResettableStage` + 阶段转发 |
| `terraauth/Authority/ShardedInboundPipeline.cs` | `InboundWork.IsReset` + 重置投递到同一分片 / 队列（保证在途包先处理完） |
| `terraauth/Plugins/HookIntegration.cs` | `HookedPipeline.ResetPlayer` 转发 |
| `terraauth/Net/Transport/Connection.cs` | 新增 `SyncedSections` / `LastStreamSection`（区块流送去重 + 跨区块判定） |
| `terraauth/Net/Transport/NetworkHost.cs` | 包 8 在 **Playing 阶段同样处理**；`StreamSectionsForPlayersAsync` 按连接去重，跨区块补发周边 **3×3**，并先发包 9（进度）；未建模包默认拒绝，状态包不走即时中继 |
| `terraauth/Authority/AuthoritySubsystems.cs` | 移动校验改**分轴**（垂直用 `max(MaxSpeed, MaxFallSpeed)`）；`MovementLimits` 新增 `MaxFallSpeed` |
| `terraauth/Protocol/PacketId.cs` | 新增 `TileSquare = 20` |
| `terraauth/Simulation/World/Tile.cs` | 新增 `TileSquarePacket`（包 20 契约：未压缩小矩形 + 逐格位标志/可选段） |
| `terraauth/Net/Transport/PacketEncoder.cs` | 新增**包 20 编码**（按协议字段核对逐字段实现）；NPC 同步**锚点偏移**（史莱姆王） |
| `terraauth/GameHost.cs` | 图格改动由包 10 改为**包 20**（宽度超 255 切分）；NPC 目标字段改 **255（无目标）** |
| `terraauth/Tests/NetworkTests.cs` | +1 用例（包 20 线格式逐字段断言） |
| `terraauth/Tests/AuthorityTests.cs` | +1 用例（快速坠落接受 / 水平瞬移仍拒） |
| `README.md`、`terraauth/README.md`、`PROJECT_STRUCTURE.md`、`VANILLA_COVERAGE.md`、`OPTIMIZATION_BACKLOG.md`、`SUMMARY.md` | 文档同步（含 §二「无接触伤害」矛盾修正、W-1 立项、第十一 ~ 十五轮） |

---

## 四、验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 默认后端 | `dotnet test "terraauth\Tests\TerraAuth.Tests.csproj" --no-restore` | **306 / 306 通过** |
| Vanilla-only 网络与集成过滤 | `dotnet test "terraauth\Tests\TerraAuth.Tests.csproj" --no-restore --filter "FullyQualifiedName~IntegrationTests|FullyQualifiedName~VanillaFeatureTests"` | **94 / 94 通过** |

> 说明：解决方案文件位于 `terraauth/terraauth/TerraAuth.sln`（与源码同目录），不在仓库根。

---

## 五、已知限制（简化模型，非原版全量）

### 架构与权威链路检查（修正进度，2026-09-12 第十九轮按代码核对）

**已修正（已与代码核对）**：

1. **命令顺序**：`CommandQueue` 改为 `(Tick, Sequence)` 优先队列，未来 Tick 不再阻塞已到期命令。
2. **放砖原子提交**：放砖权威校验改为只读，库存扣除与图格写入统一放入仿真提交阶段；入站管线注入实际仿真 Tick。
3. **未知包边界**：未登记 `UnknownPacket` 默认由 Authority 拒绝，不再作为正常客户端包即时中继。
4. **连接时序与失败传播**：出站 Channel 改为容量 2048 的**有界**队列；读写循环联动取消并观察双方异常；
   写入 / Flush 失败会传递给等待者；Kick 与认证失败路径等待 Disconnect 包刷新后再关闭连接。
5. **协议输入限额**：液体模块在分配列表**之前**校验条目上限（128）与 payload 剩余长度。
6. **拾取原子性 / 箱子会话**（第十八轮）：拾取改为仿真提交阶段库存与掉落物原子结算；箱子操作增加打开会话、
   在线状态与距离复核；实体 / 箱子广播改为「发送成功后才置位」并支持失败重试。

**仍待修正**：

7. **提交后广播一致性**：客户端原包仍可能在仿真提交前广播，需让广播绑定已提交状态。
8. **背压**：入站 `CommandQueue` / 分片入站队列 / 审计 `Channel` 仍无界，缺容量上限与过载策略。
9. **容量与架构边界**：慢客户端策略、`WorldState` 并发契约、WorkerPool 收敛、`SnapshotStore` 环形缓冲与
   `MaxEntitiesPerPacket` 实体分包、协议元数据集中及程序集拆分。

> 第十九轮为**文档与代码一致性核对**：修正了文档中过期的测试数（263 → 283）、测试文件数（7 → 9）、
> `PacketId` 常量数（39 → 41）、编解码覆盖数（入站 35 / 出站 37）、持久化后端与 Vanilla-only 边界表述，
> 并补全 `PROJECT_STRUCTURE.md` 文件树；**运行代码未改动**。详见 `OPTIMIZATION_BACKLOG.md` §附第十九轮。


- **程序化世界生成**：支持原版三档尺寸（小 / 中 / 大），并生成**分层地形**（草皮 / 泥土 / 岩层 / 地狱层、
  洞穴、按深度分带矿脉、两端海滩与海水、地下宝箱 + 战利品），层高比例 / 图格 ID / 摆放约定经协议行为验证。
  但**仍为简化的分层生成模型**：**无树木 / 生命水晶 / 生物群系（雪原 / 沙漠 / 丛林 / 腐化）/ 地牢·神庙等结构体**；
  需要完整地形请用 `WorldPath` 指定真实 `.wld`。大世界（8400×2400 ≈ 2000 万图格）内存约 0.5 GB。
- **Boss / 事件**：眼魔 / 魔焰眼 / 蜂后已按原版 aiStyle 驱动，其余 Boss AI 仅直线追击、生命值为简化表；血月 / 日食为昼夜概率、入侵为配额刷怪；
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
- **导出的 `.wld` 验证程度**：已通过**逐格 round-trip** + **严格分段走查** + **双向原版互操作实测**
  （第二十轮：修正元数据文件类型字节后，原版 `TerrariaServer.exe` 加载本服务端导出世界成功：`Loading world data: 100%` / `Server started`；
  反向本服务端加载原版自建世界成功，逐段指针断言全过，含原版 RLE 压缩图格段）。
  **双向 `.wld` 格式互操作性均已确认**；「原版客户端进图」仍待验证。
  注意：读取器跳过段 6..10，故「原版世界 → TerraAuth → 再导出」会把这些段写为空编码。
- **对端一致性**：原版客户端不支持预测协议，延迟只能靠快照频率缓解；Phase 7 的「内存修改类」条目仍需手工实验。
- **断线会话保留范围**：仅保留**运行时状态**（位置 / 血量 / 法力 / 增益 / 速度清零），不含「在线期间尚未落盘的临时实体归属」；
  且因原版客户端断线即回主菜单，属**手动重进的会话接管**（宽限期内同身份重连），非自动重连。
