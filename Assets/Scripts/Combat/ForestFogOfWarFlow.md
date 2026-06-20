# Forest Fog Of War Flow

This note tracks the combat-specific fog-of-war path for limited-depth terrain such as forests.

## Authored Forest Depth

`CombatTerrainFeatureDefinition` owns the authored rule:

- `LineOfSightMode = LimitedDepth` marks the feature as forest-like limited-depth terrain.
- `LineOfSightPassThroughDepthInches` is the pass-through distance, usually `3` inches.

`CombatZone` registers active terrain zones in `CombatZone.ActiveZones`. When a zone enables or disables, it invalidates `CombatForestFogClipper` so the next fog pass rebuilds the active limited-depth cache.

## Forest Depth Calculation

`CombatForestFogClipper` is the analytic XZ clipper.

1. `EnsureCache()` collects active `LimitedDepth` zones, their collider bounds, and their depth limits in world units.
2. `GetStrictestLimitedDepthWorld()` returns the smallest active limited-depth value.
3. `CombatForestFogDepth.ResolveDepthWorld()` applies the shared fallback of `3` inches when no active zone reports a usable depth.
4. `GetFirstContactDepthClipDistanceWorld()` walks each fog ray from the revealer eye, finds the next forest entry, measures whether that forest is deeper than the allowed pass-through distance, and returns either the forest clip distance or the full revealer radius.

The key rule is first contact plus depth: open ground before forest does not consume forest depth, and thin forest that exits before the depth limit does not clip the ray.

## Where Forest Is Processed

`CombatFogOfWarRevealer3D` keeps stock FOW pass 1 untouched:

1. `LineOfSightPhase1()` calls `base.LineOfSightPhase1()` (physics raycasts only).
2. `LineOfSightPhase2()` calls `base.LineOfSightPhase2()` (SortData, FindEdges, SetData).
3. Pass-1 `ViewPoints` upload to the GPU as the baseline wall polygon — unchanged by forest code.
4. `OnAfterResolveEdges()` appends a separate terrain LUT (forest/cloud analytic clip) after the baseline segments.
5. The fog shader applies baseline wall wedges first, then `MinTerrainClipIntoDistance` only tightens open ground.

Forest never modifies phase-1 ray buffers or wall FindEdges output.

## Where Wall Base Calculations Run

Wall handling remains in the imported FOW base path:

1. `CombatFogFramePrepare` runs before the fog world update and calls `CombatFogOccluderRuntimePolicy.PrepareForFogRaycasts()`.
2. The runtime policy prepares dynamic fog occluders such as `CombatOrbitingFogOccluderCubes`.
3. `CombatFogOfWarRevealer3D.LineOfSightPhase1()` calls `FogOfWarRevealer3D.LineOfSightPhase1()`.
4. `FogOfWarRevealer3D` schedules the raycast setup and `RaycastCommand.ScheduleBatch` against the configured obstacle mask.
5. Those base raycast results become `FirstIteration.Hits`, `Distances`, `Points`, and `Normals`.
6. Forest processing only tightens those results. It never replaces the stock wall raycast path.

## Class Responsibilities

- `CombatForestFogClipper`: find forest entries/exits and compute per-ray clip distance.
- `CombatForestFogDepth`: resolve the shared depth value used by clipping.
- `CombatForestFogRayPostProcessor`: build terrain LUT upload after stock pass 1 completes; never edits wall segments.
- `CombatForestFogDebugContour`: store and draw debug-only forest contour data.
- `CombatFogOfWarRevealer3D`: keep the phase order explicit and hand work to the small classes above.
