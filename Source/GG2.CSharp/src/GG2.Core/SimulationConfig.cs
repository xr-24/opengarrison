namespace GG2.Core;

public sealed class SimulationConfig
{
    public const int DefaultTicksPerSecond = 30;

    public int TicksPerSecond { get; init; } = DefaultTicksPerSecond;

    public double FixedDeltaSeconds => 1.0 / TicksPerSecond;

    public bool EnableLocalDummies { get; init; } = true;

    public bool EnableEnemyTrainingDummy { get; init; } = true;

    public bool EnableFriendlySupportDummy { get; init; } = true;
}
