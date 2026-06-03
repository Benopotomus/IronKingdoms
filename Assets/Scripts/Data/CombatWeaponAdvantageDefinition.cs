using UnityEngine;

namespace IronKingdoms.Combat
{
    public enum WeaponAdvantageKind
    {
        Other = 0,
        WeaponMaster = 1,
        Shield = 2,
        Buckler = 3,
        ChainWeapon = 4,
        Blessed = 5,
        Pistol = 6,
        Disruption = 7,
        CriticalDisruption = 8,
        ContinuousEffectFire = 9,
        ContinuousEffectCorrosion = 10,
        CriticalFire = 11,
        CriticalCorrosion = 12,
        ThrowPowerAttack = 13,
        DamageTypeCold = 14,
        DamageTypeFire = 15,
        DamageTypeCorrosion = 16,
        DamageTypeElectricity = 17,
        DamageTypeMagical = 18
    }

    [CreateAssetMenu(menuName = "Iron Kingdoms/Combat/Weapon Advantage", fileName = "WeaponAdvantage")]
    public class CombatWeaponAdvantageDefinition : ScriptableObject
    {
        [SerializeField] private string advantageId;
        [SerializeField] private string displayName;
        [TextArea] [SerializeField] private string description;
        [SerializeField] private WeaponAdvantageKind kind = WeaponAdvantageKind.Other;

        [Header("Rules (Mk4-inspired)")]
        [SerializeField] private bool addsExtraDamageDie;
        [SerializeField] private int armorBonus;
        [SerializeField] private bool ignoresShieldAndBucklerArmor;
        [SerializeField] private bool ignoresSpellDefAndArmBonuses;
        [SerializeField] private bool ignoresTargetInMeleeDefBonus;
        [SerializeField] private bool appliesDisruptionOnHit;
        [SerializeField] private bool appliesDisruptionOnCriticalOnly;
        [SerializeField] private bool appliesFireContinuousEffectOnHit;
        [SerializeField] private bool appliesFireContinuousEffectOnCriticalOnly;
        [SerializeField] private bool appliesCorrosionContinuousEffectOnHit;
        [SerializeField] private bool appliesCorrosionContinuousEffectOnCriticalOnly;
        [SerializeField] private bool enablesThrowPowerAttack;

        public string AdvantageId => string.IsNullOrWhiteSpace(advantageId) ? name : advantageId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description ?? string.Empty;
        public WeaponAdvantageKind Kind => kind;
        public bool AddsExtraDamageDie => addsExtraDamageDie;
        public int ArmorBonus => armorBonus;
        public bool IgnoresShieldAndBucklerArmor => ignoresShieldAndBucklerArmor;
        public bool IgnoresSpellDefAndArmBonuses => ignoresSpellDefAndArmBonuses;
        public bool IgnoresTargetInMeleeDefBonus => ignoresTargetInMeleeDefBonus;
        public bool AppliesDisruptionOnHit => appliesDisruptionOnHit;
        public bool AppliesDisruptionOnCriticalOnly => appliesDisruptionOnCriticalOnly;
        public bool AppliesFireContinuousEffectOnHit => appliesFireContinuousEffectOnHit;
        public bool AppliesFireContinuousEffectOnCriticalOnly => appliesFireContinuousEffectOnCriticalOnly;
        public bool AppliesCorrosionContinuousEffectOnHit => appliesCorrosionContinuousEffectOnHit;
        public bool AppliesCorrosionContinuousEffectOnCriticalOnly => appliesCorrosionContinuousEffectOnCriticalOnly;
        public bool EnablesThrowPowerAttack => enablesThrowPowerAttack;

        public bool IsDamageTypeAdvantage =>
            kind == WeaponAdvantageKind.DamageTypeCold
            || kind == WeaponAdvantageKind.DamageTypeFire
            || kind == WeaponAdvantageKind.DamageTypeCorrosion
            || kind == WeaponAdvantageKind.DamageTypeElectricity
            || kind == WeaponAdvantageKind.DamageTypeMagical;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(advantageId))
            {
                advantageId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }
}
