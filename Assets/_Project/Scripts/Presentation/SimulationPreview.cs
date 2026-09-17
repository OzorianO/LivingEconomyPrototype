using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingEconomy.Presentation
{
    public sealed class SimulationPreview : MonoBehaviour
    {
        private Economy economy;
        private Vector2 scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartPreview()
        {
            if (SceneManager.GetActiveScene().path != "Assets/_Project/Scenes/MainSimulation.unity") return;
            new GameObject("SimulationPreview").AddComponent<SimulationPreview>();
        }

        private void Awake() { economy = PrototypeScenario.Create(); }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, Mathf.Min(780, Screen.width - 24), Screen.height - 24), GUI.skin.box);
            GUILayout.Label("Living Economy — wallets and trade preview");
            GUILayout.Label($"NPC: {economy.Residents.Count} | Tick: {economy.Tick} | Coins: {economy.TotalMoney()} / {economy.InitialMoney}");
            GUILayout.Label("Manual transactions only. Production, jobs and hunger simulation come next.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Buy 1 bread: Oleg -> baker"))
            {
                economy.AdvanceTick();
                Debug.Log(economy.Buy("npc-04", "npc-06", Good.Bread, 1, 5));
            }
            if (GUILayout.Button("Pay 8 coins: baker -> Oleg"))
            {
                economy.AdvanceTick();
                Debug.Log(economy.Transfer("npc-06", "npc-04", 8, "Manual payment preview"));
            }
            if (GUILayout.Button("Reset")) economy = PrototypeScenario.Create();
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var npc in economy.Residents)
                GUILayout.Label($"{npc.Name} | {npc.Profession} | coins={npc.Money} | grain={npc.Stock(Good.Grain)} | bread={npc.Stock(Good.Bread)} | hunger={npc.Hunger}");
            GUILayout.Space(12);
            GUILayout.Label("Recent ledger entries (money: From -> To; goods: seller -> buyer)");
            for (int i = Mathf.Max(0, economy.Ledger.Count - 8); i < economy.Ledger.Count; i++)
                GUILayout.Label(economy.Ledger[i].ToString());
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
