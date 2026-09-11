# Phase 5 交付说明

> 完整接入 terraria-protocol，实现 TCP 监听 + 编解码 + 连接管理
> 至此 Phase 2/3/4/5 全链路骨架完成，可编译、可测试

---

## 本 Phase 新增文件（8 个）

| 文件 | 职责 |
|---|---|
| `ITerrariaProtocol.cs` | 协议版本（326）+ 包 ID 映射 + 能力声明 |
| `Framing.cs` | TCP 分帧（长度前缀），处理半包/粘包 |
| `PacketDecoder.cs` | 字节 → INetworkPacket（terraria-protocol 布局） |
| `PacketEncoder.cs` | SnapshotFrame/包 → 字节帧 |
| `Connection.cs` | 单连接状态机（Handshake→Playing→Disconnected） |
| `ConnectionManager.cs` | 连接池、超时、并发上限（默认 64） |
| `ISnapshotSender.cs` | 出站抽象（Phase 4 SnapshotBroadcaster 依赖） |
| `NetworkHost.cs` | **★ 核心**：TcpListener + Accept + 管线调度 |

> 注：`TerraAuth.csproj` 为全工程合并后的单工程文件，非本 Phase 新增。

---

## 串联关系（三阶段贯通）

```
客户端 bytes
   ↓
[Framing] 解决半包/粘包
   ↓
[PacketDecoder] bytes → INetworkPacket
   ↓
[NetworkHost.OnPacketAsync]
   ↓
[IInboundPipeline] Phase 2 权威校验
   ↓ Accept → [CommandQueue] Phase 3
                ↓
         [WorldSimulator.Tick] 确定性仿真
                ↓ Output
         [SnapshotBuilder.Build] 强类型快照
                ↓
         [SnapshotBroadcaster] Phase 4
                ↓
         [ConnectionSnapshotSender] ISnapshotSender
                ↓
         [PacketEncoder] SnapshotFrame → bytes
                ↓
         [Framing.WriteFrame] 长度前缀
                ↓
         客户端 bytes
```

**关键闭环**：`GameHost.cs` 把 Phase 2/3/4/5 全部组装——`BuildPipeline` 定义管线阶段顺序（限流→库存→移动→战斗→属性→世界→终结），`RunAsync` 并发启动网络监听 + 游戏循环 + 快照下发。

---

## 对接 terraria-protocol 的关键点

1. **包 ID 映射**：`PacketId` 枚举数值对齐 terraria-protocol 的 `MessageId`
2. **二进制布局**：小端字节序 + 长度前缀 + 特殊类型（Vector2/Color/Rectangle）
3. **版本协商**：`ConnectionRequest` 校验协议号，不匹配立即断开
4. **协议更新**：每次 Terraria 更新客户端 → 改 `ITerrariaProtocol` + Phase 2 对应包处理（影响被隔离在本层）

---

## 下一步优先级

### P0（先打通端到端）
1. ◐ **跑通第一条完整链路**：原版 Terraria 客户端已实测「连接 → 协议协商 → 进入世界」（2026-09-09，见下）；位置包 → Phase 2 → Phase 3 → 快照下发的真实 TCP 往返仍待补（`IntegrationTests` 已覆盖简化闭环）
2. ✅ `Framing` 已完成（半包/粘包测试通过）
3. ✅ `PacketDecoder` 已解析 28 个入站包（握手链 + 权威白名单 10 包 + 伤害/死亡/传送等）；包 15 `Snapshot` 解码已补（含 `BaseTick`/`Removed`）
4. ⚠️ **补齐剩余 200+ 包解析**（参考 terraria-protocol 各 Message 的 read 实现）
5. ✅ `NetworkHost` Accept 循环 + 管线贯通

### P1（完善）
- ✅ 变长整数（VarInt）编解码（`Read7BitEncodedInt` / `Write7BitEncodedInt`）
- ⚠️ 特殊类型（Color/Rectangle）读写器（Rectangle 待补）
- ✅ 连接认证（`NetworkHost` 按 `config.Current.PlayerWhitelist` 校验；SteamTicket 未实现）
- ✅ 包 15 `Snapshot` 编解码闭环（`EncodeSnapshot` / `DecodeSnapshot` 往返一致）
- ⏳ 性能基准（100 玩家，带宽/CPU 预算）

---

## 实测记录

| 日期 | 场景 | 结果 |
|---|---|---|
| 2026-09-09 | 原版 Terraria 客户端（协议 326）连接 `127.0.0.1:7777` | ✅ 握手成功 → 解析玩家名 → `InitialSpawn`/`FinishedConnecting` → 进入世界（出生点 2100,352）→ 正常断开，无异常/畸形包 |
| 2026-09-09 | 真实 TCP 往返集成测试 `TcpRoundTrip_Packet13_Reaches_SnapshotOverWire` | ✅ `NetworkHost` 监听 → `TcpClient` 握手至 Playing → 发包 13 → `MoveCommand` → 仿真 → 快照包 15 经 TCP 下发，玩家位置与上报匹配 |
| 2026-09-10 | 双客户端移动广播集成测试 `TcpRoundTrip_Packet13_IsForwarded_ToOtherPlayers` | ✅ 两 `TcpClient` 分别握手至 Playing → A 发包 13（伪造 PlayerId=7）→ B 经 TCP 收到转发的包 13，身份被覆盖为服务端分配的 #1，位置一致 |
| 2026-09-10 | 对端移动卡顿修复：`MovementAuthority` 速度量纲 | ✅ `MaxSpeed` 为 Terraria 像素/帧量纲（`MaxWalkSpeed=3.6`/`MaxFlightSpeed=8.0`），原按 `maxSpeed * dt(秒)` 误算成像素/秒，每包允许位移仅剩 `TeleportTolerance=4` 像素 → 正常移动被判 `speed_exceeded` 且不转发。改为 `maxSpeed * 60 * dt + tolerance`，`MinDtSeconds` 取 1/60 避免批量处理低估 dt；新增回归测试 `MovementAuthority_Accepts_NormalFrameStep_NotOnly_TinyMove` |
| 2026-09-10 | 移动权威收紧：Δt 上限放宽但取消静默无条件放行 | ✅ 客户端失焦时位置包间隔可达 4~7s，旧 `MaxDtSeconds=1.0` 使允许位移恒为 484px（首帧 `distance=776` 被误拒）→ 放宽到 10.0s 覆盖稀疏发包。但静默超时**不做无条件放行**（否则 >30s 后可瞬移到任意位置，成为防作弊缺口）：超过 10s 的间隔统一按 10s 计，单包上限 4804px。代价是失焦 >30s 且位移 >4804px 会被误拒，记为后续事项（需服务端权威移动/碰撞校验） |

---

## 验收状态

| # | 测试项 | 状态 |
|---|---|---|
| 5.1 | TCP 监听 | ✅ 原版客户端实测连接 `127.0.0.1:7777` 成功 |
| 5.2 | 编解码正确 | ✅ 握手包 / PlayerControls(13) / TileSection(10) / Snapshot(15) 往返 |
| 5.3 | 半包/粘包 | ✅ Framing 四测试 |
| 5.4 | 管线贯通 | ✅ 解码可达 + `IntegrationTests` 简化闭环 + 真实 TCP 往返（包 13 → MoveCommand）+ 双客户端包 13/14 转发 + 原版客户端进世界 |
| 5.5 | 快照下发 | ✅ `SnapshotBroadcaster_SendsEncodedFrames` + 包 15 字段断言 + 编解码往返 + 真实 TCP 往返（客户端收到包 15） |
| 5.6 | 连接隔离 | ⏳ 需集成测试 |
| 5.7 | 背压 | ⏳ CommandQueue 满策略 |
| 5.8 | 版本校验 | ◐ 协议 326 客户端握手通过；错误版本拒绝待测 |

---

## ⚠️ 必须认知的约束

> **原版 Terraria 客户端兼容性（已实测）**：2026-09-09 原版客户端（协议 326）已实测连接 → 握手 → 进入世界，无异常。编解码严格遵循 terraria-protocol。但**完整游玩链路（移动/挖掘/战斗 → 权威校验 → 仿真 → 快照回传）仍待验证**，深度测试前建议保留自定义测试客户端回归。

> **协议版本跟进**：每次 Terraria 更新客户端，本文件的包 ID 映射 + Phase 2 对应包处理都要更新。这是架构文档反复强调的持续成本。

---

## 项目当前完整结构

> 完整目录树与工程配置见 [`PROJECT_STRUCTURE.md`](../../PROJECT_STRUCTURE.md)。

```
terraauth/
├── TerraAuth.sln             # 解决方案（1 主工程 + 1 测试 + 1 示例插件）
├── TerraAuth.csproj          # ★ 单工程：全部分层源码合并于此
├── architecture.md           # 完整工程架构文档
├── PROJECT_STRUCTURE.md      # 项目结构文档（目录树 / 工程配置 / 模块状态）
├── OPTIMIZATION_BACKLOG.md   # 优化待办（独立立项 / 后续可能优化）
├── GameHost.cs               # ★ 组装根：串联 Phase 2/3/4/5 + 基础设施 + 扩展层
├── Program.cs                # 可执行入口
├── CoreAdapter.cs            # 核心类型 ↔ 插件/Mod 接口桥接
├── server.json               # 运行配置
├── verify.py                 # 无 SDK 静态校验脚本
├── Protocol/                 # 领域模型 + 协议包契约
├── Authority/   (Phase 2)    # 权威层（含 ShardedInboundPipeline 分片装饰）
├── Simulation/  (Phase 3)    # 仿真层（含 World/ 世界模型 + .wld 解析）
├── Net/
│   ├── Phase4/               # 快照广播 + ShadowPredictor 影子预测
│   └── Phase5/               # ★ 网络层（Framing / 编解码 / NetworkHost）
├── Config/                   # 配置（JSON + 热重载）
├── Persistence/              # 持久化（默认内嵌 LiteDb / 可选 SQLite）
├── Monitoring/               # Prometheus 指标 + /metrics 端点
├── Security/                 # 封禁（滑动窗口 + 持久化）
├── Plugins/                  # 插件系统（Hook / 加载器 / 管线装饰）
├── ModCompat/                # Mod 兼容层（策略 / 检测 / 自定义包）
├── Concurrency/              # 并行优化（WorkerPool / 分片 / 快照并行）
├── Tests/                    # 验收测试（7 个测试文件）
├── Examples/                 # 示例插件工程
├── Phase6-Infrastructure/    # 基础设施设计文档
└── Phase7-RedTeam/           # 红队对抗测试手册
```

---

**至此，Phase 2/3/4/5 形成完整闭环**。

进度更新（2026-09-11）：目标框架已升级至 **.NET 10**（`net10.0`）；**Phase 6 已完整实装**
（`Config/Persistence/Monitoring/Security`），含真实 SQLite 落库（玩家 / 审计 / 封禁，`USE_SQLITE`），
`-p:NoSqlite=true` 可降级到内嵌 LiteDb。
剩余 **Phase 7（对抗测试，手册见 `Phase7-RedTeam/`）**、**Phase 8（性能压测 + 上线）**。
