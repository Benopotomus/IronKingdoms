using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronKingdoms.Combat.PlayModeTests
{
    public class ForestFogRenderRegressionTests
    {
        private const int FogTextureSize = 256;
        private const int RadialSamples = 96;
        private const float ForegroundThreshold = 0.5f;
        private const float MaxAllowedCoverageError = 0.20f;
        private const float MaxAllowedMeanAbsError = 0.16f;

        private static readonly string ArtifactRoot = Path.Combine(
            Directory.GetCurrentDirectory(),
            "TestResults",
            "FogRenderRegression");

        private static readonly BindingFlags InstancePrivate =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator ForestFog_RenderMatchesExpectedShapeAtKeyPositions()
        {
            Directory.CreateDirectory(ArtifactRoot);

            var context = BuildScenario();
            try
            {
                // Give registration/update pipeline time to settle and populate FOW texture.
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                context.FogWorld.RenderFogTexture();
                yield return new WaitForEndOfFrame();

                var edgeOutside = new Vector3(
                    context.ForestCenter.x - context.ForestHalfWidthWorld - CombatScale.InchesToWorldUnits(10f),
                    0f,
                    context.ForestCenter.z);
                var edgeOnBoundary = new Vector3(
                    context.ForestCenter.x - context.ForestHalfWidthWorld + 0.01f,
                    0f,
                    context.ForestCenter.z);
                var insideDeep = new Vector3(
                    context.ForestCenter.x,
                    0f,
                    context.ForestCenter.z);

                yield return VerifyPosition(context, "outside-10in", edgeOutside);
                yield return VerifyPosition(context, "edge", edgeOnBoundary);
                yield return VerifyPosition(context, "inside-center", insideDeep);
            }
            finally
            {
                context.Dispose();
            }
        }

        private static IEnumerator VerifyPosition(ScenarioContext context, string label, Vector3 unitPosition)
        {
            context.Revealer.transform.position = unitPosition;

            // Update and render fresh FOW data for this checkpoint.
            context.Revealer.ManualCalculateLineOfSight();
            context.FogWorld.RenderFogTexture();
            yield return null;
            yield return new WaitForEndOfFrame();
            context.FogWorld.RenderFogTexture();
            yield return new WaitForEndOfFrame();

            var actualMask = CaptureFogMask(context.FogWorld, unitPosition, ForegroundThreshold);
            var expectedMask = BuildExpectedMask(context, unitPosition);
            var metrics = ComputeMaskMetrics(actualMask, expectedMask);

            WriteArtifacts(label, actualMask, expectedMask, metrics.DiffMask);

            Assert.LessOrEqual(
                metrics.CoverageError,
                MaxAllowedCoverageError,
                $"{label}: coverage error too high ({metrics.CoverageError:F4}).");

            Assert.LessOrEqual(
                metrics.MeanAbsoluteError,
                MaxAllowedMeanAbsError,
                $"{label}: mean absolute error too high ({metrics.MeanAbsoluteError:F4}).");
        }

        private static ScenarioContext BuildScenario()
        {
            var roots = new GameObject("FogRenderRegressionRoot");
            var rootTransform = roots.transform;

            var fogWorldGo = new GameObject("FogWorld");
            fogWorldGo.transform.SetParent(rootTransform, false);
            var fogWorld = fogWorldGo.AddComponent<FOW.FogOfWarWorld>();
            ConfigureFogWorld(fogWorld);
            fogWorld.Initialize();
            fogWorld.ClearFowTexture();

            var forestFeature = ScriptableObject.CreateInstance<CombatTerrainFeatureDefinition>();
            SetPrivateField(forestFeature, "featureId", "TestForest");
            SetPrivateField(forestFeature, "displayName", "Test Forest");
            SetPrivateField(forestFeature, "lineOfSightMode", CombatTerrainLineOfSightMode.LimitedDepth);
            SetPrivateField(forestFeature, "lineOfSightPassThroughDepthInches", 3f);

            var forestGo = new GameObject("ForestZone");
            forestGo.transform.SetParent(rootTransform, false);
            forestGo.transform.position = new Vector3(0f, 1f, 0f);
            var forestCollider = forestGo.AddComponent<BoxCollider>();
            forestCollider.isTrigger = true;
            forestCollider.size = new Vector3(20f, 2f, 16f);
            var forestZone = forestGo.AddComponent<CombatZone>();
            SetPrivateField(forestZone, "terrainFeature", forestFeature);

            var revealerGo = new GameObject("Revealer");
            revealerGo.transform.SetParent(rootTransform, false);
            revealerGo.transform.position = new Vector3(-12f, 0f, 0f);
            var revealer = revealerGo.AddComponent<CombatFogOfWarRevealer3D>();
            ConfigureRevealer(revealer);

            return new ScenarioContext(
                roots,
                fogWorld,
                forestFeature,
                forestZone,
                revealer,
                forestCollider.bounds.center,
                forestCollider.bounds.extents.x);
        }

        private static void ConfigureFogWorld(FOW.FogOfWarWorld world)
        {
            world.is2D = false;
            world.GamePlaneOrientation = FOW.FogOfWarWorld.GamePlane.XZ;
            world.FOWSamplingMode = FOW.FogOfWarWorld.FogSampleMode.Texture;
            world.FowResX = FogTextureSize;
            world.FowResY = FogTextureSize;
            world.UseMiniMap = false;
            world.UseRegrow = false;
            world.UpdateMethod = FOW.FogOfWarWorld.FowUpdateMethod.LateUpdate;
            world.RevealerUpdateMode = FOW.FogOfWarWorld.RevealerUpdateMethod.Every_Frame;
            world.MaxPossibleRevealers = 32;
            world.MaxPossibleSegmentsPerRevealer = 512;
            world.SightExtraAmount = 0f;
            world.PixelateFog = false;
            world.RoundRevealerPosition = false;
            world.UseSpatialAcceleration = false;
            world.WorldBounds = new Bounds(Vector3.zero, new Vector3(50f, 10f, 50f));
        }

        private static void ConfigureRevealer(CombatFogOfWarRevealer3D revealer)
        {
            revealer.ViewRadius = CombatScale.InchesToWorldUnits(18f);
            revealer.SoftenDistance = 0f;
            revealer.UnobscuredRadius = 0f;
            revealer.UnobscuredSoftenDistance = 0f;
            revealer.VisionHeight = 3f;
            revealer.VisionHeightSoftenDistance = 0f;
            revealer.ViewAngle = 360f;
            revealer.Opacity = 1f;
            revealer.UseOcclusion = true;
            revealer.AddCorners = true;
            revealer.ResolveEdge = true;
            revealer.RaycastResolution = 1f;
            revealer.NumExtraIterations = 1;
            revealer.NumExtraRaysOnIteration = 2;
            revealer.ObstacleLayerMask = ~0;
            revealer.ConfigureForUnit((UnitTypeDefinition)null);
        }

        private static bool[,] CaptureFogMask(FOW.FogOfWarWorld fogWorld, Vector3 revealedPoint, float threshold)
        {
            var rt = fogWorld.GetFOWRT();
            Assert.IsNotNull(rt, "Fog texture is null.");

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var readTex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            readTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readTex.Apply();
            RenderTexture.active = previous;

            var raw = new float[rt.width, rt.height];
            for (var y = 0; y < rt.height; y++)
            {
                for (var x = 0; x < rt.width; x++)
                {
                    raw[x, y] = readTex.GetPixel(x, y).r;
                }
            }

            var mask = new bool[rt.width, rt.height];
            var revealIsHigh = true;
            if (WorldToPixel(fogWorld, revealedPoint, out var revealX, out var revealY))
            {
                revealIsHigh = raw[revealX, revealY] >= threshold;
            }

            for (var y = 0; y < rt.height; y++)
            {
                for (var x = 0; x < rt.width; x++)
                {
                    mask[x, y] = revealIsHigh ? raw[x, y] >= threshold : raw[x, y] <= (1f - threshold);
                }
            }

            UnityEngine.Object.Destroy(readTex);
            return mask;
        }

        private static bool[,] BuildExpectedMask(ScenarioContext context, Vector3 origin)
        {
            var rt = context.FogWorld.GetFOWRT();
            var mask = new bool[rt.width, rt.height];
            var maxRadius = context.Revealer.TotalRevealerRadius;
            var depthLimit = CombatScale.InchesToWorldUnits(3f);
            var radiusByAngle = new float[RadialSamples];

            for (var angleIndex = 0; angleIndex < RadialSamples; angleIndex++)
            {
                var angle = (Mathf.PI * 2f * angleIndex) / RadialSamples;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var clipDistance = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(
                    origin,
                    direction,
                    maxRadius,
                    depthLimit);
                radiusByAngle[angleIndex] = Mathf.Clamp(clipDistance, 0f, maxRadius);
            }

            for (var y = 0; y < rt.height; y++)
            {
                for (var x = 0; x < rt.width; x++)
                {
                    var point = PixelToWorld(context.FogWorld, x, y);
                    var planar = new Vector2(point.x - origin.x, point.z - origin.z);
                    var distance = planar.magnitude;
                    if (distance <= 0.0001f)
                    {
                        mask[x, y] = true;
                        continue;
                    }

                    var angle = Mathf.Atan2(planar.y, planar.x);
                    if (angle < 0f)
                    {
                        angle += Mathf.PI * 2f;
                    }

                    var angleIndex = Mathf.Clamp(Mathf.RoundToInt((angle / (Mathf.PI * 2f)) * (RadialSamples - 1)), 0, RadialSamples - 1);
                    mask[x, y] = distance <= radiusByAngle[angleIndex];
                }
            }

            return mask;
        }

        private static bool WorldToPixel(FOW.FogOfWarWorld world, Vector3 point, out int x, out int y)
        {
            var rt = world.GetFOWRT();
            if (rt == null)
            {
                x = 0;
                y = 0;
                return false;
            }

            var bounds = world.WorldBounds;
            var minX = bounds.center.x - (bounds.size.x * 0.5f);
            var minZ = bounds.center.z - (bounds.size.z * 0.5f);
            var u = (point.x - minX) / bounds.size.x;
            var v = (point.z - minZ) / bounds.size.z;

            x = Mathf.RoundToInt(u * (rt.width - 1));
            y = Mathf.RoundToInt(v * (rt.height - 1));
            if (x < 0 || y < 0 || x >= rt.width || y >= rt.height)
            {
                return false;
            }

            return true;
        }

        private static Vector3 PixelToWorld(FOW.FogOfWarWorld world, int x, int y)
        {
            var bounds = world.WorldBounds;
            var rt = world.GetFOWRT();
            var u = x / (float)(rt.width - 1);
            var v = y / (float)(rt.height - 1);
            var worldX = ((u * bounds.size.x) - (bounds.size.x * 0.5f)) + bounds.center.x;
            var worldZ = ((v * bounds.size.z) - (bounds.size.z * 0.5f)) + bounds.center.z;
            return new Vector3(worldX, 0f, worldZ);
        }

        private static MaskMetrics ComputeMaskMetrics(bool[,] actual, bool[,] expected)
        {
            var width = actual.GetLength(0);
            var height = actual.GetLength(1);
            var diff = new bool[width, height];

            var mismatch = 0;
            var total = width * height;
            var actualOn = 0;
            var expectedOn = 0;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var a = actual[x, y];
                    var e = expected[x, y];
                    if (a)
                    {
                        actualOn++;
                    }

                    if (e)
                    {
                        expectedOn++;
                    }

                    var different = a != e;
                    diff[x, y] = different;
                    if (different)
                    {
                        mismatch++;
                    }
                }
            }

            var coverageError = expectedOn == 0 ? 0f : Mathf.Abs(actualOn - expectedOn) / (float)expectedOn;
            var mae = mismatch / (float)total;
            return new MaskMetrics(coverageError, mae, diff);
        }

        private static void WriteArtifacts(string label, bool[,] actual, bool[,] expected, bool[,] diff)
        {
            var labelDir = Path.Combine(ArtifactRoot, label);
            Directory.CreateDirectory(labelDir);

            WriteMaskPng(actual, Path.Combine(labelDir, "actual.png"), new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 255));
            WriteMaskPng(expected, Path.Combine(labelDir, "expected.png"), new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 255));
            WriteMaskPng(diff, Path.Combine(labelDir, "diff.png"), new Color32(255, 0, 0, 255), new Color32(0, 0, 0, 255));
        }

        private static void WriteMaskPng(bool[,] mask, string path, Color32 on, Color32 off)
        {
            var width = mask.GetLength(0);
            var height = mask.GetLength(1);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    pixels[y * width + x] = mask[x, y] ? on : off;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, InstancePrivate);
            Assert.IsNotNull(field, $"Missing private field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private readonly struct MaskMetrics
        {
            public readonly float CoverageError;
            public readonly float MeanAbsoluteError;
            public readonly bool[,] DiffMask;

            public MaskMetrics(float coverageError, float meanAbsoluteError, bool[,] diffMask)
            {
                CoverageError = coverageError;
                MeanAbsoluteError = meanAbsoluteError;
                DiffMask = diffMask;
            }
        }

        private sealed class ScenarioContext : IDisposable
        {
            public readonly GameObject Root;
            public readonly FOW.FogOfWarWorld FogWorld;
            public readonly CombatTerrainFeatureDefinition ForestFeature;
            public readonly CombatZone ForestZone;
            public readonly CombatFogOfWarRevealer3D Revealer;
            public readonly Vector3 ForestCenter;
            public readonly float ForestHalfWidthWorld;

            public ScenarioContext(
                GameObject root,
                FOW.FogOfWarWorld fogWorld,
                CombatTerrainFeatureDefinition forestFeature,
                CombatZone forestZone,
                CombatFogOfWarRevealer3D revealer,
                Vector3 forestCenter,
                float forestHalfWidthWorld)
            {
                Root = root;
                FogWorld = fogWorld;
                ForestFeature = forestFeature;
                ForestZone = forestZone;
                Revealer = revealer;
                ForestCenter = forestCenter;
                ForestHalfWidthWorld = forestHalfWidthWorld;
            }

            public void Dispose()
            {
                if (Root != null)
                {
                    UnityEngine.Object.DestroyImmediate(Root);
                }

                if (ForestFeature != null)
                {
                    UnityEngine.Object.DestroyImmediate(ForestFeature);
                }
            }
        }
    }
}
