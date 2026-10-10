using System;
using System.Linq;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace RoR2CustomCrosshair;

/// <summary>
/// Builds the crosshair template handed to <see cref="CrosshairManager"/>.
/// The template is a clone of the game's StandardCrosshair so it keeps the
/// HudElement + CrosshairController pair the manager relies on (the manager
/// only destroys a previous crosshair through its CrosshairController).
/// </summary>
internal static class CrosshairPrefabFactory
{
    // Verified against the installed Addressables catalog (game build 2026-10-08).
    private const string StandardCrosshairAddress = "RoR2/Base/UI/StandardCrosshair.prefab";

    internal const string ProceduralRootName = "ProceduralCrosshair";

    internal const string CircleLayerName = "Circle";
    internal const string OuterBarsLayerName = "OuterBars";
    internal const string InnerBarsLayerName = "InnerBars";
    internal const string CenterDotLayerName = "CenterDot";

    /// <summary>
    /// Loads the standard crosshair asynchronously and invokes
    /// <paramref name="onCreated"/> with the template, or null on failure.
    /// </summary>
    internal static void CreateAsync(Transform holder, Action<GameObject?> onCreated)
    {
        AsyncOperationHandle<GameObject> handle =
            Addressables.LoadAssetAsync<GameObject>(StandardCrosshairAddress);

        handle.Completed += op =>
        {
            if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
            {
                Plugin.Log.LogError(
                    $"Could not load '{StandardCrosshairAddress}': {op.OperationException}");
                onCreated(null);
                return;
            }

            try
            {
                onCreated(Build(op.Result, holder));
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"Could not build crosshair template: {exception}");
                onCreated(null);
            }
        };
    }

    private static GameObject Build(GameObject standardCrosshair, Transform holder)
    {
        // The holder is inactive, so Awake does not run on the template itself.
        GameObject template = UnityEngine.Object.Instantiate(standardCrosshair, holder, false);
        template.name = "RoR2CustomCrosshair";

        Plugin.Log.LogDebug(
            "StandardCrosshair components: " +
            string.Join(", ", template.GetComponents<Component>().Select(c => c.GetType().Name)));

        if (!template.TryGetComponent(out CrosshairController controller) ||
            !template.TryGetComponent(out HudElement _))
        {
            throw new InvalidOperationException(
                "StandardCrosshair no longer has CrosshairController + HudElement.");
        }

        // Stop the controller from driving the vanilla sprites (spread
        // positions, remap alpha, skill stock toggles). Our procedural arms
        // ignore spread in V1.
        controller.spriteSpreadPositions = Array.Empty<CrosshairController.SpritePosition>();
        controller.skillStockSpriteDisplays = Array.Empty<CrosshairController.SkillStockSpriteDisplay>();
        controller.remapSprites = Array.Empty<RawImage>();

        // Hide vanilla visuals without destroying anything another component
        // might still reference.
        foreach (Transform child in template.transform)
        {
            child.gameObject.SetActive(false);
        }

        foreach (Graphic graphic in template.GetComponents<Graphic>())
        {
            graphic.enabled = false;
        }

        RectTransform root = CreateRect(ProceduralRootName, template.transform);

        // Sibling order is draw order, bottom to top. Every layer is centered
        // on the root's pivot; ProceduralCrosshairView positions the parts.
        CircleLayer.Build(root, CircleLayerName);
        BarsLayer.Build(root, OuterBarsLayerName);
        BarsLayer.Build(root, InnerBarsLayerName);
        CenterDotLayer.Build(root, CenterDotLayerName);

        template.AddComponent<ProceduralCrosshairView>();

        return template;
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
