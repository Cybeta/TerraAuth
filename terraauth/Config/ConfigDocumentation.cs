// TerraAuth — 配置项中文说明（server.json 逐行注释的唯一来源）
//
// 为什么需要它：JSON 标准不支持注释，而 server.json 会被服务端在
//   「首次生成默认配置 / 切换世界 / ResetWorldChangesOnStart 复位」时整体重写
//   （见 ConfigurationService.Write）。若只在文件里手写注释，重写一次就没了。
// 做法：Write 出口统一走本文件的 Render——按属性名逐行插入中文注释；
//   读取侧打开 JsonCommentHandling.Skip，故带注释的文件（含外部编辑器保存的）照常解析。
// 维护约定：**新增 / 改名 ServerConfig 选项时，必须在此补一条说明**（键名 = JSON 属性名）。

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace TerraAuth.Config;

/// <summary>
/// server.json 的逐行中文说明表 + 文档化渲染器。
/// </summary>
public static class ConfigDocumentation
{
    /// <summary>
    /// 分段标题：在这些键之前插入一行 `// ---- 标题 ----`。
    /// 渲染顺序 = <see cref="ServerConfig"/> 的属性声明顺序（与 JSON 序列化顺序一致）。
    /// </summary>
    private static readonly Dictionary<string, string> Sections = new(StringComparer.Ordinal)
    {
        ["MaxConnections"] = "网络",
        ["WorldPath"] = "世界",
        ["MaxPlayerHp"] = "玩家属性权威",
        ["MaxFlightSpeed"] = "移动权威",
        ["GameMode"] = "战斗权威",
        ["MaxTileBreakPerSecond"] = "世界 / 交互权威",
        ["MaxPacketsPerSecond"] = "限流权威",
        ["SscEnabled"] = "库存权威（SSC 服务端背包）",
        ["DestroySummonsOnWeaponRemoval"] = "召唤权威",
        ["MaxViolationsBeforeBan"] = "封禁 / 违规处置",
        ["PlayerWhitelist"] = "连接认证",
        ["ModPolicy"] = "Mod 兼容层",
        ["MetricsEnabled"] = "监控",
        ["VerboseDiagnostics"] = "诊断",
    };

    /// <summary>键（JSON 属性名）→ 中文说明。嵌套对象的键（如 ModPolicy 内部）同样登记在此。</summary>
    private static readonly Dictionary<string, string> Docs = new(StringComparer.Ordinal)
    {
        // ---- 网络 ----
        ["MaxConnections"] = "最大并发连接数；达到后新连接被拒绝。默认 64。",
        ["HandshakeTimeoutSeconds"] = "握手超时（秒）：连接建立后未在此时限内完成版本握手即断开。默认 10。",
        ["SnapshotRateHz"] = "快照下发频率（Hz，20 = 每 50ms 一轮）。越高越顺滑，带宽与 CPU 越高。默认 20。",
        ["ViewportRadius"] = "视口裁剪半径（像素）：只向玩家下发此半径内的 NPC / 掉落物 / 液体 / 图格区块；0 = 不裁剪（全世界下发）。默认 2000。",

        // ---- 世界 ----
        ["WorldPath"] = "基准世界文件路径（.wld）。留空或文件不存在 → 按 WorldSize + WorldSeed 程序化生成。",
        ["WorldSize"] = "程序化世界尺寸档：\"Small\"（4200×1200）/ \"Medium\"（6400×1800）/ \"Large\"（8400×2400）。仅 WorldPath 为空或文件不存在时生效。",
        ["WorldSeed"] = "程序化生成的随机种子（决定地形 / 群系 / 矿脉分布）。改种子 = 换一张新地图，重启生效；换种子后请把 ResetWorldChangesOnStart 置 true 跑一次。",
        ["ResetWorldChangesOnStart"] = "启动时清空持久化的世界改动（图格 + 箱子内容）：换种子 / 换 .wld 时置 true 用一次。服务端复位后会自动把它改回 false（置 true 期间每次重启都会丢弃玩家改动）。",
        ["WorldExportPath"] = "世界导出路径（.wld）；留空 = 不导出。与 WorldPath 相同即原地保存（导出前旧文件滚动为 .bak）。",
        ["WorldExportIntervalSeconds"] = "空服导出世界的间隔（秒）；仅在 WorldExportPath 非空且无人在线时生效。默认 600。",
        ["MaxEnemies"] = "同屏敌怪上限（达到后停止刷怪；只统计存活且非城镇的 NPC，普通情况即史莱姆）。单人测试可调小（如 2）。默认 8。",
        ["SessionResumeGraceSeconds"] = "会话恢复宽限期（秒）：同名玩家在此期限内重连，服务端交还原运行时（位置 / 血量 / 增益）而不是当新玩家从头进服。0 = 关闭（断线即回收）。默认 60。",

        // ---- 玩家属性权威 ----
        ["MaxPlayerHp"] = "玩家生命上限；客户端上报超过即被纠正回权威值。默认 500。",
        ["MaxPlayerMana"] = "玩家法力上限；客户端上报超过即被纠正回权威值。默认 200。",

        // ---- 移动权威 ----
        ["MaxFlightSpeed"] = "水平移动速度上限（像素/帧，60fps）：位置包位移超过「上限 × Δt + TeleportTolerance」判为超速并拒绝。默认 8。",
        ["MaxFallSpeed"] = "垂直下落速度上限（像素/帧）。原版落地终速约 20，故垂直允许位移取 max(MaxFlightSpeed, MaxFallSpeed)。默认 20。",
        ["TeleportTolerance"] = "单次位置包的额外容差（像素），用于吸收网络抖动，避免正常移动被误判瞬移。默认 4。",
        ["ShadowPredictionMaxDeviation"] = "影子预测偏差阈值（像素）：服务端预测位置与客户端实报位置差超过此值即回正。默认 8。",

        // ---- 战斗权威 ----
        ["GameMode"] = "游戏难度：\"Classic\" / \"Expert\" / \"Master\"。决定玩家受击的减防系数与 NPC 生成时的生命 / 伤害倍率（专家 ×2、大师 ×3）。",
        ["MaxSingleDamage"] = "单次伤害上限（防「一击必杀」伪造）。注意包 28 的伤害线格式为 Int16，取值必须 ≤ 32767，否则该阈值永不触发。默认 30000。",
        ["MaxDpsWindowSeconds"] = "DPS 统计窗口（秒）。默认 5。",
        ["MaxDps"] = "窗口内最大 DPS，超过即拒绝。默认 50000。",

        // ---- 世界 / 交互权威 ----
        ["MaxTileBreakPerSecond"] = "单玩家每秒挖砖（破坏图格）上限。默认 60。",
        ["MaxTilePlacePerSecond"] = "单玩家每秒放砖（放置图格 / 墙）上限。默认 40。",
        ["MaxProjectilesPerSecond"] = "单玩家每秒新建弹幕上限。默认 30。",

        // ---- 限流权威 ----
        ["MaxPacketsPerSecond"] = "单玩家每秒上行包总量上限。默认 120。",
        ["MaxChatPerMinute"] = "单玩家每分钟聊天条数上限。默认 30。",
        ["MaxLiquidPerSecond"] = "单玩家每秒液体编辑帧上限（包 82 模块 0）。默认 60。",

        // ---- 库存权威（SSC） ----
        ["SscEnabled"] = "SSC（Server Side Characters）：true = 背包 / 生命 / 法力由服务端权威持有（进图全量下发 59 槽，开袋 / 拾取 / 合成走守恒事务）；false = 原版非 SSC 流程，以客户端本地背包为准（服务端背包权威功能全部停用）。",
        ["MaxStackSize"] = "单格最大堆叠（服务端权威上限；客户端上报超过即拒绝）。原版多数物品为 9999，本项目默认 999。",
        ["DestroySummonsOnWeaponRemoval"] = "true = 玩家把召唤武器移出（清空 / 换出）背包后立即销毁其召唤弹幕（并同步移除召唤 Buff）；false（默认）= 原版行为，召唤物继续存在至自然消失 / 被替换 / 断线。",

        // ---- 召唤权威 ----
        ["SummonAuthority"] = "召唤 / 哨兵的服务端权威档位：\"ClientDriven\"（默认，位置与节奏由客户端 AI 驱动）/ \"ServerDamage\"（命中伤害由服务端裁定）/ \"ServerAi\"（位置也由服务端接管）/ \"ServerShots\"（服务端生成派生弹幕）。逐级开启，可热重载。",

        // ---- 封禁 / 违规处置 ----
        ["MaxViolationsBeforeBan"] = "违规滑动窗口内累计达此值 → 记录封禁并踢出连接。默认 10。",
        ["ViolationWindowMinutes"] = "违规统计窗口（滑动，分钟）。默认 60。",

        // ---- 连接认证 ----
        ["PlayerWhitelist"] = "玩家名白名单（取包 4 上报的玩家名）；[] = 不限制。",

        // ---- Mod 兼容层 ----
        ["ModPolicy"] = "Mod 兼容策略（预留接口）。",
        ["Mode"] = "策略模式：\"VanillaOnly\"（仅原版客户端）/ \"Whitelist\"（只允许 AllowedMods 列出的 Mod）/ \"Blacklist\"（禁止 BlockedMods 列出的 Mod）/ \"AllowAll\"（全部允许，仅记录日志）。",
        ["BlockOnUnlistedMod"] = "Whitelist 模式下，出现未列出的 Mod 是否直接拒绝连接。",
        ["AllowClientSideMods"] = "是否允许仅客户端 Mod（不影响服务端，默认允许）。",
        ["AllowedMods"] = "白名单条目：[{ \"Name\": \"Mod 名\", \"MinVersion\": \"1.0\", \"MaxVersion\": \"2.0\", \"Hash\": \"可选哈希\" }]（后三项可省略）。",
        ["BlockedMods"] = "黑名单条目，格式同 AllowedMods。",
        ["RequiredMods"] = "强制要求的 Mod（缺失即拒绝连接），格式同 AllowedMods。",

        // ---- 监控 ----
        ["MetricsEnabled"] = "是否启用 Prometheus 指标端点。",
        ["MetricsPort"] = "Prometheus 抓取端口（默认 9090）。非管理员权限下可能绑定失败（仅告警，不影响游戏）。",

        // ---- 诊断 ----
        ["VerboseDiagnostics"] = "热路径诊断日志开关：开启后输出逐包 / 逐次受伤 / 逐次命中 / 背包事务 [InventoryTx] / 掉落物归属 [ItemOwner] 等明细，用于真机排障。正常游玩请置 false。可热重载。",
    };

    /// <summary>
    /// 把配置渲染为带逐行中文注释的 server.json 文本（值取自 <paramref name="config"/>）。
    /// 注释不参与解析，读取侧 <c>JsonCommentHandling.Skip</c> 会忽略。
    /// </summary>
    public static string Render(ServerConfig config)
    {
        var json = JsonSerializer.Serialize(config, ConfigurationService.JsonOptions);
        var sb = new StringBuilder();
        sb.AppendLine("{");
        // 文件头说明（放在首个 { 之后，仍是合法 JSONC）
        sb.AppendLine("  // ===== TerraAuth 服务端配置（server.json）=====");
        sb.AppendLine("  // 每行选项的上方注释即该项含义；改动保存后立即热重载（FileSystemWatcher）。");
        sb.AppendLine("  // 本文件会被服务端在「首次生成默认配置 / 切换世界 / 重置世界改动」时自动重写，注释不会丢失。");
        sb.AppendLine("  // 支持 // 注释与尾随逗号；枚举一律用字符串（如 \"Expert\"、\"ServerDamage\"）。");

        var lines = json.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (i == lines.Length - 1 && line.Trim() == "}")   // 收尾括号前不留注释
            {
                sb.AppendLine(line);
                continue;
            }

            var key = ExtractKey(line);
            if (key is not null)
            {
                var indent = line.Substring(0, line.Length - line.TrimStart().Length);
                if (Sections.TryGetValue(key, out var section))
                {
                    sb.AppendLine();
                    sb.AppendLine($"{indent}// ---- {section} ----");
                }
                if (Docs.TryGetValue(key, out var doc))
                    sb.AppendLine($"{indent}// {doc}");
            }
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 取 `  "Key": value` 形式的属性名；非属性行（`{` / `}` / 数组元素 / 文件头）返回 null。
    /// 要求闭引号后紧跟冒号，故数组元素（`"x",`）不会被误认为属性。
    /// </summary>
    private static string? ExtractKey(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '"') return null;
        var end = trimmed.IndexOf('"', 1);
        if (end <= 1) return null;
        var rest = trimmed.Substring(end + 1).TrimStart();
        return rest.StartsWith(':') ? trimmed.Substring(1, end - 1) : null;
    }
}
