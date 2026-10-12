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
/// The custom reticle owns the center for the local user's own live body:
/// every crosshair the game picks is replaced. The generic sprint crosshair
/// and verified-cosmetic skill crosshairs stand in for the body's current
/// ordinary crosshair; every other crosshair gets a template built from
/// itself that keeps its non-reticle information (see TemplateFor).
///
/// Information is never traded for a cleaner look: if a template cannot be
/// built, the vanilla crosshair is kept. Still vanilla: no crosshair at all
/// (the game picked none) and camera overrides (handled by the driver).
/// </summary>
internal static class CrosshairDisplayPolicy
{
    private const string StandardCrosshairAddress = "RoR2/Base/UI/StandardCrosshair.prefab";

    // Same path CrosshairManager.OnEnable loads, so this is the same asset object.
    private const string GenericSprintCrosshairPath = "Prefabs/Crosshair/SprintingCrosshair";

    // Curated vanilla crosshairs with meaningful skill-stock displays: a
    // template keeps those displays exactly as the game draws them.
    private static readonly string[] StockPreservingAddresses =
    {
        // Bandit primaries: parentheses (spread) + four primary-stock dots.
        "RoR2/Base/Bandit2/Bandit2Crosshair.prefab",

        // MUL-T Scrap Launcher: three spread nibs + four primary-stock dots.
        "RoR2/Base/Toolbot/ToolbotGrenadeLauncherCrosshair.prefab",

        // MUL-T Power Mode: spread nibs, decorative ring, stock dots for both weapons.
        "RoR2/Base/Toolbot/ToolbotDualWieldCrosshair.prefab",

        // Artificer: spread arms + four primary-stock (bolt charge) dots.
        "RoR2/Base/Mage/MageCrosshair.prefab",

        // Huntress Ballista: spinner/brackets + three shot dots.
        "RoR2/Base/Huntress/HuntressSnipeCrosshair.prefab"
    };

    // Vanilla crosshairs whose count pips the generic Count Indicator shows
    // (the matching SurvivorIndicators table reads the same skill stock).
    // While the Count Indicator is enabled, built, and showing a count this
    // frame, the pips are dropped and everything else is kept; otherwise the
    // vanilla pips stay. Always: MUL-T's Phase C1 rule (dropped whenever the
    // Count Indicator is enabled and built), kept unchanged.
    private static readonly (string Address, bool Always)[] CountSupersededAddresses =
    {
        ("RoR2/Base/Toolbot/ToolbotGrenadeLauncherCrosshair.prefab", true),
        ("RoR2/Base/Toolbot/ToolbotDualWieldCrosshair.prefab", true),
        ("RoR2/Base/Bandit2/Bandit2Crosshair.prefab", false),
        ("RoR2/Base/Mage/MageCrosshair.prefab", false),
        ("RoR2/Base/Huntress/HuntressSnipeCrosshair.prefab", false),

        // Thermal Harpoons: six utility-stock pips; its stock-0 "ready" prompt stays.
        ("RoR2/Base/Engi/EngiPaintCrosshair.prefab", false),

        // CHEF Dice: three cleaver pips.
        ("RoR2/DLC2/Chef/ChefCrosshair.prefab", false),

        // False Son Lunar Spikes (four pips) and Lunar Stake (two pips).
        ("RoR2/DLC2/FalseSon/FSCrosshair.prefab", false),
        ("RoR2/DLC2/FalseSon/FSStakeCrosshair.prefab", false),

        // Drone Tech nano pistol charge pips (stock 2..4; the drone-type icon
        // stays) and headbutt paint pips (its stock-0 prompt stays).
        ("RoR2/DLC3/Drone Tech/DroneTechCrosshair.prefab", false),
        ("RoR2/DLC3/Drone Tech/DroneTechSprintingCrosshair.prefab", false),
        ("RoR2/DLC3/Drone Tech/DroneTechPaintCrosshair.prefab", false)
    };

    // Vanilla crosshair -> Always flag, and its count-superseded template (built on first use).
    private static readonly Dictionary<GameObject, bool> SupersededByCount = new();
    private static readonly Dictionary<GameObject, GameObject?> CountSuperseded = new();

    // Skills that write their charge into bloom: while one runs, the vanilla
    // crosshair's spread-driven art is its charge display (not spread), so it
    // is kept around the custom reticle while the generic Progress Bar is not
    // showing that charge (disabled, unbuilt, or no rule).
    private static readonly BodyStateMachine WeaponMachine = new("Weapon");
    private static readonly BodyStateMachine BodyMachine = new("Body");

    private static readonly (string BodyName, BodyStateMachine Machine, System.Type StateType)[] ChargeDisplayContexts =
    {
        // Nano-Bomb / Nano-Spear: SetSpreadBloom(remap(charge)) on MageCrosshair.
        ("MageBody", WeaponMachine, typeof(EntityStates.Mage.Weapon.BaseChargeBombState)),

        // Charged / Thunder Gauntlet: SetSpreadBloom(charge) on the bracket crosshair.
        ("LoaderBody", WeaponMachine, typeof(EntityStates.Loader.BaseChargeFist)),

        // Palm Blast: SetSpreadBloom(charge) on its override crosshair.
        ("SeekerBody", WeaponMachine, typeof(EntityStates.Seeker.BaseChargePalmBlast)),

        // Club heavy swing and Laser of the Father: SetSpreadBloom(charge) on FSCrosshair.
        ("FalseSonBody", WeaponMachine, typeof(EntityStates.FalseSon.ClubSwing3)),
        ("FalseSonBody", WeaponMachine, typeof(EntityStates.FalseSon.LaserFather)),

        // Roll: SetSpreadBloom(gearCharge) on ChefCrosshair.
        ("ChefBody", BodyMachine, typeof(EntityStates.Chef.RolyPoly))
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

    // Any other vanilla crosshair -> standard or overlay template (null: keep vanilla).
    private static readonly Dictionary<GameObject, GameObject?> Classified = new();

    // Vanilla crosshair -> template keeping its charge-display art (null: none).
    private static readonly Dictionary<GameObject, GameObject?> ChargeDisplays = new();

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
        _pendingLoads = 1 + StockPreservingAddresses.Length + CountSupersededAddresses.Length + CosmeticSkillCrosshairAddresses.Length;

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

        foreach ((string address, bool always) in CountSupersededAddresses)
        {
            CrosshairPrefabFactory.LoadAsync(address, vanilla =>
            {
                if (vanilla != null)
                {
                    SupersededByCount[vanilla] = always;
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
        SupersededByCount.Clear();
        CountSuperseded.Clear();
        Classified.Clear();
        ChargeDisplays.Clear();
        CosmeticSkillCrosshairs.Clear();
        BodyIndices.Clear();
        _holder = null;
    }

    /// <summary>
    /// The template to show instead of <paramref name="picked"/> for the local
    /// user's own live <paramref name="body"/>, or null to keep vanilla.
    /// <paramref name="indicators"/> is this frame's generic indicator data
    /// for the body: vanilla art it duplicates is dropped only while the
    /// matching generic indicator is enabled, built and showing.
    /// </summary>
    internal static GameObject? ChooseTemplate(CharacterBody body, GameObject picked, IndicatorFrame indicators)
    {
        if (!IsReady || !picked)
        {
            return null;
        }

        bool indicatorsBuilt = !CrosshairPrefabFactory.IndicatorsUnavailable;
        bool countShown = indicatorsBuilt && CrosshairConfig.CountIndicator.Enabled.Value && indicators.AnyCount;
        bool progressShown = indicatorsBuilt && CrosshairConfig.ProgressBar.Enabled.Value && indicators.AnyProgress;

        // A skill displaying its charge through the vanilla spread art, unless
        // the generic Progress Bar is showing that charge.
        if (!progressShown && IsChargeDisplayContext(body))
        {
            GameObject? chargeDisplay = ChargeDisplayFor(picked);
            if (chargeDisplay != null)
            {
                return chargeDisplay;
            }
        }

        // Sprint and verified-cosmetic skill crosshairs carry no information;
        // show the template for the body's current ordinary crosshair (keeping
        // its ammo dots).
        if (IsGenericSprintCrosshair(body, picked) || IsCosmeticSkillCrosshair(body, picked))
        {
            return TemplateFor(ActiveWeaponCrosshair(body) ?? body.defaultCrosshairPrefab, countShown);
        }

        // Every other crosshair: the custom reticle owns the center, built from
        // the picked crosshair so its non-reticle information is kept.
        return TemplateFor(picked, countShown);
    }

    /// <summary>
    /// The custom template for a vanilla crosshair, built once and cached:
    /// curated stock-preserving templates first; pure reticle art gets the
    /// standard template; anything carrying more (stock displays, scope
    /// overlays, custom controllers) gets an overlay template that only
    /// removes its spread art. Null keeps the vanilla crosshair (template
    /// could not be built).
    /// </summary>
    private static GameObject? TemplateFor(GameObject? vanilla, bool countShown)
    {
        if (vanilla == null)
        {
            return _standardTemplate;
        }

        // The generic Count Indicator replaces these vanilla pips.
        if (SupersededByCount.TryGetValue(vanilla, out bool always) &&
            (countShown || always) &&
            CrosshairConfig.CountIndicator.Enabled.Value &&
            !CrosshairPrefabFactory.IndicatorsUnavailable)
        {
            return CountSupersededTemplate(vanilla);
        }

        if (StockPreserving.TryGetValue(vanilla, out GameObject? preserving))
        {
            return preserving;
        }

        return ClassifiedTemplate(vanilla);
    }

    /// <summary>Standard template for pure reticle art, else an overlay template (cached).</summary>
    private static GameObject? ClassifiedTemplate(GameObject vanilla)
    {
        if (Classified.TryGetValue(vanilla, out GameObject? classified))
        {
            return classified;
        }

        GameObject? template;
        if (CrosshairPrefabFactory.IsPureReticle(vanilla, out string extra))
        {
            template = _standardTemplate;
        }
        else
        {
            template = Build(vanilla, v => CrosshairPrefabFactory.BuildOverlay(v, _holder!));
            Plugin.Log.LogInfo(template != null
                ? $"Phase C review: {vanilla.name} carries {extra}; custom reticle overlaid, its spread art removed, everything else kept."
                : $"Phase C review: {vanilla.name} carries {extra}; overlay could not be built, vanilla crosshair kept.");
        }

        Classified[vanilla] = template;
        return template;
    }

    /// <summary>
    /// <paramref name="vanilla"/> without its count pips: curated crosshairs
    /// keep only their zero-stock status displays (standard template when
    /// none), others get an overlay template with the pips removed. If that
    /// cannot be built, the template that keeps the pips is used.
    /// </summary>
    private static GameObject? CountSupersededTemplate(GameObject vanilla)
    {
        if (!CountSuperseded.TryGetValue(vanilla, out GameObject? template))
        {
            template = StockPreserving.ContainsKey(vanilla)
                ? Build(vanilla, v => CrosshairPrefabFactory.BuildStockPreserving(v, _holder!, keepCounts: false)) ?? _standardTemplate
                : Build(vanilla, v => CrosshairPrefabFactory.BuildOverlay(v, _holder!, stripCounts: true));
            CountSuperseded[vanilla] = template;
            Plugin.Log.LogDebug(template != null
                ? $"{vanilla.name}: count pips superseded by the Count Indicator ({template.name})."
                : $"{vanilla.name}: count-superseded template could not be built; vanilla pips kept.");
        }

        if (template != null)
        {
            return template;
        }

        return StockPreserving.TryGetValue(vanilla, out GameObject? preserving) ? preserving : ClassifiedTemplate(vanilla);
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

    private static bool IsCosmeticSkillCrosshair(CharacterBody body, GameObject picked)
    {
        if (!CosmeticSkillCrosshairs.TryGetValue(picked, out string? bodyName))
        {
            return false;
        }

        return bodyName == null || IsBody(body, bodyName);
    }

    private static bool IsChargeDisplayContext(CharacterBody body)
    {
        foreach ((string bodyName, BodyStateMachine machine, System.Type stateType) in ChargeDisplayContexts)
        {
            if (!IsBody(body, bodyName))
            {
                continue;
            }

            EntityStates.EntityState? state = machine.StateOf(body);
            if (state != null && stateType.IsInstanceOfType(state))
            {
                return true;
            }
        }

        return false;
    }

    private static GameObject? ChargeDisplayFor(GameObject vanilla)
    {
        if (!ChargeDisplays.TryGetValue(vanilla, out GameObject? template))
        {
            template = Build(vanilla, v => CrosshairPrefabFactory.BuildStockPreserving(v, _holder!, keepSpreadArt: true));
            ChargeDisplays[vanilla] = template;
        }

        return template;
    }

    private static bool IsBody(CharacterBody body, string bodyName)
    {
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
