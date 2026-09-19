// TerraAuth — SSC 守恒事务的「合成 / 消耗」判据
//
// 背景：SSC 下背包 / 箱子的权威值由服务端持有，客户端的所有改动都经「守恒事务」窗口聚合后结算
// （见 WorldState.TryCommitInventoryTransaction / TryCommitChestTransaction）。原先的守恒判据要求
// 每个 (物品, 前缀) 的总堆叠数**完全不变**——拖拽 / 整理 / 拆分 / 交换满足这一条，但**合成**
// （材料减少、产物增加）与**消耗**（药水 / 投掷物 / 弹药等只减不增）会改变总量，被误判为
// 「凭空造物 / 销毁」而整窗回滚 —— 表现为玩家合成后物品被「吃掉」。本文件补上这一缺口。
//
// 判据分两步（对应 <see cref="IsConserved"/>）：
//   1. 逐 (物品, 前缀) 完全一致 → 守恒（快路径，与原实现一致）。
//   2. 否则把差异拆成「减少」（可被配方消耗的材料 / 被消耗的物品）与「增加」（合成产物）：
//      - **带前缀的「增加」一律判不守恒**：原版合成产物恒为 0 前缀，凭空出现带前缀物品（例如把
//        无前缀物品「洗」成传奇前缀）必须拒绝。
//      - **没有「增加」**（纯减少）→ 背包侧放行（= 消耗：药水 / 投掷物 / 一次性道具）；箱子侧
//        不放行（否则客户端可用包 32 清空他人箱子内容而不受任何惩罚）。
//      - **有「增加」** → 必须能被原版配方解释：对每个产物，取单次产出堆叠能整除净增量的配方
//        （玩家一次合成产出的堆叠数是配方产出堆叠的整数倍），按配方材料从「减少」池里扣减
//        （配方组按组内任意成员合计扣减）。解释得通 → 守恒，否则回滚。
//
// 数据源：原版配方表 <see cref="RecipeTable"/>（由 decompiled-tmp/gen-recipes.ps1 从
// Terraria 1.4.5.8 的 Recipe.SetupRecipes 逐条提取，见 RecipeTable.cs）。
//
// 已知边界（宁回滚不放过）：
//   - **同一窗口内的多级合成**（先合成中间物、再立刻用它合成成品，中间物净变化为 0）不被解释，
//     会回滚。窗口仅 15 tick（≈250ms），手工操作不可能在窗口内完成两级合成后再上报，
//     故只影响极端场景，且回滚方向是安全侧。
//   - 未收录配方（反向墙 / 平台配方、静态不可解的少量条目）对应的合成会回滚 —— 同样落在安全侧。

using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>
/// 配方材料需求：具体物品（<see cref="ItemId"/> &gt;= 0）或配方组（<see cref="GroupId"/> &gt;= 0）二选一。
/// </summary>
public readonly struct CraftRequirement
{
    public CraftRequirement(int itemId, int groupId, int stack)
    {
        ItemId = itemId;
        GroupId = groupId;
        Stack = stack;
    }

    /// <summary>具体物品 ID；配方组需求时为 -1。</summary>
    public readonly int ItemId;

    /// <summary><see cref="RecipeTable.Groups"/> 下标；具体物品需求时为 -1。</summary>
    public readonly int GroupId;

    /// <summary>单次合成所需数量。</summary>
    public readonly int Stack;
}

/// <summary>配方要求的液体环境（原版 <c>needWater</c> / <c>needHoney</c> / <c>needLava</c>）。</summary>
[System.Flags]
public enum CraftEnvironment
{
    None = 0,

    /// <summary>需要紧邻水源（液体 &gt; 200 的水格，或算作水源的图格，如水槽）。</summary>
    Water = 1,

    /// <summary>需要紧邻蜂蜜（液体 &gt; 200 且 LiquidType == 2）。</summary>
    Honey = 2,

    /// <summary>需要紧邻岩浆（液体 &gt; 200 且 LiquidType == 1）。</summary>
    Lava = 4,
}

/// <summary>单条原版配方：产物 + 产物堆叠 + 合成站 + 液体环境 + 材料需求。</summary>
public readonly struct CraftRecipe
{
    public CraftRecipe(
        int productItemId,
        int productStack,
        int requiredTile,
        CraftEnvironment environment,
        CraftRequirement[] requirements)
    {
        ProductItemId = productItemId;
        ProductStack = productStack;
        RequiredTile = requiredTile;
        Environment = environment;
        Requirements = requirements;
    }

    public readonly int ProductItemId;

    /// <summary>单次合成的产物堆叠数（净增量必须是它的整数倍）。</summary>
    public readonly int ProductStack;

    /// <summary>合成站图格 ID（原版 <c>requiredTile</c>）；-1 = 手工合成（无站位要求）。</summary>
    public readonly int RequiredTile;

    /// <summary>液体环境要求（原版 <c>needWater</c> / <c>needHoney</c> / <c>needLava</c>）。</summary>
    public readonly CraftEnvironment Environment;

    public readonly CraftRequirement[] Requirements;
}

public static partial class RecipeTable
{
    /// <summary>产物物品 ID → 产出它的全部配方（含多配方变体）。延迟构建：数据表 <see cref="All"/> 在另一个分部声明里。</summary>
    private static readonly Lazy<Dictionary<int, CraftRecipe[]>> s_byProduct = new(BuildProductIndex);

    private static Dictionary<int, CraftRecipe[]> BuildProductIndex()
    {
        var index = new Dictionary<int, CraftRecipe[]>(All.Length);
        var buckets = new Dictionary<int, List<CraftRecipe>>();
        foreach (var recipe in All)
        {
            if (!buckets.TryGetValue(recipe.ProductItemId, out var list))
            {
                list = new List<CraftRecipe>();
                buckets[recipe.ProductItemId] = list;
            }
            list.Add(recipe);
        }

        foreach (var (product, list) in buckets) index[product] = list.ToArray();
        return index;
    }

    /// <summary>取产出指定物品的全部配方；无配方返回 false。</summary>
    public static bool TryGetRecipes(int productItemId, out CraftRecipe[] recipes)
        => s_byProduct.Value.TryGetValue(productItemId, out recipes!);
}

/// <summary>
/// SSC 守恒事务里的「合成 / 消耗」判据：在严格总量守恒之外，放行原版配方可解释的净增量与
/// （背包侧的）纯消耗性减少。详见文件头注释。
/// </summary>
public static class CraftingConservation
{
    /// <summary>合成解释的搜索节点上限（防御性：配方 / 产物组合极端时不做无界搜索，直接回滚）。</summary>
    private const int MaxSearchNodes = 4096;

    /// <summary>
    /// 判断客户端上报结果 <paramref name="proposed"/> 相对权威值 <paramref name="authoritative"/>
    /// 是否守恒（含合成 / 消耗）。
    /// </summary>
    /// <param name="authoritative">权威总量：背包事务为当前权威背包；箱子事务为窗口开始时的守恒基准。</param>
    /// <param name="proposed">把暂存意图叠加后的总量。</param>
    /// <param name="allowConsumption">
    /// 是否放行「只减少、不增加」的纯消耗。背包侧放行（玩家消耗自己的物品）；
    /// 箱子侧不放行（否则客户端可用包 32 静默清空箱子内容）。
    /// </param>
    /// <param name="removalLimit">
    /// 每种物品**允许被消耗的上限**（物品 ID → 数量）。背包事务传入窗口开始时的权威总量：
    /// 窗口期内服务端外部塞入（/give、拾取、开袋）的物品不在其中，客户端「清空该槽」的暂存意图
    /// 会因超出上限被判不守恒（保留「与外部变更冲突 → 回滚」的既有语义）。null = 不限制。
    /// </param>
    /// <param name="environment">
    /// 玩家当前合成环境快照（可达区域图格 + 相邻液体）。传入后，配方的**合成站与液体前置条件**
    /// 一并校验（等价原版 <c>Recipe.PlayerMeetsEnvironmentConditions</c>）；null = 不校验（仅纯函数用例）。
    /// </param>
    public static bool IsConserved(
        Dictionary<(int ItemId, byte Prefix), int> authoritative,
        Dictionary<(int ItemId, byte Prefix), int> proposed,
        bool allowConsumption,
        IReadOnlyDictionary<int, int>? removalLimit = null,
        CraftingEnvironment? environment = null)
    {
        if (SameTotals(authoritative, proposed)) return true;   // 快路径：逐 (物品, 前缀) 完全一致

        var available = new Dictionary<int, int>();   // 物品 → 可被配方消耗 / 被消耗掉的净减少量
        var added = new Dictionary<int, int>();       // 物品 → 需要被配方解释的净增加量

        foreach (var (key, total) in authoritative)
        {
            int proposedTotal = proposed.GetValueOrDefault(key);
            if (proposedTotal > total)
            {
                if (!CollectAdded(added, key, proposedTotal - total)) return false;
            }
            else if (proposedTotal < total)
            {
                Add(available, key.ItemId, total - proposedTotal);
            }
        }

        // 权威侧没有、客户端新报的 (物品, 前缀) 键：同样是「增加」。
        foreach (var (key, proposedTotal) in proposed)
        {
            if (authoritative.ContainsKey(key)) continue;
            if (!CollectAdded(added, key, proposedTotal)) return false;
        }

        if (removalLimit is not null)
        {
            foreach (var (item, amount) in available)
                if (amount > removalLimit.GetValueOrDefault(item)) return false;
        }

        if (added.Count == 0) return allowConsumption;   // 纯减少：背包 = 消耗，箱子 = 不放行

        int budget = MaxSearchNodes;
        return TryExplain(available, added, requireEmptyRemovals: !allowConsumption, environment, ref budget);
    }

    private static bool SameTotals(
        Dictionary<(int ItemId, byte Prefix), int> a,
        Dictionary<(int ItemId, byte Prefix), int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, total) in a)
            if (!b.TryGetValue(key, out int other) || other != total) return false;
        return true;
    }

    /// <summary>记录一次「增加」；带非 0 前缀的增加视为凭空造物，直接判不守恒。</summary>
    private static bool CollectAdded(Dictionary<int, int> added, (int ItemId, byte Prefix) key, int amount)
    {
        if (amount <= 0) return true;
        if (key.Prefix != 0) return false;
        Add(added, key.ItemId, amount);
        return true;
    }

    private static void Add(Dictionary<int, int> totals, int itemId, int amount)
    {
        if (amount <= 0) return;
        totals[itemId] = totals.GetValueOrDefault(itemId) + amount;
    }

    /// <summary>
    /// 递归把 <paramref name="added"/> 里的净增加逐项解释成配方产物（材料从 <paramref name="available"/> 扣减，
    /// 失败回溯）。每次取一个待解释产物，遍历其候选配方，要求净增量能被单次产出堆叠整除；
    /// 传了 <paramref name="environment"/> 时，候选配方还须满足合成站 / 液体前置条件
    /// （不满足的配方直接跳过，等价原版「该配方当前不可用」）。
    /// </summary>
    private static bool TryExplain(
        Dictionary<int, int> available,
        Dictionary<int, int> added,
        bool requireEmptyRemovals,
        CraftingEnvironment? environment,
        ref int budget)
    {
        int chosen = 0;
        foreach (var (item, amount) in added)
        {
            if (amount <= 0) continue;
            chosen = item;
            break;
        }

        if (chosen == 0)
        {
            if (!requireEmptyRemovals) return true;
            foreach (var (_, amount) in available)
                if (amount > 0) return false;   // 箱子侧要求「减少」也全部由配方解释（防顺手销毁箱内物品）
            return true;
        }

        if (!RecipeTable.TryGetRecipes(chosen, out var recipes)) return false;

        int need = added[chosen];
        foreach (var recipe in recipes)
        {
            if (environment is not null && !environment.Satisfies(recipe.RequiredTile, recipe.Environment)) continue;

            int outStack = recipe.ProductStack > 0 ? recipe.ProductStack : 1;
            if (need % outStack != 0) continue;

            if (--budget < 0) return false;
            if (!TryDeduct(available, recipe.Requirements, need / outStack, out var deductions)) continue;

            added[chosen] = 0;
            if (TryExplain(available, added, requireEmptyRemovals, environment, ref budget)) return true;
            added[chosen] = need;
            Rollback(available, deductions);
        }

        return false;
    }

    /// <summary>按单次合成所需材料的 <paramref name="times"/> 倍从可用池扣减；不足则整体回退并返回 false。</summary>
    private static bool TryDeduct(
        Dictionary<int, int> available,
        CraftRequirement[] requirements,
        int times,
        out List<(int ItemId, int Amount)> deductions)
    {
        deductions = new List<(int, int)>();
        foreach (var requirement in requirements)
        {
            int need = requirement.Stack * times;
            if (need <= 0) continue;

            if (requirement.GroupId < 0)
            {
                if (!Take(available, requirement.ItemId, need, deductions)) return Fail(available, deductions);
                continue;
            }

            if (requirement.GroupId >= RecipeTable.Groups.Length) return Fail(available, deductions);
            foreach (int member in RecipeTable.Groups[requirement.GroupId])
            {
                if (need <= 0) break;
                int take = System.Math.Min(available.GetValueOrDefault(member), need);
                if (take <= 0) continue;
                Take(available, member, take, deductions);
                need -= take;
            }

            if (need > 0) return Fail(available, deductions);   // 组内合计不足
        }

        return true;
    }

    /// <summary>扣减中途失败：把本次已扣减的量整体还回可用池。</summary>
    private static bool Fail(Dictionary<int, int> available, List<(int ItemId, int Amount)> deductions)
    {
        Rollback(available, deductions);
        deductions.Clear();
        return false;
    }

    private static bool Take(
        Dictionary<int, int> available,
        int itemId,
        int amount,
        List<(int ItemId, int Amount)> deductions)
    {
        if (available.GetValueOrDefault(itemId) < amount) return false;
        available[itemId] -= amount;
        deductions.Add((itemId, amount));
        return true;
    }

    private static void Rollback(Dictionary<int, int> available, List<(int ItemId, int Amount)> deductions)
    {
        foreach (var (itemId, amount) in deductions)
            available[itemId] = available.GetValueOrDefault(itemId) + amount;
    }
}
