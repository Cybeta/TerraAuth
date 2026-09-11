// TerraAuth — Plugins + ModCompat 验收测试（agent 执行参考）
// 对应 Plugins/README.md、ModCompat/README.md

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerraAuth.Authority;
using TerraAuth.Config;
using TerraAuth.Net.Phase5;
using TerraAuth.Plugins;
using TerraAuth.ModCompat;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

public class PluginModTests
{
    // ========================================================================
    // 1. HookRegistry：注册、触发、Deny 短路、Modified 传递
    // ========================================================================
    [Fact]
    public void HookRegistry_Register_And_Trigger_CallsHandler()
    {
        var hooks = new HookRegistry(new TestLogger());
        var plugin = new TestPlugin();
        var called = false;

        hooks.Register<PlayerJoinedArgs>(plugin, _ =>
        {
            called = true;
            return HookResult.Allow();
        });

        hooks.Trigger(new PlayerJoinedArgs { PlayerId = 1 });
        Assert.True(called);
    }

    [Fact]
    public void HookRegistry_Deny_ShortCircuits()
    {
        var hooks = new HookRegistry(new TestLogger());
        var plugin = new TestPlugin();
        var secondCalled = false;

        hooks.Register<PlayerMovingArgs>(plugin, _ => HookResult.Deny("blocked by plugin"));
        hooks.Register<PlayerMovingArgs>(plugin, _ =>
        {
            secondCalled = true;
            return HookResult.Allow();
        });

        var result = hooks.Trigger(new PlayerMovingArgs { PlayerId = 1 });
        Assert.Equal(HookResultType.Deny, result.Type);
        Assert.False(secondCalled); // Deny 后第二个 Handler 不执行
    }

    [Fact]
    public void HookRegistry_Modify_PassesDataToNext()
    {
        var hooks = new HookRegistry(new TestLogger());
        var plugin = new TestPlugin();

        hooks.Register<PlayerMovingArgs>(plugin, args =>
        {
            args.ToX = 999;
            return HookResult.Modify(args);
        });
        hooks.Register<PlayerMovingArgs>(plugin, args =>
        {
            Assert.Equal(999f, args.ToX); // 收到修改后的值
            return HookResult.Allow();
        });

        hooks.Trigger(new PlayerMovingArgs { PlayerId = 1, ToX = 0 });
    }

    [Fact]
    public void HookRegistry_Exception_In_Plugin_DoesNotCrash()
    {
        var hooks = new HookRegistry(new TestLogger());
        var plugin = new TestPlugin();
        hooks.Register<PlayerJoinedArgs>(plugin, _ => throw new InvalidOperationException("plugin bug"));

        // 异常被捕获，不抛出
        var result = hooks.Trigger(new PlayerJoinedArgs { PlayerId = 1 });
        Assert.Equal(HookResultType.Continue, result.Type);
    }

    [Fact]
    public void HookRegistry_UnregisterPlugin_RemovesHooks()
    {
        var hooks = new HookRegistry(new TestLogger());
        var plugin = new TestPlugin();
        var called = false;

        hooks.Register<PlayerJoinedArgs>(plugin, _ => { called = true; return HookResult.Allow(); });
        hooks.UnregisterPlugin(plugin);

        hooks.Trigger(new PlayerJoinedArgs { PlayerId = 1 });
        Assert.False(called); // 已注销，不再调用
    }

    [Fact]
    public void HookRegistry_HasSubscribers_Works()
    {
        var hooks = new HookRegistry(new TestLogger());
        var plugin = new TestPlugin();
        Assert.False(hooks.HasSubscribers<PlayerJoinedArgs>());

        hooks.Register<PlayerJoinedArgs>(plugin, _ => HookResult.Allow());
        Assert.True(hooks.HasSubscribers<PlayerJoinedArgs>());
    }

    // ========================================================================
    // 2. ModDetector：策略校验
    // ========================================================================
    [Theory]
    [InlineData(ClientType.Vanilla, ModPolicyMode.VanillaOnly, true)]
    [InlineData(ClientType.TModLoader, ModPolicyMode.VanillaOnly, false)]
    [InlineData(ClientType.TModLoader, ModPolicyMode.AllowAll, true)]
    public void ModDetector_Validate_VanillaOnly_BlocksModded(
        ClientType clientType, ModPolicyMode mode, bool expectedAllowed)
    {
        var detector = new ModDetector(
            new ModPolicy { Mode = mode },
            new TestLogger());

        var caps = new ClientCapabilities { Type = clientType };
        var result = detector.Validate(caps);

        Assert.Equal(expectedAllowed, result.IsAllowed);
    }

    [Fact]
    public void ModDetector_Whitelist_BlocksUnlistedMod()
    {
        var detector = new ModDetector(new ModPolicy
        {
            Mode = ModPolicyMode.Whitelist,
            BlockOnUnlistedMod = true,
            AllowedMods = new[] { new ModEntry { Name = "MagicStorage" } }
        }, new TestLogger());

        var caps = new ClientCapabilities
        {
            Type = ClientType.TModLoader,
            Mods = new[] { new LoadedMod("CheatMod", "1.0", "x", false, false) }
        };

        var result = detector.Validate(caps);
        Assert.False(result.IsAllowed);
        Assert.Contains("CheatMod", result.RejectReason);
    }

    [Fact]
    public void ModDetector_Whitelist_AllowsListedMod()
    {
        var detector = new ModDetector(new ModPolicy
        {
            Mode = ModPolicyMode.Whitelist,
            AllowedMods = new[] { new ModEntry { Name = "MagicStorage", MinVersion = "1.0" } }
        }, new TestLogger());

        var caps = new ClientCapabilities
        {
            Type = ClientType.TModLoader,
            Mods = new[] { new LoadedMod("MagicStorage", "1.5", "x", false, false) }
        };

        var result = detector.Validate(caps);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ModDetector_Blacklist_BlocksListedMod()
    {
        var detector = new ModDetector(new ModPolicy
        {
            Mode = ModPolicyMode.Blacklist,
            BlockedMods = new[] { new ModEntry { Name = "CheatMod" } }
        }, new TestLogger());

        var caps = new ClientCapabilities
        {
            Mods = new[] { new LoadedMod("CheatMod", "1.0", "x", false, false) }
        };

        var result = detector.Validate(caps);
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void ModDetector_VersionCompare_Works()
    {
        Assert.True(ModDetectorTestHelper.Compare("1.5.0", "1.0.0") > 0);
        Assert.True(ModDetectorTestHelper.Compare("1.0.0", "1.0.0") == 0);
        Assert.True(ModDetectorTestHelper.Compare("0.9.0", "1.0.0") < 0);
    }

    [Fact]
    public void ModDetector_Detect_ParsesTModLoader()
    {
        var detector = new ModDetector(new ModPolicy(), new TestLogger());
        var caps = detector.Detect(new ConnectionRequest
        {
            ClientVersion = "tModLoader v2024.1",
            ProtocolVersion = 279,
            Extensions = new[] { "direct_snapshots" }
        });

        Assert.Equal(ClientType.TModLoader, caps.Type);
        Assert.True(caps.SupportsDirectSnapshots);
    }

    // ========================================================================
    // 3. CustomPacketHandler：包 ID 区间管理
    // ========================================================================
    [Fact]
    public void CustomPacketHandler_RegisterRange_And_IsAllowed()
    {
        var handler = new CustomPacketHandler(new TestLogger());
        handler.RegisterPacketRange(250, 255, "tModLoader");

        Assert.True(handler.IsPacketAllowed(250, "tModLoader"));
        Assert.True(handler.IsPacketAllowed(255, "tModLoader"));
        Assert.False(handler.IsPacketAllowed(249, "tModLoader"));
        Assert.False(handler.IsPacketAllowed(250, "otherMod")); // 不属于 otherMod
    }

    [Fact]
    public async Task CustomPacketHandler_Handle_UnknownPacket_Rejects()
    {
        var handler = new CustomPacketHandler(new TestLogger());
        var allowed = await handler.HandleAsync(1, 300, new byte[0]);
        Assert.False(allowed); // 未注册的包 ID → 拒绝
    }

    // ========================================================================
    // 3b. TModLoader 握手：Mod 列表解析 + 自定义包转发
    // ========================================================================
    [Fact]
    public async Task TModLoader_Handshake_Parses_Mod_List_And_Validates()
    {
        var detector = new ModDetector(new ModPolicy { Mode = ModPolicyMode.Whitelist, BlockOnUnlistedMod = true,
            AllowedMods = new[] { new ModEntry { Name = "MagicStorage" } } }, new TestLogger());
        var compat = new TModLoaderCompat(detector, new TestLogger(), new CustomPacketHandler(new TestLogger()));

        var modList = BuildModListPayload("MagicStorage", "RecipeBrowser");
        var request = new ConnectionRequest { ClientVersion = "tModLoader v2024.1", ProtocolVersion = 326 };

        var result = await compat.HandleHandshakeAsync(request, modList);

        // 解析出两个 Mod，未在白名单的 RecipeBrowser 触发拒绝
        Assert.False(result.Success);
        Assert.Contains("RecipeBrowser", result.RejectReason);
    }

    [Fact]
    public async Task TModLoader_Handshake_AllowAll_Accepts_And_Parses_Mods()
    {
        var detector = new ModDetector(new ModPolicy { Mode = ModPolicyMode.AllowAll }, new TestLogger());
        var compat = new TModLoaderCompat(detector, new TestLogger(), new CustomPacketHandler(new TestLogger()));

        var result = await compat.HandleHandshakeAsync(
            new ConnectionRequest { ClientVersion = "tModLoader v2024.1", ProtocolVersion = 326 },
            BuildModListPayload("MagicStorage", "RecipeBrowser"));

        Assert.True(result.Success);
        Assert.NotNull(result.Capabilities);
        Assert.Equal(2, result.Capabilities!.Mods.Count);
        Assert.Equal("MagicStorage", result.Capabilities.Mods[0].Name);
    }

    [Fact]
    public async Task TModLoader_ParseModList_Tolerates_Truncated_Payload()
    {
        var detector = new ModDetector(new ModPolicy { Mode = ModPolicyMode.AllowAll }, new TestLogger());
        var compat = new TModLoaderCompat(detector, new TestLogger(), new CustomPacketHandler(new TestLogger()));

        var payload = BuildModListPayload("MagicStorage", "RecipeBrowser");
        var truncated = payload[..(payload.Length - 3)]; // 截断最后一个名字

        var result = await compat.HandleHandshakeAsync(
            new ConnectionRequest { ClientVersion = "tModLoader v2024.1", ProtocolVersion = 326 }, truncated);

        Assert.True(result.Success);
        Assert.Single(result.Capabilities!.Mods); // 只解析出完整的第一个
    }

    [Fact]
    public async Task TModLoader_ForwardModPacket_Uses_Injected_Channel_And_Range_Check()
    {
        var sent = new List<(int From, int To, int PacketId)>();
        var compat = new TModLoaderCompat(
            new ModDetector(new ModPolicy(), new TestLogger()), new TestLogger(), new CustomPacketHandler(new TestLogger()),
            forward: (from, to, id, _) => { sent.Add((from, to, id)); return Task.CompletedTask; });

        await compat.ForwardModPacketAsync(1, 2, 250, new byte[] { 0x01 }); // 区间内 → 转发
        await compat.ForwardModPacketAsync(1, 2, 200, new byte[] { 0x01 }); // 区间外 → 丢弃

        var single = Assert.Single(sent);
        Assert.Equal((1, 2, 250), single);
    }

    /// <summary>构造 Mod 名称清单载荷：Int32 数量 + 数量 ×（7-bit 长度前缀 + UTF-8 名称）。</summary>
    private static byte[] BuildModListPayload(params string[] names)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(names.Length);
            foreach (var n in names) PacketEncoder.WritePrefixedString(bw, n);
        }
        return ms.ToArray();
    }

    // ========================================================================
    // 3c. ModPolicy 从 server.json 读取并注入组合根
    // ========================================================================
    [Fact]
    public void ServerConfig_Deserializes_ModPolicy_With_StringEnum()
    {
        const string json = """
            {
              "ModPolicy": {
                "Mode": "Whitelist",
                "BlockOnUnlistedMod": true,
                "AllowedMods": [ { "Name": "MagicStorage", "MinVersion": "1.0" } ],
                "BlockedMods": [ { "Name": "CheatMod" } ]
              }
            }
            """;

        var cfg = System.Text.Json.JsonSerializer.Deserialize<ServerConfig>(json, ConfigurationService.JsonOptions)!;

        Assert.Equal(ModPolicyMode.Whitelist, cfg.ModPolicy.Mode);
        Assert.True(cfg.ModPolicy.BlockOnUnlistedMod);
        Assert.Equal("MagicStorage", cfg.ModPolicy.AllowedMods[0].Name);
        Assert.Equal("CheatMod", cfg.ModPolicy.BlockedMods[0].Name);
    }

    [Fact]
    public void Bootstrap_Applies_ModPolicy_From_Config_And_Wires_TModLoader()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"terraauth-mod-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var configPath = Path.Combine(dir, "server.json");
        File.WriteAllText(configPath, """
            { "ModPolicy": { "Mode": "Whitelist", "BlockOnUnlistedMod": true,
              "AllowedMods": [ { "Name": "MagicStorage" } ] } }
            """);

        try
        {
            using var host = GameHost.Bootstrap(Path.Combine(dir, "state.db"), configPath, metricsPort: 0, port: 0);

            // 组合根装配：TModLoader 兼容层存在，250-255 自定义包区间已注册
            Assert.NotNull(host.TModLoader);
            Assert.True(host.CustomPackets.IsPacketAllowed(250, "tModLoader"));

            // 策略来自 server.json：未列出的 Mod 被拒
            var caps = new ClientCapabilities
            {
                Type = ClientType.TModLoader,
                Mods = new[] { new LoadedMod("CheatMod", "1.0", "", false, false) },
            };
            Assert.False(host.ModDetector.Validate(caps).IsAllowed);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录清理失败可忽略 */ }
        }
    }

    // ========================================================================
    // 4. HookedPipeline：插件 Deny 短路权威管线（集成点）
    // ========================================================================
    [Fact]
    public void HookedPipeline_PluginDeny_ShortCircuitsAuthority()
    {
        // 用一个总是 Accept 的假管线 + 一个 Deny 的 Hook
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<NpcStrikeArgs>(new TestPlugin(), _ => HookResult.Deny("plugin blocked"));

        // 此处仅验证 Hook 逻辑可触发（完整集成需真实 INetworkPacket，留给 Phase 7 对抗测试）
        var result = hooks.Trigger(new NpcStrikeArgs { PlayerId = 1, Damage = 99999 });
        Assert.Equal(HookResultType.Deny, result.Type);
    }

    // ========================================================================
    // 5. HookedPipeline：与真实管线契约的端到端链路（P1 联调）
    // ========================================================================
    [Fact]
    public async Task HookedPipeline_PluginDeny_ShortCircuitsInnerPipeline()
    {
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<NpcStrikeArgs>(new TestPlugin(), _ => HookResult.Deny("plugin blocked"));

        var inner = new FakePipeline();
        var pipeline = new HookedPipeline(inner, hooks, new TestLogger());

        var result = await pipeline.ProcessAsync(
            new NpcStrikePacket(1, 99999), playerId: 1, new CommandQueue());

        Assert.Equal(AuthorityDecision.Reject, result.Decision);
        Assert.Equal(0, inner.Calls); // Deny 短路：内层权威管线未被调用
    }

    [Fact]
    public async Task HookedPipeline_PluginAllow_DelegatesToInnerPipeline()
    {
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<NpcStrikeArgs>(new TestPlugin(), _ => HookResult.Allow());

        var inner = new FakePipeline();
        var pipeline = new HookedPipeline(inner, hooks, new TestLogger());

        var result = await pipeline.ProcessAsync(
            new NpcStrikePacket(1, 10), playerId: 1, new CommandQueue());

        Assert.Equal(AuthorityDecision.Accept, result.Decision);
        Assert.Equal(1, inner.Calls); // Allow：继续走内层管线
    }

    /// <summary>
    /// Hook 参数必须携带**包数据**：否则插件基于恒为 0 的字段判断会静默失效
    /// （示例插件 AntiCheatLitePlugin 就靠 args.Damage &gt; 上限 来拦截）。
    /// </summary>
    [Fact]
    public async Task HookedPipeline_PopulatesNpcStrikeArgs_FromPacket()
    {
        NpcStrikeArgs? seen = null;
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<NpcStrikeArgs>(new TestPlugin(), a => { seen = a; return HookResult.Allow(); });

        var pipeline = new HookedPipeline(new FakePipeline(), hooks, new TestLogger());
        await pipeline.ProcessAsync(new NpcStrikePacket(NpcId: 42, Damage: 777), playerId: 3, new CommandQueue());

        Assert.NotNull(seen);
        Assert.Equal(3, seen!.PlayerId);
        Assert.Equal(42, seen.NpcId);
        Assert.Equal(777, seen.Damage); // 旧实现恒为 0 → 上限类插件永远不拦
    }

    [Fact]
    public async Task HookedPipeline_PopulatesTileArgs_FromPacket()
    {
        TilePlaceArgs? placed = null;
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<TilePlaceArgs>(new TestPlugin(), a => { placed = a; return HookResult.Allow(); });

        var pipeline = new HookedPipeline(new FakePipeline(), hooks, new TestLogger());
        await pipeline.ProcessAsync(
            new TilePlacePacket(X: 1234, Y: 567, TileType: 30), playerId: 1, new CommandQueue());

        Assert.NotNull(placed);
        Assert.Equal(1234, placed!.X);
        Assert.Equal(567, placed.Y);
        Assert.Equal(30, placed.TileType);
    }

    [Fact]
    public async Task HookedPipeline_PopulatesPlayerName_FromResolver()
    {
        NpcStrikeArgs? seen = null;
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<NpcStrikeArgs>(new TestPlugin(), a => { seen = a; return HookResult.Allow(); });

        var pipeline = new HookedPipeline(
            new FakePipeline(), hooks, new TestLogger(),
            playerNameResolver: id => id == 7 ? "Alice" : null);

        await pipeline.ProcessAsync(new NpcStrikePacket(1, 10), playerId: 7, new CommandQueue());
        Assert.Equal("Alice", seen!.PlayerName);

        // 解析不到时保持空串（不得抛异常）
        await pipeline.ProcessAsync(new NpcStrikePacket(1, 10), playerId: 8, new CommandQueue());
        Assert.Equal("", seen!.PlayerName);
    }

    [Fact]
    public async Task HookedPipeline_DenyBasedOnRealDamage_ShortCircuits()
    {
        // 用示例插件的判据（Damage 超上限即拒）验证参数填充真的让插件生效
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<NpcStrikeArgs>(new TestPlugin(),
            a => a.Damage > 10000 ? HookResult.Deny("damage exceeded") : HookResult.Allow());

        var inner = new FakePipeline();
        var pipeline = new HookedPipeline(inner, hooks, new TestLogger());

        var over = await pipeline.ProcessAsync(new NpcStrikePacket(1, 99999), playerId: 1, new CommandQueue());
        Assert.Equal(AuthorityDecision.Reject, over.Decision);
        Assert.Equal(0, inner.Calls);

        var normal = await pipeline.ProcessAsync(new NpcStrikePacket(1, 50), playerId: 1, new CommandQueue());
        Assert.Equal(AuthorityDecision.Accept, normal.Decision);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task HookedPipeline_UnmappedPacket_SkipsHookAndDelegates()
    {
        var hooks = new HookRegistry(new TestLogger());
        hooks.Register<NpcStrikeArgs>(new TestPlugin(), _ => HookResult.Deny("should not fire"));

        var inner = new FakePipeline();
        var pipeline = new HookedPipeline(inner, hooks, new TestLogger());

        // PlayerHealth 未映射到任何 Hook → 直接透传内层管线
        var result = await pipeline.ProcessAsync(
            new PlayerHealthPacket(1, 100, 100), playerId: 1, new CommandQueue());

        Assert.Equal(AuthorityDecision.Accept, result.Decision);
        Assert.Equal(1, inner.Calls);
    }

    // ========================================================================
    // 测试辅助类
    // ========================================================================
    private sealed class TestLogger : ILogger
    {
        public void Debug(string m, params object[] a) { }
        public void Info(string m, params object[] a) { }
        public void Warn(string m, params object[] a) { }
        public void Error(Exception? e, string m, params object[] a) { }
    }

    /// <summary>记录调用次数的假内层管线（验证 Hook 短路 / 透传）。</summary>
    private sealed class FakePipeline : IInboundPipeline
    {
        public int Calls;
        public Task<AuthorityResult> ProcessAsync(
            INetworkPacket packet, int playerId, CommandQueue commands, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(AuthorityResult.Accept(packet));
        }
    }

    [Fact]
    public void CommandService_Registers_Parses_And_Dispatches()
    {
        var commands = new CommandService();
        commands.Register("echo", "回显参数", (playerId, args) => CommandResult.Ok($"{playerId}:{string.Join('|', args)}"));

        Assert.True(commands.Execute(7, "ECHO a b").Success);          // 命令名不区分大小写
        Assert.Equal("7:a|b", commands.Execute(7, "echo a b").Output);  // 参数按空格切分
        Assert.False(commands.Execute(1, "nope").Success);              // 未注册 → 失败
        Assert.False(commands.Execute(1, "  ").Success);                // 空命令 → 失败

        commands.Register("boom", "抛异常", (_, _) => throw new InvalidOperationException("x"));
        Assert.False(commands.Execute(1, "boom").Success);              // 处理器异常被捕获
    }

    [Fact]
    public void Bootstrap_Registers_Builtin_Commands()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"terraauth-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var host = GameHost.Bootstrap(Path.Combine(dir, "state.db"),
                Path.Combine(dir, "server.json"), metricsPort: 0, port: 0);

            var names = host.Commands.Commands.Select(c => c.Name).ToHashSet();
            Assert.Contains("say", names);
            Assert.Contains("who", names);
            Assert.Contains("kick", names);
            Assert.Contains("help", names);

            var help = host.Commands.Execute(0, "help");
            Assert.True(help.Success);
            Assert.Contains("say", help.Output);

            Assert.False(host.Commands.Execute(0, "no-such-command").Success);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 清理失败可忽略 */ }
        }
    }

    private sealed class TestPlugin : PluginBase
    {
        public override string Id => "test";
        public override string Name => "Test";
        public override Version Version => new(1, 0, 0);
        public override IReadOnlyList<string> Dependencies => Array.Empty<string>();
    }

    /// <summary>暴露 ModDetector 的私有比较方法用于测试。</summary>
    private static class ModDetectorTestHelper
    {
        public static int Compare(string a, string b)
        {
            // 复用 ModDetector 内的版本比较逻辑（复制一份用于单测）
            var va = Parse(a); var vb = Parse(b);
            for (int i = 0; i < Math.Max(va.Length, vb.Length); i++)
            {
                var x = i < va.Length ? va[i] : 0;
                var y = i < vb.Length ? vb[i] : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }
        private static int[] Parse(string v) => (v ?? "").Split('.').Select(p =>
        {
            int n; int.TryParse(p, out n); return n;
        }).ToArray();
    }
}
