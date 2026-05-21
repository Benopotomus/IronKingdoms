#if UNITY_EDITOR
using IronKingdoms.Combat;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    public class UnitTypeCreatorWindow : EditorWindow
    {
        private string unitName = "New Unit";
        private UnitRole role = UnitRole.Infantry;
        private float speed = 5f;
        private ModelSize modelSize = ModelSize.Base30mm;
        private int meleeAttack = 5;
        private int rangedAttack = 4;
        private int defense = 12;
        private int armor = 14;
        private int health = 10;
        private int startingResource;
        private readonly List<WeaponProfile> weapons = new() { null };
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
            modelSize = (ModelSize)EditorGUILayout.EnumPopup("Model Size", modelSize);
            meleeAttack = EditorGUILayout.IntField("Melee Attack", meleeAttack);
            rangedAttack = EditorGUILayout.IntField("Ranged Attack", rangedAttack);
            defense = EditorGUILayout.IntField("Defense", defense);
            armor = EditorGUILayout.IntField("Armor", armor);
            health = EditorGUILayout.IntField("Health", health);
            startingResource = EditorGUILayout.IntField("Starting Resource", startingResource);
            DrawWeaponReferenceList(weapons);
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
            statsProperty.FindPropertyRelative("modelSize").enumValueIndex = (int)modelSize;
            statsProperty.FindPropertyRelative("meleeAttack").intValue = meleeAttack;
            statsProperty.FindPropertyRelative("rangedAttack").intValue = rangedAttack;
            statsProperty.FindPropertyRelative("defense").intValue = defense;
            statsProperty.FindPropertyRelative("armor").intValue = armor;
            statsProperty.FindPropertyRelative("health").intValue = health;
            statsProperty.FindPropertyRelative("startingResource").intValue = startingResource;
            var validWeapons = new List<WeaponProfile>();
            for (var i = 0; i < weapons.Count; i++)
            {
                if (weapons[i] != null)
                {
                    validWeapons.Add(weapons[i]);
                }
            }

            var weaponsProperty = statsProperty.FindPropertyRelative("weapons");
            weaponsProperty.arraySize = validWeapons.Count;
            for (var i = 0; i < weaponsProperty.arraySize; i++)
            {
                var weaponProperty = weaponsProperty.GetArrayElementAtIndex(i);
                weaponProperty.objectReferenceValue = validWeapons[i];
            }

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

        private static void DrawWeaponReferenceList(List<WeaponProfile> draftWeapons)
        {
            GUILayout.Space(8f);
            GUILayout.Label("Weapons", EditorStyles.boldLabel);

            for (var i = 0; i < draftWeapons.Count; i++)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                draftWeapons[i] = (WeaponProfile)EditorGUILayout.ObjectField("Weapon Asset", draftWeapons[i], typeof(WeaponProfile), false);
                if (GUILayout.Button("Remove", GUILayout.Width(72f)))
                {
                    draftWeapons.RemoveAt(i);
                    i--;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Weapon Slot"))
            {
                draftWeapons.Add(null);
            }

            if (draftWeapons.Count == 0)
            {
                draftWeapons.Add(null);
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
            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Apply Unit Changes"))
            {
                EditorUtility.SetDirty(targetUnit);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
#endif
