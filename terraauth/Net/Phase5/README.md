# Phase 5 — 网络层（Network Layer）

> **依赖**：Phase 2（权威层）、Phase 3（仿真层）、Phase 4（快照广播）
> **目标**：实现 TCP 监听、连接生命周期、terraria-protocol 编解码、入站管线驱动、出站快照下发
> **对应架构文档**：§3.1 接入层、§3.2 协议层、§4.1 基础设施

---

## 1. 职责边界

网络层是**唯一**接触原始字节的地方。上行与下行两条链路：

```
上行（Inbound）：
  raw bytes → Framing → PacketDecoder → INetworkPacket
              → IInboundPipeline（Phase 2）→ CommandQueue（Phase 3）

下行（Outbound）：
  SnapshotBroadcaster（Phase 4）→ ISnapshotSender
              → PacketEncoder → Framing → raw bytes → client
```

**核心原则**：
- 网络层**不持有游戏状态**，只做「字节 ↔ 包对象」转换
- 所有业务逻辑（校验/仿真/广播）在 Phase 2/3/4 完成
- 网络层只负责：**收、解码、投递** 与 **收集、编码、发**

---

## 2. 文件清单（本 Phase 新增）

| 文件 | 职责 |
|---|---|
| `ITerrariaProtocol.cs` | 协议版本协商、包 ID 映射、能力声明 |
| `Framing.cs` | TCP 半包/粘包处理（长度前缀 + 包类型） |
| `PacketDecoder.cs` | 字节流 → `INetworkPacket`（terraria-protocol 包定义） |
| `PacketEncoder.cs` | `ISnapshotSender` 输出 → 字节流 |
| `Connection.cs` | 单连接状态机（Handshake / Playing / Disconnected） |
| `ConnectionManager.cs` | 连接池、超时、踢出、并发上限 |
| `NetworkHost.cs` ★ | TCP 监听 + Accept 循环 + Read/Write 调度 |
| `ISnapshotSender.cs` | 出站抽象（由 Phase 4 `SnapshotBroadcaster` 实现） |

---

## 3. 上行链路：`NetworkHost` → `IInboundPipeline`

### 3.1 读取循环（每个连接独立 Task）

```
NetworkHost.AcceptLoop
  └─ Connection.RunAsync
       └─ Framing.ReadFrameAsync  (解决半包/粘包)
            └─ PacketDecoder.Decode  (bytes → INetworkPacket)
                 └─ IInboundPipeline.ProcessAsync  (Phase 2)
                      └─ AuthorityDecision.Accept
                           └─ AuthorityToSimulation → CommandQueue  (Phase 3)
```

### 3.2 关键约束

- **单帧一个包**：每个 `INetworkPacket` 独立走管线，**不在网络层攒批**
- **异常隔离**：单个包处理异常 → 关闭该连接，**不影响其他玩家**
- **背压**：`CommandQueue` 满 → 丢弃最旧输入并告警（防恶意洪水）

---

## 4. 下行链路：`SnapshotBroadcaster` → `ISnapshotSender`

Phase 4 的 `SnapshotBroadcaster` 通过 `ISnapshotSender` 抽象下发快照。
本 Phase 提供 **TCP 实现**：

```
SnapshotBroadcaster.FlushAsync
  └─ ISnapshotSender.SendAsync(ConnectionId, SnapshotFrame)
       └─ PacketEncoder.Encode  (SnapshotFrame → bytes)
            └─ Framing.WriteFrameAsync  (长度前缀 + 包类型)
                 └─ NetworkStream.WriteAsync
```

**发送策略**：
- **按连接缓冲**：每个 `Connection` 独立 `Channel<byte[]>`（System.Threading.Channels）
- **单 writer**：每个连接只有一个发送 Task，避免 write 交错
- **限流**：缓冲区超过阈值 → 跳过非关键快照（降级为增量追赶）

**包 15（Snapshot）载荷布局**（小端；`Framing` 帧头 `[UInt16 len][Byte type]` 之后）：

| 字段 | 类型 | 说明 |
|---|---|---|
| `BaseTick` | `UInt32` | 增量基址；`0` = 全量，客户端应据此重建 |
| `Tick` | `UInt32` | 本帧 tick |
| `Checksum` | `UInt32` | xxHash32（覆盖实体 + 移除项的规范字节流） |
| `EntityCount` | `UInt16` | 实体数 |
| `Entities` | `21B × N` | `Id(Int32) + PosX/PosY/VelX/VelY(Float32) + State(Byte)` |
| `RemovedCount` | `UInt16` | 移除项数 |
| `Removed` | `5B × M` | `Id(Int32) + Reason(Byte)`；`Reason`：0=Despawn / 1=OutOfRange / 2=Destroyed |

> 实体 `Id < 0` 表示 NPC（`-1 - 索引`），正整数为玩家 Id（见 `SnapshotFrame.NpcEntityId`）。

编码侧：`PacketEncoder.EncodeSnapshot`；解码侧：`PacketDecoder.DecodeSnapshot(ReadOnlySpan<byte>)` → `SnapshotFrame`，
并经 `PacketDecoder.Decode` 主路径包装为 `SnapshotPacket : INetworkPacket`（包装置于网络层，因 Protocol 不可反向依赖 Simulation）。

---

## 5. 协议编解码（terraria-protocol 对接）

### 5.1 包 ID 映射

`ITerrariaProtocol` 定义 Terraria 协议版本与包 ID 对照表：

```
ProtocolVersion = 326  (对应 Terraria 1.4.5.8)
ConnectVersion  = "Terraria326"  (ConnectRequest 版本串)

PacketId（真实 Terraria 包号）:
  1   → ConnectionRequest   (ConnectRequest，版本串)
  2   → Disconnect          (Kick，NetworkText 原因)
  3   → ContinueConnecting  (分配 PlayerId: Byte)
  4   → PlayerInfo          (SyncPlayer)
  5   → InventorySlot       (PlayerInventorySlot)
  6   → RequestWorldInfo    (RequestWorldData)
  7   → WorldInfo           (WorldData)
  8   → TileGetSection      (RequestTileData)
  9   → StatusText
  10  → TileSendSection
  11  → TileFrameSection
  12  → PlayerSpawn
  13  → PlayerPosition      (PlayerControls)
  14  → PlayerActive
  15  → Snapshot            (TerraAuth 专用快照帧；原版 case 15 为 no-op)
  16  → PlayerHealth        (PlayerHp)
  17  → TileBreak           (Tile)
  21  → ItemDrop            (SyncItem)
  25  → ChatText
  27  → ProjectileNew      (SyncProjectile，抛射物生成 / 同步)
  28  → NpcStrike           (StrikeNPC)
  31  → Chest               (RequestChestOpen)
  32  → SyncChestItem       (箱子物品同步)
  35  → PlayerHeal          (治疗 / 回血)
  36  → SyncPlayerZone      (生物群系 / 城镇状态)
  49  → InitialSpawn
  50  → PlayerBuffs         (增益 / 减益列表)
  65  → TeleportEntity      (玩家 / NPC / 玩家间传送，含确认)
  73  → RequestTeleportationByServer (回城药水 / 海螺等由服务端发起的传送请求)
  79  → TilePlace           (PlaceObject)
  117 → PlayerHurtV2        (玩家受击，含死亡原因)
  118 → PlayerDeathV2       (玩家死亡，含死亡原因)
  129 → FinishedConnecting
```

> 完整映射参考 tModLoader `MessageID` / TShock `PacketTypes`。
> **权威白名单已全部可解码**：`ITerrariaProtocol.RequiresAuthority` 列出的包（5 / 13 / 17 / 21 / 27 / 28 / 31 / 65 / 73 / 79）均已有 Decoder / Encoder 实现。
> **其余包透明透传**：未结构化建模的包统一解码为 `UnknownPacket`（保留原始 PacketId + payload），编码侧原样写回——编解码层对本版本全部包「无缺口」，无需逐包建模；仅权威校验所需的包按需增量结构化。
> **每个新版本 Terraria 更新客户端，只需修改本文件 + Phase 2 对应包处理**——协议变更的影响被隔离在网络层。

### 5.2 编解码规则

`PacketDecoder` / `PacketEncoder` 遵循 terraria-protocol 的二进制布局：

- **小端字节序**（LittleEndian）
- **变长整数**：7-bit 变长编码（每字节最高位 `0x80` 表示续字节，最多 5 字节）
- **字符串**：7-bit 变长长度前缀（1-5 字节，UTF-8 字节数）+ UTF-8
- **特殊类型**：`Color`、`Vector2`、`Rectangle` 有固定布局

```
帧格式（与 Terraria 原版一致）：
  [UInt16 length]  — 整包字节数，含长度头自身与 type，即 length = 3 + payload.Length
  [Byte   type]    — PacketId
  [Byte[] payload] — 包体

Decode 伪代码：
  var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
  ushort length = reader.ReadUInt16();   // 整包长度（小端）
  byte packetType = reader.ReadByte();
  byte[] payload = reader.ReadBytes(length - 3);
  return PacketFactory.Create(packetType, payload);
```

---

## 6. 连接生命周期

```
Client                    NetworkHost              Connection
  │                            │                        │
  │── TCP Connect ──────────▶ │                        │
  │                            │── Accept ───────────▶ │
  │                            │                        │
  │── ConnectionRequest(1) ──▶ │  (ReadFrameAsync)     │
  │   版本串 "Terraria326"      │  → PacketDecoder      │
  │                            │  → 版本串比对         │
  │                            │                        │
  │ ◀─ ContinueConnecting(3) ─│  (Byte PlayerId + Bool)│
  │                            │                        │
  │── PlayerInfo(4) ─────────▶ │  → [白名单/Steam 待接入]│
  │── RequestWorldInfo(6) ───▶ │                        │
  │                            │                        │
  │ ◀── WorldInfo(7) ──────── │  (Encode + Flush)     │
  │── TileGetSection(8) ─────▶ │                        │
  │ ◀── StatusText(9) ─────── │  (进度文本)           │
  │ ◀── InitialSpawn(49) ──── │                        │
  │── PlayerSpawn(12) ───────▶ │  → State = Playing    │
  │ ◀── FinishedConnecting(129)│                       │
  │                            │                        │
  │ ◀── SnapshotFrames ───────│  (SnapshotBroadcaster)│
  │                            │                        │
  │── Disconnect(2) ─────────▶ │                        │
  │                            │── Cleanup ───────────▶ │
```

> 版本不匹配时服务端直接回 `Disconnect(2)`（NetworkText 原因）并关闭连接。

**状态机**（对应原版 State 0→10）：
- `Handshaking`（原版 0）：等待 `ConnectionRequest(1)` + 版本串校验（`"Terraria326"`）
- `Authenticating`（原版 1→3）：已回 `ContinueConnecting(3)`，依次等待 `PlayerInfo(4)` / `RequestWorldInfo(6)` / `TileGetSection(8)` / `PlayerSpawn(12)`
- `Playing`（原版 10）：已回 `FinishedConnecting(129)`，入站走管线，出站走快照
- `Disconnected`：资源回收，CommandQueue 清理该玩家命令

> 包 8 之后按原版 `MessageBuffer` case 8 的矩形规则逐块下发 `10 TileSection`（世界出生点 5×3 半开矩形 + 请求出生点 6×4 闭矩形，去重后逐块发送），随后发 `49 InitialSpawn`。
> 包 10 编码（Deflate + 位标志 + RLE + 尾部宝箱/牌子列表）与解码回归测试已实现。

---

## 7. 关键设计决策

### 7.1 为什么用原始 TCP 而非 WebSocket / Kestrel

- Terraria 原版协议就是 **Plain Old TCP**，无 HTTP 升级 
- 自定义二进制协议，无需 Kestrel 的 HTTP 管线
- **直接 `TcpListener` + `NetworkStream`** 即可，零依赖

### 7.2 为什么不用 TShock 的监听

TShock 挂在原版 `TerrariaServer.exe` 之上，监听由游戏内置实现 。
我们重写服务端，**必须自己实现监听 + 编解码**——这正是本 Phase 的价值。

### 7.3 背压与限流

| 场景 | 策略 |
|---|---|
| 入站洪水 | `CommandQueue` 容量上限，超限丢弃最旧 + 告警 |
| 出站堆积 | 每连接 `Channel` 满 → 跳过增量快照，等待追赶 |
| 恶意连接 | 握手超时（默认 10s）自动断开 |

---

## 8. 验收清单（架构 §8）

| # | 测试项 | 方法 | 通过标准 |
|---|---|---|---|
| 5.1 | TCP 监听 | 启动 Host，原版客户端 / telnet 连接 | 成功 Accept（原版客户端已实测 ✅） |
| 5.2 | 编解码正确 | 构造已知包 → Encode → Decode（含包 15 Snapshot） | 字节完全一致 |
| 5.3 | 半包/粘包 | 故意分片发送 | 正确重组 |
| 5.4 | 管线贯通 | 发 PlayerPosition 包 | Phase 2 收到并校验；双客户端包 13 转发（A → B 可见） |
| 5.5 | 快照下发 | 仿真推进 → FlushAsync | 客户端收到 SnapshotFrame |
| 5.6 | 连接隔离 | 恶意包炸一个连接 | 其他玩家不受影响 |
| 5.7 | 背压 | 洪水包压测 | CommandQueue 不爆，CPU 稳定 |
| 5.8 | 版本校验 | 发错误协议版本 | 握手拒绝 |

---

## 9. 测试（`Tests/NetworkTests.cs`）

```csharp
[Fact] public async Task Encode_Decode_RoundTrip_PreservesData();
[Fact] public async Task Framing_HandlesPartialFrames();
[Fact] public async Task Pipeline_ProcessesDecodedPacket();
[Fact] public async Task SnapshotBroadcaster_SendsEncodedFrames();
[Fact] public void EncodeSnapshot_Includes_BaseTick_And_Removed();
[Fact] public void EncodeSnapshot_DecodeSnapshot_RoundTrip_PreservesFrame();
[Fact] public async Task MalformedPacket_ClosesConnection();
[Fact] public async Task FloodPackets_ApplyBackpressure();
```

---

## 10. 下一步

**P0（先打通端到端）**：
1. ✅ **跑通一条完整链路**：原版 Terraria 客户端已实测「连接 → 协议协商 → 进入世界」（2026-09-09）；位置包 13 → Phase 2 校验 → Phase 3 仿真 → Phase 4 快照 → 下发的真实 TCP 往返已由 `IntegrationTests.TcpRoundTrip_Packet13_Reaches_SnapshotOverWire` 覆盖（`NetworkHost` + `TcpClient` 走完整握手链至 Playing 后发包 13，断言收到包 15 且位置匹配）
2. ✅ `NetworkHost` + `Connection` 的最小 Accept/Read/Write 循环
3. ✅ `Framing` 长度前缀编解码
4. ✅ 握手包编解码：ConnectionRequest(1) / ContinueConnecting(3) / PlayerInfo(4) / RequestWorldInfo(6) / WorldInfo(7) / TileGetSection(8) / StatusText(9) / PlayerSpawn(12) / InitialSpawn(49) / FinishedConnecting(129) / Disconnect(2)
5. ✅ `ISnapshotSender` 的 TCP 实现（对接 Phase 4）

**P1（完善）**：
- ✅ 包编解码：权威白名单 10 包已结构化（含传送反作弊 `TeleportEntity(65)` / `RequestTeleportationByServer(73)`）；其余包统一 `UnknownPacket` 透明透传（编解码层无缺口）；⚠️ 按需增量结构化
- ✅ 变长整数（`Read/Write7BitEncodedInt`）；⚠️ 特殊类型：`Vector2` / 图格 `Color` 已覆盖，独立 `Rectangle` 读写器待补
- ✅ 连接认证白名单（`NetworkHost` 按 `PlayerWhitelist` 踢出）；⏳ SteamTicket 未实现
- ⏳ 性能基准（单服 100 玩家，带宽/CPU 预算）

---

## 11. 已知风险与边界

> ⚠️ **协议版本跟进**：每次 Terraria 更新客户端，本文件的包 ID 映射 + Phase 2 对应包处理都要更新。这是架构文档反复强调的持续成本。

> ✅ **原版客户端兼容性（已实测）**：2026-09-09 原版 Terraria 客户端（协议 326）连接 `127.0.0.1:7777`，握手成功 → 解析玩家名 → 进入世界（出生点 2100,352）→ 正常断开，全程无异常/畸形包。编解码严格遵循 terraria-protocol。
> ✅ **移动链路已闭环**：位置包 13 → Phase 2 校验 → Phase 3 仿真 → Phase 4 快照 → TCP 下发的往返已由 `IntegrationTests.TcpRoundTrip_Packet13_Reaches_SnapshotOverWire` 覆盖。
> ✅ **移动权威限距（防作弊优先）**：Δt 上限 1.0s → 10.0s，覆盖客户端失焦/卡顿导致的 4~7s 稀疏发包；但**不做静默超时无条件放行**——超过 10s 的间隔一律按 10s 计，单包允许位移上限 `8.0 × 60 × 10 + 4 = 4804px`，长时静默后的大位移仍会被拒，防止其成为穿墙/瞬移缺口。
> ⏳ **后续事项：失焦误杀**：客户端失焦 >30s 且激活后位置距基准 >4804px 时会被判 `speed_exceeded`，且拒绝不更新基准会导致后续包连续被拒（原「基准冻结」现象）。彻底解决需引入服务端权威移动/碰撞校验（见 `architecture.md` §9），当前按防作弊优先取舍，暂不实施。
> ⚠️ **玩家间可见性**：原版客户端仅当 `Main.player[i].active == true` 时才绘制该玩家，故进服/断线必须下发包 14（`PlayerActive`，含 `Active=false` 反激活防幽灵）。仅发包 4（外观）不足以互相看见——这是「相互看不到」的根因。
> ⚠️ 挖掘 / 战斗 → 权威校验 → 仿真 → 快照回传仍待验证，接入真实客户端前建议先用自定义测试客户端覆盖。

> ⚠️ **不做加密 / 防逆向**：原始 TCP 无加密是 Terraria 协议现状。如需防包伪造，可在 Framing 层加 HMAC（但增加延迟）。

---

**至此，Phase 2/3/4/5 形成完整闭环**：

```
TCP bytes → Framing → Decoder → Pipeline(Phase 2) → Command(Phase 3)
                                                              ↓
Snapshot(Phase 4) → Sender → Encoder → Framing → TCP bytes
```

剩下 **Phase 7（对抗测试）** 和 **Phase 8（运营监控）**。
