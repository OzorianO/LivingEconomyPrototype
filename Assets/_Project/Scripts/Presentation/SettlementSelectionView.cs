using System.Collections.Generic;
using LivingEconomy.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingEconomy.Presentation
{
    internal sealed class SettlementSelectionView
    {
        private readonly SimulationPreview preview;
        private readonly Dictionary<GameObject,string> targets;
        private readonly Dictionary<string,Renderer> people;
        private readonly GameObject marker;
        private string selectedId, jobMessage;
        public SettlementSelectionView(SimulationPreview preview, Dictionary<GameObject,string> targets,
            Dictionary<string,Renderer> people, GameObject marker)
        { this.preview=preview; this.targets=targets; this.people=people; this.marker=marker; }
        public void Update(Camera mapCamera, IslandPlayer player)
        {
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
            if (selectedId == null) { GUILayout.Label("Selection: none. Blue = farm; yellow = bakery; grey = unemployed; green = owner; red = hungry."); return; }
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

        private void ChangeJob(string employer)
        {
            bool assigned = preview.Simulation.AssignJob(selectedId, employer, out var reason);
            jobMessage = assigned ? "Job updated." : reason;
            if (assigned) preview.RememberCompletedDay();
        }

    }
}
