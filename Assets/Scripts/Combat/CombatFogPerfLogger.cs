using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using FOW;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Aggregates fog/LOS timings for copy-paste editor console reports while pathing.
    /// Enable via CombatBootstrap or the fog debug GUI.
    /// </summary>
    public static class CombatFogPerfLogger
    {
        private struct SampleAccumulator
        {
            public long TotalTicks;
            public int Count;
            public long MaxTicks;
        }

        private static readonly Dictionary<string, SampleAccumulator> Samples = new();
        private static readonly Dictionary<string, long> Counters = new();
        private static readonly Dictionary<string, string> LastContext = new();

        private static long updatePhase1StartTicks;
        private static long lateUpdateStartTicks;
        private static int frameCount;
        private static int movingFrameCount;
        private static bool anyPawnMovingThisFrame;
        private static double windowStartRealtime;
        private static string lastReport = string.Empty;

        public static bool Enabled { get; set; }

        public static float ReportIntervalSeconds { get; set; } = 2f;

        public static bool ReportOnlyWhileMoving { get; set; } = true;

        public static string LastReport => lastReport;

        public static string LastReportFilePath { get; private set; }

        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (!enabled)
            {
                return;
            }

            ResetWindow();
            Debug.Log(
                "[CombatFogPerf] Enabled — reports every "
                + ReportIntervalSeconds.ToString("0.#")
                + "s while moving. Copy console blocks tagged [CombatFogPerf] or read "
                + GetReportFilePath());
        }

        public readonly struct Scope : System.IDisposable
        {
            private readonly string sampleName;
            private readonly long startTicks;
            private readonly bool active;

            public Scope(string sampleName, long startTicks, bool active)
            {
                this.sampleName = sampleName;
                this.startTicks = startTicks;
                this.active = active;
            }

            public void Dispose()
            {
                if (!active)
                {
                    return;
                }

                RecordSample(sampleName, startTicks);
            }
        }

        public static Scope Measure(string sampleName)
        {
            if (!Enabled)
            {
                return default;
            }

            return new Scope(sampleName, Stopwatch.GetTimestamp(), true);
        }

        public static void RecordSample(string sampleName, long startTicks)
        {
            if (!Enabled || string.IsNullOrEmpty(sampleName))
            {
                return;
            }

            var elapsed = Stopwatch.GetTimestamp() - startTicks;
            if (elapsed < 0)
            {
                return;
            }

            if (!Samples.TryGetValue(sampleName, out var acc))
            {
                acc = default;
            }

            acc.TotalTicks += elapsed;
            acc.Count++;
            if (elapsed > acc.MaxTicks)
            {
                acc.MaxTicks = elapsed;
            }

            Samples[sampleName] = acc;
        }

        public static void IncrementCounter(string counterName, int amount = 1)
        {
            if (!Enabled || string.IsNullOrEmpty(counterName) || amount == 0)
            {
                return;
            }

            Counters.TryGetValue(counterName, out var value);
            Counters[counterName] = value + amount;
        }

        public static void SetContext(string key, string value)
        {
            if (!Enabled || string.IsNullOrEmpty(key))
            {
                return;
            }

            LastContext[key] = value ?? string.Empty;
        }

        public static void NotifyPawnMoving(bool isMoving)
        {
            if (!Enabled)
            {
                return;
            }

            if (isMoving)
            {
                anyPawnMovingThisFrame = true;
            }
        }

        public static void BeginWorldUpdatePhase1()
        {
            if (!Enabled)
            {
                return;
            }

            updatePhase1StartTicks = Stopwatch.GetTimestamp();
        }

        public static void EndWorldUpdatePhase1()
        {
            if (!Enabled)
            {
                return;
            }

            RecordSample("fow.update.phase1_all", updatePhase1StartTicks);
        }

        public static void BeginWorldLateUpdate()
        {
            if (!Enabled)
            {
                return;
            }

            lateUpdateStartTicks = Stopwatch.GetTimestamp();
        }

        public static void EndWorldLateUpdate(FogOfWarWorld world)
        {
            if (!Enabled)
            {
                return;
            }

            RecordSample("fow.lateupdate.total", lateUpdateStartTicks);

            if (world != null)
            {
                SetContext("fow.activeRevealers", FogOfWarWorld.NumActiveRevealers.ToString());
                SetContext("fow.dynamicRevealers", FogOfWarWorld.numDynamicRevealers.ToString());
                SetContext("fow.revealersPerFrame", world.MaxNumRevealersPerFrame.ToString());
                SetContext("fow.updateMethod", world.UpdateMethod.ToString());
                SetContext("fow.revealerMode", world.RevealerUpdateMode.ToString());
            }

            frameCount++;
            if (anyPawnMovingThisFrame)
            {
                movingFrameCount++;
            }

            TryEmitReport(world);
            anyPawnMovingThisFrame = false;
        }

        public static void FlushReportNow()
        {
            if (!Enabled)
            {
                Debug.Log("[CombatFogPerf] Logging is disabled.");
                return;
            }

            EmitReport(FogOfWarWorld.instance, force: true);
        }

        private static void TryEmitReport(FogOfWarWorld world)
        {
            if (windowStartRealtime <= 0d)
            {
                windowStartRealtime = Time.realtimeSinceStartupAsDouble;
                return;
            }

            var elapsed = Time.realtimeSinceStartupAsDouble - windowStartRealtime;
            if (elapsed < ReportIntervalSeconds)
            {
                return;
            }

            EmitReport(world, force: false);
        }

        private static void EmitReport(FogOfWarWorld world, bool force)
        {
            var windowSeconds = Mathf.Max(0.001f, (float)(Time.realtimeSinceStartupAsDouble - windowStartRealtime));

            if (!force && ReportOnlyWhileMoving && movingFrameCount == 0)
            {
                Debug.Log(
                    "[CombatFogPerf] No moving frames in the last "
                    + windowSeconds.ToString("0.#")
                    + "s — path a unit while logging, or click Flush fog perf report now.");
                ResetWindow();
                return;
            }

            var sb = new StringBuilder(2048);
            sb.AppendLine("[CombatFogPerf] ========== report (copy below) ==========");
            sb.Append("window=").Append(windowSeconds.ToString("0.00")).Append("s");
            sb.Append(" frames=").Append(frameCount);
            sb.Append(" movingFrames=").Append(movingFrameCount);
            sb.Append(" avgFps=").Append((frameCount / windowSeconds).ToString("0.0"));
            sb.AppendLine();

            AppendSettingsLine(sb);
            AppendContextLine(sb, world);
            AppendCounterSection(sb);
            AppendSampleSection(sb);

            lastReport = sb.ToString().TrimEnd();
            WriteReportToFile(lastReport);
            Debug.Log(lastReport);
            ResetWindow();
        }

        private static string GetReportFilePath()
        {
            var projectRoot = Application.dataPath;
            if (!string.IsNullOrEmpty(projectRoot)
                && (projectRoot.EndsWith("/Assets") || projectRoot.EndsWith("\\Assets")))
            {
                projectRoot = projectRoot.Substring(0, projectRoot.Length - "/Assets".Length);
            }

            return System.IO.Path.Combine(projectRoot, "Logs", "CombatFogPerf-latest.txt");
        }

        private static void WriteReportToFile(string report)
        {
            try
            {
                var path = GetReportFilePath();
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                System.IO.File.WriteAllText(path, report + System.Environment.NewLine);
                LastReportFilePath = path;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CombatFogPerf] Could not write report file: " + ex.Message);
            }
        }

        private static void AppendSettingsLine(StringBuilder sb)
        {
            sb.Append("settings: forestPass=").Append(CombatForestFogPassSettings.UseForestPass);
            sb.Append(" movingProfile=").Append(CombatForestFogPassSettings.EnableMovingPerfProfile);
            sb.Append(" wallRay=").Append(CombatForestFogPassSettings.WallRaycastResolutionDegrees.ToString("0.##")).Append("deg");
            sb.Append(" wallRayTerrain=").Append(CombatForestFogPassSettings.UseWallRayDirectionsWhileMoving);
            sb.Append(" movingHz=").Append(CombatForestFogPassSettings.MovingLineOfSightTargetHz.ToString("0.#"));
            sb.Append(" openGroundSkip=").Append(CombatForestFogPassSettings.SkipFullLineOfSightInOpenGroundWhileMoving);
            sb.Append(" wallRayNearOnly=").Append(CombatForestFogPassSettings.UseWallRayTerrainOnlyNearZonesWhileMoving);
            sb.Append(" movingLut=").Append(CombatForestFogPassSettings.MovingTerrainLutSamples);
            sb.Append(" coarseWall=").Append(CombatForestFogPassSettings.UseMovingWallRaycastResolution);
            sb.AppendLine();
        }

        private static void AppendContextLine(StringBuilder sb, FogOfWarWorld world)
        {
            if (LastContext.Count > 0)
            {
                sb.Append("lastRevealer: ");
                var first = true;
                foreach (var pair in LastContext)
                {
                    if (pair.Key.StartsWith("fow."))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(" | ");
                    }

                    first = false;
                    sb.Append(pair.Key).Append('=').Append(pair.Value);
                }

                sb.AppendLine();
            }

            if (world != null)
            {
                sb.Append("fowWorld: activeRevealers=").Append(FogOfWarWorld.NumActiveRevealers);
                sb.Append(" dynamic=").Append(FogOfWarWorld.numDynamicRevealers);
                sb.Append(" perFrame=").Append(world.MaxNumRevealersPerFrame);
                sb.Append(" mode=").Append(world.RevealerUpdateMode);
                sb.Append(" update=").Append(world.UpdateMethod);
                sb.AppendLine();
            }
        }

        private static void AppendCounterSection(StringBuilder sb)
        {
            if (Counters.Count == 0)
            {
                return;
            }

            sb.AppendLine("counts:");
            foreach (var pair in Counters)
            {
                sb.Append("  ").Append(pair.Key).Append('=').Append(pair.Value).AppendLine();
            }
        }

        private static void AppendSampleSection(StringBuilder sb)
        {
            if (Samples.Count == 0)
            {
                sb.AppendLine("samples: (none recorded)");
                return;
            }

            sb.AppendLine("samples (avg ms | max ms | calls | % of fow.lateupdate):");
            Samples.TryGetValue("fow.lateupdate.total", out var totalLate);
            var totalLateMs = TicksToMs(totalLate.TotalTicks);

            var keys = new List<string>(Samples.Keys);
            keys.Sort();

            foreach (var key in keys)
            {
                var acc = Samples[key];
                if (acc.Count <= 0)
                {
                    continue;
                }

                var avgMs = TicksToMs(acc.TotalTicks / acc.Count);
                var maxMs = TicksToMs(acc.MaxTicks);
                sb.Append("  ").Append(key);
                sb.Append(" avg=").Append(avgMs.ToString("0.###"));
                sb.Append("ms max=").Append(maxMs.ToString("0.###"));
                sb.Append("ms n=").Append(acc.Count);

                if (key != "fow.lateupdate.total" && totalLateMs > 0.001)
                {
                    var share = TicksToMs(acc.TotalTicks) / totalLateMs * 100f;
                    sb.Append(" share=").Append(share.ToString("0.#")).Append('%');
                }

                sb.AppendLine();
            }
        }

        private static void ResetWindow()
        {
            Samples.Clear();
            Counters.Clear();
            frameCount = 0;
            movingFrameCount = 0;
            windowStartRealtime = Time.realtimeSinceStartupAsDouble;
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }
    }
}
