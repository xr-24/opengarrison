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
        if (!player.IsAlive)
        {
            _playerAnimationImages.Remove(player.Id);
            _playerWeaponFlashTicks.Remove(player.Id);
            _playerPreviousAmmoCounts.Remove(player.Id);
            _playerPreviousCooldownTicks.Remove(player.Id);
            return;
        }

        var horizontalStepSpeed = GetPlayerAnimationStepSpeed(player.HorizontalSpeed);
        var verticalStepSpeed = GetPlayerAnimationStepSpeed(player.VerticalSpeed);
        var isRemoteNetworkPlayer = _networkClient.IsConnected && !ReferenceEquals(player, _world.LocalPlayer);
        var animationImage = _playerAnimationImages.GetValueOrDefault(player.Id, 0f);
        if (horizontalStepSpeed < 0.2f)
        {
            animationImage = 0f;
        }

        var appearsAirborne = !player.IsGrounded;
        if (isRemoteNetworkPlayer && !player.IsGrounded)
        {
            appearsAirborne = verticalStepSpeed > 0.35f;
        }

        if (appearsAirborne)
        {
            animationImage = 1f;
        }

        animationImage = (animationImage + horizontalStepSpeed / 20f) % 2f;
        _playerAnimationImages[player.Id] = animationImage;

        var flashTicks = _playerWeaponFlashTicks.GetValueOrDefault(player.Id, 0);
        if (flashTicks > 0)
        {
            flashTicks -= 1;
        }

        var previousAmmo = _playerPreviousAmmoCounts.GetValueOrDefault(player.Id, player.CurrentShells);
        var previousCooldown = _playerPreviousCooldownTicks.GetValueOrDefault(player.Id, player.PrimaryCooldownTicks);
        if (player.PrimaryCooldownTicks > 0 && (player.CurrentShells < previousAmmo || previousCooldown <= 0))
        {
            flashTicks = 1;
        }

        _playerWeaponFlashTicks[player.Id] = flashTicks;
        _playerPreviousAmmoCounts[player.Id] = player.CurrentShells;
        _playerPreviousCooldownTicks[player.Id] = player.PrimaryCooldownTicks;
    }

    private void RemoveStalePlayerRenderState()
    {
        var activePlayerIds = new HashSet<int>();
        if (_world.LocalPlayer.IsAlive)
        {
            activePlayerIds.Add(_world.LocalPlayer.Id);
        }

        foreach (var player in EnumerateRemotePlayersForView())
        {
            if (player.IsAlive)
            {
                activePlayerIds.Add(player.Id);
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
        }
    }

    private float GetPlayerAnimationStepSpeed(float speedPerSecond)
    {
        return MathF.Abs(speedPerSecond) * (float)_config.FixedDeltaSeconds;
    }
}
