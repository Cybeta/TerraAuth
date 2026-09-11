# TerraAuth — 原版功能覆盖与验证矩阵

> 记录「原版客户端会用到的功能」在服务端的实现与验证状态，供"测试原版所有功能"时对照。
> 自动化验证见 [`Tests/VanillaFeatureTests.cs`](Tests/VanillaFeatureTests.cs)：**真实权威管线（GameHost.Bootstrap）+ 真实 TCP**，
> 与 `IntegrationTests`（多为桩管线）互补。
> 最后更新：2026-09-12

---

## 一、已实现并自动化验证

| 原版功能 | 客户端包 | 服务端行为 | 验证用例 |
|---|---|---|---|
| 连接 / 协议协商 | 1 | 版本串校验 → 分配 PlayerId（包 3） | `Vanilla_LoginChain_Delivers_All_Required_Packets` |
| 玩家信息 / 外观 | 4 | 名称合法性 + 白名单 + 记录外观（Slot 覆盖为服务端 ID） | `Vanilla_SecondPlayer_Is_Broadcast_To_First` |
| 请求世界数据 | 6 | 回包 7（真实世界元数据） | `Vanilla_LoginChain_...` |
| 请求出生区块 | 8 | 回包 9（进度）+ 逐块 10 + 49（出生） | `Vanilla_LoginChain_...` |
| 进入世界 | 12 | 置 Playing → 包 129 + 广播外观 4 / 激活 14 | `Vanilla_Join_Marks_Self_Active` |
| 玩家激活在线 / 离线 | 14 | 进服广播激活；断线广播 `Active=false` | `Vanilla_PlayerDisconnect_Broadcasts_Inactive` |
| **断线会话保留 + 槽位回收** | 14 | 断线不销毁运行时：按玩家名保留位置 / 血量 / 增益（`SessionResumeGraceSeconds`，默认 60s），宽限期内同身份重连**认回原运行时**并下发**携带恢复坐标**的出生包（12）；超期 / 被顶号回收。断开时释放连接槽位与并发容量，新连接复用**最小空闲 ID**（与原版一致）。注：原版客户端断线即回主菜单，故为「手动重进的会话接管」而非自动重连 | `Vanilla_SessionResume_Restores_Position_And_Hp` / `..._Off_When_Grace_Is_Zero` / `SessionResume_Expires_After_Grace` |
| 移动 / 位置 | 13 | 超速校验（`maxSpeed×60×Δt + 容差`）→ Command → 仿真 → 快照 15；并转发其他玩家 | `Vanilla_Movement_Accepted_And_Applied` / `..._Overspeed_IsRejected` |
| 挖砖 | 17 | 越界 / 超距 / 图格类型对账 → TileBreakCommand → 图格变更 → 转发 | `Vanilla_TileBreak_Removes_Solid_Tile` / `..._OutOfReach_IsRejected` |
| 放砖 | 79 | 越界 / 超距 / 类型范围 / **背包扣减（SSC）** → TilePlaceCommand | `Vanilla_InventoryReport_Then_TilePlace_Succeeds` / `..._Without_InventoryItem_IsRejected` |
| 背包同步 | 5 | 槽位 / 堆叠 / 物品校验；SSC 下服务端持有唯一真相 | `Vanilla_InventorySlot_InvalidSlot_IsRejected` |
| 物品丢弃 | 21 | 物品 ID / 堆叠校验；并中继给他人 | `Vanilla_ItemDrop_UnknownItem_IsRejected` / `..._Is_Relayed_To_OtherPlayers` |
| 开箱 | 31 | 坐标越界校验 | `Vanilla_Chest_OutOfBounds_IsRejected` |
| 攻击 NPC | 28 | 单次伤害上限 + 窗口内 DPS 上限 | `Vanilla_NpcStrike_Above_SingleDamage_Limit_IsRejected` / `..._Within_Limit_IsAccepted` |
| 抛射物 | 27 | 字段 / 速率校验；并中继给他人 | `Vanilla_Projectile_Is_Not_Rejected` / `..._Is_Relayed_To_OtherPlayers` |
| 生命 / 法力上报 | 16 | 上限校验；超限则下发**纠正包 16** | `Vanilla_Health_Above_ServerMax_Gets_Correction` |
| 传送 | 65 | 实体索引 / 落点越界 / 频率校验 | `Vanilla_Teleport_OutOfBounds_IsRejected` |
| 弃用包健壮性 | 25 | 未知 / 弃用包透传，连接不受影响 | `Vanilla_DeprecatedChatPacket_DoesNot_Disconnect` |
| **他人可见性（中继）** | 117 / 35 / 36 / 50 / 32 | 权威通过后转发给其他玩家；携带玩家字段的包以服务端分配 ID 覆盖（防伪造身份）。注：118 死亡不在此中继，改由服务端结算后统一广播 | `Vanilla_PlayerHurt_Is_Relayed_With_ServerPlayerId` / `..._PlayerBuffs_...` |
| **世界时间同步** | 18 (Time) | 持续下发 `Byte dayTime + Int32 time + Int16 sunModY + Int16 moonModY` | `Vanilla_Time_Is_Synced_To_Client` |
| **NPC 生成 / 同步** | 23 (SyncNPC) | 定期下发世界 NPC（索引 / netID / 位置 / 速度 / 朝向）；已满血形态省略生命段 | `Vanilla_Npc_Is_Synced_To_Client` |
| **聊天** | 82 (NetModule → NetTextModule) | 客户端发言 → 转服务端下行形态广播给所有人；`IServerApi.Broadcast/SendMessage` 真实下发 | `Vanilla_Chat_Is_Relayed_To_OtherPlayers` / `Vanilla_ServerBroadcast_Reaches_Client` |
| **掉落物 / 弹幕服务端模拟** | 21 / 27 / 29 | 服务端登记实体并推进生命周期：掉落物重力落地（槽位由服务端分配）、弹幕直线积分 + 生存期到期，到期由服务端补发包 29 | `Vanilla_ItemDrop_Is_Tracked_And_Falls_Under_Gravity` / `Vanilla_Projectile_Is_Tracked_And_Expires_With_Server_Destroy` |
| **敌怪刷怪（简化）** | 23 + 28 | 每 60 tick 刷史莱姆（上限 8）；包 28 扣血、归零即死亡；包 23 带生命段 | `Vanilla_Enemy_Spawns_Then_Dies_From_Strike` |
| **受伤 / 死亡（服务端结算）** | 117 / 118 | 伤害非负校验 → `DamagePlayerCommand` / `KillPlayerCommand` 结算服务端生命；归零置死亡态并由世界同步补发包 118 | `Vanilla_Hurt_Reduces_ServerHealth` / `Vanilla_Lethal_Hurt_Kills_And_Broadcasts_Death` / `Vanilla_Negative_Hurt_Is_Rejected` |
| **服务端伤害来源（接触）** | 117 / 16 | `SimulateCombat` 判定敌怪 / Boss 接触（32px）→ `ApplyPlayerDamage` 扣血（60tick 免伤帧）→ 广播包 117 + 单发包 16 权威血量；下落伤害同样经该唯一入口 | `Vanilla_EnemyContact_Damages_Player_And_Notifies` / `Vanilla_ContactDamage_Has_ImmunityWindow` |
| **复活（服务端划定复活点）** | 12 | Playing 阶段的包 12 = 复活请求 → `RespawnCommand`：忽略客户端坐标，固定回到世界出生点并满血；由世界同步补发包 12 / 16 | `Vanilla_Respawn_After_Death_Uses_Server_Spawn` |
| **法力（服务端跟踪）** | 42 | 非负校验；法力上限由服务端持有，超出即下发纠正包；当前法力不得高于上限。原版不向他人转发法力，故只做权威跟踪 | `Vanilla_Mana_Is_Tracked_Server_Side` / `Vanilla_Mana_Above_Server_Max_Gets_Correction` |
| **治疗（上限钳制）** | 35 | 非负校验 → `HealPlayerCommand`：回血上限钳制到服务端 HpMax，客户端超额治疗不会让服务端生命越界 | `Vanilla_Heal_Is_Clamped_To_Server_Max_Hp` / `Vanilla_Negative_Heal_Is_Rejected` |
| **增益（服务端持有）** | 50 | 条目数 ≤ 44（原版增益槽位）且 ID ∈ [1,400] 校验通过后，由服务端持有增益列表（唯一真相） | `Vanilla_Buffs_Are_Held_Server_Side` / `Vanilla_Invalid_Buff_Id_Is_Rejected` |
| **弹幕生成校验** | 27 | 弹幕类型须在 [1,1135]；伤害超单次上限即判为作弊拒绝（与包 28 共用阈值）；通过后由服务端登记实体 | `Vanilla_Projectile_Damage_Above_Limit_Is_Rejected` / `Vanilla_Projectile_Invalid_Type_Is_Rejected` |
| **掉落物拾取** | 22 | 槽位对账（真实存活实体）+ 拾取半径校验 + 服务端背包入库（SSC）→ 移除世界实体并下发包 21（stack=0） | `Vanilla_ItemPickup_Removes_WorldItem` / `Vanilla_ItemPickup_OutOfReach_Is_Rejected` |
| **弹幕命中判定** | 27 | 服务端按弹幕 / 敌怪距离判定命中并扣血，不再采信客户端声明 | `Vanilla_Projectile_Hit_Damages_Enemy` |
| 请求传送（回城类） | 73 | 类型 / 频率校验（与 65 共窗口） | `Vanilla_TeleportRequest_Is_Accepted` / `Vanilla_TeleportRequest_RateExceeded_Is_Rejected` |
| **箱子内容（服务端持有 + 持久化）** | 31 / 32 / 34 | 开箱校验（存在 / 距离）→ 服务端逐槽下发权威内容（包 34 + 包 32×N）；包 32 校验箱子 / 槽位 / 堆叠 / 物品 / 距离后写入服务端箱子，并**登记增量落盘**（重启后回放，按索引 + 坐标校验） | `Vanilla_ChestOpen_Sends_Authoritative_Contents` / `Vanilla_ChestItem_Is_Applied_Authoritatively` / `..._Invalid_Slot_...` / `..._OutOfReach_...` / `Vanilla_ChestContent_Survives_ServerRestart` |
| **液体（NetLiquid）** | 82 模块 0 | 客户端上报液体编辑（坐标 / 类型 / 距离校验）→ `LiquidEditCommand` 权威落盘 → 简化流动仿真（下落优先、受阻后侧向均衡）+ 混合反应（异种液体累计 ≥ 24 单位 → 黑曜石 / 蜂蜜块 / 松脆蜂蜜块 / 微光块）→ 按快照频率批量下发 | `Vanilla_Liquid_Edit_Is_Applied_And_Flows_Down` / `..._Changes_Are_Broadcast_To_Client` / `..._OutOfReach_...` / `..._Invalid_Type_...` / `Vanilla_LiquidMerge_Water_Plus_Lava_Creates_Obsidian` |
| **电路（线网 / 执行器）** | 17 | action 0..19 权威应用（方块 / 墙 / 4 色线网 / 执行器的放置与拆除）；action 19 触发服务端沿电线受限 BFS 翻转执行器 | `Vanilla_Wire_Place_And_Kill_Are_Applied` / `..._Actuator_Place_And_Kill_...` / `..._Actuate_Toggles_Connected_Actuators` / `..._Does_Not_Propagate_Without_Wire` |
| **Boss / 事件** | 7 / 21 / 23 / 28 | 简化事件状态机（血月 / 日食按昼夜概率）+ 入侵（配额刷怪、耗尽结束）+ Boss（简化追击 AI、击杀记录世界进度，进度位按已核对 NPC ID 映射）；进度变化重新下发包 7；击杀按已核对掉落表生成掉落物并由世界同步补发包 21 | `Vanilla_BloodMoon_Is_Broadcast_As_WorldData` / `..._DayNight_Transition_...` / `..._Boss_Spawn_And_Kill_Sets_Progress` / `..._Invasion_Spawns_Enemies_Then_Ends` / `..._Eclipse_...` / `Vanilla_BossKill_Drops_Loot_And_Pushes_Packet21` / `Vanilla_Progress_Uses_Verified_BossIds` |

---

## 二、**部分实现 / 未实现**的原版功能

| 功能 | 现状 | 备注 |
|---|---|---|
| 敌怪生成 / AI | 已**简化**实现：史莱姆 + 入侵哥布林 + Boss（眼魔 / 骷髅王 / 史莱姆王）直线追击 | 无原版刷怪规则（生物群系 / 昼夜细分 / 事件）、无 NPC 专属 AI；接触伤害已由服务端判定（见 §一「服务端伤害来源」） |
| 箱子内容管理 | 已**服务端持有并持久化**：开箱下发权威内容，包 32 校验后写入服务端箱子并增量落盘（重启回放） | 未实现包 33（完整箱子同步）/ 箱子命名 / 上锁 / 放置新箱子（包 34 语义为 SyncPlayerChestIndex，非放置）；客户端 UI 依赖服务端逐槽包 32 |
| 电路 / 液体 | 已**简化实现**：液体逐格流动 + 混合反应 + NetLiquid 同步；线网 4 色 / 执行器编辑权威 + 受限 BFS 信号传播 | 无液体压力模型；无门电路 / 定时器 / 压力板（action 18 未建模） |
| 世界进度 / Boss 事件 | 已**简化实现**：血月 / 日食 / 入侵 / Boss 击杀进度 + 掉落（简化表）+ 包 7 广播 | 未实现原版事件触发规则（祭坛 / 召唤物 / 生物群系）与 Boss 专属 AI 行为；掉落为简化表 |
| 世界改动持久化 | 已**实现增量落盘 + 启动回放**：图格改动（含方块 / 墙 / 液体 / 电线 / 执行器）与**箱子内容**（包 32）按 1Hz 落盘（单批 8192，集合溢出降级为全图扫描），重启后叠加回基准世界 | 箱子内容回放按「索引 + 坐标」双校验，基准世界被替换时坐标不符则跳过；崩溃最多丢 1 秒改动 |
| 世界文件（`.wld`）加载 / 导出 | 已**实现**：`ServerConfig.WorldPath` 指定 `.wld` 即作为基准世界开服；`WorldExportPath` 非空则停机 + 空服周期**导出整份 `.wld`**（自研写出器：全部 11 段含 5..9 段的合法空编码，写后读回校验 + 原子替换 + `.bak` 滚动） | 已验证：**逐格 round-trip** + **按原版加载器顺序的严格分段走查**（段起点递增、逐段位置断言、footer 用尽文件）。**未在**原版二进制**上实测加载**（本沙箱无法运行 TerrariaServer.exe，连其自建世界也崩溃） |
| 断线重连 | 已**实现「手动重进的会话接管」**：宽限期（默认 60s）内以同身份重连 → 继承位置 / 血量 / 增益，出生点由服务端按恢复坐标下发 | 原版**无自动重连**（断线即 `Netplay.Disconnect` 回主菜单），故不做自动续传；宽限期外 / 被顶号则回收为全新会话 |

> 结论：服务端已达「可进服 + 地形可见 + 彼此可见 + 挖放砖 / 箱子内容 / 液体 / 电路权威 + 受伤死亡复活 / 拾取 / 命中结算
> + 聊天 + 时间 / NPC / Boss·事件同步」，并已从「简化刷怪」推进到「简化事件与 Boss 闭环」，
> 但仍**不是完整可玩的原版服务器**：缺原版物理 / AI / 事件规则、完整掉落数据库、电路元件逻辑、液体压力模型。

---

## 三、已知隐患

1. **`MaxSingleDamage` 与协议量纲冲突**：`NpcStrike.Damage` 线格式为 **Int16**（±32767），而默认上限为 30000，
   两者几乎贴边；若把上限配置为 >32767，该上限**永远不会触发**。已加启动期校验：`MaxSingleDamage > 32767` 直接拒绝启动并提示量纲约束。
2. **出站包（7 / 10 / 15 等）未在解码器建模**：这些包只发不收，`PacketDecoder` 落为 `UnknownPacket`；
   自本轮起 18 / 23 / 82 已建模（可被客户端侧测试断言）。
3. **NPC 同步不含 ai / 增益**：仅发「位置 + 速度 + 朝向 + 生命」，故客户端看不到 NPC 的 ai 驱动动作与增益。
4. `SnapshotStore.Snapshot()` 每轮 `ToArray()` 复制 —— 见 `Net/Phase4/README.md`「后续可能优化」。
5. 原版客户端**不支持预测协议**，延迟只能靠快照频率缓解（不影响防作弊，见 `architecture.md` 约束）。
6. **玩家受伤：接触 / 下落伤害已改为服务端判定**（`SimulateCombat` + `ApplyPlayerDamage`，含 60tick 免伤帧，
   并下发包 117 表现 + 包 16 权威血量）；遗留：**敌怪远程弹幕**未建模（当前敌怪不会发射弹幕），
   且客户端上报的包 117 仍作为「额外伤害来源」被接受（可叠加，但无法凭空回血 / 抬高上限）。
7. **图格变更推送采用「小矩形包 10」**：服务端驱动的图格修改（电路翻转执行器等）以包 10 小矩形（宽 × 1，按行合并）
   推送给视口内玩家，而非原版的包 20（SendTileSquare）；若与原版客户端行为有差异，需按实测调整。
8. **液体同步已按视口裁剪**，但仍为「每玩家全量过滤」（复杂度 O(玩家数 × 变更数)）；玩家数 / 变更数继续增大后需按区块分桶下发。
9. **Boss / 事件为简化模型**：Boss AI 仅直线追击、生命值为简化表；血月 / 日食为昼夜概率、入侵为配额刷怪，均非原版规则；
   掉落为**简化表**（仅收录眼魔 / 世界吞噬者 → 恶魔矿、史莱姆王 → 凝胶、蜂后 → 蜂蜡），未复刻原版掉落数据库。
10. **液体混合反应为简化模型**：已实现异种液体累计 ≥ 24 单位生成混合图格（水 + 岩浆 → 黑曜石 56、水 + 蜂蜜 → 蜂蜜块 229、
    岩浆 + 蜂蜜 → 松脆蜂蜜块 230、微光 + 任意 → 微光块 659，图格 ID 已核对）；遗留：仅在本格为空时生成
    （原版还允许覆盖可被黑曜石破坏的图格），且无液体压力模型。
11. **区块超帧上限已有兜底**：编码后超过 `UInt16`（65535）时按较长轴二分拆分再发（`NetworkHost.SendTileSectionAsync`），
    避免高熵区块直接抛异常中断登录；原版是同通道的降级压缩路径，此处用拆分替代。
12. **弹幕伤害为「上界校验」而非原版推导**：包 27 的伤害仍由客户端声明，服务端只保证不超过配置的单次伤害上限
    （与包 28 共用阈值）；原版伤害由武器 / 装备推导，此处未建模武器表。法力 / 增益已改为服务端跟踪与持有，
    但**增益的效果**仍由客户端计算（服务端只维护列表），属简化模型。

---

## 四、协议字段的来源与核对原则

本矩阵中所有字段与类型**均按原版客户端（协议 326）的收发行为逐字段核对**，非推测：

- **入站字段类型以客户端读取侧为准** —— 写入侧可能省略类型（例如某个整型字段线上实际为 1 字节），只看写入侧容易踩量纲陷阱。
- **已踩过的量纲坑**：`NpcStrike.Damage` 线上为 **Int16**（±32767），与默认上限 30000 几乎贴边（见 §三 第 1 条）。
- **NetModule 帧**：`[UInt16 长度][Byte 82][UInt16 模块号][负载]`；模块号由注册顺序决定（聊天模块为 1）。
- **新增包的核对清单**：包号 → 出站写入顺序 → 入站读取顺序（**类型以此为准**）→ 条件位与可选段。

> 本节不含任何第三方源码；核对方式为协议行为比对，属互操作性范畴。

---

## 五、如何运行

```bash
# 仅原版功能端到端套件
dotnet test Tests/TerraAuth.Tests.csproj -c Release --filter "FullyQualifiedName~VanillaFeatureTests"
```
