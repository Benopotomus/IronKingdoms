using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;
using System.Runtime.InteropServices;

#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

namespace FOW
{
    /// <summary>
    /// Handles line-of-sight calculations using raycasting.
    /// 
    /// Pipeline:
    /// 1. Phase1: Fire initial rays in parallel (batched RaycastCommands)
    /// 2. Phase1: Calculate expected next points + conditions (Burst job)
    /// 3. Phase2: Recursively refine edges where conditions trigger
    /// 4. Phase2: Binary search edge detection (FindEdges)
    /// 5. Upload final segments to GPU
    /// 
    /// Key concepts:
    /// - SightIteration: A set of rays at a particular refinement level
    /// - ViewPoints: Final output segments sent to shader
    /// - EdgeAngles/EdgeNormals: Data for binary search edge refinement
    /// </summary>
    public abstract class RaycastRevealer : FogOfWarRevealer
    {
        [SerializeField] protected LayerMask ObstacleMask;  //todo: re-setup jobs when this changes

        [FormerlySerializedAs("QualityPreset")]
        [SerializeField] public RaycastRevealerOcclusionQualityPreset OcclusionQuality = RaycastRevealerOcclusionQualityPreset.HighResolution;

        [FormerlySerializedAs("RaycastResolution")]
        [SerializeField] protected float raycastResolution = .5f;

        [Range(0, 10)]
        [SerializeField] public int NumExtraIterations = 4;

        [Range(1, 5)]
        [FormerlySerializedAs("NumExtraRaysOnIteration")]
        [SerializeField] protected int numExtraRaysOnIteration = 3;

        [Tooltip("Should this revealer find the edges of objects?.")]
        [SerializeField] public bool ResolveEdge = true;
        [Range(1, 30)]
        [Tooltip("Higher values will lead to more accurate edge detection, especially at higher distances. however, this will also result in more raycasts.")]
        [SerializeField] public int MaxEdgeResolveIterations = 10;

        //[Header("Technical Variables")]
        [Space(5)]
        [Range(.001f, 1)]
        [Tooltip("Lower values will lead to more accurate edge detection, especially at higher distances. however, this will also result in more raycasts.")]
        [SerializeField] protected float MaxAcceptableEdgeAngleDifference = .005f;
        [Range(.001f, 1)]
        [FormerlySerializedAs("EdgeDstThreshold")]
        [SerializeField] protected float edgeDstThreshold = 0.1f;
        //[SerializeField] protected float DoubleHitMaxDelta = 0.1f;
        [FormerlySerializedAs("DoubleHitMaxAngleDelta")]
        [SerializeField] protected float doubleHitMaxAngleDelta = 15;


        #region Public Properties

        public float RaycastResolution { get { return raycastResolution; } set { raycastResolution = value; RevealerValuesChanged(); } }
        public int NumExtraRaysOnIteration { get { return numExtraRaysOnIteration; } set { numExtraRaysOnIteration = value; RevealerValuesChanged(); } }
        public float EdgeDstThreshold { get { return edgeDstThreshold; } set { edgeDstThreshold = value; RevealerValuesChanged(); } }
        public float DoubleHitMaxAngleDelta { get { return doubleHitMaxAngleDelta; } set { doubleHitMaxAngleDelta = value; RevealerValuesChanged(); } }

        #endregion

        #region Debugging

#if UNITY_EDITOR

        [Header("Debugging")]
        [SerializeField] public bool DebugMode = false;
        [SerializeField] public bool DrawInitialRays = false;
        [SerializeField] protected int SegmentTest = -1;
        [SerializeField] public bool DrawExpectedNextPoints = false;
        [SerializeField] protected bool DrawIteritiveRays;
        [SerializeField] protected bool DrawEdgeResolveRays;

        [SerializeField] protected bool DrawExtraCastLines;
        [SerializeField] protected bool DrawHiderSamples;
        [SerializeField] protected bool DebugLogHiderBlockerName;
#endif

        #endregion

        #region Profiler Markers

#if UNITY_EDITOR

        static readonly ProfilerMarker LineOfSightMarker = new ProfilerMarker("Line Of Sight");
        static readonly ProfilerMarker IterationOneMarker = new ProfilerMarker("Iteration One");
        static readonly ProfilerMarker ConditionsJobMarker = new ProfilerMarker("Conditions Calculations");
        static readonly ProfilerMarker CompletePhaseOneMarker = new ProfilerMarker("Complete Phase One Work");
        static readonly ProfilerMarker SortingMarker = new ProfilerMarker("Sorting");
        static readonly ProfilerMarker EdgeDetectionMarker = new ProfilerMarker("Edge Detection");
        static readonly ProfilerMarker ApplyDataMarker = new ProfilerMarker("Apply Data");

        protected bool ProfileRevealers = false;

#endif

        #endregion

        #region Data Structures

        public enum RaycastRevealerOcclusionQualityPreset
        {
            Custom = 0,
            ExtraLargeScaleRTS = 1,
            LargeScaleRTS = 2,
            MediumScaleRTS = 3,
            SmallScaleRTS = 4,
            HighResolution = 5,
            OverkillResolution = 6,
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct SightRay
        {
            public float2 point;
            public float2 normal;
            public float2 direction;
            public float distance;
            public float angle;
            public bool hit;

            public void SetData(bool _hit, float2 _point, float _distance, float2 _normal, float2 _direction)
            {
                hit = _hit;
                point = _point;
                distance = _distance;
                normal = _normal;
                direction = _direction;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SightSegment
        {
            public float2 Point;
            public float2 Direction;

            public float Radius;
            public float Angle;
            public bool DidHit;

            public SightSegment(float rad, float ang, bool hit, float2 point, float2 dir)
            {
                Radius = rad;
                Angle = ang;
                DidHit = hit;
                Point = point;
                Direction = dir;
            }
        }

        private static SightIteration[] _iterationPool;
        private static int _sightIterationPoolIndex;
        private static SightIteration CreateNewExtraIteration()
        {
            SightIteration newInstance = new SightIteration();
            newInstance.InitializeStruct(7);    //5 max rays per extra iteration, plus 2 (edges)
            return newInstance;
        }

        static void DoublePoolSize()
        {
            int newSize = _iterationPool.Length * 2;
            var newPool = new SightIteration[newSize];

            for (int i = 0; i < _iterationPool.Length; i++)
            {
                newPool[i] = _iterationPool[i];
            }

            for (int i = _iterationPool.Length; i < newSize; i++)
            {
                newPool[i] = CreateNewExtraIteration();
            }

            _iterationPool = newPool;
            //Debug.Log("doubled");
        }

        private static SightIteration GetSubIteration()
        {
            if (_sightIterationPoolIndex >= _iterationPool.Length)
                DoublePoolSize();
            return _iterationPool[_sightIterationPoolIndex++];
            //if (SubIterations.Count > 0)
            //    return SubIterations.Pop();
            //SightIteration newInstance = CreateNewExtraIteration();
            //return newInstance;
        }

        public class SightIteration
        {
            //public float[] RayAngles;
            public NativeArray<float> RayAngles;
            public NativeArray<bool> Hits;
            public NativeArray<float> Distances;
            public NativeArray<float2> Points;
            public NativeArray<float2> Directions;
            public NativeArray<float2> Normals;

            public NativeArray<float2> NextPoints;

            public void InitializeStruct(int NumSteps)
            {
                //RayAngles = new float[NumSteps];
                RayAngles = new NativeArray<float>(NumSteps, Allocator.Persistent);
                Hits = new NativeArray<bool>(NumSteps, Allocator.Persistent);
                Distances = new NativeArray<float>(NumSteps, Allocator.Persistent);
                Points = new NativeArray<float2>(NumSteps, Allocator.Persistent);
                Directions = new NativeArray<float2>(NumSteps, Allocator.Persistent);
                Normals = new NativeArray<float2>(NumSteps, Allocator.Persistent);
                NextPoints = new NativeArray<float2>(NumSteps, Allocator.Persistent);
            }
            public void DisposeStruct()
            {
                RayAngles.Dispose();
                Distances.Dispose();
                Hits.Dispose();
                Points.Dispose();
                Directions.Dispose();
                Normals.Dispose();
                NextPoints.Dispose();
            }
        }

        #endregion

        #region Local Values

        [NonSerialized] public SightSegment[] ViewPoints;
        protected float[] EdgeAngles;
        protected float2[] EdgeNormals;

        #endregion

        public static void InitializeIterationPool()
        {
            if (_iterationPool != null)
            {
                CleanupIterationPool();
            }

            _iterationPool = new SightIteration[1000];
            for (int i = 0; i < _iterationPool.Length; i++)
                _iterationPool[i] = CreateNewExtraIteration();

            _sightIterationPoolIndex = 0;
        }

        public static void CleanupIterationPool()
        {
            if (_iterationPool == null) return;
            foreach (SightIteration s in _iterationPool)
            {
                if (s.RayAngles.IsCreated)
                    s.DisposeStruct();
            }
            _iterationPool = null;

            //foreach (SightIteration s in SubIterations)
            //    s.DisposeStruct();
            //SubIterations.Clear();
        }

        protected override void SetupOnRegister()
        {
            ViewPoints = new SightSegment[OutputDirections.Length];
            EdgeAngles = new float[OutputDirections.Length];
            EdgeNormals = new float2[OutputDirections.Length];
        }

        protected abstract void _CleanupRaycastRevealer();

        void CleanupRaycastRevealer()
        {
            if (Initialized)
            {
                PreReqJobHandle.Complete();
                FirstIterationPointsAndConditionsJobHandle.Complete();
            }
            FirstIterationPointsAndConditionsJobHandle.Complete();
            FirstIterationPointsAndConditionsJobHandle = default;

            Initialized = false;
            if (FirstIteration != null)
            {
                FirstIteration.DisposeStruct();
                FirstIteration = null;
            }

            FirstIterationConditions.DisposeIfCreated();
            _CleanupRaycastRevealer();
        }

        protected override void CleanupRevealer()
        {
            CleanupRaycastRevealer();
        }

        protected bool Initialized;

        protected SightIteration FirstIteration;

        protected int CommandsPerJob;
        public NativeArray<bool> FirstIterationConditions;
        protected JobHandle PreReqJobHandle;
        protected CalculateNextPointsAndAngleConditions FirstIterationPointsAndConditionsJob;
        protected JobHandle FirstIterationPointsAndConditionsJobHandle;

        protected int IterationRayCount;
        protected int PreviousFirstIterationStepCount;
        protected int FirstIterationStepCount;
        private float FirstIterationAngleStep;
        protected float FirstIterationAngleStepRadians, SinStep, CosStep;
        private float cosDouble;
        protected float edgeDstThresholdSq;

        private float invIterationRayCountMinusOne;
        private float[] iterationAngleSteps;
        private float[] iterationSinSteps;
        private float[] iterationCosSteps;

        protected abstract void _InitRevealer(int StepCount);
        void InitRaycastRevealer(int StepCount, float AngleStep)
        {
            //if (FirstIteration.Distances.IsCreated)
            if (FirstIteration != null)
                CleanupRaycastRevealer();
            //InitialPoints = new SightRay[StepCount];
            PreviousFirstIterationStepCount = StepCount;
            FirstIteration = new SightIteration();
            FirstIteration.InitializeStruct(StepCount);
            FirstIterationConditions = new NativeArray<bool>(StepCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            FirstIterationPointsAndConditionsJob = new CalculateNextPointsAndAngleConditions()
            {
                Distances = FirstIteration.Distances,
                Points = FirstIteration.Points,
                Normals = FirstIteration.Normals,
                Directions = FirstIteration.Directions,
                Hits = FirstIteration.Hits,
                ExpectedNextPoints = FirstIteration.NextPoints,
                IterateConditions = FirstIterationConditions
            };
            Initialized = true;
            _InitRevealer(StepCount);
        }

        public override void SetCachedRayDistance()
        {
            base.SetCachedRayDistance();
            MaxSegmentDeltaAngle = 180 - (2 * currentInnerSoftenAmount);
        }

        protected override void RevealerValuesChanged()
        {
            base.RevealerValuesChanged();

            raycastResolution = Mathf.Clamp(raycastResolution, 0.05f, 2f);
            FirstIterationStepCount = Mathf.Max(2, Mathf.CeilToInt(viewAngle * raycastResolution));
            FirstIterationAngleStep = viewAngle / (FirstIterationStepCount - 1);
            FirstIterationAngleStepRadians = math.radians(FirstIterationAngleStep);
            math.sincos(FirstIterationAngleStepRadians, out SinStep, out CosStep);
            cosDouble = math.cos(math.radians(doubleHitMaxAngleDelta));
            edgeDstThreshold = Mathf.Max(.001f, edgeDstThreshold);
            edgeDstThresholdSq = edgeDstThreshold * edgeDstThreshold;

            IterationRayCount = numExtraRaysOnIteration + 2;
            invIterationRayCountMinusOne = 1f / (IterationRayCount - 1);

            int maxIterations = NumExtraIterations + 1;
            if (iterationAngleSteps == null || iterationAngleSteps.Length < maxIterations)
            {
                iterationAngleSteps = new float[maxIterations];
                iterationSinSteps = new float[maxIterations];
                iterationCosSteps = new float[maxIterations];
            }

            iterationAngleSteps[0] = FirstIterationAngleStep;
            iterationSinSteps[0] = SinStep;
            iterationCosSteps[0] = CosStep;

            float currentAngleStep = FirstIterationAngleStep;
            for (int i = 1; i < maxIterations; i++)
            {
                currentAngleStep *= invIterationRayCountMinusOne;
                iterationAngleSteps[i] = currentAngleStep;

                float stepRad = math.radians(currentAngleStep);
                math.sincos(stepRad, out iterationSinSteps[i], out iterationCosSteps[i]);
            }

            //please dont look at this lol
            //CommandsPerJob = Mathf.Max(Mathf.CeilToInt(FirstIterationStepCount / Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount), 1);
            //CommandsPerJob = 32;
            //int workers = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;
            //CommandsPerJob = math.max(1, FirstIterationStepCount / math.max(1, workers * 2));
            //CommandsPerJob = math.clamp(CommandsPerJob, 16, 256);
            CommandsPerJob = ComputeBatchSize(FirstIterationStepCount);
            //float raysPerCore = (float)FirstIterationStepCount / Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;
            //CommandsPerJob = Mathf.CeilToInt(raysPerCore);

            if (!Initialized || FirstIteration == null || FirstIteration.RayAngles == null || PreviousFirstIterationStepCount != FirstIterationStepCount)
            {
                InitRaycastRevealer(FirstIterationStepCount, FirstIterationAngleStep);
            }

            //set jobs values
            FirstIterationPointsAndConditionsJob.SStep = SinStep;
            FirstIterationPointsAndConditionsJob.CStep = CosStep;
            FirstIterationPointsAndConditionsJob.CosDoubleHit = cosDouble;
            FirstIterationPointsAndConditionsJob.SignEps = 1e-8f;
            FirstIterationPointsAndConditionsJob.EdgeDstThresholdSq = edgeDstThresholdSq;
            FirstIterationPointsAndConditionsJob.AddCorners = addCorners;
        }

        private float lastAddedRayAngle;
#if UNITY_EDITOR
        private int numOverFlow;
#endif
        protected void AddViewPoint(bool hit, float distance, float angle, float step, float2 normal, float2 point, float2 dir)
        {
            //#if UNITY_EDITOR
            //            Profiler.BeginSample("Add View Point");
            //#endif

            if (NumberOfPoints == ViewPoints.Length)
            {
#if UNITY_EDITOR
                numOverFlow++;
#else
                Debug.LogError("Sight Segment buffer is full! Increase Maximum Segments per Revealer on Fog Of War World!");
#endif
                return;
            }

            int idx = NumberOfPoints++;
            ref var vp = ref ViewPoints[idx];
            vp.DidHit = hit;
            vp.Radius = distance;
            vp.Angle = angle;
            vp.Point = point;
            vp.Direction = dir;

            EdgeAngles[idx] = -step;
            EdgeNormals[idx] = normal;
            lastAddedRayAngle = angle;
            //#if UNITY_EDITOR
            //            Profiler.EndSample();
            //#endif
        }

        void SetData()
        {
#if UNITY_EDITOR
            if (DebugMode)
                UnityEngine.Random.InitState(1);
#endif

            //if (true)   
            //{
            //    //cull similar points
            //    float2 lastHitPoint = Vector2.one * 99999;
            //    float sqrMinDist = .01f * .01f;
            //    int numValidPoints = 0;
            //    for (int i = 0; i < NumberOfPoints; i++)
            //    {
            //        ref SightSegment segment = ref ViewPoints[i];
            //        if (i != NumberOfPoints - 1 && math.distancesq(segment.Point, lastHitPoint) < sqrMinDist)
            //            continue;
            //        lastHitPoint = segment.Point;
            //        //Angles[numValidPoints] = segment.Angle;
            //        Directions[numValidPoints] = segment.Direction;
            //        AreHits[numValidPoints] = segment.DidHit;
            //        Radii[numValidPoints] = segment.Radius;
            //        numValidPoints++;
            //    }
            //    NumberOfPoints = numValidPoints;
            //}
            //else
            {
                //send all points
                for (int i = 0; i < NumberOfPoints; i++)
                {
                    ref SightSegment segment = ref ViewPoints[i];
                    //Angles[i] = segment.Angle;
                    OutputDirections[i] = segment.Direction;
                    OutputDistances[i] = segment.Radius + math.select(1f, 0f, segment.DidHit);
                }
            }

#if UNITY_EDITOR
            if (numOverFlow > 0)
            {
                Debug.LogError($"Sight Segment buffer is full! (this revealer tried to write {numOverFlow + ViewPoints.Length} segments!) Increase Maximum Segments per Revealer on Fog Of War World! (Current max: {FogOfWarWorld.instance.MaxPossibleSegmentsPerRevealer})");
            }
#endif
            ApplyData();
        }

        protected static int ComputeBatchSize(int count)
        {
            int workers = math.max(1, Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount);
            int targetJobs = workers * 5;
            if (count <= 256) return count;
            int batch = count / math.max(1, targetJobs);
            batch = math.max(32, (batch + 31) & ~31);
            return math.min(256, batch);
        }

        void CalculateRevealerInitialValues()
        {
            SetCachedForward();
            FirstRayAngle = ((-GetEyeRotation() + 360 + 90) % 360) - halfViewAngle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected abstract void RayCast(float angle, ref SightRay ray);

        protected SightRay currentRay;
        protected float FirstRayAngle;
        private float MaxSegmentDeltaAngle;
        protected bool AnyHits;
        public override void LineOfSightPhase1()
        {
            base.LineOfSightPhase1();

#if UNITY_EDITOR
            numOverFlow = 0;
#endif
            if (!useOcclusion)
            {
                if (CircleIsComplete)   //special condition - full circle with no occlusion = FAST FOG
                {
                    SetData();
                    return;
                }

                CalculateRevealerInitialValues();
                float midAngle = FirstRayAngle + halfViewAngle;
                float lastAngle = FirstRayAngle + viewAngle;

                FirstRayAngle = math.radians(FirstRayAngle);
                ViewPoints[0].Direction = new float2(math.cos(FirstRayAngle), math.sin(FirstRayAngle));
                ViewPoints[0].DidHit = false;
                ViewPoints[0].Radius = TotalRevealerRadius;

                midAngle = math.radians(midAngle);
                ViewPoints[1].Direction = new float2(math.cos(midAngle), math.sin(midAngle));
                ViewPoints[1].DidHit = false;
                ViewPoints[1].Radius = TotalRevealerRadius;

                lastAngle = math.radians(lastAngle);
                ViewPoints[2].Direction = new float2(math.cos(lastAngle), math.sin(lastAngle));
                ViewPoints[2].DidHit = false;
                ViewPoints[2].Radius = TotalRevealerRadius;
                NumberOfPoints = 3;
                SetData();
                return;
            }

#if UNITY_EDITOR
            if (ProfileRevealers) LineOfSightMarker.Begin();
#endif
            CalculateRevealerInitialValues();

#if UNITY_EDITOR
            if (ProfileRevealers) IterationOneMarker.Begin();
#endif

            IterationOne(FirstRayAngle, FirstIterationAngleStep);

#if UNITY_EDITOR
            if (ProfileRevealers) ConditionsJobMarker.Begin();
#endif

            FirstIterationPointsAndConditionsJobHandle = FirstIterationPointsAndConditionsJob.ScheduleParallel(FirstIterationStepCount, CommandsPerJob, PreReqJobHandle);
            JobHandle.ScheduleBatchedJobs();

#if UNITY_EDITOR
            if (ProfileRevealers) ConditionsJobMarker.End();
            if (ProfileRevealers) IterationOneMarker.End();
            if (ProfileRevealers) LineOfSightMarker.End();
#endif
        }

        public override void LineOfSightPhase2()
        {
            if (!useOcclusion)  //the no occlusion path is 100% completed in phase 1
            {
                return;
            }

            //Debug.Log("PHASE 2");
#if UNITY_EDITOR
            if (ProfileRevealers) LineOfSightMarker.Begin();
            if (ProfileRevealers) CompletePhaseOneMarker.Begin();
#endif

            FirstIterationPointsAndConditionsJobHandle.Complete();

#if UNITY_EDITOR
            if (ProfileRevealers) CompletePhaseOneMarker.End();
            if (ProfileRevealers) SortingMarker.Begin();
#endif
            AddViewPoint(FirstIteration.Hits[0], FirstIteration.Distances[0], FirstIteration.RayAngles[0], 0, FirstIteration.Normals[0], FirstIteration.Points[0], FirstIteration.Directions[0]);

            //AddViewPoint(new ViewCastInfo(InitialPoints[0].hit, InitialPoints[0].point, InitialPoints[0].distance, InitialPoints[0].angle, Normals[0], InitialPoints[0].direction));
            //Debug.Log(Points[0]);
            //Debug.Log(NextPoints[0]);
            AnyHits = false;
            SortData(ref FirstIteration, FirstIterationAngleStep, FirstIterationStepCount, 0, true);
            _sightIterationPoolIndex = 0;

#if UNITY_EDITOR
            if (ProfileRevealers) SortingMarker.End();
#endif

            if (!AnyHits && CircleIsComplete)   //early out for shader, just draw circle
            {
                NumberOfPoints = 0;
                SetData();
#if UNITY_EDITOR
                if (ProfileRevealers) LineOfSightMarker.End();
#endif
                return;
            }


            int lastIndex = FirstIterationStepCount - 1;
            AddViewPoint(FirstIteration.Hits[lastIndex], FirstIteration.Distances[lastIndex], FirstIteration.RayAngles[lastIndex], 0, FirstIteration.Normals[lastIndex], FirstIteration.Points[lastIndex], FirstIteration.Directions[lastIndex]);

#if UNITY_EDITOR
            if (ProfileRevealers) EdgeDetectionMarker.Begin();
#endif

            if (ResolveEdge)
            {
                FindEdges();    //binary search to find the edge of the object
            }

#if UNITY_EDITOR
            if (ProfileRevealers) EdgeDetectionMarker.End();
            if (ProfileRevealers) ApplyDataMarker.Begin();
#endif
            SetData();

#if UNITY_EDITOR
            if (ProfileRevealers) ApplyDataMarker.End();
            if (ProfileRevealers) LineOfSightMarker.End();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckIterateCondition(
            float2 currentPoint,
            float2 expectedPoint,
            float2 currentNormal,
            float2 previousNormal,
            bool currentHit,
            bool previousHit)
        {
            const float signEps = 1e-8f;

            if (previousHit != currentHit)  //if one hit and one didnt, we know we want to iterate again
                return true;

            bool distanceCondition = math.distancesq(currentPoint, expectedPoint) > edgeDstThresholdSq;
            if (distanceCondition) //if the point is too far away from the expected next point, iterate again
                return true;

            bool angleCondition = math.dot(currentNormal, previousNormal) < cosDouble;  //if the angles are too far apart then iterate again

            if (!addCorners & angleCondition)
            {
                float crossZ = currentNormal.x * previousNormal.y - currentNormal.y * previousNormal.x;
                bool positiveAngle = crossZ > signEps;
                return !positiveAngle;    //return crossZ <= signEps;
            }

            return angleCondition;
        }

        void SortData(ref SightIteration iteration, float angleStep, int iterationSteps, int iterationNumber, bool isFirstIteration)
        {
            //Profiler.BeginSample($"-iteration {iterationNumber}");
            //const float signEps = 1e-8f;
            

            var hits = iteration.Hits;
            var distances = iteration.Distances;
            var points = iteration.Points;
            var nextPoints = iteration.NextPoints;
            var normals = iteration.Normals;
            var rayAngles = iteration.RayAngles;
            var dirs = iteration.Directions;

            bool isLastIteration = iterationNumber == NumExtraIterations;

            for (int i = 1; i < iterationSteps; i++)
            {
#if UNITY_EDITOR
                //if (DebugMode && DrawExpectedNextPoints && !isFirstIteration)
                if (DebugMode && DrawExpectedNextPoints)
                    Debug.DrawLine(Get3Dfrom2D(points[i]), Get3Dfrom2D(nextPoints[i]) + FogOfWarWorld.UpVector * (.03f / (iterationNumber + 1)), UnityEngine.Random.ColorHSV());
#endif

                #region calculate if we need to fire extra rays or not.

                bool addViewPoint;
                if (!isFirstIteration)
                {
                    bool hitCurr = hits[i];
                    bool hitPrev = hits[i - 1];

                    if (!(hitPrev | hitCurr))   //if none hit, then theres no extra work to do
                        continue;

                    //addViewPoint = CheckIterateCondition(points[i], nextPoints[i - 1], normals[i], normals[i - 1], hitCurr, hitPrev);
                    float2 currentPoint = points[i];
                    float2 expectedPoint = nextPoints[i - 1];
                    float2 currentNormal = normals[i];
                    float2 previousNormal = normals[i - 1];

                    FogMath2D.CheckIterateCondition(
                        in currentPoint,
                        in expectedPoint,
                        in currentNormal,
                        in previousNormal,
                        hitCurr,
                        hitPrev,
                        cosDouble,
                        edgeDstThresholdSq,
                        AddCorners,
                        out addViewPoint
                    );
                }
                else
                {
                    addViewPoint = FirstIterationConditions[i];

                    if (!addViewPoint && (rayAngles[i] + FirstIterationAngleStep) - lastAddedRayAngle >= MaxSegmentDeltaAngle) //segments > 180 degrees fail in the fog shader
                        AddViewPoint(hits[i], distances[i], rayAngles[i], angleStep, normals[i], points[i], dirs[i]);
                }


                #endregion

                AnyHits |= addViewPoint;
                if (!addViewPoint)
                    continue;

                int prevIdx = i - 1;
                if (isLastIteration)
                {
                    //bool isFirstPointOfFirstIteration = isFirstIteration && i == 1;
                    //if (!isFirstPointOfFirstIteration) //this ONLY prevents the first element from being added twice... perhaps could be optimized lol.. edit: not even needed?
                    AddViewPoint(hits[prevIdx], distances[prevIdx], rayAngles[prevIdx], -angleStep, normals[prevIdx], points[prevIdx], dirs[prevIdx]);
                    AddViewPoint(hits[i], distances[i], rayAngles[i], angleStep, normals[i], points[i], dirs[i]);
                }
                else
                {
                    int nextIterationNumber = iterationNumber + 1;
                    float newAngleStep = iterationAngleSteps[nextIterationNumber];

                    float initalAngle = rayAngles[prevIdx];

                    //Profiler.BeginSample("gather iteration");
                    SightIteration newIter = Iterate(nextIterationNumber, initalAngle, newAngleStep, ref iteration, i - 1);
                    //Profiler.EndSample();

                    SortData(ref newIter, newAngleStep, IterationRayCount, nextIterationNumber, false);
                }
            }
            //Profiler.EndSample();
        }

#if UNITY_EDITOR
        private bool ProfileExtraIterations = false;
#endif
        private SightIteration Iterate(int iterNumber, float initialAngle, float angleStep, ref SightIteration PreviousIteration, int PrevIterStartIndex)
        {
#if UNITY_EDITOR
            if (ProfileExtraIterations && ProfileRevealers)
                Profiler.BeginSample($"Iteration {iterNumber + 1}");
#endif
            SightIteration iter = GetSubIteration();
            //InUseIterations.Push(iter);
            //float step = angleStep / (IterationRayCount + 1);
            //step = angleStep;

            iter.RayAngles[0] = PreviousIteration.RayAngles[PrevIterStartIndex];
            iter.Hits[0] = PreviousIteration.Hits[PrevIterStartIndex];
            iter.Distances[0] = PreviousIteration.Distances[PrevIterStartIndex];
            iter.Points[0] = PreviousIteration.Points[PrevIterStartIndex];
            iter.Directions[0] = PreviousIteration.Directions[PrevIterStartIndex];
            iter.Normals[0] = PreviousIteration.Normals[PrevIterStartIndex];

            //float stepRad = math.radians(angleStep);
            //float sStep, cStep; math.sincos(stepRad, out sStep, out cStep);
            float sStep = iterationSinSteps[iterNumber];
            float cStep = iterationCosSteps[iterNumber];

            FogMath2D.PredictNextPoint(iter.Points[0], iter.Normals[0], iter.Directions[0], iter.Distances[0], sStep, cStep, out float2 res);
            iter.NextPoints[0] = res;

            //iter.NextPoints[0] = PreviousIteration.NextPoints[PrevIterStartIndex];

            int rayCountMinusOne = IterationRayCount - 1;
            float currentAngle = initialAngle + angleStep;
            for (int i = 1; i < rayCountMinusOne; i++)
            {
                RayCast(currentAngle, ref currentRay);
#if UNITY_EDITOR
                if (DebugMode && DrawIteritiveRays)
                {
                    Debug.DrawRay(EyePosition, DirFromAngle(currentAngle) * 10, Color.red);
                    //Debug.DrawRay(EyePosition, DirFromAngle(currentRay.angle, true) * 10, Color.red);
                }
#endif
                iter.RayAngles[i] = currentRay.angle;
                iter.Hits[i] = currentRay.hit;
                iter.Distances[i] = currentRay.distance;
                iter.Points[i] = currentRay.point;
                iter.Directions[i] = currentRay.direction;
                iter.Normals[i] = currentRay.normal;

                FogMath2D.PredictNextPoint(iter.Points[i], iter.Normals[i], iter.Directions[i], iter.Distances[i], sStep, cStep, out res);
                iter.NextPoints[i] = res;
                currentAngle += angleStep;
            }

            int lastIdx = PrevIterStartIndex + 1;
            iter.RayAngles[rayCountMinusOne] = PreviousIteration.RayAngles[lastIdx];
            iter.Hits[rayCountMinusOne] = PreviousIteration.Hits[lastIdx];
            iter.Distances[rayCountMinusOne] = PreviousIteration.Distances[lastIdx];
            iter.Points[rayCountMinusOne] = PreviousIteration.Points[lastIdx];
            iter.Directions[rayCountMinusOne] = PreviousIteration.Directions[lastIdx];
            iter.Normals[rayCountMinusOne] = PreviousIteration.Normals[lastIdx];
            iter.NextPoints[rayCountMinusOne] = PreviousIteration.NextPoints[lastIdx];

#if UNITY_EDITOR
            if (DebugMode && DrawIteritiveRays)
            {
                Debug.DrawRay(EyePosition, DirFromAngle(initialAngle + angleStep * 0) * 10, Color.red);
                Debug.DrawRay(EyePosition, DirFromAngle(initialAngle + angleStep * (IterationRayCount - 1)) * 10, Color.red);
                //Debug.DrawRay(EyePosition, DirFromAngle(currentRay.angle) * 10, Color.red);
            }

            if (ProfileExtraIterations && ProfileRevealers)
                Profiler.EndSample();
#endif
            return iter;
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
        public struct CalculateNextPointsAndAngleConditions : IJobFor
        {
            //used in next point calculations
            public float AngleStep;
            public float SStep, CStep;

            //used in condition calculations
            public float CosDoubleHit;
            public float SignEps;
            public float EdgeDstThresholdSq;
            public bool AddCorners;

            //[ReadOnly] public NativeArray<SightRay> rays;
            [ReadOnly] public NativeArray<float> Distances;
            [ReadOnly] public NativeArray<float2> Points;
            [ReadOnly] public NativeArray<float2> Normals;      //unit
            [ReadOnly] public NativeArray<float2> Directions;   //unit
            [ReadOnly][NativeDisableParallelForRestriction] public NativeArray<bool> Hits;

            [WriteOnly] public NativeArray<float2> ExpectedNextPoints;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<bool> IterateConditions;
            public void Execute(int id)
            {
                float2 point = Points[id];
                float2 normal = Normals[id];
                float2 dir = Directions[id];
                float dist = Distances[id];

                FogMath2D.PredictNextPoint(point, normal, dir, dist, SStep, CStep, out float2 nextPoint);
                ExpectedNextPoints[id] = nextPoint;

                if (id == 0) { IterateConditions[0] = false; return; }

                bool hitCurr = Hits[id];
                bool hitPrev = Hits[id - 1];

                if (!((hitPrev) | (hitCurr)))
                {
                    IterateConditions[id] = false;
                    return;
                }

                if (hitPrev != hitCurr)
                {
                    IterateConditions[id] = true;
                    return;
                }

                float2 prevPoint = Points[id - 1];
                float2 prevNormal = Normals[id - 1];
                float2 prevDir = Directions[id - 1];
                float prevDist = Distances[id - 1];

                FogMath2D.PredictNextPoint(prevPoint, prevNormal, prevDir, prevDist, SStep, CStep, out float2 expectedPointFromPrev);

                //bool distanceCondition = math.distancesq(Points[id], expectedPointFromPrev) > EdgeDstThresholdSq;
                float2 delta = point - expectedPointFromPrev;
                float distSq = math.dot(delta, delta);
                bool distanceCondition = distSq > EdgeDstThresholdSq;

                bool angleCondition = math.dot(normal, prevNormal) < CosDoubleHit;

                bool sample = distanceCondition | angleCondition;

                if (!AddCorners & angleCondition & !distanceCondition)
                {
                    float crossZ = normal.x * prevNormal.y - normal.y * prevNormal.x;
                    bool positiveAngle = crossZ > SignEps;
                    if (positiveAngle)
                        sample = false;
                }

                IterateConditions[id] = sample;
            }
        }

        private void FindEdges()
        {
            for (int i = 0; i < NumberOfPoints; i++)
            {
                ref SightSegment segment = ref ViewPoints[i];
                ref float2 edgeNormal = ref EdgeNormals[i];

                float currentAngle = segment.Angle;
                float angleAdd = EdgeAngles[i] * 0.5f;  //always the same length, only thing that changes is the sign. can probably optimize here.
                //Debug.Log(angleAdd);

                currentAngle += angleAdd;
                //for (int r = 0; r < MaxEdgeResolveIterations; r++)
                for (int r = 1; r < MaxEdgeResolveIterations - 1; r++)  //angleAdd for first and last are always 0, skip them
                {
                    if (math.abs(angleAdd) < MaxAcceptableEdgeAngleDifference)
                        break;

                    RayCast(currentAngle, ref currentRay);

                    //float delta = currentAngle - segment.Angle;
                    //float sDelta, cDelta; math.sincos(math.radians(delta), out sDelta, out cDelta);
                    //FogMath2D.PredictNextPoint(segment.Point, edgeNormal, segment.Direction, segment.Radius, sDelta, cDelta, out float2 nextPoint);

                    ////bool angleBad = Vector2.Angle(edgeNormal, currentRay.normal) > DoubleHitMaxAngleDelta;
                    //bool angleBad = math.dot(edgeNormal, currentRay.normal) < cosDouble;

                    //bool mismatch = segment.DidHit != currentRay.hit ||
                    //    angleBad ||
                    //    !PointsCloseEnough(nextPoint, currentRay.point);

                    FogMath2D.CheckEdgeMismatch(
                        in segment.Point,
                        in segment.Direction,
                        in edgeNormal,
                        segment.Radius,
                        segment.Angle,
                        segment.DidHit,
                        currentAngle,
                        in currentRay.point,
                        in currentRay.normal,
                        currentRay.hit,
                        cosDouble,
                        edgeDstThresholdSq,
                        out bool mismatch,
                        out float2 nextPoint
                    );

#if UNITY_EDITOR
                    //if (DebugMode && i == DEBUGEDGESLICE)
                    if (DebugMode && DrawEdgeResolveRays)
                    {
                        if (SegmentTest != -1 && SegmentTest == i)
                        {
                            Debug.DrawLine(Get3Dfrom2D(segment.Point), Get3Dfrom2D(nextPoint) + FogOfWarWorld.UpVector * .05f, UnityEngine.Random.ColorHSV());
                            Debug.DrawRay(EyePosition, DirFromAngle(currentAngle) * currentRay.distance, angleAdd >= 0 ? Color.green : Color.cyan);
                        }
                    }
#endif

                    float sign = mismatch ? -1f : 1f;

                    if (!mismatch)
                    {
                        sign = 1;
                        segment.Direction = currentRay.direction;
                        segment.Angle = currentAngle;
                        segment.Radius = currentRay.distance;
                        EdgeNormals[i] = currentRay.normal;
                        segment.Point = currentRay.point;
                    }

                    angleAdd *= 0.5f;
                    currentAngle += angleAdd * sign;
                }
            }
        }

        //used only for debug line drawing
        protected abstract float3 _Get3DPositionfrom2D(float2 twoD);
        float3 Get3Dfrom2D(float2 twoD)
        {
            return _Get3DPositionfrom2D(twoD);
        }
    }
}