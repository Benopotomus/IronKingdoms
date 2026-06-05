using System;
using System.IO;
using System.Reflection;
using IronKingdoms.Combat;
using UnityEditor;
using UnityEngine;

namespace IronKingdoms.Editor.Tests
{
    public static class ForestFogRenderRegressionBatchRunner
    {
        private const int FogTextureSize = 256;
        private const int RadialSamples = 360;
        private const float ForegroundThreshold = 0.5f;
        private const float MaxAllowedCoverageError = 0.20f;
        private const float MaxAllowedMeanAbsError = 0.16f;
        private const float MaxAllowedAngularRadiusStepWorld = 1.75f;
        private const float MaxAllowedRadialMeanErrorWorld = 0.20f;
        private const float MaxAllowedRadialMaxErrorWorld = 0.55f;
        private const int DeterminismIterations = 20;

        private static readonly string ArtifactRoot = Path.Combine(
            Directory.GetCurrentDirectory(),
            "TestResults",
            "FogRenderRegression");

        private static readonly BindingFlags InstancePrivate =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticPrivate =
            BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(ArtifactRoot);
                Debug.Log($"[FogRegressionBatch] Artifact root: {ArtifactRoot}");

                var context = BuildScenario();
                try
                {
                    var edgeOutside = new Vector3(
                        context.ForestCenter.x - context.ForestHalfWidthWorld - CombatScale.InchesToWorldUnits(10f),
                        0f,
                        context.ForestCenter.z);
                    var edgeOnBoundary = new Vector3(
                        context.ForestCenter.x - context.ForestHalfWidthWorld + 0.01f,
                        0f,
                        context.ForestCenter.z);
                    var insideNearEdge = new Vector3(
                        context.ForestCenter.x - context.ForestHalfWidthWorld + CombatScale.InchesToWorldUnits(1f),
                        0f,
                        context.ForestCenter.z);
                    var insideAtDepthLimit = new Vector3(
                        context.ForestCenter.x - context.ForestHalfWidthWorld + CombatScale.InchesToWorldUnits(3f),
                        0f,
                        context.ForestCenter.z);
                    var insideDeep = new Vector3(
                        context.ForestCenter.x,
                        0f,
                        context.ForestCenter.z);

                    for (var iteration = 1; iteration <= DeterminismIterations; iteration++)
                    {
                        Warmup(context);
                        var iter = iteration.ToString("00");
                        VerifyPosition(context, $"outside-10in-iter{iter}", edgeOutside);
                        VerifyPosition(context, $"edge-iter{iter}", edgeOnBoundary);
                        VerifyPosition(context, $"inside-near-edge-iter{iter}", insideNearEdge);
                        VerifyPosition(context, $"inside-3in-iter{iter}", insideAtDepthLimit);
                        VerifyPosition(context, $"inside-center-iter{iter}", insideDeep);
                    }
                }
                finally
                {
                    context.Dispose();
                }

                Debug.Log("[FogRegressionBatch] PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FogRegressionBatch] FAIL: {ex}");
                EditorApplication.Exit(1);
            }
        }

        private static void Warmup(ScenarioContext context)
        {
            // Ensure active zone registry/cache are current before LOS clipping reads it.
            context.Revealer.SetCachedRayDistance();
            CombatForestFogClipper.EnsureCache();
            context.Revealer.GetComponent<CombatForestFogBlockerRing>()?.RebuildNow();
            context.FogWorld.RenderFogTexture();
            context.FogWorld.RenderFogTexture();
            context.Revealer.ManualCalculateLineOfSight();
            context.FogWorld.RenderFogTexture();
        }

        private static void VerifyPosition(ScenarioContext context, string label, Vector3 unitPosition)
        {
            context.Revealer.transform.position = unitPosition;
            context.Revealer.SetCachedRayDistance();
            CombatForestFogClipper.EnsureCache();
            context.Revealer.GetComponent<CombatForestFogBlockerRing>()?.RebuildNow();
            context.Revealer.ManualCalculateLineOfSight();
            context.FogWorld.RenderFogTexture();
            context.FogWorld.RenderFogTexture();

            var contourPointCount = GetRevealerContourPointCount(context.Revealer);
            if (contourPointCount <= 0)
            {
                throw new InvalidOperationException($"{label}: manual forest contour produced zero points.");
            }

            var expectedMaxRadius = GetExpectedMaxRadius(context.Revealer);
            var expectedRadii = BuildExpectedRadii(context, unitPosition, expectedMaxRadius);
            ValidateExpectedRadiiSanity(label, expectedRadii, expectedMaxRadius);
            var actualRadii = BuildActualRadiiFromRevealer(context.Revealer, unitPosition, expectedMaxRadius);
            var actualMask = BuildMaskFromRadii(context, unitPosition, actualRadii);
            var expectedMask = BuildMaskFromRadii(context, unitPosition, expectedRadii);
            var metrics = ComputeMaskMetrics(actualMask, expectedMask);

            WriteArtifacts(label, actualMask, expectedMask, metrics.DiffMask);
            WriteMetrics(label, metrics);
            ValidateContourShape(label, actualRadii, expectedRadii);

            Debug.Log(
                $"[FogRegressionBatch] {label}: coverage={metrics.CoverageError:F4} mae={metrics.MeanAbsoluteError:F4}");

            if (metrics.CoverageError > MaxAllowedCoverageError)
            {
                throw new InvalidOperationException(
                    $"{label}: coverage error too high ({metrics.CoverageError:F4} > {MaxAllowedCoverageError:F4}).");
            }

            if (metrics.MeanAbsoluteError > MaxAllowedMeanAbsError)
            {
                throw new InvalidOperationException(
                    $"{label}: mean absolute error too high ({metrics.MeanAbsoluteError:F4} > {MaxAllowedMeanAbsError:F4}).");
            }
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
            EnsureFogTextureForEditorBatch(fogWorld);

            var forestFeature = ScriptableObject.CreateInstance<CombatTerrainFeatureDefinition>();
            SetPrivateField(forestFeature, "featureId", "TestForest");
            SetPrivateField(forestFeature, "displayName", "Test Forest");
            SetPrivateField(forestFeature, "lineOfSightMode", CombatTerrainLineOfSightMode.LimitedDepth);
            SetPrivateField(forestFeature, "lineOfSightPassThroughDepthInches", 3f);

            var forestGo = new GameObject("ForestZone");
            forestGo.SetActive(false);
            forestGo.transform.SetParent(rootTransform, false);
            forestGo.transform.position = new Vector3(0f, 1f, 0f);
            var forestCollider = forestGo.AddComponent<BoxCollider>();
            forestCollider.isTrigger = true;
            forestCollider.size = new Vector3(20f, 2f, 16f);
            var forestZone = forestGo.AddComponent<CombatZone>();
            SetPrivateField(forestZone, "terrainFeature", forestFeature);
            forestGo.SetActive(true);
            Physics.SyncTransforms();
            forestZone.EnsureRegistered();
            CombatForestFogClipper.EnsureCache();
            if (!CombatForestFogClipper.HasActiveZones)
            {
                CombatForestFogClipper.SeedCachedZoneFromBounds(
                    forestCollider.bounds,
                    CombatScale.InchesToWorldUnits(3f));
            }

            var revealerGo = new GameObject("Revealer");
            revealerGo.transform.SetParent(rootTransform, false);
            revealerGo.transform.position = new Vector3(-12f, 0f, 0f);
            var revealer = revealerGo.AddComponent<CombatFogOfWarRevealer3D>();
            ConfigureRevealer(revealer);
            // In batch editor execution (outside Play Mode), OnEnable registration timing can vary.
            // Force registration so CachedTransform/EyePosition are valid before manual LOS calls.
            revealer.RegisterRevealer();

            return new ScenarioContext(
                roots,
                fogWorld,
                forestFeature,
                forestZone,
                revealer,
                forestCollider.bounds.center,
                forestCollider.bounds.extents.x,
                forestCollider.bounds.min,
                forestCollider.bounds.max);
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
            // Mirror gameplay configuration for geometry parity with in-scene rendering.
            revealer.AddCorners = false;
            revealer.ResolveEdge = false;
            revealer.RaycastResolution = 0.5f;
            revealer.NumExtraIterations = 0;
            revealer.NumExtraRaysOnIteration = 0;
            revealer.ObstacleLayerMask = ~0;
            revealer.ConfigureForUnit(null);
            revealer.GetComponent<CombatForestFogBlockerRing>()?.RebuildNow();
        }

        private static bool[,] BuildActualMaskFromRevealerRays(ScenarioContext context, Vector3 origin, float maxRadius)
        {
            var rt = context.FogWorld.GetFOWRT();
            var mask = new bool[rt.width, rt.height];
            var capture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            capture.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0, false);
            capture.Apply(false, false);
            RenderTexture.active = previous;
            var pixels = capture.GetPixels();
            var polarityUsesOneMinusR = true;
            if (WorldToPixel(context.FogWorld, origin, out var ox, out var oy))
            {
                var center = pixels[(oy * rt.width) + ox];
                var centerOneMinusVisible = (1f - center.r) >= ForegroundThreshold;
                var centerDirectVisible = center.r >= ForegroundThreshold;

                // Probe a few far-away points near texture borders. These should usually be hidden.
                var farVisibleOneMinus = 0;
                var farVisibleDirect = 0;
                var farProbeCount = 0;
                var probes = new (int x, int y)[]
                {
                    (0, 0),
                    (rt.width - 1, 0),
                    (0, rt.height - 1),
                    (rt.width - 1, rt.height - 1),
                    (rt.width / 2, 0),
                    (rt.width / 2, rt.height - 1),
                    (0, rt.height / 2),
                    (rt.width - 1, rt.height / 2)
                };

                foreach (var p in probes)
                {
                    var wx = PixelToWorld(context.FogWorld, p.x, p.y);
                    var planar = new Vector2(wx.x - origin.x, wx.z - origin.z);
                    // Only count truly far probes so we don't accidentally sample near-revealer pixels.
                    if (planar.magnitude <= maxRadius * 1.5f)
                    {
                        continue;
                    }

                    farProbeCount++;
                    var c = pixels[(p.y * rt.width) + p.x];
                    if ((1f - c.r) >= ForegroundThreshold)
                    {
                        farVisibleOneMinus++;
                    }

                    if (c.r >= ForegroundThreshold)
                    {
                        farVisibleDirect++;
                    }
                }

                // Score each polarity: center should be visible, far probes should be mostly hidden.
                var oneMinusScore = 0;
                var directScore = 0;
                if (centerOneMinusVisible) oneMinusScore += 2;
                if (centerDirectVisible) directScore += 2;
                if (farProbeCount > 0)
                {
                    if (farVisibleOneMinus < farVisibleDirect) oneMinusScore++;
                    if (farVisibleDirect < farVisibleOneMinus) directScore++;
                }

                polarityUsesOneMinusR = oneMinusScore >= directScore;
            }

            for (var y = 0; y < rt.height; y++)
            {
                for (var x = 0; x < rt.width; x++)
                {
                    var color = pixels[(y * rt.width) + x];
                    var visible = polarityUsesOneMinusR ? (1f - color.r) : color.r;
                    mask[x, y] = visible >= ForegroundThreshold;
                }
            }

            UnityEngine.Object.DestroyImmediate(capture);
            return mask;
        }

        private static float[] BuildActualRadiiFromForestRule(Vector3 origin, float maxRadius)
        {
            var depthLimit = CombatScale.InchesToWorldUnits(3f);
            var radii = new float[RadialSamples];
            for (var i = 0; i < RadialSamples; i++)
            {
                var angle = (Mathf.PI * 2f * i) / RadialSamples;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var clip = CombatForestFogClipper.GetFirstContactDepthClipDistanceWorld(origin, direction, maxRadius, depthLimit);
                radii[i] = Mathf.Clamp(clip, 0f, maxRadius);
            }

            return radii;
        }

        private static int GetRevealerContourPointCount(CombatFogOfWarRevealer3D revealer)
        {
            var numPointsField = typeof(FOW.FogOfWarRevealer).GetField("NumberOfPoints", InstanceAny);
            return numPointsField == null ? 0 : (int)numPointsField.GetValue(revealer);
        }

        private static float[] BuildActualRadiiFromRevealer(CombatFogOfWarRevealer3D revealer, Vector3 origin, float fallbackMaxRadius)
        {
            var radii = new float[RadialSamples];
            for (var i = 0; i < radii.Length; i++)
            {
                radii[i] = float.NaN;
            }

            var fogRevealerType = typeof(FOW.FogOfWarRevealer);
            var numPointsField = fogRevealerType.GetField("NumberOfPoints", InstanceAny);
            var outputDirectionsField = fogRevealerType.GetField("OutputDirections", InstanceAny);
            var outputDistancesField = fogRevealerType.GetField("OutputDistances", InstanceAny);
            if (numPointsField == null || outputDirectionsField == null || outputDistancesField == null)
            {
                return radii;
            }

            var numberOfPoints = (int)numPointsField.GetValue(revealer);
            var outputDirections = outputDirectionsField.GetValue(revealer) as Array;
            var outputDistances = outputDistancesField.GetValue(revealer) as float[];
            if (numberOfPoints <= 0 || outputDirections == null || outputDistances == null)
            {
                return radii;
            }

            var count = Mathf.Min(numberOfPoints, Mathf.Min(outputDirections.Length, outputDistances.Length));
            for (var bin = 0; bin < RadialSamples; bin++)
            {
                var targetAngle = (Mathf.PI * 2f * bin) / RadialSamples;
                var bestDistance = float.NaN;
                var bestAngleDelta = float.MaxValue;

                for (var i = 0; i < count; i++)
                {
                    var dirObj = outputDirections.GetValue(i);
                    if (!TryExtractDirectionXY(dirObj, out var dirX, out var dirY))
                    {
                        continue;
                    }

                    var dirSq = (dirX * dirX) + (dirY * dirY);
                    if (dirSq <= 1e-8f)
                    {
                        continue;
                    }

                    var rayAngle = Mathf.Atan2(dirY, dirX);
                    var angleDelta = Mathf.Abs(Mathf.DeltaAngle(
                        rayAngle * Mathf.Rad2Deg,
                        targetAngle * Mathf.Rad2Deg));
                    if (angleDelta >= bestAngleDelta)
                    {
                        continue;
                    }

                    var encodedDistance = outputDistances[i];
                    var decodedDistance = encodedDistance > fallbackMaxRadius + 0.5f
                        ? encodedDistance - 1f
                        : encodedDistance;
                    var value = Mathf.Clamp(decodedDistance, 0f, fallbackMaxRadius);
                    if (value <= 0.0001f)
                    {
                        continue;
                    }

                    bestAngleDelta = angleDelta;
                    bestDistance = value;
                }

                if (!float.IsNaN(bestDistance))
                {
                    radii[bin] = bestDistance;
                }
            }

            FillMissingRadiiCircular(radii, fallbackMaxRadius);
            return radii;
        }

        private static bool TryExtractDirectionXY(object directionValue, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (directionValue == null)
            {
                return false;
            }

            var type = directionValue.GetType();
            var xField = type.GetField("x", InstanceAny);
            var yField = type.GetField("y", InstanceAny);
            if (xField == null || yField == null)
            {
                return false;
            }

            x = Convert.ToSingle(xField.GetValue(directionValue));
            y = Convert.ToSingle(yField.GetValue(directionValue));
            return true;
        }

        private static void FillMissingRadiiCircular(float[] radii, float fallback)
        {
            var len = radii.Length;
            var known = 0;
            for (var i = 0; i < len; i++)
            {
                if (!float.IsNaN(radii[i]))
                {
                    known++;
                }
            }

            if (known == 0)
            {
                for (var i = 0; i < len; i++)
                {
                    radii[i] = fallback;
                }

                return;
            }

            for (var i = 0; i < len; i++)
            {
                if (!float.IsNaN(radii[i]))
                {
                    continue;
                }

                var prev = (i - 1 + len) % len;
                while (float.IsNaN(radii[prev]))
                {
                    prev = (prev - 1 + len) % len;
                }

                var next = (i + 1) % len;
                while (float.IsNaN(radii[next]))
                {
                    next = (next + 1) % len;
                }

                var prevRadius = radii[prev];
                var nextRadius = radii[next];
                var span = (next - prev + len) % len;
                if (span == 0)
                {
                    radii[i] = prevRadius;
                    continue;
                }

                var t = ((i - prev + len) % len) / (float)span;
                radii[i] = Mathf.Lerp(prevRadius, nextRadius, t);
            }
        }

        private static void SmoothRadiiCircular(float[] radii)
        {
            if (radii == null || radii.Length < 3)
            {
                return;
            }

            var original = new float[radii.Length];
            for (var i = 0; i < radii.Length; i++)
            {
                original[i] = radii[i];
            }

            var smoothed = new float[radii.Length];
            for (var i = 0; i < radii.Length; i++)
            {
                var prev = original[(i - 1 + radii.Length) % radii.Length];
                var curr = original[i];
                var next = original[(i + 1) % radii.Length];

                // Preserve sharp boundary exits/entries; only smooth when local variation is small.
                var edgeJump = Mathf.Max(Mathf.Abs(curr - prev), Mathf.Abs(curr - next));
                if (edgeJump > 1.0f)
                {
                    smoothed[i] = curr;
                    continue;
                }

                // Do not smooth across the circular seam (359 <-> 0). This can smear
                // a clipped boundary into neighboring bins and produce false overreach.
                if (i == 0 || i == radii.Length - 1)
                {
                    smoothed[i] = curr;
                    continue;
                }

                // Light smoothing for isolated single-bin noise.
                smoothed[i] = (prev + (curr * 2f) + next) * 0.25f;
            }

            for (var i = 0; i < radii.Length; i++)
            {
                radii[i] = smoothed[i];
            }

            const float maxAdjacentStep = 0.35f;
            for (var i = 0; i < radii.Length; i++)
            {
                var prev = radii[(i - 1 + radii.Length) % radii.Length];
                var curr = radii[i];
                var next = radii[(i + 1) % radii.Length];
                var neighborMin = Mathf.Min(prev, next);
                var dipAmount = neighborMin - curr;
                if (dipAmount > maxAdjacentStep)
                {
                    // Prevent isolated one-bin dips that produce artificial wedge spikes.
                    radii[i] = neighborMin - maxAdjacentStep;
                }
            }
        }

        private static float SampleRadiusInterpolated(float[] radii, float angleRadians)
        {
            if (radii == null || radii.Length == 0)
            {
                return 0f;
            }

            var normalized = angleRadians / (Mathf.PI * 2f);
            normalized -= Mathf.Floor(normalized);
            var sample = normalized * radii.Length;
            var i0 = Mathf.FloorToInt(sample) % radii.Length;
            var i1 = (i0 + 1) % radii.Length;
            var t = sample - Mathf.Floor(sample);
            return Mathf.Lerp(radii[i0], radii[i1], t);
        }

        private static bool[,] InvertMask(bool[,] mask)
        {
            var width = mask.GetLength(0);
            var height = mask.GetLength(1);
            var inverted = new bool[width, height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    inverted[x, y] = !mask[x, y];
                }
            }

            return inverted;
        }

        private static void EnsureFogTextureForEditorBatch(FOW.FogOfWarWorld world)
        {
            if (world.GetFOWRT() != null)
            {
                return;
            }

            var worldType = typeof(FOW.FogOfWarWorld);
            var rtField = worldType.GetField("FOW_RT", StaticPrivate);
            if (rtField == null)
            {
                throw new MissingFieldException(worldType.Name, "FOW_RT");
            }

            var rt = new RenderTexture(
                world.FowResX,
                world.FowResY,
                0,
                FOW.FogOfWarWorld.renderTextureFormat,
                RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1,
                useMipMap = false,
                antiAliasing = 1
            };
            rt.Create();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(1f - world.InitialFogExplorationValue, 0f, 0f, 1f - world.InitialFogExplorationValue));
            RenderTexture.active = previous;

            rtField.SetValue(null, rt);
        }

        private static float[] BuildExpectedRadii(ScenarioContext context, Vector3 origin, float maxRadius)
        {
            var depthLimit = CombatScale.InchesToWorldUnits(3f);
            var radiusByAngle = new float[RadialSamples];

            for (var angleIndex = 0; angleIndex < RadialSamples; angleIndex++)
            {
                var angle = (Mathf.PI * 2f * angleIndex) / RadialSamples;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var clipDistance = ComputeExpectedClipDistanceAgainstForestBox(
                    origin,
                    direction,
                    maxRadius,
                    depthLimit,
                    context.ForestBoundsMin,
                    context.ForestBoundsMax);
                radiusByAngle[angleIndex] = Mathf.Clamp(clipDistance, 0f, maxRadius);
            }

            return radiusByAngle;
        }

        private static float GetExpectedMaxRadius(CombatFogOfWarRevealer3D revealer)
        {
            // In batch/editor execution, TotalRevealerRadius can be stale/zero before runtime updates.
            // Use configured ViewRadius as the source of truth for expected geometry.
            var maxRadius = Mathf.Max(revealer.ViewRadius, revealer.TotalRevealerRadius);
            if (maxRadius <= 0.001f)
            {
                throw new InvalidOperationException(
                    $"Revealer max radius is not configured (ViewRadius={revealer.ViewRadius:F3}, TotalRevealerRadius={revealer.TotalRevealerRadius:F3}).");
            }

            return maxRadius;
        }

        private static bool[,] BuildMaskFromRadii(ScenarioContext context, Vector3 origin, float[] radiusByAngle)
        {
            var rt = context.FogWorld.GetFOWRT();
            var mask = new bool[rt.width, rt.height];

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

                    var radiusAtAngle = SampleRadiusInterpolated(radiusByAngle, angle);
                    mask[x, y] = distance <= radiusAtAngle;
                }
            }

            return mask;
        }

        private static void ValidateExpectedRadiiSanity(string label, float[] radii, float maxRadius)
        {
            var clipped = 0;
            for (var i = 0; i < radii.Length; i++)
            {
                if (radii[i] < maxRadius - 0.01f)
                {
                    clipped++;
                }
            }

            if (label.StartsWith("outside-10in", StringComparison.Ordinal) && (clipped <= 0 || clipped >= radii.Length))
            {
                throw new InvalidOperationException($"{label}: expected mixed clipped/unclipped rays, got clipped={clipped}/{radii.Length}.");
            }

            if (label.StartsWith("inside-near-edge", StringComparison.Ordinal) && (clipped <= 0 || clipped >= radii.Length))
            {
                throw new InvalidOperationException($"{label}: expected near-edge mixed rays, got clipped={clipped}/{radii.Length}.");
            }

            if (label.StartsWith("inside-center", StringComparison.Ordinal) && clipped != radii.Length)
            {
                throw new InvalidOperationException($"{label}: expected all rays clipped in deep forest, got clipped={clipped}/{radii.Length}.");
            }
        }

        private static float ComputeExpectedClipDistanceAgainstForestBox(
            Vector3 origin,
            Vector3 planarDirection,
            float maxDistanceWorld,
            float depthLimitWorld,
            Vector3 forestMin,
            Vector3 forestMax)
        {
            if (!TryGetRayBoxIntervalXZ(origin, planarDirection, maxDistanceWorld, forestMin, forestMax, out var enter, out var exit))
            {
                return maxDistanceWorld;
            }

            var originInside = origin.x >= forestMin.x && origin.x <= forestMax.x && origin.z >= forestMin.z && origin.z <= forestMax.z;
            if (originInside)
            {
                return exit <= depthLimitWorld ? maxDistanceWorld : Mathf.Min(maxDistanceWorld, depthLimitWorld);
            }

            var outsideDepthClip = enter + depthLimitWorld;
            var cannotSeePastExit = Mathf.Min(outsideDepthClip, exit);
            return Mathf.Clamp(cannotSeePastExit, 0f, maxDistanceWorld);
        }

        private static bool TryGetRayBoxIntervalXZ(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            Vector3 boxMin,
            Vector3 boxMax,
            out float enter,
            out float exit)
        {
            enter = 0f;
            exit = maxDistance;

            if (!SlabIntersect(origin.x, direction.x, boxMin.x, boxMax.x, ref enter, ref exit)
                || !SlabIntersect(origin.z, direction.z, boxMin.z, boxMax.z, ref enter, ref exit))
            {
                return false;
            }

            if (enter > exit || exit <= 0f || enter >= maxDistance)
            {
                return false;
            }

            enter = Mathf.Max(0f, enter);
            exit = Mathf.Min(maxDistance, exit);
            return exit > enter + 0.0001f;
        }

        private static bool SlabIntersect(
            float origin,
            float direction,
            float min,
            float max,
            ref float enter,
            ref float exit)
        {
            if (Mathf.Abs(direction) <= 1e-6f)
            {
                return origin >= min && origin <= max;
            }

            var inverseDirection = 1f / direction;
            var t0 = (min - origin) * inverseDirection;
            var t1 = (max - origin) * inverseDirection;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            if (t0 > enter)
            {
                enter = t0;
            }

            if (t1 < exit)
            {
                exit = t1;
            }

            return enter <= exit;
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

        private static void WriteMetrics(string label, MaskMetrics metrics)
        {
            var labelDir = Path.Combine(ArtifactRoot, label);
            Directory.CreateDirectory(labelDir);
            var reportPath = Path.Combine(labelDir, "metrics.txt");
            File.WriteAllText(
                reportPath,
                $"CoverageError={metrics.CoverageError:F6}{Environment.NewLine}MeanAbsoluteError={metrics.MeanAbsoluteError:F6}{Environment.NewLine}");
        }

        private static void ValidateContourShape(string label, float[] actualRadii, float[] expectedRadii)
        {
            var maxStep = MaxAngularStep(actualRadii, out var stepIndex, out var stepA, out var stepB);
            if (maxStep > MaxAllowedAngularRadiusStepWorld)
            {
                throw new InvalidOperationException(
                    $"{label}: contour wedge detected (max angular radius step {maxStep:F3} > {MaxAllowedAngularRadiusStepWorld:F3}) " +
                    $"at bin {stepIndex} ({stepA:F3}->{stepB:F3}).");
            }

            var radialErrorSum = 0f;
            var radialMaxError = 0f;
            for (var i = 0; i < RadialSamples; i++)
            {
                var err = Mathf.Abs(actualRadii[i] - expectedRadii[i]);
                radialErrorSum += err;
                radialMaxError = Mathf.Max(radialMaxError, err);
            }

            var radialMeanError = radialErrorSum / RadialSamples;
            if (radialMeanError > MaxAllowedRadialMeanErrorWorld || radialMaxError > MaxAllowedRadialMaxErrorWorld)
            {
                var worstIdx = 0;
                var worstErr = 0f;
                for (var i = 0; i < RadialSamples; i++)
                {
                    var err = Mathf.Abs(actualRadii[i] - expectedRadii[i]);
                    if (err > worstErr)
                    {
                        worstErr = err;
                        worstIdx = i;
                    }
                }

                var prevIdx = (worstIdx - 1 + RadialSamples) % RadialSamples;
                var nextIdx = (worstIdx + 1) % RadialSamples;
                throw new InvalidOperationException(
                    $"{label}: radial contour mismatch (mean={radialMeanError:F3}, max={radialMaxError:F3}) exceeds limits (mean<={MaxAllowedRadialMeanErrorWorld:F3}, max<={MaxAllowedRadialMaxErrorWorld:F3}). " +
                    $"worstBin={worstIdx} err={worstErr:F3} actual[{prevIdx},{worstIdx},{nextIdx}]=[{actualRadii[prevIdx]:F3},{actualRadii[worstIdx]:F3},{actualRadii[nextIdx]:F3}] " +
                    $"expected[{prevIdx},{worstIdx},{nextIdx}]=[{expectedRadii[prevIdx]:F3},{expectedRadii[worstIdx]:F3},{expectedRadii[nextIdx]:F3}]");
            }
        }

        private static float[] SampleContourRadii(
            RenderTexture rt,
            bool[,] mask,
            int originPixelX,
            int originPixelY,
            float unitsPerPixel)
        {
            var radii = new float[RadialSamples];
            for (var i = 0; i < RadialSamples; i++)
            {
                var angle = (Mathf.PI * 2f * i) / RadialSamples;
                var dx = Mathf.Cos(angle);
                var dy = Mathf.Sin(angle);

                var radiusPixels = 0f;
                for (var step = 0; step < Mathf.Max(rt.width, rt.height); step++)
                {
                    var px = Mathf.RoundToInt(originPixelX + (dx * step));
                    var py = Mathf.RoundToInt(originPixelY + (dy * step));
                    if (px < 0 || py < 0 || px >= rt.width || py >= rt.height)
                    {
                        break;
                    }

                    if (!mask[px, py])
                    {
                        break;
                    }

                    radiusPixels = step;
                }

                radii[i] = radiusPixels * unitsPerPixel;
            }

            return radii;
        }

        private static float MaxAngularStep(float[] radii, out int maxIndex, out float maxA, out float maxB)
        {
            var maxStep = 0f;
            maxIndex = 0;
            maxA = 0f;
            maxB = 0f;
            for (var i = 0; i < radii.Length; i++)
            {
                var prev = radii[(i - 1 + radii.Length) % radii.Length];
                var current = radii[i];
                var next = radii[(i + 1) % radii.Length];
                // Reduce one-bin stair-step noise from pixel quantization by comparing
                // locally smoothed radius values instead of raw adjacent bins.
                var smoothCurrent = Median3(prev, current, next);
                var nextIndex = (i + 1) % radii.Length;
                var nextPrev = current;
                var nextCurrent = radii[nextIndex];
                var nextNext = radii[(nextIndex + 1) % radii.Length];
                var smoothNext = Median3(nextPrev, nextCurrent, nextNext);
                var step = Mathf.Abs(smoothNext - smoothCurrent);
                if (step > maxStep)
                {
                    maxStep = step;
                    maxIndex = i;
                    maxA = smoothCurrent;
                    maxB = smoothNext;
                }
            }

            return maxStep;
        }

        private static float Median3(float a, float b, float c)
        {
            if (a > b)
            {
                (a, b) = (b, a);
            }

            if (b > c)
            {
                (b, c) = (c, b);
            }

            if (a > b)
            {
                (a, b) = (b, a);
            }

            return b;
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
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, InstancePrivate);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().Name, fieldName);
            }

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
            public readonly Vector3 ForestBoundsMin;
            public readonly Vector3 ForestBoundsMax;

            public ScenarioContext(
                GameObject root,
                FOW.FogOfWarWorld fogWorld,
                CombatTerrainFeatureDefinition forestFeature,
                CombatZone forestZone,
                CombatFogOfWarRevealer3D revealer,
                Vector3 forestCenter,
                float forestHalfWidthWorld,
                Vector3 forestBoundsMin,
                Vector3 forestBoundsMax)
            {
                Root = root;
                FogWorld = fogWorld;
                ForestFeature = forestFeature;
                ForestZone = forestZone;
                Revealer = revealer;
                ForestCenter = forestCenter;
                ForestHalfWidthWorld = forestHalfWidthWorld;
                ForestBoundsMin = forestBoundsMin;
                ForestBoundsMax = forestBoundsMax;
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
