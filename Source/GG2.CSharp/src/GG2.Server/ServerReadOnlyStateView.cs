using GG2.Core;
using GG2.Server.Plugins;
using static ServerHelpers;

namespace GG2.Server;

internal sealed class ServerReadOnlyStateView(
    Func<string> serverNameGetter,
    Func<SimulationWorld> worldGetter,
    Func<IReadOnlyDictionary<byte, ClientSession>> clientsGetter) : IGg2ServerReadOnlyState
{
    public string ServerName => serverNameGetter();

    public string LevelName => worldGetter().Level.Name;

    public int MapAreaIndex => worldGetter().Level.MapAreaIndex;

    public int MapAreaCount => worldGetter().Level.MapAreaCount;

    public GameModeKind GameMode => worldGetter().MatchRules.Mode;

    public MatchPhase MatchPhase => worldGetter().MatchState.Phase;

    public int RedCaps => worldGetter().RedCaps;

    public int BlueCaps => worldGetter().BlueCaps;

    public IReadOnlyList<Gg2ServerPlayerInfo> GetPlayers()
    {
        var world = worldGetter();
        return clientsGetter()
            .Values
            .OrderBy(client => client.Slot)
            .Select(client =>
            {
                var isSpectator = IsSpectatorSlot(client.Slot);
                PlayerTeam? team = null;
                PlayerClass? playerClass = null;
                if (!isSpectator && world.TryGetNetworkPlayer(client.Slot, out var player))
                {
                    team = player.Team;
                    playerClass = player.ClassId;
                }

                return new Gg2ServerPlayerInfo(
                    client.Slot,
                    client.Name,
                    isSpectator,
                    client.IsAuthorized,
                    team,
                    playerClass,
                    client.EndPoint.ToString());
            })
            .ToArray();
    }
}
