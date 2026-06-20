#if UNITY_EDITOR
using System.Collections.Generic;
using IronKingdoms.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronKingdoms.Editor
{
    public class PolygonForestZoneEditorWindow : EditorWindow
    {
        private const string ForestFeaturePath = "Assets/Data/Combat/TerrainFeatures/Forest.asset";
        private const string ForestMaterialPath = "Assets/Prefabs/Combat/Mat_Forest.mat";
        private const string DefaultPrefabFolder = "Assets/Prefabs/Combat";

        private readonly List<Vector3> draftVertices = new();
        private string zoneName = "PolygonForestZone";
        private float tabletopWorldY = 1.27f;
        private float gridSnapInches = 0.5f;
        private int selectedVertexIndex = -1;
        private CombatTerrainFeatureDefinition forestFeature;
        private Material forestMaterial;
        private Vector2 scrollPosition;
        private bool isDrawing;

        public static float GridSnapWorld => CombatScale.InchesToWorldUnits(
            Mathf.Max(0.1f, ActiveInstance != null ? ActiveInstance.gridSnapInches : 0.5f));

        private static PolygonForestZoneEditorWindow ActiveInstance { get; set; }

        [MenuItem("Iron Kingdoms/Tools/Polygon Forest Zone Editor")]
        private static void Open()
        {
            GetWindow<PolygonForestZoneEditorWindow>("Polygon Forest Zone");
        }

        private void OnEnable()
        {
            ActiveInstance = this;
            forestFeature = AssetDatabase.LoadAssetAtPath<CombatTerrainFeatureDefinition>(ForestFeaturePath);
            forestMaterial = AssetDatabase.LoadAssetAtPath<Material>(ForestMaterialPath);
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            if (ActiveInstance == this)
            {
                ActiveInstance = null;
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("Polygon Forest Zone Editor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click in the Scene view to place vertices on the tabletop plane. "
                + "Use at least 3 points for a closed forest shape. "
                + "Shift+click removes the nearest vertex.",
                MessageType.Info);

            zoneName = EditorGUILayout.TextField("Zone Name", zoneName);
            tabletopWorldY = EditorGUILayout.FloatField("Tabletop Y (world)", tabletopWorldY);
            gridSnapInches = EditorGUILayout.FloatField("Grid Snap (inches)", gridSnapInches);
            forestFeature = (CombatTerrainFeatureDefinition)EditorGUILayout.ObjectField(
                "Forest Feature",
                forestFeature,
                typeof(CombatTerrainFeatureDefinition),
                false);
            forestMaterial = (Material)EditorGUILayout.ObjectField(
                "Forest Material",
                forestMaterial,
                typeof(Material),
                false);

            EditorGUILayout.LabelField("Vertices", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Count: {draftVertices.Count} (minimum 3)");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(isDrawing ? "Stop Drawing" : "Start Drawing"))
                {
                    isDrawing = !isDrawing;
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Clear"))
                {
                    draftVertices.Clear();
                    selectedVertexIndex = -1;
                    SceneView.RepaintAll();
                }

                GUI.enabled = draftVertices.Count > 0;
                if (GUILayout.Button("Undo Last"))
                {
                    draftVertices.RemoveAt(draftVertices.Count - 1);
                    selectedVertexIndex = -1;
                    SceneView.RepaintAll();
                }

                GUI.enabled = true;
            }

            EditorGUILayout.Space(8f);
            GUI.enabled = draftVertices.Count >= 3;
            if (GUILayout.Button("Create In Open Scene"))
            {
                CreateZoneInScene();
            }

            if (GUILayout.Button("Save As Prefab"))
            {
                SaveAsPrefab();
            }

            GUI.enabled = true;
            EditorGUILayout.EndScrollView();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isDrawing && draftVertices.Count == 0)
            {
                return;
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            PolygonForestZoneDrawUtil.DrawDraftPolygon(draftVertices, tabletopWorldY, selectedVertexIndex);

            if (!isDrawing)
            {
                return;
            }

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var current = Event.current;
            if (current.type == EventType.Layout)
            {
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
            {
                if (current.shift)
                {
                    RemoveNearestVertex();
                }
                else if (PolygonForestZoneDrawUtil.TryGetTabletopPointFromMouse(tabletopWorldY, out var point))
                {
                    draftVertices.Add(point);
                    selectedVertexIndex = draftVertices.Count - 1;
                    current.Use();
                    Repaint();
                }
            }

            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
            {
                sceneView.Repaint();
            }
        }

        private void RemoveNearestVertex()
        {
            if (draftVertices.Count == 0
                || !PolygonForestZoneDrawUtil.TryGetTabletopPointFromMouse(tabletopWorldY, out var mousePoint))
            {
                return;
            }

            var bestIndex = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < draftVertices.Count; i++)
            {
                var distance = Vector2.SqrMagnitude(
                    new Vector2(draftVertices[i].x - mousePoint.x, draftVertices[i].z - mousePoint.z));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                draftVertices.RemoveAt(bestIndex);
                selectedVertexIndex = -1;
                Repaint();
            }
        }

        private GameObject BuildZoneRoot()
        {
            if (draftVertices.Count < 3)
            {
                EditorUtility.DisplayDialog("Polygon Forest Zone", "Add at least 3 vertices first.", "OK");
                return null;
            }

            var root = new GameObject(string.IsNullOrWhiteSpace(zoneName) ? "PolygonForestZone" : zoneName);
            Undo.RegisterCreatedObjectUndo(root, "Create Polygon Forest Zone");

            var zone = root.AddComponent<CombatZone>();
            var footprint = root.AddComponent<CombatZonePolygonFootprint>();

            var zoneSerialized = new SerializedObject(zone);
            zoneSerialized.FindProperty("terrainFeature").objectReferenceValue = forestFeature;
            zoneSerialized.ApplyModifiedPropertiesWithoutUndo();

            var footprintSerialized = new SerializedObject(footprint);
            footprintSerialized.FindProperty("visualMaterial").objectReferenceValue = forestMaterial;
            footprintSerialized.FindProperty("colliderCenterLocalY").floatValue = tabletopWorldY;
            footprintSerialized.ApplyModifiedPropertiesWithoutUndo();

            footprint.SetLocalVerticesFromWorld(draftVertices);
            footprint.RegenerateGeometry();
            return root;
        }

        private void CreateZoneInScene()
        {
            var root = BuildZoneRoot();
            if (root == null)
            {
                return;
            }

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log($"Created polygon forest zone '{root.name}' in scene '{scene.name}'.", root);
        }

        private void SaveAsPrefab()
        {
            var root = BuildZoneRoot();
            if (root == null)
            {
                return;
            }

            var path = EditorUtility.SaveFilePanelInProject(
                "Save Polygon Forest Prefab",
                string.IsNullOrWhiteSpace(zoneName) ? "PolygonForestZone" : zoneName,
                "prefab",
                "Choose where to save the polygon forest prefab.",
                DefaultPrefabFolder);

            if (string.IsNullOrEmpty(path))
            {
                DestroyImmediate(root);
                return;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"Saved polygon forest prefab to {path}.", prefab);
        }
    }
}
#endif
