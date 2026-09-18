using System;
using System.Collections.Generic;
using LivingEconomy.Simulation;
using UnityEngine;

namespace LivingEconomy.Presentation
{
    internal sealed class NpcMovementView
    {
        private readonly SimulationPreview preview;
        private readonly Action closeInteraction;
        private readonly Dictionary<string, Renderer> people;
        private readonly Dictionary<string, Vector3> homes, workplaces;
        private readonly Dictionary<string, Queue<Vector3>> routes = new Dictionary<string, Queue<Vector3>>();
        private readonly Dictionary<string, float> travelTimes = new Dictionary<string, float>();
        private readonly Dictionary<string, string> routeTargets = new Dictionary<string, string>();
        private readonly HashSet<string> blockedTargets = new HashSet<string>();
        private const float RouteTimeout = 30;
        private float stopTime;
        private int phase;
        public bool Walking { get; private set; }
        public bool Paused { get; set; }
        public bool HasBlockedTargets => blockedTargets.Count > 0;
        public void ClearRouteTests() => blockedTargets.Clear();
        public void SetBlocked(string id, bool blocked) { if (blocked) blockedTargets.Add(id); else blockedTargets.Remove(id); }
        public string Activity => !Walking ? "Day complete" : phase == 0 ? "Going to work" : phase == 1 ? "At work" : phase == 2 ? "Going to bakery" : phase == 3 ? "At bakery" : "Going home";
        public NpcMovementView(SimulationPreview preview, Dictionary<string, Renderer> people,
            Dictionary<string, Vector3> homes, Dictionary<string, Vector3> workplaces, Action closeInteraction)
        {
            this.preview = preview; this.people = people; this.homes = homes;
            this.workplaces = workplaces; this.closeInteraction = closeInteraction;
            foreach (var id in people.Keys) routes.Add(id, new Queue<Vector3>());
        }

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
        public void ResetWalking()
        {
            closeInteraction();
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
            int farmIndex = 0, bakeryIndex = 0;
            foreach (var person in people)
            {
                string employer = preview.Simulation.EmployerOf(person.Key);
                if (employer == null)
                {
                    preview.Simulation.ArriveAtWork(person.Key);
                    continue;
                }
                bool farm = employer == preview.Simulation.Farm.Id;
                int index = farm ? farmIndex++ : bakeryIndex++;
                workplaces[person.Key] = WorkSlot(farm, index);
                SetRoute(person.Key, workplaces[person.Key], employer);
            }
        }
        internal static Vector3 WorkSlot(bool farm, int index)
            => new Vector3((farm ? -7 : 7) + (index % 5 - 2) * 1.15f, 0.8f, 3.25f - index / 5 * 0.75f);
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
        public void AdvanceWalking()
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
    }
}
