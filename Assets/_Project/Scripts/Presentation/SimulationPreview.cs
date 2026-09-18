using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingEconomy.Presentation
{
    public sealed class SimulationPreview : MonoBehaviour
    {
        private DailySimulation simulation;
        private Vector2 scroll;
        private SettlementView settlement;
        private bool autoDays;
        private void Update()
        {
            if (autoDays && !settlement.Walking && !settlement.Paused) AdvanceDay();
        }
        private void AdvanceDay() { simulation.Step(); settlement.BeginDay(); }
        private void ResetScenario(DailySimulation scenario)
        {
            autoDays = false; simulation = scenario; settlement.ResetWalking();
        }
        public DailySimulation Simulation => simulation;
        public Rect Panel => new Rect(12, 12, Mathf.Min(360, Screen.width * 0.45f), Screen.height - 24);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartPreview()
        {
            if (SceneManager.GetActiveScene().path == "Assets/_Project/Scenes/MainSimulation.unity")
                new GameObject("SimulationPreview").AddComponent<SimulationPreview>();
        }
        private void Awake()
        {
            simulation = new DailySimulation();
            settlement = gameObject.AddComponent<SettlementView>();
            settlement.Initialize(this);
        }
        private void OnGUI()
        {
            var economy = simulation.Economy;
            GUILayout.BeginArea(Panel, GUI.skin.box);
            GUILayout.Label("Living Economy — daily simulation");
            GUILayout.Label($"Day {economy.Tick} | NPC: 20 | Coins: {economy.TotalMoney()} / {economy.InitialMoney}");
            GUILayout.Label($"Last day: paid {simulation.LastPaid}/18 | fed {simulation.LastFed}/20 | bread produced {simulation.LastBread}");
            GUILayout.Label("Click a resident, farm or bakery on the map to inspect it.");
            GUILayout.BeginHorizontal();
            GUI.enabled = !settlement.Walking && !autoDays;
            if (GUILayout.Button("Next day")) AdvanceDay();
            if (GUILayout.Button("Run 30 days")) { for (int i = 0; i < 30; i++) simulation.Step(); settlement.ResetWalking(); }
            GUI.enabled = true;
            if (GUILayout.Button("Reset")) ResetScenario(new DailySimulation());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Food shortage")) ResetScenario(new DailySimulation(farmYield: 0));
            if (GUILayout.Button("No business capital")) ResetScenario(new DailySimulation(capital: 0));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            autoDays = GUILayout.Toggle(autoDays, "Auto days");
            settlement.Paused = GUILayout.Toggle(settlement.Paused, "Pause walking");
            GUILayout.EndHorizontal();
            GUILayout.Label("Route: " + settlement.Activity);
            GUILayout.Label("Daily accounts settle first; walking visualizes the daily route.");
            settlement.DrawSelection();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var business in new[] { simulation.Farm, simulation.Bakery })
                GUILayout.Label($"{business.Name}: coins={business.Money}, grain={business.Stock(Good.Grain)}, bread={business.Stock(Good.Bread)}");
            foreach (var npc in simulation.Economy.Residents)
            {
                string job = simulation.Jobs.TryGetValue(npc.Id, out var employer) ? employer : "owner";
                GUILayout.Label($"{npc.Name} | job={job} | coins={npc.Money} | bread={npc.Stock(Good.Bread)} | hunger={npc.Hunger}");
            }
            GUILayout.Space(12);
            GUILayout.Label("Recent ledger entries");
            var ledger = simulation.Economy.Ledger;
            for (int i = Mathf.Max(0, ledger.Count - 10); i < ledger.Count; i++) GUILayout.Label(ledger[i].ToString());
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
