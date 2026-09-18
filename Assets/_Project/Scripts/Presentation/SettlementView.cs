using System.Collections.Generic;
using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingEconomy.Presentation
{
    // View objects keep stable IDs and resolve the current scenario after each reset.
    public sealed class SettlementView : MonoBehaviour
    {
        private SimulationPreview preview;
        private Camera mapCamera;
        private string selectedId;
        private readonly Dictionary<GameObject, string> targets = new Dictionary<GameObject, string>();
        private readonly Dictionary<string, Renderer> people = new Dictionary<string, Renderer>();
        private readonly List<Material> materials = new List<Material>();
        private GameObject marker;
        private Material farmerColor, bakerColor, hungryColor;
        private readonly Dictionary<string, Vector3> homes = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> workplaces = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Queue<Vector3>> routes = new Dictionary<string, Queue<Vector3>>();
        private float stopTime;
        private int phase;
        public bool Walking { get; private set; }
        public bool Paused { get; set; }
        public string Activity => !Walking ? "At home" : phase == 0 ? "Going to work" : phase == 1 ? "At work" : phase == 2 ? "Going to bakery" : phase == 3 ? "At bakery" : "Going home";

        public void Initialize(SimulationPreview source)
        {
            preview = source;
            mapCamera = Camera.main;
            if (mapCamera == null)
            {
                mapCamera = new GameObject("SettlementCamera").AddComponent<Camera>();
                mapCamera.tag = "MainCamera";
            }
            mapCamera.transform.position = new Vector3(0, 25, -23);
            mapCamera.transform.LookAt(new Vector3(0, 0, 1));
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = 15;
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
            Shape("Ground", PrimitiveType.Cube, new Vector3(0, -0.2f, 1), new Vector3(27, 0.4f, 23), grass);
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
                bool farm = npc.Id == "npc-00" || (preview.Simulation.Jobs.TryGetValue(npc.Id, out var job) && job == "farm");
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
            shape.transform.SetParent(transform); shape.transform.position = position;
            shape.transform.localScale = scale; shape.GetComponent<Renderer>().sharedMaterial = material;
            return shape;
        }
        private void Building(string name, string id, Vector3 location, Material wall, Material roof)
        {
            var body = Shape(name, PrimitiveType.Cube, location + Vector3.up * 1.25f, new Vector3(3.5f, 2.5f, 2.5f), wall);
            var top = Shape(name + " roof", PrimitiveType.Cube, location + Vector3.up * 2.65f, new Vector3(4, 0.4f, 3), roof);
            if (id != null) { targets.Add(body, id); targets.Add(top, id); }
        }
        private void Update()
        {
            AdvanceWalking();
            // Reserve the left side for the panel, leaving the map centered in its own viewport.
            float left = (preview.Panel.xMax + 12) / Screen.width;
            mapCamera.rect = new Rect(left, 0, 1 - left, 1);
            foreach (var npc in preview.Simulation.Economy.Residents)
            {
                bool farm = npc.Id == "npc-00" || (preview.Simulation.Jobs.TryGetValue(npc.Id, out var job) && job == "farm");
                people[npc.Id].sharedMaterial = npc.Hunger > 0 ? hungryColor : farm ? farmerColor : bakerColor;
            }
            if (selectedId != null && people.TryGetValue(selectedId, out var selectedPerson))
                marker.transform.position = new Vector3(selectedPerson.transform.position.x, 0.12f, selectedPerson.transform.position.z);
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            var position = Mouse.current.position.ReadValue();
            if (!mapCamera.pixelRect.Contains(position)) return;
            if (Physics.Raycast(mapCamera.ScreenPointToRay(position), out var hit, 100)
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
            if (selectedId == null) { GUILayout.Label("Selection: none. Blue = farm; yellow = bakery; red = hungry."); return; }
            Resident selected = selectedId == "farm" ? preview.Simulation.Farm : selectedId == "bakery" ? preview.Simulation.Bakery : null;
            if (selected == null)
                foreach (var npc in preview.Simulation.Economy.Residents) if (npc.Id == selectedId) { selected = npc; break; }
            if (selected == null) return;
            GUILayout.Label("Selected: " + selected.Name);
            GUILayout.Label($"Coins: {selected.Money} | grain: {selected.Stock(Good.Grain)} | bread: {selected.Stock(Good.Bread)}");
            if (selectedId.StartsWith("npc-"))
                GUILayout.Label($"Job: {(preview.Simulation.Jobs.TryGetValue(selectedId, out var job) ? job : "owner")} | hunger: {selected.Hunger}/100");
            else GUILayout.Label("Owner: " + (selectedId == "farm" ? "Марек" : "Данило") + " | Employees: 9");
        }

        public void ResetWalking()
        {
            Walking = false; Paused = false; phase = 0;
            foreach (var person in people)
            {
                person.Value.transform.position = homes[person.Key]; routes[person.Key].Clear();
            }
        }
        public void BeginDay()
        {
            ResetWalking(); Walking = true;
            foreach (var person in people) SetRoute(person.Key, workplaces[person.Key]);
        }
        private void SetRoute(string id, Vector3 destination)
        {
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
                if (phase == 1) preview.Simulation.FinishWork();
                phase++;
                int index = 0;
                foreach (var person in people)
                    SetRoute(person.Key, phase == 2 ? new Vector3(4.5f + index++ % 5 * 1.1f, 0.8f, 1.4f + index / 5 * 0.4f) : homes[person.Key]);
            }
            else if (arrived)
            {
                if (phase == 4) { preview.Simulation.FinishDay(); Walking = false; }
                else { phase++; stopTime = 2; }
            }
        }
        private void OnDestroy() { foreach (var material in materials) Destroy(material); }
    }
}
