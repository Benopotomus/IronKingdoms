using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;

namespace FOW
{
    public abstract class FogOfWarRevealer : MonoBehaviour
    {
        [Header("Vision Range Settings")]
        [FormerlySerializedAs("ViewRadius")]
        [SerializeField] protected float viewRadius = 15;

        [FormerlySerializedAs("SoftenDistance")]
        [SerializeField] protected float softenDistance = 3;
        [Range(0, 60)]
        [SerializeField] private float innerSoftenAngle = 5;

        [Space(2)]
        [FormerlySerializedAs("UnobscuredRadius")]
        [SerializeField] protected float unobscuredRadius = 1f;
        [FormerlySerializedAs("UnobscuredSoftenDistance")]
        [SerializeField] protected float unobscuredSoftenDistance = .25f;

        [Space(2)]
        [Tooltip("how high should this revealer see?")]
        [FormerlySerializedAs("VisionHeight")]
        [SerializeField] protected float visionHeight = 3;
        [FormerlySerializedAs("VisionHeightSoftenDistance")]
        [SerializeField] protected float visionHeightSoftenDistance = 1.5f;

        [Header("Customization Settings")]
        [Range(1f, 360)]
        [FormerlySerializedAs("ViewAngle")]
        [SerializeField] protected float viewAngle = 360;

        [Range(0, 1)]
        [FormerlySerializedAs("Opacity")]
        [SerializeField] protected float opacity = 1;

        [Tooltip("how high above this object should the sight be calculated from")]
        [SerializeField] public float EyeOffset = 0;
        [Tooltip("An offset used only in the shader, to determine how high above the revealer vision height should be calculated at")]
        [SerializeField] public float ShaderEyeOffset = 0;

        [Tooltip("Static revealers are revealers that need the sight function to be called manually, similar to the 'Called Elsewhere' option on FOW world. To change this at runtime, use the SetRevealerAsStatic(bool IsStatic) Method.")]
        [SerializeField] public bool StartRevealerAsStatic = false;
        
        [Header("Hider Settings")]
        [Tooltip("If a hider is in the softening zone, we may or may not want it to be revealed. for example, if we only want hiders to be found if they have at least 50% opacity, set this value to 0.5")]
        [Range(0, 1)]
        [FormerlySerializedAs("RevealHiderInFadeOutZonePercentage")]
        [SerializeField] protected float revealHiderInFadeOutZonePercentage = .5f;
        [SerializeField] protected int MaxHidersSampledPerFrame = 50;
        [Tooltip("Sets the hider ray origin at the hiders height")]
        [FormerlySerializedAs("CalculateHidersAtHiderHeight")]
        [SerializeField] public bool SetHiderRayOriginToHidersHeight = false;
        [Tooltip("Sets the hider ray destination at this revealers height")]
        [FormerlySerializedAs("SampleHidersAtRevealerHeight")]
        [SerializeField] public bool SetHiderRayDestinationToRevealersHeight = true;

        [Header("Occlusion Settings")]
        [Tooltip("Without occlusion, you can easilly have thousands of revealers with minimal performance cost")]
        [SerializeField] protected bool useOcclusion = true;

        [Tooltip("If you disable this, FOW will skip 'inside' edges of objects, allowing bleeding between the objects visible corners")]
        [FormerlySerializedAs("AddCorners")]
        [SerializeField] protected bool addCorners = true;

        #region Public Properties

        public float ViewRadius { get { return viewRadius; } set { viewRadius = value; RevealerValuesChanged(); } }
        public float SoftenDistance { get { return softenDistance; } set { softenDistance = value; RevealerValuesChanged(); } }

        public float InnerSoftenAngle { get { return innerSoftenAngle; } set { innerSoftenAngle = value; RevealerValuesChanged(); } }

        public float UnobscuredRadius { get { return unobscuredRadius; } set { unobscuredRadius = value; RevealerValuesChanged(); } }
        public float UnobscuredSoftenDistance { get { return unobscuredSoftenDistance; } set { unobscuredSoftenDistance = value; RevealerValuesChanged(); } }

        public float VisionHeight { get { return visionHeight; } set { visionHeight = value; RevealerValuesChanged(); } }
        public float VisionHeightSoftenDistance { get { return visionHeightSoftenDistance; } set { visionHeightSoftenDistance = value; RevealerValuesChanged(); } }

        public float ViewAngle { get { return viewAngle; } set { viewAngle = value; RevealerValuesChanged(); } }

        public float Opacity { get { return opacity; } set { opacity = value; RevealerValuesChanged(); } }

        public float RevealHiderInFadeOutZonePercentage { get { return revealHiderInFadeOutZonePercentage; } set { revealHiderInFadeOutZonePercentage = value; RevealerValuesChanged(); } }

        public bool UseOcclusion { get { return useOcclusion; } set { useOcclusion = value; RevealerValuesChanged(); } }
        public bool AddCorners { get { return addCorners; } set { addCorners = value; RevealerValuesChanged(); } }

        #endregion

        #region Profiler Markers

#if UNITY_EDITOR
        static readonly ProfilerMarker RevealingHidersMarker = new ProfilerMarker("Revealing Hiders");
#endif

        #endregion

        #region Runtime Variables

        /// <summary>
        /// When a hider becomes seen or unseen by this revealer, this will be invoked. First parameter is the hider in question, Second parameter is the hiders visibility.
        /// </summary>
        public event Action<FogOfWarHider, bool> OnHiderVisibilityChanged;
        [NonSerialized] public HiderRevealer HiderSeeker;

        [NonSerialized]
        public int RevealerArrayPosition;     //this revealers index in FogOfWarWorld.ActiveRevealers. it can change as revealers are added/removed
        [NonSerialized]
        public int RevealerGPUDataPosition;   //this revealers unchanging id, for gpu sight segment data. it will not change as long as the revealer is alive.

        [NonSerialized] public bool CurrentlyStaticRevealer = false;
        [NonSerialized] public int DynamicListIndex = -1;

        protected FogOfWarWorld.RevealerInfoStruct RevealerInfoStruct;
        protected FogOfWarWorld.RevealerDataStruct RevealerDataStruct;
        protected bool IsRegistered = false;
        
        [NonSerialized] public int NumberOfPoints;
        [NonSerialized] public float2[] OutputDirections;
        [NonSerialized] public float[] OutputDistances;

        [NonSerialized] public List<int> SpatialHashBuckets = new List<int>(capacity: 16);
        [NonSerialized] public int2 MinBucket = new int2(int.MinValue, int.MinValue);
        [NonSerialized] public int2 MaxBucket = new int2(int.MinValue, int.MinValue);
        
        protected Transform CachedTransform;

        protected float3 EyePosition;
        protected float2 RevealerPosition = new float2();
        protected float RevealerHeightPosition;

        #endregion

        #region Data Structures



        #endregion

        private void OnEnable()
        {
            RegisterRevealer();
        }

        private void OnDisable()
        {
            DeregisterRevealer();
        }

        private void OnDestroy()
        {
            OnHiderVisibilityChanged = null;
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
                return;
            if (!IsRegistered)
                return;
            RevealerValuesChanged();
        }

        protected abstract void SetupOnRegister();

        public void RegisterRevealer()
        {
            CachedTransform = transform;
            if (StartRevealerAsStatic)
                SetRevealerAsStatic(true);
            else
                SetRevealerAsStatic(false);     //fail-safe in case someone changes the value in debug mode

            NumberOfPoints = 0;
            var fow = FogOfWarWorld.instance;
            if (fow == null || fow.IsInPhasedUpdate)
            {
                if (!FogOfWarWorld.PendingRevealerDeregister.Remove(this))
                {
                    if (!FogOfWarWorld.PendingRevealerRegister.Contains(this))
                        FogOfWarWorld.PendingRevealerRegister.Add(this);
                }
                return;
            }
            if (IsRegistered)
            {
                Debug.Log("Tried to double register revealer");
                return;
            }
            if (HiderSeeker == null)
            {
                HiderSeeker = new HiderRevealer();
                HiderSeeker.OnHiderDeactivated += OnSeenHiderDeactivated;
            }

            int maxPossibleSegments = FogOfWarWorld.instance.MaxPossibleSegmentsPerRevealer;

            //Angles = new float[maxPossibleSegments];
            OutputDirections = new float2[maxPossibleSegments];
            OutputDistances = new float[maxPossibleSegments];

            MinBucket = new int2(int.MinValue, int.MinValue);
            MaxBucket = new int2(int.MinValue, int.MinValue);

            IsRegistered = true;
            RevealerGPUDataPosition = FogOfWarWorld.instance.RegisterRevealer(this);
            RevealerInfoStruct = new FogOfWarWorld.RevealerInfoStruct();
            RevealerDataStruct = new FogOfWarWorld.RevealerDataStruct();

            SetupOnRegister();

            RevealerValuesChanged();

            ManualCalculateLineOfSight();
        }

        public void DeregisterRevealer()
        {
            var fow = FogOfWarWorld.instance;
            if (fow == null || fow.IsInPhasedUpdate)
            {
                if (!FogOfWarWorld.PendingRevealerRegister.Remove(this))
                {
                    if (!FogOfWarWorld.PendingRevealerDeregister.Contains(this))
                        FogOfWarWorld.PendingRevealerDeregister.Add(this);
                }
                return;
            }
            if (!IsRegistered)
            {
                //Debug.Log("Tried to de-register revealer thats not registered");
                return;
            }

            HiderSeeker.ClearRevealedList();
            IsRegistered = false;
            FogOfWarWorld.instance.DeRegisterRevealer(this);
            SparseRevealerGrid.RemoveRevealer(this);

#if UNITY_EDITOR
            //just to help visualize debugging
            RevealerArrayPosition = -1;
            RevealerGPUDataPosition = -1;
#endif

            CleanupRevealer();
        }

        #region User methods

        /// <summary>
        /// Marks this revealer as static. prevents automatic recalculation of Line Of Sight.
        /// </summary>
        public void SetRevealerAsStatic(bool IsStatic)
        {
            if (IsRegistered)
            {
                if (CurrentlyStaticRevealer && !IsStatic)
                {
                    FogOfWarWorld.numDynamicRevealers++;
                    FogOfWarWorld.AddDynamicRevealer(this);
                }
                else if (!CurrentlyStaticRevealer && IsStatic)
                {
                    FogOfWarWorld.numDynamicRevealers--;
                    FogOfWarWorld.RemoveDynamicRevealer(this);
                }
            }

            CurrentlyStaticRevealer = IsStatic;
        }

        /// <summary>
        /// Manually calculate line of sight for this revealer.
        /// </summary>
        public void ManualCalculateLineOfSight()
        {
            LineOfSightPhase1();    //if possible, call phase 1 early in the frame, and phase 2 later in the frame!
            LineOfSightPhase2();
        }

        #endregion

        #region Hiders / point sampling

        public void RevealHiders()
        {
#if UNITY_EDITOR
            RevealingHidersMarker.Begin();
#endif
            //ForwardVectorCached = GetForward();
            //ForwardVectorProjectedCached = FogOfWarRevealer3D.Projection.Project(ForwardVectorCached);

            if (FogOfWarWorld.instance.UseSpatialAcceleration)
                ProcessHidersSpatialHash();
            else
                ProcessHidersLegacy();

#if UNITY_EDITOR
            RevealingHidersMarker.End();
#endif
        }

        protected abstract bool CanSeeHider(FogOfWarHider hider, float2 hiderPosition);

        protected int lastHiderIndex;
        void ProcessHidersLegacy()
        {
            FogOfWarHider hiderInQuestion;

            //foreach (FogOfWarHider hiderInQuestion in FogOfWarWorld.HidersList)
            for (int i = 0; i < math.min(MaxHidersSampledPerFrame, FogOfWarWorld.NumActiveHiders); i++)
            {
                lastHiderIndex = (lastHiderIndex + 1) % FogOfWarWorld.NumActiveHiders;
                hiderInQuestion = FogOfWarWorld.ActiveHiders[lastHiderIndex];

                bool seen = CanSeeHider(hiderInQuestion, FogOfWarRevealer3D.Projection.Project(hiderInQuestion.SamplePoints[0].position));
                if (HiderSeeker.ProcessSeen(hiderInQuestion, seen))
                    OnHiderVisibilityChanged?.Invoke(hiderInQuestion, seen);
            }
        }

        void ProcessHidersSpatialHash()
        {
            int seenCount = HiderSeeker.HidersSeen.Count;
            for (int i = 0; i < seenCount; i++)  //check if hiders grids no longer intersect with revealers grids
            {
                FogOfWarHider hiderToCheck = HiderSeeker.HidersSeen[i];
                if (!SparseRevealerGrid.CheckIntersection(hiderToCheck.MinBucket, hiderToCheck.MaxBucket, MinBucket, MaxBucket))
                {
                    HiderSeeker.ProcessSeen(hiderToCheck, false);
                    OnHiderVisibilityChanged?.Invoke(hiderToCheck, false);
                }
            }

            int bucketCount = SpatialHashBuckets.Count;
            for (int i = 0; i < bucketCount; i++)
            {
                int bucketIndex = SpatialHashBuckets[i];
                var bucket = SparseRevealerGrid.HiderBuckets[bucketIndex];
                int hiderCount = bucket.Count;
                for (int h = 0; h < hiderCount; h++)
                {
                    int index = bucket[h];
                    FogOfWarHider hiderInQuestion = FogOfWarWorld.UnsortedHiders[index];

                    bool seen = CanSeeHider(hiderInQuestion, hiderInQuestion.CachedPosition);
                    if (HiderSeeker.ProcessSeen(hiderInQuestion, seen))
                        OnHiderVisibilityChanged?.Invoke(hiderInQuestion, seen);
                }
            }
        }

        public void OnSeenHiderDeactivated(FogOfWarHider hider)
        {
            OnHiderVisibilityChanged?.Invoke(hider, false);
        }

        protected abstract bool _TestPoint(float3 point);
        public bool TestPoint(float3 point)
        {
            return _TestPoint(point);
        }

        #endregion

        //cached values
        protected bool CircleIsComplete;
        [NonSerialized] public float TotalRevealerRadius;   //includes view radius and soften radius
        protected float currentInnerSoftenAmount;

        //cached values (hider system)
        protected float hiderSightDist;
        protected float hiderSightDistSq;
        protected float unobscuredHiderSightDist;
        protected float unobscuredHiderSightDistSq;
        protected float hiderHeightSightDist;
        protected float halfViewAngle;
        protected float cosHalfViewAngle;

        public virtual void SetCachedRayDistance()
        {
            TotalRevealerRadius = viewRadius;
            currentInnerSoftenAmount = 0;
            if (FogOfWarWorld.UsingSoftening)
            {
                TotalRevealerRadius += softenDistance;
                currentInnerSoftenAmount = innerSoftenAngle;
            }
            
            RevealerDataStruct.RevealerTotalVisionRadius = TotalRevealerRadius;

            //cache common revealer values (hiders system)
            hiderSightDist = viewRadius;
            if (FogOfWarWorld.UsingSoftening)
                hiderSightDist += revealHiderInFadeOutZonePercentage * softenDistance;
            if (useOcclusion)
            {
                unobscuredHiderSightDist = math.abs(unobscuredRadius);
                if (FogOfWarWorld.UsingSoftening)
                    unobscuredHiderSightDist += revealHiderInFadeOutZonePercentage * unobscuredSoftenDistance;
            }
            else
                unobscuredHiderSightDist = hiderSightDist;
            unobscuredHiderSightDistSq = unobscuredHiderSightDist * unobscuredHiderSightDist;

            hiderSightDist = math.max(hiderSightDist, unobscuredHiderSightDist);
            hiderSightDistSq = hiderSightDist * hiderSightDist;

            hiderHeightSightDist = visionHeight;
            if (FogOfWarWorld.UsingSoftening)
                hiderHeightSightDist += revealHiderInFadeOutZonePercentage * visionHeightSoftenDistance;
        }

        protected virtual void RevealerValuesChanged()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (!IsRegistered)
                return;

            //cache common revealer values
            CircleIsComplete = Mathf.Approximately(viewAngle, 360);
            halfViewAngle = viewAngle / 2;
            SetCachedRayDistance();
            

            //cache common revealer values (hiders system)
            cosHalfViewAngle = math.cos(math.radians(halfViewAngle));

            //set revealer gpu data
            RevealerInfoStruct.StartIndex = RevealerGPUDataPosition * FogOfWarWorld.instance.MaxPossibleSegmentsPerRevealer;

            RevealerInfoStruct.RevealerVisionRadius = viewRadius;
            RevealerInfoStruct.RevealerVisionRadiusFade = softenDistance;

            RevealerInfoStruct.innerSoftenThreshold = math.sin(math.radians(innerSoftenAngle));
            RevealerInfoStruct.invInnerSoftenThreshold = 1 / RevealerInfoStruct.innerSoftenThreshold;

            RevealerInfoStruct.UnobscuredRadius = unobscuredRadius;
            RevealerInfoStruct.UnobscuredSoftenRadius = unobscuredSoftenDistance;

            RevealerInfoStruct.VisionHeight = visionHeight;
            RevealerInfoStruct.VisionHeightFade = visionHeightSoftenDistance;
            RevealerInfoStruct.Opacity = opacity;

            RevealerInfoStruct.UseOcclusion = (useOcclusion ? 1 : 0);

            FogOfWarWorld.instance.UpdateRevealerInfo(RevealerGPUDataPosition, RevealerInfoStruct);
        }

        //sends data to FogOfWarWorld to be uploaded to the shader
        protected void ApplyData()
        {
            RevealerDataStruct.RevealerPosition = RevealerPosition;
            RevealerDataStruct.RevealerHeight = RevealerHeightPosition + ShaderEyeOffset;
            RevealerDataStruct.NumSegments = NumberOfPoints;

            FogOfWarWorld.instance.UpdateRevealerData(RevealerGPUDataPosition, RevealerDataStruct, NumberOfPoints, OutputDirections, OutputDistances);
            SparseRevealerGrid.UpdateRevealerBuckets(this, RevealerPosition);
        }

        protected abstract void SetPositionAndHeight();
        protected abstract float GetEyeRotation();
        public abstract float3 GetEyePosition();
        protected abstract void SetCachedForward();
        public abstract float3 DirFromAngle(float angleInDegrees);
        protected abstract float AngleBetweenVector2(float3 _vec1, float3 _vec2);

        protected virtual void CleanupRevealer()
        {

        }

        protected abstract void IterationOne(float firstAngle, float angleStep);

        [NonSerialized] public float3 ForwardVectorCached;
        protected float2 ForwardVectorProjectedCached;

        //this does nothing so far. im gonna use it when i add logic to batch phase one.
        public static void PrePhaseOne()
        {

        }

        public virtual void LineOfSightPhase1()
        {
            EyePosition = GetEyePosition();
            SetPositionAndHeight();
            
            NumberOfPoints = 0;
        }

        //this does nothing so far. im gonna use it when i add logic to batch phase one.
        public static void PostPhaseOne()
        {

        }

        public virtual void LineOfSightPhase2()
        {

        }
    }

    [BurstCompile]
    internal static class FogMath2D
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSq(float2 a, float2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return dx * dx + dy * dy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SignedAngleDeg(float2 a, float2 b)
        {
            a = math.normalize(a);
            b = math.normalize(b);
            float s = a.x * b.y - a.y * b.x;
            float c = math.dot(a, b);
            return math.degrees(math.atan2(s, c));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 NormalRotate90(float2 v)
        {
            return new float2(-v.y, v.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Approximately(float a, float b)
        {
            return math.abs(b - a) < math.max(0.000001f * math.max(math.abs(a), math.abs(b)), math.EPSILON * 8f);
        }

        //next point prediction

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SinAngleC(float2 rotatedNormalUnit, float2 dirUnit, float sStep, float cStep)
        {
            float2 md = -dirUnit;

            float cosPhi = rotatedNormalUnit.x * md.x + rotatedNormalUnit.y * md.y;
            float sinPhi = rotatedNormalUnit.x * md.y - rotatedNormalUnit.y * md.x;

            return math.mad(sinPhi, cStep, cosPhi * sStep);
            //return sinPhi * cStep + cosPhi * sStep;
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
        public static void PredictNextPoint(in float2 point, in float2 normalUnit, in float2 dirUnit, float distance, float sStep, float cStep, out float2 result)
        {
            float2 rotatedNormalUnit = NormalRotate90(normalUnit);
            float sinAngleC = SinAngleC(rotatedNormalUnit, dirUnit, sStep, cStep);
            float nextDist = (distance * sStep) / sinAngleC;
            //result = point + rotatedNormalUnit * nextDist;
            result = math.mad(rotatedNormalUnit, nextDist, point);
        }

        //old method. keeping here for reference.
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static float2 PredictNextPointOldMethod(float2 point, float2 normalUnit, float2 dirUnit, float distance, float AngleStep)
        //{
        //    //rotated normal is parallel to the surface we hit
        //    float2 RotatedNormal = NormalRotate90(normalUnit);
        //    float AngleA = SignedAngleDeg(RotatedNormal, -dirUnit);
        //    float angleC = 180 - (AngleA + AngleStep);
        //    float nextDist = (distance * math.sin(math.radians(AngleStep))) / Mathf.Sin(math.radians(angleC));
        //    return point + (RotatedNormal * nextDist);
        //}

        [BurstCompile]
        public static void CheckIterateCondition(
            in float2 currentPoint,
            in float2 expectedPoint,
            in float2 currentNormal,
            in float2 previousNormal,
            bool currentHit,
            bool previousHit,
            float cosDouble,
            float edgeDstThresholdSq,
            bool addCorners,
            out bool shouldIterate)
        {
            const float signEps = 1e-8f;

            if (previousHit != currentHit)
            {
                shouldIterate = true;
                return;
            }

            bool distanceCondition = math.distancesq(currentPoint, expectedPoint) >= edgeDstThresholdSq;
            if (distanceCondition)
            {
                shouldIterate = true;
                return;
            }

            bool angleCondition = math.dot(currentNormal, previousNormal) < cosDouble;

            if (!addCorners && angleCondition)
            {
                float crossZ = currentNormal.x * previousNormal.y - currentNormal.y * previousNormal.x;
                bool positiveAngle = crossZ > signEps;
                shouldIterate = !positiveAngle;
                return;
            }

            shouldIterate = angleCondition;
        }

        [BurstCompile]
        public static void CheckEdgeMismatch(
            in float2 segmentPoint,
            in float2 segmentDirection,
            in float2 edgeNormal,
            float segmentRadius,
            float segmentAngle,
            bool segmentDidHit,
            float currentAngle,
            in float2 rayPoint,
            in float2 rayNormal,
            bool rayHit,
            float cosDouble,
            float edgeDstThresholdSq,
            out bool mismatch,
            out float2 nextPoint)
        {
            float delta = currentAngle - segmentAngle;
            float sDelta, cDelta;
            math.sincos(math.radians(delta), out sDelta, out cDelta);

            PredictNextPoint(segmentPoint, edgeNormal, segmentDirection, segmentRadius, sDelta, cDelta, out nextPoint);

            bool angleBad = math.dot(edgeNormal, rayNormal) < cosDouble;
            bool pointsFar = math.distancesq(nextPoint, rayPoint) >= edgeDstThresholdSq;

            mismatch = segmentDidHit != rayHit | angleBad | pointsFar;
        }
    }

    internal static class NativeContainerExtensions
    {
        public static void DisposeIfCreated<T>(this ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
        }
    }
}
