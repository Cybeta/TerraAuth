// TerraAuth — 放置链路验收测试：图格 ID → 物品 ID 反查（包 79 放砖 / 包 17 放墙）

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

    /// <summary>手持槽 0 只放一件指定物品。</summary>
    private static void HoldOne(PlayerRuntime player, int itemId)
    {
        player.Items[0] = itemId;
        player.ItemStacks[0] = 1;
    }

    [Fact]
    public void Place_WorkBench_Requires_Item_36_Not_Tile_18()
    {
        var (world, enforcers, player) = MakeWorld();

        // 只有「物品 18」（原版 18 = DepthMeter）→ 旧实现（图格 ID 当物品 ID）会误接受
        HoldOne(player, 18);
        var wrongItem = ValidatePlace(enforcers, 18);
        Assert.Equal(AuthorityDecision.Reject, wrongItem.Decision);
        Assert.Equal("item_not_in_inventory", wrongItem.Reason);

        // 换上真正能放置图格 18 的物品 36（工作台）→ 接受
        HoldOne(player, 36);
        var accepted = ValidatePlace(enforcers, 18);
        Assert.Equal(AuthorityDecision.Accept, accepted.Decision);

        // 扣减同样按反查后的物品 ID：扣的是物品 36，图格落成 18
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
        Assert.False(TileToItemTable.TryGetItemForTile(5, out _));
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

    [Theory]
    [InlineData(18, 36)]    // 工作台
    [InlineData(0, 2)]      // 泥土
    [InlineData(30, 9)]     // 木头
    [InlineData(1, 3)]      // 石块
    [InlineData(4, 8)]      // 火把
    public void TileToItemTable_Maps_Known_Tiles(int tileType, int itemId)
    {
        Assert.True(TileToItemTable.TryGetItemForTile(tileType, out var mapped));
        Assert.Equal(itemId, mapped);
        Assert.NotEqual(tileType, mapped);      // 映射不是恒等
    }

    [Theory]
    [InlineData(1, 26)]     // 石墙
    [InlineData(4, 93)]     // 木墙
    [InlineData(16, 30)]    // 土墙
    [InlineData(17, 135)]   // 蓝砖墙
    public void WallToItemTable_Maps_Known_Walls(int wallType, int itemId)
    {
        Assert.True(TileToItemTable.TryGetItemForWall(wallType, out var mapped));
        Assert.Equal(itemId, mapped);
    }

    /// <summary>数据表自检：文件头声明的条目数与实际字典一致，且常量与字典一致。</summary>
    [Fact]
    public void TileToItemTable_Header_Counts_Match_Dictionaries()
    {
        Assert.Equal(TileToItemTable.TileEntryCount, TileToItemTable.TileToItem.Count);
        Assert.Equal(TileToItemTable.WallEntryCount, TileToItemTable.WallToItem.Count);

        var tablePath = Path.Combine(
            Path.GetDirectoryName(ThisFile())!, "..", "Simulation", "Placement", "TileToItemTable.cs");
        Assert.True(File.Exists(tablePath), $"找不到数据表源文件：{tablePath}");

        var header = File.ReadAllText(tablePath);
        Assert.Contains(
            $"当前条目数：图格 {TileToItemTable.TileToItem.Count} 项 / 墙 {TileToItemTable.WallToItem.Count} 项。",
            header);
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
