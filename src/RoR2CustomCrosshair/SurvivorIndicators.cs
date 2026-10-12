using EntityStates.Bandit2.Weapon;
using EntityStates.Engi.EngiMissilePainter;
using EntityStates.Huntress.Weapon;
using EntityStates.Loader;
using EntityStates.Mage.Weapon;
using RoR2;

namespace RoR2CustomCrosshair;

/// <summary>
/// Indicator tables for the official survivors (Phase C2), each verified
/// against the installed game's state code and skill configuration. Only the
/// generic Progress Bar and Count Indicator renderers are used. Survivors
/// with no table either show nothing at the center beyond spread, or carry a
/// vanilla display the generic types cannot express without losing
/// information (kept vanilla; see the README roster). MUL-T keeps its Phase
/// C1 source.
/// </summary>
internal static class SurvivorIndicators
{
    private static readonly BodyStateMachine Weapon = new("Weapon");
    private static readonly BodyStateMachine Body = new("Body");

    /// <summary>A fresh set of sources (one per HUD: charge timers are per-viewer state).</summary>
    internal static IIndicatorSource[] Create() => new IIndicatorSource[]
    {
        new ToolbotIndicatorSource(),
        Bandit(), Huntress(), Engineer(), Artificer(), Loader(),
        VoidFiend(), Seeker(), FalseSon(), Chef(), DroneTech()
    };

    private static ProgressRule[] NoProgress => System.Array.Empty<ProgressRule>();
    private static CountRule[] NoCounts => System.Array.Empty<CountRule>();

    // Burst / Blast: ReloadSkillDef primaries with 4 shells; Bandit2Crosshair
    // shows the same live primary stock as dots.
    private static IIndicatorSource Bandit() => new TableIndicatorSource(
        "Bandit", "Bandit2Body", NoProgress, new[]
        {
            new CountRule("Primary shells",
                IndicatorReaders.SkillStock(SkillSlot.Primary, typeof(Bandit2FireRifle), typeof(FireShotgun2)))
        });

    // Ballista: AimArrowSnipe overrides the primary with FireArrowSnipe
    // (3 stock, no recharge) and leaves when it reaches 0;
    // HuntressSnipeCrosshair shows the same primary stock as dots.
    private static IIndicatorSource Huntress() => new TableIndicatorSource(
        "Huntress", "HuntressBody", NoProgress, new[]
        {
            new CountRule("Ballista shots", IndicatorReaders.SkillStock(SkillSlot.Primary, typeof(FireArrowSnipe)))
        });

    // Thermal Harpoons: while painting (Paint on the Weapon machine) each lock
    // deducts one utility stock (4 max, recharging); EngiPaintCrosshair shows
    // that utility stock. The world lock-on markers are not crosshair art and
    // are untouched.
    private static IIndicatorSource Engineer() => new TableIndicatorSource(
        "Engineer", "EngiBody", NoProgress, new[]
        {
            new CountRule("Harpoons available",
                IndicatorReaders.WhileState(Weapon, typeof(Paint), IndicatorReaders.SkillStock(SkillSlot.Utility, typeof(Paint))))
        });

    // Nano-Bomb / Nano-Spear: BaseChargeBombState charge = fixedAge / (baseDuration
    // / attack speed), thrown automatically at full charge. Flame / Plasma Bolt:
    // 4 primary stock; MageCrosshair shows it as dots.
    private static IIndicatorSource Artificer() => new TableIndicatorSource(
        "Artificer", "MageBody", new[]
        {
            new ProgressRule("Nano-Bomb/Spear charge",
                IndicatorReaders.Charge<BaseChargeBombState>(Weapon, (state, speed) => state.baseDuration / speed))
        }, new[]
        {
            new CountRule("Bolt charges", IndicatorReaders.SkillStock(SkillSlot.Primary, typeof(FireFireBolt)))
        });

    // Charged / Thunder Gauntlet: BaseChargeFist charge = fixedAge /
    // (baseChargeDuration / attack speed). Charged Gauntlet holds at full
    // charge while the key is held (Ready); Thunder Gauntlet releases at full.
    // The grapple readiness / range display (LoaderHookCrosshairController)
    // is a binary status only Loader has, so its vanilla art is kept.
    private static IIndicatorSource Loader() => new TableIndicatorSource(
        "Loader", "LoaderBody", new[]
        {
            new ProgressRule("Gauntlet charge",
                IndicatorReaders.Charge<BaseChargeFist>(Weapon, (state, speed) => state.baseChargeDuration / speed))
        }, NoCounts);

    // Flood: ChargeMegaBlaster charge = fixedAge / (baseDuration / attack
    // speed); at full it becomes ReadyMegaBlaster, held until release. The
    // corruption meter is a HUD overlay and stays vanilla.
    private static IIndicatorSource VoidFiend() => new TableIndicatorSource(
        "Void Fiend", "VoidSurvivorBody", new[]
        {
            new ProgressRule("Flood charge",
                IndicatorReaders.Charge<EntityStates.VoidSurvivor.Weapon.ChargeMegaBlaster>(Weapon, (state, speed) => state.baseDuration / speed)),
            new ProgressRule("Flood charged",
                IndicatorReaders.Holding(Weapon, typeof(EntityStates.VoidSurvivor.Weapon.ReadyMegaBlaster)))
        }, NoCounts);

    // Palm Blast: BaseChargePalmBlast charge = fixedAge / (baseChargeDuration /
    // attack speed), held at full until release (its vanilla crosshair shows
    // the charge through spread art). Tranquility and Meditate are HUD
    // overlays and stay vanilla.
    private static IIndicatorSource Seeker() => new TableIndicatorSource(
        "Seeker", "SeekerBody", new[]
        {
            new ProgressRule("Palm Blast charge",
                IndicatorReaders.Charge<EntityStates.Seeker.BaseChargePalmBlast>(Weapon, (state, speed) => state.baseChargeDuration / speed))
        }, NoCounts);

    // Club heavy swing (ClubSwing3) and Laser of the Father (LaserFather):
    // charge = fixedAge / (baseChargeDuration / attack speed), both held at
    // full. Lunar Spikes / Lunar Stake: secondary stock (grows with growth
    // level); FSCrosshair / FSStakeCrosshair show it as pips.
    private static IIndicatorSource FalseSon() => new TableIndicatorSource(
        "False Son", "FalseSonBody", new[]
        {
            new ProgressRule("Club charge",
                IndicatorReaders.Charge<EntityStates.FalseSon.ClubSwing3>(Weapon, (state, speed) => state.baseChargeDuration / speed)),
            new ProgressRule("Laser of the Father charge",
                IndicatorReaders.Charge<EntityStates.FalseSon.LaserFather>(Weapon, (state, speed) => state.baseChargeDuration / speed))
        }, new[]
        {
            // LunarStake derives from LunarSpikes, so both secondaries match.
            new CountRule("Lunar Spikes/Stake",
                IndicatorReaders.SkillStock(SkillSlot.Secondary, typeof(EntityStates.FalseSon.LunarSpikes)))
        });

    // Dice: 3 cleavers (one stock per cleaver out); ChefCrosshair shows the
    // primary stock as pips. Roll: RolyPoly.gearCharge = fixedAge /
    // (baseChargeDuration / attack speed) until launch, which happens on
    // release or automatically above 0.9 (top gear).
    private static IIndicatorSource Chef() => new TableIndicatorSource(
        "CHEF", "ChefBody", new[]
        {
            new ProgressRule("Roll charge", ChefRollCharge)
        }, new[]
        {
            new CountRule("Cleavers", IndicatorReaders.SkillStock(SkillSlot.Primary, typeof(EntityStates.Chef.Dice)))
        });

    private const float ChefTopGear = 0.9f;

    private static bool ChefRollCharge(CharacterBody body, out float progress, out bool ready)
    {
        progress = 0f;
        ready = false;
        if (Body.StateOf(body) is not EntityStates.Chef.RolyPoly roll)
        {
            return false;
        }

        ChefController controller = body.GetComponent<ChefController>();
        if (!controller || controller.rolyPolyStarted)
        {
            return false;
        }

        progress = UnityEngine.Mathf.Clamp01(roll.gearCharge / ChefTopGear);
        ready = roll.gearCharge >= ChefTopGear;
        return true;
    }

    // Nano pistol: stock 4, never consumed by firing; the shot's charge is
    // (stock - 1) / 3, so stocks above the first are charge pips
    // (DroneTechCrosshair shows them for stock 2..4). Headbutt paint: while
    // painting, each lock deducts one secondary stock; DroneTechPaintCrosshair
    // shows that stock. The drone-type icon stays vanilla.
    private static IIndicatorSource DroneTech() => new TableIndicatorSource(
        "Drone Tech", "DroneTechBody", NoProgress, new[]
        {
            new CountRule("Drone paint locks available",
                IndicatorReaders.WhileState(Weapon, typeof(EntityStates.DroneTech.Weapon.Paint.Paint),
                    IndicatorReaders.SkillStock(SkillSlot.Secondary, typeof(EntityStates.DroneTech.Weapon.Paint.Paint)))),
            new CountRule("Nano pistol charge", DroneTechPistolCharge)
        });

    private static readonly IndicatorReaders.SkillMatcher NanoPistol = new(new[] { typeof(EntityStates.DroneTech.Weapon.FireNanoPistol) });

    private static bool DroneTechPistolCharge(CharacterBody body, out int current, out int maximum)
    {
        // While painting, the paint crosshair (paint locks only) replaces the pistol's.
        GenericSkill? primary = IndicatorReaders.Skill(body, SkillSlot.Primary);
        if (primary != null && NanoPistol.Accepts(primary.skillDef) &&
            Weapon.StateOf(body) is not EntityStates.DroneTech.Weapon.Paint.Paint)
        {
            current = primary.stock - 1;
            maximum = primary.maxStock - 1;
            return maximum > 0;
        }

        current = maximum = 0;
        return false;
    }
}
