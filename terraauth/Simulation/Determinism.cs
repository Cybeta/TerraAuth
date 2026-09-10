// TerraAuth — Phase 3: 确定性基础设施

namespace TerraAuth.Simulation;

/// <summary>确定性随机数接口。仿真层禁止用 System.Random / Random。</summary>
public interface IRng
{
    uint NextUInt32();
    int NextInt32(int maxExclusive);
    double NextDouble();
}

/// <summary>基于 xoshiro256** 的种子化 RNG。</summary>
public sealed class XoshiroRng : IRng
{
    private ulong _s0, _s1, _s2, _s3;

    public XoshiroRng(ulong seed)
    {
        // 初始化状态（简化版，生产可用 System.Random 的确定性包装替代）
        _s0 = seed; _s1 = seed ^ 0x9E3779B97F4A7C15; _s2 = _s0 << 1; _s3 = _s1 << 1;
    }

    public uint NextUInt32() => (uint)(Next() & 0xFFFFFFFF);
    public int NextInt32(int maxExclusive) => (int)(NextUInt32() % (uint)maxExclusive);
    public double NextDouble() => NextUInt32() / (double)uint.MaxValue;

    private ulong Next()
    {
        var result = _s1 * 5;
        var t = _s1 << 17;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3;
        _s2 ^= t;
        _s3 = (_s3 << 45) | (_s3 >> (64 - 45));
        return result;
    }
}
