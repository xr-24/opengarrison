#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private readonly List<PredictedLocalInput> _pendingPredictedInputs = new();
    private Vector2 _predictedLocalPlayerPosition;
    private Vector2 _smoothedLocalPlayerRenderPosition;
    private Vector2 _predictedLocalPlayerVelocity;
    private bool _hasPredictedLocalPlayerPosition;
    private bool _hasSmoothedLocalPlayerRenderPosition;
    private bool _predictedLocalPlayerGrounded;
    private int _predictedLocalPlayerRemainingAirJumps;
    private PredictedLocalActionState _predictedLocalActionState;
    private bool _hasPredictedLocalActionState;
    private PlayerInputSnapshot _latestPredictedLocalInput;

    private void RecordPredictedInput(uint sequence, PlayerInputSnapshot input, bool jumpPressed)
    {
        var secondaryPressed = input.FireSecondary && !_latestPredictedLocalInput.FireSecondary;
        _latestPredictedLocalInput = input;

        if (!_networkClient.IsConnected || sequence == 0 || !_world.LocalPlayer.IsAlive || _world.LocalPlayerAwaitingJoin)
        {
            return;
        }

        _pendingPredictedInputs.Add(new PredictedLocalInput(sequence, input, jumpPressed, secondaryPressed));
        RebuildLocalPrediction();
    }

    private void ReconcileLocalPrediction(uint lastProcessedInputSequence)
    {
        if (!_networkClient.IsConnected || !_world.LocalPlayer.IsAlive || _world.LocalPlayerAwaitingJoin)
        {
            _hasPredictedLocalPlayerPosition = false;
            _hasSmoothedLocalPlayerRenderPosition = false;
            _hasPredictedLocalActionState = false;
            _pendingPredictedInputs.Clear();
            return;
        }

        _pendingPredictedInputs.RemoveAll(input => input.Sequence <= lastProcessedInputSequence);
        RebuildLocalPrediction();
    }

    private void RebuildLocalPrediction()
    {
        if (!_networkClient.IsConnected || !_world.LocalPlayer.IsAlive || _world.LocalPlayerAwaitingJoin)
        {
            _hasPredictedLocalPlayerPosition = false;
            _hasSmoothedLocalPlayerRenderPosition = false;
            _hasPredictedLocalActionState = false;
            return;
        }

        var player = _world.LocalPlayer;
        _predictedLocalPlayerPosition = new Vector2(player.X, player.Y);
        _predictedLocalPlayerVelocity = new Vector2(player.HorizontalSpeed, player.VerticalSpeed);
        _predictedLocalPlayerGrounded = player.IsGrounded;
        _predictedLocalPlayerRemainingAirJumps = player.RemainingAirJumps;
        _hasPredictedLocalPlayerPosition = true;
        ResetPredictedActionState(player);

        for (var index = 0; index < _pendingPredictedInputs.Count; index += 1)
        {
            ApplyPredictedInputStep(_pendingPredictedInputs[index]);
        }

        if (!_hasSmoothedLocalPlayerRenderPosition)
        {
            _smoothedLocalPlayerRenderPosition = _predictedLocalPlayerPosition;
            _hasSmoothedLocalPlayerRenderPosition = true;
        }
    }

    private void ResetPredictedActionState(PlayerEntity player)
    {
        _predictedLocalActionState = new PredictedLocalActionState
        {
            IsHeavyEating = player.IsHeavyEating,
            HeavyEatTicksRemaining = player.HeavyEatTicksRemaining,
            IsSniperScoped = player.IsSniperScoped,
            SniperChargeTicks = player.SniperChargeTicks,
            IsSpyCloaked = player.IsSpyCloaked,
            IsSpyVisibleToEnemies = player.IsSpyVisibleToEnemies,
            SpyBackstabWindupTicksRemaining = player.SpyBackstabWindupTicksRemaining,
            SpyBackstabRecoveryTicksRemaining = player.SpyBackstabRecoveryTicksRemaining,
            MedicUberCharge = player.MedicUberCharge,
            Metal = player.Metal,
            IsMedicUberReady = player.IsMedicUberReady,
            IsMedicUbering = player.IsMedicUbering,
            MedicNeedleCooldownTicks = player.MedicNeedleCooldownTicks,
            MedicNeedleRefillTicks = player.MedicNeedleRefillTicks,
            CurrentShells = player.CurrentShells,
            PrimaryCooldownTicks = player.PrimaryCooldownTicks,
            ReloadTicksUntilNextShell = player.ReloadTicksUntilNextShell,
        };
        _hasPredictedLocalActionState = true;
    }

    private void ApplyPredictedInputStep(PredictedLocalInput predictedInput)
    {
        var player = _world.LocalPlayer;
        AdvancePredictedActionState(player);
        ApplyPredictedMovementStep(predictedInput);
        ApplyPredictedPrimaryFire(player, predictedInput);
        ApplyPredictedSecondaryFire(player, predictedInput);
    }

    private struct PredictedLocalActionState
    {
        public bool IsHeavyEating;
        public int HeavyEatTicksRemaining;
        public bool IsSniperScoped;
        public int SniperChargeTicks;
        public bool IsSpyCloaked;
        public bool IsSpyVisibleToEnemies;
        public int SpyBackstabWindupTicksRemaining;
        public int SpyBackstabRecoveryTicksRemaining;
        public float MedicUberCharge;
        public float Metal;
        public bool IsMedicUberReady;
        public bool IsMedicUbering;
        public int MedicNeedleCooldownTicks;
        public int MedicNeedleRefillTicks;
        public int CurrentShells;
        public int PrimaryCooldownTicks;
        public int ReloadTicksUntilNextShell;
    }

    private readonly record struct PredictedLocalInput(
        uint Sequence,
        PlayerInputSnapshot Input,
        bool JumpPressed,
        bool SecondaryPressed);
}
