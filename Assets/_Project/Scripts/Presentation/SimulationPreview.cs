using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingEconomy.Presentation
{
    [DisallowMultipleComponent]
    public sealed class SimulationPreview : MonoBehaviour
    {
        private DailySimulation simulation;
        private Vector2 scroll;
        private SettlementView settlement;
        private bool autoDays;
        private bool showResidents, showLedger, showRouteTests;
        private SettlementReport report;
        private int reportLedgerCount = -1;
        private int reportJobCount = -1;
        private bool reportDayInProgress;
        [SerializeField, HideInInspector] private ReloadCheckpoint reloadCheckpoint = new ReloadCheckpoint();
        [System.NonSerialized] private bool ready;
        [System.NonSerialized] private string initializationError;
        private string saveMessage = "Save / Load available when everyone is home.";
        private string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "Saves", "settlement.xml");
        private void Update()
        {
            if (!EnsureInitialized()) return;
            if (autoDays && !settlement.Walking && !settlement.Paused) AdvanceDay();
        }
        private void AdvanceDay() { RememberCompletedDay(); simulation.BeginDay(); settlement.BeginDay(); }
        public void RememberCompletedDay()
        {
            CaptureHeroPose();
            reloadCheckpoint.Remember(simulation);
        }
        private void CaptureHeroPose()
        {
            if (simulation?.Hero != null && settlement?.Player != null && settlement.IsInitialized)
                simulation.SetHeroPose(settlement.Player.CapturePose());
        }
        private void ResetScenario(DailySimulation scenario)
        {
            autoDays = false; simulation = scenario; simulation.EnableHero(); settlement.ResetWalking();
            settlement.CloseInteraction(); settlement.Player.RestorePose(simulation.HeroPose);
            settlement.ClearRouteTests();
            report = null;
            RememberCompletedDay();
        }
        public DailySimulation Simulation => simulation;
        public Rect Panel => new Rect(12, 12, Mathf.Min(360, Screen.width * 0.45f), Screen.height - 24);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartPreview()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/MainSimulation.unity") return;
            SimulationPreview existing = null;
            foreach (var candidate in FindObjectsByType<SimulationPreview>(FindObjectsInactive.Include))
                if (candidate.gameObject.scene == scene)
                {
                    if (existing == null) existing = candidate;
                    else
                    {
                        candidate.enabled = false;
                        var duplicateView = candidate.GetComponent<SettlementView>();
                        if (duplicateView != null) { duplicateView.ReleaseGeneratedWorld(); duplicateView.enabled = false; }
                    }
                }
            if (existing == null) new GameObject("SimulationPreview").AddComponent<SimulationPreview>();
            else existing.StartFreshSession();
        }
        private void Awake() => EnsureInitialized();
        private void OnEnable() => EnsureInitialized();
        private void OnDisable()
        {
            autoDays = false;
            if (simulation == null || reloadCheckpoint == null) return;
            try { CaptureHeroPose(); reloadCheckpoint.Remember(simulation); }
            catch (System.Exception e) { saveMessage = "Reload checkpoint failed: " + e.Message; }
        }
        private void StartFreshSession()
        {
            simulation = null; ready = false; initializationError = null;
            reloadCheckpoint = new ReloadCheckpoint();
            autoDays = false; report = null;
            saveMessage = "New session. Use Load to restore a disk save.";
            gameObject.SetActive(true); enabled = true;
            EnsureInitialized();
        }
        private bool EnsureInitialized()
        {
            if (!Application.isPlaying || !enabled) return false;
            if (ready && simulation != null && settlement != null && settlement.IsInitialized) return true;
            if (initializationError != null) return false;
            try
            {
                if (reloadCheckpoint == null) reloadCheckpoint = new ReloadCheckpoint();
                bool restoring = simulation == null && reloadCheckpoint.HasSnapshot;
                bool interrupted = reloadCheckpoint.Interrupted;
                if (simulation == null) simulation = restoring ? reloadCheckpoint.Restore() : new DailySimulation();
                simulation.EnableHero();
                settlement = GetComponent<SettlementView>();
                if (settlement == null) settlement = gameObject.AddComponent<SettlementView>();
                settlement.enabled = true;
                settlement.Initialize(this);
                settlement.ResetWalking();
                autoDays = false; report = null;
                ready = true;
                RememberCompletedDay();
                if (restoring)
                    saveMessage = "UI restored at completed day " + simulation.Economy.Tick
                        + (interrupted ? ". Interrupted day rolled back; Next day restarts it." : ".") + " Auto days stopped.";
                return true;
            }
            catch (System.Exception e)
            {
                ready = false; autoDays = false; initializationError = e.Message;
                return false;
            }
        }
        private void OnGUI()
        {
            if (!EnsureInitialized())
            {
                if (initializationError == null) return;
                GUILayout.BeginArea(Panel, GUI.skin.box);
                GUILayout.Label("UI recovery failed: " + initializationError);
                GUILayout.Label("Disk save was not changed.");
                if (GUILayout.Button("Start new session")) StartFreshSession();
                GUILayout.EndArea(); return;
            }
            var economy = simulation.Economy;
            GUILayout.BeginArea(Panel, GUI.skin.box);
            GUILayout.Label("Living Economy — daily simulation");
            GUILayout.Label($"Day {economy.Tick} | NPC: {economy.Residents.Count} | Coins: {economy.TotalMoney()} / {economy.InitialMoney}");
            GUILayout.Label($"{(simulation.DayInProgress ? "Current" : "Completed")} day: paid {simulation.LastPaid}/{simulation.Jobs.Count} | fed {simulation.LastFed}/{economy.Residents.Count} | bread produced {simulation.LastBread}");
            GUILayout.Label($"Unemployed: {economy.Residents.Count - simulation.Jobs.Count - simulation.Businesses.Count} | unpaid: {simulation.LastUnpaid} | failed routes: {simulation.LastUnreachable}");
            GUILayout.Label("Automatic job search: " + (simulation.AutoEmployment ? "on" : "off (legacy save)"));
            GUILayout.Label("Click a resident, farm or bakery on the map to inspect it.");
            GUILayout.BeginHorizontal();
            GUI.enabled = !settlement.Walking && !autoDays;
            if (GUILayout.Button("Next day")) AdvanceDay();
            GUI.enabled = !settlement.Walking && !autoDays && !settlement.HasBlockedTargets;
            if (GUILayout.Button("Run 30 days")) { for (int i = 0; i < 30; i++) simulation.Step(); settlement.ResetWalking(); RememberCompletedDay(); }
            GUI.enabled = true;
            if (GUILayout.Button("Reset")) ResetScenario(new DailySimulation());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Food shortage")) ResetScenario(new DailySimulation(farmYield: 0));
            if (GUILayout.Button("No business capital")) ResetScenario(new DailySimulation(capital: 0));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Job search demo"))
            {
                var demo = new DailySimulation();
                demo.AssignJob("npc-01", null, out _); demo.AssignJob("npc-11", null, out _);
                ResetScenario(demo);
            }
            if (GUILayout.Button("Hero economy demo (test scenario)")) ResetScenario(DailySimulation.HeroDemo());
            GUILayout.BeginHorizontal();
            autoDays = GUILayout.Toggle(autoDays, "Auto days");
            settlement.Paused = GUILayout.Toggle(settlement.Paused, "Pause walking");
            GUILayout.EndHorizontal();
            GUILayout.Label("Route: " + settlement.Activity);
            if (settlement.HasBlockedTargets) GUILayout.Label("Route test active: fast run disabled. Use Next day or clear route tests.");
            GUILayout.BeginHorizontal();
            GUI.enabled = !simulation.DayInProgress && !autoDays;
            if (GUILayout.Button("Save"))
            {
                try { CaptureHeroPose(); SimulationSave.Write(SavePath, simulation); saveMessage = "Saved day " + simulation.Economy.Tick; }
                catch (System.Exception e) { saveMessage = "Save failed: " + e.Message; }
            }
            if (GUILayout.Button("Load"))
            {
                try { var loaded = SimulationSave.Read(SavePath); ResetScenario(loaded); saveMessage = "Loaded day " + loaded.Economy.Tick
                    + (settlement.Player.LastPoseRestoreSafe ? "" : ". Unsafe saved position; moved to spawn."); }
                catch (System.Exception e) { saveMessage = "Load failed: " + e.Message; }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(saveMessage);
            GUILayout.Label("Daily rotating priority. Wages, purchases and meals settle after each phase; arrivals do not change priority.");
            DrawReport();
            showRouteTests = GUILayout.Toggle(showRouteTests, "Route failure tests");
            if (showRouteTests) settlement.DrawRouteTests();
            settlement.DrawSelection();

            foreach (var business in new[] { simulation.Farm, simulation.Bakery })
                GUILayout.Label($"{business.Name}: coins={business.Money}, grain={business.Stock(Good.Grain)}, bread={business.Stock(Good.Bread)}");
            showResidents = GUILayout.Toggle(showResidents, "All residents");
            if (showResidents)
                foreach (var npc in simulation.Economy.Residents)
                {
                    string job = simulation.Jobs.TryGetValue(npc.Id, out var employer) ? employer : simulation.IsOwner(npc.Id) ? "owner" : "unemployed";
                    GUILayout.Label($"{npc.Name} | job={job} | coins={npc.Money} | bread={npc.Stock(Good.Bread)} | hunger={npc.Hunger}");
                }
            GUILayout.Space(12);
            showLedger = GUILayout.Toggle(showLedger, "Recent ledger entries");
            var ledger = simulation.Economy.Ledger;
            if (showLedger)
                for (int i = Mathf.Max(0, ledger.Count - 10); i < ledger.Count; i++) GUILayout.Label(ledger[i].ToString());
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        private void DrawReport()
        {
            if (report == null || report.Day != simulation.Economy.Tick || reportLedgerCount != simulation.Economy.Ledger.Count
                || reportJobCount != simulation.Jobs.Count || reportDayInProgress != simulation.DayInProgress)
            {
                report = SettlementReport.Capture(simulation);
                reportLedgerCount = simulation.Economy.Ledger.Count;
                reportJobCount = simulation.Jobs.Count;
                reportDayInProgress = simulation.DayInProgress;
            }
            GUILayout.Label(report.DayInProgress ? "Economy snapshot (day in progress)" : "Economy report (completed day)");
            GUILayout.Label($"Hungry: {report.Hungry}/{report.Residents} | severe (75+): {report.SevereHunger}");
            GUILayout.Label($"Jobs: {report.Employees} | owners: {report.Owners} | unemployed: {report.Unemployed} | vacancies: {report.Vacancies}");
            GUILayout.Label($"Failed job searches today: {report.JobSearchRefusals} | unpaid: {report.Unpaid}");
            GUILayout.Label($"Food refusals today: no stock {report.FoodStockRefusals}, no money {report.FoodMoneyRefusals}");
            GUILayout.Label($"Stocks (all accounts): grain {report.Grain}, bread {report.Bread}");
            GUILayout.Label($"Coins: NPC wallets {report.ResidentMoney}, hero {report.HeroMoney}, businesses {report.BusinessMoney} | conserved: {report.MoneyConserved}");
            GUILayout.Label($"Wallets: min {report.MinimumWallet}, median {report.MedianWallet:0.0}, mean {report.AverageWallet:0.0}, max {report.MaximumWallet}");
            GUILayout.Label($"Poorest: {report.Poorest} | richest: {report.Richest}");
            GUILayout.Label($"Top 20% hold {report.TopFifthWalletShare:0.0}% of wallet coins (business capital excluded)");
        }
    }
}
