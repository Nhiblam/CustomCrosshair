using System;
using EntityStates;
using RoR2;

namespace RoR2CustomCrosshair;

/// <summary>
/// Spread of a weapon built on <see cref="GenericBulletBaseState"/>.
///
/// GenerateBulletAttack fires with maxSpread = configured maxSpread +
/// spreadBloomAngle; BulletAttack deviates each bullet by up to that many
/// degrees, scaling the yaw part by spreadYawScale and the pitch part by
/// spreadPitchScale. The outer envelope is therefore
/// (maxSpread + bloom) * max(yawScale, pitchScale) degrees.
///
/// The configured field values (EntityStateConfiguration assets) are read
/// from a detached state instance: the EntityState constructor only applies
/// those values; the instance is never entered or attached to a body.
/// </summary>
internal sealed class GenericBulletSpread
{
    internal readonly int BulletCount;
    internal readonly float MaxSpread;
    internal readonly float YawScale;
    internal readonly float PitchScale;

    private GenericBulletSpread(GenericBulletBaseState state)
    {
        BulletCount = state.bulletCount;
        MaxSpread = state.maxSpread;
        YawScale = state.spreadYawScale;
        PitchScale = state.spreadPitchScale;
    }

    /// <summary>Configured state instance, or null if the type is not a configured bullet state.</summary>
    internal static T? ReadState<T>(Type stateType) where T : GenericBulletBaseState
    {
        try
        {
            return EntityStateCatalog.InstantiateState(stateType) as T;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Could not read configuration of {stateType.FullName}: {exception.Message}");
            return null;
        }
    }

    internal static GenericBulletSpread From(GenericBulletBaseState state) => new(state);

    /// <summary>Largest yaw (horizontal) deviation of the random spread, in degrees.</summary>
    internal float HorizontalDegrees(float bloomDegrees) => (MaxSpread + bloomDegrees) * YawScale;

    /// <summary>Largest pitch (vertical) deviation of the random spread, in degrees.</summary>
    internal float VerticalDegrees(float bloomDegrees) => (MaxSpread + bloomDegrees) * PitchScale;

    internal string Describe() =>
        $"{BulletCount} bullet(s), maxSpread {MaxSpread:0.###}, yaw x{YawScale:0.###}, pitch x{PitchScale:0.###}";
}
