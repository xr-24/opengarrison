using System;
using System.Net;
using GG2.Core;

sealed class ClientSession(byte slot, IPEndPoint endPoint, string name, TimeSpan lastSeen)
{
    public byte Slot { get; set; } = slot;
    public IPEndPoint EndPoint { get; } = endPoint;
    public string Name { get; set; } = name;
    public TimeSpan ConnectedAt { get; } = lastSeen;
    public TimeSpan LastSeen { get; set; } = lastSeen;
    public PlayerInputSnapshot LatestInput { get; set; }
    public bool HasAcceptedInput { get; set; }
    public uint LastInputSequence { get; set; }
    public uint LastTeamCommandSequence { get; set; }
    public uint LastClassCommandSequence { get; set; }
    public uint LastSpectateCommandSequence { get; set; }
    public bool IsAuthorized { get; set; } = true;
    public TimeSpan LastPasswordRequestSentAt { get; set; } = TimeSpan.MinValue;
}
