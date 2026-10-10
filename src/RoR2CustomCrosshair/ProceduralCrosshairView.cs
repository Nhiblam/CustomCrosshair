using System;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Extra gap/radius per layer, in configured units (pixels at 1920x1080).
/// Applied only to layers in Dynamic mode. Nothing sets these yet: a later
/// phase will feed actual weapon/projectile spread here.
/// </summary>
internal readonly struct DynamicOffsets : IEquatable<DynamicOffsets>
{
    internal readonly float InnerBars;
    internal readonly float OuterBars;
    internal readonly float Circle;

    internal DynamicOffsets(float innerBars, float outerBars, float circle)
    {
        InnerBars = innerBars;
        OuterBars = outerBars;
        Circle = circle;
    }

    public bool Equals(DynamicOffsets other) =>
        InnerBars == other.InnerBars && OuterBars == other.OuterBars && Circle == other.Circle;
}

/// <summary>
/// Drives the four crosshair layers of one crosshair instance. Measures the
/// transform chain once per change, builds one shared <see cref="PixelFrame"/>
/// and hands it to every layer, so all layers use the same snapped center
/// and the same scaling and rounding rules.
/// </summary>
internal sealed class ProceduralCrosshairView : MonoBehaviour
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    private static int _loggedScreenWidth = -1;
    private static int _loggedScreenHeight = -1;
    private static float _loggedPixelsPerUnit = -1f;

    private RectTransform _rect = null!;
    private RectTransform _root = null!;

    private CircleLayer _circle = null!;
    private BarsLayer _outerBars = null!;
    private BarsLayer _innerBars = null!;
    private CenterDotLayer _centerDot = null!;

    private Canvas? _canvas;

    private int _lastRevision = -1;
    private int _lastScreenWidth = -1;
    private int _lastScreenHeight = -1;
    private float _lastPixelsPerUnit = -1f;
    private Vector2 _lastRootScreen;
    private Vector2 _lastAnchorScreen;
    private DynamicOffsets _lastOffsets;

    /// <summary>Per-layer dynamic offsets; all zero until a spread source exists.</summary>
    internal DynamicOffsets Offsets { get; set; }

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _root = (RectTransform)transform.Find(CrosshairPrefabFactory.ProceduralRootName);

        _circle = new CircleLayer(_root, CrosshairPrefabFactory.CircleLayerName, CrosshairConfig.Circle);
        _outerBars = new BarsLayer(_root, CrosshairPrefabFactory.OuterBarsLayerName, CrosshairConfig.OuterBars);
        _innerBars = new BarsLayer(_root, CrosshairPrefabFactory.InnerBarsLayerName, CrosshairConfig.InnerBars);
        _centerDot = new CenterDotLayer(_root, CrosshairPrefabFactory.CenterDotLayerName, CrosshairConfig.CenterDot);

        // CrosshairManager instantiates us inside its LateUpdate, so our own
        // LateUpdate does not run before the first rendered frame. Configure
        // now so that frame already shows the real crosshair.
        Refresh();
    }

    private void OnTransformParentChanged()
    {
        _canvas = null;
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (_canvas == null)
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                return;
            }
        }

        Canvas rootCanvas = _canvas.rootCanvas;
        float pixelsPerUnit = GetPixelsPerUnit(rootCanvas);
        if (pixelsPerUnit <= 0f)
        {
            return;
        }

        Camera? camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : rootCanvas.worldCamera;

        Vector2 rootScreen = RectTransformUtility.WorldToScreenPoint(camera, _root.position);
        Vector2 anchorScreen = RectTransformUtility.WorldToScreenPoint(camera, GetIntendedCenterWorld());
        DynamicOffsets offsets = Offsets;

        if (_lastRevision == CrosshairConfig.Revision &&
            _lastScreenWidth == Screen.width &&
            _lastScreenHeight == Screen.height &&
            Mathf.Approximately(_lastPixelsPerUnit, pixelsPerUnit) &&
            (_lastRootScreen - rootScreen).sqrMagnitude < 1e-6f &&
            (_lastAnchorScreen - anchorScreen).sqrMagnitude < 1e-6f &&
            _lastOffsets.Equals(offsets))
        {
            return;
        }

        PixelFrame frame = CreateFrame(pixelsPerUnit, anchorScreen, rootScreen);

        _circle.Apply(frame, offsets.Circle);
        _outerBars.Apply(frame, offsets.OuterBars);
        _innerBars.Apply(frame, offsets.InnerBars);
        _centerDot.Apply(frame);

        LogLayoutOnce(rootCanvas, frame, rootScreen, anchorScreen);

        _lastRevision = CrosshairConfig.Revision;
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        _lastPixelsPerUnit = pixelsPerUnit;
        _lastRootScreen = rootScreen;
        _lastAnchorScreen = anchorScreen;
        _lastOffsets = offsets;
    }

    /// <summary>
    /// One shared center for every layer. Its grid (pixel boundary or pixel
    /// center) follows the first enabled centered band in priority order
    /// Inner Bars, Outer Bars, Center Dot, so the primary layer keeps its exact
    /// configured thickness; the other centered layers snap their thickness to
    /// the matching parity (see PixelFrame.CenteredSize).
    /// </summary>
    private PixelFrame CreateFrame(float pixelsPerUnit, Vector2 anchorScreen, Vector2 rootScreen)
    {
        // Settings are pixels at 1920x1080 and scale with the smaller axis ratio.
        float resolutionScale = Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight);

        float centerFraction =
            _innerBars.Enabled ? PixelFrame.FractionFor(_innerBars.Thickness, resolutionScale) :
            _outerBars.Enabled ? PixelFrame.FractionFor(_outerBars.Thickness, resolutionScale) :
            _centerDot.Enabled ? PixelFrame.FractionFor(_centerDot.Size, resolutionScale) :
            0f;

        return new PixelFrame(resolutionScale, pixelsPerUnit, centerFraction, anchorScreen, rootScreen);
    }

    /// <summary>
    /// Screen pixels covered by one local UI unit of the procedural root.
    /// The root canvas scale factor covers the CanvasScaler; the lossy-scale
    /// ratio covers any transform scaling between the canvas and the
    /// crosshair (e.g. the game's hud_scale setting).
    /// </summary>
    private float GetPixelsPerUnit(Canvas rootCanvas)
    {
        float canvasLossy = rootCanvas.transform.lossyScale.x;
        if (canvasLossy <= 0f)
        {
            return 0f;
        }

        return rootCanvas.scaleFactor * (_root.lossyScale.x / canvasLossy);
    }

    /// <summary>
    /// The crosshair's anchor point in its container, i.e. where it would sit
    /// without the anchoredPosition of (0.5, 0.5) UI units that
    /// CrosshairManager assigns to every crosshair each frame.
    /// </summary>
    private Vector3 GetIntendedCenterWorld()
    {
        Transform parent = _rect.parent;
        if (parent == null)
        {
            return _root.position;
        }

        return _root.position - parent.TransformVector(_rect.anchoredPosition);
    }

    private void LogLayoutOnce(Canvas rootCanvas, in PixelFrame frame, Vector2 rootScreen, Vector2 anchorScreen)
    {
        if (_loggedScreenWidth == Screen.width &&
            _loggedScreenHeight == Screen.height &&
            Mathf.Approximately(_loggedPixelsPerUnit, frame.PixelsPerUnit))
        {
            return;
        }

        _loggedScreenWidth = Screen.width;
        _loggedScreenHeight = Screen.height;
        _loggedPixelsPerUnit = frame.PixelsPerUnit;

        Plugin.Log.LogDebug(
            $"Crosshair layout: screen {Screen.width}x{Screen.height}, canvas {rootCanvas.renderMode} " +
            $"scaleFactor {rootCanvas.scaleFactor:0.###} pixelPerfect {rootCanvas.pixelPerfect}, " +
            $"px/unit {frame.PixelsPerUnit:0.###}, crosshair anchoredPosition {_rect.anchoredPosition}, " +
            $"root at {rootScreen.x:0.###},{rootScreen.y:0.###} px, anchor {anchorScreen.x:0.###},{anchorScreen.y:0.###} px, " +
            $"snapped center {frame.Center.x},{frame.Center.y}.");
    }
}
