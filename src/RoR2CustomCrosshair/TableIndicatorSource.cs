using System;
using System.Collections.Generic;
using EntityStates;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>Reads one progress value; false when it does not apply right now.</summary>
internal delegate bool ProgressReader(CharacterBody body, out float progress, out bool ready);

/// <summary>Reads one count value; false when it does not apply right now.</summary>
internal delegate bool CountReader(CharacterBody body, out int current, out int maximum);

/// <summary>A labelled progress reader (the label is only used for diagnostics).</summary>
internal sealed class ProgressRule
{
    internal readonly string Label;
    internal readonly ProgressReader Read;

    internal ProgressRule(string label, ProgressReader read)
    {
        Label = label;
        Read = read;
    }
}

/// <summary>A labelled count reader (the label is only used for diagnostics).</summary>
internal sealed class CountRule
{
    internal readonly string Label;
    internal readonly CountReader Read;

    internal CountRule(string label, CountReader read)
    {
        Label = label;
        Read = read;
    }
}

/// <summary>
/// Survivor-agnostic indicator source driven by a small per-survivor table
/// (see <see cref="SurvivorIndicators"/>). Each frame the progress rules,
/// then the count rules, are read in table order; the first applicable
/// rules fill channels 1 and 2 of each type. Read-only.
///
/// One instance per HUD: rules may keep per-body timing state (EntityState
/// ages are not public, so charge timers start when a state is first seen).
/// </summary>
internal sealed class TableIndicatorSource : IIndicatorSource
{
    private readonly string _survivor;
    private readonly string _bodyName;
    private readonly ProgressRule[] _progress;
    private readonly CountRule[] _counts;

    private BodyIndex _bodyIndex = BodyIndex.None;

    // Rate-limited state-change diagnostics, per channel.
    private const float LogInterval = 0.5f;
    private readonly string?[] _loggedProgressRule = new string?[IndicatorFrame.Channels];
    private readonly bool[] _loggedReady = new bool[IndicatorFrame.Channels];
    private readonly string?[] _loggedCountRule = new string?[IndicatorFrame.Channels];
    private readonly CountIndicatorState[] _loggedCount = new CountIndicatorState[IndicatorFrame.Channels];
    private readonly float[] _nextCountLog = new float[IndicatorFrame.Channels];

    internal TableIndicatorSource(string survivor, string bodyName, ProgressRule[] progress, CountRule[] counts)
    {
        _survivor = survivor;
        _bodyName = bodyName;
        _progress = progress;
        _counts = counts;
    }

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

    public void Sample(CharacterBody body, IndicatorFrame frame)
    {
        int channel = 0;
        foreach (ProgressRule rule in _progress)
        {
            if (channel >= IndicatorFrame.Channels)
            {
                break;
            }

            if (rule.Read(body, out float progress, out bool ready))
            {
                var state = new ProgressIndicatorState(progress, ready);
                frame.SetProgress(channel, state);
                LogProgress(channel, rule.Label, state);
                channel++;
            }
        }

        for (; channel < IndicatorFrame.Channels; channel++)
        {
            LogProgress(channel, null, default);
        }

        channel = 0;
        foreach (CountRule rule in _counts)
        {
            if (channel >= IndicatorFrame.Channels)
            {
                break;
            }

            if (rule.Read(body, out int current, out int maximum))
            {
                var state = new CountIndicatorState(current, maximum);
                frame.SetCount(channel, state);
                LogCount(channel, rule.Label, state);
                channel++;
            }
        }

        for (; channel < IndicatorFrame.Channels; channel++)
        {
            LogCount(channel, null, default);
        }
    }

    /// <summary>Logs a channel's rule starting/stopping and ready transitions.</summary>
    private void LogProgress(int channel, string? rule, in ProgressIndicatorState state)
    {
        if (rule == _loggedProgressRule[channel] && (rule == null || state.Ready == _loggedReady[channel]))
        {
            return;
        }

        _loggedProgressRule[channel] = rule;
        _loggedReady[channel] = state.Ready;
        Plugin.Log.LogDebug(rule == null
            ? $"[Indicator] {_survivor} progress {channel + 1}: inactive"
            : $"[Indicator] {_survivor} progress {channel + 1}: {rule}, progress {state.Progress:0.00}, ready {state.Ready}");
    }

    /// <summary>Logs a channel's rule starting/stopping immediately, value changes at most twice a second.</summary>
    private void LogCount(int channel, string? rule, in CountIndicatorState state)
    {
        bool ruleChanged = rule != _loggedCountRule[channel];
        if (!ruleChanged && (rule == null || state.Equals(_loggedCount[channel]) || Time.unscaledTime < _nextCountLog[channel]))
        {
            return;
        }

        _loggedCountRule[channel] = rule;
        _loggedCount[channel] = state;
        _nextCountLog[channel] = Time.unscaledTime + LogInterval;
        Plugin.Log.LogDebug(rule == null
            ? $"[Indicator] {_survivor} count {channel + 1}: inactive"
            : $"[Indicator] {_survivor} count {channel + 1}: {rule}, {state.Current} / {state.Maximum}");
    }
}

/// <summary>
/// Reusable, survivor-agnostic indicator readers built only from public
/// game state (skill stocks, skill defs, state machine states).
/// </summary>
internal static class IndicatorReaders
{
    /// <summary>
    /// The live stock of the skill in <paramref name="slot"/> (current /
    /// maxStock), when its skill's activation state is one of
    /// <paramref name="activationStates"/> (any skill when none are given).
    /// </summary>
    internal static CountReader SkillStock(SkillSlot slot, params Type[] activationStates)
    {
        var matches = new SkillMatcher(activationStates);
        return (CharacterBody body, out int current, out int maximum) =>
        {
            GenericSkill? skill = Skill(body, slot);
            if (skill != null && matches.Accepts(skill.skillDef))
            {
                current = skill.stock;
                maximum = skill.maxStock;
                return true;
            }

            current = maximum = 0;
            return false;
        };
    }

    /// <summary><paramref name="inner"/>, but only while <paramref name="machine"/> runs a <paramref name="stateType"/>.</summary>
    internal static CountReader WhileState(BodyStateMachine machine, Type stateType, CountReader inner)
    {
        return (CharacterBody body, out int current, out int maximum) =>
        {
            EntityState? state = machine.StateOf(body);
            if (state != null && stateType.IsInstanceOfType(state))
            {
                return inner(body, out current, out maximum);
            }

            current = maximum = 0;
            return false;
        };
    }

    /// <summary>
    /// Charge progress of a <typeparamref name="TState"/> running on
    /// <paramref name="machine"/>: time since the state instance was first
    /// seen over <paramref name="duration"/>(state, attack speed). Ready at
    /// full charge. EntityState ages are not public, so the state's own
    /// clock is mirrored from its first observation (at most a frame late).
    /// </summary>
    internal static ProgressReader Charge<TState>(BodyStateMachine machine, Func<TState, float, float> duration)
        where TState : EntityState
    {
        var timer = new StateTimer();
        return (CharacterBody body, out float progress, out bool ready) =>
        {
            if (machine.StateOf(body) is TState state)
            {
                if (timer.Observe(state, body))
                {
                    timer.Duration = Mathf.Max(duration(state, timer.AttackSpeed), 0.01f);
                }

                progress = timer.Elapsed / timer.Duration;
                ready = progress >= 1f;
                return true;
            }

            timer.Reset();
            progress = 0f;
            ready = false;
            return false;
        };
    }

    /// <summary>Full and ready while <paramref name="machine"/> runs a <paramref name="stateType"/> (a charge held at full).</summary>
    internal static ProgressReader Holding(BodyStateMachine machine, Type stateType)
    {
        return (CharacterBody body, out float progress, out bool ready) =>
        {
            EntityState? state = machine.StateOf(body);
            ready = state != null && stateType.IsInstanceOfType(state);
            progress = ready ? 1f : 0f;
            return ready;
        };
    }

    internal static GenericSkill? Skill(CharacterBody body, SkillSlot slot)
    {
        SkillLocator? skills = body.skillLocator;
        if (!skills)
        {
            return null;
        }

        GenericSkill? skill = skills!.GetSkill(slot);
        return skill ? skill : null;
    }

    /// <summary>Matches skill defs by activation state type (cached per skill def).</summary>
    internal sealed class SkillMatcher
    {
        private readonly Type[] _states;
        private readonly Dictionary<SkillDef, bool> _accepted = new();

        internal SkillMatcher(Type[] states)
        {
            _states = states;
        }

        internal bool Accepts(SkillDef? skill)
        {
            if (skill == null)
            {
                return false;
            }

            if (_states.Length == 0)
            {
                return true;
            }

            if (!_accepted.TryGetValue(skill, out bool accepted))
            {
                Type? type = skill.activationState.stateType;
                accepted = false;
                foreach (Type state in _states)
                {
                    if (type != null && state.IsAssignableFrom(type))
                    {
                        accepted = true;
                        break;
                    }
                }

                _accepted[skill] = accepted;
            }

            return accepted;
        }
    }

    /// <summary>Mirrors the age of one observed state instance.</summary>
    internal sealed class StateTimer
    {
        private EntityState? _state;
        private CharacterBody? _body;
        private float _start;

        internal float Duration = 1f;
        internal float AttackSpeed = 1f;

        internal float Elapsed => Time.time - _start;

        /// <summary>True when <paramref name="state"/> is newly observed (the timer restarted).</summary>
        internal bool Observe(EntityState state, CharacterBody body)
        {
            if (state == _state && body == _body)
            {
                return false;
            }

            _state = state;
            _body = body;
            _start = Time.time;
            AttackSpeed = Mathf.Max(body.attackSpeed, 0.01f);
            return true;
        }

        internal void Reset()
        {
            _state = null;
            _body = null;
        }
    }
}
