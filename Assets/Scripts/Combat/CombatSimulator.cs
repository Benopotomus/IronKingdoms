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

            if (distance > actor.Definition.Stats.weaponRange)
            {
                result.AddLine($"  {actor.Name} ends activation at {distance:0.0}\" and cannot attack.");
                return;
            }

            var inMelee = distance <= MeleeRangeThreshold;
            var attackStat = inMelee ? actor.Definition.Stats.meleeAttack : actor.Definition.Stats.rangedAttack;
            var boostedAttack = actor.Resource > 0 && actor.Role != UnitRole.Infantry;
            if (boostedAttack)
            {
                actor.Resource -= 1;
            }

            var attackRoll = Roll(random, boostedAttack ? 3 : 2) + attackStat;
            result.AddLine($"  Attack roll: {attackRoll} vs DEF {target.Definition.Stats.defense}" + (boostedAttack ? " (boosted)" : string.Empty));
            if (attackRoll < target.Definition.Stats.defense)
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

            var damageRoll = Roll(random, autoBoostDamage || boostedDamage ? 3 : 2) + actor.Definition.Stats.weaponPower;
            var appliedDamage = Math.Max(0, damageRoll - target.Definition.Stats.armor);
            target.Health = Math.Max(0, target.Health - appliedDamage);

            var damageNote = autoBoostDamage ? " (charge boost)" : boostedDamage ? " (boosted)" : string.Empty;
            result.AddLine($"  Damage roll: {damageRoll} - ARM {target.Definition.Stats.armor} = {appliedDamage}{damageNote}");
            result.AddLine($"  {target.Name} health remaining: {target.Health}");
        }

        private static bool TryCharge(CombatantState actor, CombatantState target, ref float distance, CombatSimulationResult result)
        {
            if (actor.Definition.Stats.weaponRange > MeleeRangeThreshold || actor.Role == UnitRole.Infantry)
            {
                return false;
            }

            var reachableDistance = actor.Definition.Stats.speed + 3f;
            var remainingGap = distance - actor.Definition.Stats.weaponRange;
            if (remainingGap <= 0f || remainingGap > reachableDistance || actor.Resource <= 0)
            {
                return false;
            }

            actor.Resource -= 1;
            distance = actor.Definition.Stats.weaponRange;
            result.AddLine($"  {actor.Name} charges into melee with {target.Name}.");
            return true;
        }

        private static void AdvanceIntoRange(CombatantState actor, ref float distance, CombatSimulationResult result)
        {
            var targetDistance = actor.Definition.Stats.weaponRange;
            if (distance <= targetDistance)
            {
                result.AddLine($"  {actor.Name} is already in range.");
                return;
            }

            var moveDistance = Math.Min(actor.Definition.Stats.speed, distance - targetDistance);
            distance = Math.Max(targetDistance, distance - moveDistance);
            result.AddLine($"  {actor.Name} advances {moveDistance:0.0}\" and closes to {distance:0.0}\".");
        }

        private static int Roll(Random random, int dice)
        {
            var total = 0;
            for (var i = 0; i < dice; i++)
            {
                total += random.Next(1, 7);
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
            public int Health { get; set; }
            public int Resource { get; set; }
            public bool IsDefeated => Health <= 0;
        }
    }
}
