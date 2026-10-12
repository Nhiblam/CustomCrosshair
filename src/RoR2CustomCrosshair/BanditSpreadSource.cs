using System;
using System.Collections.Generic;
using EntityStates;
using EntityStates.Bandit2.Weapon;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Bandit. The envelope follows the attack on the "Weapon" state machine:
///
/// Lights Out / Desperado (prep and fire states): the revolver's BulletAttack
/// with configured fixed min/maxSpread (zero in the shipped data).
/// Serrated Shiv: a projectile fired straight along the aim ray (zero).
/// Otherwise (idle, primaries, reload, Serrated Dagger): the equipped
/// primary's next shot, both GenericBulletBaseState weapons on the body's
/// spreadBloomAngle (0..8 deg on Bandit2Body):
///
/// Burst (FireShotgun2): pellets fanned evenly in yaw across
/// +/-(fixedSpreadYaw + bloom) degrees plus each pellet's random deviation
/// (zero yaw/pitch scale in the shipped data): 1 deg + bloom, open at rest.
/// Blast (Bandit2FireRifle): deviation up to (maxSpread + bloom) degrees,
/// yaw x1.3 and pitch x0.5: 1.3 x bloom, exactly zero at rest.
///
/// The crosshair is drawn round, so the widest (horizontal) extent is used.
/// A replaced primary (e.g. a Heresy item) shows no Bandit spread.
/// </summary>
internal sealed class BanditSpreadSource : ISpreadSource
{
    private const string BanditBodyName = "Bandit2Body";

    private sealed class Weapon
    {
        internal readonly string Name;
        private readonly GenericBulletBaseState _config;

        // Half-width of FireShotgun2's yaw fan at zero bloom; 0 when there is no fan.
        internal readonly float FanDegrees;
        internal readonly bool HasFan;

        internal Weapon(string name, GenericBulletBaseState config, bool hasFan, float fanDegrees)
        {
            Name = name;
            _config = config;
            HasFan = hasFan;
            FanDegrees = fanDegrees;
        }

        internal float EnvelopeDegrees(float bloom)
        {
            float random = _config.maxSpread + bloom;
            float fan = HasFan ? FanDegrees + bloom : 0f;
            return Mathf.Max(fan + random * _config.spreadYawScale, random * _config.spreadPitchScale);
        }

        internal string Describe() =>
            $"{_config.bulletCount} bullet(s), maxSpread {_config.maxSpread:0.###}, yaw x{_config.spreadYawScale:0.###}, " +
            $"pitch x{_config.spreadPitchScale:0.###}, fan {(HasFan ? $"+/-{FanDegrees:0.###} deg + bloom" : "none")}";
    }

    private readonly BodyStateMachine _weaponMachine = new("Weapon");

    private BodyIndex _banditBodyIndex = BodyIndex.None;

    // Configured weapon data per state type, read once.
    private readonly Dictionary<Type, Weapon?> _weapons = new();

    private SkillDef? _lastPrimarySkill;
    private Weapon? _lastWeapon;

    // One summary line per body and primary.
    private CharacterBody? _loggedBody;
    private Weapon? _loggedWeapon;

    // Validated in-game (B4): no per-second samples.
    public bool LogSamples => false;

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

        switch (_weaponMachine.StateOf(body))
        {
            case BaseFireSidearmRevolverState fire:
                sample = new SpreadSample(fire.maxSpread, SpreadKind.Fixed, fire.GetType().Name);
                return true;

            case BasePrepSidearmRevolverState prep:
                // The prep state always continues into its revolver shot.
                BaseFireSidearmRevolverState? shot = RevolverShotFor(prep);
                if (shot != null)
                {
                    sample = new SpreadSample(shot.maxSpread, SpreadKind.Fixed, shot.GetType().Name);
                    return true;
                }

                break;

            case Bandit2FireShiv:
                sample = SpreadSample.None("Bandit2FireShiv");
                return true;
        }

        Weapon? weapon = CurrentPrimary(body);
        if (weapon == null)
        {
            return false;
        }

        LogWeaponOnce(body, weapon);
        sample = new SpreadSample(weapon.EnvelopeDegrees(body.spreadBloomAngle), SpreadKind.Bloom, weapon.Name);
        return true;
    }

    private static BaseFireSidearmRevolverState? RevolverShotFor(BasePrepSidearmRevolverState prep)
    {
        Type? shotType = prep switch
        {
            PrepSidearmSkullRevolver => typeof(FireSidearmSkullRevolver),
            PrepSidearmResetRevolver => typeof(FireSidearmResetRevolver),
            _ => null
        };

        return shotType != null ? AttackSpread.Configured<BaseFireSidearmRevolverState>(shotType) : null;
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
            FireShotgun2? state = AttackSpread.Configured<FireShotgun2>(stateType);
            if (state != null)
            {
                // FireShotgun2.FireBullet: total fan = Random(minFixed + bloom, maxFixed + bloom) * 2,
                // only when firing more than one pellet.
                bool hasFan = state.bulletCount > 1;
                float fan = Mathf.Max(state.minFixedSpreadYaw, state.maxFixedSpreadYaw);
                weapon = new Weapon(stateType.Name, state, hasFan, fan);
            }
        }
        else if (typeof(Bandit2FireRifle).IsAssignableFrom(stateType))
        {
            Bandit2FireRifle? state = AttackSpread.Configured<Bandit2FireRifle>(stateType);
            if (state != null)
            {
                weapon = new Weapon(stateType.Name, state, false, 0f);
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
        float none = curve != null ? curve.Evaluate(0f) : 0f;
        float full = curve != null ? curve.Evaluate(1f) : 0f;
        Plugin.Log.LogDebug(
            $"[Spread] Bandit source active: primary '{_lastPrimarySkill?.skillName}' ({weapon.Name}), {weapon.Describe()}; " +
            $"bloom {none:0.###}..{full:0.###} deg, decay {body.spreadBloomDecayTime:0.###} s " +
            $"(attack speed scales decay: {body.spreadBloomDecayWithAttackSpeed}); " +
            $"envelope {weapon.EnvelopeDegrees(none):0.###} deg with no bloom, {weapon.EnvelopeDegrees(full):0.###} deg at full bloom.");
    }
}
