using System;
using LivingEconomy.Simulation;

static class Program
{
    static int count;
    static void Check(bool result, string name)
    {
        if (!result) throw new Exception("FAILED: " + name);
        count++;
    }
    static Economy Pair(long buyerMoney = 100, int buyerBread = 0) => new Economy(new[] {
        new Resident("buyer", "Buyer", Profession.None, buyerMoney, 0, buyerBread),
        new Resident("seller", "Seller", Profession.Baker, 20, 0, 10) });

    static void Main()
    {
        var e = Pair();
        Check(e.Buy("buyer", "seller", Good.Bread, 2, 5).Success, "purchase accepted");
        Check(e.Residents[0].Money == 90 && e.Residents[1].Money == 30, "exact payment");
        Check(e.Residents[0].Stock(Good.Bread) == 2 && e.Residents[1].Stock(Good.Bread) == 8, "goods delivered");
        Check(e.TotalMoney() == e.InitialMoney, "money conserved");
        var before = Snapshot(e);
        Check(!e.Buy("buyer", "seller", Good.Bread, 100, 1).Success && Snapshot(e) == before, "stock failure atomic");
        Check(!e.Buy("buyer", "seller", Good.Bread, 1, 1000).Success && Snapshot(e) == before, "money failure atomic");
        Check(!e.Buy("buyer", "seller", Good.Bread, 0, 5).Success, "zero rejected");
        Check(!e.Buy("buyer", "seller", Good.Bread, -1, 5).Success, "negative rejected");
        Check(!e.Buy("buyer", "seller", (Good)99, 1, 5).Success, "unknown good rejected");
        Check(!e.Buy(null, "seller", Good.Bread, 1, 5).Success, "unknown participant rejected");
        Check(!e.Buy("buyer", "buyer", Good.Bread, 1, 5).Success, "self trade rejected");
        Check(!e.Buy("buyer", "seller", Good.Bread, 2, long.MaxValue).Success && Snapshot(e) == before, "price overflow atomic");
        var full = Pair(100, int.MaxValue);
        Check(!full.Buy("buyer", "seller", Good.Bread, 1, 1).Success && full.Residents[0].Money == 100, "inventory overflow atomic");
        Check(e.Transfer("seller", "buyer", 8, "Wages").Success && e.Residents[0].Money == 98, "funded transfer");
        before = Snapshot(e);
        Check(!e.Transfer("seller", "buyer", 100, "Wages").Success && Snapshot(e) == before, "unfunded transfer atomic");
        Check(!e.Transfer("seller", "buyer", -1, "Wages").Success && Snapshot(e) == before, "negative transfer rejected");
        Check(!e.Transfer("seller", "buyer", 1, "").Success && Snapshot(e) == before, "transfer reason required");
        for (int i = 0; i < e.Ledger.Count; i++) Check(e.Ledger[i].Sequence == i + 1, "ledger sequence");
        var a = PrototypeScenario.Create(); var b = PrototypeScenario.Create();
        Check(a.Residents.Count == 20 && Snapshot(a) == Snapshot(b), "seed reproducibility");
        for (int i = 0; i < 10000; i++)
        {
            a.AdvanceTick();
            a.Buy("npc-04", "npc-06", Good.Bread, 1, 5);
            a.Transfer("npc-06", "npc-04", 8, "Test payment");
            if (a.TotalMoney() != a.InitialMoney) throw new Exception("Conservation failure");
            foreach (var r in a.Residents)
                if (r.Money < 0 || r.Stock(Good.Bread) < 0) throw new Exception("Negative state");
        }
        Check(true, "10000 mixed operations preserve invariants");
        var daily = new DailySimulation();
        var replay = new DailySimulation();
        for (int day = 1; day <= 100; day++)
        {
            daily.Step(); replay.Step();
            Check(daily.Economy.TotalMoney() == daily.Economy.InitialMoney, "daily money conserved");
            Check(daily.LastPaid == 18 && daily.LastFed == 20 && daily.LastBread == 20, "baseline employment and meals");
            Check(daily.Farm.Money == 120 && daily.Bakery.Money == 120, "working capital preserved");
            Check(Snapshot(daily.Economy) == Snapshot(replay.Economy), "daily replay deterministic");
            foreach (var npc in daily.Economy.Residents) Check(npc.Hunger == 0 && npc.Money >= 0, "baseline no hunger");
            if (day == 30 || day == 100) Console.WriteLine($"Day {day}: paid={daily.LastPaid}, fed={daily.LastFed}, bread={daily.LastBread}, coins={daily.Economy.TotalMoney()}");
        }
        foreach (var scenario in new[] { new DailySimulation(farmYield: 0), new DailySimulation(capital: 0) })
        {
            for (int day = 0; day < 100; day++)
            {
                scenario.Step();
                Check(scenario.Economy.TotalMoney() == scenario.Economy.InitialMoney, "crisis money conserved");
                Check(scenario.Farm.Money >= 0 && scenario.Bakery.Money >= 0, "crisis businesses solvent or zero");
                foreach (var npc in scenario.Economy.Residents)
                    Check(npc.Money >= 0 && npc.Stock(Good.Bread) >= 0 && npc.Hunger <= 100, "crisis bounds");
            }
            Check(scenario.LastPaid < 18 && scenario.LastFed < 20, "crisis affects jobs and food");
        }
        var recipe = Pair();
        before = Snapshot(recipe);
        Check(!recipe.Produce("seller", Good.Bread, 1, Good.Grain).Success && Snapshot(recipe) == before, "missing recipe inputs atomic");
        Check(!recipe.Produce("seller", Good.Bread, 1, Good.Bread).Success && Snapshot(recipe) == before, "self recipe rejected");
        foreach (var staged in new[] { new DailySimulation(), new DailySimulation(farmYield: 0), new DailySimulation(capital: 0) })
        {
            var stagedEconomy = staged.Economy;
            staged.BeginDay();
            Check(staged.LastPaid == 0 && staged.LastBread == 0 && staged.LastFed == 0, "begin day does not settle economy");
            Check(!staged.ArriveAtBakery("npc-01") && !staged.ArriveAtHome("npc-01") && !staged.FinishDay(), "out of order actions refused");
            Check(!staged.ArriveAtWork("missing") && !staged.FinishWork(), "invalid arrivals do not advance work");
            foreach (var npc in stagedEconomy.Residents)
            {
                staged.ArriveAtWork(npc.Id);
                int entries = stagedEconomy.Ledger.Count;
                Check(!staged.ArriveAtWork(npc.Id) && stagedEconomy.Ledger.Count == entries, "duplicate wages and work prevented");
            }
            Check(staged.LastFed == 0 && staged.LastBread == 0, "no purchases or bread before work closes");
            Check(staged.FinishWork() && !staged.FinishWork(), "baking executes once");
            foreach (var npc in stagedEconomy.Residents)
            {
                staged.ArriveAtBakery(npc.Id);
                int entries = stagedEconomy.Ledger.Count;
                Check(!staged.ArriveAtBakery(npc.Id) && stagedEconomy.Ledger.Count == entries, "duplicate purchases prevented");
                Check(npc.Hunger == 0, "hunger changes only at home");
                staged.ArriveAtHome(npc.Id);
                entries = stagedEconomy.Ledger.Count;
                Check(!staged.ArriveAtHome(npc.Id) && stagedEconomy.Ledger.Count == entries, "duplicate meals prevented");
            }
            Check(staged.FinishDay() && !staged.FinishDay() && !staged.DayInProgress, "day closes once");
            Check(stagedEconomy.TotalMoney() == stagedEconomy.InitialMoney, "arrival money conserved");
        }
        var savePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LivingEconomy-check-" + Guid.NewGuid() + ".xml");
        foreach (var saving in new[] { new DailySimulation(seed: 7), new DailySimulation(farmYield: 0), new DailySimulation(capital: 0) })
        {
            for (int day = 0; day < 10; day++) saving.Step();
            SimulationSave.Write(savePath, saving);
            var loaded = SimulationSave.Read(savePath);
            Check(Snapshot(saving.Economy) == Snapshot(loaded.Economy) && loaded.Economy.Tick == 10, "disk restore accounts and day");
            Check(saving.Farm.Money == loaded.Farm.Money && saving.Bakery.Money == loaded.Bakery.Money, "disk restore business balances");
            Check(saving.Economy.Ledger.Count == loaded.Economy.Ledger.Count, "disk restore ledger count");
            for (int i = 0; i < saving.Economy.Ledger.Count; i++)
                Check(saving.Economy.Ledger[i].ToString() == loaded.Economy.Ledger[i].ToString(), "disk restore exact ledger");
            for (int day = 0; day < 90; day++)
            {
                saving.Step(); loaded.Step();
                Check(Snapshot(saving.Economy) == Snapshot(loaded.Economy), "loaded continuation equivalent");
                Check(saving.Farm.Money == loaded.Farm.Money && saving.Bakery.Money == loaded.Bakery.Money
                    && loaded.Economy.TotalMoney() == loaded.Economy.InitialMoney, "loaded continuation conserved");
                for (int i = 0; i < 20; i++) Check(saving.Economy.Residents[i].Hunger == loaded.Economy.Residents[i].Hunger, "loaded hunger equivalent");
            }
            var damaged = saving.Capture(); damaged.Accounts[0].Money++;
            bool refused = false;
            try { DailySimulation.FromSave(damaged); } catch (ArgumentException) { refused = true; }
            Check(refused, "damaged money total refused");
            damaged = saving.Capture(); damaged.Version = 99; refused = false;
            try { DailySimulation.FromSave(damaged); } catch (ArgumentException) { refused = true; }
            Check(refused, "unknown save version refused");
            saving.BeginDay(); refused = false;
            try { SimulationSave.Write(savePath, saving); } catch (InvalidOperationException) { refused = true; }
            Check(refused && SimulationSave.Read(savePath).Economy.Tick == 10, "midday save refused and existing save preserved");
        }
        System.IO.File.WriteAllText(savePath, "broken xml");
        bool malformed = false;
        try { SimulationSave.Read(savePath); } catch (InvalidOperationException) { malformed = true; }
        Check(malformed, "malformed file refused");
        System.IO.File.Delete(savePath); System.IO.File.Delete(savePath + ".bak");
        Console.WriteLine($"PASS: {count} assertions; trade, arrival and disk save/load checks with 100-day continuations.");
    }
    static string Snapshot(Economy e)
    {
        string result = "";
        foreach (var r in e.Residents) result += $"{r.Id}:{r.Money}:{r.Stock(Good.Grain)}:{r.Stock(Good.Bread)};";
        return result;
    }
}
