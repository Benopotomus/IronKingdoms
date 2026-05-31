#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace FOW
{
    [CustomEditor(typeof(FogOfWarWorld))]
    public class FogOfWarWorldEditor : Editor
    {
        // Cache all serialized properties
        SerializedProperty _updateMethod;
        SerializedProperty _fogType;
        SerializedProperty _fogFade;
        SerializedProperty _fogFadePower;
        SerializedProperty _blendType;
        SerializedProperty _allowBleeding;
        SerializedProperty _sightExtraAmount;
        SerializedProperty _edgeSoftenDistance;
        SerializedProperty _maxFogDistance;
        SerializedProperty _invertFowEffect;
        SerializedProperty _pixelateFog;
        SerializedProperty _worldSpacePixelate;
        SerializedProperty _pixelDensity;
        SerializedProperty _roundRevealerPosition;
        SerializedProperty _pixelGridOffset;
        SerializedProperty _useDithering;
        SerializedProperty _ditherSize;
        SerializedProperty _unknownColor;
        SerializedProperty _saturationStrength;
        SerializedProperty _blurStrength;
        SerializedProperty _blurDistanceScreenPercentMin;
        SerializedProperty _blurDistanceScreenPercentMax;
        SerializedProperty _blurSamples;
        SerializedProperty _fogTexture;
        SerializedProperty _useTriplanar;
        SerializedProperty _fogTextureTiling;
        SerializedProperty _fogScrollSpeed;
        SerializedProperty _outlineThickness;
        SerializedProperty _fowSamplingMode;
        SerializedProperty _hidersUseFogTexture;
        SerializedProperty _hidersTextureSeenThreshold;
        SerializedProperty _asyncReadbackFogDataToCPU;
        SerializedProperty _useConstantBlur;
        SerializedProperty _constantTextureBlurQuality;
        SerializedProperty _constantTextureBlurAmount;
        SerializedProperty _useWorldBounds;
        SerializedProperty _worldBoundsSoftenDistance;
        SerializedProperty _worldBoundsInfluence;
        SerializedProperty _worldBounds;
        SerializedProperty _useMiniMap;
        SerializedProperty _fowResX;
        SerializedProperty _fowResY;
        SerializedProperty _useRegrow;
        SerializedProperty _revealerFadeIn;
        SerializedProperty _revealerFadeInSpeed;
        SerializedProperty _revealerFadeOut;
        SerializedProperty _revealerFadeOutSpeed;
        SerializedProperty _initialFogExplorationValue;
        SerializedProperty _maxFogRegrowAmount;
        SerializedProperty _revealerUpdateMode;
        SerializedProperty _maxNumRevealersPerFrame;
        SerializedProperty _useStagedGPUUploads;
        SerializedProperty _scatterRevealersShader;
        SerializedProperty _useSpatialAcceleration;
        SerializedProperty _spatialHashTableSize;
        SerializedProperty _spatialHashGridSize;

        SerializedProperty _maxPossibleRevealers;
        SerializedProperty _maxPossibleSegmentsPerRevealer;
        SerializedProperty _maxPossibleHiders;
        SerializedProperty _is2D;
        SerializedProperty _gamePlaneOrientation;

        BoxBoundsHandle _boundsHandle = new BoxBoundsHandle();
        FogOfWarWorld _fow;

        public static GUIStyle TitleStyle;
        public static GUIStyle SubTitleStyle;
        public static GUIStyle WarningGUIStyleWithWrap;

        public static bool DontBegForReviews;
        const string DontBegForReviewsKey = "FogOfWar_DontBegForReviews";

        static readonly string[] FogUpdateMethods = { "Update", "Late Update", "Start In Update, Finish in Late Update" };
        static readonly string[] FowSampleOptions = { "Pixel-Perfect", "Texture Storage" };
        static readonly string[] FogTypeOptions = { "Hard", "Soft" };
        static readonly string[] FogAppearanceOptions = { "Solid Color", "Gray Scale", "Blur", "Texture Color", "Outline (Temporarily non-functional)", "None" };
        static readonly string[] FogFadeOptions = { "Linear", "Exponential", "Smooth", "Smoother", "Smooth Step" };
        static readonly string[] FogBlendOptions = { "Maximum", "Additive" };
        static readonly string[] RevealerModeOptions = { "Every Frame", "Time Spliced", "Manual Updates" };
        static readonly string[] GamePlaneOptions = { "XZ", "XY", "ZY" };

        static void InitializeFonts()
        {
            TitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18
            };
            TitleStyle.wordWrap = false;

            SubTitleStyle = new GUIStyle(EditorStyles.label);
            SubTitleStyle.wordWrap = true;

            WarningGUIStyleWithWrap = new GUIStyle(EditorStyles.label);
            WarningGUIStyleWithWrap.normal.textColor = Color.red;
            WarningGUIStyleWithWrap.wordWrap = true;
        }

        void InitializeInspector()
        {
            _fow = (FogOfWarWorld)target;

            DontBegForReviews = EditorPrefs.GetBool(DontBegForReviewsKey, false);

            _updateMethod = serializedObject.FindProperty("UpdateMethod");
            _fogType = serializedObject.FindProperty("FogType");
            _fogFade = serializedObject.FindProperty("FogFade");
            _fogFadePower = serializedObject.FindProperty("FogFadePower");
            _blendType = serializedObject.FindProperty("BlendType");
            _allowBleeding = serializedObject.FindProperty("AllowBleeding");
            _sightExtraAmount = serializedObject.FindProperty("SightExtraAmount");
            _edgeSoftenDistance = serializedObject.FindProperty("EdgeSoftenDistance");
            _maxFogDistance = serializedObject.FindProperty("MaxFogDistance");
            _invertFowEffect = serializedObject.FindProperty("InvertFowEffect");
            _pixelateFog = serializedObject.FindProperty("PixelateFog");
            _worldSpacePixelate = serializedObject.FindProperty("WorldSpacePixelate");
            _pixelDensity = serializedObject.FindProperty("PixelDensity");
            _roundRevealerPosition = serializedObject.FindProperty("RoundRevealerPosition");
            _pixelGridOffset = serializedObject.FindProperty("PixelGridOffset");
            _useDithering = serializedObject.FindProperty("UseDithering");
            _ditherSize = serializedObject.FindProperty("DitherSize");
            _unknownColor = serializedObject.FindProperty("UnknownColor");
            _saturationStrength = serializedObject.FindProperty("SaturationStrength");
            _blurStrength = serializedObject.FindProperty("BlurStrength");
            _blurDistanceScreenPercentMin = serializedObject.FindProperty("BlurDistanceScreenPercentMin");
            _blurDistanceScreenPercentMax = serializedObject.FindProperty("BlurDistanceScreenPercentMax");
            _blurSamples = serializedObject.FindProperty("BlurSamples");
            _fogTexture = serializedObject.FindProperty("FogTexture");
            _useTriplanar = serializedObject.FindProperty("UseTriplanar");
            _fogTextureTiling = serializedObject.FindProperty("FogTextureTiling");
            _fogScrollSpeed = serializedObject.FindProperty("FogScrollSpeed");
            _outlineThickness = serializedObject.FindProperty("OutlineThickness");
            _fowSamplingMode = serializedObject.FindProperty("FOWSamplingMode");
            _hidersUseFogTexture = serializedObject.FindProperty("HidersUseFogTexture");
            _hidersTextureSeenThreshold = serializedObject.FindProperty("HiderSeenThreshold");
            _asyncReadbackFogDataToCPU = serializedObject.FindProperty("AsyncReadbackFogDataToCpu");
            _useConstantBlur = serializedObject.FindProperty("UseConstantBlur");
            _constantTextureBlurQuality = serializedObject.FindProperty("ConstantTextureBlurQuality");
            _constantTextureBlurAmount = serializedObject.FindProperty("ConstantTextureBlurAmount");
            _useWorldBounds = serializedObject.FindProperty("UseWorldBounds");
            _worldBoundsSoftenDistance = serializedObject.FindProperty("WorldBoundsSoftenDistance");
            _worldBoundsInfluence = serializedObject.FindProperty("WorldBoundsInfluence");
            _worldBounds = serializedObject.FindProperty("WorldBounds");
            _useMiniMap = serializedObject.FindProperty("UseMiniMap");
            _fowResX = serializedObject.FindProperty("FowResX");
            _fowResY = serializedObject.FindProperty("FowResY");
            _useRegrow = serializedObject.FindProperty("UseRegrow");
            _revealerFadeIn = serializedObject.FindProperty("RevealerFadeIn");
            _revealerFadeInSpeed = serializedObject.FindProperty("RevealerFadeInSpeed");
            _revealerFadeOutSpeed = serializedObject.FindProperty("RevealerFadeOutSpeed");
            _revealerFadeOut = serializedObject.FindProperty("RevealerFadeOut");
            _initialFogExplorationValue = serializedObject.FindProperty("InitialFogExplorationValue");
            _maxFogRegrowAmount = serializedObject.FindProperty("MaxFogRegrowAmount");
            _revealerUpdateMode = serializedObject.FindProperty("RevealerUpdateMode");
            _maxNumRevealersPerFrame = serializedObject.FindProperty("MaxNumRevealersPerFrame");
            _useStagedGPUUploads = serializedObject.FindProperty("UseStagedGPUUploads");
            _scatterRevealersShader = serializedObject.FindProperty("ScatterRevealersShader");
            _useSpatialAcceleration = serializedObject.FindProperty("UseSpatialAcceleration");
            _spatialHashTableSize = serializedObject.FindProperty("NumSpatialHashBuckets");
            _spatialHashGridSize = serializedObject.FindProperty("SpatialHashGridSize");


            _maxPossibleRevealers = serializedObject.FindProperty("MaxPossibleRevealers");
            _maxPossibleSegmentsPerRevealer = serializedObject.FindProperty("MaxPossibleSegmentsPerRevealer");
            _maxPossibleHiders = serializedObject.FindProperty("MaxPossibleHiders");
            _is2D = serializedObject.FindProperty("is2D");
            _gamePlaneOrientation = serializedObject.FindProperty("GamePlaneOrientation");
        }

        void OnEnable()
        {
            InitializeInspector();
            EnsureComputeShaders();
        }

        void EnsureComputeShaders()
        {
            if (_fow.ScatterRevealersShader != null)
                return;

            _fow.ScatterRevealersShader = FindComputeShaderByName("ScatterRevealers");
            if (_fow.ScatterRevealersShader != null)
            {
                EditorUtility.SetDirty(_fow);
                Debug.Log("FogOfWarWorld: Auto-assigned ScatterRevealers compute shader.");
            }
        }

        static ComputeShader FindComputeShaderByName(string shaderName)
        {
            string[] guids = AssetDatabase.FindAssets($"{shaderName} t:ComputeShader");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (shader != null && shader.name == shaderName)
                    return shader;
            }
            return null;
        }

        void OnSceneGUI()
        {
            if (_fow.UseWorldBounds || _fow.UseMiniMap ||
                _fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Texture ||
                _fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Both)
            {
                _boundsHandle.center = _fow.WorldBounds.center;
                _boundsHandle.size = _fow.WorldBounds.size;

                EditorGUI.BeginChangeCheck();
                _boundsHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_fow, "Change Bounds");
                    _fow.UpdateWorldBounds(new Bounds(_boundsHandle.center, _boundsHandle.size));
                }
            }
        }

        void PropertyWithUpdate(SerializedProperty prop, string label = null)
        {
            EditorGUI.BeginChangeCheck();

            GUIContent content;
            if (label != null)
                content = new GUIContent(label, prop.tooltip);
            else
                content = new GUIContent(prop.displayName, prop.tooltip);

            EditorGUILayout.PropertyField(prop, content);

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                _fow.UpdateAllShaderProperties();
            }
        }

        bool PropertyNoUpdate(SerializedProperty prop, string label = null)
        {
            EditorGUI.BeginChangeCheck();

            GUIContent content;
            if (label != null)
                content = new GUIContent(label, prop.tooltip);
            else
                content = new GUIContent(prop.displayName, prop.tooltip);

            EditorGUILayout.PropertyField(prop, content);

            bool change = EditorGUI.EndChangeCheck();
            if (change)
                serializedObject.ApplyModifiedProperties();
            return change;
        }

        void PopupWithUpdate(SerializedProperty prop, string label, string[] options)
        {
            EditorGUI.BeginChangeCheck();
            prop.enumValueIndex = EditorGUILayout.Popup(new GUIContent(label, prop.tooltip), prop.enumValueIndex, options);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                _fow.UpdateAllShaderProperties();
            }
        }

        void PopupNoUpdate(SerializedProperty prop, string label, string[] options)
        {
            EditorGUI.BeginChangeCheck();
            prop.enumValueIndex = EditorGUILayout.Popup(new GUIContent(label, prop.tooltip), prop.enumValueIndex, options);
            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();
        }

        bool SliderWithUpdate(SerializedProperty prop, string label, float min, float max)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Slider(prop, min, max, new GUIContent(label, prop.tooltip));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                _fow.UpdateAllShaderProperties();
                return true;
            }
            return false;
        }

        void IntSliderWithUpdate(SerializedProperty prop, string label, int min, int max)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.IntSlider(prop, min, max, new GUIContent(label, prop.tooltip));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                _fow.UpdateAllShaderProperties();
            }
        }

        void PropertyWithInitRT(SerializedProperty prop, string label = null)
        {
            EditorGUI.BeginChangeCheck();

            GUIContent content;
            if (label != null)
                content = new GUIContent(label, prop.tooltip);
            else
                content = new GUIContent(prop.displayName, prop.tooltip);

            EditorGUILayout.PropertyField(prop, content);

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                if (!Application.isPlaying)
                    return;
                _fow.InitFOWRT();
                _fow.UpdateAllShaderProperties();
                _fow.UpdateFowTextureMaterialProperties();
            }
        }

        void IntSliderWithInitRT(SerializedProperty prop, string label, int min, int max)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.IntSlider(prop, min, max, new GUIContent(label, prop.tooltip));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                if (!Application.isPlaying)
                    return;
                _fow.InitFOWRT();
                _fow.UpdateAllShaderProperties();
                _fow.UpdateFowTextureMaterialProperties();
            }
        }

        public override void OnInspectorGUI()
        {
            if (SubTitleStyle == null)
                InitializeFonts();

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

            EditorGUILayout.Space(5);

            serializedObject.Update();

            #region Rendering Options

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("FOW Rendering Options", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("General Rendering Options", EditorStyles.boldLabel);

            // Fog Type
            PopupWithUpdate(_fogType, "Fog Type", FogTypeOptions);

            if (_fow.FogType == FogOfWarWorld.FogOfWarType.Soft)
            {
                EditorGUILayout.LabelField("Soft Fog Options");
                EditorGUI.indentLevel++;
                PopupWithUpdate(_fogFade, "-Soft Fog Fade Mode", FogFadeOptions);

                if (_fow.FogFade == FogOfWarWorld.FogOfWarFadeType.Exponential)
                    PropertyWithUpdate(_fogFadePower, "   -Fade Exponent");

                PopupWithUpdate(_blendType, "-Revealer Combination Mode", FogBlendOptions);

                // Dithering
                if (_fow.FogType == FogOfWarWorld.FogOfWarType.Soft)
                {
                    PropertyWithUpdate(_useDithering, "-Use Dithering?");
                    if (_fow.UseDithering)
                        PropertyWithUpdate(_ditherSize, "  -Dithering Size");
                }

                EditorGUI.indentLevel--;

                EditorGUILayout.Space(2);
            }

            SliderWithUpdate(_sightExtraAmount, "Revealer Extra Sight Distance", -1f, 1f);

            if (_fow.FogType == FogOfWarWorld.FogOfWarType.Soft)
                SliderWithUpdate(_edgeSoftenDistance, "Revealer Extra Sight Distance Softening", 0, 1);

            SliderWithUpdate(_maxFogDistance, "Max Fog Distance", 0f, 10000f);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Fog Appearance", EditorStyles.boldLabel);

            // Fog Appearance - special case, calls SetFowAppearance
            EditorGUI.BeginChangeCheck();
            int appearanceIndex = EditorGUILayout.Popup("Fog Shader", (int)_fow.GetFowAppearance(), FogAppearanceOptions);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_fow, "Change FOW Appearance");
                _fow.SetFowAppearance((FogOfWarWorld.FogOfWarAppearance)appearanceIndex);
            }

            var appearance = _fow.GetFowAppearance();
            if (appearance != FogOfWarWorld.FogOfWarAppearance.None)
                PropertyWithUpdate(_unknownColor, "Unknown Area Color");

            switch (appearance)
            {
                case FogOfWarWorld.FogOfWarAppearance.GrayScale:
                    SliderWithUpdate(_saturationStrength, "Unknown Area Saturation Strength", 0f, 1f);
                    break;

                case FogOfWarWorld.FogOfWarAppearance.Blur:
                    SliderWithUpdate(_blurStrength, "Unknown Area Blur Strength", -1f, 1f);
                    SliderWithUpdate(_blurDistanceScreenPercentMin, "Min Screen Percent", 0f, 2f);
                    SliderWithUpdate(_blurDistanceScreenPercentMax, "Max Screen Percent", 0f, 2f);
                    IntSliderWithUpdate(_blurSamples, "Num Blur Samples", 6, 18);
                    break;

                case FogOfWarWorld.FogOfWarAppearance.Texture_Sample:
                    PropertyWithUpdate(_fogTexture, "Fog Of War Texture");
                    PropertyWithUpdate(_useTriplanar, "Use Triplanar Sampling?");
                    PropertyWithUpdate(_fogTextureTiling, "Texture Tiling");
                    PropertyWithUpdate(_fogScrollSpeed, "Texture Scroll Speed");
                    break;

                case FogOfWarWorld.FogOfWarAppearance.Outline:
                    PropertyWithUpdate(_outlineThickness, "Outline Thickness");
                    break;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("FOW Extra Rendering Options", EditorStyles.boldLabel);

            // Pixelation
            EditorGUILayout.Space(1);
            PropertyWithUpdate(_pixelateFog, "Pixelate Fog?");
            if (_fow.PixelateFog)
            {
                PropertyWithUpdate(_worldSpacePixelate, "- Use World Space?");
                PropertyWithUpdate(_pixelDensity, "- Pixel Density");
                PropertyWithUpdate(_roundRevealerPosition, "- Round Revealer Position?");
                PropertyWithUpdate(_pixelGridOffset, "- Pixel Grid Offset");
            }

            //Bounds
            EditorGUILayout.Space(2);
            PropertyWithUpdate(_useWorldBounds, "Darken World Bounds?");
            if (_fow.UseWorldBounds)
            {
                SliderWithUpdate(_worldBoundsSoftenDistance, "  -World Bounds Soften Distance", 0f, 5f);
                SliderWithUpdate(_worldBoundsInfluence, "  -World Bounds Influence", 0f, 1f);
            }

            EditorGUILayout.Space(2);
            PropertyWithUpdate(_invertFowEffect, "Invert Fow Effect?");

            EditorGUILayout.Space(2);
            PropertyWithUpdate(_allowBleeding, "Add Slight Arc Leaking?");

            EditorGUILayout.EndVertical();

            #endregion

            EditorGUILayout.Space(5);

            #region Sampling Options

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("FOW Sampling Mode", EditorStyles.boldLabel);

            PopupWithUpdate(_fowSamplingMode, "Fog Sample Mode", FowSampleOptions);

            if (_fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Texture ||
                _fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Both)
            {
                EditorGUILayout.LabelField("Texture Storage Mode Sampling Options:");
                PropertyWithUpdate(_useConstantBlur, "--Use Blur?");
                if (_fow.UseConstantBlur)
                {
                    IntSliderWithUpdate(_constantTextureBlurQuality, "--Texture Blur Quality", 1, 6);
                    SliderWithUpdate(_constantTextureBlurAmount, "--Texture Blur Amount", 0f, 5f);
                }
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndVertical();

            #endregion

            #region FOW Texture Options

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("FOW Texture Options", EditorStyles.boldLabel);

            PropertyWithUpdate(_useMiniMap, "Force Render Fow Texture (For Mini-Map)");

            if (_fow.UseMiniMap || _fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Texture ||
                _fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Both)
            {
                EditorGUILayout.Space(5);

                string reasonsForSeeingThis = "You see this because the following options are enabled:";

                if (_fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Texture)
                    reasonsForSeeingThis += "\n  -Fog Sample Mode is Texture Storage.";
                if (_fow.UseMiniMap)
                    reasonsForSeeingThis += "\n  -MiniMap generation is enabled.";
                EditorGUILayout.LabelField(reasonsForSeeingThis, SubTitleStyle);

                EditorGUILayout.Space(4);

                IntSliderWithInitRT(_fowResX, "Texture Resolution X", 128, 4096);
                IntSliderWithInitRT(_fowResY, "Texture Resolution Y", 128, 4096);

                EditorGUILayout.Space(4);

                if (PropertyNoUpdate(_hidersUseFogTexture, "Hiders use Fog Texture?"))
                {
                    _fow.SwitchHidersUseFogTextureMode(_fow.HidersUseFogTexture);
                    //_fow.ToggleFogTextureAsyncReadbackToCpu(_fow.AsyncReadbackFogDataToCpu);
                }
                EditorGUI.BeginDisabledGroup(!_fow.HidersUseFogTexture);
                if (PropertyNoUpdate(_hidersTextureSeenThreshold, "Hider Seen Threshold for Fog Texture"))
                {
                    _fow.UpdateHiderSeenThresholdForFogTexture();
                    //_fow.ToggleFogTextureAsyncReadbackToCpu(_fow.AsyncReadbackFogDataToCpu);
                }
                EditorGUI.EndDisabledGroup();

                //EditorGUI.BeginDisabledGroup(_fow.HidersUseFogTexture);
                if (PropertyNoUpdate(_asyncReadbackFogDataToCPU, "Enable Async Readback Of Fog Texture?"))
                    _fow.ToggleFogTextureAsyncReadbackToCpu(_fow.AsyncReadbackFogDataToCpu);
                //EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(10);

                PropertyWithInitRT(_useRegrow, "Use Fog Regrow/Memory?");

                if (_fow.UseRegrow)
                {
                    EditorGUI.indentLevel++;
                    PropertyWithUpdate(_revealerFadeIn, "Gradually Fade In Revealer?");
                    if (_fow.RevealerFadeIn)
                    {
                        //EditorGUI.indentLevel++;
                        if (SliderWithUpdate(_revealerFadeInSpeed, "-Revealer Fade In Speed", 0f, 10f))
                            _fow.UpdateFowTextureMaterialProperties();
                        //EditorGUI.indentLevel--;
                    }

                    PropertyWithUpdate(_revealerFadeOut, "Gradually Fade Out Revealer?");
                    if (_fow.RevealerFadeOut)
                    {
                        //EditorGUI.indentLevel++;
                        if (SliderWithUpdate(_revealerFadeOutSpeed, "-Revealer Fade Out Speed", 0f, 10f))
                            _fow.UpdateFowTextureMaterialProperties();
                        //EditorGUI.indentLevel--;
                    }

                    SliderWithUpdate(_initialFogExplorationValue, "Initial Fog Exploration Amount", 0f, 1f);
                    if (SliderWithUpdate(_maxFogRegrowAmount, "Explored Fog Retention", 0f, 1f))   //Previously Explored Fog Opacity
                        _fow.UpdateFowTextureMaterialProperties();
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndVertical();

            #endregion

            #region FOW Bounds Options

            bool showBounds = _fow.UseWorldBounds || _fow.UseMiniMap ||
                _fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Texture ||
                _fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Both;

            if (showBounds)
            {
                EditorGUILayout.Space(10);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("FOW Bounds", EditorStyles.boldLabel);

                if (_fow.WorldBounds.extents == Vector3.one * .5f)
                    EditorGUILayout.LabelField("Make sure to actually set your world bounds!", WarningGUIStyleWithWrap);

                string reasonsForSeeingThis = "You see this because the following options are enabled:";

                if (_fow.FOWSamplingMode == FogOfWarWorld.FogSampleMode.Texture)
                    reasonsForSeeingThis += "\n  -Fog Sample Mode is Texture Storage.";
                if (_fow.UseWorldBounds == true)
                    reasonsForSeeingThis += "\n  -Darken World Bounds is enabled.";
                if (_fow.UseMiniMap)
                    reasonsForSeeingThis += "\n  -MiniMap generation is enabled.";
                EditorGUILayout.LabelField(reasonsForSeeingThis, SubTitleStyle);

                PropertyWithInitRT(_worldBounds.FindPropertyRelative("m_Center"), "Center");
                PropertyWithInitRT(_worldBounds.FindPropertyRelative("m_Extent"), "Extents");

                EditorGUILayout.EndVertical();
            }

            #endregion

            EditorGUILayout.Space(5);

            #region Configuration and Optimizations
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Configuration and Optimizations", EditorStyles.boldLabel);

            PopupWithUpdate(_updateMethod, "Update Method", FogUpdateMethods);

            EditorGUILayout.Space(2);

            PopupNoUpdate(_revealerUpdateMode, "Revealer Update Timing", RevealerModeOptions);
            if (_fow.RevealerUpdateMode == FogOfWarWorld.RevealerUpdateMethod.N_Per_Frame)
                PropertyNoUpdate(_maxNumRevealersPerFrame, "--Num Revealers Per Frame");

            EditorGUILayout.Space(10);
            if (PropertyNoUpdate(_useSpatialAcceleration, "Use Spatial Acceleration?"))
            {
                _fow.SwitchSpatialAccelerationMode(_fow.UseSpatialAcceleration);
            }
            if (_fow.UseSpatialAcceleration)
            {
                EditorGUI.BeginDisabledGroup(Application.isPlaying);
                EditorGUI.indentLevel++;
                PropertyNoUpdate(_spatialHashGridSize, "Grid Size");
                PropertyNoUpdate(_spatialHashTableSize, "Table Capacity");
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(10);
            if (PropertyNoUpdate(_useStagedGPUUploads, "Use Staged GPU Uploads?"))
            {
                _fow.ToggleStagedUploads(_fow.UseStagedGPUUploads);
            }
            if (_fow.UseStagedGPUUploads)
            {
                EditorGUI.indentLevel++;
                PropertyNoUpdate(_scatterRevealersShader, "Scatter Compute Shader");
                if (_fow.ScatterRevealersShader == null)
                    EditorGUILayout.HelpBox("Assign the ScatterRevealers compute shader to enable staged GPU uploads.", MessageType.Warning);

                if (Application.isPlaying)
                {
                    int metaBytes = _fow.MaxPossibleRevealers * 12;
                    int dataBytes = _fow.MaxPossibleRevealers * 20;
                    int segBytes = _fow.MaxPossibleRevealers * _fow.MaxPossibleSegmentsPerRevealer * 12;
                    float totalMB = (metaBytes + dataBytes + segBytes) * 2f / (1024f * 1024f); // x2 for CPU + GPU
                    EditorGUILayout.LabelField($"Staging buffer memory: {totalMB:F2} MB ({totalMB / 2f:F2} MB GPU + {totalMB / 2f:F2} MB CPU)", EditorStyles.miniLabel);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            #endregion

            EditorGUILayout.Space(5);

            #region Utility Options

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Utility Options (cant be changed at runtime)");

            EditorGUI.BeginDisabledGroup(Application.isPlaying);
            PropertyNoUpdate(_maxPossibleRevealers, "Max Num Revealers");
            PropertyNoUpdate(_maxPossibleSegmentsPerRevealer, "Max Num Segments Per Revealer");
            PropertyNoUpdate(_maxPossibleHiders, "Initial Hider Pool Capacity");
            PropertyWithUpdate(_is2D, "Is 2D?");

            if (!_fow.is2D)
                PopupWithUpdate(_gamePlaneOrientation, "Game Plane", GamePlaneOptions);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
            #endregion

            #region Begging for reviews
            if (!DontBegForReviews)
            {
                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("Please consider leaving a review on the Unity Asset Store!", TitleStyle);
                GUILayout.Space(3);
                GUILayout.Label("Pixel-Perfect Fog Of War has received countless free updates over the course of many years, thanks to the continuous support and feedback from our community!", SubTitleStyle);
                GUILayout.Space(2);
                GUILayout.Label("Reviews small creators grow, and our users feedback is always considered, in order to help make our tools the best they can be!", SubTitleStyle);
                GUILayout.Space(2);
                if (GUILayout.Button("Open Asset Store Link"))
                {
                    Application.OpenURL("https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/pixel-perfect-fog-of-war-229484");
                }
                if (GUILayout.Button("Join the Discord for fast support!"))
                {
                    Application.OpenURL("http://discord.gg/invite/zRfMsDmuGw");
                }
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Never Show Again"))
                {
                    DontBegForReviews = true;
                    EditorPrefs.SetBool(DontBegForReviewsKey, true);
                }
                EditorGUILayout.EndVertical();
            }
            #endregion
        }
    }
}
#endif