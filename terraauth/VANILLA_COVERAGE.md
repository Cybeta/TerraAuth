# TerraAuth — 原版功能覆盖与验证矩阵

> 记录「原版客户端会用到的功能」在服务端的实现与验证状态，供"测试原版所有功能"时对照。
> 自动化验证见 [`Tests/VanillaFeatureTests.cs`](Tests/VanillaFeatureTests.cs)：**真实权威管线（GameHost.Bootstrap）+ 真实 TCP**，
> 与 `IntegrationTests`（多为桩管线）互补。
> 最后更新：2026-09-11

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
| 请求传送（回城类） | 73 | 类型 / 频率校验（与 65 共窗口） | 解码 + 校验已实现，**未单测** |
| 弃用包健壮性 | 25 | 未知 / 弃用包透传，连接不受影响 | `Vanilla_DeprecatedChatPacket_DoesNot_Disconnect` |
| **他人可见性（中继）** | 117 / 118 / 35 / 36 / 50 / 32 | 权威通过后转发给其他玩家；携带玩家字段的包以服务端分配 ID 覆盖（防伪造身份） | `Vanilla_PlayerHurt_Is_Relayed_With_ServerPlayerId` / `..._PlayerBuffs_...` |
| **世界时间同步** | 18 (Time) | 持续下发 `Byte dayTime + Int32 time + Int16 sunModY + Int16 moonModY` | `Vanilla_Time_Is_Synced_To_Client` |
| **NPC 生成 / 同步** | 23 (SyncNPC) | 定期下发世界 NPC（索引 / netID / 位置 / 速度 / 朝向）；已满血形态省略生命段 | `Vanilla_Npc_Is_Synced_To_Client` |
| **聊天** | 82 (NetModule → NetTextModule) | 客户端发言 → 转服务端下行形态广播给所有人；`IServerApi.Broadcast/SendMessage` 真实下发 | `Vanilla_Chat_Is_Relayed_To_OtherPlayers` / `Vanilla_ServerBroadcast_Reaches_Client` |

---

## 二、**未实现**的原版功能（原版客户端会用到，服务端当前不同步）

| 功能 | 影响 | 备注 |
|---|---|---|
| 敌怪生成 / AI | 只有出生点的向导 NPC，没有敌怪 | 仿真有 NPC 模型但无刷怪逻辑 |
| 弹幕 / 掉落物的**服务端权威模拟** | 中继可用，但服务端不模拟其运动与生命周期 | 当前为「客户端上报 → 校验 → 中继」 |
| 箱子内容管理 | 包 32 可中继，但服务端不维护箱子内容 | 打开箱子看到的仍是客户端本地数据 |
| 电路 / 液体 | 未实现 | |
| 受伤 / 死亡 / 复活流程 | 117 / 118 仅中继，无服务端结算与复活 | |
| 世界进度 / Boss 事件 | 包 7 仅在进服时下发一次 | |

> 结论：服务端已达「可进服 + 地形可见 + 彼此可见（含移动 / 受伤 / 增益 / 弹幕 / 掉落物）+ 挖放砖权威 + 聊天 + 时间与 NPC 同步」，
> 但**仍不是完整可玩的原版服务器**（缺敌怪、掉落物与弹幕的服务端模拟、箱子内容、电路液体、死亡复活）。

---

## 三、已知隐患

1. **`MaxSingleDamage` 与协议量纲冲突**：`NpcStrike.Damage` 线格式为 **Int16**（±32767），而默认上限为 30000，
   两者几乎贴边；若把上限配置为 >32767，该上限**永远不会触发**。建议上限不超过 32000，或在配置文档中明确量纲约束。
2. **出站包（7 / 10 / 15 等）未在解码器建模**：这些包只发不收，`PacketDecoder` 落为 `UnknownPacket`；
   自本轮起 18 / 23 / 82 已建模（可被客户端侧测试断言）。
3. **NPC 同步未做视口裁剪**：`GameHost.BroadcastWorldStateAsync` 把全世界 NPC 发给所有玩家，
   仅适合当前「小世界 + 极少 NPC」场景；NPC 数量增长后需按视口过滤。
4. **NPC 同步不含 ai / 生命 / 增益**：仅发「满血 + 无 ai」形态，故客户端看不到 NPC 血量与特殊动作。
5. **聊天未接入限流**：`RateAuthority` 的聊天限流针对包 25（已弃用），包 82 未纳入令牌桶。
6. `SnapshotStore.Snapshot()` 每轮 `ToArray()` 复制 —— 见 `Net/Phase4/README.md`「后续可能优化」。
7. 原版客户端**不支持预测协议**，延迟只能靠快照频率缓解（不影响防作弊，见 `architecture.md` 约束）。

---

## 四、协议布局参考来源

本矩阵中 18 / 23 / 82 的字段与类型**全部来自原版**（非猜测），工具链与位置：

- 工具：`本机对照工具`（`dotnet tool install -g 本机对照工具`）
- 目标：`Terraria/Terraria.exe`（客户端）/ `TerrariaServer.exe`（服务端，同源码）
- 全量产物：`<工作区根>/reference/`（**仓库之外**，1549 个 `.cs`，不入库）
- 关键类型：
  - `Terraria.ID.MessageID`（全部包号常量）
  - `Terraria.NetMessage.SendData`（出站：`case 18 / 23 / 82` …）
  - `Terraria.MessageBuffer.GetData`（入站：对应 case 的读取顺序，字段类型以此为准）
  - `Terraria.GameContent.NetModules.NetTextModule`（聊天上下行负载）
  - `Terraria.Initializers.NetworkInitializer`（NetModule 注册顺序 → NetTextModule 模块号 = 1）
  - `Terraria.Net.NetPacket` / `NetManager`（NetModule 帧：`[UInt16 长度][Byte 82][UInt16 模块号][负载]`）

> 新增包时请先查 `MessageBuffer.GetData` 的对应 case 确认**字段类型与顺序**（例如 `NpcStrike.Damage` 是 Int16 —— 曾因此踩坑）。

---

## 五、如何运行

```bash
# 仅原版功能端到端套件
dotnet test Tests/TerraAuth.Tests.csproj -c Release --filter "FullyQualifiedName~VanillaFeatureTests"
```
