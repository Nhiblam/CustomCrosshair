using RoR2;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Actual weapon spread for the current shot, as a cone half-angle in
/// degrees around the aim direction (BulletAttack deviates each bullet by
/// Random.Range(minSpread, maxSpread) degrees from its aim vector).
/// </summary>
internal readonly struct SpreadSample
{
    /// <summary>Half-angle the next shot may deviate by, in degrees.</summary>
    internal readonly float CurrentDegrees;

    /// <summary>Half-angle with no accumulated bloom, in degrees.</summary>
    internal readonly float RestingDegrees;

    /// <summary>The body's spreadBloomAngle behind this sample (diagnostics only).</summary>
    internal readonly float BloomDegrees;

    internal SpreadSample(float currentDegrees, float restingDegrees, float bloomDegrees)
    {
        CurrentDegrees = currentDegrees;
        RestingDegrees = restingDegrees;
        BloomDegrees = bloomDegrees;
    }
}

/// <summary>
/// Observes a body's real weapon spread. Implementations must only read
/// gameplay state, never change it.
/// </summary>
internal interface ISpreadSource
{
    /// <summary>True when this source is for the body's survivor (cheap identity check).</summary>
    bool CanHandle(CharacterBody body);

    /// <summary>
    /// Returns true and a sample when this source understands the body's
    /// current ordinary weapon (runtime loadout, including skill overrides);
    /// false otherwise. Only called after <see cref="CanHandle"/> returned true.
    /// </summary>
    bool TrySample(CharacterBody body, out SpreadSample sample);

    /// <summary>Temporary per-second sample diagnostics for survivors still being validated.</summary>
    bool LogSamples { get; }
}

/// <summary>
/// Picks at most one spread source for a body. The survivor match is cached
/// per body, so a new body (respawn, stage change, body swap) re-resolves;
/// the loadout is re-checked by the source on every sample.
/// </summary>
internal sealed class SpreadSourceResolver
{
    private readonly ISpreadSource[] _sources;

    // Unity's == treats a destroyed body as null, so a stale reference
    // never matches a live body.
    private CharacterBody? _body;
    private ISpreadSource? _source;

    internal SpreadSourceResolver(params ISpreadSource[] sources)
    {
        _sources = sources;
    }

    internal bool TrySample(CharacterBody body, out SpreadSample sample, out ISpreadSource? source)
    {
        if (body != _body)
        {
            _body = body;
            _source = null;
            foreach (ISpreadSource candidate in _sources)
            {
                if (candidate.CanHandle(body))
                {
                    _source = candidate;
                    break;
                }
            }
        }

        source = _source;
        sample = default;
        return _source != null && _source.TrySample(body, out sample);
    }
}

internal static class SpreadProjection
{
    /// <summary>
    /// Screen-space radius, in pixels, of a cone with the given half-angle
    /// around the view axis: tan(halfAngle) / tan(verticalFov / 2) * (pixelHeight / 2).
    /// Unity's Camera.fieldOfView is the vertical FOV, and with square pixels
    /// the same pixels-per-tangent applies horizontally, so the cone projects
    /// to a circle regardless of aspect ratio.
    /// </summary>
    internal static float HalfAngleToPixels(float halfAngleDegrees, Camera camera)
    {
        float halfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        if (halfFov <= 0f || halfAngleDegrees <= 0f)
        {
            return 0f;
        }

        float angle = Mathf.Min(halfAngleDegrees, 89f) * Mathf.Deg2Rad;
        return Mathf.Tan(angle) / Mathf.Tan(halfFov) * (camera.pixelHeight * 0.5f);
    }
}
