# Test Level Combat Controller Organization

`TestLevelUnitController` is still one Unity component for scene compatibility, but its implementation is split into partial files by responsibility. This keeps existing serialized scene/prefab references stable while making the prototype controller easier to navigate.

## Files

- `TestLevelUnitController.cs`  
  Main match loop and tightly coupled combat flow: `Awake`, `Start`, `Update`, selection state, player input, movement/pathing, enemy AI, attacks, line of sight, and fog visibility.

- `TestLevelUnitController.Types.cs`  
  Private runtime data owned by the controller: turn/action enums, floating damage entries, and `RuntimeUnit`.

- `TestLevelUnitController.Setup.cs`  
  Scene service discovery and one-time global setup: camera manager, nav path builder, definition catalog, `FogOfWarWorld`, and FOW camera effect.

- `TestLevelUnitController.Spawning.cs`  
  Unit creation and pawn configuration: spawn placements, prefab/procedural pawn creation, pawn sizing, navmesh cut setup, and player fog revealer setup.

- `TestLevelUnitController.Gui.cs`  
  IMGUI presentation and transient visual feedback: roster panels, selected-unit panel, action bar, hover panels, combat log, and floating damage text.

## Ownership Rules

- Keep scene-level wiring and global services in `Setup`.
- Keep anything that creates or configures spawned pawn GameObjects in `Spawning`.
- Keep GUI drawing and GUI-only formatting in `Gui`.
- Keep rules that mutate the match state in the main controller until they can be promoted to dedicated services with explicit inputs/outputs.

## Next Extraction Targets

The remaining main controller is still large because movement, combat resolution, AI, and visibility share `RuntimeUnit` state heavily. Good next candidates are:

- `CombatMovementController` for movement orders, path previews, rough-terrain cost, and navmesh queries.
- `CombatTurnController` for player/enemy turn transitions and enemy activation sequencing.
- `CombatVisibilityController` for live fog visibility and line-of-sight cache ownership.
- `CombatAttackResolver` for hit/damage rolls and combat log event generation.

Those should become real collaborating classes once their APIs are clear enough to avoid leaking most of `TestLevelUnitController` state.
