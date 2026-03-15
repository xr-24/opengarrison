using GG2.Core;
using GG2.Protocol;
using GG2.Server.Plugins;

namespace GG2.Server;

internal sealed class ServerAdminOperations(
    Action<string> log,
    Action<IPEndPoint, IProtocolMessage> sendMessage,
    Func<IReadOnlyDictionary<byte, ClientSession>> clientsGetter,
    Func<ServerSessionManager> sessionManagerGetter,
    Func<SimulationWorld> worldGetter,
    Func<MapRotationManager> mapRotationManagerGetter,
    Func<SnapshotBroadcaster> snapshotBroadcasterGetter,
    Action<MapChangingEvent>? notifyMapChanging = null,
    Action<MapChangedEvent>? notifyMapChanged = null) : IGg2ServerAdminOperations
{
    public void BroadcastSystemMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var relay = new ChatRelayMessage(0, "[server]", text.Trim());
        foreach (var client in clientsGetter().Values)
        {
            sendMessage(client.EndPoint, relay);
        }

        log($"[server] system message: {text.Trim()}");
    }

    public bool TryDisconnect(byte slot, string reason)
    {
        if (!clientsGetter().TryGetValue(slot, out var client))
        {
            return false;
        }

        sendMessage(client.EndPoint, new ConnectionDeniedMessage(reason));
        sessionManagerGetter().RemoveClient(slot, reason);
        return true;
    }

    public bool TryMoveToSpectator(byte slot) => sessionManagerGetter().TryMoveClientToSpectator(slot);

    public bool TrySetTeam(byte slot, PlayerTeam team) => sessionManagerGetter().TrySetClientTeam(slot, team);

    public bool TrySetClass(byte slot, PlayerClass playerClass) => sessionManagerGetter().TrySetClientClass(slot, playerClass);

    public bool TryChangeMap(string levelName, int mapAreaIndex = 1, bool preservePlayerStats = false)
    {
        var world = worldGetter();
        var changingEvent = new MapChangingEvent(
            world.Level.Name,
            world.Level.MapAreaIndex,
            world.Level.MapAreaCount,
            levelName,
            mapAreaIndex,
            preservePlayerStats,
            world.MatchState.WinnerTeam);
        if (!world.TryLoadLevel(levelName, mapAreaIndex, preservePlayerStats))
        {
            return false;
        }

        notifyMapChanging?.Invoke(changingEvent);
        mapRotationManagerGetter().AlignCurrentMap(levelName);
        snapshotBroadcasterGetter().ResetTransientEvents();
        notifyMapChanged?.Invoke(new MapChangedEvent(
            world.Level.Name,
            world.Level.MapAreaIndex,
            world.Level.MapAreaCount,
            world.MatchRules.Mode));
        log($"[server] admin changed map to {world.Level.Name} area {world.Level.MapAreaIndex}/{world.Level.MapAreaCount}");
        return true;
    }
}
