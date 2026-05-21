# WARMACHINE Mk4 Rules Reference

> Extracted from *WARMACHINE Mk4 Core Book (Abridged Digital Edition)* — Steamforged Games / Privateer Press.  
> This document is an authoritative rules digest for use as a design guide for the IronKingdoms prototype.  
> All rules here supersede the earlier assumptions in `CombatPrototypeDesign.md`.

---

## Table of Contents

1. [Model Types](#1-model-types)
2. [Model Statistics (Stat Bar)](#2-model-statistics-stat-bar)
3. [Weapon Statistics](#3-weapon-statistics)
4. [Advantages](#4-advantages)
5. [Base Sizes & Model Volumes](#5-base-sizes--model-volumes)
6. [The Game Round & Player Turn](#6-the-game-round--player-turn)
7. [Maintenance Phase](#7-maintenance-phase)
8. [Control Phase](#8-control-phase)
9. [Activation Phase](#9-activation-phase)
10. [Normal Movement Options](#10-normal-movement-options)
11. [Line of Sight & Targeting](#11-line-of-sight--targeting)
12. [Combat Actions](#12-combat-actions)
13. [Attack Resolution](#13-attack-resolution)
14. [Melee Attacks](#14-melee-attacks)
15. [Ranged Attacks](#15-ranged-attacks)
16. [Arcane Attacks](#16-arcane-attacks)
17. [Special Attack Types (AOE, Spray)](#17-special-attack-types-aoe-spray)
18. [Damage](#18-damage)
19. [Damage Grids (Warjacks)](#19-damage-grids-warjacks)
20. [Warcasters & Focus](#20-warcasters--focus)
21. [Warjacks](#21-warjacks)
22. [Units](#22-units)
23. [Spells & Spellcasting](#23-spells--spellcasting)
24. [Continuous Effects & Status Effects](#24-continuous-effects--status-effects)
25. [Terrain](#25-terrain)
26. [Power Attacks](#26-power-attacks)
27. [Design Notes for IronKingdoms Prototype](#27-design-notes-for-ironkingdoms-prototype)

---

## 1. Model Types

| Type | Description |
|---|---|
| **Warcaster** | Independent Leader model. Controls a battlegroup of warjacks via focus allocation. Has ARC, CTRL, Feat, Focus Manipulation, Power Field, and Spellcaster special rules. |
| **Warjack** | Independent Cohort model. Armed war machine empowered by a warcaster's focus. Has Cortex, Damage Grid. Light = medium base, Heavy = large base, Super Heavy = XL base, Colossal = huge base. |
| **Unit** | Group of troopers activating together. Made up of Grunts + optional Attachments. |
| **Solo** | Individual warrior model. Operates alone. |
| **Warlock** | Like a Warcaster but controls Warbeasts and uses Fury Manipulation instead of Focus Manipulation. |
| **Warbeast** | Cohort model analogous to Warjack in Hordes/Circle factions. Uses Fury. Has Life Spiral instead of damage grid. |
| **Battle Engine** | Independent model on a huge base (120 mm). Has the Construct advantage. |
| **Structure** | Stationary, huge-base (120 mm). Has Construct advantage. |

---

## 2. Model Statistics (Stat Bar)

| Stat | Name | Usage |
|---|---|---|
| **SPD** | Speed | Model moves up to SPD inches on a full advance. |
| **MAT** | Melee Attack | Added to 2d6 for melee attack rolls. |
| **RAT** | Ranged Attack | Added to 2d6 for ranged attack rolls. |
| **AAT** | Arcane Attack | Added to 2d6 for arcane/spell attack rolls. |
| **DEF** | Defense | Attack roll must equal or exceed DEF to hit. |
| **ARM** | Armor | Model takes 1 damage point per point the damage roll exceeds ARM. |
| **ARC** | Arcana | Number of focus (warcasters) or fury (warlocks) points per turn. |
| **CTRL** | Control Range | Radius in inches of the warcaster/warlock's control range. |
| **FURY** | Fury | Max fury a warbeast can generate before risking frenzy. |
| **THR** | Threshold | Warbeast's resistance to frenzying. |

**Base stats vs. current stats:**  
- Base stats = printed values. Apply modifiers that **double** first, then **halve**, then **add bonuses**, then **apply penalties**. Result is the current stat. Stats cannot go below 0.

---

## 3. Weapon Statistics

| Stat | Name | Usage |
|---|---|---|
| **RNG** | Range | Max inches from point of origin to target. Attack auto-misses if target is beyond RNG. "SP" prefix = spray. "CTRL" = attacks within control range. |
| **ROF** | Rate of Fire | Number of initial ranged attacks per activation. |
| **AOE** | Area of Effect | Models within this many inches of a direct hit suffer blast damage. |
| **POW** | Power | Added to 2d6 for damage rolls. "—" = no damage. On AOE weapons: first number is direct hit POW, second (after "/") is blast damage POW. |
| **L/R/H** | Location | Warjack weapon location: Left arm, Right arm, Head, Superstructure. |

**Melee weapon** is denoted by a sword icon; **ranged weapon** by a pistol icon.

### Weapon Qualities (icons on stat bar)

| Quality | Effect |
|---|---|
| **Weapon Master** | Add an extra die to damage rolls. |
| **Shield** | Cumulative +2 ARM bonus while system is not crippled. |
| **Buckler** | Cumulative +1 ARM bonus while system is not crippled. |
| **Chain Weapon** | Ignores Buckler and Shield ARM bonuses and Shield Wall. |
| **Blessed** | Attacks ignore bonuses from spells/animi that add to ARM or DEF. |
| **Pistol** | Ignores Target in Melee DEF bonus. |
| **Disruption** | Warjack hit loses focus and cannot gain/channel focus for one round. |
| **Critical Disruption** | On a critical hit: target warjack suffers Disruption. |
| **Continuous Effect: Fire** | Hit model suffers the Fire continuous effect. |
| **Continuous Effect: Corrosion** | Hit model suffers the Corrosion continuous effect. |
| **Critical Fire** | On a critical hit: target suffers the Fire continuous effect. |
| **Critical Corrosion** | On a critical hit: target suffers the Corrosion continuous effect. |
| **Throw Power Attack** | Can be used to make throw power attacks. |
| **Damage Type: Cold/Fire/Corrosion/Electricity/Magical** | Specifies damage type for resistance purposes. |

---

## 4. Advantages

Advantages are always in effect. Marked as icons on the stat bar.

| Advantage | Effect |
|---|---|
| **Advance Deployment** | Deploy after normal deployment, up to 3" beyond zone. |
| **Ambush** | Can be held off-table and placed at end of any Control Phase after round 1. |
| **Amphibious** | Treats shallow water as open terrain while advancing; gains concealment while fully in shallow water; does not block LOS in shallow water. |
| **Arc Node** | Warjack can channel spells cast by its battlegroup controller. |
| **Assault** | Can make one ranged attack after charge movement but before Combat Action. Ignores Target in Melee penalty for that attack. |
| **Cavalry** | Cavalry model rules apply. |
| **Combined Melee Attack** | Can combine melee attacks with unit members (each extra model adds +1 to attack and damage rolls). |
| **Combined Ranged Attack** | Can combine ranged attacks with unit members (+1 per model to attack and damage rolls). Cannot target models in melee (except huge-based). |
| **Construct** | Not a living model. |
| **Dual Attack** | Can make both melee and ranged initial attacks in the same activation. |
| **Eyeless Sight** | Ignores cloud effects for LOS, ignores concealment and Stealth, never suffers Blind. |
| **Flight** | Can fly; moves over terrain and models freely, ignores falling. Lost while knocked down or stationary. |
| **Gladiator** | +2 on power attack damage and collateral damage rolls. |
| **Gunfighter** | Can make ranged attacks while engaged without restricting targeting to engaging models. |
| **Headbutt Power Attack** | Can make headbutt power attacks. |
| **Incorporeal** | Immune to continuous effects and non-magical damage. Cannot be pushed/slammed/thrown. Treats all non-impassable terrain as open. Can move through obstructions and models. Does not have to forfeit Combat Action on disengage. Loses Incorporeal when making a melee or ranged attack (until next activation). |
| **'Jack Marshal** | Can command warjacks. |
| **Pathfinder** | Treats rough terrain as open while advancing. Does not stop on obstacles during charge/slam/trample. |
| **Slam Power Attack** | Can make slam power attacks. |
| **Soulless** | Does not generate a soul token when destroyed. |
| **Stealth** | Ranged and arcane attacks from more than 5" away automatically miss. Not an intervening model for LOS from >5" away. |
| **Tough** | When disabled, roll d6: on 5–6, remove 1 damage point, model is no longer disabled but becomes knocked down. Loses Tough while knocked down. |
| **Trample Power Attack** | Can make trample power attacks. |
| **Undead** | Not a living model. |
| **Unstoppable** | Does not have to forfeit Combat Action when disengaging during Normal Movement. |

---

## 5. Base Sizes & Model Volumes

| Base Size | Diameter | Model Volume Height | Typical Models |
|---|---|---|---|
| Small | 30 mm | 1.75" | Human-sized warriors, most infantry |
| Medium | 40 mm | 2.25" | Larger creatures, light warjacks |
| Large | 50 mm | 2.75" | Very large creatures, heavy warjacks |
| Extra Large | 80 mm | 3.25" | Super-heavy warjacks, super-heavy warbeasts |
| Huge | 120 mm | 5" | Colossals, gargantuans, battle engines |

Volume heights are used for LOS and terrain blocking calculations.  
Extra-large and huge-based models **never** gain DEF bonuses from concealment, cover, or elevation.

---

## 6. The Game Round & Player Turn

- Battles are fought in a series of **game rounds**.
- Each game round, players take turns in established order (first player, then second player).
- A **round** lasts from the start of the current player's turn to the start of their next turn (both players take one turn each during that span).
- Game effects with a duration of "one round" expire at the beginning of the triggering player's next turn.

**A player's turn has three phases in order:**
1. **Maintenance Phase**
2. **Control Phase**
3. **Activation Phase**

---

## 7. Maintenance Phase

Resolve in this order:
1. Remove all focus points from warjacks. Remove focus points from models with Focus Manipulation that exceed their ARC. Remove fury points from models with Fury Manipulation that exceed their ARC. **Leave fury points on warbeasts.**
2. Check for expiration of **continuous effects** on your models. Roll d6 per continuous effect: 1–2 = expires; 3–6 = remains and takes effect.
3. Resolve all other Maintenance Phase effects.

---

## 8. Control Phase

Resolve in this order:
1. **Warcasters replenish focus**: Each model with Focus Manipulation gains focus so it has a number equal to its current ARC.  
   **Warlocks leech fury**: Each model with Fury Manipulation can leech any number of fury from warbeasts in its battlegroup within control range (cannot exceed current ARC).
2. **Spirit Bond** (Fury Manipulation only): Can gain up to 1 fury per medium-based or larger warbeast destroyed/removed this game.
3. **Warjacks power up**: Each warjack with a functional cortex within its controller's control range gains 1 focus point.
4. **Focus allocation**: Each model with Focus Manipulation can allocate focus points to warjacks in its battlegroup within its control range. Cannot allocate to a warjack with a crippled cortex. A warjack can hold at most 3 focus points at any time.
5. **Upkeep spells**: Each Focus/Fury Manipulation model can spend 1 focus or fury point per upkeep spell to keep it in play. Spells not maintained expire immediately.
6. **Threshold checks** (Fury Manipulation armies): Make a threshold check for each warbeast with 1+ fury. Warbeasts that fail immediately frenzy.
7. Resolve all other Control Phase effects.

---

## 9. Activation Phase

- **All models you control must be activated once per turn**, in the order you choose.
- Units and independent models activate one at a time.
- A model must be on the table to activate.
- A model cannot forfeit activation unless a special rule allows it.

**Model activation sequence:**
1. Normal Movement (if not forfeited).
2. Combat Action (if not forfeited).
3. Activation ends.

**Unit activation:**
- One trooper is chosen to move (all others are simultaneously placed within 2" of that model with LOS to it after its movement ends).
- Troopers that cannot be placed within 2" are destroyed.
- After placement, each trooper resolves its Combat Action one at a time.

**Forfeiting:**
- A model can voluntarily forfeit Normal Movement or Combat Action (not both), but cannot do so if also required to forfeit.
- Forfeiting Normal Movement still allows placement via special rules (Reposition, Sprint, etc.).

---

## 10. Normal Movement Options

A model or unit chooses **one** of the following each activation:

| Option | Details |
|---|---|
| **Forfeit** | No movement. |
| **Aim** | No movement. Gains +2 bonus to **every ranged attack roll** this activation. Cannot gain this bonus while engaged. |
| **Full Advance** | Moves up to current SPD in inches. |
| **Run** | Moves up to SPD + 5". Must forfeit Combat Action before advancing. Activation ends when run movement completes (no end-of-activation special rules apply). Warjack must spend 1 focus to run; warbeast must be forced. |
| **Charge** | Rushes into melee with a declared target. See Charge rules below. Warjack must spend 1 focus to charge; warbeast must be forced. |

**Advancing** = any intentional movement (full advance, run, or charge). Measured from the leading edge of the base.

**Rough terrain:** If a model begins in or would enter rough terrain at any point, reduce distance moved by 2" (minimum 1"). Only affects advancing, not involuntary movement.

### Charge Rules

1. Declare charge and target before moving. Model needs LOS to target.
2. Advance up to SPD + 3" in a straight line toward the target to bring it into melee range. Ignores terrain, distance, and other models for the direction check.
3. Cannot voluntarily stop until target is in melee range; can stop once target is in melee range.
4. Stops if contacting a model, obstacle, or obstruction (unless a special rule lets it pass through).
5. **Successful charge**: Target is in melee range at end of charge movement. First melee attack must target the charged model. If the charging model advanced at least 3", the first melee attack against the charge target is a **charge attack** (damage roll is automatically boosted).
6. **Failed charge**: Target not in melee range at end of movement. Activation ends.
7. A model that forfeits its Combat Action cannot charge. Cannot charge while engaged. Cannot target friendly models with a charge.

---

## 11. Line of Sight & Targeting

- A model must have **line of sight (LOS)** to target another model.
- LOS is checked from the **point of origin** (usually the attacking model or the model through which a spell is channeled).
- An **intervening model** is any model whose base can be crossed by a straight line between the two models' bases.

**LOS is blocked if the line:**
1. Passes through a terrain feature that blocks LOS, OR
2. Passes over the base of an intervening model with a base equal to or larger than the target's base, OR
3. Passes through a cloud effect.

**Troopers in a unit do not block LOS for other members of their unit.**

**Model volume** determines if terrain blocks LOS:
- Terrain shorter than 1.75" blocks LOS to nothing.
- Terrain 1.75"–2.24" blocks LOS to small-based models behind it.
- Terrain 2.25"–2.74" blocks LOS to small- and medium-based models.
- Terrain ≥ 2.75" blocks LOS to large-based and smaller models.

**Elevation and LOS:**
- An elevated model ignores intervening models at lower elevations (except those within 1" of the target).
- A model at lower elevation ignores intervening models lower than the elevated target.

**Range** is measured from the **nearest edge of the point of origin's base** to the nearest edge of the target's base.

---

## 12. Combat Actions

After Normal Movement, a model chooses one Combat Action option:

- **Forfeit** the Combat Action.
- **Make initial attacks** with each melee weapon (one attack each), OR make initial ranged attacks equal to each weapon's ROF.
- **Make one special attack** (listed as H Attack on the model).
- **Make one special action** (H Action).
- **Make one power attack** (if available).

A model **cannot** make both melee and ranged attacks in the same Combat Action (unless it has Dual Attack).

**Additional attacks** can be made after initial attacks if granted by a special rule (e.g., spending focus or being forced). Additional attacks are basic attacks. Cannot make special or power attacks as additional attacks.

**Running**: When using Normal Movement to run, the Combat Action is forfeited before moving.

**Disengaging**: A model that begins its Normal Movement engaged and advances out of enemy melee range must **forfeit its Combat Action** that activation (unless it has Unstoppable).

---

## 13. Attack Resolution

### Attack Roll Formula

| Attack Type | Roll |
|---|---|
| Melee | 2d6 + MAT |
| Ranged | 2d6 + RAT |
| Arcane | 2d6 + AAT |

- Attack **hits** if roll ≥ target's DEF.
- Attack **misses** if roll < target's DEF.
- **All 1s = automatic miss**. **All 6s = automatic hit** (unless rolling only one die).
- **Critical hit**: Attack hits and at least two dice show the same number.
- Attacks cannot target friendly models unless a special rule forces it.

**Boosting an attack roll**: Spend 1 focus/fury point before rolling to add an extra die. A particular roll can only be boosted once.

**Rerolls**: Resolve all rerolls before applying effects triggered by hit/miss.

**Effects triggering on a hit** are resolved before making the damage roll.

---

## 14. Melee Attacks

- Can target any model **in LOS and within melee range**.
- **Melee range** = longest melee range of any of the model's melee weapons.
- Individual weapons can only be used against targets within that weapon's own range.
- Models with no melee weapons have no melee range.

**Engaged / Engaging:**
- A model is **engaged** when it is within an enemy model's melee range and in that model's LOS.
- A model is **engaging** when it has an enemy within its own melee range and LOS.
- Both engaged and engaging = **in melee**.
- An engaged model at the start of Normal Movement cannot run, charge, or make slam power attacks.

**Melee attack roll modifiers:**
- Target behind obstacle/obstruction (any portion of volume obscured): +2 DEF to target vs. melee attacks.
- Knocked down target: automatic hit.
- Stationary target: automatic hit.

---

## 15. Ranged Attacks

- Can target any model **in LOS**.
- If target is beyond maximum RNG, the attack automatically misses.
- Initial attacks per activation = weapon's ROF.

**Ranged attack roll modifiers:**
| Modifier | Effect |
|---|---|
| Aim | +2 to all ranged attack rolls (cannot aim while engaged) |
| Concealment (target within 1" of feature) | +2 DEF to target vs. ranged/arcane |
| Cover (target within 1" of feature) | +4 DEF to target vs. ranged/arcane |
| Elevation (large or smaller, elevated) | +2 DEF vs. ranged/arcane from lower elevation |
| Engaged attacker | Can only target models engaging it; cannot aim |
| Target in melee (large or smaller) | +4 DEF to target vs. ranged/arcane |
| Knocked down target | Base DEF reduced to 5 |
| Stationary target | Base DEF reduced to 5 |

Cover and concealment are **not** cumulative with each other, but are cumulative with other DEF modifiers.  
Spray attacks ignore concealment, cover, and Stealth.  
Extra-large and huge-based models never gain DEF bonuses from concealment, cover, elevation, or Target in Melee.

**Engaged attacker**: A model engaged by enemy models can only make ranged attacks against models engaging it.

**Target in Melee**: Large or smaller models in melee gain +4 DEF against ranged and arcane attacks.

---

## 16. Arcane Attacks

- Ranged attack made by casting an offensive spell.
- Follows ranged attack modifiers except it is not affected by rules that affect only ranged attacks (e.g., aiming does not help arcane attacks).
- **Roll**: 2d6 + AAT vs. target DEF.
- Damage from spells is magical damage.

---

## 17. Special Attack Types (AOE, Spray)

### Area of Effect (AOE)

- On a **direct hit** against a target with an extra-large or smaller base: up to AOE number of additional models closest to the target within AOE inches also suffer a **blast damage roll** (POW = second number in the weapon's POW/blast listing).
- On a **miss** (but target was in range): target is still hit but not directly hit—suffers blast damage roll only.
- Blast damage cannot be boosted.
- Reducing a weapon's AOE to 0 makes it no longer an AOE attack.

### Spray Attacks

- Measure the full spray length, centered on the target model's base.
- Every model whose volume the line intersects (in LOS, accounting for terrain) can be hit.
- Make a **separate attack roll** against each model in the spray.
- Each hit is a **direct hit**. Separate damage rolls for each.
- Ignore concealment, cover, Stealth, and intervening models.
- Spray is a simultaneous attack.

---

## 18. Damage

### Damage Roll Formula

```
Damage Roll = 2d6 + POW
```

Compare result to the target's ARM. Target takes **1 damage point per point the roll exceeds ARM**.  
Boosted damage roll = add an extra die.  
A weapon with POW "—" does not deal damage.

**Damage types**: Cold, Fire, Corrosion, Electricity, Magical.  
If a model is **resistant** to a damage type, remove one die from the damage roll (even if multiple types, still only drop one die).

**Recording damage**: Mark damage boxes left to right (most models) or via damage grid/life spiral. A model is **disabled** when all damage boxes are marked.

**Damage capacity**: Most troopers have none (1 damage point kills them). Warcasters, warjacks, solos, etc., have damage capacity tracked with boxes.

---

## 19. Damage Grids (Warjacks)

- Warjack damage grids have **6 columns** of boxes labeled 1–6.
- When the warjack suffers damage, **roll a d6** to determine which column takes the damage. Fill from the top unmarked box downward. Wrap around from column 6 back to column 1 if needed.
- A warjack is **destroyed** when all damage boxes are marked.

**Warjack system letters:**
| Letter | System |
|---|---|
| A | Arc Node |
| C | Cortex |
| F | Front weapon system |
| L | Left arm weapon system |
| R | Right arm weapon system |
| H | Head |
| M | Movement |
| G | Power Field Generator |
| S | Superstructure (colossals only) |

**Crippled system effects:**
- **Cortex crippled**: Cannot have focus points. Cannot power up, be allocated focus, or channel spells.
- **Weapon system crippled**: Cannot use that weapon. Loses Shield/Buckler ARM bonus from that weapon.
- **Movement crippled**: Warjack suffers –2 SPD and cannot run or charge.
- **Arc Node crippled**: Cannot channel spells.

---

## 20. Warcasters & Focus

### Special Rules (All Warcasters)

**Battlegroup Controller / Leader**: Controls a battlegroup of warjacks. Can allocate focus to warjacks in the battlegroup within control range. A warjack cannot be allocated focus if it has a crippled cortex. Max 3 focus on a warjack at any time.

**Power Up**: During Control Phase, each warjack with a functional cortex in the battlegroup and within the warcaster's control range gains 1 focus point.

**Focus Manipulation**:
- During your Control Phase, the warcaster replenishes focus so it has a number of points equal to its current ARC.
- Starts the game with ARC focus points.
- During Maintenance Phase, loses all focus in excess of ARC.
- Unless stated otherwise, can spend focus only during its own activation.

**Control Range (CTRL)**:
- Circular area centered on the warcaster, radius = current CTRL in inches, measured from the edge of its base.
- Warjack must be in CTRL range to: power up, receive allocated focus, or channel spells.

**Focus: Additional Attack**: Spend 1 focus to make 1 additional melee attack during Combat Action.

**Focus: Boost**: Spend 1 focus to add an extra die to any attack or damage roll. Must declare before rolling. A roll can only be boosted once, but a model can boost multiple different rolls.

**Focus: Shake Effect** (during Control Phase, after allocating focus):
- Spend 1 focus to stand up from knocked down.
- Spend 1 focus to end a stationary effect.
- Spend 1 focus to end a shakeable effect (Blind, Shadow Bind, etc.).

**Power Field**: During activation, can spend focus to remove damage (1 point per focus). When about to suffer damage, can immediately spend 1 focus to reduce that damage by 5.

**Spellcaster**: Can cast spells by paying spell COST in focus.

**Racking Spells**: Some warcasters can select ("rack") additional spells before the game. Selections are revealed simultaneously and cannot change during the game.

**Feat**: Unique, once-per-game ability. Can be used at any time during activation (before/after moving or attacking, but not during).

---

## 21. Warjacks

### Special Rules (All Warjacks)

- Have **Construct** advantage (not living models).
- Independent Cohort models.
- Have **Cortex** and **Damage Grid**.
- Max 3 focus points at any time.
- Cannot gain/spend focus while Cortex is crippled.

**Focus: Additional Attack**: Spend 1 focus for 1 additional melee attack during Combat Action.

**Focus: Boost**: Spend 1 focus to add an extra die to any attack or damage roll.

**Focus: Run or Charge**: Must spend 1 focus to run or charge.

**Focus: Shake Effect** (during Control Phase after allocation): Spend 1 focus each to stand up, end stationary, or end shakeable effects.

**Power Attacks**: Can make power attacks by spending 1 focus.

**Autonomous Warjack**: A warjack without a controller. Becomes autonomous when its 'jack marshal is destroyed or its controller is under enemy control. Acts normally but gains no battlegroup/marshal benefits.

**Colossals (Huge base)**:
- Can only advance (never be placed).
- Never suffers Disruption.
- Cannot gain Advance Deployment, Incorporeal, Stealth, or Ambush.
- Cannot be pushed, knocked down, made stationary, slammed, or thrown.
- Cannot be affected by Grievous Wounds.
- Must be assigned to a battlegroup from the start of the game.

---

## 22. Units

- A **unit** is a group of troopers that activates together.
- Composed of **Grunts** (basic troopers) plus optional **Attachments** (command or weapon).
- A unit can have at most 1 command attachment and up to 3 weapon attachments.
- Weapon attachments replace Grunts; command attachments are added.

**Unit Movement (Normal Movement)**:
1. Choose one trooper to move (the "moving model").
2. After that model's movement, place all other troopers within 2" of it with LOS to it.
3. Troopers that cannot be placed within 2" are destroyed.

**Unit Combat Actions**: Each trooper resolves its Combat Action one at a time.

**Troopers do not block LOS for other members of their unit.**

---

## 23. Spells & Spellcasting

### Spell Statistics

| Stat | Description |
|---|---|
| **COST** | Focus or fury points to cast. |
| **RNG** | Range in inches from point of origin. "SELF" = self only. "CTRL" = any model in control range. |
| **AOE** | Area of effect (same as weapons). "CTRL" = centered on caster affecting all within CTRL. |
| **POW** | Damage power. "—" = no damage. Damage from spells is magical. |
| **UP** | Upkeep: yes/no. If yes, costs 1 focus/fury per turn in Control Phase to maintain. |
| **OFF** | Offensive spell: yes/no. Must make attack roll vs. DEF. |

### Casting Spells

- A model with ARC can cast any number of spells during its activation that it can afford.
- Can cast before moving, after moving, before attacking, after attacking — but not *while* moving or attacking.
- A spell's **point of origin** = the model casting or channeling it.
- Offensive spell: Make arcane attack roll (2d6 + AAT vs. DEF).
- Non-offensive spell with numeric RNG: If in range, applies automatically (no attack roll).

### Channeling Spells (Arc Node)

- A warcaster can channel a spell through a friendly warjack with the Arc Node advantage that is in its battlegroup and within its CTRL range.
- The warjack becomes the **point of origin** of the spell.
- The warcaster must have LOS to the arc node warjack, and the warjack's arc node must not be crippled.
- A warjack suffering Disruption cannot channel spells.

### Upkeep Spells

- Maintained during Control Phase for 1 focus/fury point.
- If not paid, the spell immediately expires.

---

## 24. Continuous Effects & Status Effects

### Continuous Effects

- Remain on a model and can affect it on subsequent turns.
- Resolved during your **Maintenance Phase**.
- Roll d6 per continuous effect: 1–2 = expires; 3–6 = remains and takes effect.
- A model can have only one of each continuous effect type at a time.

| Effect | Impact per turn |
|---|---|
| **Fire** | POW 12 fire damage roll each turn. Immunity: Resistance: Fire. |
| **Corrosion** | 1 point of corrosion damage each turn. Immunity: Resistance: Corrosion. |

### Knockdown

- Model cannot advance, attack, cast spells, use feats, make special actions, or channel spells.
- Does not have a melee range; does not engage or get engaged.
- Melee attacks against knocked down model **automatically hit**.
- Base DEF becomes 5.
- Does not block LOS; never an intervening model.
- Not cumulative.
- Stand up at start of next activation: forfeit either Normal Movement or Combat Action.
  - If forfeiting Combat Action to stand: can still make a full advance (cannot run, charge, or power attack).

### Stationary

- Same restrictions as knocked down.
- Melee attacks **automatically hit**.
- Base DEF becomes 5.
- Does not engage or get engaged.
- Removed by spending 1 focus (Focus Manipulation) or 1 fury (Fury Manipulation) during Control Phase.

### Blind

A shakeable effect. Model cannot make attacks. While blind, model suffers –4 MAT/RAT. Removed by spending focus/fury during Control Phase.

### Incorporeal (See Advantages section above)

---

## 25. Terrain

### Terrain Types

| Type | Effect |
|---|---|
| **Open** | Normal movement. No restrictions. |
| **Rough** | Moving through reduces advance by 2" (minimum 1"). Only affects advancing. |
| **Elevated** | Elevated large-or-smaller models gain +2 DEF vs. ranged/arcane from lower elevation. LOS rules modified by elevation. |
| **Difficult** | Cannot be entered by most models (not intended for play, not impassable per se). |
| **Impassable** | Cannot be entered for any reason. |

### Standard Terrain Features

| Feature | Rules |
|---|---|
| **Crater** | Rough terrain. Models completely within gain cover vs. attackers not touching the crater, and Resistance: Blast. |
| **Dense Fog** | Cloud effect clusters. Blocks LOS through more than 3" of fog. |
| **Dust Devil** | 3" template. +2 DEF for ranged attacks targeting models inside. –3 RNG for ranged attacks made from inside. |
| **Forest** | Rough terrain. Provides concealment to models completely inside. LOS can pass through up to 3" of forest. Cannot see completely through a forest. Forests do not block LOS to huge-based models. |
| **Hill** | Open terrain. Provides elevation. Does not provide cover or concealment. Models do not take falling damage moving off a hill. |
| **Obstruction** | 1" tall or greater. Difficult terrain. Only models with Flight can move through them. |

### Concealment vs. Cover

| Bonus | DEF Bonus | Granted By |
|---|---|---|
| **Concealment** | +2 DEF vs. ranged and arcane | Target within 1" of a concealment-granting feature (forests, hedges, bushes). |
| **Cover** | +4 DEF vs. ranged and arcane | Target within 1" of a cover-granting feature (stone walls, large boulders, buildings). |

- Not cumulative with each other.
- Both are cumulative with other DEF modifiers.
- Spray attacks ignore both.
- Extra-large and huge-based models never gain these bonuses.

---

## 26. Power Attacks

Available to warjacks, warbeasts, and some models with specific advantages. Spend 1 focus/fury to make a power attack.

| Power Attack | Summary |
|---|---|
| **Headbutt** | Model in melee range must make a STR vs. STR roll; on success, target is knocked down. |
| **Slam** | Combines Normal Movement and Combat Action. Move directly toward target, STR vs. STR; on success target is moved directly away from attacker and knocked down. Additional die to damage if it contacts equal/larger base, obstacle, or obstruction. Collateral damage to models it contacts. |
| **Throw** | Target is thrown (moved directly away) and knocked down; suffers damage roll. Models it contacts with equal/smaller base also knocked down and suffer collateral damage. |
| **Trample** | Combine Normal Movement and Combat Action. Move up to SPD + 3" in a straight line. Move through small-based models in the path (must have room at end). Make melee attack rolls against each small-based enemy model moved through. Simultaneous damage. |

**Falling damage:**
- Fall of up to 2" = 2d6 + POW 12.
- Add 1d6 for each additional 2" of fall (rounded up).
- Models landed on (equal/smaller base) also become knocked down and suffer the same damage roll.

---

## 27. Design Notes for IronKingdoms Prototype

The following rules from the actual Mk4 rulebook should be reconciled with the current prototype implementation in `Assets/Scripts/Combat/`:

### Turn Flow Corrections

The correct three-phase turn structure is **Maintenance → Control → Activation** (not "Maintenance → Control → Activation" with the old assumption that it cycles per-unit). The correct flow:

1. **Maintenance**: Remove excess focus/fury, resolve continuous effects.
2. **Control**: Replenish warcaster focus, power up warjacks, allocate focus, maintain upkeep spells.
3. **Activation**: All models activate once, in any order.

### Core Dice Formulas to Implement

```
Attack Roll  = 2d6 + MAT (melee) / RAT (ranged) / AAT (arcane)  ≥ target DEF
Damage Roll  = 2d6 + weapon POW  > target ARM  (excess = damage points)
Boosted roll = add one extra d6 (declared before rolling)
Critical hit = hit + at least two dice showing the same number
Charge attack = automatically boosted first damage roll (if charged ≥ 3")
```

### Stats to Track Per Unit

- SPD, MAT, RAT, DEF, ARM, damage capacity
- For warcasters: ARC, CTRL, focus points current value, upkeep spells
- For warjacks: damage grid (6 columns), system damage (C, M, F, L, R, H)
- Weapon: RNG, ROF, AOE, POW, type (melee/ranged), weapon qualities

### Key Combat Modifiers Checklist

- [ ] Aim: +2 to all ranged attack rolls (cannot aim while engaged)
- [ ] Charge attack: boosted first damage roll if advanced ≥ 3"
- [ ] Boost (focus/fury): +1d6 to attack or damage roll
- [ ] Knocked down target: auto-hit; base DEF 5
- [ ] Stationary target: auto-hit; base DEF 5
- [ ] Target in melee (large or smaller): +4 DEF vs. ranged/arcane
- [ ] Concealment: +2 DEF vs. ranged/arcane
- [ ] Cover: +4 DEF vs. ranged/arcane
- [ ] Elevation: +2 DEF vs. ranged/arcane from lower elevation (large or smaller only)
- [ ] Engaged attacker: ranged attacks restricted to engaging models only; cannot aim
- [ ] Melee against model behind obstacle: +2 DEF

### Model Types for the Prototype

Priority model types to support (in order):
1. Warcaster (Focus Manipulation, Spellcaster, Feat, Power Field)
2. Heavy Warjack (Cortex, Damage Grid, Focus spending)
3. Infantry Unit (Grunts + unit movement rules)
4. Solo (independent warrior)

### Known Gaps vs. Rulebook

The existing prototype omits (future work):
- Warbeast / Warlock (Fury / Forcing / Life Spiral / Frenzy / Threshold)
- Full damage grid system with per-column d6 roll
- Warcaster power field (reduce incoming damage with focus)
- Unit Combined Melee/Ranged Attack advantages
- AOE and spray attack resolution
- Spellcasting and channeling through arc nodes
- Feat system
- Power attacks (Slam, Throw, Headbutt, Trample)
- Continuous effects (Fire, Corrosion) with d6 expiry rolls
- Status effects: Knockdown, Stationary, Blind
- Terrain types: concealment, cover, elevation bonuses
