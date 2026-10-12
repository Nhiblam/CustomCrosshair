using System;
using System.Collections.Generic;
using EntityStates;
using EntityStates.Toolbot;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// MUL-T indicator data (Phase C1). Read-only.
///
/// Channel 0 is the stance weapon (skillLocator.primary: ToolbotBodyPrimary1
/// in stance A, ToolbotBodyPrimary2 in stance B, null during the Retool
/// swap). Channel 1 is the Power Mode offhand (skillLocator.secondary
/// overridden with the second weapon, run on the "Weapon2" machine). Each
/// slot has its own GenericSkill, state machine and stock, so two copies of
/// the same weapon stay independent.
///
/// Rebar Puncher (FireSpear): no cooldown and stock 1 refilled instantly; its
/// interruptPriority (Any) cannot interrupt FireSpear / CooldownSpear, so the
/// weapon is unavailable exactly while those states run. Ready =
/// GenericSkill.CanExecute() (the game's own check). Progress = time through
/// FireSpear (baseDuration / attack speed) + CooldownSpear (baseDuration /
/// attack speed), timed from the observed state transitions because
/// EntityState ages are not public.
///
/// Scrap Launcher (FireGrenadeLauncher): live GenericSkill.stock / maxStock.
/// </summary>
internal sealed class ToolbotIndicatorSource : IIndicatorSource
{
    private const string ToolbotBodyName = "ToolbotBody";

    private enum WeaponKind
    {
        Other,
        Rebar,
        Scrap
    }

    private sealed class RebarCycle
    {
        private EntityState? _tracked;
        private float _phaseStart;
        private float _fireDuration;
        private float _cooldownDuration;

        internal ProgressIndicatorState Sample(GenericSkill skill, CharacterBody body)
        {
            bool ready = skill.CanExecute();
            EntityState? state = skill.stateMachine ? skill.stateMachine.state : null;
            float now = Time.time;
            float speed = Mathf.Max(body.attackSpeed, 0.01f);
            float progress;

            switch (state)
            {
                case FireSpear fire:
                    if (state != _tracked)
                    {
                        _tracked = state;
                        _phaseStart = now;
                        _fireDuration = fire.baseDuration / speed;
                        _cooldownDuration = CooldownSpear.baseDuration / speed;
                    }

                    progress = (now - _phaseStart) / Total();
                    break;

                case CooldownSpear:
                    if (state != _tracked)
                    {
                        _tracked = state;
                        _phaseStart = now;
                        _cooldownDuration = CooldownSpear.baseDuration / speed;
                        if (_fireDuration <= 0f)
                        {
                            FireSpear? config = AttackSpread.Configured<FireSpear>(typeof(FireSpear));
                            _fireDuration = (config != null ? config.baseDuration : 1f) / speed;
                        }
                    }

                    progress = (_fireDuration + now - _phaseStart) / Total();
                    break;

                default:
                    _tracked = null;
                    progress = ready ? 1f : 0f;
                    break;
            }

            return new ProgressIndicatorState(progress, ready);
        }

        private float Total() => Mathf.Max(_fireDuration + _cooldownDuration, 0.01f);
    }

    private BodyIndex _toolbotBodyIndex = BodyIndex.None;
    private readonly Dictionary<SkillDef, WeaponKind> _kinds = new();
    private readonly RebarCycle[] _rebar = { new(), new() };

    // Diagnostics: one line per weapon configuration change (C1 per-shot logs removed).
    private readonly WeaponKind[] _loggedKinds = { WeaponKind.Other, WeaponKind.Other };
    private bool _loggedPowerMode;
    private bool _loggedAny;

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

    public void Sample(CharacterBody body, IndicatorFrame frame)
    {
        SkillLocator? skills = body.skillLocator;
        if (!skills)
        {
            return;
        }

        GenericSkill? stance = skills!.primary && skills.primary.skillDef is ToolbotWeaponSkillDef ? skills.primary : null;
        GenericSkill? offhand = skills.secondary && skills.secondary.skillDef is ToolbotWeaponSkillDef ? skills.secondary : null;

        WeaponKind stanceKind = KindOf(stance);
        WeaponKind offhandKind = KindOf(offhand);
        LogWeapons(stanceKind, offhandKind, offhand != null);

        SampleSlot(0, stance, stanceKind, body, frame);
        SampleSlot(1, offhand, offhandKind, body, frame);
    }

    private void SampleSlot(int channel, GenericSkill? skill, WeaponKind kind, CharacterBody body, IndicatorFrame frame)
    {
        switch (kind)
        {
            case WeaponKind.Rebar:
                frame.SetProgress(channel, _rebar[channel].Sample(skill!, body));
                break;

            case WeaponKind.Scrap:
                frame.SetCount(channel, new CountIndicatorState(skill!.stock, skill.maxStock));
                break;
        }
    }

    private WeaponKind KindOf(GenericSkill? skill)
    {
        SkillDef? def = skill ? skill!.skillDef : null;
        if (def == null)
        {
            return WeaponKind.Other;
        }

        if (!_kinds.TryGetValue(def, out WeaponKind kind))
        {
            Type? type = def.activationState.stateType;
            kind = type == null ? WeaponKind.Other
                : typeof(FireSpear).IsAssignableFrom(type) ? WeaponKind.Rebar
                : typeof(FireGrenadeLauncher).IsAssignableFrom(type) ? WeaponKind.Scrap
                : WeaponKind.Other;
            _kinds[def] = kind;
        }

        return kind;
    }

    private void LogWeapons(WeaponKind stance, WeaponKind offhand, bool powerMode)
    {
        if (_loggedAny && _loggedKinds[0] == stance && _loggedKinds[1] == offhand && _loggedPowerMode == powerMode)
        {
            return;
        }

        _loggedAny = true;
        _loggedKinds[0] = stance;
        _loggedKinds[1] = offhand;
        _loggedPowerMode = powerMode;
        Plugin.Log.LogDebug($"[Indicator] MUL-T indicators: slot1={stance}, slot2={(powerMode ? offhand.ToString() : "none")}, PowerMode={powerMode}");
    }
}
