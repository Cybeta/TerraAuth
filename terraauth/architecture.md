# TerraAuth — 泰拉瑞亚服务端权威防作弊架构

> **项目代号**：TerraAuth
> **目标运行时**：与原版 Terraria 客户端兼容（不修改客户端）
> **技术栈**：C# / .NET 10 (LTS)
> **架构定位**：Level 4 深度服务端权威 + 行为分析（Level 5 演进）
> **文档版本**：v1.0（架构基线，供 arget 工程落地）

---

## 0. 一页纸总结（Executive Summary）

### 0.1 我们要解决什么

原版 Terraria 是 **client-first** 架构：客户端挖砖、改血、算伤害、生成抛射物，服务端基本照单全收。TShock 在此基础上做"阈值拦截"，本质是**检测**而非**权威**。CE 改本地内存后，客户端发出的包在外层看来仍然"合法"，因此 TShock 挡不住。

### 0.2 我们的核心思路

反转信息流：

> **客户端只说"我想做什么"（意图），服务端决定"发生了什么"（事实）。**

把世界状态、玩家属性、物品、伤害、抛射物、移动全部收归服务端持有与模拟。客户端从"决策者"降级为"输入设备 + 显示器"。

### 0.3 可达成的效果

| 作弊类型 | 防护效果 |
|---|---|
| CE 改 HP/MP/伤害/物品/速度 | ✅ 架构性失效（服务端不认） |
| 瞬移 / 加速 / 穿墙 | ✅ 物理校验 snap back |
| 物品复制 / 金币生成 / 刷堆叠 | ✅ SSC + 事件溯源，完全拦截 |
| 挖砖/放砖/抛射物 spam、液体漏洞 | ✅ 服务端限流 + 授权 |
| 自瞄 / 自动脚本 / 透视 | 🟡 仅行为分析检测，有误判率 |
| 协议层 0-day 漏洞 | 🟡 与 TShock 同源风险，需持续跟进 |

**天花板 = Level 4**（"不修改原版客户端"约束下的理论上限）。Level 5（ML 行为分析）作为第二阶段演进。

### 0.4 关键取舍（Trade-off）

- **性能 vs 安全**：完全服务端权威增加 CPU/带宽，需客户端预测 + 服务端协调补偿延迟
- **误判 vs 漏判**：阈值需可配置，上线前必须做回归测试（见 §8）
- **协议跟进成本**：每次 Terraria 版本更新需重构对应包处理（ unavoidable）

---

## 1. 架构总览

### 1.1 分层架构图

```
┌─────────────────────────────────────────────────────────────┐
│                     Client (原版 Terraria)                   │
│   输入采集 → 本地预测渲染 → 发送「意图包」→ 接收「状态包」    │
└──────────────────────┬──────────────────────────────────────┘
                       │  TCP / Terraria Protocol
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  L4  接入层 (Transport Layer)                                │
│   ─ TcpServer / Connection / PacketFraming / 加解密协商       │
│   ─ 协议版本适配 (ProtocolVersionAdapter)                    │
├─────────────────────────────────────────────────────────────┤
│  L3  协议层 (Protocol Layer)  ← 对应 terraria-protocol 包定义 │
│   ─ PacketRegistry (200+ 包类型分发)                         │
│   ─ PacketSerializer / PacketDeserializer                    │
│   ─ IPacketHandler<T> 统一处理接口                           │
├─────────────────────────────────────────────────────────────┤
│  L2  权威层 (Authority Layer)  ★ 核心                        │
│   ─ WorldAuthority     世界状态（tiles/液体/NPC/抛射物）      │
│   ─ PlayerAuthority    玩家状态（HP/MP/位置/速度/BUFF）      │
│   ─ InventoryAuthority 物品权威（SSC + 事件溯源）            │
│   ─ CombatAuthority     战斗权威（伤害重算）                  │
│   ─ MovementAuthority   移动权威（物理校验）                  │
│   ─ RateAuthority       频率权威（速率/限流）                 │
├─────────────────────────────────────────────────────────────┤
│  L1  仿真层 (Simulation Layer)                               │
│   ─ GameLoop (固定 timestep tick)                           │
│   ─ WorldSimulator / NpcAI / ProjectileSimulator             │
│   ─ LiquidSimulator / TileSimulator                         │
│   ─ EventSourcing (状态变更事件流)                           │
├─────────────────────────────────────────────────────────────┤
│  L0  基础设施层 (Infrastructure)                             │
│   ─ ECS / 内存管理 / 线程模型                               │
│   ─ Persistence (SQLite/PostgreSQL + 快照)                   │
│   ─ Telemetry (OpenTelemetry) / AuditLog                    │
│   ─ AntiCheatAnalysis (行为分析, L5 演进)                   │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 设计原则

1. **Single Source of Truth**：所有可变状态只在服务端持有，客户端是只读视图
2. **意图-确认模型（Request-Authorize-Commit）**：客户端发"请求"，服务端校验后"确认"，再广播"提交"
3. **确定性仿真**：相同输入 + 相同初始状态 → 相同结果（便于回放校验、断线重连）
4. **可观测优先**：所有权威决策均可审计、可回放
5. **协议兼容**：对外仍是标准 Terraria 协议，老客户端无感知

---

## 2. 模块划分（设计基线 → 实际实现）

> **说明**：下列工程树是 **v1.0 架构基线** 的设计划分（按层拆分为多个类库）。
> 实际落地已**合并为单一工程** `TerraAuth.csproj`（`OutputType=Exe`），分层改由 **命名空间** 承载，目录结构保持不变。
> 权威的目录树 / 工程配置 / 模块实现状态见 [`PROJECT_STRUCTURE.md`](PROJECT_STRUCTURE.md)。

### 2.1 设计模块 → 实际目录映射

| 设计基线模块 | 实际目录 / 命名空间 | 状态 |
|---|---|---|
| `TerraAuth.Server` | `Program.cs` / `GameHost.cs`（`TerraAuth`） | 已实现 |
| `TerraAuth.Transport` | `Net/Transport/`（`TerraAuth.Net.Transport`） | 部分 |
| `TerraAuth.Protocol` | `Protocol/`（`TerraAuth.Protocol`） | 部分 |
| `TerraAuth.Authority` | `Authority/`（`TerraAuth.Authority`） | 已实现 |
| `TerraAuth.Simulation` | `Simulation/`（`TerraAuth.Simulation`） | 部分 |
| `TerraAuth.Domain` | `Simulation/World/`（`Tile` / `WorldState` 等） | 部分 |
| `TerraAuth.Persistence` | `Persistence/` | 部分 |
| `TerraAuth.Telemetry` | `Monitoring/` + `Authority/AuditLogger.cs` | 已实现 |
| `TerraAuth.AntiCheat` | 未落地（见 `Phase7-RedTeam/`） | 未实现 |
| `TerraAuth.Abstractions` | `Protocol/Types.cs`、`Concurrency/ParallelConfig.cs` 等 | 已实现 |
| （基线外新增）插件 / Mod / 并行 | `Plugins/` · `ModCompat/` · `Concurrency/` | 已实现 / 部分 |

### 2.2 设计基线工程树（历史，仅供参考）

```
TerraAuth/
├── TerraAuth.Server/              # 主程序 (Generic Host)
│   ├── Program.cs
│   └── ServerHost.cs
│
├── TerraAuth.Transport/           # L4 接入层
│   ├── TcpServer.cs
│   ├── ClientConnection.cs        # 每连接状态机
│   ├── PacketFraming.cs            # 长度前缀 + 分包
│   └── ProtocolVersionAdapter.cs  # 多版本兼容
│
├── TerraAuth.Protocol/            # L3 协议层 (与 terraria-protocol 映射)
│   ├── Packets/
│   │   ├── IPacket.cs
│   │   ├── PlayerPositionPacket.cs
│   │   ├── NpcStrikePacket.cs
│   │   ├── TileBreakPacket.cs
│   │   ├── PlayerHPPacket.cs
│   │   ├── ChestItemPacket.cs
│   │   └── ... (200+ 包定义)
│   ├── PacketRegistry.cs           # 包 ID → handler 映射
│   ├── PacketSerializer.cs
│   └── Handlers/                  # IPacketHandler<T>
│       ├── IHandlerContext.cs      # 提供 Authority 引用
│       ├── PlayerPositionHandler.cs
│       ├── NpcStrikeHandler.cs
│       └── ...
│
├── TerraAuth.Authority/           # L2 权威层 ★
│   ├── IAuthority.cs               # 统一接口
│   ├── WorldAuthority.cs
│   ├── PlayerAuthority.cs
│   ├── InventoryAuthority.cs
│   ├── CombatAuthority.cs
│   ├── MovementAuthority.cs
│   ├── RateAuthority.cs
│   ├── ProjectileAuthority.cs
│   └── Rules/                      # 校验规则（可配置）
│       ├── MovementRule.cs
│       ├── DamageRule.cs
│       └── RateRule.cs
│
├── TerraAuth.Simulation/          # L1 仿真层
│   ├── GameLoop.cs                 # 主 tick 循环
│   ├── WorldSimulator.cs
│   ├── TileSimulator.cs
│   ├── LiquidSimulator.cs
│   ├── NpcAI.cs
│   ├── ProjectileSimulator.cs
│   └── EventSourcing/
│       ├── GameEvent.cs
│       ├── EventStore.cs
│       └── StateRebuilder.cs
│
├── TerraAuth.Domain/              # 领域模型 (POCO / 值类型)
│   ├── Player.cs
│   ├── Item.cs
│   ├── Projectile.cs
│   ├── Tile.cs
│   ├── Npc.cs
│   └── World.cs
│
├── TerraAuth.Persistence/         # L0 持久化
│   ├── WorldRepository.cs
│   ├── PlayerRepository.cs
│   └── SnapshotManager.cs          # 定时快照 + WAL
│
├── TerraAuth.Telemetry/           # L0 可观测
│   ├── AuditLogger.cs              # 安全审计日志
│   ├── Metrics.cs                  # Prometheus 指标
│   └── ReplayRecorder.cs           # 对局回放
│
├── TerraAuth.AntiCheat/           # L5 行为分析 (演进)
│   ├── BehaviorProfiler.cs
│   ├── AnomalyDetector.cs
│   └── Models/                     # ML 模型 (后续)
│
└── TerraAuth.Abstractions/        # 共享接口/常量
    ├── Constants.cs
    └── Result.cs                   # 权威决策结果
```

---

## 3. 核心数据流：从「客户端包」到「权威决策」

### 3.1 统一处理管线（Pipeline）

每个客户端包都经过以下管线，**任一环节拒绝则丢弃并审计**：

```
[1] 反序列化  →  [2] 连接状态校验  →  [3] 频率/限流 (RateAuthority)
        →  [4] 语义校验 (Authority)  →  [5] 仿真应用 (Simulation)
        →  [6] 事件溯源记录  →  [7] 状态广播 (广播给相关客户端)
```

**关键**：步骤 [4][5] 是服务端权威核心——客户端包**只用于推导意图**，所有状态变更都由服务端模型产生。

### 3.2 伪代码：包处理骨架

```csharp
public interface IPacketHandler<T> where T : IPacket
{
    /// <summary>处理客户端意图包，返回权威决策结果。</summary>
    AuthorityResult Handle(T packet, HandlerContext ctx);
}

public class HandlerContext
{
    public PlayerAuthority Player { get; }
    public WorldAuthority World { get; }
    public IAuditLogger Audit { get; }
}

public readonly struct AuthorityResult
{
    public bool Accepted { get; init; }
    public IReadOnlyList<IGameEvent> EmittedEvents { get; init; } // 服务端产生的状态变更
    public Packet? CorrectionToClient { get; init; }              // 纠正客户端的包
    public CheatAction Action { get; init; } // None / Warn / Kick / Ban
}
```

### 3.3 意图-确认模型示例

**传统（原版）**：客户端发 `PlayerHP { hp: 99999 }` → 服务端直接设 HP。

**TerraAuth**：
```
Client → "我请求将 HP 设为 99999"
   │
   ▼ (PlayerAuthority.ApplyHPRequest)
Server: 当前权威 HP = 85, 变更来源 = 未知
   → Rule: 客户端不得直接设置 HP, HP 只能由服务端(伤害/回血/BUFF)驱动
   → Result: Accepted=false, CorrectionToClient=PlayerHP{85}, Action=Warn
   → Audit: "player=X attempted direct HP set, rejected"
```

**合法路径**：服务端仿真每 tick 回血 → `PlayerAuthority` 产出 `PlayerHPChangedEvent` → `PlayerHPPacket` 下发给客户端（客户端只是显示）。

---

## 4. 关键包的服务端权威处理（协议映射）

> 映射对象：`terraria-protocol` 中定义的包。以下为代表性关键包，完整清单需对照协议库补齐。

| # | 包名 (协议) | 原版语义 | TerraAuth 处理策略 |
|---|---|---|---|
| 1 | `PlayerPosition` / `PlayerVelocity` | 客户端上报位置 | **MovementAuthority** 校验位移 ≤ `maxSpeed × 60 × Δt + TeleportTolerance`（Δt 钳制在 `[1/60, 10]` 秒，长时静默同样按 10s 计、不做无条件放行），超速拒绝、否则更新权威位置 |
| 2 | `PlayerHP` / `PlayerMana` | 客户端上报血蓝 | **只读参考**，服务端用权威值纠正，拒绝直接设置 |
| 3 | `NpcStrike` | 客户端说"我打了 NPC X 造成 Y 伤害" | **CombatAuthority** 用服务端持有的武器/护甲/BUFF/距离重算伤害，应用到 NPC |
| 4 | `TileBreak` / `TilePlace` | 客户端挖/放砖 | **请求-授权**：服务端按挖掘速度模型逐 tick 授权，超速率丢弃 |
| 5 | `Chest` / `ItemDrop` / `InventorySlot` | 物品变更 | **InventoryAuthority** SSC + 事件溯源，所有变更走服务端授权事件 |
| 6 | `ProjectileNew` / `ProjectileDestroy` | 抛射物生成 | **ProjectileAuthority** 服务端生成/模拟，客户端只发"请求" |
| 7 | `PlayerBuff` | BUFF 施加 | BUFF 状态服务端持有，客户端发"请求施加"，校验来源合法性 |
| 8 | `Liquid` / 电路相关包 | 液体/电路操作 | **WorldAuthority** 服务端模拟液体流动，校验操作合法性 |
| 9 | `SyncPlayer` | 玩家状态同步 | 服务端为权威源，客户端只接收 |

### 4.1 伪代码：移动权威

```csharp
public AuthorityResult Handle(PlayerPositionPacket pkt, HandlerContext ctx)
{
    var authority = ctx.Player.Movement;
    var rawDt = ctx.SinceLastPacket; // 距上次收到位置包的秒数

    // Δt 钳制在 [1/60, 10] 秒：下限防批量处理低估 dt，上限容忍稀疏发包；
    // 长时静默（失焦/卡顿/重连）一律按 10s 计，不做无条件放行（防穿墙/瞬移缺口）
    var dt = Math.Clamp(rawDt, MinDtSeconds, MaxDtSeconds);

    // 1. 计算最大允许位移（考虑坐骑/翅膀/钩爪等合法加速来源）
    var maxDelta = authority.CalculateMaxDelta(dt); // maxSpeed * 60 * dt + tolerance
    var actual = Vector2.Distance(authority.LastPosition, pkt.Position);

    if (actual > maxDelta)
    {
        // 超速/瞬移：拒绝并纠正回权威位置
        return AuthorityResult.Reject(
            correction: new PlayerPositionPacket(authority.LastPosition),
            action: CheatAction.Warn,
            reason: $"speed exceeded: {actual} > {maxDelta}");
    }

    // 2. 穿墙检测：服务端 tile 状态判断路径是否合法
    if (!authority.PathIsClear(authority.LastPosition, pkt.Position))
    {
        return AuthorityResult.Reject(
            correction: new PlayerPositionPacket(authority.LastPosition),
            action: CheatAction.Kick,
            reason: "wall clip detected");
    }

    // 3. 通过：更新权威状态，广播给其他客户端
    authority.CommitPosition(pkt.Position, pkt.Velocity);
    return AuthorityResult.Accept(
        events: [new PlayerMovedEvent(ctx.PlayerId, pkt.Position)]);
}
```

### 4.2 伪代码：战斗权威

```csharp
public AuthorityResult Handle(NpcStrikePacket pkt, HandlerContext ctx)
{
    var combat = ctx.Authority.Combat;
    var weapon = ctx.Player.Inventory.GetHeldWeapon();   // 服务端持有的武器实例
    var npc = ctx.World.GetNpc(pkt.NpcId);

    // 1. 基础校验：NPC 是否存在、在攻击范围内
    if (npc == null || !combat.InRange(ctx.Player, npc, weapon))
        return AuthorityResult.Reject(reason: "out of range");

    // 2. 服务端重算伤害（不信任 pkt.Damage）
    var realDamage = combat.CalculateDamage(
        weapon: weapon,
        attacker: ctx.Player,   // 服务端护甲/BUFF
        target: npc,            // 服务端防御
        distance: combat.Distance(ctx.Player, npc),
        isCritical: pkt.IsCritical);  // 暴击也由服务端按概率重算

    // 3. 应用权威伤害
    npc.ApplyDamage(realDamage);
    return AuthorityResult.Accept(
        events: [new NpcDamagedEvent(pkt.NpcId, realDamage)]);
}
```

---

## 5. 服务端权威六大子系统

### 5.1 WorldAuthority（世界权威）

- 持有完整 tile 矩阵、液体状态、NPC 列表、抛射物列表
- 客户端对世界的修改**全部转为请求**，服务端授权后应用
- 支持**区块分区锁**，并行处理不同区域

### 5.2 PlayerAuthority（玩家权威）

- 持有 HP/MP/位置/速度/加速度/碰撞体/BUFF 列表
- 维护**合法加速来源状态**（坐骑、翅膀、钩爪——服务端记录激活状态）
- 每 tick 产出 `PlayerStateSnapshot` 下发给客户端（客户端只渲染）

### 5.3 InventoryAuthority（物品权威，SSC 强化）

- 物品栏、金币、堆叠数服务端为唯一真相源
- **事件溯源**：每次变更产生 `ItemChangedEvent`，可追溯、可对账
- 白名单 + 来源校验：物品只能来自拾取/合成/掉落/交易等服务端授权事件
- 堆叠对账：客户端上报的物品变更必须能与事件流对应，否则回滚

### 5.4 CombatAuthority（战斗权威）

- 伤害公式服务端独占
- 武器实例数据服务端持有（CE 改客户端武器数值无效）
- 暴击、击退、穿透均由服务端按概率/规则重算

### 5.5 MovementAuthority（移动权威）

- 确定性运动学积分（位置 += 速度 × Δt）
- 碰撞检测基于服务端 tile 状态
- 合法速度上限动态计算（考虑服务端记录的加速来源）

### 5.6 RateAuthority（频率权威）

- 每玩家令牌桶：挖砖、放砖、抛射物、伤害、聊天
- 超出阈值：丢弃 + 记录，连续触发升级为 Warn/Kick
- 防止 packet flood（同时也是 DoS 防护）

---

## 6. 仿真层与一致性

### 6.1 确定性 GameLoop

```
while (running)
{
    var dt = fixedTimestep;            // 固定步长, 与原版 tick 对齐
    Input: 消费本 tick 内所有客户端意图包 → 产出事件
    Update: 应用事件 → 推进世界仿真 (NPC AI / 液体 / 抛射物 / 回血)
    Reconcile: 校验客户端预测偏差, 超阈值纠正
    Broadcast: 下发状态快照给相关客户端
}
```

**确定性要求**：
- 用固定 timestep，禁止依赖系统时钟的随机性（用 seeded RNG）
- 所有随机事件（暴击、掉落）由服务端 RNG 驱动 → 可回放、可审计

### 6.2 客户端预测 + 服务端协调

为避免"粘手"感：
- 客户端本地预测移动（原版行为）
- 服务端每 tick 下发权威位置
- 客户端收到后与本地预测比对，**小偏差平滑插值，大偏差（被纠正）硬 snap**
- 这是"手感"与"安全"的平衡点，需在 arget 阶段调参

### 6.3 事件溯源（Event Sourcing）

```
意图包 → [Authority] → 领域事件 (GameEvent) → EventStore → 状态变更
                                              ↓
                                        SnapshotManager (定时快照)
```

- **断线重连**：从最近快照 + 重放事件 = 完整状态
- **回放校验**：存疑对局可用同一事件流重放，复现判定
- **审计**：所有权威决策均有事件记录

---

## 7. 基础设施：可观测与持久化

### 7.1 审计日志（AuditLog）

每个权威拒绝/纠正都记录结构化日志：

```json
{
  "ts": "2026-09-09T12:00:00Z",
  "player": "PlayerName",
  "playerId": 12345,
  "event": "position_rejected",
  "clientValue": { "x": 0, "y": 0 },
  "serverValue": { "x": 2500, "y": 1200 },
  "reason": "speed_exceeded",
  "tick": 1234567,
  "action": "Warn"
}
```

### 7.2 指标体系（Prometheus / OpenTelemetry）

| 指标 | 说明 | 告警阈值 |
|---|---|---|
| `authority_rejected_total{reason}` | 各类拒绝计数 | 单玩家突增 → 可能在试探 |
| `packet_processing_seconds` | 单包处理耗时 | P99 > 5ms 需优化 |
| `rate_limit_drop_total` | 限流丢弃数 | 突增 = flood/DoS |
| `memory/tile_state_bytes` | 世界状态内存 | 持续增长 = 泄漏 |
| `player_bandwidth_bytes/sec` | 单玩家带宽 | 异常高 = 可能 flood |

### 7.3 持久化

- **世界快照**：定时（如每 5 分钟）+ 关键事件触发
- **WAL（Write-Ahead Log）**：事件先写日志再应用，崩溃可恢复
- **玩家数据**：SQLite（小服）/ PostgreSQL（大服）

---

## 8. 验收与评估（对照评估框架）

### 8.1 实验室对抗测试（上线前）

| 测试类 | 方法 | 通过标准 |
|---|---|---|
| CE 改 HP/MP/伤害/物品/速度 | CE 直改本地内存 | ✅ 全部被服务端纠正/拒绝，阻断率 ≥ 99% |
| 瞬移/穿墙 | CE 改坐标 | ✅ snap back + 拒绝 |
| 构造非法包 | 自定义发包工具 | ✅ 全部被对应 Authority 拒绝 |
| Packet flood / 重放 | 洪水攻击 + 重放合法包 | ✅ 限流生效，服务端不崩溃 |
| **回归测试（防误杀）** | 钩爪/坐骑/飞行/传送/断线重连/高延迟(>200ms)/多人协作/液体电路 | ✅ 误判率 < 0.1% |

### 8.2 核心 KPI

```
阻断率 (Block Rate)        ≥ 99%  (内存修改类)
误判率 (False Positive)    < 0.1%
绕过率 (Bypass Rate)       → 0 (不可能绝对 0)
检测延迟 (Time-to-Detect)  < 1s (实时) / 分钟级 (行为分析)
性能开销 (vs 原版)         < 20% CPU, < 30% 带宽
```

### 8.3 成熟度自评

- **L4**（本架构目标）：服务端权威 + 频率限制 → 内存修改类架构性失效
- **L5**（演进）：叠加 ML 行为分析抓自瞄/脚本/透视
- **L6**（超出约束）：需修改客户端，本架构不覆盖

---

## 9. 里程碑（Roadmap for arget）

```
Phase 0 — 协议兼容骨架 (2 周)
  └── TcpServer + PacketFraming + PacketRegistry + 协议版本适配
  └── 能与原版客户端完成连接/进入世界（透传模式, 暂不权威）

Phase 1 — 玩家权威 MVP (3 周)
  └── PlayerAuthority: HP/MP/位置/速度
  └── MovementAuthority: 物理校验 + snap back
  └── RateAuthority: 令牌桶限流
  └── AuditLog + Metrics 接入

Phase 2 — 物品 & 战斗权威 (3 周)
  └── InventoryAuthority: SSC + 事件溯源
  └── CombatAuthority: 伤害重算
  └── 对抗测试: CE 改血/物品/伤害全部失效

Phase 3 — 世界权威 (4 周)
  └── WorldAuthority: tile/液体/抛射物/电路
  └── TileSimulator + LiquidSimulator + ProjectileSimulator
  └── 挖砖/放砖请求-授权模型

Phase 4 — 一致性 & 性能 (2 周)
  └── 确定性 GameLoop + 客户端预测协调
  └── 快照 + WAL + 断线重连
  └── 性能基准测试, 调优到 KPI 内

Phase 5 — 行为分析 L5 (持续, 可与 Phase 3-4 并行)
  └── BehaviorProfiler: 速度/挖掘/DPS/操作熵基线
  └── AnomalyDetector: 离群点 → 标记 → 人工复核
```

---

## 10. 技术选型说明（为什么 C# / .NET）

| 维度 | C# (.NET 10) | 说明 |
|---|---|---|
| 协议复用 | ✅ 极佳 | 与原版 Terraria 同语言，可直接参考/复用协议类型定义，对接 `terraria-protocol` 的 .NET 生态 |
| 性能 | ✅ 良好 | Span\<T\>、内存\<T\>、SIMD、原生 AOT 可选，tick 密集仿真够用 |
| 并发 | ✅ | Task + Channels + lock-free 集合，`System.Threading.Channels` 做包队列 |
| 生态 | ✅ | EF Core / Dapper 持久化，OpenTelemetry 原生支持 |
| 部署 | ✅ | 单文件发布 + 原生 AOT，跨平台 |

**线程模型建议**：
- 每连接一个读循环（async） → 解析后入 `Channel<Packet>`
- 单线程 GameLoop 消费所有包 + 推进仿真（**避免锁竞争，保证确定性**）
- 广播写回使用 `Channel` 分发到各连接写循环

---

## 11. 风险与边界（必须认知）

1. **协议缺陷是底层风险**：即便完整实现，Terraria 协议本身缺陷导致的物品复制/DoS 风险仍存在（与 TShock 同源），需随版本持续跟进
2. **版本跟进成本**：每次 Terraria 更新需重构对应包处理，是持续投入而非一次性
3. **延迟敏感**：完全服务端权威会让高延迟玩家感觉"粘手"，需精心调校预测/协调参数
4. **误判伤害**：过度严格的移动/速率校验会误杀正常玩家（钩爪、坐骑、传送法杖），回归测试是上线闸门
5. **L4 天花板**：自瞄/脚本/透视只能检测不能阻止——若业务要求更高，必须修改客户端（超出本架构约束）

---

## 附录 A：术语表

- **Authority（权威）**：服务端对某项状态的唯一所有权与决策权
- **SSC (Server Side Characters)**：服务端持有角色数据，本地存档不可信
- **意图-确认模型**：客户端发"想做什么"，服务端确认后才生效
- **Event Sourcing（事件溯源）**：状态变更以事件流记录，可从事件重建任意时刻状态
- **snap back**：服务端检测到客户端偏离权威状态后，强制纠正回权威值
- **确定性仿真**：相同输入+初始状态 → 相同结果，便于回放/断线重连

## 附录 B：参考资源

- `terraria-protocol`：社区维护的 Terraria 客户端-服务端通信协议定义库（Rust，包定义可迁移至 C#）
- TShock Bouncer：现有阈值拦截反作弊参考（GetDataHandlers + Threshold）
- TShock 安全策略文档：协议缺陷风险的官方说明

---

> **给 arget 的落地提示**：建议从 **Phase 0 + Phase 1** 起步，先跑通"透传模式 + 移动权威"，用 CE 实测能挡住瞬移/超速后再推进 Phase 2。每个 Phase 结束时都必须跑一遍 §8.1 的对抗测试 + 回归测试，确保**阻断率达标且不误杀**。架构是骨架，验收测试才是质量的闸门。
