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
        Console.WriteLine($"PASS: {count} assertions; 10000 mixed-operation ticks.");
    }
    static string Snapshot(Economy e)
    {
        string result = "";
        foreach (var r in e.Residents) result += $"{r.Id}:{r.Money}:{r.Stock(Good.Grain)}:{r.Stock(Good.Bread)};";
        return result;
    }
}
