# TerraAuth — 原版功能覆盖与验证矩阵

> 记录「原版客户端会用到的功能」在服务端的实现与验证状态，供"测试原版所有功能"时对照。
> 自动化验证见 [`Tests/VanillaFeatureTests.cs`](Tests/VanillaFeatureTests.cs)：**真实权威管线（GameHost.Bootstrap）+ 真实 TCP**，
> 与 `IntegrationTests`（多为桩管线）互补。
> 最后更新：2026-09-16（基础原版客户端真机验证已完成；小动物逐类 aiStyle 与地表树木已落地；完整游玩回归仍待完成）

---

## 一、已实现并自动化验证

| 原版功能 | 客户端包 | 服务端行为 | 验证用例 |
|---|---|---|---|
| 连接 / 协议协商 | 1 | 版本串校验 → 分配 PlayerId（包 3） | `Vanilla_LoginChain_Delivers_All_Required_Packets` |
| 玩家信息 / 外观 | 4 | 名称合法性 + 白名单 + 记录外观（Slot 覆盖为服务端 ID） | `Vanilla_SecondPlayer_Is_Broadcast_To_First` |
| 请求世界数据 | 6 | 回包 7（真实世界元数据） | `Vanilla_LoginChain_...` |
| 请求出生区块 | 8 | 回包 9（进度）+ 逐块 10 + 49（出生） | `Vanilla_LoginChain_...` |
| **区块流送（离开出生点后的地形）** | 10 | 服务端**主动按玩家位置补发**（快照循环 20Hz）：以玩家所在区块为中心取 **3×3**，**逐连接去重**（已发过的区块不重复编码），有新块时先发包 9（进度）再逐块发包 10 | `Vanilla_TileSections_Stream_As_Player_Moves` |
| **服务端驱动的图格改动** | 20 | 执行器翻转 / 液体混合等少量图格改动走**包 20（TileSquare）**：未压缩小矩形 + 逐格位标志与可选段，按视口下发给附近玩家；矩形宽度超 Byte 上限（255）自动切分 | `Vanilla_Actuate_Pushes_Tile_Update_To_Client` / `Encode_TileSquare_Writes_Vanilla_Layout` |
| **未建模包处理** | （任意） | 当前 Vanilla-only 生产路径对未结构化包默认拒绝，不进行即时中继；**拒绝不计入违规窗口**（否则正常客户端累计若干次即被误踢），并按 `PacketId` 统计（首次出现必打印、每 100 次打印、停机汇总）；状态包仅在仿真提交后由服务端生成同步包 | `Vanilla_UnmodeledPacket_Is_Rejected_And_Not_Relayed` / `Vanilla_UnmodeledPackets_Are_Counted_But_DoNotCause_Kick` |
| 进入世界 | 12 | 置 Playing → 包 129 + 广播外观 4 / 激活 14 | `Vanilla_Join_Marks_Self_Active` |
| 玩家激活在线 / 离线 | 14 | 进服广播激活；断线广播 `Active=false` | `Vanilla_PlayerDisconnect_Broadcasts_Inactive` |
| **断线会话保留 + 槽位回收** | 14 | 断线不销毁运行时：按玩家名保留位置 / 血量 / 增益（`SessionResumeGraceSeconds`，默认 60s），宽限期内同身份重连**认回原运行时**并下发**携带恢复坐标**的出生包（12）；超期 / 被顶号回收。断开时释放连接槽位与并发容量，新连接复用**最小空闲 ID**（与原版一致）。注：原版客户端断线即回主菜单，故为「手动重进的会话接管」而非自动重连 | `Vanilla_SessionResume_Restores_Position_And_Hp` / `..._Off_When_Grace_Is_Zero` / `SessionResume_Expires_After_Grace` |
| 移动 / 位置 | 13 | **分轴**超速校验（水平 `maxSpeed×60×Δt`；垂直 `max(maxSpeed, MaxFallSpeed)×60×Δt`，均 + 容差）；超上限在 **×4 可疑带**内放行（原版受击击退 / 被挤出方块 / 斜坡校正的合法大位移），只有真瞬移量级才拒绝 → Command → 仿真 → 快照 15；并转发其他玩家 | `Vanilla_Movement_Accepted_And_Applied` / `..._Overspeed_IsRejected` / `MovementAuthority_Accepts_KnockbackScale_Step` |
| 挖砖 | 17 | 越界 / 超距（包 17 第 5 字段在「挖」时是 **fail 标志**而非图格类型，不作类型对账）→ TileBreakCommand → 图格变更 + **图格掉落**（`TileDropTable` 静态图格、成熟草药按帧掉主物品，开花草药额外掉种子；树木整棵倒下并按手持斧力单次判定额外木材）→ 转发 | `Vanilla_TileBreak_Removes_Solid_Tile` / `Vanilla_TileBreak_OutOfReach_IsRejected` / `Vanilla_TileBreak_HitOnly_Flag_Is_Not_Rejected_As_TypeMismatch` / `Vanilla_TileBreak_Drops_Item_To_Client` / `TileBreakCommand_Drops_Mature_And_Flowering_Herbs` / `TileBreakCommand_Tree_AxePower_Adds_One_Wood_Per_Tree` |
| 放砖 | 79 | 越界 / 超距 / 类型范围 / **背包扣减（SSC）** → TilePlaceCommand | `Vanilla_InventoryReport_Then_TilePlace_Succeeds` / `..._Without_InventoryItem_IsRejected` |
| 背包同步 | 5 | 槽位 / 堆叠 / 物品校验；SSC 下服务端持有唯一真相；**断线时按玩家名把背包 / 生命 / 法力落盘，新会话进服时回读**（`PlayerProfileCodec`，否则重进即清空） | `Vanilla_InventorySlot_InvalidSlot_IsRejected` / `Vanilla_SscInventory_Survives_Reconnect` / `PlayerProfileCodec_RoundTrips_Inventory_And_Vitals` |
| 物品丢弃 | 21 | 物品 ID / 堆叠校验；并中继给他人 | `Vanilla_ItemDrop_UnknownItem_IsRejected` / `..._Is_Relayed_To_OtherPlayers` |
| 开箱 | 31 | 坐标越界校验 | `Vanilla_Chest_OutOfBounds_IsRejected` |
| 攻击 NPC | 28 | 单次伤害上限 + 窗口内 DPS 上限 | `Vanilla_NpcStrike_Above_SingleDamage_Limit_IsRejected` / `..._Within_Limit_IsAccepted` |
| 抛射物 | 27 | 字段 / 速率校验；并中继给他人 | `Vanilla_Projectile_Is_Not_Rejected` / `..._Is_Relayed_To_OtherPlayers` |
| 生命 / 法力上报 | 16 | 上限校验；超限则下发**纠正包 16** | `Vanilla_Health_Above_ServerMax_Gets_Correction` |
| 传送 | 65 | 实体索引 / 落点越界 / 频率校验 | `Vanilla_Teleport_OutOfBounds_IsRejected` |
| 弃用包健壮性 | 25 | 未知 / 弃用包透传，连接不受影响 | `Vanilla_DeprecatedChatPacket_DoesNot_Disconnect` |
| **他人可见性（中继）** | 117 / 35 / 36 / 50 / 32 / 13 | 权威通过后转发给其他玩家；携带玩家字段的包以服务端分配 ID 覆盖（防伪造身份）；**包 13 的挂载 / 相机 / 回城等可选尾随字段一并保留转发**。注：118 死亡不在此中继，改由服务端结算后统一广播 | `Vanilla_PlayerHurt_Is_Relayed_With_ServerPlayerId` / `..._PlayerBuffs_...` / `..._PlayerControls_Relay_Preserves_Mount_And_Camera` |
| **世界时间同步** | 18 (Time) | 持续下发 `Byte dayTime + Int32 time + Int16 sunModY + Int16 moonModY` | `Vanilla_Time_Is_Synced_To_Client` |
| **NPC 生成 / 同步** | 23 (SyncNPC) | 60Hz 循环内**逐 NPC 分档**下发：Boss / 近身（3 格内）NPC 逐 tick，其余 20Hz（原版令牌桶为普通 ≈2 包/秒、Boss ≈12Hz）；包内含索引 / netID / 位置 / 速度 / 朝向 / **原版 `ai[0..3]`**；**状态变化（含 ai / 朝向）才发**，未变化按 1s 心跳补发（保证新入服玩家能看到静止 NPC）；已满血形态省略生命段 | `Vanilla_Npc_Is_Synced_To_Client` / `Vanilla_NpcSync_SendsOnChange_AndSkipsUnchanged` / `Vanilla_NpcSync_Carries_Vanilla_Ai_State` |
| **聊天** | 82 (NetModule → NetTextModule) | 客户端发言 → 转服务端下行形态广播给所有人；`IServerApi.Broadcast/SendMessage` 真实下发 | `Vanilla_Chat_Is_Relayed_To_OtherPlayers` / `Vanilla_ServerBroadcast_Reaches_Client` |
| **掉落物 / 弹幕服务端模拟** | 21 / 27 / 29 | 服务端登记实体并推进生命周期：掉落物重力落地（槽位由服务端分配）、弹幕按**行为表**推进（见下）并到期，到期 / 撞图格由服务端补发包 29；**服务端弹幕（Boss AI 发射，`Owner = -1`）由世界同步补发包 27 广播给所有玩家**，客户端上报弹幕转发给除归属者外的玩家。**弹幕行为表**（原版 `SetDefaults` 字段）：96 CursedFlameHostile 直线 + 图格碰撞、101 EyeFire `extraUpdates = 3`（每 tick 积分 4 次）+ 生存期钳到 60、719 QueenBeeStinger 直线 + 图格碰撞；未登记类型保持简化直线积分。**命中归属**：玩家弹幕（`Owner >= 0`）结算敌怪伤害，敌对弹幕结算玩家伤害（`projectile_damage`，共用免伤帧） | `Vanilla_ItemDrop_Is_Tracked_And_Falls_Under_Gravity` / `Vanilla_Projectile_Is_Tracked_And_Expires_With_Server_Destroy` / `Vanilla_ServerProjectile_IsBroadcastToOtherPlayers` / `Vanilla_HostileProjectile_Damages_Player` / `Vanilla_Projectile_Stops_At_SolidTile` |
| **敌怪 AI（按原版 aiStyle）** | 23 + 28 | `WorldSimulator.NpcAi.cs` 按 `NPC.aiStyle` 分派：**aiStyle 1 史莱姆**（贴地等待 → 三档起跳 `-6/-8` → 空中加速，地面摩擦 `0.8`）、**3 战士**（加速 `0.07` / 上限 `1.5`、撞墙掉头、障碍起跳 `-8/-6/-5`）、**7 城镇 NPC**（离家 25 格、1/80 掉头、障碍起跳）、**4 眼魔**（追玩家上方 200px、600 帧转冲刺、每 110 帧召仆从）、**31 魔焰眼**（±400px 绕行 + type 96、二阶段贴身 type 101）、**43 蜂后**（`ai[0]` 攻击选择状态机 + 召唤 210/211 + type 719 毒刺）；未登记 aiStyle 走简化追击兜底。每 60 tick 刷史莱姆（上限 8）；包 28 扣血、归零即死亡；包 23 带生命段与 ai | `Vanilla_Enemy_Spawns_Then_Dies_From_Strike` / `Enemy_Hops_WithGroundedWait_InsteadOfGroundGliding` / `TownNpc_WalksSmoothly_WithoutPerTickDirectionFlip` / `Vanilla_Spazmatism_EmitsFireball_And_EntersCharge` / `Vanilla_QueenBee_CyclesAttackChoice` |
| **受伤 / 死亡（服务端结算）** | 117 / 118 | 伤害非负校验 → `DamagePlayerCommand` / `KillPlayerCommand` 结算服务端生命；归零置死亡态并由世界同步补发包 118 | `Vanilla_Hurt_Reduces_ServerHealth` / `Vanilla_Lethal_Hurt_Kills_And_Broadcasts_Death` / `Vanilla_Negative_Hurt_Is_Rejected` |
| **服务端伤害来源（接触）** | 117 / 16 | `SimulateCombat` 按**原版 `Update_NPCCollision` 口径**判定敌怪 / Boss 接触（玩家 20×42 vs NPC 逐类型尺寸；**两端取整后 AABB 求交、无最小重叠**）→ `ApplyPlayerDamage` 扣血（原版 `Hurt` 的 `immuneTime`：接触 / 弹幕 / 下落统一 40、伤害被压到 1 取 20，**与客户端上报的包 117 共用同一窗口**）→ 广播包 117 + 单发包 16 权威血量；下落伤害同样经该唯一入口（脚底 = `Position.Y + 42`） | `Vanilla_EnemyContact_Damages_Player_And_Notifies` / `Vanilla_ContactDamage_Has_ImmunityWindow` / `StandingPlayer_DoesNotAccumulate_FallDistance` / `ContactDamage_Aligns_With_Vanilla_Intersect` / `ClientReportedDamage_Respects_ImmunityWindow` |
| **复活（服务端划定复活点）** | 12 | Playing 阶段的包 12 = 复活请求 → `RespawnCommand`：忽略客户端坐标，固定回到世界出生点并满血；由世界同步补发包 12 / 16 | `Vanilla_Respawn_After_Death_Uses_Server_Spawn` |
| **法力（服务端跟踪）** | 42 | 非负校验；法力上限由服务端持有，超出即下发纠正包；当前法力不得高于上限。原版不向他人转发法力，故只做权威跟踪 | `Vanilla_Mana_Is_Tracked_Server_Side` / `Vanilla_Mana_Above_Server_Max_Gets_Correction` |
| **治疗（上限钳制）** | 35 | 非负校验 → `HealPlayerCommand`：回血上限钳制到服务端 HpMax，客户端超额治疗不会让服务端生命越界 | `Vanilla_Heal_Is_Clamped_To_Server_Max_Hp` / `Vanilla_Negative_Heal_Is_Rejected` |
| **增益（服务端持有）** | 50 | 条目数 ≤ 44（原版增益槽位）且 ID ∈ [1,400] 校验通过后，由服务端持有增益列表（唯一真相） | `Vanilla_Buffs_Are_Held_Server_Side` / `Vanilla_Invalid_Buff_Id_Is_Rejected` |
| **弹幕生成校验** | 27 | 弹幕类型须在 [1,1135]；伤害超单次上限即判为作弊拒绝（与包 28 共用阈值）；通过后由服务端登记实体 | `Vanilla_Projectile_Damage_Above_Limit_Is_Rejected` / `Vanilla_Projectile_Invalid_Type_Is_Rejected` |
| **掉落物拾取** | 22 | 槽位对账（真实存活实体）+ 拾取半径校验 + 服务端背包入库（SSC）→ 移除世界实体并下发包 21（stack=0），并给拾取者一条中文聊天提示（包 82，`ItemDisplayNameTable`；未知项回退调试名或 `Item#ID`） | `Vanilla_ItemPickup_Removes_WorldItem` / `Vanilla_ItemPickup_OutOfReach_Is_Rejected` / `PickupItem_Queues_Chat_Notice` / `PickupItem_Uses_ItemDisplayName_Fallback` |
| **弹幕命中判定** | 27 | 服务端按弹幕 / 敌怪距离判定命中并扣血，不再采信客户端声明 | `Vanilla_Projectile_Hit_Damages_Enemy` |
| 请求传送（回城类） | 73 | 类型 / 频率校验（与 65 共窗口） | `Vanilla_TeleportRequest_Is_Accepted` / `Vanilla_TeleportRequest_RateExceeded_Is_Rejected` |
| **箱子内容（服务端持有 + 持久化）** | 31 / 32 / 34 | 开箱校验（存在 / 距离）→ 服务端逐槽下发权威内容（包 34 + 包 32×N）；包 32 校验箱子 / 槽位 / 堆叠 / 物品 / 距离后写入服务端箱子，并**登记增量落盘**（重启后回放，按索引 + 坐标校验） | `Vanilla_ChestOpen_Sends_Authoritative_Contents` / `Vanilla_ChestItem_Is_Applied_Authoritatively` / `..._Invalid_Slot_...` / `..._OutOfReach_...` / `Vanilla_ChestContent_Survives_ServerRestart` |
| **液体（NetLiquid）** | 82 模块 0 | 客户端上报液体编辑（坐标 / 类型 / 距离校验）→ `LiquidEditCommand` 权威落盘 → 简化流动仿真（下落优先、受阻后侧向均衡）+ 混合反应（异种液体累计 ≥ 24 单位 → 黑曜石 / 蜂蜜块 / 松脆蜂蜜块 / 微光块）→ 按快照频率批量下发 | `Vanilla_Liquid_Edit_Is_Applied_And_Flows_Down` / `..._Changes_Are_Broadcast_To_Client` / `..._OutOfReach_...` / `..._Invalid_Type_...` / `Vanilla_LiquidMerge_Water_Plus_Lava_Creates_Obsidian` |
| **电路（线网 / 执行器）** | 17 | action 0..19 权威应用（方块 / 墙 / 4 色线网 / 执行器的放置与拆除）；action 19 触发服务端沿电线受限 BFS 翻转执行器 | `Vanilla_Wire_Place_And_Kill_Are_Applied` / `..._Actuator_Place_And_Kill_...` / `..._Actuate_Toggles_Connected_Actuators` / `..._Does_Not_Propagate_Without_Wire` |
| **原版刷怪规则** | 8 / 23 | 场景度量（原版阈值与图格表：腐化 / 猩红 300、神圣 125、丛林 140、雪原 1500、蘑菇 100、陨石 75、地牢 250、墓地 28；神圣与邪恶互相抵消）+ 深度带（天空 / 地表 / 泥土 / 岩层 / 地狱）→ `GetSpawnRate`（困难 540/6、夜晚 ×0.6、血月再 ×0.3、日食 ×0.2、群系系数、附近敌怪阶梯、下限 60 / 上限 15）→ 每 tick 掷骰 → `FindSpawnTile`（刷怪区 168×104、安全区不入、落点上方 2×3 需空）+ `CheckNotSpawningOnScreen` → `SpawnAnNPC` 分支池（含地表白天的**小动物**分支与夜晚的萤火虫 / 恶魔眼） | `NetIdMap_Resolves_BaseType` / `BiomeScanner_Applies_Vanilla_Thresholds_And_DepthBands` / `SpawnRate_Applies_DayNight_And_Event_Modifiers` / `SpawnPool_SurfaceDay_SpawnsCrittersAndSlimes` / `..._SurfaceNight_IsZombieFamily` / `..._BloodMoon_...` / `..._Eclipse_...` / `..._Sky_...` / `..._Underworld_...` / `..._GoblinInvasion_...` / `..._JungleCavern_...` |
| **小动物（critter）** | — | 集合覆盖原版 `NPCID.Sets.CountsAsCritter`（98 型）；**按 aiStyle 分类实现**（1 蚂蚱 / 7 城镇行走 / 16 鱼 / 24 鸟 / 64 萤火虫 / 65 蝴蝶 / 66 蚯蚓 / 67 蜗牛 / 68 鸭 / 112 仙灵 / 114 蜻蜓 / 115 瓢虫 / 116 水黾 / 118 海马），各类小动物均经协议行为验证（如蚂蚱复用 `Ai001Slimes` 的蚂蚱分支：`flag3` 时**背向玩家跳跃**；兔 / 松鼠按 aiStyle 7 的威胁扫描背向加速逃离），**永不主动接近玩家**；碰撞盒尺寸按原版 `SetDefaults` 补齐（兔 18×20 / 鸟 14×14 / 企鹅 16×34 …）。配套物理步已把 `noGravity` 与 `noTileCollide` **解耦**（蜗牛爬墙 / 海马 / 水黾这类「无重力但要格碰撞」的 AI 才能拿到 `collideX/collideY`）。与原版一致：小动物同样计入 `nearbyActiveNPCs`，因此**会占用 `MaxEnemies` 配额**（生产 `server.json` 为 2，白天可能被小动物占满 —— 想优先出敌怪就调高该值） | `CritterSet_Maps_To_Vanilla_AiStyle` / `CritterSet_Excludes_Hostiles` / `Critter_FleesFromPlayer_InsteadOfChasing` / `Critter_Bird_TakesOff_WhenPlayerApproaches` / `Critter_Worm_Crawls_When_Awake` / `Critter_Snail_DoesNotSinkThroughGround` / `Critter_Grasshopper_HopsAway_FromPlayer` / `Physics_NoGravity_Still_Collides_WithTiles` |
| **生成阶段生物群系** | — | 世界生成产出腐化 / 猩红（种子二选一）、雪原、沙漠（含地下沙漠墙）、丛林、发光蘑菇地、地牢，并写下 `Progress.Crimson` 与包 7 的 `DungeonX/Y`；锚点距出生点 ≥ 300 格、距边界 ≥ 420 格（出生点保持无群系）。判据直接用刷怪读的 `BiomeScanner`，即「群系池可达」由测试保证 | `Generate_Produces_Biomes_DetectedBySceneMetrics` / `Generate_Keeps_SpawnArea_Free_Of_Biomes` |
| **困难模式地形转换** | 113 → 包 7 | 血肉墙（113）被击杀 → `Progress.HardMode` 置位（原版 `WorldGen.StartHardmode`），仿真层检测到该位后执行**一次** `WorldGen.initializeHardMode` 的等价转换：按原版公式选两条带位置（`maxTilesX × 0.300~0.399` 与镜像，并按地牢所在侧挪到 `× 0.200~0.299`）→ 一条转**神圣**、一条**刷新为邪恶**（原版两次 `GERunner`，方向相反）→ 在地下洞窟补神圣 / 腐化 / 猩红背景墙；图格映射逐项对照 `WorldGen.Convert`（石 → 珍珠石 / 黑檀石 / 猩红石，草 → 神圣 / 腐化 / 猩红草，冰 → 神圣冰 / 腐化冰 / 血肉冰，沙与硬化沙 / 沙岩同理）。载入时已是困难模式的世界视为转换已完成（与原版守卫一致） | `WallOfFlesh_Kill_Sets_HardMode` / `Hardmode_Conversion_Creates_Hallow_Band` / `Hardmode_Conversion_Runs_Only_Once` |
| **陨石坠落** | 落点 API | 移植原版 `WorldGen.meteor` 的落点判据与坑体生成：35 格内不得有玩家屏幕 / NPC / 宝箱 / 地牢砖 / 禁落图格（26/226/470/475/488/597）；坑体按 5 圈同心衰减（内圈填陨石 37 → 上半部掏空 → 清液体与悬空陨石 → 两圈稀疏外扩）。**触发链未实现**（原版由砸碎第 3 颗暗影珠 / 猩红之心触发落点搜索，我们的世界尚未生成暗影珠），故以 `WorldSimulator.TryDropMeteor` 暴露给运维 / 插件 / 测试 | `Meteor_Creates_Crater_And_Unlocks_MeteorZone` |
| **生命水晶** | 12 → 包 10 | 世界生成在岩层撒生命水晶（数量 = `maxTilesX / 300`），摆放与判据走原版 `WorldGen.AddLifeCrystal`：自落点向下找第一块实地 → 2×2 摆放图格 12、帧 (0/18, 0/18)、**脚下两格必须实心**、无岩浆、非地牢墙 | `Generate_Places_Life_Crystals` |
| **Boss / 事件** | 7 / 21 / 23 / 28 | 简化事件状态机（血月 / 日食按昼夜概率）+ 入侵（配额刷怪、耗尽结束）+ Boss（眼魔 / 魔焰眼 / 蜂后按**原版 aiStyle 4 / 31 / 43** 驱动运动与攻击，其余简化追击；击杀记录世界进度，进度位按已核对 NPC ID 映射）；进度变化重新下发包 7；击杀按已核对掉落表生成掉落物并由世界同步补发包 21 | `Vanilla_BloodMoon_Is_Broadcast_As_WorldData` / `..._DayNight_Transition_...` / `..._Boss_Spawn_And_Kill_Sets_Progress` / `..._Invasion_Spawns_Enemies_Then_Ends` / `..._Eclipse_...` / `Vanilla_BossKill_Drops_Loot_And_Pushes_Packet21` / `Vanilla_Progress_Uses_Verified_BossIds` |

---

## 二、**部分实现 / 未实现**的原版功能

| 功能 | 现状 | 备注 |
|---|---|---|
| 世界生成 | 已**支持原版三档尺寸的程序化生成**（`ServerConfig.WorldSize` = `Small` 4200×1200 / `Medium` 6400×1800 / `Large` 8400×2400），并生成**分层地形**：草皮 / 泥土（含黏土、沙斑）/ 岩层 / 地狱层、洞穴、按深度分带的矿脉、两端海滩与海水、地下宝箱（2×2 摆放 + 战利品）、**地下生命水晶**（原版 `AddLifeCrystal`：2×2 + 帧 0/18）；**生成阶段应有的生物群系已产出**：腐化 / 猩红（按世界种子二选一，含 `Progress.Crimson`）、雪原、沙漠（含地下沙漠的沙岩 / 硬化沙墙）、丛林、发光蘑菇地、地牢（地牢砖 + 地牢墙 + 包 7 的 `DungeonX/Y`），群系为**斜向 + 横向蜿蜒**的条带（对应原版 `GERunner` 的斜插形态），锚点距出生点 ≥ 300 格、距边界 ≥ 420 格；层高比例 / 图格 ID / 摆放约定经协议行为验证；`WorldPath` 指定 `.wld` 时以真实世界为准 | 仍是「原版风格的分层生成」，**仍为简化的分层生成模型**：**地表树木已产出**（原版 `WorldGen.GrowTree` 的**树干**部分：可长树的 7 种地表 —— 草地 / 腐化 / 猩红 / 丛林 / 神圣 / 蘑菇草 / 雪块，需左右至少一侧同类地表以保证地势平缓，树干 5-16 格（丛林 +5），10 种树干图案 × 3 种抖动的原版帧表 + 枝条 / 根部 / 树顶帧；树冠在原版里不是图格，由客户端按树干顶帧 + 世界 `TreeTops` 元数据绘制，故不在图格数据内）/ 神庙 / 蜂巢 / 大理石·花岗岩等结构体**，群系形状为条带近似（非原版逐段移植）；神圣与陨石**刻意不产出**（原版也不在生成阶段：见「困难模式转换 / 陨石」行）；大世界内存约 0.5 GB |
| 敌怪生成 / AI | 已按**原版 aiStyle** 移植 6 套：1 史莱姆 / 3 战士 / 7 城镇 NPC / 4 眼魔 / 31 魔焰眼 / 43 蜂后（详见 §一「敌怪 AI」）；未登记 aiStyle 的 NPC 走简化追击兜底。刷怪已按**原版 `NPC.Spawner` 规则**：场景度量（生物群系 / 深度带，阈值与图格表同原版 `SceneMetrics`）→ `GetSpawnRate`（昼夜 / 血月 / 日食 / 困难 / 群系系数 + 钳制）→ `FindSpawnTile` 落点 → `SpawnAnNPC` 加权池（地表昼夜含**小动物** / 泥土层 / 岩层 / 地狱 / 地下沙漠 / 日食 / 血月 / 入侵 / 四柱塔按 `Main.invasionType`），负 netID 变体按 `NPCID.FromNetId` 解析。**小动物已纳入并按原版 aiStyle 逐类移植**（14 组，见 §一「小动物」）。**神圣与陨石池已可达**：神圣由困难模式转换产出、陨石由 `TryDropMeteor` 产出（见 §一） | 小动物已按原版 aiStyle 逐类移植（仅仙灵的「带路找宝箱」状态需宝藏地图数据、蠕虫 374 的变形等个别分支未移植，均已在代码注释标明）；换型（原版 `NPC.Transform`，如海鸥 603 ↔ 602 / 鸭 362 ↔ 363 的形态往返）已按原版实现并**同步 netID**（客户端只在包 23 的 netID 变化时重建外观，故换型后会强制补发一次包 23）；种子变体（remixWorld / drunkWorld / dualDungeons / Skyblock）、四柱区域、撒旦军队、南瓜月 / 霜月、花岗岩·大理石环境怪、沙尘暴未建模；水中生物（鱼 / 海龟）未建模；陨石缺自然触发链（暗影珠未生成）；入侵配额仍按「刷新扣减」（原版按击杀点数扣减）；其余 aiStyle 未移植（按需登记即可扩展）；接触伤害已由服务端判定（见 §一「服务端伤害来源」） |
| 箱子内容管理 | 已**服务端持有并持久化**：开箱（包 31 → `OpenChestCommand`，在仿真提交阶段建立**绑定连接会话**的打开会话）下发权威内容，包 32 校验后写入服务端箱子并增量落盘（重启回放） | 未实现包 33（完整箱子同步）/ 箱子命名 / 上锁 / 放置新箱子（包 34 语义为 SyncPlayerChestIndex，非放置）；客户端 UI 依赖服务端逐槽包 32。打开会话按 `(PlayerId, SessionId)` 绑定，并在**包 31 负坐标（关箱请求）**、**离开交互距离（每 tick 距离复核）**、断线三处关闭：旧连接（槽位被复用）的延迟箱子命令与断线清理不再影响新连接 |
| 电路 / 液体 | 已**简化实现**：液体逐格流动 + 混合反应 + NetLiquid 同步；线网 4 色 / 执行器编辑权威 + 受限 BFS 信号传播 | 无液体压力模型；无门电路 / 定时器 / 压力板（action 18 未建模） |
| 世界进度 / Boss 事件 | 已**简化实现**：血月 / 日食 / 入侵 / Boss 击杀进度 + 掉落（简化表）+ 包 7 广播 | 未实现原版事件触发规则（祭坛 / 召唤物 / 生物群系）；Boss 专属 AI 已部分落地（眼魔 4 / 魔焰眼 31 / 蜂后 43），其余仍为简化追击；掉落为简化表 |
| 世界改动持久化 | 已**实现增量落盘 + 启动回放**：图格改动（含方块 / 墙 / 液体 / 电线 / 执行器）与**箱子内容**（包 32）按 1Hz 落盘（单批 8192，集合溢出降级为全图扫描），重启后叠加回基准世界 | 箱子内容回放按「索引 + 坐标」双校验，基准世界被替换时坐标不符则跳过；崩溃最多丢 1 秒改动 |
| 世界文件（`.wld`）加载 / 导出 | 已**实现**：`ServerConfig.WorldPath` 指定 `.wld` 即作为基准世界开服；`WorldExportPath` 非空则停机 + 空服周期**导出整份 `.wld`**（写出器：全部 11 段含 5..9 段的合法空编码，写后读回校验 + 原子替换 + `.bak` 滚动） | 已验证（**双向**）：**逐格 round-trip** + **严格分段走查** + **服务端兼容性测试加载本服务端导出世界**（11 段指针均通过）+ **本服务端加载兼容世界**（逐段指针断言均通过，含 RLE 压缩图格段）。**双向格式互操作性均已确认**；原版客户端已完成连接、进图及 `/give` 掉落物拾取验证，完整游玩回归仍待验证。注意：读取器**跳过段 6..10**（图格实体 / 压力板 / 城镇管理 / 图鉴 / 创造之力），「原版世界 → TerraAuth → 再导出」会把这些段写成空编码（新生成世界通常本就为空） |
| 断线重连 | 已**实现「手动重进的会话接管」**：宽限期（默认 60s）内以同身份重连 → 继承位置 / 血量 / 增益，出生点由服务端按恢复坐标下发；接管的运行时**换发新的会话标识**，旧连接的在途命令一律 `stale_session` | 原版**无自动重连**（断线即 `Netplay.Disconnect` 回主菜单），故不做自动续传；宽限期外 / 被顶号则回收为全新会话 |

> 结论：服务端已达「可进服 + 地形可见 + 彼此可见 + 挖放砖 / 箱子内容 / 液体 / 电路权威 + 受伤死亡复活 / 拾取 / 命中结算
> + 聊天 + 时间 / NPC / Boss·事件同步」，并已从「简化刷怪」推进到「简化事件与 Boss 闭环」，
> 但仍**不是完整可玩的原版服务器**：缺原版物理 / AI / 事件规则、完整掉落数据库、电路元件逻辑、液体压力模型。

---

## 三、已知隐患

1. **`MaxSingleDamage` 与协议量纲冲突**：`NpcStrike.Damage` 线格式为 **Int16**（±32767），而默认上限为 30000，
   两者几乎贴边；若把上限配置为 >32767，该上限**永远不会触发**。已加启动期校验：`MaxSingleDamage > 32767` 直接拒绝启动并提示量纲约束。
2. **出站包（7 / 10 / 15 等）未在解码器建模**：这些包只发不收，`PacketDecoder` 落为 `UnknownPacket`；
   自本轮起 18 / 23 / 82 已建模（可被客户端侧测试断言）。
3. **NPC 同步已携带原版 `ai[0..3]`**：客户端在 ai 位未置位时会把该 ai 显式置 0，故「省略」与「发送 0」等价 ——
   依赖 ai 的 aiStyle（如史莱姆跳跃状态）**必须下发**，否则客户端表现与服务端不一致。现以 `bitsA bit2..5` 承载 4 个 float，
   并把 ai / 朝向纳入「变化才发」判定。已补齐**同步锚点**（史莱姆王：位置 + 体型×锚点）并把目标字段改为 **255（显式无目标）**。
   遗留：NPC 的**增益**（buff）未同步；未登记 aiStyle 的 NPC 动作仍由客户端自身 AI 兜底。
4. `SnapshotStore.Snapshot()` 每轮 `ToArray()` 复制 —— 见 `Net/Snapshots/README.md`「后续可能优化」。
5. 原版客户端**不支持预测协议**，延迟只能靠快照频率缓解（不影响防作弊，见 `architecture.md` 约束）。
6. **玩家受伤：接触 / 下落伤害已改为服务端判定**（`SimulateCombat` + `ApplyPlayerDamage`，含原版免伤帧 —— 接触 / 弹幕 / 下落同档 40、伤害被压到 1 取 20，
   并下发包 117 表现 + 包 16 权威血量）；敌对弹幕（Boss 弹幕）同样由服务端结算玩家伤害（`projectile_damage`，共用免伤帧）；
   遗留：**弹幕行为表只登记服务端会发射的 96 / 101 / 719**（按原版 `SetDefaults` 字段），
   其余类型（含客户端上报）仍为简化直线积分；粉尘 / 音效未做（纯客户端表现），
   且客户端上报的包 117 仍作为「额外伤害来源」被接受（可叠加，但无法凭空回血 / 抬高上限）。
7. **图格推送分两条路（已完成协议字段核对）**：**少量图格改动**（电路翻转执行器、液体混合等）走**包 20（TileSquare）**，
   按视口下发给附近玩家；**区块级地形**（离开出生点后的流送）走**包 10（TileSection）**。
   与原版一致：包 20 用于小矩形改动，包 10 用于区块下载。
8. **液体同步已按视口裁剪**，但仍为「每玩家全量过滤」（复杂度 O(玩家数 × 变更数)）；玩家数 / 变更数继续增大后需按区块分桶下发。
9. **Boss / 事件为简化模型**：眼魔（4）/ 魔焰眼（31）/ 蜂后（43）已按原版 `aiStyle` 移植运动与攻击节奏，
   其余 Boss 仍为直线追击、生命值为简化表；血月 / 日食为昼夜概率、入侵为配额刷怪，均非原版规则；
   掉落为**简化表**（仅收录眼魔 / 世界吞噬者 → 恶魔矿、史莱姆王 → 凝胶、蜂后 → 蜂蜡），未复刻原版掉落数据库。
10. **液体混合反应为简化模型**：已实现异种液体累计 ≥ 24 单位生成混合图格（水 + 岩浆 → 黑曜石 56、水 + 蜂蜜 → 蜂蜜块 229、
    岩浆 + 蜂蜜 → 松脆蜂蜜块 230、微光 + 任意 → 微光块 659，图格 ID 已完成协议行为验证）；遗留：仅在本格为空时生成
    （原版还允许覆盖可被黑曜石破坏的图格），且无液体压力模型。
11. **区块超帧上限已有兜底**：编码后超过 `UInt16`（65535）时按较长轴二分拆分再发（`NetworkHost.SendTileSectionAsync`），
    避免高熵区块直接抛异常中断登录；原版是同通道的降级压缩路径，此处用拆分替代。
12. **弹幕伤害为「上界校验」而非原版推导**：包 27 的伤害仍由客户端声明，服务端只保证不超过配置的单次伤害上限
    （与包 28 共用阈值）；原版伤害由武器 / 装备推导，此处未建模武器表。法力 / 增益已改为服务端跟踪与持有，
    但**增益的效果**仍由客户端计算（服务端只维护列表），属简化模型。
13. **实体位置语义与物理常数已按原版对齐**：`position` = **碰撞盒左上角**，脚底 = `position.Y + height`；
    玩家 20×42（原版 `Player.cs`），NPC 逐类型尺寸（见 `Simulation/NpcAI/NpcSizes.cs`，按原版 `NPC.SetDefaults` 核对：
    向导 18×40、蓝史莱姆 24×18、哥布林工兵 18×38、眼魔 / 魔焰眼 100×110、蜂后 66×66、黄蜂 12×12 / 8×8、
    史莱姆王 98×92、骷髅王 80×102）；NPC 物理常数取原版默认 **`gravity = 0.3` / `maxFallSpeed = 10`**
    （`NPC.UpdateNPC_UpdateGravity`）。**这些必须与客户端一致** —— 客户端收到包 23 后即按自己的尺寸 / 常数
    解释该坐标并自行推进 / 碰撞（`netOffset` 只用于渲染平滑），不一致会表现为「贴图陷地 / 悬空 + 持续抖动」
    以及「看着没碰到却扣血」。遗留：少数 NPC 类型在原版会覆盖 `gravity` / `maxFallSpeed`，当前统一用默认值；
    弹幕命中判定统一按 16×16 近似；NPC 水平方向未做完整 AABB 阻挡（靠同步收敛）。

---

## 四、协议字段核对原则

本矩阵中所有字段与类型**均通过协议行为逐字段核对**，非推测：

- **入站字段类型以协议字段定义为准** —— 某些字段可能省略显式类型，需结合线上长度与互操作性验证，避免量纲陷阱。
- **已踩过的量纲坑**：`NpcStrike.Damage` 线上为 **Int16**（±32767），与默认上限 30000 几乎贴边（见 §三 第 1 条）。
- **NetModule 帧**：`[UInt16 长度][Byte 82][UInt16 模块号][负载]`；模块号由注册顺序决定（聊天模块为 1）。
- **新增包的核对清单**：包号 → 字段顺序 → **字段类型** → 条件位与可选段。

> 本节采用协议行为比对与客户端互操作性验证，保留可复现的测试结论。

---

## 五、如何运行

```bash
# 仅原版功能端到端套件
dotnet test Tests/TerraAuth.Tests.csproj -c Release --filter "FullyQualifiedName~VanillaFeatureTests"
```
