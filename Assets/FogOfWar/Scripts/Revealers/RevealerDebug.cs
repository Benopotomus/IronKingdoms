using UnityEngine;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FOW
{
    public class RevealerDebug : MonoBehaviour
    {
#if UNITY_EDITOR
        public bool DrawDebugStats = true;
        public bool DrawSegments = false;
        public bool DrawOutline = false;
        public bool DrawForward = false;
        [SerializeField] protected float DrawRayNoise = 0;

        private FogOfWarRevealer _revealer;

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;
            if (_revealer == null)
            {
                if (!TryGetComponent<FogOfWarRevealer>(out _revealer))
                    return;
            }
            if (!DrawDebugStats)
                return;
            if (FogOfWarWorld.instance == null)
                return;

            for (int i = 0; i < _revealer.NumberOfPoints; i++)
            {
                if (DrawDebugStats)
                {
                    float3 rayVec = GetSegmentEnd(i) - _revealer.GetEyePosition() + (float3)UnityEngine.Random.insideUnitSphere * DrawRayNoise;
                    if (DrawSegments)
                    {
                        Debug.DrawRay(_revealer.GetEyePosition(), rayVec, Color.blue, Time.deltaTime);
                        DrawString(i.ToString(), _revealer.GetEyePosition() + rayVec);
                    }

                    if (i != 0 && DrawOutline)
                        Debug.DrawLine(GetSegmentEnd(i), GetSegmentEnd(i - 1), Color.yellow);
                    //Debug.DrawLine(ViewPoints[i].point, ViewPoints[i - 1].point, Color.yellow);
                }
            }

            if (DrawForward)
                Debug.DrawLine(_revealer.GetEyePosition(), _revealer.GetEyePosition() + _revealer.ForwardVectorCached * 3, Color.magenta);
        }

        static readonly GUIStyle _labelStyle = new GUIStyle();
        static void DrawString(string text, Vector3 worldPos)
        {
            _labelStyle.normal.textColor = Color.red;
            UnityEditor.Handles.Label(worldPos, text, _labelStyle);
        }

        float3 GetSegmentEnd(int index)
        {
            //return _revealer.GetEyePosition() + (_revealer.DirFromAngle(_revealer.Directions[index]) * (_revealer.AreHits[index] ? _revealer.Radii[index] : _revealer.GetRayDistance()));
            var proj = FogOfWarRevealer3D.Projection;
            float2 dir2D = _revealer.OutputDirections[index];
            float3 dir3D = default;
            dir3D[proj.Axis0] = dir2D.x;
            dir3D[proj.Axis1] = dir2D.y;
            float dist = math.min(_revealer.OutputDistances[index], _revealer.TotalRevealerRadius);
            return _revealer.GetEyePosition() + dir3D * dist;
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(RevealerDebug))]
    public class RevealerDebugEditor : Editor
    {
        RaycastRevealer Revealer;
        public override void OnInspectorGUI()
        {
            RevealerDebug stat = (RevealerDebug)target;
            DrawDefaultInspector();
            

            //FogOfWarRevealer rev = stat.GetRevealerComponent();

            if (Revealer == null)
            {
                if (!stat.TryGetComponent<RaycastRevealer>(out Revealer))
                {
                    EditorGUILayout.LabelField($"Revealer component not found.");
                    return;
                }
            }


            EditorGUILayout.LabelField(" ");

            EditorGUILayout.LabelField($"Revealer array position id (can change): {Revealer.RevealerArrayPosition}");
            EditorGUILayout.LabelField($"GPU data position id (cant change): {Revealer.RevealerGPUDataPosition}");

            if (!stat.DrawDebugStats)
                return;

            EditorGUILayout.LabelField(" ");
            if (stat.DrawSegments)
            {
                EditorGUILayout.LabelField($"NUM SEGMENTS: {Revealer.NumberOfPoints}");
                for (int i = 0; i < Revealer.NumberOfPoints; i++)
                {
                    EditorGUILayout.LabelField($"------------- Segment {i} -------------");
                    EditorGUILayout.LabelField($"Angle: {Revealer.ViewPoints[i].Angle}");
                    EditorGUILayout.LabelField($"Direction: {Revealer.OutputDirections[i]}");
                    EditorGUILayout.LabelField($"Radius: {Revealer.OutputDistances[i]}");
                    EditorGUILayout.LabelField($"Did Hit?: {Revealer.OutputDistances[i] <= Revealer.TotalRevealerRadius}");
                }
            }

            EditorGUILayout.LabelField($"HASH BUCKETS:");
            for (int i = 0; i < Revealer.SpatialHashBuckets.Count; i++)
            {
                EditorGUILayout.LabelField(Revealer.SpatialHashBuckets[i].ToString());
            }
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Debug Toggle Static"))
                {
                    Revealer.SetRevealerAsStatic(!Revealer.CurrentlyStaticRevealer);
                }
            }
        }
    }
#endif
}