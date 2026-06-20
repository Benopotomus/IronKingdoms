#if UNITY_EDITOR
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    internal static class PolygonCircleZonePrefabCreator
    {
        private const string ForestFeaturePath = "Assets/Data/Combat/TerrainFeatures/Forest.asset";
        private const string CloudFeaturePath = "Assets/Data/Combat/TerrainFeatures/Cloud.asset";
        private const string ForestMaterialPath = "Assets/Prefabs/Combat/Mat_Forest.mat";
        private const string CloudMaterialPath = "Assets/Prefabs/Combat/Mat_Cloud.mat";
        private const string PrefabFolder = "Assets/Prefabs/Combat";

        private const int SegmentCount = 32;
        private const float TabletopLocalY = 0f;
        private const float ColliderHeight = 2.54f;

        [MenuItem("Iron Kingdoms/Create/Polygon Circle Forest Zone (3in, 32 seg)")]
        public static void CreateOrUpdateForest3In() =>
            CreateOrUpdate("PolygonCircleForestZone_3in", 3f, ForestFeaturePath, ForestMaterialPath);

        [MenuItem("Iron Kingdoms/Create/Polygon Circle Forest Zone (4in, 32 seg)")]
        public static void CreateOrUpdateForest4In() =>
            CreateOrUpdate("PolygonCircleForestZone_4in", 4f, ForestFeaturePath, ForestMaterialPath);

        [MenuItem("Iron Kingdoms/Create/Polygon Circle Forest Zone (5in, 32 seg)")]
        public static void CreateOrUpdateForest5In() =>
            CreateOrUpdate("PolygonCircleForestZone_5in", 5f, ForestFeaturePath, ForestMaterialPath);

        [MenuItem("Iron Kingdoms/Create/Polygon Circle Cloud Zone (3in, 32 seg)")]
        public static void CreateOrUpdateCloud3In() =>
            CreateOrUpdate("PolygonCircleCloudZone_3in", 3f, CloudFeaturePath, CloudMaterialPath);

        [MenuItem("Iron Kingdoms/Create/All Polygon Circle Zone Prefabs (3–5in Forest + 3in Cloud)")]
        public static void CreateOrUpdateAll()
        {
            CreateOrUpdateForest3In();
            CreateOrUpdateForest4In();
            CreateOrUpdateForest5In();
            CreateOrUpdateCloud3In();
            Debug.Log("Created/updated all polygon circle zone prefabs in Assets/Prefabs/Combat.");
        }

        private static void CreateOrUpdate(
            string prefabName,
            float diameterInches,
            string terrainFeaturePath,
            string materialPath)
        {
            var terrainFeature = AssetDatabase.LoadAssetAtPath<CombatTerrainFeatureDefinition>(terrainFeaturePath);
            var visualMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (terrainFeature == null || visualMaterial == null)
            {
                EditorUtility.DisplayDialog(
                    "Polygon Circle Zone",
                    $"Missing terrain feature or material for {prefabName}.",
                    "OK");
                return;
            }

            var prefabPath = $"{PrefabFolder}/{prefabName}.prefab";
            var root = new GameObject(prefabName);
            try
            {
                var zone = root.AddComponent<CombatZone>();
                var footprint = root.AddComponent<CombatZonePolygonFootprint>();

                var zoneSerialized = new SerializedObject(zone);
                zoneSerialized.FindProperty("terrainFeature").objectReferenceValue = terrainFeature;
                zoneSerialized.ApplyModifiedPropertiesWithoutUndo();

                var footprintSerialized = new SerializedObject(footprint);
                footprintSerialized.FindProperty("visualMaterial").objectReferenceValue = visualMaterial;
                footprintSerialized.FindProperty("colliderCenterLocalY").floatValue = TabletopLocalY;
                footprintSerialized.FindProperty("colliderHeight").floatValue = ColliderHeight;
                footprintSerialized.FindProperty("footprintPlaneMigrated").boolValue = true;
                footprintSerialized.ApplyModifiedPropertiesWithoutUndo();

                footprint.SetRegularPolygonFootprint(diameterInches, SegmentCount);
                footprint.RegenerateGeometry();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                CombatZonePolygonFootprintMeshPersistence.BakePrefabAtPath(prefabPath);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Debug.Log($"Saved {prefabPath} ({diameterInches}\" diameter, {SegmentCount} segments).", prefab);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
#endif
