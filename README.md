# IronKingdoms
Turn-based strategy game with narrative storyline.

## Unity prototype bootstrap
This branch now contains a minimal Unity 6000.3.10f1 project scaffold with:
- a Warmachine Mk4-inspired combat prototype,
- a `TestCombatScene` for validating turn flow,
- ScriptableObject-driven unit definitions, and
- an editor tool for creating and editing unit stat assets with weapon profiles.

Open `/home/runner/work/IronKingdoms/IronKingdoms` in Unity 6000.3.10f1, then load `Assets/Scenes/TestCombatScene.unity` and press Play.

`TestCombatScene` now includes `TestLevelUnitController`:
- assign player and enemy unit assets directly in the inspector,
- control player units by left-click/number keys and right-click move orders,
- let simple enemy AI drive enemy units, and
- view a live roster panel plus selected-unit details popout UI.

Design notes live in `Docs/CombatPrototypeDesign.md`.
