#nullable enable

namespace GG2.Client;

public partial class Game1
{
    private bool IsServerLauncherMode => _startupMode == GameStartupMode.ServerLauncher;

    private bool IsHostedServerRunning
    {
        get
        {
            if (_hostedServerProcess is null)
            {
                return false;
            }

            try
            {
                return !_hostedServerProcess.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    private void InitializeServerLauncherMode()
    {
        _startupSplashOpen = false;
        _mainMenuOpen = true;
        _manualConnectOpen = false;
        _optionsMenuOpen = false;
        _creditsOpen = false;
        _controlsMenuOpen = false;
        _hostSetupOpen = false;
        _hostSetupEditField = HostSetupEditField.None;
        OpenHostSetupMenu();
        _menuStatusMessage = "Configure and start a dedicated server.";
    }

    private void UpdateServerLauncherState()
    {
        if (!IsServerLauncherMode || _hostedServerProcess is null)
        {
            return;
        }

        try
        {
            if (!_hostedServerProcess.HasExited)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        _hostedServerProcess.Dispose();
        _hostedServerProcess = null;
        _menuStatusMessage = "Dedicated server exited.";
    }

    private string GetHostSetupTitle()
    {
        return IsServerLauncherMode ? "Dedicated Server" : "Host Game";
    }

    private string GetHostSetupSubtitle()
    {
        if (!IsServerLauncherMode)
        {
            return "Server rules and stock map rotation";
        }

        return IsHostedServerRunning
            ? "Dedicated server is running in the background"
            : "Configure and run a headless server process";
    }

    private string GetHostSetupPrimaryButtonLabel()
    {
        if (!IsServerLauncherMode)
        {
            return "Host";
        }

        return IsHostedServerRunning ? "Restart Server" : "Start Server";
    }

    private string GetHostSetupSecondaryButtonLabel()
    {
        if (!IsServerLauncherMode)
        {
            return "Back";
        }

        return IsHostedServerRunning ? "Stop Server" : "Quit";
    }

    private bool TryHandleServerLauncherBackAction()
    {
        if (!IsServerLauncherMode)
        {
            return false;
        }

        _hostSetupEditField = HostSetupEditField.None;
        if (IsHostedServerRunning)
        {
            StopHostedServer();
            _menuStatusMessage = "Dedicated server stopped.";
        }
        else
        {
            Exit();
        }

        return true;
    }

    private void BeginDedicatedServerLaunch(
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
        _creditsOpen = false;
        _controlsMenuOpen = false;

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

        _pendingHostedConnectTicks = -1;
        _pendingHostedConnectPort = port;
        _hostSetupEditField = HostSetupEditField.None;
        _menuStatusMessage = $"Dedicated server running on UDP port {port}.";
    }
}
