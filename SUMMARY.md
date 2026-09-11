# TerraAuth 交付汇总

> **单一汇总文件**：本文把本轮全部交付事项（功能 / 修复 / 测试 / 文档）与完整变更文件清单合并为一处，
> 作为本次推送的对照说明。更细的模块状态见 [`terraauth/README.md`](terraauth/README.md)，
> 原版功能覆盖见 [`terraauth/VANILLA_COVERAGE.md`](terraauth/VANILLA_COVERAGE.md)，
> 逐轮回溯见 [`terraauth/OPTIMIZATION_BACKLOG.md`](terraauth/OPTIMIZATION_BACKLOG.md)。
>
> 生成日期：2026-09-11 ｜ 当前测试：**231 用例**（默认 SQLite 后端与 `-p:NoSqlite=true` 兜底后端均全绿）

---

## 一、项目定位

TerraAuth 是 **Terraria 协议（协议 326）的服务端权威代理 / 反作弊框架**。

原版 Terraria 为纯客户端权威架构——位置、血量、伤害全部由客户端上报，服务端基本只做转发，
内存修改工具可直接篡改坐标 / 血量 / 伤害且几乎无拦截。TerraAuth 的目标是在**协议层**把关键状态收回服务端：
**客户端只能声明意图，所有状态变更必须经过服务端权威校验后才生效**。

- 技术栈：C# / .NET 10（`net10.0`）
- 解决方案与源码：`terraauth/terraauth/`（`TerraAuth.sln` 与 `TerraAuth.csproj` 同目录）
- 已实测：原版 Terraria 客户端（协议 326）可完成 握手 → 进入世界 → 正常断开，双客户端互相可见

---

## 二、交付汇总（按主题）

### 1. 战斗 / 生存权威（服务端结算）

- **受伤 / 死亡 / 复活全链路收归服务端**：包 117 非负校验 → `DamagePlayerCommand` 结算服务端生命；
  归零置死亡态 → 世界同步补发包 118；Playing 阶段包 12 视为复活请求，**复活点由服务端固定为世界出生点**（忽略客户端坐标）。
  拆分 `Dead`（死亡态）与 `Active`（在线），修掉「下落伤害致死把玩家置为离线态导致永久冻结」的缺陷。
- **服务端伤害来源（接触）**：仿真新增敌怪 / Boss 接触伤害判定（32px + 60tick 免伤帧 + 简化伤害表），
  统一经同一入口结算；下发包 117（表现，广播）+ 包 16（单发权威血量）——此前下落伤害只改服务端血量、客户端毫不知情。
- **掉落物拾取（包 22）**：服务端做槽位对账（真实存活实体）+ 拾取半径校验 + 服务端背包入库（SSC），
  成功后移除世界实体并下发包 21（stack=0）；拒绝远程拾取 / 重复拾取。
- **弹幕命中判定（包 27）**：仿真按弹幕 / 敌怪距离判定命中并扣血，不再采信客户端声明。

### 2. 世界内容权威

- **箱子内容服务端持有（包 31 / 32 / 34）**：开箱校验（存在 / 距离）后由服务端逐槽下发权威内容；
  包 32 校验箱子 / 槽位 / 堆叠 / 物品 / 距离后落盘——服务端是唯一真相。
- **液体（包 82 模块 0，NetLiquid）**：客户端液体编辑经校验（越界 / 类型 / 距离 / 条目数）后由 `LiquidEditCommand` 落盘；
  仿真为脏格驱动的简化流动（下落优先、受阻后侧向均衡），下发按**视口裁剪**（每玩家只收到视野内的变更）。
- **液体混合反应**：异种液体相邻且累计 ≥ 24 单位时消耗异种液体并生成混合图格
  （水 + 岩浆 → 黑曜石、水 + 蜂蜜 → 蜂蜜块、岩浆 + 蜂蜜 → 松脆蜂蜜块、微光 + 任意 → 微光块），
  随后经图格 / 液体变更通道下发。
- **电路（包 17）**：修正 action 0..19 语义映射（方块 / 墙 / 4 色线网 / 执行器放置与拆除）；
  action 19 触发服务端沿电线受限 BFS（≤2000 格）翻转连通执行器；翻转结果按行合并为「宽 × 1」小矩形逐格下发视口内玩家。

### 3. Boss / 事件（简化模型 + 进度权威）

- **进度位按已核对 ID 映射**：击杀眼魔 / 世界吞噬者 / 骷髅王 / 史莱姆王 / 蜂后 / 石巨人 / 世纪之花 /
  猪龙鱼公爵 / 邪教徒 / 月亮领主 / 三机械 Boss / 小丑 → 置对应世界进度位，进度变化重新下发包 7。
- **Boss 掉落**：按已核对掉落表生成服务端掉落物（眼魔 / 世界吞噬者 → 恶魔矿、史莱姆王 → 凝胶、蜂后 → 蜂蜡），
  由世界同步按视口补发包 21。
- **事件状态机**：血月 / 日食按昼夜概率触发；入侵按配额刷怪、耗尽即结束；Boss 为简化直线追击 AI。

### 4. 健壮性兜底

- **区块超帧上限**：包 10 编码超过 `UInt16`（65535）时按较长轴二分拆分再发（递归），
  修掉高熵区块直接抛异常中断登录 / 出生点下载的隐患。
- **配置量纲校验**：`MaxSingleDamage > 32767`（Int16 线格式上限）启动即拒绝并提示，避免阈值静默失效。
- **权威层兜底**：`ConnectionStateStage` 对 `PlayerId <= 0` 的无身份上下文显式丢弃（网络层保证仅 Playing 入管线）。

### 5. 基础设施补全

- `IMetrics.SetGauge`（插件自定义仪表盘，含标签）；`/metrics` 导出同步支持。
- `IAuditRepository.QueryRecentAsync` 双后端实现；`CoreEventStore.QueryAsync` 接持久化审计查询（读审计可用）。
- `PlayerIdentity.ToPlayerId`（Guid → 连接槽位，与 `ToGuid` 互逆）。
- **命令子系统**：`Authority/CommandService.cs`（注册 / 解析 / 分发，命令名不区分大小写、处理器异常转失败结果），
  内置 `say` / `who` / `kick` / `help`；`IServerApi.ExecuteCommand` 由「仅落审计」改为真实分发。

### 6. Mod 兼容

- `ModPolicy` 从 `server.json` 读取（枚举以字符串读写）；`TModLoaderCompat` 装配：
  Mod 名称清单解析 + 自定义包 250-255 转发（绑定网络层单播）。

### 7. 测试与对抗自动化

- 套件 **231 用例**，默认后端与 `-p:NoSqlite=true` 兜底后端均全绿。
- `VanillaFeatureTests` 以**真实权威管线 + 真实 TCP** 逐项验证原版功能；
  `AntiCheat_*` 覆盖 Phase 7 可自动化部分：DPS 窗口、非法堆叠 / 箱内未知物品、无身份包丢弃、
  恶意包重放不推进权威、洪水限流 → 违规累计踢出、高熵区块拆分下的登录完整性。

### 8. 文档同步

根 `README.md`、`terraauth/README.md`、`PROJECT_STRUCTURE.md`、`VANILLA_COVERAGE.md`、
`OPTIMIZATION_BACKLOG.md`、`Phase6/Phase7` 与 `ModCompat` README 均已按上述实现同步更新。

---

## 三、本次推送变更文件清单

共 **41 个文件**（38 修改 + 3 新增），约 +4056 / −185 行。

### 新增

| 文件 | 说明 |
|---|---|
| `SUMMARY.md` | 本文（交付汇总，单一合并文件） |
| `terraauth/Authority/CommandService.cs` | 服务器命令子系统（注册 / 解析 / 分发） |
| `terraauth/Tests/WorldFileTests.cs` | `.wld` 解析测试（最小合法世界 + 版本 / 魔数 / footer 拒绝路径） |

### 源码（`terraauth/`）

| 文件 | 行数 | 说明 |
|---|---|---|
| `Authority/AuthoritySubsystems.cs` | +254 | 权威子系统补全（拾取 / 箱子 / 电路 / 液体校验等） |
| `Authority/IAuthorityLayer.cs` | +7 | 权威层接口扩展 |
| `Authority/InboundPipeline.cs` | +29 | 管线阶段（含 ConnectionStateStage 显式防御） |
| `Config/ConfigurationService.cs` | +18 | 配置量纲校验（Int16 上限） |
| `Config/ServerConfig.cs` | +10 | `ModPolicy` 配置节 |
| `CoreAdapter.cs` | +67 | `ExecuteCommand` 分发、`QueryAsync` 接审计查询 |
| `GameHost.cs` | +293 | 世界同步补发（图格 / 液体 / 受伤 / 新掉落物）、开箱内容下发 |
| `ModCompat/ModPolicy.cs` | +78 | Mod 策略配置化 |
| `Monitoring/IMetrics.cs` | +2 | `SetGauge` |
| `Monitoring/PrometheusMetrics.cs` | +13 | Gauge 导出 |
| `Net/Phase5/NetworkHost.cs` | +119 | 包 10 超帧二分拆分、按玩家定制广播、单播 / 原始包发送 |
| `Net/Phase5/PacketDecoder.cs` | +46 | 新增入站包解析（拾取 / 箱子 / 伤害 / 死亡 / 传送 / 包 82 模块） |
| `Net/Phase5/PacketEncoder.cs` | +26 | 新增出站包编码 |
| `Persistence/IPersistence.cs` | +2 | 审计近期查询接口 |
| `Persistence/SqlitePersistence.cs` | +37 | `QueryRecentAsync` 实现 |
| `Protocol/PacketId.cs` | +2 | 新增包常量 |
| `Protocol/Types.cs` | +38 | 新增包契约 / 领域类型 |
| `Security/PlayerIdentity.cs` | +8 | `ToPlayerId`（与 `ToGuid` 互逆） |
| `Simulation/CommandQueue.cs` | +312 | 拾取 / 箱子 / 液体 / 电路 / 伤害 / 复活等命令 + `Apply` 语义 |
| `Simulation/World/WorldFileReader.cs` | +2 | `.wld` 解析运算符优先级缺陷修复 |
| `Simulation/World/WorldState.cs` | +254 | 进度位 / 掉落表 / 图格与液体待推送集 / 箱子锁 |
| `Simulation/WorldEntities.cs` | +9 | 掉落物 `NewNotified`（区分客户端上报与服务端生成） |
| `Simulation/WorldSimulator.cs` | +534 | 液体流动与混合、电路 BFS、接触伤害、Boss / 事件、实体物理 |

### 测试（`terraauth/Tests/`）

| 文件 | 行数 | 说明 |
|---|---|---|
| `VanillaFeatureTests.cs` | +1029 | 原版功能端到端 + Phase 7 对抗自动化 |
| `PluginModTests.cs` | +182 | 插件 / Mod 兼容（含 TModLoader 转发通道） |
| `IntegrationTests.cs` | +71 | 集成与去重 / 校验 |
| `NetworkTests.cs` | +28 | 网络编解码回归 |
| `TerraAuth.Tests.csproj` | +1 | 新增测试文件纳入编译 |

### 文档 / 配置

| 文件 | 行数 | 说明 |
|---|---|---|
| `README.md`（仓库根） | +11 | 阶段状态表 + 已实测能力 |
| `terraauth/README.md` | +28 | 模块状态表 + 当前状态摘要 |
| `terraauth/PROJECT_STRUCTURE.md` | +32 | 结构 / 模块 / 测试说明 |
| `terraauth/VANILLA_COVERAGE.md` | +52 | 覆盖矩阵 + 已知隐患 |
| `terraauth/OPTIMIZATION_BACKLOG.md` | +99 | 第五~七轮回溯 + 阻塞项解除 |
| `terraauth/ModCompat/README.md` | +15 | Mod 兼容层说明 |
| `terraauth/Phase6-Infrastructure/README.md` | +2 | 基础设施说明同步 |
| `terraauth/Phase7-RedTeam/README.md` | +19 | 对抗清单自动化落地说明 |
| `terraauth/server.json` | +8 | `ModPolicy` 默认配置 |
| `.gitignore` | — | 移除本机绝对路径与来源描述，守卫规则保留 |

---

## 四、验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 默认后端 | `dotnet test TerraAuth.sln -c Release` | **231 / 231 通过** |
| 兜底后端 | `dotnet test TerraAuth.sln -c Release -p:NoSqlite=true` | **231 / 231 通过** |

> 说明：解决方案文件位于 `terraauth/terraauth/TerraAuth.sln`（与源码同目录），不在仓库根。

---

## 五、已知限制（简化模型，非原版全量）

- **Boss / 事件**：AI 仅直线追击、生命值为简化表；血月 / 日食为昼夜概率、入侵为配额刷怪；
  掉落为**简化表**（仅收录眼魔 / 世界吞噬者 / 史莱姆王 / 蜂后），未复刻原版掉落数据库。
- **液体**：混合反应仅在本格为空时生成；无液体压力模型。
- **电路**：无门电路 / 定时器 / 压力板（action 18 未建模）。
- **物理 / AI**：弹幕无图格碰撞与追踪 / 反弹行为；掉落物无拾取动画与合并。
- **图格推送**：服务端驱动的图格修改采用「小矩形包 10」而非原版包 20，若客户端行为有差异需按实测调整。
- **对端一致性**：原版客户端不支持预测协议，延迟只能靠快照频率缓解；Phase 7 的「内存修改类」条目仍需手工实验。
