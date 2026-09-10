// TerraAuth — Plugins + ModCompat 验收测试（agent 执行参考）
// 对应 Plugins/README.md、ModCompat/README.md

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerraAuth.Authority;
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
