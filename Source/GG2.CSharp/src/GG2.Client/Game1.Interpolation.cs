#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private readonly Dictionary<int, Vector2> _interpolatedEntityPositions = new();
    private readonly Dictionary<PlayerTeam, Vector2> _interpolatedIntelPositions = new();
    private readonly Dictionary<int, InterpolationTrack> _entityInterpolationTracks = new();
    private readonly Dictionary<PlayerTeam, InterpolationTrack> _intelInterpolationTracks = new();
    private readonly Stopwatch _networkInterpolationClock = Stopwatch.StartNew();
    private double _networkInterpolationClockSeconds;
    private float _networkSnapshotInterpolationDurationSeconds = 1f / SimulationConfig.DefaultTicksPerSecond;
    private float _smoothedSnapshotIntervalSeconds = 1f / SimulationConfig.DefaultTicksPerSecond;
    private double _lastSnapshotReceivedTimeSeconds = -1d;
    private bool _hasReceivedSnapshot;
    private ulong _lastAppliedSnapshotFrame;

    private Vector2 GetRenderPosition(int entityId, float x, float y, bool allowInterpolation = true)
    {
        if (!allowInterpolation || !_networkClient.IsConnected)
        {
            return new Vector2(x, y);
        }

        return _interpolatedEntityPositions.GetValueOrDefault(entityId, new Vector2(x, y));
    }

    private Vector2 GetRenderPosition(PlayerEntity player, bool allowInterpolation = true)
    {
        if (_networkClient.IsConnected && ReferenceEquals(player, _world.LocalPlayer) && _hasPredictedLocalPlayerPosition)
        {
            if (_hasSmoothedLocalPlayerRenderPosition)
            {
                return _smoothedLocalPlayerRenderPosition;
            }

            return _predictedLocalPlayerPosition;
        }

        if (_networkClient.IsConnected && !ReferenceEquals(player, _world.LocalPlayer))
        {
            return new Vector2(player.X, player.Y);
        }

        return GetRenderPosition(player.Id, player.X, player.Y, allowInterpolation);
    }

    private Vector2 GetRenderIntelPosition(TeamIntelligenceState intelState)
    {
        if (!_networkClient.IsConnected)
        {
            return new Vector2(intelState.X, intelState.Y);
        }

        return _interpolatedIntelPositions.GetValueOrDefault(intelState.Team, new Vector2(intelState.X, intelState.Y));
    }

    private readonly record struct InterpolationTrack(
        Vector2 Start,
        Vector2 Target,
        double StartTimeSeconds,
        float DurationSeconds,
        Vector2 Velocity,
        float ExtrapolationDurationSeconds,
        float MaxExtrapolationDistance);
}
