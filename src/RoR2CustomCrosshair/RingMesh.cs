using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoR2CustomCrosshair;

/// <summary>
/// Procedural concentric-band mesh: a hollow ring with optional inner and
/// outer border bands. No texture or shader: the default UI material draws
/// vertex colors, and every band edge gets a one-pixel alpha feather
/// (centered on the edge) for anti-aliasing. Where two bands meet, the
/// feather blends color-to-color instead of fading to transparent, so the
/// border never sits underneath the ring and cannot tint its interior.
///
/// Implemented as an <see cref="IMeshModifier"/> on a sprite-less Image
/// rather than a Graphic subclass: Graphic.OnPopulateMesh is protected in the
/// shipped UnityEngine.UI (the publicized GameLibs copy shows it as public),
/// while IMeshModifier is a public interface the Image calls after building
/// its own quad, which we replace.
/// </summary>
[RequireComponent(typeof(Image))]
internal sealed class RingMesh : MonoBehaviour, IMeshModifier
{
    private readonly struct Stop
    {
        internal readonly float Radius;
        internal readonly Color32 Color;

        internal Stop(float radius, Color32 color)
        {
            Radius = radius;
            Color = color;
        }
    }

    private readonly struct Shape : System.IEquatable<Shape>
    {
        internal readonly float Inner, Outer, Border, Feather;
        internal readonly Color32 Fill, BorderColor;
        internal readonly int Segments;

        internal Shape(float inner, float outer, float border, float feather, Color32 fill, Color32 borderColor, int segments)
        {
            Inner = inner;
            Outer = outer;
            Border = border;
            Feather = feather;
            Fill = fill;
            BorderColor = borderColor;
            Segments = segments;
        }

        public bool Equals(Shape o) =>
            Inner == o.Inner && Outer == o.Outer && Border == o.Border && Feather == o.Feather &&
            Segments == o.Segments && Same(Fill, o.Fill) && Same(BorderColor, o.BorderColor);

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    private readonly List<Stop> _stops = new();
    private Shape _shape;
    private bool _hasShape;
    private Image? _image;

    internal Image Image => _image != null ? _image : _image = GetComponent<Image>();

    /// <summary>
    /// Sets the ring geometry in this object's local units. Rebuilds the
    /// mesh only when something changed.
    /// </summary>
    /// <param name="inner">Inner edge radius of the ring.</param>
    /// <param name="outer">Outer edge radius of the ring.</param>
    /// <param name="border">Border width on each side; 0 for none.</param>
    /// <param name="feather">Anti-aliasing width (one screen pixel).</param>
    internal void SetShape(float inner, float outer, float border, float feather, Color fill, Color borderColor, int segments)
    {
        var shape = new Shape(inner, outer, border, feather, fill, borderColor, segments);
        if (_hasShape && shape.Equals(_shape))
        {
            return;
        }

        _shape = shape;
        _hasShape = true;
        Image.SetVerticesDirty();
    }

    // Image (a MaskableGraphic) uses VertexHelper generation; the legacy
    // Mesh overload is never called for it.
    public void ModifyMesh(Mesh mesh)
    {
    }

    public void ModifyMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!_hasShape)
        {
            return;
        }

        BuildStops(_shape);

        int segments = _shape.Segments;
        float step = 2f * Mathf.PI / segments;

        for (int s = 0; s < _stops.Count; s++)
        {
            Stop stop = _stops[s];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * step;
                var position = new Vector3(Mathf.Cos(angle) * stop.Radius, Mathf.Sin(angle) * stop.Radius);
                vh.AddVert(position, stop.Color, Vector4.zero);
            }
        }

        for (int s = 0; s + 1 < _stops.Count; s++)
        {
            int a = s * segments;
            int b = (s + 1) * segments;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                vh.AddTriangle(a + i, b + i, b + next);
                vh.AddTriangle(a + i, b + next, a + next);
            }
        }
    }

    /// <summary>
    /// Radial color stops, inside to outside. Bands are contiguous:
    /// [inner border] [ring] [outer border]. Each band edge becomes two
    /// stops half a feather either side of it.
    /// </summary>
    private void BuildStops(Shape shape)
    {
        _stops.Clear();

        float halfFeather = shape.Feather * 0.5f;
        bool hasBorder = shape.Border > 0f;

        // Band boundaries and the colors between them.
        var edges = new List<float>(4);
        var colors = new List<Color32>(3);

        if (hasBorder && shape.Inner > 0f)
        {
            edges.Add(Mathf.Max(0f, shape.Inner - shape.Border));
            colors.Add(shape.BorderColor);
        }

        edges.Add(shape.Inner);
        colors.Add(shape.Fill);
        edges.Add(shape.Outer);

        if (hasBorder)
        {
            colors.Add(shape.BorderColor);
            edges.Add(shape.Outer + shape.Border);
        }

        for (int e = 0; e < edges.Count; e++)
        {
            Color32 inside = e == 0 ? Transparent(colors[0]) : colors[e - 1];
            Color32 outside = e == edges.Count - 1 ? Transparent(colors[e - 1]) : colors[e];

            if (e == 0 && edges[0] <= 0f)
            {
                // Band reaches the center: solid disc, no inner feather.
                AddStop(0f, outside);
                continue;
            }

            AddStop(edges[e] - halfFeather, inside);
            AddStop(edges[e] + halfFeather, outside);
        }
    }

    private void AddStop(float radius, Color32 color)
    {
        // Bands are at least one pixel wide, so stops never cross; clamp anyway.
        float previous = _stops.Count > 0 ? _stops[_stops.Count - 1].Radius : 0f;
        _stops.Add(new Stop(Mathf.Max(radius, previous), color));
    }

    private static Color32 Transparent(Color32 color)
    {
        return new Color32(color.r, color.g, color.b, 0);
    }
}
