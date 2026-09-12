# TerraAuth

**Terraria 协议的服务端权威代理 / 反作弊框架**

原版 Terraria 是纯客户端权威架构——位置、血量、伤害全部由客户端上报，服务端只做转发。任何 CE / CheatEngine 用户可以直接写内存改坐标、改血量、改伤害，原版服务器几乎没有拦截能力。

TerraAuth 的目标是在**协议层**把关键状态收回服务端，提供可靠的反作弊能力：客户端只能声明意图，所有状态变更必须经过服务端权威校验后才生效。

## 当前进度

| 阶段 | 内容 | 状态 |
|---|---|---|
| Phase 0 | 协议兼容骨架（TCP Server + Framing + 握手） | ✅ 完成 |
| Phase 1 | 玩家权威（HP/MP/位置/速度 + 限速） | ✅ 位置 / 血量 / **法力（包 42，含上限纠正）** / 移动限速均已落地 |
| Phase 2 | 物品 & 战斗权威 | ✅ 物品 ID / 堆叠校验 + SSC 背包对账 + **箱子内容服务端持有**（开箱下发权威内容、包 32 校验落盘）；单次伤害上限 + DPS 窗口；受伤→死亡→复活（`DamagePlayerCommand` / `KillPlayerCommand` / `RespawnCommand`）+ 弹幕命中判定 + 掉落物拾取（包 22）均服务端结算 |
| Phase 3 | 世界权威（tile / 液体 / 抛射物 / 电路） | ◐ tile（挖 / 放）全链路已落地；抛射物 / 掉落物已跟踪并结算命中与拾取；**液体**逐格简化流动 + 混合反应（水 + 岩浆 → 黑曜石等）+ NetLiquid 同步；**电路**线网 / 执行器编辑权威 + 受限 BFS 信号传播；**简化 Boss / 事件**（血月 · 日食 · 入侵 · 击杀进度 + 掉落 → 包 7 广播 / 包 21 掉落） |
| Phase 4 | 一致性 & 性能（快照 + WAL + 断线重连） | ◐ 快照包 15 + 视野裁剪 + 影子预测已实现；**断线宽限期会话保留已落地**（同身份重连续回位置 / 血量 / 增益）；WAL 待定 |
| **Phase 5** | **TCP 传输层（NetworkHost + Connection + 编解码）** | **✅ 完成并实测** |
| Phase 6 | 基础设施（持久化 / 审计 / 监控） | ✅ 配置热重载 / 审计 / Prometheus / 封禁已实装；真实 SQLite 落库（玩家 / 审计 / 封禁）完成 |
| Phase 7 | 红队对抗测试 | ◐ 服务端可自动化部分已落为测试（P1/P2/P3/P4/P5·M6/M1/M3/M4/M9 见 `Phase7-RedTeam/README.md` §零），CE 改内存类仍需手工实验 |
| 扩展 | 插件系统 / Mod 兼容层 / 多线程优化 | ✅ 已整合（Hook 触发写时复制 · 每包零分配） |

> 更细的模块状态与待办：见 [`terraauth/README.md`](terraauth/README.md) §模块实现状态；
> **原版功能覆盖现状（哪些已实现 / 未实现）**：见 [`terraauth/VANILLA_COVERAGE.md`](terraauth/VANILLA_COVERAGE.md)；
> 未实施的优化 / 补全项：见 [`terraauth/OPTIMIZATION_BACKLOG.md`](terraauth/OPTIMIZATION_BACKLOG.md)。

图例：✅ 完成 / ◐ 部分实现 / ⏳ 未实施

## 已实测能力

- ✅ 原版 Terraria 客户端（协议 326）连接 → 握手 → 进入世界 → 正常断开
- ✅ 双客户端同时进服，互相可见（需正确下发包 14 `PlayerActive`）
- ✅ 位置包（包 13）→ 权威校验 → 仿真 → 快照（包 15）经 TCP 下发完整闭环
- ✅ 移动广播：A 发包 13 → B 经 TCP 收到转发，位置一致
- ✅ 移动速度校验：**分轴**判定 —— 水平 `maxSpeed × 60 × Δt + 容差`，垂直 `max(maxSpeed, MaxFallSpeed) × 60 × Δt + 容差`（Δt 钳制在 `[1/60s, 10s]`，不做静默超时无条件放行）
- ✅ 区块流送：包 8 在**游戏内也处理**，并由快照循环按玩家位置补发周边区块（**3×3**，跨区块才发、已发过的区块不重复编码）—— 离开出生点后地形正常
- ✅ 图格推送与原版一致：**少量图格改动**（执行器翻转 / 液体混合）走**包 20（TileSquare）**，**区块级地形**走**包 10（TileSection）**
- ✅ 未建模包边界：未结构化的客户端包默认拒绝，不绕过权威管线即时转发；状态包在仿真提交后由服务端生成同步包
- ✅ Tile（挖 / 放方块）服务端权威全链路：校验 → Command → 仿真 → 增量广播 → SSC 背包扣减
- ✅ 违规处置闭环：窗口内权威拒绝累计达阈值 → 下发包 2 后踢出连接
- ✅ 断线会话保留：断开不销毁运行时，宽限期（`SessionResumeGraceSeconds`，默认 60s）内**同身份重连续回位置 / 血量 / 增益**，出生包下发恢复坐标；连接槽位与并发容量随即释放并复用最小空闲 ID（原版客户端断线即回主菜单，故为「手动重进的会话接管」）
- ✅ 持久化落盘：玩家存档 / 审计 / 封禁写入 SQLite，重启后仍在（封禁不再重启即失效）；**世界改动（挖 / 放 / 液体 / 电线 / 执行器）与箱子内容增量落盘并在启动时回放，玩家建筑与箱子内容重启不丢**
- ✅ 世界文件（`.wld`）：`WorldPath` 指定即可**用真实世界开服**；未指定时按 `WorldSize` **程序化生成三档尺寸**（`Small` 4200×1200 / `Medium` 6400×1800 / `Large` 8400×2400，与原版三档一致），地形为**分层生成**（洞穴 / 矿脉 / 海滩与海水 / 地狱层 / 地下宝箱）；`WorldExportPath` 启用后停机 + 空服导出整份 `.wld`（写出器带写后读回校验 + 原子替换 + `.bak` 滚动）
- ✅ 世界同步与聊天：时间（包 18）/ NPC（包 23）定期下发；聊天（包 82 NetTextModule）可收发，`IServerApi.Broadcast/SendMessage` 真实生效
- ✅ 他人可见性：移动 / 挖放砖 / 受伤 / 增益 / 弹幕 / 掉落物按协议包中继（带玩家字段的包以服务端 ID 覆盖）
- ✅ 战斗 / 生存权威：受伤（117）非负校验 + 服务端生命结算 → 死亡广播（118）；**敌怪 / Boss 接触伤害由服务端判定**（32px + 60tick 免伤帧，下发 117 表现 + 16 权威血量）；复活（12）由服务端划定复活点；弹幕命中判定与掉落物拾取（22）均在服务端结算；**法力（42）服务端跟踪 + 上限纠正、治疗（35）回血上限钳制到服务端 HpMax、增益（50）服务端持有列表、弹幕生成（27）类型与伤害上界校验**
- ✅ 世界内容权威：箱子内容服务端持有（开箱下发权威内容 + 包 32 校验落盘）；液体 NetLiquid 逐格流动仿真与编辑校验（同步按视口裁剪）+ 混合反应（异种液体累计 ≥ 24 → 黑曜石 / 蜂蜜块 / 松脆蜂蜜块 / 微光块）；电路线网 / 执行器编辑权威 + 受限 BFS 信号传播 + 图格变更推送；简化 Boss / 事件（血月 · 日食 · 入侵 · 击杀进度 + 按已核对 NPC ID 记录进度并生成掉落，经包 21 补发）
- ✅ 健壮性与运维：区块编码超 `UInt16` 帧上限时自动拆分（不中断登录）；阈值量纲启动校验；服务端命令子系统（say / who / kick / help，插件可扩展）；`/metrics` 支持自定义 Gauge；插件事件查询接持久化审计；Phase 7 对抗清单的服务端可自动化部分已落为测试

## 快速启动

前置：.NET 10 SDK

```bash
cd terraauth
dotnet run --project TerraAuth.csproj -- --config server.json
# 默认监听 127.0.0.1:7777
# Prometheus 指标在 http://127.0.0.1:9090/metrics
```

用原版 Terraria 客户端（协议 326）连接 `127.0.0.1:7777` 即可进入世界。

世界来源由 `server.json` 决定：`WorldPath` 指定 `.wld` 则加载真实世界；为空时按 `WorldSize`（`Small` / `Medium` / `Large`）程序化生成，默认 `Small`。

## 技术栈

| 层 | 技术 |
|---|---|
| 传输 | `System.IO.Pipelines` + `Channel<>` 异步管线，Worker 池处理 |
| 协议 | 原生 Terraria 1.4.5.8（协议 326），位置包 13 / 快照包 15 为核心 |
| 仿真 | 确定性 GameLoop，20Hz 快照频率，Command 模式应用变更 |
| 权威 | `MovementAuthority`（位置超速 + 传送频率）、`PlayerAuthority`（属性只读） |
| 监控 | Prometheus 指标 + 结构化审计日志（SQLite / 内嵌 LiteDb） |
| 语言 | C# 14 + .NET 10 |

## 代码结构

```
terraauth/
├─ Authority/           # 权威管线（限流 / 库存 / 移动 / 战斗 / 属性 / 世界 + 分片并行）
│  ├─ AuthoritySubsystems.cs   # 六子系统默认实现
│  ├─ InboundPipeline.cs       # 权威 → Command 转换
│  └─ AuditLogger.cs           # 结构化日志
├─ Simulation/          # 仿真 + GameLoop + Command 模型
│  ├─ WorldSimulator.cs        # 世界状态（六阶段 tick）
│  ├─ CommandQueue.cs          # Move / TileBreak / TilePlace 等
│  └─ World/                   # Tile / TileIdSets / WorldState / .wld 解析
├─ Net/
│  ├─ Phase4/                  # 快照构造 + 视野裁剪 + 影子预测
│  └─ Phase5/                  # TCP 传输 + Framing + 编解码
├─ Protocol/            # Terraria 包 ID 与类型定义
├─ Config/              # 阈值配置 + FileSystemWatcher 热重载
├─ Persistence/         # 持久化（默认内嵌 LiteDb / 可选 SQLite）
├─ Monitoring/          # Prometheus 指标 + /metrics 端点
├─ Security/            # 封禁（滑动窗口 + 存储）
├─ Plugins/             # 插件系统（Hook / 加载器 / 管线装饰）
├─ ModCompat/           # 未来 MOD 兼容层（当前生产禁用）
├─ Concurrency/         # 并行优化（Worker 池 / 分片 / 快照并行）
├─ Tests/               # xUnit 验收测试（283 用例）
└─ server.json          # 阈值配置
```

## 移动权威判定

```
allowedX = MaxSpeed × 60 × Δt + TeleportTolerance
allowedY = max(MaxSpeed, MaxFallSpeed) × 60 × Δt + TeleportTolerance
Δt = ClampDt(now - state.LastSeenAt)   // 钳制 [1/60, 10] 秒
if (|dx| > allowedX || |dy| > allowedY) → Reject("speed_exceeded")，拒绝时不更新权威基准
```

- **分轴判定**：水平用 `MaxSpeed = MaxFlightSpeed = 8.0`（像素/帧）；**垂直用 `max(MaxSpeed, MaxFallSpeed)`**
  （`MaxFallSpeed = 20.0`）。原版下落终速约 20 px/帧，若垂直也按 8 判定，**任何一次正常坠落都会被误判超速**
  → 服务端位置不再更新（后续挖 / 放 / 交互全部 out_of_reach）且累计违规被踢（默认 10 次 / 60s）。
- `TeleportTolerance = 4` 像素；长时静默（>10s）一律按 10s 计（水平上限 `4804px`），**不做无条件放行**（防穿墙 / 瞬移）
- **已知限制**：客户端失焦时位置包间隔可达 4~7s（客户端降频），**水平**静默位移过大仍可能被误判；
  彻底解决需服务端权威移动 / 碰撞校验（Phase 3 世界权威落地后补齐）

## 许可

本项目仅为学习 / 研究用途，与 Re-Logic 官方无关。Terraria 协议实现参考社区规范。
