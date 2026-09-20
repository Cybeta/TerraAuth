// TerraAuth — 放置链路验收测试：图格 / 墙 ID → 物品**集合**反查
//   （包 79 放砖、包 17 action 1 放砖 / action 3 放墙）

using System.Runtime.CompilerServices;
using TerraAuth.Authority;
using TerraAuth.Protocol;
using TerraAuth.Simulation;
using Xunit;

namespace TerraAuth.Tests;

/// <summary>
/// 真机缺陷回归：放置包携带的是**图格 ID**，背包里是**物品 ID**，两者不相等。
/// 旧实现拿图格 ID 直接查背包 → 工作台（物品 36 / 图格 18）永远查不到 →
/// 放置被拒（item_not_in_inventory）→ 服务端地图缺该图格 → 之后在它旁边合成因站位缺工作台整窗回滚。
/// 升级后反查表收录**全部**候选物品（工作台图格 18 ↔ 36/635/637…），手持任一变体都应接受。
/// </summary>
public class PlacementTests
{
    // 玩家站在 (2,2) 图格中心：放置距离校验（180px）稳过。
    private const float StandX = 40f;
    private const float StandY = 40f;
    private const int TargetX = 2;
    private const int TargetY = 2;

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
        AuthorityEnforcers enforcers, int tileType, int x = TargetX, int y = TargetY)
        => enforcers.World.Validate(new TilePlacePacket(x, y, tileType), 1, new CommandQueue());

    /// <summary>包 17（TileManipulation）放置请求：action 1 = PlaceTile，action 3 = PlaceWall。</summary>
    private static AuthorityResult ValidateBreak(
        AuthorityEnforcers enforcers, byte action, int tileType, int x = TargetX, int y = TargetY)
        => enforcers.World.Validate(new TileBreakPacket(x, y, action) { TileType = tileType }, 1, new CommandQueue());

    /// <summary>指定槽位只放一件指定物品。</summary>
    private static void HoldOne(PlayerRuntime player, int itemId, int slot = 0)
    {
        player.Items[slot] = itemId;
        player.ItemStacks[slot] = 1;
    }

    [Fact]
    public void Place_WorkBench_Accepts_Any_Candidate_Item_Variant()
    {
        var (_, enforcers, player) = MakeWorld();

        // 635（EbonwoodWorkBench）也是工作台图格 18 的合法放置物品 ——
        // 旧的「只认物品 ID 最小者」实现会把它误拒，这是本次升级的核心回归点。
        Assert.Contains(635, TileToItemTable.TileToItems[18]);
        HoldOne(player, 635);
        Assert.Equal(AuthorityDecision.Accept, ValidatePlace(enforcers, 18).Decision);

        // 最小 ID 的 36（WorkBench）依旧接受
        HoldOne(player, 36);
        Assert.Equal(AuthorityDecision.Accept, ValidatePlace(enforcers, 18).Decision);

        // 18（DepthMeter）不在候选集合里 → 拒
        HoldOne(player, 18);
        var wrongItem = ValidatePlace(enforcers, 18);
        Assert.Equal(AuthorityDecision.Reject, wrongItem.Decision);
        Assert.Equal("item_not_in_inventory", wrongItem.Reason);
    }

    [Fact]
    public void Place_WorkBench_Command_Consumes_NonMinimal_Variant()
    {
        var (world, _, player) = MakeWorld();

        // 扣减同样按集合语义：手持 635 时扣的是 635，图格落成 18
        HoldOne(player, 635);
        Assert.True(new TilePlaceCommand(1, 1, TargetX, TargetY, 18, 0).Apply(world, new XoshiroRng(1)).Applied);
        Assert.True(world.Tiles[TargetX, TargetY].Active);
        Assert.Equal(18, world.Tiles[TargetX, TargetY].Type);
        Assert.Equal(0, player.ItemStacks[0]);
        Assert.Equal(0, player.Items[0]);
    }

    [Fact]
    public void Place_WorkBench_Without_InventoryItem_Is_Rejected()
    {
        var (_, enforcers, _) = MakeWorld();

        var result = ValidatePlace(enforcers, 18);

        Assert.Equal(AuthorityDecision.Reject, result.Decision);
        Assert.Equal("item_not_in_inventory", result.Reason);
    }

    [Fact]
    public void Place_Dirt_Tile_Maps_To_DirtBlock_Item()
    {
        var (_, enforcers, player) = MakeWorld();

        // 图格 0（泥土）对应的物品是 2（DirtBlock），不是图格 ID 0
        HoldOne(player, 3);                     // 只有石块 → 拒绝
        Assert.Equal(AuthorityDecision.Reject, ValidatePlace(enforcers, 0).Decision);

        HoldOne(player, 2);                     // 泥土块 → 接受
        Assert.Equal(AuthorityDecision.Accept, ValidatePlace(enforcers, 0).Decision);
    }

    [Fact]
    public void Place_Wood_Tile_Maps_To_Wood_Item()
    {
        var (_, enforcers, player) = MakeWorld();

        // 图格 30（木块）对应的物品是 9（Wood）：手持「物品 30」本身就证明了映射不是恒等
        HoldOne(player, 30);
        var rawId = ValidatePlace(enforcers, 30);
        Assert.Equal(AuthorityDecision.Reject, rawId.Decision);
        Assert.Equal("item_not_in_inventory", rawId.Reason);

        HoldOne(player, 9);
        Assert.Equal(AuthorityDecision.Accept, ValidatePlace(enforcers, 30).Decision);
    }

    [Fact]
    public void Place_Unmapped_Tile_Is_Rejected()
    {
        var (world, enforcers, player) = MakeWorld();

        // 图格 5（树木）不由任何物品的 createTile 直接放置（靠树种生长）→ 反查不到 → 拒绝
        Assert.False(TileToItemTable.TryGetItemsForTile(5, out _));
        HoldOne(player, 9);

        var result = ValidatePlace(enforcers, 5);

        Assert.Equal(AuthorityDecision.Reject, result.Decision);
        Assert.Equal("tile_item_unknown", result.Reason);

        // 命令层同口径：不应用、不扣物品、不改世界
        var applied = new TilePlaceCommand(1, 1, TargetX, TargetY, 5, 0).Apply(world, new XoshiroRng(1));
        Assert.False(applied.Applied);
        Assert.Equal(1, player.ItemStacks[0]);
        Assert.False(world.Tiles[TargetX, TargetY].Active);
    }

    [Fact]
    public void Packet17_Action1_PlaceTile_Uses_Same_Mapping_And_Consumes_Item()
    {
        var (world, enforcers, player) = MakeWorld();

        // 物品 30（DepthMeter）不是图格 30（木块）的候选物品 → 拒
        HoldOne(player, 30);
        var rejected = ValidateBreak(enforcers, action: 1, tileType: 30);
        Assert.Equal(AuthorityDecision.Reject, rejected.Decision);
        Assert.Equal("item_not_in_inventory", rejected.Reason);

        // 物品 9（Wood）→ 权威层接受
        HoldOne(player, 9);
        Assert.Equal(AuthorityDecision.Accept, ValidateBreak(enforcers, action: 1, tileType: 30).Decision);

        // 命令层必须与图格写入同一提交单元扣减：图格 30 落成且物品 9 减 1
        Assert.True(new TileBreakCommand(1, 1, TargetX, TargetY, 1, 30).Apply(world, new XoshiroRng(1)).Applied);
        Assert.True(world.Tiles[TargetX, TargetY].Active);
        Assert.Equal(30, world.Tiles[TargetX, TargetY].Type);
        Assert.Equal(0, player.ItemStacks[0]);
        Assert.Equal(0, player.Items[0]);
    }

    [Fact]
    public void Packet17_Action3_PlaceWall_Uses_Wall_Mapping_And_Consumes_Item()
    {
        var (world, enforcers, player) = MakeWorld();

        // 未收录的墙（3）→ tile_item_unknown（反查不到即拒，不得凭空放墙）
        Assert.False(TileToItemTable.TryGetItemsForWall(3, out _));
        HoldOne(player, 93);
        var unknownWall = ValidateBreak(enforcers, 3, 3);
        Assert.Equal(AuthorityDecision.Reject, unknownWall.Decision);
        Assert.Equal("tile_item_unknown", unknownWall.Reason);

        // 物品 4 不在墙 4 的候选集合里 → 拒
        HoldOne(player, 4);
        var wrongItem = ValidateBreak(enforcers, 3, 4);
        Assert.Equal(AuthorityDecision.Reject, wrongItem.Decision);
        Assert.Equal("item_not_in_inventory", wrongItem.Reason);

        // 物品 93（WoodWall）→ 权威层接受，命令应用后墙落成且物品 93 减 1
        HoldOne(player, 93);
        Assert.Equal(AuthorityDecision.Accept, ValidateBreak(enforcers, 3, 4).Decision);

        Assert.True(new TileBreakCommand(1, 1, TargetX, TargetY, 3, 4).Apply(world, new XoshiroRng(1)).Applied);
        Assert.Equal(4, world.Tiles[TargetX, TargetY].Wall);
        Assert.Equal(0, player.ItemStacks[0]);
        Assert.Equal(0, player.Items[0]);
    }

    [Fact]
    public void Place_Consumes_From_SelectedSlot_First()
    {
        var (world, enforcers, player) = MakeWorld();

        // 槽 0 放 36、槽 1 放 635，手持槽指向 1 → 校验通过且扣的是槽 1 的 635，槽 0 的 36 不动
        HoldOne(player, 36, slot: 0);
        HoldOne(player, 635, slot: 1);
        player.SelectedSlot = 1;

        Assert.Equal(AuthorityDecision.Accept, ValidatePlace(enforcers, 18).Decision);
        Assert.True(new TilePlaceCommand(1, 1, TargetX, TargetY, 18, 0).Apply(world, new XoshiroRng(1)).Applied);

        Assert.Equal(0, player.ItemStacks[1]);
        Assert.Equal(0, player.Items[1]);
        Assert.Equal(1, player.ItemStacks[0]);
        Assert.Equal(36, player.Items[0]);
    }

    [Theory]
    [InlineData(18, 36)]    // 工作台
    [InlineData(18, 635)]   // 工作台（黑曜木变体，非最小 ID）
    [InlineData(0, 2)]      // 泥土
    [InlineData(30, 9)]     // 木头
    [InlineData(1, 3)]      // 石块
    [InlineData(4, 8)]      // 火把
    public void TileToItems_Maps_Known_Tiles(int tileType, int itemId)
    {
        Assert.True(TileToItemTable.TryGetItemsForTile(tileType, out var items));
        Assert.Contains(itemId, items);
    }

    [Theory]
    [InlineData(1, 26)]     // 石墙
    [InlineData(4, 93)]     // 木墙
    [InlineData(16, 30)]    // 土墙
    [InlineData(17, 135)]   // 蓝砖墙
    public void WallToItems_Maps_Known_Walls(int wallType, int itemId)
    {
        Assert.True(TileToItemTable.TryGetItemsForWall(wallType, out var items));
        Assert.Contains(itemId, items);
    }

    /// <summary>数据表自检：文件头声明的条目数与实际字典一致，且常量与字典一致。</summary>
    [Fact]
    public void TileToItemTable_Header_Counts_Match_Dictionaries()
    {
        Assert.Equal(TileToItemTable.TileEntryCount, TileToItemTable.TileToItems.Count);
        Assert.Equal(TileToItemTable.WallEntryCount, TileToItemTable.WallToItems.Count);

        var tablePath = Path.Combine(
            Path.GetDirectoryName(ThisFile())!, "..", "Simulation", "Placement", "TileToItemTable.cs");
        Assert.True(File.Exists(tablePath), $"找不到数据表源文件：{tablePath}");

        var header = File.ReadAllText(tablePath);
        Assert.Contains(
            $"当前条目数：图格 {TileToItemTable.TileToItems.Count} 项 / 墙 {TileToItemTable.WallToItems.Count} 项。",
            header);
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
