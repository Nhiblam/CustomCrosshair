using System;
using System.Collections.Generic;
using EntityStates.Toolbot;
using RoR2;
using RoR2.Skills;

namespace RoR2CustomCrosshair;

/// <summary>
/// MUL-T (Phase B3). The stance state machine assigns the active weapon to
/// skillLocator.primary (ToolbotBodyPrimary1 in stance A, ToolbotBodyPrimary2
/// in stance B, null during the Retool swap) and installs that weapon's
/// ToolbotWeaponSkillDef.crosshairSpreadCurve as the body's spreadBloomCurve.
/// Power Mode keeps the stance weapon as primary and overrides
/// skillLocator.secondary with the second weapon.
///
/// Of the four weapons only the Auto-Nailgun's real spread reads bloom:
/// sustained fire tilts each nail by spreadBloomAngle * sin(n / 2) in a
/// spiral, and the release burst fires with maxSpread = spreadBloomAngle
/// (yaw/pitch scale = configured scale squared). Rebar Puncher has zero
/// spread (its bloom is a cooldown timer), Scrap Launcher has a fixed
/// configured spread (bloom is only added for the vanilla crosshair), and
/// Power-Saw is a melee hitbox. So the sample is the Nailgun's cone, and
/// only while a Nailgun is equipped in the active or Power Mode offhand slot;
/// both share the body's single bloom value and curve.
/// </summary>
internal sealed class ToolbotSpreadSource : ISpreadSource
{
    private const string ToolbotBodyName = "ToolbotBody";

    private BodyIndex _toolbotBodyIndex = BodyIndex.None;

    // Avoid resolving activation state types every frame (two weapons in Power Mode).
    private readonly Dictionary<SkillDef, bool> _isNailgun = new();

    // Temporary Phase B3 diagnostics: one summary per body and weapon pair.
    private CharacterBody? _loggedBody;
    private SkillDef? _loggedPrimary;
    private SkillDef? _loggedOffhand;

    public bool LogSamples => true;

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

        // Null while Retool swaps weapons: no weapon, no spread.
        ToolbotWeaponSkillDef? primary = skills!.primary ? skills.primary.skillDef as ToolbotWeaponSkillDef : null;

        // Only Power Mode puts a weapon in the secondary slot.
        ToolbotWeaponSkillDef? offhand = skills.secondary ? skills.secondary.skillDef as ToolbotWeaponSkillDef : null;

        LogWeaponsOnce(body, primary, offhand);

        if (!IsNailgun(primary) && !IsNailgun(offhand))
        {
            return false;
        }

        if (body.spreadBloomCurve == null)
        {
            return false;
        }

        float scale = NailgunEnvelopeScale();
        float bloom = body.spreadBloomAngle;
        float restingBloom = body.spreadBloomCurve.Evaluate(0f);
        sample = new SpreadSample(bloom * scale, restingBloom * scale, bloom);
        return true;
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

    private bool IsNailgun(SkillDef? skill)
    {
        if (skill == null)
        {
            return false;
        }

        if (!_isNailgun.TryGetValue(skill, out bool isNailgun))
        {
            Type? stateType = skill.activationState.stateType;
            isNailgun = stateType != null && typeof(FireNailgun).IsAssignableFrom(stateType);
            _isNailgun[skill] = isNailgun;
        }

        return isNailgun;
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

        string Describe(SkillDef? skill) => skill == null
            ? "none"
            : $"'{skill.skillName}' ({skill.activationState.stateType?.Name}, " +
              $"{(IsNailgun(skill) ? "dynamic spread" : "no spread growth")})";

        string curve = body.spreadBloomCurve != null
            ? $"{body.spreadBloomCurve.Evaluate(0f):0.###}..{body.spreadBloomCurve.Evaluate(1f):0.###} deg"
            : "none";

        Plugin.Log.LogDebug(
            $"[Spread] MUL-T weapons: active {Describe(primary)}" +
            (offhand != null ? $", Power Mode offhand {Describe(offhand)}" : "") +
            $"; body bloom curve {curve}, decay {body.spreadBloomDecayTime:0.###} s; " +
            $"nailgun envelope scale {NailgunEnvelopeScale():0.###}.");
    }
}
