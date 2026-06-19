using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using System;
using UnityEngine.Serialization;
using static FOW.FogOfWarRevealer3D;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Unity.Profiling;


#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

namespace FOW
{
    [DefaultExecutionOrder(-100)]
    public class FogOfWarWorld : MonoBehaviour
    {

        #region SERIALIZED STUFF

        //RENDERING OPTIONS
        public FogOfWarType FogType = FogOfWarType.Soft;
        public FogOfWarFadeType FogFade = FogOfWarFadeType.Smoothstep;
        public float FogFadePower = 1;
        public FogOfWarBlendMode BlendType = FogOfWarBlendMode.Additive;

        #region DITHERING

        [Tooltip("Uses dithering instead of true opacity.")]
        public bool UseDithering = false;
        public float DitherSize = 20;

        #endregion

        [Tooltip("Prevents Z-Fighting by allowing fog to slightly expand past its actual vision radius." +
            "\n\nYou can also use negative values to prevent unwanted bleeding, in the case you use pixelated fog, or texture fog with a low resolution.")]
        public float SightExtraAmount = .01f;
        [Tooltip("Controls softening for Revealer Extra Sight Distance")]
        public float EdgeSoftenDistance = .1f;

        [Tooltip("Controls the maximum distance FOW is rendered")]
        public float MaxFogDistance = 10000f;

        //SHADER OPTIONS
        [SerializeField] private FogOfWarAppearance FogAppearance;

        [Tooltip("The color of the fog")]
        public Color UnknownColor = new Color(.35f, .35f, .35f);

        #region Grayscale Sample Options

        public float SaturationStrength = 0;

        #endregion

        #region Blur Fog Shader Options

        public float BlurStrength = 1;
        //public float blurPixelOffset = 2.5f;
        [Range(0, 2)]
        public float BlurDistanceScreenPercentMin = .1f;
        [Range(0, 2)]
        public float BlurDistanceScreenPercentMax = 1;
        public int BlurSamples = 6;

        #endregion

        #region Texture Sample Shader Options

        public Texture2D FogTexture;
        public bool UseTriplanar = true;
        public Vector2 FogTextureTiling = Vector2.one;
        public Vector2 FogScrollSpeed = Vector2.one;

        #endregion

        #region Outline Shader Options

        public float OutlineThickness = .1f;

        #endregion

        //EXTRA RENDERING OPTIONS
        #region PIXELATION

        public bool PixelateFog = false;
        public bool WorldSpacePixelate = false;
        public float PixelDensity = 2f;
        public bool RoundRevealerPosition = false;
        public Vector2 PixelGridOffset;

        #endregion

        #region WORLD BOUNDS

        public bool UseWorldBounds;
        public float WorldBoundsSoftenDistance = 1f;
        public float WorldBoundsInfluence = 1;

        #endregion

        public bool InvertFowEffect;

        [Tooltip("Allows fog to slightly bleed past obstacle edges in an arc shape")]
        public bool AllowBleeding = false;

        //SAMPLING MODE OPTIONS
        [Tooltip("Controls how fog is sampled in the fullscreen shader" +
            "\n\nPixel-Perfect- Fog is calculated per-pixel in screen space." +
            "\n  Pros:\n    -This mode allows for unlimited world sized with full resolution fog." +
            "\n  Cons:\n    -Cannot use temporal based effects, like fog memory/regrow/retention." +
            "\n\nTexture Storage- This mode uses a more traditional method of rendering FOW. It first does the fog calculations on a Render Texture, then samples that render texture in the fullscreen shader." +
            "\n  Pros:\n    -Can use extra fog effects, such as fog memory/regrow/retention." +
            "\n  Cons:\n    -Requires rendering to a render texture, which uses gpu memory.\n    -Resolution bound, large worlds requires rendering the fog texture at higher resolutions to avoid seeing noticeable grids.")]
        public FogSampleMode FOWSamplingMode = FogSampleMode.Pixel_Perfect;

        #region TEXTURE SAMPLE FOG OPTIONS

        [Tooltip("When true, hiders will sample the Texture Storage fog, instead of using a revealers direct line of sight")]
        public bool HidersUseFogTexture = true;
        [Tooltip("The threshold at which hiders are seen with the fog texture.")]
        [Range(0,1f)]
        public float HiderSeenThreshold = .5f;
        [Tooltip("When true, sampling the fog texture on the CPU will be much faster, but will calculate it even when its not needed." +
            "\n\nYou should definitely keep this true if you need to sample the fog texture from code frequently." +
            "\n\nIf Hiders Use Fog Texture is true, then this is also true.")]
        public bool AsyncReadbackFogDataToCpu = false;

        public bool UseConstantBlur = true;
        public int ConstantTextureBlurQuality = 2;
        public float ConstantTextureBlurAmount = 0.75f;

        #endregion

        //FOW TEXTURE OPTIONS
        public bool UseMiniMap;
        //public int FowTextureMsaa = 8;
        public int FowResX = 512;
        public int FowResY = 512;

        #region REGROW OPTIONS

        public bool UseRegrow;
        public bool RevealerFadeIn = false;
        public float RevealerFadeInSpeed = .5f;

        public bool RevealerFadeOut = false;
        [FormerlySerializedAs("FogRegrowSpeed")]
        public float RevealerFadeOutSpeed = .5f;

        public float InitialFogExplorationValue = 0;
        public float MaxFogRegrowAmount = .3f;

        #endregion

        //WORLD BOUNDS
        public Bounds WorldBounds = new Bounds(Vector3.zero, Vector3.one);

        //CONFIG AND OPTIMIZATION
        [Tooltip("Changes where Fog of War updates revealers, calculates hiders, and updates the fog texture." +
            "\n\nUpdate: Updates happen in update" +
            "\n\nLate Update: Updates happen in Late Update" +
            "\n\nStart in update, Finish in late update: Since revealers use the c# jobs system, we can use this option to let the job run for as long as possible before completing the job manually.")]
        public FowUpdateMethod UpdateMethod = FowUpdateMethod.LateUpdate;

        [Tooltip("Controls how revealers are calculated" +
            "\n\nEvery Frame- Every revealer is calculated every frame" +
            "\n\nTime Spliced- Revealers take turns being calculated. You can choose how many are calculated per-frame." +
            "\n\nManual Updates- Revealers will not be automatically updated. Instead, you can update them manually in code.")]
        [FormerlySerializedAs("revealerMode")]
        public RevealerUpdateMethod RevealerUpdateMode = RevealerUpdateMethod.N_Per_Frame;

        [Tooltip("The number of revealers to update each frame. Only used when Revealer Mode is set to 'Time Spliced'")]
        [Min(1)]
        public int MaxNumRevealersPerFrame = 25;

        [Tooltip("Batch GPU data upload by Defering till end of frame")]
        public bool UseStagedGPUUploads = true;
        public ComputeShader ScatterRevealersShader;

        [SerializeField] public bool UseSpatialAcceleration = true;
        [Tooltip("The cell size used for the spatial hash grid. The best value to use for this will be your average revealers radius (including soften distance) times two.")]
        [SerializeField] private int SpatialHashGridSize = 32;
        [Tooltip("How many buckets to use when spatial hashing. more buckets = less collision")]
        [SerializeField] private int NumSpatialHashBuckets = 1024;

        //utility options
        [Tooltip("The Max possible number of revealers. Keep this as low as possible to use less GPU memory")]
        public int MaxPossibleRevealers = 256;
        [Tooltip("The Max possible number of segments per revealer. Keep this as low as possible to use less GPU memory")]
        public int MaxPossibleSegmentsPerRevealer = 128;
        [Tooltip("The Max possible number of Hiders. Keep this as low as possible to use less memory. It will automatically resize if you add too many hiders, but that can cause a hitch!")]
        public int MaxPossibleHiders = 512;
        public bool is2D;
        [FormerlySerializedAs("gamePlane")]
        public GamePlane GamePlaneOrientation = GamePlane.XZ;

        #endregion

        #region RUNTIME STUFF

        public static FogOfWarWorld instance;

        public static bool UsingSoftening;

        public Material FogOfWarMaterial;
        public Material FowTextureMaterial;
        static RenderTexture FOW_RT;
        static RenderTexture FOW_TEMP_RT;

        static int TotalMaximumSightSegments;
        public static ComputeBuffer ActiveRevealerIndicesBuffer;    //only used for non-spatial hash path. remove when spatial hashing is battle tested.
        public static ComputeBuffer RevealerInfoBuffer;
        public static ComputeBuffer RevealerDataBuffer;
        public static ComputeBuffer AnglesBuffer;

        #region REVEALERS

        public static FogOfWarRevealer[] ActiveRevealers;
        public static FogOfWarRevealer[] UnsortedRevealers;
        public static int NumActiveRevealers;
        public static int numDynamicRevealers;
        public static List<int> DynamicRevealerIndices = new List<int>();
        public static Queue<int> DeregisteredRevealerIDs = new Queue<int>();

        #endregion

        #region HIDERS

        public static FogOfWarHider[] ActiveHiders;
        public static FogOfWarHider[] UnsortedHiders;
        public static int[] ActiveHiderIndices;
        public static int NumActiveHiders;
        public static Queue<int> DeregisteredHiderIDs = new Queue<int>();

        #endregion

        #region Deferred Registration
        
        public bool IsInPhasedUpdate { get; private set; }

        public static List<FogOfWarRevealer> PendingRevealerRegister = new List<FogOfWarRevealer>();
        public static List<FogOfWarRevealer> PendingRevealerDeregister = new List<FogOfWarRevealer>();
        public static List<FogOfWarHider> PendingHiderRegister = new List<FogOfWarHider>();
        public static List<FogOfWarHider> PendingHiderDeregister = new List<FogOfWarHider>();

        private void DrainPending()
        {
            for (int i = 0; i < PendingRevealerDeregister.Count; i++)
            {
                var r = PendingRevealerDeregister[i];
                if (r != null) r.DeregisterRevealer();
            }
            PendingRevealerDeregister.Clear();

            for (int i = 0; i < PendingRevealerRegister.Count; i++)
            {
                var r = PendingRevealerRegister[i];
                if (r != null) r.RegisterRevealer();
            }
            PendingRevealerRegister.Clear();

            for (int i = 0; i < PendingHiderDeregister.Count; i++)
            {
                var h = PendingHiderDeregister[i];
                if (h != null) h.DeregisterHider();
            }
            PendingHiderDeregister.Clear();

            for (int i = 0; i < PendingHiderRegister.Count; i++)
            {
                var h = PendingHiderRegister[i];
                if (h != null) h.RegisterHider();
            }
            PendingHiderRegister.Clear();
        }

        #endregion

        private static int[] indiciesDataToSet = new int[1];
        private static bool UsingFowTexture;

        private AsyncFogTextureReader _asyncFogTextureReader;
        private static bool revealerSeesHiders;
        //private static bool UsingFogAsyncReadback;

        //staged GPU upload fields
        private DirtyRevealerMeta[] _dirtyMetas;
        private RevealerDataStruct[] _stagingRevealerData;
        private GpuSightSegment[] _stagingSegments;
        private int _dirtyCount;
        private int _segmentWriteHead;
        private ComputeBuffer _dirtyMetaBuffer;
        private ComputeBuffer _stagingDataBuffer;
        private ComputeBuffer _stagingSegBuffer;
        private int _scatterKernel;

        #endregion

        #region SHADER IDS

        //global keywords
        internal static GlobalKeyword _spatialHashKw;
        internal static GlobalKeyword _is2DKw;
        internal static GlobalKeyword _useWorldBoundsKw;
        internal static GlobalKeyword _sampleTextureKw;
        internal static GlobalKeyword _useTextureBlurKw;
        internal static GlobalKeyword _sampleRealtimeKw;
        internal static GlobalKeyword _hardKw;
        internal static GlobalKeyword _softKw;
        internal static GlobalKeyword _bleedOnKw;
        internal static GlobalKeyword _pixelateKw;
        internal static GlobalKeyword _ditherOnKw;

        //compute buffer shader ids
        int activeRevealerIndicesID = Shader.PropertyToID("_ActiveRevealerIndices");
        int revealerInfoID = Shader.PropertyToID("_RevealerInfoBuffer");
        int revealerDataID = Shader.PropertyToID("_RevealerDataBuffer");
        int sightSegmentBufferID = Shader.PropertyToID("_SightSegmentBuffer");

        // scatter compute shader ids
        int dirtyCountID = Shader.PropertyToID("_DirtyCount");
        int maxSegmentsPerRevealerID = Shader.PropertyToID("_MaxSegmentsPerRevealer");
        int dirtyMetaID = Shader.PropertyToID("_DirtyMeta");
        int stagingDataID = Shader.PropertyToID("_StagingData");
        int stagingSegmentsID = Shader.PropertyToID("_StagingSegments");

        static int FowEffectStrengthID = Shader.PropertyToID("FowEffectStrength");
        int numRevealersID = Shader.PropertyToID("_NumRevealers");
        int materialColorID = Shader.PropertyToID("_unKnownColor");
        int extraRadiusID = Shader.PropertyToID("_extraRadius");
        int maxDistanceID = Shader.PropertyToID("_maxDistance");
        int fadePowerID = Shader.PropertyToID("_fadePower");
        int saturationStrengthID = Shader.PropertyToID("_saturationStrength");
        int blurStrengthID = Shader.PropertyToID("_blurStrength");
        int blurPixelOffsetMinID = Shader.PropertyToID("_blurPixelOffsetMin");
        int blurPixelOffsetMaxID = Shader.PropertyToID("_blurPixelOffsetMax");
        int blurSamplesID = Shader.PropertyToID("_blurSamples");
        int blurPeriodID = Shader.PropertyToID("_samplePeriod");
        int fowTextureID = Shader.PropertyToID("_fowTexture");
        int fowTilingID = Shader.PropertyToID("_fowTiling");
        int fowSpeedID = Shader.PropertyToID("_fowScrollSpeed");

        int fowPlaneID = Shader.PropertyToID("_fowPlane");
        int edgeSoftenDistanceID = Shader.PropertyToID("_edgeSoftenDistance");
        int pixelateWSID = Shader.PropertyToID("_pixelateWS");
        int pixelDensityID = Shader.PropertyToID("_pixelDensity");
        int pixelOffsetID = Shader.PropertyToID("_pixelOffset");
        int ditherSizeID = Shader.PropertyToID("_ditherSize");
        int invertEffectID = Shader.PropertyToID("_invertEffect");
        int fadeTypeID = Shader.PropertyToID("_fadeType");
        int blendMaxID = Shader.PropertyToID("BLEND_MAX");
        int skipTriplanarID = Shader.PropertyToID("_skipTriplanar");
        int fowAxisID = Shader.PropertyToID("_fowAxis");
        int lineThicknessID = Shader.PropertyToID("lineThickness");
        int fowRTID = Shader.PropertyToID("_FowRT");
        static readonly int CombatTabletopGroundYId = Shader.PropertyToID("_combatTabletopGroundY");
        int sampleBlurQualityID = Shader.PropertyToID("_Sample_Blur_Quality");
        int sampleBlurAmountID = Shader.PropertyToID("_Sample_Blur_Amount");
        int worldBoundsID = Shader.PropertyToID("_worldBounds");
        int worldBoundsInfluenceID = Shader.PropertyToID("_worldBoundsInfluence");
        int worldBoundsSoftenDistanceID = Shader.PropertyToID("_worldBoundsSoftenDistance");

        //fow render texture properties
        int fowRTFadeOutSpeedID = Shader.PropertyToID("_FowRT_FadeOutSpeed");
        int fowRTFadeInSpeedID = Shader.PropertyToID("_FowRT_FadeInSpeed");
        int fowRTMaxRegrowAmountID = Shader.PropertyToID("_FowRT_MaxRegrowAmount");

        #endregion

        #region Profiler Markers

#if UNITY_EDITOR

        static readonly ProfilerMarker UploadToGpuProfileMarker = new ProfilerMarker("Write to compute buffers");
        static readonly ProfilerMarker FlushGpuUploadsProfileMarker = new ProfilerMarker("Flush Staged GPU Uploads");
        static readonly ProfilerMarker HiderBucketsProfileMarker = new ProfilerMarker("Update Hider Buckets");
        static readonly ProfilerMarker RegisterRevealersProfileMarker = new ProfilerMarker("Register Revealer");
        static readonly ProfilerMarker DeRegisterRevealersProfileMarker = new ProfilerMarker("De-Register Revealer");
        static readonly ProfilerMarker RegisterHiderProfileMarker = new ProfilerMarker("Register Hider");
        static readonly ProfilerMarker DeRegisterHiderProfileMarker = new ProfilerMarker("De-Register Hider");
        static readonly ProfilerMarker LoopRevealersProfileMarker = new ProfilerMarker("Loop Revealers");
        static readonly ProfilerMarker FogTextureBlitProfileMarker = new ProfilerMarker("Fog Texture Blit");
        static readonly ProfilerMarker TextureHiderSampleProfileMarker = new ProfilerMarker("Fog Hider Sample (from texture)");

#endif

        #endregion

        #region Data Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct RevealerInfoStruct
        {
            public int StartIndex;

            public float RevealerVisionRadius;
            public float RevealerVisionRadiusFade;

            public float innerSoftenThreshold;
            public float invInnerSoftenThreshold;

            public float UnobscuredRadius;
            public float UnobscuredSoftenRadius;
            
            public float VisionHeight;
            public float VisionHeightFade;
            public float Opacity;
            public int UseOcclusion;   //0 false, 1 true
            public int NumTerrainClipSegments;
            public int CircleIsComplete;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct RevealerDataStruct
        {
            public float RevealerTotalVisionRadius;
            public Vector2 RevealerPosition;
            public float RevealerHeight;
            public int NumSegments;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct GpuSightSegment
        {
            public float2 direction;
            public float length;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct DirtyRevealerMeta
        {
            public int GpuId;
            public int StagingSegmentStart;
            public int NumSegments;
        };

        public enum FowUpdateMethod
        {
            Update,
            LateUpdate,
            StartInUpdateFinishInLateUpdate
        };

        public enum RevealerUpdateMethod
        {
            Every_Frame,
            N_Per_Frame,
            Controlled_ElseWhere,
        };

        public enum FogSampleMode
        {
            Pixel_Perfect,
            Texture,
            Both,
        };

        public enum FogOfWarType
        {
            //No_Bleed,
            //No_Bleed_Soft,
            Hard,
            Soft,
        };

        public enum FogOfWarFadeType
        {
            Linear,
            Exponential,
            Smooth,
            Smoother,
            Smoothstep,
        };

        public enum FogOfWarBlendMode
        {
            Max,
            Additive,
        };

        public enum FogOfWarAppearance
        {
            Solid_Color,
            GrayScale,
            Blur,
            Texture_Sample,
            Outline,
            None
        };

        public enum GamePlane
        {
            XZ,
            XY,
            ZY,
        };

        #endregion

        #region Unity Methods

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            _spatialHashKw = GlobalKeyword.Create("FOW_USE_SPATIAL_HASHING");
            _is2DKw = GlobalKeyword.Create("FOW_IS_2D");
            _useWorldBoundsKw = GlobalKeyword.Create("FOW_USE_WORLD_BOUNDS");
            _sampleTextureKw = GlobalKeyword.Create("FOW_SAMPLE_TEXTURE");
            _useTextureBlurKw = GlobalKeyword.Create("FOW_USE_TEXTURE_BLUR");
            _sampleRealtimeKw = GlobalKeyword.Create("FOW_SAMPLE_REALTIME");
            _hardKw = GlobalKeyword.Create("FOW_HARD");
            _softKw = GlobalKeyword.Create("FOW_SOFT");
            _bleedOnKw = GlobalKeyword.Create("FOW_BLEED_ON");
            _pixelateKw = GlobalKeyword.Create("FOW_PIXELATE");
            _ditherOnKw = GlobalKeyword.Create("FOW_DITHER_ON");
            ResetStatics();
        }

        static void ResetStatics()
        {
            instance = null;
            
            NumActiveRevealers = 0;
            numDynamicRevealers = 0;
            DynamicRevealerIndices.Clear();
            PendingRevealerRegister = new List<FogOfWarRevealer>();
            PendingRevealerDeregister = new List<FogOfWarRevealer>();
            DeregisteredRevealerIDs = new Queue<int>();

            NumActiveHiders = 0;
            PendingHiderRegister = new List<FogOfWarHider>();
            PendingHiderDeregister = new List<FogOfWarHider>();
            DeregisteredHiderIDs = new Queue<int>();
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // see the unity bug workaround section
            UnityBugWorkaround.OnAssetPostProcess += ReInitializeFOW;
#endif
            Initialize();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            // see the unity bug workaround section
            UnityBugWorkaround.OnAssetPostProcess -= ReInitializeFOW;
#endif
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        int currentIndex = 0;
        int numToLoopThisFrame;
        int phase2StartIndex;
        private void Update()
        {
            if (UpdateMethod == FowUpdateMethod.Update)
            {
                CalculateFOWPhaseOne();
                CalculateFOWPhaseTwo();
            }
            else if (UpdateMethod == FowUpdateMethod.StartInUpdateFinishInLateUpdate)
                CalculateFOWPhaseOne();
        }

        private void LateUpdate()
        {
            if (UpdateMethod == FowUpdateMethod.LateUpdate)
            {
                CalculateFOWPhaseOne();
                CalculateFOWPhaseTwo();
            }
            else if (UpdateMethod == FowUpdateMethod.StartInUpdateFinishInLateUpdate)
                CalculateFOWPhaseTwo();
        }

        #endregion

        #region FOW/Revealer updates
        
        void CalculateFOWPhaseOne()
        {
            IsInPhasedUpdate = true;
            if (NumActiveRevealers <= 0)
                return;

            if (revealerSeesHiders && SparseRevealerGrid.SpatialAccelerationActive)
            {
#if UNITY_EDITOR
                HiderBucketsProfileMarker.Begin();
#endif
                for (int i = 0; i < NumActiveHiders; i++)
                {
                    ActiveHiders[i].UpdateBuckets();
                }
#if UNITY_EDITOR
                HiderBucketsProfileMarker.End();
#endif
            }

#if UNITY_EDITOR
            LoopRevealersProfileMarker.Begin();
#endif

            switch (RevealerUpdateMode)
            {
                case RevealerUpdateMethod.Every_Frame:
                    for (int i = 0; i < NumActiveRevealers; i++)
                    {
                        var revealer = ActiveRevealers[i];
                        if (!revealer.CurrentlyStaticRevealer)
                            revealer.LineOfSightPhase1();
                        if (revealerSeesHiders)
                            revealer.RevealHiders();
                    }
                    break;
                case RevealerUpdateMethod.N_Per_Frame:
                    if (numDynamicRevealers == 0)
                        break;
                    numToLoopThisFrame = math.min(MaxNumRevealersPerFrame, numDynamicRevealers);
                    numToLoopThisFrame = math.max(numToLoopThisFrame, 1);
                    phase2StartIndex = currentIndex;
                    for (int i = 0; i < numToLoopThisFrame; i++)
                    {
                        currentIndex = (currentIndex + 1) % numDynamicRevealers;
                        var revealer = ActiveRevealers[DynamicRevealerIndices[currentIndex]];
                        revealer.LineOfSightPhase1();
                        if (revealerSeesHiders)
                            revealer.RevealHiders();
                    }
                    break;
                case RevealerUpdateMethod.Controlled_ElseWhere: break;
            }

            //FogOfWarRevealer.PostPhaseOne();

#if UNITY_EDITOR
            LoopRevealersProfileMarker.End();
#endif
        }

        void CalculateFOWPhaseTwo()
        {
            if (NumActiveRevealers > 0)
            {
#if UNITY_EDITOR
                LoopRevealersProfileMarker.Begin();
#endif
                switch (RevealerUpdateMode)
                {
                    case RevealerUpdateMethod.Every_Frame:
                        for (int i = 0; i < NumActiveRevealers; i++)
                        {
                            if (!ActiveRevealers[i].CurrentlyStaticRevealer)
                                ActiveRevealers[i].LineOfSightPhase2();
                        }
                        break;
                    case RevealerUpdateMethod.N_Per_Frame:
                        if (numDynamicRevealers == 0)
                            break;
                        int replayIndex = phase2StartIndex;
                        for (int i = 0; i < numToLoopThisFrame; i++)
                        {
                            replayIndex = (replayIndex + 1) % numDynamicRevealers;
                            ActiveRevealers[DynamicRevealerIndices[replayIndex]].LineOfSightPhase2();
                        }
                        break;
                    case RevealerUpdateMethod.Controlled_ElseWhere: break;
                }

#if UNITY_EDITOR
                LoopRevealersProfileMarker.End();
#endif
            }

            IsInPhasedUpdate = false;
            DrainPending();

            if (UseStagedGPUUploads)
                FlushStagedRevealerData();

            RenderFogTexture();
        }

        public void RenderFogTexture()
        {
            if (!UsingFowTexture)
                return;
#if UNITY_EDITOR
            FogTextureBlitProfileMarker.Begin();
#endif
            if (SparseRevealerGrid.SpatialAccelerationActive)
                SparseRevealerGrid.FlattenAndUpload();

            if (UseRegrow)
            {
                Graphics.Blit(FOW_RT, FOW_TEMP_RT);
                Graphics.Blit(FOW_TEMP_RT, FOW_RT, FowTextureMaterial, 0);
            }
            else
                Graphics.Blit(null, FOW_RT, FowTextureMaterial, 0);

#if UNITY_EDITOR
            FogTextureBlitProfileMarker.End();
#endif

            bool revealHidersWithTexture = !revealerSeesHiders && NumActiveHiders != 0;
            if (AsyncReadbackFogDataToCpu || revealHidersWithTexture)
            {
#if UNITY_EDITOR
                TextureHiderSampleProfileMarker.Begin();
#endif
                
                _asyncFogTextureReader.Update(FOW_RT);
                if (revealHidersWithTexture)
                    _asyncFogTextureReader.SeekHiders();

#if UNITY_EDITOR
                TextureHiderSampleProfileMarker.End();
#endif
            }
        }

        #endregion

        #region Dumb Unity Bug Workaround :)
#if UNITY_EDITOR
        //BASICALLY, every time an asset is updated in the project folder, materials are losing the compute buffer data. 
        //So, im hooking onto asset post processing, and re-initializing the material with the necessary data
        public void ReInitializeFOW()
        {
            StartCoroutine(FixFowDebug());
        }

        IEnumerator FixFowDebug()
        {
            yield return new WaitForEndOfFrame();
            enabled = false;
            enabled = true;
        }
#endif
        #endregion

        #region Initialization/Cleanup

        void Cleanup()
        {
            IsInPhasedUpdate = false;
            PendingRevealerDeregister.Clear();
            PendingHiderDeregister.Clear();

            if (FogOfWarMaterial != null)
                Destroy(FogOfWarMaterial);
            if (FowTextureMaterial != null)
                Destroy(FowTextureMaterial);
            SetFowEffectStrength(0);

            for (int i = NumActiveRevealers - 1; i >= 0; i--)
            {
                FogOfWarRevealer revealer = ActiveRevealers[i];
                revealer.DeregisterRevealer();
                PendingRevealerRegister.Add(revealer);
            }

            for (int i = NumActiveHiders - 1; i >= 0; i--)
            {
                FogOfWarHider hider = ActiveHiders[i];
                hider.DeregisterHider();
                PendingHiderRegister.Add(hider);
            }

            ActiveRevealerIndicesBuffer?.Dispose();
            RevealerInfoBuffer?.Dispose();
            RevealerDataBuffer?.Dispose();
            AnglesBuffer?.Dispose();
            ActiveRevealerIndicesBuffer = null;
            RevealerInfoBuffer = null;
            RevealerDataBuffer = null;
            AnglesBuffer = null;

            DeallocateStagedBuffers();

            if (FOW_TEMP_RT != null)
            {
                FOW_TEMP_RT.Release();
                Destroy(FOW_TEMP_RT);
                FOW_TEMP_RT = null;
            }

            if (FOW_RT != null)
            {
                FOW_RT.Release();
                Destroy(FOW_RT);
                FOW_RT = null;
            }

            _asyncFogTextureReader?.Dispose();
            _asyncFogTextureReader = null;
            RaycastRevealer.CleanupIterationPool();
            instance = null;
            SparseRevealerGrid.Cleanup();
        }

        public void Initialize()
        {
            if (instance != null)
                return;

            instance = this;
            RaycastRevealer.InitializeIterationPool();

            SetFowEffectStrength(1);

            if (!is2D)
                FogOfWarRevealer3D.Projection = new PlaneProjection(GamePlaneOrientation);
            else
                FogOfWarRevealer3D.Projection = new PlaneProjection(GamePlane.XY);

            TotalMaximumSightSegments = MaxPossibleRevealers * MaxPossibleSegmentsPerRevealer;

            ActiveRevealers = new FogOfWarRevealer[MaxPossibleRevealers];
            UnsortedRevealers = new FogOfWarRevealer[MaxPossibleRevealers];
            ActiveHiders = new FogOfWarHider[MaxPossibleHiders];
            UnsortedHiders = new FogOfWarHider[MaxPossibleHiders];

            ActiveRevealerIndicesBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(int)), ComputeBufferType.Default);
            RevealerInfoBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(RevealerInfoStruct)), ComputeBufferType.Default);
            RevealerDataBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(RevealerDataStruct)), ComputeBufferType.Default);

            SightSegmentsUploadData = new GpuSightSegment[MaxPossibleSegmentsPerRevealer];
            AnglesBuffer = new ComputeBuffer(TotalMaximumSightSegments, Marshal.SizeOf(typeof(GpuSightSegment)), ComputeBufferType.Default);

            if (UseStagedGPUUploads)
                AllocateStagedBuffers();

            BindComputeBuffersToShader();
            InitializeFogProperties();
            
            if (UseMiniMap || FOWSamplingMode == FogSampleMode.Texture || FOWSamplingMode == FogSampleMode.Both)
            {
                if (FowTextureMaterial != null)
                    Destroy(FowTextureMaterial);
                FowTextureMaterial = new Material(Shader.Find("Hidden/FullScreen/FOW/FOW_RT"));
                InitFOWRT();
                UpdateFowTextureMaterialProperties();
            }

            if (FogOfWarMaterial != null)
                Destroy(FogOfWarMaterial);

            SetFogShader();
            SwitchSpatialAccelerationMode(UseSpatialAcceleration);
            SwitchHidersUseFogTextureMode(HidersUseFogTexture);
            //ToggleFogTextureAsyncReadbackToCpu(AsyncReadbackFogDataToCpu);
            UpdateAllShaderProperties();
            FowBoundsUpdated();

            DrainPending();
        }

        public void SwitchSpatialAccelerationMode(bool useSpatial)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (useSpatial && !SparseRevealerGrid.SpatialAccelerationActive)
            {
                SparseRevealerGrid.Initialize(NumSpatialHashBuckets, SpatialHashGridSize);
            }
            else if (!useSpatial && SparseRevealerGrid.SpatialAccelerationActive)
            {
                SparseRevealerGrid.Cleanup();
                Shader.SetKeyword(_spatialHashKw, false);
            }
        }

        public void ToggleStagedUploads(bool enabled)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            UseStagedGPUUploads = enabled;

            if (enabled)
            {
                AllocateStagedBuffers();
            }
            else
            {
                DeallocateStagedBuffers();
            }
        }

        void AllocateStagedBuffers()
        {
            if (_dirtyMetas == null || _dirtyMetas.Length != MaxPossibleRevealers)
            {
                _dirtyMetas = new DirtyRevealerMeta[MaxPossibleRevealers];
                _stagingRevealerData = new RevealerDataStruct[MaxPossibleRevealers];
                _stagingSegments = new GpuSightSegment[TotalMaximumSightSegments];
            }

            if (_dirtyMetaBuffer == null)
            {
                _dirtyMetaBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(DirtyRevealerMeta)), ComputeBufferType.Default);
                _stagingDataBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(RevealerDataStruct)), ComputeBufferType.Default);
                _stagingSegBuffer = new ComputeBuffer(TotalMaximumSightSegments, Marshal.SizeOf(typeof(GpuSightSegment)), ComputeBufferType.Default);
            }

            if (ScatterRevealersShader != null)
                _scatterKernel = ScatterRevealersShader.FindKernel("ScatterRevealers");
            else
                Debug.LogWarning("FogOfWarWorld: UseStagedGPUUploads is enabled but ScatterRevealersShader is not assigned. Staged uploads will fall back to legacy per-revealer SetData.");

            _dirtyCount = 0;
            _segmentWriteHead = 0;
        }

        void DeallocateStagedBuffers()
        {
            _dirtyMetaBuffer?.Dispose();
            _stagingDataBuffer?.Dispose();
            _stagingSegBuffer?.Dispose();
            _dirtyMetaBuffer = null;
            _stagingDataBuffer = null;
            _stagingSegBuffer = null;
        }

        public void SwitchHidersUseFogTextureMode(bool useFogTextureToSeeHiders)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            HidersUseFogTexture = useFogTextureToSeeHiders;

            revealerSeesHiders = FOWSamplingMode == FogSampleMode.Pixel_Perfect || !HidersUseFogTexture;

            if (useFogTextureToSeeHiders)
            {
                for (int i = 0; i < NumActiveRevealers; i++)
                    ActiveRevealers[i].HiderSeeker.ClearRevealedList();
            }
            else
            {
                if (_asyncFogTextureReader != null)
                    _asyncFogTextureReader.UnseeAllHiders();
            }

            //re-initialize or dispose the async texture readback
            ToggleFogTextureAsyncReadbackToCpu(AsyncReadbackFogDataToCpu);
        }

        public void ToggleFogTextureAsyncReadbackToCpu(bool useAsyncReadback)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            AsyncReadbackFogDataToCpu = useAsyncReadback;

            useAsyncReadback |= !revealerSeesHiders;

            //UsingFogAsyncReadback = useAsyncFeedback;

            if (!useAsyncReadback && _asyncFogTextureReader != null)
            {
                _asyncFogTextureReader.UnseeAllHiders();
                _asyncFogTextureReader.Dispose();
                _asyncFogTextureReader = null;
            }
            if (useAsyncReadback && _asyncFogTextureReader == null)
            {
                _asyncFogTextureReader = new AsyncFogTextureReader();
                UpdateHiderSeenThresholdForFogTexture();
            }
        }

        public void UpdateHiderSeenThresholdForFogTexture()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (_asyncFogTextureReader == null)
                return;
            _asyncFogTextureReader.HiderSeeingThreshold = 1 - HiderSeenThreshold;
        }

        public static float3 UpVector;
        public static float3 ForwardVector;
        public void SetFogShader()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            UsingSoftening = false;
            string shaderName = "Hidden/FullScreen/FOW";
            switch (FogAppearance)
            {
                case FogOfWarAppearance.Solid_Color: shaderName += "/SolidColor"; break;
                case FogOfWarAppearance.GrayScale: shaderName += "/GrayScale"; break;
                case FogOfWarAppearance.Blur: shaderName += "/Blur"; break;
                case FogOfWarAppearance.Texture_Sample: shaderName += "/TextureSample"; break;
                case FogOfWarAppearance.Outline: shaderName += "/Outline"; break;
                case FogOfWarAppearance.None: shaderName = "Hidden/BlitCopy"; break;
            }
            FogOfWarMaterial = new Material(Shader.Find(shaderName));

#if UNITY_2021_2_OR_NEWER
#else
            //this was required in unity 2020.3.28. when updating to 2020.3.48, its no longer required. not sure what version fixes it exactly.
            //FogOfWarMaterial.EnableKeyword("_VS_NORMAL");   //this is only for urp/texture sample fog mode
#endif
        }
        
        public void InitializeFogProperties()
        {
            Shader.SetKeyword(_is2DKw, is2D);
            if (!is2D)
            {
                switch (GamePlaneOrientation)
                {
                    case GamePlane.XZ:
                        Shader.SetGlobalInt(fowPlaneID, 1);
                        UpVector = Vector3.up;
                        break;
                    case GamePlane.XY:
                        Shader.SetGlobalInt(fowPlaneID, 2);
                        UpVector = -Vector3.forward;
                        break;
                    case GamePlane.ZY:
                        Shader.SetGlobalInt(fowPlaneID, 3);
                        UpVector = Vector3.right;
                        break;
                }
            }
            else
            {
                UpVector = -Vector3.forward;
                Shader.SetGlobalInt(fowPlaneID, 0);
            }
        }

        public void BindComputeBuffersToShader()
        {
            Shader.SetGlobalBuffer(activeRevealerIndicesID, ActiveRevealerIndicesBuffer);
            Shader.SetGlobalBuffer(revealerInfoID, RevealerInfoBuffer);
            Shader.SetGlobalBuffer(revealerDataID, RevealerDataBuffer);
            Shader.SetGlobalBuffer(sightSegmentBufferID, AnglesBuffer);
        }

        public void UpdateAllShaderProperties()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
            // if (material == null)   //fix for "Enter Playmode Options"
            //     return;
#endif
            UsingFowTexture = UseMiniMap || FOWSamplingMode == FogSampleMode.Texture || FOWSamplingMode == FogSampleMode.Both;

            Shader.SetKeyword(_hardKw, FogType == FogOfWarType.Hard);
            Shader.SetKeyword(_softKw, FogType == FogOfWarType.Soft);
            bool newUsingSoftening = FogType == FogOfWarType.Soft;

            if (UsingSoftening != newUsingSoftening)
            {
                UsingSoftening = newUsingSoftening;
                for (int i = 0; i < NumActiveRevealers; i++)
                {
                    ActiveRevealers[i].SetCachedRayDistance();
                }
            }

            Shader.SetKeyword(_bleedOnKw, AllowBleeding);

            Shader.SetGlobalColor(materialColorID, UnknownColor);

            Shader.SetGlobalFloat(extraRadiusID, SightExtraAmount);

            Shader.SetGlobalFloat(edgeSoftenDistanceID, EdgeSoftenDistance);
            Shader.SetGlobalFloat(maxDistanceID, MaxFogDistance);

            #region Pixellation

            Shader.SetKeyword(_pixelateKw, PixelateFog && !WorldSpacePixelate);

            Shader.SetGlobalInt(pixelateWSID, 0);
            if (PixelateFog && WorldSpacePixelate)
                Shader.SetGlobalInt(pixelateWSID, 1);

            if (PixelateFog)
                Shader.SetGlobalFloat(extraRadiusID, SightExtraAmount + (1f / PixelDensity));

            #endregion

            Shader.SetGlobalFloat(pixelDensityID, PixelDensity);
            Shader.SetGlobalVector(pixelOffsetID, PixelGridOffset);

            Shader.SetKeyword(_ditherOnKw, UseDithering);
            Shader.SetGlobalFloat(ditherSizeID, DitherSize);

            Shader.SetGlobalInt(invertEffectID, InvertFowEffect ? 1 : 0);

            switch (FogFade)
            {
                case FogOfWarFadeType.Linear:
                    Shader.SetGlobalInt(fadeTypeID, 0);
                    break;
                case FogOfWarFadeType.Exponential:
                    Shader.SetGlobalInt(fadeTypeID, 4);
                    Shader.SetGlobalFloat(fadePowerID, FogFadePower);
                    break;
                case FogOfWarFadeType.Smooth:
                    Shader.SetGlobalInt(fadeTypeID, 1);
                    break;
                case FogOfWarFadeType.Smoother:
                    Shader.SetGlobalInt(fadeTypeID, 2);
                    break;
                case FogOfWarFadeType.Smoothstep:
                    Shader.SetGlobalInt(fadeTypeID, 3);
                    break;
            }

            Shader.SetGlobalInt(blendMaxID, BlendType == FogOfWarBlendMode.Max ? 1 : 0);
            
            switch (FogAppearance)
            {
                case FogOfWarAppearance.Solid_Color:
                    break;
                case FogOfWarAppearance.GrayScale:
                    Shader.SetGlobalFloat(saturationStrengthID, SaturationStrength);
                    break;
                case FogOfWarAppearance.Blur:
                    Shader.SetGlobalFloat(blurStrengthID, BlurStrength);
                    Shader.SetGlobalFloat(blurPixelOffsetMinID, Screen.height * (BlurDistanceScreenPercentMin / 100));
                    Shader.SetGlobalFloat(blurPixelOffsetMaxID, Screen.height * (BlurDistanceScreenPercentMax / 100));
                    Shader.SetGlobalInt(blurSamplesID, BlurSamples);
                    Shader.SetGlobalFloat(blurPeriodID, (2 * Mathf.PI) / BlurSamples);    //TAU = 2 * PI
                    break;
                case FogOfWarAppearance.Texture_Sample:
                    Shader.SetGlobalTexture(fowTextureID, FogTexture);
                    Shader.SetGlobalInt(skipTriplanarID, 0);
                    if (!UseTriplanar)
                    {
                        Shader.SetGlobalInt(skipTriplanarID, 1);
                        Shader.SetGlobalVector(fowAxisID, (Vector3)UpVector);
                    }
                    Shader.SetGlobalVector(fowTilingID, FogTextureTiling);
                    Shader.SetGlobalVector(fowSpeedID, FogScrollSpeed);
                    break;
                case FogOfWarAppearance.Outline:
                    Shader.SetGlobalFloat(lineThicknessID, OutlineThickness);
                    break;
            }


            Shader.SetKeyword(_sampleRealtimeKw, FOWSamplingMode == FogSampleMode.Pixel_Perfect || FOWSamplingMode == FogSampleMode.Both);

            bool sampleTexture = FOWSamplingMode == FogSampleMode.Texture || FOWSamplingMode == FogSampleMode.Both;
            Shader.SetKeyword(_sampleTextureKw, sampleTexture);
            Shader.SetKeyword(_useTextureBlurKw, sampleTexture && UseConstantBlur);
            if (sampleTexture)
            {
                Shader.SetGlobalTexture(fowRTID, FOW_RT);
                if (UseConstantBlur)
                {
                    Shader.SetGlobalFloat(sampleBlurQualityID, ConstantTextureBlurQuality);
                    Shader.SetGlobalFloat(sampleBlurAmountID, ConstantTextureBlurAmount);
                }
            }

            Shader.SetKeyword(_useWorldBoundsKw, UseRegrow);

            Shader.SetGlobalFloat(worldBoundsInfluenceID, 0);
            if (UseWorldBounds)
            {
                Shader.SetGlobalFloat(worldBoundsSoftenDistanceID, WorldBoundsSoftenDistance);
                Shader.SetGlobalFloat(worldBoundsInfluenceID, WorldBoundsInfluence);
            }
        }
        
        public void UpdateFowTextureMaterialProperties()
        {
            FowTextureMaterial.SetFloat(fowRTFadeOutSpeedID, RevealerFadeOut ? RevealerFadeOutSpeed : 9999999);
            FowTextureMaterial.SetFloat(fowRTFadeInSpeedID, RevealerFadeIn ? RevealerFadeInSpeed : 9999999);
            FowTextureMaterial.SetFloat(fowRTMaxRegrowAmountID, MaxFogRegrowAmount);
            if (UseRegrow)
                FowTextureMaterial.EnableKeyword("FOW_USE_REGROW");
            else
                FowTextureMaterial.DisableKeyword("FOW_USE_REGROW");
        }

        public const RenderTextureFormat renderTextureFormat = RenderTextureFormat.RHalf;
        public const TextureFormat saveTextureFormat = TextureFormat.RHalf;
        public void InitFOWRT()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            var tmp = RenderTexture.active;
            
            FOW_RT = new RenderTexture(FowResX, FowResY, 0, renderTextureFormat, RenderTextureReadWrite.Linear);
            //Debug.Log(FOW_RT.filterMode);
            //Debug.Log(FOW_RT.antiAliasing);
            //Debug.Log(FOW_RT.anisoLevel);
            //FOW_RT.antiAliasing = FowTextureMsaa;
            //FOW_RT.filterMode = FilterMode.Trilinear;
            //FOW_RT.anisoLevel = 9;

            FOW_RT.filterMode = FilterMode.Bilinear;
            FOW_RT.anisoLevel = 1;
            FOW_RT.useMipMap = false;
            FOW_RT.antiAliasing = 1;
            FOW_RT.Create();
            RenderTexture.active = FOW_RT;
            //GL.Begin(GL.TRIANGLES);
            GL.Clear(true, true, new Color(1 - InitialFogExplorationValue, 0, 0, 1 - InitialFogExplorationValue));

            if (UseRegrow)
            {
                FOW_TEMP_RT = new RenderTexture(FOW_RT);
                FOW_TEMP_RT.Create();
            }

            RenderTexture.active = tmp;
        }

        public RenderTexture GetFOWRT()
        {
            return FOW_RT;
        }

        public void SetFowTextureFilterMode(FilterMode filterMode)
        {
            if (FOW_RT == null)
            {
                return;
            }

            FOW_RT.filterMode = filterMode;
        }

        public void SetCombatTabletopGroundY(float groundYWorld)
        {
            Shader.SetGlobalFloat(CombatTabletopGroundYId, groundYWorld);
        }

        public void DisableCombatTabletopGroundY()
        {
            Shader.SetGlobalFloat(CombatTabletopGroundYId, -99999f);
        }

        public void ClearFowTexture()
        {
            var tmp = RenderTexture.active;

            RenderTexture.active = FOW_RT;
            GL.Begin(GL.TRIANGLES);
            GL.Clear(true, true, new Color(0, 0, 0, 1 - InitialFogExplorationValue));
            GL.End();
            if (FOW_TEMP_RT != null)
            {
                RenderTexture.active = FOW_TEMP_RT;
                GL.Begin(GL.TRIANGLES);
                GL.Clear(true, true, new Color(0, 0, 0, 1 - InitialFogExplorationValue));
                GL.End();
            }

            RenderTexture.active = tmp;
        }

        /// <summary>
        /// Creates the fog persistence render texture when switching to texture storage after Initialize().
        /// </summary>
        public void EnsureTextureStorageReady()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }
#endif
            var needsTexture = UseMiniMap
                || FOWSamplingMode == FogSampleMode.Texture
                || FOWSamplingMode == FogSampleMode.Both;
            if (!needsTexture)
            {
                return;
            }

            if (FowTextureMaterial == null)
            {
                FowTextureMaterial = new Material(Shader.Find("Hidden/FullScreen/FOW/FOW_RT"));
            }

            if (GetFOWRT() == null)
            {
                InitFOWRT();
            }

            UpdateFowTextureMaterialProperties();
        }

        #endregion

        #region All Bounds Stuff

        public void UpdateWorldBounds(Vector3 center, Vector3 extent)
        {
            WorldBounds.center = center;
            WorldBounds.extents = extent;
            FowBoundsUpdated();
        }

        public void UpdateWorldBounds(Bounds newBounds)
        {
            WorldBounds = newBounds;
            FowBoundsUpdated();
        }

        public static Vector4 CachedFowShaderBounds;
        private void FowBoundsUpdated()
        {
            CachedFowShaderBounds = GetBoundsVectorForShader();
            SetFowShaderBounds();
        }

        private void SetFowShaderBounds()
        {
            Shader.SetGlobalVector(worldBoundsID, CachedFowShaderBounds);
        }

        public Vector4 GetBoundsVectorForShader()
        {
            if (is2D)
                return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.y, WorldBounds.center.y);

            switch(GamePlaneOrientation)
            {
                case GamePlane.XZ: return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.z, WorldBounds.center.z);
                case GamePlane.XY: return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.y, WorldBounds.center.y);
                case GamePlane.ZY: return new Vector4(WorldBounds.size.z, WorldBounds.center.z, WorldBounds.size.z, WorldBounds.center.z);
            }

            return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.z, WorldBounds.center.z);
        }

        /// <summary>
        /// Gets the world position of the provided point on the FOW plane
        /// </summary>
        public Vector2 GetFowBoundsPositionFromWorldPosition(Vector3 WorldPosition)
        {
            if (is2D)
                return new Vector2(WorldPosition.x, WorldPosition.y);

            switch (GamePlaneOrientation)
            {
                case GamePlane.XZ: return new Vector2(WorldPosition.x, WorldPosition.z);
                case GamePlane.XY: return new Vector2(WorldPosition.x, WorldPosition.y);
                case GamePlane.ZY: return new Vector2(WorldPosition.z, WorldPosition.y);
            }

            return new Vector2(WorldPosition.x, WorldPosition.z);
        }

        #endregion

        #region Revealer Tracking

        void SetNumRevealers()
        {
            Shader.SetGlobalInt(numRevealersID, NumActiveRevealers);
        }
        
        public int RegisterRevealer(FogOfWarRevealer newRevealer)
        {
#if UNITY_EDITOR
            //RegisterRevealersProfileMarker.Begin();
#endif
            int emptySlotID = NumActiveRevealers;

            NumActiveRevealers++;
            if (!newRevealer.CurrentlyStaticRevealer)
            {
                numDynamicRevealers++;
                ActiveRevealers[emptySlotID] = newRevealer;
                newRevealer.RevealerArrayPosition = emptySlotID;
                AddDynamicRevealer(newRevealer);
            }
            else
            {
                ActiveRevealers[emptySlotID] = newRevealer;
                newRevealer.RevealerArrayPosition = emptySlotID;
            }
            SetNumRevealers();

            int newID = emptySlotID;
            if (DeregisteredRevealerIDs.Count > 0)
            {
                newID = DeregisteredRevealerIDs.Dequeue();
            }

            indiciesDataToSet[0] = newID;
            ActiveRevealerIndicesBuffer.SetData(indiciesDataToSet, 0, emptySlotID, 1);

            //_circleIndicesArray = indicesBuffer.BeginWrite<int>(numCircles - 1, 1);
            //_circleIndicesArray[0] = newID;

            //indicesBuffer.EndWrite<int>(1);

#if UNITY_EDITOR
            //RegisterRevealersProfileMarker.End();
#endif
            UnsortedRevealers[newID] = newRevealer;
            return newID;
        }

        public void DeRegisterRevealer(FogOfWarRevealer toRemove)
        {
#if UNITY_EDITOR
            //DeRegisterRevealersProfileMarker.Begin();
#endif
            int index = toRemove.RevealerArrayPosition;

            DeregisteredRevealerIDs.Enqueue(toRemove.RevealerGPUDataPosition);

            NumActiveRevealers--;
            if (!toRemove.CurrentlyStaticRevealer)
            {
                numDynamicRevealers--;
                RemoveDynamicRevealer(toRemove);
            }

            FogOfWarRevealer toSwap = ActiveRevealers[NumActiveRevealers]; //the last revealer in the buffer
            if (toSwap.RevealerArrayPosition != index) //put the LAST active revealer in this slot
            {
                //swap the array position
                ActiveRevealers[index] = toSwap;
                toSwap.RevealerArrayPosition = index;

                //update dynamic list entry if the swapped revealer is dynamic
                if (toSwap.DynamicListIndex >= 0)
                    DynamicRevealerIndices[toSwap.DynamicListIndex] = index;

                //notify the gpu about the swap
                indiciesDataToSet[0] = toSwap.RevealerGPUDataPosition;
                ActiveRevealerIndicesBuffer.SetData(indiciesDataToSet, 0, index, 1);
            }

            SetNumRevealers();

#if UNITY_EDITOR
            //DeRegisterRevealersProfileMarker.End();
#endif
        }

        public static void AddDynamicRevealer(FogOfWarRevealer revealer)
        {
            revealer.DynamicListIndex = DynamicRevealerIndices.Count;
            DynamicRevealerIndices.Add(revealer.RevealerArrayPosition);
        }

        public static void RemoveDynamicRevealer(FogOfWarRevealer revealer)
        {
            int idx = revealer.DynamicListIndex;
            if (idx < 0) return;

            int last = DynamicRevealerIndices.Count - 1;
            if (idx != last)
            {
                DynamicRevealerIndices[idx] = DynamicRevealerIndices[last];
                ActiveRevealers[DynamicRevealerIndices[idx]].DynamicListIndex = idx;
            }
            DynamicRevealerIndices.RemoveAt(last);
            revealer.DynamicListIndex = -1;
        }

        public int RegisterHider(FogOfWarHider newHider)
        {
#if UNITY_EDITOR
            //RegisterHiderProfileMarker.Begin();
#endif
            int emptySlotID = NumActiveHiders;

            if (ActiveHiders.Length == emptySlotID)
            {
                Array.Resize(ref ActiveHiders, ActiveHiders.Length * 2);
                Array.Resize(ref UnsortedHiders, UnsortedHiders.Length * 2);
            }

            NumActiveHiders++;

            ActiveHiders[emptySlotID] = newHider;
            newHider.HiderArrayPosition = emptySlotID;

            int newID = emptySlotID;
            if (DeregisteredHiderIDs.Count > 0)
            {
                newID = DeregisteredHiderIDs.Dequeue();
            }

            UnsortedHiders[newID] = newHider;
#if UNITY_EDITOR
            //RegisterHiderProfileMarker.End();
#endif
            return newID;
        }

        public void DeRegisterHider(FogOfWarHider toRemove)
        {
#if UNITY_EDITOR
            //DeRegisterHiderProfileMarker.Begin();
#endif
            int index = toRemove.HiderArrayPosition;

            DeregisteredHiderIDs.Enqueue(toRemove.HiderPermanantID);

            NumActiveHiders--;

            FogOfWarHider toSwap = ActiveHiders[NumActiveHiders]; //the last hider in the buffer
            if (toSwap.HiderArrayPosition != index) //put the LAST active hider in this slot
            {
                //swap the array position
                ActiveHiders[index] = toSwap;
                toSwap.HiderArrayPosition = index;
            }

#if UNITY_EDITOR
            //DeRegisterHiderProfileMarker.End();
#endif
        }

        #endregion

        #region Shader Data Upload

        private RevealerInfoStruct[] _revealerInfoToSet = new RevealerInfoStruct[1];
        public void UpdateRevealerInfo(int id, RevealerInfoStruct info)
        {
            _revealerInfoToSet[0] = info;
            RevealerInfoBuffer.SetData(_revealerInfoToSet, 0, id, 1);
        }

        //private JobHandle setAnglesBuffersJobHandle;
        //private SetAnglesBuffersJob setAnglesBuffersJob;
        //private NativeArray<ConeEdgeStruct> AnglesNativeArray;    //was used when using computebuffer.beginwrite. will be used again when unity fixes a bug internally
        //private NativeArray<int> _circleIndicesArray;
        //private NativeArray<CircleStruct> _circleArray;
        //private NativeArray<ConeEdgeStruct> _angleArray;
        private GpuSightSegment[] SightSegmentsUploadData;
        private RevealerDataStruct[] _revealerDataToSet = new RevealerDataStruct[1];
        public void UpdateRevealerData(int gpuPositionId, in RevealerDataStruct data, int numHits, float2[] directions, float[] distances)
        {
            UpdateRevealerData(gpuPositionId, data, numHits, directions, distances, 0, null, null);
        }

        public void UpdateRevealerData(
            int gpuPositionId,
            in RevealerDataStruct data,
            int numBaselineHits,
            float2[] baselineDirections,
            float[] baselineDistances,
            int numTerrainClipHits,
            float2[] terrainClipDirections,
            float[] terrainClipDistances)
        {
#if UNITY_EDITOR
            UploadToGpuProfileMarker.Begin();
#endif

            if (UseStagedGPUUploads)
                UpdateRevealerDataCompute(
                    gpuPositionId,
                    data,
                    numBaselineHits,
                    baselineDirections,
                    baselineDistances,
                    numTerrainClipHits,
                    terrainClipDirections,
                    terrainClipDistances);
            else
                UpdateRevealerDataLegacy(
                    gpuPositionId,
                    data,
                    numBaselineHits,
                    baselineDirections,
                    baselineDistances,
                    numTerrainClipHits,
                    terrainClipDirections,
                    terrainClipDistances);

#if UNITY_EDITOR
            UploadToGpuProfileMarker.End();
#endif
        }

        void UpdateRevealerDataLegacy(
            int gpuPositionId,
            in RevealerDataStruct data,
            int numBaselineHits,
            float2[] baselineDirections,
            float[] baselineDistances,
            int numTerrainClipHits,
            float2[] terrainClipDirections,
            float[] terrainClipDistances)
        {
            _revealerDataToSet[0] = data;
            RevealerDataBuffer.SetData(_revealerDataToSet, 0, gpuPositionId, 1);

            var totalHits = numBaselineHits + numTerrainClipHits;
            if (totalHits == 0)
            {
                return;
            }

            if (totalHits > MaxPossibleSegmentsPerRevealer)
            {
                Debug.LogError($"the revealer is trying to register {totalHits} segments. this is more than was set by maxPossibleSegmentsPerRevealer");
                return;
            }

            var writeIndex = 0;
            for (var i = 0; i < numBaselineHits; i++)
            {
                ref var segment = ref SightSegmentsUploadData[writeIndex++];
                segment.direction = baselineDirections[i];
                segment.length = baselineDistances[i];
            }

            for (var i = 0; i < numTerrainClipHits; i++)
            {
                ref var segment = ref SightSegmentsUploadData[writeIndex++];
                segment.direction = terrainClipDirections[i];
                segment.length = terrainClipDistances[i];
            }

            AnglesBuffer.SetData(SightSegmentsUploadData, 0, gpuPositionId * MaxPossibleSegmentsPerRevealer, totalHits);
        }

        void UpdateRevealerDataCompute(
            int gpuPositionId,
            in RevealerDataStruct data,
            int numBaselineHits,
            float2[] baselineDirections,
            float[] baselineDistances,
            int numTerrainClipHits,
            float2[] terrainClipDirections,
            float[] terrainClipDistances)
        {
            var totalHits = numBaselineHits + numTerrainClipHits;
            if (totalHits > MaxPossibleSegmentsPerRevealer)
            {
                Debug.LogError($"the revealer is trying to register {totalHits} segments. this is more than was set by maxPossibleSegmentsPerRevealer");
                totalHits = MaxPossibleSegmentsPerRevealer;
                numTerrainClipHits = math.max(0, totalHits - numBaselineHits);
            }

            if (_dirtyCount >= _dirtyMetas.Length || _segmentWriteHead + totalHits > _stagingSegments.Length)
                FlushStagedRevealerData();

            var idx = _dirtyCount++;
            _dirtyMetas[idx] = new DirtyRevealerMeta
            {
                GpuId = gpuPositionId,
                StagingSegmentStart = _segmentWriteHead,
                NumSegments = totalHits
            };
            _stagingRevealerData[idx] = data;

            for (var i = 0; i < numBaselineHits; i++)
            {
                _stagingSegments[_segmentWriteHead++] = new GpuSightSegment
                {
                    direction = baselineDirections[i],
                    length = baselineDistances[i]
                };
            }

            for (var i = 0; i < numTerrainClipHits; i++)
            {
                _stagingSegments[_segmentWriteHead++] = new GpuSightSegment
                {
                    direction = terrainClipDirections[i],
                    length = terrainClipDistances[i]
                };
            }
        }

        public void FlushStagedRevealerData()
        {
            if (_dirtyCount == 0)
                return;

            if (ScatterRevealersShader == null)
            {
                FlushStagedRevealerDataLegacy();
                return;
            }
#if UNITY_EDITOR
            FlushGpuUploadsProfileMarker.Begin();
#endif
            _dirtyMetaBuffer.SetData(_dirtyMetas, 0, 0, _dirtyCount);
            _stagingDataBuffer.SetData(_stagingRevealerData, 0, 0, _dirtyCount);
            _stagingSegBuffer.SetData(_stagingSegments, 0, 0, _segmentWriteHead);

            ScatterRevealersShader.SetInt(dirtyCountID, _dirtyCount);
            ScatterRevealersShader.SetInt(maxSegmentsPerRevealerID, MaxPossibleSegmentsPerRevealer);

            ScatterRevealersShader.SetBuffer(_scatterKernel, dirtyMetaID, _dirtyMetaBuffer);
            ScatterRevealersShader.SetBuffer(_scatterKernel, stagingDataID, _stagingDataBuffer);
            ScatterRevealersShader.SetBuffer(_scatterKernel, stagingSegmentsID, _stagingSegBuffer);
            ScatterRevealersShader.SetBuffer(_scatterKernel, revealerDataID, RevealerDataBuffer);
            ScatterRevealersShader.SetBuffer(_scatterKernel, sightSegmentBufferID, AnglesBuffer);

            int threadGroups = (_dirtyCount + 63) / 64;
            ScatterRevealersShader.Dispatch(_scatterKernel, threadGroups, 1, 1);

            _dirtyCount = 0;
            _segmentWriteHead = 0;
#if UNITY_EDITOR
            FlushGpuUploadsProfileMarker.End();
#endif
        }

        void FlushStagedRevealerDataLegacy()
        {
#if UNITY_EDITOR
            FlushGpuUploadsProfileMarker.Begin();
#endif
            for (int i = 0; i < _dirtyCount; i++)
            {
                var meta = _dirtyMetas[i];
                _revealerDataToSet[0] = _stagingRevealerData[i];
                RevealerDataBuffer.SetData(_revealerDataToSet, 0, meta.GpuId, 1);

                if (meta.NumSegments > 0)
                    AnglesBuffer.SetData(_stagingSegments, meta.StagingSegmentStart, meta.GpuId * MaxPossibleSegmentsPerRevealer, meta.NumSegments);
            }

            _dirtyCount = 0;
            _segmentWriteHead = 0;
#if UNITY_EDITOR
            FlushGpuUploadsProfileMarker.End();
#endif
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct SetAnglesBuffersJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<GpuSightSegment> Angles;
            [WriteOnly]
            public NativeArray<GpuSightSegment> AnglesArray;

            public void Execute(int index)
            {
                AnglesArray[index] = Angles[index];
            }
        }

        public static void OnPreRenderFog()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            if (SparseRevealerGrid.SpatialAccelerationActive && instance.FOWSamplingMode == FogSampleMode.Pixel_Perfect)
                SparseRevealerGrid.FlattenAndUpload();
        }

        #endregion

        /// <summary>
        /// Set the Global strength of FOW shaders. Range: 0-1
        /// </summary>
        public static void SetFowEffectStrength(float strength)
        {
            Shader.SetGlobalFloat(FowEffectStrengthID, strength);
        }

        /// <summary>
        /// Translates world position to FOW texture UV
        /// </summary>
        public static float2 GetFowTextureUVFromWorldPosition(Vector3 WorldPosition)
        {
            var bounds = FogOfWarWorld.CachedFowShaderBounds;
            float2 Position = instance.GetFowBoundsPositionFromWorldPosition(WorldPosition);
            float2 uv = new Vector2((((Position.x - bounds.y) + (bounds.x / 2)) / bounds.x),
                 (((Position.y - bounds.w) + (bounds.z / 2)) / bounds.z));

            return uv;
        }

        /// <summary>
        /// Test if provided point is currently visible.
        /// </summary>
        public static bool SampleFogTextureAtPoint(Vector3 WorldPosition)
        {
            float color = SampleFogTextureColorAtPoint(WorldPosition);

            if (color > .5f)
                return true;

            return false;
        }

        /// <summary>
        /// Samples the fog texture opacity at the given world position
        /// </summary>
        public static float SampleFogTextureColorAtPoint(Vector3 WorldPosition)
        {
            Vector2 uv = GetFowTextureUVFromWorldPosition(WorldPosition);

            if (instance._asyncFogTextureReader != null && instance._asyncFogTextureReader.HasData && (instance.AsyncReadbackFogDataToCpu || NumActiveHiders != 0))
                return 1 - instance._asyncFogTextureReader.SampleAsyncData(uv);

            //Debug.Log("taking slow path");
            Color color = SamplePixelSlow(FOW_RT, uv);

            //white = see, black = not see
            return 1 - color.r;
        }

        static Texture2D sampleTex;
        private static Color SamplePixelSlow(RenderTexture rt, Vector2 uv)
        {
            if (rt == null) return Color.magenta;

            int x = Mathf.Clamp((int)(uv.x * rt.width), 0, rt.width - 1);
            int y = Mathf.Clamp((int)(uv.y * rt.height), 0, rt.height - 1);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            if (sampleTex == null)
                sampleTex = new Texture2D(1, 1, saveTextureFormat, false, true);
            sampleTex.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            sampleTex.Apply(false, false);

            Color c = sampleTex.GetPixel(0, 0);

            RenderTexture.active = previous;

            return c;
        }

        static Texture2D gridSampleTex;

        /// <summary>
        /// One GPU read for debug overlays. Fills row-major visibility (1 = lit, 0 = fogged).
        /// </summary>
        public static bool TrySampleFogTextureVisibilityGrid(
            Vector3 centerWorld,
            float halfExtentWorld,
            int resolution,
            float[] visibilityOut)
        {
            if (instance == null
                || FOW_RT == null
                || resolution < 2
                || halfExtentWorld <= 0f
                || visibilityOut == null
                || visibilityOut.Length < resolution * resolution)
            {
                return false;
            }

            var groundY = instance.WorldBounds.center.y;
            var minX = centerWorld.x - halfExtentWorld;
            var minZ = centerWorld.z - halfExtentWorld;
            var step = (halfExtentWorld * 2f) / (resolution - 1);

            if (instance._asyncFogTextureReader != null
                && instance._asyncFogTextureReader.HasData
                && (instance.AsyncReadbackFogDataToCpu || NumActiveHiders != 0))
            {
                for (var y = 0; y < resolution; y++)
                {
                    for (var x = 0; x < resolution; x++)
                    {
                        var world = new Vector3(minX + x * step, groundY, minZ + y * step);
                        var uv = GetFowTextureUVFromWorldPosition(world);
                        visibilityOut[(y * resolution) + x] =
                            1f - instance._asyncFogTextureReader.SampleAsyncData(uv);
                    }
                }

                return true;
            }

            var cornerA = new Vector3(minX, groundY, minZ);
            var cornerB = new Vector3(minX + ((resolution - 1) * step), groundY, minZ + ((resolution - 1) * step));
            var uvMin = GetFowTextureUVFromWorldPosition(cornerA);
            var uvMax = GetFowTextureUVFromWorldPosition(cornerB);
            var u0 = Mathf.Min(uvMin.x, uvMax.x);
            var u1 = Mathf.Max(uvMin.x, uvMax.x);
            var v0 = Mathf.Min(uvMin.y, uvMax.y);
            var v1 = Mathf.Max(uvMin.y, uvMax.y);

            var px0 = Mathf.Clamp(Mathf.FloorToInt(u0 * FOW_RT.width), 0, FOW_RT.width - 2);
            var py0 = Mathf.Clamp(Mathf.FloorToInt(v0 * FOW_RT.height), 0, FOW_RT.height - 2);
            var px1 = Mathf.Clamp(Mathf.CeilToInt(u1 * FOW_RT.width), px0 + 1, FOW_RT.width);
            var py1 = Mathf.Clamp(Mathf.CeilToInt(v1 * FOW_RT.height), py0 + 1, FOW_RT.height);
            var readWidth = px1 - px0;
            var readHeight = py1 - py0;

            var previous = RenderTexture.active;
            RenderTexture.active = FOW_RT;

            if (gridSampleTex == null
                || gridSampleTex.width != readWidth
                || gridSampleTex.height != readHeight)
            {
                if (gridSampleTex != null)
                {
                    Destroy(gridSampleTex);
                }

                gridSampleTex = new Texture2D(readWidth, readHeight, saveTextureFormat, false, true);
            }

            gridSampleTex.ReadPixels(new Rect(px0, py0, readWidth, readHeight), 0, 0, false);
            gridSampleTex.Apply(false, false);
            RenderTexture.active = previous;

            var pixels = gridSampleTex.GetPixels();
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var u = x / (float)(resolution - 1);
                    var v = y / (float)(resolution - 1);
                    var px = Mathf.Clamp(Mathf.RoundToInt(u * (readWidth - 1)), 0, readWidth - 1);
                    var py = Mathf.Clamp(Mathf.RoundToInt(v * (readHeight - 1)), 0, readHeight - 1);
                    visibilityOut[(y * resolution) + x] = 1f - pixels[(py * readWidth) + px].r;
                }
            }

            return true;
        }

        /// <summary>
        /// Test if provided point is currently visible.
        /// </summary>
        public static bool TestPointVisibility(Vector3 point)
        {
            if (instance.UseSpatialAcceleration)
            {
                Vector2 projectedPos = FogOfWarRevealer3D.Projection.Project(point);
                int hash = SparseRevealerGrid.GetCellHash(projectedPos);
                for (int i = 0; i < SparseRevealerGrid.RevealerBucketCounts[hash]; i++)
                {
                    if (UnsortedRevealers[SparseRevealerGrid.RevealerBuckets[hash][i]].TestPoint(point))
                        return true;
                }
            }
            else
            {
                for (int i = 0; i < NumActiveRevealers; i++)
                {
                    if (ActiveRevealers[i].TestPoint(point))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Change the fog appearence type at runtime
        /// </summary>
        public void SetFowAppearance(FogOfWarAppearance AppearanceMode)
        {
            FogAppearance = AppearanceMode;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            enabled = false;
            enabled = true;
        }

        public FogOfWarAppearance GetFowAppearance()
        {
            return FogAppearance;
        }

        /// <summary>
        /// Retuns a byte array that you can save to a file
        /// </summary>
        public byte[] GetFowTextureSaveData()
        {
            var tex = new Texture2D(FOW_RT.width, FOW_RT.height, saveTextureFormat, mipChain: false, linear: true);

            var tmp = RenderTexture.active;

            RenderTexture.active = FOW_RT;
            tex.ReadPixels(new Rect(0, 0, FOW_RT.width, FOW_RT.height), 0, 0, false);
            tex.Apply(false, false);

            RenderTexture.active = tmp;

            Destroy(tex);

            return ImageConversion.EncodeToPNG(tex);
        }

        /// <summary>
        /// Loads the FOW exploration data from a byte array created with GetFowTextureSaveData
        /// </summary>
        public void LoadFowTextureData(byte[] save)
        {
            ClearFowTexture();

            Texture2D temp = new Texture2D(1, 1, saveTextureFormat, mipChain: false, linear: true);
            temp.LoadImage(save);

            Graphics.Blit(temp, FOW_RT);
        }
    }

    //this class revealers hiders based off the FOW texture, instead of using revealers directly
    public sealed class AsyncFogTextureReader : IDisposable
    {
        public bool HasData;
        public HiderRevealer HiderSeeker;
        public float HiderSeeingThreshold = .5f;

        NativeArray<half> _front;
        NativeArray<half> _back;
        int _w, _h;
        bool _requestPending;
        AsyncGPUReadbackRequest _request;

        public AsyncFogTextureReader()
        {
            HiderSeeker = new HiderRevealer();
        }

        public void Update(RenderTexture rt)
        {
            if (rt == null) return;

            if (!_front.IsCreated || _w != rt.width || _h != rt.height)
            {
                if (_requestPending)
                {
                    _request.WaitForCompletion();
                    _requestPending = false;
                }
                Resize(rt.width, rt.height);
            }

            if (_requestPending && _request.done)
            {
                _requestPending = false;

                if (!_request.hasError)
                {
                    (_front, _back) = (_back, _front); //swap front and back
                    HasData = true;
                }
            }

            if (!_requestPending)
            {
                //_request = AsyncGPUReadback.RequestIntoNativeArray(ref _back, rt, 0);
                _request = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _back,
                    rt,
                    0,
                    FogOfWarWorld.saveTextureFormat);
                _requestPending = true;
            }
        }

        public void SeekHiders()
        {
            if (FogOfWarWorld.NumActiveHiders == 0)
                return;

            if (!HasData) return;

            for (int i = 0; i < FogOfWarWorld.NumActiveHiders; i++)
            {
                FogOfWarHider hider = FogOfWarWorld.ActiveHiders[i];
                bool seen = CanSeeHider(hider);
                HiderSeeker.ProcessSeen(hider, seen);
            }
        }

        bool CanSeeHider(FogOfWarHider hider)
        {
            var bounds = FogOfWarWorld.CachedFowShaderBounds;

            for (int i = 0; i < hider.SamplePoints.Length; i++)
            {
                float3 worldPos = hider.SamplePoints[i].position;
                float2 uv = FogOfWarWorld.GetFowTextureUVFromWorldPosition(worldPos);

                float sample = SampleAsyncData(uv);
                if (sample < HiderSeeingThreshold)
                    return true;
            }

            return false;
        }

        public half SampleAsyncData(float2 uv)
        {
            //uv.y = 1 - uv.y;

            int x = Mathf.Clamp((int)(uv.x * _w), 0, _w - 1);
            int y = Mathf.Clamp((int)(uv.y * _h), 0, _h - 1);

            return _front[y * _w + x];
        }

        void Resize(int w, int h)
        {
            _front.DisposeIfCreated();
            _back.DisposeIfCreated();

            _w = w;
            _h = h;
            int size = w * h;
            _front = new NativeArray<half>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _back = new NativeArray<half>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HasData = false;
        }

        public void UnseeAllHiders()
        {
            HiderSeeker.ClearRevealedList();
        }

        public void Dispose()
        {
            HiderSeeker?.ClearRevealedList();
            if (_requestPending)
            {
                _request.WaitForCompletion();
                _requestPending = false;
            }

            _front.DisposeIfCreated();
            _back.DisposeIfCreated();
            HasData = false;
        }
    }

    public static class SparseRevealerGrid
    {
        static readonly int gridRangesID = Shader.PropertyToID("_GridRanges");
        static readonly int revealerGridIdsID = Shader.PropertyToID("_RevealerGridIds");
        static readonly int tableSizeID = Shader.PropertyToID("_TableSize");
        static readonly int cellSizeID = Shader.PropertyToID("_CellSize");

        public static bool SpatialAccelerationActive = false;
        public static List<int>[] RevealerBuckets; //each bucket has a list of revealer ids
        public static int[] RevealerBucketCounts;  //cached bucket counts
        public static List<int>[] HiderBuckets;
        public static int[] HiderBucketCounts;

        public static bool Dirty;

        static HashSet<int> _activeBuckets = new HashSet<int>();
        static int _totalEntries = 0;
        private static HashSet<int> _tempHashes = new HashSet<int>();

        static int _tableSize = 512;
        static int _cellSize = 32;

        static ComputeBuffer _gridRangesBuffer;
        static ComputeBuffer _revealerGridIdsBuffer;

        static int2[] _ranges;
        static int[] _revealerGridIds;
        static int _maxGridIds;

        public static void Initialize(int tableSize, int cellSize)
        {
            SpatialAccelerationActive = true;
            _tableSize = tableSize;
            _cellSize = cellSize;

            RevealerBuckets = new List<int>[_tableSize];
            RevealerBucketCounts = new int[_tableSize];

            HiderBuckets = new List<int>[_tableSize];
            HiderBucketCounts = new int[_tableSize];
            
            for (int i = 0; i < _tableSize; i++)
            {
                RevealerBuckets[i] = new List<int>();
                HiderBuckets[i] = new List<int>();
            }

            _maxGridIds = FogOfWarWorld.instance.MaxPossibleRevealers * 4;  //it will automatically resize if needed
            _ranges = new int2[_tableSize];
            _revealerGridIds = new int[_maxGridIds];

            _gridRangesBuffer = new ComputeBuffer(_tableSize, sizeof(int) * 2);
            _revealerGridIdsBuffer = new ComputeBuffer(_maxGridIds, sizeof(int));

            _gridRangesBuffer.SetData(_ranges);

            BindPropertiesToShader();
        }

        public static void Cleanup()
        {
            SpatialAccelerationActive = false;

            if (FogOfWarWorld.ActiveRevealers != null)
            {
                for (int i = 0; i < FogOfWarWorld.NumActiveRevealers; i++)
                    FogOfWarWorld.ActiveRevealers[i].SpatialHashBuckets.Clear();
            }
            if (FogOfWarWorld.ActiveHiders != null)
            {
                for (int i = 0; i < FogOfWarWorld.NumActiveHiders; i++)
                    FogOfWarWorld.ActiveHiders[i].SpatialHashBuckets.Clear();
            }

            _gridRangesBuffer?.Dispose();
            _revealerGridIdsBuffer?.Dispose();
            _gridRangesBuffer = null;
            _revealerGridIdsBuffer = null;
            _totalEntries = 0;
            _activeBuckets.Clear();
        }

        public static int2 GetCell(float2 position)
        {
            return new int2(
                (int)math.floor(position.x / _cellSize),
                (int)math.floor(position.y / _cellSize)
            );
        }

        public static int GetCellHash(int2 cell)
        {
            uint h = (uint)cell.x * 73856093u ^ (uint)cell.y * 19349663u;
            return (int)(h % (uint)_tableSize);
        }

        public static int GetCellHash(float2 position)
        {
            int2 cell = GetCell(position);

            return GetCellHash(cell);
        }

        private static void SwapRemove(List<int> list, int item)    //order doesnt matter for revealer buckets and this is slightly faster than regular remove
        {
            int index = list.IndexOf(item);
            if (index < 0) return;

            list[index] = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
        }

        public static void UpdateRevealerBuckets(FogOfWarRevealer revealer, float2 position)
        {
            if (!SpatialAccelerationActive)
                return;

            //add revealer to new buckets based off revealer sight angle, radius, position, and facing direction (could also use first/last sight angle direction idk)
            int2 minCell = GetCell(position - revealer.TotalRevealerRadius);
            int2 maxCell = GetCell(position + revealer.TotalRevealerRadius);

            if (revealer.MinBucket.x == minCell.x && revealer.MinBucket.y == minCell.y &&
                revealer.MaxBucket.x == maxCell.x && revealer.MaxBucket.y == maxCell.y)
                return;

            revealer.MinBucket = minCell;
            revealer.MaxBucket = maxCell;

            for (int i = 0; i < revealer.SpatialHashBuckets.Count; i++)
            {
                int hash = revealer.SpatialHashBuckets[i];
                SwapRemove(RevealerBuckets[hash], revealer.RevealerGPUDataPosition);
                RevealerBucketCounts[hash]--;
                _totalEntries--;
                if (RevealerBucketCounts[hash] == 0)
                {
                    _activeBuckets.Remove(hash);
                    _ranges[hash] = default;
                }
            }
            revealer.SpatialHashBuckets.Clear();
            _tempHashes.Clear();

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    int hash = GetCellHash(new int2(x, y));

                    if (!_tempHashes.Add(hash))
                        continue;

                    if (RevealerBucketCounts[hash] == 0)
                        _activeBuckets.Add(hash);

                    RevealerBuckets[hash].Add(revealer.RevealerGPUDataPosition);
                    RevealerBucketCounts[hash]++;
                    _totalEntries++;
                    revealer.SpatialHashBuckets.Add(hash);
                }
            }
            Dirty = true;
        }

        public static void RemoveRevealer(FogOfWarRevealer revealer)
        {
            for (int i = 0; i < revealer.SpatialHashBuckets.Count; i++)
            {
                int hash = revealer.SpatialHashBuckets[i];
                SwapRemove(RevealerBuckets[hash], revealer.RevealerGPUDataPosition);
                RevealerBucketCounts[hash]--;
                if (RevealerBucketCounts[hash] == 0)
                {
                    _activeBuckets.Remove(hash);
                    _ranges[hash] = default;
                }
            }
            revealer.SpatialHashBuckets.Clear();
            Dirty = true;
        }

        public static void UpdatHiderBuckets(FogOfWarHider hider, float2 position)
        {
            if (!SpatialAccelerationActive)
                return;

            //add hider to new buckets based off his SAMPLE POSITIONS
            int2 minCell = GetCell(position - hider.MaxSamplePointLocalPosition);
            int2 maxCell = GetCell(position + hider.MaxSamplePointLocalPosition);

            if (hider.MinBucket.x == minCell.x && hider.MinBucket.y == minCell.y &&
                hider.MaxBucket.x == maxCell.x && hider.MaxBucket.y == maxCell.y)
                return;

            hider.MinBucket = minCell;
            hider.MaxBucket = maxCell;


            for (int i = 0; i < hider.SpatialHashBuckets.Count; i++)
            {
                int hash = hider.SpatialHashBuckets[i];
                SwapRemove(HiderBuckets[hash], hider.HiderPermanantID);
                HiderBucketCounts[hash]--;
                _totalEntries--;
            }
            hider.SpatialHashBuckets.Clear();
            _tempHashes.Clear();

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    int hash = GetCellHash(new int2(x, y));

                    if (!_tempHashes.Add(hash))
                        continue;

                    HiderBuckets[hash].Add(hider.HiderPermanantID);
                    HiderBucketCounts[hash]++;
                    _totalEntries++;
                    hider.SpatialHashBuckets.Add(hash);
                }
            }

            return;
        }

        public static void RemoveHider(FogOfWarHider hider)
        {
            for (int i = 0; i < hider.SpatialHashBuckets.Count; i++)
            {
                int hash = hider.SpatialHashBuckets[i];
                //HiderBuckets[hash].Remove(hider.HiderPermanantID);
                SwapRemove(HiderBuckets[hash], hider.HiderPermanantID);
                HiderBucketCounts[hash]--;
            }
            hider.SpatialHashBuckets.Clear();
        }

#if UNITY_EDITOR
        static readonly ProfilerMarker FlattenProfileMarker = new ProfilerMarker("Spatial Hash Flatten + Upload");
#endif
        public static void FlattenAndUpload()
        {
            if (!Dirty)
                return;

#if UNITY_EDITOR
            FlattenProfileMarker.Begin();
#endif
            Dirty = false;

            #region check if we need to resize the grid id buffer

            if (_totalEntries > _maxGridIds)
            {
                _maxGridIds = _totalEntries * 2;
                _revealerGridIds = new int[_maxGridIds];
                _revealerGridIdsBuffer?.Dispose();
                _revealerGridIdsBuffer = new ComputeBuffer(_maxGridIds, sizeof(int));

                BindPropertiesToShader();   //rebind after resizing
            }

            #endregion

            // Flatten — only iterate active buckets
            int writeIndex = 0;
            foreach (int i in _activeBuckets)
            {
                int count = RevealerBucketCounts[i];
                _ranges[i] = new int2(writeIndex, writeIndex + count);

                var bucket = RevealerBuckets[i];
                for (int j = 0; j < count; j++)
                    _revealerGridIds[writeIndex++] = bucket[j];
            }

            // Upload
            _gridRangesBuffer.SetData(_ranges);
            if (_totalEntries > 0)
                _revealerGridIdsBuffer.SetData(_revealerGridIds, 0, 0, _totalEntries);

#if UNITY_EDITOR
            FlattenProfileMarker.End();
#endif
        }

        private static void InsertionSort(List<int> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                int key = list[i];
                int j = i - 1;

                while (j >= 0 && list[j] > key)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
        }

        public static bool CheckIntersection(int2 minBucket1, int2 maxBucket1, int2 minBucket2, int2 maxBucket2)
        {
            return math.all(maxBucket1 >= minBucket2) & math.all(minBucket1 <= maxBucket2);
        }

        public static void BindPropertiesToShader()
        {
            if (!Application.isPlaying)
                return;

            Shader.SetKeyword(FogOfWarWorld._spatialHashKw, SpatialAccelerationActive);

            if (!SpatialAccelerationActive)
                return;

            Shader.SetGlobalBuffer(gridRangesID, _gridRangesBuffer);
            Shader.SetGlobalBuffer(revealerGridIdsID, _revealerGridIdsBuffer);
            Shader.SetGlobalInt(tableSizeID, _tableSize);
            Shader.SetGlobalFloat(cellSizeID, _cellSize);
        }
    }
}