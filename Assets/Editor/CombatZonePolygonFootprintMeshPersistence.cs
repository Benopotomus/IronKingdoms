#if UNITY_EDITOR
using System;
using System.IO;
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    internal static class CombatZonePolygonFootprintMeshPersistenceMenu
    {
        [MenuItem("Iron Kingdoms/Tools/Bake Polygon Zone Visual Meshes (Selected Prefabs)")]
        private static void BakeSelectedPrefabsMenu()
        {
            CombatZonePolygonFootprintMeshPersistence.BakeSelectedPrefabs();
        }

        [MenuItem("Iron Kingdoms/Tools/Bake Polygon Circle Forest Zone Prefab")]
        private static void BakeCircleForestPrefabMenu()
        {
            CombatZonePolygonFootprintMeshPersistence.BakePrefabAtPath(
                "Assets/Prefabs/Combat/PolygonCircleForestZone_3in.prefab");
            Debug.Log("Baked PolygonCircleForestZone_3in prefab visual mesh.");
        }
    }

    [InitializeOnLoad]
    internal static class CombatZonePolygonFootprintEditorBootstrap
    {
        static CombatZonePolygonFootprintEditorBootstrap()
        {
            CombatZonePolygonFootprint.EditorPersistVisualMeshHandler = CombatZonePolygonFootprintMeshPersistence.PersistVisualMesh;
        }
    }

    internal static class CombatZonePolygonFootprintMeshPersistence
    {
        private const string StandaloneMeshFolder = "Assets/Prefabs/Combat/ZoneVisualMeshes";
        private static string bakePrefabPathOverride;

        public static void PersistVisualMesh(GameObject root, Transform visual, Mesh sourceMesh)
        {
            if (sourceMesh == null || visual == null || root == null)
            {
                return;
            }

            var meshFilter = visual.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                return;
            }

            var meshName = $"{root.name}_VisualMesh";
            var prefabPath = bakePrefabPathOverride
                ?? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            var persistedMesh = string.IsNullOrEmpty(prefabPath)
                ? FindOrCreateStandaloneMeshAsset(root, meshName, sourceMesh)
                : FindOrCreateEmbeddedMesh(prefabPath, meshName, sourceMesh);

            if (persistedMesh == null)
            {
                return;
            }

            meshFilter.sharedMesh = persistedMesh;
            EditorUtility.SetDirty(meshFilter);
            EditorUtility.SetDirty(root);

            if (!string.IsNullOrEmpty(prefabPath))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(meshFilter);
            }

            AssetDatabase.SaveAssets();
        }

        public static void BakePrefabAtPath(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
            {
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            bakePrefabPathOverride = prefabPath;
            try
            {
                var footprint = root.GetComponent<CombatZonePolygonFootprint>();
                if (footprint == null || !footprint.HasFootprint)
                {
                    return;
                }

                footprint.RegenerateGeometry();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                bakePrefabPathOverride = null;
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void BakeSelectedPrefabs()
        {
            var baked = 0;
            foreach (var selected in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BakePrefabAtPath(path);
                baked++;
            }

            if (baked > 0)
            {
                Debug.Log($"Baked visual meshes for {baked} prefab(s).");
            }
            else
            {
                Debug.LogWarning("Select one or more polygon zone prefabs to bake visual meshes.");
            }
        }

        private static Mesh FindOrCreateEmbeddedMesh(string prefabPath, string meshName, Mesh sourceMesh)
        {
            var existingMesh = FindEmbeddedMesh(prefabPath, meshName);
            if (existingMesh != null)
            {
                CopyMeshGeometry(sourceMesh, existingMesh);
                EditorUtility.SetDirty(existingMesh);
                return existingMesh;
            }

            var meshCopy = UnityEngine.Object.Instantiate(sourceMesh);
            meshCopy.name = meshName;
            AssetDatabase.AddObjectToAsset(meshCopy, prefabPath);
            EditorUtility.SetDirty(AssetDatabase.LoadMainAssetAtPath(prefabPath));
            return meshCopy;
        }

        private static Mesh FindEmbeddedMesh(string prefabPath, string meshName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(prefabPath);
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Mesh mesh && mesh.name == meshName)
                {
                    return mesh;
                }
            }

            return null;
        }

        private static Mesh FindOrCreateStandaloneMeshAsset(GameObject root, string meshName, Mesh sourceMesh)
        {
            EnsureFolderExists(StandaloneMeshFolder);
            var assetPath = $"{StandaloneMeshFolder}/{SanitizeAssetName(root.name)}_VisualMesh.asset";
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existingMesh != null)
            {
                CopyMeshGeometry(sourceMesh, existingMesh);
                EditorUtility.SetDirty(existingMesh);
                return existingMesh;
            }

            var meshCopy = UnityEngine.Object.Instantiate(sourceMesh);
            meshCopy.name = meshName;
            AssetDatabase.CreateAsset(meshCopy, assetPath);
            return meshCopy;
        }

        private static void CopyMeshGeometry(Mesh source, Mesh destination)
        {
            destination.Clear(false);
            destination.SetVertices(source.vertices);
            destination.SetTriangles(source.triangles, 0);
            destination.RecalculateNormals();
            destination.RecalculateBounds();
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            var leaf = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string SanitizeAssetName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }
    }
}
#endif
