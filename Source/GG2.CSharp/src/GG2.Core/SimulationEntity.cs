namespace GG2.Core;

public abstract class SimulationEntity
{
    protected SimulationEntity(int id)
    {
        Id = id;
    }

    public int Id { get; }
}
