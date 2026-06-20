using System.Collections.Generic;
using FOW;
using Pathfinding;
using System;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public partial class TestLevelUnitController
    {
        [ContextMenu("Spawn Units")]
        public void SpawnUnits()
        {
            CombatMatchSetup.RunMatchPhasesImmediate(this);
        }

        /// <summary>Stage: create movement/attack line renderers used during the match.</summary>
        public void PrepareMatchVisualizers()
        {
            BuildVisualizers();
        }

        /// <summary>Stage: clear prior pawns and spawn both armies at configured anchors.</summary>
        public void SpawnArmies()
        {
            CombatStartupLog.Log(
                $"SpawnArmies begin: playerDefs={playerUnits?.Count ?? 0}, enemyDefs={enemyUnits?.Count ?? 0}, "
                + $"playerAnchor={(playerSpawnAnchor != null ? playerSpawnAnchor.name : "null")}, "
                + $"enemyAnchor={(enemySpawnAnchor != null ? enemySpawnAnchor.name : "null")}.");

            ClearSpawnedUnits();
            SpawnArmy(playerUnits, playerSpawnAnchor, playerRuntimeUnits, true, new Color(0.2f, 0.5f, 1f), "Player");
            SpawnArmy(enemyUnits, enemySpawnAnchor, enemyRuntimeUnits, false, new Color(1f, 0.3f, 0.3f), "Enemy");
            losDirtyVersion++;

            CombatStartupLog.Log(
                $"SpawnArmies done: spawned player={playerRuntimeUnits.Count}, enemy={enemyRuntimeUnits.Count}.");
        }

        /// <summary>Stage: sync fog revealers and model visibility after all units exist.</summary>
        public void InitializeMatchVisibility()
        {
            CombatStartupLog.Log(
                $"InitializeMatchVisibility begin: playerRuntime={playerRuntimeUnits.Count}, "
                + $"fogWorld={(fogOfWarWorld != null ? fogOfWarWorld.name : "null")}.");
            try
            {
                RefreshAllPlayerFogVisionRules();
                MarkPlayerFogRevealerActivationDirty();
                UpdateUnitNavmeshCutActivation(GetPathingUnitForNavmeshClearance());
                CombatStartupLog.Log("InitializeMatchVisibility done.");
            }
            catch (Exception ex)
            {
                CombatStartupLog.LogException("InitializeMatchVisibility", ex);
                throw;
            }
        }

        private void RefreshAllPlayerFogVisionRules()
        {
            for (var i = 0; i < playerRuntimeUnits.Count; i++)
            {
                var unit = playerRuntimeUnits[i];
                var revealer = GetFogRevealer(unit);
                revealer?.ApplyVisionRulesFromUnit(unit);
            }
        }

        /// <summary>Stage: activate the first player turn once armies and visibility are ready.</summary>
        public void BeginMatch()
        {
            CombatStartupLog.Log("BeginMatch begin.");
            try
            {
                StartPlayerTurn();
                MarkPlayerFogRevealerActivationDirty();
                ForceSyncPlayerFogRevealerActivation();
                CombatStartupLog.Log(
                    $"BeginMatch done. activeTurn={activeTurnSide}, matchPhase={CombatMatchSetup.CurrentPhase}.");
            }
            catch (Exception ex)
            {
                CombatStartupLog.LogException("BeginMatch", ex);
                throw;
            }
        }

        private void ForceSyncPlayerFogRevealerActivation()
        {
            var fow = FogOfWarWorld.instance;
            if (fow != null && fow.IsInPhasedUpdate)
            {
                return;
            }

            if (SyncPlayerFogRevealerActivation())
            {
                UpdateFogOfWarVisibility();
            }
        }

        public bool IsMatchReady => CombatMatchSetup.CurrentPhase == CombatMatchSetupPhase.Ready;

        public int PlayerRuntimeUnitCount => playerRuntimeUnits.Count;

        public int EnemyRuntimeUnitCount => enemyRuntimeUnits.Count;

        public void SetSpawnAnchors(Transform playerAnchor, Transform enemyAnchor)
        {
            playerSpawnAnchor = playerAnchor;
            enemySpawnAnchor = enemyAnchor;
            CombatStartupLog.Log(
                $"SetSpawnAnchors: player={(playerAnchor != null ? $"{playerAnchor.name} @ {playerAnchor.position}" : "null")}, "
                + $"enemy={(enemyAnchor != null ? $"{enemyAnchor.name} @ {enemyAnchor.position}" : "null")}.");
        }

        /// <summary>
        /// Prevents <see cref="SpawnUnits"/> from being called automatically in Start.
        /// Call this from <see cref="CombatMapSetup"/> before the map scene has finished loading
        /// so that units are not spawned before their spawn-point anchors are resolved.
        /// </summary>
        public void LogSpawnDiagnostics(string context)
        {
            CombatStartupLog.Log(
                $"{context}: phase={CombatMatchSetup.CurrentPhase}, "
                + $"playerDefs={playerUnits?.Count ?? 0}, enemyDefs={enemyUnits?.Count ?? 0}, "
                + $"playerSpawned={playerRuntimeUnits.Count}, enemySpawned={enemyRuntimeUnits.Count}, "
                + $"playerAnchor={(playerSpawnAnchor != null ? $"{playerSpawnAnchor.name} @ {playerSpawnAnchor.position}" : "null")}, "
                + $"enemyAnchor={(enemySpawnAnchor != null ? $"{enemySpawnAnchor.name} @ {enemySpawnAnchor.position}" : "null")}.");
        }

        public void DisableAutoSpawn()
        {
            autoSpawnOnStart = false;
        }

        private void SpawnArmy(
            List<UnitTypeDefinition> units,
            Transform anchor,
            List<Unit> destination,
            bool isPlayerControlled,
            Color color,
            string armyLabel)
        {
            if (units == null)
            {
                CombatStartupLog.LogWarning($"SpawnArmy skipped '{armyLabel}': unit list is null.");
                return;
            }

            if (units.Count == 0)
            {
                CombatStartupLog.LogWarning($"SpawnArmy skipped '{armyLabel}': unit list is empty.");
                return;
            }

            EnsureMatchArmySpawnerAssigned();
            var placements = matchArmySpawner.BuildPlacements(units, anchor, spawnSpacing);
            CombatStartupLog.Log($"SpawnArmy '{armyLabel}': {placements.Count} placement(s) from {units.Count} definition(s).");

            for (var i = 0; i < placements.Count; i++)
            {
                var unitDefinition = placements[i].UnitDefinition;
                if (unitDefinition == null)
                {
                    CombatStartupLog.LogWarning($"SpawnArmy '{armyLabel}' slot {i}: null UnitTypeDefinition.");
                    continue;
                }

                unitDefinition.Stats.EnsureAdvantageDefaults();
                unitDefinition.Stats.EnsureAbilityDefaults();
                var pawnScale = unitDefinition.Stats.modelSize.GetPawnScale();
                var spawnPos = placements[i].Position;

                GameObject pawn;
                try
                {
                    if (unitDefinition.VisualPrefab != null)
                    {
                        pawn = Instantiate(unitDefinition.VisualPrefab, spawnPos, Quaternion.identity, transform);
                    }
                    else
                    {
                        pawn = BuildProceduralPawn(pawnScale, spawnPos);
                    }
                }
                catch (Exception ex)
                {
                    CombatStartupLog.LogException($"SpawnArmy '{armyLabel}' instantiate '{unitDefinition.DisplayName}'", ex);
                    continue;
                }

                pawn.name = $"{unitDefinition.DisplayName} ({(isPlayerControlled ? "Player" : "Enemy")})";
                ConfigurePawnForModelSize(pawn, unitDefinition.Stats.modelSize);
                CombatMapSceneProvider.MoveToMapScene(pawn);

                var baseColor = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f);
                ApplyPawnColor(pawn, color, baseColor);

                var pawnCollider = pawn.GetComponent<CapsuleCollider>();
                if (pawnCollider != null)
                {
                    pawnCollider.isTrigger = true;
                }

                ConfigureUnitNavmeshCut(pawn, pawnCollider, pawnScale);

                var unitPawn = pawn.GetComponent<UnitPawn>();
                if (unitPawn == null)
                {
                    unitPawn = pawn.AddComponent<UnitPawn>();
                }

                var unit = unitPawn.Bind(unitDefinition, isPlayerControlled);
                if (unit == null)
                {
                    CombatStartupLog.LogWarning(
                        $"SpawnArmy '{armyLabel}' slot {i}: UnitPawn.Bind returned null for '{unitDefinition.DisplayName}'.");
                    Destroy(pawn);
                    continue;
                }

                if (isPlayerControlled)
                {
                    try
                    {
                        ConfigurePlayerFogRevealer(pawn, pawnCollider, unit);
                    }
                    catch (Exception ex)
                    {
                        CombatStartupLog.LogException(
                            $"SpawnArmy '{armyLabel}' fog revealer '{unitDefinition.DisplayName}'",
                            ex);
                    }
                }

                unit.VisionRulesChanged += HandleUnitVisionRulesChanged;
                unit.SnapToNavmesh(navPathBuilder);
                destination.Add(unit);
                allRuntimeUnits.Add(unit);
                CombatStartupLog.Log(
                    $"SpawnArmy '{armyLabel}' slot {i}: spawned '{unitDefinition.DisplayName}' @ {spawnPos}.");
            }
        }

        private static void ApplyPawnColor(GameObject pawn, Color bodyColor, Color baseColor)
        {
            var bodyTransform = pawn.transform.Find("Body");
            var baseTransform = pawn.transform.Find("Base");
            if (bodyTransform != null)
            {
                var bodyRenderer = bodyTransform.GetComponent<Renderer>();
                if (bodyRenderer != null)
                {
                    bodyRenderer.material.color = bodyColor;
                }
            }

            if (baseTransform != null)
            {
                var baseRenderer = baseTransform.GetComponent<Renderer>();
                if (baseRenderer != null)
                {
                    baseRenderer.material.color = baseColor;
                }
            }
        }

        private void ConfigurePlayerFogRevealer(GameObject pawn, CapsuleCollider pawnCollider, Unit unit)
        {
            if (pawn == null || unit?.Definition == null)
            {
                return;
            }

            var unitDefinition = unit.Definition;
            var unitPawn = pawn.GetComponent<UnitPawn>();
            unitPawn?.ApplyAdditionalLoadoutTo(unit);

            CombatMapSceneProvider.MoveToMapScene(pawn);

            var wasActive = pawn.activeSelf;
            pawn.SetActive(false);

            var stats = unitDefinition.Stats;
            var revealer = pawn.GetComponent<CombatFogOfWarRevealer3D>();
            if (revealer == null)
            {
                revealer = pawn.AddComponent<CombatFogOfWarRevealer3D>();
            }

            revealer.StartRevealerAsStatic = false;
            revealer.UseOcclusion = true;
            // Forest depth is enforced by analytic clip in CombatFogOfWarRevealer3D phase 2.
            revealer.AddCorners = true;
            // ResolveEdge runs on raw physics first; forest subtractively trims open visibility afterward.
            revealer.ResolveEdge = true;
            revealer.OcclusionQuality = RaycastRevealer.RaycastRevealerOcclusionQualityPreset.Custom;
            // Wall baseline raycasts (independent of the 720-bin forest terrain LUT).
            revealer.RaycastResolution = CombatForestFogPassSettings.ResolveWallRaycastResolutionDegrees(false);
            // Forest depth is applied after stock edge resolve in CombatFogOfWarRevealer3D.
            revealer.NumExtraIterations = 0;
            revealer.NumExtraRaysOnIteration = 0;
            revealer.ObstacleLayerMask = CombatLayers.FogOccluderMask;
            revealer.ViewAngle = 360f;
            revealer.ViewRadius = CombatScale.InchesToWorldUnits(stats.visibilityRange);
            revealer.VisionHeight = stats.modelSize.VolumeHeightWorldUnits();
            revealer.EyeOffset = pawnCollider != null ? pawnCollider.height * 0.5f : 0f;

            pawn.SetActive(wasActive);
            revealer.ApplyVisionRulesFromUnit(unit);
        }

        private static void ConfigurePawnForModelSize(GameObject pawn, ModelSize modelSize)
        {
            if (pawn == null)
            {
                return;
            }

            var pawnScale = modelSize.GetPawnScale();
            var bodyTransform = pawn.transform.Find("Body");
            if (bodyTransform != null)
            {
                bodyTransform.localPosition = new Vector3(0f, pawnScale.y, 0f);
                bodyTransform.localScale = pawnScale;
            }

            var baseTransform = pawn.transform.Find("Base");
            if (baseTransform != null)
            {
                baseTransform.localPosition = new Vector3(0f, PawnBaseHeightScale, 0f);
                baseTransform.localScale = new Vector3(pawnScale.x, PawnBaseHeightScale, pawnScale.z);
            }

            var pawnCollider = pawn.GetComponent<CapsuleCollider>();
            if (pawnCollider == null)
            {
                pawnCollider = pawn.AddComponent<CapsuleCollider>();
            }

            pawnCollider.direction = 1;
            pawnCollider.center = new Vector3(0f, pawnScale.y, 0f);
            pawnCollider.radius = Mathf.Max(0.1f, pawnScale.x * 0.5f);
            pawnCollider.height = Mathf.Max(pawnCollider.radius * RadiusToDiameterMultiplier, pawnScale.y * RadiusToDiameterMultiplier);
            pawnCollider.isTrigger = true;
        }

        private void ConfigureUnitNavmeshCut(GameObject pawn, CapsuleCollider pawnCollider, Vector3 pawnScale)
        {
            if (pawn == null)
            {
                return;
            }

            var navmeshCut = pawn.GetComponent<NavmeshCut>();
            if (navmeshCut == null)
            {
                navmeshCut = pawn.AddComponent<NavmeshCut>();
            }

            var radius = pawnCollider != null
                ? pawnCollider.radius
                : Mathf.Max(0.1f, pawnScale.x * 0.5f);
            navmeshCut.type = NavmeshCut.MeshType.Circle;
            navmeshCut.circleRadius = radius;
            navmeshCut.circleResolution = UnitNavmeshCutCircleResolution;
            navmeshCut.height = Mathf.Max(UnitNavmeshCutMinimumHeight, pawnScale.y * NavmeshCutHeightMultiplier);
            navmeshCut.center = Vector3.zero;
            navmeshCut.isDual = false;
            navmeshCut.updateDistance = UnitNavmeshCutUpdateDistance;
            navmeshCut.radiusExpansionMode = NavmeshCut.RadiusExpansionMode.DontExpand;
            navmeshCut.useRotationAndScale = false;
        }

        private GameObject BuildProceduralPawn(Vector3 pawnScale, Vector3 spawnPos)
        {
            var root = new GameObject();
            root.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
            root.transform.SetParent(transform);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0f, pawnScale.y, 0f);
            body.transform.localScale = pawnScale;
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            var baseDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseDisk.name = "Base";
            baseDisk.transform.SetParent(root.transform);
            baseDisk.transform.localPosition = new Vector3(0f, PawnBaseHeightScale, 0f);
            baseDisk.transform.localScale = new Vector3(pawnScale.x, PawnBaseHeightScale, pawnScale.z);
            var baseCollider = baseDisk.GetComponent<Collider>();
            if (baseCollider != null)
            {
                baseCollider.enabled = false;
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.center = new Vector3(0f, pawnScale.y, 0f);
            col.radius = pawnScale.x * 0.5f;
            col.height = pawnScale.y * 2f;

            root.AddComponent<UnitPawn>();
            return root;
        }

        private void ClearSpawnedUnits()
        {
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var unit = allRuntimeUnits[i];
                unit.VisionRulesChanged -= HandleUnitVisionRulesChanged;
                if (unit.Pawn != null)
                {
                    var unitPawn = unit.Pawn.GetComponent<UnitPawn>();
                    unitPawn?.ClearRuntimeUnit();
                    Destroy(unit.Pawn);
                }
            }

            playerRuntimeUnits.Clear();
            enemyRuntimeUnits.Clear();
            allRuntimeUnits.Clear();
        }

        private void HandleUnitVisionRulesChanged(Unit unit)
        {
            losDirtyVersion++;
            if (unit != null && unit.IsPlayerControlled)
            {
                unit.RefreshFogRevealerConfiguration();
                MarkPlayerFogRevealerActivationDirty();
            }

            UpdateFogOfWarVisibility();
        }
    }
}
