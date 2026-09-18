using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LivingEconomy.Simulation
{
    public interface IReadOnlyItemInventory
    {
        ItemCatalog Catalog { get; }
        IReadOnlyDictionary<string, int> Quantities { get; }
        long Capacity { get; }
        int SlotCapacity { get; }
        long TotalQuantity { get; }
        long UsedSlots { get; }
        int Quantity(string itemId);
    }

    // Trusted simulation-thread storage API, not a player/network authorization boundary.
    // UI gets IReadOnlyItemInventory; controllers must request actions from the simulation.
    public sealed class ItemInventory : IReadOnlyItemInventory
    {
        private readonly Dictionary<string, int> quantities = new Dictionary<string, int>(StringComparer.Ordinal);
        public ItemCatalog Catalog { get; }
        public IReadOnlyDictionary<string, int> Quantities { get; }
        public long Capacity { get; }
        public int SlotCapacity { get; }
        public long TotalQuantity { get; private set; }
        public long UsedSlots { get; private set; }
        public IReadOnlyItemInventory ReadOnly { get; }

        public ItemInventory(ItemCatalog catalog, long capacity = long.MaxValue, int slotCapacity = int.MaxValue)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (capacity < 0 || slotCapacity < 0) throw new ArgumentOutOfRangeException();
            Capacity = capacity; SlotCapacity = slotCapacity;
            Quantities = new ReadOnlyDictionary<string, int>(quantities);
            ReadOnly = new ReadOnlyView(this);
        }

        public int Quantity(string itemId) => itemId != null && quantities.TryGetValue(itemId, out var q) ? q : 0;

        internal bool CanSet(string itemId, int amount, out string reason)
        {
            reason = null;
            if (!Catalog.TryGet(itemId, out var item)) reason = "Unknown item";
            else if (amount < 0) reason = "Quantity must not be negative";
            else if (TotalQuantity - Quantity(itemId) + amount > Capacity) reason = "Inventory capacity exceeded";
            else if (UsedSlots - Slots(Quantity(itemId), item.MaxStack) + Slots(amount, item.MaxStack) > SlotCapacity)
                reason = "Inventory slots exceeded";
            return reason == null;
        }

        internal void SetQuantity(string itemId, int amount)
        {
            if (!CanSet(itemId, amount, out var reason)) throw new ArgumentException(reason);
            Catalog.TryGet(itemId, out var item);
            int previous = Quantity(itemId);
            TotalQuantity += (long)amount - previous;
            UsedSlots += Slots(amount, item.MaxStack) - Slots(previous, item.MaxStack);
            if (amount == 0) quantities.Remove(itemId); else quantities[itemId] = amount;
        }

        public bool TryAdd(string itemId, int amount, out string reason)
        {
            if (!CanAdd(itemId, amount, out reason)) return false;
            SetQuantity(itemId, Quantity(itemId) + amount);
            return true;
        }

        internal bool CanAdd(string itemId, int amount, out string reason)
        {
            reason = null;
            if (!Catalog.TryGet(itemId, out _)) reason = "Unknown item";
            else if (amount <= 0) reason = "Quantity must be positive";
            else if ((long)Quantity(itemId) + amount > int.MaxValue) reason = "Stock overflow";
            else return CanSet(itemId, Quantity(itemId) + amount, out reason);
            return false;
        }

        public bool TryRemove(string itemId, int amount, out string reason)
        {
            reason = null;
            if (!Catalog.TryGet(itemId, out _)) reason = "Unknown item";
            else if (amount <= 0) reason = "Quantity must be positive";
            else if (Quantity(itemId) < amount) reason = "Insufficient stock";
            if (reason != null) return false;
            SetQuantity(itemId, Quantity(itemId) - amount);
            return true;
        }

        // Authorization is supplied by the owning simulation service before any mutation.
        // Denied access, invalid requests and insufficient capacity leave BOTH stores intact.
        public static bool TryTransfer(ItemInventory source, ItemInventory target, string itemId,
            int amount, bool accessGranted, out string reason)
        {
            reason = null;
            if (!accessGranted) reason = "Access denied";
            else if (source == null || target == null) reason = "Missing inventory";
            else if (ReferenceEquals(source, target)) reason = "Inventories must differ";
            else if (!ReferenceEquals(source.Catalog, target.Catalog)) reason = "Catalogs must match";
            else if (!source.Catalog.TryGet(itemId, out _)) reason = "Unknown item";
            else if (amount <= 0) reason = "Quantity must be positive";
            else if (source.Quantity(itemId) < amount) reason = "Insufficient stock";
            else if (!target.CanAdd(itemId, amount, out reason)) return false;
            if (reason != null) return false;
            source.SetQuantity(itemId, source.Quantity(itemId) - amount);
            target.SetQuantity(itemId, target.Quantity(itemId) + amount);
            return true;
        }

        private static long Slots(int quantity, int stack) => ((long)quantity + stack - 1) / stack;

        private sealed class ReadOnlyView : IReadOnlyItemInventory
        {
            private readonly ItemInventory store;
            public ReadOnlyView(ItemInventory store) { this.store = store; }
            public ItemCatalog Catalog => store.Catalog;
            public IReadOnlyDictionary<string, int> Quantities => store.Quantities;
            public long Capacity => store.Capacity;
            public int SlotCapacity => store.SlotCapacity;
            public long TotalQuantity => store.TotalQuantity;
            public long UsedSlots => store.UsedSlots;
            public int Quantity(string itemId) => store.Quantity(itemId);
        }
    }
}
