// 诊断日志开关（真机排障用）
// 逐包 / 逐次受伤 / 逐次命中这类输出在正常游玩时是纯噪声（且会让热路径无谓构造字符串），
// 但它们正是「没碰到却掉血」「掉落物拾不到」这类问题的定位依据，故保留代码、默认关闭，
// 由 ServerConfig.VerboseDiagnostics（支持热重载）打开。
//
// 归属根命名空间：Protocol / Simulation / Net / Authority 各层都可能输出诊断，
// 放在任一层都会造成跨层依赖，故置于根命名空间（各子命名空间可直接引用，无需 using）。
// 调用约定：`if (DiagnosticLog.Enabled) Console.WriteLine(...)` —— 开关关闭时连字符串都不构造。

namespace TerraAuth;

/// <summary>热路径诊断输出的全局开关（默认关闭）。</summary>
public static class DiagnosticLog
{
    private static volatile bool _enabled;

    /// <summary>是否输出诊断日志；由组合根按 <c>ServerConfig.VerboseDiagnostics</c> 接线。</summary>
    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
