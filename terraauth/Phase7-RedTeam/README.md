# Phase 7 — 红队对抗测试手册

> 配套 `architecture.md` §8 验收章节。每个 Phase 结束后**必须**跑一遍本清单。

## 零、自动化现状（服务端可自动化部分）

下表仅为**可自动化的子集**，只列出已落为可执行测试的清单项；M 组 / R 组其余项（需 CE 改内存或真实客户端交互）仍为手工实验：

| 清单项 | 自动化测试 | 覆盖点 |
|--------|-----------|--------|
| P1 | `AntiCheat_DpsWindow_RejectsBurst` | 单次在 `MaxSingleDamage` 内、窗口累计超 `MaxDps` → `dps_exceeded` |
| P2 | `Vanilla_Movement_Overspeed_IsRejected`（既有） | 超速 → `speed_exceeded` + 位置纠正 |
| P3 | `AntiCheat_IllegalItemStack_IsRejected` / `AntiCheat_ChestItem_WithUnknownItem_Is_Rejected` | 非法堆叠 `invalid_stack`；未知物品 `unknown_item` |
| P4 | `AntiCheat_UnidentifiedPlayer_IsSilentlyDropped` / `AntiCheat_Replayed_Malicious_Movement_DoesNotAdvance_Authority` | 无身份包丢弃；恶意包重放不推进权威 |
| P5 / M6 | `AntiCheat_PacketFlood_IsRateLimited_AndEventuallyKicked` | 洪水 → 限流 → 违规累计达阈值踢出（包 2 + 关闭） |
| M1 | `Vanilla_Health_Above_ServerMax_Gets_Correction`（既有） | 血量超上限被纠正 |
| M3 | `Vanilla_TilePlace_Without_InventoryItem_Is_Rejected`（既有） | 无物品放砖被拒 |
| M4 | 同 P2 | 速度上限 |
| M9 | `Vanilla_ItemPickup_*`（既有） | 拾取按世界真实实体对账 |
| 健壮性 | `Vanilla_ChaoticSection_Is_Split_Without_Breaking_Login` | 超帧上限区块拆分后登录仍完成 |

> 说明：P4 的「nonce/tick 去重」目前**未实现**（协议无 nonce），当前保证的是「重放不会推进权威状态」而非「包被去重」。

## 一、测试矩阵

### M 组：内存修改类（CE 直改）— 目标阻断率 ≥ 99%

| 编号 | 测试项 | 操作 | 预期 |
|------|--------|------|------|
| M1 | HP 篡改 | CE 改本地 HP 为 99999 | 服务端纠正回真实值 |
| M2 | MP 篡改 | CE 改本地 MP | 服务端纠正 |
| M3 | 物品生成 | CE 改物品 ID / 堆叠数 | SSC 不认可，回滚 |
| M4 | 移动速度 | CE 改速度变量 | 速度校验拒绝，snap back |
| M5 | 穿墙 | CE 改碰撞相关内存 | 位置校验拒绝 |
| M6 | 挖掘速率 | CE 加速挖掘循环 | 挖/放砖限流拦截：`ServerConfig.MaxTileBreakPerSecond`（默认 60）/ `MaxTilePlacePerSecond`（默认 40）→ `WorldLimits`，违规码 `tile_break_rate_exceeded` / `tile_place_rate_exceeded` |
| M7 | 伤害注入 | CE 改武器伤害数值 | 服务端重算，按真实武器算 |
| M8 | 抛射物 spam | CE 触发大量抛射物 | 服务端限流丢弃 |
| M9 | 金币复制 | CE 复制堆叠 | 服务端对账拒绝 |

### P 组：协议攻击（发包工具）— 洪水下 CPU < 200%

| 编号 | 测试项 | 操作 | 预期 |
|------|--------|------|------|
| P1 | 非法伤害包 | 构造 99999 伤害包 | 服务端重算拒绝 |
| P2 | 非法位置包 | 瞬间移动到地图另一端 | 速度校验拒绝 |
| P3 | 非法物品包 | 不存在物品 ID | 白名单校验拒绝 |
| P4 | 重放攻击 | 录制合法包重复发送 | nonce/tick 去重拒绝 |
| P5 | 洪水攻击 | 短时海量合法格式包 | 限流，不崩溃不卡顿 |

### R 组：合法回归（误判率 < 0.1%）

| 编号 | 测试场景 | 预期 |
|------|---------|------|
| R1 | 钩爪移动 | 不判瞬移 |
| R2 | 坐骑 / 飞行 | 速度在合法范围 |
| R3 | 传送机 / 传送法杖 | 不判瞬移 |
| R4 | 断线重连 | 状态正确恢复 |
| R5 | 高延迟（>200ms） | 操作有延迟但不误判 |
| R6 | 多人协作挖同一区域 | 不触发速率异常 |
| R7 | 液体 / 电路工程 | 不触发液体/放置误判 |

## 二、数据采集

### 审计日志（SQLite）

```sql
-- 每日拦截统计
SELECT DATE(timestamp) AS day, COUNT(*) AS blocks
FROM AuditLogs
WHERE action LIKE 'reject_%'
GROUP BY DATE(timestamp);

-- 高频违规玩家
SELECT player_id, COUNT(*) AS violations
FROM AuditLogs
WHERE timestamp > NOW() - INTERVAL 7 DAY
GROUP BY player_id
HAVING violations > 10
ORDER BY violations DESC;
```

### Prometheus 指标

| 指标 | 告警阈值 |
|------|---------|
| `blocked:total` | 突增 10x = 有人在试探 |
| `false_positives:total` | 持续上升 = 阈值过紧 |
| `packet_processing_time` | P99 > 5ms 需优化 |

### 经济系统间接指标

| 指标 | 异常信号 |
|------|---------|
| 金币日产出 | 突增 5x = 刷钱 |
| 稀有物品流通量 | 骤增 = 物品生成 |
| 物品价格 | 暴跌 = 供应被刷 |

## 三、验收结论模板

```
测试日期: YYYY-MM-DD
测试版本: TerraAuth x.y.z / Terraria protocol zzz
执行人: ___

M 组: x/9 通过 (阻断率 xx%)
P 组: x/5 通过 (峰值 CPU xx%, 内存泄漏: 有/无)
R 组: x/7 通过 (误判率 xx%)

结论: [通过 / 需修复]
待修复项:
1. ...
```

## 四、迭代流程

```
每周  → 审计日志审查 + 经济异常扫描 + 举报复核
每月  → CE 重测 + 性能基准 + 误判率统计
每季  → 成熟度自评 + 协议更新适配 + 策略调优
```
