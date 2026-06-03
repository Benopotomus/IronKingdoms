#if UNITY_EDITOR
using System.Text;
using IronKingdoms.Combat;
using Pathfinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronKingdoms.Editor
{
    public static class CombatNavmeshBakeMenu
    {
        private const string CombatMapScenePath = "Assets/Scenes/CombatMapScene.unity";

        [MenuItem("Iron Kingdoms/Tools/Combat Navmesh/Apply High Quality Settings")]
        public static void ApplyHighQualitySettings()
        {
            ApplyQualityToActiveAstar(CombatRecastGraphSettings.NavmeshBakeQuality.High, scan: false);
        }

        [MenuItem("Iron Kingdoms/Tools/Combat Navmesh/Apply Standard Settings")]
        public static void ApplyStandardSettings()
        {
            ApplyQualityToActiveAstar(CombatRecastGraphSettings.NavmeshBakeQuality.Standard, scan: false);
        }

        [MenuItem("Iron Kingdoms/Tools/Combat Navmesh/Scan Combat Map (High Quality)")]
        public static void ScanCombatMapHighQuality()
        {
            ApplyQualityToCombatMapScene(CombatRecastGraphSettings.NavmeshBakeQuality.High, scan: true);
        }

        private static void ApplyQualityToCombatMapScene(CombatRecastGraphSettings.NavmeshBakeQuality quality, bool scan)
        {
            var scene = EditorSceneManager.GetSceneByPath(CombatMapScenePath);
            var openedHere = false;
            if (!scene.IsValid())
            {
                scene = EditorSceneManager.OpenScene(CombatMapScenePath, OpenSceneMode.Single);
                openedHere = true;
            }
            else if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(CombatMapScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                ApplyQualityToActiveAstar(quality, scan);
                if (scan)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (openedHere && EditorSceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void ApplyQualityToActiveAstar(CombatRecastGraphSettings.NavmeshBakeQuality quality, bool scan)
        {
            var astar = Object.FindFirstObjectByType<AstarPath>();
            if (astar == null)
            {
                Debug.LogError("Combat navmesh bake: no AstarPath found in open scene(s). Open CombatMapScene or load the map additively.");
                return;
            }

            var graphs = astar.data?.graphs;
            if (graphs == null || graphs.Length == 0)
            {
                Debug.LogError("Combat navmesh bake: AstarPath has no graphs.");
                return;
            }

            var log = new StringBuilder();
            log.AppendLine($"Applying {quality} Recast settings:");
            var applied = 0;
            for (var i = 0; i < graphs.Length; i++)
            {
                if (graphs[i] is not RecastGraph recastGraph)
                {
                    continue;
                }

                if (!CombatRecastGraphSettings.TryApplyByGraphName(recastGraph, quality))
                {
                    log.AppendLine($"  Skipped '{recastGraph.name}' (expected Base30mm, Base40mm, Base50mm, Base80mm, or Base120mm).");
                    continue;
                }

                applied++;
                log.AppendLine(
                    $"  {recastGraph.name}: voxel={recastGraph.cellSize:0.###}, radius={recastGraph.characterRadius:0.###}, height={recastGraph.walkableHeight:0.###}, maxEdge={recastGraph.maxEdgeLength:0.###}, contourErr={recastGraph.contourMaxError:0.###}");
            }

            if (applied == 0)
            {
                Debug.LogError("Combat navmesh bake: no Recast graphs were updated. Check graph names on the A* component.");
                return;
            }

            EditorUtility.SetDirty(astar);
            Debug.Log(log.ToString());

            if (!scan)
            {
                Debug.Log("Settings applied. Run 'Scan Combat Map (High Quality)' to rebuild and save baked navmesh data.");
                return;
            }

            EditorUtility.DisplayProgressBar("Combat Navmesh", "Scanning Recast graphs (high quality may take a minute)...", 0.35f);
            try
            {
                astar.Scan();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.SetDirty(astar);
            Debug.Log($"Combat navmesh scan complete ({applied} graph(s)). Save the scene to persist baked data.");
        }
    }
}
#endif
