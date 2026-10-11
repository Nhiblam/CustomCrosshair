using System;
using System.Collections.Generic;
using EntityStates;
using EntityStates.Bandit2.Weapon;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Bandit's primaries (Phase B2). Both are GenericBulletBaseState weapons
/// reading the body's spreadBloomAngle (0..8 deg on Bandit2Body):
///
/// Blast (Bandit2FireRifle): one bullet, deviation up to (maxSpread + bloom)
/// degrees, yaw scaled x1.3 and pitch x0.5 in the shipped configuration.
///
/// Burst (FireShotgun2): pellets fanned evenly in yaw across
/// +/-(fixedSpreadYaw + bloom) degrees, plus each pellet's own random
/// deviation (configured with zero yaw/pitch scale in the shipped data, so the
/// fan is the whole cone).
///
/// The sample is the outer angular envelope the next shot can reach. The
/// crosshair is drawn round, so this is the widest (horizontal) extent.
/// Active only while the primary slot's current skill runs one of these two
/// states, so a replaced primary (e.g. a Heresy item) shows no Bandit spread.
/// </summary>
internal sealed class BanditSpreadSource : ISpreadSource
{
    private const string BanditBodyName = "Bandit2Body";

    private sealed class Weapon
    {
        internal readonly string Name;
        internal readonly GenericBulletSpread Spread;

        // Half-width of FireShotgun2's yaw fan at zero bloom; 0 when there is no fan.
        internal readonly float FanDegrees;
        internal readonly bool HasFan;

        internal Weapon(string name, GenericBulletSpread spread, bool hasFan, float fanDegrees)
        {
            Name = name;
            Spread = spread;
            HasFan = hasFan;
            FanDegrees = fanDegrees;
        }

        internal float EnvelopeDegrees(float bloom)
        {
            float fan = HasFan ? FanDegrees + bloom : 0f;
            return Mathf.Max(fan + Spread.HorizontalDegrees(bloom), Spread.VerticalDegrees(bloom));
        }
    }

    private BodyIndex _banditBodyIndex = BodyIndex.None;

    // Configured weapon data per state type, read once.
    private readonly Dictionary<Type, Weapon?> _weapons = new();

    private SkillDef? _lastPrimarySkill;
    private Weapon? _lastWeapon;

    // Temporary Phase B2 diagnostics: one summary per body and primary.
    private CharacterBody? _loggedBody;
    private Weapon? _loggedWeapon;

    public bool LogSamples => true;

    public bool CanHandle(CharacterBody body)
    {
        if (_banditBodyIndex == BodyIndex.None)
        {
            _banditBodyIndex = BodyCatalog.FindBodyIndex(BanditBodyName);
            if (_banditBodyIndex == BodyIndex.None)
            {
                return false;
            }
        }

        return body.bodyIndex == _banditBodyIndex;
    }

    public bool TrySample(CharacterBody body, out SpreadSample sample)
    {
        sample = default;

        Weapon? weapon = CurrentPrimary(body);
        if (weapon == null || body.spreadBloomCurve == null)
        {
            return false;
        }

        float bloom = body.spreadBloomAngle;
        float restingBloom = body.spreadBloomCurve.Evaluate(0f);
        sample = new SpreadSample(weapon.EnvelopeDegrees(bloom), weapon.EnvelopeDegrees(restingBloom), bloom);
        LogWeaponOnce(body, weapon);
        return true;
    }

    private Weapon? CurrentPrimary(CharacterBody body)
    {
        GenericSkill? primary = body.skillLocator ? body.skillLocator.primary : null;
        SkillDef? skill = primary ? primary!.skillDef : null;
        if (skill == null)
        {
            return null;
        }

        if (skill != _lastPrimarySkill)
        {
            _lastPrimarySkill = skill;
            Type? stateType = skill.activationState.stateType;
            _lastWeapon = stateType != null ? WeaponFor(stateType) : null;
        }

        return _lastWeapon;
    }

    private Weapon? WeaponFor(Type stateType)
    {
        if (_weapons.TryGetValue(stateType, out Weapon? cached))
        {
            return cached;
        }

        Weapon? weapon = null;
        if (typeof(FireShotgun2).IsAssignableFrom(stateType))
        {
            FireShotgun2? state = GenericBulletSpread.ReadState<FireShotgun2>(stateType);
            if (state != null)
            {
                // FireShotgun2.FireBullet: total fan = Random(minFixed + bloom, maxFixed + bloom) * 2,
                // only when firing more than one pellet.
                bool hasFan = state.bulletCount > 1;
                float fan = Mathf.Max(state.minFixedSpreadYaw, state.maxFixedSpreadYaw);
                weapon = new Weapon(stateType.Name, GenericBulletSpread.From(state), hasFan, fan);
            }
        }
        else if (typeof(Bandit2FireRifle).IsAssignableFrom(stateType))
        {
            Bandit2FireRifle? state = GenericBulletSpread.ReadState<Bandit2FireRifle>(stateType);
            if (state != null)
            {
                weapon = new Weapon(stateType.Name, GenericBulletSpread.From(state), false, 0f);
            }
        }

        _weapons[stateType] = weapon;
        return weapon;
    }

    private void LogWeaponOnce(CharacterBody body, Weapon weapon)
    {
        if (_loggedBody == body && _loggedWeapon == weapon)
        {
            return;
        }

        _loggedBody = body;
        _loggedWeapon = weapon;

        AnimationCurve curve = body.spreadBloomCurve;
        float rest = curve.Evaluate(0f);
        float full = curve.Evaluate(1f);
        Plugin.Log.LogDebug(
            $"[Spread] Bandit source active: primary '{_lastPrimarySkill?.skillName}' ({weapon.Name}), " +
            $"{weapon.Spread.Describe()}, fan {(weapon.HasFan ? $"+/-{weapon.FanDegrees:0.###} deg + bloom" : "none")}; " +
            $"bloom {rest:0.###}..{full:0.###} deg, decay {body.spreadBloomDecayTime:0.###} s " +
            $"(attack speed scales decay: {body.spreadBloomDecayWithAttackSpeed}); " +
            $"envelope {weapon.EnvelopeDegrees(rest):0.###} deg at rest, {weapon.EnvelopeDegrees(full):0.###} deg at full bloom.");
    }
}
