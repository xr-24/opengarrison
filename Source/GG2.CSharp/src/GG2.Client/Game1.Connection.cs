#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private sealed record HostedServerLaunchTarget(string FileName, string ArgumentsPrefix, string WorkingDirectory);

    private int _pendingHostedConnectTicks = -1;
    private int _pendingHostedConnectPort = 8190;
    private Process? _hostedServerProcess;
    private string? _recentConnectHost;
    private int _recentConnectPort;

    private void BeginHostedGame(
        string serverName,
        int port,
        int maxPlayers,
        string password,
        int timeLimitMinutes,
        int capLimit,
        int respawnSeconds,
        bool lobbyAnnounce,
        bool autoBalance)
    {
        CloseManualConnectMenu(clearStatus: true);
        CloseLobbyBrowser(clearStatus: true);
        _optionsMenuOpen = false;
        _hostSetupOpen = false;
        _hostSetupEditField = HostSetupEditField.None;
        _editingPlayerName = false;
        _networkClient.Disconnect();

        if (!TryStartHostedServer(
                serverName,
                port,
                maxPlayers,
                password,
                timeLimitMinutes,
                capLimit,
                respawnSeconds,
                lobbyAnnounce,
                autoBalance,
                out var error))
        {
            _menuStatusMessage = error;
            return;
        }

        _pendingHostedConnectPort = port;
        _pendingHostedConnectTicks = 20;
        _menuStatusMessage = "Starting local server...";
    }

    private void UpdatePendingHostedConnect()
    {
        if (_pendingHostedConnectTicks < 0)
        {
            return;
        }

        if (_networkClient.IsConnected)
        {
            _pendingHostedConnectTicks = -1;
            return;
        }

        if (_hostedServerProcess is not null && _hostedServerProcess.HasExited)
        {
            _pendingHostedConnectTicks = -1;
            _menuStatusMessage = "Local server exited before connect.";
            return;
        }

        if (_pendingHostedConnectTicks > 0)
        {
            _pendingHostedConnectTicks -= 1;
            return;
        }

        _pendingHostedConnectTicks = -1;
        TryConnectToServer("127.0.0.1", _pendingHostedConnectPort, addConsoleFeedback: false);
    }

    private void TryConnectFromMenu()
    {
        var host = _connectHostBuffer.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            _menuStatusMessage = "Host is required.";
            return;
        }

        if (!int.TryParse(_connectPortBuffer.Trim(), out var port) || port is <= 0 or > 65535)
        {
            _menuStatusMessage = "Port must be 1-65535.";
            return;
        }

        TryConnectToServer(host, port, addConsoleFeedback: false);
    }

    private bool TryConnectToServer(string host, int port, bool addConsoleFeedback)
    {
        if (_networkClient.Connect(host, port, _world.LocalPlayer.DisplayName, out var error))
        {
            RecordRecentConnection(host, port);
            _lastAppliedSnapshotFrame = 0;
            _hasReceivedSnapshot = false;
            _lastSnapshotReceivedTimeSeconds = -1d;
            _smoothedSnapshotIntervalSeconds = 1f / SimulationConfig.DefaultTicksPerSecond;
            _pendingNetworkVisualEvents.Clear();
            _hasPredictedLocalPlayerPosition = false;
            _hasSmoothedLocalPlayerRenderPosition = false;
            _pendingPredictedInputs.Clear();
            _entityInterpolationTracks.Clear();
            _intelInterpolationTracks.Clear();
            _interpolatedEntityPositions.Clear();
            _interpolatedIntelPositions.Clear();
            CloseLobbyBrowser(clearStatus: false);
            _menuStatusMessage = $"Connecting to {host}:{port}...";
            if (addConsoleFeedback)
            {
                AddConsoleLine($"connecting to {host}:{port} over udp");
            }

            return true;
        }

        _menuStatusMessage = $"Connect failed: {error}";
        if (addConsoleFeedback)
        {
            AddConsoleLine($"connect failed: {error}");
        }

        return false;
    }

    private void ReturnToMainMenu(string? statusMessage = null)
    {
        _networkClient.Disconnect();
        StopHostedServer();
        _pendingHostedConnectTicks = -1;
        _pendingHostedConnectPort = 8190;
        _mainMenuOpen = true;
        _optionsMenuOpen = false;
        _optionsMenuOpenedFromGameplay = false;
        _hostSetupOpen = false;
        _hostSetupEditField = HostSetupEditField.None;
        CloseLobbyBrowser(clearStatus: false);
        _manualConnectOpen = false;
        _creditsOpen = false;
        _inGameMenuOpen = false;
        _controlsMenuOpen = false;
        _controlsMenuOpenedFromGameplay = false;
        _pendingControlsBinding = null;
        _teamSelectOpen = false;
        _classSelectOpen = false;
        _pendingClassSelectTeam = null;
        _editingPlayerName = false;
        _editingConnectHost = false;
        _editingConnectPort = false;
        _consoleOpen = false;
        _scoreboardOpen = false;
        _bubbleMenuKind = BubbleMenuKind.None;
        _bubbleMenuClosing = false;
        _passwordPromptOpen = false;
        _passwordEditBuffer = string.Empty;
        _passwordPromptMessage = string.Empty;
        _menuStatusMessage = statusMessage ?? string.Empty;
        _autoBalanceNoticeText = string.Empty;
        _autoBalanceNoticeTicks = 0;
    }

    private void ShowAutoBalanceNotice(string text, int seconds)
    {
        _autoBalanceNoticeText = text;
        _autoBalanceNoticeTicks = Math.Max(1, seconds * _config.TicksPerSecond);
    }

    private bool TryStartHostedServer(
        string serverName,
        int port,
        int maxPlayers,
        string password,
        int timeLimitMinutes,
        int capLimit,
        int respawnSeconds,
        bool lobbyAnnounce,
        bool autoBalance,
        out string error)
    {
        error = string.Empty;

        StopHostedServer();

        var serverLaunchTarget = FindServerLaunchTarget();
        if (serverLaunchTarget is null)
        {
            error = "Could not find GG2.Server. Build the server first.";
            return false;
        }

        try
        {
            var configArg = $" --config \"{RuntimePaths.GetConfigPath(Gg2PreferencesDocument.DefaultFileName)}\"";
            var portArg = port > 0 ? $" --port {port}" : string.Empty;
            var nameArg = string.IsNullOrWhiteSpace(serverName) ? string.Empty : $" --name \"{serverName}\"";
            var maxPlayersArg = maxPlayers > 0 ? $" --max-players {maxPlayers}" : string.Empty;
            var passwordArg = string.IsNullOrWhiteSpace(password) ? string.Empty : $" --password \"{password}\"";
            var timeLimitArg = timeLimitMinutes > 0 ? $" --time-limit {timeLimitMinutes}" : string.Empty;
            var capLimitArg = capLimit > 0 ? $" --cap-limit {capLimit}" : string.Empty;
            var respawnArg = respawnSeconds >= 0 ? $" --respawn-seconds {respawnSeconds}" : string.Empty;
            var lobbyArg = lobbyAnnounce ? " --lobby" : " --no-lobby";
            var autoBalanceArg = autoBalance ? " --auto-balance" : " --no-auto-balance";
            var arguments = $"{serverLaunchTarget.ArgumentsPrefix}{configArg}{portArg}{nameArg}{maxPlayersArg}{passwordArg}{timeLimitArg}{capLimitArg}{respawnArg}{lobbyArg}{autoBalanceArg}".Trim();
            var startInfo = new ProcessStartInfo(
                serverLaunchTarget.FileName,
                arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                WorkingDirectory = serverLaunchTarget.WorkingDirectory,
            };
            _hostedServerProcess = Process.Start(startInfo);
            if (_hostedServerProcess is null)
            {
                error = "Failed to start local server process.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to start local server: {ex.Message}";
            return false;
        }
    }

    private void StopHostedServer()
    {
        if (_hostedServerProcess is null)
        {
            return;
        }

        try
        {
            if (!_hostedServerProcess.HasExited)
            {
                try
                {
                    _hostedServerProcess.StandardInput.WriteLine("shutdown");
                    _hostedServerProcess.StandardInput.Flush();
                }
                catch
                {
                }

                if (!_hostedServerProcess.WaitForExit(2000))
                {
                    _hostedServerProcess.Kill(entireProcessTree: true);
                    _hostedServerProcess.WaitForExit(1000);
                }
            }
        }
        catch
        {
        }
        finally
        {
            _hostedServerProcess.Dispose();
            _hostedServerProcess = null;
        }
    }

    private void CloseManualConnectMenu(bool clearStatus)
    {
        _manualConnectOpen = false;
        _editingConnectHost = false;
        _editingConnectPort = false;
        if (clearStatus)
        {
            _menuStatusMessage = string.Empty;
        }
    }

    private static HostedServerLaunchTarget? FindServerLaunchTarget()
    {
        foreach (var candidate in EnumerateServerAppHostCandidates())
        {
            if (File.Exists(candidate))
            {
                return new HostedServerLaunchTarget(candidate, string.Empty, Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory);
            }
        }

        foreach (var candidate in EnumerateServerAssemblyCandidates())
        {
            if (File.Exists(candidate))
            {
                return new HostedServerLaunchTarget("dotnet", $"\"{candidate}\"", Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateServerAppHostCandidates()
    {
        var appHostFileNames = OperatingSystem.IsWindows()
            ? new[] { "GG2.Server.exe", "GG2.Server" }
            : new[] { "GG2.Server", "GG2.Server.exe" };
        var directCandidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };
        foreach (var directory in directCandidates)
        {
            foreach (var fileName in appHostFileNames)
            {
                yield return Path.Combine(directory, fileName);
            }
        }

        var probes = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        var relativeDirectories = new[]
        {
            Path.Combine("Source", "GG2.CSharp", "src", "GG2.Server", "bin", "Debug", "net8.0"),
            Path.Combine("Source", "GG2.CSharp", "src", "GG2.Server", "bin", "Release", "net8.0"),
            Path.Combine("src", "GG2.Server", "bin", "Debug", "net8.0"),
            Path.Combine("src", "GG2.Server", "bin", "Release", "net8.0"),
        };

        foreach (var probe in probes)
        {
            var directory = new DirectoryInfo(probe);
            while (directory is not null)
            {
                foreach (var relativeDirectory in relativeDirectories)
                {
                    foreach (var fileName in appHostFileNames)
                    {
                        yield return Path.Combine(directory.FullName, relativeDirectory, fileName);
                    }
                }

                directory = directory.Parent;
            }
        }
    }

    private static IEnumerable<string> EnumerateServerAssemblyCandidates()
    {
        var directCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "GG2.Server.dll"),
            Path.Combine(Directory.GetCurrentDirectory(), "GG2.Server.dll"),
        };
        foreach (var candidate in directCandidates)
        {
            yield return candidate;
        }

        var probes = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        var relativePaths = new[]
        {
            Path.Combine("Source", "GG2.CSharp", "src", "GG2.Server", "bin", "Debug", "net8.0", "GG2.Server.dll"),
            Path.Combine("Source", "GG2.CSharp", "src", "GG2.Server", "bin", "Release", "net8.0", "GG2.Server.dll"),
            Path.Combine("src", "GG2.Server", "bin", "Debug", "net8.0", "GG2.Server.dll"),
            Path.Combine("src", "GG2.Server", "bin", "Release", "net8.0", "GG2.Server.dll"),
        };

        foreach (var probe in probes)
        {
            var directory = new DirectoryInfo(probe);
            while (directory is not null)
            {
                foreach (var relativePath in relativePaths)
                {
                    yield return Path.Combine(directory.FullName, relativePath);
                }

                directory = directory.Parent;
            }
        }
    }
}
