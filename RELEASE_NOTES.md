# TerraAuth v0.2.0

> 服务器端权威仿真反作弊服务端（Terraria 1.4.5.8 / 协议 326）

## 修复

### 1. 丢弃召唤法杖后召唤物残留攻击
- **现象**：将召唤法杖丢出背包后，已召唤的仆从/弹幕不消失，仍持续发射弹幕造成伤害，需手动移除召唤 Buff 才能清除
- **根因**：原版仆从由客户端召唤 Buff 驱动存活，仅销毁服务端弹幕（包 29）不够——客户端 Buff 未移除时仆从 AI 仍存活并攻击；且弹幕 `Active=false` 后会被后续包 27（ProjectileSync）复活
- **修复**：
  - 新增 `SummonWeaponBuff` 映射表（召唤武器 → Buff ID），销毁弹幕时同步移除玩家召唤 Buff
  - 经包 50（PlayerBuffs）回写服务端权威 Buff 变更，客户端视觉同步
  - 弹幕置 `Destroyed=true` 永久销毁，拒绝包 27 复活
  - `CommandQueue` 双路径触发：`SetInventorySlotCommand`（换出/清空武器）+ `SpawnItemCommand`（丢弃物品）
  - `ServerConfig` 新增 `DestroySummonsOnWeaponRemoval` 开关（默认关，保持原版行为；server.json 已启用）

### 2. 丢弃后掉落物无法拾取
- **现象**：丢弃物品后掉落物无法拾取
- **根因**：包 21（SyncItem）不含归属数据，客户端对未收包 22（SyncItemOwner）的物品默认视为无主（255）并拒绝抓取
- **修复**：对所有掉落物广播包 22 设置客户端归属，按原版 `FindOwner` 就近分配（无主 5 tick / 已归属 300 tick 刷新），仅在归属变化时重发

### 3. 其他
- 新增入站包诊断日志
- `.gitignore` 覆盖根目录日志，运行时数据不入库

## 变更文件
- `Simulation/Combat/SummonProjectileTable.cs`（召唤武器 → Buff 映射）
- `Simulation/World/WorldState.cs`（弹幕销毁 + Buff 移除 + 变更标记）
- `Simulation/CommandQueue.cs`（双路径触发）
- `GameHost.cs`（包 50 回写 / 包 22 归属广播）
- `Config/ServerConfig.cs`、`server.json`（开关配置）
- `Tests/SimulationTests.cs`、`Tests/VanillaFeatureTests.cs`（378 行新增用例）

## 测试
当前全量测试为 438/438 通过；本版本新增覆盖：召唤 Buff 移除、包 50 回写、包 22 就近归属、弹幕永久销毁不复活等场景。

## 使用
- 下载对应平台的 `TerraAuth-win-x64.zip` / `TerraAuth-linux-x64.zip`，解压后运行
- 默认监听 `7777`，用原版 Terraria 客户端（协议 326）连接即可进入世界
