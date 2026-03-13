using System;
using GG2.Protocol;
using System.Collections.Generic;

var msg = new SnapshotMessage(
    123UL,
    30,
    "truefort",
    1,
    1,
    0,
    100,
    1,
    2,
    0,
    0,
    new SnapshotIntelState(1, 10, 20, true, false, 0),
    new SnapshotIntelState(2, 30, 40, false, true, 50),
    new List<SnapshotPlayerState>
    {
        new SnapshotPlayerState(1, "Player 1", 1, 1, true, 1, 2, 3, 4, 100, 100, 6, 10, true, false, false, false, 1f, 45f, false, 0f, true, 7, 1f)
    },
    new List<SnapshotSentryState>(),
    new List<SnapshotShotState>(),
    new List<SnapshotShotState>(),
    new List<SnapshotShotState>(),
    new List<SnapshotRocketState>(),
    new List<SnapshotFlameState>(),
    new List<SnapshotMineState>(),
    new List<SnapshotDeadBodyState>(),
    null,
    new List<SnapshotSoundEvent> { new SnapshotSoundEvent("IntelGetSnd", 1, 2) });

var bytes = ProtocolCodec.Serialize(msg);
Console.WriteLine($"bytes={bytes.Length}");
var ok = ProtocolCodec.TryDeserialize(bytes, out var result);
Console.WriteLine($"ok={ok} type={result?.GetType().Name}");
if (result is SnapshotMessage snapshot)
{
    Console.WriteLine($"players={snapshot.Players.Count} sounds={snapshot.SoundEvents.Count} frame={snapshot.Frame}");
}
