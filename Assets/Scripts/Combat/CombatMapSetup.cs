using Pathfinding;
using Pathfinding.Graphs.Grid;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Procedurally builds the test combat map geometry and initialises an A* GridGraph
    /// that covers the play area. Attach this component to any GameObject in the
    /// TestCombatScene before the <see cref="TestLevelUnitController"/> runs.
    /// </summary>
    public class CombatMapSetup : MonoBehaviour
    {
        // ── Grid configuration ───────────────────────────────────────────────
        private const int GridNodeCountX = 42;
        private const int GridNodeCountZ = 42;
        private const float GridNodeSize = 0.5f;

        // ── Map geometry ─────────────────────────────────────────────────────
        private const float GroundHalfExtent = 11f;
        private const float GroundThickness = 0.2f;
        private const float WallHeight = 1.8f;
        private const float WallThickness = 0.6f;
        private const float PillarSize = 1.5f;
        private const float PillarHeight = 3f;
        private const float CrateSize = 1f;

        // ── Materials / colours ──────────────────────────────────────────────
        private static readonly Color GroundColor = new(0.35f, 0.32f, 0.28f);
        private static readonly Color WallColor = new(0.55f, 0.50f, 0.45f);
        private static readonly Color PillarColor = new(0.48f, 0.44f, 0.40f);
        private static readonly Color CrateColor = new(0.60f, 0.45f, 0.28f);

        private void Awake()
        {
            BuildMap();
            SetupPathfinding();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Map geometry
        // ─────────────────────────────────────────────────────────────────────

        private void BuildMap()
        {
            var mapRoot = new GameObject("CombatMap");
            mapRoot.transform.SetParent(transform);

            // Ground plane
            var groundScale = new Vector3(GroundHalfExtent * 2f, GroundThickness, GroundHalfExtent * 2f);
            CreateBox(mapRoot.transform, "Ground", Vector3.down * (GroundThickness * 0.5f), groundScale, GroundColor);

            // ── Pillars (four corners of the central area) ───────────────────
            var pillarScale = new Vector3(PillarSize, PillarHeight, PillarSize);
            float py = PillarHeight * 0.5f;
            CreateBox(mapRoot.transform, "Pillar_NW", new Vector3(-4f, py, 3f), pillarScale, PillarColor);
            CreateBox(mapRoot.transform, "Pillar_NE", new Vector3(4f, py, 3f), pillarScale, PillarColor);
            CreateBox(mapRoot.transform, "Pillar_SW", new Vector3(-4f, py, -3f), pillarScale, PillarColor);
            CreateBox(mapRoot.transform, "Pillar_SE", new Vector3(4f, py, -3f), pillarScale, PillarColor);

            // ── Central low wall ─────────────────────────────────────────────
            CreateBox(mapRoot.transform, "Wall_Central",
                new Vector3(0f, WallHeight * 0.5f, 0f),
                new Vector3(5f, WallHeight, WallThickness),
                WallColor);

            // ── Side wall segments ────────────────────────────────────────────
            CreateBox(mapRoot.transform, "Wall_Left",
                new Vector3(-6.5f, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, 3.5f),
                WallColor);
            CreateBox(mapRoot.transform, "Wall_Right",
                new Vector3(6.5f, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, 3.5f),
                WallColor);

            // ── Scatter crates ────────────────────────────────────────────────
            var crateScale = Vector3.one * CrateSize;
            CreateBox(mapRoot.transform, "Crate_A",
                new Vector3(-2f, CrateSize * 0.5f, -1.5f),
                crateScale, CrateColor);
            CreateBox(mapRoot.transform, "Crate_B",
                new Vector3(2f, CrateSize * 0.5f, 1.5f),
                crateScale, CrateColor);
            CreateBox(mapRoot.transform, "Crate_C",
                new Vector3(-1f, CrateSize * 0.5f, 4.5f),
                crateScale, CrateColor);
            CreateBox(mapRoot.transform, "Crate_D",
                new Vector3(1f, CrateSize * 0.5f, -4.5f),
                crateScale, CrateColor);
        }

        private static void CreateBox(Transform parent, string objName, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objName;
            go.transform.SetParent(parent);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"))
                {
                    color = color
                };
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  A* Pathfinding setup
        // ─────────────────────────────────────────────────────────────────────

        private void SetupPathfinding()
        {
            // Create AstarPath component if one isn't already present in the scene.
            var astar = AstarPath.active;
            if (astar == null)
            {
                var astarGo = new GameObject("AstarPath");
                astar = astarGo.AddComponent<AstarPath>();
            }

            // Clear any existing graphs and add a fresh GridGraph.
            astar.data.ClearGraphs();
            var gg = astar.data.AddGraph<GridGraph>();

            // Cover a 21 × 21 world-unit area centred on the origin.
            gg.SetDimensions(GridNodeCountX, GridNodeCountZ, GridNodeSize);
            gg.center = Vector3.zero;

            // Obstacle collision detection: use a sphere at each node.
            gg.collision.use2D = false;
            gg.collision.collisionCheck = true;
            gg.collision.type = ColliderType.Sphere;
            gg.collision.diameter = 0.85f;   // slightly larger than a unit pawn
            gg.collision.height = 1.5f;
            gg.collision.mask = Physics.DefaultRaycastLayers;

            // Height detection so nodes hug the ground surface.
            gg.collision.heightCheck = true;
            gg.collision.heightMask = Physics.DefaultRaycastLayers;
            gg.collision.fromHeight = 10f;

            // Allow diagonal movement.
            gg.neighbours = NumNeighbours.Eight;
            gg.cutCorners = false;

            // Scan the graph now that the geometry is in place.
            astar.Scan();
        }
    }
}
