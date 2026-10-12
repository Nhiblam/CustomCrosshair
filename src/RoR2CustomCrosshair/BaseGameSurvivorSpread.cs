using EntityStates;
using EntityStates.Captain.Weapon;
using EntityStates.Croco;
using EntityStates.Engi;
using EntityStates.Engi.EngiMissilePainter;
using EntityStates.Engi.EngiWeapon;
using EntityStates.Huntress;
using EntityStates.Huntress.HuntressWeapon;
using EntityStates.Loader;
using EntityStates.Mage.Weapon;
using EntityStates.Treebot;
using EntityStates.Treebot.Weapon;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Firing-envelope tables for Huntress through Captain (Phase B5), each
/// verified against the installed game's state code and configuration.
/// Generic bullet/projectile states (e.g. Ballista, Eviscerate's projectile,
/// Captain's shotgun blast) need no rule: TableSpreadSource reads them.
/// </summary>
internal static class BaseGameSurvivorSpread
{
    internal static ISpreadSource[] Create() => new ISpreadSource[]
    {
        Huntress(), Engineer(), Artificer(), Mercenary(), Rex(), Loader(), Acrid(), Captain()
    };

    private static ISpreadSource Huntress() => new TableSpreadSource(
        "Huntress", "HuntressBody", new[] { "Weapon", "Body" },
        // Strafe / Flurry: HuntressArrowOrb sent to the tracked target.
        AttackRule.None(typeof(FireSeekingArrow), "Strafe/Flurry (homing arrow)"),
        // Laser Glaive: bouncing LightningOrb to the tracked target.
        AttackRule.None(typeof(ThrowGlaive), "Laser Glaive (homing orb)"),
        // Cyclone: projectile fired straight along the aim ray.
        AttackRule.None(typeof(ThrowCycloneKickable), "Cyclone (straight projectile)"),
        // Arrow Rain: projectile placed at a world target point.
        AttackRule.None(typeof(ArrowRain), "Arrow Rain (world-targeted)"));
    // Ballista (FireArrowSnipe) is a GenericBulletBaseState with zero spread/scales.

    private static ISpreadSource Engineer() => new TableSpreadSource(
        "Engineer", "EngiBody", new[] { "Weapon" },
        // Bouncing Grenades: ChargeGrenades raises bloom with charge
        // (SetSpreadBloom(lerp(min, max bonus))); each grenade deviates by
        // Random(0, spreadBloomAngle) and adds bloom. arcAngle only tilts the aim.
        AttackRule.Bloom(typeof(ChargeGrenades), "Bouncing Grenades (charging)"),
        AttackRule.Bloom(typeof(FireGrenades), "Bouncing Grenades"),
        // Laser Cutter: BulletAttack with zero spread; its bloom pulse is cosmetic.
        AttackRule.None(typeof(EnterEngiLaserCutter), "Laser Cutter (zero-spread beam)"),
        AttackRule.None(typeof(EngiLaserCutter), "Laser Cutter (zero-spread beam)"),
        // Mines, Spider Mines, Bubble Shield: thrown straight along the aim ray.
        AttackRule.None(typeof(FireMines), "Mine/Bubble Shield (straight throw)"),
        // Thermal Harpoons: homing missiles to painted targets.
        AttackRule.None(typeof(Fire), "Thermal Harpoons (homing)"));

    private static ISpreadSource Artificer() => new TableSpreadSource(
        "Artificer", "MageBody", new[] { "Weapon", "Body" },
        // Flame / Plasma Bolt: projectile along the aim ray (bloom is cosmetic).
        AttackRule.None(typeof(FireFireBolt), "Flame/Plasma Bolt (straight projectile)"),
        // Nano-Bomb / Nano-Spear: charge sets bloom for the vanilla charge display
        // only; the bomb is thrown along the aim ray.
        AttackRule.None(typeof(BaseChargeBombState), "Nano-Bomb/Spear charging (charge does not change aim)"),
        AttackRule.None(typeof(BaseThrowBombState), "Nano-Bomb/Spear (straight projectile)"),
        // Flamethrower: BulletAttack with zero spread (radius 2 beam).
        AttackRule.None(typeof(Flamethrower), "Flamethrower (zero-spread beam)"),
        // FireEarthShards: shards fanned in yaw up to maxSpread / 2, plus
        // ApplySpread(minNoise, maxNoise) noise.
        AttackRule.Fixed(typeof(FireEarthShards), "Earth shards (fan + noise)",
            () => FireEarthShards.maxSpread * 0.5f + FireEarthShards.maxNoise));

    // Mercenary: all melee; Eviscerate's projectile (ThrowEvisProjectile) is a
    // GenericProjectileBaseState with zero spread. No rules needed.
    private static ISpreadSource Mercenary() => new TableSpreadSource(
        "Mercenary", "MercBody", new[] { "Weapon", "Body" });

    private static ISpreadSource Rex() => new TableSpreadSource(
        "REX", "TreebotBody", new[] { "Weapon" },
        // DIRECTIVE: Inject: syringes at fixed yaw offsets
        // floor(n - (count - 1) / 2) / (count - 1) * totalYawSpread, no random spread.
        AttackRule.Fixed(typeof(FireSyringe), "DIRECTIVE: Inject (fixed yaw fan)", SyringeFanDegrees),
        // Sonic Boom: a BullseyeSearch area cone, not projectile dispersion.
        AttackRule.None(typeof(ChargeSonicBoom), "Sonic Boom (area cone)"),
        AttackRule.None(typeof(EntityStates.Treebot.Weapon.FireSonicBoom), "Sonic Boom (area cone)"),
        // Seed Barrage / Tangling Growth throws, Bramble Volley seed: world-targeted or along the aim ray.
        AttackRule.None(typeof(AimThrowableBase), "Mortar/throw (world-targeted)"),
        AttackRule.None(typeof(TreebotFireFruitSeed), "DIRECTIVE: Harvest (straight projectile)"),
        AttackRule.None(typeof(FireFlower2), "Tangling Growth (world-targeted)"));

    private static ISpreadSource Loader() => new TableSpreadSource(
        "Loader", "LoaderBody", new[] { "Weapon", "Hook", "Pylon", "Body" },
        // Grapple / Spiked Fist and M551 Pylon: projectiles along the aim ray.
        AttackRule.None(typeof(EntityStates.Loader.FireHook), "Grapple (straight projectile)"),
        AttackRule.None(typeof(ThrowPylon), "M551 Pylon (straight projectile)"),
        // Charged Gauntlet / Thunder Gauntlet: charge sets bloom for the vanilla
        // charge display only; the punch is melee.
        AttackRule.None(typeof(BaseChargeFist), "Gauntlet charging (charge does not change aim)"));

    private static ISpreadSource Acrid() => new TableSpreadSource(
        "Acrid", "CrocoBody", new[] { "Weapon", "Mouth", "Body" },
        // Neurotoxin / Epidemic and Breath: projectiles along the aim ray.
        AttackRule.None(typeof(FireSpit), "Neurotoxin/Epidemic (straight projectile)"),
        AttackRule.None(typeof(Breath), "Breath (straight projectile)"));

    private static ISpreadSource Captain() => new TableSpreadSource(
        "Captain", "CaptainBody", new[] { "Weapon", "Skillswap" },
        // Vulcan Shotgun: ChargeCaptainShotgun sets bloom to age / chargeDuration;
        // Captain's bloom curve is inverted (8 deg at no bloom -> 1 deg at full),
        // and FireCaptainShotgun (GenericBulletBaseState) fires 8 pellets with
        // maxSpread = configured 0 + spreadBloomAngle at unit scale. So the cone
        // narrows while charging; the live FireCaptainShotgun is read generically.
        new AttackRule(typeof(ChargeCaptainShotgun), (_, body) => CaptainShotgunEnvelope(body)),
        // Power Tazer: projectile along the aim ray.
        AttackRule.None(typeof(FireTazer), "Power Tazer (straight projectile)"),
        // Orbital strikes and supply drops: world-targeted.
        AttackRule.None(typeof(AimThrowableBase), "Orbital Probe (world-targeted)"),
        AttackRule.None(typeof(CallSupplyDropBase), "Supply Beacon (world-targeted)"));

    private static SpreadSample CaptainShotgunEnvelope(RoR2.CharacterBody body)
    {
        FireCaptainShotgun? shot = AttackSpread.Configured<FireCaptainShotgun>(typeof(FireCaptainShotgun));
        return shot != null
            ? AttackSpread.Bullet(shot, body.spreadBloomAngle, "Vulcan Shotgun (charging)")
            : new SpreadSample(body.spreadBloomAngle, SpreadKind.Bloom, "Vulcan Shotgun (charging)");
    }

    private static float SyringeFanDegrees()
    {
        int count = FireSyringe.projectileCount;
        if (count < 2)
        {
            return 0f;
        }

        float widest = 0f;
        for (int n = 0; n < count; n++)
        {
            float yaw = Mathf.FloorToInt(n - (count - 1) / 2f) / (float)(count - 1) * FireSyringe.totalYawSpread;
            widest = Mathf.Max(widest, Mathf.Abs(yaw));
        }

        return widest;
    }
}
