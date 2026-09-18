using System;
using System.Collections.Generic;
using LivingEconomy.Simulation;

static class InventoryChecks
{
    public static void Run(Action<bool, string> check)
    {
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
