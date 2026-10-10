using RoR2;
using RoR2.UI;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Lives on each HUD and substitutes the custom crosshair through that HUD's
/// own <see cref="CrosshairManager.pickCrosshairGlobal"/> event.
///
/// The body-level CrosshairUtils.RequestOverrideForBody stack is deliberately
/// not used: it also applies and resets the body's spread bloom values, which
/// feed real bullet spread, so a persistent cosmetic request could alter
/// gameplay. Substituting at the manager is purely local presentation, is
/// re-evaluated every frame, and leaves no request to leak across deaths,
/// respawns or stage changes.
/// </summary>
internal sealed class HudCrosshairDriver : MonoBehaviour
{
    // Same path CrosshairManager.OnEnable loads, so this is the same asset object.
    private const string GenericSprintCrosshairPath = "Prefabs/Crosshair/SprintingCrosshair";

    private static GameObject? _genericSprintCrosshair;
    private static bool _genericSprintCrosshairLoaded;

    private CrosshairManager? _crosshairManager;
    private bool _replacing;

    internal static void Attach(HUD hud)
    {
        if (!hud.GetComponent<HudCrosshairDriver>())
        {
            hud.gameObject.AddComponent<HudCrosshairDriver>();
        }
    }

    private void Awake()
    {
        _crosshairManager = GetComponent<CrosshairManager>();
        if (!_crosshairManager)
        {
            Plugin.Log.LogWarning($"HUD '{name}' has no CrosshairManager; custom crosshair unavailable.");
        }
    }

    private void OnEnable()
    {
        if (_crosshairManager)
        {
            _crosshairManager!.pickCrosshairGlobal += OnPickCrosshair;
        }
    }

    private void OnDisable()
    {
        if (_crosshairManager)
        {
            _crosshairManager!.pickCrosshairGlobal -= OnPickCrosshair;
        }

        SetReplacing(false, null);
    }

    private void OnPickCrosshair(CrosshairManager manager, ref GameObject crosshairPrefab)
    {
        GameObject? template = Plugin.CrosshairPrefab;
        CharacterBody? body = GetOwnLiveBody(manager);

        bool replace =
            template != null &&
            body != null &&
            CrosshairConfig.Enabled.Value &&
            (IsOrdinaryCrosshair(body, crosshairPrefab) || IsGenericSprintCrosshair(body, crosshairPrefab));

        if (replace)
        {
            crosshairPrefab = template!;
        }

        SetReplacing(replace, body);
    }

    /// <summary>
    /// The body this manager draws for, but only when it is the HUD local
    /// user's own live body viewed through the normal gameplay camera (same
    /// ownership test the game uses in CameraRigController.ModifyAimRayIfApplicable).
    /// Returns null while dead, spectating another player, or under a camera override.
    /// </summary>
    private static CharacterBody? GetOwnLiveBody(CrosshairManager manager)
    {
        CameraRigController rig = manager.cameraRigController;
        if (!rig || rig.hasOverride)
        {
            return null;
        }

        LocalUser? viewer = rig.localUserViewer;
        CharacterBody body = rig.targetBody;
        if (viewer == null || !body || viewer.cachedBodyObject != body.gameObject)
        {
            return null;
        }

        if (!body.healthComponent || !body.healthComponent.alive)
        {
            return null;
        }

        return body;
    }

    /// <summary>
    /// True when the manager picked the body's normal aiming crosshair: its
    /// default, or a loadout crosshair registered by SkillCrosshairController
    /// below Skill priority. Skill-state crosshairs (scopes, painters, charge
    /// indicators) and "no crosshair" stay vanilla.
    /// </summary>
    private static bool IsOrdinaryCrosshair(CharacterBody body, GameObject picked)
    {
        if (!picked)
        {
            return false;
        }

        if (picked == body.defaultCrosshairPrefab)
        {
            return true;
        }

        if (body.TryGetComponent(out SkillCrosshairController skillCrosshairs) &&
            skillCrosshairs.options != null)
        {
            foreach (SkillCrosshairController.SkillDefCrosshairOptions option in skillCrosshairs.options)
            {
                if (option != null &&
                    option.priority < CrosshairUtils.OverridePriority.Skill &&
                    option.crosshairPrefab == picked)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when the manager took its sprint branch with the generic sprint
    /// crosshair. CrosshairManager.UpdateCrosshair picks
    /// <c>sprintCrosshairPrefabOverride ?? SprintingCrosshair</c> when
    /// <c>!hasOverride &amp;&amp; alive &amp;&amp; isSprinting &amp;&amp; useSprintCrosshair</c>
    /// (camera override and alive are already checked by GetOwnLiveBody).
    /// Requiring both that state and the asset identity means another mod's
    /// pick handler can't make us replace something else. Survivor-specific
    /// sprint crosshairs (sprintCrosshairPrefabOverride) stay vanilla.
    /// </summary>
    private static bool IsGenericSprintCrosshair(CharacterBody body, GameObject picked)
    {
        if (!picked ||
            !body.isSprinting ||
            !body.useSprintCrosshair ||
            body.sprintCrosshairPrefabOverride)
        {
            return false;
        }

        GameObject? sprintCrosshair = GetGenericSprintCrosshair();
        return sprintCrosshair != null && picked == sprintCrosshair;
    }

    private static GameObject? GetGenericSprintCrosshair()
    {
        if (!_genericSprintCrosshairLoaded)
        {
            _genericSprintCrosshairLoaded = true;
            _genericSprintCrosshair = LegacyResourcesAPI.Load<GameObject>(GenericSprintCrosshairPath);
            if (_genericSprintCrosshair == null)
            {
                Plugin.Log.LogWarning(
                    $"Could not load '{GenericSprintCrosshairPath}'; the vanilla sprint crosshair will be shown.");
            }
        }

        return _genericSprintCrosshair;
    }

    private void SetReplacing(bool replacing, CharacterBody? body)
    {
        if (replacing == _replacing)
        {
            return;
        }

        _replacing = replacing;
        Plugin.Log.LogDebug(replacing
            ? $"Custom crosshair active for {body!.GetDisplayName()} ({body.name})."
            : "Custom crosshair released; vanilla crosshair logic in control.");
    }
}
