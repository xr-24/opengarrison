namespace GG2.Server.Plugins;

public interface IGg2ServerLifecycleHooks
{
    void OnServerStarting();

    void OnServerStarted();

    void OnServerStopping();

    void OnServerStopped();
}

public interface IGg2ServerClientHooks
{
    void OnHelloReceived(HelloReceivedEvent e);

    void OnClientConnected(ClientConnectedEvent e);

    void OnClientDisconnected(ClientDisconnectedEvent e);

    void OnPasswordAccepted(PasswordAcceptedEvent e);

    void OnPlayerTeamChanged(PlayerTeamChangedEvent e);

    void OnPlayerClassChanged(PlayerClassChangedEvent e);
}

public interface IGg2ServerChatHooks
{
    void OnChatReceived(ChatReceivedEvent e);
}

public interface IGg2ServerMapHooks
{
    void OnMapChanging(MapChangingEvent e);

    void OnMapChanged(MapChangedEvent e);
}
