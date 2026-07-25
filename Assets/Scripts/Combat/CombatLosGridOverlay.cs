using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Renders a Baldur's Gate 3-style red LOS threat grid as a translucent ground mesh.
    /// </summary>
    public sealed class CombatLosGridOverlay
    {
        private const string RootName = "LosThreatGridOverlay";
        private const string ShaderName = "IronKingdoms/Combat/LosGridOverlay";
        private const float DrawHeight = 0.045f;
        private const float CellInset = 0.04f;

        private static readonly Color FillColor = new(0.82f, 0.12f, 0.1f, 0.42f);
        private static readonly Color GridLineColor = new(1f, 0.28f, 0.18f, 0.78f);

        private readonly CombatLosGridSampler sampler = new();
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<Color> colors = new();
        private readonly List<int> triangles = new();

        private Transform parent;
        private GameObject root;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material material;
        private bool isVisible;

        public bool IsVisible => isVisible;
        public int VisibleCellCount => sampler.VisibleCells.Count;
        public CombatLosGridSampler Sampler => sampler;

        public void SetParent(Transform overlayParent)
        {
            parent = overlayParent;
            EnsureRenderer();
        }

        public void Hide()
        {
            isVisible = false;
            if (root != null)
            {
                root.SetActive(false);
            }

            if (mesh != null)
            {
                mesh.Clear();
            }

            sampler.Clear();
        }

        public void Rebuild(
            IReadOnlyList<Unit> visionObservers,
            IReadOnlyList<Unit> allUnits,
            float groundY = 0f)
        {
            EnsureRenderer();
            if (root == null || mesh == null || material == null)
            {
                return;
            }

            sampler.Rebuild(
                visionObservers,
                allUnits,
                sampler.SampleWallDistanceWorld,
                groundY);

            BuildMesh(groundY + DrawHeight);
            root.SetActive(sampler.VisibleCells.Count > 0);
            isVisible = sampler.VisibleCells.Count > 0;
        }

        private void BuildMesh(float drawY)
        {
            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();

            var cellSizeWorld = CombatScale.InchesToWorldUnits(sampler.CellSizeInches);
            var half = cellSizeWorld * 0.5f * (1f - CellInset);
            var cells = sampler.VisibleCells;

            foreach (var key in cells)
            {
                CombatLosGridSampler.UnpackCellKey(key, out var cellX, out var cellZ);
                var center = CombatLosGridSampler.CellCenterWorld(cellX, cellZ, cellSizeWorld, drawY);
                AppendCellQuad(center, half);
            }

            mesh.Clear();
            if (vertices.Count == 0)
            {
                return;
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AppendCellQuad(Vector3 center, float half)
        {
            var index = vertices.Count;
            vertices.Add(new Vector3(center.x - half, center.y, center.z - half));
            vertices.Add(new Vector3(center.x + half, center.y, center.z - half));
            vertices.Add(new Vector3(center.x + half, center.y, center.z + half));
            vertices.Add(new Vector3(center.x - half, center.y, center.z + half));

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));

            colors.Add(Color.white);
            colors.Add(Color.white);
            colors.Add(Color.white);
            colors.Add(Color.white);

            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 1);
            triangles.Add(index);
            triangles.Add(index + 3);
            triangles.Add(index + 2);
        }

        private void EnsureRenderer()
        {
            if (root != null)
            {
                return;
            }

            if (parent == null)
            {
                return;
            }

            root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.layer = 0;

            meshFilter = root.AddComponent<MeshFilter>();
            meshRenderer = root.AddComponent<MeshRenderer>();
            mesh = new Mesh { name = "LosThreatGridMesh" };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;

            var shader = Shader.Find(ShaderName)
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                ?? Shader.Find("Unlit/Color");
            material = shader != null ? new Material(shader) : null;
            if (material != null)
            {
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", FillColor);
                }

                if (material.HasProperty("_GridColor"))
                {
                    material.SetColor("_GridColor", GridLineColor);
                }

                if (material.HasProperty("_GridLineWidth"))
                {
                    material.SetFloat("_GridLineWidth", 0.08f);
                }

                material.renderQueue = (int)RenderQueue.Transparent;
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            root.SetActive(false);
        }
    }
}
