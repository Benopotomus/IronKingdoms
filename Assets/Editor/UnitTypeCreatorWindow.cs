#if UNITY_EDITOR
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    public class UnitTypeCreatorWindow : EditorWindow
    {
        private string unitName = "New Unit";
        private UnitRole role = UnitRole.Infantry;
        private float speed = 5f;
        private int meleeAttack = 5;
        private int rangedAttack = 4;
        private int defense = 12;
        private int armor = 14;
        private int health = 10;
        private int startingResource;
        private int weaponPower = 5;
        private float weaponRange = 1.5f;
        private string designNotes = string.Empty;

        [MenuItem("Iron Kingdoms/Tools/Unit Type Creator")]
        private static void Open()
        {
            GetWindow<UnitTypeCreatorWindow>("Unit Type Creator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Unit Definition", EditorStyles.boldLabel);
            unitName = EditorGUILayout.TextField("Display Name", unitName);
            role = (UnitRole)EditorGUILayout.EnumPopup("Role", role);
            speed = EditorGUILayout.FloatField("Speed", speed);
            meleeAttack = EditorGUILayout.IntField("Melee Attack", meleeAttack);
            rangedAttack = EditorGUILayout.IntField("Ranged Attack", rangedAttack);
            defense = EditorGUILayout.IntField("Defense", defense);
            armor = EditorGUILayout.IntField("Armor", armor);
            health = EditorGUILayout.IntField("Health", health);
            startingResource = EditorGUILayout.IntField("Starting Resource", startingResource);
            weaponPower = EditorGUILayout.IntField("Weapon Power", weaponPower);
            weaponRange = EditorGUILayout.FloatField("Weapon Range", weaponRange);
            designNotes = EditorGUILayout.TextArea(designNotes, GUILayout.MinHeight(60f));

            GUILayout.Space(12f);
            if (GUILayout.Button("Create Unit Type Asset"))
            {
                CreateAsset();
            }
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
            statsProperty.FindPropertyRelative("weaponPower").intValue = weaponPower;
            statsProperty.FindPropertyRelative("weaponRange").floatValue = weaponRange;
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
    }
}
#endif
