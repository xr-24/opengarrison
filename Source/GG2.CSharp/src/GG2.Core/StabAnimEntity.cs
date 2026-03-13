namespace GG2.Core;

public sealed class StabAnimEntity : SimulationEntity
{
    public const int WarmupTicks = 10;
    public const int SwingTicks = 32;
    public const int LifetimeTicks = WarmupTicks + SwingTicks;

    public StabAnimEntity(
        int id,
        int ownerId,
        PlayerTeam team,
        float x,
        float y,
        float directionDegrees) : base(id)
    {
        OwnerId = ownerId;
        Team = team;
        X = x;
        Y = y;
        DirectionDegrees = directionDegrees;
        TicksRemaining = LifetimeTicks;
    }

    public int OwnerId { get; }

    public PlayerTeam Team { get; }

    public float X { get; private set; }

    public float Y { get; private set; }

    public float DirectionDegrees { get; }

    public int TicksRemaining { get; private set; }

    public bool IsExpired => TicksRemaining <= 0;

    public float Alpha
    {
        get
        {
            if (TicksRemaining > SwingTicks)
            {
                return 0.15f;
            }

            var swingProgress = 1f - (TicksRemaining / (float)SwingTicks);
            return 0.3f + swingProgress * 0.7f;
        }
    }

    public void AdvanceOneTick(float ownerX, float ownerY)
    {
        X = ownerX;
        Y = ownerY;
        TicksRemaining -= 1;
    }
}
