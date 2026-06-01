using System;

namespace IronKingdoms.Combat
{
    [Flags]
    public enum UnitAdvantage
    {
        None = 0,
        AdvanceDeployment = 1 << 0,
        Ambush = 1 << 1,
        Amphibious = 1 << 2,
        ArcNode = 1 << 3,
        Assault = 1 << 4,
        Cavalry = 1 << 5,
        CombinedMeleeAttack = 1 << 6,
        CombinedRangedAttack = 1 << 7,
        Construct = 1 << 8,
        DualAttack = 1 << 9,
        EyelessSight = 1 << 10,
        Flight = 1 << 11,
        Gladiator = 1 << 12,
        Gunfighter = 1 << 13,
        HeadbuttPowerAttack = 1 << 14,
        Incorporeal = 1 << 15,
        JackMarshal = 1 << 16,
        Pathfinder = 1 << 17,
        SlamPowerAttack = 1 << 18,
        Soulless = 1 << 19,
        Stealth = 1 << 20,
        Tough = 1 << 21,
        TramplePowerAttack = 1 << 22,
        Undead = 1 << 23,
        Unstoppable = 1 << 24
    }
}
