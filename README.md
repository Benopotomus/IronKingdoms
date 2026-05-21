# IronKingdoms
Turn-based strategy game with narrative storyline.

## Unity prototype bootstrap
This branch now contains a minimal Unity 6000.3.10f1 project scaffold with:
- a Warmachine Mk4-inspired combat prototype,
- a `TestCombatScene` for validating turn flow,
- ScriptableObject-driven unit/weapon definitions with Mk4 model sizes and shared weapon profiles, and
- editor tools for creating and editing unit and weapon assets.

Open `/home/runner/work/IronKingdoms/IronKingdoms` in Unity 6000.3.10f1, then load `Assets/Scenes/TestCombatScene.unity` and press Play.

`TestCombatScene` now includes `TestLevelUnitController`:
- assign player and enemy unit assets directly in the inspector,
- control player units by left-click/number keys and right-click move orders,
- let simple enemy AI drive enemy units, and
- view a live roster panel plus a lower-left selected-unit details panel.

Player activation is staged in the current prototype. Units move first, then take a combat action. After taking a combat action, a unit cannot use movement for the rest of that activation.

Design notes live in `Docs/CombatPrototypeDesign.md`.
