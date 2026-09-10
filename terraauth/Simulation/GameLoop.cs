// TerraAuth — Phase 3: 固定 timestep 游戏循环
// 架构 §4.3：确定性核心，禁止读墙钟

using System.Diagnostics;

namespace TerraAuth.Simulation;

public sealed class GameLoop
{
    private readonly WorldSimulator _simulator;
    private readonly WorldState _world;
    private readonly double _stepSeconds; // 固定 dt
    private readonly int _maxSubSteps;    // 防 spiral of death
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _accumulator;
    private bool _running;

    public long TickCount => _world.Tick;
    public int TargetHz { get; }

    public GameLoop(WorldSimulator simulator, WorldState world, int targetHz = 60)
    {
        _simulator = simulator;
        _world = world;
        TargetHz = targetHz;
        _stepSeconds = 1.0 / targetHz;
        _maxSubSteps = 8;
    }

    /// <summary>启动循环，直到 cancellation。</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _running = true;
        _accumulator = 0;

        while (_running && !ct.IsCancellationRequested)
        {
            // 注意：真实实现应从 NetworkHost 的输入事件计时
            // 此处用固定间隔模拟（生产应基于高精度计时器）
            await Task.Delay(TimeSpan.FromMilliseconds(1000.0 / TargetHz), ct)
                .ConfigureAwait(false);

            _accumulator += _stepSeconds;

            var steps = 0;
            while (_accumulator >= _stepSeconds && steps < _maxSubSteps)
            {
                _simulator.Tick();   // ★ 固定 dt，永不变速
                _accumulator -= _stepSeconds;
                steps++;
            }

            if (steps == _maxSubSteps)
                _accumulator = 0; // 掉帧而非变速
        }
    }

    public void Stop() => _running = false;

    // ---------- 供确定性测试 ----------

    internal void StepOne()
    {
        _simulator.Tick();
    }
}
