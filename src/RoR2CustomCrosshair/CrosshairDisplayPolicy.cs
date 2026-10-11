using System.Collections.Generic;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>
/// Decides which template, if any, replaces the crosshair CrosshairManager
/// picked. Independent of spread: a custom crosshair can be fully static.
///
/// Replaced: the body's ordinary crosshair (default, a loadout crosshair
/// below Skill priority, or the active MUL-T weapon's crosshair), the generic
/// sprint crosshair, vanilla crosshairs whose ammo displays we can keep, and
/// vanilla skill crosshairs verified to be purely cosmetic. Everything else
/// (scopes, painters, charge indicators, survivor-specific sprint
/// crosshairs) stays vanilla.
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

    // Vanilla crosshairs with meaningful skill-stock displays, kept until the
    // universal indicator system replaces them.
    private static readonly string[] StockPreservingAddresses =
    {
        // Bandit primaries: parentheses (spread) + four primary-stock dots.
        "RoR2/Base/Bandit2/Bandit2Crosshair.prefab",

        // MUL-T Scrap Launcher: three spread nibs + four primary-stock dots.
        "RoR2/Base/Toolbot/ToolbotGrenadeLauncherCrosshair.prefab",

        // MUL-T Power Mode: spread nibs, decorative ring, stock dots for both weapons.
        "RoR2/Base/Toolbot/ToolbotDualWieldCrosshair.prefab"
    };

    // Vanilla skill crosshairs with no spread, stock or timer data (their
    // CrosshairController has no stock displays), optionally limited to one body.
    private static readonly (string Address, string? BodyName)[] CosmeticSkillCrosshairAddresses =
    {
        // Bandit Lights Out / Desperado: a ring with a 0.1 s scale-in animation.
        ("RoR2/Base/Bandit2/Bandit2CrosshairPrepRevolver.prefab", null),
        ("RoR2/Base/Bandit2/Bandit2CrosshairPrepRevolverFire.prefab", null),

        // MUL-T Retool swap and Power Mode start: the generic cannot-fire
        // crosshair (ToolbotStanceBase.emptyCrosshairPrefab).
        ("RoR2/Base/UI/BadCrosshair.prefab", "ToolbotBody")
    };

    private static Transform? _holder;
    private static GameObject? _standardTemplate;

    // Vanilla crosshair -> template keeping its stock displays (null: keep vanilla).
    private static readonly Dictionary<GameObject, GameObject?> StockPreserving = new();

    // Cosmetic skill crosshair -> body it applies to (null: any body).
    private static readonly Dictionary<GameObject, string?> CosmeticSkillCrosshairs = new();
    private static readonly Dictionary<string, BodyIndex> BodyIndices = new();

    private static GameObject? _genericSprintCrosshair;
    private static bool _genericSprintCrosshairLoaded;

    private static int _pendingLoads;

    /// <summary>False until every template and reference asset has loaded.</summary>
    internal static bool IsReady => _pendingLoads == 0 && _standardTemplate != null;

    internal static void Initialize(Transform holder)
    {
        _holder = holder;
        _pendingLoads = 1 + StockPreservingAddresses.Length + CosmeticSkillCrosshairAddresses.Length;

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

        foreach (string address in StockPreservingAddresses)
        {
            CrosshairPrefabFactory.LoadAsync(address, vanilla =>
            {
                if (vanilla != null)
                {
                    GameObject? template = Build(vanilla, v => CrosshairPrefabFactory.BuildStockPreserving(v, holder));
                    StockPreserving[vanilla] = template;
                    if (template == null)
                    {
                        Plugin.Log.LogWarning($"{vanilla.name}: ammo displays could not be preserved; its vanilla crosshair will be kept.");
                    }
                }

                _pendingLoads--;
            });
        }

        foreach ((string address, string? bodyName) in CosmeticSkillCrosshairAddresses)
        {
            CrosshairPrefabFactory.LoadAsync(address, prefab =>
            {
                if (prefab != null)
                {
                    CosmeticSkillCrosshairs[prefab] = bodyName;
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
        BodyIndices.Clear();
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

        if (StockPreserving.ContainsKey(picked) ||
            IsOrdinaryCrosshair(body, picked) ||
            IsActiveWeaponCrosshair(body, picked))
        {
            return TemplateFor(picked);
        }

        // Sprint and cosmetic skill crosshairs carry no information; show the
        // template for the body's current ordinary crosshair (keeping its ammo dots).
        if (IsGenericSprintCrosshair(body, picked) || IsCosmeticSkillCrosshair(body, picked))
        {
            return TemplateFor(ActiveWeaponCrosshair(body) ?? body.defaultCrosshairPrefab);
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

    /// <summary>
    /// MUL-T's stance states request the active weapon's
    /// ToolbotWeaponSkillDef.crosshairPrefab at Skill priority; for MUL-T that
    /// is the ordinary crosshair. Null when the primary is not a MUL-T weapon,
    /// including during the Retool swap (primary slot unset), so stock-dot
    /// templates (which read the primary slot) are never shown then.
    /// </summary>
    private static GameObject? ActiveWeaponCrosshair(CharacterBody body)
    {
        GenericSkill? primary = body.skillLocator ? body.skillLocator.primary : null;
        return primary && primary!.skillDef is ToolbotWeaponSkillDef weapon && weapon.crosshairPrefab
            ? weapon.crosshairPrefab
            : null;
    }

    private static bool IsActiveWeaponCrosshair(CharacterBody body, GameObject picked)
    {
        GameObject? weaponCrosshair = ActiveWeaponCrosshair(body);
        return weaponCrosshair != null && picked == weaponCrosshair;
    }

    private static bool IsCosmeticSkillCrosshair(CharacterBody body, GameObject picked)
    {
        if (!CosmeticSkillCrosshairs.TryGetValue(picked, out string? bodyName))
        {
            return false;
        }

        if (bodyName == null)
        {
            return true;
        }

        if (!BodyIndices.TryGetValue(bodyName, out BodyIndex index) || index == BodyIndex.None)
        {
            index = BodyCatalog.FindBodyIndex(bodyName);
            BodyIndices[bodyName] = index;
        }

        return index != BodyIndex.None && body.bodyIndex == index;
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
