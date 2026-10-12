# RoR2 Custom Crosshair

A work-in-progress Risk of Rain 2 mod that replaces the normal gameplay crosshair with a configurable, procedural crosshair system inspired by modern competitive shooters.

The current implementation focuses on local visual customization only. It does not modify weapon behavior, projectile spread, networking, or other gameplay mechanics.

## Current Status

The current prototype is implemented, builds successfully, deploys into the development r2modman profile, and has been manually tested in-game by the project owner. The currently implemented visual framework appears to be working as intended based on testing completed so far.

Current version: **0.2.0**. Phases A, B1–B5 and C1 are verified in-game. Phase C2 (full official roster: indicators for base-game and DLC survivors, DLC spread, Distance From Crosshair) is **implemented — awaiting user verification**.

### Working foundation

- Procedural crosshair rendering.
- BepInEx configuration persistence.
- RiskOfOptions in-game configuration.
- RiskOfOptions mod list shows the project icon (`icon.png`, embedded in the DLL).
- Live configuration updates without restarting the game.
- Local-player-only crosshair behavior.
- Spectator protection.
- Normal gameplay replacement.
- Sprint replacement.
- The custom reticle owns the center for all survivors and abilities; meaningful vanilla indicators and overlays are kept around it.
- Resolution-aware scaling using a 1920x1080 reference.
- HUD-scale compensation.
- Pixel-aligned symmetric geometry.
- True outline rendering that does not tint translucent fills.
- Crosshair lifecycle survives normal gameplay state changes.
- Generic Progress Bar and Count Indicator, driven per survivor for the whole official roster.
- No R2API dependency is currently required.
- No gameplay networking or configuration synchronization.

## Current Crosshair Framework

The renderer is divided into four independently configurable visual systems.

### Inner Bars

Four centered bars with independent settings for:

- Enabled
- Static / Dynamic mode
- Length
- Thickness
- Gap
- RGB color
- Opacity
- Outline enabled
- Outline thickness
- Outline RGB
- Outline opacity

### Outer Bars

A second four-bar layer with the same independent controls as Inner Bars.

Inner and Outer Bars can be enabled at the same time and use the same exact center and scaling rules.

### Circle

A hollow circular crosshair layer with:

- Enabled
- Static / Dynamic mode
- Radius
- Thickness
- RGB color
- Opacity
- Outline enabled
- Outline thickness
- Outline RGB
- Outline opacity

The circle is rendered as generated ring geometry rather than a texture-based approximation.

### Center Dot

A separate centered dot with:

- Enabled
- Size
- RGB color
- Opacity
- Outline enabled
- Outline thickness
- Outline RGB
- Outline opacity

The dot is a filled circle rendered with the same generated geometry as the Circle layer. Its outline is a separate ring around the dot, so a translucent dot never shows the outline color through its interior.

At very small sizes (about 2–4 px) the anti-aliased edge makes the dot look slightly soft; this keeps a correct circular shape instead of falling back to a square.

## Static and Dynamic Modes

Static layers always render exactly their configured geometry. Static has no gameplay awareness: with Gap 0 it stays at Gap 0 for every survivor, weapon and ability.

Dynamic represents the **actual current bullet/projectile firing envelope**: the widest angle by which a bullet or projectile of the relevant attack can leave the aim direction if it fires right now. That includes:

- inherent/base spread
- accumulated bloom
- fixed spread
- ability-specific spread

The envelope is projected through the gameplay camera's field of view into screen pixels and added to the configured geometry:

```text
Dynamic bars:   Displayed Gap    = Configured Gap    + projected current spread
Dynamic circle: Displayed Radius = Configured Radius + projected current spread
```

Nothing is subtracted: a weapon with inherent spread is already open at rest. Depending on the weapon, Dynamic may therefore be:

- stationary but offset (fixed spread),
- moving (spread that changes with bloom or ability), or
- zero (no spread), looking the same as Static.

While an ability is firing its own bullets/projectiles, Dynamic shows that attack's envelope; otherwise it shows the equipped primary's next shot. Abilities that reset or narrow the real spread shrink it.

Current support:

- Commando — supported, verified in-game (B4 contract).
- Bandit (Burst and Blast) — supported, verified in-game (B4 contract).
- MUL-T — supported, verified in-game (B4 contract).
- Huntress, Engineer, Artificer, Mercenary, REX, Loader, Acrid, Captain — implemented (Phase B5), awaiting in-game verification. Details below.
- Other survivors (DLC and modded) — pending. Their Dynamic layers stay at the configured geometry, the same as Static.

Commando: idle and Double Tap use the body's `spreadBloomAngle` (about 0° with no bloom, up to 2°), Double Tap's maximum deviation in degrees. Tactical Slide and Phase Round reset bloom, so Dynamic shrinks. While an ability fires, its own spread is shown: Phase Blast 8° + bloom, Suppressive Fire a fixed 1°, Sabotage its ±11° fan plus 0.75°, Phase Round and Frag Grenade 0°.

Bandit: Burst is its horizontal pellet fan, 1° + bloom (open at rest, up to 9°); Blast is bloom × 1.3 horizontally (exactly 0° at rest, up to 10.4°). Both are wider horizontally than vertically; the round crosshair shows the horizontal extent. Lights Out / Desperado shots and Serrated Shiv have 0° spread. Bandit's vanilla ammo dots (primary stock) are kept below the custom crosshair; a customizable ammo/count indicator is planned for Phase C (Universal Indicator Framework).

MUL-T: the active weapon is the game's current primary skill, so Retool switching is followed immediately (no weapon is active during the swap itself). Per weapon:

- Auto-Nailgun — `spreadBloomAngle` on the Nailgun's curve: 2° with no bloom, up to 5°.
- Rebar Puncher — 0° (the shot has no spread). The vanilla crosshair's expand/contract is a readiness timer, not spread; a readiness indicator is deferred to Phase C.
- Scrap Launcher — a fixed 1°: open, not moving. The vanilla rocket-charge dots are kept.
- Power-Saw — 0°, melee.
- Power Mode (both weapons at once) — the wider of the two weapons' envelopes; the vanilla stock dots for both weapons are kept.

Phase B5 survivors (implemented, awaiting in-game verification):

- Huntress — 0° throughout: Strafe/Flurry and Laser Glaive are homing orbs, Cyclone flies straight, Arrow Rain is world-targeted, Ballista has zero spread. Ballista's three vanilla shot dots are kept.
- Engineer — Bouncing Grenades use `spreadBloomAngle` (0°–7°): the cone widens while charging (up to 3.5°) and further during the volley. Laser Cutter, mines, Bubble Shield and Thermal Harpoons are 0°. The harpoon painting crosshair (target count) is kept as an overlay.
- Artificer — 0° for Flame/Plasma Bolt, Nano-Bomb/Spear, Flamethrower; the alternate utility's earth shards are a fixed 15° (±5° fan + up to 10° noise) while firing. Bolt charge dots are kept; while charging Nano-Bomb/Spear the vanilla spread arms (its charge display) stay around the custom reticle until the Phase C progress bar.
- Mercenary — 0°: all melee; Eviscerate's projectile has no spread.
- REX — DIRECTIVE: Inject is a fixed ±0.5° yaw fan (open, not moving). Sonic Boom is an area cone, mortars and Tangling Growth are world-targeted, DIRECTIVE: Harvest flies straight: 0°.
- Loader — 0°: punches are melee, grapples and the pylon fly straight. While charging a gauntlet, the vanilla bracket art (its charge display) stays around the custom reticle until the Phase C progress bar. The grapple ready / in-range indicators are kept.
- Acrid — 0°: Neurotoxin, Epidemic and Breath fly straight; the rest is melee.
- Captain — Vulcan Shotgun: `spreadBloomAngle` on an inverted curve, 8° uncharged contracting continuously to 1° at full charge (charge time 1.2 s ÷ attack speed). Power Tazer flies straight (0°); orbital strikes and supply beacons are world-targeted (0°). The supply beacon crosshair (stock and key glyph) is kept as an overlay.

Dynamic mode is reserved specifically for actual weapon/projectile spread.

Dynamic mode will **not** represent:

- charge state
- cooldown
- windup
- readiness
- time remaining
- shots remaining
- skill stock

Those concepts will be handled by separate indicator systems.

The renderer exposes independent dynamic offsets for:

- Inner Bars
- Outer Bars
- Circle

This allows any combination of Static and Dynamic layers later without another renderer rewrite.

## Configuration Layout

RiskOfOptions currently exposes separate configuration categories for:

- General
- Inner Bars
- Outer Bars
- Circle
- Center Dot
- Progress Bar (generic progress / charge / readiness indicator)
- Count Indicator (generic ammo / stock / count indicator)

Existing older crosshair configuration is migrated into Inner Bars on first launch of the newer framework.

Default behavior keeps Inner Bars enabled while Outer Bars, Circle, and Center Dot start disabled so existing users retain approximately the same appearance after migration.

## Current Technical Architecture

### Crosshair replacement

The mod works through Risk of Rain 2's HUD crosshair-selection flow instead of permanently forcing `CrosshairUtils.RequestOverrideForBody`.

This was chosen because body-level crosshair override requests can interact with spread-bloom state. The current HUD-level replacement path is cosmetic and avoids intentionally changing gameplay spread values.

### Central reticle ownership

For the local player's own live survivor, the custom crosshair owns the central reticle across all survivors and abilities. Vanilla central reticle art is replaced; meaningful ancillary information is kept rather than discarded:

- Curated templates keep known indicators (Bandit, MUL-T, Artificer and Huntress Ballista stock dots) and remove the vanilla reticle art.
- When the generic Count Indicator shows the same count (enabled, built, and active this frame), the vanilla count pips are removed and everything else in that crosshair is kept (for example Engineer's and Drone Tech's stock-0 "ready" prompts, Drone Tech's drone-type icon). If the Count Indicator is disabled or cannot be built, the vanilla pips are kept.
- Skills that show their charge through vanilla spread art (Artificer Nano-Bomb/Spear, Loader gauntlets, Seeker Palm Blast, False Son club and laser, CHEF Roll) keep that art only while the generic Progress Bar is not showing the charge.
- Vanilla crosshairs that are only reticle art get the plain custom crosshair.
- Any other vanilla crosshair (stock displays, scope overlays, text, custom controllers) keeps everything except its spread-driven art, with the custom reticle drawn on top. Each such asset is logged once as a `Phase C review` item.

Still vanilla: moments where the game shows no crosshair at all, and camera overrides (cutscene-style cameras), which are not live aiming.

### Rendering

The runtime crosshair template is based on the game's standard crosshair prefab while replacing its visible graphics with custom procedural UI elements.

Draw order is currently:

1. Circle
2. Outer Bars
3. Inner Bars
4. Center Dot

### Shared geometry

All enabled layers use one shared measurement frame containing:

- screen-space center
- resolution scale
- Canvas compensation
- pixel rounding rules

This keeps the different crosshair systems centered on the same exact origin.

### Resolution behavior

The target reference resolution is:

`1920 x 1080`

The current implementation scales crosshair geometry relative to resolution while compensating for the active Unity Canvas/HUD scale so the crosshair does not simply inherit arbitrary UI scaling twice.

Pixel snapping is performed symmetrically so opposite arms remain visually balanced.

## Development Environment

Current development setup:

- Windows 11
- Visual Studio Code
- .NET / C# project
- BepInEx 5.x
- MMHOOK.RoR2
- RiskOfOptions
- RiskOfRain2.GameLibs
- `netstandard2.1`

Development repository:

`C:\Users\yomama\Desktop\3\RoR2CustomCrosshair`

Development r2modman profile:

`C:\Users\yomama\AppData\Roaming\r2modmanPlus-local\RiskOfRain2\profiles\CrosshairDev`

Development plugin deployment folder:

`C:\Users\yomama\AppData\Roaming\r2modmanPlus-local\RiskOfRain2\profiles\CrosshairDev\BepInEx\plugins\RoR2CustomCrosshair`

Typical debug output:

`src\RoR2CustomCrosshair\bin\Debug\netstandard2.1\RoR2CustomCrosshair.dll`

## Build

From the repository root:

```powershell
dotnet restore .\src\RoR2CustomCrosshair\RoR2CustomCrosshair.csproj

dotnet build .\src\RoR2CustomCrosshair\RoR2CustomCrosshair.csproj -c Debug
```

Release build (used for the Thunderstore package):

```powershell
dotnet build .\src\RoR2CustomCrosshair\RoR2CustomCrosshair.csproj -c Release
```

Both configurations build with zero warnings and zero errors, and the sources are also compiled against the installed game's shipped (non-publicized) assemblies as a check that only public game members are used.

### Package

The Thunderstore / r2modman package (`dist\CustomCrosshairV2-<version>.zip`, not committed) contains only `manifest.json`, `icon.png`, `README.md` and the Release `RoR2CustomCrosshair.dll`.

Dependencies:

- `bbepis-BepInExPack` — BepInEx.
- `RiskofThunder-HookGenPatcher` — generates the `MMHOOK_RoR2.dll` the plugin uses to attach to the HUD.
- `Rune580-Risk_Of_Options` — in-game settings menu.

No R2API dependency.

## Vanilla Survivor Audit So Far

Testing has already identified several recurring categories of behavior across survivors.

### Actual spread / bloom visualization candidates

Examples identified so far include:

- Commando primary fire
- Bandit primary attacks
- MUL-T Nailgun
- MUL-T Scrap Launcher / rocket-style primary
- Engineer primary
- Railgunner primary
- Captain primary

These should eventually feed Dynamic crosshair geometry only when they represent actual attack spread.

### Charge / readiness behavior

Examples identified so far include:

- Artificer charged secondary
- Loader charged utility
- Captain primary windup/spread narrowing
- MUL-T rebar-style readiness behavior
- Railgunner special charge/readiness state

These should not be treated as Dynamic spread unless the underlying value actually represents projectile spread.

Instead, charge/readiness information will be handled by dedicated indicators where appropriate.

### Discrete count indicators

Examples include:

- Huntress ability shot count
- MUL-T rocket charges

These will later be represented independently from the crosshair itself.

## Planned Execution

### Phase B — Actual Spread / Bloom Support

Phase A is the crosshair rendering/configuration framework. Phase B adds actual weapon spread, one survivor at a time:

- B1 — Commando: implemented and verified in-game.
- B2 — Bandit: implemented and verified in-game (vanilla ammo dots kept).
- B3 — MUL-T: implemented and verified in-game (active-weapon detection, Retool, Power Mode; vanilla stock dots kept).
- B4 — Dynamic contract and central reticle ownership: Dynamic now shows the absolute current firing envelope (no resting subtraction) including the active ability's own spread; the custom reticle owns the center for every crosshair. Verified in-game.
- B5 — remaining base-game survivors (Huntress, Engineer, Artificer, Mercenary, REX, Loader, Acrid, Captain): implemented through a shared table-driven spread source. Verified in-game.
- DLC survivors: spread tables added in Phase C2 under the same B4 contract (see the C2 roster).

#### B1 — Commando Dynamic Spread Prototype

The first implementation target was deliberately narrow: Commando only.

Goals:

- Investigate the real gameplay spread source.
- Determine whether `spreadBloomAngle` or another authoritative value represents the current attack cone.
- Confirm how Commando spread increases while firing and recovers afterward.
- Convert the gameplay spread value into a screen-space offset.
- Feed that value into the existing independent Dynamic offsets.
- Keep Static layers completely unchanged.
- Verify that reading spread data does not modify gameplay behavior.

Expected behavior example:

```text
Inner Bars: Static
Outer Bars: Dynamic
Circle: Dynamic
```

Only layers set to Dynamic should react to Commando's weapon spread.

#### Phase B — Generic Spread Framework (ongoing)

Built up across B1–B3 and extended with each new survivor:

- Generalize the spread-data interface.
- Separate gameplay spread acquisition from rendering.
- Support independent Dynamic offsets for Inner Bars, Outer Bars, and Circle.
- Prefer real gameplay spread/trajectory information over copying vanilla UI pixel spacing.
- Account for camera/FOV projection when necessary.
- Add additional survivor/attack providers gradually.

The renderer should not contain survivor-specific conditionals.

### Phase C — Universal Indicator Framework

Add separate indicator systems that are not part of Dynamic crosshair spread (count dots, numeric counts, charge bars, timer bars, readiness bars). Vanilla ammo/stock dots and charge displays move to this system where the generic types can show the same information (C2); vanilla crosshairs logged as `Phase C review` keep their remaining non-reticle information.

**Positioning rule:** indicators use predefined placement options chosen by the mod (for example Above, Below, Left, Right, or a smaller indicator-specific subset). There are no arbitrary X/Y coordinate controls or free-position sliders.

#### C1 — Universal Indicator Foundation + MUL-T (implemented, verified in-game)

Generic indicators are drawn around the custom crosshair, independent of the crosshair's Static/Dynamic mode and of Dynamic spread. Renderers know nothing about survivors; gameplay providers supply values (progress 0–1 + ready, or current + maximum count) on up to two channels, so two independent instances of each indicator can show at once.

- Placement: predefined positions only — Above, Below, Left, Right. Each slot starts just outside the crosshair's configured size plus the indicator's Distance From Crosshair (added in C2), so Dynamic spread never moves an indicator. Indicators on the same side stack outward automatically with fixed spacing. No X/Y controls.
- Progress Bar tab: Enabled; Mode Static (whole bar Not Ready color, then Ready color) or Dynamic (fills with progress, then Ready color); Orientation Horizontal/Vertical; Fill From left-or-bottom or right-or-top; Placement; Length; Thickness; Not Ready and Ready colors; Show Empty Background (Dynamic only, drawn only over the unfilled part); true outline.
- Count Indicator tab: Enabled; Mode Dots (circular), Number (current or current / maximum, game font) or Bars; Layout horizontal row or vertical column; Bar Shape vertical or horizontal; sizes, spacing, color; true outline.
- Sizes are pixels at 1920×1080 and follow the crosshair's resolution and HUD-scale handling; edges snap to whole pixels.

MUL-T integration:

- Rebar Puncher → Progress Bar. Ready is the game's own "can fire" check; progress runs through the shot and cooldown states (both scale with attack speed).
- Scrap Launcher → Count Indicator, live stock and maximum stock.
- Retool follows the active weapon; nothing shows during the swap itself.
- Power Mode shows the stance weapon and the offhand weapon as two independent indicators (e.g. Rebar bar + Scrap count, or two of the same).
- MUL-T's vanilla stock dots are replaced by the generic Count Indicator while it is enabled; with it disabled (or if indicators cannot be built) the vanilla dots are kept.

Other survivors' indicators were migrated in C2.

#### C2 — Full Official Roster Integration (implemented — awaiting user verification)

The official roster was enumerated from the installed game's survivor definitions (build 25475991): 20 selectable survivors (13 base game, 2 DLC1, 3 DLC2, 2 DLC3, 2 DLC4) plus the hidden Heretic. Every survivor was classified from its skill definitions, entity-state code and vanilla crosshair assets. Only the two generic indicator types are used; no survivor draws its own UI.

**Distance From Crosshair.** Progress Bar and Count Indicator each have their own *Distance From Crosshair* slider (default 8, range 0–128, pixels at 1920×1080, same resolution and HUD-scale handling as all other sizes, updates live). It is the gap between the crosshair's configured outer edge and the first indicator in its slot; further indicators in the same slot keep stacking outward with the mod's spacing, and an indicator never starts closer than its own tab's distance. Placement remains the predefined Above / Below / Left / Right; there are no X/Y controls.

**Roster.**

| Survivor | Content | Dynamic spread | Progress Bar | Count Indicator | Kept vanilla |
|---|---|---|---|---|---|
| Commando | Base | Pistol bloom (B1) | — | — | — |
| Huntress | Base | Zero (homing / straight shots) | — | Ballista shots | — |
| Bandit | Base | Primary bloom (B2) | — | Shells (both primaries) | — |
| MUL-T | Base | Per weapon (B3) | Rebar Puncher readiness | Scrap Launcher stock (both Power Mode slots) | — |
| Engineer | Base | Bouncing Grenades bloom | — | Harpoons available while painting | Stock-0 "ready" prompt; world lock-on markers |
| Artificer | Base | Earth shards fan; bolts straight | Nano-Bomb / Nano-Spear charge | Bolt charges | — |
| Mercenary | Base | Melee: none | — | — | — |
| REX | Base | Syringe fan | — | — | — |
| Loader | Base | Straight projectiles: none | Gauntlet charge (Charged Gauntlet holds Ready) | — | Grapple ready / in-range display |
| Acrid | Base | Straight projectiles: none | — | — | — |
| Captain | Base | Vulcan Shotgun (narrows while charging) | — | — | Supply-beacon display (see below) |
| Railgunner | DLC1 | M99 pistol inaccuracy + bloom; scoped snipes exact | — | — | Scope overlays, reload bar / boost window, charge and offline overlays |
| Void Fiend | DLC1 | Drown bloom; corrupted Drown fixed; corrupted Flood bloom + fixed | Flood charge (held = Ready) | — | Corruption meter |
| Seeker | DLC2 | None (straight / homing / world-targeted) | Palm Blast charge (held = Ready) | — | Tranquility petals, Meditate UI |
| False Son | DLC2 | None (straight spikes, zero-spread lasers) | Club heavy-swing charge; Laser of the Father charge | Lunar Spikes / Lunar Stake stock | — |
| CHEF | DLC2 | Sear fixed cone; Glaze bloom + fixed | Roll charge (top gear = Ready) | Cleavers | — |
| Drifter | DLC3 | Cleanup pellet cone | — | — | Junk meter, bag card |
| Drone Tech | DLC3 | Nano pistol exact; Nano Bomb small fixed | — | Nano pistol charge pips; headbutt paint locks while painting | Drone-type icon, drone queue overlay |
| Acolyte | DLC4 | None (melee / straight / targeted) | — | — | Quickstrike recharge fills |
| Enforcer | DLC4 | Riot shotgun inaccuracy + bloom; shielded pellet cone | — | — | Shell pips with empowered-shot recolor |
| Heretic | Hidden | No table | — | — | Vanilla |

"None" in the spread column means the attack has no angular dispersion (it fires along the aim ray, homes, is melee or is world-targeted), so Dynamic layers stay at their configured size.

**Kept vanilla on purpose** (information is never traded for a cleaner look):

- Captain: each beacon's ready container also carries its key-binding glyph, so replacing it with a count would lose which key places which beacon.
- Enforcer: the shell pips recolor when the next shot is empowered; the Count Indicator has no such state.
- Acolyte: the crosshair shows each Quickstrike's recharge as a fill, which a count alone cannot show.
- Loader grapple: a binary ready / in-range status that only Loader has, so no third generic indicator type was added.
- HUD overlays (Railgunner reload and charge UI, Void Fiend corruption, Seeker Tranquility / Meditate, Drifter junk and bag, Drone Tech queue) are not crosshair art and are untouched; world-space markers (Engineer and Drone Tech lock-ons) likewise.

**Diagnostics.** MUL-T's C1 per-shot logs were removed (one line per weapon configuration change remains). The new survivor tables log at Debug level when a channel starts or stops, on Ready transitions, and on count changes (at most twice a second per channel).

#### Progress / Charge Bar

A reusable bar for normalized state such as charge progress, readiness, remaining duration or timer depletion.

- Mode:
  - Static: the whole bar uses the Not Ready color; when ready, the whole bar switches to the Ready color.
  - Dynamic: the fill grows with actual progress; at completion, the full bar switches to the Ready color.
- Orientation: Horizontal or Vertical.
- Fill direction: the directions appropriate to the orientation.
- Appearance: width, height, charging/Not Ready color, Ready color, optional empty background (color/opacity), outline and outline appearance.
- Position: predefined placement options only, plus Distance From Crosshair.

#### Count Indicator

A reusable indicator for discrete resources such as shots or charges.

- Data: CurrentCount and MaximumCount.
- Display mode:
  - Dots: size, spacing, predefined horizontal/vertical layout.
  - Number: the current number, or current / maximum.
  - Bars: one bar per available shot/stock, as a horizontal row or vertical column.
- Appearance: color, opacity, outline.
- Position: predefined placement options only, plus Distance From Crosshair.

### Phase E — Loadout-Aware Survivor Support

The mod should inspect the survivor's active runtime loadout rather than assuming one fixed skill configuration.

Planned goals:

- Identify equipped Primary / Secondary / Utility / Special skills.
- Detect alternate skill variants.
- Select the correct spread or indicator provider from the equipped abilities.
- Handle survivors such as Huntress, Bandit, and MUL-T whose meaningful crosshair behavior changes with loadout.

### Phase F — Survivor-Specific Adapters

After the generic systems are stable, add survivor/skill adapters incrementally.

Current examples include:

- Commando spread
- Huntress shot-count indicator
- Bandit spread
- MUL-T multiple primary behavior
- MUL-T dual-primary special handling
- Engineer spread
- Artificer charge/stock behavior
- Loader charge indicator
- Captain spread/windup behavior
- Railgunner spread and special indicators

The goal is for survivor-specific code to provide data to generic renderers rather than draw custom UI directly whenever possible.

### Phase G — Remaining Survivor Compatibility Audit

Continue testing remaining vanilla and DLC survivors and classify each behavior as:

- no special handling needed
- actual spread
- charge/readiness
- discrete count
- special HUD element
- scope/alternate aiming state
- other exception

Complex indicators that do not fit the current generic systems should be documented first and implemented later rather than forcing them into the wrong architecture.

### Phase H — Modded Survivor Support

Later work may include:

- detect modded survivors safely
- global enable/disable for modded survivors
- per-survivor behavior overrides
- avoid hard dependencies on survivor mods
- preserve fallback behavior when no custom adapter exists

### Phase I — Configuration / UX Expansion

Potential later improvements:

- better presets
- reset controls
- import/export
- more descriptive tooltips
- per-survivor configuration
- indicator customization

### Phase J — Compatibility and Release Hardening

Before release:

- test multiple resolutions
- test ultrawide
- test HUD scale changes
- test fullscreen/windowed transitions
- test multiplayer
- test spectating
- test death/respawn
- test stage transitions
- test body swaps
- test common HUD/crosshair mods
- audit allocations/per-frame work
- remove temporary diagnostics
- improve defensive logging
- prepare Thunderstore package
- write final user documentation
- perform fresh-profile installation testing

## Explicitly Out of Scope Right Now

The following should not be implemented until their respective planned phases:

- broad survivor-specific hardcoding inside the renderer
- fake spread animation
- dynamic gap driven by unrelated skill state
- networking
- synchronized settings
- publishing / uploading releases
- complex special HUD replacement without first understanding its gameplay meaning

## Design Rule Going Forward

Keep these concepts separate:

```text
Crosshair Geometry
    Inner Bars
    Outer Bars
    Circle
    Center Dot

Dynamic Spread
    Actual projectile / bullet spread only

Indicators
    Charge
    Timer
    Readiness
    Shot / charge count
    Future ability-specific information
```

The crosshair renderer should display data. Survivor- and skill-specific logic should determine what data is meaningful.

## Disclaimer

This project is experimental and under active development. Risk of Rain 2 updates may change internal APIs, assets, UI behavior, or generated hooks. Every game update should be treated as requiring compatibility verification before assuming the mod remains safe and correct.
