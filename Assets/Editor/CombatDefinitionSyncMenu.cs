#if UNITY_EDITOR
using System;
using System.IO;
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    public static class CombatDefinitionSyncMenu
    {
        private const string CombatDataRoot = "Assets/Data/Combat";
        private const string AdvantagesFolder = CombatDataRoot + "/Advantages";
        private const string WeaponAdvantagesFolder = CombatDataRoot + "/WeaponAdvantages";
        private const string ModifiersFolder = CombatDataRoot + "/DefenseModifiers";
        private const string TerrainFolder = CombatDataRoot + "/TerrainFeatures";
        private const string AbilitiesFolder = CombatDataRoot + "/Abilities";
        private const string CatalogPath = CombatDataRoot + "/CombatDefinitionCatalog.asset";

        [MenuItem("Iron Kingdoms/Tools/Sync Combat Definition Assets")]
        public static void SyncCombatDefinitionAssets()
        {
            EnsureFolder(CombatDataRoot);
            EnsureFolder(AdvantagesFolder);
            EnsureFolder(WeaponAdvantagesFolder);
            EnsureFolder(ModifiersFolder);
            EnsureFolder(TerrainFolder);
            EnsureFolder(AbilitiesFolder);

            var advantages = SyncAdvantages();
            var weaponAdvantages = SyncWeaponAdvantages();
            var defenseModifiers = SyncDefenseModifiers();
            var terrainFeatures = SyncTerrainFeatures(defenseModifiers);
            var abilities = SyncAbilities();
            SyncCatalog(advantages, weaponAdvantages, defenseModifiers, terrainFeatures, abilities);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Combat definition assets synced under Assets/Data/Combat.");
        }

        private static CombatAdvantageDefinition[] SyncAdvantages()
        {
            var results = new System.Collections.Generic.List<CombatAdvantageDefinition>();
            foreach (UnitAdvantage legacyValue in Enum.GetValues(typeof(UnitAdvantage)))
            {
                if (legacyValue == UnitAdvantage.None)
                {
                    continue;
                }

                var path = $"{AdvantagesFolder}/{legacyValue}.asset";
                var asset = GetOrCreateAsset<CombatAdvantageDefinition>(path);
                ApplyAdvantageDefaults(asset, legacyValue);
                EditorUtility.SetDirty(asset);
                results.Add(asset);
            }

            return results.ToArray();
        }

        private static CombatWeaponAdvantageDefinition[] SyncWeaponAdvantages()
        {
            var results = new System.Collections.Generic.List<CombatWeaponAdvantageDefinition>();
            foreach (WeaponAdvantageKind kind in Enum.GetValues(typeof(WeaponAdvantageKind)))
            {
                if (kind == WeaponAdvantageKind.Other)
                {
                    continue;
                }

                var path = $"{WeaponAdvantagesFolder}/{kind}.asset";
                var asset = GetOrCreateAsset<CombatWeaponAdvantageDefinition>(path);
                ApplyWeaponAdvantageDefaults(asset, kind);
                EditorUtility.SetDirty(asset);
                results.Add(asset);
            }

            return results.ToArray();
        }

        private static CombatDefenseModifierDefinition[] SyncDefenseModifiers()
        {
            var concealment = GetOrCreateAsset<CombatDefenseModifierDefinition>($"{ModifiersFolder}/Concealment.asset");
            SetDefenseModifier(
                concealment,
                "Concealment",
                "Concealment",
                "+2 DEF vs ranged and arcane attacks for models completely inside concealing terrain.",
                CombatDefenseModifierCategory.Concealment,
                CombatDefenseModifierApplication.UnitCompletelyInsideTerrainZone,
                2);

            var cover = GetOrCreateAsset<CombatDefenseModifierDefinition>($"{ModifiersFolder}/Cover.asset");
            SetDefenseModifier(
                cover,
                "Cover",
                "Cover",
                "+4 DEF vs ranged and arcane attacks for models within 1\" of a cover-granting feature (walls, boulders, buildings).",
                CombatDefenseModifierCategory.Cover,
                CombatDefenseModifierApplication.UnitWithinOneInchOfFeature,
                4);

            EditorUtility.SetDirty(concealment);
            EditorUtility.SetDirty(cover);
            return new[] { concealment, cover };
        }

        private static CombatTerrainFeatureDefinition[] SyncTerrainFeatures(
            CombatDefenseModifierDefinition[] defenseModifiers)
        {
            var concealment = defenseModifiers[0];
            var cover = defenseModifiers[1];

            var roughTerrain = GetOrCreateAsset<CombatTerrainFeatureDefinition>($"{TerrainFolder}/RoughTerrain.asset");
            SetTerrainFeature(
                roughTerrain,
                "RoughTerrain",
                "Rough Terrain",
                "Open terrain feature that slows movement.",
                isRoughTerrain: true,
                movementSpeedMultiplier: 0.5f,
                defenseModifier: null,
                lineOfSightMode: CombatTerrainLineOfSightMode.None,
                passThroughDepthInches: 0f,
                hugeTargetsIgnoreLimit: false);

            var forest = GetOrCreateAsset<CombatTerrainFeatureDefinition>($"{TerrainFolder}/Forest.asset");
            SetTerrainFeature(
                forest,
                "Forest",
                "Forest",
                "Rough terrain. Models completely inside gain concealment. LOS passes through up to 3\" of forest and cannot see completely through thicker forest. Forests do not limit LOS to huge-based targets.",
                isRoughTerrain: true,
                movementSpeedMultiplier: 0.5f,
                defenseModifier: concealment,
                lineOfSightMode: CombatTerrainLineOfSightMode.LimitedDepth,
                passThroughDepthInches: 3f,
                hugeTargetsIgnoreLimit: true);

            var wall = GetOrCreateAsset<CombatTerrainFeatureDefinition>($"{TerrainFolder}/Wall.asset");
            SetTerrainFeature(
                wall,
                "Wall",
                "Wall",
                "Models within 1\" gain cover (+4 DEF vs ranged and arcane attacks).",
                isRoughTerrain: false,
                movementSpeedMultiplier: 1f,
                defenseModifier: cover,
                lineOfSightMode: CombatTerrainLineOfSightMode.None,
                passThroughDepthInches: 0f,
                hugeTargetsIgnoreLimit: false);

            var cloud = GetOrCreateAsset<CombatTerrainFeatureDefinition>($"{TerrainFolder}/Cloud.asset");
            SetTerrainFeature(
                cloud,
                "Cloud",
                "Cloud",
                "Models completely inside gain concealment. Clouds block line of sight when they intervene between models, but models completely inside the same cloud can see each other. Fog reveal may pass into clouds up to 3\". Clouds do not affect movement.",
                isRoughTerrain: false,
                movementSpeedMultiplier: 1f,
                defenseModifier: concealment,
                lineOfSightMode: CombatTerrainLineOfSightMode.BlocksCompletely,
                passThroughDepthInches: 3f,
                hugeTargetsIgnoreLimit: false);

            EditorUtility.SetDirty(roughTerrain);
            EditorUtility.SetDirty(forest);
            EditorUtility.SetDirty(wall);
            EditorUtility.SetDirty(cloud);
            return new[] { roughTerrain, forest, wall, cloud };
        }

        private static CombatAbilityDefinition[] SyncAbilities()
        {
            var headhunter = SyncAbility(
                $"{AbilitiesFolder}/Headhunter.asset",
                "Headhunter",
                "Headhunter",
                "This model ignores forests when determining line of sight. While completely in a forest, this model gains +2 DEF against melee attack rolls.",
                ignoresForestForLineOfSight: true,
                meleeDefenseBonusWhileCompletelyInside: 2);

            var treewalker = SyncAbility(
                $"{AbilitiesFolder}/Treewalker.asset",
                "Treewalker",
                "Treewalker",
                "This model ignores forests when determining line of sight.",
                ignoresForestForLineOfSight: true,
                meleeDefenseBonusWhileCompletelyInside: 0);

            return new[] { headhunter, treewalker };
        }

        private static CombatAbilityDefinition SyncAbility(
            string path,
            string abilityId,
            string displayName,
            string description,
            bool ignoresForestForLineOfSight,
            int meleeDefenseBonusWhileCompletelyInside,
            int rangedDefenseBonusWhileCompletelyInside = 0)
        {
            var asset = GetOrCreateAsset<CombatAbilityDefinition>(path);
            var serializedAbility = new SerializedObject(asset);
            serializedAbility.FindProperty("abilityId").stringValue = abilityId;
            serializedAbility.FindProperty("displayName").stringValue = displayName;
            serializedAbility.FindProperty("description").stringValue = description;
            serializedAbility.FindProperty("ignoresForestForLineOfSight").boolValue = ignoresForestForLineOfSight;
            serializedAbility.FindProperty("requiredTerrainFeatureId").stringValue = "Forest";
            serializedAbility.FindProperty("meleeDefenseBonusWhileCompletelyInside").intValue = meleeDefenseBonusWhileCompletelyInside;
            serializedAbility.FindProperty("rangedDefenseBonusWhileCompletelyInside").intValue = rangedDefenseBonusWhileCompletelyInside;
            serializedAbility.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void SyncCatalog(
            CombatAdvantageDefinition[] advantages,
            CombatWeaponAdvantageDefinition[] weaponAdvantages,
            CombatDefenseModifierDefinition[] defenseModifiers,
            CombatTerrainFeatureDefinition[] terrainFeatures,
            CombatAbilityDefinition[] abilities)
        {
            var catalog = GetOrCreateAsset<CombatDefinitionCatalog>(CatalogPath);
            var serializedObject = new SerializedObject(catalog);
            serializedObject.FindProperty("advantages").arraySize = advantages.Length;
            for (var i = 0; i < advantages.Length; i++)
            {
                serializedObject.FindProperty("advantages").GetArrayElementAtIndex(i).objectReferenceValue = advantages[i];
            }

            serializedObject.FindProperty("weaponAdvantages").arraySize = weaponAdvantages.Length;
            for (var i = 0; i < weaponAdvantages.Length; i++)
            {
                serializedObject.FindProperty("weaponAdvantages").GetArrayElementAtIndex(i).objectReferenceValue = weaponAdvantages[i];
            }

            serializedObject.FindProperty("defenseModifiers").arraySize = defenseModifiers.Length;
            for (var i = 0; i < defenseModifiers.Length; i++)
            {
                serializedObject.FindProperty("defenseModifiers").GetArrayElementAtIndex(i).objectReferenceValue = defenseModifiers[i];
            }

            serializedObject.FindProperty("terrainFeatures").arraySize = terrainFeatures.Length;
            for (var i = 0; i < terrainFeatures.Length; i++)
            {
                serializedObject.FindProperty("terrainFeatures").GetArrayElementAtIndex(i).objectReferenceValue = terrainFeatures[i];
            }

            serializedObject.FindProperty("abilities").arraySize = abilities.Length;
            for (var i = 0; i < abilities.Length; i++)
            {
                serializedObject.FindProperty("abilities").GetArrayElementAtIndex(i).objectReferenceValue = abilities[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            catalog.RegisterAsActiveCatalog();
        }

        private static void ApplyAdvantageDefaults(CombatAdvantageDefinition asset, UnitAdvantage legacyValue)
        {
            var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("advantageId").stringValue = legacyValue.ToString();
            serializedObject.FindProperty("displayName").stringValue = SplitCamelCase(legacyValue.ToString());
            serializedObject.FindProperty("description").stringValue = $"Mk4-inspired {legacyValue} advantage.";
            serializedObject.FindProperty("ignoresConcealmentAndStealth").boolValue = legacyValue == UnitAdvantage.EyelessSight;
            serializedObject.FindProperty("treatsRoughTerrainAsOpenWhileAdvancing").boolValue = legacyValue == UnitAdvantage.Pathfinder;
            serializedObject.FindProperty("ignoresForestLineOfSightLimits").boolValue = legacyValue == UnitAdvantage.EyelessSight;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyWeaponAdvantageDefaults(CombatWeaponAdvantageDefinition asset, WeaponAdvantageKind kind)
        {
            var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("advantageId").stringValue = kind.ToString();
            serializedObject.FindProperty("displayName").stringValue = GetWeaponAdvantageDisplayName(kind);
            serializedObject.FindProperty("description").stringValue = GetWeaponAdvantageDescription(kind);
            serializedObject.FindProperty("kind").enumValueIndex = (int)kind;
            serializedObject.FindProperty("addsExtraDamageDie").boolValue = kind == WeaponAdvantageKind.WeaponMaster;
            serializedObject.FindProperty("armorBonus").intValue = kind switch
            {
                WeaponAdvantageKind.Shield => 2,
                WeaponAdvantageKind.Buckler => 1,
                _ => 0
            };
            serializedObject.FindProperty("ignoresShieldAndBucklerArmor").boolValue = kind == WeaponAdvantageKind.ChainWeapon;
            serializedObject.FindProperty("ignoresSpellDefAndArmBonuses").boolValue = kind == WeaponAdvantageKind.Blessed;
            serializedObject.FindProperty("ignoresTargetInMeleeDefBonus").boolValue = kind == WeaponAdvantageKind.Pistol;
            serializedObject.FindProperty("appliesDisruptionOnHit").boolValue = kind == WeaponAdvantageKind.Disruption;
            serializedObject.FindProperty("appliesDisruptionOnCriticalOnly").boolValue = kind == WeaponAdvantageKind.CriticalDisruption;
            serializedObject.FindProperty("appliesFireContinuousEffectOnHit").boolValue = kind == WeaponAdvantageKind.ContinuousEffectFire;
            serializedObject.FindProperty("appliesFireContinuousEffectOnCriticalOnly").boolValue = kind == WeaponAdvantageKind.CriticalFire;
            serializedObject.FindProperty("appliesCorrosionContinuousEffectOnHit").boolValue = kind == WeaponAdvantageKind.ContinuousEffectCorrosion;
            serializedObject.FindProperty("appliesCorrosionContinuousEffectOnCriticalOnly").boolValue = kind == WeaponAdvantageKind.CriticalCorrosion;
            serializedObject.FindProperty("enablesThrowPowerAttack").boolValue = kind == WeaponAdvantageKind.ThrowPowerAttack;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string GetWeaponAdvantageDisplayName(WeaponAdvantageKind kind)
        {
            return kind switch
            {
                WeaponAdvantageKind.ContinuousEffectFire => "Continuous Effect: Fire",
                WeaponAdvantageKind.ContinuousEffectCorrosion => "Continuous Effect: Corrosion",
                WeaponAdvantageKind.DamageTypeCold => "Damage Type: Cold",
                WeaponAdvantageKind.DamageTypeFire => "Damage Type: Fire",
                WeaponAdvantageKind.DamageTypeCorrosion => "Damage Type: Corrosion",
                WeaponAdvantageKind.DamageTypeElectricity => "Damage Type: Electricity",
                WeaponAdvantageKind.DamageTypeMagical => "Damage Type: Magical",
                _ => SplitCamelCase(kind.ToString())
            };
        }

        private static string GetWeaponAdvantageDescription(WeaponAdvantageKind kind)
        {
            return kind switch
            {
                WeaponAdvantageKind.WeaponMaster => "Add an extra die to damage rolls.",
                WeaponAdvantageKind.Shield => "Cumulative +2 ARM bonus while the system is not crippled.",
                WeaponAdvantageKind.Buckler => "Cumulative +1 ARM bonus while the system is not crippled.",
                WeaponAdvantageKind.ChainWeapon => "Ignores Buckler and Shield ARM bonuses and Shield Wall.",
                WeaponAdvantageKind.Blessed => "Attacks ignore bonuses from spells and animi that add to ARM or DEF.",
                WeaponAdvantageKind.Pistol => "Ignores Target in Melee DEF bonus.",
                WeaponAdvantageKind.Disruption => "A warjack hit loses focus and cannot gain or channel focus for one round.",
                WeaponAdvantageKind.CriticalDisruption => "On a critical hit, the target warjack suffers Disruption.",
                WeaponAdvantageKind.ContinuousEffectFire => "Hit model suffers the Fire continuous effect.",
                WeaponAdvantageKind.ContinuousEffectCorrosion => "Hit model suffers the Corrosion continuous effect.",
                WeaponAdvantageKind.CriticalFire => "On a critical hit, the target suffers the Fire continuous effect.",
                WeaponAdvantageKind.CriticalCorrosion => "On a critical hit, the target suffers the Corrosion continuous effect.",
                WeaponAdvantageKind.ThrowPowerAttack => "Can be used to make throw power attacks.",
                WeaponAdvantageKind.DamageTypeCold => "Specifies cold damage for resistance purposes.",
                WeaponAdvantageKind.DamageTypeFire => "Specifies fire damage for resistance purposes.",
                WeaponAdvantageKind.DamageTypeCorrosion => "Specifies corrosion damage for resistance purposes.",
                WeaponAdvantageKind.DamageTypeElectricity => "Specifies electricity damage for resistance purposes.",
                WeaponAdvantageKind.DamageTypeMagical => "Specifies magical damage for resistance purposes.",
                _ => $"Mk4-inspired {kind} weapon advantage."
            };
        }

        private static void SetDefenseModifier(
            CombatDefenseModifierDefinition asset,
            string id,
            string displayName,
            string description,
            CombatDefenseModifierCategory category,
            CombatDefenseModifierApplication application,
            int defenseBonus)
        {
            var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("modifierId").stringValue = id;
            serializedObject.FindProperty("displayName").stringValue = displayName;
            serializedObject.FindProperty("description").stringValue = description;
            serializedObject.FindProperty("category").enumValueIndex = (int)category;
            serializedObject.FindProperty("application").enumValueIndex = (int)application;
            serializedObject.FindProperty("defenseBonus").intValue = defenseBonus;
            serializedObject.FindProperty("appliesToRangedAndArcane").boolValue = true;
            serializedObject.FindProperty("appliesToMelee").boolValue = false;
            serializedObject.FindProperty("ignoredBySprayAttacks").boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetTerrainFeature(
            CombatTerrainFeatureDefinition asset,
            string id,
            string displayName,
            string description,
            bool isRoughTerrain,
            float movementSpeedMultiplier,
            CombatDefenseModifierDefinition defenseModifier,
            CombatTerrainLineOfSightMode lineOfSightMode,
            float passThroughDepthInches,
            bool hugeTargetsIgnoreLimit)
        {
            var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("featureId").stringValue = id;
            serializedObject.FindProperty("displayName").stringValue = displayName;
            serializedObject.FindProperty("description").stringValue = description;
            serializedObject.FindProperty("isRoughTerrain").boolValue = isRoughTerrain;
            serializedObject.FindProperty("movementSpeedMultiplier").floatValue = movementSpeedMultiplier;
            serializedObject.FindProperty("defenseModifierWhenInside").objectReferenceValue = defenseModifier;
            serializedObject.FindProperty("lineOfSightMode").enumValueIndex = (int)lineOfSightMode;
            serializedObject.FindProperty("lineOfSightPassThroughDepthInches").floatValue = passThroughDepthInches;
            serializedObject.FindProperty("doesNotLimitLineOfSightToHugeBasedTargets").boolValue = hugeTargetsIgnoreLimit;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TAsset GetOrCreateAsset<TAsset>(string path) where TAsset : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<TAsset>(path);
            if (existing != null)
            {
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<TAsset>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static string SplitCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var result = value[0].ToString();
            for (var i = 1; i < value.Length; i++)
            {
                if (char.IsUpper(value[i]))
                {
                    result += " ";
                }

                result += value[i];
            }

            return result;
        }
    }
}
#endif
