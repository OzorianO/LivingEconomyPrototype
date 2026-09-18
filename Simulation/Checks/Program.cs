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
        InventoryChecks.Run(Check);
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
            }
            Check(staged.FinishShopping() && !staged.FinishShopping(), "shopping closes once");
            foreach (var npc in stagedEconomy.Residents)
            {
                staged.ArriveAtHome(npc.Id);
                int entries = stagedEconomy.Ledger.Count;
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
        var employment = new DailySimulation(autoEmployment: false);
        long employmentMoney = employment.Economy.TotalMoney();
        Check(!employment.AssignJob("npc-01", "bakery", out var reason) && reason == "No vacancy.", "full employer rejects switch");
        Check(employment.EmployerOf("npc-01") == "farm", "failed switch keeps old job");
        Check(!employment.AssignJob("npc-00", "bakery", out _), "owner cannot take employee vacancy");
        Check(!employment.AssignJob("missing", null, out _), "unknown resident rejected");
        Check(!employment.AssignJob("npc-01", "missing", out _), "unknown business rejected");
        Check(employment.AssignJob("npc-11", null, out _), "resignation creates vacancy");
        Check(employment.AssignJob("npc-01", "bakery", out _), "atomic switch to vacancy");
        Check(employment.AssignJob("npc-11", "farm", out _), "vacated farm job filled");
        Check(!employment.AssignJob("npc-12", "farm", out _), "last vacancy cannot be double filled");
        Check(employment.AssignJob("npc-02", null, out _), "unemployment supported");
        Check(employment.Economy.TotalMoney() == employmentMoney, "employment never creates money");
        var jobSave = employment.Capture();
        SimulationSave.Write(savePath, employment);
        var loadedEmployment = SimulationSave.Read(savePath);
        System.IO.File.Delete(savePath);
        Check(loadedEmployment.EmployerOf("npc-01") == "bakery" && loadedEmployment.EmployerOf("npc-02") == null, "changed and missing jobs restored");
        for (int day = 0; day < 100; day++)
        {
            employment.Step(); loadedEmployment.Step();
            Check(!employment.DayInProgress && employment.Economy.TotalMoney() == employmentMoney, "unemployed does not block day or create money");
            Check(Snapshot(employment.Economy) == Snapshot(loadedEmployment.Economy), "changed jobs save continuation matches");
        }
        employment.BeginDay();
        Check(!employment.AssignJob("npc-03", null, out _), "midday job changes refused");
        var legacy = new DailySimulation().Capture(); legacy.Version = 1; legacy.Businesses.Clear();
        foreach (var legacyAccount in legacy.Accounts)
            if (legacyAccount.Id == "farm") legacyAccount.Name = "Farm (owner npc-00)";
            else if (legacyAccount.Id == "bakery") legacyAccount.Name = "Bakery (owner npc-06)";
        Check(DailySimulation.FromSave(legacy).Jobs.Count == 18, "legacy v1 migration");
        var alternate = new DailySimulation(businesses: new[] {
            new BusinessDefinition("farm", "npc-03", 8, 5), new BusinessDefinition("bakery", "npc-12", 7, 7) });
        Check(alternate.IsOwner("npc-03") && alternate.Jobs.Count == 15, "scenario owners capacities wages are data");
        var alternateLoaded = DailySimulation.FromSave(alternate.Capture());
        alternate.Step(); alternateLoaded.Step();
        Check(Snapshot(alternate.Economy) == Snapshot(alternateLoaded.Economy), "custom business rules saved");
        var invalidJobSave = new DailySimulation().Capture(); invalidJobSave.Jobs[0].Employer = "missing";
        bool invalidJob = false;
        try { DailySimulation.FromSave(invalidJobSave); } catch (ArgumentException) { invalidJob = true; }
        Check(invalidJob, "invalid saved employer rejected");
        OrderedArrivals();
        UnreachableRoutes();
        AutonomousJobs();
        EconomicReports();
        ReloadCheckpoints();
        Console.WriteLine($"PASS: {count} assertions; trade, arrival and disk save/load checks with 100-day continuations.");
    }

    static void OrderedArrivals()
    {
        foreach (int seed in new[] { 7, 42, 123 })
        foreach (var parameters in new[] { (yield: 1, capital: 120L), (yield: 0, capital: 12L), (yield: 2, capital: 6L) })
        {
            var fast = new DailySimulation(seed, parameters.yield, parameters.capital);
            var animated = new DailySimulation(seed, parameters.yield, parameters.capital);
            var rng = new Random(seed);
            for (int day = 0; day < 100; day++)
            {
                fast.Step(); animated.BeginDay();
                var residents = new System.Collections.Generic.List<Resident>(animated.Economy.Residents);
                Shuffle(residents, rng);
                foreach (var npc in residents) animated.ArriveAtWork(npc.Id);
                Check(animated.LastPaid == 0, "arrival order cannot allocate scarce wages");
                Check(animated.FinishWork(), "random work arrivals finish");
                Shuffle(residents, rng);
                foreach (var npc in residents) animated.ArriveAtBakery(npc.Id);
                Check(animated.FinishShopping(), "random shopping arrivals finish");
                Shuffle(residents, rng);
                foreach (var npc in residents) animated.ArriveAtHome(npc.Id);
                Check(animated.FinishDay(), "random home arrivals finish");
                Check(FullState(fast) == FullState(animated), "scarcity result independent of frame arrival order");
                Check(Ledger(fast) == Ledger(animated), "scarcity ledger independent of arrival order");
            }
        }
        var v2 = new DailySimulation(autoEmployment: false).Capture(); v2.Version = 2;
        Check(!DailySimulation.FromSave(v2).AutoEmployment, "legacy v2 keeps manual employment");
    }

    static void UnreachableRoutes()
    {
        foreach (DayStage stage in Enum.GetValues(typeof(DayStage)))
        {
            var simulation = new DailySimulation();
            simulation.BeginDay();
            Check(!simulation.ReportUnreachable("missing", stage, "Missing")
                && !simulation.ReportUnreachable("npc-01", stage, ""), "invalid failures refused");
            foreach (var npc in simulation.Economy.Residents)
                if (stage == DayStage.Work && npc.Id == "npc-01")
                    Check(simulation.ReportUnreachable(npc.Id, stage, "Blocked work"), "work failure accepted");
                else simulation.ArriveAtWork(npc.Id);
            Check(simulation.FinishWork(), "unreachable work cannot stall day");
            foreach (var npc in simulation.Economy.Residents)
                if (stage == DayStage.Shopping && npc.Id == "npc-01")
                    Check(simulation.ReportUnreachable(npc.Id, stage, "Blocked shop"), "shop failure accepted");
                else simulation.ArriveAtBakery(npc.Id);
            Check(simulation.FinishShopping(), "unreachable shop cannot stall day");
            foreach (var npc in simulation.Economy.Residents)
                if (stage == DayStage.Home && npc.Id == "npc-01")
                    Check(simulation.ReportUnreachable(npc.Id, stage, "Blocked home"), "home failure accepted");
                else simulation.ArriveAtHome(npc.Id);
            Check(!simulation.ReportUnreachable("npc-01", stage, "Duplicate"), "failure cannot repeat action");
            Check(simulation.FinishDay() && simulation.LastUnreachable == 1, "failure completes day exactly once");
            Check(simulation.Economy.TotalMoney() == simulation.Economy.InitialMoney, "failure preserves money");
            var affected = simulation.Economy.Residents[1];
            if (stage == DayStage.Work) Check(simulation.LastPaid == 17, "unreachable work receives no wage");
            if (stage == DayStage.Shopping) Check(affected.Hunger == 25, "unreachable shop misses food");
            if (stage == DayStage.Home) Check(affected.Hunger == 25 && affected.Stock(Good.Bread) == 1, "unreachable home preserves carried food and misses meal");
            simulation.Step(); Check(!simulation.DayInProgress, "next day after failure works");
        }
        var blocked = new DailySimulation(); blocked.BeginDay();
        foreach (var npc in blocked.Economy.Residents) blocked.ReportUnreachable(npc.Id, DayStage.Work, "All blocked");
        Check(blocked.FinishWork(), "all work targets blocked completes");
        foreach (var npc in blocked.Economy.Residents) blocked.ReportUnreachable(npc.Id, DayStage.Shopping, "All blocked");
        Check(blocked.FinishShopping(), "all shops blocked completes");
        foreach (var npc in blocked.Economy.Residents) blocked.ReportUnreachable(npc.Id, DayStage.Home, "All blocked");
        Check(blocked.FinishDay() && blocked.LastUnreachable == 60 && blocked.LastFed == 0, "all routes blocked day completes");
    }

    static void AutonomousJobs()
    {
        var seeker = new DailySimulation(businesses: new[] {
            new BusinessDefinition("farm", "npc-00", 9, 5), new BusinessDefinition("bakery", "npc-06", 9, 7) });
        seeker.AssignJob("npc-01", null, out _); seeker.AssignJob("npc-11", null, out _);
        long coins = seeker.Economy.TotalMoney();
        seeker.BeginDay();
        Check(seeker.EmployerOf("npc-01") == "bakery" && seeker.EmployerOf("npc-11") == "farm", "highest wage wins and last vacancy claimed once");
        Check(seeker.DecisionOf("npc-01").Contains("highest"), "decision reason visible");
        Check(seeker.Economy.TotalMoney() == coins, "automatic hiring creates no money");

        var unfunded = new DailySimulation(capital: 0);
        unfunded.AssignJob("npc-01", null, out _); unfunded.Step();
        Check(unfunded.EmployerOf("npc-01") == null && unfunded.DecisionOf("npc-01").Contains("cannot fund"), "unfunded vacancy refused with reason");
        Check(unfunded.LastUnpaid > 0, "unpaid worker retries without production");

        foreach (int seed in new[] { 7, 42, 123 })
        {
            var town = new DailySimulation(seed, businesses: new[] {
                new BusinessDefinition("farm", "npc-00", 7, 6), new BusinessDefinition("bakery", "npc-06", 7, 6) });
            town.AssignJob("npc-01", null, out _);
            town.Step();
            Check(town.Jobs.Count == 14, "automatic search respects capacity with unemployed residents");
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LivingEconomy-autonomy-" + Guid.NewGuid() + ".xml");
            try
            {
                SimulationSave.Write(path, town);
                var loaded = SimulationSave.Read(path);
                Check(loaded.AutoEmployment && loaded.DecisionOf("npc-01") == town.DecisionOf("npc-01"), "disk save preserves autonomy and decision reason");
                for (int day = 0; day < 100; day++)
                {
                    town.Step(); loaded.Step();
                    Check(!town.DayInProgress && FullState(town) == FullState(loaded), "autonomous save continuation matches");
                    Check(town.Economy.TotalMoney() == town.Economy.InitialMoney, "autonomous money conserved");
                    Check(town.EmployeeCount("farm") <= 7 && town.EmployeeCount("bakery") <= 7, "autonomous capacity preserved");
                    foreach (var npc in town.Economy.Residents)
                        Check(npc.Money >= 0 && npc.Hunger >= 0 && npc.Hunger <= 100 && npc.Stock(Good.Bread) >= 0, "autonomous bounds");
                }
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }
    }

    static void Shuffle(System.Collections.Generic.List<Resident> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = random.Next(i + 1); var item = list[i]; list[i] = list[j]; list[j] = item; }
    }

    static void EconomicReports()
    {
        var initial = new DailySimulation();
        string before = FullState(initial); int entries = initial.Economy.Ledger.Count;
        var report = SettlementReport.Capture(initial);
        Check(FullState(initial) == before && initial.Economy.Ledger.Count == entries, "report cannot mutate economy or ledger");
        Check(report.Residents == 20 && report.Employees == 18 && report.Owners == 2 && report.Unemployed == 0, "report distinguishes employees and owners");
        Check(report.ResidentMoney == 1402 && report.BusinessMoney == 240 && report.TotalMoney == 1642 && report.MoneyConserved, "report reconciles wallets and working capital");
        Check(report.Grain == 0 && report.Bread == 0 && report.Hungry == 0 && report.Vacancies == 0, "initial stocks and jobs reported");
        initial.Step(); report = SettlementReport.Capture(initial);
        Check(report.Day == 1 && !report.DayInProgress && report.Hungry == 0 && report.FoodStockRefusals == 0, "baseline report reflects completed day");

        var shortage = new DailySimulation(farmYield: 0);
        shortage.Step(); report = SettlementReport.Capture(shortage);
        Check(report.FoodStockRefusals == 20 && report.FoodMoneyRefusals == 0 && report.Hungry == 20 && report.Bread == 0, "report identifies stock shortage");
        shortage.Step(); shortage.Step(); report = SettlementReport.Capture(shortage);
        Check(report.FoodStockRefusals == 20 && report.SevereHunger == 20, "report counts this day only and severe hunger");
        var restored = DailySimulation.FromSave(shortage.Capture());
        var restoredReport = SettlementReport.Capture(restored);
        Check(restoredReport.FoodStockRefusals == report.FoodStockRefusals && restoredReport.Hungry == report.Hungry
            && restoredReport.ResidentMoney == report.ResidentMoney && restoredReport.MedianWallet == report.MedianWallet, "report reconstructed after restore");

        var poverty = new DailySimulation();
        poverty.Economy.Transfer("npc-00", "npc-01", poverty.Economy.Residents[0].Money, "Report scenario wealth transfer");
        poverty.Economy.Buy("npc-00", "bakery", Good.Bread, 1, 6);
        Check(SettlementReport.Capture(poverty).FoodMoneyRefusals == 1, "report identifies unaffordable food");
        poverty.Step(); report = SettlementReport.Capture(poverty);
        Check(report.FoodMoneyRefusals == 1 && report.Hungry == 1, "report excludes old money refusal and reflects missed meal");

        var concentrated = new DailySimulation();
        foreach (var npc in concentrated.Economy.Residents)
            if (npc.Id != "npc-00" && npc.Money > 0)
                concentrated.Economy.Transfer(npc.Id, "npc-00", npc.Money, "Report scenario concentration");
        report = SettlementReport.Capture(concentrated);
        Check(report.MinimumWallet == 0 && report.MedianWallet == 0 && report.MaximumWallet == report.ResidentMoney
            && report.TopFifthWalletShare == 100m && report.AverageWallet == 70.1m, "wallet distribution excludes business assets");

        var vacancies = new DailySimulation(autoEmployment: false);
        vacancies.AssignJob("npc-01", null, out _); report = SettlementReport.Capture(vacancies);
        Check(report.Unemployed == 1 && report.Vacancies == 1 && report.Employees + report.Owners + report.Unemployed == 20, "report accounts for unemployment and vacancy");
        vacancies.BeginDay();
        Check(SettlementReport.Capture(vacancies).DayInProgress, "report marks partial day");
        var unfunded = new DailySimulation(capital: 0);
        unfunded.AssignJob("npc-01", null, out _); unfunded.Step(); report = SettlementReport.Capture(unfunded);
        Check(report.JobSearchRefusals == 1 && report.Unpaid == 17 && report.Unemployed == 1, "report separates unsuccessful search from unpaid employee");
        Check(report.MoneyConserved, "crisis report reconciles money");
    }

    static void ReloadCheckpoints()
    {
        var simulation = new DailySimulation();
        for (int i = 0; i < 5; i++) simulation.Step();
        simulation.AssignJob("npc-01", null, out _);
        var checkpoint = new ReloadCheckpoint(); checkpoint.Remember(simulation);
        string saved = checkpoint.Xml; string before = FullState(simulation);
        Check(checkpoint.HasSnapshot && !checkpoint.Interrupted, "completed reload checkpoint captured");
        var restored = checkpoint.Restore();
        Check(FullState(restored) == before && Ledger(restored) == Ledger(simulation), "reload checkpoint preserves economy jobs and unicode names");
        simulation.BeginDay();
        foreach (var npc in simulation.Economy.Residents) simulation.ArriveAtWork(npc.Id);
        simulation.FinishWork(); checkpoint.Remember(simulation);
        Check(checkpoint.Interrupted && checkpoint.Xml == saved, "interrupted work retains previous completed-day checkpoint");
        restored = checkpoint.Restore();
        Check(!restored.DayInProgress && restored.Economy.Tick == 5 && FullState(restored) == before, "reload rolls back partial wages and hiring");
        var expected = SimulationSave.FromXml(saved); expected.Step(); restored.Step();
        Check(FullState(restored) == FullState(expected) && Ledger(restored) == Ledger(expected), "restarted day neither duplicates wages nor changes decisions");
        checkpoint.Remember(restored);
        Check(!checkpoint.Interrupted && checkpoint.Restore().Economy.Tick == 6, "completed restarted day advances checkpoint");
        bool refused = false;
        try { new ReloadCheckpoint().Remember(simulation); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "cannot checkpoint partial day without rollback state");
        refused = false;
        try { new ReloadCheckpoint { Xml = "broken" }.Restore(); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "corrupt reload snapshot refuses silent reset");
        refused = false;
        try { SimulationSave.FromXml("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///missing'>]><SaveData>&e;</SaveData>"); }
        catch (System.Xml.XmlException) { refused = true; }
        catch (InvalidOperationException) { refused = true; }
        Check(refused, "reload XML rejects DTD");
    }
    static string Ledger(DailySimulation simulation)
        => string.Join("\n", simulation.Economy.Ledger);
    static string FullState(DailySimulation simulation)
    {
        string state = Snapshot(simulation.Economy) + $"|{simulation.Farm.Money}:{simulation.Farm.Stock(Good.Grain)}:{simulation.Bakery.Money}:{simulation.Bakery.Stock(Good.Grain)}:{simulation.Bakery.Stock(Good.Bread)}|{simulation.LastPaid}:{simulation.LastFed}:{simulation.LastBread}:{simulation.LastUnpaid}:{simulation.LastUnreachable}";
        foreach (var npc in simulation.Economy.Residents) state += $"|{npc.Hunger}:{simulation.EmployerOf(npc.Id)}";
        return state;
    }
    static string Snapshot(Economy e)
    {
        string result = "";
        foreach (var r in e.Residents) result += $"{r.Id}:{r.Money}:{r.Stock(Good.Grain)}:{r.Stock(Good.Bread)};";
        return result;
    }
}
