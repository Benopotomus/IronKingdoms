using System.Collections.Generic;
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    [CustomEditor(typeof(CombatZonePolygonFootprint))]
    public class CombatZonePolygonFootprintEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var footprint = (CombatZonePolygonFootprint)target;

            EditorGUILayout.LabelField("Polygon Zone Tools", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate Mesh", GUILayout.Height(24f)))
                {
                    RegenerateMesh(footprint);
                }

                if (GUILayout.Button("Open Zone Editor", GUILayout.Height(24f)))
                {
                    PolygonForestZoneEditorWindow.OpenAndLoadFrom(footprint);
                }
            }

            EditorGUILayout.HelpBox(
                "Scene view: drag green handles to move vertices. Shift+click removes a vertex. Ctrl+click adds one on an edge.",
                MessageType.None);

            EditorGUILayout.Space(6f);
            DrawDefaultInspector();
        }

        [MenuItem("CONTEXT/CombatZonePolygonFootprint/Regenerate Mesh")]
        private static void RegenerateMeshContextMenu(MenuCommand command)
        {
            if (command.context is CombatZonePolygonFootprint footprint)
            {
                RegenerateMesh(footprint);
            }
        }

        internal static void RegenerateMesh(CombatZonePolygonFootprint footprint)
        {
            if (footprint == null || !footprint.HasFootprint)
            {
                EditorUtility.DisplayDialog(
                    "Regenerate Mesh",
                    "This zone needs at least 3 footprint vertices before a mesh can be built.",
                    "OK");
                return;
            }

            Undo.RecordObject(footprint, "Regenerate Polygon Zone Mesh");
            footprint.RegenerateGeometry();
            EditorUtility.SetDirty(footprint);

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(footprint.gameObject);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                CombatZonePolygonFootprintMeshPersistence.BakePrefabAtPath(prefabPath);
            }

            SceneView.RepaintAll();
        }

        private void OnSceneGUI()
        {
            var footprint = (CombatZonePolygonFootprint)target;
            if (!footprint.HasFootprint)
            {
                return;
            }

            PolygonForestZoneDrawUtil.DrawFootprintHandles(footprint, allowEdit: true);
        }
    }

    internal static class PolygonForestZoneDrawUtil
    {
        private const float VertexPickRadiusScale = 0.12f;
        private const float EdgeInsertRadiusScale = 0.15f;

        private static readonly List<Vector3> WorldVerticesScratch = new();

        public static void DrawFootprintHandles(CombatZonePolygonFootprint footprint, bool allowEdit)
        {
            if (footprint == null || !footprint.HasFootprint)
            {
                return;
            }

            CollectWorldVertices(footprint, WorldVerticesScratch);
            var tabletopY = footprint.TabletopWorldY;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(0.2f, 0.85f, 0.35f, 0.95f);
            for (var i = 0; i < WorldVerticesScratch.Count; i++)
            {
                var next = (i + 1) % WorldVerticesScratch.Count;
                Handles.DrawLine(WorldVerticesScratch[i], WorldVerticesScratch[next]);
            }

            if (!allowEdit)
            {
                DrawVertexDots(WorldVerticesScratch, -1);
                return;
            }

            if (TryHandleVertexEditInput(footprint, WorldVerticesScratch, tabletopY))
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            for (var i = 0; i < WorldVerticesScratch.Count; i++)
            {
                var snapped = SnapWorldPoint(WorldVerticesScratch[i], tabletopY);
                var moved = Handles.FreeMoveHandle(
                    snapped,
                    HandleUtility.GetHandleSize(snapped) * 0.08f,
                    Vector3.zero,
                    Handles.DotHandleCap);
                moved.y = tabletopY;
                WorldVerticesScratch[i] = SnapWorldPoint(moved, tabletopY);
            }

            if (EditorGUI.EndChangeCheck())
            {
                ApplyWorldVertices(footprint, WorldVerticesScratch);
            }

            DrawVertexDots(WorldVerticesScratch, -1);
        }

        private static void DrawVertexDots(IReadOnlyList<Vector3> worldVertices, int selectedIndex)
        {
            for (var i = 0; i < worldVertices.Count; i++)
            {
                var point = worldVertices[i];
                if (i == selectedIndex)
                {
                    Handles.color = Color.yellow;
                    Handles.SphereHandleCap(
                        0,
                        point,
                        Quaternion.identity,
                        HandleUtility.GetHandleSize(point) * 0.1f,
                        EventType.Repaint);
                }
                else
                {
                    Handles.color = new Color(0.2f, 0.85f, 0.35f, 0.95f);
                    Handles.DotHandleCap(
                        0,
                        point,
                        Quaternion.identity,
                        HandleUtility.GetHandleSize(point) * 0.08f,
                        EventType.Repaint);
                }
            }
        }

        private static bool TryHandleVertexEditInput(
            CombatZonePolygonFootprint footprint,
            List<Vector3> worldVertices,
            float tabletopY)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown
                || current.button != 0
                || current.alt
                || !TryGetTabletopPointFromMouse(tabletopY, out var mousePoint))
            {
                return false;
            }

            if (current.shift)
            {
                if (worldVertices.Count <= 3)
                {
                    return false;
                }

                if (!TryFindNearestVertexIndex(worldVertices, mousePoint, tabletopY, out var removeIndex))
                {
                    return false;
                }

                worldVertices.RemoveAt(removeIndex);
                ApplyWorldVertices(footprint, worldVertices);
                current.Use();
                return true;
            }

            if (current.control)
            {
                if (!TryFindNearestEdgeInsertIndex(worldVertices, mousePoint, tabletopY, out var insertIndex, out var insertPoint))
                {
                    return false;
                }

                worldVertices.Insert(insertIndex, insertPoint);
                ApplyWorldVertices(footprint, worldVertices);
                current.Use();
                return true;
            }

            return false;
        }

        private static void ApplyWorldVertices(CombatZonePolygonFootprint footprint, List<Vector3> worldVertices)
        {
            Undo.RecordObject(footprint, "Edit Polygon Zone");
            footprint.SetLocalVerticesFromWorld(worldVertices);
            footprint.RegenerateGeometry();
            EditorUtility.SetDirty(footprint);
            SceneView.RepaintAll();
        }

        public static void DrawDraftPolygon(
            IReadOnlyList<Vector3> worldVertices,
            float tabletopY,
            int selectedIndex)
        {
            if (worldVertices == null || worldVertices.Count == 0)
            {
                return;
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(0.2f, 0.85f, 0.35f, 0.95f);
            for (var i = 0; i < worldVertices.Count; i++)
            {
                var a = worldVertices[i];
                a.y = tabletopY;
                if (i + 1 < worldVertices.Count)
                {
                    var b = worldVertices[i + 1];
                    b.y = tabletopY;
                    Handles.DrawLine(a, b);
                }
            }

            DrawVertexDots(worldVertices, selectedIndex);

            if (worldVertices.Count >= 3)
            {
                var first = worldVertices[0];
                var last = worldVertices[worldVertices.Count - 1];
                first.y = tabletopY;
                last.y = tabletopY;
                Handles.color = new Color(0.2f, 0.85f, 0.35f, 0.35f);
                Handles.DrawDottedLine(last, first, 4f);
            }
        }

        public static bool TryGetTabletopPointFromMouse(float tabletopY, out Vector3 worldPoint)
        {
            worldPoint = default;
            var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, tabletopY, 0f));
            if (!plane.Raycast(ray, out var enter))
            {
                return false;
            }

            worldPoint = SnapWorldPoint(ray.GetPoint(enter), tabletopY);
            return true;
        }

        public static Vector3 SnapWorldPoint(Vector3 worldPoint, float tabletopY)
        {
            var snap = PolygonForestZoneEditorWindow.GridSnapWorld;
            worldPoint.x = Mathf.Round(worldPoint.x / snap) * snap;
            worldPoint.z = Mathf.Round(worldPoint.z / snap) * snap;
            worldPoint.y = tabletopY;
            return worldPoint;
        }

        public static void CollectWorldVertices(CombatZonePolygonFootprint footprint, List<Vector3> worldVertices)
        {
            worldVertices.Clear();
            footprint.CollectWorldFootprintCorners(worldVertices);
        }

        public static void CollectWorldVerticesIntoDraft(
            CombatZonePolygonFootprint footprint,
            List<Vector3> draftVertices)
        {
            draftVertices.Clear();
            if (footprint == null || !footprint.HasFootprint)
            {
                return;
            }

            CollectWorldVertices(footprint, draftVertices);
        }

        private static bool TryFindNearestVertexIndex(
            IReadOnlyList<Vector3> worldVertices,
            Vector3 mousePoint,
            float tabletopY,
            out int nearestIndex)
        {
            nearestIndex = -1;
            if (worldVertices == null || worldVertices.Count == 0)
            {
                return false;
            }

            var pickRadius = HandleUtility.GetHandleSize(mousePoint) * VertexPickRadiusScale;
            var pickRadiusSq = pickRadius * pickRadius;
            var bestDistanceSq = float.MaxValue;
            for (var i = 0; i < worldVertices.Count; i++)
            {
                var vertex = worldVertices[i];
                vertex.y = tabletopY;
                var distanceSq = HorizontalDistanceSq(vertex, mousePoint);
                if (distanceSq > pickRadiusSq || distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                nearestIndex = i;
            }

            return nearestIndex >= 0;
        }

        private static bool TryFindNearestEdgeInsertIndex(
            IReadOnlyList<Vector3> worldVertices,
            Vector3 mousePoint,
            float tabletopY,
            out int insertIndex,
            out Vector3 insertPoint)
        {
            insertIndex = -1;
            insertPoint = default;
            if (worldVertices == null || worldVertices.Count < 2)
            {
                return false;
            }

            var pickRadius = HandleUtility.GetHandleSize(mousePoint) * EdgeInsertRadiusScale;
            var pickRadiusSq = pickRadius * pickRadius;
            var bestDistanceSq = float.MaxValue;
            for (var i = 0; i < worldVertices.Count; i++)
            {
                var next = (i + 1) % worldVertices.Count;
                var a = worldVertices[i];
                var b = worldVertices[next];
                a.y = tabletopY;
                b.y = tabletopY;
                if (!TryClosestPointOnSegmentXZ(a, b, mousePoint, out var closest, out var distanceSq)
                    || distanceSq > pickRadiusSq
                    || distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                insertIndex = next;
                insertPoint = SnapWorldPoint(closest, tabletopY);
            }

            return insertIndex >= 0;
        }

        private static bool TryClosestPointOnSegmentXZ(
            Vector3 segmentStart,
            Vector3 segmentEnd,
            Vector3 point,
            out Vector3 closestPoint,
            out float distanceSq)
        {
            closestPoint = default;
            distanceSq = float.MaxValue;
            var ab = segmentEnd - segmentStart;
            ab.y = 0f;
            var lengthSq = ab.sqrMagnitude;
            if (lengthSq <= 1e-8f)
            {
                return false;
            }

            var t = Vector3.Dot(point - segmentStart, ab) / lengthSq;
            t = Mathf.Clamp01(t);
            closestPoint = segmentStart + ab * t;
            closestPoint.y = segmentStart.y;
            distanceSq = HorizontalDistanceSq(closestPoint, point);
            return true;
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
