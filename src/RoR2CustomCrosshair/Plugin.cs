using BepInEx;
using BepInEx.Logging;
using RoR2.UI;
using UnityEngine;

namespace RoR2CustomCrosshair;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.rune580.riskofoptions")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.example.ror2customcrosshair";
    public const string PluginName = "RoR2 Custom Crosshair";
    public const string PluginVersion = "0.2.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private GameObject? _prefabHolder;

    private void Awake()
    {
        Log = Logger;

        CrosshairConfig.Bind(Config);
        CrosshairConfig.RegisterRiskOfOptions();

        _prefabHolder = new GameObject("RoR2CustomCrosshair_Templates");
        _prefabHolder.SetActive(false);
        DontDestroyOnLoad(_prefabHolder);

        CrosshairDisplayPolicy.Initialize(_prefabHolder.transform);

        // HUDs are created per local user by CameraRigController; plugins load
        // at startup, before any HUD exists.
        On.RoR2.UI.HUD.Awake += HUD_Awake;

        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private static void HUD_Awake(On.RoR2.UI.HUD.orig_Awake orig, HUD self)
    {
        orig(self);
        HudCrosshairDriver.Attach(self);
    }

    private void OnDestroy()
    {
        On.RoR2.UI.HUD.Awake -= HUD_Awake;

        foreach (HudCrosshairDriver driver in FindObjectsOfType<HudCrosshairDriver>())
        {
            Destroy(driver);
        }

        CrosshairDisplayPolicy.Shutdown();
        if (_prefabHolder != null)
        {
            Destroy(_prefabHolder);
        }
    }
}
