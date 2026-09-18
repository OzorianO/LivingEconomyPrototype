using System;
using System.Collections.Generic;
using LivingEconomy.Simulation;

static class InventoryChecks
{
    public static void Run(Action<bool, string> check)
    {
        DeathChecks(check);
        HeroChecks(check);
        var catalog = ItemCatalog.Prototype;
        check(catalog.TryGet("grain", out var grain) && grain.MaxStack == 100, "stable grain catalog ID");
        check(catalog.TryGet("bread", out var bread) && bread.MaxStack == 20, "stable bread catalog ID");
        check(!catalog.TryGet(null, out _) && !catalog.TryGet("Bread", out _), "catalog lookup ordinal and null safe");
        Throws(() => new ItemDefinition("", "Bread", 20), check, "invalid item ID");
        Throws(() => new ItemDefinition("bread", "Bread", 0), check, "invalid stack limit");
        Throws(() => new ItemCatalog(new[] { bread, bread }), check, "duplicate catalog ID");
        Throws(() => new ItemInventory(catalog, -1), check, "negative capacity");
        Throws(() => new ItemInventory(catalog, 10, -1), check, "negative slot capacity");
        var source = new ItemInventory(catalog);
        var bag = new ItemInventory(catalog, capacity: 50, slotCapacity: 2);
        check(source.TryAdd("bread", 100, out _) && source.UsedSlots == 5, "quantity splits into stacks");
        var view = bag.ReadOnly;
        check(!(view is ItemInventory), "read-only view does not expose mutable store");
        check(ItemInventory.TryTransfer(source, bag, "bread", 20, true, out _), "transfer to bounded container");
        check(view.Quantity("bread") == 20 && view.UsedSlots == 1 && source.Quantity("bread") == 80, "live read-only view");
        check(ItemInventory.TryTransfer(source, bag, "bread", 1, true, out _) && bag.UsedSlots == 2, "second stack allocated");
        CheckRejected(source, bag, "bread", 20, true, check, "slot overflow atomic");
        CheckRejected(source, bag, "bread", 1, false, check, "denied access atomic");
        CheckRejected(source, bag, "bread", 0, true, check, "zero transfer atomic");
        CheckRejected(source, bag, "bread", -1, true, check, "negative transfer atomic");
        CheckRejected(source, bag, "bread", int.MaxValue, true, check, "insufficient stock atomic");
        CheckRejected(source, bag, "unknown", 1, true, check, "unknown item atomic");
        CheckRejected(source, bag, null, 1, true, check, "null item atomic");
        CheckRejected(source, source, "bread", 1, true, check, "self transfer atomic");
        CheckRejected(source, new ItemInventory(new ItemCatalog(new[] { bread })), "bread", 1, true, check, "catalog mismatch atomic");
        check(!ItemInventory.TryTransfer(null, bag, "bread", 1, true, out _), "missing source rejected");
        var tiny = new ItemInventory(catalog, 1);
        check(tiny.TryAdd("grain", 1, out _), "quantity capacity filled");
        CheckRejected(source, tiny, "bread", 1, true, check, "quantity capacity atomic");
        var empty = new ItemInventory(catalog, 0, 0);
        CheckRejected(source, empty, "bread", 1, true, check, "zero capacity atomic");
        string before = State(bag);
        check(!bag.TryAdd("bread", int.MaxValue, out _) && State(bag) == before, "addition overflow atomic");
        check(!bag.TryRemove("bread", 22, out _) && State(bag) == before, "removal shortage atomic");
        check(!bag.TryRemove("bread", -1, out _) && State(bag) == before, "negative removal rejected");
        check(bag.TryRemove("bread", 21, out _) && bag.UsedSlots == 0 && bag.TotalQuantity == 0 && bag.Quantities.Count == 0, "empty stacks released");
        check(bag.TryAdd("grain", 50, out _) && bag.UsedSlots == 1, "freed slots reused by other item");
        Throws(() => ((IDictionary<string, int>)bag.Quantities).Add("bread", 1), check, "dictionary is read-only");
        var full = new ItemInventory(catalog);
        check(full.TryAdd("bread", int.MaxValue, out _), "legacy max quantity fits");
        CheckRejected(source, full, "bread", 1, true, check, "max quantity transfer overflow atomic");

        var economy = new Economy(new[] {
            new Resident("npc", "NPC", Profession.None, 100, 10, 0),
            new Resident("hero-test", "Hero", Profession.None, 0, 0, 0) });
        var business = economy.AddBusiness("warehouse", "Warehouse");
        var npc = economy.Residents[0];
        check(economy.TransferGoods("npc", "warehouse", Good.Grain, 4, true).Success, "NPC to business shared transfer");
        check(economy.TransferGoods("warehouse", "hero-test", Good.Grain, 2, true).Success, "business to agent shared transfer");
        check(npc.Inventory[Good.Grain] == 6 && npc.Items.Quantity("grain") == 6 && business.Stock(Good.Grain) == 2, "legacy and new views have one authority");
        check(economy.TotalMoney() == 100, "goods transfer does not create money");
        check(!economy.TransferGoods("warehouse", "npc", Good.Grain, 1, false).Success && business.Stock(Good.Grain) == 2, "economy denied transfer logged without mutation");
        var container = new ItemInventory(catalog, 5, 1);
        check(ItemInventory.TryTransfer(business.Store, container, "grain", 2, true, out _), "business to container same API");
        check(ItemInventory.TryTransfer(container, npc.Store, "grain", 2, true, out _) && npc.Stock(Good.Grain) == 8, "container to NPC same API");
        check(economy.Produce("warehouse", Good.Grain, 2).Success && business.Items.Quantity("grain") == 2, "production updates shared inventory");
        check(economy.Produce("warehouse", Good.Bread, 2, Good.Grain).Success && business.Items.Quantity("grain") == 0, "recipe consumes shared inventory");
        check(economy.Buy("npc", "warehouse", Good.Bread, 1, 5).Success && npc.Items.Quantity("bread") == 1, "purchase updates shared inventory");
        check(economy.Eat(npc) && npc.Items.Quantity("bread") == 0, "meal consumes shared inventory");

        var random = new Random(42);
        var left = new ItemInventory(catalog); var right = new ItemInventory(catalog, 250, 4);
        left.TryAdd("bread", 100, out _); left.TryAdd("grain", 200, out _);
        for (int i = 0; i < 1000; i++)
        {
            var from = i % 2 == 0 ? left : right; var to = i % 2 == 0 ? right : left;
            string id = i % 3 == 0 ? "grain" : "bread";
            string old = State(left) + State(right);
            bool ok = ItemInventory.TryTransfer(from, to, id, random.Next(-2, 31), i % 7 != 0, out _);
            check(ok || old == State(left) + State(right), "random refusal atomic");
            check(left.Quantity("bread") + right.Quantity("bread") == 100 && left.Quantity("grain") + right.Quantity("grain") == 200, "random item conservation");
            check(right.TotalQuantity <= right.Capacity && right.UsedSlots <= right.SlotCapacity, "random capacity bounds");
        }
    }

    static string State(ItemInventory inventory) => inventory.Quantity("grain") + ":" + inventory.Quantity("bread") + ":" + inventory.TotalQuantity + ":" + inventory.UsedSlots;
    static void HeroChecks(Action<bool, string> check)
    {
        PoseChecks(check);
        var simulation = new DailySimulation();
        var hero = simulation.EnableHero();
        check(ReferenceEquals(hero, simulation.EnableHero()) && hero.Money == 0 && hero.Items.TotalQuantity == 0, "hero activation idempotent and empty");
        check(simulation.Economy.Residents.Count == 20 && simulation.Economy.TotalMoney() == 1642, "hero not an automated NPC or new money");
        check(!simulation.ExecuteAction("hero", AgentAction.BuyBread).Success && hero.Money == 0, "zero-wallet purchase refused");
        check(!simulation.ExecuteAction("hero", AgentAction.ConsumeBread).Success && hero.Hunger == 0, "empty consume does not mutate needs");
        check(!simulation.ExecuteAction("bakery", AgentAction.ConsumeBread).Success, "business cannot execute agent command");
        check(!simulation.ExecuteAction(null, AgentAction.BuyBread).Success && !simulation.ExecuteAction("hero", (AgentAction)99).Success, "invalid agent commands refused");
        var demo = DailySimulation.HeroDemo();
        hero = demo.Hero;
        check(hero.Money == 12 && hero.Hunger == 50 && hero.Thirst == 20 && demo.Bakery.Stock(Good.Bread) == 2, "developer demo funded wallet and actual bread");
        check(demo.Economy.TotalMoney() == 1642 && demo.Economy.InitialMoney == 1642 && SettlementReport.Capture(demo).MoneyConserved, "demo and report money conserved");
        check(demo.ExecuteAction("hero", AgentAction.BuyBread).Success && hero.Money == 6 && hero.Stock(Good.Bread) == 1 && demo.Bakery.Stock(Good.Bread) == 1, "hero command purchases real stock");
        check(demo.ExecuteAction("hero", AgentAction.ConsumeBread).Success && hero.Hunger == 25 && hero.Thirst == 20 && hero.Stock(Good.Bread) == 0, "hero consume one item reduces only hunger");
        check(!demo.ExecuteAction("hero", AgentAction.ConsumeBread).Success && hero.Hunger == 25, "repeat consume cannot duplicate effects");
        string xml = SimulationSave.ToXml(demo);
        var loaded = SimulationSave.FromXml(xml);
        check(loaded.Capture().Version == 4 && loaded.Hero.Money == 6 && loaded.Hero.Hunger == 25 && loaded.Hero.Thirst == 20, "XML v4 hero wallet and needs roundtrip");
        check(SimulationSave.ToXml(loaded) == xml, "hero exact XML roundtrip");
        check(loaded.ExecuteAction("hero", AgentAction.BuyBread).Success && loaded.Hero.Stock(Good.Bread) == 1, "loaded hero can continue command");
        loaded = SimulationSave.FromXml(SimulationSave.ToXml(loaded));
        check(loaded.Hero.Money == 0 && loaded.Hero.Stock(Good.Bread) == 1 && loaded.Hero.Items.Capacity == 40, "carried bread and capacity roundtrip");
        check(!loaded.ExecuteAction("hero", AgentAction.BuyBread).Success && loaded.Hero.Stock(Good.Bread) == 1, "loaded insufficient money leaves inventory intact");
        var capacity = DailySimulation.HeroDemo();
        capacity.Hero.Store.TryAdd("grain", 40, out _);
        long coins = capacity.Bakery.Money;
        check(!capacity.ExecuteAction("hero", AgentAction.BuyBread).Success && capacity.Hero.Money == 12 && capacity.Bakery.Money == coins && capacity.Bakery.Stock(Good.Bread) == 2, "hero capacity failure leaves money and stock intact");
        check(!capacity.Economy.Produce("hero", Good.Bread, 1).Success && capacity.Hero.Items.TotalQuantity == 40, "production capacity failure atomic");
        check(capacity.Economy.Produce("hero", Good.Bread, 1, Good.Grain).Success && capacity.Hero.Items.TotalQuantity == 40, "recipe checks final capacity after ingredients");
        var npc = demo.Economy.Residents[1];
        check(demo.ExecuteAction(npc.Id, AgentAction.BuyBread).Success && npc.Stock(Good.Bread) == 1, "NPC uses same buy command as hero");
        demo.Economy.AdvanceNeeds(npc, 50, 20);
        check(demo.ExecuteAction(npc.Id, AgentAction.ConsumeBread).Success && npc.Hunger == 25 && npc.Thirst == 20, "NPC uses same consume and needs API");
        for (int v = 1; v <= 3; v++)
        {
            var old = new DailySimulation().Capture(); old.Version = v;
            var migrated = DailySimulation.FromSave(old); migrated.EnableHero();
            check(migrated.Hero.Money == 0 && migrated.Hero.Items.TotalQuantity == 0 && migrated.Economy.TotalMoney() == old.InitialMoney, "legacy save adds empty hero without invented property");
        }
        var corrupted = demo.Capture();
        corrupted.Accounts.Find(a => a.Id == "hero").Thirst = 101;
        Throws(() => DailySimulation.FromSave(corrupted), check, "corrupt hero needs refused");
        corrupted = demo.Capture(); corrupted.Accounts.Find(a => a.Id == "hero").Grain = 41;
        Throws(() => DailySimulation.FromSave(corrupted), check, "corrupt hero capacity refused");
        corrupted = demo.Capture(); corrupted.Accounts.RemoveAll(a => a.Id == "hero");
        Throws(() => DailySimulation.FromSave(corrupted), check, "missing v4 hero refused");
        var checkpoint = new ReloadCheckpoint(); checkpoint.Remember(demo);
        demo.BeginDay(); demo.ExecuteAction("hero", AgentAction.ConsumeBread); checkpoint.Remember(demo);
        check(SimulationSave.ToXml(checkpoint.Restore()) == checkpoint.Xml, "partial day rollback includes hero");
        var baseline = new DailySimulation(); baseline.EnableHero();
        for (int i = 0; i < 100; i++) baseline.Step();
        check(baseline.LastPaid == 18 && baseline.LastFed == 20 && baseline.Economy.TotalMoney() == 1642, "zero-wallet hero preserves NPC 100-day baseline");
        check(baseline.Hero.Hunger == 100 && baseline.Hero.Thirst == 100, "hero needs advance once per finished day and clamp");
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hero-check-" + Guid.NewGuid() + ".xml");
        try
        {
            SimulationSave.Write(path, loaded);
            var disk = SimulationSave.Read(path);
            check(disk.Hero.Money == loaded.Hero.Money && disk.Hero.Stock(Good.Bread) == 1 && disk.Hero.Thirst == loaded.Hero.Thirst, "hero disk save/load preserves wallet items needs");
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
    static void DeathChecks(Action<bool, string> check)
    {
        var demo = DailySimulation.HeroDemo();
        var npc = demo.Agent("npc-01");
        check(demo.ExecuteAction(npc.Id, AgentAction.BuyBread).Success, "death fixture buys real personal bread");
        long wallet = npc.Money; int stock = npc.Stock(Good.Bread);
        var point = new SavedPoint { X = -9.55f, Y = 0.8f, Z = -3.5f };
        check(demo.KillAgent(npc.Id, point).Success && npc.IsDead && npc.Money == wallet && npc.Stock(Good.Bread) == stock, "death keeps personal property on same account");
        point.X = 99;
        check(npc.DeathPoint.X == -9.55f, "death position input copied");
        var readPoint = npc.DeathPoint; readPoint.X = 99;
        check(npc.DeathPoint.X == -9.55f, "death position output copied");
        check(!demo.KillAgent(npc.Id, point).Success && demo.EmployerOf(npc.Id) == null, "death idempotent and removes employee job");
        check(!demo.ExecuteAction(npc.Id, AgentAction.BuyBread).Success && !demo.ExecuteAction(npc.Id, AgentAction.ConsumeBread).Success, "dead agent commands refused");
        check(!demo.Economy.Transfer(npc.Id, "hero", 1, "Bypass").Success && !demo.Economy.Transfer("hero", npc.Id, 1, "Gift").Success, "dead wallet blocks ordinary transfers both directions");
        check(!demo.Economy.Produce(npc.Id, Good.Grain, 1).Success && !demo.Economy.TransferGoods(npc.Id, "hero", Good.Bread, 1, true).Success, "dead stock blocks normal production or transfer bypass");
        string before = Property(demo);
        check(!demo.Loot("hero", npc.Id, Good.Bread, 1, true, false).Success && Property(demo) == before, "denied combined loot atomic");
        check(!demo.Loot("hero", "npc-02", null, 0, true, true).Success && Property(demo) == before, "cannot loot living agent");
        check(!demo.Loot("hero", "bakery", null, 0, true, true).Success && Property(demo) == before, "cannot loot business capital");
        check(!demo.Loot(npc.Id, "hero", null, 0, true, true).Success, "dead looter cannot act");
        check(!demo.Loot("hero", npc.Id, (Good)99, 1, true, true).Success && Property(demo) == before, "invalid loot item atomic");
        check(!demo.Loot("hero", npc.Id, Good.Bread, -1, true, true).Success && Property(demo) == before, "negative loot atomic");
        check(!demo.Loot("hero", npc.Id, Good.Bread, 2, true, true).Success && Property(demo) == before, "insufficient item leaves corpse coins intact");
        demo.Hero.Store.TryAdd("grain", 40, out _); before = Property(demo);
        check(!demo.Loot("hero", npc.Id, Good.Bread, 1, true, true).Success && Property(demo) == before, "capacity failure leaves coins and items intact");
        demo.Hero.Store.TryRemove("grain", 40, out _);
        long playerWallet = demo.Hero.Money;
        check(demo.Loot("hero", npc.Id, Good.Bread, 1, true, true).Success
            && demo.Hero.Money == playerWallet + wallet && demo.Hero.Stock(Good.Bread) == 1
            && npc.Money == 0 && npc.Stock(Good.Bread) == 0, "combined loot moves exact existing property");
        before = Property(demo);
        check(!demo.Loot("hero", npc.Id, null, 0, true, true).Success && !demo.Loot("hero", npc.Id, Good.Bread, 1, false, true).Success
            && Property(demo) == before, "empty corpse cannot be looted twice");
        check(demo.Economy.TotalMoney() == 1642 && SettlementReport.Capture(demo).MoneyConserved, "loot and corpse report conserve money");
        string xml = SimulationSave.ToXml(demo);
        var loaded = SimulationSave.FromXml(xml);
        check(loaded.Capture().Version == 6 && loaded.Agent(npc.Id).IsDead && loaded.Agent(npc.Id).DeathPoint.X == -9.55f, "v6 death position roundtrip");
        check(SimulationSave.ToXml(loaded) == xml && !loaded.Loot("hero", npc.Id, null, 0, true, true).Success, "Load does not refill looted corpse");
        for (int i = 0; i < 100; i++) loaded.Step();
        check(loaded.Agent(npc.Id).Money == 0 && loaded.Agent(npc.Id).Stock(Good.Bread) == 0 && loaded.LastPaid == 17, "dead NPC receives no wages or food in 100 days");
        check(loaded.Economy.TotalMoney() == 1642 && SettlementReport.Capture(loaded).Dead == 1, "dead day stages complete and preserve conservation");
        var ownerDemo = DailySimulation.HeroDemo(); var owner = ownerDemo.Agent(ownerDemo.Businesses[1].Owner);
        long businessMoney = ownerDemo.Bakery.Money; int businessBread = ownerDemo.Bakery.Stock(Good.Bread);
        check(ownerDemo.KillAgent(owner.Id, new SavedPoint { X = 7, Y = 0.8f, Z = -3.5f }).Success, "business owner can die without deleting business");
        check(ownerDemo.Bakery.Suspended && ownerDemo.EmployeeCount("bakery") == 0 && !ownerDemo.BusinessActive("bakery"), "owner death suspends business and releases employees");
        check(!ownerDemo.AssignJob("npc-02", "bakery", out _) && !ownerDemo.ExecuteAction("hero", AgentAction.BuyBread).Success, "suspended business rejects hiring and selling");
        check(!ownerDemo.Economy.Transfer("bakery", "npc-02", 1, "Wage").Success && !ownerDemo.Economy.Produce("bakery", Good.Bread, 1).Success, "suspended business blocks payout and production");
        check(ownerDemo.Loot("hero", owner.Id, null, 0, true, true).Success && ownerDemo.Bakery.Money == businessMoney
            && ownerDemo.Bakery.Stock(Good.Bread) == businessBread, "owner body does not include business assets");
        ownerDemo = SimulationSave.FromXml(SimulationSave.ToXml(ownerDemo));
        check(ownerDemo.Bakery.Suspended && ownerDemo.EmployeeCount("bakery") == 0, "suspension derives correctly after Load");
        for (int i = 0; i < 30; i++) ownerDemo.Step();
        check(ownerDemo.Bakery.Money == businessMoney && ownerDemo.Bakery.Stock(Good.Bread) == businessBread && ownerDemo.LastPaid <= 9, "suspended business stays untouched for 30 days");
        check(ownerDemo.Economy.TotalMoney() == 1642 && SettlementReport.Capture(ownerDemo).MoneyConserved, "owner death conservation includes frozen business");
        var heroDead = DailySimulation.HeroDemo();
        heroDead.ExecuteAction("hero", AgentAction.BuyBread);
        check(heroDead.KillAgent("hero", new SavedPoint()).Success, "hero shares same death state");
        check(heroDead.Loot("npc-01", "hero", Good.Bread, 1, true, true).Success && heroDead.Hero.Money == 0 && heroDead.Hero.Stock(Good.Bread) == 0, "NPC uses same corpse loot API for hero");
        heroDead = SimulationSave.FromXml(SimulationSave.ToXml(heroDead));
        check(heroDead.Hero.IsDead && !heroDead.ExecuteAction("hero", AgentAction.ConsumeBread).Success, "hero death and no action survive Load");
        heroDead.Step(); check(heroDead.Hero.Hunger == 50 && heroDead.Hero.Thirst == 20, "dead hero needs do not keep advancing");
        var noHero = new DailySimulation();
        noHero.KillAgent("npc-01", new SavedPoint());
        check(SimulationSave.FromXml(SimulationSave.ToXml(noHero)).Hero == null, "v6 NPC-only save does not invent hero");
        var corrupt = demo.Capture(); corrupt.Accounts.Find(a => a.Id == npc.Id).DeathPoint = null;
        Throws(() => DailySimulation.FromSave(corrupt), check, "missing death position refused");
        corrupt = demo.Capture(); corrupt.Accounts.Find(a => a.Id == npc.Id).DeathPoint.X = float.NaN;
        Throws(() => DailySimulation.FromSave(corrupt), check, "non-finite corpse position refused");
        corrupt = demo.Capture(); corrupt.Accounts.Find(a => a.Id == "bakery").IsDead = true; corrupt.Accounts.Find(a => a.Id == "bakery").DeathPoint = new SavedPoint();
        Throws(() => DailySimulation.FromSave(corrupt), check, "business cannot become corpse");
        corrupt = demo.Capture(); corrupt.Jobs.Add(new SavedJob { Resident = npc.Id, Employer = "farm" });
        Throws(() => DailySimulation.FromSave(corrupt), check, "dead saved employee refused");
        corrupt = demo.Capture(); corrupt.Version = 4;
        Throws(() => DailySimulation.FromSave(corrupt), check, "dead state cannot be downgraded to legacy save");
        var activeDay = new DailySimulation(); activeDay.BeginDay();
        check(!activeDay.KillAgent("npc-01", new SavedPoint()).Success && !activeDay.Agent("npc-01").IsDead, "death transition restricted to completed day boundary");
        check(!demo.KillAgent("bakery", new SavedPoint()).Success && !demo.KillAgent("missing", new SavedPoint()).Success, "unknown or business death refused");
        check(!demo.KillAgent("npc-02", new SavedPoint { X = float.NaN }).Success && !demo.Agent("npc-02").IsDead, "invalid death point atomic");
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "death-check-" + Guid.NewGuid() + ".xml");
        try { SimulationSave.Write(path, loaded); check(SimulationSave.Read(path).Agent(npc.Id).IsDead, "v6 disk state persists death"); }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    static string Property(DailySimulation simulation)
    {
        string state = "";
        foreach (var npc in simulation.Economy.Residents) state += $"{npc.Id}:{npc.Money}:{npc.Stock(Good.Grain)}:{npc.Stock(Good.Bread)};";
        if (simulation.Hero != null) state += $"hero:{simulation.Hero.Money}:{simulation.Hero.Stock(Good.Grain)}:{simulation.Hero.Stock(Good.Bread)};";
        return state + $"biz:{simulation.Farm.Money}:{simulation.Farm.Stock(Good.Grain)}:{simulation.Bakery.Money}:{simulation.Bakery.Stock(Good.Bread)}";
    }

    static void PoseChecks(Action<bool, string> check)
    {
        var demo = DailySimulation.HeroDemo();
        var pose = new SavedHeroPose { X = 2, Y = 0.08f, Z = -2, FacingYaw = 90, CameraYaw = 135,
            CameraPitch = -30, CameraDistance = 5, FirstPerson = true };
        demo.SetHeroPose(pose); pose.X = 99;
        check(demo.HeroPose.X == 2, "pose input copied");
        var external = demo.HeroPose; external.X = 99;
        check(demo.HeroPose.X == 2, "pose output copied");
        var snapshot = demo.Capture(); snapshot.HeroPose.X = 99;
        check(demo.HeroPose.X == 2, "captured pose copied");
        string xml = SimulationSave.ToXml(demo);
        var loaded = SimulationSave.FromXml(xml);
        check(loaded.Capture().Version == 5 && loaded.HeroPose.X == 2 && loaded.HeroPose.CameraPitch == -30 && loaded.HeroPose.FirstPerson, "XML v5 position and camera roundtrip");
        check(SimulationSave.ToXml(loaded) == xml && loaded.Hero.Money == 12, "pose save preserves exact state and wallet");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f })
        {
            var invalid = loaded.HeroPose; invalid.X = bad;
            Throws(() => loaded.SetHeroPose(invalid), check, "invalid position refused");
            check(SimulationSave.ToXml(loaded) == xml, "invalid position update atomic");
        }
        var missing = demo.Capture(); missing.HeroPose = null;
        Throws(() => DailySimulation.FromSave(missing), check, "missing v5 pose refused");
        var wrongVersion = demo.Capture(); wrongVersion.Version = 4;
        Throws(() => DailySimulation.FromSave(wrongVersion), check, "pose in wrong schema refused");
        var invalidYaw = loaded.HeroPose; invalidYaw.CameraYaw = 360;
        Throws(() => loaded.SetHeroPose(invalidYaw), check, "invalid camera yaw refused");
        var invalidPitch = loaded.HeroPose; invalidPitch.CameraPitch = -76;
        Throws(() => loaded.SetHeroPose(invalidPitch), check, "invalid first-person pitch refused");
        invalidPitch = loaded.HeroPose; invalidPitch.FirstPerson = false;
        Throws(() => loaded.SetHeroPose(invalidPitch), check, "invalid third-person pitch refused");
        var invalidDistance = loaded.HeroPose; invalidDistance.CameraDistance = 2;
        Throws(() => loaded.SetHeroPose(invalidDistance), check, "invalid distance refused");
        var legacy = SimulationSave.FromXml(SimulationSave.ToXml(DailySimulation.HeroDemo()));
        check(legacy.HeroPose == null && legacy.Hero.Money == 12, "v4 retains property without invented pose");
        var checkpoint = new ReloadCheckpoint(); checkpoint.Remember(demo); demo.BeginDay();
        var moved = demo.HeroPose; moved.X = 4; demo.SetHeroPose(moved); checkpoint.Remember(demo);
        check(checkpoint.Restore().HeroPose.X == 2, "partial day checkpoint rolls pose back together with economy");
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pose-check-" + Guid.NewGuid() + ".xml");
        try
        {
            SimulationSave.Write(path, loaded);
            check(SimulationSave.Read(path).HeroPose.CameraYaw == 135, "pose disk save/load");
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    static void CheckRejected(ItemInventory from, ItemInventory to, string id, int amount, bool access, Action<bool, string> check, string label)
    {
        string old = State(from) + "/" + State(to);
        check(!ItemInventory.TryTransfer(from, to, id, amount, access, out var reason) && !string.IsNullOrEmpty(reason)
            && old == State(from) + "/" + State(to), label);
    }
    static void Throws(Action action, Action<bool, string> check, string label)
    {
        try { action(); }
        catch (ArgumentException) { check(true, label); return; }
        catch (NotSupportedException) { check(true, label); return; }
        check(false, label);
    }
}
