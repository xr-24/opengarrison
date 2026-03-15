namespace GG2.Server.Plugins;

public interface IGg2ServerPluginContext
{
    string PluginId { get; }

    string PluginDirectory { get; }

    string ConfigDirectory { get; }

    string MapsDirectory { get; }

    IGg2ServerReadOnlyState ServerState { get; }

    IGg2ServerAdminOperations AdminOperations { get; }

    void RegisterCommand(IGg2ServerCommand command);

    void Log(string message);
}
