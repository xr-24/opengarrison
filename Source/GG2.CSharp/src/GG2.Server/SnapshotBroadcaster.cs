using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using GG2.Core;
using GG2.Protocol;
using static ServerHelpers;

sealed class SnapshotBroadcaster
{
    private readonly SimulationWorld _world;
    private readonly SimulationConfig _config;
    private readonly Dictionary<byte, ClientSession> _clientsBySlot;
    private readonly Action<IPEndPoint, IProtocolMessage> _sendMessage;
    private readonly ulong _transientEventReplayTicks;
    private readonly List<RetainedSnapshotSoundEvent> _recentSoundEvents = new();
    private readonly List<RetainedSnapshotVisualEvent> _recentVisualEvents = new();
    private ulong _nextTransientEventId = 1;

    public SnapshotBroadcaster(
        SimulationWorld world,
        SimulationConfig config,
        Dictionary<byte, ClientSession> clientsBySlot,
        ulong transientEventReplayTicks,
        Action<IPEndPoint, IProtocolMessage> sendMessage)
    {
        _world = world;
        _config = config;
        _clientsBySlot = clientsBySlot;
        _transientEventReplayTicks = transientEventReplayTicks;
        _sendMessage = sendMessage;
    }

    public void ResetTransientEvents()
    {
        _recentSoundEvents.Clear();
        _recentVisualEvents.Clear();
        _nextTransientEventId = 1;
    }

    public void BroadcastSnapshots(int ticks)
    {
        if (ticks <= 0 || _clientsBySlot.Count == 0)
        {
            return;
        }

        for (var tick = 0; tick < ticks; tick += 1)
        {
            var currentFrame = (ulong)_world.Frame;
            AppendRetainedVisualEvents(_world.DrainPendingVisualEvents(), currentFrame);
            AppendRetainedSoundEvents(_world.DrainPendingSoundEvents(), currentFrame);
            _recentVisualEvents.RemoveAll(visualEvent => visualEvent.ExpiresAfterFrame < currentFrame);
            _recentSoundEvents.RemoveAll(soundEvent => soundEvent.ExpiresAfterFrame < currentFrame);
            var visualEvents = _recentVisualEvents.Select(visualEvent => visualEvent.Event).ToArray();
            var soundEvents = _recentSoundEvents.Select(soundEvent => soundEvent.Event).ToArray();
            foreach (var client in _clientsBySlot.Values)
            {
                SendSnapshot(client, visualEvents, soundEvents);
            }
        }
    }

    private void AppendRetainedSoundEvents(IReadOnlyList<WorldSoundEvent> soundEvents, ulong currentFrame)
    {
        for (var index = 0; index < soundEvents.Count; index += 1)
        {
            _recentSoundEvents.Add(new RetainedSnapshotSoundEvent(
                ToSnapshotSoundEvent(soundEvents[index], _nextTransientEventId++),
                currentFrame + _transientEventReplayTicks));
        }
    }

    private void AppendRetainedVisualEvents(IReadOnlyList<WorldVisualEvent> visualEvents, ulong currentFrame)
    {
        for (var index = 0; index < visualEvents.Count; index += 1)
        {
            _recentVisualEvents.Add(new RetainedSnapshotVisualEvent(
                ToSnapshotVisualEvent(visualEvents[index], _nextTransientEventId++),
                currentFrame + _transientEventReplayTicks));
        }
    }

    private void SendSnapshot(ClientSession client, SnapshotVisualEvent[] visualEvents, SnapshotSoundEvent[] soundEvents)
    {
        var players = _world
            .EnumerateActiveNetworkPlayers()
            .Select(entry => ToSnapshotPlayerState(_world, entry.Slot, entry.Player))
            .ToList();

        var spectatorCount = _clientsBySlot.Keys.Count(IsSpectatorSlot);
        var mapAreaIndex = (byte)Math.Clamp(_world.Level.MapAreaIndex, 1, byte.MaxValue);
        var mapAreaCount = (byte)Math.Clamp(_world.Level.MapAreaCount, 1, byte.MaxValue);
        var snapshot = new SnapshotMessage(
            (ulong)_world.Frame,
            _config.TicksPerSecond,
            _world.Level.Name,
            mapAreaIndex,
            mapAreaCount,
            (byte)_world.MatchRules.Mode,
            (byte)_world.MatchState.Phase,
            _world.MatchState.WinnerTeam.HasValue ? (byte)_world.MatchState.WinnerTeam.Value : (byte)0,
            _world.MatchState.TimeRemainingTicks,
            _world.RedCaps,
            _world.BlueCaps,
            spectatorCount,
            client.LastInputSequence,
            ToSnapshotIntelState(_world.RedIntel),
            ToSnapshotIntelState(_world.BlueIntel),
            players,
            _world.CombatTraces.Select(ToSnapshotCombatTraceState).ToArray(),
            _world.Sentries.Select(ToSnapshotSentryState).ToArray(),
            _world.Shots.Select(ToSnapshotBulletState).ToArray(),
            _world.Bubbles.Select(ToSnapshotBubbleState).ToArray(),
            _world.Blades.Select(ToSnapshotBladeState).ToArray(),
            _world.Needles.Select(ToSnapshotNeedleState).ToArray(),
            _world.RevolverShots.Select(ToSnapshotRevolverState).ToArray(),
            _world.Rockets.Select(ToSnapshotRocketState).ToArray(),
            _world.Flames.Select(ToSnapshotFlameState).ToArray(),
            _world.Mines.Select(ToSnapshotMineState).ToArray(),
            _world.PlayerGibs.Select(ToSnapshotPlayerGibState).ToArray(),
            _world.BloodDrops.Select(ToSnapshotBloodDropState).ToArray(),
            _world.DeadBodies.Select(ToSnapshotDeadBodyState).ToArray(),
            _world.ControlPointSetupTicksRemaining,
            _world.ControlPoints.Select(ToSnapshotControlPointState).ToArray(),
            _world.Generators.Select(ToSnapshotGeneratorState).ToArray(),
            ToSnapshotDeathCamState(_world.GetNetworkPlayerDeathCam(client.Slot)),
            _world.KillFeed.Select(ToSnapshotKillFeedEntry).ToArray(),
            visualEvents,
            soundEvents);

        _sendMessage(client.EndPoint, snapshot);
    }
}
