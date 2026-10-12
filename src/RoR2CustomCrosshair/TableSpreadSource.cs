using System;
using System.Collections.Generic;
using EntityStates;
using RoR2;
using RoR2.Skills;

namespace RoR2CustomCrosshair;

/// <summary>Firing envelope of one attack state (live, or a configured instance for the next shot).</summary>
internal delegate SpreadSample AttackEnvelope(EntityState state, CharacterBody body);

/// <summary>An attack state type (matched with its subclasses) and how to read its envelope.</summary>
internal sealed class AttackRule
{
    internal readonly Type StateType;
    internal readonly AttackEnvelope Envelope;

    internal AttackRule(Type stateType, AttackEnvelope envelope)
    {
        StateType = stateType;
        Envelope = envelope;
    }

    /// <summary>The attack fires along the aim ray, homes, or is world-targeted: no angular spread.</summary>
    internal static AttackRule None(Type stateType, string context) =>
        new(stateType, (_, _) => SpreadSample.None(context));

    /// <summary>The attack's maximum deviation is the body's spreadBloomAngle.</summary>
    internal static AttackRule Bloom(Type stateType, string context) =>
        new(stateType, (_, body) => new SpreadSample(body.spreadBloomAngle, SpreadKind.Bloom, context));

    /// <summary>A constant envelope, in degrees (usually from configured static fields).</summary>
    internal static AttackRule Fixed(Type stateType, string context, Func<float> degrees) =>
        new(stateType, (_, _) => new SpreadSample(degrees(), SpreadKind.Fixed, context));
}

/// <summary>
/// Survivor-agnostic spread source driven by a small per-survivor table
/// (see <see cref="BaseGameSurvivorSpread"/>):
///
/// 1. If a watched state machine is running an attack state with a rule (or a
///    GenericBulletBaseState / GenericProjectileBaseState, read generically),
///    that active attack's envelope is shown.
/// 2. Otherwise the equipped primary's next shot: the primary skill's
///    activation state type, read the same way from a configured instance.
/// 3. A primary with no rule (melee, or an unknown replacement such as a
///    Heresy item) has no known ranged envelope: zero.
///
/// Rules only describe real firing math verified against the game build;
/// charge, readiness and placement states are never given spread they do
/// not have.
/// </summary>
internal sealed class TableSpreadSource : ISpreadSource
{
    private readonly string _survivor;
    private readonly string _bodyName;
    private readonly BodyStateMachine[] _machines;
    private readonly AttackRule[] _rules;

    private BodyIndex _bodyIndex = BodyIndex.None;

    // Rule (or null) per concrete state type, and activation state type per skill.
    private readonly Dictionary<Type, AttackRule?> _ruleByType = new();
    private readonly Dictionary<SkillDef, Type?> _stateTypeBySkill = new();

    // One line per attack context change (temporary B5 diagnostics).
    private CharacterBody? _loggedBody;
    private string? _loggedContext;

    internal TableSpreadSource(string survivor, string bodyName, string[] machineNames, params AttackRule[] rules)
    {
        _survivor = survivor;
        _bodyName = bodyName;
        _rules = rules;
        _machines = new BodyStateMachine[machineNames.Length];
        for (int i = 0; i < machineNames.Length; i++)
        {
            _machines[i] = new BodyStateMachine(machineNames[i]);
        }
    }

    public bool LogSamples => true;

    public bool CanHandle(CharacterBody body)
    {
        if (_bodyIndex == BodyIndex.None)
        {
            _bodyIndex = BodyCatalog.FindBodyIndex(_bodyName);
            if (_bodyIndex == BodyIndex.None)
            {
                return false;
            }
        }

        return body.bodyIndex == _bodyIndex;
    }

    public bool TrySample(CharacterBody body, out SpreadSample sample)
    {
        foreach (BodyStateMachine machine in _machines)
        {
            EntityState? state = machine.StateOf(body);
            if (state != null && Evaluate(state, body, out sample))
            {
                Log(body, sample, "active");
                return true;
            }
        }

        sample = PrimaryNextShot(body);
        Log(body, sample, "primary");
        return true;
    }

    private SpreadSample PrimaryNextShot(CharacterBody body)
    {
        GenericSkill? primary = body.skillLocator ? body.skillLocator.primary : null;
        SkillDef? skill = primary ? primary!.skillDef : null;
        if (skill == null)
        {
            return SpreadSample.None("no primary");
        }

        if (!_stateTypeBySkill.TryGetValue(skill, out Type? stateType))
        {
            stateType = skill.activationState.stateType;
            _stateTypeBySkill[skill] = stateType;
        }

        EntityState? configured = stateType != null ? AttackSpread.Configured<EntityState>(stateType) : null;
        if (configured != null && Evaluate(configured, body, out SpreadSample next))
        {
            return next;
        }

        return SpreadSample.None($"{skill.skillName} (no ranged spread)");
    }

    private bool Evaluate(EntityState state, CharacterBody body, out SpreadSample sample)
    {
        Type type = state.GetType();
        if (!_ruleByType.TryGetValue(type, out AttackRule? rule))
        {
            rule = null;
            foreach (AttackRule candidate in _rules)
            {
                if (candidate.StateType.IsAssignableFrom(type))
                {
                    rule = candidate;
                    break;
                }
            }

            _ruleByType[type] = rule;
        }

        if (rule != null)
        {
            sample = rule.Envelope(state, body);
            return true;
        }

        return AttackSpread.TryFromState(state, body.spreadBloomAngle, out sample);
    }

    private void Log(CharacterBody body, in SpreadSample sample, string source)
    {
        if (_loggedBody == body && _loggedContext == sample.Context)
        {
            return;
        }

        _loggedBody = body;
        _loggedContext = sample.Context;
        Plugin.Log.LogDebug(
            $"[Spread] {_survivor} / {sample.Context} ({source}): {sample.Kind} envelope {sample.Degrees:0.###} deg; " +
            $"body bloom curve {Curve(body)}.");
    }

    private static string Curve(CharacterBody body) => body.spreadBloomCurve != null
        ? $"{body.spreadBloomCurve.Evaluate(0f):0.###}..{body.spreadBloomCurve.Evaluate(1f):0.###} deg"
        : "none";
}
