#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private Vector2 GetCameraTopLeft(int viewportWidth, int viewportHeight, int mouseX, int mouseY)
    {
        float x;
        float y;
        if (_killCamEnabled && !_world.LocalPlayer.IsAlive && _world.LocalDeathCam is not null)
        {
            var halfViewportWidth = viewportWidth / 2f;
            var halfViewportHeight = viewportHeight / 2f;
            x = Math.Clamp(
                _world.LocalDeathCam.FocusX - halfViewportWidth,
                0f,
                Math.Max(0f, _world.Bounds.Width - viewportWidth));
            y = Math.Clamp(
                _world.LocalDeathCam.FocusY - halfViewportHeight,
                0f,
                Math.Max(0f, _world.Bounds.Height - viewportHeight));
            return new Vector2(x, y);
        }

        var localViewPosition = GetLocalViewPosition();
        if (GetPlayerIsSniperScoped(_world.LocalPlayer))
        {
            x = Math.Clamp(
                localViewPosition.X + mouseX - viewportWidth,
                0f,
                Math.Max(0f, _world.Bounds.Width - viewportWidth));
            y = Math.Clamp(
                localViewPosition.Y + mouseY - viewportHeight,
                0f,
                Math.Max(0f, _world.Bounds.Height - viewportHeight));
        }
        else
        {
            var halfViewportWidth = viewportWidth / 2f;
            var halfViewportHeight = viewportHeight / 2f;

            x = Math.Clamp(
                localViewPosition.X - halfViewportWidth,
                0f,
                Math.Max(0f, _world.Bounds.Width - viewportWidth));
            y = Math.Clamp(
                localViewPosition.Y - halfViewportHeight,
                0f,
                Math.Max(0f, _world.Bounds.Height - viewportHeight));
        }

        return new Vector2(x, y);
    }

    private Vector2 GetLocalViewPosition()
    {
        if (_networkClient.IsSpectator)
        {
            var spectatorFocus = GetSpectatorFocusPlayer();
            if (spectatorFocus is not null)
            {
                return GetRenderPosition(spectatorFocus);
            }
        }

        if (_networkClient.IsConnected && _world.LocalPlayer.IsAlive)
        {
            if (_hasSmoothedLocalPlayerRenderPosition)
            {
                return _smoothedLocalPlayerRenderPosition;
            }

            if (_hasPredictedLocalPlayerPosition)
            {
                return _predictedLocalPlayerPosition;
            }
        }

        return new Vector2(_world.LocalPlayer.X, _world.LocalPlayer.Y);
    }

    private bool IsUsingPredictedLocalState(PlayerEntity player)
    {
        return _networkClient.IsConnected
            && ReferenceEquals(player, _world.LocalPlayer)
            && _hasPredictedLocalActionState;
    }

    private bool GetPlayerIsHeavyEating(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.IsHeavyEating
            : player.IsHeavyEating;
    }

    private int GetPlayerHeavyEatTicksRemaining(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.HeavyEatTicksRemaining
            : player.HeavyEatTicksRemaining;
    }

    private bool GetPlayerIsSniperScoped(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.IsSniperScoped
            : player.IsSniperScoped;
    }

    private int GetPlayerSniperChargeTicks(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.SniperChargeTicks
            : player.SniperChargeTicks;
    }

    private int GetPlayerSniperRifleDamage(PlayerEntity player)
    {
        if (player.ClassId != PlayerClass.Sniper || !GetPlayerIsSniperScoped(player))
        {
            return PlayerEntity.SniperBaseDamage;
        }

        var chargeTicks = GetPlayerSniperChargeTicks(player);
        return PlayerEntity.SniperBaseDamage + (int)MathF.Floor(MathF.Sqrt(chargeTicks * 125f / 6f));
    }

    private bool GetPlayerIsSpyCloaked(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.IsSpyCloaked
            : player.IsSpyCloaked;
    }

    private bool GetPlayerIsSpyVisibleToEnemies(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.IsSpyVisibleToEnemies
            : player.IsSpyVisibleToEnemies;
    }

    private bool GetPlayerIsSpyBackstabAnimating(PlayerEntity player)
    {
        if (!IsUsingPredictedLocalState(player))
        {
            return player.IsSpyBackstabAnimating;
        }

        return _predictedLocalActionState.SpyBackstabWindupTicksRemaining > 0
            || _predictedLocalActionState.SpyBackstabRecoveryTicksRemaining > 0;
    }

    private float GetPlayerMedicUberCharge(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.MedicUberCharge
            : player.MedicUberCharge;
    }

    private float GetPlayerMetal(PlayerEntity player)
    {
        return IsUsingPredictedLocalState(player)
            ? _predictedLocalActionState.Metal
            : player.Metal;
    }

    private PlayerEntity? GetSpectatorFocusPlayer()
    {
        PlayerEntity? fallback = null;
        foreach (var player in EnumerateRemotePlayersForView())
        {
            if (player.IsAlive)
            {
                return player;
            }

            fallback ??= player;
        }

        return fallback;
    }

    private IEnumerable<PlayerEntity> EnumerateRemotePlayersForView()
    {
        if (_networkClient.IsConnected)
        {
            for (var index = 0; index < _world.RemoteSnapshotPlayers.Count; index += 1)
            {
                yield return _world.RemoteSnapshotPlayers[index];
            }

            yield break;
        }

        if (_config.EnableEnemyTrainingDummy && _world.EnemyPlayerEnabled)
        {
            yield return _world.EnemyPlayer;
        }

        if (_config.EnableFriendlySupportDummy && _world.FriendlyDummyEnabled)
        {
            yield return _world.FriendlyDummy;
        }
    }

    private static string GetIntelStateLabel(TeamIntelligenceState intelState)
    {
        if (intelState.IsAtBase)
        {
            return "home";
        }

        if (intelState.IsDropped)
        {
            return $"dropped:{intelState.ReturnTicksRemaining}";
        }

        return "carried";
    }

    private PlayerEntity? FindPlayerById(int playerId)
    {
        if (_world.LocalPlayer.Id == playerId)
        {
            return _world.LocalPlayer;
        }

        foreach (var player in EnumerateRemotePlayersForView())
        {
            if (player.Id == playerId)
            {
                return player;
            }
        }

        return null;
    }
}
