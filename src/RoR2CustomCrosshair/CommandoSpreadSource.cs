using System;
using EntityStates.Commando.CommandoWeapon;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Commando's Double Tap spread (Phase B prototype).
///
/// FirePistol2 fires with BulletAttack.minSpread = 0 and
/// maxSpread = characterBody.spreadBloomAngle, then calls AddSpreadBloom.
/// spreadBloomAngle = spreadBloomCurve.Evaluate(bloom), where bloom is a
/// 0..1 value that each shot raises and CharacterBody.Update decays
/// linearly. So spreadBloomAngle is exactly the half-angle, in degrees, that
/// the next Double Tap bullet may deviate from the aim direction.
///
/// Active only while the primary slot's current skill runs FirePistol2, so a
/// replaced primary (e.g. a Heresy item) does not show Double Tap spread.
/// </summary>
internal sealed class CommandoSpreadSource : ISpreadSource
{
    private const string CommandoBodyName = "CommandoBody";

    private BodyIndex _commandoBodyIndex = BodyIndex.None;

    // Avoid resolving the activation state type every frame.
    private SkillDef? _lastPrimarySkill;
    private bool _lastPrimaryIsDoubleTap;

    // One summary line per body (validated in-game; no per-second samples).
    private CharacterBody? _loggedBody;

    public bool LogSamples => false;

    public bool CanHandle(CharacterBody body) => IsCommando(body);

    public bool TrySample(CharacterBody body, out SpreadSample sample)
    {
        sample = default;

        if (!IsCommando(body) || !PrimaryIsDoubleTap(body) || body.spreadBloomCurve == null)
        {
            return false;
        }

        sample = new SpreadSample(body.spreadBloomAngle, body.spreadBloomCurve.Evaluate(0f), body.spreadBloomAngle);
        LogBodyOnce(body);
        return true;
    }

    private bool IsCommando(CharacterBody body)
    {
        if (_commandoBodyIndex == BodyIndex.None)
        {
            _commandoBodyIndex = BodyCatalog.FindBodyIndex(CommandoBodyName);
            if (_commandoBodyIndex == BodyIndex.None)
            {
                return false;
            }
        }

        return body.bodyIndex == _commandoBodyIndex;
    }

    private bool PrimaryIsDoubleTap(CharacterBody body)
    {
        GenericSkill? primary = body.skillLocator ? body.skillLocator.primary : null;
        SkillDef? skill = primary ? primary!.skillDef : null;
        if (skill == null)
        {
            return false;
        }

        if (skill != _lastPrimarySkill)
        {
            _lastPrimarySkill = skill;
            Type? stateType = skill.activationState.stateType;
            _lastPrimaryIsDoubleTap = stateType != null && typeof(FirePistol2).IsAssignableFrom(stateType);
        }

        return _lastPrimaryIsDoubleTap;
    }

    private void LogBodyOnce(CharacterBody body)
    {
        if (_loggedBody == body)
        {
            return;
        }

        _loggedBody = body;

        string vanilla = "none";
        GameObject crosshair = body.defaultCrosshairPrefab;
        if (crosshair && crosshair.TryGetComponent(out CrosshairController controller))
        {
            vanilla = $"{crosshair.name} maxSpreadAngle {controller.maxSpreadAngle:0.###}";
            if (controller.spriteSpreadPositions is { Length: > 0 } positions)
            {
                CrosshairController.SpritePosition first = positions[0];
                vanilla += $", sprite '{(first.target ? first.target.name : "?")}' moves {first.zeroPosition} -> {first.onePosition}";
            }
        }

        AnimationCurve curve = body.spreadBloomCurve;
        Plugin.Log.LogDebug(
            $"[Spread] Commando source active: primary '{_lastPrimarySkill?.skillName}', " +
            $"spread {curve.Evaluate(0f):0.###} deg at rest, {curve.Evaluate(1f):0.###} deg at full bloom, " +
            $"decay time {body.spreadBloomDecayTime:0.###} s (scales with attack speed: {body.spreadBloomDecayWithAttackSpeed}), " +
            $"vanilla crosshair: {vanilla}.");
    }
}
