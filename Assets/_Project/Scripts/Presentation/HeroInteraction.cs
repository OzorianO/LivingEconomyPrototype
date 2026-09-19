using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingEconomy.Presentation
{
    // Presentation validates reach; shared simulation commands own the actual transaction.
    [DisallowMultipleComponent]
    public sealed class HeroInteraction : MonoBehaviour
    {
        private SettlementView view;
        private SimulationPreview preview;
        private Camera camera;
        private string openedId;
        private Vector2 scroll;
        private string actionMessage = "Start with 0 coins. Hero economy demo is a separate test fixture.";
        public bool IsOpen => openedId != null;
        public string OpenedId => openedId;
        public const float Reach = 3;

        public void Initialize(SettlementView settlement, SimulationPreview source, Camera mapCamera)
        { view = settlement; preview = source; camera = mapCamera; }

        private void Update()
        {
            if (view == null || view.Player == null || !view.Player.Exploring || preview.Simulation == null)
            { Close(); return; }
            if (openedId != null && !CanReach(openedId)) Close();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else TryOpen(NearestId());
            }
        }

        private bool PositionOf(string id, out Vector3 point)
        {
            if (view != null && view.TryWorldActionPosition(id, out point)) return true;
            if (id == "farm" || id == "bakery")
            { point = new Vector3(id == "farm" ? -7 : 7, 0.8f, 3.5f); return true; }
            point = Vector3.zero;
            return view != null && view.TryPersonPosition(id, out point);
        }

        public bool CanReach(string id)
        {
            if (string.IsNullOrEmpty(id) || view == null || view.Player == null || !view.Player.Exploring
                || !PositionOf(id, out var point)) return false;
            var origin = view.Player.transform.position + Vector3.up * 0.9f;
            var offset = point - origin;
            if (offset.sqrMagnitude > Reach * Reach) return false;
            // Raycasts ignore colliders containing their origin; reject that case explicitly.
            foreach (var collider in Physics.OverlapSphere(origin, 0.04f, ~0, QueryTriggerInteraction.Ignore))
                if (!collider.transform.IsChildOf(view.Player.transform) && !(collider is CapsuleCollider)) return false;
            // NPC capsules do not obstruct conversation, but building walls do.
            foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(view.Player.transform) && !(hit.collider is CapsuleCollider)) return false;
            return true;
        }

        public string NearestId()
        {
            if (preview == null || preview.Simulation == null || view == null || view.Player == null) return null;
            string best = null; float bestDistance = float.PositiveInfinity;
            Consider("farm", ref best, ref bestDistance); Consider("bakery", ref best, ref bestDistance);
            Consider("tree-01", ref best, ref bestDistance); Consider(RecipeCatalog.WorkbenchId, ref best, ref bestDistance);
            foreach (var npc in preview.Simulation.Economy.Residents) Consider(npc.Id, ref best, ref bestDistance);
            return best;
        }

        private void Consider(string id, ref string best, ref float bestDistance)
        {
            if (!CanReach(id) || !PositionOf(id, out var point)) return;
            float distance = (point - (view.Player.transform.position + Vector3.up * 0.9f)).sqrMagnitude;
            if (distance < bestDistance || (Mathf.Approximately(distance, bestDistance) && string.CompareOrdinal(id, best) < 0))
            { best = id; bestDistance = distance; }
        }

        public bool TryOpen(string id)
        {
            if (!CanReach(id)) return false;
            openedId = id; scroll = Vector2.zero; return true;
        }
        public void Close() { openedId = null; }

        public bool BuyBread()
        {
            if (openedId != "bakery" || !CanReach("bakery") || preview == null || preview.Simulation?.Hero == null)
            { actionMessage = "Open the bakery interaction within reach first."; return false; }
            return Perform(AgentAction.BuyBread);
        }

        public bool ConsumeBread()
        {
            if (view == null || view.Player == null || !view.Player.Exploring || preview?.Simulation?.Hero == null) return false;
            return Perform(AgentAction.ConsumeBread);
        }

        public bool HarvestTree()
        {
            if (openedId != "tree-01" || !CanReach(openedId) || preview?.Simulation?.Hero == null) return false;
            var result = preview.Simulation.ExecuteWorldAction(preview.Simulation.Hero.Id, WorldAction.Harvest, openedId);
            actionMessage = result.Success ? "Collected 1 log." : result.Reason;
            preview.RememberCompletedDay();
            return result.Success;
        }

        public bool Craft(string recipeId)
        {
            if (openedId != RecipeCatalog.WorkbenchId || !CanReach(openedId) || preview?.Simulation?.Hero == null) return false;
            var result = preview.Simulation.ExecuteWorldAction(preview.Simulation.Hero.Id, WorldAction.Craft, recipeId, openedId);
            actionMessage = result.Success ? $"Crafted {result.Quantity} {result.ItemId}." : result.Reason;
            preview.RememberCompletedDay();
            return result.Success;
        }

        public bool Loot(Good? good, bool takeMoney)
        {
            if (!IsOpen || !CanReach(openedId) || preview?.Simulation?.Hero == null) return false;
            var selected = Account(openedId);
            if (selected == null || !selected.IsAgent || !selected.IsDead) return false;
            var result = preview.Simulation.Loot(preview.Simulation.Hero.Id, openedId, good,
                good.HasValue ? 1 : 0, takeMoney, true);
            actionMessage = result.Success ? "Personal property transferred from body." : result.Reason;
            preview.RememberCompletedDay();
            return result.Success;
        }

        private bool Perform(AgentAction action)
        {
            var result = preview.Simulation.ExecuteAction(preview.Simulation.Hero.Id, action);
            actionMessage = result.Success ? (action == AgentAction.BuyBread ? "Bought 1 bread for 6 coins." : "Ate 1 bread; hunger reduced by 25.") : result.Reason;
            preview.RememberCompletedDay();
            return result.Success;
        }

        private Resident Account(string id)
        {
            if (preview == null || preview.Simulation == null) return null;
            if (id == "farm") return preview.Simulation.Farm;
            if (id == "bakery") return preview.Simulation.Bakery;
            foreach (var npc in preview.Simulation.Economy.Residents) if (npc.Id == id) return npc;
            return null;
        }

        private void OnGUI()
        {
            if (view == null || view.Player == null || !view.Player.Exploring || camera == null || preview.Simulation == null) return;
            float width = Mathf.Max(100, Mathf.Min(460, camera.pixelRect.width - 20));
            float x = camera.pixelRect.x + 10;
            var hero = preview.Simulation.Hero;
            if (hero != null)
            {
                GUILayout.BeginArea(new Rect(x, 115, width, 125), GUI.skin.box);
                GUILayout.Label($"Hero: {hero.Money} coins | Bread: {hero.Items.Quantity(ItemCatalog.BreadId)} | Grain: {hero.Items.Quantity(ItemCatalog.GrainId)} | Axe: {hero.Items.Quantity(ItemCatalog.AxeId)}");
                GUILayout.Label($"Hunger: {hero.Hunger}/100 | Thirst: {hero.Thirst}/100 | Items: {hero.Items.TotalQuantity}/{hero.Items.Capacity}");
                GUI.enabled = hero.Stock(Good.Bread) > 0;
                if (GUILayout.Button("Eat 1 bread")) ConsumeBread();
                GUI.enabled = true;
                GUILayout.Label(actionMessage);
                GUILayout.EndArea();
            }
            if (!IsOpen)
            {
                var id = NearestId(); var account = Account(id);
                if (account == null && id != "tree-01" && id != RecipeCatalog.WorkbenchId) return;
                var rect = new Rect(x, Mathf.Max(115, Screen.height - 70), width, 55);
                string prompt = id == "tree-01" ? "E: Use resource tree" : id == RecipeCatalog.WorkbenchId ? "E: Use woodworking bench"
                    : "E: " + (account.IsDead ? "Loot body: " : id == "farm" || id == "bakery" ? "Inspect " : "Talk to ") + account.Name;
                if (GUI.Button(rect, prompt)) TryOpen(id);
                return;
            }
            if (openedId == "tree-01" || openedId == RecipeCatalog.WorkbenchId)
            {
                DrawWorldActionPanel(x, width, hero);
                return;
            }
            var selected = Account(openedId);
            if (selected == null) return;
            float height = Mathf.Min(260, Mathf.Max(100, Screen.height - 130));
            GUILayout.BeginArea(new Rect(x, Mathf.Max(115, Screen.height - height - 10), width, height), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(selected.Name);
            if (openedId == "farm" || openedId == "bakery")
            {
                var business = preview.Simulation.Business(openedId);
                if (selected.Suspended) GUILayout.Label("SUSPENDED: owner dead. Business funds/stock are not body loot.");
                GUILayout.Label($"Owner: {Account(business.Owner)?.Name ?? business.Owner}");
                GUILayout.Label($"Employees: {preview.Simulation.EmployeeCount(openedId)}/{business.Capacity} | wage: {business.Wage} coins/day");
                GUILayout.Label($"Business capital: {selected.Money} | grain: {selected.Stock(Good.Grain)} | bread: {selected.Stock(Good.Bread)}");
                GUILayout.Label(openedId == "farm" ? "The farm produces grain and sells it to the bakery." : "The bakery buys grain, bakes bread and sells it to residents.");
                if (openedId == "bakery")
                {
                    if (GUILayout.Button("Buy 1 bread — 6 coins")) BuyBread();
                    GUILayout.Label("Consumes real bakery stock. Employment is not implemented yet.");
                }
            }
            else
            {
                if (selected.IsDead)
                {
                    GUILayout.Label($"BODY: personal coins {selected.Money} | grain {selected.Stock(Good.Grain)} | bread {selected.Stock(Good.Bread)}");
                    if (GUILayout.Button("Take 1 bread")) Loot(Good.Bread, false);
                    if (GUILayout.Button("Take 1 grain")) Loot(Good.Grain, false);
                    if (GUILayout.Button("Take remaining personal coins")) Loot(null, true);
                }
                else
                {
                string job = preview.Simulation.EmployerOf(openedId);
                GUILayout.Label($"Job: {job ?? "unemployed"} | hunger: {selected.Hunger}/100");
                GUILayout.Label($"Coins: {selected.Money} | bread carried: {selected.Stock(Good.Bread)}");
                GUILayout.Label("Current decision: " + preview.Simulation.DecisionOf(openedId));
                GUILayout.Label("Information only: branching conversations and quests are not implemented yet.");
                }
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close (E / Escape)")) Close();
            GUILayout.EndArea();
        }

        private void DrawWorldActionPanel(float x, float width, Resident hero)
        {
            float height = openedId == "tree-01" ? 185 : 260;
            GUILayout.BeginArea(new Rect(x, Mathf.Max(115, Screen.height - height - 10), width, height), GUI.skin.box);
            if (openedId == "tree-01")
            {
                var node = preview.Simulation.ResourceNodes[0];
                GUILayout.Label($"Resource tree — logs remaining: {node.Available}/{node.Capacity}");
                GUILayout.Label($"Axe required — carried: {hero.Items.Quantity(ItemCatalog.AxeId)}");
                if (GUILayout.Button("Chop: collect 1 log")) HarvestTree();
            }
            else
            {
                GUILayout.Label("Woodworking bench");
                GUILayout.Label($"Logs: {hero.Items.Quantity(ItemCatalog.LogId)} | Planks: {hero.Items.Quantity(ItemCatalog.PlankId)} | Firewood: {hero.Items.Quantity(ItemCatalog.FirewoodId)} | Sticks: {hero.Items.Quantity(ItemCatalog.StickId)}");
                if (GUILayout.Button("1 Log → 4 Planks")) Craft(RecipeCatalog.PlanksId);
                if (GUILayout.Button("1 Log → 4 Firewood")) Craft(RecipeCatalog.FirewoodId);
                if (GUILayout.Button("1 Log → 6 Sticks")) Craft(RecipeCatalog.SticksId);
            }
            GUILayout.Label(actionMessage);
            if (GUILayout.Button("Close (E / Escape)")) Close();
            GUILayout.EndArea();
        }
    }
}
