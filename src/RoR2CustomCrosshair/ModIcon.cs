using System;
using System.IO;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Loads the package icon (repository icon.png, embedded in the assembly)
/// as a Sprite for the RiskOfOptions mod list. Created once per session;
/// RiskOfOptions keeps referencing it for as long as the game runs.
/// </summary>
internal static class ModIcon
{
    private const string ResourceName = "RoR2CustomCrosshair.icon.png";

    private static Sprite? _sprite;

    /// <summary>The icon sprite, or null (with a logged warning) if it could not be loaded.</summary>
    internal static Sprite? Load()
    {
        if (_sprite != null)
        {
            return _sprite;
        }

        try
        {
            byte[]? bytes = ReadResource();
            if (bytes == null)
            {
                Plugin.Log.LogWarning($"Embedded resource '{ResourceName}' not found; RiskOfOptions will show its default icon.");
                return null;
            }

            // Mipmaps keep the 256x256 image clean when the menu draws it small.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "RoR2CustomCrosshairIcon",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // markNonReadable frees the CPU-side copy once uploaded to the GPU.
            if (!texture.LoadImage(bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                Plugin.Log.LogWarning("Could not decode the embedded icon; RiskOfOptions will show its default icon.");
                return null;
            }

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            sprite.name = texture.name;

            // Runtime-created assets have no scene owner; keep them alive
            // through Resources.UnloadUnusedAssets on scene changes.
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;

            _sprite = sprite;
            return _sprite;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Could not load the mod icon; RiskOfOptions will show its default icon. {exception}");
            return null;
        }
    }

    private static byte[]? ReadResource()
    {
        using Stream? stream = typeof(ModIcon).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
