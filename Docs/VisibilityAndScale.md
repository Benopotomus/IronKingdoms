# Visibility Range & Combat Scale

> Reference for how Warmachine Mk4 inch measurements map to Unity world space, and how the prototype **visibility range** stat drives fog of war.

Related docs:
- [`WMH_MK4_Rules_Reference.md`](WMH_MK4_Rules_Reference.md) — printed Mk4 rules (SPD, RNG, model volumes, LOS)
- [`CombatPrototypeDesign.md`](CombatPrototypeDesign.md) — prototype scope and data model
- Source code: `Assets/Scripts/Data/CombatScale.cs`, `Assets/Scripts/Data/CombatStats.cs`

---

## Inch ↔ world unit conversion

Warmachine rules are written in **inches**. The IronKingdoms prototype maps models to Unity using a fixed tabletop scale:

| Quantity | Value |
|---|---|
| Millimeters per inch | 25.4 mm |
| Millimeters per Unity world unit | 30 mm |
| **World units per inch** | 25.4 ÷ 30 ≈ **0.8467** |
| **Inches per world unit** | 30 ÷ 25.4 ≈ **1.1811** |

Use `CombatScale.InchesToWorldUnits(inches)` and `CombatScale.WorldUnitsToInches(worldUnits)` for all gameplay conversions (movement, weapon range rings, fog revealers, navmesh spacing derived from bases, etc.).

### Model base & volume scale

Base diameters follow Mk4 base sizes (`ModelSize` enum):

| `ModelSize` | Base diameter | Volume height (Mk4) |
|---|---|---|
| Base30mm | 30 mm → 1.0 world unit | 1.75" |
| Base40mm | 40 mm → 1.333 world units | 2.25" |
| Base50mm | 50 mm → 1.667 world units | 2.75" |
| Base80mm | 80 mm → 2.667 world units | 3.25" |
| Base120mm | 120 mm → 4.0 world units | 5" |

Volume heights come from Mk4 §5 (Base Sizes & Model Volumes) and are used for line-of-sight blocking tiers in the rules reference. In code, `ModelSize.VolumeHeightInches()` and `VolumeHeightWorldUnits()` expose these values.

---

## Visibility range (`visibilityRange`)

### What it is

**Visibility range** is a **prototype stat** on `CombatStats` (not a printed Mk4 stat-bar field). It answers: *how far out from this model does the controlling player reveal the battlefield on the fog-of-war map?*

| Property | Location | Default |
|---|---|---|
| Field | `CombatStats.visibilityRange` | **36 inches** (`CombatScale.DefaultVisibilityRangeInches`) |
| Authoring | Unit Type Creator window, unit asset Inspector | Per unit |

### Why 36 inches?

Mk4 does not define a “map reveal radius” — both players see the full table in a physical game. The digital prototype adds fog of war, so we need an explicit reveal distance in inches.

**36"** is the prototype default because:

1. **Standard table context** — Mk4 games are commonly played on a **48" × 48"** table. Thirty-six inches is three quarters of a table edge, a reasonable squad-level “local battlefield awareness” radius without revealing the entire map from one spot.
2. **Consistent with Mk4 measurement** — Like SPD, RNG, CTRL, and AOE, the value is stored and authored in **inches**, then converted to world units at runtime.
3. **Per-model tuning** — Scouts, warcasters, or special rules can use a shorter or longer value on individual `UnitTypeDefinition` assets without code changes.

Adjust per unit in the Inspector or Unit Type Creator when a model should reveal more or less map (e.g. a forward scout at 24", a command element at 48").

### Runtime wiring

When player units spawn, `TestLevelUnitController.ConfigurePlayerFogRevealer` attaches `FogOfWarRevealer3D` and sets:

| FOW property | Source |
|---|---|
| `ViewRadius` | `CombatScale.InchesToWorldUnits(stats.visibilityRange)` |
| `VisionHeight` | `stats.modelSize.VolumeHeightWorldUnits()` (Mk4 model volume) |
| `EyeOffset` | Half of pawn capsule height |
| `UseOcclusion` | `true` — raycasts against **`FogOccluder`** layer geometry |
| `ObstacleLayerMask` | `CombatLayers.FogOccluderMask` (walls, crates, etc.) |

Forest and other **limited-depth** terrain (`CombatTerrainFeatureDefinition` with `LimitedDepth`) clip fog rays through `CombatFogOfWarRevealer3D` using the same inch depth as combat LOS (forest default **3"**). Only **forest thickness along each sight line** counts toward that 3" budget — if you are inside a patch and your line passes through less than 3" of forest before open ground, vision continues outside the trees.

Only **player-controlled** models receive fog revealers. Enemy visibility to the player is handled separately via line-of-sight checks, fog texture sampling, and renderer toggling.

Player **targeting** requires all of:

| Check | Meaning |
|---|---|
| Weapon range | Existing `IsTargetInRange` |
| Geometric LOS | Terrain, walls, and intervening models (`HasLineOfSight`) |
| Visibility range | Attacker's `visibilityRange` stat (planar inches) |
| Live fog | Target sight point must be above shroud on the fog texture (`IsInLiveFogVision`) |

Enemy AI targeting ignores fog and uses geometric LOS only.

### Wall & obstacle occlusion

Map geometry that should block fog (and combat line-of-sight) is placed on the **`FogOccluder`** Unity layer (`CombatLayers.cs`). Player fog revealers use `UseOcclusion = true` and raycast against that layer so vision does not pass through walls.

Walls in `CombatMapScene` (`Wall_Central`, `Wall_Left`, `Wall_Right`) are tagged **`Wall`** — use `CombatTags.Wall` in code.

Assign **`FogOccluder`** to new blocking props in the combat map scene. Tag wall objects with **`Wall`**. Do not put the ground plane on the occluder layer.

**Multi-scene physics:** The combat map loads additively (`CombatMapScene`). Fog revealers raycast against the **pawn's** `PhysicsScene`, not the global default. Units are moved into the map scene at spawn via `CombatMapSceneProvider.MoveToMapScene` so wall colliders on `FogOccluder` are visible to fog occlusion raycasts. Combat line-of-sight terrain checks use the same map `PhysicsScene`.

### Fog memory (explored vs never seen)

The prototype uses the FOW plugin's **texture storage + regrow** mode (not pixel-perfect realtime-only sampling):

| State | What you see |
|---|---|
| **Never visited** | Solid black (`UnknownColor`, 0% scene visibility). Values below ~2.5% visibility are snapped to full black in `FOW_SolidColor.shader` to avoid blur/bleed. |
| **Currently in vision** | Full scene color (100% visibility) |
| **Explored, out of vision** | Dimmed shroud — terrain layout visible at reduced brightness |

Configuration (via `TestLevelUnitController.ConfigureFogOfWarWorld`):

| Setting | Role | Default |
|---|---|---|
| `FOWSamplingMode` | `Texture` — persists exploration on a render texture | — |
| `UseRegrow` | Keeps shroud when revealers leave an area | `true` |
| `MaxFogRegrowAmount` | Visibility retained in shroud (`fogExploredShroudVisibility`) | **0.35** |
| `InitialFogExplorationValue` | Starting fog on unexplored texels | `0` (fully hidden) |
| `UseConstantBlur` | Off — blur bleeds explored pixels into unexplored areas | `false` |
| `FogType` / `FogFade` | Soft + Smoothstep for smoother vision-radius edges | — |
| `fogVisionEdgeSoftenDistance` | Global edge soften distance (world units) on `TestLevelUnitController` | **0.75** |
| `fogWorldBoundsSize` | Fog texture coverage in Unity world units (XZ) | 24 × 24 |

Tune **Fog Explored Shroud Visibility** on `TestLevelUnitController` in the Inspector. Lower = darker shroud; higher = closer to live vision.

### Example conversion

Default visibility range:

```
36" × (25.4 / 30) ≈ 30.48 world units
```

Previously the controller used a hard-coded `playerFogRevealerRadius = 10` **world units** (~11.8"), which did not follow inch-based rules scaling.

---

## Relationship to Mk4 line of sight

Mk4 **line of sight** (see rules reference §11) governs whether one model can **target** another: origin, intervening terrain, model volume, elevation, etc.

**Visibility range** governs **map exploration** (fog of war) only. Distance is measured **base edge to base edge**, matching Mk4 range measurement. The two systems are related in spirit but not identical:

- Fog reveal uses texture-storage revealers with regrow memory, **`FogOccluder`** raycast occlusion, and **limited-depth terrain clipping** for forests.
- Combat LOS uses `TestLevelUnitController.HasLineOfSight` and `CombatLineOfSightVolume` for enemy spot/hide behavior.

Future work may align fog occlusion with Mk4 terrain blocking using revealer raycasts (`UseOcclusion = true`) and shared LOS geometry.

---

## Authoring checklist

1. Set **Visibility Range (in)** on each `UnitTypeDefinition` (default 36).
2. Confirm **Model Size** matches the Mk4 base — it drives pawn scale and fog `VisionHeight`.
3. Weapon **RNG** and unit **SPD** remain separate inch stats; visibility range does not replace weapon range or movement.
4. For global scale changes, edit constants in `CombatScale.cs` and update this doc.

---

## Code index

| File | Role |
|---|---|
| `Assets/Scripts/Data/CombatScale.cs` | Inch ↔ world conversion constants and helpers |
| `Assets/Scripts/Data/CombatStats.cs` | `visibilityRange` stat |
| `Assets/Scripts/Data/ModelSize.cs` | Base diameter and volume height in inches / world units |
| `Assets/Scripts/Combat/TestLevelUnitController.cs` | Spawns fog revealers from unit stats; drives Hide / Left-Shift LOS threat grid |
| `Assets/Scripts/Combat/CombatLosGridSampler.cs` | Angular enemy vision → inch cell mask for the threat grid |
| `Assets/Scripts/Combat/CombatLosGridOverlay.cs` | BG3-style red translucent LOS grid mesh |
| `Assets/Shaders/Combat/LosGridOverlay.shader` | Transparent fill + cell edge lines for the threat grid |
| `Assets/Editor/UnitTypeCreatorWindow.cs` | Authoring UI for visibility range |
