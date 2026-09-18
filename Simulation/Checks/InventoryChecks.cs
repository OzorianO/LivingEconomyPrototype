using System;
using System.Collections.Generic;
using LivingEconomy.Simulation;

static class InventoryChecks
{
    public static void Run(Action<bool, string> check)
    {
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
