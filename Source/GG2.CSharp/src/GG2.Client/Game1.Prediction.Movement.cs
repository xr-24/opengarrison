#nullable enable

using Microsoft.Xna.Framework;
using System;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private const int MaxPredictedCollisionResolutionIterations = 10;
    private const float PredictedCollisionMoveStep = 1f;
    private const float PredictedStepUpHeight = 6f;

    private float GetPredictedMovementScale(PlayerEntity player, PlayerInputSnapshot input)
    {
        if (_predictedLocalActionState.IsHeavyEating || player.IsTaunting)
        {
            return 0f;
        }

        if (player.ClassId == PlayerClass.Spy && IsPredictedSpyBackstabAnimating())
        {
            return 0f;
        }

        if (player.ClassId == PlayerClass.Sniper && _predictedLocalActionState.IsSniperScoped)
        {
            return PlayerEntity.SniperScopedMoveScale;
        }

        if (player.ClassId == PlayerClass.Heavy && input.FirePrimary)
        {
            return PlayerEntity.HeavyPrimaryMoveScale;
        }

        return 1f;
    }

    private float GetPredictedJumpScale(PlayerEntity player)
    {
        if (player.ClassId == PlayerClass.Sniper && _predictedLocalActionState.IsSniperScoped)
        {
            return PlayerEntity.SniperScopedJumpScale;
        }

        if (player.ClassId == PlayerClass.Spy && IsPredictedSpyBackstabAnimating())
        {
            return 0f;
        }

        return 1f;
    }

    private void UpdateLocalPredictedRenderPosition(float deltaSeconds)
    {
        if (!_networkClient.IsConnected || !_hasPredictedLocalPlayerPosition || !_world.LocalPlayer.IsAlive || _world.LocalPlayerAwaitingJoin)
        {
            _hasSmoothedLocalPlayerRenderPosition = false;
            return;
        }

        if (!_hasSmoothedLocalPlayerRenderPosition)
        {
            _smoothedLocalPlayerRenderPosition = _predictedLocalPlayerPosition;
            _hasSmoothedLocalPlayerRenderPosition = true;
            return;
        }

        var delta = _predictedLocalPlayerPosition - _smoothedLocalPlayerRenderPosition;
        var distance = delta.Length();
        if (distance <= 0.01f)
        {
            _smoothedLocalPlayerRenderPosition = _predictedLocalPlayerPosition;
            return;
        }

        if (distance >= 24f)
        {
            _smoothedLocalPlayerRenderPosition = _predictedLocalPlayerPosition;
            return;
        }

        var isActivelyMoving = _latestPredictedLocalInput.Left
            || _latestPredictedLocalInput.Right
            || _latestPredictedLocalInput.Up
            || MathF.Abs(_predictedLocalPlayerVelocity.X) > 20f
            || MathF.Abs(_predictedLocalPlayerVelocity.Y) > 20f;
        var catchUpRate = isActivelyMoving ? 32f : 18f;
        if (distance >= 8f)
        {
            catchUpRate = MathF.Max(catchUpRate, 42f);
        }

        var followFactor = 1f - MathF.Exp(-catchUpRate * deltaSeconds);
        _smoothedLocalPlayerRenderPosition = Vector2.Lerp(
            _smoothedLocalPlayerRenderPosition,
            _predictedLocalPlayerPosition,
            followFactor);
    }

    private void ApplyPredictedMovementStep(PredictedLocalInput predictedInput)
    {
        var player = _world.LocalPlayer;
        var dt = (float)_config.FixedDeltaSeconds;
        var moveScale = GetPredictedMovementScale(player, predictedInput.Input);
        var maxRunSpeed = player.MaxRunSpeed * moveScale;
        var groundAcceleration = player.GroundAcceleration * moveScale;
        var groundDeceleration = player.GroundDeceleration * moveScale;
        var canMove = !_predictedLocalActionState.IsHeavyEating && !player.IsTaunting;

        var horizontalDirection = 0f;
        if (canMove && predictedInput.Input.Left)
        {
            horizontalDirection -= 1f;
        }

        if (canMove && predictedInput.Input.Right)
        {
            horizontalDirection += 1f;
        }

        if (horizontalDirection != 0f)
        {
            _predictedLocalPlayerVelocity.X += horizontalDirection * groundAcceleration * dt;
            _predictedLocalPlayerVelocity.X = float.Clamp(_predictedLocalPlayerVelocity.X, -maxRunSpeed, maxRunSpeed);
        }
        else
        {
            var deceleration = groundDeceleration * dt;
            if (_predictedLocalPlayerVelocity.X > 0f)
            {
                _predictedLocalPlayerVelocity.X = float.Max(0f, _predictedLocalPlayerVelocity.X - deceleration);
            }
            else if (_predictedLocalPlayerVelocity.X < 0f)
            {
                _predictedLocalPlayerVelocity.X = float.Min(0f, _predictedLocalPlayerVelocity.X + deceleration);
            }
        }

        if (canMove && predictedInput.JumpPressed)
        {
            TryPredictedJump(player);
        }

        _predictedLocalPlayerVelocity.Y += player.Gravity * dt;
        MovePredictedWithCollisions(player, _predictedLocalPlayerVelocity.X * dt, _predictedLocalPlayerVelocity.Y * dt);

        _predictedLocalPlayerPosition = new Vector2(
            _world.Bounds.ClampX(_predictedLocalPlayerPosition.X, player.Width),
            _world.Bounds.ClampY(_predictedLocalPlayerPosition.Y, player.Height));
    }

    private void TryPredictedJump(PlayerEntity player)
    {
        var jumpScale = GetPredictedJumpScale(player);
        if (jumpScale <= 0f)
        {
            return;
        }

        if (_predictedLocalPlayerGrounded)
        {
            _predictedLocalPlayerVelocity.Y = -player.JumpSpeed * jumpScale;
            _predictedLocalPlayerGrounded = false;
            return;
        }

        if (_predictedLocalPlayerRemainingAirJumps <= 0)
        {
            return;
        }

        _predictedLocalPlayerVelocity.Y = -player.JumpSpeed * jumpScale;
        _predictedLocalPlayerRemainingAirJumps -= 1;
    }

    private void MovePredictedWithCollisions(PlayerEntity player, float moveX, float moveY)
    {
        NudgePredictedOutsideBlockingGeometry(player);
        var remainingX = moveX;
        var remainingY = moveY;
        _predictedLocalPlayerGrounded = false;

        for (var iteration = 0; iteration < MaxPredictedCollisionResolutionIterations && (MathF.Abs(remainingX) >= 1f || MathF.Abs(remainingY) >= 1f); iteration += 1)
        {
            var previousPosition = _predictedLocalPlayerPosition;
            MovePredictedContact(player, remainingX, remainingY);
            remainingX -= _predictedLocalPlayerPosition.X - previousPosition.X;
            remainingY -= _predictedLocalPlayerPosition.Y - previousPosition.Y;

            var collisionRectified = false;
            if (remainingY != 0f && !CanOccupyPredicted(player, _predictedLocalPlayerPosition.X, _predictedLocalPlayerPosition.Y + MathF.Sign(remainingY)))
            {
                if (remainingY > 0f)
                {
                    _predictedLocalPlayerGrounded = true;
                    _predictedLocalPlayerRemainingAirJumps = player.MaxAirJumps;
                }

                _predictedLocalPlayerVelocity.Y = 0f;
                remainingY = 0f;
                collisionRectified = true;
            }

            if (remainingX != 0f && !CanOccupyPredicted(player, _predictedLocalPlayerPosition.X + MathF.Sign(remainingX), _predictedLocalPlayerPosition.Y))
            {
                if (TryStepUpPredicted(player, MathF.Sign(remainingX)))
                {
                    collisionRectified = true;
                }
                else
                {
                    _predictedLocalPlayerVelocity.X = 0f;
                    remainingX = 0f;
                    collisionRectified = true;
                }
            }

            if (!collisionRectified && (MathF.Abs(remainingX) >= 1f || MathF.Abs(remainingY) >= 1f))
            {
                _predictedLocalPlayerVelocity.Y = 0f;
                remainingY = 0f;
            }
        }

        if (MathF.Abs(remainingX) <= 0f && MathF.Abs(remainingY) <= 0f)
        {
            return;
        }

        if (CanOccupyPredicted(player, _predictedLocalPlayerPosition.X + remainingX, _predictedLocalPlayerPosition.Y + remainingY))
        {
            _predictedLocalPlayerPosition.X += remainingX;
            _predictedLocalPlayerPosition.Y += remainingY;
            return;
        }

        if (MathF.Abs(remainingX) > 0f && MathF.Abs(remainingY) <= 0f && CanOccupyPredicted(player, _predictedLocalPlayerPosition.X + remainingX, _predictedLocalPlayerPosition.Y))
        {
            _predictedLocalPlayerPosition.X += remainingX;
            return;
        }

        if (MathF.Abs(remainingY) > 0f && MathF.Abs(remainingX) <= 0f && CanOccupyPredicted(player, _predictedLocalPlayerPosition.X, _predictedLocalPlayerPosition.Y + remainingY))
        {
            _predictedLocalPlayerPosition.Y += remainingY;
        }
    }

    private void MovePredictedContact(PlayerEntity player, float deltaX, float deltaY)
    {
        var distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance <= 0f)
        {
            return;
        }

        var steps = Math.Max(1, (int)MathF.Ceiling(distance / PredictedCollisionMoveStep));
        var stepX = deltaX / steps;
        var stepY = deltaY / steps;
        for (var step = 0; step < steps; step += 1)
        {
            var nextX = _predictedLocalPlayerPosition.X + stepX;
            var nextY = _predictedLocalPlayerPosition.Y + stepY;
            if (!CanOccupyPredicted(player, nextX, nextY))
            {
                break;
            }

            _predictedLocalPlayerPosition = new Vector2(nextX, nextY);
        }
    }

    private void NudgePredictedOutsideBlockingGeometry(PlayerEntity player)
    {
        if (CanOccupyPredicted(player, _predictedLocalPlayerPosition.X, _predictedLocalPlayerPosition.Y))
        {
            return;
        }

        for (var offset = 1; offset <= 8; offset += 1)
        {
            if (CanOccupyPredicted(player, _predictedLocalPlayerPosition.X + offset, _predictedLocalPlayerPosition.Y))
            {
                _predictedLocalPlayerPosition.X += offset;
                return;
            }
        }

        for (var offset = 1; offset <= 16; offset += 1)
        {
            if (CanOccupyPredicted(player, _predictedLocalPlayerPosition.X - offset, _predictedLocalPlayerPosition.Y))
            {
                _predictedLocalPlayerPosition.X -= offset;
                return;
            }
        }
    }

    private bool CanOccupyPredicted(PlayerEntity player, float x, float y)
    {
        var left = x - (player.Width / 2f);
        var right = x + (player.Width / 2f);
        var top = y - (player.Height / 2f);
        var bottom = y + (player.Height / 2f);

        foreach (var solid in _world.Level.Solids)
        {
            if (left < solid.Right && right > solid.Left && top < solid.Bottom && bottom > solid.Top)
            {
                return false;
            }
        }

        foreach (var gate in _world.Level.GetBlockingTeamGates(player.Team, player.IsCarryingIntel))
        {
            if (left < gate.Right && right > gate.Left && top < gate.Bottom && bottom > gate.Top)
            {
                return false;
            }
        }

        foreach (var wall in _world.Level.GetRoomObjects(RoomObjectType.PlayerWall))
        {
            if (left < wall.Right && right > wall.Left && top < wall.Bottom && bottom > wall.Top)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryStepUpPredicted(PlayerEntity player, float horizontalDirection)
    {
        if (horizontalDirection == 0f || _predictedLocalPlayerVelocity.X == 0f)
        {
            return false;
        }

        var obstacleTop = FindPredictedBlockingObstacleTop(player, _predictedLocalPlayerPosition.X + horizontalDirection, _predictedLocalPlayerPosition.Y);
        if (!obstacleTop.HasValue)
        {
            return false;
        }

        var bottom = _predictedLocalPlayerPosition.Y + (player.Height / 2f);
        var stepDelta = bottom - obstacleTop.Value;
        if (stepDelta < 0f || stepDelta > PredictedStepUpHeight)
        {
            return false;
        }

        var targetY = _predictedLocalPlayerPosition.Y - stepDelta;
        if (!CanOccupyPredicted(player, _predictedLocalPlayerPosition.X, targetY))
        {
            return false;
        }

        _predictedLocalPlayerPosition.Y = targetY;
        return true;
    }

    private float? FindPredictedBlockingObstacleTop(PlayerEntity player, float x, float y)
    {
        var left = x - (player.Width / 2f);
        var right = x + (player.Width / 2f);
        var top = y - (player.Height / 2f);
        var bottom = y + (player.Height / 2f);
        float? obstacleTop = null;

        foreach (var solid in _world.Level.Solids)
        {
            if (left < solid.Right && right > solid.Left && top < solid.Bottom && bottom > solid.Top)
            {
                obstacleTop = obstacleTop.HasValue ? MathF.Min(obstacleTop.Value, solid.Top) : solid.Top;
            }
        }

        foreach (var wall in _world.Level.GetRoomObjects(RoomObjectType.PlayerWall))
        {
            if (left < wall.Right && right > wall.Left && top < wall.Bottom && bottom > wall.Top)
            {
                obstacleTop = obstacleTop.HasValue ? MathF.Min(obstacleTop.Value, wall.Top) : wall.Top;
            }
        }

        return obstacleTop;
    }
}
