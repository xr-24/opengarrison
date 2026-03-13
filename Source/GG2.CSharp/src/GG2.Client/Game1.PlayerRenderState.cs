#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private void UpdatePlayerRenderState(PlayerEntity player)
    {
        var playerStateKey = GetPlayerStateKey(player);
        if (!player.IsAlive)
        {
            _playerAnimationImages.Remove(playerStateKey);
            _playerWeaponFlashTicks.Remove(playerStateKey);
            _playerPreviousAmmoCounts.Remove(playerStateKey);
            _playerPreviousCooldownTicks.Remove(playerStateKey);
            _playerPreviousRenderPositions.Remove(playerStateKey);
            _playerPreviousRenderSampleTimes.Remove(playerStateKey);
            return;
        }

        var observedRenderVelocity = SampleObservedRenderVelocity(player);
        var renderHorizontalSpeed = GetPlayerRenderHorizontalSpeed(player, observedRenderVelocity);
        var renderVerticalSpeed = GetPlayerRenderVerticalSpeed(player, observedRenderVelocity);
        var horizontalStepSpeed = GetPlayerAnimationStepSpeed(renderHorizontalSpeed);
        var verticalStepSpeed = GetPlayerAnimationStepSpeed(renderVerticalSpeed);
        var isRemoteNetworkPlayer = _networkClient.IsConnected && !ReferenceEquals(player, _world.LocalPlayer);
        var animationImage = _playerAnimationImages.GetValueOrDefault(playerStateKey, 0f);
        if (horizontalStepSpeed < 0.2f)
        {
            animationImage = 0f;
        }

        var appearsAirborne = !GetPlayerRenderIsGrounded(player);
        if (_networkClient.IsConnected
            && ReferenceEquals(player, _world.LocalPlayer)
            && MathF.Abs(renderVerticalSpeed) > 20f)
        {
            appearsAirborne = true;
        }

        if (isRemoteNetworkPlayer && !player.IsGrounded)
        {
            appearsAirborne = verticalStepSpeed > 0.35f;
        }

        if (appearsAirborne)
        {
            animationImage = 1f;
        }

        animationImage = (animationImage + horizontalStepSpeed / 20f) % 2f;
        _playerAnimationImages[playerStateKey] = animationImage;

        var flashTicks = _playerWeaponFlashTicks.GetValueOrDefault(playerStateKey, 0);
        if (flashTicks > 0)
        {
            flashTicks -= 1;
        }

        var previousAmmo = _playerPreviousAmmoCounts.GetValueOrDefault(playerStateKey, player.CurrentShells);
        var previousCooldown = _playerPreviousCooldownTicks.GetValueOrDefault(playerStateKey, player.PrimaryCooldownTicks);
        if (player.PrimaryCooldownTicks > 0 && (player.CurrentShells < previousAmmo || previousCooldown <= 0))
        {
            flashTicks = 1;
        }

        _playerWeaponFlashTicks[playerStateKey] = flashTicks;
        _playerPreviousAmmoCounts[playerStateKey] = player.CurrentShells;
        _playerPreviousCooldownTicks[playerStateKey] = player.PrimaryCooldownTicks;
    }

    private void RemoveStalePlayerRenderState()
    {
        var activePlayerIds = new HashSet<int>();
        if (_world.LocalPlayer.IsAlive)
        {
            activePlayerIds.Add(GetPlayerStateKey(_world.LocalPlayer));
        }

        foreach (var player in EnumerateRemotePlayersForView())
        {
            if (player.IsAlive)
            {
                activePlayerIds.Add(GetPlayerStateKey(player));
            }
        }

        var stalePlayerIds = new List<int>();
        foreach (var playerId in _playerAnimationImages.Keys)
        {
            if (!activePlayerIds.Contains(playerId))
            {
                stalePlayerIds.Add(playerId);
            }
        }

        foreach (var playerId in stalePlayerIds)
        {
            _playerAnimationImages.Remove(playerId);
            _playerWeaponFlashTicks.Remove(playerId);
            _playerPreviousAmmoCounts.Remove(playerId);
            _playerPreviousCooldownTicks.Remove(playerId);
            _playerPreviousRenderPositions.Remove(playerId);
            _playerPreviousRenderSampleTimes.Remove(playerId);
        }
    }

    private float GetPlayerAnimationStepSpeed(float speedPerSecond)
    {
        return MathF.Abs(speedPerSecond) * (float)_config.FixedDeltaSeconds;
    }

    private Vector2 SampleObservedRenderVelocity(PlayerEntity player)
    {
        var playerStateKey = GetPlayerStateKey(player);
        var currentPosition = GetRenderPosition(player, allowInterpolation: !ReferenceEquals(player, _world.LocalPlayer));
        var currentTimeSeconds = _networkInterpolationClockSeconds;
        if (!_playerPreviousRenderPositions.TryGetValue(playerStateKey, out var previousPosition)
            || !_playerPreviousRenderSampleTimes.TryGetValue(playerStateKey, out var previousTimeSeconds))
        {
            _playerPreviousRenderPositions[playerStateKey] = currentPosition;
            _playerPreviousRenderSampleTimes[playerStateKey] = currentTimeSeconds;
            return Vector2.Zero;
        }

        _playerPreviousRenderPositions[playerStateKey] = currentPosition;
        _playerPreviousRenderSampleTimes[playerStateKey] = currentTimeSeconds;

        var elapsedSeconds = currentTimeSeconds - previousTimeSeconds;
        if (elapsedSeconds <= 0.0001d)
        {
            return Vector2.Zero;
        }

        return (currentPosition - previousPosition) / (float)elapsedSeconds;
    }

    private float GetPlayerRenderHorizontalSpeed(PlayerEntity player, Vector2 observedRenderVelocity)
    {
        if (_networkClient.IsConnected && ReferenceEquals(player, _world.LocalPlayer))
        {
            if (_hasPredictedLocalPlayerPosition)
            {
                return MathF.Abs(observedRenderVelocity.X) > MathF.Abs(_predictedLocalPlayerVelocity.X)
                    ? observedRenderVelocity.X
                    : _predictedLocalPlayerVelocity.X;
            }

            return observedRenderVelocity.X;
        }

        return player.HorizontalSpeed;
    }

    private float GetPlayerRenderVerticalSpeed(PlayerEntity player, Vector2 observedRenderVelocity)
    {
        if (_networkClient.IsConnected && ReferenceEquals(player, _world.LocalPlayer))
        {
            if (_hasPredictedLocalPlayerPosition)
            {
                return MathF.Abs(observedRenderVelocity.Y) > MathF.Abs(_predictedLocalPlayerVelocity.Y)
                    ? observedRenderVelocity.Y
                    : _predictedLocalPlayerVelocity.Y;
            }

            return observedRenderVelocity.Y;
        }

        return player.VerticalSpeed;
    }

    private bool GetPlayerRenderIsGrounded(PlayerEntity player)
    {
        return _networkClient.IsConnected
            && ReferenceEquals(player, _world.LocalPlayer)
            && _hasPredictedLocalPlayerPosition
                ? _predictedLocalPlayerGrounded
                : player.IsGrounded;
    }
}
