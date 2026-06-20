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
        private const string GridSnapPrefKey = "IronKingdoms.PolygonForestZone.GridSnapInches";

        private readonly List<Vector3> draftVertices = new();
        private string zoneName = "PolygonForestZone";
        private float tabletopWorldY = 1.27f;
        private float gridSnapInches = 0.5f;
        private int selectedVertexIndex = -1;
        private CombatTerrainFeatureDefinition forestFeature;
        private Material forestMaterial;
        private CombatZonePolygonFootprint editingFootprint;
        private Vector2 scrollPosition;
        private bool isDrawing;

        public static float GridSnapWorld => CombatScale.InchesToWorldUnits(
            Mathf.Max(0.1f, ActiveInstance != null ? ActiveInstance.gridSnapInches : EditorPrefs.GetFloat(GridSnapPrefKey, 0.5f)));

        private static PolygonForestZoneEditorWindow ActiveInstance { get; set; }

        [MenuItem("Iron Kingdoms/Tools/Polygon Forest Zone Editor")]
        private static void Open()
        {
            GetWindow<PolygonForestZoneEditorWindow>("Polygon Forest Zone");
        }

        public static void OpenAndLoadFrom(CombatZonePolygonFootprint footprint)
        {
            var window = GetWindow<PolygonForestZoneEditorWindow>("Polygon Forest Zone");
            window.LoadFromFootprint(footprint);
        }

        private void OnEnable()
        {
            ActiveInstance = this;
            gridSnapInches = EditorPrefs.GetFloat(GridSnapPrefKey, gridSnapInches);
            forestFeature = AssetDatabase.LoadAssetAtPath<CombatTerrainFeatureDefinition>(ForestFeaturePath);
            forestMaterial = AssetDatabase.LoadAssetAtPath<Material>(ForestMaterialPath);
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorPrefs.SetFloat(GridSnapPrefKey, gridSnapInches);
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
                "Create a new zone by placing vertices, or load an existing zone to edit it.\n"
                + "Click to add vertices. Shift+click removes the nearest vertex. "
                + "Ctrl+click inserts a vertex on the nearest edge (when editing in Scene).",
                MessageType.Info);

            DrawSelectionSection();

            EditorGUILayout.Space(6f);
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
                    editingFootprint = null;
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
            if (editingFootprint != null && GUILayout.Button("Apply To Selected Zone"))
            {
                ApplyToSelectedFootprint();
            }

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

        private void DrawSelectionSection()
        {
            EditorGUILayout.LabelField("Existing Zone", EditorStyles.boldLabel);
            editingFootprint = (CombatZonePolygonFootprint)EditorGUILayout.ObjectField(
                "Editing Target",
                editingFootprint,
                typeof(CombatZonePolygonFootprint),
                true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load Selected Zone"))
                {
                    if (TryGetSelectedFootprint(out var footprint))
                    {
                        LoadFromFootprint(footprint);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Polygon Forest Zone",
                            "Select a GameObject with a CombatZonePolygonFootprint in the scene or project.",
                            "OK");
                    }
                }

                GUI.enabled = editingFootprint != null && draftVertices.Count >= 3;
                if (GUILayout.Button("Select In Scene"))
                {
                    Selection.activeGameObject = editingFootprint.gameObject;
                    EditorGUIUtility.PingObject(editingFootprint.gameObject);
                }

                GUI.enabled = true;
            }

            if (editingFootprint != null)
            {
                EditorGUILayout.HelpBox(
                    $"Editing '{editingFootprint.name}'. Adjust vertices, then click Apply To Selected Zone.",
                    MessageType.None);
            }
        }

        private void LoadFromFootprint(CombatZonePolygonFootprint footprint)
        {
            if (footprint == null || !footprint.HasFootprint)
            {
                EditorUtility.DisplayDialog(
                    "Polygon Forest Zone",
                    "That zone has no polygon footprint to load.",
                    "OK");
                return;
            }

            editingFootprint = footprint;
            zoneName = footprint.name;
            tabletopWorldY = footprint.TabletopWorldY;
            PolygonForestZoneDrawUtil.CollectWorldVerticesIntoDraft(footprint, draftVertices);
            selectedVertexIndex = -1;
            isDrawing = true;
            Selection.activeGameObject = footprint.gameObject;
            SceneView.RepaintAll();
            Repaint();
        }

        private void ApplyToSelectedFootprint()
        {
            if (editingFootprint == null)
            {
                if (!TryGetSelectedFootprint(out editingFootprint))
                {
                    EditorUtility.DisplayDialog(
                        "Polygon Forest Zone",
                        "Select a zone to apply changes to, or use Load Selected Zone first.",
                        "OK");
                    return;
                }
            }

            if (draftVertices.Count < 3)
            {
                EditorUtility.DisplayDialog("Polygon Forest Zone", "Need at least 3 vertices.", "OK");
                return;
            }

            Undo.RecordObject(editingFootprint, "Edit Polygon Forest Zone");
            editingFootprint.SetLocalVerticesFromWorld(draftVertices);
            editingFootprint.RegenerateGeometry();
            EditorUtility.SetDirty(editingFootprint);
            if (!EditorUtility.IsPersistent(editingFootprint))
            {
                EditorSceneManager.MarkSceneDirty(editingFootprint.gameObject.scene);
            }

            Debug.Log($"Updated polygon forest zone '{editingFootprint.name}'.", editingFootprint);
        }

        private static bool TryGetSelectedFootprint(out CombatZonePolygonFootprint footprint)
        {
            footprint = null;
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                return false;
            }

            footprint = selected.GetComponent<CombatZonePolygonFootprint>();
            return footprint != null && footprint.HasFootprint;
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
                    RemoveNearestDraftVertex();
                }
                else if (current.control && draftVertices.Count >= 2)
                {
                    InsertDraftVertexOnNearestEdge();
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

        private void RemoveNearestDraftVertex()
        {
            if (draftVertices.Count <= 3
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

        private void InsertDraftVertexOnNearestEdge()
        {
            if (!PolygonForestZoneDrawUtil.TryGetTabletopPointFromMouse(tabletopWorldY, out var mousePoint))
            {
                return;
            }

            var bestIndex = -1;
            var bestDistance = float.MaxValue;
            var bestPoint = mousePoint;
            for (var i = 0; i < draftVertices.Count; i++)
            {
                var next = (i + 1) % draftVertices.Count;
                var a = draftVertices[i];
                var b = draftVertices[next];
                a.y = tabletopWorldY;
                b.y = tabletopWorldY;
                var ab = b - a;
                var lengthSq = ab.sqrMagnitude;
                if (lengthSq <= 1e-8f)
                {
                    continue;
                }

                var t = Mathf.Clamp01(Vector3.Dot(mousePoint - a, ab) / lengthSq);
                var closest = a + ab * t;
                var distance = Vector2.SqrMagnitude(
                    new Vector2(closest.x - mousePoint.x, closest.z - mousePoint.z));
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestIndex = next;
                bestPoint = PolygonForestZoneDrawUtil.SnapWorldPoint(closest, tabletopWorldY);
            }

            if (bestIndex < 0)
            {
                return;
            }

            draftVertices.Insert(bestIndex, bestPoint);
            selectedVertexIndex = bestIndex;
            Repaint();
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
            footprintSerialized.FindProperty("colliderCenterLocalY").floatValue = 0f;
            footprintSerialized.ApplyModifiedPropertiesWithoutUndo();

            var centroid = ComputeDraftCentroid();
            root.transform.position = new Vector3(centroid.x, tabletopWorldY, centroid.z);
            footprint.SetLocalVerticesFromWorld(draftVertices);
            footprint.RegenerateGeometry();
            return root;
        }

        private Vector3 ComputeDraftCentroid()
        {
            if (draftVertices.Count == 0)
            {
                return Vector3.zero;
            }

            var sum = Vector3.zero;
            for (var i = 0; i < draftVertices.Count; i++)
            {
                sum += draftVertices[i];
            }

            return sum / draftVertices.Count;
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
            editingFootprint = root.GetComponent<CombatZonePolygonFootprint>();
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
