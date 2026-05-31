using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Runtime.CompilerServices;
using Unity.Profiling;

#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

namespace FOW
{
	public class FogOfWarRevealer3D : RaycastRevealer
    {
		//public int rev3dProp;
		private NativeArray<RaycastCommand> RaycastCommandsNative;
		private NativeArray<RaycastHit> RaycastHits;
		//private NativeArray<float3> Vector3Directions;
		private JobHandle IterationOneRaycastJobHandle;
		private Phase1SetupJob SetupJob;
		private JobHandle SetupJobJobHandle;
		private GetVector2Data Vector2DataJob;
		private JobHandle Vector2NormalJobHandle;
		private PhysicsScene physicsScene;

		public static PlaneProjection Projection;
#if UNITY_2022_2_OR_NEWER
		public QueryParameters RayQueryParameters;
#endif

#if UNITY_EDITOR
        static readonly ProfilerMarker PartOneProfilerMarker = new ProfilerMarker("Part One");
        static readonly ProfilerMarker PartTwoProfilerMarker = new ProfilerMarker("Part Two");
        static readonly ProfilerMarker PartThreeProfilerMarker = new ProfilerMarker("Part Three");
#endif

        public readonly struct PlaneProjection
        {
            // Indices into float3: 0=x, 1=y, 2=z
            public readonly int Axis0;      // First 2D axis
            public readonly int Axis1;      // Second 2D axis  
            public readonly int HeightAxis; // The "up" axis

            public readonly float3 UpVector;

            public PlaneProjection(FogOfWarWorld.GamePlane plane)
            {
                switch (plane)
                {
                    case FogOfWarWorld.GamePlane.XZ:
                        Axis0 = 0; Axis1 = 2; HeightAxis = 1;
                        UpVector = new float3(0, 1, 0);
                        break;
                    case FogOfWarWorld.GamePlane.XY:
                        Axis0 = 0; Axis1 = 1; HeightAxis = 2;
                        UpVector = new float3(0, 0, 1);
                        break;
                    default: // ZY
                        Axis0 = 2; Axis1 = 1; HeightAxis = 0;
                        UpVector = new float3(1, 0, 0);
                        break;
                }
            }

            //[MethodImpl(MethodImplOptions.AggressiveInlining)]
            //public float2 Project(float3 v)
            //{
            //    // Explicit branches that JIT can optimize better
            //    return (Axis0, Axis1) switch
            //    {
            //        (0, 2) => new float2(v.x, v.z),
            //        (0, 1) => new float2(v.x, v.y),
            //        _ => new float2(v.z, v.y)
            //    };
            //}
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float2 Project(float3 v)
            {
                return new float2(v[Axis0], v[Axis1]);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float GetHeight(float3 v)
            {
                return v[HeightAxis];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float3 To3D(float2 v, float height)
            {
                float3 result = default;
                result[Axis0] = v.x;
                result[Axis1] = v.y;
                result[HeightAxis] = height;
                return result;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float3 DirectionFromAngle(float angleDeg)
            {
                float s, c;
                math.sincos(math.radians(angleDeg), out s, out c);
                float3 dir = default;
                dir[Axis0] = c;
                dir[Axis1] = s;
                dir[HeightAxis] = 0;
                return dir;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float DistanceSq2D(float3 a, float3 b)
            {
                float dx = a[Axis0] - b[Axis0];
                float dy = a[Axis1] - b[Axis1];
                return dx * dx + dy * dy;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Distance2D(float3 a, float3 b)
            {
                float dx = a[Axis0] - b[Axis0];
                float dy = a[Axis1] - b[Axis1];
                return math.sqrt(dx * dx + dy * dy);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float HeightDifference(float3 a, float3 b)
            {
                return math.abs(a[HeightAxis] - b[HeightAxis]);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float3 SetHeight(float3 v, float newHeight)
            {
                float3 result = v;
                result[HeightAxis] = newHeight;
                return result;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float GetRotationAngle(quaternion rot)
            {
                float3 euler = math.degrees(ToEuler(rot));
                return euler[HeightAxis];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float3 ToEuler(quaternion q)
            {
                float3 euler;

                // Roll (x)
                float sinr_cosp = 2f * (q.value.w * q.value.x + q.value.y * q.value.z);
                float cosr_cosp = 1f - 2f * (q.value.x * q.value.x + q.value.y * q.value.y);
                euler.x = math.atan2(sinr_cosp, cosr_cosp);

                // Pitch (y)
                float sinp = 2f * (q.value.w * q.value.y - q.value.z * q.value.x);
                euler.y = math.abs(sinp) >= 1f ? math.sign(sinp) * math.PI / 2f : math.asin(sinp);

                // Yaw (z)
                float siny_cosp = 2f * (q.value.w * q.value.z + q.value.x * q.value.y);
                float cosy_cosp = 1f - 2f * (q.value.y * q.value.y + q.value.z * q.value.z);
                euler.z = math.atan2(siny_cosp, cosy_cosp);

                return euler;
            }
        }

        protected override void _InitRevealer(int StepCount)
		{
            physicsScene = gameObject.scene.GetPhysicsScene();

            //if (RaycastCommands != null)
            //if (RaycastCommandsNative.IsCreated)
				//CleanupRevealer();

			//RaycastCommands = new RaycastCommand[StepCount];
			RaycastCommandsNative = new NativeArray<RaycastCommand>(StepCount, Allocator.Persistent);
			RaycastHits = new NativeArray<RaycastHit>(StepCount, Allocator.Persistent);
			//Vector3Directions = new NativeArray<float3>(StepCount, Allocator.Persistent);

#if UNITY_2022_2_OR_NEWER
			RayQueryParameters = new QueryParameters(ObstacleMask, false, QueryTriggerInteraction.UseGlobal, false);
#endif
			SetupJob = new Phase1SetupJob()
			{
				Proj = Projection,
				RayAngles = FirstIteration.RayAngles,
				//Vector3Directions = Vector3Directions,
				Vector2Directions = FirstIteration.Directions,
				RaycastCommandsNative = RaycastCommandsNative,
#if UNITY_2021_2_OR_NEWER
                PhysicsScene = physicsScene,
#endif
            };

            Vector2DataJob = new GetVector2Data()
			{
				Proj = Projection,
				RaycastHits = RaycastHits,
				Hits = FirstIteration.Hits,
				Distances = FirstIteration.Distances,

				RayDirections = FirstIteration.Directions,
				OutPoints = FirstIteration.Points,
				OutNormals = FirstIteration.Normals
			};
		}

		protected override void _CleanupRaycastRevealer()
        {
			//if (!RaycastCommandsNative.IsCreated)
			//	return;

            RaycastCommandsNative.DisposeIfCreated();
            RaycastHits.DisposeIfCreated();
			//Vector3Directions.Dispose();
		}
		
		protected override void IterationOne(float firstAngle, float angleStep)
        {
#if UNITY_EDITOR
            if (ProfileRevealers) PartOneProfilerMarker.Begin();	//if this is taking a super long time on some frames only, update unity!
#endif
			SetupJob.FirstAngle = firstAngle;
			SetupJob.AngleStep = angleStep;
			SetupJob.RayDistance = TotalRevealerRadius;
			SetupJob.EyePosition = EyePosition;
#if UNITY_2022_2_OR_NEWER
			RayQueryParameters.layerMask = ObstacleMask;
			SetupJob.Parameters = RayQueryParameters;
#else
			SetupJob.LayerMask = ObstacleMask;
#endif

			SetupJobJobHandle = SetupJob.ScheduleParallel(FirstIterationStepCount, CommandsPerJob, default(JobHandle));

#if UNITY_EDITOR
			if (DebugMode && DrawInitialRays)
			{
				SetupJobJobHandle.Complete();
				for (int i = 0; i < FirstIterationStepCount; i++)
				{
					//Debug.DrawRay(EyePosition, Vector3Directions[i] * RayDistance, Color.white);
                    float2 dir = FirstIteration.Directions[i];
                    float3 dir3D = default;
                    dir3D[Projection.Axis0] = dir.x;
                    dir3D[Projection.Axis1] = dir.y;
                    Debug.DrawRay(EyePosition, dir3D * TotalRevealerRadius, Color.white);
				}
			}
#endif

#if UNITY_EDITOR
            if (ProfileRevealers) PartOneProfilerMarker.End();
            if (ProfileRevealers) PartTwoProfilerMarker.Begin();
#endif
            //IterationOneJobHandle = RaycastCommand.ScheduleBatch(RaycastCommandsNative, RaycastHits, 64);
            //Debug.Log(CommandsPerJob);

            //float raysPerCore = (float)FirstIterationStepCount / Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;
            //int raycastBatchSize = Mathf.CeilToInt(raysPerCore);
            IterationOneRaycastJobHandle = RaycastCommand.ScheduleBatch(RaycastCommandsNative, RaycastHits, 90, SetupJobJobHandle);

#if UNITY_EDITOR
            if (ProfileRevealers) PartTwoProfilerMarker.End();
            if (ProfileRevealers) PartThreeProfilerMarker.Begin();
#endif
            //Vector2DataJob.RayDistance = ViewRadius;
            Vector2DataJob.RayDistance = TotalRevealerRadius;
            Vector2DataJob.ProjectedEyePosition = Projection.Project(EyePosition);
            //Vector2NormalJobHandle = Vector2DataJob.Schedule(FirstIterationStepCount, 32, IterationOneJobHandle);

            Vector2NormalJobHandle = Vector2DataJob.ScheduleParallel(FirstIterationStepCount, CommandsPerJob, IterationOneRaycastJobHandle);
            //Vector2NormalJobHandle.Complete();

            PreReqJobHandle = Vector2NormalJobHandle;
            //PointsJob.SStep = SinStep;
            //PointsJob.CStep = CosStep;
            //PointsJobHandle = PointsJob.Schedule(FirstIterationStepCount, CommandsPerJob, Vector2NormalJobHandle);
#if UNITY_EDITOR
            if (ProfileRevealers) PartThreeProfilerMarker.End();
#endif
        }

        //public new static void PostPhaseOne() //for when i batch all iteration 1
        //{

        //}

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
		struct Phase1SetupJob : IJobFor
        {
			public PlaneProjection Proj;

			public float FirstAngle;
			public float AngleStep;
			public float RayDistance;
			public float3 EyePosition;
#if UNITY_2022_2_OR_NEWER
			public QueryParameters Parameters;
#else
			public int LayerMask;
#endif

#if UNITY_2021_2_OR_NEWER
            public PhysicsScene PhysicsScene;
#endif

            [WriteOnly, NoAlias] public NativeArray<float> RayAngles;
			//[WriteOnly] public NativeArray<float3> Vector3Directions;
			[WriteOnly, NoAlias] public NativeArray<float2> Vector2Directions;
			[WriteOnly, NoAlias] public NativeArray<RaycastCommand> RaycastCommandsNative;
            
			public void Execute(int id)
            {
                //float angle = FirstAngle + (AngleStep * id);
                float angle = math.mad(AngleStep, id, FirstAngle);
                RayAngles[id] = angle;
                float3 dir = Proj.DirectionFromAngle(angle);
                //Vector3Directions[id] = dir;
                Vector2Directions[id] = Proj.Project(dir);

#if UNITY_2022_2_OR_NEWER
				RaycastCommandsNative[id] = new RaycastCommand(PhysicsScene, EyePosition, dir, Parameters, RayDistance);
#elif UNITY_2021_2_OR_NEWER
                RaycastCommandsNative[id] = new RaycastCommand(PhysicsScene, EyePosition, dir, RayDistance, layerMask: LayerMask, maxHits: 1);
#else
                RaycastCommandsNative[id] = new RaycastCommand(EyePosition, dir, RayDistance, layerMask: LayerMask);
#endif
            }
        }

		[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, OptimizeFor = OptimizeFor.Performance)]
		struct GetVector2Data : IJobFor
        {
            public PlaneProjection Proj;
            public float RayDistance;
			public float2 ProjectedEyePosition;
			[ReadOnly] public NativeArray<RaycastHit> RaycastHits;
			[ReadOnly] public NativeArray<float2> RayDirections;

            [WriteOnly, NoAlias] public NativeArray<bool> Hits;
            [WriteOnly, NoAlias] public NativeArray<float> Distances;
            [WriteOnly, NoAlias] public NativeArray<float2> OutPoints;
			[WriteOnly, NoAlias] public NativeArray<float2> OutNormals;
			public void Execute(int id)
			{
                float hitDist = RaycastHits[id].distance;
                bool hit = hitDist > 0f;

                float dist = math.select(RayDistance, hitDist, hit);
                float2 dir = RayDirections[id];
                float2 point = ProjectedEyePosition + dir * dist;

                // For normal: if hit, project and normalize; if miss, negate direction
                float2 projectedNormal = math.normalizesafe(Proj.Project(RaycastHits[id].normal));
                float2 hitNormal = math.select(-dir, projectedNormal, hit);

                Hits[id] = hit;
                Distances[id] = dist;
                OutPoints[id] = point;
                OutNormals[id] = hitNormal;
			}
		}

        RaycastHit RayHit;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected override void RayCast(float angle, ref SightRay ray)
        {
			float3 direction = DirFromAngle(angle);
			ray.angle = angle;
			ray.direction = GetVector2D(direction);
			if (physicsScene.Raycast(EyePosition, direction, out RayHit, TotalRevealerRadius, ObstacleMask))
            {
				ray.hit = true;
				ray.normal = math.normalizesafe(GetVector2D(RayHit.normal));
				ray.distance = RayHit.distance;
				ray.point = GetVector2D(RayHit.point);
			}
			else
            {
				ray.hit = false;
				ray.normal = -ray.direction;
				ray.distance = TotalRevealerRadius;
				//ray.point = GetVector2D(CachedTransform.position) + (ray.direction * RayDistance);
                ray.point = GetVector2D(EyePosition) + ray.direction * TotalRevealerRadius;
            }
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 GetVector2D(float3 v)
        {
            return Projection.Project(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override float GetEyeRotation()
        {
            //float2 forward2D = Projection.Project(GetForward());
            return math.degrees(math.atan2(ForwardVectorProjectedCached.x, ForwardVectorProjectedCached.y));
        }

		public override float3 GetEyePosition()
        {
            float3 eyePos = (float3)CachedTransform.position + FogOfWarWorld.UpVector * EyeOffset;
			if (FogOfWarWorld.instance.PixelateFog && FogOfWarWorld.instance.RoundRevealerPosition)
            {
				eyePos *= FogOfWarWorld.instance.PixelDensity;
                float3 PixelGridOffset = new float3(FogOfWarWorld.instance.PixelGridOffset.x, 0, FogOfWarWorld.instance.PixelGridOffset.y);
				eyePos -= PixelGridOffset;
				eyePos = (float3)(math.round(eyePos));
				eyePos += PixelGridOffset;
				eyePos /= FogOfWarWorld.instance.PixelDensity;
			}
			return eyePos;
		}

		//[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected override bool CanSeeHider(FogOfWarHider hiderInQuestion, float2 hiderPosition)
        {
            float maxSampleDist = hiderInQuestion.MaxSamplePointLocalPosition;
            float distSqToHider = FogMath2D.DistanceSq(hiderPosition, RevealerPosition);

            if (maxSampleDist == 0)     //fast path for hiders with 1 sample point
            {
                if (distSqToHider > hiderSightDistSq)
                    return false;
            }
            else
            {
                // Expand search radius by max sample offset so we don't miss hiders 
                // whose origin is outside range but sample points are inside
                float threshold = hiderSightDist + maxSampleDist;
                if (distSqToHider > threshold * threshold)
                    return false;
            }

            //return false;

            float heightDist = Projection.HeightDifference(EyePosition, hiderInQuestion.SamplePoints[0].position) - maxSampleDist;

            if (heightDist > hiderHeightSightDist)
                return false;

            if (maxSampleDist == 0)   //if only one sample point, then skip loop and extra distance calcs
                return CanSeeWorldPositionPartTwo(distSqToHider, hiderInQuestion.SamplePoints[0].position);

            for (int j = 0; j < hiderInQuestion.SamplePoints.Length; j++)
            {
                if (CanSeeHiderExtraSamplePoint(hiderInQuestion.SamplePoints[j]))
                    return true;
            }

            return false;
		}
		
		bool CanSeeHiderExtraSamplePoint(Transform samplePoint)
		{
			return CanSeeWorldPosition(samplePoint.position);
        }
        
        bool CanSeeWorldPosition(float3 samplePointPosition)
        {
            float heightDist = Projection.HeightDifference(EyePosition, samplePointPosition);
            if (heightDist > hiderHeightSightDist)
                return false;

            float sqDistToPoint = Projection.DistanceSq2D(samplePointPosition, EyePosition);
            if (sqDistToPoint > hiderSightDistSq)
                return false;

            return CanSeeWorldPositionPartTwo(sqDistToPoint, samplePointPosition);
		}

        float3 hiderPosition;
        float3 revealerOrigin;
        //part 2 handles vision angle + occlusion
        bool CanSeeWorldPositionPartTwo(float sqDistToPoint, float3 samplePointPosition)
        {
            if (sqDistToPoint < unobscuredHiderSightDistSq)
                return unobscuredRadius >= 0;   //for negative ubobscured radius

            if (IsInFOV(samplePointPosition - EyePosition, ForwardVectorProjectedCached))
            {
                if (!useOcclusion)
                    return true;

                revealerOrigin = EyePosition;
                if (SetHiderRayOriginToHidersHeight)
                    SetRevealerOrigin(EyePosition, samplePointPosition);

                hiderPosition = samplePointPosition;
                if (SetHiderRayDestinationToRevealersHeight)
                    SetHiderPositionToMyHeight(samplePointPosition, EyePosition);
                //else
                //	hiderPosition = samplePointPosition;

                float distToPoint = math.sqrt(sqDistToPoint);
                if (!physicsScene.Raycast(revealerOrigin, hiderPosition - revealerOrigin, out RayHit, distToPoint, ObstacleMask))
                {
#if UNITY_EDITOR
                    if (DrawHiderSamples)
                        Debug.DrawLine(revealerOrigin, hiderPosition, Color.green);
#endif
                    return true;
                }
#if UNITY_EDITOR
                else
                {
                    if (DebugLogHiderBlockerName)
                        Debug.Log(RayHit.collider.gameObject.name);
                    if (DrawHiderSamples)
                    {
                        Debug.DrawLine(revealerOrigin, RayHit.point, Color.green);
                        Debug.DrawLine(RayHit.point, hiderPosition, Color.red);
                    }
                }
#endif
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsInFOV(float3 dirToTarget, float2 forwardProjected)
        {
            if (CircleIsComplete)
                return true;
            float2 dirProjected = math.normalize(Projection.Project(dirToTarget));
            return math.dot(dirProjected, forwardProjected) >= cosHalfViewAngle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetHiderPositionToMyHeight(float3 point, float3 eyePosition)
        {
            hiderPosition = Projection.SetHeight(point, Projection.GetHeight(eyePosition));
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetRevealerOrigin(float3 point, float3 _hiderPosition)
        {
            revealerOrigin = Projection.SetHeight(point, Projection.GetHeight(_hiderPosition));
        }

        protected override bool _TestPoint(float3 point)
        {
            return CanSeeWorldPosition(point);
            //EyePosition = GetEyePosition();
            //ForwardVectorCached = GetForward();
            //float distToPoint = DistBetweenVectors(point, EyePosition);
            //         bool inFov = math.abs(AngleBetweenVector2(point - EyePosition, ForwardVectorCached)) < (ViewAngle * 0.5f);
            //         if (distToPoint < UnobscuredRadius || (distToPoint < sightDist && inFov))
            //{
            //	SetHiderPositionToMyHeight(point, EyePosition);
            //	if (!physicsScene.Raycast(EyePosition, hiderPosition - EyePosition, distToPoint, ObstacleMask))
            //		return true;
            //}
            //return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void SetPositionAndHeight()
        {
            RevealerPosition = Projection.Project(EyePosition);
            RevealerHeightPosition = Projection.GetHeight(EyePosition);
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override float AngleBetweenVector2(float3 _vec1, float3 _vec2)
		{
            return FogMath2D.SignedAngleDeg(Projection.Project(_vec1), Projection.Project(_vec2));
        }

        protected override void SetCachedForward()
        {
            ForwardVectorCached = Projection.HeightAxis == 1 ? CachedTransform.forward : CachedTransform.up;
            ForwardVectorProjectedCached = math.normalize(Projection.Project(ForwardVectorCached));
        }


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override float3 DirFromAngle(float angleInDegrees)
		{
            return Projection.DirectionFromAngle(angleInDegrees);
		}

		protected override float3 _Get3DPositionfrom2D(float2 pos)
        {
            return Projection.To3D(pos, Projection.GetHeight(CachedTransform.position));
		}
    }
}