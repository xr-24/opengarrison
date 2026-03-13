using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GG2.Core;
using GG2.Protocol;
using static ServerHelpers;

sealed class GameServer
{
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

    private UdpClient _udp = null!;
    private LobbyServerRegistrar? _lobbyRegistrar;
    private SimulationWorld _world = null!;
    private FixedStepSimulator _simulator = null!;
    private Stopwatch _clock = null!;
    private TimeSpan _previous;
    private Dictionary<byte, ClientSession> _clientsBySlot = null!;
    private ServerSessionManager _sessionManager = null!;
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
            Console.WriteLine);
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
            SendMessage);

        Console.WriteLine($"GG2.Server booting at {_config.TicksPerSecond} ticks/sec.");
        Console.WriteLine($"Protocol version: {ProtocolVersion.Current}");
        Console.WriteLine($"UDP bind: 0.0.0.0:{_port}");
        Console.WriteLine($"Name: {_serverName}");
        Console.WriteLine($"Max players: {_maxPlayableClients}");
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
        Console.WriteLine("[server] type \"shutdown\" to stop.");
        Console.WriteLine("Waiting for a UDP hello packet. Pass a different port as the first CLI argument to override 8190.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _helloRateLimiter.Prune();
                _passwordRateLimiter.Prune();
                _sessionManager.PruneTimedOutClients();
                PumpIncomingPackets();
                _sessionManager.RefreshPasswordRequests();

                _sessionManager.ApplyPlayableClientInputs();

                var now = _clock.Elapsed;
                var elapsedSeconds = (now - _previous).TotalSeconds;
                _previous = now;

                var ticks = _simulator.Step(elapsedSeconds);
                _lobbyRegistrar?.Tick(now, BuildLobbyServerName(_serverName, _world, _clientsBySlot, _passwordRequired, _maxPlayableClients));
                if (ticks > 0)
                {
                    _autoBalancer.Tick(now, ticks, _autoBalanceEnabled);
                }
                if (_mapRotationManager.TryApplyPendingMapChange())
                {
                    _snapshotBroadcaster.ResetTransientEvents();
                }
                _snapshotBroadcaster.BroadcastSnapshots(ticks);

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
            NotifyClientsOfShutdown();
            Console.WriteLine("[server] shutdown complete.");
        }
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
                        if (!client.HasAcceptedInput || IsSequenceNewer(input.Sequence, client.LastInputSequence))
                        {
                            client.LastInputSequence = input.Sequence;
                            client.LatestInput = ToCoreInput(input);
                            client.HasAcceptedInput = true;
                        }

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
}
