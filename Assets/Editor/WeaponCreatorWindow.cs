#if UNITY_EDITOR
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    public class WeaponCreatorWindow : EditorWindow
    {
        private string weaponName = "New Weapon";
        private WeaponAttackType attackType = WeaponAttackType.Melee;
        private int power = 5;
        private float range = 1.5f;
        private int matModifier;
        private int ratModifier;
        private WeaponProfile editTarget;
        private Vector2 scrollPosition;

        [MenuItem("Iron Kingdoms/Tools/Weapon Creator")]
        private static void Open()
        {
            GetWindow<WeaponCreatorWindow>("Weapon Creator");
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Create Weapon Definition", EditorStyles.boldLabel);
            weaponName = EditorGUILayout.TextField("Display Name", weaponName);
            attackType = (WeaponAttackType)EditorGUILayout.EnumPopup("Type", attackType);
            power = EditorGUILayout.IntField("Power", power);
            range = EditorGUILayout.FloatField("Range", range);
            matModifier = EditorGUILayout.IntField("MAT Modifier", matModifier);
            ratModifier = EditorGUILayout.IntField("RAT Modifier", ratModifier);

            GUILayout.Space(12f);
            if (GUILayout.Button("Create Weapon Asset"))
            {
                CreateAsset();
            }

            GUILayout.Space(16f);
            GUILayout.Label("Edit Existing Weapon Definition", EditorStyles.boldLabel);
            editTarget = (WeaponProfile)EditorGUILayout.ObjectField("Weapon Asset", editTarget, typeof(WeaponProfile), false);
            if (editTarget != null)
            {
                DrawEditInspector(editTarget);
            }

            EditorGUILayout.EndScrollView();
        }

        private void CreateAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Weapon",
                SanitizeFileName(weaponName),
                "asset",
                "Choose where to save the weapon definition asset.",
                "Assets/Data/Weapons");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var asset = CreateInstance<WeaponProfile>();
            asset.name = weaponName;

            var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("displayName").stringValue = weaponName;
            serializedObject.FindProperty("attackType").enumValueIndex = (int)attackType;
            serializedObject.FindProperty("power").intValue = power;
            serializedObject.FindProperty("range").floatValue = range;
            serializedObject.FindProperty("matModifier").intValue = matModifier;
            serializedObject.FindProperty("ratModifier").intValue = ratModifier;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static void DrawEditInspector(WeaponProfile targetWeapon)
        {
            var serializedObject = new SerializedObject(targetWeapon);
            serializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("attackType"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("power"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("range"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("matModifier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ratModifier"));
            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Apply Weapon Changes"))
            {
                EditorUtility.SetDirty(targetWeapon);
                AssetDatabase.SaveAssets();
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalidCharacter in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "Weapon" : value;
        }
    }
}
#endif
