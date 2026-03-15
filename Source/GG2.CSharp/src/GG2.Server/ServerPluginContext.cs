using GG2.Server.Plugins;

namespace GG2.Server;

internal sealed class ServerPluginContext(
    string pluginId,
    string pluginDirectory,
    string configDirectory,
    string mapsDirectory,
    IGg2ServerReadOnlyState serverState,
    IGg2ServerAdminOperations adminOperations,
    PluginCommandRegistry commandRegistry,
    Action<string> log) : IGg2ServerPluginContext
{
    public string PluginId { get; } = pluginId;

    public string PluginDirectory { get; } = pluginDirectory;

    public string ConfigDirectory { get; } = configDirectory;

    public string MapsDirectory { get; } = mapsDirectory;

    public IGg2ServerReadOnlyState ServerState { get; } = serverState;

    public IGg2ServerAdminOperations AdminOperations { get; } = adminOperations;

    public void RegisterCommand(IGg2ServerCommand command)
    {
        commandRegistry.RegisterPluginCommand(command, PluginId);
    }

    public void Log(string message)
    {
        log($"[plugin:{PluginId}] {message}");
    }
}
