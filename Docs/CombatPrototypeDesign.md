# Combat Prototype Design

## Goal
Bootstrap this branch as a Unity 6000.3.10f1 project with a small, testable combat loop inspired by the baseline Warmachine Mk4 turn structure.

## Source note
The requested Google Drive rules document was not reachable from this sandbox, so this prototype uses publicly available Warmachine Mk4 high-level concepts as a starting point and calls out assumptions that should be reconciled once the exact source document is available.

## Prototype scope
- Unity version pinned to `6000.3.10f1`.
- One playable verification scene: `Assets/Scenes/TestCombatScene.unity`.
- ScriptableObject-driven unit definitions for authoring unit archetypes and their stat blocks.
- Weapon profile assignment per unit (power/range/type) to mirror Mk4-style loadouts.
- A small runtime combat simulator that exercises a three-phase turn loop:
  1. Maintenance
  2. Control
  3. Activation
- A lightweight editor window for creating and editing unit types.
- A test-level runtime controller for assigning player/enemy armies in inspector and validating command + AI behavior.

## Warmachine-inspired rules digest used here
This implementation intentionally stays at the "starter combat harness" level rather than a full rules engine.

### Turn flow
- Combat alternates by round.
- Each model turn is resolved as Maintenance -> Control -> Activation.
- The acting unit refreshes its command resource during Control.
- Activation is staged for the player and enemy prototype flows:
  1. Resolve movement (Advance/Run/Charge) or forfeit movement to Aim (+2 to hit on the next attack this activation).
  2. Resolve one combat action.
  3. After the combat action is taken, movement is no longer available for that unit this activation.

### Combat resolution
- Attacks use 2d6 plus the relevant attack stat.
- Hits are resolved against DEF.
- Damage uses 2d6 plus weapon power against ARM.
- Resource-bearing units can boost attack and damage.
- Melee units can perform a simple charge if they can close the gap within `speed + 3`, granting a boosted first damage roll.

### Data model
Each `UnitTypeDefinition` captures:
- role,
- movement speed,
- model size (Mk4 base diameter and volume height),
- melee attack,
- ranged attack,
- defense,
- armor,
- health,
- starting resource,
- weapon profiles (name, attack type, power, range).

## Test scene layout
`Assets/Scenes/TestCombatScene.unity` contains:
- a `Main Camera`,
- a `Directional Light`,
- `AttackerSpawn`,
- `DefenderSpawn`, and
- `CombatFlowBootstrap`, and
- `TestLevelUnitController` (attached to the bootstrap object).

`TestLevelUnitController` exposes inspector-assigned player/enemy unit arrays, spawns assigned units in the test level, gives player-issued move orders to selected units, enforces staged activation (movement before combat action), runs a simple enemy pursuit/attack loop, and draws a roster + lower-left selected-unit panel UI in play mode.

## Authoring workflow
### Unit types
Use `Iron Kingdoms/Tools/Unit Type Creator` from the Unity menu to create and edit `UnitTypeDefinition` assets with weapon profile assignment.

### Test scenarios
Create or duplicate `TestCombatScenarioAsset` assets and point them at different attacker/defender unit definitions to validate new balance ideas.

## Included sample content
- `Assets/Data/Units/Steam Knight.asset`: short-range armored bruiser with resource boosts.
- `Assets/Data/Units/Line Rifleman.asset`: ranged infantry target for combat-flow verification.
- `Assets/Data/Units/Ironclad Vanguard.asset`: heavy Mk4-inspired armored warjack-style profile.
- `Assets/Data/Units/Tempest Gun Mage.asset`: elite Mk4-inspired ranged command unit profile.
- `Assets/Data/Scenarios/TestCombatScenario.asset`: sample duel used by the scene.

## Known limitations
- No board, terrain, line-of-sight, or scenario objective rules yet.
- No polished production UI beyond prototype IMGUI command panels.
- No animation, VFX, or map logic yet.
- The inaccessible source document still needs a pass to reconcile terminology and any custom deviations from baseline Warmachine Mk4 rules.

## Recommended next pass
1. Reconcile this document with the intended Google Drive rules pack.
2. Expand the simulator from duel resolution into multi-model activations.
3. Add scenario objectives and grid/zone validation.
4. Add dedicated Unity edit mode or play mode tests once Unity CI/editor access is available.
