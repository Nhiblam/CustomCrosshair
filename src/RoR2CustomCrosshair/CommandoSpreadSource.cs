using System;
using EntityStates;
using EntityStates.Commando.CommandoWeapon;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Commando. The envelope follows the attack on the "Weapon" state machine
/// (every Commando attack runs there):
///
/// Suppressive Fire (FireBarrage): BulletAttack with the configured fixed
/// min/maxSpread; bloom is only added afterwards.
/// Sabotage: projectiles fanned in yaw by yawSpread, plus ApplySpread(maxSpread).
/// Phase Blast (FireShotgunBlast, GenericBulletBaseState): maxSpread + bloom.
/// Phase Round, Frag Grenade (GenericProjectileBaseState): fixed spread (zero
/// in the shipped data).
/// Otherwise (idle, Double Tap, utilities): Double Tap's next shot, whose
/// BulletAttack uses maxSpread = spreadBloomAngle. Abilities that reset bloom
/// (Tactical Slide sets it to 0, Phase Round adds -1) therefore shrink it.
/// </summary>
internal sealed class CommandoSpreadSource : ISpreadSource
{
    private const string CommandoBodyName = "CommandoBody";

    private readonly BodyStateMachine _weapon = new("Weapon");

    private BodyIndex _commandoBodyIndex = BodyIndex.None;

    // Avoid resolving the activation state type every frame.
    private SkillDef? _lastPrimarySkill;
    private bool _lastPrimaryIsDoubleTap;

    // One summary line per body.
    private CharacterBody? _loggedBody;

    // Validated in-game (B4): no per-second samples.
    public bool LogSamples => false;

    public bool CanHandle(CharacterBody body) => IsCommando(body);

    public bool TrySample(CharacterBody body, out SpreadSample sample)
    {
        sample = default;
        if (!IsCommando(body) || body.spreadBloomCurve == null)
        {
            return false;
        }

        LogBodyOnce(body);
        float bloom = body.spreadBloomAngle;

        switch (_weapon.StateOf(body))
        {
            case FireBarrage:
                // Static fields configured by EntityStateConfiguration; BulletAttack default unit scales.
                sample = new SpreadSample(FireBarrage.maxSpread, SpreadKind.Fixed, "Suppressive Fire");
                return true;

            case Sabotage sabotage:
                sample = new SpreadSample(SabotageEnvelope(sabotage), SpreadKind.Fixed, sabotage.GetType().Name);
                return true;

            case EntityState state when AttackSpread.TryFromState(state, bloom, out sample):
                return true;
        }

        if (!PrimaryIsDoubleTap(body))
        {
            return false;
        }

        // FirePistol2: minSpread 0, maxSpread = spreadBloomAngle, unit scales.
        sample = new SpreadSample(bloom, SpreadKind.Bloom, "Double Tap");
        return true;
    }

    /// <summary>
    /// Sabotage.FireProjectile rotates projectile n (0-based) in yaw by
    /// (count / 2 - n) * yawSpread - yawSpread / 2, then applies
    /// ApplySpread(minSpread, maxSpread) at unit scale.
    /// </summary>
    private static float SabotageEnvelope(Sabotage sabotage)
    {
        float widest = 0f;
        for (int n = 0; n < Mathf.Max(1, sabotage.projectileCount); n++)
        {
            float angle = (sabotage.projectileCount / 2f - n) * sabotage.yawSpread - sabotage.yawSpread / 2f;
            widest = Mathf.Max(widest, Mathf.Abs(angle));
        }

        return widest + sabotage.maxSpread;
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
        }

        AnimationCurve curve = body.spreadBloomCurve;
        Plugin.Log.LogDebug(
            $"[Spread] Commando source active: Double Tap spread {curve.Evaluate(0f):0.###} deg with no bloom, " +
            $"{curve.Evaluate(1f):0.###} deg at full bloom, decay {body.spreadBloomDecayTime:0.###} s; " +
            $"Suppressive Fire fixed {FireBarrage.maxSpread:0.###} deg; vanilla crosshair: {vanilla}.");
    }
}
