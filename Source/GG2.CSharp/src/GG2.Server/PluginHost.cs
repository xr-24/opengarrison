using GG2.Server.Plugins;

namespace GG2.Server;

internal sealed class PluginHost
{
    private readonly PluginCommandRegistry _commandRegistry;
    private readonly IGg2ServerReadOnlyState _serverState;
    private readonly IGg2ServerAdminOperations _adminOperations;
    private readonly Action<string> _log;
    private readonly string _pluginsDirectory;
    private readonly string _pluginConfigRoot;
    private readonly string _mapsDirectory;
    private readonly List<PluginLoader.LoadedPlugin> _loadedPlugins = new();

    public PluginHost(
        PluginCommandRegistry commandRegistry,
        IGg2ServerReadOnlyState serverState,
        IGg2ServerAdminOperations adminOperations,
        string pluginsDirectory,
        string pluginConfigRoot,
        string mapsDirectory,
        Action<string> log)
    {
        _commandRegistry = commandRegistry;
        _serverState = serverState;
        _adminOperations = adminOperations;
        _pluginsDirectory = pluginsDirectory;
        _pluginConfigRoot = pluginConfigRoot;
        _mapsDirectory = mapsDirectory;
        _log = log;
    }

    public IReadOnlyList<string> LoadedPluginIds => _loadedPlugins
        .Select(entry => entry.Plugin.Id)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void LoadPlugins()
    {
        _loadedPlugins.Clear();
        _loadedPlugins.AddRange(PluginLoader.LoadFromDirectory(_pluginsDirectory, CreateContext, _log));
        foreach (var loadedPlugin in _loadedPlugins)
        {
            _log($"[plugin] loaded {loadedPlugin.Plugin.DisplayName} ({loadedPlugin.Plugin.Id} {loadedPlugin.Plugin.Version})");
        }
    }

    public void LoadPlugins(IEnumerable<System.Reflection.Assembly> assemblies)
    {
        _loadedPlugins.Clear();
        _loadedPlugins.AddRange(PluginLoader.LoadFromAssemblies(assemblies, CreateContext, _log));
    }

    public void NotifyServerStarting() => Dispatch<IGg2ServerLifecycleHooks>(hook => hook.OnServerStarting());

    public void NotifyServerStarted() => Dispatch<IGg2ServerLifecycleHooks>(hook => hook.OnServerStarted());

    public void NotifyServerStopping() => Dispatch<IGg2ServerLifecycleHooks>(hook => hook.OnServerStopping());

    public void NotifyServerStopped() => Dispatch<IGg2ServerLifecycleHooks>(hook => hook.OnServerStopped());

    public void NotifyHelloReceived(HelloReceivedEvent e) => Dispatch<IGg2ServerClientHooks>(hook => hook.OnHelloReceived(e));

    public void NotifyClientConnected(ClientConnectedEvent e) => Dispatch<IGg2ServerClientHooks>(hook => hook.OnClientConnected(e));

    public void NotifyClientDisconnected(ClientDisconnectedEvent e) => Dispatch<IGg2ServerClientHooks>(hook => hook.OnClientDisconnected(e));

    public void NotifyPasswordAccepted(PasswordAcceptedEvent e) => Dispatch<IGg2ServerClientHooks>(hook => hook.OnPasswordAccepted(e));

    public void NotifyPlayerTeamChanged(PlayerTeamChangedEvent e) => Dispatch<IGg2ServerClientHooks>(hook => hook.OnPlayerTeamChanged(e));

    public void NotifyPlayerClassChanged(PlayerClassChangedEvent e) => Dispatch<IGg2ServerClientHooks>(hook => hook.OnPlayerClassChanged(e));

    public void NotifyChatReceived(ChatReceivedEvent e) => Dispatch<IGg2ServerChatHooks>(hook => hook.OnChatReceived(e));

    public void NotifyMapChanging(MapChangingEvent e) => Dispatch<IGg2ServerMapHooks>(hook => hook.OnMapChanging(e));

    public void NotifyMapChanged(MapChangedEvent e) => Dispatch<IGg2ServerMapHooks>(hook => hook.OnMapChanged(e));

    public void ShutdownPlugins()
    {
        for (var index = _loadedPlugins.Count - 1; index >= 0; index -= 1)
        {
            try
            {
                _loadedPlugins[index].Plugin.Shutdown();
            }
            catch (Exception ex)
            {
                _log($"[plugin] shutdown failed for {_loadedPlugins[index].Plugin.Id}: {ex.Message}");
            }
        }
    }

    private IGg2ServerPluginContext CreateContext(IGg2ServerPlugin plugin)
    {
        var pluginDirectory = Path.Combine(_pluginsDirectory, plugin.Id);
        var configDirectory = Path.Combine(_pluginConfigRoot, plugin.Id);
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(_mapsDirectory);
        return new ServerPluginContext(
            plugin.Id,
            pluginDirectory,
            configDirectory,
            _mapsDirectory,
            _serverState,
            _adminOperations,
            _commandRegistry,
            _log);
    }

    private void Dispatch<THook>(Action<THook> callback) where THook : class
    {
        foreach (var loadedPlugin in _loadedPlugins)
        {
            if (loadedPlugin.Plugin is not THook hook)
            {
                continue;
            }

            try
            {
                callback(hook);
            }
            catch (Exception ex)
            {
                _log($"[plugin] hook failed for {loadedPlugin.Plugin.Id}: {ex.Message}");
            }
        }
    }
}
