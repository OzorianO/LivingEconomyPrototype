using System.Collections.Generic;
using UnityEngine;

namespace LivingEconomy.Presentation
{
    // Owns procedural geometry creation; resource lifetime belongs to SettlementView.
    internal sealed class IslandGenerator
    {
        private readonly Transform generatedRoot;
        private readonly List<Material> materials;
        private readonly List<Mesh> islandMeshes;
        private readonly Dictionary<GameObject, string> targets;
        public Collider Ground { get; private set; }
        public float WaterLevel { get; private set; } = -0.92f;
        public IslandGenerator(Transform root, List<Material> materials, List<Mesh> meshes, Dictionary<GameObject, string> targets)
        { generatedRoot = root; this.materials = materials; islandMeshes = meshes; this.targets = targets; }

        // Flat inhabited core preserves existing routes; relief is confined to the coast.
        // Ring sizes include the entire field, houses and all current route waypoints.
        public void BuildIsland(Material grass, Material timber)
        {
            var sand = ColorMaterial(new Color(0.82f, 0.72f, 0.47f));
            var sea = ColorMaterial(new Color(0.06f, 0.32f, 0.47f));
            var shallows = ColorMaterial(new Color(0.15f, 0.57f, 0.62f));
            var leaves = ColorMaterial(new Color(0.18f, 0.38f, 0.21f));
            var stone = ColorMaterial(new Color(0.46f, 0.49f, 0.46f));
            var preparedIsland = Resources.Load<GameObject>("World/MedievalIsland");
            if (preparedIsland != null)
            {
                var island = Object.Instantiate(preparedIsland, generatedRoot, false);
                island.name = "Medieval Island";
                Ground = island.GetComponentInChildren<Collider>();
                var waterMarker = island.transform.Find("Water level");
                if (Ground == null || waterMarker == null) throw new System.InvalidOperationException("Prepared island is incomplete.");
                WaterLevel = waterMarker.position.y;
                Shape("Ocean", PrimitiveType.Cube, new Vector3(0, WaterLevel, 0), new Vector3(500, 0.1f, 500), sea);
                BuildVillageDetails(timber, stone, leaves);
                return;
            }
            Shape("Ocean", PrimitiveType.Cube, new Vector3(0, -0.92f, 1), new Vector3(500, 0.1f, 500), sea);
            CreateIslandMesh("Island terrain", new[] { 0f, 0.35f, 0.55f, 0.66f, 0.74f, 0.85f, 0.94f, 1f },
                new[] { 0f, 0f, 0f, 0f, 0f, -0.08f, -0.4f, -1.15f }, new[] { grass, sand }, true);
            CreateIslandMesh("Coastal shallows", new[] { 0.96f, 1.10f },
                new[] { -0.85f, -0.85f }, new[] { shallows }, false);
            for (int i = 0; i < 22; i++)
            {
                float angle = i * Mathf.PI * 2 / 22 + 0.09f;
                var point = CoastPoint(angle, 0.70f, 0);
                float size = 0.8f + (i % 4) * 0.13f;
                var trunk = Shape("Island tree trunk " + i, PrimitiveType.Cylinder,
                    point + Vector3.up * 1.1f * size, new Vector3(0.32f, 1.1f, 0.32f) * size, timber);
                var crown = Shape("Island tree crown " + i, PrimitiveType.Sphere,
                    point + Vector3.up * 2.8f * size, new Vector3(2.1f, 2.8f, 2.1f) * size, leaves);
                RemoveDecorationCollider(trunk); RemoveDecorationCollider(crown);
            }
            for (int i = 0; i < 12; i++)
            {
                var rock = Shape("Shore rock " + i, PrimitiveType.Sphere,
                    CoastPoint(i * Mathf.PI * 2 / 12, 0.90f, -0.15f),
                    new Vector3(1.1f + i % 3 * 0.3f, 0.9f, 0.8f), stone);
                RemoveDecorationCollider(rock);
            }
            // Additional trees sit exactly on terrain vertices on the northern ridge.
            for (int i = 0; i < 9; i++)
            {
                float angle = (16 + i * 2) * Mathf.PI * 2 / 96;
                var point = CoastPoint(angle, 0.85f, -0.08f + RidgeHeight(angle));
                Decoration("Ridge tree trunk " + i, PrimitiveType.Cylinder, point + Vector3.up, new Vector3(0.3f, 1, 0.3f), timber);
                Decoration("Ridge tree crown " + i, PrimitiveType.Sphere, point + Vector3.up * 2.5f, new Vector3(1.9f, 2.5f, 1.9f), leaves);
            }
            BuildVillageDetails(timber, stone, leaves);
        }

        private static float RidgeHeight(float angle)
        {
            float north = Mathf.Max(0, Mathf.Sin(angle));
            return 3.8f * Mathf.Pow(north, 6) * (0.72f + 0.28f * Mathf.Cos(angle * 5));
        }

        public GameObject Decoration(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var item = Shape(name, type, position, scale, material);
            RemoveDecorationCollider(item);
            return item;
        }

        private void BuildVillageDetails(Material timber, Material stone, Material leaves)
        {
            // All props are outside the existing home/work/shop route corridors.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 8; i++)
                    Decoration("Field fence post", PrimitiveType.Cube, new Vector3(-10.9f + i * 1.1f, 0.5f, side < 0 ? 7.1f : 11.3f), new Vector3(0.12f, 1, 0.12f), timber);
                Decoration("Field fence rail", PrimitiveType.Cube, new Vector3(-7, 0.6f, side < 0 ? 7.1f : 11.3f), new Vector3(8, 0.12f, 0.12f), timber);
            }
            for (int i = 0; i < 5; i++)
            {
                Decoration("Bakery crate " + i, PrimitiveType.Cube, new Vector3(9.4f, 0.35f, 4 + i * 0.6f), new Vector3(0.55f, 0.7f, 0.5f), timber);
                Decoration("Village shrub " + i, PrimitiveType.Sphere, new Vector3(-12 + i * 6, 0.4f, -10.5f), new Vector3(1.4f, 0.8f, 1.1f), leaves);
            }
            Decoration("Well base", PrimitiveType.Cylinder, new Vector3(13, 0.4f, -6), new Vector3(1.5f, 0.4f, 1.5f), stone);
            var dark = ColorMaterial(new Color(0.08f, 0.17f, 0.19f));
            Decoration("Well opening", PrimitiveType.Cylinder, new Vector3(13, 0.81f, -6), new Vector3(1.1f, 0.02f, 1.1f), dark);
            for (int side = -1; side <= 1; side += 2)
                Decoration("Well support", PrimitiveType.Cube, new Vector3(13 + side * 0.65f, 1.3f, -6), new Vector3(0.13f, 1.8f, 0.13f), timber);
            Decoration("Well crossbeam", PrimitiveType.Cube, new Vector3(13, 2.2f, -6), new Vector3(1.6f, 0.15f, 0.15f), timber);
        }

        private void RemoveDecorationCollider(GameObject decoration)
        {
            var collider = decoration.GetComponent<Collider>();
            if (collider == null) return;
            collider.enabled = false;
            Object.Destroy(collider);
        }

        private static Vector3 CoastPoint(float angle, float radius, float height)
        {
            float outline = 1 + 0.045f * Mathf.Sin(angle * 3) + 0.025f * Mathf.Cos(angle * 7);
            return new Vector3(Mathf.Cos(angle) * 26 * radius * outline, height,
                1 + Mathf.Sin(angle) * 22 * radius * outline);
        }

        private void CreateIslandMesh(string name, float[] radii, float[] heights, Material[] palette, bool collider)
        {
            const int segments = 96;
            var vertices = new Vector3[radii.Length * segments];
            for (int ring = 0; ring < radii.Length; ring++)
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    float ridge = name == "Island terrain" && radii[ring] == 0.85f ? RidgeHeight(angle) : 0;
                    vertices[ring * segments + i] = CoastPoint(angle, radii[ring], heights[ring] + ridge);
                }
            var triangles = new List<int>[palette.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();
            for (int ring = 0; ring < radii.Length - 1; ring++)
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    int a = ring * segments + i, b = ring * segments + next;
                    int c = (ring + 1) * segments + i, d = (ring + 1) * segments + next;
                    var indices = triangles[palette.Length == 1 || ring < radii.Length - 3 ? 0 : 1];
                    // Clockwise in XZ gives upward-facing triangles.
                    if (radii[ring] > 0) indices.AddRange(new[] { a, b, c });
                    indices.AddRange(new[] { b, d, c });
                }
            var mesh = new Mesh { name = "Generated " + name, vertices = vertices, subMeshCount = palette.Length };
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); islandMeshes.Add(mesh);
            var surface = new GameObject(name); surface.transform.SetParent(generatedRoot, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterials = palette;
            if (collider)
            {
                var meshCollider = surface.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh; Ground = meshCollider;
            }
        }

        public Material ColorMaterial(Color color)
        {
            var template = Resources.Load<Material>("IslandSurface");
            if (template == null) throw new System.InvalidOperationException("Missing Resources/IslandSurface material.");
            var material = new Material(template) { color = color, hideFlags = HideFlags.DontSave };
            materials.Add(material); return material;
        }
        public GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var shape = GameObject.CreatePrimitive(type); shape.name = name;
            shape.transform.SetParent(generatedRoot); shape.transform.position = position;
            shape.transform.localScale = scale; shape.GetComponent<Renderer>().sharedMaterial = material;
            return shape;
        }
        public void Building(string name, string id, Vector3 location, Material wall, Material roof)
        {
            var body = Shape(name, PrimitiveType.Cube, location + Vector3.up * 1.25f, new Vector3(3.5f, 2.5f, 2.5f), wall);
            if (id != null) targets.Add(body, id);
            // Two pitched slabs meet at the ridge rather than a flat block roof.
            for (int side = -1; side <= 1; side += 2)
            {
                var top = Shape(name + " roof " + side, PrimitiveType.Cube, location + new Vector3(0, 2.98f, side * 0.75f), new Vector3(4, 0.18f, 1.85f), roof);
                top.transform.rotation = Quaternion.Euler(side * 30, 0, 0);
                if (id != null) targets.Add(top, id);
            }
            var trim = ColorMaterial(new Color(0.26f, 0.17f, 0.11f));
            var glass = ColorMaterial(new Color(0.24f, 0.47f, 0.53f));
            float front = id == null ? 1 : -1;
            Decoration(name + " door", PrimitiveType.Cube, location + new Vector3(0, 0.85f, front * 1.27f), new Vector3(0.75f, 1.7f, 0.08f), trim);
            for (int side = -1; side <= 1; side += 2)
            {
                var window = location + new Vector3(side * 1.1f, 1.5f, front * 1.29f);
                Decoration(name + " window frame", PrimitiveType.Cube, window, new Vector3(0.72f, 0.8f, 0.08f), trim);
                Decoration(name + " window glass", PrimitiveType.Cube, window + new Vector3(0, 0, front * 0.05f), new Vector3(0.55f, 0.62f, 0.03f), glass);
                Decoration(name + " timber corner", PrimitiveType.Cube, location + new Vector3(side * 1.72f, 1.25f, front * 1.28f), new Vector3(0.13f, 2.5f, 0.13f), trim);
            }
            if (id == "bakery")
                Decoration("Bakery chimney", PrimitiveType.Cube, location + new Vector3(1, 3.3f, 0.3f), new Vector3(0.5f, 1.6f, 0.5f), trim);
        }
    }
}
