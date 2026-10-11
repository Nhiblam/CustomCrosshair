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
    /// </summary>
    internal static GameObject? BuildStockPreserving(GameObject vanillaCrosshair, Transform holder)
    {
        GameObject template = Clone(vanillaCrosshair, holder, "RoR2CustomCrosshair_" + vanillaCrosshair.name, out CrosshairController controller);
        Transform root = template.transform;

        var kept = new List<CrosshairController.SkillStockSpriteDisplay>();
        foreach (CrosshairController.SkillStockSpriteDisplay display in
                 controller.skillStockSpriteDisplays ?? Array.Empty<CrosshairController.SkillStockSpriteDisplay>())
        {
            if (display.target && !IsAtCenter(root, display.target.transform))
            {
                kept.Add(display);
            }
        }

        if (kept.Count == 0)
        {
            UnityEngine.Object.Destroy(template);
            return null;
        }

        controller.spriteSpreadPositions = Array.Empty<CrosshairController.SpritePosition>();
        controller.remapSprites = Array.Empty<RawImage>();
        controller.skillStockSpriteDisplays = kept.ToArray();

        // Keep each kept display and its parents; hide every other branch.
        var keep = new HashSet<Transform>();
        foreach (CrosshairController.SkillStockSpriteDisplay display in kept)
        {
            for (Transform t = display.target.transform; t != null && t != root; t = t.parent)
            {
                keep.Add(t);
            }
        }

        HideVanillaVisuals(template, keep);
        AddProceduralCrosshair(template);

        Plugin.Log.LogDebug(
            $"{vanillaCrosshair.name}: keeping vanilla stock displays " +
            string.Join(", ", kept.Select(d => d.target.name)) + "; spread artwork removed.");
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
