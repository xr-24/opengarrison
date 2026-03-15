using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GG2.Core;
using GG2.Protocol;
using GG2.Server.Plugins;
using static ServerHelpers;

sealed class GameServer
{
    private sealed record PendingConsoleCommand(
        string Command,
        bool EchoToConsole,
        TaskCompletionSource<IReadOnlyList<string>>? Completion);

    private const int WsaConnReset = 10054;
    private const int SioUdpConnReset = -1744830452;
    private const int MaxNewHelloAttemptsPerWindow = 8;
    private const int MaxPasswordFailuresPerWindow = 3;
    private static readonly TimeSpan HelloAttemptWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HelloCooldown = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PasswordFailureWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PasswordCooldown = TimeSpan.FromSeconds(10);

    private readonly SimulationConfig _config;
    private readonly int _port;
    private readonly string _serverName;
    private readonly string? _serverPassword;
    private readonly bool _useLobbyServer;
    private readonly string _lobbyHost;
    private readonly int _lobbyPort;
    private readonly string _protocolUuidString;
    private readonly int _lobbyHeartbeatSeconds;
    private readonly int _lobbyResolveSeconds;
    private readonly string? _requestedMap;
    private readonly string? _mapRotationFile;
    private readonly IReadOnlyList<string> _stockMapRotation;
    private readonly int _maxPlayableClients;
    private readonly int _maxTotalClients;
    private readonly int _maxSpectatorClients;
    private readonly int _autoBalanceDelaySeconds;
    private readonly int _autoBalanceNewPlayerGraceSeconds;
    private readonly bool _autoBalanceEnabled;
    private readonly int? _timeLimitMinutesOverride;
    private readonly int? _capLimitOverride;
    private readonly int? _respawnSecondsOverride;
    private readonly double _clientTimeoutSeconds;
    private readonly double _passwordTimeoutSeconds;
    private readonly double _passwordRetrySeconds;
    private readonly ulong _transientEventReplayTicks;
    private readonly bool _passwordRequired;
    private readonly byte[] _protocolUuidBytes;
    private readonly ConcurrentQueue<PendingConsoleCommand> _pendingConsoleCommands = new();

    private UdpClient _udp = null!;
    private LobbyServerRegistrar? _lobbyRegistrar;
    private SimulationWorld _world = null!;
    private FixedStepSimulator _simulator = null!;
    private Stopwatch _clock = null!;
    private TimeSpan _previous;
    private Dictionary<byte, ClientSession> _clientsBySlot = null!;
    private ServerSessionManager _sessionManager = null!;
    private GG2.Server.PluginCommandRegistry _pluginCommandRegistry = null!;
    private GG2.Server.PluginHost? _pluginHost;
    private AutoBalancer _autoBalancer = null!;
    private SnapshotBroadcaster _snapshotBroadcaster = null!;
    private MapRotationManager _mapRotationManager = null!;
    private EndpointRateLimiter _helloRateLimiter = null!;
    private EndpointRateLimiter _passwordRateLimiter = null!;

    public GameServer(
        SimulationConfig config,
        int port,
        string serverName,
        string? serverPassword,
        bool useLobbyServer,
        string lobbyHost,
        int lobbyPort,
        string protocolUuidString,
        int lobbyHeartbeatSeconds,
        int lobbyResolveSeconds,
        string? requestedMap,
        string? mapRotationFile,
        IReadOnlyList<string> stockMapRotation,
        int maxPlayableClients,
        int maxTotalClients,
        int maxSpectatorClients,
        int autoBalanceDelaySeconds,
        int autoBalanceNewPlayerGraceSeconds,
        bool autoBalanceEnabled,
        int? timeLimitMinutesOverride,
        int? capLimitOverride,
        int? respawnSecondsOverride,
        double clientTimeoutSeconds,
        double passwordTimeoutSeconds,
        double passwordRetrySeconds,
        ulong transientEventReplayTicks)
    {
        _config = config;
        _port = port;
        _serverName = serverName;
        _serverPassword = serverPassword;
        _useLobbyServer = useLobbyServer;
        _lobbyHost = lobbyHost;
        _lobbyPort = lobbyPort;
        _protocolUuidString = protocolUuidString;
        _lobbyHeartbeatSeconds = lobbyHeartbeatSeconds;
        _lobbyResolveSeconds = lobbyResolveSeconds;
        _requestedMap = requestedMap;
        _mapRotationFile = mapRotationFile;
        _stockMapRotation = stockMapRotation;
        _maxPlayableClients = maxPlayableClients;
        _maxTotalClients = maxTotalClients;
        _maxSpectatorClients = maxSpectatorClients;
        _autoBalanceDelaySeconds = autoBalanceDelaySeconds;
        _autoBalanceNewPlayerGraceSeconds = autoBalanceNewPlayerGraceSeconds;
        _autoBalanceEnabled = autoBalanceEnabled;
        _timeLimitMinutesOverride = timeLimitMinutesOverride;
        _capLimitOverride = capLimitOverride;
        _respawnSecondsOverride = respawnSecondsOverride;
        _clientTimeoutSeconds = clientTimeoutSeconds;
        _passwordTimeoutSeconds = passwordTimeoutSeconds;
        _passwordRetrySeconds = passwordRetrySeconds;
        _transientEventReplayTicks = transientEventReplayTicks;
        _passwordRequired = !string.IsNullOrWhiteSpace(serverPassword);
        _protocolUuidBytes = ParseProtocolUuid(protocolUuidString);
    }

    public void Run(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(_port);
        using var timerResolution = WindowsTimerResolutionScope.Create1Millisecond();
        _udp = udp;
        _udp.Client.Blocking = false;
        TryDisableUdpConnectionReset(_udp.Client);

        if (_useLobbyServer)
        {
            _lobbyRegistrar = new LobbyServerRegistrar(
                _udp,
                _lobbyHost,
                _lobbyPort,
                _protocolUuidBytes,
                _port,
                TimeSpan.FromSeconds(_lobbyHeartbeatSeconds),
                TimeSpan.FromSeconds(_lobbyResolveSeconds));
        }

        _world = new SimulationWorld(_config);
        if (_timeLimitMinutesOverride.HasValue || _capLimitOverride.HasValue || _respawnSecondsOverride.HasValue)
        {
            _world.ConfigureMatchDefaults(
                timeLimitMinutes: _timeLimitMinutesOverride,
                capLimit: _capLimitOverride,
                respawnSeconds: _respawnSecondsOverride);
        }
        _world.AutoRestartOnMapChange = false;
        _mapRotationManager = new MapRotationManager(_world, _requestedMap, _mapRotationFile, _stockMapRotation, Console.WriteLine);
        _world.DespawnEnemyDummy();
        _world.TryPrepareNetworkPlayerJoin(SimulationWorld.LocalPlayerSlot);

        _simulator = new FixedStepSimulator(_world);
        _clock = Stopwatch.StartNew();
        _previous = _clock.Elapsed;
        _clientsBySlot = new Dictionary<byte, ClientSession>();
        _helloRateLimiter = new EndpointRateLimiter(MaxNewHelloAttemptsPerWindow, HelloAttemptWindow, HelloCooldown, () => _clock.Elapsed);
        _passwordRateLimiter = new EndpointRateLimiter(MaxPasswordFailuresPerWindow, PasswordFailureWindow, PasswordCooldown, () => _clock.Elapsed);

        _sessionManager = new ServerSessionManager(
            _world,
            _clientsBySlot,
            _maxPlayableClients,
            _maxTotalClients,
            _maxSpectatorClients,
            () => _clock.Elapsed,
            _serverPassword,
            _passwordRequired,
            _clientTimeoutSeconds,
            _passwordTimeoutSeconds,
            _passwordRetrySeconds,
            GetPasswordRateLimitReason,
            RecordPasswordFailure,
            ClearPasswordFailures,
            SendMessage,
            Console.WriteLine,
            OnClientRemoved,
            OnPasswordAccepted,
            OnPlayerTeamChanged,
            OnPlayerClassChanged);
        _autoBalancer = new AutoBalancer(
            _world,
            _config,
            _clientsBySlot,
            _autoBalanceDelaySeconds,
            _autoBalanceNewPlayerGraceSeconds,
            _passwordRequired,
            SendMessage,
            Console.WriteLine);
        _snapshotBroadcaster = new SnapshotBroadcaster(
            _world,
            _config,
            _clientsBySlot,
            _transientEventReplayTicks,
            SendSnapshotPayload);
        InitializePluginRuntime();
        _pluginHost?.LoadPlugins();
        _pluginHost?.NotifyServerStarting();

        Console.WriteLine($"GG2.Server booting at {_config.TicksPerSecond} ticks/sec.");
        Console.WriteLine($"Protocol version: {ProtocolVersion.Current}");
        Console.WriteLine($"UDP bind: 0.0.0.0:{_port}");
        Console.WriteLine($"Name: {_serverName}");
        Console.WriteLine($"Max players: {_maxPlayableClients}");
        if (timerResolution.IsActive)
        {
            Console.WriteLine("[server] high-resolution timer enabled (1 ms).");
        }
        if (_timeLimitMinutesOverride.HasValue)
        {
            Console.WriteLine($"Time limit: {_timeLimitMinutesOverride.Value} minutes");
        }
        if (_capLimitOverride.HasValue)
        {
            Console.WriteLine($"Cap limit: {_capLimitOverride.Value}");
        }
        if (_respawnSecondsOverride.HasValue)
        {
            Console.WriteLine($"Respawn: {_respawnSecondsOverride.Value} seconds");
        }
        Console.WriteLine($"Auto-balance: {(_autoBalanceEnabled ? "Enabled" : "Disabled")}");
        Console.WriteLine($"Level: {_world.Level.Name} area={_world.Level.MapAreaIndex}/{_world.Level.MapAreaCount} imported={_world.Level.ImportedFromSource} mode={_world.MatchRules.Mode}");
        Console.WriteLine($"World bounds: {_world.Bounds.Width}x{_world.Bounds.Height}");
        Console.WriteLine(_passwordRequired ? "[server] password required" : "[server] no password set");
        if (_useLobbyServer)
        {
            Console.WriteLine($"[server] lobby registration enabled host={_lobbyHost}:{_lobbyPort}");
        }
        Console.WriteLine("[server] type \"help\" for commands. Type \"shutdown\" to stop.");
        foreach (var line in BuildConsoleCommandResponse("status"))
        {
            Console.WriteLine(line);
        }

        foreach (var line in BuildConsoleCommandResponse("rotation"))
        {
            Console.WriteLine(line);
        }
        Console.WriteLine("Waiting for a UDP hello packet. Pass a different port as the first CLI argument to override 8190.");
        _pluginHost?.NotifyServerStarted();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ProcessPendingConsoleCommands();
                _helloRateLimiter.Prune();
                _passwordRateLimiter.Prune();
                _sessionManager.PruneTimedOutClients();
                PumpIncomingPackets();
                _sessionManager.RefreshPasswordRequests();

                var now = _clock.Elapsed;
                var elapsedSeconds = (now - _previous).TotalSeconds;
                _previous = now;

                var ticks = ServerSimulationBatch.Advance(
                    _simulator,
                    elapsedSeconds,
                    _sessionManager.PreparePlayableClientInputsForNextTick,
                    () =>
                    {
                        _autoBalancer.Tick(now, 1, _autoBalanceEnabled);
                        if (_mapRotationManager.TryApplyPendingMapChange(out var transition))
                        {
                            NotifyMapTransition(transition);
                            _snapshotBroadcaster.ResetTransientEvents();
                        }
                    },
                    _snapshotBroadcaster.BroadcastSnapshot);
                _lobbyRegistrar?.Tick(now, BuildLobbyServerName(_serverName, _world, _clientsBySlot, _passwordRequired, _maxPlayableClients));

                if (ticks > 0 && _world.Frame % _config.TicksPerSecond == 0)
                {
                    var activePlayableCount = _world.EnumerateActiveNetworkPlayers().Count();
                    Console.WriteLine(
                        $"[server] frame={_world.Frame} clients={_clientsBySlot.Count} " +
                        $"mode={_world.MatchRules.Mode} phase={_world.MatchState.Phase} hp={_world.LocalPlayer.Health}/{_world.LocalPlayer.MaxHealth} " +
                        $"ammo={_world.LocalPlayer.CurrentShells}/{_world.LocalPlayer.MaxShells} pos=({_world.LocalPlayer.X:F1},{_world.LocalPlayer.Y:F1}) " +
                        $"activePlayable={activePlayableCount} spectators={_clientsBySlot.Keys.Count(IsSpectatorSlot)} caps={_world.RedCaps}-{_world.BlueCaps}");
                }

                Thread.Sleep(1);
            }
        }
        finally
        {
            _pluginHost?.NotifyServerStopping();
            NotifyClientsOfShutdown();
            _pluginHost?.NotifyServerStopped();
            _pluginHost?.ShutdownPlugins();
            Console.WriteLine("[server] shutdown complete.");
        }
    }

    public void EnqueueConsoleCommand(string command)
    {
        if (!string.IsNullOrWhiteSpace(command))
        {
            _pendingConsoleCommands.Enqueue(new PendingConsoleCommand(command.Trim(), EchoToConsole: true, Completion: null));
        }
    }

    public Task<IReadOnlyList<string>> ExecuteAdminCommandAsync(string command, bool echoToConsole, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested)
        {
            tcs.TrySetCanceled(cancellationToken);
            return tcs.Task;
        }

        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _pendingConsoleCommands.Enqueue(new PendingConsoleCommand(command.Trim(), echoToConsole, tcs));
        return tcs.Task;
    }

    private void PumpIncomingPackets()
    {
        while (_udp.Available > 0)
        {
            try
            {
                IPEndPoint remoteEndPoint = new(IPAddress.Any, 0);
                var payload = _udp.Receive(ref remoteEndPoint);
                if (!ProtocolCodec.TryDeserialize(payload, out var message) || message is null)
                {
                    continue;
                }

                switch (message)
                {
                    case ServerStatusRequestMessage:
                        SendServerStatus(remoteEndPoint);
                        break;
                    case HelloMessage hello:
                        HandleHello(hello, remoteEndPoint);
                        break;
                    case PasswordSubmitMessage passwordSubmit:
                        var passwordClient = FindClient(_clientsBySlot, remoteEndPoint);
                        if (passwordClient is null)
                        {
                            break;
                        }

                        passwordClient.LastSeen = _clock.Elapsed;
                        _sessionManager.HandlePasswordSubmit(passwordClient, passwordSubmit);
                        break;
                    case ChatSubmitMessage chatSubmit:
                        var chatClient = FindClient(_clientsBySlot, remoteEndPoint);
                        if (chatClient is null)
                        {
                            break;
                        }

                        chatClient.LastSeen = _clock.Elapsed;
                        if (!chatClient.IsAuthorized && _passwordRequired)
                        {
                            break;
                        }

                        BroadcastChat(chatClient, chatSubmit.Text);
                        break;
                    case SnapshotAckMessage snapshotAck:
                        var ackClient = FindClient(_clientsBySlot, remoteEndPoint);
                        if (ackClient is null)
                        {
                            break;
                        }

                        ackClient.LastSeen = _clock.Elapsed;
                        ackClient.AcknowledgeSnapshot(snapshotAck.Frame);
                        break;
                    case InputStateMessage input:
                        var client = FindClient(_clientsBySlot, remoteEndPoint);
                        if (client is null)
                        {
                            break;
                        }

                        client.LastSeen = _clock.Elapsed;
                        if (!client.IsAuthorized && _passwordRequired)
                        {
                            break;
                        }
                        client.TrySetLatestInput(input.Sequence, ToCoreInput(input));

                        if (input.ChatBubbleFrameIndex >= 0)
                        {
                            _world.TryTriggerNetworkPlayerChatBubble(client.Slot, input.ChatBubbleFrameIndex);
                        }
                        break;
                    case ControlCommandMessage command:
                        var controlClient = FindClient(_clientsBySlot, remoteEndPoint);
                        if (controlClient is null)
                        {
                            break;
                        }

                        controlClient.LastSeen = _clock.Elapsed;
                        if (!controlClient.IsAuthorized && _passwordRequired)
                        {
                            break;
                        }
                        _sessionManager.HandleControlCommand(controlClient, command);
                        break;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset || ex.ErrorCode == WsaConnReset)
            {
                Console.WriteLine("[server] ignoring UDP connection reset from disconnected client");
            }
        }
    }

    private void HandleHello(HelloMessage hello, IPEndPoint remoteEndPoint)
    {
        _pluginHost?.NotifyHelloReceived(new HelloReceivedEvent(hello.Name, remoteEndPoint.ToString(), hello.Version));
        if (hello.Version != ProtocolVersion.Current)
        {
            Console.WriteLine($"[server] rejected client {remoteEndPoint} due to protocol mismatch client={hello.Version} server={ProtocolVersion.Current}");
            SendMessage(remoteEndPoint, new ConnectionDeniedMessage("Protocol mismatch."));
            return;
        }

        var existingClient = FindClient(_clientsBySlot, remoteEndPoint);
        if (existingClient is not null)
        {
            existingClient.Name = hello.Name;
            existingClient.LastSeen = _clock.Elapsed;
            _sessionManager.ApplyClientName(existingClient.Slot, hello.Name);
            SendMessage(remoteEndPoint, new WelcomeMessage(_serverName, ProtocolVersion.Current, _config.TicksPerSecond, _world.Level.Name, existingClient.Slot));
            if (_passwordRequired && !existingClient.IsAuthorized)
            {
                SendMessage(remoteEndPoint, new PasswordRequestMessage());
                existingClient.LastPasswordRequestSentAt = _clock.Elapsed;
            }
            Console.WriteLine($"[server] client refreshed {remoteEndPoint} slot={existingClient.Slot} name=\"{hello.Name}\" version={hello.Version}");
            return;
        }

        if (GetHelloRateLimitReason(remoteEndPoint) is { } rateLimitReason)
        {
            Console.WriteLine($"[server] rejected client {remoteEndPoint}; {rateLimitReason}");
            SendMessage(remoteEndPoint, new ConnectionDeniedMessage(rateLimitReason));
            return;
        }

        var assignedSlot = FindAvailableSlot(_clientsBySlot, _maxTotalClients, _maxSpectatorClients, _maxPlayableClients);
        if (assignedSlot == 0)
        {
            Console.WriteLine($"[server] rejected client {remoteEndPoint}; server is full");
            SendMessage(remoteEndPoint, new ConnectionDeniedMessage("Server is full."));
            return;
        }

        var client = new ClientSession(assignedSlot, remoteEndPoint, hello.Name, _clock.Elapsed)
        {
            IsAuthorized = !_passwordRequired,
        };
        _clientsBySlot[assignedSlot] = client;
        _sessionManager.ApplyClientName(assignedSlot, hello.Name);
        if (SimulationWorld.IsPlayableNetworkPlayerSlot(assignedSlot))
        {
            _world.TryPrepareNetworkPlayerJoin(assignedSlot);
        }

        var welcome = new WelcomeMessage(
            _serverName,
            ProtocolVersion.Current,
            _config.TicksPerSecond,
            _world.Level.Name,
            assignedSlot);
        SendMessage(remoteEndPoint, welcome);
        if (_passwordRequired && !client.IsAuthorized)
        {
            SendMessage(remoteEndPoint, new PasswordRequestMessage());
            client.LastPasswordRequestSentAt = _clock.Elapsed;
        }

        _helloRateLimiter.Reset(remoteEndPoint);
        _passwordRateLimiter.Reset(remoteEndPoint);
        Console.WriteLine($"[server] client connected {remoteEndPoint} slot={assignedSlot} name=\"{hello.Name}\" version={hello.Version}");
        _pluginHost?.NotifyClientConnected(new ClientConnectedEvent(
            assignedSlot,
            hello.Name,
            remoteEndPoint.ToString(),
            client.IsAuthorized,
            IsSpectatorSlot(assignedSlot)));
    }

    private void ProcessPendingConsoleCommands()
    {
        while (_pendingConsoleCommands.TryDequeue(out var request))
        {
            var lines = BuildConsoleCommandResponse(request.Command);
            if (request.EchoToConsole)
            {
                foreach (var line in lines)
                {
                    Console.WriteLine(line);
                }
            }

            request.Completion?.TrySetResult(lines);
        }
    }

    private List<string> BuildConsoleCommandResponse(string command)
    {
        var normalized = command.Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        if (_pluginCommandRegistry.TryExecute(normalized, CreateCommandContext(), CancellationToken.None, out var responseLines))
        {
            return responseLines.ToList();
        }

        return [$"[server] unknown command \"{normalized}\". Type help for commands."];
    }

    private void AddConsoleStatusSummary(List<string> lines)
    {
        var activePlayableCount = _world.EnumerateActiveNetworkPlayers().Count();
        var spectatorCount = _clientsBySlot.Keys.Count(IsSpectatorSlot);
        var uptime = FormatDuration(_clock.Elapsed);
        var lobbyValue = _useLobbyServer ? $"enabled {_lobbyHost}:{_lobbyPort}" : "disabled";
        var passwordValue = _passwordRequired ? "required" : "off";
        lines.Add(
            $"[server] status | name={_serverName} | port={_port} | tickrate={_config.TicksPerSecond} | players={activePlayableCount}/{_maxPlayableClients} | spectators={spectatorCount} | map={_world.Level.Name} area={_world.Level.MapAreaIndex}/{_world.Level.MapAreaCount} | mode={_world.MatchRules.Mode} | phase={_world.MatchState.Phase} | score={_world.RedCaps}-{_world.BlueCaps} | lobby={lobbyValue} | password={passwordValue} | uptime={uptime}");
    }

    private void AddConsoleRulesSummary(List<string> lines)
    {
        var autoBalanceValue = _autoBalanceEnabled ? "enabled" : "disabled";
        var respawnSeconds = _respawnSecondsOverride ?? 5;
        lines.Add(
            $"[server] rules | timeLimit={_world.MatchRules.TimeLimitMinutes} | capLimit={_world.MatchRules.CapLimit} | respawn={respawnSeconds} | autoBalance={autoBalanceValue}");
    }

    private void AddConsoleLobbySummary(List<string> lines)
    {
        var lobbyValue = _useLobbyServer ? "enabled" : "disabled";
        lines.Add($"[server] lobby | enabled={lobbyValue} | host={_lobbyHost} | port={_lobbyPort}");
    }

    private void AddConsoleMapSummary(List<string> lines)
    {
        var winner = _world.MatchState.WinnerTeam?.ToString() ?? "none";
        lines.Add(
            $"[server] map | name={_world.Level.Name} | area={_world.Level.MapAreaIndex}/{_world.Level.MapAreaCount} | mode={_world.MatchRules.Mode} | phase={_world.MatchState.Phase} | winner={winner} | imported={_world.Level.ImportedFromSource}");
        lines.Add($"[server] world | bounds={_world.Bounds.Width}x{_world.Bounds.Height}");
    }

    private void AddConsoleRotationSummary(List<string> lines)
    {
        var rotation = _mapRotationManager.MapRotation;
        var currentIndex = rotation.Count == 0 ? 0 : Math.Clamp(_mapRotationManager.CurrentRotationIndex + 1, 1, rotation.Count);
        var source = string.IsNullOrWhiteSpace(_mapRotationFile) ? "stock" : _mapRotationFile!;
        var entries = rotation.Count == 0 ? _world.Level.Name : string.Join(", ", rotation);
        lines.Add($"[server] rotation | source={source} | current={currentIndex}/{Math.Max(1, rotation.Count)} | entries={entries}");
    }

    private void AddConsolePlayersSummary(List<string> lines)
    {
        if (_clientsBySlot.Count == 0)
        {
            lines.Add("[server] players | count=0");
            return;
        }

        lines.Add($"[server] players | count={_clientsBySlot.Count}");
        foreach (var client in _clientsBySlot.Values.OrderBy(entry => entry.Slot))
        {
            var role = "Spectator";
            if (!IsSpectatorSlot(client.Slot))
            {
                role = _world.TryGetNetworkPlayer(client.Slot, out var player)
                    ? player.Team.ToString()
                    : "Unassigned";
            }

            var connectedFor = FormatDuration(_clock.Elapsed - client.ConnectedAt);
            var authorized = client.IsAuthorized ? "yes" : "pending";
            lines.Add(
                $"[server] player | slot={client.Slot} | name={client.Name} | role={role} | authorized={authorized} | endpoint={client.EndPoint} | connected={connectedFor}");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1d
            ? duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void SendServerStatus(IPEndPoint remoteEndPoint)
    {
        var playerCount = _clientsBySlot.Count;
        var spectatorCount = _clientsBySlot.Keys.Count(IsSpectatorSlot);
        SendMessage(
            remoteEndPoint,
            new ServerStatusResponseMessage(
                _serverName,
                _world.Level.Name,
                (byte)_world.MatchRules.Mode,
                playerCount - spectatorCount,
                _maxPlayableClients,
                spectatorCount));
    }

    private void BroadcastChat(ClientSession client, string text)
    {
        var sanitized = text.Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        if (sanitized.Length > 120)
        {
            sanitized = sanitized[..120];
        }

        var team = SimulationWorld.IsPlayableNetworkPlayerSlot(client.Slot) && _world.TryGetNetworkPlayer(client.Slot, out var player)
            ? (byte)player.Team
            : (byte)0;
        _pluginHost?.NotifyChatReceived(new ChatReceivedEvent(
            client.Slot,
            client.Name,
            sanitized,
            team == 0 ? null : (PlayerTeam)team));
        var relay = new ChatRelayMessage(team, client.Name, sanitized);
        foreach (var session in _clientsBySlot.Values)
        {
            SendMessage(session.EndPoint, relay);
        }

        Console.WriteLine($"[chat] {client.Name}: {sanitized}");
    }

    private void SendMessage(IPEndPoint remoteEndPoint, IProtocolMessage message)
    {
        var payload = ProtocolCodec.Serialize(message);
        SendPayload(remoteEndPoint, payload);
    }

    private void SendSnapshotPayload(IPEndPoint remoteEndPoint, SnapshotMessage snapshot, byte[] payload)
    {
        SendPayload(remoteEndPoint, payload);
    }

    private void SendPayload(IPEndPoint remoteEndPoint, byte[] payload)
    {
        _udp.Send(payload, payload.Length, remoteEndPoint);
    }

    private string? GetHelloRateLimitReason(IPEndPoint remoteEndPoint)
    {
        if (_passwordRateLimiter.IsLimited(remoteEndPoint, out var passwordRetryAfter))
        {
            return BuildRetryMessage("Too many password attempts", passwordRetryAfter);
        }

        if (!_helloRateLimiter.TryConsume(remoteEndPoint, out var helloRetryAfter))
        {
            return BuildRetryMessage("Too many connection attempts", helloRetryAfter);
        }

        return null;
    }

    private string? GetPasswordRateLimitReason(IPEndPoint remoteEndPoint)
    {
        if (!_passwordRateLimiter.IsLimited(remoteEndPoint, out var retryAfter))
        {
            return null;
        }

        return BuildRetryMessage("Too many password attempts", retryAfter);
    }

    private void RecordPasswordFailure(IPEndPoint remoteEndPoint)
    {
        _passwordRateLimiter.TryConsume(remoteEndPoint, out _);
    }

    private void ClearPasswordFailures(IPEndPoint remoteEndPoint)
    {
        _passwordRateLimiter.Reset(remoteEndPoint);
    }

    private void NotifyClientsOfShutdown()
    {
        if (_clientsBySlot is null || _clientsBySlot.Count == 0)
        {
            return;
        }

        foreach (var client in _clientsBySlot.Values)
        {
            try
            {
                SendMessage(client.EndPoint, new ConnectionDeniedMessage("Server shutting down."));
            }
            catch
            {
            }
        }
    }

    private static string BuildRetryMessage(string prefix, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        return $"{prefix}. Try again in {seconds}s.";
    }

    private static void TryDisableUdpConnectionReset(Socket socket)
    {
        try
        {
            socket.IOControl((IOControlCode)SioUdpConnReset, [0, 0, 0, 0], null);
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private void InitializePluginRuntime()
    {
        _pluginCommandRegistry = new GG2.Server.PluginCommandRegistry();
        RegisterBuiltInCommands();
        _pluginHost = new GG2.Server.PluginHost(
            _pluginCommandRegistry,
            new GG2.Server.ServerReadOnlyStateView(() => _serverName, () => _world, () => _clientsBySlot),
            new GG2.Server.ServerAdminOperations(
                Console.WriteLine,
                SendMessage,
                () => _clientsBySlot,
                () => _sessionManager,
                () => _world,
                () => _mapRotationManager,
                () => _snapshotBroadcaster,
                evt => _pluginHost?.NotifyMapChanging(evt),
                evt => _pluginHost?.NotifyMapChanged(evt)),
            Path.Combine(RuntimePaths.ApplicationRoot, "Plugins"),
            Path.Combine(RuntimePaths.ConfigDirectory, "plugins"),
            Path.Combine(RuntimePaths.ApplicationRoot, "Maps"),
            Console.WriteLine);
    }

    private Gg2ServerCommandContext CreateCommandContext()
    {
        return new Gg2ServerCommandContext(
            new GG2.Server.ServerReadOnlyStateView(() => _serverName, () => _world, () => _clientsBySlot),
            new GG2.Server.ServerAdminOperations(
                Console.WriteLine,
                SendMessage,
                () => _clientsBySlot,
                () => _sessionManager,
                () => _world,
                () => _mapRotationManager,
                () => _snapshotBroadcaster,
                evt => _pluginHost?.NotifyMapChanging(evt),
                evt => _pluginHost?.NotifyMapChanged(evt)));
    }

    private void RegisterBuiltInCommands()
    {
        _pluginCommandRegistry.RegisterBuiltIn(
            "help",
            "Show server and plugin commands.",
            "help",
            (context, _, _) => Task.FromResult<IReadOnlyList<string>>(BuildHelpLines()),
            "?");
        _pluginCommandRegistry.RegisterBuiltIn(
            "status",
            "Show overall server status.",
            "status",
            (context, _, _) =>
            {
                var lines = new List<string>();
                AddConsoleStatusSummary(lines);
                AddConsoleRulesSummary(lines);
                AddConsoleLobbySummary(lines);
                AddConsoleMapSummary(lines);
                return Task.FromResult<IReadOnlyList<string>>(lines);
            },
            "info");
        _pluginCommandRegistry.RegisterBuiltIn(
            "players",
            "List connected players.",
            "players",
            (context, _, _) =>
            {
                var lines = new List<string>();
                AddConsolePlayersSummary(lines);
                return Task.FromResult<IReadOnlyList<string>>(lines);
            },
            "who");
        _pluginCommandRegistry.RegisterBuiltIn(
            "map",
            "Show current map details.",
            "map",
            (context, _, _) =>
            {
                var lines = new List<string>();
                AddConsoleMapSummary(lines);
                return Task.FromResult<IReadOnlyList<string>>(lines);
            },
            "level");
        _pluginCommandRegistry.RegisterBuiltIn(
            "rules",
            "Show match rules.",
            "rules",
            (context, _, _) =>
            {
                var lines = new List<string>();
                AddConsoleRulesSummary(lines);
                return Task.FromResult<IReadOnlyList<string>>(lines);
            });
        _pluginCommandRegistry.RegisterBuiltIn(
            "lobby",
            "Show lobby registration state.",
            "lobby",
            (context, _, _) =>
            {
                var lines = new List<string>();
                AddConsoleLobbySummary(lines);
                return Task.FromResult<IReadOnlyList<string>>(lines);
            });
        _pluginCommandRegistry.RegisterBuiltIn(
            "rotation",
            "Show the active map rotation.",
            "rotation",
            (context, _, _) =>
            {
                var lines = new List<string>();
                AddConsoleRotationSummary(lines);
                return Task.FromResult<IReadOnlyList<string>>(lines);
            },
            "maps");
        _pluginCommandRegistry.RegisterBuiltIn(
            "plugins",
            "List loaded server plugins.",
            "plugins",
            (context, _, _) => Task.FromResult<IReadOnlyList<string>>(BuildPluginLines()));
    }

    private IReadOnlyList<string> BuildHelpLines()
    {
        var lines = new List<string>
        {
            "[server] commands:",
        };
        foreach (var command in _pluginCommandRegistry.GetPrimaryCommands())
        {
            var ownerSuffix = command.IsBuiltIn ? string.Empty : $" [plugin:{command.OwnerId}]";
            lines.Add($"[server]   {command.Name} - {command.Description} ({command.Usage}){ownerSuffix}");
        }

        lines.Add("[server] shutdown is handled directly by the host console/admin pipe.");
        return lines;
    }

    private IReadOnlyList<string> BuildPluginLines()
    {
        var pluginIds = _pluginHost?.LoadedPluginIds ?? [];
        if (pluginIds.Count == 0)
        {
            return ["[server] plugins | count=0"];
        }

        return
        [
            $"[server] plugins | count={pluginIds.Count}",
            .. pluginIds.Select(pluginId => $"[server] plugin | id={pluginId}")
        ];
    }

    private void OnClientRemoved(ClientSession client, string reason)
    {
        _pluginHost?.NotifyClientDisconnected(new ClientDisconnectedEvent(
            client.Slot,
            client.Name,
            client.EndPoint.ToString(),
            reason,
            client.IsAuthorized));
    }

    private void OnPasswordAccepted(ClientSession client)
    {
        _pluginHost?.NotifyPasswordAccepted(new PasswordAcceptedEvent(
            client.Slot,
            client.Name,
            client.EndPoint.ToString()));
    }

    private void OnPlayerTeamChanged(ClientSession client, PlayerTeam team)
    {
        _pluginHost?.NotifyPlayerTeamChanged(new PlayerTeamChangedEvent(client.Slot, client.Name, team));
    }

    private void OnPlayerClassChanged(ClientSession client, PlayerClass playerClass)
    {
        _pluginHost?.NotifyPlayerClassChanged(new PlayerClassChangedEvent(client.Slot, client.Name, playerClass));
    }

    private void NotifyMapTransition(MapChangeTransition transition)
    {
        _pluginHost?.NotifyMapChanging(new MapChangingEvent(
            transition.CurrentLevelName,
            transition.CurrentAreaIndex,
            transition.CurrentAreaCount,
            transition.NextLevelName,
            transition.NextAreaIndex,
            transition.PreservePlayerStats,
            transition.WinnerTeam));
        _pluginHost?.NotifyMapChanged(new MapChangedEvent(
            _world.Level.Name,
            _world.Level.MapAreaIndex,
            _world.Level.MapAreaCount,
            _world.MatchRules.Mode));
    }
}
