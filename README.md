# AI-Generated Project Notice

**Everything in this project—including the code, architecture, documentation, prompts, implementation plans, and this README—has been generated with AI assistance.** The project is being manually reviewed and tested in Risk of Rain 2, but AI-generated work should not be assumed correct without verification.

# RoR2 Custom Crosshair

A work-in-progress Risk of Rain 2 mod that replaces the normal gameplay crosshair with a configurable, procedural crosshair system inspired by modern competitive shooters.

The current implementation focuses on local visual customization only. It does not modify weapon behavior, projectile spread, networking, or other gameplay mechanics.

## Current Status

The current prototype is implemented, builds successfully, deploys into the development r2modman profile, and has been manually tested in-game by the project owner. The currently implemented visual framework appears to be working as intended based on testing completed so far.

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
- Special skill crosshairs can remain vanilla when intentionally preserved.
- Resolution-aware scaling using a 1920x1080 reference.
- HUD-scale compensation.
- Pixel-aligned symmetric geometry.
- True outline rendering that does not tint translucent fills.
- Crosshair lifecycle survives normal gameplay state changes.
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

Static layers always render exactly their configured geometry.

Dynamic mode visualizes actual weapon/projectile spread. Current support:

- Commando (Double Tap) — supported, verified in-game.
- Bandit (Burst and Blast) — supported, verified in-game.
- MUL-T — supported, awaiting in-game verification (details below).
- Other survivors — pending. Their Dynamic layers stay at the configured geometry, the same as Static.

For Commando, the spread source is the body's `spreadBloomAngle`, which Double Tap uses as the maximum angle (in degrees) each bullet may deviate from the aim direction. That angle is projected through the gameplay camera's field of view into screen pixels, and the growth above the resting spread is added to the configured Gap (bars) or Radius (circle). The configured value remains the resting geometry.

For Bandit, the spread is the widest angle the next shot can reach, computed from the weapon's configured spread plus `spreadBloomAngle`: Burst's horizontal pellet fan (1° + bloom) and Blast's random deviation (bloom × 1.3 horizontally). Both are wider horizontally than vertically; the round crosshair shows the horizontal extent. Bandit's vanilla ammo dots (primary stock) are kept below the custom crosshair; a customizable ammo/count indicator is planned for Phase C (Universal Indicator Framework).

For MUL-T, the active weapon is the game's current primary skill, so Retool switching is followed immediately (no weapon is active during the swap itself). Per weapon:

- Auto-Nailgun — dynamic spread (`spreadBloomAngle`, 2°–5° on the Nailgun's spread curve).
- Rebar Puncher — custom crosshair, no spread (the weapon has zero spread). The vanilla crosshair's expand/contract is a reload/readiness timer, not spread, so Dynamic layers do not move; a readiness indicator is deferred to Phase C.
- Scrap Launcher — custom crosshair; its spread is a fixed 1° and does not grow, so Dynamic layers do not move. The vanilla rocket-charge dots are kept.
- Power-Saw — custom crosshair, melee, no spread.
- Power Mode (both weapons at once) — custom crosshair with the vanilla stock dots for both weapons kept. Both weapons share one bloom value; Dynamic layers follow the Nailgun's spread when a Nailgun is equipped, otherwise they stay at base geometry.

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

Existing older crosshair configuration is migrated into Inner Bars on first launch of the newer framework.

Default behavior keeps Inner Bars enabled while Outer Bars, Circle, and Center Dot start disabled so existing users retain approximately the same appearance after migration.

## Current Technical Architecture

### Crosshair replacement

The mod works through Risk of Rain 2's HUD crosshair-selection flow instead of permanently forcing `CrosshairUtils.RequestOverrideForBody`.

This was chosen because body-level crosshair override requests can interact with spread-bloom state. The current HUD-level replacement path is cosmetic and avoids intentionally changing gameplay spread values.

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

The most recent implementation was reported to build with zero warnings and zero errors.

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
- B3 — MUL-T: implemented, awaiting in-game verification (active-weapon detection, Retool, Power Mode; vanilla stock dots kept).
- Later — further spread-based survivors.

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

Add separate indicator systems that are not part of Dynamic crosshair spread (count dots, numeric counts, charge bars, timer bars, readiness bars). Vanilla ammo/stock dots currently kept for Bandit and MUL-T will move to this system.

#### Charge / Timer Bar

A reusable bar capable of showing normalized state such as:

- charge progress
- readiness progress
- remaining duration
- timer depletion

Potential customization:

- Enabled
- Position
- Width
- Height
- Color
- Opacity
- Outline
- Fill direction

#### Count Indicator

A reusable indicator for discrete resources such as shots or charges.

Supported display styles should eventually include:

- Dots
- Numeric count

Potential customization:

- Display mode
- Position
- Size
- Spacing
- Color
- Opacity
- Outline

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
- final Thunderstore packaging
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
