namespace GG2.Server.Plugins;

public interface IGg2ServerPlugin
{
    string Id { get; }

    string DisplayName { get; }

    Version Version { get; }

    void Initialize(IGg2ServerPluginContext context);

    void Shutdown();
}
