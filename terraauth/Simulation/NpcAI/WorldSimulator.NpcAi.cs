// TerraAuth — NPC AI（按原版 aiStyle 移植）
//
// 结构对齐原版：`NPC.aiStyle` 决定走哪套 AI；每套 AI 只读写 `NPC.ai[0..3]` 与 `velocity/direction`，
// 重力与图格碰撞由统一的物理步（StepNpcPhysics）负责 —— 与「AI 设速度、Update 走物理」的原版顺序一致。
//
// 移植范围（逐步补齐，未移植的走 <see cref="AiFallback"/> 简化追击）：
//   aiStyle 1  Slimes        —— AI_001_Slimes（史莱姆 + 蚂蚱 377/446 分支）
//   aiStyle 3  Fighters      —— AI_003_Fighters（哥布林工兵 26）
//   aiStyle 4  EyeOfCthulhu  —— 眼魔（Boss）
//   aiStyle 7  TownEntities  —— AI_007_TownEntities（城镇 NPC + 小动物 Ai007Critter）
//   aiStyle 16/24/64/65/66/67/68/112/114/115/116/118 —— 小动物逐类（见 NpcCritterSet.AiStyleOf）
//   其余（Boss 专属 aiStyle）—— 待移植
//
// 说明：原版 AI 中的粉尘 / 音效 / 光照 / rotation / spriteDirection 属客户端表现，
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
        // 小动物优先：按原版各自的 aiStyle 逐类分发。这些 aiStyle 里混着敌对追击实现
        // （1 史莱姆扑向玩家 / 3 战士冲锋），故走 Ai0XX 里针对小动物的分支实现，而不是通用 aiStyle 分派。
        if (NpcCritterSet.Is(npc.Type))
        {
            switch (NpcCritterSet.AiStyleOf(npc.Type))
            {
                case 1: Ai001Slimes(npc); break;           // 蚂蚱（377/446）
                case 7: Ai007Critter(npc); break;          // 兔 / 松鼠 / 企鹅 / 青蛙 / 乌龟 / 鸭（城镇行走型）
                case 16: Ai016Fishes(npc); break;          // 金鱼 / 青蛙
                case 24: Ai024Birds(npc); break;           // 鸟 / 金鸟 / 海鸥
                case 64: Ai064Fireflies(npc); break;       // 萤火虫 / 荧光虫
                case 65: Ai065Butterflies(npc); break;     // 蝴蝶 / 金蝴蝶
                case 66: Ai066Worms(npc); break;           // 蚯蚓 / 金蚯蚓
                case 67: Ai067Snails(npc); break;          // 蜗牛 / 荧光蜗牛
                case 68: Ai068Ducks(npc); break;           // 鸭 / 金鸭（地面走 + 遇水游）
                case 112: Ai112Fairies(npc); break;        // 仙灵
                case 114: Ai114Dragonflies(npc); break;    // 蜻蜓 / 金蜻蜓
                case 115: Ai115Ladybugs(npc); break;       // 瓢虫 / 金瓢虫
                case 116: Ai116WaterStriders(npc); break;  // 水黾
                case 118: Ai118Seahorses(npc); break;      // 海马
                default: AiCritterFallback(npc); break;    // 未登记（理论上不会出现）
            }

            StepNpcPhysics(npc);
            return;
        }

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

    // ========================================================================
    // 小动物（按原版 aiStyle 逐类移植；集合与分派见 NpcCritterSet）
    // ========================================================================
    //
    // 口径：只移植「服务端权威运动 + 状态」部分（速度 / ai[] / direction / 跳跃 / 液体交互），
    // 跳过粉尘 / 音效 / 光照 / rotation / spriteDirection 等纯客户端表现。
    // 原版把每类小动物的 AI 内联在巨型 AI() 分派里（AI_001 的蚂蚱分支、aiStyle 7 的寻路、
    // aiStyle 24/64/65/66/67/68/112/114/115/116/118 的飞行/爬行/游泳），此处逐一按行号移植。

    // ---- 随机数（原版 Main.rand 语义：确定性 RNG 映射，见 Determinism.cs）----

    /// <summary>原版 <c>Main.rand.NextFloat()</c>。</summary>
    private float NpcNextFloat() => (float)_rng.NextDouble();

    /// <summary>原版 <c>Main.rand.NextFloat(min, max)</c>。</summary>
    private float NpcNextFloat(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

    /// <summary>原版 <c>Main.rand.NextFloatDirection()</c>：[-1, 1)，步进 0.001。</summary>
    private float NpcNextFloatDirection() => _rng.NextInt32(2001) / 1000f - 1f;

    /// <summary>原版 <c>Main.rand.NextFloat() * 2π</c>（AI 用作随机角）。</summary>
    private float NpcNextAngleRadians() => (float)_rng.NextDouble() * MathF.PI * 2f;

    /// <summary>原版 <c>Main.rand.Next(min, max)</c>。</summary>
    private int NpcNextInt(int min, int max)
    {
        int span = max - min;
        return span <= 0 ? min : min + (int)(_rng.NextUInt32() % (uint)span);
    }

    /// <summary>原版 <c>Main.rand.NextVector2Circular(halfWidth, halfHeight)</c>（椭圆内均匀随机点）。</summary>
    private (float X, float Y) NpcRngCircular(float halfWidth, float halfHeight)
    {
        float angle = NpcNextAngleRadians();
        float magnitude = NpcNextFloat();
        return (MathF.Cos(angle) * halfWidth * magnitude, MathF.Sin(angle) * halfHeight * magnitude);
    }

    /// <summary>原版 <c>Main.rand.NextVector2CircularEdge(halfWidth, halfHeight)</c>（椭圆边缘随机点）。</summary>
    private (float X, float Y) NpcRngCircularEdge(float halfWidth, float halfHeight)
    {
        float angle = NpcNextAngleRadians();
        return (MathF.Cos(angle) * halfWidth, MathF.Sin(angle) * halfHeight);
    }

    /// <summary>原版 <c>Vector2.ToRotationVector2()</c>：(cos, sin)。</summary>
    private static (float X, float Y) NpcAngleToVector(float radians) => (MathF.Cos(radians), MathF.Sin(radians));

    /// <summary>原版 <c>Vector2.ToRotation()</c>：atan2(Y, X)。</summary>
    private static float NpcVectorToAngle(float x, float y) => MathF.Atan2(y, x);

    // ---- 图格 / 液体查询 ----

    /// <summary>图格液体量（越界返回 0）。</summary>
    private byte NpcLiquidAt(int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0 || tileX >= _world.Tiles.Width || tileY >= _world.Tiles.Height) return 0;
        return _world.Tiles[tileX, tileY].Liquid;
    }

    /// <summary>图格液体类型（越界返回 0 = 水）。</summary>
    private byte NpcLiquidTypeAt(int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0 || tileX >= _world.Tiles.Width || tileY >= _world.Tiles.Height) return 0;
        return _world.Tiles[tileX, tileY].LiquidType;
    }

    /// <summary>是否计入「浸湿」的液体（原版排除岩浆 type 1 与微光 type 3）。</summary>
    private static bool NpcIsWaterLike(byte liquidType) => liquidType is not (1 or 3);

    /// <summary>矩形区域内是否存在实心格（对应原版 <c>Collision.SolidTiles</c> / <c>SolidCollision</c> 的判定口径）。</summary>
    private bool NpcSolidInRect(int left, int right, int top, int bottom)
    {
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
                if (NpcTileSolid(x, y)) return true;
        return false;
    }

    /// <summary>原版 <c>Collision.SolidCollision(position, width, height)</c>：碰撞盒所覆盖图格是否有实心格。</summary>
    private bool NpcSolidCollision(float x, float y, int width, int height)
    {
        int left = (int)(x / TileSize);
        int right = (int)((x + width - 1f) / TileSize);
        int top = (int)(y / TileSize);
        int bottom = (int)((y + height - 1f) / TileSize);
        return NpcSolidInRect(left, right, top, bottom);
    }

    /// <summary>
    /// 原版 <c>Collision.GetWaterLine</c>：从 (X, Y) 向上看最近液面（返回该液面的世界 Y 坐标）。
    /// </summary>
    private bool NpcTryGetWaterLine(int tileX, int tileY, out float waterLine)
    {
        waterLine = 0f;
        if (tileX < 10 || tileY < 10 || tileX >= _world.MaxTilesX - 10 || tileY >= _world.MaxTilesY - 10) return false;
        if (NpcLiquidAt(tileX, tileY - 2) > 0) return false;

        if (NpcLiquidAt(tileX, tileY - 1) > 0)
        {
            waterLine = tileY * 16 - NpcLiquidAt(tileX, tileY - 1) / 16;
            return true;
        }
        if (NpcLiquidAt(tileX, tileY) > 0)
        {
            waterLine = (tileY + 1) * 16 - NpcLiquidAt(tileX, tileY) / 16;
            return true;
        }
        if (NpcLiquidAt(tileX, tileY + 1) > 0)
        {
            waterLine = (tileY + 2) * 16 - NpcLiquidAt(tileX, tileY + 1) / 16;
            return true;
        }
        return false;
    }

    /// <summary>原版 <c>Collision.GetWaterLineIterate</c>：向上扫到液体顶端后再定液面（海马 / 鸭子用）。</summary>
    private bool NpcTryGetWaterLineIterate(int tileX, int tileY, out float waterLine)
    {
        waterLine = 0f;
        if (tileX < 0 || tileX >= _world.Tiles.Width || tileY < 0 || tileY >= _world.Tiles.Height) return false;

        while (tileY > 0 && NpcLiquidAt(tileX, tileY) > 0) tileY--;
        tileY++;
        if (tileY >= _world.Tiles.Height) return false;

        if (NpcLiquidAt(tileX, tileY) > 0)
        {
            waterLine = tileY * 16 - NpcLiquidAt(tileX, tileY - 1) / 16;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 原版 <c>Collision.WetCollision</c>：碰撞盒「内盒」是否与液体格相交（判定 <c>NPC.wet</c>）。
    /// 内盒 = 中心 ± (min(10, W) / 2, H / 2 / 2)，按格内液面高度（<c>liquid</c> 越少液面越低）做矩形相交。
    /// </summary>
    private bool NpcWetAt(float x, float y, int width, int height)
    {
        float boxW = MathF.Min(10f, width);
        float boxH = MathF.Min(height / 2f, height);
        float boxX = x + width * 0.5f - boxW * 0.5f;
        float boxY = y + height * 0.5f - boxH * 0.5f;

        int minX = Math.Clamp((int)(x / TileSize) - 1, 0, _world.MaxTilesX - 1);
        int maxX = Math.Clamp((int)((x + width) / TileSize) + 2, 0, _world.MaxTilesX - 1);
        int minY = Math.Clamp((int)(y / TileSize) - 1, 0, _world.MaxTilesY - 40);
        int maxY = Math.Clamp((int)((y + height) / TileSize) + 2, 0, _world.MaxTilesY - 40);

        for (int i = minX; i < maxX; i++)
        {
            for (int j = minY; j < maxY; j++)
            {
                ref var tile = ref _world.Tiles[i, j];
                if (tile.Liquid == 0) continue;
                if (!NpcIsWaterLike(tile.LiquidType)) continue;

                float tileTop = j * 16f;
                float shrink = (256 - tile.Liquid) / 32f * 2f;
                tileTop += shrink;
                float tileHeight = 16f - (int)shrink;

                if (boxX + boxW > i * 16f && boxX < i * 16f + 16f
                    && boxY + boxH > tileTop && boxY < tileTop + tileHeight)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 原版 <c>Collision.DrownCollision(position, width, height, gravDir, includeSlopes)</c>：碰撞盒「内盒」
    /// 是否浸没在液体中（AI_007 用于判定「要淹死了」→ 打断寻路；AI_001 蚂蚱的 wet 分支亦用其近亲概念）。
    /// 内盒宽 min(10, W)、高 min(12, H)；盒 Y 起点 = <c>Position.Y - 2</c>（gravDir 为 -1 时再 + H/2 - 6）；
    /// 被排除的那一行实心格（gravDir 为 1 取顶行、为 -1 取脚底行）不计入液体相交。
    /// </summary>
    private bool NpcDrowning(float x, float y, int width, int height, float gravDir)
    {
        int boxW = Math.Min(10, width);    // 原版 num = 10，Width 更小则取 Width
        int boxH = Math.Min(12, height);   // 原版 num2 = 12，Height 更小则取 Height

        float boxX = x + width / 2f - boxW / 2f;
        float boxY = y - 2f + (gravDir == -1f ? height / 2 - 6 : 0);

        int minX = Math.Clamp((int)(x / TileSize) - 1, 0, _world.MaxTilesX - 1);
        int maxX = Math.Clamp((int)((x + width) / TileSize) + 2, 0, _world.MaxTilesX - 1);
        int minY = Math.Clamp((int)(y / TileSize) - 1, 0, _world.MaxTilesY - 40);
        int maxY = Math.Clamp((int)((y + height) / TileSize) + 2, 0, _world.MaxTilesY - 40);

        int excludedRow = gravDir == 1f ? minY : maxY - 1;

        for (int i = minX; i < maxX; i++)
        {
            for (int j = minY; j < maxY; j++)
            {
                ref var tile = ref _world.Tiles[i, j];
                if (tile.Liquid == 0) continue;
                if (!NpcIsWaterLike(tile.LiquidType)) continue;
                if (j == excludedRow && tile.Active && TileIdSets.IsTileSolid(tile.Type)) continue;

                float tileTop = j * 16f;
                float shrink = (256 - tile.Liquid) / 32f * 2f;
                tileTop += shrink;
                float tileHeight = 16f - (int)shrink;

                if (boxX + boxW > i * 16f && boxX < i * 16f + 16f
                    && boxY + boxH > tileTop && boxY < tileTop + tileHeight)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 原版 <c>NPCID.Sets.TownCritter</c>：会影响 aiStyle 7 的回家 / 寻路判定
    /// （<c>GetWalkPrediction</c> 对 TownCritter 恒返回「不悬崖」）。
    /// </summary>
    private static bool NpcIsTownCritter(int type) => type is
        46 or 148 or 149 or 230 or 299 or 300 or 337 or 361 or 362 or 364 or 366 or 367 or 443 or 445
        or 446 or 447 or 538 or 539 or 540 or 593 or 602 or 603 or 608 or 609 or 610 or 616 or 617 or 625
        or 687 or (>= 639 and <= 652);

    /// <summary>是否「友善」（原版 <c>NPC.friendly</c>）：小动物与城镇 NPC。</summary>
    private static bool NpcFriendly(WorldNpc npc) => NpcCritterSet.Is(npc.Type) || npc.IsTownNpc;

    /// <summary>
    /// 未登记 aiStyle 的小动物兜底：原地不动的慢速随机游走（不会主动接近玩家）。
    /// </summary>
    private void AiCritterFallback(WorldNpc npc)
    {
        npc.VelocityX *= 0.9f;
        if (npc.VelocityX is > -0.05f and < 0.05f) npc.VelocityX = 0f;
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

    /// <summary>
    /// 逐类型重力覆盖（原版 <c>NPC.UpdateNPC_UpdateGravity</c>）：默认 gravity 0.3 / maxFall 10，
    /// 少数类型按 <c>ai</c> 状态覆盖（258 重力 0.1 且下落限速 3；425（ai[2]==1）重力 0.1；
    /// 427（ai[2]==1）重力 0.1 且下落限速 4；426 重力 0.1 且限速 3；541 无重力；
    /// 576/577（ai[0]&gt;0 且 ai[1]==2）重力 0.45 且限速 32；城镇 NPC 坐下（aiStyle==7 且 ai[0]==25）重力归零）。
    /// 液体 / 空间高度因子（乘 0.25~1）未建模，保持原版地面默认口径。
    /// </summary>
    private (float Gravity, float MaxFall) NpcGravityOf(WorldNpc npc) => npc.Type switch
    {
        258 => (0.1f, 3f),
        425 => npc.Ai[2] == 1f ? (0.1f, NpcMaxFallSpeed) : (NpcGravity, NpcMaxFallSpeed),
        427 => npc.Ai[2] == 1f ? (0.1f, 4f) : (NpcGravity, NpcMaxFallSpeed),   // 原版 427 命中额外下落限速 4
        426 => (0.1f, 3f),
        541 => (0f, NpcMaxFallSpeed),
        576 or 577 => npc.Ai[0] > 0f && npc.Ai[1] == 2f ? (0.45f, 32f) : (NpcGravity, NpcMaxFallSpeed),
        _ when npc.AiStyle == 7 && npc.Ai[0] == 25f => (0f, NpcMaxFallSpeed), // 城镇 NPC 坐下(ai[0]==25)重力归零
        _ => (NpcGravity, NpcMaxFallSpeed),
    };

    /// <summary>
    /// 重力 + 位移 + 图格碰撞（所有走通用物理的 NPC 共用），并填写 AI 下一帧读取的瞬态：
    /// <c>CollideX</c> / <c>CollideY</c> / <c>PrevVelocity*</c> / <c>Grounded</c> / <c>Wet</c>。
    /// <para>
    /// 与旧实现的区别：<see cref="WorldNpc.NoGravity"/> 与 <see cref="WorldNpc.NoTileCollide"/> **解耦** ——
    /// 前者只管重力，后者才跳过图格碰撞。原版很多小动物（蜗牛爬墙、仙灵 / 蜻蜓贴地态、海马）是
    /// <c>noGravity = true</c> 但仍需要 <c>collideX</c> / <c>collideY</c> 来转向，旧实现把它们混在一起会导致
    /// 这些 AI 永远拿不到碰撞标志。
    /// </para>
    /// 未建模（保持简化，与原版差异仅为斜坡 / 平台的一格精度）：斜坡、半砖、平台、斜坡下落。
    /// </summary>
    private void StepNpcPhysics(WorldNpc npc)
    {
        var (width, height) = NpcSizes.Of(npc.Type);

        npc.PrevVelocityX = npc.VelocityX;
        npc.PrevVelocityY = npc.VelocityY;
        npc.CollideX = false;
        npc.CollideY = false;
        npc.Grounded = false;

        if (!npc.NoGravity)
        {
            var (gravity, maxFall) = NpcGravityOf(npc);
            npc.VelocityY = MathF.Min(npc.VelocityY + gravity, maxFall);
        }

        float nextX = npc.X + npc.VelocityX;
        float nextY = npc.Y + npc.VelocityY;

        if (!npc.NoTileCollide)
        {
            // ---- 水平：先按目标 X 找前进边缘列，命中则贴齐并清零速度 ----
            if (npc.VelocityX != 0f)
            {
                int rowTop = (int)((npc.Y + 1f) / TileSize);
                int rowBottom = (int)((npc.Y + height - 1f) / TileSize);

                if (npc.VelocityX > 0f)
                {
                    int col = (int)((nextX + width - 1f) / TileSize);
                    for (int r = rowTop; r <= rowBottom; r++)
                    {
                        if (!NpcTileSolid(col, r)) continue;
                        nextX = col * TileSize - width;
                        npc.VelocityX = 0f;
                        npc.CollideX = true;
                        break;
                    }
                }
                else
                {
                    int col = (int)(nextX / TileSize);
                    for (int r = rowTop; r <= rowBottom; r++)
                    {
                        if (!NpcTileSolid(col, r)) continue;
                        nextX = (col + 1) * TileSize;
                        npc.VelocityX = 0f;
                        npc.CollideX = true;
                        break;
                    }
                }
            }

            // ---- 垂直：下落贴地 / 上升撞顶（原版落地只需部分踩实）----
            int colLeft = (int)((nextX + 1f) / TileSize);
            int colRight = (int)((nextX + width - 1f) / TileSize);

            if (npc.VelocityY > 0f)
            {
                int row = (int)((nextY + height) / TileSize);
                for (int c = colLeft; c <= colRight; c++)
                {
                    if (!NpcTileSolid(c, row)) continue;
                    nextY = row * TileSize - height;
                    npc.VelocityY = 0f;
                    npc.CollideY = true;
                    npc.Grounded = true;
                    break;
                }
            }
            else if (npc.VelocityY < 0f)
            {
                int row = (int)(nextY / TileSize);
                for (int c = colLeft; c <= colRight; c++)
                {
                    if (!NpcTileSolid(c, row)) continue;
                    nextY = (row + 1) * TileSize;
                    npc.VelocityY = 0f;
                    npc.CollideY = true;
                    break;
                }
            }
            else
            {
                // vy == 0：仍探测贴地（AI 依赖 Grounded 决定是否走「等待 → 起跳」）
                int row = (int)((nextY + height + 1f) / TileSize);
                for (int c = colLeft; c <= colRight; c++)
                {
                    if (!NpcTileSolid(c, row)) continue;
                    npc.Grounded = true;
                    break;
                }
            }
        }

        // 世界边界钳制（所有 NPC 共用；越界会让客户端图格查询越界崩溃）
        npc.X = Math.Clamp(nextX, TileSize, (_world.MaxTilesX - 1) * TileSize);
        npc.Y = Math.Clamp(nextY, TileSize, (_world.MaxTilesY - 1) * TileSize);

        // 液体探测按**新位置**（原版 NPC.Update 末尾重算 wet）
        npc.Wet = NpcWetAt(npc.X, npc.Y, width, height);
    }

    // ========================================================================
    // aiStyle 1：Slimes（原版 AI_001_Slimes，type 1 蓝史莱姆路径）
    // ========================================================================

    /// <summary>原版 <c>num54</c>：跳跃计时门限（type 1 取 -1000；部分变体取 -500 / -400）。</summary>
    private const float SlimeJumpThreshold = -1000f;

    /// <summary>
    /// 原版 AI_001_Slimes（type 1 蓝史莱姆路径 + 蚂蚱 377/446 分支，逐条对照原版行号）：
    /// <list type="bullet">
    ///   <item>`flag3`（是否「有威胁」）：原版 <c>!dayTime || life != lifeMax || Y &gt; worldSurface*16 || slimeRain</c>；
    ///         蚂蚱 377/446 覆盖为「有玩家在 200px 内且未湿」。</item>
    ///   <item>湿水分支（73267-73314，非 return）：`collideY → velocity.Y = -2`；上浮 `velocity.Y -= 0.5`（下限 -4）；
    ///         速度朝 -Y 时用 `ai[3] == position.X` 判卡住 → 掉头。</item>
    ///   <item>`ai[2] == 0` 一次性初始化：`ai[0] = -100`、`ai[2] = 1`、选定目标方向。</item>
    ///   <item>贴地（`velocity.Y == 0`）：落地卡墙退位；`ai[3] == position.X` → 卡住反向；地面摩擦 `velocity.X *= 0.8`；
    ///         `ai[0]++`（flag3 再 +1，蚂蚱再 +3）；按 `ai[0]` 与门限的关系分 1/2/3 三档起跳。</item>
    ///   <item>起跳：1/2 档 `velocity.Y = -6`、`velocity.X += 2 * direction`；3 档 `velocity.Y = -8`、
    ///         `velocity.X += 3 * direction` 并记录 `ai[3] = position.X`。蚂蚱额外 `*(-0.9, 0.6)` 并在
    ///         `flag3` 时翻向（真正实现「背向玩家跳走」）；头顶实心时按高度修正 `velocity.Y`。</item>
    ///   <item>空中：朝 `direction` 水平加速 `0.2`，上限 ±3（原版 `0.93` 阻尼 + `0.2` 加速）。</item>
    /// </list>
    /// 未移植：type 59 / 71 / 81 / 138 / 183 / 184 / 244 / 304 / 658 / 659 / 667 / 676 / 685 等变体的专属数值与视觉分支。
    /// </summary>
    private void Ai001Slimes(WorldNpc npc)
    {
        var ai = npc.Ai;
        var (width, height) = NpcSizes.Of(npc.Type);
        bool isGrasshopper = npc.Type is 377 or 446;
        var target = NearestPlayer(npc.X, npc.Y);

        // 原版 72838：flag3 = 非白天 / 已受伤 / 在地表以下 / 史莱姆雨
        bool flag3 = !_world.DayTime
            || npc.Life != npc.LifeMax
            || npc.Y > _world.WorldSurface * TileSize
            || _world.Progress.SlimeRain;

        // 原版 72858：蚂蚱在 200px 内有存活玩家且未湿时也视为「有威胁」（用于背向跳跃）
        if (isGrasshopper && target is not null && !npc.Wet)
        {
            float dx = target.AimPosition.X + NpcSizes.PlayerWidth / 2f - (npc.X + width / 2f);
            float dy = target.AimPosition.Y + NpcSizes.PlayerHeight / 2f - (npc.Y + height / 2f);
            if (dx * dx + dy * dy <= 200f * 200f) flag3 = true;
        }

        // ---- 原版 73267-73314：湿水分支（不 return，之后仍走地面 / 空中分支）----
        if (npc.Wet)
        {
            if (npc.CollideY) npc.VelocityY = -2f;
            if (npc.VelocityY < 0f && ai[3] == npc.X)
            {
                npc.Direction *= -1;
                ai[2] = 200f;
            }
            if (npc.VelocityY > 0f) ai[3] = npc.X;

            if (npc.VelocityY > 2f) npc.VelocityY *= 0.9f;
            npc.VelocityY -= 0.5f;
            if (npc.VelocityY < -4f) npc.VelocityY = -4f;

            if (ai[2] == 1f && flag3) AimAtTarget(npc, target, width);
        }

        // 原版 73316：一次性初始化
        if (ai[2] == 0f)
        {
            ai[0] = -100f;
            ai[2] = 1f;
            AimAtTarget(npc, target, width);
        }

        // 原版 73263：ai[2] 递减
        if (ai[2] > 1f) ai[2] -= 1f;

        // ---- 原版 73322-73509：贴地 ----
        if (npc.VelocityY == 0f)
        {
            // 落地瞬间卡在实心方块里 → 沿运动反方向退位（原版 73324）
            if (npc.CollideY && npc.PrevVelocityY != 0f && NpcSolidCollision(npc.X, npc.Y, width, height))
                npc.X -= npc.VelocityX + npc.Direction;

            if (ai[3] != 0f && ai[3] == npc.X)
            {
                npc.Direction *= -1;
                ai[2] = 200f;
            }

            ai[3] = 0f;

            npc.VelocityX *= 0.8f;
            if (npc.VelocityX is > -0.1f and < 0.1f) npc.VelocityX = 0f;

            if (flag3) ai[0] += 1f;
            ai[0] += 1f;
            if (isGrasshopper) ai[0] += 3f;   // 原版 73392：蚂蚱跳得更频繁

            int phase = 0;
            if (ai[0] >= 0f) phase = 1;
            if (ai[0] >= SlimeJumpThreshold && ai[0] <= SlimeJumpThreshold * 0.5f) phase = 2;
            if (ai[0] >= SlimeJumpThreshold * 2f && ai[0] <= SlimeJumpThreshold * 1.5f) phase = 3;

            if (phase > 0)
            {
                if (flag3 && ai[2] == 1f) AimAtTarget(npc, target, width);

                if (phase == 3)
                {
                    npc.VelocityY = -8f;
                    npc.VelocityX += 3f * npc.Direction;
                    ai[0] = -200f;
                    ai[3] = npc.X;
                }
                else
                {
                    npc.VelocityY = -6f;
                    npc.VelocityX += 2f * npc.Direction;
                    ai[0] = -120f + (phase == 1 ? SlimeJumpThreshold : SlimeJumpThreshold * 2f);
                }

                // 原版 73488-73503：蚂蚱跳跃修正 —— 阻尼、flag3 时翻向（背向玩家）、头顶实心时压低跳跃
                if (isGrasshopper)
                {
                    npc.VelocityY *= 0.9f;
                    npc.VelocityX *= 0.6f;
                    if (flag3)
                    {
                        npc.Direction = -npc.Direction;
                        npc.VelocityX *= -1f;
                    }

                    int cx = (int)((npc.X + width / 2f) / TileSize);
                    int cy = (int)((npc.Y + height / 2f) / TileSize) - 1;
                    if (NpcTileSolid(cx, cy) && -npc.VelocityY + height > 16f)
                        npc.VelocityY = -(16 - height);
                }
            }

            return;
        }

        // ---- 原版 73510-73528：空中（只有朝目标方向且未超速时才加速）----
        if (target is null) return;
        if ((npc.Direction == 1 && npc.VelocityX >= 3f) || (npc.Direction == -1 && npc.VelocityX <= -3f)) return;

        if (npc.CollideX && MathF.Abs(npc.VelocityX) == 0.2f)
            npc.X -= 1.4f * npc.Direction;
        if (npc.CollideY && npc.PrevVelocityY != 0f && NpcSolidCollision(npc.X, npc.Y, width, height))
            npc.X -= npc.VelocityX + npc.Direction;

        if ((npc.Direction == -1 && npc.VelocityX < 0.01f) || (npc.Direction == 1 && npc.VelocityX > -0.01f))
            npc.VelocityX += 0.2f * npc.Direction;
        else
            npc.VelocityX *= 0.93f;
    }

    /// <summary>原版 <c>TargetClosest()</c> 的朝向部分：把 <c>direction</c> 指向目标中心所在侧。</summary>
    private static void AimAtTarget(WorldNpc npc, PlayerRuntime? target, int npcWidth)
    {
        if (target is null) return;
        float targetCenterX = target.AimPosition.X + NpcSizes.PlayerWidth / 2f;
        npc.Direction = targetCenterX > npc.X + npcWidth / 2f ? 1 : -1;
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
        npc.NoGravity = true;      // 原版：noGravity + noTileCollide
        npc.NoTileCollide = true;

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

    /// <summary>
    /// 在玩家附近生成一只 NPC（原版 NewNPC + 初速 5）：暂存到本 tick 末尾统一入队。
    /// 生命上限取 <see cref="NpcStatsTable"/>（仆从 type 5 = 8、蜜蜂 210 = 20、小蜜蜂 211 = 10）——
    /// 曾硬编码 20，比客户端上限大 → 包 23 只发当前生命，血条被截断显示为满（实测「10/8」）。
    /// </summary>
    private void SpawnNpcNear(int type, PlayerRuntime target, float upwardSpeed)
    {
        var (width, height) = NpcSizes.Of(type);
        var stats = NpcStatsTable.OfNetId(type);
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
            Life = stats.LifeMax,
            LifeMax = stats.LifeMax,
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
        npc.NoTileCollide = true;

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
        npc.NoTileCollide = true;

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

    // ========================================================================
    // 小动物 aiStyle 7：城镇行走型（原版 AI_007_TownEntities 的小动物路径）
    // ========================================================================

    /// <summary>原版 <c>AI_007_TownEntities_IsInAGoodRestingSpot</c>：当前格是否就是「家」的落脚点。</summary>
    private bool NpcIsInAGoodRestingSpot(WorldNpc npc, int tileX, int tileY, int idealRestX, int idealRestY)
    {
        if (!_world.DayTime && npc.Ai[0] == 5f)
            return MathF.Abs(tileX - idealRestX) <= 7 && MathF.Abs(tileY - idealRestY) <= 7;

        if (npc.Type is 361 or 445 or 687 && npc.Wet) return false;

        return tileX == idealRestX && tileY == idealRestY;
    }

    /// <summary>
    /// 原版 <c>AI_007_FindGoodRestingSpot</c>：把「家」下扫到第一个可站立的图格。
    /// 原版之后的椅子扫描（设置 <c>ai[0] = 5</c> 坐下）依赖 <c>TileID.Sets.CanBeSatOnForNPCs</c> 家具，
    /// 我们的世界生成不放置该类家具，故略去（行为等价）。
    /// </summary>
    private void NpcFindGoodRestingSpot(WorldNpc npc, out int floorX, out int floorY)
    {
        floorX = npc.HomeTileX;
        floorY = npc.HomeTileY;
        if (floorX == -1 || floorY == -1) return;

        while (floorY < _world.MaxTilesY - 20 && !NpcTileSolid(floorX, floorY))
            floorY++;
    }

    /// <summary>
    /// 原版 <c>AI_007_TownEntities_GetWalkPrediction</c>：判断「继续走」与「避免掉下去」。
    /// 关键点：<c>NPCID.Sets.TownCritter</c> 的类型会直接得到 <c>avoidFalling = false</c>（不悬崖探测）。
    /// </summary>
    private void NpcGetWalkPrediction(WorldNpc npc, bool canBreatheUnderWater, bool currentlyDrowning,
        int myTileX, int homeFloorX, int tileX, int tileY, out bool keepwalking, out bool avoidFalling)
    {
        var (width, height) = NpcSizes.Of(npc.Type);
        keepwalking = false;
        avoidFalling = true;

        bool withinHomeLeash = myTileX >= homeFloorX - 35 && myTileX <= homeFloorX + 35;

        // 原版对 isLikeATownNPC（= townNPC）有额外探测；小动物走 false 分支，跳过。

        if (!keepwalking && currentlyDrowning) keepwalking = true;

        if (avoidFalling && (NpcIsTownCritter(npc.Type) || (!withinHomeLeash && npc.Direction == Math.Sign(homeFloorX - myTileX))))
            avoidFalling = false;

        if (!avoidFalling) return;

        bool lava = false;
        int liquidCount = 0;
        int solidX = 0;
        int solidY = 0;

        for (int j = -1; j <= 4; j++)
        {
            int y = tileY + j;
            if (y < 0 || y >= _world.Tiles.Height) continue;

            byte liquid = NpcLiquidAt(tileX, y);
            if (liquid > 0)
            {
                liquidCount++;
                if (NpcLiquidTypeAt(tileX, y) == 1)
                {
                    lava = true;
                    break;
                }
            }

            if (NpcTileSolid(tileX, y))
            {
                if (liquidCount > 0)
                {
                    solidX = tileX;
                    solidY = y;
                }
                avoidFalling = false;
                break;
            }
        }

        avoidFalling |= lava;

        if (liquidCount >= Math.Ceiling(height / (double)TileSize)) avoidFalling = true;

        if (!avoidFalling && solidX != 0 && solidY != 0)
        {
            float dropX = solidX * TileSize + 8f - width / 2f;
            float dropY = solidY * TileSize - height;
            avoidFalling = NpcDrowning(dropX, dropY, width, height, 1f);
        }
    }

    /// <summary>
    /// 原版 aiStyle 7 的小动物路径（AI_007_TownEntities 去掉住房判定 / 攻击 / 传送 / 微光 / 椅子分支）：
    /// 威胁扫描（NPC + 玩家）→ 抢占器（转向背向威胁并进入行走）、原地 idle（随机掉头 / 回家）、
    /// 行走（加速至 num22 / 前后 ±35 格拴绳 / 三档障碍起跳 / 卡住翻向）。
    /// </summary>
    private void Ai007Critter(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);

        // 原版 63484：陆地形态（362/364/602/608）在快速下落或入水时切换为游动形态，随后立即 return。
        // 原版为 `Transform(type + 1)`（ai 全归零），下一帧由分派转到 aiStyle 68。
        if (npc.Type is 362 or 364 or 602 or 608
            && (npc.VelocityY > 4f || npc.VelocityY < -4f || npc.Wet))
        {
            TransformNpc(npc, npc.Type + 1);   // 362→363 / 364→365 / 602→603 / 608→609
            return;
        }

        // 原版 63606：首帧落地时把「家」定在脚下（原版 NewNPC 默认 -1，这里保持同一口径）
        if (npc.HomeTileX == -1 && npc.HomeTileY == -1 && npc.VelocityY == 0f)
        {
            npc.HomeTileX = (int)((npc.X + width / 2f) / TileSize);
            npc.HomeTileY = (int)((npc.Y + height + 4f) / TileSize);
        }

        int myTileX = (int)((npc.X + width / 2f) / TileSize);
        int myTileY = (int)((npc.Y + height + 1f) / TileSize);
        NpcFindGoodRestingSpot(npc, out int floorX, out int floorY);

        npc.DirectionY = -1;
        if (npc.Direction == 0) npc.Direction = 1;

        // 原版 63286-63302：恶劣天气 / 夜晚 → 找地方休息
        bool flag = _world.Raining || !_world.DayTime || _world.Eclipse || _world.Progress.SlimeRain;

        bool flag7 = npc.Type is 300 or 447 or 610;   // 走速更快的特殊小动物
        bool flag8 = npc.Type is 616 or 617 or 625;   // 城镇鸭（水陆两栖）
        bool flag9 = npc.Type is 361 or 445 or 687;   // 青蛙
        bool flag10 = false;                          // 城镇史莱姆（不是小动物）
        bool flag11 = flag8 || flag9;                 // 可在水下呼吸
        const float threatRange = 200f;               // 原版 num11（未逐个登记 DangerDetectRange）

        // ---- 原版 63795-63867：威胁扫描（敌对 NPC，其次玩家）----
        bool flag16 = false;
        float threatLeftX = -1f;    // num13：左侧最近威胁的 X 偏移
        float threatRightX = -1f;   // num14：右侧最近威胁的 X 偏移
        float myCenterX = npc.X + width / 2f;
        float myCenterY = npc.Y + height / 2f;

        if (!flag8)
        {
            foreach (var other in _world.Npcs)
            {
                if (ReferenceEquals(other, npc) || !other.Active) continue;
                if (NpcCritterSet.Is(other.Type) || other.IsTownNpc) continue;   // 原版：friendly || damage <= 0
                if (other.Damage <= 0) continue;

                var (ow, oh) = NpcSizes.Of(other.Type);
                float ox = other.X + ow / 2f;
                float oy = other.Y + oh / 2f;
                float dx = ox - myCenterX;
                float dy = oy - myCenterY;
                if (dx * dx + dy * dy >= threatRange * threatRange) continue;
                if (!other.NoTileCollide && !NpcHasLineOfSight(myCenterX, myCenterY, ox, oy)) continue;

                flag16 = true;
                if (dx < 0f && (threatLeftX == -1f || dx > threatLeftX)) threatLeftX = dx;
                if (dx > 0f && (threatRightX == -1f || dx < threatRightX)) threatRightX = dx;
            }

            if (!flag16)
            {
                // 原版 63850 的条件带有 stinky（恶臭增益）判定；小动物对**普通玩家**同样应当逃离，
                // 故这里按「附近有存活玩家」的口径处理（与既有 Critter 逃离测试一致）。
                foreach (var player in _world.Players.Values)
                {
                    if (!player.Active || player.Dead) continue;

                    float dx = player.AimPosition.X + NpcSizes.PlayerWidth / 2f - myCenterX;
                    float dy = player.AimPosition.Y + NpcSizes.PlayerHeight / 2f - myCenterY;
                    if (dx * dx + dy * dy >= threatRange * threatRange) continue;

                    flag16 = true;
                    if (dx < 0f && (threatLeftX == -1f || dx > threatLeftX)) threatLeftX = dx;
                    if (dx > 0f && (threatRightX == -1f || dx < threatRightX)) threatRightX = dx;
                }
            }
        }

        // ---- 原版 63868-63939：抢占器（有威胁时强制转向「背向威胁」并进入行走）----
        int threatSide = 0;   // num15：威胁在哪一侧（1 = 威胁在右 → 逃向左）
        if (flag16)
        {
            threatSide = threatLeftX == -1f
                ? 1
                : threatRightX != -1f
                    ? (threatRightX < -threatLeftX ? 1 : -1)
                    : -1;

            if (ai[0] == 8f)
            {
                if (npc.Direction == -threatSide)
                {
                    ai[0] = 1f;
                    ai[1] = 300f + NpcNextInt(0, 300);
                    ai[2] = 0f;
                    local[3] = 0f;
                }
            }
            else if (ai[0] is not (10f or 12f or 13f or 14f or 15f))
            {
                if (ai[0] != 1f)
                {
                    int probeX = (int)((npc.X + width / 2f + 15f * npc.Direction) / TileSize);
                    int probeY = (int)((npc.Y + height - 16f) / TileSize);
                    bool currentlyDrowning = npc.Wet && !flag11;
                    NpcGetWalkPrediction(npc, flag11, currentlyDrowning, myTileX, floorX, probeX, probeY,
                        out bool _, out bool avoidFalling);

                    if (!avoidFalling)
                    {
                        ai[0] = 1f;
                        ai[1] = 120f + NpcNextInt(0, 120);
                        ai[2] = 0f;
                        local[3] = 0f;
                        npc.Direction = -threatSide;
                    }
                }
                else if (npc.Direction != -threatSide)
                {
                    npc.Direction = -threatSide;
                }
            }
        }

        bool flag21 = !flag11 && NpcDrowning(npc.X, npc.Y, width, height, 1f);

        // ---- 原版 63941-64115：idle（ai[0] == 0）----
        if (ai[0] == 0f)
        {
            if (local[3] > 0f) local[3] -= 1f;

            if ((flag9 | flag10) && npc.Wet)
            {
                // 青蛙在水里 → 上岸
                ai[0] = 1f;
                ai[1] = 200f + NpcNextInt(500, 700);
                ai[2] = 0f;
                local[3] = 0f;
            }
            else if (flag && !NpcIsTownCritter(npc.Type))
            {
                // 夜晚 / 恶劣天气归家（TownCritter 恒跳过 —— 只有 303 会进入）
                if (myTileX == floorX && myTileY == floorY)
                {
                    if (npc.VelocityX > 0.1f) npc.VelocityX -= 0.1f;
                    else if (npc.VelocityX < -0.1f) npc.VelocityX += 0.1f;
                    else npc.VelocityX = 0f;
                }
                else
                {
                    npc.Direction = myTileX > floorX ? -1 : 1;
                    ai[0] = 1f;
                    ai[1] = 200f + NpcNextInt(0, 200);
                    ai[2] = 0f;
                    local[3] = 0f;
                }
            }
            else
            {
                if (flag7) npc.VelocityX *= 0.5f;
                if (npc.VelocityX > 0.1f) npc.VelocityX -= 0.1f;
                else if (npc.VelocityX < -0.1f) npc.VelocityX += 0.1f;
                else npc.VelocityX = 0f;

                if (ai[1] > 0f) ai[1] -= 1f;

                int probeX = (int)((npc.X + width / 2f + 15f * npc.Direction) / TileSize);
                int probeY = (int)((npc.Y + height - 16f) / TileSize);
                bool currentlyDrowning = npc.Wet && !flag11;
                NpcGetWalkPrediction(npc, flag11, currentlyDrowning, myTileX, floorX, probeX, probeY,
                    out bool _, out bool avoidFalling);

                if (npc.Wet && !flag11 && NpcDrowning(npc.X, npc.Y, width, height, 1f))
                {
                    ai[0] = 1f;
                    ai[1] = 200f + NpcNextInt(0, 300);
                    ai[2] = 0f;
                    if (NpcIsTownCritter(npc.Type)) ai[1] += NpcNextInt(200, 400);
                    local[3] = 0f;
                }

                if (ai[1] <= 0f)
                {
                    if (!avoidFalling)
                    {
                        ai[0] = 1f;
                        ai[1] = 200f + NpcNextInt(0, 300);
                        ai[2] = 0f;
                        if (NpcIsTownCritter(npc.Type)) ai[1] += NpcNextInt(200, 400);
                        local[3] = 0f;
                    }
                    else
                    {
                        npc.Direction *= -1;
                        ai[1] = 60f + NpcNextInt(0, 120);
                    }
                }
            }

            // 原版 64090-64114：拴绳（离「家」太远就回头）+ 偶发掉头
            if (!flag || NpcIsInAGoodRestingSpot(npc, myTileX, myTileY, floorX, floorY))
            {
                if (myTileX < floorX - 25 || myTileX > floorX + 25)
                {
                    if (local[3] == 0f)
                    {
                        if (myTileX < floorX - 50 && npc.Direction == -1) npc.Direction = 1;
                        else if (myTileX > floorX + 50 && npc.Direction == 1) npc.Direction = -1;
                    }
                }
                else if (NpcNextInt(0, 80) == 0 && local[3] == 0f)
                {
                    local[3] = 200f;
                    npc.Direction *= -1;
                }
            }

            return;
        }

        // ---- 原版 64116-64595：行走（ai[0] == 1）----
        if (ai[0] == 1f)
        {
            if (flag && NpcIsInAGoodRestingSpot(npc, myTileX, myTileY, floorX, floorY) && !NpcIsTownCritter(npc.Type))
            {
                ai[0] = 0f;
                ai[1] = 200f + NpcNextInt(0, 200);
                local[3] = 60f;
                return;
            }

            if (!flag21)
            {
                // 原版 64130-64140：离家 35 格以上且朝家走 → 加速消耗计时（尽快结束行走）
                if (!npc.Homeless && (myTileX < floorX - 35 || myTileX > floorX + 35))
                {
                    if (npc.X < floorX * TileSize && npc.Direction == -1) ai[1] -= 5f;
                    else if (npc.X > floorX * TileSize && npc.Direction == 1) ai[1] -= 5f;
                }
                ai[1] -= 1f;
            }

            if (ai[1] <= 0f)
            {
                ai[0] = 0f;
                ai[1] = 300f + NpcNextInt(0, 300);
                ai[2] = 0f;
                ai[1] += NpcIsTownCritter(npc.Type) ? -NpcNextInt(0, 100) : NpcNextInt(0, 900);
                local[3] = 60f;
                return;
            }

            // 走速 / 加速度（原版 64191-64255）
            float walkMax = 1f;
            float walkAccel = 0.07f;
            if (npc.Type is 299 or 539 or 538 or (>= 639 and <= 645)) walkMax = 1.5f;
            else if (flag8) { walkMax = npc.Wet ? 2f : 0.5f; walkAccel = npc.Wet ? 1f : 0.07f; }
            if (npc.Type == 625) { walkMax = npc.Wet ? 2.5f : 0.2f; walkAccel = npc.Wet ? 1f : 0.07f; }
            if (flag7) { walkMax = 2f; walkAccel = 1f; }
            if (NpcFriendly(npc) && (flag16 | flag21))
            {
                walkMax = 1.5f + (1f - npc.Life / (float)npc.LifeMax) * 0.9f;
                walkAccel = 0.1f;
            }

            if (flag9 && npc.Wet)
            {
                if (MathF.Abs(npc.VelocityX) < 0.05f && MathF.Abs(npc.VelocityY) < 0.05f)
                    npc.VelocityX += walkMax * 10f * npc.Direction;
                else
                    npc.VelocityX *= 0.9f;
            }
            else if (npc.VelocityX < -walkMax || npc.VelocityX > walkMax)
            {
                if (npc.VelocityY == 0f) npc.VelocityX *= 0.8f;
            }
            else if (npc.VelocityX < walkMax && npc.Direction == 1)
            {
                npc.VelocityX = MathF.Min(walkMax, npc.VelocityX + walkAccel);
            }
            else if (npc.VelocityX > -walkMax && npc.Direction == -1)
            {
                // 原版 64271-64277 此处写作 `if (velocity.X > num22) velocity.X = num22;`（方向写反），
                // 移植时修正为对称钳制，否则左行速度会越过 -num22 无限增长。
                npc.VelocityX = MathF.Max(-walkMax, npc.VelocityX - walkAccel);
            }

            if (npc.VelocityY != 0f) return;

            // ---- 原版 64295-64572：贴地障碍探测 + 三档起跳 ----
            int probeCol = (int)((npc.X + width / 2f + 15f * npc.Direction) / TileSize);
            int probeRow = (int)((npc.Y + height - 16f) / TileSize);
            NpcGetWalkPrediction(npc, flag11, flag21, myTileX, floorX, probeCol, probeRow,
                out bool keepwalking, out bool avoidFalling);

            bool flag23 = false;
            if (avoidFalling && !flag23)
            {
                int centerCol = (int)((npc.X + width / 2f) / TileSize);
                int solidCount = 0;
                for (int offset = -1; offset <= 1; offset++)
                    if (NpcTileSolid(centerCol + offset, probeRow + 1)) solidCount++;

                if (solidCount <= 2)
                {
                    keepwalking = false;
                    avoidFalling = false;
                    ai[0] = 0f;
                    ai[1] = 50f + NpcNextInt(0, 50);
                    ai[2] = 0f;
                    local[3] = 40f;
                    return;
                }
            }

            // 原地卡住 → 掉头（原版 64347）
            if (npc.X == local[3] && !flag23)
            {
                npc.Direction *= -1;
                local[3] = 180f;
                return;
            }

            if (flag21 && !flag23)
            {
                if (local[3] > 180f) local[3] = 180f;
                if (local[3] > 0f) local[3] -= 1f;
            }
            else
            {
                local[3] = -1f;
            }

            bool flag25 = height / 16 < 3;
            bool flag26 = false;   // 需要起跳
            bool flag27 = false;   // 需要掉头（前方有 2 格以上墙，且无绕过空间）

            if ((npc.VelocityX < 0f && npc.Direction == -1) || (npc.VelocityX > 0f && npc.Direction == 1))
            {
                bool wall2 = NpcTileSolid(probeCol, probeRow - 2)
                    && (!flag25 || NpcTileSolid(probeCol, probeRow - 1));
                if (wall2)
                {
                    if (!NpcSolidInRect(probeCol - npc.Direction * 2, probeCol - npc.Direction, probeRow - 5, probeRow - 1)
                        && !NpcSolidInRect(probeCol, probeCol, probeRow - 5, probeRow - 3))
                    {
                        npc.VelocityY = -6f;
                    }
                    else if (flag7 && NpcTileSolid((int)(myCenterX / TileSize) + npc.Direction, (int)(myCenterY / TileSize)))
                    {
                        npc.Direction *= -1;
                        npc.VelocityX = 0f;
                    }
                    else
                    {
                        flag27 = true;
                    }
                }
                else if (NpcTileSolid(probeCol, probeRow - 1))
                {
                    if (!NpcSolidInRect(probeCol - npc.Direction * 2, probeCol - npc.Direction, probeRow - 4, probeRow - 1)
                        && !NpcSolidInRect(probeCol, probeCol, probeRow - 4, probeRow - 2))
                    {
                        npc.VelocityY = -5f;
                    }
                    else
                    {
                        flag26 = true;
                    }
                }
                else if (npc.Y + height - probeRow * TileSize > 20f && NpcTileSolid(probeCol, probeRow))
                {
                    if (!NpcSolidInRect(probeCol - npc.Direction * 2, probeCol, probeRow - 3, probeRow - 1))
                    {
                        npc.VelocityY = -4.4f;
                    }
                    else
                    {
                        flag26 = true;
                    }
                }
            }

            if (avoidFalling) flag26 = true;

            if (flag27)
            {
                keepwalking = false;
                npc.VelocityX = 0f;
                ai[0] = 8f;
                ai[1] = 240f;
            }
            else if (flag26)
            {
                npc.Direction *= -1;
                npc.VelocityX *= -1f;
            }

            if (keepwalking) ai[1] = 90f;

            if (npc.VelocityY < 0f)
            {
                local[3] = npc.X;
                if (npc.Wet) npc.VelocityY *= 1.2f;
                else if (NpcIsTownCritter(npc.Type) && !flag7) npc.VelocityY *= 1.2f;
            }
        }
    }

    /// <summary>原版 <c>Collision.CanHit</c> 的简化版：两点之间是否有实心格阻挡（按 8px 步长采样）。</summary>
    private bool NpcHasLineOfSight(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0.001f) return true;

        int steps = (int)(len / 8f) + 1;
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            if (NpcTileSolid((int)((x1 + dx * t) / TileSize), (int)((y1 + dy * t) / TileSize))) return false;
        }
        return true;
    }

    /// <summary>原版 <c>Rectangle(position - 100, width + 200, height + 200).Intersects(玩家碰撞盒)</c>。</summary>
    private static bool NpcPlayerInAlertBox(WorldNpc npc, int width, int height, PlayerRuntime player)
    {
        float left = npc.X - 100f, right = npc.X + width + 100f;
        float top = npc.Y - 100f, bottom = npc.Y + height + 100f;
        float pLeft = player.AimPosition.X, pRight = pLeft + NpcSizes.PlayerWidth;
        float pTop = player.AimPosition.Y, pBottom = pTop + NpcSizes.PlayerHeight;
        return right > pLeft && left < pRight && bottom > pTop && top < pBottom;
    }

    // ========================================================================
    // aiStyle 16：鱼（原版 aiStyle == 16 分支，28607-29136）
    // ========================================================================

    /// <summary>
    /// 原版 aiStyle 16（55 金鱼 / 592 / 607 / 615 / 688）：水中游动（`wet` 分支）——
    /// 撞墙反射、`ai[0]` 上下摆动、撞顶 / 撞底赋值、头部有水且有实体 → 下潜；
    /// 离开水则贴地弹跳（随机初速 + 重力）。速度上限 3 / 2（615 的横向上限为 3）。
    /// 未移植：斜坡转向（`topSlope`）、688 受击跳跃（需 <c>justHit</c>）、157/65/102/692 专属数值。
    /// </summary>
    private void Ai016Fishes(WorldNpc npc)
    {
        var ai = npc.Ai;
        var (width, height) = NpcSizes.Of(npc.Type);
        float centerX = npc.X + width / 2f;
        float centerY = npc.Y + height / 2f;
        var target = NearestPlayer(centerX, centerY);

        if (npc.Direction == 0) AimAtTarget(npc, target, width);

        if (npc.Wet)
        {
            if (npc.CollideX)
            {
                npc.VelocityX *= -1f;
                npc.Direction *= -1;
            }
            if (npc.CollideY)
            {
                if (npc.VelocityY > 0f)
                {
                    npc.VelocityY = -MathF.Abs(npc.VelocityY);
                    npc.DirectionY = -1;
                    ai[0] = -1f;
                }
                else if (npc.VelocityY < 0f)
                {
                    npc.VelocityY = MathF.Abs(npc.VelocityY);
                    npc.DirectionY = 1;
                    ai[0] = 1f;
                }
            }

            AimAtTarget(npc, target, width);

            if (ai[0] == 0f) ai[0] = 1f;

            npc.VelocityX += npc.Direction * 0.1f;
            float maxSwimX = npc.Type == 615 ? 3f : 1f;
            if (npc.VelocityX < -maxSwimX || npc.VelocityX > maxSwimX) npc.VelocityX *= 0.95f;

            if (ai[0] == -1f)
            {
                npc.VelocityY -= 0.01f;
                if (npc.VelocityY < -0.3f) ai[0] = 1f;
            }
            else
            {
                npc.VelocityY += 0.01f;
                if (npc.VelocityY > 0.3f) ai[0] = -1f;
            }

            // 原版 29047-29071：头部所在格有水（>128）且前方 / 下方有实体 → 下潜
            int tileX = (int)(npc.X + width / 2f) / (int)TileSize;
            int tileY = (int)(npc.Y + height / 2f) / (int)TileSize;
            if (NpcLiquidAt(tileX, tileY - 1) > 128
                && (NpcTileSolid(tileX, tileY + 1) || NpcTileSolid(tileX, tileY + 2)))
                ai[0] = -1f;

            if (npc.VelocityY > 0.4f || npc.VelocityY < -0.4f) npc.VelocityY *= 0.95f;
            return;
        }

        // 原版 29078-29104：离开水 → 贴地弹跳
        if (npc.VelocityY == 0f)
        {
            npc.VelocityY = NpcNextInt(-50, -20) * 0.1f;
            npc.VelocityX = NpcNextInt(-20, 20) * 0.1f;
            npc.Direction = NpcNextInt(0, 2) == 0 ? 1 : -1;
        }

        npc.VelocityY += 0.3f;
        if (npc.VelocityY > 10f) npc.VelocityY = 10f;
        ai[0] = 1f;
    }

    // ========================================================================
    // aiStyle 24：鸟（原版 aiStyle == 24 分支，30476-30703）
    // ========================================================================

    /// <summary>
    /// 原版 aiStyle 24（74 鸟 / 297 / 298 / 442 / 611/689 海鸥 / 671-675）：
    /// `ai[0] == 0` 贴地待飞（被扰动或玩家进入 ±100 矩形时起飞）；`ai[0] == 1` 空中游走
    /// （撞墙 / 撞顶反弹、朝 direction 收敛至 ±3（671-675 为 ±4）、前方 15 格地形探测调整高度）；
    /// 进水则上浮并重新选目标。
    /// 未移植：海鸥夜间降落到城镇 NPC 头顶（ai[0] == 2）、鸟粪弹幕、689 夜间变形。
    /// </summary>
    private void Ai024Birds(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);
        var target = NearestPlayer(npc.X + width / 2f, npc.Y + height / 2f);

        npc.NoGravity = true;
        bool isSeagull = npc.Type is 611 or 689;

        if (isSeagull && local[0] == 0f)
        {
            // 原版 30499：海鸥出生即起飞
            local[0] = 1f;
            AimAtTarget(npc, target, width);
            ai[0] = 1f;
        }

        if (ai[0] == 0f)
        {
            npc.NoGravity = false;
            AimAtTarget(npc, target, width);

            if (npc.VelocityX != 0f || npc.VelocityY < 0f || npc.VelocityY > 0.3f)
            {
                ai[0] = 1f;
                npc.Direction = -npc.Direction;
            }
            else if (!isSeagull && target is not null
                     && (NpcPlayerInAlertBox(npc, width, height, target) || npc.Life < npc.LifeMax))
            {
                ai[0] = 1f;
                npc.VelocityY -= 6f;
                npc.Direction = -npc.Direction;
            }
        }
        else if (ai[0] == 2f)
        {
            // 原版 30565：降落（海鸥夜间落在城镇 NPC 头顶）
            npc.VelocityX *= 0.98f;
            if (npc.VelocityY == 0f)
            {
                ai[0] = 0f;
                npc.VelocityX = 0f;
            }
            npc.VelocityY += 0.05f;
            if (npc.VelocityY > 2f) npc.VelocityY = 2f;
        }
        else if (target is not null && !target.Dead)
        {
            float maxSpeed = npc.Type is >= 671 and <= 675 ? 4f : 3f;

            if (npc.CollideX)
            {
                npc.Direction *= -1;
                npc.VelocityX = npc.PrevVelocityX * -0.5f;
                if (npc.Direction == -1 && npc.VelocityX > 0f && npc.VelocityX < maxSpeed - 1f) npc.VelocityX = maxSpeed - 1f;
                if (npc.Direction == 1 && npc.VelocityX < 0f && npc.VelocityX > -maxSpeed + 1f) npc.VelocityX = -maxSpeed + 1f;
            }
            if (npc.CollideY)
            {
                npc.VelocityY = npc.PrevVelocityY * -0.5f;
                if (npc.VelocityY > 0f && npc.VelocityY < 1f) npc.VelocityY = 1f;
                if (npc.VelocityY < 0f && npc.VelocityY > -1f) npc.VelocityY = -1f;
            }

            if (npc.Direction == -1 && npc.VelocityX > -maxSpeed)
            {
                npc.VelocityX -= 0.1f;
                if (npc.VelocityX > maxSpeed) npc.VelocityX -= 0.1f;
                else if (npc.VelocityX > 0f) npc.VelocityX -= 0.05f;
                if (npc.VelocityX < -maxSpeed) npc.VelocityX = -maxSpeed;
            }
            else if (npc.Direction == 1 && npc.VelocityX < maxSpeed)
            {
                npc.VelocityX += 0.1f;
                if (npc.VelocityX < -maxSpeed) npc.VelocityX += 0.1f;
                else if (npc.VelocityX < 0f) npc.VelocityX += 0.05f;
                if (npc.VelocityX > maxSpeed) npc.VelocityX = maxSpeed;
            }

            // 原版 30643-30687：前方 15 格地形探测（决定爬升 / 下降）
            int probeCol = (int)((npc.X + width / 2f) / TileSize) + npc.Direction;
            int probeRow = (int)((npc.Y + height) / TileSize);
            bool clear = true;
            bool blockedNear = false;

            for (int r = probeRow; r < probeRow + 15; r++)
            {
                if (r < 0 || r >= _world.Tiles.Height || probeCol < 0 || probeCol >= _world.Tiles.Width) continue;
                if (!NpcTileSolid(probeCol, r) && NpcLiquidAt(probeCol, r) == 0) continue;

                if (r < probeRow + 5) blockedNear = true;
                clear = false;
                break;
            }

            if (clear) npc.VelocityY += 0.05f;
            else npc.VelocityY -= 0.1f;
            if (blockedNear) npc.VelocityY -= 0.2f;
            npc.VelocityY = Math.Clamp(npc.VelocityY, -4f, 2f);
        }

        if (npc.Wet)
        {
            ai[1] = 0f;
            if (npc.VelocityY > 0f) npc.VelocityY *= 0.95f;
            npc.VelocityY -= 0.5f;
            if (npc.VelocityY < -4f) npc.VelocityY = -4f;
            AimAtTarget(npc, target, width);
        }
    }

    // ========================================================================
    // aiStyle 64：萤火虫（原版 aiStyle == 64 分支，39556-39785）
    // ========================================================================

    /// <summary>
    /// 原版 aiStyle 64（355 萤火虫 / 358 荧光虫 / 654 / 677）：
    /// 每隔 60-180 帧重选一个「目标速度」（远离玩家 &gt;700px 时朝玩家方向飞、否则在原地小范围游荡），
    /// 用 1/80 的指数平滑逼近；下方 4 格有实体 / 液体则反弹上抬，上方 30 格全空则下压。
    /// </summary>
    private void Ai064Fireflies(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);
        npc.NoGravity = true;

        float centerX = npc.X + width / 2f;
        float centerY = npc.Y + height / 2f;
        var target = NearestPlayer(centerX, centerY);

        float wantedX = ai[0];
        float wantedY = ai[1];

        if (ai[3] == 0f) ai[3] = NpcNextInt(75, 111) * 0.01f;

        if (--local[0] <= 0f)
        {
            AimAtTarget(npc, target, width);
            local[0] = NpcNextInt(60, 180);

            float targetCenterX = target is null ? centerX : target.AimPosition.X + NpcSizes.PlayerWidth / 2f;
            float dx = MathF.Abs(centerX - targetCenterX);

            if (target is not null && dx > 700f && local[3] == 0f)
            {
                float speed = NpcNextInt(50, 151) * 0.01f;
                if (dx > 1000f) speed = NpcNextInt(150, 201) * 0.01f;
                else if (dx > 850f) speed = NpcNextInt(100, 151) * 0.01f;

                float vx = npc.Direction * NpcNextInt(100, 251);
                float vy = NpcNextInt(-50, 51);
                if (npc.Y > target.AimPosition.Y - 100f) vy -= NpcNextInt(100, 251);

                float inv = speed / MathF.Sqrt(vx * vx + vy * vy);
                wantedX = vx * inv;
                wantedY = vy * inv;
            }
            else
            {
                local[3] = 1f;
                float speed = NpcNextInt(5, 151) * 0.01f;
                float vx = NpcNextInt(-100, 101);
                float vy = NpcNextInt(-100, 101);
                float inv = speed / MathF.Sqrt(vx * vx + vy * vy);
                wantedX = vx * inv;
                wantedY = vy * inv;
            }
        }

        // 原版 39607-39690：677（边界 / 威胁规避）
        if (npc.Type == 677)
        {
            int tx = (int)(centerX / TileSize);
            int ty = (int)(centerY / TileSize);
            const int border = 40;
            bool interior = true;

            if (tx < border) { wantedX += 0.5f; if (wantedX > 3f) wantedX = 3f; interior = false; }
            else if (tx > _world.MaxTilesX - border) { wantedX -= 0.5f; if (wantedX < -3f) wantedX = -3f; interior = false; }
            if (ty < border) { wantedY += 0.5f; if (wantedY > 3f) npc.VelocityY = 3f; interior = false; }
            else if (ty > _world.MaxTilesY - border) { wantedY -= 0.5f; if (wantedY < -3f) wantedY = -3f; interior = false; }

            if (local[1] > 0f)
            {
                local[1] -= 1f;
            }
            else if (interior)
            {
                local[1] = 15f;
                float count = 0f;
                float awayX = 0f, awayY = 0f;

                foreach (var other in _world.Npcs)
                {
                    if (ReferenceEquals(other, npc) || !other.Active) continue;
                    if (NpcCritterSet.Is(other.Type) || other.IsTownNpc || other.Damage <= 0) continue;

                    var (ow, oh) = NpcSizes.Of(other.Type);
                    float ox = other.X + ow / 2f;
                    float oy = other.Y + oh / 2f;
                    float ddx = ox - centerX;
                    float ddy = oy - centerY;
                    if (ddx * ddx + ddy * ddy > 100f * 100f) continue;

                    count++;
                    float len = MathF.Sqrt(ddx * ddx + ddy * ddy);
                    if (len > 0.001f) { awayX += -ddx / len; awayY += -ddy / len; }
                }

                foreach (var player in _world.Players.Values)
                {
                    if (!player.Active) continue;
                    float px = player.AimPosition.X + NpcSizes.PlayerWidth / 2f;
                    float py = player.AimPosition.Y + NpcSizes.PlayerHeight / 2f;
                    float ddx = px - centerX;
                    float ddy = py - centerY;
                    if (ddx * ddx + ddy * ddy > 150f * 150f) continue;

                    count++;
                    float len = MathF.Sqrt(ddx * ddx + ddy * ddy);
                    if (len > 0.001f) { awayX += -ddx / len; awayY += -ddy / len; }
                }

                if (count > 0f)
                {
                    awayX = awayX / count * 2f;
                    awayY = awayY / count * 2f;
                    npc.VelocityX += awayX;
                    npc.VelocityY += awayY;

                    float len = MathF.Sqrt(npc.VelocityX * npc.VelocityX + npc.VelocityY * npc.VelocityY);
                    if (len > 8f)
                    {
                        npc.VelocityX = npc.VelocityX / len * 8f;
                        npc.VelocityY = npc.VelocityY / len * 8f;
                    }

                    local[0] = 10f;
                }
            }
        }

        // 原版 39724-39726：1/80 指数平滑逼近目标速度
        npc.VelocityX = (npc.VelocityX * 79f + wantedX) / 80f;
        npc.VelocityY = (npc.VelocityY * 79f + wantedY) / 80f;

        // 原版 39727-39743：下方 4 格有实体 / 液体 → 反弹
        if (npc.VelocityY > 0f)
        {
            int tx = (int)(centerX / TileSize);
            int ty = (int)(centerY / TileSize);
            for (int r = ty; r < ty + 4; r++)
            {
                if (r < 0 || r >= _world.Tiles.Height || tx < 0 || tx >= _world.Tiles.Width) continue;
                if (!NpcTileSolid(tx, r) && NpcLiquidAt(tx, r) == 0) continue;

                wantedY *= -1f;
                if (npc.VelocityY > 0f) npc.VelocityY *= 0.9f;
                break;
            }
        }

        // 原版 39744-39765：上方 30 格全空 → 下压
        if (npc.VelocityY < 0f)
        {
            int tx = (int)(centerX / TileSize);
            int ty = (int)(centerY / TileSize);
            bool anySolid = false;
            for (int r = ty; r < ty + 30; r++)
            {
                if (r < 0 || r >= _world.Tiles.Height || tx < 0 || tx >= _world.Tiles.Width) continue;
                if (!NpcTileSolid(tx, r)) continue;
                anySolid = true;
                break;
            }

            if (!anySolid)
            {
                wantedY *= -1f;
                if (npc.VelocityY < 0f) npc.VelocityY *= 0.9f;
            }
        }

        if (npc.CollideX)
        {
            wantedX = npc.VelocityX < 0f ? MathF.Abs(wantedX) : -MathF.Abs(wantedX);
            npc.VelocityX *= -0.2f;
        }

        if (npc.VelocityX < 0f) npc.Direction = -1;
        if (npc.VelocityX > 0f) npc.Direction = 1;

        ai[0] = wantedX;
        ai[1] = wantedY;
    }

    // ========================================================================
    // aiStyle 65：蝴蝶（原版 AI_065_Butterflies，52272-52588）
    // ========================================================================

    /// <summary>
    /// 原版 AI_065_Butterflies（356 蝴蝶 / 444 / 653 / 661）：与萤火虫同构但节奏更慢
    /// （60 帧平滑、目标速度 3）、下方 3 格 / 上方 30 格探测、撞墙时把目标速度翻向。
    /// 未移植：661 金蝴蝶的「离开神圣之地后消散」（客户端可见性 / 掉落逻辑）、被敌对 NPC 与玩家
    /// 挤压时的反向加速（依赖 <c>NPC.Hitbox.Distance</c> 与 <c>damage</c> 组合，收益低）。
    /// </summary>
    private void Ai065Butterflies(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);
        npc.NoGravity = true;

        float centerX = npc.X + width / 2f;
        float centerY = npc.Y + height / 2f;
        var target = NearestPlayer(centerX, centerY);

        float wantedX = ai[0];
        float wantedY = ai[1];

        if (ai[3] == 0f) ai[3] = NpcNextInt(75, 111) * 0.01f;

        if (--local[0] <= 0f)
        {
            AimAtTarget(npc, target, width);
            local[0] = NpcNextInt(90, 240);

            float targetCenterX = target is null ? centerX : target.AimPosition.X + NpcSizes.PlayerWidth / 2f;
            float dx = MathF.Abs(centerX - targetCenterX);

            if (target is not null && dx > 700f && local[3] == 0f)
            {
                float speed = NpcNextInt(50, 151) * 0.01f;
                if (dx > 1000f) speed = NpcNextInt(150, 201) * 0.01f;
                else if (dx > 850f) speed = NpcNextInt(100, 151) * 0.01f;

                float vx = npc.Direction * NpcNextInt(100, 251);
                float vy = NpcNextInt(-50, 51);
                if (npc.Y > target.AimPosition.Y - 100f) vy -= NpcNextInt(100, 251);

                float inv = speed / MathF.Sqrt(vx * vx + vy * vy);
                wantedX = vx * inv;
                wantedY = vy * inv;
            }
            else
            {
                local[3] = 1f;
                float speed = NpcNextInt(26, 301) * 0.01f;
                float vx = NpcNextInt(-100, 101);
                float vy = NpcNextInt(-100, 101);
                float inv = speed / MathF.Sqrt(vx * vx + vy * vy);
                wantedX = vx * inv;
                wantedY = vy * inv;
            }
        }

        // 原版 52478-52480：1/60 指数平滑
        npc.VelocityX = (npc.VelocityX * 59f + wantedX) / 60f;
        npc.VelocityY = (npc.VelocityY * 59f + wantedY) / 60f;

        // 原版 52481-52497：下方 3 格有实体 / 液体 → 反弹
        if (npc.VelocityY > 0f)
        {
            int tx = (int)(centerX / TileSize);
            int ty = (int)(centerY / TileSize);
            for (int r = ty; r < ty + 3; r++)
            {
                if (r < 0 || r >= _world.Tiles.Height || tx < 0 || tx >= _world.Tiles.Width) continue;
                if (!NpcTileSolid(tx, r) && NpcLiquidAt(tx, r) == 0) continue;

                wantedY *= -1f;
                if (npc.VelocityY > 0f) npc.VelocityY *= 0.9f;
                break;
            }
        }

        // 原版 52498-52519：上方 30 格全空 → 下压
        if (npc.VelocityY < 0f)
        {
            int tx = (int)(centerX / TileSize);
            int ty = (int)(centerY / TileSize);
            bool anySolid = false;
            for (int r = ty; r < ty + 30; r++)
            {
                if (r < 0 || r >= _world.Tiles.Height || tx < 0 || tx >= _world.Tiles.Width) continue;
                if (!NpcTileSolid(tx, r)) continue;
                anySolid = true;
                break;
            }

            if (!anySolid)
            {
                wantedY *= -1f;
                if (npc.VelocityY < 0f) npc.VelocityY *= 0.9f;
            }
        }

        if (npc.CollideX)
        {
            wantedX = npc.VelocityX < 0f ? MathF.Abs(wantedX) : -MathF.Abs(wantedX);
            npc.VelocityX *= -0.2f;
        }

        if (npc.VelocityX < 0f) npc.Direction = -1;
        if (npc.VelocityX > 0f) npc.Direction = 1;

        ai[0] = wantedX;
        ai[1] = wantedY;
    }

    // ========================================================================
    // aiStyle 66：蚯蚓（原版 aiStyle == 66 分支，39790-39885）
    // ========================================================================

    /// <summary>
    /// 原版 aiStyle 66（357 蚯蚓 / 448 / 374 / 484-487 / 606）：贴地时按 <c>ai[0]</c> 在
    /// 「爬行（velocity.X = speed * direction）」与「静止」之间切换（切换周期 300-900 / 600-1800 帧），
    /// 撞墙掉头；离地时补 direction。374（岩浆蚯蚓）另有「玩家 160px 内累计 90 帧 → 变形 375」，
    /// 服务端只累计不变形。
    /// </summary>
    private void Ai066Worms(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);

        if (npc.VelocityY == 0f)
        {
            if (ai[0] == 1f)
            {
                if (npc.Direction == 0)
                    AimAtTarget(npc, NearestPlayer(npc.X + width / 2f, npc.Y + height / 2f), width);

                if (npc.CollideX) npc.Direction *= -1;

                float speed = npc.Type switch
                {
                    485 => 0.25f,
                    486 => 0.325f,
                    487 => 0.4f,
                    _ => 0.2f,
                };

                npc.VelocityX = speed * npc.Direction;
                if (npc.Type == 374) npc.VelocityX *= 3f;
            }
            else
            {
                npc.VelocityX = 0f;
            }

            local[1] -= 1f;
            if (local[1] <= 0f)
            {
                if (ai[0] == 1f)
                {
                    ai[0] = 0f;
                    local[1] = NpcNextInt(300, 900);
                }
                else
                {
                    ai[0] = 1f;
                    local[1] = NpcNextInt(600, 1800);
                }
            }
        }
        else if (npc.Direction == 0)
        {
            npc.Direction = npc.VelocityX < 0f ? -1 : 1;
        }

        if (npc.Type != 374) return;

        // 原版 39865-39884：374 受惊计数（累计到 90 → 变形为 375；服务端不执行变形）
        bool playerNear = false;
        foreach (var player in _world.Players.Values)
        {
            if (!player.Active || player.Dead) continue;
            float dx = player.AimPosition.X + NpcSizes.PlayerWidth / 2f - (npc.X + width / 2f);
            float dy = player.AimPosition.Y + NpcSizes.PlayerHeight / 2f - (npc.Y + height / 2f);
            if (dx * dx + dy * dy <= 160f * 160f)
            {
                playerNear = true;
                break;
            }
        }

        if (playerNear && ai[1] < 90f) ai[1] += 1f;
    }

    // ========================================================================
    // aiStyle 67：蜗牛（原版 aiStyle == 67 分支，39886-40160）
    // ========================================================================

    /// <summary>
    /// 原版 aiStyle 67（359 蜗牛 / 360/655 荧光蜗牛）：两态切换 ——
    /// <c>ai[2] &gt; 0</c> 时「贴地爬行」（velocity.X = 0.3/0.6 × direction，撞顶则转入爬墙）；
    /// <c>ai[2] == 0</c> 时「爬墙状态机」（noGravity = true，靠 collideX / collideY 在墙面与地面间转向，
    /// 速度 = 0.3/0.6 × (direction, directionY)）。长时间不碰撞也会主动转入爬行。
    /// 未移植：359 的缩放动画（ai[3] / scale，纯客户端）、微光传送（GetShimmered）。
    /// </summary>
    private void Ai067Snails(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);
        float speed = npc.Type is 360 or 655 ? 0.6f : 0.3f;

        if (ai[0] == 0f)
        {
            AimAtTarget(npc, NearestPlayer(npc.X + width / 2f, npc.Y + height / 2f), width);
            npc.DirectionY = 1;
            ai[0] = 1f;
        }

        // 原版 39893-39898 的微光传送（GetShimmered）略去：我们的世界里不存在微光液体。

        if (ai[2] == 0f && NpcNextInt(0, 7200) == 0) ai[2] = 2f;

        if (!npc.CollideX && !npc.CollideY)
        {
            if (++local[3] > 5f) ai[2] = 2f;
        }
        else
        {
            local[3] = 0f;
        }

        if (ai[2] > 0f)
        {
            // ---- 贴地爬行 ----
            ai[1] = 0f;
            ai[0] = 1f;
            npc.DirectionY = 1;
            npc.VelocityX = speed * npc.Direction;
            npc.NoGravity = false;

            if (npc.CollideY) ai[2] -= 1f;

            if (npc.CollideX && npc.VelocityY == 0f)
            {
                ai[2] = 0f;
                npc.DirectionY = -1;
                ai[1] = 1f;
            }

            // 原版 39946-39990：同一 X 上连续卡住 → 强制掉头
            if (npc.VelocityY == 0f)
            {
                if (local[1] == npc.X)
                {
                    if (++local[2] > 10f)
                    {
                        npc.Direction = 1;
                        npc.VelocityX = npc.Direction * speed;
                        local[2] = 0f;
                    }
                }
                else
                {
                    local[1] = npc.X;
                    local[2] = 0f;
                }
            }
            else
            {
                local[1] = npc.X;
            }
        }

        if (ai[2] != 0f) return;

        // ---- 爬墙状态机（原版 39990-40158）----
        npc.NoGravity = true;

        if (ai[1] == 0f)
        {
            if (npc.CollideY) ai[0] = 2f;
            else if (ai[0] == 2f)
            {
                npc.Direction = -npc.Direction;
                ai[1] = 1f;
                ai[0] = 1f;
            }

            if (npc.CollideX)
            {
                npc.DirectionY = -npc.DirectionY;
                ai[1] = 1f;
            }
        }
        else
        {
            if (npc.CollideX) ai[0] = 2f;
            else if (ai[0] == 2f)
            {
                npc.DirectionY = -npc.DirectionY;
                ai[1] = 0f;
                ai[0] = 1f;
            }

            if (npc.CollideY)
            {
                npc.Direction = -npc.Direction;
                ai[1] = 0f;
            }
        }

        npc.VelocityX = speed * npc.Direction;
        npc.VelocityY = speed * npc.DirectionY;
    }

    // ========================================================================
    // aiStyle 68：鸭（原版 aiStyle == 68 分支，40161-40424）
    // ========================================================================

    /// <summary>
    /// 原版 aiStyle 68（363 鸭 / 365 / 603 / 609）：陆地形态贴地游走；遇水则切换为「游水」——
    /// 沿水面以 <c>(velocity.X * 19 + 2 * direction) / 20</c> 前进，前方有实体 / 无水则掉头，
    /// 并用内联液面高度把身体压在水面下 6px；离水 / 玩家靠近或受伤则起飞（<c>ai[0] = 1</c>）。
    /// 飞行 300 帧后落地回到陆地形态（Type -= 1，交由 aiStyle 7 接管）。
    /// </summary>
    private void Ai068Ducks(WorldNpc npc)
    {
        var ai = npc.Ai;
        var (width, height) = NpcSizes.Of(npc.Type);
        var target = NearestPlayer(npc.X + width / 2f, npc.Y + height / 2f);

        npc.NoGravity = true;

        if (ai[0] == 0f)
        {
            npc.NoGravity = false;

            int previousDirection = npc.Direction;
            AimAtTarget(npc, target, width);
            if (target is not null && previousDirection != 0) npc.Direction = previousDirection;

            if (npc.Wet)
            {
                int aheadX = (int)((npc.X + width / 2f + (width / 2f + 8f) * npc.Direction) / TileSize);
                if (aheadX >= 5 && aheadX < _world.MaxTilesX - 5)
                {
                    int centerRow = (int)((npc.Y + height / 2f) / TileSize);
                    int topRow = (int)(npc.Y / TileSize);
                    int bottomRow = (int)((npc.Y + height) / TileSize);

                    npc.VelocityX = (npc.VelocityX * 19f + 2f * npc.Direction) / 20f;

                    if (NpcTileSolid(aheadX, centerRow) || NpcTileSolid(aheadX, topRow) || NpcTileSolid(aheadX, bottomRow)
                        || NpcLiquidAt(aheadX, bottomRow) == 0)
                        npc.Direction *= -1;

                    if (npc.VelocityY > 0f) npc.VelocityY *= 0.5f;
                    npc.NoGravity = true;

                    // 原版 40210-40256：内联液面（只取中心列的上 / 中 / 下三格）
                    int cx = (int)((npc.X + width / 2f) / TileSize);
                    int cy = (int)((npc.Y + height / 2f) / TileSize);
                    float waterY = npc.Y + height;

                    if (NpcLiquidAt(cx, cy - 1) > 0) waterY = cy * 16 - NpcLiquidAt(cx, cy - 1) / 16;
                    else if (NpcLiquidAt(cx, cy) > 0) waterY = (cy + 1) * 16 - NpcLiquidAt(cx, cy) / 16;
                    else if (NpcLiquidAt(cx, cy + 1) > 0) waterY = (cy + 2) * 16 - NpcLiquidAt(cx, cy + 1) / 16;

                    waterY -= 6f;
                    float bodyCenterY = npc.Y + height / 2f;

                    if (bodyCenterY > waterY)
                    {
                        npc.VelocityY -= 0.1f;
                        if (npc.VelocityY < -8f) npc.VelocityY = -8f;
                        if (bodyCenterY + npc.VelocityY < waterY) npc.VelocityY = waterY - bodyCenterY;
                    }
                    else
                    {
                        npc.VelocityY = waterY - bodyCenterY;
                    }
                }
            }

            if (!npc.Wet)
            {
                ai[0] = 1f;
                npc.Direction = -npc.Direction;
                return;
            }

            if (target is not null && (NpcPlayerInAlertBox(npc, width, height, target) || npc.Life < npc.LifeMax))
            {
                ai[0] = 1f;
                npc.VelocityY -= 6f;
                npc.Direction = -npc.Direction;
            }

            return;
        }

        // ---- 原版 40281-40423：飞行 / 游动 ----
        if (target is not null && target.Dead) return;

        ai[1] += 1f;
        bool shouldLand = ai[1] >= 300f;

        if (shouldLand)
        {
            if (npc.VelocityY == 0f || npc.CollideY || npc.Wet)
            {
                npc.VelocityX = 0f;
                npc.VelocityY = 0f;
                ai[0] = 0f;
                ai[1] = 0f;

                // 原版 40305：落地回到陆地形态（363→362 / 365→364 / 603→602 / 609→608），
                // `Transform(type - 1, 0f, 200 + rand(200))` —— 覆写 ai[1] 给下一段行走计时
                if (!npc.Wet && npc.Type is 363 or 365 or 603 or 609)
                    TransformNpc(npc, npc.Type - 1, 0f, 200f + NpcNextInt(0, 200));
            }
            else
            {
                npc.VelocityX *= 0.98f;
                npc.VelocityY += 0.1f;
                if (npc.VelocityY > 2f) npc.VelocityY = 2f;
            }

            return;
        }

        if (npc.CollideX)
        {
            npc.Direction *= -1;
            npc.VelocityX = npc.PrevVelocityX * -0.5f;
            if (npc.Direction == -1 && npc.VelocityX > 0f && npc.VelocityX < 2f) npc.VelocityX = 2f;
            if (npc.Direction == 1 && npc.VelocityX < 0f && npc.VelocityX > -2f) npc.VelocityX = -2f;
        }
        if (npc.CollideY)
        {
            npc.VelocityY = npc.PrevVelocityY * -0.5f;
            if (npc.VelocityY > 0f && npc.VelocityY < 1f) npc.VelocityY = 1f;
            if (npc.VelocityY < 0f && npc.VelocityY > -1f) npc.VelocityY = -1f;
        }

        if (npc.Direction == -1 && npc.VelocityX > -3f)
        {
            npc.VelocityX -= 0.1f;
            if (npc.VelocityX > 3f) npc.VelocityX -= 0.1f;
            else if (npc.VelocityX > 0f) npc.VelocityX -= 0.05f;
            if (npc.VelocityX < -3f) npc.VelocityX = -3f;
        }
        else if (npc.Direction == 1 && npc.VelocityX < 3f)
        {
            npc.VelocityX += 0.1f;
            if (npc.VelocityX < -3f) npc.VelocityX += 0.1f;
            else if (npc.VelocityX < 0f) npc.VelocityX += 0.05f;
            if (npc.VelocityX > 3f) npc.VelocityX = 3f;
        }

        // 原版 40378-40422：前方 15 格地形探测
        int probeCol = (int)((npc.X + width / 2f) / TileSize) + npc.Direction;
        int probeRow = (int)((npc.Y + height) / TileSize);
        bool clear = true;
        bool blockedNear = false;

        for (int r = probeRow; r < probeRow + 15; r++)
        {
            if (r < 0 || r >= _world.Tiles.Height || probeCol < 0 || probeCol >= _world.Tiles.Width) continue;
            if (!NpcTileSolid(probeCol, r) && NpcLiquidAt(probeCol, r) == 0) continue;

            if (r < probeRow + 5) blockedNear = true;
            clear = false;
            break;
        }

        if (clear) npc.VelocityY += 0.1f;
        else npc.VelocityY -= 0.1f;
        if (blockedNear) npc.VelocityY -= 0.2f;
        npc.VelocityY = Math.Clamp(npc.VelocityY, -4f, 3f);
    }

    // ========================================================================
    // aiStyle 112：仙灵（原版 AI_112_FairyCritter，56967-57700）
    // ========================================================================

    /// <summary>
    /// 原版 AI_112_FairyCritter 的**自然小动物两态**（583-585）：
    /// <c>ai[2] == 0</c> 家园悬停（在出生点 20px 范围内漂浮；玩家进入 250px 即转入逃离）；
    /// <c>ai[2] == 1</c> 逃离（朝远离玩家的方向加速至 ±4.5，前方 21×8 区域探测地形调整高度）。
    /// 未移植：<c>ai[2] &gt;= 2</c> 的「带路找宝箱」状态机（2/3/4/5/6/7，依赖
    /// <c>GetFairyTreasureCoords</c> 的宝藏地图数据，我们的世界模型没有该数据）。
    /// </summary>
    private void Ai112Fairies(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);
        npc.NoGravity = true;

        float centerX = npc.X + width / 2f;
        float centerY = npc.Y + height / 2f;
        var target = NearestPlayer(centerX, centerY);

        if (ai[2] > 1f) ai[2] = 1f;   // 宝藏状态（2-7）依赖宝藏点数据，降级为逃离态

        if (ai[2] == 0f)
        {
            npc.NoTileCollide = false;

            if (ai[0] == 0f && ai[1] == 0f)
            {
                ai[0] = centerX;
                ai[1] = centerY;
            }

            if (local[0] == 0f)
            {
                local[0] = 1f;
                npc.VelocityX = NpcNextFloat(2f, 4f) * (NpcNextInt(0, 2) * 2 - 1) * 0.7f;
                npc.VelocityY = NpcNextFloat(1f, 2f) * (NpcNextInt(0, 2) * 2 - 1) * 0.7f;
            }

            float homeDx = ai[0] - centerX;
            float homeDy = ai[1] - centerY;
            if (MathF.Sqrt(homeDx * homeDx + homeDy * homeDy) > 20f)
            {
                npc.VelocityX += (homeDx > 0f ? 1f : -1f) * 0.04f;
                npc.VelocityY += (homeDy > 0f ? 1f : -1f) * 0.04f;
                if (MathF.Abs(npc.VelocityY) > 2f) npc.VelocityY *= 0.95f;
            }

            AimAtTarget(npc, target, width);

            if (target is not null)
            {
                float dx = target.AimPosition.X + NpcSizes.PlayerWidth / 2f - centerX;
                float dy = target.AimPosition.Y + NpcSizes.PlayerHeight / 2f - centerY;
                if (!target.Dead && dx * dx + dy * dy < 250f * 250f)
                {
                    ai[2] = 1f;
                    npc.Direction = dx > 0f ? -1 : 1;
                    if (npc.VelocityX * npc.Direction < 0f) npc.VelocityX = npc.Direction * 2f;
                    ai[3] = 0f;
                }
            }

            return;
        }

        // ---- 原版 57243-57325：逃离态 ----
        npc.NoTileCollide = false;

        if (npc.CollideX)
        {
            npc.Direction *= -1;
            npc.VelocityX = npc.Direction * 2f;
        }
        if (npc.CollideY)
        {
            npc.VelocityY = npc.PrevVelocityY > 0f ? 1f : -1f;
        }

        const float fleeMaxSpeed = 4.5f;
        if (MathF.Sign(npc.VelocityX) != npc.Direction || MathF.Abs(npc.VelocityX) < fleeMaxSpeed)
        {
            npc.VelocityX += npc.Direction * 0.04f;
            if (npc.VelocityX * npc.Direction < 0f)
                npc.VelocityX += npc.Direction * (MathF.Abs(npc.VelocityX) > fleeMaxSpeed ? 0.4f : 0.2f);
            else if (MathF.Abs(npc.VelocityX) > fleeMaxSpeed)
                npc.VelocityX = npc.Direction * fleeMaxSpeed;
        }

        // 原版 57276-57324：前方 21 列 × 8 行地形探测
        int startCol = (int)((npc.X + width / 2f) / TileSize);
        const int scanCols = 20;
        if (npc.Direction < 0) startCol -= scanCols;
        int startRow = (int)((npc.Y + height) / TileSize);

        bool clearAhead = true;
        bool obstructionNear = false;

        for (int i = startCol; i <= startCol + scanCols; i++)
        {
            for (int j = startRow; j < startRow + 8; j++)
            {
                if (i < 0 || i >= _world.Tiles.Width || j < 0 || j >= _world.Tiles.Height) continue;
                if (!NpcTileSolid(i, j) && NpcLiquidAt(i, j) == 0) continue;

                if (j < startRow + 5) obstructionNear = true;
                clearAhead = false;
                break;
            }

            if (!clearAhead) break;
        }

        if (clearAhead) npc.VelocityY += 0.05f;
        else npc.VelocityY -= 0.2f;
        if (obstructionNear) npc.VelocityY -= 0.3f;

        npc.VelocityY = Math.Clamp(npc.VelocityY, -5f, 3f);
    }

    // ========================================================================
    // aiStyle 114：蜻蜓（原版 AI_114_Dragonflies，56484-56738）
    // ========================================================================

    /// <summary>
    /// 原版 AI_114_Dragonflies（595-601）：在出生点附近「悬停（ai[0] == 0，速度衰减 → 朝家园点游回）」
    /// 与「快速位移（ai[0] == 1，4 帧，离家园点 &gt;112px 时延长到 200 帧）」之间交替；
    /// 玩家 / 敌对 NPC 靠近时反向逃开并重设家园点。未移植：香蒲草（<c>FindCattailTop</c>）栖息点、
    /// 萤火虫式光照。
    /// </summary>
    private void Ai114Dragonflies(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);
        npc.NoGravity = true;

        if (local[0] == 0f)
        {
            local[0] = 1f;
            ai[2] = npc.X + width / 2f;
            ai[3] = npc.Y + height / 2f;

            var inside = NpcRngCircular(5f, 3f);
            var edge = NpcRngCircularEdge(5f, 3f);
            npc.VelocityX = (inside.X + edge.X) * 0.4f;
            npc.VelocityY = (inside.Y + edge.Y) * 0.4f;
            ai[1] = 0f;
            ai[0] = 1f;
        }

        float centerX = npc.X + width / 2f;
        float centerY = npc.Y + height / 2f;

        switch ((int)ai[0])
        {
            case 0:
                npc.VelocityX *= 0.94f;
                npc.VelocityY *= 0.94f;

                ai[1] += 1f;
                if (ai[1] >= 60 + NpcNextInt(0, 60))
                {
                    float homeDx = ai[2] - centerX;
                    float homeDy = ai[3] - centerY;
                    float dist = MathF.Sqrt(homeDx * homeDx + homeDy * homeDy);

                    if (dist > 96f)
                    {
                        npc.VelocityX = homeDx / dist * 3f;
                        npc.VelocityY = homeDy / dist * 3f;
                    }
                    else if (dist > 16f)
                    {
                        var jitter = NpcRngCircular(1f, 0.5f);
                        npc.VelocityX = homeDx / dist * 1f + jitter.X;
                        npc.VelocityY = homeDy / dist * 1f + jitter.Y;
                    }
                    else
                    {
                        var inside = NpcRngCircular(5f, 3f);
                        var edge = NpcRngCircularEdge(5f, 3f);
                        npc.VelocityX = (inside.X + edge.X) * 0.4f;
                        npc.VelocityY = (inside.Y + edge.Y) * 0.4f;
                    }

                    ai[1] = 0f;
                    ai[0] = 1f;
                }
                break;

            case 1:
            {
                float homeDx = ai[2] - centerX;
                float homeDy = ai[3] - centerY;
                int holdTicks = MathF.Sqrt(homeDx * homeDx + homeDy * homeDy) > 112f ? 200 : 4;

                ai[1] += 1f;
                if (ai[1] >= holdTicks)
                {
                    ai[1] = 0f;
                    ai[0] = 0f;
                }

                int col = (int)(centerX / TileSize);
                int row = (int)(centerY / TileSize);
                for (int r = row; r < row + 3; r++)
                {
                    if (r < 0 || r >= _world.Tiles.Height || col < 0 || col >= _world.Tiles.Width) continue;
                    if (!NpcTileSolid(col, r) && NpcLiquidAt(col, r) == 0) continue;

                    if (npc.VelocityY > 0f) npc.VelocityY *= 0.9f;
                    npc.VelocityY -= 0.2f;
                }

                if (npc.VelocityY < 0f)
                {
                    bool anySolid = false;
                    for (int r = row; r < row + 30; r++)
                    {
                        if (r < 0 || r >= _world.Tiles.Height || col < 0 || col >= _world.Tiles.Width) continue;
                        if (!NpcTileSolid(col, r)) continue;
                        anySolid = true;
                        break;
                    }

                    if (!anySolid) npc.VelocityY *= 0.9f;
                }
                break;
            }
        }

        if (npc.VelocityX != 0f) npc.Direction = npc.VelocityX > 0f ? 1 : -1;
        if (npc.Wet) npc.VelocityY = -3f;

        // 原版 56656-56698：敌对 NPC（100px）/ 玩家（150px）靠近 → 反向并重设家园点
        if (local[1] > 0f)
        {
            local[1] -= 1f;
            return;
        }

        local[1] = 15f;

        float count = 0f;
        float awayX = 0f, awayY = 0f;

        foreach (var other in _world.Npcs)
        {
            if (ReferenceEquals(other, npc) || !other.Active) continue;
            if (NpcCritterSet.Is(other.Type) || other.IsTownNpc || other.Damage <= 0) continue;

            var (ow, oh) = NpcSizes.Of(other.Type);
            float ddx = other.X + ow / 2f - centerX;
            float ddy = other.Y + oh / 2f - centerY;
            if (ddx * ddx + ddy * ddy > 100f * 100f) continue;

            count++;
            float len = MathF.Sqrt(ddx * ddx + ddy * ddy);
            if (len > 0.001f) { awayX += -ddx / len; awayY += -ddy / len; }
        }

        foreach (var player in _world.Players.Values)
        {
            if (!player.Active) continue;
            float ddx = player.AimPosition.X + NpcSizes.PlayerWidth / 2f - centerX;
            float ddy = player.AimPosition.Y + NpcSizes.PlayerHeight / 2f - centerY;
            if (ddx * ddx + ddy * ddy > 150f * 150f) continue;

            count++;
            float len = MathF.Sqrt(ddx * ddx + ddy * ddy);
            if (len > 0.001f) { awayX += -ddx / len; awayY += -ddy / len; }
        }

        if (count > 0f)
        {
            awayX = awayX / count * 2f;
            awayY = awayY / count * 2f;
            npc.VelocityX += awayX;
            npc.VelocityY += awayY;

            float len = MathF.Sqrt(npc.VelocityX * npc.VelocityX + npc.VelocityY * npc.VelocityY);
            if (len > 16f)
            {
                npc.VelocityX = npc.VelocityX / len * 16f;
                npc.VelocityY = npc.VelocityY / len * 16f;
            }

            ai[1] = -10f;
            ai[0] = 1f;
            ai[2] = centerX + awayX * 10f;
            ai[3] = centerY + awayY * 10f;
            return;
        }

        // 原版 56705-56736：到达家园点后换一个「空中休息点」（原版还会优先选香蒲草顶）
        float backDx = ai[2] - centerX;
        float backDy = ai[3] - centerY;
        if (MathF.Sqrt(backDx * backDx + backDy * backDy) >= 16f) return;
        if (NpcNextInt(0, 4) != 0) return;

        int col2 = (int)(centerX / TileSize);
        int row2 = (int)(centerY / TileSize);
        while (row2 < _world.MaxTilesY - 20 && row2 < (int)_world.WorldSurface && !NpcTileSolid(col2, row2))
            row2++;

        row2 -= NpcNextInt(3, 6);
        ai[2] = col2 * 16;
        ai[3] = row2 * 16;
    }

    // ========================================================================
    // aiStyle 115：瓢虫（原版 AI_115_LadyBugs，56324-56482）
    // ========================================================================

    /// <summary>
    /// 原版 AI_115_LadyBugs（604/605/669）：<c>ai[2] == 0</c> 空中漂浮（随风 + 朝 ai[0] 角度平滑逼近，
    /// 下方 4 格 / 上方 30 格地形探测调整高度）；<c>ai[2] == 1</c> 落地行走
    /// （朝 direction 收敛、遇水重新起飞）。未移植：669 的稀有度 / 光照表现。
    /// </summary>
    private void Ai115Ladybugs(WorldNpc npc)
    {
        var ai = npc.Ai;
        var local = npc.LocalAi;
        var (width, height) = NpcSizes.Of(npc.Type);
        npc.NoGravity = true;

        float centerX = npc.X + width / 2f;
        float centerY = npc.Y + height / 2f;

        if (ai[1] == 0f) ai[1] = NpcNextFloat() * 0.2f + 0.7f;

        if (--local[0] <= 0f)
        {
            local[0] = NpcNextInt(60, 181);

            if (NpcNextInt(0, 5) == 0)
            {
                if (ai[2] == 0f)
                {
                    ai[2] = 1f;
                    ai[0] = 0f;
                }
                else if (ai[2] == 1f)
                {
                    ai[2] = 0f;
                    ai[0] = NpcNextAngleRadians();
                }
            }

            // 原版 56380-56386：无条件重设朝向角（远离玩家 &gt;700px 时朝玩家飞）
            var target = NearestPlayer(centerX, centerY);
            ai[0] = NpcNextAngleRadians();
            if (target is not null)
            {
                float dx = target.AimPosition.X + NpcSizes.PlayerWidth / 2f - centerX;
                float dy = target.AimPosition.Y + NpcSizes.PlayerHeight / 2f - centerY;
                if (MathF.Sqrt(dx * dx + dy * dy) > 700f)
                    ai[0] = NpcVectorToAngle(dx, dy) + NpcNextFloatDirection() * 0.3f;
            }
        }

        var heading = NpcAngleToVector(ai[0]);

        if (ai[2] == 0f)
        {
            // ---- 空中漂浮 ----
            float targetVx = heading.X + _world.WindSpeedTarget * 0.8f;
            float targetVy = heading.Y;
            npc.VelocityX += (targetVx - npc.VelocityX) * 0.0125f;
            npc.VelocityY += (targetVy - npc.VelocityY) * 0.0125f;

            if (npc.VelocityY > 0f)
            {
                int col = (int)(centerX / TileSize);
                int row = (int)(centerY / TileSize);
                for (int r = row; r < row + 4; r++)
                {
                    if (r < 0 || r >= _world.Tiles.Height || col < 0 || col >= _world.Tiles.Width) continue;
                    if (!NpcTileSolid(col, r) && NpcLiquidAt(col, r) == 0) continue;

                    ai[0] = -ai[0];
                    if (npc.VelocityY > 0f) npc.VelocityY *= 0.9f;
                    break;
                }
            }

            if (npc.VelocityY < 0f)
            {
                int col = (int)(centerX / TileSize);
                int row = (int)(centerY / TileSize);
                bool anyBlocked = false;
                for (int r = row; r < row + 30; r++)
                {
                    if (r < 0 || r >= _world.Tiles.Height || col < 0 || col >= _world.Tiles.Width) continue;
                    if (!NpcTileSolid(col, r) && NpcLiquidAt(col, r) == 0) continue;
                    anyBlocked = true;
                    break;
                }

                if (!anyBlocked)
                {
                    ai[0] = -ai[0];
                    if (npc.VelocityY < 0f) npc.VelocityY *= 0.9f;
                }
            }

            if (npc.CollideX)
            {
                ai[0] = -ai[0] + MathF.PI;
                npc.VelocityX *= -0.2f;
            }
        }
        else
        {
            // ---- 落地行走 ----
            if (npc.VelocityY > 0f)
            {
                int col = (int)(centerX / TileSize) + npc.Direction;
                int row = (int)(centerY / TileSize);
                for (int r = row; r < row + 4; r++)
                {
                    if (r < 0 || r >= _world.Tiles.Height || col < 0 || col >= _world.Tiles.Width) continue;
                    if (NpcLiquidAt(col, r) == 0) continue;

                    // 前方有水 → 重新起飞
                    var target = NearestPlayer(centerX, centerY);
                    npc.VelocityY = -1f;
                    ai[2] = 0f;
                    ai[0] = NpcNextFloat() * (MathF.PI / 4f) - MathF.PI / 2f;
                    if (target is not null)
                    {
                        float dx = target.AimPosition.X + NpcSizes.PlayerWidth / 2f - centerX;
                        float dy = target.AimPosition.Y + NpcSizes.PlayerHeight / 2f - centerY;
                        if (MathF.Sqrt(dx * dx + dy * dy) > 700f)
                            ai[0] = NpcVectorToAngle(dx, dy) + NpcNextFloatDirection() * 0.3f;
                    }
                    return;
                }
            }

            if (npc.VelocityY != 0f)
            {
                npc.VelocityX *= 0.98f;
                npc.VelocityY += (2f - npc.VelocityY) * 0.005f;
            }
            else
            {
                npc.VelocityX += (npc.Direction * 1f - npc.VelocityX) * 0.05f;
                npc.VelocityY += 0.2f;
                if (npc.CollideX)
                {
                    npc.Direction *= -1;
                    npc.VelocityX *= -0.2f;
                }
            }
        }

        npc.Direction = npc.VelocityX > 0f ? 1 : -1;
    }

    // ========================================================================
    // aiStyle 116：水黾（原版 AI_116_WaterStriders，56258-56322）
    // ========================================================================

    /// <summary>
    /// 原版 AI_116_WaterStriders（612/613）：贴液面滑行 —— 液面以上时把脚底钳到液面，
    /// 液面以下时上浮（每帧 -0.8，上限 -4）；摩擦力衰减水平速度，
    /// 每 60-240 帧随机给一次水平冲量（±5）。未移植：无（该 AI 无额外分支）。
    /// </summary>
    private void Ai116WaterStriders(WorldNpc npc)
    {
        var ai = npc.Ai;
        var (width, height) = NpcSizes.Of(npc.Type);
        npc.NoGravity = true;

        int tileX = (int)((npc.X + width / 2f) / TileSize);
        int tileY = (int)((npc.Y + height / 2f) / TileSize);

        bool onSurface = false;
        if (NpcTryGetWaterLine(tileX, tileY, out float waterLine))
        {
            float bottom = npc.Y + height - 1f;
            if (npc.Y + height / 2f > waterLine)
            {
                npc.VelocityY -= 0.8f;
                if (npc.VelocityY < -4f) npc.VelocityY = -4f;
                if (bottom + npc.VelocityY < waterLine) npc.VelocityY = waterLine - bottom;
            }
            else
            {
                npc.VelocityY = MathF.Min(npc.VelocityY, waterLine - bottom);
                onSurface = true;
            }
        }
        else if (npc.Wet)
        {
            npc.VelocityY -= 0.3f;
        }

        if ((int)ai[0] != 0) return;

        ai[1] += 1f;
        npc.VelocityX *= 0.9f;
        if (npc.VelocityY == 0f) npc.VelocityX *= 0.6f;

        bool inWater = npc.Wet || onSurface;
        bool canBurst = inWater || npc.VelocityY == 0f;
        int cooldown = inWater ? NpcNextInt(120, 241) : NpcNextInt(60, 241);

        if (!canBurst || ai[1] < cooldown) return;

        ai[1] = 0f;
        npc.VelocityX = NpcNextFloatDirection() * 5f;

        if (!inWater)
        {
            if (npc.VelocityY == 0f) npc.VelocityY = -2f;
            ai[1] = 60f;
        }
    }

    // ========================================================================
    // aiStyle 118：海马（原版 AI_118_Seahorses，55404-55475）
    // ========================================================================

    /// <summary>
    /// 原版 AI_118_Seahorses（626/627）：<c>noGravity = wet</c>；水中沿 <c>ai[0]</c> 角度随机漂移
    /// （每 450-600 帧重选角度，速度上限 3），撞墙 / 撞顶按角度反射；离水则水平减速。
    /// 未移植：rotation（纯客户端表现）。
    /// </summary>
    private void Ai118Seahorses(WorldNpc npc)
    {
        var ai = npc.Ai;
        var (width, height) = NpcSizes.Of(npc.Type);
        npc.NoGravity = npc.Wet;

        float centerX = npc.X + width / 2f;
        float centerY = npc.Y + height / 2f;
        int tileX = (int)(centerX / TileSize);
        int tileY = (int)(centerY / TileSize);

        bool nearSurface = NpcTryGetWaterLineIterate(tileX, tileY, out float waterLine)
            && npc.Y - waterLine < 20f;

        if (!npc.Wet)
        {
            if (npc.VelocityY == 0f) npc.VelocityX *= 0.95f;
        }
        else
        {
            ai[1] -= 1f;
            if (ai[1] <= 0f)
            {
                var drift = NpcAngleToVector(ai[0]);
                npc.VelocityX += drift.X * 0.06f;
                npc.VelocityY += drift.Y * 0.06f;

                float len = MathF.Sqrt(npc.VelocityX * npc.VelocityX + npc.VelocityY * npc.VelocityY);
                if (len > 3f)
                {
                    npc.VelocityX = Math.Clamp(npc.VelocityX, -3f, 3f);
                    ai[1] = NpcNextInt(450, 600);
                    ai[0] = NpcNextAngleRadians();
                    if (nearSurface && ai[0] > MathF.PI) ai[0] -= MathF.PI;
                }
            }
            else
            {
                npc.VelocityX *= 0.95f;
                npc.VelocityY *= 0.95f;
            }
        }

        bool hitSurface = npc.CollideY && npc.Wet && (!nearSurface || npc.VelocityY < 0f);
        if (npc.CollideX || hitSurface)
        {
            var reflected = NpcAngleToVector(ai[0]);
            if (npc.CollideX) reflected.X *= -1f;
            if (hitSurface) reflected.Y *= -1f;

            ai[0] = NpcVectorToAngle(reflected.X, reflected.Y);

            var unit = NpcAngleToVector(ai[0]);
            float len = MathF.Sqrt(npc.VelocityX * npc.VelocityX + npc.VelocityY * npc.VelocityY);
            npc.VelocityX = unit.X * len;
            npc.VelocityY = unit.Y * len;
        }
    }
}
