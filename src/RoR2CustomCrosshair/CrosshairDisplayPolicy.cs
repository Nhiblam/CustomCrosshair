using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Decides which template, if any, replaces the crosshair CrosshairManager
/// picked. Independent of spread: a custom crosshair can be fully static.
///
/// Replaced: the body's ordinary crosshair (default, or a loadout crosshair
/// below Skill priority), the generic sprint crosshair, and vanilla skill
/// crosshairs verified to be purely cosmetic. Everything else (scopes,
/// painters, charge indicators, survivor-specific sprint crosshairs) stays
/// vanilla.
///
/// Vanilla crosshairs that carry ammo information get a template that keeps
/// those displays; if that template cannot be built, the vanilla crosshair is
/// kept rather than losing the information.
/// </summary>
internal static class CrosshairDisplayPolicy
{
    private const string StandardCrosshairAddress = "RoR2/Base/UI/StandardCrosshair.prefab";

    // Same path CrosshairManager.OnEnable loads, so this is the same asset object.
    private const string GenericSprintCrosshairPath = "Prefabs/Crosshair/SprintingCrosshair";

    // Bandit's primary crosshair: parentheses (spread) + four primary-stock dots.
    private const string BanditCrosshairAddress = "RoR2/Base/Bandit2/Bandit2Crosshair.prefab";

    // Bandit's Lights Out / Desperado crosshairs: a ring with a 0.1 s scale-in
    // animation and no spread, stock or timer data (CrosshairController has
    // no spread positions, stock displays or remap sprites).
    private static readonly string[] CosmeticSkillCrosshairAddresses =
    {
        "RoR2/Base/Bandit2/Bandit2CrosshairPrepRevolver.prefab",
        "RoR2/Base/Bandit2/Bandit2CrosshairPrepRevolverFire.prefab"
    };

    private static Transform? _holder;
    private static GameObject? _standardTemplate;

    // Vanilla crosshair -> template keeping its stock displays (null: keep vanilla).
    private static readonly Dictionary<GameObject, GameObject?> StockPreserving = new();
    private static readonly HashSet<GameObject> CosmeticSkillCrosshairs = new();

    private static GameObject? _genericSprintCrosshair;
    private static bool _genericSprintCrosshairLoaded;

    private static int _pendingLoads;

    /// <summary>False until every template and reference asset has loaded.</summary>
    internal static bool IsReady => _pendingLoads == 0 && _standardTemplate != null;

    internal static void Initialize(Transform holder)
    {
        _holder = holder;
        _pendingLoads = 2 + CosmeticSkillCrosshairAddresses.Length;

        CrosshairPrefabFactory.LoadAsync(StandardCrosshairAddress, standard =>
        {
            _standardTemplate = Build(standard, s => CrosshairPrefabFactory.BuildStandard(s, holder));
            if (_standardTemplate != null)
            {
                Plugin.Log.LogInfo("Crosshair template ready.");
            }
            else
            {
                Plugin.Log.LogError("Crosshair template unavailable; vanilla crosshairs will be used.");
            }

            _pendingLoads--;
        });

        CrosshairPrefabFactory.LoadAsync(BanditCrosshairAddress, bandit =>
        {
            if (bandit != null)
            {
                GameObject? template = Build(bandit, b => CrosshairPrefabFactory.BuildStockPreserving(b, holder));
                StockPreserving[bandit] = template;
                if (template == null)
                {
                    Plugin.Log.LogWarning($"{bandit.name}: ammo displays could not be preserved; its vanilla crosshair will be kept.");
                }
            }

            _pendingLoads--;
        });

        foreach (string address in CosmeticSkillCrosshairAddresses)
        {
            CrosshairPrefabFactory.LoadAsync(address, prefab =>
            {
                if (prefab != null)
                {
                    CosmeticSkillCrosshairs.Add(prefab);
                }

                _pendingLoads--;
            });
        }
    }

    internal static void Shutdown()
    {
        _standardTemplate = null;
        StockPreserving.Clear();
        CosmeticSkillCrosshairs.Clear();
        _holder = null;
    }

    /// <summary>
    /// The template to show instead of <paramref name="picked"/> for the local
    /// user's own live <paramref name="body"/>, or null to keep vanilla.
    /// </summary>
    internal static GameObject? ChooseTemplate(CharacterBody body, GameObject picked)
    {
        if (!IsReady || !picked)
        {
            return null;
        }

        if (IsOrdinaryCrosshair(body, picked))
        {
            return TemplateFor(picked);
        }

        // Sprint and cosmetic skill crosshairs carry no information; show the
        // template for the body's ordinary crosshair (keeping its ammo dots).
        if (IsGenericSprintCrosshair(body, picked) || CosmeticSkillCrosshairs.Contains(picked))
        {
            return TemplateFor(body.defaultCrosshairPrefab);
        }

        return null;
    }

    private static GameObject? TemplateFor(GameObject? vanilla)
    {
        if (vanilla != null && StockPreserving.TryGetValue(vanilla, out GameObject? preserving))
        {
            return preserving;
        }

        return _standardTemplate;
    }

    private static GameObject? Build(GameObject? source, System.Func<GameObject, GameObject?> build)
    {
        if (source == null || _holder == null)
        {
            return null;
        }

        try
        {
            return build(source);
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError($"Could not build crosshair template from {source.name}: {exception}");
            return null;
        }
    }

    /// <summary>
    /// True when the manager picked the body's normal aiming crosshair: its
    /// default, or a loadout crosshair registered by SkillCrosshairController
    /// below Skill priority. Skill-state crosshairs (scopes, painters, charge
    /// indicators) and "no crosshair" are not ordinary.
    /// </summary>
    private static bool IsOrdinaryCrosshair(CharacterBody body, GameObject picked)
    {
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
    /// (camera override and alive are checked by the caller). Requiring both
    /// that state and the asset identity means another mod's pick handler
    /// can't make us replace something else. Survivor-specific sprint
    /// crosshairs (sprintCrosshairPrefabOverride) stay vanilla.
    /// </summary>
    private static bool IsGenericSprintCrosshair(CharacterBody body, GameObject picked)
    {
        if (!body.isSprinting ||
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
}
