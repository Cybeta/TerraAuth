using System.Collections.Generic;

namespace TerraAuth.Simulation;

/// <summary>已登记武器的服务端开火行为；未知武器继续使用兼容性默认值。</summary>
public readonly record struct WeaponUseBehavior(
    int UseTime,
    int UseAnimation,
    int ManaCost);

public static class WeaponUseBehaviorTable
{
    public const int CompatibilityUseTime = 4;
    public const int CompatibilityUseAnimation = 4;

    public static readonly IReadOnlyDictionary<int, WeaponUseBehavior> Of =
        new Dictionary<int, WeaponUseBehavior>
        {
            [39] = new(30, 30, 0),
            [44] = new(30, 30, 0),
            [95] = new(15, 15, 0),
            [98] = new(8, 8, 0),
            [197] = new(12, 12, 0),
            [506] = new(30, 30, 0),
            [533] = new(7, 7, 0),
            [757] = new(20, 20, 0),
            [1157] = new(36, 36, 10),
            [127] = new(8, 8, 6),
            [3474] = new(10, 10, 10),
        };

    public static WeaponUseBehavior For(int itemId)
        => Of.TryGetValue(itemId, out var behavior)
            ? behavior
            : new(CompatibilityUseTime, CompatibilityUseAnimation, 0);
}
