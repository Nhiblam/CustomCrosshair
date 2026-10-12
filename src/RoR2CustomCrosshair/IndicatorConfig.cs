using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Mod-defined indicator slots around the crosshair center. There are no
/// X/Y controls: the mod decides the exact position of each slot.
/// </summary>
internal enum IndicatorPlacement
{
    Above,
    Below,
    Left,
    Right
}

/// <summary>Static: whole bar switches color. Dynamic: fill grows with progress.</summary>
internal enum ProgressBarMode
{
    Static,
    Dynamic
}

internal enum BarOrientation
{
    Horizontal,
    Vertical
}

/// <summary>Where the fill starts: left (horizontal) / bottom (vertical), or the opposite end.</summary>
internal enum FillFrom
{
    LeftOrBottom,
    RightOrTop
}

internal enum CountDisplayMode
{
    Dots,
    Number,
    Bars
}

internal enum CountLayout
{
    Horizontal,
    Vertical
}

internal enum CountNumberFormat
{
    Current,
    CurrentOfMaximum
}

/// <summary>
/// "Distance From Crosshair": gap between the crosshair's configured
/// (static) outer edge and the first indicator in a slot. Shared shape for
/// both indicator tabs; each tab has its own value.
/// </summary>
internal static class IndicatorDistance
{
    internal const float Default = 8f;
    internal const float Max = 128f;

    internal static ConfigEntry<float> Bind(ConfigFile config, string section) =>
        CrosshairConfig.BindFloat(config, section, "Distance From Crosshair", Default, 0f, Max,
            "Gap between the crosshair's configured outer edge and the first indicator in its slot, in pixels at 1920x1080. " +
            "Further indicators in the same slot stack outward from it.");

    internal static void Register(ConfigEntry<float> distance) =>
        CrosshairConfig.AddFloatOption(distance, 0f, Max);
}

/// <summary>Generic progress / charge / readiness bar ("Progress Bar" tab).</summary>
internal sealed class ProgressBarSettings
{
    internal const float MaxLength = 200f;
    internal const float MaxThickness = 40f;

    internal readonly ConfigEntry<bool> Enabled;
    internal readonly ConfigEntry<ProgressBarMode> Mode;
    internal readonly ConfigEntry<BarOrientation> Orientation;
    internal readonly ConfigEntry<FillFrom> FillFrom;
    internal readonly ConfigEntry<IndicatorPlacement> Placement;
    internal readonly ConfigEntry<float> Distance;
    internal readonly ConfigEntry<float> Length;
    internal readonly ConfigEntry<float> Thickness;
    internal readonly ColorSettings NotReady;
    internal readonly ColorSettings Ready;
    internal readonly ConfigEntry<bool> ShowBackground;
    internal readonly ColorSettings Background;
    internal readonly OutlineSettings Outline;

    internal ProgressBarSettings(ConfigFile config, string section)
    {
        Enabled = config.Bind(section, "Enabled", true,
            "Show progress / charge / readiness bars (e.g. MUL-T Rebar Puncher readiness).");
        Mode = config.Bind(section, "Mode", ProgressBarMode.Dynamic,
            "Static: the whole bar is Not Ready color until ready, then Ready color. Dynamic: the bar fills with progress, then turns Ready color.");
        Orientation = config.Bind(section, "Orientation", BarOrientation.Vertical, "Horizontal or vertical bar.");
        FillFrom = config.Bind(section, "Fill From", RoR2CustomCrosshair.FillFrom.LeftOrBottom,
            "Dynamic fill start: left (horizontal) / bottom (vertical), or the opposite end.");
        Placement = config.Bind(section, "Placement", IndicatorPlacement.Right,
            "Mod-defined slot around the crosshair. Several indicators on one side stack automatically.");
        Distance = IndicatorDistance.Bind(config, section);
        Length = CrosshairConfig.BindFloat(config, section, "Length", 24f, 4f, MaxLength,
            "Bar length along its orientation, in pixels at 1920x1080.");
        Thickness = CrosshairConfig.BindFloat(config, section, "Thickness", 4f, 1f, MaxThickness,
            "Bar thickness across its orientation, in pixels at 1920x1080.");
        NotReady = new ColorSettings(config, section, "Not Ready ", new Color32(255, 140, 0, 255));
        Ready = new ColorSettings(config, section, "Ready ", new Color32(60, 220, 60, 255));
        ShowBackground = config.Bind(section, "Show Empty Background", true,
            "Dynamic mode: draw the unfilled part of the bar.");
        Background = new ColorSettings(config, section, "Background ", new Color32(0, 0, 0, 120));
        Outline = new OutlineSettings(config, section);
    }

    internal void Register()
    {
        ModSettingsManager.AddOption(new CheckBoxOption(Enabled));
        ModSettingsManager.AddOption(new ChoiceOption(Mode));
        ModSettingsManager.AddOption(new ChoiceOption(Orientation));
        ModSettingsManager.AddOption(new ChoiceOption(FillFrom));
        ModSettingsManager.AddOption(new ChoiceOption(Placement));
        IndicatorDistance.Register(Distance);
        CrosshairConfig.AddFloatOption(Length, 4f, MaxLength);
        CrosshairConfig.AddFloatOption(Thickness, 1f, MaxThickness);
        NotReady.Register();
        Ready.Register();
        ModSettingsManager.AddOption(new CheckBoxOption(ShowBackground));
        Background.Register();
        Outline.Register();
    }
}

/// <summary>Generic ammo / stock / count indicator ("Count Indicator" tab).</summary>
internal sealed class CountIndicatorSettings
{
    internal const float MaxDotSize = 30f;
    internal const float MaxBarLength = 60f;
    internal const float MaxBarThickness = 30f;
    internal const float MaxSpacing = 40f;
    internal const float MaxFontSize = 64f;

    internal readonly ConfigEntry<bool> Enabled;
    internal readonly ConfigEntry<CountDisplayMode> Mode;
    internal readonly ConfigEntry<IndicatorPlacement> Placement;
    internal readonly ConfigEntry<float> Distance;
    internal readonly ConfigEntry<CountLayout> Layout;
    internal readonly ConfigEntry<float> Spacing;
    internal readonly ConfigEntry<float> DotSize;
    internal readonly ConfigEntry<BarOrientation> BarShape;
    internal readonly ConfigEntry<float> BarLength;
    internal readonly ConfigEntry<float> BarThickness;
    internal readonly ConfigEntry<CountNumberFormat> NumberFormat;
    internal readonly ConfigEntry<float> FontSize;
    internal readonly ColorSettings Fill;
    internal readonly OutlineSettings Outline;

    internal CountIndicatorSettings(ConfigFile config, string section)
    {
        Enabled = config.Bind(section, "Enabled", true,
            "Show ammo / stock count indicators (e.g. MUL-T Scrap Launcher stock).");
        Mode = config.Bind(section, "Mode", CountDisplayMode.Dots, "Dots, a number, or one bar per available count.");
        Placement = config.Bind(section, "Placement", IndicatorPlacement.Below,
            "Mod-defined slot around the crosshair. Several indicators on one side stack automatically.");
        Distance = IndicatorDistance.Bind(config, section);
        Layout = config.Bind(section, "Layout", CountLayout.Horizontal, "Dots/Bars: a horizontal row or a vertical column.");
        Spacing = CrosshairConfig.BindFloat(config, section, "Spacing", 3f, 0f, MaxSpacing,
            "Dots/Bars: gap between elements, in pixels at 1920x1080.");
        DotSize = CrosshairConfig.BindFloat(config, section, "Dot Size", 4f, 1f, MaxDotSize,
            "Dots: diameter, in pixels at 1920x1080.");
        BarShape = config.Bind(section, "Bar Shape", BarOrientation.Vertical, "Bars: each element is a vertical or horizontal bar.");
        BarLength = CrosshairConfig.BindFloat(config, section, "Bar Length", 8f, 1f, MaxBarLength,
            "Bars: element length along its shape, in pixels at 1920x1080.");
        BarThickness = CrosshairConfig.BindFloat(config, section, "Bar Thickness", 3f, 1f, MaxBarThickness,
            "Bars: element thickness across its shape, in pixels at 1920x1080.");
        NumberFormat = config.Bind(section, "Number Format", CountNumberFormat.Current, "Number: current count, or current / maximum.");
        FontSize = CrosshairConfig.BindFloat(config, section, "Font Size", 14f, 6f, MaxFontSize,
            "Number: text size, in pixels at 1920x1080.");
        Fill = new ColorSettings(config, section, "", new Color32(255, 255, 255, 255));
        Outline = new OutlineSettings(config, section);
    }

    internal void Register()
    {
        ModSettingsManager.AddOption(new CheckBoxOption(Enabled));
        ModSettingsManager.AddOption(new ChoiceOption(Mode));
        ModSettingsManager.AddOption(new ChoiceOption(Placement));
        IndicatorDistance.Register(Distance);
        ModSettingsManager.AddOption(new ChoiceOption(Layout));
        CrosshairConfig.AddFloatOption(Spacing, 0f, MaxSpacing);
        CrosshairConfig.AddFloatOption(DotSize, 1f, MaxDotSize);
        ModSettingsManager.AddOption(new ChoiceOption(BarShape));
        CrosshairConfig.AddFloatOption(BarLength, 1f, MaxBarLength);
        CrosshairConfig.AddFloatOption(BarThickness, 1f, MaxBarThickness);
        ModSettingsManager.AddOption(new ChoiceOption(NumberFormat));
        CrosshairConfig.AddFloatOption(FontSize, 6f, MaxFontSize);
        Fill.Register();
        Outline.Register();
    }
}
