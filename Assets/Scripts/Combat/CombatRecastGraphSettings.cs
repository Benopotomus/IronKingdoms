using Pathfinding;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Shared RecastGraph bake parameters for the combat map's per-base-size navmeshes.
    /// Re-scan in the editor after changing quality (Iron Kingdoms → Tools → Combat Navmesh).
    /// </summary>
    public static class CombatRecastGraphSettings
    {
        public enum NavmeshBakeQuality
        {
            Standard,
            High
        }

        public static bool TryResolveModelSize(string graphName, out ModelSize modelSize)
        {
            switch (graphName)
            {
                case "Base30mm":
                    modelSize = ModelSize.Base30mm;
                    return true;
                case "Base40mm":
                    modelSize = ModelSize.Base40mm;
                    return true;
                case "Base50mm":
                    modelSize = ModelSize.Base50mm;
                    return true;
                case "Base80mm":
                    modelSize = ModelSize.Base80mm;
                    return true;
                case "Base120mm":
                    modelSize = ModelSize.Base120mm;
                    return true;
                default:
                    modelSize = ModelSize.Base30mm;
                    return false;
            }
        }

        public static void Apply(RecastGraph graph, ModelSize modelSize, NavmeshBakeQuality quality = NavmeshBakeQuality.High)
        {
            if (graph == null)
            {
                return;
            }

            graph.characterRadius = modelSize.BaseDiameterWorldUnits() * 0.5f;
            graph.walkableHeight = modelSize.VolumeHeightWorldUnits() + 0.1f;
            graph.walkableClimb = CombatScale.InchesToWorldUnits(1f);
            graph.maxSlope = 30f;
            graph.useTiles = true;
            graph.editorTileSize = 128;

            switch (quality)
            {
                case NavmeshBakeQuality.High:
                    graph.cellSize = 0.125f;
                    graph.contourMaxError = 1.25f;
                    graph.maxEdgeLength = 10f;
                    graph.minRegionSize = 2f;
                    graph.collectionSettings.colliderRasterizeDetail = 2f;
                    break;
                default:
                    graph.cellSize = 0.25f;
                    graph.contourMaxError = 2f;
                    graph.maxEdgeLength = 20f;
                    graph.minRegionSize = 3f;
                    graph.collectionSettings.colliderRasterizeDetail = 1f;
                    break;
            }
        }

        public static bool TryApplyByGraphName(RecastGraph graph, NavmeshBakeQuality quality = NavmeshBakeQuality.High)
        {
            if (graph == null || !TryResolveModelSize(graph.name, out var modelSize))
            {
                return false;
            }

            Apply(graph, modelSize, quality);
            return true;
        }
    }
}
