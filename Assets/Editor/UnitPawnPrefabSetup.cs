#if UNITY_EDITOR
using System.Collections.Generic;
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    public static class UnitPawnPrefabSetup
    {
        [MenuItem("Iron Kingdoms/Combat/Ensure Unit Pawn On Unit Prefabs")]
        public static void EnsureUnitPawnOnAllUnitPrefabs()
        {
            var definitions = LoadAllUnitDefinitions();
            var updatedCount = 0;

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                var prefab = definition.VisualPrefab;
                if (prefab == null)
                {
                    continue;
                }

                var prefabPath = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var unitPawn = root.GetComponent<UnitPawn>();
                    if (unitPawn == null)
                    {
                        unitPawn = root.AddComponent<UnitPawn>();
                    }

                    var serializedPawn = new SerializedObject(unitPawn);
                    serializedPawn.FindProperty("unitDefinition").objectReferenceValue = definition;
                    serializedPawn.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    updatedCount++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"UnitPawn setup complete. Updated {updatedCount} unit prefab(s).");
        }

        private static List<UnitTypeDefinition> LoadAllUnitDefinitions()
        {
            var results = new List<UnitTypeDefinition>();
            var guids = AssetDatabase.FindAssets($"t:{nameof(UnitTypeDefinition)}", new[] { "Assets/Data/Units" });
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var definition = AssetDatabase.LoadAssetAtPath<UnitTypeDefinition>(path);
                if (definition != null)
                {
                    results.Add(definition);
                }
            }

            return results;
        }
    }
}
#endif
