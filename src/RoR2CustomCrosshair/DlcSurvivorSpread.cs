using EntityStates.Railgunner.Weapon;
using EntityStates.Seeker;
using EntityStates.VoidSurvivor.Weapon;
using RoR2;

namespace RoR2CustomCrosshair;

/// <summary>
/// Firing-envelope tables for the DLC survivors (Phase C2), each verified
/// against the installed game's state code and configuration, under the
/// locked B4 contract (absolute current envelope; active attack overrides the
/// primary's next shot). Generic bullet/projectile states (e.g. Railgunner's
/// snipes) need no rule: TableSpreadSource reads them.
/// </summary>
internal static class DlcSurvivorSpread
{
    internal static ISpreadSource[] Create() => new ISpreadSource[]
    {
        Railgunner(), VoidFiend(), Seeker(), FalseSon(), Chef(), Drifter(), DroneTech(), Acolyte(), Enforcer()
    };

    private static ISpreadSource Railgunner() => new TableSpreadSource(
        "Railgunner", "RailgunnerBody", new[] { "Weapon", "WeaponMine" },
        // M99 pistol: projectile rotated by Random(0, baseInaccuracyDegrees +
        // spreadBloomAngle) around a random axis; each shot adds bloom.
        new AttackRule(typeof(FirePistol), (state, body) =>
            new SpreadSample(((FirePistol)state).baseInaccuracyDegrees + body.spreadBloomAngle, SpreadKind.Bloom, "M99 pistol")),
        // Super / Cryo windup: SetSpreadBloom sweep is cosmetic; the following
        // FireSnipeSuper / FireSnipeCryo bullet has yaw/pitch scale 0.
        AttackRule.None(typeof(BaseWindupSnipe), "Supercharge/Cryocharge windup (zero-spread shot)"),
        AttackRule.None(typeof(BaseChargeSnipe), "Supercharge/Cryocharge charging (no shot)"),
        // Mines: thrown straight along the aim ray.
        AttackRule.None(typeof(BaseFireMine), "Mine (straight throw)"));
    // Scoped snipes (FireSnipeLight: maxSpread 0 + bloom; Heavy/Super/Cryo:
    // scale 0) are GenericBulletBaseStates, read generically.

    private static ISpreadSource VoidFiend() => new TableSpreadSource(
        "Void Fiend", "VoidSurvivorBody", new[] { "Weapon", "Body" },
        // Drown: BulletAttack maxSpread = spreadBloomAngle only (its maxSpread
        // field is unused); bloom added after each shot.
        AttackRule.Bloom(typeof(FireHandBeam), "Drown"),
        // Corrupted Drown: each tick fires with the fixed maxSpread field.
        new AttackRule(typeof(FireCorruptHandBeam), (state, _) =>
            new SpreadSample(((FireCorruptHandBeam)state).maxSpread, SpreadKind.Fixed, "Corrupted Drown")),
        new AttackRule(typeof(ChargeCorruptHandBeam), (_, _) => CorruptBeamEnvelope()),
        // Flood: ApplySpread(0, 0) after aim assist; its bloom is cosmetic.
        AttackRule.None(typeof(ChargeMegaBlaster), "Flood charging (zero-spread shot)"),
        AttackRule.None(typeof(ReadyMegaBlaster), "Flood charged (zero-spread shot)"),
        AttackRule.None(typeof(FireMegaBlasterBase), "Flood (zero-spread projectile)"),
        // Corrupted Flood: ApplySpread(0, spreadBloomAngle + spread); bloom is
        // added before firing, so the shot includes it.
        new AttackRule(typeof(FireCorruptDisks), (state, body) =>
            new SpreadSample(body.spreadBloomAngle + ((FireCorruptDisks)state).spread, SpreadKind.Bloom, "Corrupted Flood")));
    // Melee (Swing), blinks and Suppress have no ranged spread.

    private static SpreadSample CorruptBeamEnvelope()
    {
        FireCorruptHandBeam? beam = AttackSpread.Configured<FireCorruptHandBeam>(typeof(FireCorruptHandBeam));
        return beam != null
            ? new SpreadSample(beam.maxSpread, SpreadKind.Fixed, "Corrupted Drown (charging)")
            : SpreadSample.None("Corrupted Drown (charging, unconfigured)");
    }

    private static ISpreadSource Seeker() => new TableSpreadSource(
        "Seeker", "SeekerBody", new[] { "Weapon", "Body" },
        // Spirit Punch / Soul Search: projectiles along the aim ray (bloom cosmetic).
        AttackRule.None(typeof(SpiritPunch), "Spirit Punch (straight projectile)"),
        AttackRule.None(typeof(SoulSearch), "Soul Search (homing)"),
        // Unseen Hand: world-targeted.
        AttackRule.None(typeof(UnseenHand), "Unseen Hand (world-targeted)"),
        // Palm Blast: charge writes bloom for display only; the blast has no spread.
        AttackRule.None(typeof(BaseChargePalmBlast), "Palm Blast charging (charge does not change aim)"),
        AttackRule.None(typeof(BaseFirePalmBlast), "Palm Blast (no spread)"));
    // Soul Spiral orbits the body; Sojourn / Reprieve / Meditate do not aim.

    private static ISpreadSource FalseSon() => new TableSpreadSource(
        "False Son", "FalseSonBody", new[] { "Weapon", "Body" },
        // Lunar Spikes / Lunar Stake / spike shotgun: one projectile along the
        // aim ray (after aim assist); their bloom is cosmetic.
        AttackRule.None(typeof(EntityStates.FalseSon.LunarSpikes), "Lunar Spikes/Stake (straight projectile)"),
        AttackRule.None(typeof(EntityStates.FalseSon.LunarSpikesShotgun), "Lunar Spikes shotgun (straight projectile)"),
        // Club heavy-swing and Laser of the Father charges write bloom for
        // display only; the laser and burst are zero-spread BulletAttacks.
        AttackRule.None(typeof(EntityStates.FalseSon.ClubSwing3), "Club charge (melee)"),
        AttackRule.None(typeof(EntityStates.FalseSon.LaserFather), "Laser of the Father charging (zero-spread beam)"),
        AttackRule.None(typeof(EntityStates.FalseSon.LaserFatherCharged), "Laser of the Father (zero-spread beam)"),
        AttackRule.None(typeof(EntityStates.FalseSon.LaserFatherBurstPrep), "Laser Burst windup (zero-spread beam)"),
        AttackRule.None(typeof(EntityStates.FalseSon.LaserFatherBurst), "Laser Burst (zero-spread beam)"),
        // Meridian's Will: world-targeted.
        AttackRule.None(typeof(EntityStates.FalseSon.MeridiansWillAim), "Meridian's Will (world-targeted)"));
    // Club swings are melee; Step of the Brothers is a dash.

    private static ISpreadSource Chef() => new TableSpreadSource(
        "CHEF", "ChefBody", new[] { "Weapon", "Body" },
        // Dice: one cleaver at a fixed ~1.15 deg upward offset (not
        // dispersion); the boosted ring is not aimed. Bloom while cleavers are
        // out is cosmetic.
        AttackRule.None(typeof(EntityStates.Chef.Dice), "Dice (straight projectile)"),
        // Sear: BulletAttack with static maxSpread (does not read bloom).
        AttackRule.Fixed(typeof(EntityStates.Chef.Sear), "Sear", () => EntityStates.Chef.Sear.maxSpread),
        // Ice Box: cubes straight along the aim ray.
        AttackRule.None(typeof(EntityStates.Chef.IceBox), "Ice Box (straight projectiles)"),
        // Glaze: later grenades deviate by Random(0, spreadBloomAngle +
        // xDeviationSpread); bloom is added after every grenade.
        new AttackRule(typeof(EntityStates.Chef.Glaze), (_, body) =>
            new SpreadSample(body.spreadBloomAngle + EntityStates.Chef.Glaze.xDeviationSpread, SpreadKind.Bloom, "Glaze")),
        // Roll: charge writes bloom for display only; no aim.
        AttackRule.None(typeof(EntityStates.Chef.RolyPoly), "Roll charge (no aim)"));
    // Oil Spill and Yes, Chef do not aim.

    private static ISpreadSource Drifter() => new TableSpreadSource(
        "Drifter", "DrifterBody", new[] { "Weapon", "Body" },
        // Cleanup: arc throw plus pellets ApplySpread(0, shotgunConeAngle).
        new AttackRule(typeof(EntityStates.Drifter.AimMagicBag), (state, _) =>
            new SpreadSample(((EntityStates.Drifter.AimMagicBag)state).shotgunConeAngle, SpreadKind.Fixed, "Cleanup (pellet cone)")),
        // Cleanup (classic): pellets at Random(0, shotgunConeAngle) radially.
        new AttackRule(typeof(EntityStates.Drifter.Cleanup), (state, _) =>
            new SpreadSample(((EntityStates.Drifter.Cleanup)state).shotgunConeAngle, SpreadKind.Fixed, "Cleanup classic (pellet cone)")),
        // Empty Bag and other arc throws: world-targeted.
        AttackRule.None(typeof(EntityStates.AimThrowableBase), "Throw (world-targeted)"),
        // Repossess: target search, then a dash.
        AttackRule.None(typeof(EntityStates.Drifter.AimRepossess), "Repossess (targeted)"));
    // Blunt Force, Tornado Slam, Suffocate and Junk Cube are melee; Tinker is
    // a GenericProjectileBaseState with zero spread, read generically.

    private static ISpreadSource DroneTech() => new TableSpreadSource(
        "Drone Tech", "DroneTechBody", new[] { "Weapon", "Shield" },
        // Drone commands, drone ball and shield formation: targeted or straight.
        AttackRule.None(typeof(EntityStates.DroneTech.Weapon.Activate), "Command (targeted)"),
        AttackRule.None(typeof(EntityStates.DroneTech.Weapon.ThrowDroneBall), "Drone ball (straight projectile)"),
        AttackRule.None(typeof(EntityStates.DroneTech.Weapon.ShieldFormation), "Shield formation (straight projectile)"),
        AttackRule.None(typeof(EntityStates.DroneTech.Weapon.FireShield), "Shield (straight projectile)"),
        AttackRule.None(typeof(EntityStates.DroneTech.Weapon.Paint.BaseDroneHeadbuttPaintState), "Headbutt paint (lock-on)"));
    // Nano pistol (GenericBulletBaseState, yaw/pitch scale 0: always exact;
    // its bloom is cosmetic) and Nano Bomb (GenericProjectileBaseState) are
    // read generically.

    private static ISpreadSource Acolyte() => new TableSpreadSource(
        "Acolyte", "AcolyteBody", new[] { "Weapon", "Body" },
        // Bola: projectile straight along the aim ray.
        AttackRule.None(typeof(EntityStates.Acolyte.BolaWhip), "Bola (straight projectile)"),
        // Sacrifice: lock-on target. Soul Jar: world-targeted arc.
        AttackRule.None(typeof(EntityStates.Acolyte.Weapon.AimSacrifice), "Sacrifice (lock-on)"),
        AttackRule.None(typeof(EntityStates.AimThrowableBase), "Soul Jar (world-targeted)"));
    // Blade combo is melee (bloom on hit is cosmetic); Javelin / Blade Sling
    // are zero-spread GenericProjectileBaseStates, read generically.

    private static ISpreadSource Enforcer() => new TableSpreadSource(
        "Enforcer", "Enforcer2Body", new[] { "Reload", "Weapon", "Body", "Shield" },
        // Shielded riot shotgun: adds bulletSpreadBloom, then overrides the
        // pellet cone with spreadBloomAngle * maxSpread (yaw scale 1).
        new AttackRule(typeof(EntityStates.Enforcer.FireRiotShotgunShielded), ShieldedShotgunEnvelope));
    // Riot shotgun (GenericBulletBaseState: maxSpread + bloom), cluster
    // grenades, stun net and tear gas (zero-spread GenericProjectileBaseStates)
    // are read generically; shield dash, leap and kinetic charge do not aim.

    /// <summary>
    /// FireRiotShotgunShielded.OnEnter: AddSpreadBloom(bulletSpreadBloom), then
    /// maxSpread = spreadBloomAngle * maxSpread. For the next shot the added
    /// bloom is applied to the body's current bloom (recovered through the
    /// body curve, which is linear from zero for Enforcer); a live shot has
    /// already added it.
    /// </summary>
    private static SpreadSample ShieldedShotgunEnvelope(EntityStates.EntityState state, CharacterBody body)
    {
        var shot = (EntityStates.Enforcer.FireRiotShotgunShielded)state;
        float scale = UnityEngine.Mathf.Max(shot.spreadYawScale, shot.spreadPitchScale);
        float angle = body.spreadBloomAngle;
        UnityEngine.AnimationCurve? curve = body.spreadBloomCurve;
        if (state.outer == null && curve != null && curve.length > 0)
        {
            float full = curve.Evaluate(1f);
            float bloom = full > 0f ? UnityEngine.Mathf.Clamp01(angle / full) : 0f;
            angle = curve.Evaluate(UnityEngine.Mathf.Clamp01(bloom + shot.bulletSpreadBloom));
        }

        return new SpreadSample(angle * shot.maxSpread * scale, SpreadKind.Bloom, "Riot shotgun (shielded)");
    }
}
