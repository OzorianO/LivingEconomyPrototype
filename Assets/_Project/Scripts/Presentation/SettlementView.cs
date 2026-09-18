using System.Collections.Generic;
using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingEconomy.Presentation
{
    // View objects keep stable IDs and resolve the current scenario after each reset.
    [DisallowMultipleComponent]
    public sealed class SettlementView : MonoBehaviour
    {
        private SimulationPreview preview;
        private Camera mapCamera;
        private IslandPlayer player;
        public IslandPlayer Player => player;
        [SerializeField, HideInInspector] private Vector3 cameraFocus = new Vector3(0, 0, 1);
        [SerializeField, HideInInspector] private float cameraYaw = -25, cameraPitch = 43, cameraDistance = 70;
        private string selectedId;
        private readonly Dictionary<GameObject, string> targets = new Dictionary<GameObject, string>();
        private readonly Dictionary<string, Renderer> people = new Dictionary<string, Renderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> islandMeshes = new List<Mesh>();
        private GameObject marker;
        private Material farmerColor, bakerColor, hungryColor;
        private readonly Dictionary<string, Vector3> homes = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> workplaces = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Queue<Vector3>> routes = new Dictionary<string, Queue<Vector3>>();
        private readonly Dictionary<string, float> travelTimes = new Dictionary<string, float>();
        private readonly Dictionary<string, string> routeTargets = new Dictionary<string, string>();
        private readonly HashSet<string> blockedTargets = new HashSet<string>();
        private const float RouteTimeout = 30;
        [SerializeField, HideInInspector] private Transform generatedRoot;
        [System.NonSerialized] private bool initialized;
        public bool IsInitialized => initialized;
        private float stopTime;
        private int phase;
        public bool Walking { get; private set; }
        public bool Paused { get; set; }
        public bool HasBlockedTargets => blockedTargets.Count > 0;
        public void ClearRouteTests() => blockedTargets.Clear();
        public string Activity => !Walking ? "Day complete" : phase == 0 ? "Going to work" : phase == 1 ? "At work" : phase == 2 ? "Going to bakery" : phase == 3 ? "At bakery" : "Going home";

        public void Initialize(SimulationPreview source)
        {
            if (initialized && preview == source && generatedRoot != null) return;
            preview = source;
            ReleaseGeneratedWorld();
            targets.Clear(); people.Clear(); homes.Clear(); workplaces.Clear(); routes.Clear();
            travelTimes.Clear(); routeTargets.Clear(); blockedTargets.Clear();
            selectedId = null; jobMessage = null; Walking = false; Paused = false;
            generatedRoot = new GameObject("GeneratedSettlement").transform;
            generatedRoot.SetParent(transform, false);
            mapCamera = Camera.main;
            if (mapCamera == null)
            {
                mapCamera = new GameObject("SettlementCamera").AddComponent<Camera>();
                mapCamera.tag = "MainCamera";
                mapCamera.transform.SetParent(generatedRoot, false);
            }
            mapCamera.orthographic = false;
            mapCamera.fieldOfView = 50;
            mapCamera.nearClipPlane = 0.25f;
            mapCamera.farClipPlane = 250;
            ApplyCameraPose();
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = new Color(0.16f, 0.23f, 0.29f);
            var grass = ColorMaterial(new Color(0.29f, 0.43f, 0.29f));
            var path = ColorMaterial(new Color(0.64f, 0.57f, 0.44f));
            var timber = ColorMaterial(new Color(0.58f, 0.37f, 0.21f));
            var plaster = ColorMaterial(new Color(0.85f, 0.73f, 0.53f));
            var roof = ColorMaterial(new Color(0.50f, 0.24f, 0.18f));
            farmerColor = ColorMaterial(new Color(0.32f, 0.65f, 0.87f));
            bakerColor = ColorMaterial(new Color(0.94f, 0.70f, 0.26f));
            hungryColor = ColorMaterial(new Color(0.9f, 0.25f, 0.22f));
            BuildIsland(grass, timber);
            Shape("Main road", PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(24, 0.05f, 2), path);
            Shape("Village path", PrimitiveType.Cube, new Vector3(0, 0.03f, -4), new Vector3(2, 0.05f, 10), path);
            Building("Farm", "farm", new Vector3(-7, 0, 5), timber, roof);
            Building("Bakery", "bakery", new Vector3(7, 0, 5), plaster, roof);
            var soil = ColorMaterial(new Color(0.38f, 0.26f, 0.16f));
            var wheat = ColorMaterial(new Color(0.77f, 0.69f, 0.25f));
            Shape("Field", PrimitiveType.Cube, new Vector3(-7, 0.02f, 9), new Vector3(7, 0.06f, 4), soil);
            for (int row = 0; row < 4; row++)
                Shape("Crop row", PrimitiveType.Cube, new Vector3(-7, 0.22f, 7.6f + row), new Vector3(6, 0.4f, 0.25f), wheat);
            for (int i = 0; i < 4; i++)
                Building("House " + (i + 1), null, new Vector3(-9 + i * 6, 0, -7), plaster, roof);
            int farmIndex = 0, bakeryIndex = 0;
            foreach (var npc in preview.Simulation.Economy.Residents)
            {
                bool farm = preview.Simulation.EmployerOf(npc.Id) == preview.Simulation.Farm.Id;
                int index = farm ? farmIndex++ : bakeryIndex++;
                float x = (farm ? -7 : 7) + (index % 5 - 2) * 1.15f;
                var person = Shape(npc.Name, PrimitiveType.Capsule, new Vector3(x, 0.8f, 1.8f + index / 5 * 1.5f),
                    new Vector3(0.65f, 0.8f, 0.65f), farm ? farmerColor : bakerColor);
                targets.Add(person, npc.Id); people.Add(npc.Id, person.GetComponent<Renderer>());
                workplaces.Add(npc.Id, person.transform.position);
                int number = homes.Count;
                var home = new Vector3(-9 + (number / 5) * 6 + (number % 5 - 2) * 0.55f, 0.8f, -3.5f);
                homes.Add(npc.Id, home); routes.Add(npc.Id, new Queue<Vector3>());
                person.transform.position = home;
            }
            marker = Shape("Selection", PrimitiveType.Cylinder, Vector3.zero, new Vector3(1, 0.035f, 1),
                ColorMaterial(new Color(0.3f, 1, 0.7f)));
            Destroy(marker.GetComponent<Collider>()); marker.SetActive(false);
            BuildPlayer();
            initialized = true;
        }

        private void BuildPlayer()
        {
            var hero = new GameObject("Island hero"); hero.transform.SetParent(generatedRoot, false);
            hero.transform.position = new Vector3(0, 0.08f, -1.5f);
            var shirt = ColorMaterial(new Color(0.55f, 0.22f, 0.68f));
            var skin = ColorMaterial(new Color(0.86f, 0.65f, 0.43f));
            var trousers = ColorMaterial(new Color(0.16f, 0.20f, 0.26f));
            PlayerPart(hero.transform, "Hero torso", PrimitiveType.Cube, new Vector3(0, 1.05f, 0), new Vector3(0.55f, 0.65f, 0.32f), shirt);
            PlayerPart(hero.transform, "Hero head", PrimitiveType.Sphere, new Vector3(0, 1.6f, 0), Vector3.one * 0.4f, skin);
            Transform legA = null, legB = null;
            for (int side = -1; side <= 1; side += 2)
            {
                PlayerPart(hero.transform, "Hero arm " + side, PrimitiveType.Cube, new Vector3(side * 0.36f, 1.03f, 0), new Vector3(0.16f, 0.6f, 0.18f), shirt);
                var pivot = new GameObject("Hero leg pivot " + side).transform;
                pivot.SetParent(hero.transform, false); pivot.localPosition = new Vector3(side * 0.15f, 0.75f, 0);
                PlayerPart(pivot, "Hero leg " + side, PrimitiveType.Cube, new Vector3(0, -0.35f, 0), new Vector3(0.2f, 0.7f, 0.22f), trousers);
                if (side < 0) legA = pivot; else legB = pivot;
            }
            player = hero.AddComponent<IslandPlayer>();
            player.Initialize(this, mapCamera, generatedRoot.Find("Island terrain").GetComponent<MeshCollider>(), legA, legB);
        }

        private void PlayerPart(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material)
        {
            var part = Decoration(name, type, Vector3.zero, scale, material);
            part.transform.SetParent(parent, false); part.transform.localPosition = localPosition;
        }

        public void RestoreOverview() => ApplyCameraPose();

        public void ReleaseGeneratedWorld()
        {
            initialized = false;
            var ownedMaterials = new HashSet<Material>(materials);
            var ownedMeshes = new HashSet<Mesh>(islandMeshes);
            // Migration of the old preview: its generated primitives were direct children.
            var legacyNames = new HashSet<string> { "Ground", "Main road", "Village path", "Farm", "Farm roof", "Bakery", "Bakery roof", "Field", "Crop row", "Selection" };
            for (int i = 1; i <= 4; i++) { legacyNames.Add("House " + i); legacyNames.Add("House " + i + " roof"); }
            if (preview != null && preview.Simulation != null)
                foreach (var npc in preview.Simulation.Economy.Residents) legacyNames.Add(npc.Name);
            foreach (Transform child in transform)
            {
                if (child == generatedRoot || child.name == "GeneratedSettlement"
                    || (gameObject.name == "SimulationPreview" && legacyNames.Contains(child.name)))
                {
                    foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            if (material != null) ownedMaterials.Add(material);
                    foreach (var filter in child.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null && (filter.sharedMesh.name.StartsWith("Generated ")
                            || filter.gameObject.name == "Island terrain" || filter.gameObject.name == "Coastal shallows"))
                            ownedMeshes.Add(filter.sharedMesh);
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }
            foreach (var material in ownedMaterials) if (material != null) Destroy(material);
            materials.Clear(); generatedRoot = null;
            foreach (var mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
            islandMeshes.Clear();
        }

        // Flat inhabited core preserves existing routes; relief is confined to the coast.
        // Ring sizes include the entire field, houses and all current route waypoints.
        private void BuildIsland(Material grass, Material timber)
        {
            var sand = ColorMaterial(new Color(0.82f, 0.72f, 0.47f));
            var sea = ColorMaterial(new Color(0.06f, 0.32f, 0.47f));
            var shallows = ColorMaterial(new Color(0.15f, 0.57f, 0.62f));
            var leaves = ColorMaterial(new Color(0.18f, 0.38f, 0.21f));
            var stone = ColorMaterial(new Color(0.46f, 0.49f, 0.46f));
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

        private GameObject Decoration(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
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
            Destroy(collider);
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
            if (collider) surface.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private Material ColorMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader); material.color = color;
            materials.Add(material); return material;
        }
        private GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var shape = GameObject.CreatePrimitive(type); shape.name = name;
            shape.transform.SetParent(generatedRoot); shape.transform.position = position;
            shape.transform.localScale = scale; shape.GetComponent<Renderer>().sharedMaterial = material;
            return shape;
        }
        private void Building(string name, string id, Vector3 location, Material wall, Material roof)
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
        private void Update()
        {
            if (!initialized || preview == null || preview.Simulation == null || mapCamera == null) return;
            AdvanceWalking();
            // Reserve the left side for the panel, leaving the map centered in its own viewport.
            float left = Mathf.Clamp((preview.Panel.xMax + 12) / Mathf.Max(1, Screen.width), 0, 0.8f);
            mapCamera.rect = new Rect(left, 0, 1 - left, 1);
            UpdateCameraControls();
            foreach (var npc in preview.Simulation.Economy.Residents)
            {
                bool farm = preview.Simulation.EmployerOf(npc.Id) == preview.Simulation.Farm.Id;
                if (people[npc.Id] != null)
                    people[npc.Id].sharedMaterial = npc.Hunger > 0 ? hungryColor : farm ? farmerColor : bakerColor;
            }
            if (selectedId != null && people.TryGetValue(selectedId, out var selectedPerson) && selectedPerson != null)
                marker.transform.position = new Vector3(selectedPerson.transform.position.x, 0.12f, selectedPerson.transform.position.z);
            if (player != null && player.Exploring) return;
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            var position = Mouse.current.position.ReadValue();
            if (!mapCamera.pixelRect.Contains(position)) return;
            if (Physics.Raycast(mapCamera.ScreenPointToRay(position), out var hit, 250)
                && targets.TryGetValue(hit.collider.gameObject, out var id))
            {
                selectedId = id;
                marker.transform.position = new Vector3(hit.collider.transform.position.x, 0.12f, hit.collider.transform.position.z);
                marker.SetActive(true);
            }
            else { selectedId = null; marker.SetActive(false); }
        }
        public void DrawSelection()
        {
            GUILayout.Label("Camera: right drag = orbit; middle drag = pan; wheel = zoom; R = reset (over map).");
            if (selectedId == null) { GUILayout.Label("Selection: none. Blue = farm; yellow = bakery; red = hungry."); return; }
            Resident selected = selectedId == "farm" ? preview.Simulation.Farm : selectedId == "bakery" ? preview.Simulation.Bakery : null;
            if (selected == null)
                foreach (var npc in preview.Simulation.Economy.Residents) if (npc.Id == selectedId) { selected = npc; break; }
            if (selected == null) return;
            GUILayout.Label("Selected: " + selected.Name);
            GUILayout.Label($"Coins: {selected.Money} | grain: {selected.Stock(Good.Grain)} | bread: {selected.Stock(Good.Bread)}");
            if (selectedId.StartsWith("npc-"))
            {
                GUILayout.Label($"Job: {(preview.Simulation.Jobs.TryGetValue(selectedId, out var job) ? job : preview.Simulation.IsOwner(selectedId) ? "owner" : "unemployed")} | hunger: {selected.Hunger}/100");
                GUILayout.Label("Decision: " + preview.Simulation.DecisionOf(selectedId));
            }
            else
            {
                var business = preview.Simulation.Business(selectedId);
                GUILayout.Label($"Owner: {business.Owner} | Employees: {preview.Simulation.EmployeeCount(selectedId)}/{business.Capacity} | wage: {business.Wage}");
            }
            if (selectedId.StartsWith("npc-") && !preview.Simulation.IsOwner(selectedId))
            {
                GUI.enabled = !preview.Simulation.DayInProgress;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Farm job")) ChangeJob("farm");
                if (GUILayout.Button("Bakery job")) ChangeJob("bakery");
                if (GUILayout.Button("Leave job")) ChangeJob(null);
                GUILayout.EndHorizontal(); GUI.enabled = true;
                if (jobMessage != null) GUILayout.Label(jobMessage);
            }
        }

        public void ResetCamera()
        {
            cameraFocus = new Vector3(0, 0, 1);
            cameraYaw = -25; cameraPitch = 43; cameraDistance = 70;
            ApplyCameraPose();
        }

        private void ApplyCameraPose()
        {
            if (mapCamera == null) return;
            cameraPitch = Mathf.Clamp(cameraPitch, 20, 75);
            cameraDistance = Mathf.Clamp(cameraDistance, 12, 100);
            cameraFocus.x = Mathf.Clamp(cameraFocus.x, -20, 20);
            cameraFocus.z = Mathf.Clamp(cameraFocus.z, -17, 19);
            cameraFocus.y = 0;
            var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0);
            mapCamera.transform.SetPositionAndRotation(cameraFocus + rotation * Vector3.back * cameraDistance, rotation);
        }

        private void UpdateCameraControls()
        {
            if (player != null && player.Exploring) return;
            var mouse = Mouse.current;
            if (mouse == null || !mapCamera.pixelRect.Contains(mouse.position.ReadValue())) return;
            var delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed)
            {
                cameraYaw = Mathf.Repeat(cameraYaw + delta.x * 0.2f, 360);
                cameraPitch -= delta.y * 0.15f;
            }
            if (mouse.middleButton.isPressed)
            {
                var right = Quaternion.Euler(0, cameraYaw, 0) * Vector3.right;
                var forward = Quaternion.Euler(0, cameraYaw, 0) * Vector3.forward;
                float unitsPerPixel = 2 * cameraDistance * Mathf.Tan(mapCamera.fieldOfView * Mathf.Deg2Rad / 2)
                    / Mathf.Max(1, mapCamera.pixelHeight);
                cameraFocus -= (right * delta.x + forward * delta.y) * unitsPerPixel;
            }
            cameraDistance = ZoomDistance(cameraDistance, mouse.scroll.ReadValue().y);
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetCamera();
            ApplyCameraPose();
        }

        private static float ZoomDistance(float distance, float scroll)
        {
            // Input System can expose either normalized notches or raw Windows wheel units.
            float notches = Mathf.Abs(scroll) >= 10 ? scroll / 120 : scroll;
            return Mathf.Clamp(distance * Mathf.Exp(-notches * 0.12f), 12, 100);
        }

        private string jobMessage;
        public void DrawRouteTests()
        {
            GUILayout.Label("Route failure tests (no NavMesh yet)");
            ToggleBlocked("farm", "Block farm");
            ToggleBlocked("bakery", "Block bakery / shop");
        }
        private void ToggleBlocked(string id, string label)
        {
            bool blocked = GUILayout.Toggle(blockedTargets.Contains(id), label);
            if (blocked) blockedTargets.Add(id); else blockedTargets.Remove(id);
        }
        private void ChangeJob(string employer)
        {
            bool assigned = preview.Simulation.AssignJob(selectedId, employer, out var reason);
            jobMessage = assigned ? "Job updated." : reason;
            if (assigned) preview.RememberCompletedDay();
        }

        public void ResetWalking()
        {
            Walking = false; Paused = false; phase = 0;
            foreach (var person in people)
            {
                if (person.Value != null) person.Value.transform.position = homes[person.Key];
                routes[person.Key].Clear();
            }
            travelTimes.Clear(); routeTargets.Clear();
        }
        public void BeginDay()
        {
            ResetWalking(); Walking = true;
            int index = 0;
            foreach (var person in people)
            {
                string employer = preview.Simulation.EmployerOf(person.Key);
                if (employer == null)
                {
                    preview.Simulation.ArriveAtWork(person.Key);
                    continue;
                }
                bool farm = employer == preview.Simulation.Farm.Id;
                workplaces[person.Key] = new Vector3((farm ? -7 : 7) + (index % 5 - 2) * 1.15f, 0.8f, 1.8f + index / 5 * 1.5f);
                SetRoute(person.Key, workplaces[person.Key], employer); index++;
            }
        }
        private DayStage CurrentStage => phase == 0 ? DayStage.Work : phase == 2 ? DayStage.Shopping : DayStage.Home;
        private void FailRoute(string id, string reason)
        {
            routes[id].Clear();
            preview.Simulation.ReportUnreachable(id, CurrentStage, reason);
        }
        private void SetRoute(string id, Vector3 destination, string target)
        {
            travelTimes[id] = 0; routeTargets[id] = target;
            if (people[id] == null || blockedTargets.Contains(target)
                || float.IsNaN(destination.x) || float.IsInfinity(destination.x)
                || float.IsNaN(destination.y) || float.IsInfinity(destination.y)
                || float.IsNaN(destination.z) || float.IsInfinity(destination.z))
            {
                FailRoute(id, "Destination unavailable: " + target);
                return;
            }
            var origin = people[id].transform.position;
            var route = routes[id]; route.Clear();
            // Door fronts join the road; waypoints never enter building colliders.
            route.Enqueue(new Vector3(origin.x, 0.8f, 0));
            route.Enqueue(new Vector3(destination.x, 0.8f, 0));
            route.Enqueue(destination);
        }
        private void AdvanceWalking()
        {
            if (!Walking || Paused) return;
            bool arrived = true;
            foreach (var person in people)
            {
                var route = routes[person.Key];
                if (route.Count == 0) continue;
                travelTimes[person.Key] += Time.deltaTime;
                if (person.Value == null || blockedTargets.Contains(routeTargets[person.Key]) || travelTimes[person.Key] > RouteTimeout)
                {
                    FailRoute(person.Key, "Route unavailable or timed out: " + routeTargets[person.Key]);
                    continue;
                }
                person.Value.transform.position = Vector3.MoveTowards(person.Value.transform.position, route.Peek(), 5 * Time.deltaTime);
                if (Vector3.Distance(person.Value.transform.position, route.Peek()) < 0.01f)
                {
                    route.Dequeue();
                    if (route.Count == 0)
                    {
                        if (phase == 0) preview.Simulation.ArriveAtWork(person.Key);
                        else if (phase == 2) preview.Simulation.ArriveAtBakery(person.Key);
                        else if (phase == 4) preview.Simulation.ArriveAtHome(person.Key);
                    }
                }
                if (route.Count > 0) arrived = false;
            }
            if (phase == 1 || phase == 3)
            {
                stopTime -= Time.deltaTime;
                if (stopTime > 0) return;
                if (phase == 1 && !preview.Simulation.FinishWork()) return;
                if (phase == 3 && !preview.Simulation.FinishShopping()) return;
                phase++;
                int index = 0;
                foreach (var person in people)
                    SetRoute(person.Key, phase == 2 ? new Vector3(4.5f + index++ % 5 * 1.1f, 0.8f, 1.4f + index / 5 * 0.4f) : homes[person.Key],
                        phase == 2 ? "bakery" : "home:" + person.Key);
            }
            else if (arrived)
            {
                if (phase == 4)
                {
                    if (preview.Simulation.FinishDay()) { Walking = false; preview.RememberCompletedDay(); }
                }
                else { phase++; stopTime = 2; }
            }
        }
        private void OnDestroy()
        {
            foreach (var material in materials) if (material != null) Destroy(material);
            foreach (var mesh in islandMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}
