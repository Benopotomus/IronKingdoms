using System;

namespace IronKingdoms.Combat
{
    public static class CombatSimulator
    {
        private const float MeleeRangeThreshold = 1.5f;

        public static CombatSimulationResult Simulate(TestCombatScenarioAsset scenario)
        {
            var result = new CombatSimulationResult();

            if (scenario == null || scenario.Attacker == null || scenario.Defender == null)
            {
                result.AddLine("Scenario is missing one or more unit references.");
                return result;
            }

            var random = new Random(scenario.RandomSeed);
            var attacker = new CombatantState(scenario.Attacker);
            var defender = new CombatantState(scenario.Defender);
            var distance = Math.Max(0f, scenario.StartingDistance);

            result.AddLine($"Starting duel: {attacker.Name} vs {defender.Name} at {distance:0.0}\"");

            for (var round = 1; round <= scenario.MaxRounds; round++)
            {
                result.AddLine($"Round {round}");
                ExecuteTurn(attacker, defender, random, ref distance, result);
                if (defender.IsDefeated)
                {
                    result.WinnerName = attacker.Name;
                    result.RoundsCompleted = round;
                    result.AddLine($"Winner: {attacker.Name}");
                    return result;
                }

                ExecuteTurn(defender, attacker, random, ref distance, result);
                if (attacker.IsDefeated)
                {
                    result.WinnerName = defender.Name;
                    result.RoundsCompleted = round;
                    result.AddLine($"Winner: {defender.Name}");
                    return result;
                }
            }

            result.RoundsCompleted = scenario.MaxRounds;
            if (attacker.Health == defender.Health)
            {
                result.WinnerName = "Draw";
                result.AddLine("Result: draw on remaining health.");
            }
            else
            {
                result.WinnerName = attacker.Health > defender.Health ? attacker.Name : defender.Name;
                result.AddLine($"Winner on remaining health: {result.WinnerName}");
            }

            return result;
        }

        private static void ExecuteTurn(CombatantState actor, CombatantState target, Random random, ref float distance, CombatSimulationResult result)
        {
            result.AddLine($"- {actor.Name} Maintenance");
            actor.Resource = actor.Definition.Stats.startingResource;
            result.AddLine($"- {actor.Name} Control: restore resource to {actor.Resource}");
            result.AddLine($"- {actor.Name} Activation");

            var charged = TryCharge(actor, target, ref distance, result);
            if (!charged)
            {
                AdvanceIntoRange(actor, ref distance, result);
            }

            var weapon = actor.PrimaryWeapon;
            if (distance > weapon.Range)
            {
                result.AddLine($"  {actor.Name} ends activation at {distance:0.0}\" and cannot attack.");
                return;
            }

            var inMelee = weapon.AttackType == WeaponAttackType.Melee;
            var baseAttackStat = inMelee ? actor.Definition.Stats.meleeAttack : actor.Definition.Stats.rangedAttack;
            var attackStat = baseAttackStat + weapon.GetAttackModifier();
            var boostedAttack = actor.Resource > 0 && actor.Role != UnitRole.Infantry;
            if (boostedAttack)
            {
                actor.Resource -= 1;
            }

            var attackDiceCount = weapon.GetAttackDiceCount(boostedAttack);
            var attackDice = RollDice(random, attackDiceCount);
            var attackTotal = Sum(attackDice) + attackStat;
            var attackNote = boostedAttack ? " (boosted)" : string.Empty;
            result.AddLine($"  Attack roll: {attackTotal} vs DEF {target.Definition.Stats.defense}{attackNote}");
            if (!weapon.EvaluateAttackHit(attackDice[0], attackDice[1], attackTotal, target.Definition.Stats.defense))
            {
                result.AddLine($"  {actor.Name} misses.");
                return;
            }

            var autoBoostDamage = charged && inMelee;
            var boostedDamage = false;
            if (!autoBoostDamage && actor.Resource > 0 && actor.Role != UnitRole.Infantry)
            {
                actor.Resource -= 1;
                boostedDamage = true;
            }

            var damageDiceCount = weapon.GetDamageDiceCount(autoBoostDamage || boostedDamage);
            var damageDice = RollDice(random, damageDiceCount);
            var damageRoll = Sum(damageDice);
            var appliedDamage = weapon.EvaluateDamage(damageRoll, target.Definition.Stats.armor);
            target.Health = Math.Max(0, target.Health - appliedDamage);

            var damageNote = autoBoostDamage ? " (charge boost)" : boostedDamage ? " (boosted)" : string.Empty;
            result.AddLine($"  Damage roll: {damageRoll + weapon.Power} - ARM {target.Definition.Stats.armor} = {appliedDamage}{damageNote}");
            result.AddLine($"  {target.Name} health remaining: {target.Health}");
        }

        private static bool TryCharge(CombatantState actor, CombatantState target, ref float distance, CombatSimulationResult result)
        {
            if (actor.PrimaryWeapon.AttackType != WeaponAttackType.Melee || actor.Role == UnitRole.Infantry)
            {
                return false;
            }

            var reachableDistance = actor.Definition.Stats.speed + 3f;
            var remainingGap = distance - actor.PrimaryWeapon.Range;
            if (remainingGap <= 0f || remainingGap > reachableDistance || actor.Resource <= 0)
            {
                return false;
            }

            actor.Resource -= 1;
            distance = actor.PrimaryWeapon.Range;
            result.AddLine($"  {actor.Name} charges into melee with {target.Name}.");
            return true;
        }

        private static void AdvanceIntoRange(CombatantState actor, ref float distance, CombatSimulationResult result)
        {
            var targetDistance = actor.PrimaryWeapon.Range;
            if (distance <= targetDistance)
            {
                result.AddLine($"  {actor.Name} is already in range.");
                return;
            }

            var moveDistance = Math.Min(actor.Definition.Stats.speed, distance - targetDistance);
            distance = Math.Max(targetDistance, distance - moveDistance);
            result.AddLine($"  {actor.Name} advances {moveDistance:0.0}\" and closes to {distance:0.0}\".");
        }

        private static int[] RollDice(Random random, int count)
        {
            var size = Math.Max(2, count);
            var dice = new int[size];
            for (var i = 0; i < size; i++)
            {
                dice[i] = random.Next(1, 7);
            }

            return dice;
        }

        private static int Sum(int[] dice)
        {
            var total = 0;
            foreach (var d in dice)
            {
                total += d;
            }

            return total;
        }

        private sealed class CombatantState
        {
            public CombatantState(UnitTypeDefinition definition)
            {
                Definition = definition;
                Health = definition.Stats.health;
                Resource = definition.Stats.startingResource;
            }

            public UnitTypeDefinition Definition { get; }
            public string Name => Definition.DisplayName;
            public UnitRole Role => Definition.Role;
            public WeaponProfile PrimaryWeapon => Definition.Stats.GetPrimaryWeapon();
            public int Health { get; set; }
            public int Resource { get; set; }
            public bool IsDefeated => Health <= 0;
        }
    }
}
