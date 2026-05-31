using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace FOW.Demos
{
    public class UnitSpawnerAndMover : MonoBehaviour
    {
        public GameObject RevealerPrefab;
        public int NumToSpawn = 1000;

        public int MoveRange = 98;
        public float MoveSpeed = 5f;
        public float RotationSpeed = 10f;

        [Header("Wandering Settings")]
        [Tooltip("Maximum distance for new target from current position")]
        public float WanderRadius = 10f;

        [Header("Obstacle Avoidance (Optional)")]
        public bool UseRaycasting = false;
        public LayerMask ObstacleLayer;
        public int MaxPathRetries = 5;

        private TransformAccessArray transformAccessArray;
        private NativeArray<float3> targetPositions;
        private NativeArray<Unity.Mathematics.Random> randoms;
        private NativeArray<bool> needsNewTarget;

        private JobHandle moveJobHandle;

        private const float ArrivalThresholdSqr = 0.5f;

        private int frameCount = 0;

        private void Awake()
        {
            float gridSize = math.ceil(math.sqrt(NumToSpawn));
            float spacing = MoveRange * 2 / gridSize;
            Vector3 offset = new Vector3(-MoveRange, 0, -MoveRange);
            List<Transform> targets = new List<Transform>();
            for (int i = 0; i < NumToSpawn; i++)
            {
                int row = i / (int)gridSize;
                int col = i % (int)gridSize;

                float x = col * spacing;
                float z = row * spacing;

                GameObject instance = Instantiate(RevealerPrefab, new Vector3(x, 0, z) + offset, quaternion.identity);
                targets.Add(instance.transform);
            }

            Transform[] transforms = targets.ToArray();

            transformAccessArray = new TransformAccessArray(transforms);
            targetPositions = new NativeArray<float3>(transforms.Length, Allocator.Persistent);
            randoms = new NativeArray<Unity.Mathematics.Random>(transforms.Length, Allocator.Persistent);
            needsNewTarget = new NativeArray<bool>(transforms.Length, Allocator.Persistent);

            uint seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);

            for (int i = 0; i < transforms.Length; i++)
            {
                var rng = Unity.Mathematics.Random.CreateFromIndex(seed + (uint)i);
                float3 startPos = transforms[i].position;
                targetPositions[i] = GetClampedWanderPosition(startPos, rng.NextFloat2Direction() * rng.NextFloat(WanderRadius));
                randoms[i] = rng;
                needsNewTarget[i] = false;
            }
        }

        private void Update()
        {
            moveJobHandle.Complete();

            // Handle raycasting on main thread if enabled (can't do Physics in jobs)
            if (UseRaycasting)
            {
                HandleRaycastTargetSelection();
            }

            float speed = MoveSpeed;
            if (frameCount < 5) //we dont want the revealers to be stuck in objects in the first few frames
                speed = 1000;
            var moveJob = new MoveTowardsJob
            {
                TargetPositions = targetPositions,
                Randoms = randoms,
                NeedsNewTarget = needsNewTarget,
                DeltaSpeed = Time.deltaTime * speed,
                DeltaRotation = Time.deltaTime * RotationSpeed,
                ArrivalThresholdSqr = ArrivalThresholdSqr,
                MoveRange = MoveRange,
                WanderRadius = WanderRadius,
                UseRaycasting = UseRaycasting
            };

            moveJobHandle = moveJob.Schedule(transformAccessArray);
            frameCount++;
        }

        private void HandleRaycastTargetSelection()
        {
            for (int i = 0; i < needsNewTarget.Length; i++)
            {
                if (!needsNewTarget[i]) continue;

                var rng = randoms[i];
                float3 currentPos = targetPositions[i];

                bool foundValidTarget = false;
                float3 newTarget = float3.zero;

                for (int attempt = 0; attempt < MaxPathRetries; attempt++)
                {
                    // Generate random direction and distance
                    float2 randomDir = rng.NextFloat2Direction();
                    float randomDist = rng.NextFloat(WanderRadius * 0.5f, WanderRadius);

                    float3 offset = new float3(randomDir.x * randomDist, 0, randomDir.y * randomDist);
                    newTarget = currentPos + offset;

                    // Clamp to bounds
                    newTarget.x = math.clamp(newTarget.x, -MoveRange, MoveRange);
                    newTarget.z = math.clamp(newTarget.z, -MoveRange, MoveRange);
                    //newTarget.y = 1f;

                    // Raycast to check for obstacles
                    Vector3 direction = (Vector3)(newTarget - currentPos);
                    float distance = math.length(direction);

                    if (distance > 0.01f && !Physics.Raycast(currentPos, direction.normalized, distance, ObstacleLayer))
                    {
                        foundValidTarget = true;
                        break;
                    }
                }

                if (foundValidTarget)
                {
                    targetPositions[i] = newTarget;
                }
                else
                {
                    //float2 randomDir = rng.NextFloat2Direction();
                    //float randomDist = rng.NextFloat(WanderRadius * 0.25f, WanderRadius * 0.5f);
                    //newTarget = currentPos + new float3(randomDir.x * randomDist, 0, randomDir.y * randomDist);
                    //newTarget.x = math.clamp(newTarget.x, -MoveRange, MoveRange);
                    //newTarget.z = math.clamp(newTarget.z, -MoveRange, MoveRange);
                    ////newTarget.y = 1f;
                    //targetPositions[i] = newTarget;
                    targetPositions[i] = currentPos;
                }

                randoms[i] = rng;
                needsNewTarget[i] = false;
            }
        }

        private float3 GetClampedWanderPosition(float3 currentPos, float2 offset)
        {
            float3 newPos = currentPos + new float3(offset.x, 0, offset.y);
            newPos.x = math.clamp(newPos.x, -MoveRange, MoveRange);
            newPos.z = math.clamp(newPos.z, -MoveRange, MoveRange);
            //newPos.y = 1f;
            return newPos;
        }

        private void OnDestroy()
        {
            moveJobHandle.Complete();

            if (transformAccessArray.isCreated)
                transformAccessArray.Dispose();

            if (targetPositions.IsCreated)
                targetPositions.Dispose();

            if (randoms.IsCreated)
                randoms.Dispose();

            if (needsNewTarget.IsCreated)
                needsNewTarget.Dispose();
        }

        [BurstCompile]
        private struct MoveTowardsJob : IJobParallelForTransform
        {
            public NativeArray<float3> TargetPositions;
            public NativeArray<Unity.Mathematics.Random> Randoms;
            public NativeArray<bool> NeedsNewTarget;
            public float DeltaSpeed;
            public float DeltaRotation;
            public float ArrivalThresholdSqr;
            public int MoveRange;
            public float WanderRadius;
            public bool UseRaycasting;

            public void Execute(int index, TransformAccess transform)
            {
                float3 currentPos = transform.position;
                float3 targetPos = TargetPositions[index];

                float3 direction = targetPos - currentPos;
                float sqrDistance = math.lengthsq(direction);

                if (sqrDistance < ArrivalThresholdSqr)
                {
                    if (UseRaycasting)
                    {
                        // Flag for main thread to handle with raycasting
                        NeedsNewTarget[index] = true;
                        TargetPositions[index] = currentPos; // Store current pos for raycast origin
                    }
                    else
                    {
                        // No raycasting - pick new target directly in job
                        var rng = Randoms[index];

                        // Generate random direction and distance within WanderRadius
                        float2 randomDir = rng.NextFloat2Direction();
                        float randomDist = rng.NextFloat(WanderRadius * 0.5f, WanderRadius);

                        targetPos = currentPos + new float3(randomDir.x * randomDist, 0, randomDir.y * randomDist);

                        // Clamp to bounds
                        targetPos.x = math.clamp(targetPos.x, -MoveRange, MoveRange);
                        targetPos.z = math.clamp(targetPos.z, -MoveRange, MoveRange);
                        //targetPos.y = 1f;

                        TargetPositions[index] = targetPos;
                        Randoms[index] = rng;
                    }

                    direction = targetPos - currentPos;
                    sqrDistance = math.lengthsq(direction);
                }

                if (sqrDistance > 0.0001f)
                {
                    float distance = math.sqrt(sqrDistance);
                    float3 normalizedDirection = direction / distance;

                    float moveDistance = math.min(DeltaSpeed, distance);
                    transform.position = currentPos + normalizedDirection * moveDistance;

                    quaternion targetRotation = quaternion.LookRotationSafe(normalizedDirection, math.up());
                    transform.rotation = math.slerp(transform.rotation, targetRotation, DeltaRotation);
                }
            }
        }
    }
}