# TerraAuth

**Terraria 协议的服务端权威代理 / 反作弊框架**

原版 Terraria 是纯客户端权威架构——位置、血量、伤害全部由客户端上报，服务端只做转发。任何 CE / CheatEngine 用户可以直接写内存改坐标、改血量、改伤害，原版服务器几乎没有拦截能力。

TerraAuth 的目标是在**协议层**把关键状态收回服务端，提供可靠的反作弊能力：客户端只能声明意图，所有状态变更必须经过服务端权威校验后才生效。

## 当前进度

| 阶段 | 内容 | 状态 |
|---|---|---|
| Phase 0 | 协议兼容骨架（TCP Server + Framing + 握手） | ✅ 完成 |
| Phase 1 | 玩家权威 MVP（HP/MP/位置/速度 + 限速） | ⚠️ 位置权威已落地，HP/MP 只读参考 |
| Phase 2 | 物品 & 战斗权威 | ⏳ 待实施 |
| Phase 3 | 世界权威（tile / 液体 / 抛射物 / 电路） | ⏳ 待实施 |
| Phase 4 | 一致性 & 性能（快照 + WAL + 断线重连） | ⏳ 快照已实现，其余待定 |
| **Phase 5** | **TCP 传输层（NetworkHost + Connection + 编解码）** | **✅ 完成并实测** |
| Phase 6 | 基础设施（持久化 / 审计 / 监控） | ⚠️ 骨架就位 |
| Phase 7 | 红队对抗测试 | ⏳ 待实施 |

## 已实测能力

- ✅ 原版 Terraria 客户端（协议 326）连接 → 握手 → 进入世界 → 正常断开
- ✅ 双客户端同时进服，互相可见（需正确下发包 14 `PlayerActive`）
- ✅ 位置包（包 13）→ 权威校验 → 仿真 → 快照（包 15）经 TCP 下发完整闭环
- ✅ 移动广播：A 发包 13 → B 经 TCP 收到转发，位置一致
- ✅ 移动速度校验：`maxSpeed × 60 × Δt + TeleportTolerance`（Δt 钳制在 `[1/60s, 10s]`，不做静默超时无条件放行）

## 快速启动

前置：.NET 8 SDK

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
| 监控 | Prometheus 指标 + 结构化审计日志（SQLite / 控制台） |
| 语言 | C# 12 + .NET 8 |

## 代码结构

```
terraauth/
├─ Authority/           # Phase 2：权威管线（移动 / 传送 / 属性）
│  ├─ AuthoritySubsystems.cs   # MovementAuthority（核心）
│  ├─ InboundPipeline.cs       # 权威 → Command 转换
│  └─ AuditLogger.cs           # 结构化日志
├─ Simulation/          # Phase 3：仿真 + GameLoop + Command 模型
│  ├─ WorldSimulator.cs        # 世界状态
│  ├─ CommandQueue.cs          # MoveCommand 等
│  └─ World/                   # Tile / WorldState
├─ Net/
│  ├─ Phase4/                  # 快照构造 + 视野裁剪
│  └─ Phase5/                  # TCP 传输 + Framing + 编解码
├─ Protocol/            # Terraria 包 ID 与类型定义
├─ Plugins/             # 插件 Hook（预留）
├─ Tests/               # xUnit 验收测试
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
