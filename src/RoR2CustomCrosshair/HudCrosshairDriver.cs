using RoR2;
using RoR2.UI;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Lives on each HUD and substitutes the custom crosshair through that HUD's
/// own <see cref="CrosshairManager.pickCrosshairGlobal"/> event.
///
/// The body-level CrosshairUtils.RequestOverrideForBody stack is deliberately
/// not used: it also applies and resets the body's spread bloom values, which
/// feed real bullet spread, so a persistent cosmetic request could alter
/// gameplay. Substituting at the manager is purely local presentation, is
/// re-evaluated every frame, and leaves no request to leak across deaths,
/// respawns or stage changes.
///
/// Which crosshairs are replaced is <see cref="CrosshairDisplayPolicy"/>'s
/// decision; how far Dynamic layers move comes from the spread sources.
/// </summary>
internal sealed class HudCrosshairDriver : MonoBehaviour
{
    // Real weapon spread observers, shared by every HUD (read-only, cached per skill).
    private static readonly ISpreadSource[] SpreadSources = CreateSpreadSources();

    private static ISpreadSource[] CreateSpreadSources()
    {
        var sources = new System.Collections.Generic.List<ISpreadSource>
        {
            new CommandoSpreadSource(),
            new BanditSpreadSource(),
            new ToolbotSpreadSource()
        };
        sources.AddRange(BaseGameSurvivorSpread.Create());
        sources.AddRange(DlcSurvivorSpread.Create());
        return sources.ToArray();
    }

    // Offsets are quantized so the view only re-applies on visible changes.
    private const float OffsetStepPixels = 0.25f;

    // Temporary diagnostics for sources still being validated.
    private const float SpreadLogInterval = 1f;
    private float _nextSpreadLogTime;
    private string? _lastLoggedContext;
    private float _lastLoggedPixels = -1f;

    private readonly SpreadSourceResolver _spreadResolver = new(SpreadSources);

    // Gameplay indicator observers, one set per HUD (they keep per-viewer
    // timing state, so split-screen players never share them).
    private readonly IndicatorSourceResolver _indicatorResolver = new(SurvivorIndicators.Create());
    private readonly IndicatorFrame _indicators = new();
    private int _indicatorFrameNumber = -1;
    private CharacterBody? _indicatorBody;

    private CrosshairManager? _crosshairManager;
    private bool _replacing;

    internal static void Attach(HUD hud)
    {
        if (!hud.GetComponent<HudCrosshairDriver>())
        {
            hud.gameObject.AddComponent<HudCrosshairDriver>();
        }
    }

    private void Awake()
    {
        _crosshairManager = GetComponent<CrosshairManager>();
        if (!_crosshairManager)
        {
            Plugin.Log.LogWarning($"HUD '{name}' has no CrosshairManager; custom crosshair unavailable.");
        }
    }

    private void OnEnable()
    {
        if (_crosshairManager)
        {
            _crosshairManager!.pickCrosshairGlobal += OnPickCrosshair;
        }
    }

    private void OnDisable()
    {
        if (_crosshairManager)
        {
            _crosshairManager!.pickCrosshairGlobal -= OnPickCrosshair;
        }

        SetReplacing(false, null);
    }

    private void OnPickCrosshair(CrosshairManager manager, ref GameObject crosshairPrefab)
    {
        CharacterBody? body = GetOwnLiveBody(manager);
        GameObject? template = body != null && CrosshairConfig.Enabled.Value
            ? CrosshairDisplayPolicy.ChooseTemplate(body, crosshairPrefab, SampleIndicators(body))
            : null;

        if (template != null)
        {
            crosshairPrefab = template;
        }

        SetReplacing(template != null, body);
    }

    /// <summary>
    /// Dynamic offsets for the custom crosshair on this HUD: the radius, in
    /// screen pixels, of the actual current firing envelope (inherent, fixed,
    /// bloom and ability spread). Zero unless the custom crosshair is showing for the
    /// local user's own live body and a spread source understands that
    /// body's ordinary weapon. Read-only with respect to gameplay.
    /// </summary>
    internal DynamicOffsets GetDynamicOffsets()
    {
        if (!_replacing || !_crosshairManager)
        {
            return default;
        }

        CameraRigController rig = _crosshairManager!.cameraRigController;
        CharacterBody? body = GetOwnLiveBody(_crosshairManager);
        Camera? camera = rig ? rig.sceneCam : null;
        if (body == null || camera == null ||
            !_spreadResolver.TrySample(body, out SpreadSample sample, out ISpreadSource? source))
        {
            return default;
        }

        // Dynamic = configured geometry + the absolute projected envelope.
        float projected = SpreadProjection.HalfAngleToPixels(sample.Degrees, camera);
        float pixels = Mathf.Round(projected / OffsetStepPixels) * OffsetStepPixels;

        if (source!.LogSamples)
        {
            LogSpreadSample(sample, camera, pixels);
        }

        return new DynamicOffsets(pixels, pixels, pixels);
    }

    /// <summary>
    /// Indicator values for the custom crosshair on this HUD, sampled at most
    /// once per frame into a reused frame. Empty unless the custom crosshair
    /// is showing for the local user's own live body. Read-only with respect
    /// to gameplay.
    /// </summary>
    internal IndicatorFrame GetIndicators()
    {
        CharacterBody? body = _replacing && _crosshairManager ? GetOwnLiveBody(_crosshairManager!) : null;
        if (body == null)
        {
            _indicators.Clear();
            _indicatorBody = null;
            _indicatorFrameNumber = Time.frameCount;
            return _indicators;
        }

        return SampleIndicators(body);
    }

    /// <summary>
    /// <paramref name="body"/>'s indicator values, sampled at most once per
    /// frame (the template choice and the view share one sample).
    /// </summary>
    private IndicatorFrame SampleIndicators(CharacterBody body)
    {
        if (Time.frameCount != _indicatorFrameNumber || body != _indicatorBody)
        {
            _indicatorFrameNumber = Time.frameCount;
            _indicatorBody = body;
            _indicatorResolver.Sample(body, _indicators);
        }

        return _indicators;
    }

    /// <summary>
    /// Temporary B4 diagnostics: logs immediately when the attack context
    /// changes, otherwise at most once per second while the value changes.
    /// </summary>
    private void LogSpreadSample(in SpreadSample sample, Camera camera, float pixels)
    {
        bool contextChanged = sample.Context != _lastLoggedContext;
        bool valueChanged = Mathf.Abs(pixels - _lastLoggedPixels) >= OffsetStepPixels;
        if (!contextChanged && (!valueChanged || Time.unscaledTime < _nextSpreadLogTime))
        {
            return;
        }

        _lastLoggedContext = sample.Context;
        _lastLoggedPixels = pixels;
        _nextSpreadLogTime = Time.unscaledTime + SpreadLogInterval;
        Plugin.Log.LogDebug(
            $"[Spread] {sample.Context}: {sample.Kind} envelope {sample.Degrees:0.###} deg, " +
            $"vertical FOV {camera.fieldOfView:0.#}, camera {camera.pixelWidth}x{camera.pixelHeight} -> " +
            $"projected spread radius {pixels:0.##} px.");
    }

    /// <summary>
    /// The body this manager draws for, but only when it is the HUD local
    /// user's own live body viewed through the normal gameplay camera (same
    /// ownership test the game uses in CameraRigController.ModifyAimRayIfApplicable).
    /// Returns null while dead, spectating another player, or under a camera override.
    /// </summary>
    private static CharacterBody? GetOwnLiveBody(CrosshairManager manager)
    {
        CameraRigController rig = manager.cameraRigController;
        if (!rig || rig.hasOverride)
        {
            return null;
        }

        LocalUser? viewer = rig.localUserViewer;
        CharacterBody body = rig.targetBody;
        if (viewer == null || !body || viewer.cachedBodyObject != body.gameObject)
        {
            return null;
        }

        if (!body.healthComponent || !body.healthComponent.alive)
        {
            return null;
        }

        return body;
    }

    private void SetReplacing(bool replacing, CharacterBody? body)
    {
        if (replacing == _replacing)
        {
            return;
        }

        _replacing = replacing;
        Plugin.Log.LogDebug(replacing
            ? $"Custom crosshair active for {body!.GetDisplayName()} ({body.name})."
            : "Custom crosshair released; vanilla crosshair logic in control.");
    }
}
