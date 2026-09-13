// TerraAuth — Phase 3: 固定 timestep 游戏循环
// 架构 §4.3：确定性核心，禁止读墙钟
//
// 定时方式参考原版服务端：启动时 timeBeginPeriod(1) 把系统时钟分辨率提到 1ms（原版同样调用），
// 再用 Stopwatch 精确对齐 60Hz 边界。此前用 Task.Delay(1000/60) 驱动，Windows 默认时钟粒度
// ~15.6ms，步长被切成 15.6/31.2ms，负载一高仿真频率掉到 ~32Hz 甚至更低 ——
// 客户端按 60Hz 推进预测时系统性超前（史莱姆冒烟实测偏移 7~10px，根因即此处）。
// 一 tick 耗时超过步长时**顺延**（不追赶），与原版「掉帧而非变速」一致。

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TerraAuth.Simulation;

public sealed class GameLoop
{
    private readonly WorldSimulator _simulator;
    private readonly WorldState _world;
    private readonly double _stepSeconds; // 固定 dt
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _running;

    public long TickCount => _world.Tick;
    public int TargetHz { get; }

    public GameLoop(WorldSimulator simulator, WorldState world, int targetHz = 60)
    {
        _simulator = simulator;
        _world = world;
        TargetHz = targetHz;
        _stepSeconds = 1.0 / targetHz;
    }

    /// <summary>启动循环，直到 cancellation（专用线程跑同步阻塞循环，等价原版主循环）。</summary>
    public Task RunAsync(CancellationToken ct = default) => Task.Run(() => RunLoop(ct), ct);

    private void RunLoop(CancellationToken ct)
    {
        _running = true;

        // 参考原版：提升系统时钟分辨率到 1ms。没有这一步，Thread.Sleep(1) 实际睡 ~15.6ms，
        // 16.67ms 的步长会被切成 15.6/31.2ms → 仿真频率在 60Hz 与 ~32Hz 间抖动。
        // 服务器进程退出时 OS 自动复位计时器，无需显式 timeEndPeriod。
        if (OperatingSystem.IsWindows())
            NativeMethods.timeBeginPeriod(1);

        var stepTicks = Stopwatch.Frequency / TargetHz;
        var next = _clock.ElapsedTicks + stepTicks;

        // 诊断：每 ~5s 打印一次仿真频率（tick 数 / 墙钟），验证高精度定时是否让仿真
        // 稳定在 TargetHz；若显著低于 TargetHz，说明 Tick() 本身耗时是瓶颈（需另行优化）。
        long diagWindowTicks = 0;
        long diagWindowStart = _clock.ElapsedTicks;

        while (_running && !ct.IsCancellationRequested)
        {
            // 等到下一 tick 边界：剩余 >1ms 时 Sleep(1)（省 CPU），否则自旋/让出（保精度）
            long remaining;
            while ((remaining = next - _clock.ElapsedTicks) > 0)
            {
                if (remaining > Stopwatch.Frequency / 1000)
                    Thread.Sleep(1);
                else
                    Thread.Yield();
            }

            _simulator.Tick();   // ★ 固定 dt，永不变速
            next += stepTicks;
            if (next <= _clock.ElapsedTicks)   // 单 tick 耗时超过步长 → 顺延，不追赶
                next = _clock.ElapsedTicks + stepTicks;

            if (++diagWindowTicks >= TargetHz * 5)
            {
                var wall = (_clock.ElapsedTicks - diagWindowStart) / (double)Stopwatch.Frequency;
                Console.WriteLine($"[Sim] 仿真 {diagWindowTicks} ticks / {wall:F2}s = {diagWindowTicks / wall:F1}Hz");
                diagWindowTicks = 0;
                diagWindowStart = _clock.ElapsedTicks;
            }
        }
    }

    public void Stop() => _running = false;

    // ---------- 供确定性测试 ----------

    internal void StepOne()
    {
        _simulator.Tick();
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint uMilliseconds);
    }
}
