namespace GG2.Core;

public sealed partial class PlayerEntity
{

    public bool TryStartTaunt()
    {
        if (!IsAlive || IsTaunting || IsHeavyEating || IsSpyCloaked || IsSpyBackstabAnimating)
        {
            return false;
        }

        IsTaunting = true;
        TauntFrameIndex = Team == PlayerTeam.Blue
            ? GetTauntLengthFrames(ClassId) + 1f
            : 0f;
        return true;
    }

    public bool TryStartHeavySelfHeal()
    {
        if (!IsAlive || ClassId != PlayerClass.Heavy || IsHeavyEating || IsTaunting)
        {
            return false;
        }

        IsHeavyEating = true;
        HeavyEatTicksRemaining = HeavyEatDurationTicks;
        HeavyHealingAccumulator = 0f;
        return true;
    }

    private void AdvanceTauntState()
    {
        if (!IsTaunting)
        {
            return;
        }

        TauntFrameIndex += TauntFrameStepPerTick;
        var tauntEndFrame = Team == PlayerTeam.Blue
            ? (GetTauntLengthFrames(ClassId) * 2f) + 1f
            : GetTauntLengthFrames(ClassId);
        if (TauntFrameIndex < tauntEndFrame)
        {
            return;
        }

        IsTaunting = false;
    }

    private void AdvanceHeavyState()
    {
        if (!IsHeavyEating)
        {
            return;
        }

        HeavyEatTicksRemaining -= 1;
        HeavyHealingAccumulator += HeavyEatHealPerTick;
        var wholeHealing = (int)HeavyHealingAccumulator;
        if (wholeHealing > 0)
        {
            HeavyHealingAccumulator -= wholeHealing;
            Health = int.Clamp(Health + wholeHealing, 0, MaxHealth);
        }

        if (HeavyEatTicksRemaining > 0)
        {
            return;
        }

        IsHeavyEating = false;
        HeavyEatTicksRemaining = 0;
        HeavyHealingAccumulator = 0f;
        IsTaunting = false;
        TauntFrameIndex = 0f;
    }

    public bool TryToggleSniperScope()
    {
        if (!IsAlive || ClassId != PlayerClass.Sniper || IsTaunting)
        {
            return false;
        }

        IsSniperScoped = !IsSniperScoped;
        if (!IsSniperScoped)
        {
            SniperChargeTicks = 0;
        }

        return true;
    }

    public bool TryToggleSpyCloak()
    {
        if (!IsAlive || ClassId != PlayerClass.Spy || IsCarryingIntel || IsSpyBackstabAnimating || IsTaunting)
        {
            return false;
        }

        if (IsSpyCloaked)
        {
            if (SpyCloakAlpha > SpyCloakToggleThreshold + 0.0001f)
            {
                return false;
            }
        }
        else if (SpyCloakAlpha < 0.9999f)
        {
            return false;
        }

        IsSpyCloaked = !IsSpyCloaked;
        if (!IsSpyCloaked)
        {
            IsSpyVisibleToEnemies = false;
        }

        return true;
    }

    public bool TryStartSpyBackstab(float directionDegrees)
    {
        if (!IsAlive || ClassId != PlayerClass.Spy || !IsSpyCloaked || !IsSpyBackstabReady || IsTaunting)
        {
            return false;
        }

        SpyBackstabDirectionDegrees = directionDegrees;
        SpyBackstabWindupTicksRemaining = SpyBackstabWindupTicksDefault;
        SpyBackstabRecoveryTicksRemaining = 0;
        IsSpyVisibleToEnemies = true;
        return true;
    }

    public bool TryConsumeSpyBackstabHitboxTrigger(out float directionDegrees)
    {
        if (SpyBackstabWindupTicksRemaining != 0 || SpyBackstabRecoveryTicksRemaining != SpyBackstabRecoveryTicksDefault)
        {
            directionDegrees = 0f;
            return false;
        }

        directionDegrees = SpyBackstabDirectionDegrees;
        SpyBackstabRecoveryTicksRemaining -= 1;
        return true;
    }

    public void SetMedicHealingTarget(PlayerEntity? target)
    {
        MedicHealTargetId = target?.Id;
        IsMedicHealing = target is not null;
    }

    public void ClearMedicHealingTarget()
    {
        MedicHealTargetId = null;
        IsMedicHealing = false;
    }

    public bool TryStartMedicUber()
    {
        if (!IsAlive || ClassId != PlayerClass.Medic || !IsMedicUberReady)
        {
            return false;
        }

        IsMedicUbering = true;
        IsMedicUberReady = false;
        return true;
    }

    public void AddMedicUberCharge(float amount)
    {
        if (ClassId != PlayerClass.Medic || IsMedicUbering || amount <= 0f)
        {
            return;
        }

        MedicUberCharge = float.Min(MedicUberMaxCharge, MedicUberCharge + amount);
        if (MedicUberCharge >= MedicUberMaxCharge)
        {
            MedicUberCharge = MedicUberMaxCharge;
            IsMedicUberReady = true;
        }
    }

    public void FillMedicUberCharge()
    {
        if (ClassId != PlayerClass.Medic)
        {
            return;
        }

        MedicUberCharge = MedicUberMaxCharge;
        IsMedicUberReady = true;
    }

    public bool TryFireMedicNeedle()
    {
        if (!IsAlive
            || ClassId != PlayerClass.Medic
            || IsTaunting
            || IsMedicHealing
            || IsMedicUbering
            || MedicNeedleCooldownTicks > 0
            || CurrentShells <= 0)
        {
            return false;
        }

        CurrentShells -= 1;
        MedicNeedleCooldownTicks = MedicNeedleFireCooldownTicks;
        MedicNeedleRefillTicks = 0;
        return true;
    }

    public void RefreshUber(int ticks = DefaultUberRefreshTicks)
    {
        if (!IsAlive)
        {
            return;
        }

        UberTicksRemaining = int.Max(UberTicksRemaining, ticks);
    }

    public bool ApplyContinuousHealing(float healing)
    {
        if (!IsAlive || healing <= 0f)
        {
            return false;
        }

        ContinuousHealingAccumulator += healing;
        var wholeHealing = (int)ContinuousHealingAccumulator;
        if (wholeHealing <= 0)
        {
            return false;
        }

        ContinuousHealingAccumulator -= wholeHealing;
        var previousHealth = Health;
        Health = int.Min(MaxHealth, Health + wholeHealing);
        return Health > previousHealth;
    }

    private void AdvanceSniperState()
    {
        if (ClassId != PlayerClass.Sniper || !IsSniperScoped || PrimaryCooldownTicks > 0)
        {
            SniperChargeTicks = 0;
            return;
        }

        if (SniperChargeTicks < SniperChargeMaxTicks)
        {
            SniperChargeTicks += 1;
        }
    }

    private void AdvanceSpyState()
    {
        if (ClassId != PlayerClass.Spy)
        {
            SpyCloakAlpha = 1f;
            IsSpyVisibleToEnemies = false;
            SpyBackstabWindupTicksRemaining = 0;
            SpyBackstabRecoveryTicksRemaining = 0;
            return;
        }

        SpyCloakAlpha = IsSpyCloaked
            ? float.Max(0f, SpyCloakAlpha - SpyCloakFadePerTick)
            : float.Min(1f, SpyCloakAlpha + SpyCloakFadePerTick);

        if (SpyBackstabWindupTicksRemaining > 0)
        {
            SpyBackstabWindupTicksRemaining -= 1;
            if (SpyBackstabWindupTicksRemaining == 0)
            {
                SpyBackstabRecoveryTicksRemaining = SpyBackstabRecoveryTicksDefault;
            }

            return;
        }

        if (SpyBackstabRecoveryTicksRemaining > 0)
        {
            SpyBackstabRecoveryTicksRemaining -= 1;
        }

        IsSpyVisibleToEnemies = IsSpyCloaked
            && (SpyCloakAlpha > 0f || SpyBackstabWindupTicksRemaining > 0 || SpyBackstabRecoveryTicksRemaining > 0);
    }

    private void AdvancePyroAirblastState()
    {
        if (ClassId != PlayerClass.Pyro)
        {
            PyroAirblastCooldownTicks = 0;
            return;
        }

        if (PyroAirblastCooldownTicks > 0)
        {
            PyroAirblastCooldownTicks -= 1;
        }
    }

    private void AdvanceUberState()
    {
        if (UberTicksRemaining > 0)
        {
            UberTicksRemaining -= 1;
        }
    }

    private void AdvanceMedicState(PlayerInputSnapshot input)
    {
        if (ClassId != PlayerClass.Medic)
        {
            return;
        }

        if (IsMedicUbering)
        {
            MedicUberCharge = float.Max(0f, MedicUberCharge - 10f);
            if (MedicUberCharge <= 0f)
            {
                MedicUberCharge = 0f;
                IsMedicUbering = false;
            }
        }

        if (MedicNeedleCooldownTicks > 0)
        {
            MedicNeedleCooldownTicks -= 1;
        }

        if (CurrentShells >= MaxShells)
        {
            MedicNeedleRefillTicks = 0;
            return;
        }

        if (MedicNeedleCooldownTicks > 0)
        {
            MedicNeedleRefillTicks = 0;
            return;
        }

        if (MedicNeedleRefillTicks <= 0)
        {
            MedicNeedleRefillTicks = MedicNeedleRefillTicksDefault;
            return;
        }

        MedicNeedleRefillTicks -= 1;
        if (MedicNeedleRefillTicks <= 0)
        {
            CurrentShells = int.Min(MaxShells, CurrentShells + 1);
            MedicNeedleRefillTicks = CurrentShells < MaxShells ? MedicNeedleRefillTicksDefault : 0;
        }
    }

    private static int GetTauntLengthFrames(PlayerClass classId)
    {
        return classId switch
        {
            PlayerClass.Scout => 8,
            PlayerClass.Engineer => 12,
            PlayerClass.Pyro => 9,
            PlayerClass.Soldier => 15,
            PlayerClass.Demoman => 10,
            PlayerClass.Heavy => 11,
            PlayerClass.Sniper => 12,
            PlayerClass.Medic => 10,
            PlayerClass.Spy => 10,
            PlayerClass.Quote => 16,
            _ => 0,
        };
    }
}
