#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FOW
{
    [CustomEditor(typeof(RaycastRevealer), true)]
    [CanEditMultipleObjects]
    public class RaycastRevealerEditor : Editor
    {
        const string undoName = "Change Revealer Properties";

        SerializedProperty UseOcclusionProperty;
        SerializedProperty ObstacleMaskProperty;
        SerializedProperty OcclusionQualityProperty;
        SerializedProperty ViewRadiusProperty;
        SerializedProperty SoftenDistanceProperty;
        SerializedProperty UnobscuredSoftenDistanceProperty;
        SerializedProperty RaycastResolutionProperty;
        SerializedProperty DoubleHitMaxAngleDeltaProperty;
        SerializedProperty DebugModeProperty;

        SerializedProperty NumExtraIterationsProperty;
        SerializedProperty NumExtraRaysOnIterationProperty;
        SerializedProperty ResolveEdgeProperty;
        SerializedProperty MaxEdgeResolveIterationsProperty;
        SerializedProperty EdgeDstThresholdProperty;

        private void OnEnable()
        {
            UseOcclusionProperty = serializedObject.FindProperty("useOcclusion");
            ObstacleMaskProperty = serializedObject.FindProperty("ObstacleMask");
            OcclusionQualityProperty = serializedObject.FindProperty("OcclusionQuality");
            ViewRadiusProperty = serializedObject.FindProperty("viewRadius");
            SoftenDistanceProperty = serializedObject.FindProperty("softenDistance");
            UnobscuredSoftenDistanceProperty = serializedObject.FindProperty("unobscuredSoftenDistance");
            RaycastResolutionProperty = serializedObject.FindProperty("raycastResolution");
            DoubleHitMaxAngleDeltaProperty = serializedObject.FindProperty("doubleHitMaxAngleDelta");
            DebugModeProperty = serializedObject.FindProperty("DebugMode");

            NumExtraIterationsProperty = serializedObject.FindProperty("NumExtraIterations");
            NumExtraRaysOnIterationProperty = serializedObject.FindProperty("numExtraRaysOnIteration");
            ResolveEdgeProperty = serializedObject.FindProperty("ResolveEdge");
            MaxEdgeResolveIterationsProperty = serializedObject.FindProperty("MaxEdgeResolveIterations");
            EdgeDstThresholdProperty = serializedObject.FindProperty("edgeDstThreshold");
        }

        void DrawPropertiesUpTo(SerializedObject serializedObject, string stopPropertyName = null)
        {
            SerializedProperty property = serializedObject.GetIterator();

            // Must call NextVisible(true) to enter the serialized property
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.name == stopPropertyName)
                        break;

                    EditorGUILayout.PropertyField(property, true);
                }
                while (property.NextVisible(false));
            }
        }


        /// <summary>
        /// Draws serialized properties starting *after* startProperty (exclusive),
        /// and stopping *before* stopPropertyName (exclusive).
        /// If startProperty is null, starts from the beginning.
        /// If stopPropertyName is null, draws to the end.
        /// </summary>
        void DrawPropertiesBetween(SerializedObject serializedObject, SerializedProperty startProperty = null, string stopPropertyName = null)
        {
            //SerializedProperty currentProperty = startProperty ?? serializedObject.GetIterator();
            SerializedProperty currentProperty = startProperty != null
                ? startProperty.Copy()
                : serializedObject.GetIterator();
            bool skipFirst = startProperty != null;

            // First move to the first visible property
            if (!skipFirst && !currentProperty.NextVisible(true))
                return;
            //Debug.Log(currentProperty.name);
            do
            {
                if (skipFirst)
                {
                    skipFirst = false;
                    continue; // skip the actual startProperty
                }

                if (!string.IsNullOrEmpty(stopPropertyName) && currentProperty.name == stopPropertyName)
                    break;

                EditorGUILayout.PropertyField(
                    currentProperty,
                    new GUIContent(currentProperty.displayName, currentProperty.tooltip),
                    true
                );
                //EditorGUILayout.PropertyField(currentProperty, true);
            }
            while (currentProperty.NextVisible(false));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            #region draw script name
            EditorGUI.BeginDisabledGroup(true);
            SerializedProperty currentProperty = serializedObject.GetIterator();
            currentProperty.NextVisible(true);
            EditorGUILayout.PropertyField(
                        currentProperty,
                        new GUIContent(currentProperty.displayName, currentProperty.tooltip),
                        true
                    );
            EditorGUI.EndDisabledGroup();
            #endregion

            //DrawPropertiesUpTo(serializedObject, "OcclusionQuality");
            DrawPropertiesBetween(serializedObject, currentProperty, "ObstacleMask");
            ClampPropertyMin(ViewRadiusProperty, 0f);
            ClampPropertyMin(SoftenDistanceProperty, 0f);
            ClampPropertyMin(UnobscuredSoftenDistanceProperty, 0f);
            serializedObject.ApplyModifiedProperties();

            if (UseOcclusionProperty.hasMultipleDifferentValues || UseOcclusionProperty.boolValue)
            {
                EditorGUILayout.PropertyField(ObstacleMaskProperty);
                serializedObject.ApplyModifiedProperties();

                // Check if quality changed
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(OcclusionQualityProperty);
                serializedObject.ApplyModifiedProperties();
                if (EditorGUI.EndChangeCheck())
                {
                    SetRevealerQuality((RaycastRevealer.RaycastRevealerOcclusionQualityPreset)OcclusionQualityProperty.intValue);
                    serializedObject.ApplyModifiedProperties();
                    //serializedObject.Update();
                }

                // Show custom quality options if applicable
                bool allCustom = !OcclusionQualityProperty.hasMultipleDifferentValues &&
                    OcclusionQualityProperty.intValue == (int)RaycastRevealer.RaycastRevealerOcclusionQualityPreset.Custom;

                if (allCustom)
                {
                    DrawPropertiesBetween(serializedObject, OcclusionQualityProperty, "DebugMode");
                    ClampPropertyRange(RaycastResolutionProperty, 0.05f, 2f);
                    ClampPropertyMin(DoubleHitMaxAngleDeltaProperty, 1f);
                }
            }
            else if (UseOcclusionProperty.hasMultipleDifferentValues)
            {
                // Show mixed value state
                EditorGUILayout.PropertyField(UseOcclusionProperty);
            }


            bool anyHasDebugComponent = false;
            foreach (var t in targets)
            {
                if (((RaycastRevealer)t).TryGetComponent<RevealerDebug>(out _))
                {
                    anyHasDebugComponent = true;
                    break;
                }
            }

            if (anyHasDebugComponent)
            {
                EditorGUILayout.PropertyField(DebugModeProperty, true);
                DrawPropertiesBetween(serializedObject, DebugModeProperty);
            }
            else
            {
                // Disable debug mode for all targets without the component
                foreach (var t in targets)
                {
                    RaycastRevealer revealer = (RaycastRevealer)t;
                    if (!revealer.TryGetComponent<RaycastRevealer>(out _) && revealer.DebugMode)
                    {
                        revealer.DebugMode = false;
                        EditorUtility.SetDirty(revealer);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        void ClampPropertyMin(SerializedProperty property, float minValue)
        {
            if (property != null && !property.hasMultipleDifferentValues && property.floatValue < minValue)
            {
                property.floatValue = minValue;
            }
        }

        void ClampPropertyRange(SerializedProperty property, float minValue, float maxValue)
        {
            if (property != null && !property.hasMultipleDifferentValues)
            {
                if (property.floatValue < minValue) property.floatValue = minValue;
                else if (property.floatValue > maxValue) property.floatValue = maxValue;
            }
        }

        void SetRevealerQuality(RaycastRevealer.RaycastRevealerOcclusionQualityPreset quality)
        {
            switch (quality)
            {
                case RaycastRevealer.RaycastRevealerOcclusionQualityPreset.ExtraLargeScaleRTS:
                    RaycastResolutionProperty.floatValue = 1;
                    NumExtraIterationsProperty.intValue = 0;
                    NumExtraRaysOnIterationProperty.intValue = 1;
                    ResolveEdgeProperty.boolValue = false;
                    MaxEdgeResolveIterationsProperty.intValue = 1;
                    EdgeDstThresholdProperty.floatValue = .5f;
                    break;

                case RaycastRevealer.RaycastRevealerOcclusionQualityPreset.LargeScaleRTS:
                    RaycastResolutionProperty.floatValue = 2;
                    NumExtraIterationsProperty.intValue = 0;
                    NumExtraRaysOnIterationProperty.intValue = 1;
                    ResolveEdgeProperty.boolValue = false;
                    MaxEdgeResolveIterationsProperty.intValue = 1;
                    EdgeDstThresholdProperty.floatValue = .5f;
                    break;

                case RaycastRevealer.RaycastRevealerOcclusionQualityPreset.MediumScaleRTS:
                    RaycastResolutionProperty.floatValue = .5f;
                    NumExtraIterationsProperty.intValue = 1;
                    NumExtraRaysOnIterationProperty.intValue = 3;
                    ResolveEdgeProperty.boolValue = false;
                    MaxEdgeResolveIterationsProperty.intValue = 1;
                    EdgeDstThresholdProperty.floatValue = .3f;
                    break;

                case RaycastRevealer.RaycastRevealerOcclusionQualityPreset.SmallScaleRTS:
                    RaycastResolutionProperty.floatValue = .5f;
                    NumExtraIterationsProperty.intValue = 1;
                    NumExtraRaysOnIterationProperty.intValue = 3;
                    ResolveEdgeProperty.boolValue = true;
                    MaxEdgeResolveIterationsProperty.intValue = 1;
                    EdgeDstThresholdProperty.floatValue = .2f;
                    break;

                case RaycastRevealer.RaycastRevealerOcclusionQualityPreset.HighResolution:
                    RaycastResolutionProperty.floatValue = .5f;
                    NumExtraIterationsProperty.intValue = 3;
                    NumExtraRaysOnIterationProperty.intValue = 3;
                    ResolveEdgeProperty.boolValue = true;
                    MaxEdgeResolveIterationsProperty.intValue = 3;
                    EdgeDstThresholdProperty.floatValue = .15f;
                    break;

                case RaycastRevealer.RaycastRevealerOcclusionQualityPreset.OverkillResolution:
                    RaycastResolutionProperty.floatValue = 1f;
                    NumExtraIterationsProperty.intValue = 4;
                    NumExtraRaysOnIterationProperty.intValue = 3;
                    ResolveEdgeProperty.boolValue = true;
                    MaxEdgeResolveIterationsProperty.intValue = 7;
                    EdgeDstThresholdProperty.floatValue = .1f;
                    break;
            }
        }
    }
}
#endif