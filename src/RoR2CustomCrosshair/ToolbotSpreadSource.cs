using System;
using System.Collections.Generic;
using EntityStates.Toolbot;
using RoR2;
using RoR2.Skills;

namespace RoR2CustomCrosshair;

/// <summary>
/// MUL-T. The stance state machine assigns the active weapon to
/// skillLocator.primary (ToolbotBodyPrimary1 in stance A, ToolbotBodyPrimary2
/// in stance B, null during the Retool swap) and installs that weapon's
/// ToolbotWeaponSkillDef.crosshairSpreadCurve as the body's spreadBloomCurve.
/// Power Mode keeps the stance weapon as primary and overrides
/// skillLocator.secondary with the second weapon; both fire.
///
/// Each weapon's spread does not depend on which of its states is running,
/// so the envelope comes from the equipped weapon skills:
/// Auto-Nailgun: sustained fire tilts each nail by up to spreadBloomAngle
/// (2..5 deg on its curve); the release burst fires with maxSpread =
/// spreadBloomAngle at yaw/pitch scale squared.
/// Scrap Launcher (GenericProjectileBaseState): fixed configured spread (1 deg).
/// Rebar Puncher (GenericBulletBaseState): zero spread and zero scales; its
/// bloom is a cooldown timer for the vanilla crosshair.
/// Power-Saw: melee hitbox, no spread.
/// In Power Mode both weapons fire at once, so the envelope is their union
/// (the wider cone). No weapon during the Retool swap: zero.
/// </summary>
internal sealed class ToolbotSpreadSource : ISpreadSource
{
    private const string ToolbotBodyName = "ToolbotBody";

    private BodyIndex _toolbotBodyIndex = BodyIndex.None;

    // Avoid resolving activation state types every frame (two weapons in Power Mode).
    private readonly Dictionary<SkillDef, Type?> _stateTypes = new();

    // One summary line per body and weapon pair.
    private CharacterBody? _loggedBody;
    private SkillDef? _loggedPrimary;
    private SkillDef? _loggedOffhand;

    // Validated in-game (B4): no per-second samples.
    public bool LogSamples => false;

    public bool CanHandle(CharacterBody body)
    {
        if (_toolbotBodyIndex == BodyIndex.None)
        {
            _toolbotBodyIndex = BodyCatalog.FindBodyIndex(ToolbotBodyName);
            if (_toolbotBodyIndex == BodyIndex.None)
            {
                return false;
            }
        }

        return body.bodyIndex == _toolbotBodyIndex;
    }

    public bool TrySample(CharacterBody body, out SpreadSample sample)
    {
        sample = default;

        SkillLocator? skills = body.skillLocator;
        if (!skills)
        {
            return false;
        }

        // Null while Retool swaps weapons.
        ToolbotWeaponSkillDef? primary = skills!.primary ? skills.primary.skillDef as ToolbotWeaponSkillDef : null;

        // Only Power Mode puts a weapon in the secondary slot.
        ToolbotWeaponSkillDef? offhand = skills.secondary ? skills.secondary.skillDef as ToolbotWeaponSkillDef : null;

        LogWeaponsOnce(body, primary, offhand);

        float bloom = body.spreadBloomAngle;
        sample = WeaponEnvelope(primary, bloom);
        if (offhand != null)
        {
            SpreadSample second = WeaponEnvelope(offhand, bloom);
            if (second.Degrees > sample.Degrees)
            {
                sample = second;
            }
        }

        return true;
    }

    private SpreadSample WeaponEnvelope(SkillDef? weapon, float bloom)
    {
        Type? stateType = StateTypeOf(weapon);
        if (stateType == null)
        {
            return SpreadSample.None("no weapon");
        }

        if (typeof(FireNailgun).IsAssignableFrom(stateType))
        {
            return new SpreadSample(bloom * NailgunEnvelopeScale(), SpreadKind.Bloom, stateType.Name);
        }

        // Scrap Launcher (projectile) and Rebar Puncher (bullet) via their configured state.
        if (AttackSpread.TryFromConfiguredState(stateType, bloom, out SpreadSample configured))
        {
            return configured;
        }

        // Power-Saw and anything else without ranged spread.
        return SpreadSample.None(stateType.Name);
    }

    /// <summary>
    /// Sustained fire stays within spreadBloomAngle (scale 1); the release
    /// burst's random spread reaches spreadBloomAngle * scale^2 per axis.
    /// </summary>
    private static float NailgunEnvelopeScale()
    {
        float yaw = BaseNailgunState.spreadYawScale;
        float pitch = BaseNailgunState.spreadPitchScale;
        return Math.Max(1f, Math.Max(yaw * yaw, pitch * pitch));
    }

    private Type? StateTypeOf(SkillDef? skill)
    {
        if (skill == null)
        {
            return null;
        }

        if (!_stateTypes.TryGetValue(skill, out Type? stateType))
        {
            stateType = skill.activationState.stateType;
            _stateTypes[skill] = stateType;
        }

        return stateType;
    }

    private void LogWeaponsOnce(CharacterBody body, SkillDef? primary, SkillDef? offhand)
    {
        if (_loggedBody == body && _loggedPrimary == primary && _loggedOffhand == offhand)
        {
            return;
        }

        _loggedBody = body;
        _loggedPrimary = primary;
        _loggedOffhand = offhand;

        float none = body.spreadBloomCurve != null ? body.spreadBloomCurve.Evaluate(0f) : 0f;
        float full = body.spreadBloomCurve != null ? body.spreadBloomCurve.Evaluate(1f) : 0f;

        string Describe(SkillDef? skill)
        {
            if (skill == null)
            {
                return "none";
            }

            SpreadSample atNone = WeaponEnvelope(skill, none);
            SpreadSample atFull = WeaponEnvelope(skill, full);
            return $"'{skill.skillName}' ({StateTypeOf(skill)?.Name}, {atNone.Kind} spread " +
                   $"{atNone.Degrees:0.###}..{atFull.Degrees:0.###} deg)";
        }

        Plugin.Log.LogDebug(
            $"[Spread] MUL-T weapons: active {Describe(primary)}" +
            (offhand != null ? $", Power Mode offhand {Describe(offhand)}" : "") +
            $"; body bloom curve {none:0.###}..{full:0.###} deg, decay {body.spreadBloomDecayTime:0.###} s.");
    }
}
