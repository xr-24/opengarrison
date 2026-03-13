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
            _entitySnapshotHistories.Clear();
            _intelSnapshotHistories.Clear();
            _remotePlayerSnapshotHistories.Clear();
            _lastAppliedSnapshotFrame = 0;
            _hasReceivedSnapshot = false;
            _lastSnapshotReceivedTimeSeconds = -1d;
            _latestSnapshotServerTimeSeconds = -1d;
            _latestSnapshotReceivedClockSeconds = -1d;
            _smoothedSnapshotIntervalSeconds = 1f / SimulationConfig.DefaultTicksPerSecond;
            _smoothedSnapshotJitterSeconds = 0f;
            _remotePlayerInterpolationBackTimeSeconds = 1f / SimulationConfig.DefaultTicksPerSecond;
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
        var localPlayerStateKey = GetPlayerStateKey(_world.LocalPlayer);
        _interpolatedEntityPositions[localPlayerStateKey] = new Vector2(_world.LocalPlayer.X, _world.LocalPlayer.Y);
        activeEntityIds.Add(localPlayerStateKey);
        foreach (var player in EnumerateRemotePlayersForView())
        {
            UpdateInterpolatedRemotePlayerPosition(player);
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
            _entitySnapshotHistories.Remove(entityId);
            _remotePlayerSnapshotHistories.Remove(entityId);
        }

        UpdateInterpolatedIntelPosition(_world.RedIntel);
        UpdateInterpolatedIntelPosition(_world.BlueIntel);
    }

    private void UpdateInterpolatedEntityPosition(int entityId, float x, float y)
    {
        if (_entitySnapshotHistories.TryGetValue(entityId, out var history) && history.Count > 0)
        {
            _interpolatedEntityPositions[entityId] = EvaluateEntitySnapshotHistory(history, GetEntityRenderTimeSeconds());
            return;
        }

        if (!_entityInterpolationTracks.TryGetValue(entityId, out var track))
        {
            _interpolatedEntityPositions[entityId] = new Vector2(x, y);
            return;
        }

        _interpolatedEntityPositions[entityId] = EvaluateInterpolationTrack(track);
    }

    private void UpdateInterpolatedIntelPosition(TeamIntelligenceState intelState)
    {
        if (_intelSnapshotHistories.TryGetValue(intelState.Team, out var history) && history.Count > 0)
        {
            _interpolatedIntelPositions[intelState.Team] = EvaluateEntitySnapshotHistory(history, GetEntityRenderTimeSeconds());
            return;
        }

        if (!_intelInterpolationTracks.TryGetValue(intelState.Team, out var track))
        {
            _interpolatedIntelPositions[intelState.Team] = new Vector2(intelState.X, intelState.Y);
            return;
        }

        _interpolatedIntelPositions[intelState.Team] = EvaluateInterpolationTrack(track);
    }

    private void CaptureRemoteInterpolationTargets(ulong snapshotFrame, int tickRate)
    {
        if (!_networkClient.IsConnected)
        {
            return;
        }

        var baseIntervalSeconds = tickRate > 0
            ? MathF.Max(1f / 120f, 1f / tickRate)
            : 1f / SimulationConfig.DefaultTicksPerSecond;
        var snapshotReceivedTimeSeconds = _networkInterpolationClockSeconds;
        var snapshotServerTimeSeconds = GetSnapshotTimelineTimeSeconds(snapshotFrame, tickRate);
        if (_hasReceivedSnapshot)
        {
            var observedIntervalSeconds = (float)Math.Max(
                0d,
                snapshotServerTimeSeconds - _latestSnapshotServerTimeSeconds);
            if (observedIntervalSeconds > 0f)
            {
                var clampedObservedIntervalSeconds = Math.Clamp(observedIntervalSeconds, baseIntervalSeconds * 0.5f, 0.25f);
                _smoothedSnapshotIntervalSeconds += (clampedObservedIntervalSeconds - _smoothedSnapshotIntervalSeconds) * 0.2f;

                var arrivalIntervalSeconds = (float)Math.Max(
                    0d,
                    snapshotReceivedTimeSeconds - _lastSnapshotReceivedTimeSeconds);
                var jitterSampleSeconds = MathF.Abs(arrivalIntervalSeconds - observedIntervalSeconds);
                _smoothedSnapshotJitterSeconds += (jitterSampleSeconds - _smoothedSnapshotJitterSeconds) * 0.1f;
            }
        }
        else
        {
            _smoothedSnapshotIntervalSeconds = baseIntervalSeconds;
            _smoothedSnapshotJitterSeconds = 0f;
            _hasReceivedSnapshot = true;
        }

        _lastSnapshotReceivedTimeSeconds = snapshotReceivedTimeSeconds;
        _latestSnapshotServerTimeSeconds = snapshotServerTimeSeconds;
        _latestSnapshotReceivedClockSeconds = snapshotReceivedTimeSeconds;
        var targetIntervalSeconds = MathF.Max(baseIntervalSeconds, _smoothedSnapshotIntervalSeconds);
        _networkSnapshotInterpolationDurationSeconds = Math.Clamp(
            targetIntervalSeconds * 0.9f,
            baseIntervalSeconds * 0.5f,
            0.12f);
        _remotePlayerInterpolationBackTimeSeconds = Math.Clamp(
            MathF.Max(
                baseIntervalSeconds * 2f,
                (_smoothedSnapshotIntervalSeconds * 2f) + (_smoothedSnapshotJitterSeconds * 3f)),
            0.06f,
            0.18f);

        foreach (var player in EnumerateRemotePlayersForView())
        {
            AppendRemotePlayerSnapshot(player, snapshotServerTimeSeconds);
        }

        foreach (var deadBody in _world.DeadBodies)
        {
            CaptureEntityInterpolationTarget(true, deadBody.Id, deadBody.X, deadBody.Y, Vector2.Zero, 0f, 0f, snapshotServerTimeSeconds);
        }

        foreach (var sentry in _world.Sentries)
        {
            CaptureEntityInterpolationTarget(true, sentry.Id, sentry.X, sentry.Y, Vector2.Zero, 0f, 0f, snapshotServerTimeSeconds);
        }

        foreach (var shot in _world.Shots)
        {
            CaptureProjectileInterpolationTarget(shot.Id, shot.X, shot.Y, new Vector2(shot.VelocityX, shot.VelocityY), 18f, snapshotServerTimeSeconds);
        }

        foreach (var shot in _world.RevolverShots)
        {
            CaptureProjectileInterpolationTarget(shot.Id, shot.X, shot.Y, new Vector2(shot.VelocityX, shot.VelocityY), 20f, snapshotServerTimeSeconds);
        }

        foreach (var needle in _world.Needles)
        {
            CaptureProjectileInterpolationTarget(needle.Id, needle.X, needle.Y, new Vector2(needle.VelocityX, needle.VelocityY), 18f, snapshotServerTimeSeconds);
        }

        foreach (var flame in _world.Flames)
        {
            CaptureProjectileInterpolationTarget(flame.Id, flame.X, flame.Y, new Vector2(flame.VelocityX, flame.VelocityY), 36f, snapshotServerTimeSeconds);
        }

        foreach (var rocket in _world.Rockets)
        {
            var rocketVelocity = new Vector2(MathF.Cos(rocket.DirectionRadians) * rocket.Speed, MathF.Sin(rocket.DirectionRadians) * rocket.Speed);
            CaptureProjectileInterpolationTarget(rocket.Id, rocket.X, rocket.Y, rocketVelocity, 24f, snapshotServerTimeSeconds);
        }

        foreach (var mine in _world.Mines)
        {
            CaptureProjectileInterpolationTarget(mine.Id, mine.X, mine.Y, new Vector2(mine.VelocityX, mine.VelocityY), 18f, snapshotServerTimeSeconds);
        }

        foreach (var gib in _world.PlayerGibs)
        {
            CaptureEntityInterpolationTarget(true, gib.Id, gib.X, gib.Y, new Vector2(gib.VelocityX, gib.VelocityY), 0.03f, 12f, snapshotServerTimeSeconds);
        }

        foreach (var bloodDrop in _world.BloodDrops)
        {
            CaptureEntityInterpolationTarget(true, bloodDrop.Id, bloodDrop.X, bloodDrop.Y, new Vector2(bloodDrop.VelocityX, bloodDrop.VelocityY), 0.03f, 8f, snapshotServerTimeSeconds);
        }

        CaptureIntelInterpolationTarget(_world.RedIntel.Team, _world.RedIntel.X, _world.RedIntel.Y, snapshotServerTimeSeconds);
        CaptureIntelInterpolationTarget(_world.BlueIntel.Team, _world.BlueIntel.X, _world.BlueIntel.Y, snapshotServerTimeSeconds);
    }

    private void CaptureEntityInterpolationTarget(bool isActive, int entityId, float x, float y)
    {
        CaptureEntityInterpolationTarget(isActive, entityId, x, y, Vector2.Zero, 0f, 0f, _latestSnapshotServerTimeSeconds);
    }

    private void UpdateInterpolatedRemotePlayerPosition(PlayerEntity player)
    {
        if (!_remotePlayerSnapshotHistories.TryGetValue(player.Id, out var history) || history.Count == 0)
        {
            _interpolatedEntityPositions[player.Id] = new Vector2(player.X, player.Y);
            return;
        }

        var renderTimeSeconds = GetRemotePlayerRenderTimeSeconds();
        if (history.Count == 1)
        {
            _interpolatedEntityPositions[player.Id] = EvaluateRemotePlayerExtrapolation(history[0], renderTimeSeconds);
            return;
        }

        if (renderTimeSeconds <= history[0].TimeSeconds)
        {
            _interpolatedEntityPositions[player.Id] = history[0].Position;
            return;
        }

        for (var index = 1; index < history.Count; index += 1)
        {
            var newer = history[index];
            if (renderTimeSeconds > newer.TimeSeconds)
            {
                continue;
            }

            var older = history[index - 1];
            _interpolatedEntityPositions[player.Id] = InterpolateRemotePlayerSample(older, newer, renderTimeSeconds);
            return;
        }

        _interpolatedEntityPositions[player.Id] = EvaluateRemotePlayerExtrapolation(history[^1], renderTimeSeconds);
    }

    private void AppendRemotePlayerSnapshot(PlayerEntity player, double snapshotTimeSeconds)
    {
        var sample = new PlayerSnapshotSample(
            new Vector2(player.X, player.Y),
            new Vector2(player.HorizontalSpeed, player.VerticalSpeed),
            snapshotTimeSeconds,
            player.Team,
            player.ClassId,
            player.IsAlive);
        if (!_remotePlayerSnapshotHistories.TryGetValue(player.Id, out var history))
        {
            history = new List<PlayerSnapshotSample>(4);
            _remotePlayerSnapshotHistories[player.Id] = history;
        }

        if (ShouldResetRemotePlayerSnapshotHistory(player, sample, history))
        {
            history.Clear();
            history.Add(sample);
            _interpolatedEntityPositions[player.Id] = sample.Position;
        }
        else
        {
            if (history.Count > 0)
            {
                var latest = history[^1];
                if (sample.TimeSeconds <= latest.TimeSeconds)
                {
                    history[^1] = sample;
                }
                else
                {
                    history.Add(sample);
                }
            }
            else
            {
                history.Add(sample);
            }
        }

        var minHistoryTimeSeconds = snapshotTimeSeconds - 0.25d;
        while (history.Count > 2 && history[1].TimeSeconds < minHistoryTimeSeconds)
        {
            history.RemoveAt(0);
        }

        if (!_interpolatedEntityPositions.ContainsKey(player.Id))
        {
            _interpolatedEntityPositions[player.Id] = sample.Position;
        }

        _entityInterpolationTracks.Remove(player.Id);
    }

    private bool ShouldResetRemotePlayerSnapshotHistory(
        PlayerEntity player,
        PlayerSnapshotSample sample,
        List<PlayerSnapshotSample> history)
    {
        if (history.Count == 0)
        {
            return true;
        }

        var latest = history[^1];
        if (latest.Team != sample.Team
            || latest.ClassId != sample.ClassId
            || latest.IsAlive != sample.IsAlive)
        {
            return true;
        }

        var snapshotJumpThreshold = GetRemotePlayerSnapThreshold(latest, sample, RemotePlayerHistorySnapDistance);
        if (Vector2.DistanceSquared(latest.Position, sample.Position) > snapshotJumpThreshold * snapshotJumpThreshold)
        {
            return true;
        }

        var renderedPosition = _interpolatedEntityPositions.GetValueOrDefault(player.Id, latest.Position);
        var correctionThreshold = GetRemotePlayerCorrectionSnapThreshold(latest, sample);
        return Vector2.DistanceSquared(renderedPosition, sample.Position) > correctionThreshold * correctionThreshold;
    }

    private static float GetRemotePlayerSnapThreshold(
        PlayerSnapshotSample older,
        PlayerSnapshotSample newer,
        float minimumDistance)
    {
        var intervalSeconds = (float)Math.Max(
            1d / SimulationConfig.DefaultTicksPerSecond,
            newer.TimeSeconds - older.TimeSeconds);
        var maxExpectedSpeed = MathF.Max(older.Velocity.Length(), newer.Velocity.Length());
        return MathF.Max(minimumDistance, (maxExpectedSpeed * intervalSeconds * 3f) + 24f);
    }

    private float GetRemotePlayerCorrectionSnapThreshold(
        PlayerSnapshotSample older,
        PlayerSnapshotSample newer)
    {
        var maxExpectedSpeed = MathF.Max(older.Velocity.Length(), newer.Velocity.Length());
        var bufferedDelaySeconds = _remotePlayerInterpolationBackTimeSeconds
            + _smoothedSnapshotIntervalSeconds
            + _smoothedSnapshotJitterSeconds;
        var expectedBufferedDistance = maxExpectedSpeed * MathF.Max(0.05f, bufferedDelaySeconds);
        return MathF.Max(
            RemotePlayerCorrectionSnapDistance * 2f,
            (expectedBufferedDistance * 1.75f) + 32f);
    }

    private static Vector2 InterpolateRemotePlayerSample(PlayerSnapshotSample older, PlayerSnapshotSample newer, double renderTimeSeconds)
    {
        var durationSeconds = newer.TimeSeconds - older.TimeSeconds;
        if (durationSeconds <= 0.0001d)
        {
            return newer.Position;
        }

        var alpha = float.Clamp((float)((renderTimeSeconds - older.TimeSeconds) / durationSeconds), 0f, 1f);
        var t2 = alpha * alpha;
        var t3 = t2 * alpha;
        var h00 = (2f * t3) - (3f * t2) + 1f;
        var h10 = t3 - (2f * t2) + alpha;
        var h01 = (-2f * t3) + (3f * t2);
        var h11 = t3 - t2;
        var duration = (float)durationSeconds;
        return (older.Position * h00)
            + (older.Velocity * duration * h10)
            + (newer.Position * h01)
            + (newer.Velocity * duration * h11);
    }

    private static Vector2 EvaluateRemotePlayerExtrapolation(PlayerSnapshotSample sample, double renderTimeSeconds)
    {
        var extrapolationSeconds = float.Clamp((float)(renderTimeSeconds - sample.TimeSeconds), 0f, 0.075f);
        if (extrapolationSeconds <= 0f || sample.Velocity == Vector2.Zero)
        {
            return sample.Position;
        }

        var offset = sample.Velocity * extrapolationSeconds;
        var distance = offset.Length();
        if (distance > 36f)
        {
            offset *= 36f / distance;
        }

        return sample.Position + offset;
    }

    private double GetRemotePlayerRenderTimeSeconds()
    {
        return GetSnapshotRenderTimeSeconds(_remotePlayerInterpolationBackTimeSeconds);
    }

    private static double GetSnapshotTimelineTimeSeconds(ulong snapshotFrame, int tickRate)
    {
        var effectiveTickRate = tickRate > 0 ? tickRate : SimulationConfig.DefaultTicksPerSecond;
        return snapshotFrame / (double)effectiveTickRate;
    }

    private double GetSnapshotRenderTimeSeconds(float backTimeSeconds)
    {
        return GetEstimatedServerTimeSeconds() - backTimeSeconds;
    }

    private double GetEstimatedServerTimeSeconds()
    {
        if (_latestSnapshotServerTimeSeconds < 0d)
        {
            return _networkInterpolationClockSeconds;
        }

        if (_latestSnapshotReceivedClockSeconds < 0d)
        {
            return _latestSnapshotServerTimeSeconds;
        }

        var extrapolationHeadroomSeconds = Math.Clamp(
            Math.Max(
                _smoothedSnapshotIntervalSeconds + (_smoothedSnapshotJitterSeconds * 2f),
                0.05f),
            0.05f,
            0.15f);
        var localElapsedSinceSnapshotSeconds = Math.Clamp(
            _networkInterpolationClockSeconds - _latestSnapshotReceivedClockSeconds,
            0d,
            extrapolationHeadroomSeconds);
        return _latestSnapshotServerTimeSeconds + localElapsedSinceSnapshotSeconds;
    }

    private void CaptureProjectileInterpolationTarget(int entityId, float x, float y, Vector2 velocity, float maxExtrapolationDistance, double snapshotTimeSeconds)
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
            snapshotTimeSeconds);
    }

    private void CaptureEntityInterpolationTarget(
        bool isActive,
        int entityId,
        float x,
        float y,
        Vector2 velocity,
        float extrapolationDurationSeconds,
        float maxExtrapolationDistance,
        double snapshotTimeSeconds)
    {
        if (!isActive)
        {
            _entityInterpolationTracks.Remove(entityId);
            _entitySnapshotHistories.Remove(entityId);
            _interpolatedEntityPositions[entityId] = new Vector2(x, y);
            return;
        }

        AppendEntitySnapshot(
            _entitySnapshotHistories,
            entityId,
            new Vector2(x, y),
            velocity,
            snapshotTimeSeconds,
            extrapolationDurationSeconds,
            maxExtrapolationDistance);
        _entityInterpolationTracks.Remove(entityId);
    }

    private void CaptureIntelInterpolationTarget(PlayerTeam team, float x, float y, double snapshotTimeSeconds)
    {
        AppendEntitySnapshot(
            _intelSnapshotHistories,
            team,
            new Vector2(x, y),
            Vector2.Zero,
            snapshotTimeSeconds,
            0f,
            0f);
        _intelInterpolationTracks.Remove(team);
    }

    private void AppendEntitySnapshot<TKey>(
        Dictionary<TKey, List<EntitySnapshotSample>> histories,
        TKey key,
        Vector2 position,
        Vector2 velocity,
        double snapshotTimeSeconds,
        float extrapolationDurationSeconds,
        float maxExtrapolationDistance)
        where TKey : notnull
    {
        var sample = new EntitySnapshotSample(
            position,
            velocity,
            snapshotTimeSeconds,
            extrapolationDurationSeconds,
            maxExtrapolationDistance);
        if (!histories.TryGetValue(key, out var history))
        {
            history = new List<EntitySnapshotSample>(4);
            histories[key] = history;
        }

        if (history.Count > 0)
        {
            var latest = history[^1];
            if (sample.TimeSeconds <= latest.TimeSeconds)
            {
                history[^1] = sample;
            }
            else if (ShouldResetEntitySnapshotHistory(latest, sample))
            {
                history.Clear();
                history.Add(sample);
            }
            else
            {
                history.Add(sample);
            }
        }
        else
        {
            history.Add(sample);
        }

        var minHistoryTimeSeconds = snapshotTimeSeconds - 0.25d;
        while (history.Count > 2 && history[1].TimeSeconds < minHistoryTimeSeconds)
        {
            history.RemoveAt(0);
        }
    }

    private static bool ShouldResetEntitySnapshotHistory(EntitySnapshotSample older, EntitySnapshotSample newer)
    {
        var intervalSeconds = (float)Math.Max(
            1d / SimulationConfig.DefaultTicksPerSecond,
            newer.TimeSeconds - older.TimeSeconds);
        var maxExpectedSpeed = MathF.Max(older.Velocity.Length(), newer.Velocity.Length());
        var extrapolationAllowance = MathF.Max(older.MaxExtrapolationDistance, newer.MaxExtrapolationDistance);
        var snapThreshold = MathF.Max(24f, (maxExpectedSpeed * intervalSeconds * 3f) + extrapolationAllowance + 8f);
        return Vector2.DistanceSquared(older.Position, newer.Position) > snapThreshold * snapThreshold;
    }

    private Vector2 EvaluateEntitySnapshotHistory(List<EntitySnapshotSample> history, double renderTimeSeconds)
    {
        if (history.Count == 0)
        {
            return Vector2.Zero;
        }

        if (history.Count == 1)
        {
            return EvaluateEntitySampleExtrapolation(history[0], renderTimeSeconds);
        }

        if (renderTimeSeconds <= history[0].TimeSeconds)
        {
            return history[0].Position;
        }

        for (var index = 1; index < history.Count; index += 1)
        {
            var newer = history[index];
            if (renderTimeSeconds > newer.TimeSeconds)
            {
                continue;
            }

            var older = history[index - 1];
            return InterpolateEntitySnapshotSample(older, newer, renderTimeSeconds);
        }

        return EvaluateEntitySampleExtrapolation(history[^1], renderTimeSeconds);
    }

    private static Vector2 InterpolateEntitySnapshotSample(EntitySnapshotSample older, EntitySnapshotSample newer, double renderTimeSeconds)
    {
        var durationSeconds = newer.TimeSeconds - older.TimeSeconds;
        if (durationSeconds <= 0.0001d)
        {
            return newer.Position;
        }

        var alpha = float.Clamp((float)((renderTimeSeconds - older.TimeSeconds) / durationSeconds), 0f, 1f);
        return Vector2.Lerp(older.Position, newer.Position, alpha);
    }

    private static Vector2 EvaluateEntitySampleExtrapolation(EntitySnapshotSample sample, double renderTimeSeconds)
    {
        var extrapolationSeconds = float.Clamp(
            (float)(renderTimeSeconds - sample.TimeSeconds),
            0f,
            sample.ExtrapolationDurationSeconds);
        if (extrapolationSeconds <= 0f || sample.Velocity == Vector2.Zero)
        {
            return sample.Position;
        }

        var offset = sample.Velocity * extrapolationSeconds;
        var distance = offset.Length();
        if (sample.MaxExtrapolationDistance > 0f && distance > sample.MaxExtrapolationDistance)
        {
            offset *= sample.MaxExtrapolationDistance / distance;
        }

        return sample.Position + offset;
    }

    private double GetEntityRenderTimeSeconds()
    {
        return GetSnapshotRenderTimeSeconds(MathF.Min(_networkSnapshotInterpolationDurationSeconds, 0.045f));
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
