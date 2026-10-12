using System.Collections.Generic;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoR2CustomCrosshair;

/// <summary>
/// Generic indicators drawn around the crosshair center: progress bars and
/// count indicators, two channels each. Knows nothing about survivors or
/// skills; it renders <see cref="IndicatorFrame"/> values with the shared
/// <see cref="PixelFrame"/>, so it uses the reticle's exact center, scaling
/// and pixel snapping.
///
/// Placement: each indicator goes in a mod-defined slot (Above / Below /
/// Left / Right). The first indicator in a slot starts outside the
/// crosshair's configured (static) size plus its own tab's Distance From
/// Crosshair, so Dynamic spread never moves it. Indicators sharing a slot
/// stack outward with fixed spacing, in the order progress channel 1, 2,
/// count channel 1, 2; a later indicator never starts closer than its own
/// distance.
/// </summary>
internal sealed class IndicatorLayer
{
    // Mod-owned stacking gap, in pixels at 1920x1080.
    private const float StackSpacing = 6f;

    private static readonly string[] ProgressNames = { "Progress1", "Progress2" };
    private static readonly string[] CountNames = { "Count1", "Count2" };

    private readonly ProgressBarView[] _progress = new ProgressBarView[IndicatorFrame.Channels];
    private readonly CountIndicatorView[] _counts = new CountIndicatorView[IndicatorFrame.Channels];

    // Next free distance from the center per placement (Above, Below, Left,
    // Right); 0 = nothing placed in that slot yet.
    private readonly float[] _slotDistance = new float[4];

    internal IndicatorLayer(Transform layer)
    {
        for (int i = 0; i < IndicatorFrame.Channels; i++)
        {
            _progress[i] = new ProgressBarView(layer.Find(ProgressNames[i]));
            _counts[i] = new CountIndicatorView(layer.Find(CountNames[i]));
        }
    }

    internal static void Build(Transform root, string name)
    {
        Transform layer = CrosshairPrefabFactory.CreateRect(name, root);
        foreach (string progress in ProgressNames)
        {
            ProgressBarView.Build(CrosshairPrefabFactory.CreateRect(progress, layer));
        }

        foreach (string count in CountNames)
        {
            CountIndicatorView.Build(CrosshairPrefabFactory.CreateRect(count, layer));
        }
    }

    internal void Apply(in PixelFrame frame, IndicatorFrame data)
    {
        ProgressBarSettings progressSettings = CrosshairConfig.ProgressBar;
        CountIndicatorSettings countSettings = CrosshairConfig.CountIndicator;

        float extent = StaticCrosshairExtent(frame);
        float progressStart = extent + frame.WholePixels(progressSettings.Distance.Value);
        float countStart = extent + frame.WholePixels(countSettings.Distance.Value);
        for (int i = 0; i < _slotDistance.Length; i++)
        {
            _slotDistance[i] = 0f;
        }

        float spacing = frame.WholePixels(StackSpacing);

        for (int i = 0; i < IndicatorFrame.Channels; i++)
        {
            ProgressIndicatorState state = data.Progress[i];
            if (!state.Active || !progressSettings.Enabled.Value)
            {
                _progress[i].Hide();
                continue;
            }

            Vector2 size = _progress[i].Size(frame, progressSettings);
            _progress[i].Apply(frame, progressSettings, state, Take(frame, progressSettings.Placement.Value, progressStart, size, spacing));
        }

        for (int i = 0; i < IndicatorFrame.Channels; i++)
        {
            CountIndicatorState state = data.Counts[i];
            if (!state.Active || !countSettings.Enabled.Value)
            {
                _counts[i].Hide();
                continue;
            }

            Vector2 size = _counts[i].Size(frame, countSettings, state);
            _counts[i].Apply(frame, countSettings, state, Take(frame, countSettings.Placement.Value, countStart, size, spacing));
        }
    }

    /// <summary>
    /// The next box (xMin, yMin, xMax, yMax) in pixels relative to the center
    /// for an indicator of <paramref name="size"/> in <paramref name="placement"/>,
    /// no closer than <paramref name="start"/>.
    /// </summary>
    private Vector4 Take(in PixelFrame frame, IndicatorPlacement placement, float start, Vector2 size, float spacing)
    {
        int slot = (int)placement;
        float d = Mathf.Max(start, _slotDistance[slot]);
        bool vertical = placement is IndicatorPlacement.Above or IndicatorPlacement.Below;
        _slotDistance[slot] = d + (vertical ? size.y : size.x) + spacing;

        float x0 = frame.SnapEdge(-size.x * 0.5f);
        float y0 = frame.SnapEdge(-size.y * 0.5f);
        return placement switch
        {
            IndicatorPlacement.Above => new Vector4(x0, d, x0 + size.x, d + size.y),
            IndicatorPlacement.Below => new Vector4(x0, -d - size.y, x0 + size.x, -d),
            IndicatorPlacement.Left => new Vector4(-d - size.x, y0, -d, y0 + size.y),
            _ => new Vector4(d, y0, d + size.x, y0 + size.y)
        };
    }

    /// <summary>
    /// Farthest extent of the enabled crosshair layers at their configured
    /// geometry (Dynamic offsets excluded), in pixels from the center.
    /// </summary>
    private static float StaticCrosshairExtent(in PixelFrame frame)
    {
        float extent = Mathf.Max(BarsExtent(frame, CrosshairConfig.InnerBars), BarsExtent(frame, CrosshairConfig.OuterBars));

        CircleSettings circle = CrosshairConfig.Circle;
        if (circle.Enabled.Value)
        {
            float outer = frame.EdgeDistance(circle.Radius.Value) + frame.Width(circle.Thickness.Value);
            extent = Mathf.Max(extent, outer + Border(frame, circle.Outline));
        }

        DotSettings dot = CrosshairConfig.CenterDot;
        if (dot.Enabled.Value)
        {
            extent = Mathf.Max(extent, frame.CenteredSize(dot.Size.Value) * 0.5f + Border(frame, dot.Outline));
        }

        return frame.SnapEdge(extent);
    }

    private static float BarsExtent(in PixelFrame frame, BarsSettings bars)
    {
        if (!bars.Enabled.Value)
        {
            return 0f;
        }

        float outer = frame.EdgeDistance(bars.Gap.Value) + frame.Width(bars.Length.Value);
        return outer + Border(frame, bars.Outline);
    }

    internal static int Border(in PixelFrame frame, OutlineSettings outline) =>
        outline.Visible ? frame.Width(outline.Thickness.Value) : 0;

    /// <summary>Four border strips around a rect, never overlapping it (top/bottom span the corners).</summary>
    internal static void PlaceBorder(in PixelFrame frame, Image[] strips, Vector4 r, int border, Color color)
    {
        if (border <= 0)
        {
            foreach (Image strip in strips)
            {
                strip.enabled = false;
            }

            return;
        }

        float o = border;
        Show(frame, strips[0], new Vector4(r.x - o, r.w, r.z + o, r.w + o), color);
        Show(frame, strips[1], new Vector4(r.x - o, r.y - o, r.z + o, r.y), color);
        Show(frame, strips[2], new Vector4(r.x - o, r.y, r.x, r.w), color);
        Show(frame, strips[3], new Vector4(r.z, r.y, r.z + o, r.w), color);
    }

    internal static void Show(in PixelFrame frame, Image image, Vector4 r, Color color)
    {
        frame.Place(image.rectTransform, r);
        image.color = color;
        image.enabled = true;
    }
}

/// <summary>
/// Generic progress / charge / readiness bar. Static: the whole bar is the
/// Not Ready or Ready color. Dynamic: the fill covers progress x length (whole
/// pixels), the optional background covers only the unfilled part, and the
/// bar turns Ready color when ready. The border is four strips outside the
/// bar, so neither fill nor background is ever drawn over another color.
/// </summary>
internal sealed class ProgressBarView
{
    private static readonly string[] StripNames = { "BorderTop", "BorderBottom", "BorderLeft", "BorderRight" };

    private readonly Image _background;
    private readonly Image _fill;
    private readonly Image[] _strips = new Image[4];

    internal ProgressBarView(Transform root)
    {
        _background = root.Find("Background").GetComponent<Image>();
        _fill = root.Find("Fill").GetComponent<Image>();
        for (int i = 0; i < StripNames.Length; i++)
        {
            _strips[i] = root.Find(StripNames[i]).GetComponent<Image>();
        }
    }

    internal static void Build(Transform root)
    {
        foreach (string strip in StripNames)
        {
            CrosshairPrefabFactory.CreateImage(strip, root);
        }

        CrosshairPrefabFactory.CreateImage("Background", root);
        CrosshairPrefabFactory.CreateImage("Fill", root);
    }

    /// <summary>Footprint including the border, in pixels.</summary>
    internal Vector2 Size(in PixelFrame frame, ProgressBarSettings settings)
    {
        int length = frame.Width(settings.Length.Value);
        int thickness = frame.Width(settings.Thickness.Value);
        int border = 2 * IndicatorLayer.Border(frame, settings.Outline);
        return settings.Orientation.Value == BarOrientation.Horizontal
            ? new Vector2(length + border, thickness + border)
            : new Vector2(thickness + border, length + border);
    }

    internal void Apply(in PixelFrame frame, ProgressBarSettings settings, in ProgressIndicatorState state, Vector4 box)
    {
        int border = IndicatorLayer.Border(frame, settings.Outline);
        var bar = new Vector4(box.x + border, box.y + border, box.z - border, box.w - border);
        Color color = state.Ready ? settings.Ready.Value : settings.NotReady.Value;

        if (settings.Mode.Value == ProgressBarMode.Static)
        {
            IndicatorLayer.Show(frame, _fill, bar, color);
            _background.enabled = false;
        }
        else
        {
            bool horizontal = settings.Orientation.Value == BarOrientation.Horizontal;
            bool fromStart = settings.FillFrom.Value == FillFrom.LeftOrBottom;
            float length = horizontal ? bar.z - bar.x : bar.w - bar.y;
            float filled = Mathf.Round(Mathf.Clamp01(state.Progress) * length);

            // Split the bar at the fill edge: fill on one side, background on the other.
            float split = horizontal
                ? (fromStart ? bar.x + filled : bar.z - filled)
                : (fromStart ? bar.y + filled : bar.w - filled);

            Vector4 fill = horizontal
                ? (fromStart ? new Vector4(bar.x, bar.y, split, bar.w) : new Vector4(split, bar.y, bar.z, bar.w))
                : (fromStart ? new Vector4(bar.x, bar.y, bar.z, split) : new Vector4(bar.x, split, bar.z, bar.w));
            Vector4 empty = horizontal
                ? (fromStart ? new Vector4(split, bar.y, bar.z, bar.w) : new Vector4(bar.x, bar.y, split, bar.w))
                : (fromStart ? new Vector4(bar.x, split, bar.z, bar.w) : new Vector4(bar.x, bar.y, bar.z, split));

            if (filled > 0f)
            {
                IndicatorLayer.Show(frame, _fill, fill, color);
            }
            else
            {
                _fill.enabled = false;
            }

            if (settings.ShowBackground.Value && filled < length)
            {
                IndicatorLayer.Show(frame, _background, empty, settings.Background.Value);
            }
            else
            {
                _background.enabled = false;
            }
        }

        IndicatorLayer.PlaceBorder(frame, _strips, bar, border, settings.Outline.Color.Value);
    }

    internal void Hide()
    {
        _fill.enabled = false;
        _background.enabled = false;
        foreach (Image strip in _strips)
        {
            strip.enabled = false;
        }
    }
}

/// <summary>
/// Generic count indicator: one circular dot or one bar per current count,
/// or a number. The layout box is sized for max(current, maximum) so
/// elements do not shift as the count changes; elements fill from the
/// left (row) or top (column). Dots reuse the procedural disc mesh; bars use
/// a fill with a true border; the number uses the game's own text component
/// and font.
/// </summary>
internal sealed class CountIndicatorView
{
    // TMP outline width (0..1, SDF units) per pixel of configured outline.
    private const float TextOutlinePerPixel = 0.15f;

    private readonly Transform _root;
    private readonly TextMeshProUGUI _text;
    private readonly List<RingPart> _dots = new();
    private readonly List<BorderedRect> _bars = new();

    private int _lastTextCurrent = -1;
    private int _lastTextMaximum = -1;
    private CountNumberFormat _lastTextFormat;

    internal CountIndicatorView(Transform root)
    {
        _root = root;
        _text = root.Find("Text").GetComponent<TextMeshProUGUI>();
    }

    internal static void Build(Transform root)
    {
        RectTransform text = CrosshairPrefabFactory.CreateRect("Text", root);
        text.gameObject.AddComponent<CanvasRenderer>();

        // RoR2's text component applies the game's language font in Awake.
        HGTextMeshProUGUI label = text.gameObject.AddComponent<HGTextMeshProUGUI>();
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.Center;
        label.text = string.Empty;
        label.enabled = false;
    }

    /// <summary>Footprint including borders, in pixels.</summary>
    internal Vector2 Size(in PixelFrame frame, CountIndicatorSettings settings, in CountIndicatorState state)
    {
        if (settings.Mode.Value == CountDisplayMode.Number)
        {
            int font = frame.Width(settings.FontSize.Value);
            int chars = settings.NumberFormat.Value == CountNumberFormat.Current
                ? Digits(state.Current)
                : Digits(state.Current) + 3 + Digits(state.Maximum);
            return new Vector2(Mathf.Ceil(chars * 0.6f * font), font);
        }

        int slots = Mathf.Max(1, Mathf.Max(state.Current, state.Maximum));
        Vector2 element = ElementFootprint(frame, settings);
        float spacing = frame.WholePixels(settings.Spacing.Value);
        return settings.Layout.Value == CountLayout.Horizontal
            ? new Vector2(slots * element.x + (slots - 1) * spacing, element.y)
            : new Vector2(element.x, slots * element.y + (slots - 1) * spacing);
    }

    internal void Apply(in PixelFrame frame, CountIndicatorSettings settings, in CountIndicatorState state, Vector4 box)
    {
        CountDisplayMode mode = settings.Mode.Value;
        int border = IndicatorLayer.Border(frame, settings.Outline);
        Color fill = settings.Fill.Value;
        Color borderColor = settings.Outline.Color.Value;

        if (mode == CountDisplayMode.Number)
        {
            ApplyText(frame, settings, state, box, border, fill, borderColor);
        }
        else
        {
            _text.enabled = false;
        }

        int shown = mode == CountDisplayMode.Dots ? state.Current : 0;
        EnsureDots(shown);
        Vector2 element = ElementFootprint(frame, settings);
        float spacing = frame.WholePixels(settings.Spacing.Value);
        bool horizontal = settings.Layout.Value == CountLayout.Horizontal;

        for (int i = 0; i < _dots.Count; i++)
        {
            if (i >= shown)
            {
                _dots[i].Hide();
                continue;
            }

            Vector4 slot = Slot(box, element, spacing, horizontal, i);
            float radius = (element.x - 2 * border) * 0.5f;
            var center = new Vector2((slot.x + slot.z) * 0.5f, (slot.y + slot.w) * 0.5f);
            _dots[i].Apply(frame, 0f, radius, border, fill, borderColor, center);
        }

        shown = mode == CountDisplayMode.Bars ? state.Current : 0;
        EnsureBars(shown);
        for (int i = 0; i < _bars.Count; i++)
        {
            if (i >= shown)
            {
                _bars[i].Hide();
                continue;
            }

            Vector4 slot = Slot(box, element, spacing, horizontal, i);
            var bar = new Vector4(slot.x + border, slot.y + border, slot.z - border, slot.w - border);
            _bars[i].Apply(frame, bar, fill, border, borderColor);
        }
    }

    internal void Hide()
    {
        _text.enabled = false;
        foreach (RingPart dot in _dots)
        {
            dot.Hide();
        }

        foreach (BorderedRect bar in _bars)
        {
            bar.Hide();
        }
    }

    private static Vector2 ElementFootprint(in PixelFrame frame, CountIndicatorSettings settings)
    {
        int border = 2 * IndicatorLayer.Border(frame, settings.Outline);
        if (settings.Mode.Value == CountDisplayMode.Dots)
        {
            int size = frame.Width(settings.DotSize.Value) + border;
            return new Vector2(size, size);
        }

        int length = frame.Width(settings.BarLength.Value);
        int thickness = frame.Width(settings.BarThickness.Value);
        return settings.BarShape.Value == BarOrientation.Vertical
            ? new Vector2(thickness + border, length + border)
            : new Vector2(length + border, thickness + border);
    }

    /// <summary>Element i's footprint: left to right in a row, top to bottom in a column.</summary>
    private static Vector4 Slot(Vector4 box, Vector2 element, float spacing, bool horizontal, int i)
    {
        if (horizontal)
        {
            float x = box.x + i * (element.x + spacing);
            return new Vector4(x, box.y, x + element.x, box.y + element.y);
        }

        float top = box.w - i * (element.y + spacing);
        return new Vector4(box.x, top - element.y, box.x + element.x, top);
    }

    private void ApplyText(
        in PixelFrame frame, CountIndicatorSettings settings, in CountIndicatorState state,
        Vector4 box, int border, Color fill, Color borderColor)
    {
        CountNumberFormat format = settings.NumberFormat.Value;
        if (state.Current != _lastTextCurrent || state.Maximum != _lastTextMaximum || format != _lastTextFormat)
        {
            _lastTextCurrent = state.Current;
            _lastTextMaximum = state.Maximum;
            _lastTextFormat = format;
            _text.text = format == CountNumberFormat.Current
                ? state.Current.ToString()
                : $"{state.Current} / {state.Maximum}";
        }

        frame.Place(_text.rectTransform, box);
        _text.fontSize = frame.ToLocal(frame.Width(settings.FontSize.Value));
        _text.color = fill;
        _text.outlineWidth = border > 0 ? Mathf.Clamp01(border * TextOutlinePerPixel) : 0f;
        _text.outlineColor = borderColor;
        _text.enabled = true;
    }

    private void EnsureDots(int count)
    {
        while (_dots.Count < count)
        {
            string name = "Dot" + _dots.Count;
            RingPart.Build(_root, name);
            _dots.Add(RingPart.Bind(_root, name));
        }
    }

    private void EnsureBars(int count)
    {
        while (_bars.Count < count)
        {
            string name = "Bar" + _bars.Count;
            BorderedRect.BuildBorder(_root, name);
            BorderedRect.BuildFill(_root, name);
            _bars.Add(BorderedRect.Bind(_root, name));
        }
    }

    private static int Digits(int value) => value < 10 ? 1 : value < 100 ? 2 : 3;
}
