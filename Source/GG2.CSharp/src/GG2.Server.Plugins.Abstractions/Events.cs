using GG2.Core;

namespace GG2.Server.Plugins;

public readonly record struct HelloReceivedEvent(string PlayerName, string EndPoint, int Version);

public readonly record struct ClientConnectedEvent(
    byte Slot,
    string PlayerName,
    string EndPoint,
    bool IsAuthorized,
    bool IsSpectator);

public readonly record struct ClientDisconnectedEvent(
    byte Slot,
    string PlayerName,
    string EndPoint,
    string Reason,
    bool WasAuthorized);

public readonly record struct PasswordAcceptedEvent(byte Slot, string PlayerName, string EndPoint);

public readonly record struct PlayerTeamChangedEvent(byte Slot, string PlayerName, PlayerTeam Team);

public readonly record struct PlayerClassChangedEvent(byte Slot, string PlayerName, PlayerClass PlayerClass);

public readonly record struct ChatReceivedEvent(byte Slot, string PlayerName, string Text, PlayerTeam? Team);

public readonly record struct MapChangingEvent(
    string CurrentLevelName,
    int CurrentAreaIndex,
    int CurrentAreaCount,
    string NextLevelName,
    int NextAreaIndex,
    bool PreservePlayerStats,
    PlayerTeam? WinnerTeam);

public readonly record struct MapChangedEvent(
    string LevelName,
    int AreaIndex,
    int AreaCount,
    GameModeKind Mode);
