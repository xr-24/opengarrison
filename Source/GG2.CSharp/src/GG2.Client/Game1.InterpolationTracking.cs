#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private void UpdateInterpolatedWorldState()
    {
        if (!_networkClient.IsConnected)
        {
            _interpolatedEntityPositions.Clear();
            _interpolatedIntelPositions.Clear();
            _entityInterpolationTracks.Clear();
            _intelInterpolationTracks.Clear();
            _lastAppliedSnapshotFrame = 0;
            _hasReceivedSnapshot = false;
            _lastSnapshotReceivedTimeSeconds = -1d;
            _smoothedSnapshotIntervalSeconds = 1f / SimulationConfig.DefaultTicksPerSecond;
            _pendingNetworkVisualEvents.Clear();
            _processedNetworkSoundEventIds.Clear();
            _processedNetworkSoundEventOrder.Clear();
            _processedNetworkVisualEventIds.Clear();
            _processedNetworkVisualEventOrder.Clear();
            _hasPredictedLocalPlayerPosition = false;
            _hasPredictedLocalActionState = false;
            _pendingPredictedInputs.Clear();
            return;
        }

        var activeEntityIds = new HashSet<int>();
        _interpolatedEntityPositions[_world.LocalPlayer.Id] = new Vector2(_world.LocalPlayer.X, _world.LocalPlayer.Y);
        activeEntityIds.Add(_world.LocalPlayer.Id);
        foreach (var player in EnumerateRemotePlayersForView())
        {
            UpdateInterpolatedEntityPosition(player.Id, player.X, player.Y);
            activeEntityIds.Add(player.Id);
        }

        foreach (var deadBody in _world.DeadBodies)
        {
            UpdateInterpolatedEntityPosition(deadBody.Id, deadBody.X, deadBody.Y);
            activeEntityIds.Add(deadBody.Id);
        }

        foreach (var sentry in _world.Sentries)
        {
            UpdateInterpolatedEntityPosition(sentry.Id, sentry.X, sentry.Y);
            activeEntityIds.Add(sentry.Id);
        }

        foreach (var shot in _world.Shots)
        {
            UpdateInterpolatedEntityPosition(shot.Id, shot.X, shot.Y);
            activeEntityIds.Add(shot.Id);
        }

        foreach (var shot in _world.RevolverShots)
        {
            UpdateInterpolatedEntityPosition(shot.Id, shot.X, shot.Y);
            activeEntityIds.Add(shot.Id);
        }

        foreach (var needle in _world.Needles)
        {
            UpdateInterpolatedEntityPosition(needle.Id, needle.X, needle.Y);
            activeEntityIds.Add(needle.Id);
        }

        foreach (var flame in _world.Flames)
        {
            UpdateInterpolatedEntityPosition(flame.Id, flame.X, flame.Y);
            activeEntityIds.Add(flame.Id);
        }

        foreach (var rocket in _world.Rockets)
        {
            UpdateInterpolatedEntityPosition(rocket.Id, rocket.X, rocket.Y);
            activeEntityIds.Add(rocket.Id);
        }

        foreach (var mine in _world.Mines)
        {
            UpdateInterpolatedEntityPosition(mine.Id, mine.X, mine.Y);
            activeEntityIds.Add(mine.Id);
        }

        foreach (var gib in _world.PlayerGibs)
        {
            UpdateInterpolatedEntityPosition(gib.Id, gib.X, gib.Y);
            activeEntityIds.Add(gib.Id);
        }

        foreach (var bloodDrop in _world.BloodDrops)
        {
            UpdateInterpolatedEntityPosition(bloodDrop.Id, bloodDrop.X, bloodDrop.Y);
            activeEntityIds.Add(bloodDrop.Id);
        }

        var staleEntityIds = new List<int>();
        foreach (var entityId in _interpolatedEntityPositions.Keys)
        {
            if (!activeEntityIds.Contains(entityId))
            {
                staleEntityIds.Add(entityId);
            }
        }

        foreach (var entityId in staleEntityIds)
        {
            _interpolatedEntityPositions.Remove(entityId);
            _entityInterpolationTracks.Remove(entityId);
        }

        UpdateInterpolatedIntelPosition(_world.RedIntel);
        UpdateInterpolatedIntelPosition(_world.BlueIntel);
    }

    private void UpdateInterpolatedEntityPosition(int entityId, float x, float y)
    {
        if (!_entityInterpolationTracks.TryGetValue(entityId, out var track))
        {
            _interpolatedEntityPositions[entityId] = new Vector2(x, y);
            return;
        }

        _interpolatedEntityPositions[entityId] = EvaluateInterpolationTrack(track);
    }

    private void UpdateInterpolatedIntelPosition(TeamIntelligenceState intelState)
    {
        if (!_intelInterpolationTracks.TryGetValue(intelState.Team, out var track))
        {
            _interpolatedIntelPositions[intelState.Team] = new Vector2(intelState.X, intelState.Y);
            return;
        }

        _interpolatedIntelPositions[intelState.Team] = EvaluateInterpolationTrack(track);
    }

    private void CaptureRemoteInterpolationTargets(int tickRate)
    {
        if (!_networkClient.IsConnected)
        {
            return;
        }

        var baseIntervalSeconds = tickRate > 0
            ? MathF.Max(1f / 120f, 1f / tickRate)
            : 1f / SimulationConfig.DefaultTicksPerSecond;
        var snapshotReceivedTimeSeconds = _networkInterpolationClockSeconds;
        if (_hasReceivedSnapshot)
        {
            var observedIntervalSeconds = (float)Math.Max(
                0d,
                snapshotReceivedTimeSeconds - _lastSnapshotReceivedTimeSeconds);
            if (observedIntervalSeconds > 0f)
            {
                var clampedObservedIntervalSeconds = Math.Clamp(observedIntervalSeconds, baseIntervalSeconds * 0.5f, 0.25f);
                _smoothedSnapshotIntervalSeconds += (clampedObservedIntervalSeconds - _smoothedSnapshotIntervalSeconds) * 0.2f;
            }
        }
        else
        {
            _smoothedSnapshotIntervalSeconds = baseIntervalSeconds;
            _hasReceivedSnapshot = true;
        }

        _lastSnapshotReceivedTimeSeconds = snapshotReceivedTimeSeconds;
        var targetIntervalSeconds = MathF.Max(baseIntervalSeconds, _smoothedSnapshotIntervalSeconds);
        _networkSnapshotInterpolationDurationSeconds = Math.Clamp(
            targetIntervalSeconds * 1.15f,
            baseIntervalSeconds,
            0.25f);

        foreach (var player in EnumerateRemotePlayersForView())
        {
            CapturePlayerInterpolationTarget(true, player);
        }

        foreach (var deadBody in _world.DeadBodies)
        {
            CaptureEntityInterpolationTarget(true, deadBody.Id, deadBody.X, deadBody.Y);
        }

        foreach (var sentry in _world.Sentries)
        {
            CaptureEntityInterpolationTarget(true, sentry.Id, sentry.X, sentry.Y);
        }

        foreach (var shot in _world.Shots)
        {
            CaptureProjectileInterpolationTarget(shot.Id, shot.X, shot.Y, new Vector2(shot.VelocityX, shot.VelocityY), 18f);
        }

        foreach (var shot in _world.RevolverShots)
        {
            CaptureProjectileInterpolationTarget(shot.Id, shot.X, shot.Y, new Vector2(shot.VelocityX, shot.VelocityY), 20f);
        }

        foreach (var needle in _world.Needles)
        {
            CaptureProjectileInterpolationTarget(needle.Id, needle.X, needle.Y, new Vector2(needle.VelocityX, needle.VelocityY), 18f);
        }

        foreach (var flame in _world.Flames)
        {
            CaptureProjectileInterpolationTarget(flame.Id, flame.X, flame.Y, new Vector2(flame.VelocityX, flame.VelocityY), 36f);
        }

        foreach (var rocket in _world.Rockets)
        {
            var rocketVelocity = new Vector2(MathF.Cos(rocket.DirectionRadians) * rocket.Speed, MathF.Sin(rocket.DirectionRadians) * rocket.Speed);
            CaptureProjectileInterpolationTarget(rocket.Id, rocket.X, rocket.Y, rocketVelocity, 24f);
        }

        foreach (var mine in _world.Mines)
        {
            CaptureProjectileInterpolationTarget(mine.Id, mine.X, mine.Y, new Vector2(mine.VelocityX, mine.VelocityY), 18f);
        }

        foreach (var gib in _world.PlayerGibs)
        {
            CaptureEntityInterpolationTarget(true, gib.Id, gib.X, gib.Y);
        }

        foreach (var bloodDrop in _world.BloodDrops)
        {
            CaptureEntityInterpolationTarget(true, bloodDrop.Id, bloodDrop.X, bloodDrop.Y);
        }

        CaptureIntelInterpolationTarget(_world.RedIntel.Team, _world.RedIntel.X, _world.RedIntel.Y);
        CaptureIntelInterpolationTarget(_world.BlueIntel.Team, _world.BlueIntel.X, _world.BlueIntel.Y);
    }

    private void CaptureEntityInterpolationTarget(bool isActive, int entityId, float x, float y)
    {
        CaptureEntityInterpolationTarget(isActive, entityId, x, y, Vector2.Zero, 0f, 0f, _networkSnapshotInterpolationDurationSeconds);
    }

    private void CapturePlayerInterpolationTarget(bool isActive, PlayerEntity player)
    {
        if (!isActive)
        {
            CaptureEntityInterpolationTarget(false, player.Id, player.X, player.Y);
            return;
        }

        var playerVelocity = new Vector2(player.HorizontalSpeed, player.VerticalSpeed);
        var extrapolationDurationSeconds = MathF.Min(_networkSnapshotInterpolationDurationSeconds * 0.5f, 0.075f);
        CaptureEntityInterpolationTarget(
            true,
            player.Id,
            player.X,
            player.Y,
            playerVelocity,
            extrapolationDurationSeconds,
            18f,
            _networkSnapshotInterpolationDurationSeconds);
    }

    private void CaptureProjectileInterpolationTarget(int entityId, float x, float y, Vector2 velocity, float maxExtrapolationDistance)
    {
        var extrapolationDurationSeconds = MathF.Min(_networkSnapshotInterpolationDurationSeconds * 0.6f, 0.05f);
        CaptureEntityInterpolationTarget(
            true,
            entityId,
            x,
            y,
            velocity,
            extrapolationDurationSeconds,
            maxExtrapolationDistance,
            0f);
    }

    private void CaptureEntityInterpolationTarget(
        bool isActive,
        int entityId,
        float x,
        float y,
        Vector2 velocity,
        float extrapolationDurationSeconds,
        float maxExtrapolationDistance,
        float interpolationDurationSeconds)
    {
        if (!isActive || interpolationDurationSeconds <= 0f)
        {
            _entityInterpolationTracks.Remove(entityId);
            _interpolatedEntityPositions[entityId] = new Vector2(x, y);
            return;
        }

        _entityInterpolationTracks[entityId] = new InterpolationTrack(
            _interpolatedEntityPositions.GetValueOrDefault(entityId, new Vector2(x, y)),
            new Vector2(x, y),
            _networkInterpolationClockSeconds,
            interpolationDurationSeconds,
            velocity,
            extrapolationDurationSeconds,
            maxExtrapolationDistance);
    }

    private void CaptureIntelInterpolationTarget(PlayerTeam team, float x, float y)
    {
        _intelInterpolationTracks[team] = new InterpolationTrack(
            _interpolatedIntelPositions.GetValueOrDefault(team, new Vector2(x, y)),
            new Vector2(x, y),
            _networkInterpolationClockSeconds,
            _networkSnapshotInterpolationDurationSeconds,
            Vector2.Zero,
            0f,
            0f);
    }

    private Vector2 EvaluateInterpolationTrack(InterpolationTrack track)
    {
        if (track.DurationSeconds <= 0f)
        {
            if (track.ExtrapolationDurationSeconds <= 0f || track.MaxExtrapolationDistance <= 0f || track.Velocity == Vector2.Zero)
            {
                return track.Target;
            }

            var immediateExtraSeconds = float.Clamp((float)(_networkInterpolationClockSeconds - track.StartTimeSeconds), 0f, track.ExtrapolationDurationSeconds);
            var immediateExtraOffset = track.Velocity * immediateExtraSeconds;
            var immediateExtraDistance = immediateExtraOffset.Length();
            if (immediateExtraDistance > track.MaxExtrapolationDistance)
            {
                immediateExtraOffset *= track.MaxExtrapolationDistance / immediateExtraDistance;
            }

            return track.Target + immediateExtraOffset;
        }

        var elapsedSeconds = _networkInterpolationClockSeconds - track.StartTimeSeconds;
        if (elapsedSeconds <= track.DurationSeconds)
        {
            var alpha = float.Clamp((float)(elapsedSeconds / track.DurationSeconds), 0f, 1f);
            return Vector2.Lerp(track.Start, track.Target, alpha);
        }

        if (track.ExtrapolationDurationSeconds <= 0f || track.MaxExtrapolationDistance <= 0f || track.Velocity == Vector2.Zero)
        {
            return track.Target;
        }

        var extraSeconds = float.Clamp((float)(elapsedSeconds - track.DurationSeconds), 0f, track.ExtrapolationDurationSeconds);
        var extraOffset = track.Velocity * extraSeconds;
        var extraDistance = extraOffset.Length();
        if (extraDistance > track.MaxExtrapolationDistance)
        {
            extraOffset *= track.MaxExtrapolationDistance / extraDistance;
        }

        return track.Target + extraOffset;
    }
}
