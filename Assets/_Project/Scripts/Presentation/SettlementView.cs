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
        private HeroInteraction interaction;
        public HeroInteraction Interaction => interaction;
        public bool InteractionOpen => interaction != null && interaction.IsOpen;
        public void CloseInteraction() { if (interaction != null) interaction.Close(); }
        public bool TryPersonPosition(string id, out Vector3 position)
        {
            if (id != null && people.TryGetValue(id, out var renderer) && renderer != null)
            { position = renderer.transform.position; return true; }
            position = Vector3.zero; return false;
        }
        public IslandPlayer Player => player;
        [SerializeField, HideInInspector] private Vector3 cameraFocus = new Vector3(0, 0, 1);
        [SerializeField, HideInInspector] private float cameraYaw = -25, cameraPitch = 43, cameraDistance = 70;
        private readonly Dictionary<GameObject, string> targets = new Dictionary<GameObject, string>();
        private readonly Dictionary<string, Renderer> people = new Dictionary<string, Renderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> islandMeshes = new List<Mesh>();
        private GameObject marker;
        private Material farmerColor, bakerColor, hungryColor, unemployedColor, ownerColor;
        private IslandGenerator world;
        private readonly OverviewCameraController overview = new OverviewCameraController();
        private NpcMovementView movement;
        private SettlementSelectionView selection;
        private readonly Dictionary<string, Vector3> homes = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> workplaces = new Dictionary<string, Vector3>();
        [SerializeField, HideInInspector] private Transform generatedRoot;
        [System.NonSerialized] private bool initialized;
        public bool IsInitialized => initialized;
        public bool Walking => movement != null && movement.Walking;
        public bool Paused { get => movement != null && movement.Paused; set { if (movement != null) movement.Paused = value; } }
        public bool HasBlockedTargets => movement != null && movement.HasBlockedTargets;
        public void ClearRouteTests() => movement?.ClearRouteTests();
        public string Activity => movement?.Activity ?? "Day complete";

        public void Initialize(SimulationPreview source)
        {
            if (initialized && preview == source && generatedRoot != null) return;
            preview = source;
            ReleaseGeneratedWorld();
            targets.Clear(); people.Clear(); homes.Clear(); workplaces.Clear();
            generatedRoot = new GameObject("GeneratedSettlement").transform;
            generatedRoot.SetParent(transform, false);
            world = new IslandGenerator(generatedRoot, materials, islandMeshes, targets);
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
            var grass = world.ColorMaterial(new Color(0.29f, 0.43f, 0.29f));
            var path = world.ColorMaterial(new Color(0.64f, 0.57f, 0.44f));
            var timber = world.ColorMaterial(new Color(0.58f, 0.37f, 0.21f));
            var plaster = world.ColorMaterial(new Color(0.85f, 0.73f, 0.53f));
            var roof = world.ColorMaterial(new Color(0.50f, 0.24f, 0.18f));
            farmerColor = world.ColorMaterial(new Color(0.32f, 0.65f, 0.87f));
            bakerColor = world.ColorMaterial(new Color(0.94f, 0.70f, 0.26f));
            hungryColor = world.ColorMaterial(new Color(0.9f, 0.25f, 0.22f));
            unemployedColor = world.ColorMaterial(new Color(0.55f, 0.55f, 0.58f));
            ownerColor = world.ColorMaterial(new Color(0.35f, 0.72f, 0.42f));
            world.BuildIsland(grass, timber);
            world.Shape("Main road", PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(24, 0.05f, 2), path);
            world.Shape("Village path", PrimitiveType.Cube, new Vector3(0, 0.03f, -4), new Vector3(2, 0.05f, 10), path);
            world.Building("Farm", "farm", new Vector3(-7, 0, 5), timber, roof);
            world.Building("Bakery", "bakery", new Vector3(7, 0, 5), plaster, roof);
            var soil = world.ColorMaterial(new Color(0.38f, 0.26f, 0.16f));
            var wheat = world.ColorMaterial(new Color(0.77f, 0.69f, 0.25f));
            world.Shape("Field", PrimitiveType.Cube, new Vector3(-7, 0.02f, 9), new Vector3(7, 0.06f, 4), soil);
            for (int row = 0; row < 4; row++)
                world.Shape("Crop row", PrimitiveType.Cube, new Vector3(-7, 0.22f, 7.6f + row), new Vector3(6, 0.4f, 0.25f), wheat);
            for (int i = 0; i < 4; i++)
                world.Building("House " + (i + 1), null, new Vector3(-9 + i * 6, 0, -7), plaster, roof);
            int farmIndex = 0, bakeryIndex = 0;
            foreach (var npc in preview.Simulation.Economy.Residents)
            {
                bool farm = preview.Simulation.EmployerOf(npc.Id) == preview.Simulation.Farm.Id;
                int index = farm ? farmIndex++ : bakeryIndex++;
                var person = world.Shape(npc.Name, PrimitiveType.Capsule, NpcMovementView.WorkSlot(farm, index),
                    new Vector3(0.65f, 0.8f, 0.65f), ResidentMaterial(npc));
                targets.Add(person, npc.Id); people.Add(npc.Id, person.GetComponent<Renderer>());
                workplaces.Add(npc.Id, person.transform.position);
                int number = homes.Count;
                var home = new Vector3(-9 + (number / 5) * 6 + (number % 5 - 2) * 0.55f, 0.8f, -3.5f);
                homes.Add(npc.Id, home);
                person.transform.position = home;
            }
            marker = world.Shape("Selection", PrimitiveType.Cylinder, Vector3.zero, new Vector3(1, 0.035f, 1),
                world.ColorMaterial(new Color(0.3f, 1, 0.7f)));
            Destroy(marker.GetComponent<Collider>()); marker.SetActive(false);
            movement = new NpcMovementView(preview, people, homes, workplaces, CloseInteraction);
            selection = new SettlementSelectionView(preview, targets, people, marker);
            BuildPlayer();
            initialized = true;
        }

        private void BuildPlayer()
        {
            var hero = new GameObject("Island hero"); hero.transform.SetParent(generatedRoot, false);
            hero.transform.position = new Vector3(0, 0.08f, -1.5f);
            var shirt = world.ColorMaterial(new Color(0.55f, 0.22f, 0.68f));
            var skin = world.ColorMaterial(new Color(0.86f, 0.65f, 0.43f));
            var trousers = world.ColorMaterial(new Color(0.16f, 0.20f, 0.26f));
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
            interaction = hero.AddComponent<HeroInteraction>();
            interaction.Initialize(this, preview, mapCamera);
            player.RestorePose(preview.Simulation.HeroPose);
        }

        private void PlayerPart(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material)
        {
            var part = world.Decoration(name, type, Vector3.zero, scale, material);
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

        private void Update()
        {
            if (!initialized || preview == null || preview.Simulation == null || mapCamera == null) return;
            movement.AdvanceWalking();
            // Reserve the left side for the panel, leaving the map centered in its own viewport.
            float left = Mathf.Clamp((preview.Panel.xMax + 12) / Mathf.Max(1, Screen.width), 0, 0.8f);
            mapCamera.rect = new Rect(left, 0, 1 - left, 1);
            UpdateCameraControls();
            foreach (var npc in preview.Simulation.Economy.Residents)
            {
                if (people[npc.Id] != null)
                    people[npc.Id].sharedMaterial = ResidentMaterial(npc);
            }
            selection.Update(mapCamera, player);
        }
        public void DrawSelection() => selection?.DrawSelection();
        public void DrawRouteTests() => movement?.DrawRouteTests();
        public void SetRouteBlocked(string id, bool blocked) => movement?.SetBlocked(id, blocked);
        public void ResetWalking() => movement?.ResetWalking();
        public void BeginDay() => movement.BeginDay();
        public void ResetCamera() => overview.ResetCamera(mapCamera, ref cameraFocus, ref cameraYaw, ref cameraPitch, ref cameraDistance);
        private void ApplyCameraPose() => overview.ApplyCameraPose(mapCamera, ref cameraFocus, cameraYaw, ref cameraPitch, ref cameraDistance);
        private void UpdateCameraControls() => overview.UpdateCameraControls(mapCamera, player, ref cameraFocus, ref cameraYaw, ref cameraPitch, ref cameraDistance);
        private static float ZoomDistance(float distance, float scroll) => OverviewCameraController.ZoomDistance(distance, scroll);
        private Material ResidentMaterial(Resident npc)
        {
            if (npc.Hunger > 0) return hungryColor;
            if (preview.Simulation.IsOwner(npc.Id)) return ownerColor;
            string employer = preview.Simulation.EmployerOf(npc.Id);
            return employer == preview.Simulation.Farm.Id ? farmerColor
                : employer == preview.Simulation.Bakery.Id ? bakerColor : unemployedColor;
        }
        private void OnDestroy()
        {
            foreach (var material in materials) if (material != null) Destroy(material);
            foreach (var mesh in islandMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}
