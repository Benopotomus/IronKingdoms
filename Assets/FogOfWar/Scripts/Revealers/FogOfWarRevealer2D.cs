using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

namespace FOW
{
    public class FogOfWarRevealer2D : RaycastRevealer
    {
        private RaycastHit2D[] InitialRayResults;
        private PhysicsScene2D physicsScene2D;
        
        protected override void _InitRevealer(int StepCount)
        {
            InitialRayResults = new RaycastHit2D[StepCount];
            physicsScene2D = gameObject.scene.GetPhysicsScene2D();
        }

        protected override void _CleanupRaycastRevealer()
        {

        }

        protected override void IterationOne(float firstAngle, float angleStep)
        {
            for (int i = 0; i < FirstIterationStepCount; i++)
            {
                FirstIteration.RayAngles[i] = firstAngle + (angleStep * i);
                FirstIteration.Directions[i] = DirectionFromAngle(FirstIteration.RayAngles[i], true);
                RayHit = physicsScene2D.Raycast((Vector3)EyePosition, FirstIteration.Directions[i], TotalRevealerRadius, ObstacleMask);
                if (RayHit.collider != null)
                {
                    FirstIteration.Hits[i] = true;
                    FirstIteration.Normals[i] = RayHit.normal;
                    FirstIteration.Distances[i] = RayHit.distance;
                    FirstIteration.Points[i] = RayHit.point;
                }
                else
                {
                    FirstIteration.Hits[i] = false;
                    FirstIteration.Normals[i] = -FirstIteration.Directions[i];
                    FirstIteration.Distances[i] = TotalRevealerRadius;
                    FirstIteration.Points[i] = GetPositionxy(EyePosition) + FirstIteration.Directions[i] * TotalRevealerRadius;
                }
            }

            //PointsJob.SStep = SinStep;
            //PointsJob.CStep = CosStep;
            //PointsJobHandle = PointsJob.Schedule(FirstIterationStepCount, CommandsPerJob, default(JobHandle));
            PreReqJobHandle = default(JobHandle);
        }

        private RaycastHit2D RayHit;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void RayCast(float angle, ref SightRay ray)
        {
            Vector2 direction = DirectionFromAngle(angle, true);
            ray.angle = angle;
            ray.direction = direction;
            RayHit = physicsScene2D.Raycast((Vector3)EyePosition, direction, TotalRevealerRadius, ObstacleMask);

            if (RayHit.collider != null)
            {
                ray.hit = true;
                ray.normal = RayHit.normal;
                ray.distance = RayHit.distance;
                ray.point = RayHit.point;
            }
            else
            {
                ray.hit = false;
                ray.normal = -direction;
                ray.distance = TotalRevealerRadius;
                ray.point = GetPositionxy(EyePosition) + ray.direction * TotalRevealerRadius;
            }
        }

        private float2 pos2d;
        private float2 GetPositionxy(Vector3 pos)
        {
            pos2d.x = pos.x;
            pos2d.y = pos.y;
            return pos2d;
        }

        protected override float GetEyeRotation()
        {
            Vector3 up = transform.up;
            up.z = 0;
            up.Normalize();
            float ang = Vector3.SignedAngle(up, Vector3.up, -Vector3.forward);
            return -ang;
            //return transform.eulerAngles.z;
        }

        public override float3 GetEyePosition()
        {
            Vector3 eyePos = transform.position;
            if (FogOfWarWorld.instance.PixelateFog && FogOfWarWorld.instance.RoundRevealerPosition)
            {
                eyePos *= FogOfWarWorld.instance.PixelDensity;
                Vector3 PixelGridOffset = new Vector3(FogOfWarWorld.instance.PixelGridOffset.x, FogOfWarWorld.instance.PixelGridOffset.y, 0);
                eyePos -= PixelGridOffset;
                eyePos = (Vector3)(Vector3Int.RoundToInt(eyePos));
                eyePos += PixelGridOffset;
                eyePos /= FogOfWarWorld.instance.PixelDensity;
            }
            return eyePos;
        }

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

        float3 hiderPosition;
        bool CanSeeWorldPosition(float3 samplePointPosition)
        {
            //float distToHider = distBetweenVectors(samplePointPosition, EyePosition);

            float sqDistToPoint = DistanceSq(samplePointPosition, EyePosition);
            if (sqDistToPoint > hiderSightDistSq)
                return false;


            return CanSeeWorldPositionPartTwo(sqDistToPoint, samplePointPosition);
        }

        bool CanSeeWorldPositionPartTwo(float sqDistToPoint, float3 samplePointPosition)
        {
            if (sqDistToPoint < unobscuredHiderSightDistSq)
                return unobscuredRadius >= 0;   //for negative ubobscured radius

            if (IsInFOV(samplePointPosition - EyePosition, ForwardVectorProjectedCached))
            {
                if (!useOcclusion)
                    return true;

                SetHiderPosition(samplePointPosition);
                float distToPoint = math.sqrt(sqDistToPoint);
                if (!physicsScene2D.Raycast((Vector3)EyePosition, (Vector3)(hiderPosition - EyePosition), distToPoint, ObstacleMask))
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsInFOV(float3 dirToTarget, float2 forwardProjected)
        {
            if (CircleIsComplete)
                return true;
            float2 dirProjected = math.normalize(new float2(dirToTarget.x, dirToTarget.y));
            return math.dot(dirProjected, forwardProjected) >= cosHalfViewAngle;
        }

        void SetHiderPosition(float3 point)
        {
            hiderPosition.x = point.x;
            hiderPosition.y = point.y;
            //hiderPosition.z = getEyePos().z;
        }

        protected override bool _TestPoint(float3 point)
        {
            return CanSeeWorldPosition(point);
        }

        protected override void SetPositionAndHeight()
        {
            RevealerPosition.x = EyePosition.x;
            RevealerPosition.y = EyePosition.y;
            RevealerHeightPosition = transform.position.z;
        }

        protected override float AngleBetweenVector2(float3 _vec1, float3 _vec2)
        {
            float2 a = new float2(_vec1.x, _vec1.y);
            float2 b = new float2(_vec2.x, _vec2.y);
            return FogMath2D.SignedAngleDeg(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSq(float3 a, float3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return dx * dx + dy * dy;
        }

        protected override void SetCachedForward()
        {
            ForwardVectorCached = math.normalize(new float3(transform.up.x, transform.up.y, 0));
            ForwardVectorProjectedCached = new float2(ForwardVectorCached.x, ForwardVectorCached.y);
        }

        Vector2 DirectionFromAngle(float angleInDegrees, bool angleIsGlobal)
        {
            if (!angleIsGlobal)
                angleInDegrees += transform.eulerAngles.z;
            float s, c; math.sincos(math.radians(angleInDegrees), out s, out c);
            return new float2(c, s);

            //direction2d.x = Mathf.Cos(angleInDegrees * Mathf.Deg2Rad);
            //direction2d.y = Mathf.Sin(angleInDegrees * Mathf.Deg2Rad);
            //return direction2d;
        }

        Vector3 direction = Vector3.zero;
        public override float3 DirFromAngle(float angleInDegrees)
        {
            float angleInRadians = math.radians(angleInDegrees);
            math.sincos(angleInRadians, out direction.y, out direction.x);
            return direction;
        }

        protected override float3 _Get3DPositionfrom2D(float2 pos)
        {
            return new Vector3(pos.x, pos.y, 0);
        }
    }
}
