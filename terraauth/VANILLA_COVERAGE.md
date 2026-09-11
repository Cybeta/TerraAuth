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
| 物品丢弃 | 21 | 物品 ID / 堆叠校验 | `Vanilla_ItemDrop_UnknownItem_IsRejected` |
| 开箱 | 31 | 坐标越界校验 | `Vanilla_Chest_OutOfBounds_IsRejected` |
| 攻击 NPC | 28 | 单次伤害上限 + 窗口内 DPS 上限 | `Vanilla_NpcStrike_Above_SingleDamage_Limit_IsRejected` / `..._Within_Limit_IsAccepted` |
| 抛射物 | 27 | 字段 / 速率校验 | `Vanilla_Projectile_Is_Not_Rejected` |
| 生命 / 法力上报 | 16 | 上限校验；超限则下发**纠正包 16** | `Vanilla_Health_Above_ServerMax_Gets_Correction` |
| 传送 | 65 | 实体索引 / 落点越界 / 频率校验 | `Vanilla_Teleport_OutOfBounds_IsRejected` |
| 请求传送（回城类） | 73 | 类型 / 频率校验（与 65 共窗口） | 解码 + 校验已实现，**未单测** |
| 弃用包健壮性 | 25 | 未知 / 弃用包透传，连接不受影响 | `Vanilla_DeprecatedChatPacket_DoesNot_Disconnect` |
| **他人可见性（中继）** | 27 / 21 / 117 / 118 / 35 / 36 / 50 / 32 | 权威通过后**转发给其他玩家**；携带玩家字段的包一律以服务端分配 ID 覆盖（防伪造身份驱动他人状态） | `Vanilla_Projectile_Is_Relayed_To_OtherPlayers` / `..._ItemDrop_...` / `..._PlayerHurt_Is_Relayed_With_ServerPlayerId` / `..._PlayerBuffs_...` |

---

## 二、已解码 / 已校验，但**无服务端行为**（透传接受）

| 包 | 现状 |
|---|---|
| 32 `SyncChestItem` | 仅解码；箱子内容不同步 |
| 35 `PlayerHeal` | 仅解码；无回血结算与下发 |
| 36 `SyncPlayerZone` | 仅解码；无生物群系 / 城镇 NPC 状态同步 |
| 50 `PlayerBuffs` | 仅解码；无增益列表同步 |
| 117 `PlayerHurtV2` / 118 `PlayerDeathV2` | 仅解码；无受伤 / 死亡 / 复活流程 |

---

## 三、**未实现**的原版功能（原版客户端会用到，服务端当前不同步）

| 功能 | 影响 | 备注 |
|---|---|---|
| NPC 生成与同步（包 23 等） | 世界中无敌怪 / 城镇 NPC | 仿真有 NPC 模型，缺网络同步 |
| 世界物品 / 掉落物同步（包 21 下行） | 掉落物不可见 | 当前仅上行校验 |
| 弹幕同步（包 27 下行） | 他人弹幕不可见 | 当前仅上行校验 |
| 聊天（NetTextModule 文本包） | 无聊天 | 包 25 自 1.4 弃用；`IServerApi.Broadcast/SendMessage` 目前仅落审计 |
| 箱子内容同步（包 32 下行） | 打开箱子看不到内容 | |
| 时间 / 天气 / 月相持续同步 | 客户端昼夜不推进 | 服务端有推进逻辑（包 7 仅进服首发） |
| 受伤 / 死亡 / 复活流程 | 无伤害同步与复活 | |
| 电路 / 液体 | 未实现 | |

> 结论：当前服务端是「防作弊代理 + 移动 / 图格权威 + 快照下发」的**子集**。
> 原版客户端可进世界、可看到地形与彼此移动、可挖 / 放砖（服务端权威），但**尚未**达到"完整可玩"。

---

## 四、已知隐患

1. **`MaxSingleDamage` 与协议量纲冲突**：`NpcStrike.Damage` 线格式为 **Int16**（±32767），而默认上限为 30000，
   两者几乎贴边；若把上限配置为 >32767，该上限**永远不会触发**。建议上限不超过 32000，或在配置文档中明确量纲约束。
2. **出站包（7 / 10 / 15 等）未在解码器建模**：这些包只发不收，`PacketDecoder` 落为 `UnknownPacket`，
   自动化测试无法直接断言其字段（本矩阵改用服务端权威状态作参照）。若需校验出站 payload，应单独增加"出站包解析"工具。
3. `SnapshotStore.Snapshot()` 每轮 `ToArray()` 复制 —— 见 `Net/Phase4/README.md`「后续可能优化」。
4. 原版客户端**不支持预测协议**，延迟只能靠快照频率缓解（不影响防作弊，见 `architecture.md` 约束）。

---

## 五、如何运行

```bash
# 仅原版功能端到端套件
dotnet test Tests/TerraAuth.Tests.csproj -c Release --filter "FullyQualifiedName~VanillaFeatureTests"
```

---

## 六、下一轮：待补协议布局（阻塞项）

以下三项**需要权威线格式**才能实现。不做猜测式实现 —— 布局错误会产出真实客户端无法解析的包，
比"未实现"更糟（会表现为客户端异常/掉线，且难以定位）。

| 项 | 需要的包 | 需要的布局信息 | 阻塞原因 |
|---|---|---|---|
| 时间 / 天气 / 月相同步 | **18 (Time)** | `NetMessage.SendData` case 18 的字段顺序与类型（dayTime / time / moonPhase / bloodMoon / eclipse …） | 仓库内既无该包编解码，也无布局文档；TShock 官方 Wiki 抓取只取到目录部分 |
| 聊天 | **82 (NetModule) + NetTextModule** | NetModule 帧结构（模块标识 + 长度 + 负载）与 `NetTextModule.SerializeServerMessage` 的负载字段（作者 / 文本 / 颜色） | 同上 |
| NPC 同步 | **23 (NPC Update)** | 完整字段序列与条件位（netId / 命中 / 生命 / 增益 / 目标 / `ai[]` 等随标志位增减）；另需服务端 NPC 生命周期（生成 / 定期更新 / 失效） | 同上 |

**解除阻塞方式**：提供本机原版源码中 `Terraria.MessageBuffer.GetData` / `NetMessage.SendData` 的
`case 18 / 23 / 82` 片段（工作区已有 `Terraria/Terraria.exe`），或指定一份可信的 1.4.5.8 协议参考。

> 另注：**时间同步是否需要**尚待实测确认 —— 原版客户端可能自行按 tick 推进 `Main.time`（服务端同样每 tick +1），
> 若确实如此则无需持续下发；只有在服务端强制改时间（如日月切换）时才需要包 18。建议先用真实客户端观察昼夜是否自行推进。
