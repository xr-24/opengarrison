namespace GG2.Server.Plugins;

public interface IGg2ServerCommand
{
    string Name { get; }

    string Description { get; }

    string Usage { get; }

    Task<IReadOnlyList<string>> ExecuteAsync(
        Gg2ServerCommandContext context,
        string arguments,
        CancellationToken cancellationToken);
}
