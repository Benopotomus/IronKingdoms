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

`CombatFogOfWarRevealer3D` controls the phase order:

1. `LineOfSightPhase1()` calls `base.LineOfSightPhase1()`.
2. The base FOW phase performs the normal physics raycasts against wall and fog-occluder colliders.
3. `LineOfSightPhase2()` waits for the base phase-1 jobs to complete.
4. `CombatForestFogRayPostProcessor.Apply()` edits the first-iteration ray buffers in place:
   - It keeps the nearer of the stock wall hit distance and the analytic forest clip distance.
   - It adds bridge hits for forest-limited miss rays so stock FOW sorting does not draw open chords through clipped forest arcs.
   - It forces forest-limited and adjacent open samples into the contour conditions used by stock sorting.
5. The revealer reruns `FirstIterationPointsAndConditionsJob` and then calls `base.LineOfSightPhase2()` so stock FOW builds the final contour from the edited ray buffers.

`CombatForestFogDebugContour` only records and draws debug lines after the post-processor finishes. It does not change visibility.

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
- `CombatForestFogRayPostProcessor`: modify FOW phase-1 ray buffers after wall hits are known.
- `CombatForestFogDebugContour`: store and draw debug-only forest contour data.
- `CombatFogOfWarRevealer3D`: keep the phase order explicit and hand work to the small classes above.
