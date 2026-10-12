using System;
using System.Collections.Generic;
using System.Linq;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace RoR2CustomCrosshair;

/// <summary>
/// Builds the crosshair templates handed to <see cref="CrosshairManager"/>.
/// Every template is a clone of a game crosshair so it keeps the HudElement +
/// CrosshairController pair the manager relies on (the manager only
/// destroys a previous crosshair through its CrosshairController).
/// </summary>
internal static class CrosshairPrefabFactory
{
    internal const string ProceduralRootName = "ProceduralCrosshair";

    internal const string CircleLayerName = "Circle";
    internal const string OuterBarsLayerName = "OuterBars";
    internal const string InnerBarsLayerName = "InnerBars";
    internal const string CenterDotLayerName = "CenterDot";
    internal const string IndicatorLayerName = "Indicators";

    /// <summary>True if the generic indicator layer failed to build (vanilla indicators are then kept).</summary>
    internal static bool IndicatorsUnavailable { get; private set; }

    /// <summary>Loads a prefab asynchronously; invokes <paramref name="onLoaded"/> with it or null.</summary>
    internal static void LoadAsync(string address, Action<GameObject?> onLoaded)
    {
        AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(address);
        handle.Completed += op =>
        {
            if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
            {
                Plugin.Log.LogError($"Could not load '{address}': {op.OperationException}");
                onLoaded(null);
                return;
            }

            onLoaded(op.Result);
        };
    }

    /// <summary>
    /// The general template: StandardCrosshair with every vanilla visual and
    /// controller behavior removed, plus the procedural crosshair.
    /// </summary>
    internal static GameObject BuildStandard(GameObject standardCrosshair, Transform holder)
    {
        GameObject template = Clone(standardCrosshair, holder, "RoR2CustomCrosshair", out CrosshairController controller);

        Plugin.Log.LogDebug(
            "StandardCrosshair components: " +
            string.Join(", ", template.GetComponents<Component>().Select(c => c.GetType().Name)));

        // Stop the controller from driving the vanilla sprites (spread
        // positions, remap alpha, skill stock toggles).
        controller.spriteSpreadPositions = Array.Empty<CrosshairController.SpritePosition>();
        controller.skillStockSpriteDisplays = Array.Empty<CrosshairController.SkillStockSpriteDisplay>();
        controller.remapSprites = Array.Empty<RawImage>();

        HideVanillaVisuals(template, new HashSet<Transform>());
        AddProceduralCrosshair(template);
        return template;
    }

    /// <summary>
    /// A template that keeps a vanilla crosshair's off-center skill-stock
    /// displays (e.g. Bandit's ammo dots) exactly as the game draws them:
    /// same objects, same positions, still toggled by the vanilla
    /// CrosshairController from the skill's stock. Its spread artwork and
    /// anything at the crosshair center (owned by the custom crosshair) are
    /// removed. Returns null when there is no such display to keep.
    ///
    /// With <paramref name="keepSpreadArt"/> the spread-driven art is kept and
    /// still driven by the controller: used only while a skill writes its
    /// charge into bloom, where that art is the vanilla charge display (not
    /// spread). Returns null when there is nothing to keep.
    ///
    /// Without <paramref name="keepCounts"/> the count pips (displays shown
    /// from stock 1 up) are dropped because the generic Count Indicator shows
    /// that count; zero-stock status displays are still kept.
    /// </summary>
    internal static GameObject? BuildStockPreserving(
        GameObject vanillaCrosshair, Transform holder, bool keepSpreadArt = false, bool keepCounts = true)
    {
        string suffix = keepSpreadArt ? "_ChargeDisplay" : keepCounts ? "" : "_CountSuperseded";
        GameObject template = Clone(vanillaCrosshair, holder, "RoR2CustomCrosshair_" + vanillaCrosshair.name + suffix, out CrosshairController controller);
        Transform root = template.transform;

        var kept = new List<CrosshairController.SkillStockSpriteDisplay>();
        foreach (CrosshairController.SkillStockSpriteDisplay display in
                 controller.skillStockSpriteDisplays ?? Array.Empty<CrosshairController.SkillStockSpriteDisplay>())
        {
            if (display.target && !IsAtCenter(root, display.target.transform) && (keepCounts || !IsCountDisplay(display)))
            {
                kept.Add(display);
            }
        }

        var keptSpread = new List<CrosshairController.SpritePosition>();
        if (keepSpreadArt)
        {
            foreach (CrosshairController.SpritePosition spread in
                     controller.spriteSpreadPositions ?? Array.Empty<CrosshairController.SpritePosition>())
            {
                if (spread.target)
                {
                    keptSpread.Add(spread);
                }
            }
        }

        if (kept.Count == 0 && keptSpread.Count == 0)
        {
            UnityEngine.Object.Destroy(template);
            return null;
        }

        controller.spriteSpreadPositions = keptSpread.ToArray();
        controller.remapSprites = Array.Empty<RawImage>();
        controller.skillStockSpriteDisplays = kept.ToArray();

        // Keep each kept display / spread target and its parents; hide every other branch.
        var keep = new HashSet<Transform>();
        foreach (Transform target in kept.Select(d => d.target.transform).Concat(keptSpread.Select(s => (Transform)s.target)))
        {
            for (Transform t = target; t != null && t != root; t = t.parent)
            {
                keep.Add(t);
            }
        }

        HideVanillaVisuals(template, keep);
        AddProceduralCrosshair(template);

        Plugin.Log.LogDebug(
            $"{vanillaCrosshair.name}{suffix}: keeping vanilla stock displays [" +
            string.Join(", ", kept.Select(d => d.target.name)) + "]" +
            (keepSpreadArt ? $" and charge-display art [{string.Join(", ", keptSpread.Select(s => s.target.name))}]" : "; spread artwork removed") + ".");
        return template;
    }

    // Components that only draw, lay out or animate crosshair art.
    private static readonly HashSet<Type> PresentationTypes = new()
    {
        typeof(Image), typeof(RawImage), typeof(HudElement), typeof(CrosshairController),
        typeof(AspectRatioFitter), typeof(LayoutElement), typeof(ContentSizeFitter),
        typeof(HorizontalLayoutGroup), typeof(VerticalLayoutGroup), typeof(GridLayoutGroup),
        typeof(Shadow), typeof(Outline)
    };

    // Vanilla reticle sprites are at most ~100 UI units; anything bigger is an overlay.
    private const float LargeArtUnits = 300f;

    // Animation-only components used on vanilla crosshair art (matched by name
    // because they live in different game assemblies).
    private static readonly HashSet<string> PresentationTypeNames = new()
    {
        "ObjectScaleCurve", "RotateAroundAxis"
    };

    /// <summary>
    /// True when a vanilla crosshair is only reticle art: no skill-stock
    /// displays and no components beyond drawing, layout and animation.
    /// Such crosshairs get the standard template. Otherwise
    /// <paramref name="extra"/> lists what it carries beyond art.
    /// </summary>
    internal static bool IsPureReticle(GameObject vanilla, out string extra)
    {
        var found = new List<string>();
        if (vanilla.TryGetComponent(out CrosshairController controller) &&
            controller.skillStockSpriteDisplays is { Length: > 0 } stocks)
        {
            found.Add($"{stocks.Length} skill-stock display(s)");
        }

        foreach (MonoBehaviour behaviour in vanilla.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null &&
                !PresentationTypes.Contains(behaviour.GetType()) &&
                !PresentationTypeNames.Contains(behaviour.GetType().Name))
            {
                found.Add($"{behaviour.GetType().Name} on '{behaviour.name}'");
            }
        }

        // Large or screen-stretched art (scope frames, vignettes) is not a
        // reticle even when it is only images; spread-driven art is.
        var spreadArt = new HashSet<Transform>();
        if (controller != null)
        {
            foreach (CrosshairController.SpritePosition spread in
                     controller.spriteSpreadPositions ?? Array.Empty<CrosshairController.SpritePosition>())
            {
                if (spread.target)
                {
                    spreadArt.Add(spread.target);
                }
            }
        }

        foreach (Graphic graphic in vanilla.GetComponentsInChildren<Graphic>(true))
        {
            RectTransform rect = graphic.rectTransform;
            if (spreadArt.Contains(rect))
            {
                continue;
            }

            if (rect.anchorMin != rect.anchorMax ||
                Mathf.Abs(rect.sizeDelta.x) > LargeArtUnits ||
                Mathf.Abs(rect.sizeDelta.y) > LargeArtUnits)
            {
                found.Add($"large art '{graphic.name}'");
            }
        }

        extra = string.Join(", ", found.Distinct());
        return found.Count == 0;
    }

    /// <summary>
    /// A template for a vanilla crosshair that carries more than reticle art
    /// (indicators, scope overlays, custom controllers) but has not been
    /// curated: only its spread-driven art and root graphic are removed;
    /// everything else stays exactly as the game draws it, under the
    /// custom reticle.
    ///
    /// With <paramref name="stripCounts"/> its count pips (stock displays
    /// shown from stock 1 up) are removed too, because the generic Count
    /// Indicator shows that count; zero-stock status displays (e.g. a
    /// "ready" prompt) stay driven by the controller.
    /// </summary>
    internal static GameObject BuildOverlay(GameObject vanillaCrosshair, Transform holder, bool stripCounts = false)
    {
        string suffix = stripCounts ? "_CountSuperseded" : "";
        GameObject template = Clone(vanillaCrosshair, holder, "RoR2CustomCrosshair_" + vanillaCrosshair.name + suffix, out CrosshairController controller);

        if (stripCounts)
        {
            var kept = new List<CrosshairController.SkillStockSpriteDisplay>();
            foreach (CrosshairController.SkillStockSpriteDisplay display in
                     controller.skillStockSpriteDisplays ?? Array.Empty<CrosshairController.SkillStockSpriteDisplay>())
            {
                if (!display.target)
                {
                    continue;
                }

                if (IsCountDisplay(display))
                {
                    display.target.SetActive(false);
                }
                else
                {
                    kept.Add(display);
                }
            }

            controller.skillStockSpriteDisplays = kept.ToArray();
        }

        foreach (CrosshairController.SpritePosition spread in
                 controller.spriteSpreadPositions ?? Array.Empty<CrosshairController.SpritePosition>())
        {
            if (spread.target)
            {
                spread.target.gameObject.SetActive(false);
            }
        }

        foreach (RawImage remap in controller.remapSprites ?? Array.Empty<RawImage>())
        {
            if (remap)
            {
                remap.enabled = false;
            }
        }

        controller.spriteSpreadPositions = Array.Empty<CrosshairController.SpritePosition>();
        controller.remapSprites = Array.Empty<RawImage>();

        foreach (Graphic graphic in template.GetComponents<Graphic>())
        {
            graphic.enabled = false;
        }

        AddProceduralCrosshair(template);
        return template;
    }

    private static GameObject Clone(GameObject source, Transform holder, string name, out CrosshairController controller)
    {
        // The holder is inactive, so Awake does not run on the template itself.
        GameObject template = UnityEngine.Object.Instantiate(source, holder, false);
        template.name = name;

        if (!template.TryGetComponent(out controller) || !template.TryGetComponent(out HudElement _))
        {
            UnityEngine.Object.Destroy(template);
            throw new InvalidOperationException($"{source.name} no longer has CrosshairController + HudElement.");
        }

        return template;
    }

    /// <summary>A stock display that is one pip of a count (shown from stock 1 up), not a zero-stock status.</summary>
    private static bool IsCountDisplay(CrosshairController.SkillStockSpriteDisplay display) =>
        display.minimumStockCountToBeValid >= 1;

    private static bool IsAtCenter(Transform root, Transform target)
    {
        return root.InverseTransformPoint(target.position).sqrMagnitude < 1f;
    }

    /// <summary>
    /// Deactivates vanilla child branches not in <paramref name="keep"/> and
    /// disables root graphics, without destroying anything another component
    /// might still reference.
    /// </summary>
    private static void HideVanillaVisuals(GameObject template, HashSet<Transform> keep)
    {
        foreach (Transform child in template.transform)
        {
            if (!keep.Contains(child))
            {
                child.gameObject.SetActive(false);
            }
        }

        foreach (Graphic graphic in template.GetComponents<Graphic>())
        {
            graphic.enabled = false;
        }
    }

    private static void AddProceduralCrosshair(GameObject template)
    {
        RectTransform root = CreateRect(ProceduralRootName, template.transform);

        // Generic indicators sit underneath the reticle layers so the reticle
        // stays dominant. If they cannot be built, the template works without
        // them and the policy keeps the vanilla information instead.
        try
        {
            IndicatorLayer.Build(root, IndicatorLayerName);
        }
        catch (Exception exception)
        {
            IndicatorsUnavailable = true;
            Transform partial = root.Find(IndicatorLayerName);
            if (partial != null)
            {
                UnityEngine.Object.Destroy(partial.gameObject);
            }

            Plugin.Log.LogError($"Could not build indicator layer; vanilla indicators will be kept: {exception}");
        }

        // Sibling order is draw order, bottom to top. Every layer is centered
        // on the root's pivot; ProceduralCrosshairView positions the parts.
        CircleLayer.Build(root, CircleLayerName);
        BarsLayer.Build(root, OuterBarsLayerName);
        BarsLayer.Build(root, InnerBarsLayerName);
        CenterDotLayer.Build(root, CenterDotLayerName);

        template.AddComponent<ProceduralCrosshairView>();
    }

    /// <summary>A zero-size RectTransform whose pivot sits on its parent's center.</summary>
    internal static RectTransform CreateRect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)obj.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    internal static void CreateImage(string name, Transform parent)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();

        // An Image without a sprite renders as a solid quad tinted by color.
        // Created disabled: a new GameObject's RectTransform is 100x100 and an
        // Image defaults to white, which is exactly what flashed for one frame
        // before. ProceduralCrosshairView enables it on its first Apply.
        Image image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.maskable = false;
        image.color = Color.clear;
        image.enabled = false;
    }
}
