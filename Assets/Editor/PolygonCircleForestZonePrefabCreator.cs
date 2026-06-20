#if UNITY_EDITOR
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    internal static class PolygonCircleForestZonePrefabCreator
    {
        private const string ForestFeaturePath = "Assets/Data/Combat/TerrainFeatures/Forest.asset";
        private const string ForestMaterialPath = "Assets/Prefabs/Combat/Mat_Forest.mat";
        private const string PrefabPath = "Assets/Prefabs/Combat/PolygonCircleForestZone_3in.prefab";

        private const float DiameterInches = 3f;
        private const int SegmentCount = 32;
        private const float TabletopLocalY = 0f;
        private const float ColliderHeight = 2.54f;

        [MenuItem("Iron Kingdoms/Create/Polygon Circle Forest Zone (3in, 32 seg)")]
        public static void CreateOrUpdatePrefab()
        {
            var forestFeature = AssetDatabase.LoadAssetAtPath<CombatTerrainFeatureDefinition>(ForestFeaturePath);
            var forestMaterial = AssetDatabase.LoadAssetAtPath<Material>(ForestMaterialPath);
            if (forestFeature == null || forestMaterial == null)
            {
                EditorUtility.DisplayDialog(
                    "Polygon Circle Forest Zone",
                    "Missing Forest.asset or Mat_Forest.mat.",
                    "OK");
                return;
            }

            var root = new GameObject("PolygonCircleForestZone_3in");
            try
            {
                var zone = root.AddComponent<CombatZone>();
                var footprint = root.AddComponent<CombatZonePolygonFootprint>();

                var zoneSerialized = new SerializedObject(zone);
                zoneSerialized.FindProperty("terrainFeature").objectReferenceValue = forestFeature;
                zoneSerialized.ApplyModifiedPropertiesWithoutUndo();

                var footprintSerialized = new SerializedObject(footprint);
                footprintSerialized.FindProperty("visualMaterial").objectReferenceValue = forestMaterial;
                footprintSerialized.FindProperty("colliderCenterLocalY").floatValue = TabletopLocalY;
                footprintSerialized.FindProperty("colliderHeight").floatValue = ColliderHeight;
                footprintSerialized.ApplyModifiedPropertiesWithoutUndo();

                footprint.SetRegularPolygonFootprint(DiameterInches, SegmentCount);
                footprint.RegenerateGeometry();

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (existing != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"Updated {PrefabPath}.", existing);
                }
                else
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                    Debug.Log($"Created {PrefabPath}.", prefab);
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
#endif
