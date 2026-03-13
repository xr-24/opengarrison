using GG2.Protocol;

namespace GG2.Core;

public sealed partial class SimulationWorld
{
    public bool ApplySnapshot(SnapshotMessage snapshot, byte localPlayerSlot = 1)
    {
        if ((!string.Equals(Level.Name, snapshot.LevelName, StringComparison.OrdinalIgnoreCase)
                || Level.MapAreaIndex != snapshot.MapAreaIndex)
            && !TryLoadLevel(snapshot.LevelName, snapshot.MapAreaIndex, preservePlayerStats: false))
        {
            return false;
        }

        MatchRules = MatchRules with
        {
            Mode = (GameModeKind)snapshot.GameMode,
            TimeLimitTicks = Math.Max(snapshot.TimeRemainingTicks, MatchRules.TimeLimitTicks),
        };
        MatchState = new MatchState(
            (MatchPhase)snapshot.MatchPhase,
            snapshot.TimeRemainingTicks,
            snapshot.WinnerTeam == 0 ? null : (PlayerTeam)snapshot.WinnerTeam);
        LocalDeathCam = snapshot.LocalDeathCam is null
            ? null
            : new LocalDeathCamState(
                snapshot.LocalDeathCam.FocusX,
                snapshot.LocalDeathCam.FocusY,
                snapshot.LocalDeathCam.KillMessage,
                snapshot.LocalDeathCam.KillerName,
                snapshot.LocalDeathCam.KillerTeam == 0 ? null : (PlayerTeam)snapshot.LocalDeathCam.KillerTeam,
                snapshot.LocalDeathCam.Health,
                snapshot.LocalDeathCam.MaxHealth,
                snapshot.LocalDeathCam.RemainingTicks);
        RedCaps = snapshot.RedCaps;
        BlueCaps = snapshot.BlueCaps;
        SpectatorCount = Math.Max(0, snapshot.SpectatorCount);
        ApplySnapshotControlPoints(snapshot);
        ApplySnapshotGenerators(snapshot);
        RedIntel.ApplyNetworkState(snapshot.RedIntel.X, snapshot.RedIntel.Y, snapshot.RedIntel.IsAtBase, snapshot.RedIntel.IsDropped, snapshot.RedIntel.ReturnTicksRemaining);
        BlueIntel.ApplyNetworkState(snapshot.BlueIntel.X, snapshot.BlueIntel.Y, snapshot.BlueIntel.IsAtBase, snapshot.BlueIntel.IsDropped, snapshot.BlueIntel.ReturnTicksRemaining);
        _killFeed.Clear();
        for (var killFeedIndex = 0; killFeedIndex < snapshot.KillFeed.Count; killFeedIndex += 1)
        {
            var entry = snapshot.KillFeed[killFeedIndex];
            _killFeed.Add(new KillFeedEntry(
                entry.KillerName,
                (PlayerTeam)entry.KillerTeam,
                entry.WeaponSpriteName,
                entry.VictimName,
                (PlayerTeam)entry.VictimTeam));
        }
        _killFeedTrimTicks = _killFeed.Count > 0 ? KillFeedLifetimeTicks : 0;

        var localPlayerState = snapshot.Players.FirstOrDefault(player => player.Slot == localPlayerSlot);
        var isSpectatorSnapshot = localPlayerState is null && !IsPlayableNetworkPlayerSlot(localPlayerSlot);
        if (localPlayerState is null && !isSpectatorSnapshot)
        {
            return false;
        }

        if (localPlayerState is not null)
        {
            ApplySnapshotPlayer(LocalPlayer, localPlayerState);
            TrySetNetworkPlayerAwaitingJoin(LocalPlayerSlot, localPlayerState.IsAwaitingJoin);
            TrySetNetworkPlayerRespawnTicks(LocalPlayerSlot, localPlayerState.RespawnTicks);
        }
        else
        {
            TrySetNetworkPlayerAwaitingJoin(LocalPlayerSlot, true);
            TrySetNetworkPlayerRespawnTicks(LocalPlayerSlot, 0);
            LocalDeathCam = null;
            LocalPlayer.ClearMedicHealingTarget();
            LocalPlayer.Kill();
        }

        var remotePlayerStates = snapshot.Players
            .Where(player => IsPlayableNetworkPlayerSlot(player.Slot))
            .Where(player => isSpectatorSnapshot || player.Slot != localPlayerSlot)
            .OrderBy(player => player.Slot)
            .ToList();
        EnemyPlayerEnabled = false;
        _enemyDummyRespawnTicks = 0;
        ClearEnemyInputOverride();
        EnemyPlayer.Kill();
        FriendlyDummyEnabled = false;
        FriendlyDummy.Kill();
        SyncRemoteSnapshotPlayers(remotePlayerStates);

        if (localPlayerState is not null)
        {
            TrySetNetworkPlayerConfiguredTeam(LocalPlayerSlot, LocalPlayer.Team);
        }
        ApplySnapshotTransientEntities(snapshot);
        _combatTraces.Clear();
        for (var traceIndex = 0; traceIndex < snapshot.CombatTraces.Count; traceIndex += 1)
        {
            var trace = snapshot.CombatTraces[traceIndex];
            _combatTraces.Add(new CombatTrace(
                trace.StartX,
                trace.StartY,
                trace.EndX,
                trace.EndY,
                trace.TicksRemaining,
                trace.HitCharacter,
                (PlayerTeam)trace.Team,
                trace.IsSniperTracer));
        }
        _pendingSoundEvents.Clear();
        for (var soundIndex = 0; soundIndex < snapshot.SoundEvents.Count; soundIndex += 1)
        {
            var soundEvent = snapshot.SoundEvents[soundIndex];
            _pendingSoundEvents.Add(new WorldSoundEvent(soundEvent.SoundName, soundEvent.X, soundEvent.Y, soundEvent.EventId));
        }

        return true;
    }

    private static void ApplySnapshotPlayer(PlayerEntity player, SnapshotPlayerState snapshotPlayer)
    {
        player.SetDisplayName(snapshotPlayer.Name);
        player.ApplyNetworkState(
            (PlayerTeam)snapshotPlayer.Team,
            CharacterClassCatalog.GetDefinition((PlayerClass)snapshotPlayer.ClassId),
            snapshotPlayer.IsAlive,
            snapshotPlayer.X,
            snapshotPlayer.Y,
            snapshotPlayer.HorizontalSpeed,
            snapshotPlayer.VerticalSpeed,
            snapshotPlayer.Health,
            snapshotPlayer.Ammo,
            snapshotPlayer.Kills,
            snapshotPlayer.Deaths,
            snapshotPlayer.Caps,
            snapshotPlayer.HealPoints,
            snapshotPlayer.Metal,
            snapshotPlayer.IsGrounded,
            snapshotPlayer.IsCarryingIntel,
            snapshotPlayer.IsSpyCloaked,
            snapshotPlayer.IsUbered,
            snapshotPlayer.IsHeavyEating,
            snapshotPlayer.HeavyEatTicksRemaining,
            snapshotPlayer.IsSniperScoped,
            snapshotPlayer.SniperChargeTicks,
            snapshotPlayer.FacingDirectionX,
            snapshotPlayer.AimDirectionDegrees,
            snapshotPlayer.IsTaunting,
            snapshotPlayer.TauntFrameIndex,
            snapshotPlayer.IsChatBubbleVisible,
            snapshotPlayer.ChatBubbleFrameIndex,
            snapshotPlayer.ChatBubbleAlpha);
    }

    private void ApplySnapshotTransientEntities(SnapshotMessage snapshot)
    {
        ClearTransientSnapshotEntities();
        ApplySnapshotSentries(snapshot.Sentries);
        ApplySnapshotShots(snapshot.Shots, _shots, state =>
        {
            var entity = new ShotProjectileEntity(state.Id, (PlayerTeam)state.Team, state.OwnerId, state.X, state.Y, state.VelocityX, state.VelocityY);
            entity.ApplyNetworkState(state.X, state.Y, state.VelocityX, state.VelocityY, state.TicksRemaining);
            return entity;
        });
        ApplySnapshotShots(snapshot.Bubbles, _bubbles, state =>
        {
            var entity = new BubbleProjectileEntity(state.Id, (PlayerTeam)state.Team, state.OwnerId, state.X, state.Y, state.VelocityX, state.VelocityY);
            entity.ApplyNetworkState(state.X, state.Y, state.VelocityX, state.VelocityY, state.TicksRemaining);
            return entity;
        });
        ApplySnapshotShots(snapshot.Blades, _blades, state =>
        {
            var entity = new BladeProjectileEntity(state.Id, (PlayerTeam)state.Team, state.OwnerId, state.X, state.Y, state.VelocityX, state.VelocityY, hitDamage: 0);
            entity.ApplyNetworkState(state.X, state.Y, state.VelocityX, state.VelocityY, state.TicksRemaining, hitDamage: 0);
            return entity;
        });
        ApplySnapshotShots(snapshot.Needles, _needles, state =>
        {
            var entity = new NeedleProjectileEntity(state.Id, (PlayerTeam)state.Team, state.OwnerId, state.X, state.Y, state.VelocityX, state.VelocityY);
            entity.ApplyNetworkState(state.X, state.Y, state.VelocityX, state.VelocityY, state.TicksRemaining);
            return entity;
        });
        ApplySnapshotShots(snapshot.RevolverShots, _revolverShots, state =>
        {
            var entity = new RevolverProjectileEntity(state.Id, (PlayerTeam)state.Team, state.OwnerId, state.X, state.Y, state.VelocityX, state.VelocityY);
            entity.ApplyNetworkState(state.X, state.Y, state.VelocityX, state.VelocityY, state.TicksRemaining);
            return entity;
        });
        ApplySnapshotRockets(snapshot.Rockets);
        ApplySnapshotFlames(snapshot.Flames);
        ApplySnapshotMines(snapshot.Mines);
        ApplySnapshotPlayerGibs(snapshot.PlayerGibs);
        ApplySnapshotBloodDrops(snapshot.BloodDrops);
        ApplySnapshotDeadBodies(snapshot.DeadBodies);
    }

    private void ClearTransientSnapshotEntities()
    {
        RemoveEntities(_shots);
        RemoveEntities(_bubbles);
        RemoveEntities(_blades);
        RemoveEntities(_needles);
        RemoveEntities(_revolverShots);
        RemoveEntities(_flames);
        RemoveEntities(_rockets);
        RemoveEntities(_mines);
        RemoveEntities(_sentries);
        RemoveEntities(_playerGibs);
        RemoveEntities(_bloodDrops);
        RemoveEntities(_deadBodies);
    }

    private void ApplySnapshotSentries(IReadOnlyList<SnapshotSentryState> sentries)
    {
        for (var index = 0; index < sentries.Count; index += 1)
        {
            var sentryState = sentries[index];
            var entity = new SentryEntity(
                sentryState.Id,
                sentryState.OwnerPlayerId,
                (PlayerTeam)sentryState.Team,
                sentryState.X,
                sentryState.Y,
                sentryState.FacingDirectionX);
            entity.ApplyNetworkState(
                sentryState.X,
                sentryState.Y,
                sentryState.Health,
                sentryState.IsBuilt,
                sentryState.FacingDirectionX,
                sentryState.DesiredFacingDirectionX,
                sentryState.AimDirectionDegrees,
                sentryState.ReloadTicksRemaining,
                sentryState.AlertTicksRemaining,
                sentryState.ShotTraceTicksRemaining,
                sentryState.HasLanded,
                sentryState.HasActiveTarget,
                sentryState.CurrentTargetPlayerId < 0 ? null : sentryState.CurrentTargetPlayerId,
                sentryState.LastShotTargetX,
                sentryState.LastShotTargetY);
            _sentries.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void ApplySnapshotShots<T>(IReadOnlyList<SnapshotShotState> shots, List<T> target, Func<SnapshotShotState, T> factory)
        where T : SimulationEntity
    {
        for (var index = 0; index < shots.Count; index += 1)
        {
            var entity = factory(shots[index]);
            target.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void ApplySnapshotRockets(IReadOnlyList<SnapshotRocketState> rockets)
    {
        for (var index = 0; index < rockets.Count; index += 1)
        {
            var rocketState = rockets[index];
            var entity = new RocketProjectileEntity(
                rocketState.Id,
                (PlayerTeam)rocketState.Team,
                rocketState.OwnerId,
                rocketState.X,
                rocketState.Y,
                rocketState.Speed,
                rocketState.DirectionRadians);
            entity.ApplyNetworkState(rocketState.X, rocketState.Y, rocketState.PreviousX, rocketState.PreviousY, rocketState.DirectionRadians, rocketState.Speed, rocketState.TicksRemaining);
            _rockets.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void ApplySnapshotFlames(IReadOnlyList<SnapshotFlameState> flames)
    {
        for (var index = 0; index < flames.Count; index += 1)
        {
            var flameState = flames[index];
            var entity = new FlameProjectileEntity(
                flameState.Id,
                (PlayerTeam)flameState.Team,
                flameState.OwnerId,
                flameState.X,
                flameState.Y,
                flameState.VelocityX,
                flameState.VelocityY);
            entity.ApplyNetworkState(
                flameState.X,
                flameState.Y,
                flameState.PreviousX,
                flameState.PreviousY,
                flameState.VelocityX,
                flameState.VelocityY,
                flameState.TicksRemaining,
                flameState.AttachedPlayerId < 0 ? null : flameState.AttachedPlayerId,
                flameState.AttachedOffsetX,
                flameState.AttachedOffsetY);
            _flames.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void ApplySnapshotBloodDrops(IReadOnlyList<SnapshotBloodDropState> bloodDrops)
    {
        for (var index = 0; index < bloodDrops.Count; index += 1)
        {
            var bloodDropState = bloodDrops[index];
            var entity = new BloodDropEntity(
                bloodDropState.Id,
                bloodDropState.X,
                bloodDropState.Y,
                bloodDropState.VelocityX,
                bloodDropState.VelocityY);
            entity.ApplyNetworkState(
                bloodDropState.X,
                bloodDropState.Y,
                bloodDropState.VelocityX,
                bloodDropState.VelocityY,
                bloodDropState.IsStuck,
                bloodDropState.TicksRemaining);
            _bloodDrops.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void ApplySnapshotMines(IReadOnlyList<SnapshotMineState> mines)
    {
        for (var index = 0; index < mines.Count; index += 1)
        {
            var mineState = mines[index];
            var entity = new MineProjectileEntity(
                mineState.Id,
                (PlayerTeam)mineState.Team,
                mineState.OwnerId,
                mineState.X,
                mineState.Y,
                mineState.VelocityX,
                mineState.VelocityY);
            entity.ApplyNetworkState(
                mineState.X,
                mineState.Y,
                mineState.VelocityX,
                mineState.VelocityY,
                mineState.IsStickied,
                mineState.IsDestroyed,
                mineState.ExplosionDamage);
            _mines.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void ApplySnapshotDeadBodies(IReadOnlyList<SnapshotDeadBodyState> deadBodies)
    {
        for (var index = 0; index < deadBodies.Count; index += 1)
        {
            var deadBodyState = deadBodies[index];
            var entity = new DeadBodyEntity(
                deadBodyState.Id,
                (PlayerClass)deadBodyState.ClassId,
                (PlayerTeam)deadBodyState.Team,
                deadBodyState.X,
                deadBodyState.Y,
                deadBodyState.Width,
                deadBodyState.Height,
                deadBodyState.HorizontalSpeed,
                deadBodyState.VerticalSpeed,
                deadBodyState.FacingLeft);
            entity.ApplyNetworkState(
                deadBodyState.X,
                deadBodyState.Y,
                deadBodyState.HorizontalSpeed,
                deadBodyState.VerticalSpeed,
                deadBodyState.TicksRemaining);
            _deadBodies.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void ApplySnapshotPlayerGibs(IReadOnlyList<SnapshotPlayerGibState> playerGibs)
    {
        for (var index = 0; index < playerGibs.Count; index += 1)
        {
            var playerGibState = playerGibs[index];
            var entity = new PlayerGibEntity(
                playerGibState.Id,
                playerGibState.SpriteName,
                playerGibState.FrameIndex,
                playerGibState.X,
                playerGibState.Y,
                playerGibState.VelocityX,
                playerGibState.VelocityY,
                playerGibState.RotationSpeedDegrees,
                horizontalFriction: 0.4f,
                rotationFriction: 0.6f,
                lifetimeTicks: playerGibState.TicksRemaining,
                bloodChance: playerGibState.BloodChance);
            entity.ApplyNetworkState(
                playerGibState.X,
                playerGibState.Y,
                playerGibState.VelocityX,
                playerGibState.VelocityY,
                playerGibState.RotationDegrees,
                playerGibState.RotationSpeedDegrees,
                playerGibState.TicksRemaining);
            _playerGibs.Add(entity);
            _entities[entity.Id] = entity;
        }
    }

    private void SyncRemoteSnapshotPlayers(IEnumerable<SnapshotPlayerState> snapshotPlayers)
    {
        var activeSlots = new HashSet<byte>();
        _remoteSnapshotPlayers.Clear();
        foreach (var snapshotPlayer in snapshotPlayers)
        {
            activeSlots.Add(snapshotPlayer.Slot);
            if (!_remoteSnapshotPlayersBySlot.TryGetValue(snapshotPlayer.Slot, out var player))
            {
                ReserveEntityId(snapshotPlayer.PlayerId);
                player = new PlayerEntity(
                    snapshotPlayer.PlayerId,
                    CharacterClassCatalog.GetDefinition((PlayerClass)snapshotPlayer.ClassId),
                    snapshotPlayer.Name);
                _remoteSnapshotPlayersBySlot[snapshotPlayer.Slot] = player;
            }

            ApplySnapshotPlayer(player, snapshotPlayer);
            _remoteSnapshotPlayers.Add(player);
        }

        var staleSlots = new List<byte>();
        foreach (var entry in _remoteSnapshotPlayersBySlot)
        {
            if (activeSlots.Contains(entry.Key))
            {
                continue;
            }

            staleSlots.Add(entry.Key);
        }

        for (var index = 0; index < staleSlots.Count; index += 1)
        {
            _remoteSnapshotPlayersBySlot.Remove(staleSlots[index]);
        }
    }

    private void ReserveEntityId(int entityId)
    {
        if (entityId >= _nextEntityId)
        {
            _nextEntityId = entityId + 1;
        }
    }
}
