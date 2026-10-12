using System;
using System.Collections.Generic;
using EntityStates;
using RoR2;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Survivor-agnostic readers for the firing envelope of common attack
/// state types. Read-only: states are inspected, never entered or changed.
/// </summary>
internal static class AttackSpread
{
    // Configured (EntityStateConfiguration) instances per state type, read once.
    private static readonly Dictionary<Type, EntityState?> ConfiguredStates = new();

    /// <summary>
    /// GenericBulletBaseState fires BulletAttack with maxSpread = configured
    /// maxSpread + spreadBloomAngle and scales the yaw/pitch parts, so the
    /// widest deviation is (maxSpread + bloom) * max(yawScale, pitchScale).
    /// </summary>
    internal static SpreadSample Bullet(GenericBulletBaseState state, float bloomDegrees, string context)
    {
        float scale = Mathf.Max(state.spreadYawScale, state.spreadPitchScale);
        return new SpreadSample((state.maxSpread + bloomDegrees) * scale, SpreadKind.Bloom, context);
    }

    /// <summary>
    /// GenericProjectileBaseState aims with Util.ApplySpread(min, max) at
    /// unit scale; bloom is only added afterwards, never read. Fixed spread.
    /// (projectilePitchBonus shifts the aim, it does not widen it.)
    /// </summary>
    internal static SpreadSample Projectile(GenericProjectileBaseState state, string context)
    {
        return new SpreadSample(state.maxSpread, SpreadKind.Fixed, context);
    }

    /// <summary>Envelope of a live state of a common base type; false for other types.</summary>
    internal static bool TryFromState(EntityState state, float bloomDegrees, out SpreadSample sample)
    {
        switch (state)
        {
            case GenericBulletBaseState bullet:
                sample = Bullet(bullet, bloomDegrees, state.GetType().Name);
                return true;
            case GenericProjectileBaseState projectile:
                sample = Projectile(projectile, state.GetType().Name);
                return true;
            default:
                sample = default;
                return false;
        }
    }

    /// <summary>
    /// Envelope of a skill's next activation, from a detached configured
    /// instance of its state type (the EntityState constructor only applies
    /// configured field values; the instance is never entered).
    /// </summary>
    internal static bool TryFromConfiguredState(Type stateType, float bloomDegrees, out SpreadSample sample)
    {
        EntityState? state = Configured<EntityState>(stateType);
        if (state == null)
        {
            sample = default;
            return false;
        }

        return TryFromState(state, bloomDegrees, out sample);
    }

    internal static T? Configured<T>(Type stateType) where T : EntityState
    {
        if (!ConfiguredStates.TryGetValue(stateType, out EntityState? state))
        {
            try
            {
                state = EntityStateCatalog.InstantiateState(stateType);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Could not read configuration of {stateType.FullName}: {exception.Message}");
                state = null;
            }

            ConfiguredStates[stateType] = state;
        }

        return state as T;
    }
}

/// <summary>Cached lookup of one named EntityStateMachine on the current body.</summary>
internal sealed class BodyStateMachine
{
    private readonly string _name;
    private CharacterBody? _body;
    private EntityStateMachine? _machine;

    internal BodyStateMachine(string name)
    {
        _name = name;
    }

    /// <summary>The machine's current state, or null.</summary>
    internal EntityState? StateOf(CharacterBody body)
    {
        if (body != _body)
        {
            _body = body;
            _machine = EntityStateMachine.FindByCustomName(body.gameObject, _name);
        }

        return _machine ? _machine!.state : null;
    }
}
