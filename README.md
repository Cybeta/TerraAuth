# TerraAuth

**Terraria 协议的服务端权威代理 / 反作弊框架**

原版 Terraria 是纯客户端权威架构——位置、血量、伤害全部由客户端上报，服务端只做转发。任何 CE / CheatEngine 用户可以直接写内存改坐标、改血量、改伤害，原版服务器几乎没有拦截能力。

TerraAuth 的目标是在**协议层**把关键状态收回服务端，提供可靠的反作弊能力：客户端只能声明意图，所有状态变更必须经过服务端权威校验后才生效。

## 当前进度

| 阶段 | 内容 | 状态 |
|---|---|---|
| Phase 0 | 协议兼容骨架（TCP Server + Framing + 握手） | ✅ 完成 |
| Phase 1 | 玩家权威（HP/MP/位置/速度 + 限速） | ✅ 位置 / 血上限 / 移动限速已落地（MP 未跟踪） |
| Phase 2 | 物品 & 战斗权威 | ✅ 物品 ID / 堆叠校验 + SSC 背包对账；单次伤害上限 + DPS 窗口（简化模型） |
| Phase 3 | 世界权威（tile / 液体 / 抛射物 / 电路） | ◐ tile（挖 / 放）全链路已落地（校验 → Command → 仿真 → 增量广播 → SSC 扣减）；液体 / 电路 / 抛射物待实施 |
| Phase 4 | 一致性 & 性能（快照 + WAL + 断线重连） | ◐ 快照包 15 + 视野裁剪 + 影子预测已实现；WAL / 断线重连待定 |
| **Phase 5** | **TCP 传输层（NetworkHost + Connection + 编解码）** | **✅ 完成并实测** |
| Phase 6 | 基础设施（持久化 / 审计 / 监控） | ✅ 配置热重载 / 审计 / Prometheus / 封禁已实装；真实 SQLite 落库（玩家 / 审计 / 封禁）完成 |
| Phase 7 | 红队对抗测试 | ⏳ 手册就绪（`Phase7-RedTeam/`），尚未执行 |
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
- ✅ 移动速度校验：`maxSpeed × 60 × Δt + TeleportTolerance`（Δt 钳制在 `[1/60s, 10s]`，不做静默超时无条件放行）
- ✅ Tile（挖 / 放方块）服务端权威全链路：校验 → Command → 仿真 → 增量广播 → SSC 背包扣减
- ✅ 违规处置闭环：窗口内权威拒绝累计达阈值 → 下发包 2 后踢出连接
- ✅ 持久化落盘：玩家存档 / 审计 / 封禁写入 SQLite，重启后仍在（封禁不再重启即失效）

## 快速启动

前置：.NET 10 SDK

```bash
cd terraauth
dotnet run --project TerraAuth.csproj -- --config server.json
# 默认监听 127.0.0.1:7777
# Prometheus 指标在 http://127.0.0.1:9090/metrics
```

用原版 Terraria 客户端（协议 326）连接 `127.0.0.1:7777` 即可进入世界。

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
├─ ModCompat/           # Mod 兼容层（策略 / 检测 / 自定义包）
├─ Concurrency/         # 并行优化（Worker 池 / 分片 / 快照并行）
├─ Tests/               # xUnit 验收测试（160 用例）
└─ server.json          # 阈值配置
```

## 移动权威判定

```
allowed = maxSpeed × 60 × Δt + TeleportTolerance
Δt = ClampDt(now - state.LastSeenAt)   // 钳制 [1/60, 10] 秒
if (distance > allowed) → Reject("speed_exceeded")，拒绝时不更新权威基准
```

- `MaxFlightSpeed = 8.0`（像素/帧），`TeleportTolerance = 4` 像素
- 长时静默（>10s）一律按 10s 计，单包允许位移上限 `4804px`，**不做无条件放行**（防穿墙 / 瞬移缺口）
- **已知限制**：客户端失焦时位置包间隔可达 4~7s（客户端降频），若静默 >30s 且位移 >4804px 会被误判为超速，且拒绝不更新基准会导致后续包连续被拒（原「基准冻结」现象），彻底解决需服务端权威移动 / 碰撞校验（Phase 3 世界权威落地后补齐）

## 许可

本项目仅为学习 / 研究用途，与 Re-Logic 官方无关。Terraria 协议实现参考社区规范。
