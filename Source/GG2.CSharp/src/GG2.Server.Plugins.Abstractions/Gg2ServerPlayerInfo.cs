using GG2.Core;

namespace GG2.Server.Plugins;

public readonly record struct Gg2ServerPlayerInfo(
    byte Slot,
    string Name,
    bool IsSpectator,
    bool IsAuthorized,
    PlayerTeam? Team,
    PlayerClass? PlayerClass,
    string EndPoint);
