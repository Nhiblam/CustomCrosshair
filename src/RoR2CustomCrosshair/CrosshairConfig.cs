using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// BepInEx persistence plus RiskOfOptions registration. Each layer is one
/// config section, which RiskOfOptions shows as its own category tab.
/// Rendering code only reads the entries and compares <see cref="Revision"/>.
/// </summary>
internal static class CrosshairConfig
{
    private const int CurrentConfigVersion = 2;
    private const float SliderStep = 0.5f;

    internal static ConfigEntry<bool> Enabled = null!;

    internal static BarsSettings InnerBars = null!;
    internal static BarsSettings OuterBars = null!;
    internal static CircleSettings Circle = null!;
    internal static DotSettings CenterDot = null!;

    /// <summary>Incremented whenever any setting changes.</summary>
    internal static int Revision { get; private set; }

    internal static void Bind(ConfigFile config)
    {
        bool saveOnSet = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;

        Enabled = config.Bind(
            "General", "Enabled", true,
            "Replace the ordinary crosshair of your own survivor with the custom crosshair.");

        InnerBars = new BarsSettings(config, "Inner Bars", new BarsDefaults
        {
            Enabled = true, Length = 8f, Thickness = 2f, Gap = 4f, GapMax = 64f,
            Fill = new Color32(0, 255, 0, 255)
        });

        OuterBars = new BarsSettings(config, "Outer Bars", new BarsDefaults
        {
            Enabled = false, Length = 10f, Thickness = 2f, Gap = 14f, GapMax = 128f,
            Fill = new Color32(255, 255, 255, 255)
        });

        Circle = new CircleSettings(config, "Circle");
        CenterDot = new DotSettings(config, "Center Dot");

        MigrateFromVersion1(config);

        config.Save();
        config.SaveOnConfigSet = saveOnSet;

        // Any change bumps Revision; SaveOnConfigSet persists it to disk.
        config.SettingChanged += (_, _) => Revision++;
    }

    internal static void RegisterRiskOfOptions()
    {
        ModSettingsManager.SetModDescription(
            "Procedural crosshair with Inner Bars, Outer Bars, Circle and Center Dot layers. " +
            "Sizes are pixels at 1920x1080 and scale with resolution.");

        ModSettingsManager.AddOption(new CheckBoxOption(Enabled));

        InnerBars.Register();
        OuterBars.Register();
        Circle.Register();
        CenterDot.Register();
    }

    /// <summary>
    /// Version 1 had a single crosshair in sections "Crosshair",
    /// "Crosshair Color", "Outline" and "Outline Color". Those values become
    /// the Inner Bars settings, then the old keys are removed from the file.
    /// Runs once: the stored version is 0 for version-1 files and for new
    /// files (where copying the old defaults equals the Inner Bars defaults).
    /// </summary>
    private static void MigrateFromVersion1(ConfigFile config)
    {
        ConfigEntry<int> version = config.Bind(
            "Internal", "Config Version", 0,
            "Settings layout version, used for migration. Do not edit.");

        if (version.Value >= CurrentConfigVersion)
        {
            return;
        }

        BarsSettings inner = InnerBars;
        Move(config, "Crosshair", "Length", inner.Length);
        Move(config, "Crosshair", "Thickness", inner.Thickness);
        Move(config, "Crosshair", "Gap", inner.Gap);
        Move(config, "Crosshair Color", "Opacity", inner.Fill.Opacity);
        Move(config, "Crosshair Color", "R", inner.Fill.Red);
        Move(config, "Crosshair Color", "G", inner.Fill.Green);
        Move(config, "Crosshair Color", "B", inner.Fill.Blue);
        Move(config, "Outline", "Enabled", inner.Outline.Enabled);
        Move(config, "Outline", "Thickness", inner.Outline.Thickness);
        Move(config, "Outline Color", "Opacity", inner.Outline.Color.Opacity);
        Move(config, "Outline Color", "R", inner.Outline.Color.Red);
        Move(config, "Outline Color", "G", inner.Outline.Color.Green);
        Move(config, "Outline Color", "B", inner.Outline.Color.Blue);

        version.Value = CurrentConfigVersion;
        Plugin.Log.LogInfo("Migrated crosshair settings to the layered layout (old values now under 'Inner Bars').");
    }

    private static void Move<T>(ConfigFile config, string oldSection, string oldKey, ConfigEntry<T> target)
    {
        // Binding picks up the old value from the file if it is there.
        ConfigEntry<T> old = config.Bind(oldSection, oldKey, (T)target.DefaultValue);
        target.Value = old.Value;
        config.Remove(old.Definition);
    }

    internal static ConfigEntry<float> BindFloat(
        ConfigFile config, string section, string key, float defaultValue, float min, float max, string description)
    {
        return config.Bind(section, key, defaultValue,
            new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
    }

    internal static ConfigEntry<int> BindByte(ConfigFile config, string section, string key, int defaultValue)
    {
        return config.Bind(section, key, defaultValue,
            new ConfigDescription($"{key} value from 0 to 255.", new AcceptableValueRange<int>(0, 255)));
    }

    internal static void AddFloatOption(ConfigEntry<float> entry, float min, float max)
    {
        // Step sliders submit while dragging, so the crosshair updates live.
        ModSettingsManager.AddOption(new StepSliderOption(entry, new StepSliderConfig
        {
            min = min,
            max = max,
            increment = SliderStep,
            FormatString = "{0:0.0}"
        }));
    }

    internal static void AddByteOption(ConfigEntry<int> entry)
    {
        ModSettingsManager.AddOption(new IntSliderOption(entry, new IntSliderConfig { min = 0, max = 255 }));
    }
}

internal sealed class ColorSettings
{
    internal readonly ConfigEntry<int> Red;
    internal readonly ConfigEntry<int> Green;
    internal readonly ConfigEntry<int> Blue;
    internal readonly ConfigEntry<int> Opacity;

    internal ColorSettings(ConfigFile config, string section, string keyPrefix, Color32 defaults)
    {
        Red = CrosshairConfig.BindByte(config, section, keyPrefix + "R", defaults.r);
        Green = CrosshairConfig.BindByte(config, section, keyPrefix + "G", defaults.g);
        Blue = CrosshairConfig.BindByte(config, section, keyPrefix + "B", defaults.b);
        Opacity = CrosshairConfig.BindByte(config, section, keyPrefix + "Opacity", defaults.a);
    }

    internal Color Value => new(Red.Value / 255f, Green.Value / 255f, Blue.Value / 255f, Opacity.Value / 255f);

    internal void Register()
    {
        CrosshairConfig.AddByteOption(Red);
        CrosshairConfig.AddByteOption(Green);
        CrosshairConfig.AddByteOption(Blue);
        CrosshairConfig.AddByteOption(Opacity);
    }
}

internal sealed class OutlineSettings
{
    internal const float MaxThickness = 10f;

    internal readonly ConfigEntry<bool> Enabled;
    internal readonly ConfigEntry<float> Thickness;
    internal readonly ColorSettings Color;

    internal OutlineSettings(ConfigFile config, string section)
    {
        Enabled = config.Bind(section, "Outline Enabled", true, "Draw a border around this layer.");
        Thickness = CrosshairConfig.BindFloat(config, section, "Outline Thickness", 1f, 0f, MaxThickness,
            "Border width on each side, in pixels at 1920x1080.");
        Color = new ColorSettings(config, section, "Outline ", new Color32(0, 0, 0, 255));
    }

    internal bool Visible => Enabled.Value && Thickness.Value > 0.001f;

    internal void Register()
    {
        ModSettingsManager.AddOption(new CheckBoxOption(Enabled));
        CrosshairConfig.AddFloatOption(Thickness, 0f, MaxThickness);
        Color.Register();
    }
}

internal struct BarsDefaults
{
    internal bool Enabled;
    internal float Length;
    internal float Thickness;
    internal float Gap;
    internal float GapMax;
    internal Color32 Fill;
}

/// <summary>Four arms around the center (Inner Bars and Outer Bars).</summary>
internal sealed class BarsSettings
{
    internal const float MaxLength = 64f;
    internal const float MaxThickness = 20f;

    private readonly float _gapMax;

    internal readonly ConfigEntry<bool> Enabled;
    internal readonly ConfigEntry<CrosshairMode> Mode;
    internal readonly ConfigEntry<float> Length;
    internal readonly ConfigEntry<float> Thickness;
    internal readonly ConfigEntry<float> Gap;
    internal readonly ColorSettings Fill;
    internal readonly OutlineSettings Outline;

    internal BarsSettings(ConfigFile config, string section, BarsDefaults defaults)
    {
        _gapMax = defaults.GapMax;

        Enabled = config.Bind(section, "Enabled", defaults.Enabled, $"Show the {section}.");
        Mode = config.Bind(section, "Mode", CrosshairMode.Static,
            "Static: fixed gap. Dynamic: gap will follow actual weapon spread (not implemented yet; currently same as Static).");
        Length = CrosshairConfig.BindFloat(config, section, "Length", defaults.Length, 0.5f, MaxLength,
            "Length of each arm, in pixels at 1920x1080.");
        Thickness = CrosshairConfig.BindFloat(config, section, "Thickness", defaults.Thickness, 0.5f, MaxThickness,
            "Thickness of each arm, in pixels at 1920x1080.");
        Gap = CrosshairConfig.BindFloat(config, section, "Gap", defaults.Gap, 0f, _gapMax,
            "Distance from screen center to the inner edge of each arm, in pixels at 1920x1080.");
        Fill = new ColorSettings(config, section, "", defaults.Fill);
        Outline = new OutlineSettings(config, section);
    }

    internal void Register()
    {
        ModSettingsManager.AddOption(new CheckBoxOption(Enabled));
        ModSettingsManager.AddOption(new ChoiceOption(Mode));
        CrosshairConfig.AddFloatOption(Length, 0.5f, MaxLength);
        CrosshairConfig.AddFloatOption(Thickness, 0.5f, MaxThickness);
        CrosshairConfig.AddFloatOption(Gap, 0f, _gapMax);
        Fill.Register();
        Outline.Register();
    }
}

/// <summary>Hollow ring around the center.</summary>
internal sealed class CircleSettings
{
    internal const float MaxRadius = 128f;
    internal const float MaxThickness = 20f;

    internal readonly ConfigEntry<bool> Enabled;
    internal readonly ConfigEntry<CrosshairMode> Mode;
    internal readonly ConfigEntry<float> Radius;
    internal readonly ConfigEntry<float> Thickness;
    internal readonly ColorSettings Fill;
    internal readonly OutlineSettings Outline;

    internal CircleSettings(ConfigFile config, string section)
    {
        Enabled = config.Bind(section, "Enabled", false, "Show the circle.");
        Mode = config.Bind(section, "Mode", CrosshairMode.Static,
            "Static: fixed radius. Dynamic: radius will follow actual weapon spread (not implemented yet; currently same as Static).");
        Radius = CrosshairConfig.BindFloat(config, section, "Radius", 16f, 0f, MaxRadius,
            "Distance from screen center to the inner edge of the ring, in pixels at 1920x1080.");
        Thickness = CrosshairConfig.BindFloat(config, section, "Thickness", 2f, 0.5f, MaxThickness,
            "Ring thickness, in pixels at 1920x1080.");
        Fill = new ColorSettings(config, section, "", new Color32(255, 255, 255, 255));
        Outline = new OutlineSettings(config, section);
    }

    internal void Register()
    {
        ModSettingsManager.AddOption(new CheckBoxOption(Enabled));
        ModSettingsManager.AddOption(new ChoiceOption(Mode));
        CrosshairConfig.AddFloatOption(Radius, 0f, MaxRadius);
        CrosshairConfig.AddFloatOption(Thickness, 0.5f, MaxThickness);
        Fill.Register();
        Outline.Register();
    }
}

/// <summary>Square dot at the center. Always static.</summary>
internal sealed class DotSettings
{
    internal const float MaxSize = 20f;

    internal readonly ConfigEntry<bool> Enabled;
    internal readonly ConfigEntry<float> Size;
    internal readonly ColorSettings Fill;
    internal readonly OutlineSettings Outline;

    internal DotSettings(ConfigFile config, string section)
    {
        Enabled = config.Bind(section, "Enabled", false, "Show the center dot.");
        Size = CrosshairConfig.BindFloat(config, section, "Size", 2f, 0.5f, MaxSize,
            "Width and height of the dot, in pixels at 1920x1080.");
        Fill = new ColorSettings(config, section, "", new Color32(0, 255, 0, 255));
        Outline = new OutlineSettings(config, section);
    }

    internal void Register()
    {
        ModSettingsManager.AddOption(new CheckBoxOption(Enabled));
        CrosshairConfig.AddFloatOption(Size, 0.5f, MaxSize);
        Fill.Register();
        Outline.Register();
    }
}
