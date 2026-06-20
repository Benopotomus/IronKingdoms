#if UNITY_EDITOR
using System.Collections.Generic;
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor
{
    [CustomEditor(typeof(CombatZonePolygonFootprint))]
    public class CombatZonePolygonFootprintEditor : UnityEditor.Editor
    {
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
        private static readonly List<Vector3> WorldVerticesScratch = new();

        public static void DrawFootprintHandles(CombatZonePolygonFootprint footprint, bool allowEdit)
        {
            if (footprint == null || !footprint.HasFootprint)
            {
                return;
            }

            CollectWorldVertices(footprint, WorldVerticesScratch);
            var tabletopY = footprint.TabletopWorldY;

            Handles.color = new Color(0.2f, 0.85f, 0.35f, 0.95f);
            for (var i = 0; i < WorldVerticesScratch.Count; i++)
            {
                var next = (i + 1) % WorldVerticesScratch.Count;
                Handles.DrawLine(WorldVerticesScratch[i], WorldVerticesScratch[next]);
            }

            if (!allowEdit)
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
                Undo.RecordObject(footprint, "Move Polygon Forest Vertex");
                footprint.SetLocalVerticesFromWorld(WorldVerticesScratch);
                footprint.RegenerateGeometry();
                EditorUtility.SetDirty(footprint);
            }
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

                if (i == selectedIndex)
                {
                    Handles.color = Color.yellow;
                    Handles.SphereHandleCap(0, a, Quaternion.identity, HandleUtility.GetHandleSize(a) * 0.1f, EventType.Repaint);
                    Handles.color = new Color(0.2f, 0.85f, 0.35f, 0.95f);
                }
                else
                {
                    Handles.DotHandleCap(0, a, Quaternion.identity, HandleUtility.GetHandleSize(a) * 0.08f, EventType.Repaint);
                }
            }

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
    }
}
#endif
