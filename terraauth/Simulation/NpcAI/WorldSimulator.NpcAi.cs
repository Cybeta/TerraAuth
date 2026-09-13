// TerraAuth — NPC AI（按原版 aiStyle 移植）
//
// 结构对齐原版：`NPC.aiStyle` 决定走哪套 AI；每套 AI 只读写 `NPC.ai[0..3]` 与 `velocity/direction`，
// 重力与图格碰撞由统一的物理步（StepNpcPhysics）负责 —— 与「AI 设速度、Update 走物理」的原版顺序一致。
//
// 移植范围（逐步补齐，未移植的走 <see cref="AiFallback"/> 简化追击）：
//   aiStyle 1  Slimes        —— AI_001_Slimes（type 1 蓝史莱姆路径）
//   aiStyle 3  Fighters      —— 待移植（哥布林工兵 26）
//   aiStyle 2  FloatingEye   —— 待移植（眼魔 4 等 Boss 分支）
//   其余（Boss 专属 aiStyle）—— 待移植
//
// 说明：原版 AI 中的粉尘 / 音效 / 光照 / 原版弹幕行为属客户端表现与未补齐的框架，
// 此处按「服务端权威运动 + 状态」口径移植：位置、速度、ai[]、direction、跳跃/攻击节奏。

using System;
using TerraAuth.Protocol;   // Vector2

namespace TerraAuth.Simulation;

public partial class WorldSimulator
{
    /// <summary>type → 原版 aiStyle。未列入者返回 0（走简化兜底），移植一个新 aiStyle 时在此登记。</summary>
    private static int NpcAiStyleOf(int npcType) => npcType switch
    {
        1 or 16 or 59 or 71 or 81 or 138 or 147 or 183 or 184 or 204 or 302 or 304 or 334
            or 377 or 446 or 537 or 658 or 659 or 667 or 685 => 1,   // Slimes（原版 AI_001_Slimes 覆盖的 slime 家族）
        21 or 26 or 31 or 287 or 294 or 295 or 296 or 338 or 339 or 340 => 3,  // Fighters（含哥布林工兵 26）
        4 => 4,                                                  // Eye of Cthulhu（眼魔）
        126 => 31,                                               // Spazmatism（魔焰眼）
        222 => 43,                                               // Queen Bee（蜂后）
        22 or 54 or 107 or 108 or 124 or 142 or 160 or 178 or 207 or 208 or 209 or 227 or 228
            or 229 or 368 or 441 or 550 or 588 or 633 => 7,          // TownEntities（含向导 22）
        _ => 0,
    };

    /// <summary>
    /// 单只 NPC 的 AI 分派（在物理步之前调用；与「原版先 AI 设速度、再 Update 走物理」同序）。
    /// </summary>
    private void RunNpcAi(WorldNpc npc)
    {
        var style = npc.AiStyle != 0 ? npc.AiStyle : NpcAiStyleOf(npc.Type);
        switch (style)
        {
            case 1:
                Ai001Slimes(npc);
                break;
            case 3:
                Ai003Fighters(npc);
                break;
            case 4:
                Ai004EyeOfCthulhu(npc);
                break;
            case 7:
                Ai007TownEntities(npc);
                break;
            case 31:
                Ai031Spazmatism(npc);
                break;
            case 43:
                Ai043QueenBee(npc);
                break;
            default:
                if (npc.IsBoss)
                {
                    // 未移植 aiStyle 的 Boss：简化直线追击（自移动，不走通用物理）
                    SimulateBossStep(npc);
                    return;
                }
                AiFallback(npc);
                break;
        }

        StepNpcPhysics(npc);
    }

    /// <summary>
    /// 未移植 aiStyle 的简化兜底：朝最近玩家水平移动（+ 统一物理的重力/落地）。
    /// </summary>
    private void AiFallback(WorldNpc npc)
    {
        var target = NearestPlayer(npc.X, npc.Y);
        if (target is not null)
        {
            float dx = target.AimPosition.X - npc.X;
            if (dx != 0f) npc.Direction = dx > 0f ? 1 : -1;
        }

        npc.VelocityX = target is null ? 0f : npc.Direction;   // 1 px/tick
    }

    /// <summary>重力 + 位移 + 图格落地（所有走通用物理的 NPC 共用）；飞行体（<see cref="WorldNpc.NoGravity"/>）只积分位移。</summary>
    private void StepNpcPhysics(WorldNpc npc)
    {
        if (npc.NoGravity)
        {
            // 飞行体：无重力、无图格碰撞，只做世界边界钳制
            npc.Grounded = false;
            npc.X = Math.Clamp(npc.X + npc.VelocityX, TileSize, (_world.MaxTilesX - 1) * TileSize);
            npc.Y = Math.Clamp(npc.Y + npc.VelocityY, TileSize, (_world.MaxTilesY - 1) * TileSize);
            return;
        }

        // 原版约定：X/Y = 碰撞盒左上角，脚底 = Y + height（逐类型尺寸，见 NpcSizes）
        var (width, height) = NpcSizes.Of(npc.Type);

        npc.VelocityY = Math.Min(npc.VelocityY + NpcGravity, NpcMaxFallSpeed);

        var nextX = npc.X + npc.VelocityX;
        var nextY = npc.Y + npc.VelocityY;

        int leftX = (int)(nextX / TileSize);
        int rightX = (int)((nextX + width - 1) / TileSize);
        int feetY = (int)((nextY + height) / TileSize);

        // 左右任一侧脚底有实心格即落到该格上沿（原版 NPC 站住只需部分踩实）
        if (NpcTileSolid(leftX, feetY) || NpcTileSolid(rightX, feetY))
        {
            nextY = feetY * TileSize - height;
            npc.VelocityY = 0f;
            npc.Grounded = true;
        }
        else
        {
            npc.Grounded = false;
        }

        // 水平阻挡：前方边缘是实心格则停在原地（否则 NPC 会穿过地形直线前进，与客户端发散）
        if (NpcBlockedHorizontally(npc, nextX, nextY, width, height))
        {
            nextX = npc.X;
            npc.VelocityX = 0f;
        }

        // 注意：落地**不清零水平速度** —— 原版地面摩擦由各 aiStyle 自己处理（史莱姆 velocity.X *= 0.8、
        // 城镇 NPC 向 0/±上限 收敛）。早期实现每 tick 清零，导致贴地 NPC 永远加不起速度，
        // 与服务端下发的 velocity 一起让客户端表现成「抖动 / 忽快忽慢」。
        npc.X = nextX;
        npc.Y = nextY;
    }

    /// <summary>
    /// NPC 水平阻挡：前进方向的边缘格是实心 → 停住（不清零速度以外的状态，交回 aiStyle 处理转向）。
    /// 早期只做垂直落地、**水平完全不阻挡**，NPC 会"穿墙"直线前进，而客户端有完整碰撞 →
    /// 两边位置持续发散（表现为「史莱姆一直朝某个方向走」「看着离得很远却在掉血」）。
    /// 顺带让原版的「卡住翻向」判定（<c>ai[3] == position.X</c>）能真正生效。
    /// </summary>
    private bool NpcBlockedHorizontally(WorldNpc npc, float nextX, float nextY, int width, int height)
    {
        if (npc.VelocityX == 0f) return false;

        // 前进方向的边缘：向右取右边界、向左取左边界
        float edge = npc.VelocityX > 0 ? nextX + width : nextX;
        int col = (int)(edge / TileSize);
        int midRow = (int)((nextY + height * 0.5f) / TileSize);
        int lowRow = (int)((nextY + height - 1f) / TileSize);

        return NpcTileSolid(col, midRow) || NpcTileSolid(col, lowRow);
    }

    // ========================================================================
    // aiStyle 1：Slimes（原版 AI_001_Slimes，type 1 蓝史莱姆路径）
    // ========================================================================

    /// <summary>原版 <c>num54</c>：跳跃计时门限（type 1 取 -1000；部分变体取 -500 / -400）。</summary>
    private const float SlimeJumpThreshold = -1000f;

    /// <summary>
    /// 原版 AI_001_Slimes 的移动核心（逐条对照原版源码，去掉了仅客户端表现与特殊变体分支）：
    /// <list type="bullet">
    ///   <item>`ai[2] == 0` 时一次性初始化：`ai[0] = -100`、`ai[2] = 1`、选定目标方向。</item>
    ///   <item>贴地：`ai[2]` 递减；`ai[3] == position.X` → 判定卡住 → 反向；地面摩擦 `velocity.X *= 0.8`；
    ///         `ai[0]++`（有目标再 +1）；按 `ai[0]` 与门限的关系分 1/2/3 三档起跳。</item>
    ///   <item>起跳：1/2 档 `velocity.Y = -6`、`velocity.X += 2 * direction`；3 档 `velocity.Y = -8`、
    ///         `velocity.X += 3 * direction` 并记录 `ai[3] = position.X`（供落地后的卡住判定）。</item>
    ///   <item>空中：朝 `direction` 水平加速 `0.2`，上限 ±3（原版 `0.93` 阻尼 + `0.2` 加速）。</item>
    /// </list>
    /// </summary>
    private void Ai001Slimes(WorldNpc npc)
    {
        var ai = npc.Ai;
        var target = NearestPlayer(npc.X, npc.Y);

        // 原版：if (ai[2] == 0f) { ai[0] = -100f; ai[2] = 1f; TargetClosest(); }
        if (ai[2] == 0f)
        {
            ai[0] = -100f;
            ai[2] = 1f;
            if (target is not null)
                npc.Direction = target.AimPosition.X > npc.X ? 1 : -1;
        }

        // 原版：if (ai[2] > 1f) ai[2]--;
        if (ai[2] > 1f) ai[2] -= 1f;

        if (npc.Grounded)
        {
            // 原版：if (ai[3] == position.X) { direction *= -1; ai[2] = 200f; } ai[3] = 0f;
            if (ai[3] != 0f && ai[3] == npc.X)
            {
                npc.Direction *= -1;
                ai[2] = 200f;
            }

            ai[3] = 0f;

            // 原版地面摩擦（type 1 走 else 分支）：velocity.X *= 0.8f，趋零
            npc.VelocityX *= 0.8f;
            if (npc.VelocityX is > -0.1f and < 0.1f)
                npc.VelocityX = 0f;

            // 原版：if (flag3) ai[0]++;  ai[0]++;   （flag3 = 有有效目标）
            ai[0] += 1f;
            if (target is not null)
                ai[0] += 1f;

            // 原版三档门限（num55）
            int phase = 0;
            if (ai[0] >= 0f) phase = 1;
            if (ai[0] >= SlimeJumpThreshold && ai[0] <= SlimeJumpThreshold * 0.5f) phase = 2;
            if (ai[0] >= SlimeJumpThreshold * 2f && ai[0] <= SlimeJumpThreshold * 1.5f) phase = 3;

            if (phase > 0)
            {
                // 原版：if (flag3 && ai[2] == 1f) TargetClosest();
                if (target is not null && ai[2] == 1f)
                    npc.Direction = target.AimPosition.X > npc.X ? 1 : -1;

                if (phase == 3)
                {
                    // 原版强跳：velocity.Y = -8f; velocity.X += 3 * direction; ai[0] = -200f; ai[3] = position.X;
                    npc.VelocityY = -8f;
                    npc.VelocityX += 3f * npc.Direction;
                    ai[0] = -200f;
                    ai[3] = npc.X;
                }
                else
                {
                    // 原版普通跳：velocity.Y = -6f; velocity.X += 2 * direction;
                    //             ai[0] = -120f;（1 档 += num54，2 档 += num54 * 2）
                    npc.VelocityY = -6f;
                    npc.VelocityX += 2f * npc.Direction;
                    ai[0] = -120f + (phase == 1 ? SlimeJumpThreshold : SlimeJumpThreshold * 2f);
                }

                npc.Grounded = false;
            }

            return;
        }

        // 原版空中分支：朝 direction 水平加速（目标存在时才加速），上限 ±3
        if (target is null) return;

        bool towardRight = npc.Direction == 1;
        if ((towardRight && npc.VelocityX < 3f) || (!towardRight && npc.VelocityX > -3f))
        {
            if ((!towardRight && npc.VelocityX < 0.01f) || (towardRight && npc.VelocityX > -0.01f))
                npc.VelocityX += 0.2f * npc.Direction;
            else
                npc.VelocityX *= 0.93f;
        }
    }

    // ========================================================================
    // aiStyle 3：Fighters（原版 AI_003_Fighters，type 26 哥布林工兵路径）
    // ========================================================================

    /// <summary>地面行走加速度（原版 type 26：0.07f）。</summary>
    private const float FighterWalkAccel = 0.07f;

    /// <summary>最大水平速度（原版 type 26：num84 = 1.5f）。</summary>
    private const float FighterWalkMax = 1.5f;

    /// <summary>贴地且水平速度为 0 时，累计到该值即掉头（原版 <c>ai[0] &gt;= 2f</c>）。</summary>
    private const float FighterStuckTurnTicks = 2f;

    /// <summary>
    /// 原版 AI_003_Fighters（type 26）的移动核心：贴地加速走（0.07 / 上限 1.5）、撞墙掉头（ai[0] 累计 2）、
    /// 遇障碍起跳（-8 / -6 / -5）。type 26 无弹幕攻击（攻击 = 接触伤害）；破门分支（ai[1]/ai[2]）未移植。
    /// </summary>
    private void Ai003Fighters(WorldNpc npc)
    {
        var ai = npc.Ai;
        var target = NearestPlayer(npc.X, npc.Y);

        // 原版：目标有效时 direction 指向玩家
        if (target is not null)
        {
            float dx = target.AimPosition.X - npc.X;
            if (MathF.Abs(dx) > 1f) npc.Direction = dx > 0f ? 1 : -1;
        }

        if (!npc.Grounded) return;

        // 原版 68784-68799：贴地且水平速度为 0 → 累计 ai[0]，到 2 掉头
        if (npc.VelocityX == 0f)
        {
            ai[0] += 1f;
            if (ai[0] >= FighterStuckTurnTicks)
            {
                npc.Direction *= -1;
                ai[0] = 0f;
            }
        }
        else
        {
            ai[0] = 0f;
        }

        // 原版 69101-69139：加速 0.07、上限 1.5；超速时贴地衰减
        if (npc.VelocityX < -FighterWalkMax || npc.VelocityX > FighterWalkMax)
        {
            npc.VelocityX *= 0.8f;
        }
        else if (npc.VelocityX < FighterWalkMax && npc.Direction == 1)
        {
            npc.VelocityX = Math.Min(FighterWalkMax, npc.VelocityX + FighterWalkAccel);
        }
        else if (npc.VelocityX > -FighterWalkMax && npc.Direction == -1)
        {
            npc.VelocityX = Math.Max(-FighterWalkMax, npc.VelocityX - FighterWalkAccel);
        }

        TryFighterJump(npc);
    }

    /// <summary>
    /// 原版 71518-71575 的障碍起跳（type 26 走 height &gt;= 32 分支）：
    /// 前方 2~3 格实心 → -8；1 格实心 → -6；仅台阶高度差 → -5。
    /// </summary>
    private void TryFighterJump(WorldNpc npc)
    {
        // 支撑行 = 脚下那一行；身体在支撑行上方（详见 Ai007TownEntities 的同名说明）
        var (width, height) = NpcSizes.Of(npc.Type);
        int supportRow = (int)((npc.Y + height + 1f) / TileSize);
        int bodyRow = supportRow - 1;
        int frontCol = (int)((npc.Direction > 0 ? npc.X + width : npc.X - 1f) / TileSize);

        if (NpcTileSolid(frontCol, bodyRow) && NpcTileSolid(frontCol, bodyRow - 1))
            npc.VelocityY = -8f;
        else if (NpcTileSolid(frontCol, bodyRow))
            npc.VelocityY = -6f;
        else
            return;

        npc.Grounded = false;
    }

    // ========================================================================
    // aiStyle 7：TownEntities（原版 AI_007_TownEntities，type 22 向导路径）
    // ========================================================================

    /// <summary>城镇 NPC 走速上限 / 加速度（原版 type 22：num22 = 1f、num23 = 0.07f）。</summary>
    private const float TownWalkMax = 1f;
    private const float TownWalkAccel = 0.07f;

    /// <summary>距家超过该格数即朝家走（原版 64092：±25 格）。</summary>
    private const float TownHomeLeashTiles = 25f;

    /// <summary>
    /// 原版 AI_007_TownEntities 的**地面运动核心**：家附近游走（每 tick 1/80 概率掉头）、离家 &gt;25 格朝家走、
    /// 前方障碍起跳（-6 / -5 / -4.4）。
    /// 重力在原版属引擎侧（gravity = 0.3、maxFall = 10），此处交给共用物理步。
    /// 未移植：住房判定 / 坐下（ai[0]=5）/ 传送回家 / 远程攻击状态 / 微光状态机（见 backlog 剩余清单）。
    /// </summary>
    private void Ai007TownEntities(WorldNpc npc)
    {
        float homeX = (npc.HomeTileX + 0.5f) * TileSize;
        float dx = npc.X - homeX;

        if (MathF.Abs(dx) > TownHomeLeashTiles * TileSize)
        {
            npc.Direction = dx > 0f ? -1 : 1;   // 离家太远 → 朝家走
        }
        else if (_rng.NextUInt32() % 80 == 0)
        {
            npc.Direction *= -1;                // 家附近随机掉头（原版 Main.rand.Next(80) == 0）
        }

        if (!npc.Grounded) return;

        // 原版 64256-64278：加速 0.07、上限 1
        if (npc.VelocityX < -TownWalkMax || npc.VelocityX > TownWalkMax)
        {
            npc.VelocityX *= 0.8f;
        }
        else if (npc.VelocityX < TownWalkMax && npc.Direction == 1)
        {
            npc.VelocityX = Math.Min(TownWalkMax, npc.VelocityX + TownWalkAccel);
        }
        else if (npc.VelocityX > -TownWalkMax && npc.Direction == -1)
        {
            npc.VelocityX = Math.Max(-TownWalkMax, npc.VelocityX - TownWalkAccel);
        }

        // 原版 64416-64463：前方障碍起跳。
        // 注意「支撑行」= 脚下那一行（脚底贴它的上沿），身体在支撑行**上方**。
        // 早期实现直接探支撑行 → 平地前方本来就是地面 → 每 tick 都判成障碍 → 向导一直跳。
        var (width, height) = NpcSizes.Of(npc.Type);
        int supportRow = (int)((npc.Y + height + 1f) / TileSize);
        int bodyRow = supportRow - 1;
        int frontCol = (int)((npc.Direction > 0 ? npc.X + width : npc.X - 1f) / TileSize);

        if (NpcTileSolid(frontCol, bodyRow) && NpcTileSolid(frontCol, bodyRow - 1))
            npc.VelocityY = -6f;
        else if (NpcTileSolid(frontCol, bodyRow))
            npc.VelocityY = -5f;
        else
            return;

        npc.Grounded = false;
    }

    /// <summary>图格是否实心（越界视为不实心）。</summary>
    private bool NpcTileSolid(int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0 || tileX >= _world.Tiles.Width || tileY >= _world.Tiles.Height)
            return false;

        ref var tile = ref _world.Tiles[tileX, tileY];
        return tile.Active && TileIdSets.IsTileSolid(tile.Type);
    }

    // ========================================================================
    // aiStyle 4：Eye of Cthulhu（原版 aiStyle == 4 分支，24826-25706）
    // ========================================================================

    /// <summary>一阶段「追玩家上方 200px」的加速度 / 限速（原版 num13 = 0.04f、num12 = 5f）。</summary>
    private const float EyeChaseAccel = 0.04f;
    private const float EyeChaseMaxSpeed = 5f;

    /// <summary>冲刺初速（原版 num50 = 6.8f）。</summary>
    private const float EyeChargeSpeed = 6.8f;

    /// <summary>
    /// 原版眼魔（type 4 / aiStyle 4）的运动与攻击节奏（逐条对照原版行号）：
    /// <list type="bullet">
    ///   <item>阶段：`life &lt; lifeMax * 0.5` → `ai[0] = 1`（原版 25151-25162）。</item>
    ///   <item>`ai[1] == 0` 追击：朝「玩家中心上方 200px」加速 0.04、限速 5（原版 24948-24967）；
    ///         600 帧后转冲刺准备（原版 num18 = 600）；玩家在下方且距离 &lt; 500 时每 110 帧生成 1 只仆从 type 5（原版 25019-25057）。</item>
    ///   <item>`ai[1] == 1` 蓄力：朝玩家方向初速 6.8 → `ai[1] = 2`（原版 25398-25423）。</item>
    ///   <item>`ai[1] == 2` 冲刺：40 帧后 `velocity *= 0.97`；130 帧计一次冲刺，连冲 3 次回到 `ai[1] = 0`（原版 25426-25480）。</item>
    /// </list>
    /// 未移植：二阶段自旋（rotation，纯客户端表现）、专家模式预判冲刺（ai[1] = 3/4/5）、白天脱战上浮（简化为无目标时上浮）。
    /// </summary>
    private void Ai004EyeOfCthulhu(WorldNpc npc)
    {
        var ai = npc.Ai;
        npc.NoGravity = true;   // 原版：noGravity + noTileCollide

        var target = NearestPlayer(npc.X, npc.Y);
        if (target is null)
        {
            npc.VelocityY -= 0.04f;   // 无目标 → 缓慢上浮（原版 24942 的脱战上浮）
            return;
        }

        if (ai[0] == 0f && npc.Life < npc.LifeMax * 0.5f)
        {
            ai[0] = 1f;   // 进入二阶段（原版 25151-25162）
            ai[1] = 0f;
            ai[2] = 0f;
            ai[3] = 0f;
        }

        float px = target.AimPosition.X;
        float py = target.AimPosition.Y;

        if (ai[1] == 0f)
        {
            float targetY = ai[0] == 0f ? py + 21f - 200f : py + 21f;
            float dx = px - npc.X;
            float dy = targetY - npc.Y;
            float accel = ai[0] == 0f ? EyeChaseAccel : EyeChaseAccel * 2f;

            if (MathF.Abs(dx) > 8f) npc.VelocityX += MathF.Sign(dx) * accel;
            if (MathF.Abs(dy) > 8f) npc.VelocityY += MathF.Sign(dy) * accel;

            float max = ai[0] == 0f ? EyeChaseMaxSpeed : EyeChaseMaxSpeed * 1.2f;
            npc.VelocityX = Math.Clamp(npc.VelocityX, -max, max);
            npc.VelocityY = Math.Clamp(npc.VelocityY, -max, max);

            if (ai[0] == 0f)
            {
                ai[2] += 1f;
                if (ai[2] >= 600f)
                {
                    ai[1] = 1f;
                    ai[2] = 0f;
                    ai[3] = 0f;
                    return;
                }

                // 玩家在下方且距离 < 500 → 每 110 帧生成 1 只仆从（type 5，初速 5）
                float dist = MathF.Sqrt(dx * dx + (py - npc.Y) * (py - npc.Y));
                if (npc.Y + 55f < py && dist < 500f)
                {
                    ai[3] += 1f;
                    if (ai[3] >= 110f)
                    {
                        ai[3] = 0f;
                        SpawnNpcNear(5, target, 5f);
                    }
                }
            }

            return;
        }

        if (ai[1] == 1f)
        {
            float dx = px + 21f - (npc.X + 50f);
            float dy = py + 21f - (npc.Y + 55f);
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.001f)
            {
                npc.VelocityX = dx / len * EyeChargeSpeed;
                npc.VelocityY = dy / len * EyeChargeSpeed;
            }

            ai[1] = 2f;
            ai[2] = 0f;
            return;
        }

        // ai[1] == 2：冲刺 + 减速；连冲 3 次回到追玩家
        ai[2] += 1f;
        if (ai[2] >= 40f)
        {
            npc.VelocityX *= 0.97f;
            npc.VelocityY *= 0.97f;
        }

        if (ai[2] >= 130f)
        {
            ai[3] += 1f;
            ai[2] = 0f;
            if (ai[3] >= 3f)
            {
                ai[1] = 0f;
                ai[3] = 0f;
            }
        }
    }

    /// <summary>在玩家附近生成一只 NPC（原版 NewNPC + 初速 5）：暂存到本 tick 末尾统一入队。</summary>
    private void SpawnNpcNear(int type, PlayerRuntime target, float upwardSpeed)
    {
        var (width, height) = NpcSizes.Of(type);
        float cx = target.AimPosition.X + 10f + (_rng.NextUInt32() % 2 == 0 ? -40f : 40f);   // 玩家碰撞盒中心 ±40
        float cy = target.AimPosition.Y + PlayerHalfHeight;                                   // 玩家碰撞盒中心

        _pendingNpcSpawns.Add(new WorldNpc
        {
            Type = type,
            NetId = (short)type,
            AiStyle = NpcAiStyleOf(type),
            X = cx - width / 2f,     // 原版：X/Y = 碰撞盒左上角
            Y = cy - height / 2f,
            Active = true,
            Life = 20,
            LifeMax = 20,
            VelocityX = 0f,
            VelocityY = -upwardSpeed,
            Generation = (byte)(_rng.NextUInt32() & 0xFF),
        });
    }

    // ========================================================================
    // aiStyle 31：Spazmatism（原版 aiStyle == 31 分支，32227-32867）
    // ========================================================================

    /// <summary>魔焰弹（原版 type 96）初速 / 伤害：一阶段绕行时每 60 帧一发。</summary>
    private const int SpazmatismFireballType = 96;
    private const float SpazmatismOrbitAccel = 0.4f;
    private const float SpazmatismOrbitMaxSpeed = 12f;
    private const int SpazmatismFireIntervalTicks = 60;

    /// <summary>二阶段贴身火球（原版 type 101）与冲刺初速。</summary>
    private const int SpazmatismPhase2FireType = 101;
    private const float SpazmatismChargeSpeed = 13f;
    private const float SpazmatismPhase2ChargeSpeed = 14f;

    /// <summary>
    /// 原版魔焰眼（type 126 / aiStyle 31）的移动与攻击节奏（基础难度，非专家）：
    /// <list type="bullet">
    ///   <item>阶段：`life &lt; lifeMax * 0.4` → `ai[0] = 1`（原版 32542-32548）；`ai[0] == 1` 为 100 帧自旋，随后 `ai[0] = 2` 进入二阶段攻击。</item>
    ///   <item>一阶段 `ai[1] == 0`：绕到玩家中心 ±400px（加速 0.4 / 限速 12），每 60 帧发 1 枚 type 96（12f）；600 帧后转冲刺（原版 32333-32462）。</item>
    ///   <item>`ai[1] == 1`：朝玩家方向 13f → `ai[1] = 2`；`ai[1] == 2`：8 帧后 `*=0.9` 减速，42 帧计一次冲刺，连冲 10 次回落（原版 32464-32540）。</item>
    ///   <item>二阶段：贴身 180px 悬停（加速 0.1 / 限速 4）+ 每 &gt;8 帧发 1 枚 type 101（6f）；400 帧后冲刺 14f、50 帧后 `*=0.93`、80 帧计一次、连冲 6 次（原版 32628-32860）。</item>
    /// </list>
    /// 未移植：专家模式数值缩放、白天/死亡脱战（简化为无目标上浮）、弹幕的原版飞行行为（当前为直线积分）。
    /// </summary>
    private void Ai031Spazmatism(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        npc.NoGravity = true;

        var target = NearestPlayer(npc.X, npc.Y);
        if (target is null)
        {
            npc.VelocityY -= 0.04f;
            return;
        }

        float px = target.AimPosition.X + 10f;   // 玩家碰撞盒中心（宽 20）
        float py = target.AimPosition.Y + 21f;

        if (ai[0] == 0f && npc.Life < npc.LifeMax * 0.4f)
        {
            ai[0] = 1f;
            ai[1] = 0f;
            ai[2] = 0f;
            ai[3] = 0f;
        }

        if (ai[0] == 0f)
        {
            if (ai[1] == 0f)
            {
                // 绕行：目标点 = 玩家中心 + (侧面 400px, 0)
                float side = npc.Direction >= 0 ? 1f : -1f;
                float dx = px + side * 400f - npc.X;
                float dy = py - npc.Y;
                if (MathF.Abs(dx) > 8f) npc.VelocityX += MathF.Sign(dx) * SpazmatismOrbitAccel;
                if (MathF.Abs(dy) > 8f) npc.VelocityY += MathF.Sign(dy) * SpazmatismOrbitAccel;
                npc.VelocityX = Math.Clamp(npc.VelocityX, -SpazmatismOrbitMaxSpeed, SpazmatismOrbitMaxSpeed);
                npc.VelocityY = Math.Clamp(npc.VelocityY, -SpazmatismOrbitMaxSpeed, SpazmatismOrbitMaxSpeed);

                ai[2] += 1f;
                if (ai[2] >= 600f)
                {
                    ai[1] = 1f;
                    ai[2] = 0f;
                    ai[3] = 0f;
                    return;
                }

                // 每 60 帧发一枚魔焰弹（原版 32436-32459）
                ai[3] += 1f;
                if (ai[3] >= SpazmatismFireIntervalTicks)
                {
                    ai[3] = 0f;
                    SpawnNpcProjectileToward(SpazmatismFireballType, npc, px, py, SpazmatismOrbitMaxSpeed, 25, 300);
                }

                return;
            }

            if (ai[1] == 1f)
            {
                SetVelocityToward(npc, px, py, SpazmatismChargeSpeed);
                ai[1] = 2f;
                ai[2] = 0f;
                return;
            }

            // ai[1] == 2：冲刺 + 减速，连冲 10 次
            ai[2] += 1f;
            if (ai[2] >= 8f)
            {
                npc.VelocityX *= 0.9f;
                npc.VelocityY *= 0.9f;
            }

            if (ai[2] >= 42f && (ai[3] += 1f) >= 10f)
            {
                ai[1] = 0f;
                ai[3] = 0f;
                ai[2] = 0f;
            }

            return;
        }

        // ---- 二阶段 ----
        if (ai[0] == 1f)
        {
            // 自旋 100 帧（原版 32574-32580；rotation 为纯客户端表现）
            ai[1] += 1f;
            if (ai[1] >= 100f)
            {
                ai[0] = 2f;
                ai[1] = 0f;
                ai[2] = 0f;
                ai[3] = 0f;
            }

            return;
        }

        if (ai[1] == 0f)
        {
            float side = npc.Direction >= 0 ? 1f : -1f;
            float dx = px + side * 180f - npc.X;
            float dy = py - npc.Y;
            if (MathF.Abs(dx) > 8f) npc.VelocityX += MathF.Sign(dx) * 0.1f;
            if (MathF.Abs(dy) > 8f) npc.VelocityY += MathF.Sign(dy) * 0.1f;
            npc.VelocityX = Math.Clamp(npc.VelocityX, -4f, 4f);
            npc.VelocityY = Math.Clamp(npc.VelocityY, -4f, 4f);

            ai[2] += 1f;
            if (ai[2] >= 400f)
            {
                ai[1] = 1f;
                ai[2] = 0f;
                return;
            }

            // 每 >8 帧发一枚火球（原版 32736-32779，用 localAI[1] 节流）
            local[1] += 1f;
            if (local[1] > 8f)
            {
                local[1] = 0f;
                SpawnNpcProjectileToward(SpazmatismPhase2FireType, npc, px, py, 6f, 30, 300);
            }

            return;
        }

        if (ai[1] == 1f)
        {
            SetVelocityToward(npc, px, py, SpazmatismPhase2ChargeSpeed);
            ai[1] = 2f;
            ai[2] = 0f;
            return;
        }

        ai[2] += 1f;
        if (ai[2] >= 50f)
        {
            npc.VelocityX *= 0.93f;
            npc.VelocityY *= 0.93f;
        }

        if (ai[2] >= 80f && (ai[3] += 1f) >= 6f)
        {
            ai[1] = 0f;
            ai[3] = 0f;
            ai[2] = 0f;
        }
    }

    // ========================================================================
    // aiStyle 43：Queen Bee（原版 aiStyle == 43 分支，35530-36226）
    // ========================================================================

    /// <summary>毒刺（原版 type 719）初速 8f、周期 40 帧；小黄蜂（原版 210 / 211）初速 5f。</summary>
    private const int QueenBeeStingerType = 719;
    private const float QueenBeeStingerSpeed = 8f;
    private const int QueenBeeStingerIntervalTicks = 40;

    /// <summary>
    /// 原版蜂后（type 222 / aiStyle 43）的攻击选择状态机（基础难度、无生物群系修正）：
    /// `ai[0] = -1` 随机挑下一招（与上一招不同，上一招记在 localAI[0]）；
    /// `0` 悬停 + 对齐后冲刺（2 轮后重选）；`1` 黄蜂突进（每 40 帧召 210/211，5 次后重选）；
    /// `2` 重新接近（距离 &lt; 200 转突进）；`3` 毒刺齐射（每 40 帧发 type 719，800 帧后重选）；
    /// `4` 拉开距离（距离 &lt; 2000 重选）；`5` 玩家死亡后离场。
    /// 未移植：`num750` 难度修正（水下/非丛林/古德世界）、专家数值、弹幕原版飞行行为。
    /// </summary>
    private void Ai043QueenBee(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        npc.NoGravity = true;

        var target = NearestPlayer(npc.X, npc.Y);
        if (target is null)
        {
            ai[0] = 5f;   // 无目标 → 离场
        }

        if (target is not null)
        {
            float px = target.AimPosition.X + 10f;
            float py = target.AimPosition.Y + 21f;
            float dist = MathF.Sqrt((px - npc.X) * (px - npc.X) + (py - npc.Y) * (py - npc.Y));

            // 距离过远 → 拉开距离状态（原版 35562）
            if (ai[0] != 5f && dist > 3000f) ai[0] = 4f;

            switch ((int)ai[0])
            {
                case -1:
                    // 原版 35611-35637：随机选 0/2/3，且不与上一招相同
                    int next;
                    do { next = (int)(_rng.NextUInt32() % 3); next = next == 1 ? 2 : next == 2 ? 3 : 0; }
                    while (next == (int)local[0]);
                    local[0] = next;
                    ai[0] = next;
                    ai[1] = 0f;
                    break;

                case 0:
                    // 悬停 / 对齐后冲刺（原版 35638-35867，num754 = 2）
                    if (MathF.Abs(py - (npc.Y + 33f)) < 20f)
                    {
                        ai[1] += 1f;
                        if (ai[1] > 4f) { ai[0] = -1f; ai[1] = 0f; break; }
                        SetVelocityToward(npc, px, py, 12f);
                    }
                    else
                    {
                        float dx = px - npc.X;
                        float dy = py - npc.Y;
                        if (dy > 0f) npc.VelocityY += 0.15f; else npc.VelocityY -= 0.15f;
                        npc.VelocityY = Math.Clamp(npc.VelocityY, -12f, 12f);

                        if (MathF.Abs(dx) > 600f) npc.VelocityX += 0.15f * npc.Direction;
                        else if (MathF.Abs(dx) < 300f) npc.VelocityX -= 0.15f * npc.Direction;
                        else npc.VelocityX *= 0.8f;

                        npc.VelocityX = Math.Clamp(npc.VelocityX, -16f, 16f);
                    }
                    break;

                case 1:
                    // 黄蜂突进（原版 35924-36045）：每 40 帧召 1 只 210/211，5 次后重选
                    SetVelocityToward(npc, px, py, 14f);
                    if (++ai[1] > 40f)
                    {
                        ai[1] = 0f;
                        ai[2] += 1f;
                        SpawnNpcNear(_rng.NextUInt32() % 2 == 0 ? 210 : 211, target, 5f);
                    }

                    if (ai[2] > 5f) { ai[0] = -1f; ai[1] = 1f; }
                    break;

                case 2:
                    // 重新接近（原版 35872-35885）：加速 0.07、限速 12
                    float tdx = px - npc.X;
                    float tdy = py + 21f - 200f - npc.Y;
                    if (MathF.Abs(tdx) > 8f) npc.VelocityX += MathF.Sign(tdx) * 0.07f;
                    if (MathF.Abs(tdy) > 8f) npc.VelocityY += MathF.Sign(tdy) * 0.07f;
                    npc.VelocityX = Math.Clamp(npc.VelocityX, -12f, 12f);
                    npc.VelocityY = Math.Clamp(npc.VelocityY, -12f, 12f);
                    if (dist < 200f) { ai[0] = 1f; ai[1] = 0f; ai[2] = 0f; }
                    break;

                case 3:
                    // 毒刺齐射（原版 36046-36198）：玩家在下方时每 40 帧发一枚 type 719
                    ai[1] += 1f;
                    if (npc.Y + 66f < py && (int)ai[1] % QueenBeeStingerIntervalTicks == QueenBeeStingerIntervalTicks - 1)
                        SpawnNpcProjectileToward(QueenBeeStingerType, npc, px, py, QueenBeeStingerSpeed, 11, 300);

                    if (ai[1] > QueenBeeStingerIntervalTicks * 20)
                    {
                        ai[0] = -1f;
                        ai[1] = 3f;
                    }
                    break;

                case 4:
                    // 拉开距离（原版 36205-36222）：速度向「远离玩家」收敛
                    float ax = px - npc.X;
                    float ay = py - npc.Y;
                    float alen = MathF.Sqrt(ax * ax + ay * ay);
                    if (alen > 0.001f)
                    {
                        float ex = -ax / alen * 14f;
                        float ey = -ay / alen * 14f;
                        npc.VelocityX = (npc.VelocityX * 14f + ex) / 15f;
                        npc.VelocityY = (npc.VelocityY * 14f + ey) / 15f;
                    }

                    if (dist < 2000f) ai[0] = -1f;
                    break;
            }
        }

        if ((int)ai[0] == 5)
        {
            // 离场（原版 35573-35609）：减速上浮，超出世界后由清理逻辑移除
            npc.VelocityY = npc.VelocityY * 0.98f;
            if (npc.VelocityX > 0f) npc.VelocityX -= 0.08f;
            else if (npc.VelocityX < 0f) npc.VelocityX += 0.08f;

            if (npc.Y < 0f) npc.Active = false;
        }
    }

    /// <summary>把 NPC 速度设为「朝 (tx, ty) 的给定速率」（对应原版 `velocity = dir * speed`）。</summary>
    private static void SetVelocityToward(WorldNpc npc, float tx, float ty, float speed)
    {
        float dx = tx - npc.X;
        float dy = ty - npc.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0.001f) return;

        npc.VelocityX = dx / len * speed;
        npc.VelocityY = dy / len * speed;
    }

    /// <summary>服务端弹幕计数器：用负键段避免与客户端弹幕（非负键）冲突。</summary>
    private int _nextServerProjectileKey = -1;

    /// <summary>从 NPC 碰撞盒中心朝 (tx, ty) 发射一枚服务端弹幕（AI 调用；由物理循环在遍历结束后入队）。</summary>
    private void SpawnNpcProjectileToward(int type, WorldNpc npc, float tx, float ty, float speed, int damage, int timeLeft)
    {
        var (width, height) = NpcSizes.Of(npc.Type);
        float muzzleX = npc.X + width / 2f;
        float muzzleY = npc.Y + height / 2f;

        float dx = tx - muzzleX;
        float dy = ty - muzzleY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0.001f) return;

        _pendingProjectiles.Add(new ProjectileEntity
        {
            Key = _nextServerProjectileKey--,
            Owner = -1,                      // 服务端弹幕
            Type = type,
            Position = new Vector2(muzzleX, muzzleY),
            Velocity = new Vector2(dx / len * speed, dy / len * speed),
            Damage = damage,
            TimeLeft = timeLeft,
            Active = true,
            NewNotified = false,             // 由世界同步循环推送包 27
        });
    }
}
