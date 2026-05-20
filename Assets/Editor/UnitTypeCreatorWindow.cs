#if UNITY_EDITOR
using IronKingdoms.Combat;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    public class UnitTypeCreatorWindow : EditorWindow
    {
        private sealed class WeaponDraft
        {
            public string displayName = "Primary Weapon";
            public WeaponAttackType attackType = WeaponAttackType.Melee;
            public int power = 5;
            public float range = 1.5f;
        }

        private string unitName = "New Unit";
        private UnitRole role = UnitRole.Infantry;
        private float speed = 5f;
        private int meleeAttack = 5;
        private int rangedAttack = 4;
        private int defense = 12;
        private int armor = 14;
        private int health = 10;
        private int startingResource;
        private readonly List<WeaponDraft> weapons = new() { new WeaponDraft() };
        private string designNotes = string.Empty;
        private Vector2 scrollPosition;
        private UnitTypeDefinition editTarget;

        [MenuItem("Iron Kingdoms/Tools/Unit Type Creator")]
        private static void Open()
        {
            GetWindow<UnitTypeCreatorWindow>("Unit Type Creator");
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Create Unit Definition", EditorStyles.boldLabel);
            unitName = EditorGUILayout.TextField("Display Name", unitName);
            role = (UnitRole)EditorGUILayout.EnumPopup("Role", role);
            speed = EditorGUILayout.FloatField("Speed", speed);
            meleeAttack = EditorGUILayout.IntField("Melee Attack", meleeAttack);
            rangedAttack = EditorGUILayout.IntField("Ranged Attack", rangedAttack);
            defense = EditorGUILayout.IntField("Defense", defense);
            armor = EditorGUILayout.IntField("Armor", armor);
            health = EditorGUILayout.IntField("Health", health);
            startingResource = EditorGUILayout.IntField("Starting Resource", startingResource);
            DrawWeaponDraftList(weapons);
            designNotes = EditorGUILayout.TextArea(designNotes, GUILayout.MinHeight(60f));

            GUILayout.Space(12f);
            if (GUILayout.Button("Create Unit Type Asset"))
            {
                CreateAsset();
            }

            GUILayout.Space(16f);
            GUILayout.Label("Edit Existing Unit Definition", EditorStyles.boldLabel);
            editTarget = (UnitTypeDefinition)EditorGUILayout.ObjectField("Unit Asset", editTarget, typeof(UnitTypeDefinition), false);

            if (editTarget != null)
            {
                DrawEditInspector(editTarget);
            }

            EditorGUILayout.EndScrollView();
        }

        private void CreateAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Unit Type",
                SanitizeFileName(unitName),
                "asset",
                "Choose where to save the unit definition asset.",
                "Assets/Data/Units");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var asset = CreateInstance<UnitTypeDefinition>();
            asset.name = unitName;

            var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("displayName").stringValue = unitName;
            serializedObject.FindProperty("role").enumValueIndex = (int)role;

            var statsProperty = serializedObject.FindProperty("stats");
            statsProperty.FindPropertyRelative("speed").floatValue = speed;
            statsProperty.FindPropertyRelative("meleeAttack").intValue = meleeAttack;
            statsProperty.FindPropertyRelative("rangedAttack").intValue = rangedAttack;
            statsProperty.FindPropertyRelative("defense").intValue = defense;
            statsProperty.FindPropertyRelative("armor").intValue = armor;
            statsProperty.FindPropertyRelative("health").intValue = health;
            statsProperty.FindPropertyRelative("startingResource").intValue = startingResource;
            var weaponPowerProperty = statsProperty.FindPropertyRelative("weaponPower");
            var weaponRangeProperty = statsProperty.FindPropertyRelative("weaponRange");
            var weaponsProperty = statsProperty.FindPropertyRelative("weapons");
            weaponsProperty.arraySize = Mathf.Max(1, weapons.Count);
            for (var i = 0; i < weaponsProperty.arraySize; i++)
            {
                var source = i < weapons.Count ? weapons[i] : weapons[0];
                var weaponProperty = weaponsProperty.GetArrayElementAtIndex(i);
                weaponProperty.FindPropertyRelative("displayName").stringValue = source.displayName;
                weaponProperty.FindPropertyRelative("attackType").enumValueIndex = (int)source.attackType;
                weaponProperty.FindPropertyRelative("power").intValue = source.power;
                weaponProperty.FindPropertyRelative("range").floatValue = source.range;
            }

            var primaryWeapon = weapons[0];
            weaponPowerProperty.intValue = primaryWeapon.power;
            weaponRangeProperty.floatValue = primaryWeapon.range;
            serializedObject.FindProperty("designNotes").stringValue = designNotes;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalidCharacter in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "UnitType" : value;
        }

        private static void DrawWeaponDraftList(List<WeaponDraft> draftWeapons)
        {
            GUILayout.Space(8f);
            GUILayout.Label("Weapons", EditorStyles.boldLabel);

            for (var i = 0; i < draftWeapons.Count; i++)
            {
                var weapon = draftWeapons[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                weapon.displayName = EditorGUILayout.TextField("Name", weapon.displayName);
                if (GUILayout.Button("Remove", GUILayout.Width(72f)))
                {
                    draftWeapons.RemoveAt(i);
                    i--;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }

                EditorGUILayout.EndHorizontal();
                weapon.attackType = (WeaponAttackType)EditorGUILayout.EnumPopup("Type", weapon.attackType);
                weapon.power = EditorGUILayout.IntField("Power", weapon.power);
                weapon.range = EditorGUILayout.FloatField("Range", weapon.range);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Weapon"))
            {
                draftWeapons.Add(new WeaponDraft());
            }

            if (draftWeapons.Count == 0)
            {
                draftWeapons.Add(new WeaponDraft());
            }
        }

        private static void DrawEditInspector(UnitTypeDefinition targetUnit)
        {
            var serializedObject = new SerializedObject(targetUnit);
            serializedObject.UpdateIfRequiredOrScript();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("role"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stats"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("designNotes"));

            if (GUILayout.Button("Apply Unit Changes"))
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(targetUnit);
                AssetDatabase.SaveAssets();
            }
            else
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
#endif
