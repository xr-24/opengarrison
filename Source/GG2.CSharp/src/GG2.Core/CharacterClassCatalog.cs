namespace GG2.Core;

public static class CharacterClassCatalog
{
    public static PrimaryWeaponDefinition Scattergun { get; } = new(
        DisplayName: "Scattergun",
        Kind: PrimaryWeaponKind.PelletGun,
        MaxAmmo: 6,
        AmmoPerShot: 1,
        ProjectilesPerShot: 6,
        ReloadDelayTicks: 20,
        AmmoReloadTicks: 15,
        SpreadDegrees: 7f,
        MinShotSpeed: 11f,
        AdditionalRandomShotSpeed: 4f);

    public static PrimaryWeaponDefinition Shotgun { get; } = new(
        DisplayName: "Shotgun",
        Kind: PrimaryWeaponKind.PelletGun,
        MaxAmmo: 8,
        AmmoPerShot: 1,
        ProjectilesPerShot: 4,
        ReloadDelayTicks: 20,
        AmmoReloadTicks: 15,
        SpreadDegrees: 5f,
        MinShotSpeed: 11f,
        AdditionalRandomShotSpeed: 4f);

    public static PrimaryWeaponDefinition Flamethrower { get; } = new(
        DisplayName: "Flamethrower",
        Kind: PrimaryWeaponKind.FlameThrower,
        MaxAmmo: 200,
        AmmoPerShot: 2,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 1,
        AmmoReloadTicks: 0,
        SpreadDegrees: 5f,
        MinShotSpeed: 5f,
        AdditionalRandomShotSpeed: 5f,
        AutoReloads: false,
        AmmoRegenPerTick: 1);

    public static PrimaryWeaponDefinition RocketLauncher { get; } = new(
        DisplayName: "Rocket Launcher",
        Kind: PrimaryWeaponKind.RocketLauncher,
        MaxAmmo: 4,
        AmmoPerShot: 1,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 30,
        AmmoReloadTicks: 25,
        SpreadDegrees: 0f,
        MinShotSpeed: 13f,
        AdditionalRandomShotSpeed: 0f);

    public static PrimaryWeaponDefinition MineLauncher { get; } = new(
        DisplayName: "Mine Launcher",
        Kind: PrimaryWeaponKind.MineLauncher,
        MaxAmmo: 8,
        AmmoPerShot: 1,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 26,
        AmmoReloadTicks: 20,
        SpreadDegrees: 0f,
        MinShotSpeed: 12f,
        AdditionalRandomShotSpeed: 0f);

    public static PrimaryWeaponDefinition Minigun { get; } = new(
        DisplayName: "Minigun",
        Kind: PrimaryWeaponKind.Minigun,
        MaxAmmo: 200,
        AmmoPerShot: 4,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 2,
        AmmoReloadTicks: 0,
        SpreadDegrees: 7f,
        MinShotSpeed: 12f,
        AdditionalRandomShotSpeed: 1f,
        AutoReloads: false,
        AmmoRegenPerTick: 1);

    public static PrimaryWeaponDefinition Rifle { get; } = new(
        DisplayName: "Rifle",
        Kind: PrimaryWeaponKind.Rifle,
        MaxAmmo: 1,
        AmmoPerShot: 0,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 40,
        AmmoReloadTicks: 0,
        SpreadDegrees: 0f,
        MinShotSpeed: 0f,
        AdditionalRandomShotSpeed: 0f,
        AutoReloads: false);

    public static PrimaryWeaponDefinition Medigun { get; } = new(
        DisplayName: "Medigun",
        Kind: PrimaryWeaponKind.Medigun,
        MaxAmmo: 40,
        AmmoPerShot: 0,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 3,
        AmmoReloadTicks: 0,
        SpreadDegrees: 0f,
        MinShotSpeed: 0f,
        AdditionalRandomShotSpeed: 0f,
        AutoReloads: false);

    public static PrimaryWeaponDefinition Revolver { get; } = new(
        DisplayName: "Revolver",
        Kind: PrimaryWeaponKind.Revolver,
        MaxAmmo: 6,
        AmmoPerShot: 1,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 18,
        AmmoReloadTicks: 55,
        SpreadDegrees: 1f,
        MinShotSpeed: 20f,
        AdditionalRandomShotSpeed: 0f,
        RefillsAllAtOnce: true);

    public static PrimaryWeaponDefinition Blade { get; } = new(
        DisplayName: "Blade",
        Kind: PrimaryWeaponKind.Blade,
        MaxAmmo: 100,
        AmmoPerShot: 0,
        ProjectilesPerShot: 1,
        ReloadDelayTicks: 5,
        AmmoReloadTicks: 0,
        SpreadDegrees: 4f,
        MinShotSpeed: 10f,
        AdditionalRandomShotSpeed: 2f,
        AutoReloads: false,
        AmmoRegenPerTick: 1);

    // These values currently mirror the first translated Scout-style movement slice.
    public static CharacterClassDefinition Scout { get; } = new(
        Id: PlayerClass.Scout,
        DisplayName: "Scout",
        PrimaryWeapon: Scattergun,
        MaxHealth: 100,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 252f,
        GroundAcceleration: 765f,
        GroundDeceleration: 1100f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 1);

    // Engineer values are grounded in the original GG2 create event and pygg2's max-speed sanity check.
    public static CharacterClassDefinition Engineer { get; } = new(
        Id: PlayerClass.Engineer,
        DisplayName: "Engineer",
        PrimaryWeapon: Shotgun,
        MaxHealth: 120,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 180f,
        GroundAcceleration: 546f,
        GroundDeceleration: 786f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    // Pyro values come from the original GG2 create event with pygg2 used only as a max-speed sanity check.
    public static CharacterClassDefinition Pyro { get; } = new(
        Id: PlayerClass.Pyro,
        DisplayName: "Pyro",
        PrimaryWeapon: Flamethrower,
        MaxHealth: 120,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 198f,
        GroundAcceleration: 600f,
        GroundDeceleration: 860f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    public static CharacterClassDefinition Soldier { get; } = new(
        Id: PlayerClass.Soldier,
        DisplayName: "Soldier",
        PrimaryWeapon: RocketLauncher,
        MaxHealth: 175,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 162f,
        GroundAcceleration: 492f,
        GroundDeceleration: 708f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    public static CharacterClassDefinition Demoman { get; } = new(
        Id: PlayerClass.Demoman,
        DisplayName: "Demoman",
        PrimaryWeapon: MineLauncher,
        MaxHealth: 120,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 180f,
        GroundAcceleration: 546f,
        GroundDeceleration: 786f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    // Heavy values come directly from the original GG2 create event:
    // runPower=0.8, jumpStrength=8, maxHp=200, weapons[0]=Minigun.
    public static CharacterClassDefinition Heavy { get; } = new(
        Id: PlayerClass.Heavy,
        DisplayName: "Heavy",
        PrimaryWeapon: Minigun,
        MaxHealth: 200,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 144f,
        GroundAcceleration: 436.8f,
        GroundDeceleration: 628.8f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    // Sniper values come directly from the original GG2 create event:
    // runPower=0.9, jumpStrength=8, maxHp=120, weapons[0]=Rifle.
    public static CharacterClassDefinition Sniper { get; } = new(
        Id: PlayerClass.Sniper,
        DisplayName: "Sniper",
        PrimaryWeapon: Rifle,
        MaxHealth: 120,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 162f,
        GroundAcceleration: 492f,
        GroundDeceleration: 708f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    // Medic values come directly from the original GG2 create event:
    // runPower=1.09, jumpStrength=8, maxHp=120, weapons[0]=Medigun.
    public static CharacterClassDefinition Medic { get; } = new(
        Id: PlayerClass.Medic,
        DisplayName: "Medic",
        PrimaryWeapon: Medigun,
        MaxHealth: 120,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 196.2f,
        GroundAcceleration: 595.14f,
        GroundDeceleration: 856.74f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    // Spy values come directly from the original GG2 create event:
    // runPower=1.08, jumpStrength=8, maxHp=100, weapons[0]=Revolver, canCloak=1.
    public static CharacterClassDefinition Spy { get; } = new(
        Id: PlayerClass.Spy,
        DisplayName: "Spy",
        PrimaryWeapon: Revolver,
        MaxHealth: 100,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 194.4f,
        GroundAcceleration: 589.68f,
        GroundDeceleration: 849.42f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    public static CharacterClassDefinition Quote { get; } = new(
        Id: PlayerClass.Quote,
        DisplayName: "Quote",
        PrimaryWeapon: Blade,
        MaxHealth: 140,
        Width: 24f,
        Height: 36f,
        MaxRunSpeed: 192.6f,
        GroundAcceleration: 585.54f,
        GroundDeceleration: 843.18f,
        Gravity: 700f,
        JumpSpeed: 300f,
        MaxAirJumps: 0);

    public static CharacterClassDefinition GetDefinition(PlayerClass playerClass)
    {
        return playerClass switch
        {
            PlayerClass.Engineer => Engineer,
            PlayerClass.Pyro => Pyro,
            PlayerClass.Soldier => Soldier,
            PlayerClass.Demoman => Demoman,
            PlayerClass.Heavy => Heavy,
            PlayerClass.Sniper => Sniper,
            PlayerClass.Medic => Medic,
            PlayerClass.Spy => Spy,
            PlayerClass.Quote => Quote,
            _ => Scout,
        };
    }
}
