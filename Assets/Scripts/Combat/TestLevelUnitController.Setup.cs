using FOW;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public partial class TestLevelUnitController
    {
        private void EnsureCameraManagerAssigned()
        {
            if (cameraManager == null)
            {
                cameraManager = GetComponent<CombatCameraManager>();
            }
        }

        private void EnsureNavPathBuilderAssigned()
        {
            if (navPathBuilder == null)
            {
                navPathBuilder = GetComponent<NavPathBuilder>();
            }

            if (navPathBuilder == null)
            {
                navPathBuilder = NavPathBuilder.instance;
            }

            if (navPathBuilder == null)
            {
                var go = new GameObject("NavPathBuilder");
                go.transform.SetParent(transform);
                navPathBuilder = go.AddComponent<NavPathBuilder>();
            }
        }

        private void EnsureMatchArmySpawnerAssigned()
        {
            matchArmySpawner ??= new MatchArmySpawner(navPathBuilder);
        }

        private void EnsureDefinitionCatalogAssigned()
        {
            if (definitionCatalog != null)
            {
                definitionCatalog.RegisterAsActiveCatalog();
                return;
            }

            definitionCatalog = CombatDefinitionCatalog.Instance;
            definitionCatalog?.RegisterAsActiveCatalog();
        }

        private void EnsureFogOfWarWorldAssigned()
        {
            if (fogOfWarWorld != null)
            {
                return;
            }

            fogOfWarWorld = FogOfWarWorld.instance;
            if (fogOfWarWorld != null)
            {
                return;
            }

            fogOfWarWorld = FindFirstObjectByType<FogOfWarWorld>();
            if (fogOfWarWorld != null)
            {
                return;
            }

            var fogWorldObject = new GameObject("FogOfWarWorld");
            fogWorldObject.transform.SetParent(transform);
            fogOfWarWorld = fogWorldObject.AddComponent<FogOfWarWorld>();
        }

        /// <summary>
        /// Applies the combat prototype's fog defaults to whichever FogOfWarWorld the scene uses.
        /// Keep global FOW tuning here; per-unit revealer tuning lives with pawn spawning.
        /// </summary>
        private void ConfigureFogOfWarWorld()
        {
            if (fogOfWarWorld == null)
            {
                return;
            }

            fogOfWarWorld.GamePlaneOrientation = FogOfWarWorld.GamePlane.XZ;
            fogOfWarWorld.FOWSamplingMode = FogOfWarWorld.FogSampleMode.Texture;
            // Start raycast jobs in Update so they can overlap gameplay work; finish in LateUpdate.
            fogOfWarWorld.UpdateMethod = FogOfWarWorld.FowUpdateMethod.StartInUpdateFinishInLateUpdate;
            fogOfWarWorld.RevealerUpdateMode = FogOfWarWorld.RevealerUpdateMethod.N_Per_Frame;
            fogOfWarWorld.MaxNumRevealersPerFrame = maxFogRevealersPerFrame;
            // Combat samples the fog texture directly; there are no FogOfWarHider components to update per revealer.
            fogOfWarWorld.HidersUseFogTexture = true;
            var previousMaxSegments = fogOfWarWorld.MaxPossibleSegmentsPerRevealer;
            fogOfWarWorld.MaxPossibleSegmentsPerRevealer = Mathf.Max(previousMaxSegments, 512);
            fogOfWarWorld.SightExtraAmount = 0f;
            fogOfWarWorld.PixelateFog = false;
            fogOfWarWorld.RoundRevealerPosition = false;

            // Texture storage + regrow keeps explored-but-out-of-sight areas dimmed (shroud)
            // while never-visited areas stay fully black.
            fogOfWarWorld.UseRegrow = true;
            fogOfWarWorld.RevealerFadeIn = false;
            fogOfWarWorld.RevealerFadeOut = false;
            fogOfWarWorld.InitialFogExplorationValue = 0f;
            fogOfWarWorld.MaxFogRegrowAmount = fogExploredShroudVisibility;
            fogOfWarWorld.MaxPossibleSegmentsPerRevealer = 1000;
            fogOfWarWorld.UseConstantBlur = false;
            fogOfWarWorld.FogType = debugUseCrispFogRendering
                ? FogOfWarWorld.FogOfWarType.Hard
                : FogOfWarWorld.FogOfWarType.Soft;
            fogOfWarWorld.FogFade = debugUseCrispFogRendering
                ? FogOfWarWorld.FogOfWarFadeType.Linear
                : FogOfWarWorld.FogOfWarFadeType.Smoothstep;
            fogOfWarWorld.EdgeSoftenDistance = debugUseCrispFogRendering
                ? 0.05f
                : fogVisionEdgeSoftenDistance;
            fogOfWarWorld.FowResX = 1024;
            fogOfWarWorld.FowResY = 1024;

            var halfExtents = new Vector3(
                fogWorldBoundsSize.x * 0.5f,
                0.5f,
                fogWorldBoundsSize.y * 0.5f);
            fogOfWarWorld.UpdateWorldBounds(
                new Vector3(fogWorldBoundsCenter.x, 0f, fogWorldBoundsCenter.y),
                halfExtents);

            // Alpha 0 uses the solid unknown color in the FOW shader; non-zero alpha multiplies
            // the live scene color through, which leaves terrain visible in unseen areas.
            fogOfWarWorld.UnknownColor = new Color(0f, 0f, 0f, 0f);

            // FogOfWarWorld.Initialize() runs during AddComponent, before these values are set.
            fogOfWarWorld.EnsureTextureStorageReady();
            fogOfWarWorld.SwitchHidersUseFogTextureMode(fogOfWarWorld.HidersUseFogTexture);
            fogOfWarWorld.UpdateAllShaderProperties();
            FogOfWarWorld.SetFowEffectStrength(1f);

            // Segment budget is consumed during FogOfWarWorld.Initialize(); if we raised it
            // after initialization, reinitialize once so new buffers are allocated.
            if (fogOfWarWorld.MaxPossibleSegmentsPerRevealer != previousMaxSegments
                && ReferenceEquals(FogOfWarWorld.instance, fogOfWarWorld)
                && fogOfWarWorld.isActiveAndEnabled)
            {
                fogOfWarWorld.enabled = false;
                fogOfWarWorld.enabled = true;
            }
        }

        private void EnsureFogOfWarCameraEffectAssigned()
        {
            var activeCamera = cameraManager?.ActiveCamera;
            if (activeCamera == null)
            {
                activeCamera = Camera.main;
            }

            if (activeCamera == null
                || activeCamera.GetComponent<FowImageEffectOpaque>() != null
                || activeCamera.GetComponent<FowImageEffect>() != null)
            {
                return;
            }

            activeCamera.gameObject.AddComponent<FowImageEffectOpaque>();
        }
    }
}
