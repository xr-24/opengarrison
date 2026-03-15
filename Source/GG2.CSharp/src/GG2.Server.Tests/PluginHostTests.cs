using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GG2.Core;
using GG2.Server;
using GG2.Server.Plugins;
using Xunit;

namespace GG2.Server.Tests;

public sealed class PluginHostTests
{
    [Fact]
    public void PluginCommandRegistry_TracksBuiltInAndPluginCommands()
    {
        var registry = new PluginCommandRegistry();

        registry.RegisterBuiltIn(
            "status",
            "Show server status.",
            "status",
            (_, _, _) => Task.FromResult<IReadOnlyList<string>>(["status"]));
        registry.RegisterPluginCommand(new TestCommand("motd"), "sample.plugin");

        var commands = registry.GetPrimaryCommands();

        Assert.Contains(commands, command => command.Name == "status" && command.IsBuiltIn);
        Assert.Contains(commands, command => command.Name == "motd" && !command.IsBuiltIn && command.OwnerId == "sample.plugin");
    }

    [Fact]
    public void PluginHost_LoadsPlugin_RegistersCommands_AndDispatchesHooks()
    {
        RecordingPlugin.Reset();
        var logs = new List<string>();
        var commandRegistry = new PluginCommandRegistry();
        var host = new PluginHost(
            commandRegistry,
            new TestState(),
            new TestAdminOperations(),
            CreateTempDirectory(),
            CreateTempDirectory(),
            CreateTempDirectory(),
            logs.Add);

        host.LoadPlugins([typeof(RecordingPlugin).Assembly]);

        var plugin = RecordingPlugin.Instance;
        Assert.NotNull(plugin);
        Assert.True(commandRegistry.TryExecute(
            "plugin-echo hello world",
            new Gg2ServerCommandContext(new TestState(), new TestAdminOperations()),
            CancellationToken.None,
            out var response));
        Assert.Equal(["plugin:hello world", "level=ctf_truefort"], response);

        host.NotifyServerStarting();
        host.NotifyServerStarted();
        host.NotifyHelloReceived(new HelloReceivedEvent("Alice", "127.0.0.1:8190", 1));
        host.NotifyClientConnected(new ClientConnectedEvent(1, "Alice", "127.0.0.1:8190", true, false));
        host.NotifyPasswordAccepted(new PasswordAcceptedEvent(1, "Alice", "127.0.0.1:8190"));
        host.NotifyPlayerTeamChanged(new PlayerTeamChangedEvent(1, "Alice", PlayerTeam.Red));
        host.NotifyPlayerClassChanged(new PlayerClassChangedEvent(1, "Alice", PlayerClass.Scout));
        host.NotifyChatReceived(new ChatReceivedEvent(1, "Alice", "hello", PlayerTeam.Red));
        host.NotifyMapChanging(new MapChangingEvent("ctf_truefort", 1, 1, "cp_egypt", 1, false, PlayerTeam.Red));
        host.NotifyMapChanged(new MapChangedEvent("cp_egypt", 1, 1, GameModeKind.ControlPoint));
        host.NotifyClientDisconnected(new ClientDisconnectedEvent(1, "Alice", "127.0.0.1:8190", "quit", true));
        host.NotifyServerStopping();
        host.NotifyServerStopped();
        host.ShutdownPlugins();

        Assert.Equal("recording.plugin", plugin!.Context?.PluginId);
        Assert.Contains(plugin.Events, entry => entry == "lifecycle:starting");
        Assert.Contains(plugin.Events, entry => entry == "lifecycle:started");
        Assert.Contains(plugin.Events, entry => entry == "hello:Alice");
        Assert.Contains(plugin.Events, entry => entry == "connected:1");
        Assert.Contains(plugin.Events, entry => entry == "password:1");
        Assert.Contains(plugin.Events, entry => entry == "team:1:Red");
        Assert.Contains(plugin.Events, entry => entry == "class:1:Scout");
        Assert.Contains(plugin.Events, entry => entry == "chat:hello");
        Assert.Contains(plugin.Events, entry => entry == "map-changing:cp_egypt:1");
        Assert.Contains(plugin.Events, entry => entry == "map-changed:cp_egypt:ControlPoint");
        Assert.Contains(plugin.Events, entry => entry == "disconnected:quit");
        Assert.Contains(plugin.Events, entry => entry == "lifecycle:stopping");
        Assert.Contains(plugin.Events, entry => entry == "lifecycle:stopped");
        Assert.True(plugin.ShutdownCalled);
        Assert.Contains(logs, line => line.Contains("loaded Recording Plugin", StringComparison.Ordinal));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gg2-plugin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestState : IGg2ServerReadOnlyState
    {
        public string ServerName => "Test Server";

        public string LevelName => "ctf_truefort";

        public int MapAreaIndex => 1;

        public int MapAreaCount => 1;

        public GameModeKind GameMode => GameModeKind.CaptureTheFlag;

        public MatchPhase MatchPhase => MatchPhase.Running;

        public int RedCaps => 0;

        public int BlueCaps => 0;

        public IReadOnlyList<Gg2ServerPlayerInfo> GetPlayers() => [];
    }

    private sealed class TestAdminOperations : IGg2ServerAdminOperations
    {
        public void BroadcastSystemMessage(string text)
        {
        }

        public bool TryDisconnect(byte slot, string reason) => true;

        public bool TryMoveToSpectator(byte slot) => true;

        public bool TrySetTeam(byte slot, PlayerTeam team) => true;

        public bool TrySetClass(byte slot, PlayerClass playerClass) => true;

        public bool TryChangeMap(string levelName, int mapAreaIndex = 1, bool preservePlayerStats = false) => true;
    }

    private sealed class TestCommand : IGg2ServerCommand
    {
        public TestCommand(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Description => "Test command";

        public string Usage => Name;

        public Task<IReadOnlyList<string>> ExecuteAsync(
            Gg2ServerCommandContext context,
            string arguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>([$"arguments={arguments}", $"level={context.ServerState.LevelName}"]);
        }
    }

    public sealed class RecordingPlugin :
        IGg2ServerPlugin,
        IGg2ServerLifecycleHooks,
        IGg2ServerClientHooks,
        IGg2ServerChatHooks,
        IGg2ServerMapHooks
    {
        public static RecordingPlugin? Instance { get; private set; }

        public static void Reset()
        {
            Instance = null;
        }

        public string Id => "recording.plugin";

        public string DisplayName => "Recording Plugin";

        public Version Version => new(1, 0, 0);

        public IGg2ServerPluginContext? Context { get; private set; }

        public List<string> Events { get; } = [];

        public bool ShutdownCalled { get; private set; }

        public void Initialize(IGg2ServerPluginContext context)
        {
            Context = context;
            Instance = this;
            context.RegisterCommand(new PluginEchoCommand());
            context.Log("initialized");
        }

        public void Shutdown()
        {
            ShutdownCalled = true;
        }

        public void OnServerStarting() => Events.Add("lifecycle:starting");

        public void OnServerStarted() => Events.Add("lifecycle:started");

        public void OnServerStopping() => Events.Add("lifecycle:stopping");

        public void OnServerStopped() => Events.Add("lifecycle:stopped");

        public void OnHelloReceived(HelloReceivedEvent e) => Events.Add($"hello:{e.PlayerName}");

        public void OnClientConnected(ClientConnectedEvent e) => Events.Add($"connected:{e.Slot}");

        public void OnClientDisconnected(ClientDisconnectedEvent e) => Events.Add($"disconnected:{e.Reason}");

        public void OnPasswordAccepted(PasswordAcceptedEvent e) => Events.Add($"password:{e.Slot}");

        public void OnPlayerTeamChanged(PlayerTeamChangedEvent e) => Events.Add($"team:{e.Slot}:{e.Team}");

        public void OnPlayerClassChanged(PlayerClassChangedEvent e) => Events.Add($"class:{e.Slot}:{e.PlayerClass}");

        public void OnChatReceived(ChatReceivedEvent e) => Events.Add($"chat:{e.Text}");

        public void OnMapChanging(MapChangingEvent e) => Events.Add($"map-changing:{e.NextLevelName}:{e.NextAreaIndex}");

        public void OnMapChanged(MapChangedEvent e) => Events.Add($"map-changed:{e.LevelName}:{e.Mode}");

        private sealed class PluginEchoCommand : IGg2ServerCommand
        {
            public string Name => "plugin-echo";

            public string Description => "Echoes plugin arguments.";

            public string Usage => "plugin-echo <text>";

            public Task<IReadOnlyList<string>> ExecuteAsync(
                Gg2ServerCommandContext context,
                string arguments,
                CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<string>>([$"plugin:{arguments}", $"level={context.ServerState.LevelName}"]);
            }
        }
    }
}
