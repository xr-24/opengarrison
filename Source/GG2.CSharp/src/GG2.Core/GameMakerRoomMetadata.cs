using System.Collections.Generic;

namespace GG2.Core;

public sealed record GameMakerRoomMetadata(
    string Name,
    WorldBounds Bounds,
    string PrimaryBackgroundAssetName,
    IReadOnlyList<SpawnPoint> RedSpawns,
    IReadOnlyList<SpawnPoint> BlueSpawns,
    IReadOnlyList<IntelBaseMarker> IntelBases,
    IReadOnlyList<RoomObjectMarker> RoomObjects,
    IReadOnlyList<float> AreaBoundaries);
