using System;
using System.IO;
using System.Reflection;
using LivingEconomy.Presentation;
using LivingEconomy.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs only in the disposable verification project, never deployed to the game.
[InitializeOnLoad]
public static class ReloadLifecycleVerification
{
    private const string Prefix = "LivingEconomy.ReloadVerification.";
    static ReloadLifecycleVerification()
    {
        SessionState.SetInt(Prefix + "Domain", SessionState.GetInt(Prefix + "Domain", 0) + 1);
        EditorApplication.update += Update;
        Application.logMessageReceived += OnLog;
    }
    public static void Run()
    {
        SessionState.SetBool(Prefix + "Running", true);
        SessionState.SetInt(Prefix + "Step", 0);
        SessionState.SetInt(Prefix + "Checks", 0);
        SessionState.SetInt(Prefix + "SearchStartupErrors", 0);
        SessionState.SetString(Prefix + "Deadline", DateTime.UtcNow.AddMinutes(3).Ticks.ToString());
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainSimulation.unity");
        EditorApplication.EnterPlaymode();
    }
    private static void Check(bool value, string reason)
    {
        if (!value) throw new Exception(reason);
        int count = SessionState.GetInt(Prefix + "Checks", 0) + 1;
        SessionState.SetInt(Prefix + "Checks", count);
        Debug.Log("RELOAD CHECK: " + reason);
    }
    private static void Call(SimulationPreview preview, string method, params object[] args)
        => typeof(SimulationPreview).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(preview, args);
    private static SimulationPreview Preview()
    {
        var previews = UnityEngine.Object.FindObjectsByType<SimulationPreview>();
        Check(previews.Length == 1, "one preview component");
        return previews[0];
    }
    private static void CheckWorld(SimulationPreview preview)
    {
        Check(preview.GetComponents<SettlementView>().Length == 1, "one settlement component");
        int roots = 0, people = 0;
        foreach (Transform child in preview.transform)
            if (child.gameObject.activeSelf && child.name == "GeneratedSettlement")
            {
                roots++;
                foreach (var capsule in child.GetComponentsInChildren<CapsuleCollider>())
                    if (capsule.enabled && capsule.gameObject.name != "Resource tree trunk") people++;
            }
        Check(roots == 1 && people == 20 - SettlementReport.Capture(preview.Simulation).Dead, "one generated world with expected living NPC colliders");
        var view = preview.GetComponent<SettlementView>();
        var camera = (Camera)typeof(SettlementView).GetField("mapCamera", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        Check(camera != null && !camera.orthographic && Mathf.Abs(camera.fieldOfView - 50) < 0.01f, "perspective camera is active");
        var zoom = typeof(SettlementView).GetMethod("ZoomDistance", BindingFlags.NonPublic | BindingFlags.Static);
        float normalizedZoom = (float)zoom.Invoke(null, new object[] { 70f, 1f });
        float rawZoom = (float)zoom.Invoke(null, new object[] { 70f, 120f });
        Check(Mathf.Abs(normalizedZoom - rawZoom) < 0.001f && normalizedZoom < 63, "raw and normalized wheel input have matching responsive zoom");
        typeof(SettlementView).GetField("cameraDistance", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, 2f);
        typeof(SettlementView).GetField("cameraPitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, 100f);
        typeof(SettlementView).GetMethod("ApplyCameraPose", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
        Check((float)typeof(SettlementView).GetField("cameraDistance", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view) == 12f
            && (float)typeof(SettlementView).GetField("cameraPitch", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view) == 75f, "camera zoom and tilt are bounded");
        view.ResetCamera();
        Check(camera.transform.position.y > 40 && camera.transform.position.y < 55, "reset restores island overview");
        Transform terrain = null, activeRoot = null;
        foreach (Transform child in preview.transform)
            if (child.gameObject.activeSelf && child.name == "GeneratedSettlement")
            {
                activeRoot = child;
                terrain = child.Find("Medieval Island/Imported island terrain") ?? child.Find("Island terrain");
            }
        Check(terrain != null && terrain.GetComponent<Collider>() != null, "island terrain has a ground collider");
        var importedTerrain = terrain.GetComponent<Terrain>();
        if (importedTerrain != null)
        {
            Check(importedTerrain.terrainData.heightmapResolution == 1025
                && Mathf.Abs(importedTerrain.terrainData.size.x - 160) < 0.01f, "optimized Asset Store terrain is active");
            float plateau = importedTerrain.transform.position.y + importedTerrain.terrainData.GetInterpolatedHeight(0.5f, 0.5f);
            Check(Mathf.Abs(plateau) < 0.2f, "settlement plateau is level with existing routes");
        }
        else
        {
            var mesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh.vertexCount == 768 && mesh.subMeshCount == 2, "fallback island has detailed grass and beach mesh");
            Check(mesh.bounds.max.y > 2.5f && mesh.normals[96].y > 0.99f, "fallback island ridge and ground are valid");
        }
        Check(activeRoot.Find("Bakery chimney") != null && activeRoot.Find("Well base") != null, "village has bakery chimney and well");
        Check(Mathf.Abs(activeRoot.Find("House 1 roof 1").eulerAngles.x - 30) < 0.01f, "houses have pitched roofs");
        Check(activeRoot.Find("Resource tree trunk") != null && activeRoot.Find("Workbench top") != null, "resource tree and woodworking bench are represented");
        Physics.SyncTransforms();
        var heroes = activeRoot.GetComponentsInChildren<IslandPlayer>();
        Check(heroes.Length == 1 && view.Player == heroes[0], "one controllable island hero");
        var hero = heroes[0];
        Check(hero.IsDryGround(Vector3.zero) && !hero.IsDryGround(new Vector3(100, 0, 100)), "hero cannot enter open water");
        var axe = activeRoot.Find("Island hero/Hero axe");
        Check(axe != null && axe.gameObject.activeSelf == (preview.Simulation.Hero.Items.Quantity(ItemCatalog.AxeId) > 0),
            "Asset Store axe visibility follows hero inventory");
        var beforeHero = SimulationSave.ToXml(preview.Simulation);
        hero.SetExploring(true); hero.TeleportToSpawn();
        for (int i = 0; i < 8; i++) hero.MoveExplorer(Vector3.zero, false, false, 0.02f);
        var start = hero.transform.position;
        for (int i = 0; i < 25; i++) hero.MoveExplorer(Vector3.right, false, false, 0.02f);
        Check(hero.transform.position.x > start.x + 1.5f && hero.transform.position.y < 0.2f, "hero walks on island ground");
        hero.MoveExplorer(Vector3.zero, false, true, 0.02f);
        float highest = hero.transform.position.y;
        for (int i = 0; i < 55; i++) { hero.MoveExplorer(Vector3.zero, false, false, 0.02f); highest = Mathf.Max(highest, hero.transform.position.y); }
        Check(highest > 0.8f && hero.transform.position.y < 0.2f, "hero jumps and lands");
        var cc = hero.GetComponent<CharacterController>(); cc.enabled = false;
        hero.transform.position = new Vector3(7, 0.08f, 2.8f); cc.enabled = true; Physics.SyncTransforms();
        for (int i = 0; i < 50; i++) hero.MoveExplorer(Vector3.forward, false, false, 0.02f);
        Check(hero.transform.position.z < 3.6f, "building walls block hero movement");
        hero.TeleportToSpawn(); hero.SetExploring(false);
        Check(!hero.Exploring && camera.transform.position.y > 40, "overview mode restores settlement camera");
        Check(SimulationSave.ToXml(preview.Simulation) == beforeHero, "hero movement does not mutate the economy");
        hero.SetExploring(true);
        var interaction = view.Interaction;
        Check(interaction != null && !interaction.IsOpen && !interaction.TryOpen("missing") && !interaction.TryOpen("farm"), "interaction rejects unknown and distant targets");
        cc.enabled = false; hero.transform.position = new Vector3(7, 0.08f, 2.8f); cc.enabled = true; Physics.SyncTransforms();
        Check(interaction.TryOpen("bakery") && interaction.OpenedId == "bakery", "hero can inspect nearby bakery entrance");
        float lockedX = hero.transform.position.x;
        hero.MoveExplorer(Vector3.right, true, true, 0.05f);
        Check(Mathf.Abs(hero.transform.position.x - lockedX) < 0.01f, "open interaction blocks hero movement");
        interaction.Close();
        cc.enabled = false; hero.transform.position = new Vector3(7, 0.08f, 6.4f); cc.enabled = true; Physics.SyncTransforms();
        Check(!interaction.CanReach("bakery"), "building walls prevent interaction through them");
        cc.enabled = false; hero.transform.position = new Vector3(-7, 0.08f, 2.8f); cc.enabled = true; Physics.SyncTransforms();
        Check(interaction.TryOpen("farm"), "hero can inspect nearby farm entrance");
        hero.SetExploring(false);
        Check(!interaction.IsOpen, "overview mode closes interaction");
        hero.SetExploring(true);
        view.TryPersonPosition("npc-00", out var npcPoint);
        cc.enabled = false; hero.transform.position = npcPoint + new Vector3(0, -0.72f, 1.2f); cc.enabled = true; Physics.SyncTransforms();
        Check(interaction.TryOpen("npc-00"), "hero can open nearby resident information");
        hero.TeleportToSpawn();
        typeof(HeroInteraction).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(interaction, null);
        Check(!interaction.IsOpen, "out-of-range interaction closes");
        Check(SimulationSave.ToXml(preview.Simulation) == beforeHero, "read-only interactions preserve all economy state");
        var groundCollider = terrain.GetComponent<Collider>();
        foreach (var point in new[] { new Vector3(-10.5f, 0, 11), new Vector3(10.8f, 0, -8.5f), Vector3.zero })
        {
            Check(groundCollider.Raycast(new Ray(point + Vector3.up * 10, Vector3.down), out var hit, 20)
                && Mathf.Abs(hit.point.y) < 0.05f, "field, houses and roads remain on flat island ground");
        }
    }
    private static void CheckPresentation(SimulationPreview preview)
    {
        var view = preview.GetComponent<SettlementView>();
        string original = SimulationSave.ToXml(preview.Simulation);
        Check(preview.Simulation.AssignJob("npc-11", null, out _), "presentation test can leave bakery job");
        typeof(SettlementView).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
        var people = (System.Collections.Generic.Dictionary<string, Renderer>)typeof(SettlementView)
            .GetField("people", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        var unemployed = people["npc-11"].sharedMaterial;
        Check(unemployed != people["npc-12"].sharedMaterial, "unemployed resident is not coloured as a baker");
        Check(people["npc-00"].sharedMaterial != people["npc-01"].sharedMaterial
            && people["npc-00"].sharedMaterial != unemployed, "owner has a distinct colour");
        Call(preview, "ResetScenario", SimulationSave.FromXml(original));
        Call(preview, "AdvanceDay");
        var slots = (System.Collections.Generic.Dictionary<string, Vector3>)typeof(SettlementView)
            .GetField("workplaces", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        var counters = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var npc in preview.Simulation.Economy.Residents)
        {
            string employer = preview.Simulation.EmployerOf(npc.Id);
            if (employer == null) continue;
            counters.TryGetValue(employer, out int index); counters[employer] = index + 1;
            float centre = employer == "farm" ? -7 : 7;
            var expected = new Vector3(centre + (index % 5 - 2) * 1.15f, 0.8f, 3.25f - index / 5 * 0.75f);
            Check(Vector3.Distance(slots[npc.Id], expected) < 0.001f, "work slot is indexed within its own business: " + npc.Id);
            Check(!Physics.CheckSphere(slots[npc.Id], 0.32f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore), "work slot does not intersect building colliders: " + npc.Id);
        }
        Call(preview, "ResetScenario", SimulationSave.FromXml(original));
    }
    private static void CheckHeroEconomy(SimulationPreview preview)
    {
        string original = SimulationSave.ToXml(preview.Simulation);
        Call(preview, "ResetScenario", DailySimulation.HeroDemo());
        var view = preview.GetComponent<SettlementView>();
        var player = view.Player; var interaction = view.Interaction;
        player.SetExploring(true); player.TeleportToSpawn();
        string before = SimulationSave.ToXml(preview.Simulation);
        Check(!interaction.BuyBread() && SimulationSave.ToXml(preview.Simulation) == before, "hero cannot buy remotely or without open bakery");
        var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
        player.transform.position = new Vector3(7, 0.08f, 2.8f); cc.enabled = true; Physics.SyncTransforms();
        Check(interaction.TryOpen("bakery") && interaction.BuyBread(), "reachable bakery interaction executes purchase");
        var hero = preview.Simulation.Hero;
        Check(hero.Money == 6 && hero.Stock(Good.Bread) == 1 && preview.Simulation.Bakery.Stock(Good.Bread) == 1, "Unity purchase delivers stock and charges exactly once");
        Check(interaction.ConsumeBread() && hero.Hunger == 25 && hero.Thirst == 20, "Unity consume uses shared needs and inventory");
        Check(!interaction.ConsumeBread() && hero.Hunger == 25, "Unity repeated consume has no duplicate effect");
        Check(interaction.BuyBread() && hero.Money == 0 && hero.Stock(Good.Bread) == 1, "Unity second purchase carries saved bread");
        before = SimulationSave.ToXml(preview.Simulation);
        Call(preview, "ResetScenario", SimulationSave.FromXml(before));
        Check(SimulationSave.ToXml(preview.Simulation) == before && preview.Simulation.Hero.Stock(Good.Bread) == 1, "Unity Load restores hero wallet inventory and needs");
        Check(SettlementReport.Capture(preview.Simulation).MoneyConserved, "Unity report includes hero funds without new money");
        Call(preview, "ResetScenario", SimulationSave.FromXml(original));
        player.SetExploring(false);
    }
    private static void CheckHeroPose(SimulationPreview preview)
    {
        string original = SimulationSave.ToXml(preview.Simulation);
        var view = preview.GetComponent<SettlementView>(); var player = view.Player;
        var camera = (Camera)typeof(SettlementView).GetField("mapCamera", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        var pose = new SavedHeroPose { X = 2, Y = 0.08f, Z = -2, FacingYaw = 90, CameraYaw = 135,
            CameraPitch = -20, CameraDistance = 5, FirstPerson = true };
        Check(player.RestorePose(pose), "valid dry pose restored without reset");
        player.SetExploring(true);
        typeof(IslandPlayer).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(player, null);
        Check(player.FirstPerson && Vector3.Distance(camera.transform.position, player.transform.position + Vector3.up * 1.6f) < 0.001f,
            "first-person camera is at eye height");
        Check(Mathf.Abs(camera.nearClipPlane - 0.05f) < 0.001f, "first-person uses a short near clip");
        bool hidden = true; foreach (var renderer in player.GetComponentsInChildren<Renderer>()) hidden &= !renderer.enabled;
        Check(hidden, "first-person hides own primitive body");
        player.MoveExplorer(Vector3.right, false, false, 0.02f);
        Check(player.transform.position.x > 2.05f, "first-person still uses collision controller movement");
        preview.RememberCompletedDay(); string saved = SimulationSave.ToXml(preview.Simulation);
        var expected = player.CapturePose();
        player.TeleportToSpawn();
        Call(preview, "ResetScenario", SimulationSave.FromXml(saved));
        Check(Mathf.Abs(player.transform.position.x - expected.X) < 0.001f && player.FirstPerson
            && Mathf.Abs(player.CapturePose().CameraYaw - 135) < 0.001f, "Load restores moved position and camera mode");
        Check(SimulationSave.ToXml(preview.Simulation) == saved, "pose Load roundtrip has stable precision");
        player.SetExploring(false);
        bool visible = true; foreach (var renderer in player.GetComponentsInChildren<Renderer>()) visible &= renderer.enabled;
        Check(visible && Mathf.Abs(camera.nearClipPlane - 0.25f) < 0.001f, "overview restores body and normal near clip");
        player.SetExploring(true); player.SetFirstPerson(false);
        Check(!player.FirstPerson && player.CapturePose().CameraPitch >= 8, "third-person mode clamps its pitch");
        pose = player.CapturePose(); pose.X = 50;
        Check(!player.RestorePose(pose) && !player.LastPoseRestoreSafe && player.transform.position.x == 0,
            "saved pose over water falls back safely");
        pose.X = 7; pose.Y = 0.08f; pose.Z = 5;
        Check(!player.RestorePose(pose), "saved pose in building wall falls back safely");
        long coins = preview.Simulation.Hero.Money;
        Call(preview, "ResetScenario", SimulationSave.FromXml(original));
        Check(preview.Simulation.Hero.Money == coins, "pose changes never create or lose hero money");
        player.SetExploring(false);
    }
    private static void CheckDeathLoot(SimulationPreview preview)
    {
        string original = SimulationSave.ToXml(preview.Simulation);
        var demo = DailySimulation.HeroDemo();
        Check(demo.ExecuteAction("npc-01", AgentAction.BuyBread).Success, "corpse fixture purchases personal bread");
        Check(demo.KillAgent("npc-01", new SavedPoint { X = -1, Y = 0.8f, Z = -2 }).Success, "corpse fixture performs death transition");
        Call(preview, "ResetScenario", demo);
        var view = preview.GetComponent<SettlementView>(); var player = view.Player; var interaction = view.Interaction;
        player.SetExploring(true); player.TeleportToSpawn(); Physics.SyncTransforms();
        Check(view.TryPersonPosition("npc-01", out var bodyPoint) && Mathf.Abs(bodyPoint.x + 1) < 0.001f, "corpse view restored at recorded death position");
        Check(interaction.TryOpen("npc-01") && interaction.Loot(Good.Bread, false), "nearby corpse interaction transfers actual bread");
        long coins = preview.Simulation.Agent("npc-01").Money; long heroCoins = preview.Simulation.Hero.Money;
        Check(interaction.Loot(null, true) && preview.Simulation.Hero.Money == heroCoins + coins, "nearby corpse interaction transfers personal wallet");
        Check(!interaction.Loot(null, true) && !interaction.Loot(Good.Bread, false), "corpse interaction cannot refill empty loot");
        string saved = SimulationSave.ToXml(preview.Simulation);
        Call(preview, "ResetScenario", SimulationSave.FromXml(saved));
        Check(preview.Simulation.Agent("npc-01").IsDead && preview.Simulation.Agent("npc-01").Money == 0
            && preview.Simulation.Hero.Stock(Good.Bread) == 1, "Unity Load preserves death and emptied corpse");
        var people = (System.Collections.Generic.Dictionary<string, Renderer>)typeof(SettlementView).GetField("people", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        Check(!people["npc-01"].GetComponent<CapsuleCollider>().enabled, "corpse collider does not block walking agents");
        var corpsePosition = people["npc-01"].transform.position;
        preview.Simulation.Step(); view.ResetWalking();
        Check(Vector3.Distance(people["npc-01"].transform.position, corpsePosition) < 0.001f
            && preview.Simulation.Agent("npc-01").Money == 0, "day advance neither moves corpse nor pays it wages");
        Check(SettlementReport.Capture(preview.Simulation).MoneyConserved, "Unity corpse wallet report conserves all coins");
        Check(preview.Simulation.KillAgent("hero", new SavedPoint()).Success, "hero supports same death transition in Unity");
        typeof(IslandPlayer).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(player, null);
        var start = player.transform.position; player.SetExploring(true); player.MoveExplorer(Vector3.right, true, true, 0.05f);
        Check(!player.Exploring && player.transform.position == start, "dead hero cannot re-enable movement");
        Call(preview, "ResetScenario", SimulationSave.FromXml(original));
        player.SetExploring(false);
    }
    private static void Update()
    {
        if (!SessionState.GetBool(Prefix + "Running", false)) return;
        try
        {
            if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Prefix + "Deadline", "0"))) throw new Exception("Lifecycle verification timed out");
            int step = SessionState.GetInt(Prefix + "Step", 0);
            if (step == 0)
            {
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var preview = Preview();
                Check(preview.Simulation.Hero.Items.Quantity(ItemCatalog.AxeId) == 1, "fresh session grants one starting axe");
                CheckWorld(preview);
                Call(preview, "ResetScenario", DailySimulation.HeroDemo());
                Check(preview.Simulation.ExecuteAction("hero", AgentAction.BuyBread).Success, "prepare carried hero bread before actual domain reload");
                Check(preview.Simulation.KillAgent("npc-02", new SavedPoint { X = -1, Y = 0.8f, Z = -2 }).Success, "prepare corpse before actual domain reload");
                for (int i = 0; i < 5; i++) preview.Simulation.Step();
                preview.Simulation.AssignJob("npc-01", null, out _);
                preview.RememberCompletedDay();
                SessionState.SetString(Prefix + "Expected", SimulationSave.ToXml(preview.Simulation));
                Call(preview, "AdvanceDay");
                foreach (var npc in preview.Simulation.Economy.Residents) preview.Simulation.ArriveAtWork(npc.Id);
                preview.Simulation.FinishWork();
                Check(preview.Simulation.LastPaid == 17, "partial day changed living wages before reload");
                SessionState.SetInt(Prefix + "Step", 1);
                SessionState.SetInt(Prefix + "RequestedDomain", SessionState.GetInt(Prefix + "Domain", 0));
                EditorUtility.RequestScriptReload();
                return;
            }
            if (step == 1)
            {
                // Wait until a new domain has actually initialized, not just another old frame.
                if (EditorApplication.isCompiling || SessionState.GetInt(Prefix + "Domain", 0) <= SessionState.GetInt(Prefix + "RequestedDomain", 0)) return;
                var preview = Preview();
                if (preview.Simulation == null || preview.Simulation.DayInProgress) return;
                Check(preview.Simulation.Economy.Tick == 5, "actual domain reload rolled back interrupted day");
                Check(SimulationSave.ToXml(preview.Simulation) == SessionState.GetString(Prefix + "Expected", ""), "actual reload preserves exact completed economy and jobs");
                Check(preview.Simulation.Agent("npc-02").IsDead, "actual domain reload preserves corpse state");
                CheckWorld(preview);
                var view = preview.GetComponent<SettlementView>();
                view.SetRouteBlocked("farm", true);
                Call(preview, "AdvanceDay");
                SessionState.SetInt(Prefix + "Step", 2); return;
            }
            if (step == 2)
            {
                var previews = UnityEngine.Object.FindObjectsByType<SimulationPreview>();
                if (previews.Length != 1 || previews[0].Simulation.DayInProgress) return;
                var preview = previews[0];
                Check(preview.Simulation.Economy.Tick == 6 && preview.Simulation.LastUnreachable > 0, "animated day completes despite blocked farm");
                Check(preview.Simulation.Economy.TotalMoney() == preview.Simulation.Economy.InitialMoney, "animated recovery conserves money");
                SessionState.SetString(Prefix + "Completed", SimulationSave.ToXml(preview.Simulation));
                SessionState.SetInt(Prefix + "RequestedDomain", SessionState.GetInt(Prefix + "Domain", 0));
                SessionState.SetInt(Prefix + "Step", 3); EditorUtility.RequestScriptReload(); return;
            }
            if (step == 3)
            {
                if (EditorApplication.isCompiling || SessionState.GetInt(Prefix + "Domain", 0) <= SessionState.GetInt(Prefix + "RequestedDomain", 0)) return;
                var preview = Preview();
                if (preview.Simulation == null) return;
                Check(SimulationSave.ToXml(preview.Simulation) == SessionState.GetString(Prefix + "Completed", ""), "actual completed-day reload preserves exact state");
                Check(preview.Simulation.Agent("npc-02").IsDead, "completed-day reload preserves corpse state");
                CheckWorld(preview);
                SessionState.SetInt(Prefix + "Step", 4); EditorApplication.ExitPlaymode(); return;
            }
            if (step == 4)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetInt(Prefix + "Step", 5); EditorApplication.EnterPlaymode(); return;
            }
            if (step == 5)
            {
                if (!EditorApplication.isPlaying) return;
                var preview = Preview(); CheckWorld(preview); CheckPresentation(preview); CheckHeroEconomy(preview); CheckHeroPose(preview); CheckDeathLoot(preview);
                Check(preview.Simulation.Economy.Tick == 0, "second Play starts fresh without duplicate UI");
                Call(preview, "ResetScenario", SimulationSave.FromXml(SessionState.GetString(Prefix + "Expected", "")));
                Check(preview.Simulation.Economy.Tick == 5, "load after repeated Play restores snapshot");
                Finish(true, "Actual domain reload, rollback, blocked-route animation, repeated Play and Load passed.");
            }
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    private static void OnLog(string message, string trace, LogType type)
    {
        if (trace.Contains("UnityEditor.Search.") && SessionState.GetBool(Prefix + "Running", false))
        {
            SessionState.SetInt(Prefix + "SearchStartupErrors", SessionState.GetInt(Prefix + "SearchStartupErrors", 0) + 1);
            return; // Known headless editor search-index startup failure, unrelated to game lifecycle.
        }
        if (SessionState.GetBool(Prefix + "Running", false) && (type == LogType.Exception || type == LogType.Error))
            Finish(false, message + "\n" + trace);
    }
    private static void Finish(bool success, string reason)
    {
        SessionState.SetBool(Prefix + "Running", false);
        string result = (success ? "PASS" : "FAIL") + ": " + SessionState.GetInt(Prefix + "Checks", 0) + " lifecycle checks. " + reason
            + " Search-index startup errors excluded: " + SessionState.GetInt(Prefix + "SearchStartupErrors", 0) + ".";
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "lifecycle-result.txt"), result);
        Debug.Log(result);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
