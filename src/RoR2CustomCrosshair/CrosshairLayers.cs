using UnityEngine;
using UnityEngine.UI;

namespace RoR2CustomCrosshair;

/// <summary>
/// Shared screen-pixel frame for one Apply. Every layer converts its
/// settings through this one frame, so all layers share the same snapped
/// center, the same resolution scale and the same rounding rules.
/// </summary>
internal readonly struct PixelFrame
{
    /// <summary>Screen pixels per configured unit (1 at 1920x1080).</summary>
    internal readonly float ResolutionScale;

    /// <summary>Screen pixels per local UI unit of the procedural root.</summary>
    internal readonly float PixelsPerUnit;

    /// <summary>0 when the center is on a pixel boundary, 0.5 when on a pixel center.</summary>
    internal readonly float CenterFraction;

    /// <summary>Snapped center in screen pixels.</summary>
    internal readonly Vector2 Center;

    /// <summary>Pixel offset from the procedural root's pivot to <see cref="Center"/>.</summary>
    internal readonly Vector2 OriginOffset;

    internal PixelFrame(float resolutionScale, float pixelsPerUnit, float centerFraction, Vector2 anchorScreen, Vector2 rootScreen)
    {
        ResolutionScale = resolutionScale;
        PixelsPerUnit = pixelsPerUnit;
        CenterFraction = centerFraction;
        Center = new Vector2(SnapToGrid(anchorScreen.x, centerFraction), SnapToGrid(anchorScreen.y, centerFraction));
        OriginOffset = Center - rootScreen;
    }

    /// <summary>
    /// The center fraction that lets a band of this many configured units be
    /// centered with both edges on pixel boundaries.
    /// </summary>
    internal static float FractionFor(float logicalSize, float resolutionScale)
    {
        return Mathf.Max(1, RoundPx(logicalSize * resolutionScale)) % 2 == 0 ? 0f : 0.5f;
    }

    /// <summary>Whole pixels, at least one (lengths, ring thickness, borders).</summary>
    internal int Width(float logical)
    {
        return Mathf.Max(1, RoundPx(logical * ResolutionScale));
    }

    /// <summary>
    /// Whole pixels for a band centered on <see cref="Center"/> (bar
    /// thickness, dot size): the nearest size whose parity keeps both edges on
    /// pixel boundaries around the shared center (even for a boundary center,
    /// odd for a pixel-center center).
    /// </summary>
    internal int CenteredSize(float logical)
    {
        float px = logical * ResolutionScale;
        if (CenterFraction == 0f)
        {
            return Mathf.Max(2, 2 * RoundPx(px * 0.5f));
        }

        return Mathf.Max(1, 2 * (int)Mathf.Floor(px * 0.5f) + 1);
    }

    /// <summary>
    /// Distance from the center to an edge that lands on a pixel boundary
    /// (bar inner edge, ring inner radius). The mirrored edge is a boundary
    /// too because 2 * center is a whole number.
    /// </summary>
    /// <param name="logical">Configured distance (pixels at 1920x1080).</param>
    /// <param name="extraPixels">Added screen pixels (dynamic spread) before snapping.</param>
    internal float EdgeDistance(float logical, float extraPixels = 0f)
    {
        float pixels = Mathf.Max(0f, Mathf.Max(0f, logical) * ResolutionScale + extraPixels);
        return Mathf.Floor(CenterFraction + pixels + 0.5f) - CenterFraction;
    }

    internal float ToLocal(float pixels)
    {
        return pixels / PixelsPerUnit;
    }

    /// <summary>Places a rect given as (xMin, yMin, xMax, yMax) pixels relative to the center.</summary>
    internal void Place(RectTransform rect, Vector4 pixelRect)
    {
        var centerPx = new Vector2((pixelRect.x + pixelRect.z) * 0.5f, (pixelRect.y + pixelRect.w) * 0.5f);
        var sizePx = new Vector2(pixelRect.z - pixelRect.x, pixelRect.w - pixelRect.y);
        rect.anchoredPosition = (OriginOffset + centerPx) / PixelsPerUnit;
        rect.sizeDelta = sizePx / PixelsPerUnit;
    }

    internal static int RoundPx(float value)
    {
        return (int)Mathf.Floor(value + 0.5f);
    }

    /// <summary>Nearest value of the form n + fraction.</summary>
    private static float SnapToGrid(float value, float fraction)
    {
        return Mathf.Floor(value - fraction + 0.5f) + fraction;
    }
}

/// <summary>
/// An axis-aligned fill rectangle with a true border: four strips around it
/// that never overlap the fill or each other (top/bottom span the corners).
/// </summary>
internal sealed class BorderedRect
{
    private static readonly string[] SideNames = { "Top", "Bottom", "Left", "Right" };

    private readonly Image _fill;
    private readonly Image[] _strips = new Image[4];

    private BorderedRect(Transform layer, string name)
    {
        _fill = Find(layer, "Fill" + name);
        for (int side = 0; side < 4; side++)
        {
            _strips[side] = Find(layer, "Outline" + name + SideNames[side]);
        }
    }

    internal static BorderedRect Bind(Transform layer, string name) => new(layer, name);

    internal static void BuildBorder(Transform layer, string name)
    {
        foreach (string side in SideNames)
        {
            CrosshairPrefabFactory.CreateImage("Outline" + name + side, layer);
        }
    }

    internal static void BuildFill(Transform layer, string name)
    {
        CrosshairPrefabFactory.CreateImage("Fill" + name, layer);
    }

    /// <param name="r">(xMin, yMin, xMax, yMax) in pixels relative to the center.</param>
    internal void Apply(in PixelFrame frame, Vector4 r, Color fill, int border, Color borderColor)
    {
        Show(_fill, frame, r, fill);

        if (border <= 0)
        {
            foreach (Image strip in _strips)
            {
                strip.enabled = false;
            }

            return;
        }

        float o = border;
        Show(_strips[0], frame, new Vector4(r.x - o, r.w, r.z + o, r.w + o), borderColor);
        Show(_strips[1], frame, new Vector4(r.x - o, r.y - o, r.z + o, r.y), borderColor);
        Show(_strips[2], frame, new Vector4(r.x - o, r.y, r.x, r.w), borderColor);
        Show(_strips[3], frame, new Vector4(r.z, r.y, r.z + o, r.w), borderColor);
    }

    internal void Hide()
    {
        _fill.enabled = false;
        foreach (Image strip in _strips)
        {
            strip.enabled = false;
        }
    }

    private static void Show(Image image, in PixelFrame frame, Vector4 r, Color color)
    {
        frame.Place(image.rectTransform, r);
        image.color = color;
        image.enabled = true;
    }

    private static Image Find(Transform layer, string name)
    {
        return layer.Find(name).GetComponent<Image>();
    }
}

/// <summary>Four arms around the center: Inner Bars or Outer Bars.</summary>
internal sealed class BarsLayer
{
    private static readonly string[] ArmNames = { "Left", "Right", "Top", "Bottom" };

    private readonly BarsSettings _settings;
    private readonly BorderedRect[] _arms = new BorderedRect[4];

    internal BarsLayer(Transform root, string layerName, BarsSettings settings)
    {
        _settings = settings;
        Transform layer = root.Find(layerName);
        for (int arm = 0; arm < 4; arm++)
        {
            _arms[arm] = BorderedRect.Bind(layer, ArmNames[arm]);
        }
    }

    internal static void Build(Transform root, string layerName)
    {
        Transform layer = CrosshairPrefabFactory.CreateRect(layerName, root);

        // All borders first, then all fills, so one arm's border never draws
        // over another arm's fill when the gap is small.
        foreach (string arm in ArmNames)
        {
            BorderedRect.BuildBorder(layer, arm);
        }

        foreach (string arm in ArmNames)
        {
            BorderedRect.BuildFill(layer, arm);
        }
    }

    internal bool Enabled => _settings.Enabled.Value;

    internal float Thickness => _settings.Thickness.Value;

    /// <param name="dynamicOffset">Extra gap in screen pixels; used only in Dynamic mode.</param>
    internal void Apply(in PixelFrame frame, float dynamicOffset)
    {
        if (!Enabled)
        {
            foreach (BorderedRect arm in _arms)
            {
                arm.Hide();
            }

            return;
        }

        float extra = _settings.Mode.Value == CrosshairMode.Dynamic ? dynamicOffset : 0f;

        float half = frame.CenteredSize(_settings.Thickness.Value) * 0.5f;
        float inner = frame.EdgeDistance(_settings.Gap.Value, extra);
        float outer = inner + frame.Width(_settings.Length.Value);

        Color fill = _settings.Fill.Value;
        Color borderColor = _settings.Outline.Color.Value;
        int border = _settings.Outline.Visible ? frame.Width(_settings.Outline.Thickness.Value) : 0;

        _arms[0].Apply(frame, new Vector4(-outer, -half, -inner, half), fill, border, borderColor);
        _arms[1].Apply(frame, new Vector4(inner, -half, outer, half), fill, border, borderColor);
        _arms[2].Apply(frame, new Vector4(-half, inner, half, outer), fill, border, borderColor);
        _arms[3].Apply(frame, new Vector4(-half, -outer, half, -inner), fill, border, borderColor);
    }
}

/// <summary>
/// A sprite-less Image whose quad <see cref="RingMesh"/> replaces with
/// concentric bands around the shared center: a hollow ring (Circle) or,
/// with an inner radius of zero, a filled disc (Center Dot).
/// </summary>
internal sealed class RingPart
{
    // Segment length along the outer edge, in pixels; smooth with the 1 px feather.
    private const float SegmentPixels = 1.5f;
    private const int MinSegments = 24;
    private const int MaxSegments = 512;

    private readonly RingMesh _mesh;
    private readonly Image _image;

    private RingPart(Transform layer, string name)
    {
        _mesh = layer.Find(name).GetComponent<RingMesh>();
        _image = _mesh.Image;
    }

    internal static RingPart Bind(Transform layer, string name) => new(layer, name);

    internal static void Build(Transform layer, string name)
    {
        // Created disabled like every other part; enabled on first Apply.
        CrosshairPrefabFactory.CreateImage(name, layer);
        layer.Find(name).gameObject.AddComponent<RingMesh>();
    }

    /// <param name="inner">Inner edge radius in pixels; 0 for a filled disc.</param>
    /// <param name="outer">Outer edge radius in pixels.</param>
    /// <param name="border">Border width in pixels on each side; 0 for none.</param>
    internal void Apply(in PixelFrame frame, float inner, float outer, int border, Color fill, Color borderColor)
    {
        float extent = outer + border + 1f;
        int segments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * (outer + border) / SegmentPixels), MinSegments, MaxSegments);

        // The pivot sits exactly on the shared center. Colors live in the
        // generated vertices; the Image's own color is unused.
        frame.Place(_image.rectTransform, new Vector4(-extent, -extent, extent, extent));
        _image.color = Color.white;
        _mesh.SetShape(
            frame.ToLocal(inner),
            frame.ToLocal(outer),
            frame.ToLocal(border),
            frame.ToLocal(1f),
            fill,
            borderColor,
            segments);
        _image.enabled = true;
    }

    internal void Hide()
    {
        _image.enabled = false;
    }
}

/// <summary>Filled circular dot on the center. Always static.</summary>
internal sealed class CenterDotLayer
{
    private const string DotName = "Dot";

    private readonly DotSettings _settings;
    private readonly RingPart _dot;

    internal CenterDotLayer(Transform root, string layerName, DotSettings settings)
    {
        _settings = settings;
        _dot = RingPart.Bind(root.Find(layerName), DotName);
    }

    internal static void Build(Transform root, string layerName)
    {
        RingPart.Build(CrosshairPrefabFactory.CreateRect(layerName, root), DotName);
    }

    internal bool Enabled => _settings.Enabled.Value;

    internal float Size => _settings.Size.Value;

    internal void Apply(in PixelFrame frame)
    {
        if (!Enabled)
        {
            _dot.Hide();
            return;
        }

        // Same parity rule as the bars' thickness, so the dot's left/right/
        // top/bottom extremes land on pixel boundaries around the shared center.
        float radius = frame.CenteredSize(_settings.Size.Value) * 0.5f;
        int border = _settings.Outline.Visible ? frame.Width(_settings.Outline.Thickness.Value) : 0;
        _dot.Apply(frame, 0f, radius, border, _settings.Fill.Value, _settings.Outline.Color.Value);
    }
}

/// <summary>Hollow ring around the center.</summary>
internal sealed class CircleLayer
{
    private const string RingName = "Ring";

    private readonly CircleSettings _settings;
    private readonly RingPart _ring;

    internal CircleLayer(Transform root, string layerName, CircleSettings settings)
    {
        _settings = settings;
        _ring = RingPart.Bind(root.Find(layerName), RingName);
    }

    internal static void Build(Transform root, string layerName)
    {
        RingPart.Build(CrosshairPrefabFactory.CreateRect(layerName, root), RingName);
    }

    /// <param name="dynamicOffset">Extra radius in screen pixels; used only in Dynamic mode.</param>
    internal void Apply(in PixelFrame frame, float dynamicOffset)
    {
        if (!_settings.Enabled.Value)
        {
            _ring.Hide();
            return;
        }

        float extra = _settings.Mode.Value == CrosshairMode.Dynamic ? dynamicOffset : 0f;

        // Inner edge snapped like the bars' gap: its axis-aligned extremes
        // land on pixel boundaries around the shared center.
        float inner = frame.EdgeDistance(_settings.Radius.Value, extra);
        float outer = inner + frame.Width(_settings.Thickness.Value);
        int border = _settings.Outline.Visible ? frame.Width(_settings.Outline.Thickness.Value) : 0;
        _ring.Apply(frame, inner, outer, border, _settings.Fill.Value, _settings.Outline.Color.Value);
    }
}
