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
    private static readonly ISpreadSource[] SpreadSources =
    {
        new CommandoSpreadSource(),
        new BanditSpreadSource(),
        new ToolbotSpreadSource()
    };

    // Offsets are quantized so the view only re-applies on visible changes.
    private const float OffsetStepPixels = 0.25f;

    // Temporary diagnostics for sources still being validated: at most one sample per second.
    private const float SpreadLogInterval = 1f;
    private float _nextSpreadLogTime;

    private readonly SpreadSourceResolver _spreadResolver = new(SpreadSources);

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
            ? CrosshairDisplayPolicy.ChooseTemplate(body, crosshairPrefab)
            : null;

        if (template != null)
        {
            crosshairPrefab = template;
        }

        SetReplacing(template != null, body);
    }

    /// <summary>
    /// Dynamic offsets for the custom crosshair on this HUD: how far, in
    /// screen pixels, the actual weapon spread cone currently reaches beyond
    /// its resting size. Zero unless the custom crosshair is showing for the
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

        float current = SpreadProjection.HalfAngleToPixels(sample.CurrentDegrees, camera);
        float resting = SpreadProjection.HalfAngleToPixels(sample.RestingDegrees, camera);
        float pixels = Mathf.Round(Mathf.Max(0f, current - resting) / OffsetStepPixels) * OffsetStepPixels;

        if (source!.LogSamples)
        {
            LogSpreadSample(source, sample, camera, current, pixels);
        }

        return new DynamicOffsets(pixels, pixels, pixels);
    }

    private void LogSpreadSample(ISpreadSource source, in SpreadSample sample, Camera camera, float currentPixels, float offsetPixels)
    {
        if (offsetPixels <= 0f || Time.unscaledTime < _nextSpreadLogTime)
        {
            return;
        }

        _nextSpreadLogTime = Time.unscaledTime + SpreadLogInterval;
        Plugin.Log.LogDebug(
            $"[Spread] {source.GetType().Name}: bloom {sample.BloomDegrees:0.###} deg -> spread {sample.CurrentDegrees:0.###} deg " +
            $"(rest {sample.RestingDegrees:0.###}), vertical FOV {camera.fieldOfView:0.#}, camera {camera.pixelWidth}x{camera.pixelHeight} -> " +
            $"cone radius {currentPixels:0.#} px, dynamic offset +{offsetPixels:0.##} px.");
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
