using System.Collections.Generic;
using FOW;
using Pathfinding;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public partial class TestLevelUnitController
    {
        [ContextMenu("Spawn Units")]
        public void SpawnUnits()
        {
            BuildVisualizers();
            ClearSpawnedUnits();
            SpawnArmy(playerUnits, playerSpawnAnchor, playerRuntimeUnits, true, new Color(0.2f, 0.5f, 1f));
            SpawnArmy(enemyUnits, enemySpawnAnchor, enemyRuntimeUnits, false, new Color(1f, 0.3f, 0.3f));
            losDirtyVersion++;
            StartPlayerTurn();
            UpdateFogOfWarVisibility();
        }

        public void SetSpawnAnchors(Transform playerAnchor, Transform enemyAnchor)
        {
            playerSpawnAnchor = playerAnchor;
            enemySpawnAnchor = enemyAnchor;
        }

        /// <summary>
        /// Prevents <see cref="SpawnUnits"/> from being called automatically in Start.
        /// Call this from <see cref="CombatMapSetup"/> before the map scene has finished loading
        /// so that units are not spawned before their spawn-point anchors are resolved.
        /// </summary>
        public void DisableAutoSpawn()
        {
            autoSpawnOnStart = false;
        }

        private void SpawnArmy(List<UnitTypeDefinition> units, Transform anchor, List<RuntimeUnit> destination, bool isPlayerControlled, Color color)
        {
            if (units == null)
            {
                return;
            }

            EnsureMatchArmySpawnerAssigned();
            var placements = matchArmySpawner.BuildPlacements(units, anchor, spawnSpacing);
            for (var i = 0; i < placements.Count; i++)
            {
                var unitDefinition = placements[i].UnitDefinition;
                unitDefinition.Stats.EnsureAdvantageDefaults();
                unitDefinition.Stats.EnsureAbilityDefaults();
                var pawnScale = unitDefinition.Stats.modelSize.GetPawnScale();
                var spawnPos = placements[i].Position;

                GameObject pawn;
                if (unitDefinition.VisualPrefab != null)
                {
                    pawn = Instantiate(unitDefinition.VisualPrefab, spawnPos, Quaternion.identity, transform);
                }
                else
                {
                    pawn = BuildProceduralPawn(pawnScale, spawnPos);
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
                if (isPlayerControlled)
                {
                    ConfigurePlayerFogRevealer(pawn, pawnCollider, unitDefinition);
                }

                var runtimeUnit = new RuntimeUnit(unitDefinition, isPlayerControlled, pawn);
                SnapUnitToNavmesh(runtimeUnit);
                destination.Add(runtimeUnit);
                allRuntimeUnits.Add(runtimeUnit);
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

        private void ConfigurePlayerFogRevealer(GameObject pawn, CapsuleCollider pawnCollider, UnitTypeDefinition unitDefinition)
        {
            if (pawn == null || unitDefinition == null)
            {
                return;
            }

            CombatMapSceneProvider.MoveToMapScene(pawn);

            var wasActive = pawn.activeSelf;
            pawn.SetActive(false);

            var stats = unitDefinition.Stats;
            var revealer = pawn.GetComponent<CombatFogOfWarRevealer3D>();
            if (revealer == null)
            {
                revealer = pawn.AddComponent<CombatFogOfWarRevealer3D>();
            }
            revealer.ConfigureForUnit(unitDefinition);
            revealer.StartRevealerAsStatic = false;
            revealer.UseOcclusion = true;
            // Forest depth is enforced by analytic clip in CombatFogOfWarRevealer3D phase 2.
            revealer.AddCorners = true;
            revealer.ResolveEdge = false;
            revealer.OcclusionQuality = RaycastRevealer.RaycastRevealerOcclusionQualityPreset.HighResolution;
            revealer.RaycastResolution = 1f;
            // Extra edge refinement rays are tuned for hard collider silhouettes and can
            // distort depth-capped forest circles into wedges/half-arcs.
            revealer.NumExtraIterations = 0;
            revealer.NumExtraRaysOnIteration = 0;
            revealer.ObstacleLayerMask = CombatLayers.FogOccluderMask;
            revealer.ViewAngle = 360f;
            revealer.ViewRadius = CombatScale.InchesToWorldUnits(stats.visibilityRange);
            revealer.VisionHeight = stats.modelSize.VolumeHeightWorldUnits();
            revealer.EyeOffset = pawnCollider != null ? pawnCollider.height * 0.5f : 0f;

            pawn.SetActive(wasActive);
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

            return root;
        }

        private void ClearSpawnedUnits()
        {
            for (var i = 0; i < allRuntimeUnits.Count; i++)
            {
                var runtimeUnit = allRuntimeUnits[i];
                if (runtimeUnit.Pawn != null)
                {
                    Destroy(runtimeUnit.Pawn);
                }
            }

            playerRuntimeUnits.Clear();
            enemyRuntimeUnits.Clear();
            allRuntimeUnits.Clear();
        }
    }
}
