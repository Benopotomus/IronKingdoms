using FOW;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Parallel forest terrain clip sampling: AABB cull + full clip on worker threads when zone
    /// footprints are cache-safe. Never calls EnsureCache or CombatZone from jobs.
    /// </summary>
    internal static class CombatForestFogClipBinParallelBuilder
    {
        private readonly struct ZoneAabb
        {
            public readonly float MinX;
            public readonly float MaxX;
            public readonly float MinZ;
            public readonly float MaxZ;

            public ZoneAabb(float minX, float maxX, float minZ, float maxZ)
            {
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
            }
        }

        private struct AabbCullJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ZoneAabb> Zones;
            [ReadOnly] public NativeArray<float2> Directions;
            [ReadOnly] public float3 Origin;
            [ReadOnly] public float MaxDistance;
            [WriteOnly] public NativeArray<byte> NeedsFullClip;

            public void Execute(int index)
            {
                var direction = Directions[index];
                if (math.lengthsq(direction) <= 1e-8f || MaxDistance <= 0.001f || Zones.Length == 0)
                {
                    NeedsFullClip[index] = 0;
                    return;
                }

                direction = math.normalize(direction);
                for (var z = 0; z < Zones.Length; z++)
                {
                    var zone = Zones[z];
                    if (CombatFogPlanarGeometry.RayMayHitHorizontalAabb(
                            new float2(Origin.x, Origin.z),
                            direction,
                            MaxDistance,
                            zone.MinX,
                            zone.MaxX,
                            zone.MinZ,
                            zone.MaxZ))
                    {
                        NeedsFullClip[index] = 1;
                        return;
                    }
                }

                NeedsFullClip[index] = 0;
            }
        }

        private struct ForestClipSampleJob : IJobParallelFor
        {
            public CombatForestFogLutBuildContext BuildContext;
            [ReadOnly] public NativeArray<float3> DirectionsWorld;
            [WriteOnly] public NativeArray<float> ClipDistances;

            public void Execute(int index)
            {
                ClipDistances[index] = CombatForestFogClipperParallelCache.GetFirstContactDepthClipDistanceWorldParallelSafe(
                    BuildContext,
                    DirectionsWorld[index]);
            }
        }

        public static bool TryBuildSmoothedClipDistances(
            in CombatForestFogLutBuildContext buildContext,
            float maxSearchRadius,
            float originRadiusWorld,
            FogOfWarRevealer3D.PlaneProjection projection,
            float[] clipDistances,
            int sampleCount,
            float neighborHalfAngleRadians,
            bool useMedianSmoothing)
        {
            _ = originRadiusWorld;
            _ = projection;
            if (!CombatForestFogPassSettings.UseParallelForestClipLutBuild
                || sampleCount < CombatForestFogPassSettings.ParallelForestClipLutMinSamples
                || !CombatForestFogPassSettings.ShouldUseParallelForestClip(false, sampleCount))
            {
                return false;
            }

            return TryFillClipDistancesInternal(
                buildContext,
                maxSearchRadius,
                sampleCount,
                index => CombatForestFogAngularTables.GetDirectionWorldXZ(index, sampleCount),
                clipDistances,
                neighborHalfAngleRadians,
                useMedianSmoothing);
        }

        public static bool TryFillWallRayClipDistances(
            in CombatForestFogLutBuildContext buildContext,
            float maxSearchRadius,
            Vector3[] directionsWorld,
            float[] clipDistances,
            int count,
            bool requireFullTerrainFidelity)
        {
            if (!CombatForestFogPassSettings.ShouldUseParallelForestClip(requireFullTerrainFidelity, count))
            {
                return false;
            }

            return TryFillClipDistancesInternal(
                buildContext,
                maxSearchRadius,
                count,
                index => directionsWorld[index],
                clipDistances,
                neighborHalfAngleRadians: -1f,
                useMedianSmoothing: false);
        }

        private static bool TryFillClipDistancesInternal(
            in CombatForestFogLutBuildContext buildContext,
            float maxSearchRadius,
            int sampleCount,
            System.Func<int, Vector3> directionAtIndex,
            float[] clipDistances,
            float neighborHalfAngleRadians,
            bool useMedianSmoothing)
        {
            if (clipDistances == null || sampleCount <= 0 || sampleCount > clipDistances.Length)
            {
                return false;
            }

            if (!buildContext.HasForest)
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    clipDistances[i] = maxSearchRadius;
                }

                return true;
            }

            var zoneCount = CombatForestFogClipper.GetCachedZoneAabbCount();
            if (zoneCount <= 0)
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    clipDistances[i] = maxSearchRadius;
                }

                return true;
            }

            var zoneData = new ZoneAabb[zoneCount];
            for (var z = 0; z < zoneCount; z++)
            {
                var aabb = CombatForestFogClipper.GetCachedZoneAabb(z);
                zoneData[z] = new ZoneAabb(aabb.MinX, aabb.MaxX, aabb.MinZ, aabb.MaxZ);
            }

            var directionData = new float3[sampleCount];
            var directionWorldData = new float3[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var directionWorld = directionAtIndex(i);
                directionWorld.y = 0f;
                directionWorldData[i] = directionWorld;
                directionData[i] = new float3(directionWorld.x, 0f, directionWorld.z);
            }

            var nativeZones = new NativeArray<ZoneAabb>(zoneData, Allocator.TempJob);
            var nativeDirections = new NativeArray<float2>(sampleCount, Allocator.TempJob);
            var nativeDirectionsWorld = new NativeArray<float3>(directionWorldData, Allocator.TempJob);
            var needsFullClip = new NativeArray<byte>(sampleCount, Allocator.TempJob);
            var clipNative = new NativeArray<float>(sampleCount, Allocator.TempJob);
            try
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    nativeDirections[i] = new float2(directionData[i].x, directionData[i].z);
                }

                if (!buildContext.RayStartedInsideForest)
                {
                    var cullJob = new AabbCullJob
                    {
                        Zones = nativeZones,
                        Directions = nativeDirections,
                        Origin = new float3(buildContext.FlatEye.x, 0f, buildContext.FlatEye.z),
                        MaxDistance = buildContext.MaxSearchRadius,
                        NeedsFullClip = needsFullClip,
                    };
                    cullJob.Schedule(sampleCount, 32).Complete();
                }
                else
                {
                    for (var i = 0; i < sampleCount; i++)
                    {
                        needsFullClip[i] = 1;
                    }
                }

                for (var i = 0; i < sampleCount; i++)
                {
                    clipNative[i] = needsFullClip[i] == 0 ? maxSearchRadius : 0f;
                }

                if (CombatForestFogPassSettings.UseParallelForestClipFullSample
                    && CombatForestFogClipper.CanRunParallelForestClipSampling())
                {
                    CombatForestFogClipperParallelCache.BeginJobSnapshot();
                    try
                    {
                        var sampleJob = new ForestClipSampleJob
                        {
                            BuildContext = buildContext,
                            DirectionsWorld = nativeDirectionsWorld,
                            ClipDistances = clipNative,
                        };
                        sampleJob.Schedule(sampleCount, 8).Complete();
                    }
                    finally
                    {
                        CombatForestFogClipperParallelCache.EndJobSnapshot();
                    }

                    for (var i = 0; i < sampleCount; i++)
                    {
                        if (needsFullClip[i] == 0)
                        {
                            clipNative[i] = maxSearchRadius;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < sampleCount; i++)
                    {
                        if (needsFullClip[i] == 0)
                        {
                            clipDistances[i] = maxSearchRadius;
                            continue;
                        }

                        clipDistances[i] = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                            buildContext,
                            directionWorldData[i]);
                    }

                    if (useMedianSmoothing)
                    {
                        ApplyMedianSmoothingMainThread(
                            buildContext,
                            clipDistances,
                            sampleCount,
                            neighborHalfAngleRadians,
                            needsFullClip);
                    }

                    return true;
                }

                for (var i = 0; i < sampleCount; i++)
                {
                    clipDistances[i] = clipNative[i];
                }

                if (useMedianSmoothing)
                {
                    ApplyMedianSmoothingMainThread(
                        buildContext,
                        clipDistances,
                        sampleCount,
                        neighborHalfAngleRadians,
                        needsFullClip);
                }

                return true;
            }
            finally
            {
                if (nativeZones.IsCreated)
                {
                    nativeZones.Dispose();
                }

                if (nativeDirections.IsCreated)
                {
                    nativeDirections.Dispose();
                }

                if (nativeDirectionsWorld.IsCreated)
                {
                    nativeDirectionsWorld.Dispose();
                }

                if (needsFullClip.IsCreated)
                {
                    needsFullClip.Dispose();
                }

                if (clipNative.IsCreated)
                {
                    clipNative.Dispose();
                }
            }
        }

        private static void ApplyMedianSmoothingMainThread(
            in CombatForestFogLutBuildContext buildContext,
            float[] clipDistances,
            int sampleCount,
            float neighborHalfAngleRadians,
            NativeArray<byte> needsFullClip)
        {
            if (neighborHalfAngleRadians <= 1e-6f)
            {
                return;
            }

            var scratch = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                if (needsFullClip.IsCreated && needsFullClip[i] == 0)
                {
                    scratch[i] = clipDistances[i];
                    continue;
                }

                var directionWorld = CombatForestFogAngularTables.GetDirectionWorldXZ(i, sampleCount);
                scratch[i] = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorldSmoothed(
                    buildContext.FlatEye,
                    directionWorld,
                    buildContext.MaxSearchRadius,
                    buildContext.DepthWorld,
                    buildContext.OriginRadiusWorld,
                    neighborHalfAngleRadians);
            }

            for (var i = 0; i < sampleCount; i++)
            {
                clipDistances[i] = scratch[i];
            }
        }
    }
}
