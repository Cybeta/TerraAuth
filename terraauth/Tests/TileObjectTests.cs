// TerraAuth — 图格物件（家具 / 工作台）端到端验收：放置按 footprint + 帧写入，破坏整体移除
//
// 真机缺陷：包 79（PlaceObject）只写「点击的那一格」且不写帧（帧停在 Tile.Empty 的 -1,-1），
// 客户端本地放置成功并显示整件家具，服务端世界却只有一格无帧的图格 ——
// 表现为「放下的工作台看起来碎了 / 没了」，且在同一格再放会被 tile_already_exists 拒绝。

using System.Runtime.CompilerServices;
using TerraAuth.Authority;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

/// <summary>
/// 图格物件（原版 <c>TileObjectData</c> 收录的家具类图格）的服务端模型回归：
/// 放置写满 footprint 且逐格配帧、破坏整件移除且只掉 1 份，表外普通方块行为完全不变。
/// </summary>
public class TileObjectTests
{
    // 玩家站在 (2,2) 图格中心：放置距离校验（180px）稳过（与 PlacementTests 同夹具）。
    private const float StandX = 40f;
    private const float StandY = 40f;
    private const int TargetX = 2;
    private const int TargetY = 2;

    /// <summary>工作台：图格 18（Style2x1）、物品 36（WorkBench）。</summary>
    private const int WorkBenchTile = 18;
    private const int WorkBenchItem = 36;

    private static (WorldState World, AuthorityEnforcers Enforcers, PlayerRuntime Player) MakeWorld()
    {
        var world = new WorldState
        {
            Tiles = new TileMap(16, 16),
            MaxTilesX = 16,
            MaxTilesY = 16,
        };
        var enforcers = new AuthorityEnforcers(new RateLimits(), new NoOpAuditLogger(), world);
        world.InventoryLedger = enforcers.Inventory as IInventoryLedger;

        var player = new PlayerRuntime { Id = 1, SessionId = 1, Active = true, Position = new Vector2(StandX, StandY) };
        lock (world.PlayersLock)
            world.Players[1] = player;
        return (world, enforcers, player);
    }

    private static AuthorityResult ValidatePlace(
        AuthorityEnforcers enforcers, int tileType, int style = 0, int x = TargetX, int y = TargetY)
        => enforcers.World.Validate(new TilePlacePacket(x, y, tileType) { Style = style }, 1, new CommandQueue());

    /// <summary>指定槽位只放一件指定物品。</summary>
    private static void HoldOne(PlayerRuntime player, int itemId, int slot = 0)
    {
        player.Items[slot] = itemId;
        player.ItemStacks[slot] = 1;
    }

    /// <summary>工作台条目必须与原版一致：2×1、锚点在点击格、横向排样式、每格 16px + 2px 间隙、行高 18。</summary>
    [Fact]
    public void TileObjectTable_WorkBench_Entry_Matches_Vanilla_Geometry()
    {
        Assert.True(TileObjectTable.TryGet(WorkBenchTile, out var info));
        Assert.Equal(2, info.Width);
        Assert.Equal(1, info.Height);
        Assert.Equal(0, info.OriginX);
        Assert.Equal(0, info.OriginY);
        Assert.True(info.StyleHorizontal);
        Assert.Equal(16, info.CoordinateWidth);
        Assert.Equal(2, info.CoordinatePadding);
        Assert.Equal(new[] { 18 }, info.CoordinateHeights);
        Assert.Equal(36, info.CoordinateFullWidth);   // (16 + 2) * 2
        Assert.Equal(20, info.CoordinateFullHeight);  // (18 + 2)
    }

    /// <summary>帧公式抽样：style 直接作为 placement style，横向排样式 → style 1 的基准是 2 * 18 = 36。</summary>
    [Fact]
    public void TryGetFrame_WorkBench_Matches_Vanilla_Place_Formula()
    {
        Assert.True(TileObjectTable.TryGetFrame(WorkBenchTile, 0, 0, 0, out short x00, out short y00));
        Assert.Equal((short)0, x00);
        Assert.Equal((short)0, y00);

        Assert.True(TileObjectTable.TryGetFrame(WorkBenchTile, 0, 1, 0, out short x10, out short y10));
        Assert.Equal((short)18, x10);
        Assert.Equal((short)0, y10);

        Assert.True(TileObjectTable.TryGetFrame(WorkBenchTile, 1, 0, 0, out short x01, out _));
        Assert.Equal((short)36, x01);
        Assert.True(TileObjectTable.TryGetFrame(WorkBenchTile, 1, 1, 0, out short x11, out _));
        Assert.Equal((short)54, x11);

        // footprint 之外 / 非物件类型 → false
        Assert.False(TileObjectTable.TryGetFrame(WorkBenchTile, 0, 2, 0, out _, out _));
        Assert.False(TileObjectTable.TryGetFrame(WorkBenchTile, 0, 0, 1, out _, out _));
        Assert.False(TileObjectTable.TryGetFrame(1, 0, 0, 0, out _, out _));
    }

    /// <summary>经包 79 放工作台：两格都是图格 18 且帧为 0 / 18，只扣 1 件物品。</summary>
    [Fact]
    public void Place_WorkBench_Writes_Whole_Footprint_With_Frames()
    {
        var (world, enforcers, player) = MakeWorld();
        HoldOne(player, WorkBenchItem);

        Assert.Equal(AuthorityDecision.Accept, ValidatePlace(enforcers, WorkBenchTile).Decision);
        Assert.True(new TilePlaceCommand(1, 1, TargetX, TargetY, WorkBenchTile, 0).Apply(world, new XoshiroRng(1)).Applied);

        var anchor = world.Tiles[TargetX, TargetY];
        var second = world.Tiles[TargetX + 1, TargetY];
        Assert.True(anchor.Active);
        Assert.Equal(WorkBenchTile, anchor.Type);
        Assert.Equal((short)0, anchor.FrameX);
        Assert.Equal((short)0, anchor.FrameY);
        Assert.True(second.Active);
        Assert.Equal(WorkBenchTile, second.Type);
        Assert.Equal((short)18, second.FrameX);
        Assert.Equal((short)0, second.FrameY);

        Assert.Equal(0, player.ItemStacks[0]);   // 整件家具只扣 1 件
        Assert.Equal(0, player.Items[0]);
    }

    /// <summary>footprint 内任一格被占用即拒 —— 证明占用检查覆盖整个 footprint，而不是只看点击格。</summary>
    [Fact]
    public void Place_WorkBench_Rejects_When_Footprint_Cell_Occupied()
    {
        var (world, enforcers, player) = MakeWorld();
        HoldOne(player, WorkBenchItem);

        // 点击格自身空着，第二格被石块占着：旧的单格检查会放行
        world.Tiles[TargetX + 1, TargetY] = new Tile { Active = true, Type = 1 };

        var rejected = ValidatePlace(enforcers, WorkBenchTile);
        Assert.Equal(AuthorityDecision.Reject, rejected.Decision);
        Assert.Equal("tile_already_exists", rejected.Reason);
        Assert.Contains("占用格=(3,2)", rejected.Detail);
        Assert.Contains("锚点=(2,2)", rejected.Detail);
    }

    /// <summary>表外类型（石块 1 / 泥土 0 这类普通方块）行为不变：单格检查、单格写入、帧不受影响、扣 1 件。</summary>
    [Fact]
    public void Place_Normal_Tile_Still_Writes_Single_Cell_Without_Frames()
    {
        Assert.False(TileObjectTable.TryGet(0, out _));
        Assert.False(TileObjectTable.TryGet(1, out _));

        var (world, enforcers, player) = MakeWorld();
        HoldOne(player, 3);   // 石块：图格 1 ↔ 物品 3

        // 单格检查：相邻格被占用不影响（只有图格物件才按 footprint 判）
        world.Tiles[TargetX + 1, TargetY] = new Tile { Active = true, Type = 0 };
        Assert.Equal(AuthorityDecision.Accept, ValidatePlace(enforcers, 1).Decision);

        Assert.True(new TilePlaceCommand(1, 1, TargetX, TargetY, 1, 0).Apply(world, new XoshiroRng(1)).Applied);
        Assert.True(world.Tiles[TargetX, TargetY].Active);
        Assert.Equal(1, world.Tiles[TargetX, TargetY].Type);
        Assert.Equal((short)-1, world.Tiles[TargetX, TargetY].FrameX);   // 帧沿用 Empty 标记，不套用物件帧算法
        Assert.Equal((short)-1, world.Tiles[TargetX, TargetY].FrameY);
        Assert.Equal(0, player.ItemStacks[0]);
    }

    /// <summary>拆工作台任意一格 → 两格都清空，且只掉 1 件工作台。</summary>
    [Theory]
    [InlineData(0)]   // 拆锚点格
    [InlineData(1)]   // 拆物件内的第二格
    public void Break_WorkBench_Removes_Whole_Footprint_And_Drops_Once(int offset)
    {
        var (world, _, player) = MakeWorld();
        HoldOne(player, WorkBenchItem);
        Assert.True(new TilePlaceCommand(1, 1, TargetX, TargetY, WorkBenchTile, 0).Apply(world, new XoshiroRng(1)).Applied);

        Assert.True(new TileBreakCommand(1, 1, TargetX + offset, TargetY, 0, 0)
            .Apply(world, new XoshiroRng(1)).Applied);

        Assert.False(world.Tiles[TargetX, TargetY].Active);
        Assert.False(world.Tiles[TargetX + 1, TargetY].Active);
        Assert.Single(world.Items);
        Assert.Equal(WorkBenchItem, world.Items[0].ItemId);
        Assert.Equal(1, world.Items[0].Stack);
    }

    /// <summary>
    /// 退化分支：本次修复之前服务端写下的多格物件没有帧（仍是 Empty 的 -1,-1），无法由帧反推锚点 →
    /// 按连通块反查整个 footprint，仍然整件移除且只掉 1 份。
    /// </summary>
    [Fact]
    public void Break_Legacy_WorkBench_Without_Frames_Falls_Back_To_Connected_Footprint()
    {
        var (world, _, _) = MakeWorld();
        world.Tiles[TargetX, TargetY] = new Tile { Active = true, Type = WorkBenchTile, FrameX = -1, FrameY = -1 };
        world.Tiles[TargetX + 1, TargetY] = new Tile { Active = true, Type = WorkBenchTile, FrameX = -1, FrameY = -1 };

        Assert.True(new TileBreakCommand(1, 1, TargetX + 1, TargetY, 0, 0)
            .Apply(world, new XoshiroRng(1)).Applied);

        Assert.False(world.Tiles[TargetX, TargetY].Active);
        Assert.False(world.Tiles[TargetX + 1, TargetY].Active);
        Assert.Single(world.Items);
        Assert.Equal(WorkBenchItem, world.Items[0].ItemId);
    }

    /// <summary>表外类型破坏行为不变：只清单格，不动相邻格。</summary>
    [Fact]
    public void Break_Normal_Tile_Still_Removes_Single_Cell()
    {
        var (world, _, _) = MakeWorld();
        world.Tiles[TargetX, TargetY] = new Tile { Active = true, Type = 1 };
        world.Tiles[TargetX + 1, TargetY] = new Tile { Active = true, Type = 1 };

        Assert.True(new TileBreakCommand(1, 1, TargetX, TargetY, 0, 0).Apply(world, new XoshiroRng(1)).Applied);

        Assert.False(world.Tiles[TargetX, TargetY].Active);
        Assert.True(world.Tiles[TargetX + 1, TargetY].Active);
    }

    /// <summary>数据表自检：文件头声明的条目数与实际字典一致。</summary>
    [Fact]
    public void TileObjectTable_Header_Counts_Match_Dictionary()
    {
        Assert.Equal(TileObjectTable.EntryCount, TileObjectTable.Objects.Count);
        Assert.True(TileObjectTable.EntryCount > 300, $"条目数异常：{TileObjectTable.EntryCount}");

        var tablePath = Path.Combine(
            Path.GetDirectoryName(ThisFile())!, "..", "Simulation", "Placement", "TileObjectTable.cs");
        Assert.True(File.Exists(tablePath), $"找不到数据表源文件：{tablePath}");

        var header = File.ReadAllText(tablePath);
        Assert.Contains($"当前条目数：{TileObjectTable.Objects.Count} 项。", header);
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
