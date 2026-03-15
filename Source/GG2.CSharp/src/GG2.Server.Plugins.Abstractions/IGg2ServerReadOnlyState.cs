using GG2.Core;

namespace GG2.Server.Plugins;

public interface IGg2ServerReadOnlyState
{
    string ServerName { get; }

    string LevelName { get; }

    int MapAreaIndex { get; }

    int MapAreaCount { get; }

    GameModeKind GameMode { get; }

    MatchPhase MatchPhase { get; }

    int RedCaps { get; }

    int BlueCaps { get; }

    IReadOnlyList<Gg2ServerPlayerInfo> GetPlayers();
}
