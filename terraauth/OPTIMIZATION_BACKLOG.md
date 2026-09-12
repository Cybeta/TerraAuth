# TerraAuth — 优化待办（Backlog）

> 记录**尚未实施**的优化 / 补全事项，供后续排期取舍。已实施项见文末「本轮回溯」。
> 最后更新：2026-09-12（第十六轮：地形生成器重写 + 包 13 尾随字段 + 箱子索引修复）

---

## 一、独立立项

### W-1 支持加载真实 `.wld` 世界 —— ✅ 已实施（第十轮）

`ServerConfig.WorldPath` 指定世界文件即可开服（为空仍程序化生成）；并新增 `.wld` **写出器**与导出开关
（`WorldExportPath` / `WorldExportIntervalSeconds`，停机 + 空服导出）。详见第十轮回溯。

**遗留**：写出文件已通过**逐格 round-trip** + **按原版加载器顺序的严格分段走查**；
但**尚未在原版二进制上实测加载**（本沙箱无法运行 TerrariaServer.exe，自建世界同样崩溃）。

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

### 第十六轮（2026-09-12）：地形生成器重写 + 包 13 尾随字段 + 箱子索引修复

**问题**（用户指定两项）：

1. **地形生成器过于简陋**：原实现只有「正弦地表 + 草/土/石 + 背景墙 + 一名向导」，
   没有洞穴 / 矿脉 / 海滩 / 地狱层 / 宝箱 —— 玩家进服后无处可探索、无矿可挖。
2. **包 13 转发丢失可选尾随字段**：解码时把挂载类型 / 回城双坐标 / 相机目标**读后即丢**，
   编码时又强制清掉对应标志位 → 他人看不到坐骑 / 相机 / 回城表现。

**已实施**：

- **地形重写**（层高比例 / 图格 ID / 摆放约定均按原版读写两侧核对）：
  - **层高**：地表基准 = 高 × 0.30 × (0.90~1.10)；岩层 = (地表 + 高 × 0.20) × (0.90~1.10)；
    地表钳制在 `[高 × 0.17（小世界 +0.02）, 高 × 0.26]`；`worldSurface` = 最高列 + 25；
    `rockLayer` 与 `worldSurface` 的差对齐到 6 的倍数；海平面 = (地表 + 岩层) / 2 + 40；地狱层 = 底部 200 格。
  - **地表起伏**：改用**确定性哈希值噪声**（多倍频 fBm + 平滑插值），不再用固定正弦叠加，且不依赖 `System.Random`。
  - **地层**：草皮 / 泥土（含黏土与沙斑）/ 岩层（石头 + 石墙）/ 地狱层（灰烬 + 狱石）。
  - **洞穴**：脊状噪声挖隧道 + 深层空腔；**出生点半径 16 格内不挖洞**（防出生即坠落）。
  - **矿脉**：按深度分带（浅 铜/铁 → 中 铁/银 → 深 银/金），随机游走成脉，只替换泥土 / 黏土 / 沙 / 石。
  - **海滩 + 海水**：两端海滩，越靠地图边缘越深，海平面到沙面之间灌满水。
  - **宝箱**：**2×2 摆放 + 原版帧约定**（`frameX ∈ {0,18}`、`frameY ∈ {0,18}`，实体登记在左上格，
    下方两格须实心），并带随机战利品（硬币 / 火把 / 木材 / 治疗药水 / 手里剑，物品 ID 按原版）。
- **包 13 尾随字段保真**：解码侧**保留**挂载类型 / 回城双坐标 / 相机目标；编码侧**按字段是否存在**校准
  三个可选位并写出尾随字段（标志位与负载严格自洽，避免接收端多读 / 少读字节导致整连接错位）。
  → 他人可见坐骑、相机与回城表现。
- **顺带修复**：`.wld` 读取的箱子去重（原版 RemoveChest 语义）会移动列表位置，
  但 `Chest.Index` 未重新对齐 → `FindChestByIndex` 会错位；现在去重后按列表下标重排 Index。

**未包含（明确边界）**：树木（需要 tree 的 frame-important 图集帧映射）、生命水晶、
生物群系（雪原 / 沙漠 / 丛林 / 腐化）、地牢 / 神庙等结构体。

**测试**：+2（**259 通过**）—— `Generate_Produces_Layered_Terrain_With_Ores_Caves_Ocean_And_Chests`
（断言矿脉 / 洞穴 / 草皮 / 地狱层 / 海水 / 宝箱数量与战利品）、
`Vanilla_PlayerControls_Relay_Preserves_Mount_And_Camera`（包 13 中继保留挂载与相机）；
另把两个受「世界生成变重」影响的既有用例等待窗口放宽（`KickAsync_...` 与 `AntiCheat_PacketFlood_...`）。
默认后端与 `-p:NoSqlite=true` 兜底后端均 259/259 通过（套件耗时由 ~26s 增至 ~40s，主要来自逐格噪声）。

### 第十五轮（2026-09-12）：按原版读写两侧核对后落地（包 20 图格方阵 + 区块流送对齐 + NPC 同步细节）

**做法**：以上一轮列出的「未确认项」为清单，逐项**对照原版客户端读取侧与服务端写入侧**的字段布局核对，
不再靠推测；核对结论直接落到实现里。

**1) 服务端驱动的图格改动改用包 20（TileSquare）** —— 落实上一轮标注「需实测再定」的协议改动：

- **核对结果**：原版对「少量图格改动」走**包 20**（未压缩的小矩形，逐格位标志 + 可选段），
  **只有区块级地形下载**才走包 10；且包 20 的写入侧与读取侧字段顺序完全一致。
- **线格式**（已按两侧核对）：`Int16 X + Int16 Y + Byte 宽 + Byte 高 + Byte 变更类型 + 逐格(3 个标志字节 + 可选段)`；
  逐格：b1 = 存在方块 / 存在墙 / 存在液体 / 线 / 半砖 / 执行器 / 未激活；b2 = 线2 / 线3 / 方块油漆存在 / 墙油漆存在 / 斜坡(bits4-6) / 线4；
  b3 = 全亮方块 / 全亮墙 / 隐形方块 / 隐形墙；随后按需写 方块油漆、墙油漆、类型（frame-important 再写 FrameX/Y）、墙、液体量+类型。
- **实现**：新增 `TileSquarePacket` 与编码器（含左上角钳制到世界内）；`FlushTileUpdatesAsync` 由「包 10 小矩形」改为**包 20**，
  并按 Byte 宽度上限（255）切分超宽的行矩形。

**2) 区块流送参数对齐原版**：

- 原版是**服务端按客户端位置主动补发**（每 tick 检查），以玩家所在区块为中心取 **(2×fluff+1)²**（fluff=1 → 3×3）方块，
  **逐客户端记录已发区块**（避免重复下发），并在有新块时先发**包 9（进度）**再逐块发包 10。
- 实现据此调整：流送矩形由 5×3 改为 **3×3**；下发前先统计「尚未下发」的块数并发包 9；去重沿用 `SyncedSections`。
- 顺带核对确认：登录期的「世界出生点 5×3 + 请求点 6×4」与包 8 的服务端处理完全一致（无需改动）。

**3) 包 23（NPC 同步）细节**：

- **ai 字段无需补发**：核对客户端读取侧 —— ai 位未置位时客户端**显式把该 ai 置 0**，
  因此「省略 ai」与「发送 ai=0」等价；当前服务端无真实 NPC ai，保持省略即可（原判断成立）。
- **同步锚点补齐**：原版对「锚点非零」的 NPC 上报 `位置 + 体型 × 锚点`，客户端接收后再减去同一偏移。
  当前仅**史莱姆王（类型 50）**锚点为 (0.5,1)、体型 98×92 → 现在编码时补 (49,92)，修掉客户端画偏。
- **目标字段语义修正**：原版 NPC AI 以 `target == 255`（部分 AI 还含 `<=0`）判为「无目标」并自行选最近玩家。
  原先固定发 `0` 会被当成「目标 = 玩家槽位 0」→ 现改为发 **255（无目标）**。

**测试**：+1（**257 通过**）—— `Encode_TileSquare_Writes_Vanilla_Layout`（单格覆盖全部可选段：油漆 / 帧 / 墙 / 液体 /
线 / 执行器 / 斜坡 / 全亮 / 隐形，逐字段断言线格式）；`Vanilla_Actuate_Pushes_Tile_Update_To_Client` 改为断言包 20。
默认后端与 `-p:NoSqlite=true` 兜底后端均 257/257 通过。

**仍未落地**：世界内容（程序化生成仍是「可加载地形」，完整地形需真实 `.wld`）。

### 第十四轮（2026-09-12）：Play 阶段区块流送 + 移动权威垂直轴 + 未建模包中继

**问题**（本轮以「原版客户端能否正常游玩」为线索做代码核对时发现，此前文档均未列）：

1. **出生点以外没有地形**：包 8（`SpawnTileData`）只在握手期处理（`when State == Authenticating`），
   进入 Playing 后落到 `default: return State == Playing` → 进管线 → 无命令 → **丢弃**。
   而原版客户端是**边走边请求周边区块**的。结果：玩家离开登录时下发的出生区块后，远处地形在客户端为空。
2. **正常坠落被判超速**：移动权威只用水平上限算允许位移（`MaxSpeed = MaxFlightSpeed = 8` px/帧），
   而原版下落终速 `MaxFallSpeed ≈ 20 px/帧` 必然超出。拒绝时又**不更新基准、也不生成 MoveCommand**
   → 服务端位置停在原处（后续挖 / 放 / 开箱 / 拾取的 `IsWithinReach` 全部判 out_of_reach），
   且每次 Reject 计入违规窗口 → 默认 **10 次 / 60s 直接踢出** ⇒ **从高处掉落即被踢**。
3. **未建模包被静默丢弃**：解码器只结构化约 35 个包，其余统一 `UnknownPacket`；
   而 `RelayToOthersAsync` 的 switch **没有 default 分支** → 未建模包既不中继也不处理，
   表情 / 告示牌 / 家具 / NetModule 其他模块等「他人可见性」全部丢失。

**已实施**：

- **区块流送**：`Connection` 记录 `SyncedSections`（已下发区块，避免重复 Deflate 编码）与 `LastStreamSection`；
  `NetworkHost.SendSectionOnceAsync` 统一「未发过才下发」；新增 `StreamSectionsForPlayersAsync`
  （由快照循环按 20Hz 调用，**仅当玩家跨越区块边界**时补发其周边 5×3 区块）；
  包 8 在 Playing 阶段也被处理（按请求点补发）。登录期出生区块路径复用同一去重逻辑。
- **移动权威分轴判定**：`MovementLimits` 新增 `MaxFallSpeed`（默认 20，由 `ServerConfig.MaxFallSpeed` 注入）；
  水平用 `MaxSpeed`，**垂直用 `max(MaxSpeed, MaxFallSpeed)`**；拒绝时仍保持基准不变（防瞬移污染）。
- **未建模包默认中继**：`RelayToOthersAsync` 增加 default 分支 —— `UnknownPacket` 默认转发给其他玩家；
  `IsSelfOnlyPacket` 列出「握手 / 世界与区块请求 / 自身属性上报 / 服务端自持（库存 / 箱子 / 拾取）」等不中继的包。

**测试**：+3（**256 通过**）——`Vanilla_TileSections_Stream_As_Player_Moves`（移动后补发区块、位置未变不重复下发）、
`MovementAuthority_Accepts_FastFall_But_Still_Rejects_HorizontalTeleport`（垂直放宽但水平瞬移仍拒）、
`Vanilla_UnmodeledPacket_Is_Relayed_To_OtherPlayers`（未建模包到达其他玩家）。
顺带把既有偶发用例 `AntiCheat_PacketFlood_...` 的等待窗口 5s → 15s（全量并行跑时踢出会变慢）。
默认后端与 `-p:NoSqlite=true` 兜底后端均 256/256 通过。

**未纳入**：**世界内容** —— 程序化生成仍是「可加载地形」（三档尺寸已支持），完整地形需 `WorldPath` 指定真实 `.wld`。
其余原「未纳入」项（NPC 同步细节、图格推送包号）已按原版**读写两侧**核对后于第十五轮落地。

### 第十三轮（2026-09-12）：程序化生成支持原版三档世界尺寸

**问题**：程序化生成器只有一档尺寸（小世界 4200×1200），且地表 / 岩层是**写死的行号常量**（360 / 720）。
要让原版客户端在「中等 / 大世界」下正常游玩（或按需选尺寸），既缺尺寸开关，也没有按高度缩放的地层。

**已实施**：

- **尺寸枚举 + 尺寸感知生成**：`WorldSize`（`Small` / `Medium` / `Large`）与 `WorldGenerator.Dimensions`，
  对应原版三档：小 4200×1200、中 6400×1800、大 8400×2400；
  `WorldGenerator.Generate(WorldSize, worldName, seed)` 统一入口，`GenerateSmall(...)` 保留为 `Small` 的便捷入口。
- **地层按高度比例缩放**：地表 / 岩层由写死行号改为 `高度 × 0.30 / 0.60` —— 小世界仍为 360 / 720（**行为不变、确定性不变**），
  中 / 大世界自动得 540/1080、720/1440，避免大世界地表偏上、岩层过浅。
- **配置化**：`ServerConfig.WorldSize`（server.json 用字符串枚举，沿用 `JsonStringEnumConverter`）；
  `GameHost.LoadBaseWorld` 按配置选择尺寸；仅当 `WorldPath` 为空 / 文件不存在时生效（指定真实 `.wld` 时以文件尺寸为准）。
- **协议量纲核实**：包 7（WorldInfo）的 `MaxTilesX/Y`、`WorldSurface`、`RockLayer`、`SpawnTileX/Y` 均为 **Int16**，
  大世界 8400 / 2400 / 1440 全部落在 `±32767` 内，无需改协议。

**边界说明**：本次只是把尺寸与布局打通，**地形内容仍是「可加载地形」**（正弦地表 + 草/土/石 + 背景墙 + 一名向导 NPC），
**不含矿石 / 洞穴 / 生物群系 / 树木 / 地牢 / 生命水晶** —— 完整地形请用 `WorldPath` 指定真实 `.wld`。
大世界约 2000 万图格，`Tile` 约 26 字节 → 内存约 0.5 GB（中世界约 0.3 GB、小世界约 0.13 GB）。

**测试**：+4（**253 通过**）——`Generate_Supports_All_Vanilla_Sizes`（Theory 三档：尺寸 / 区块数 / Int16 范围 / 出生点贴地 / 包 7 字段）、
`Vanilla_WorldSize_Config_Generates_Medium_World`（配置字符串枚举 → 中世界生成 → 登录链仍完成）。
默认后端与 `-p:NoSqlite=true` 兜底后端均 253/253 通过。

### 第十二轮（2026-09-12）：箱子内容持久化（W-1 解除阻塞项）

**问题**：第九轮评估箱子内容持久化时，因「基准世界恒为程序化生成 → 世界里没有箱子」而判定落盘路径不可达、未留死代码，
记为「待 W-1（加载真实 `.wld`）落地后一并补」。第十轮已落地 `.wld` 加载 → **阻塞解除**：
真实世界自带箱子，玩家开箱取放（包 32）会改服务端箱子内容，但此前该改动只存在内存，**重启即丢**（与修复前的「重启丢建筑」同性质）。

**已实施**（自研，沿用图格增量落盘的同构设计）：

- **脏集登记**：`WorldState` 新增箱子待落盘集合（`_pendingPersistChests`，受 `WorldPersistLock` 保护）与
  `MarkPersistChest` / `DrainPersistChests`；`HasPendingPersist` 一并纳入箱子。
  `SyncChestItemCommand.Apply`（包 32 权威通过后）在写入服务端箱子后登记该索引。
- **序列化**：`Chest.SerializeItems` / `DeserializeItems` —— 「Int32 格数 + 每格定长 7 字节（Type/Stack/Prefix）」，
  与自身严格对称；反序列化对非法格数做防御（返回空数组，避免损坏数据导致崩溃）。
- **持久化层**：`IWorldRepository` 新增 `SaveChestChangesAsync` / `LoadChestChangesAsync`（`WorldChestRecord`：索引 + 坐标 + 字节串）；
  SQLite 新增 `WorldChests` 表（单事务批量 upsert），内嵌兜底后端同步 `_worldChests` + JSON 段。
- **回放**：`GameHost.ApplyPersistedWorldChanges` 在回放图格后回放箱子 —— 按**索引定位 + 坐标校验**
  （基准世界被替换时坐标不符则跳过，避免错位套用）。
- **落盘**：`FlushWorldChangesAsync` 追加 `FlushChestChangesAsync`（同一 1Hz 循环与停机冲刷路径）；
  失败**重新排队**（同图格语义，避免「取出即丢」）。

**测试**：+1（**249 通过**）——`Vanilla_ChestContent_Survives_ServerRestart`：
造一份含箱子的 `.wld` 作为基准世界 → 经真实包 32 权威链路写入物品 → 落盘 → **复用同一 DB 重启** → 箱子内容被回放。
默认后端与 `-p:NoSqlite=true` 兜底后端均 249/249 通过。

### 第十一轮（2026-09-12）：断线宽限期会话保留 + 连接槽位回收

**问题**（两项，均为本轮回溯自查发现）：

1. **断线即销毁**：连接断开时玩家运行时被直接释放，位置 / 血量 / 增益全部丢失，
   重连只能从世界出生点以满血重新开始 —— 对「短暂掉线后重进」的体验是硬伤。
2. **连接槽位从不回收（真实缺陷）**：`ConnectionManager` 只在 `KickAsync` 里移除连接，
   **正常断开的连接会永久留在 `_connections`** —— 槽位（`PlayerId`）与并发容量都不释放。
   后果：玩家 ID 只增不减（与原版「复用最小空闲槽位」不一致），且累计达 `MaxConnections` 次连接后新玩家被拒之门外。
   该缺陷由本轮新增测试暴露（重连拿到的是新槽位，而非原槽位）。

**已实施**（全部自研）：

- **会话保留（宽限期）**：`WorldState` 新增离线会话表（`_offlineSessions`，受 `PlayersLock` 保护）。
  断线时 `MarkPlayerOffline` 把运行时移出在线集合、置 `Active=false`、清零速度，并按
  `ServerConfig.SessionResumeGraceSeconds`（默认 60s）记录回收截止 tick；宽限期为 0 即直接回收（同时也修掉「运行时不释放」的泄漏）。
- **同身份接管**：`TryResumePlayer` 在宽限期内按**恢复键（玩家名）**认回运行时 ——
  位置 / 血量 / 增益原样交还，改挂到新连接分配到的槽位；出生包（12）改发**恢复后的坐标**（而非世界出生点），客户端落回原位。
- **过期回收**：仿真循环每 tick 调 `ReapOfflineSessions` 回收超期会话（含被顶号场景），不留幽灵运行时。
- **移动基线清理**：断线时经 `IInboundPipeline.ResetPlayer` 清掉该槽位的移动校验基线。
  分片管线把「按玩家重置」投递到**同一队列 / 同一分片**，保证该玩家在途包先处理完再清状态 ——
  否则在途的旧位置包会把基线重新写回，重连玩家首个位置包仍被判超速。
- **连接槽位回收**：`NetworkHost.RunConnectionAsync` 在连接结束时调用 `_connections.RemoveAsync`（释放槽位 + 并发容量）；
  回收先于退出清理，使重连能拿到同一槽位。`RemoveAsync` 增加「键 + 实例」双匹配（`ICollection.Remove`），
  避免槽位已被新连接复用时**误杀新连接**（被踢连接在 `RunAsync` 结束后会再次走到该路径）。

**与原版的差异（明确限制）**：原版客户端**断线即回主菜单**（`Netplay.Disconnect=true`），服务端无任何自动重连机制；
故本实现是「**手动重进的会话接管**」（宽限期内以同身份重连 → 继承状态），而非自动重连 / 状态续传。

**测试**：+3（**248 通过**）——`Vanilla_SessionResume_Restores_Position_And_Hp`（断线后同身份重连续回位置 / 血量，出生包携带恢复坐标）、
`Vanilla_SessionResume_Off_When_Grace_Is_Zero`（宽限期 0 → 不保留，重连为全新满血运行时）、
`SessionResume_Expires_After_Grace`（越期回收 + 不再认回）。默认后端与 `-p:NoSqlite=true` 兜底后端均 248/248 通过。

### 第十轮（2026-09-11）：世界文件（`.wld`）加载 / 导出 + 增量落盘加固（含性能实测）

**问题**（三项）：

1. 基准世界恒为程序化生成 —— `.wld` 解析器虽已实现却从未被使用，**无法在真实世界上开服**（W-1）。
2. 只有增量落盘、没有可携带的整份产物：备份 / 迁移 / 换基准世界都缺一环。
3. 增量落盘的**待处理集合无上限**，单批上限写死 2048，超大范围改动会排队过久。

**已实施**（全部自研实现，未搬用第三方源码）：

- **基准世界可配置**：`ServerConfig.WorldPath` 指定 `.wld` 即加载（为空 / 文件不存在则回退程序化生成并打印提示）；
  `Bootstrap` 抽出 `LoadBaseWorld` 统一入口。世界改动持久化对新基准同样生效（增量叠加）。
- **`.wld` 写出器**（`Simulation/World/WorldFileWriter.cs`）：与自身读取器**布局严格对称**的自研编码器 ——
  文件元数据 + 11 个分段指针（先占位、写完回填）+ 图格重要度位图 + 头部（含全部世界旗标）+ 图格 + 箱子 + 告示牌 + NPC + footer。
  图格编码支持全部特征位（方块 / 墙（含高位）/ 液体四类 / 四色线 / 执行器 / 斜坡 / 半砖 / 油漆 / 隐形 / 全亮 / 双字节类型）。
- **写后校验 + 原子替换**：`Write()` = 序列化到内存 → 写临时文件 → **用自身读取器读回校验** → 通过才替换目标，
  替换前把旧文件滚动为 `.bak`；校验失败保留原文件并抛错，避免写出「读不回来」的世界。
- **导出触发**：`WorldExportPath` 非空时，停机导出一次（`force`），并在**无人在线**时按
  `WorldExportIntervalSeconds`（默认 600s）周期导出 —— 全量序列化是 O(世界大小)，故有意避开在线时段；
  在线可靠性仍由增量日志负责。
- **增量落盘加固**：单批上限 2048 → **8192**（`WorldState.PersistBatchSize`）；
  待处理集合加上限（262144，约 1 MB），**溢出时降级为「全图游标扫描」模式**（内存有界、最终一致，走完一遍自动回到正常模式）；
  新增 `HasPendingPersist`，停机时**循环冲刷到排空**（轮数有上界，不会挂死）。
- **性能实测**（小世界 4200×1200，见下表）用于选型：增量成本 ∝ 改动量，全量成本 ∝ 世界大小。
- **测试**：+4（**244 通过**）——`Wld_Write_Then_Read_RoundTrips_All_Tile_Features`（64×32 特征世界逐格往返，
  覆盖全部标志位 / 液体 / 油漆 / 高位墙 / 双字节类型）、`..._Header_Flags`（世界旗标 / 模式 / 时间 / 沙尘强度）、
  `Vanilla_WorldFile_Is_Used_As_Base_World`（配置 `.wld` → 开服基准世界来自文件且登录链正常）、
  `Vanilla_World_Is_Exported_To_Wld_When_Configured`（导出落地并可被读回；未配置则不导出）。
  默认后端与 `-p:NoSqlite=true` 兜底后端均 244/244 通过。

**性能实测数据**（用于方案选型）：

| 场景 | 实测 |
|---|---|
| 增量：无改动批次 | 0.00 ms |
| 增量：满批落盘（8192 格，SQLite） | ≈ 4×23.5 ms（线性于批大小） |
| 增量：登记一格 | ≈ 45 ns |
| 全量：遍历 504 万格 + 序列化 | ≈ 178 ms（含每格分配；真实写出器可复用缓冲更快） |
| 全量：Deflate 压缩（该世界 72 MB → 1 MB） | ≈ 28 ms |

**本轮补正（接续第十轮）**：

- **修掉一处会让原版拒绝加载的缺陷**：原先段 5..9（图格实体 / 加权压力板 / 城镇房间管理 / 图鉴 / 创造之力）
  的位置全部指向 footer（即「空段」）。本服务端读取器会跳过这些段，但**原版加载器逐段读取并要求
  「读完正好落在下一段起点」**，空段会被判 `BadSectionPointer`。现按各段语义写出**合法空编码**：
  三段各一个 `Int32 0`、图鉴三个 `Int32 0`、创造之力一个 `false`。
- **更强的验证**：新增 `Wld_Written_File_Passes_Strict_Section_Walk` —— 按**原版加载器的顺序与位置断言**逐段走查
  （段起点严格递增、每段读完正好落在下一段起点、footer 读完正好用尽文件），补上 round-trip 覆盖不到的 5..9 段。
- **原版二进制实测未完成**：本机装有 TerrariaServer.exe，但**在当前沙箱环境无法运行** ——
  用本服务端导出的世界启动会崩溃，**用原版 `-autocreate` 自建世界启动同样崩溃、且零输出**，
  故判定为**环境限制而非文件格式问题**；真实原版加载验证仍待在有图形 / 交互控制台的环境执行。

**测试**：245 通过（含上述严格走查）。默认后端与 `-p:NoSqlite=true` 兜底后端均 245/245。

### 第九轮（2026-09-11）：世界改动持久化（重启不丢建筑）+ 偶发测试加固

**问题**（本轮自查发现，此前文档未列）：服务端只有 `.wld` **读取器**、没有写回，且 `Bootstrap` 恒走程序化生成 ——
**玩家挖 / 放 / 灌液体 / 铺线在服务端重启后会全部丢失**，这对任何长期开服都是硬伤。

**已实施**：

- **世界改动增量落盘**：新增 `IWorldRepository`（`SaveTileChangesAsync` / `LoadTileChangesAsync`）与
  `WorldTiles` 表（SQLite）/ `WorldTiles` 段（内嵌兜底），随 `IDbExecutor` 双后端实现。
  图格以 **15 字节定长编码**（`Tile.Serialize` / `Deserialize`）存储，一份记录含方块 / 墙 / 液体 / 电线 / 执行器 / 油漆 / frame。
- **登记点**：`WorldState.MarkPersistTile` 挂在**所有真实写入点** —— 客户端发起的 `TileBreakCommand`（挖 / 放砖 / 墙 / 线网 / 执行器）、
  `TilePlaceCommand`，服务端驱动的执行器翻转（`MarkTileChanged`）、液体流动与混合反应（`MarkLiquidChanged`）。
  待落盘集合按坐标去重（`HashSet`），**取出即移除**；落盘失败会**重新排队**重试，避免「取出即丢」。
- **回放**：`Bootstrap` 生成基准世界后调用 `ApplyPersistedWorldChanges`，把上次运行落盘的图格叠加回去
  （基准世界确定性 → 只需存增量，无需写回 `.wld`）。
- **落盘时机**：1Hz 世界同步循环（单批上限 2048 格，超限留待下轮）+ `RunAsync` 退出时冲刷一次。
- **偶发测试加固**：`VanillaSession.ReadUntilAsync` 补 `IOException` / `SocketException` 处理
  （服务端踢出连接的瞬间还在读 socket 会抛异常，导致 `AntiCheat_PacketFlood_...` 偶发失败）。
- **测试**：+1（**240 通过**）——`Vanilla_WorldEdits_Survive_ServerRestart`（挖一格 → 落盘 → **复用同一 DB 重启** → 该格仍为空）。
  默认后端与 `-p:NoSqlite=true` 兜底后端均 240/240 通过。

**已评估但未纳入**：箱子内容持久化 —— 当前基准世界**不含箱子**且包 34（放置箱子）未建模，落盘路径不可达，
故不留死代码；待 W-1（加载真实 `.wld`）落地后一并补。

**新增立项**：W-1「支持加载真实 `.wld` 世界」——`WorldFileReader` 已实现却从未被 `Bootstrap` 使用，目前无法在真实世界上开服。

### 第八轮（2026-09-11）：反作弊闭环补全 —— 法力 / 治疗 / 增益 / 弹幕生成权威化

**问题**：项目定位是「把客户端可伪造的状态收回服务端」，但仍有四个包**只做限流、不做语义校验**：
包 42（法力）、包 35（治疗）、包 50（增益）、包 27（弹幕生成）。其中包 27 的伤害直接来自客户端声明，
可被用来一击秒杀任意敌怪；法力则完全未在服务端跟踪（玩家快照里 `Mp / MaxMp` 恒为 0）。

**数值核对**（按原版客户端协议 326 逐项核对，仅记录结论）：
增益槽位数 44；有效增益 ID 区间 [1, 400]；有效弹幕类型区间 [1, 1135]；
包 16 线格式仅含生命（不含法力），法力单独走包 42（`Byte 玩家 + Int16 法力 + Int16 上限`）。

**已实施**：

- **法力服务端跟踪（包 42）**：新增 `PlayerManaPacket` + 编解码 + `SetManaCommand`；权威层 `ValidateMana`
  非负校验、上限由服务端持有（超出即「纠正」下发权威包）、当前法力不得高于上限。按原版语义**不向他人转发**。
  玩家快照（`IServerApi`）的 `Mp / MaxMp` 由恒 0 改为真实值。
- **治疗上限钳制（包 35）**：权威层非负校验（负治疗拒绝）→ `HealPlayerCommand` 把回血上限钳制到服务端 `HpMax`，
  客户端声明的超额治疗不会让服务端生命越界。
- **增益服务端持有（包 50）**：权威层校验条目数 ≤ 44 且 ID ∈ [1,400]（越界拒绝）→ `SetBuffsCommand`
  把列表写入服务端玩家状态（唯一真相）。
- **弹幕生成校验（包 27）**：权威层新增 `ValidateProjectile` —— 弹幕类型须在 [1,1135]，伤害超配置单次伤害上限
  （与包 28 共用阈值）即判为作弊**拒绝**，关闭「声明 32767 一击秒杀」的通道；通过后仍由服务端登记实体并推进生命周期。
- **文档一致性**：修正 `VANILLA_COVERAGE.md` §二「无 NPC 专属 AI 与接触伤害」与 §一 已实装接触伤害的自相矛盾。
- **测试**：+8（**239 通过**）——法力跟踪 / 法力上限纠正 / 治疗钳制 / 负治疗拒绝 / 增益持有 / 越界增益拒绝 /
  超上限弹幕伤害拒绝 / 越界弹幕类型拒绝。默认后端与 `-p:NoSqlite=true` 兜底后端均 239/239 通过。

**仍为简化模型**：包 27 伤害仍由客户端声明，服务端只做**上界校验**（原版伤害由武器 / 装备推导，未建模武器表）；
增益的**效果**仍由客户端计算（服务端只维护列表）；治疗来源未做「是否有合法治疗来源」的溯源。

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
