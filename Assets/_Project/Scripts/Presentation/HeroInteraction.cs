using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingEconomy.Presentation
{
    // Read-only first interaction layer; transactions will use core commands later.
    [DisallowMultipleComponent]
    public sealed class HeroInteraction : MonoBehaviour
    {
        private SettlementView view;
        private SimulationPreview preview;
        private Camera camera;
        private string openedId;
        private Vector2 scroll;
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
            if (!IsOpen)
            {
                var id = NearestId(); var account = Account(id);
                if (account == null) return;
                var rect = new Rect(x, Mathf.Max(115, Screen.height - 70), width, 55);
                if (GUI.Button(rect, "E: " + (id == "farm" || id == "bakery" ? "Inspect " : "Talk to ") + account.Name)) TryOpen(id);
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
                GUILayout.Label($"Owner: {Account(business.Owner)?.Name ?? business.Owner}");
                GUILayout.Label($"Employees: {preview.Simulation.EmployeeCount(openedId)}/{business.Capacity} | wage: {business.Wage} coins/day");
                GUILayout.Label($"Business capital: {selected.Money} | grain: {selected.Stock(Good.Grain)} | bread: {selected.Stock(Good.Bread)}");
                GUILayout.Label(openedId == "farm" ? "The farm produces grain and sells it to the bakery." : "The bakery buys grain, bakes bread and sells it to residents.");
                GUILayout.Label("Hero employment and purchases will be added in the next steps.");
            }
            else
            {
                string job = preview.Simulation.EmployerOf(openedId);
                GUILayout.Label($"Job: {job ?? "unemployed"} | hunger: {selected.Hunger}/100");
                GUILayout.Label($"Coins: {selected.Money} | bread carried: {selected.Stock(Good.Bread)}");
                GUILayout.Label("Current decision: " + preview.Simulation.DecisionOf(openedId));
                GUILayout.Label("Information only: branching conversations and quests are not implemented yet.");
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close (E / Escape)")) Close();
            GUILayout.EndArea();
        }
    }
}
