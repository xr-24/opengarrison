using GG2.Server.Plugins;

namespace GG2.Server;

internal sealed class BuiltInServerCommand(
    string name,
    string description,
    string usage,
    Func<Gg2ServerCommandContext, string, CancellationToken, Task<IReadOnlyList<string>>> executeAsync) : IGg2ServerCommand
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    public string Usage { get; } = usage;

    public Task<IReadOnlyList<string>> ExecuteAsync(
        Gg2ServerCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
        => executeAsync(context, arguments, cancellationToken);
}
