using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections;

namespace LivingEconomy.Simulation
{
    public enum Profession { Farmer, Baker, Trader, None }
    public enum Good { Grain, Bread }

    public sealed class Resident
    {
        internal ItemInventory Store { get; }
        public IReadOnlyItemInventory Items => Store.ReadOnly;
        public string Id { get; }
        public string Name { get; }
        public Profession Profession { get; }
        public long Money { get; internal set; }
        public int Hunger { get; internal set; }
        public int Thirst { get; internal set; }
        public bool IsDead { get; internal set; }
        public bool Suspended { get; internal set; }
        public bool IsAgent { get; internal set; } = true;
        public bool CanOperate => !IsDead && !Suspended;
        internal SavedPoint deathPoint;
        public SavedPoint DeathPoint => deathPoint?.Copy();
        public IReadOnlyDictionary<Good, int> Inventory { get; }

        public Resident(string id, string name, Profession profession, long money, int grain, int bread)
            : this(id, name, profession, money, grain, bread, long.MaxValue, int.MaxValue) { }

        internal Resident(string id, string name, Profession profession, long money, int grain, int bread,
            long capacity, int slots)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A resident needs an ID and name.");
            if (money < 0 || grain < 0 || bread < 0) throw new ArgumentOutOfRangeException();
            if (!Enum.IsDefined(typeof(Profession), profession)) throw new ArgumentException("Unknown profession.");
            Id = id; Name = name; Profession = profession; Money = money;
            Store = new ItemInventory(ItemCatalog.Prototype, capacity, slots);
            Store.SetQuantity(ItemCatalog.GrainId, grain); Store.SetQuantity(ItemCatalog.BreadId, bread);
            Inventory = new LegacyStockView(Store);
        }

        public int Stock(Good good) => Enum.IsDefined(typeof(Good), good) ? Store.Quantity(ItemCatalog.IdFor(good)) : 0;
        internal void SetStock(Good good, int amount) => Store.SetQuantity(ItemCatalog.IdFor(good), amount);

        // Compatibility projection, not a second copy of stock. Existing UI and XML v3
        // keep reading Grain/Bread while the authoritative storage uses stable item IDs.
        private sealed class LegacyStockView : IReadOnlyDictionary<Good, int>
        {
            private readonly ItemInventory store;
            public LegacyStockView(ItemInventory store) { this.store = store; }
            public int Count => 2;
            public IEnumerable<Good> Keys { get { yield return Good.Grain; yield return Good.Bread; } }
            public IEnumerable<int> Values { get { foreach (var key in Keys) yield return this[key]; } }
            public int this[Good key] => store.Quantity(ItemCatalog.IdFor(key));
            public bool ContainsKey(Good key) => Enum.IsDefined(typeof(Good), key);
            public bool TryGetValue(Good key, out int value)
            {
                value = ContainsKey(key) ? this[key] : 0; return ContainsKey(key);
            }
            public IEnumerator<KeyValuePair<Good, int>> GetEnumerator()
            {
                foreach (var key in Keys) yield return new KeyValuePair<Good, int>(key, this[key]);
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    public sealed class LedgerEntry
    {
        public long Sequence { get; }
        public long Tick { get; }
        public string Kind { get; }
        public string From { get; }
        public string To { get; }
        public Good? Good { get; }
        public int Quantity { get; }
        public long Amount { get; }
        public bool Success { get; }
        public string Reason { get; }
        internal LedgerEntry(long sequence, long tick, string kind, string from, string to,
            Good? good, int quantity, long amount, bool success, string reason)
        {
            Sequence = sequence; Tick = tick; Kind = kind; From = from; To = to;
            Good = good; Quantity = quantity; Amount = amount; Success = success; Reason = reason;
        }
        public override string ToString() => $"#{Sequence} tick={Tick} {Kind} {From}->{To} "
            + $"{Good} x{Quantity} amount={Amount} {(Success ? "OK" : "REFUSED")} {Reason}";
    }

    // All mutations happen synchronously on the simulation thread.
    public sealed class Economy
    {
        private readonly Dictionary<string, Resident> byId = new Dictionary<string, Resident>();
        private readonly List<Resident> residents = new List<Resident>();
        private readonly List<LedgerEntry> ledger = new List<LedgerEntry>();
        public IReadOnlyList<Resident> Residents { get; }
        public IReadOnlyList<LedgerEntry> Ledger { get; }
        public long InitialMoney { get; private set; }
        public long Tick { get; private set; }

        public Economy(IEnumerable<Resident> initialResidents)
        {
            if (initialResidents == null) throw new ArgumentNullException(nameof(initialResidents));
            long total = 0;
            foreach (var source in initialResidents)
            {
                if (source == null || byId.ContainsKey(source.Id)) throw new ArgumentException("Duplicate or missing resident.");
                // Copy input so another economy cannot mutate our accounts.
                var resident = new Resident(source.Id, source.Name, source.Profession, source.Money,
                    source.Stock(Good.Grain), source.Stock(Good.Bread), source.Items.Capacity, source.Items.SlotCapacity);
                total = checked(total + resident.Money);
                byId.Add(resident.Id, resident); residents.Add(resident);
                Record("Initial", "scenario", resident.Id, null, 0, resident.Money, true, "Starting wallet");
                foreach (Good good in Enum.GetValues(typeof(Good)))
                    Record("InitialStock", "scenario", resident.Id, good, resident.Stock(good), 0, true, "Starting inventory");
            }
            InitialMoney = total;
            Residents = residents.AsReadOnly(); Ledger = ledger.AsReadOnly();
        }

        public void AdvanceTick() => Tick = checked(Tick + 1);

        public Resident AddBusiness(string id, string name)
        {
            var account = new Resident(id, name, Profession.None, 0, 0, 0);
            account.IsAgent = false;
            byId.Add(id, account);
            Record("Business", "scenario", id, null, 0, 0, true, "Separate business account");
            return account;
        }

        internal Resident AddPlayer()
        {
            var player = new Resident("hero", "Hero", Profession.None, 0, 0, 0, 40, 4);
            byId.Add(player.Id, player);
            Record("Player", "scenario", player.Id, null, 0, 0, true, "Zero starting wallet; no money created");
            return player;
        }

        internal LedgerEntry RefuseAction(string actor, string reason)
            => Record("Action", actor, actor, null, 0, 0, false, reason);

        internal LedgerEntry KillAgent(Resident agent, SavedPoint position)
        {
            if (agent == null || !agent.IsAgent || !byId.TryGetValue(agent.Id, out var owned) || !ReferenceEquals(agent, owned))
                return RefuseAction(agent?.Id, "Unknown agent");
            if (agent.IsDead) return RefuseAction(agent.Id, "Agent already dead");
            if (position == null) return RefuseAction(agent.Id, "Missing death position");
            try { position.Validate(); }
            catch (ArgumentException) { return RefuseAction(agent.Id, "Invalid death position"); }
            agent.IsDead = true; agent.deathPoint = position.Copy();
            return Record("Death", agent.Id, agent.Id, null, 0, 0, true, "Personal wallet and items remain on body");
        }

        // Single-threaded, preflight-first: items AND coins succeed together or neither changes.
        public LedgerEntry Loot(string actorId, string corpseId, Good? good, int quantity, bool takeMoney, bool accessGranted)
        {
            var error = ValidateParties(actorId, corpseId, out var actor, out var corpse, false);
            if (error == null && !accessGranted) error = "Access denied";
            if (error == null && (!actor.IsAgent || !actor.CanOperate)) error = "Looter must be a living agent";
            if (error == null && (!corpse.IsAgent || !corpse.IsDead)) error = "Target is not a corpse";
            if (error == null && (good.HasValue ? !Enum.IsDefined(typeof(Good), good.Value) || quantity <= 0 : quantity != 0))
                error = "Invalid loot quantity or item";
            if (error == null && good.HasValue && corpse.Stock(good.Value) < quantity) error = "Insufficient stock";
            if (error == null && good.HasValue) actor.Store.CanAdd(ItemCatalog.IdFor(good.Value), quantity, out error);
            long amount = error == null && takeMoney ? corpse.Money : 0;
            if (error == null && !good.HasValue && amount == 0) error = "Nothing to loot";
            long balance = 0;
            if (error == null)
                try { balance = checked(actor.Money + amount); }
                catch (OverflowException) { error = "Balance overflow"; }
            if (error != null) return Record("Loot", corpseId, actorId, good, quantity, amount, false, error);
            if (good.HasValue)
                ItemInventory.TryTransfer(corpse.Store, actor.Store, ItemCatalog.IdFor(good.Value), quantity, true, out _);
            corpse.Money -= amount; actor.Money = balance;
            return Record("Loot", corpseId, actorId, good, quantity, amount, true, "Existing personal property transferred from body");
        }

        public LedgerEntry ConsumeBread(string id)
        {
            if (id == null || !byId.TryGetValue(id, out var account))
                return RefuseAction(id, "Unknown participant");
            if (!account.IsAgent || !account.CanOperate) return RefuseAction(id, "Agent cannot act");
            if (account.Stock(Good.Bread) == 0)
                return Record("Meal", id, "consumption", Good.Bread, 0, 0, false, "No food");
            account.SetStock(Good.Bread, account.Stock(Good.Bread) - 1);
            account.Hunger = Math.Max(0, account.Hunger - 25);
            return Record("Meal", id, "consumption", Good.Bread, 1, 0, true, "Bread eaten");
        }

        internal void AdvanceNeeds(Resident account, int hunger, int thirst)
        {
            if (account == null || !byId.TryGetValue(account.Id, out var owned) || !ReferenceEquals(account, owned)
                || hunger < 0 || thirst < 0) throw new ArgumentException("Invalid needs update.");
            if (account.IsDead) return;
            account.Hunger = (int)Math.Min(100L, (long)account.Hunger + hunger);
            account.Thirst = (int)Math.Min(100L, (long)account.Thirst + thirst);
            Note("Needs", account.Id, account.Id, true, "Hunger and thirst advanced");
        }

        public LedgerEntry Produce(string id, Good output, int quantity, Good? input = null)
        {
            Resident account;
            string error = id == null || !byId.TryGetValue(id, out account) ? "Unknown participant" : null;
            account = error == null ? byId[id] : null;
            if (error == null && !account.CanOperate) error = "Participant inactive";
            if (error == null && (!Enum.IsDefined(typeof(Good), output) || quantity <= 0
                || (input.HasValue && (!Enum.IsDefined(typeof(Good), input.Value) || input == output)))) error = "Invalid recipe";
            if (error == null && input.HasValue && account.Stock(input.Value) < quantity) error = "Insufficient ingredients";
            int stock = 0;
            if (error == null)
                try { stock = checked(account.Stock(output) + quantity); }
                catch (OverflowException) { error = "Stock overflow"; }
            if (error == null && (account.Items.Capacity != long.MaxValue || account.Items.SlotCapacity != int.MaxValue))
            {
                var proposed = new ItemInventory(ItemCatalog.Prototype, account.Items.Capacity, account.Items.SlotCapacity);
                foreach (var item in account.Items.Quantities) proposed.TryAdd(item.Key, item.Value, out _);
                if (input.HasValue) proposed.TryRemove(ItemCatalog.IdFor(input.Value), quantity, out _);
                proposed.TryAdd(ItemCatalog.IdFor(output), quantity, out error);
            }
            if (error != null) return Record("Production", id, id, output, quantity, 0, false, error);
            if (input.HasValue)
            {
                account.SetStock(input.Value, account.Stock(input.Value) - quantity);
                Record("Ingredient", id, "production", input, quantity, 0, true, "Recipe input consumed");
            }
            account.SetStock(output, stock);
            return Record("Production", "production", id, output, quantity, 0, true, "Recipe output created");
        }

        public bool Eat(Resident resident)
        {
            if (resident == null || !residents.Contains(resident)) throw new ArgumentException("Unknown resident");
            if (resident.IsDead) return false;
            bool fed = ConsumeBread(resident.Id).Success;
            if (!fed) resident.Hunger = Math.Min(100, resident.Hunger + 25);
            return fed;
        }

        internal void Note(string kind, string resident, string target, bool success, string reason)
            => Record(kind, resident, target, null, 0, 0, success, reason);

        internal void MissMeal(Resident resident, string reason)
        {
            if (resident.IsDead) return;
            resident.Hunger = Math.Min(100, resident.Hunger + 25);
            Record("Meal", resident.Id, "consumption", Good.Bread, 0, 0, false, reason);
        }

        public LedgerEntry Transfer(string payerId, string receiverId, long amount, string reason)
        {
            var error = ValidateParties(payerId, receiverId, out var payer, out var receiver);
            if (error == null && amount <= 0) error = "Amount must be positive";
            if (error == null && string.IsNullOrWhiteSpace(reason)) error = "Reason required";
            if (error == null && payer.Money < amount) error = "Insufficient money";
            long balance = 0;
            if (error == null)
            {
                try { balance = checked(receiver.Money + amount); }
                catch (OverflowException) { error = "Balance overflow"; }
            }
            if (error != null) return Record("Transfer", payerId, receiverId, null, 0, amount, false, error);
            payer.Money -= amount; receiver.Money = balance;
            return Record("Transfer", payerId, receiverId, null, 0, amount, true, reason);
        }

        public LedgerEntry Buy(string buyerId, string sellerId, Good good, int quantity, long unitPrice)
        {
            var error = ValidateParties(buyerId, sellerId, out var buyer, out var seller);
            if (error == null && !Enum.IsDefined(typeof(Good), good)) error = "Unknown good";
            if (error == null && (quantity <= 0 || unitPrice <= 0)) error = "Quantity and price must be positive";
            long amount = 0, sellerBalance = 0;
            int buyerStock = 0;
            if (error == null)
            {
                try
                {
                    amount = checked(unitPrice * quantity);
                    sellerBalance = checked(seller.Money + amount);
                    buyerStock = checked(buyer.Stock(good) + quantity);
                }
                catch (OverflowException) { error = "Transaction overflow"; }
            }
            if (error == null && buyer.Money < amount) error = "Insufficient money";
            if (error == null && seller.Stock(good) < quantity) error = "Insufficient stock";
            if (error == null && !buyer.Store.CanAdd(ItemCatalog.IdFor(good), quantity, out error))
                return Record("Purchase", buyerId, sellerId, good, quantity, amount, false, error);
            if (error != null) return Record("Purchase", buyerId, sellerId, good, quantity, amount, false, error);
            buyer.Money -= amount; seller.Money = sellerBalance;
            seller.SetStock(good, seller.Stock(good) - quantity); buyer.SetStock(good, buyerStock);
            return Record("Purchase", buyerId, sellerId, good, quantity, amount, true, "Goods transferred seller to buyer");
        }

        // Trusted simulation command: the controller's ownership/permission check is
        // passed explicitly. It never moves money; purchases continue through Buy.
        public LedgerEntry TransferGoods(string from, string to, Good good, int quantity, bool accessGranted)
        {
            var error = ValidateParties(from, to, out var source, out var target);
            if (error == null && !Enum.IsDefined(typeof(Good), good)) error = "Unknown good";
            if (error == null)
                ItemInventory.TryTransfer(source.Store, target.Store, ItemCatalog.IdFor(good), quantity, accessGranted, out error);
            return Record("GoodsTransfer", from, to, good, quantity, 0, error == null,
                error ?? "Goods transferred without payment");
        }

        public long TotalMoney()
        {
            long total = 0;
            foreach (var resident in byId.Values) total = checked(total + resident.Money);
            return total;
        }

        internal void Restore(SaveData data)
        {
            if (data.Accounts == null || data.Accounts.Count != byId.Count || data.Ledger == null
                || data.Tick < 0 || data.InitialMoney < 0) throw new ArgumentException("Invalid saved economy.");
            var seen = new HashSet<string>();
            long total = 0;
            bool foundGenericItem = false;
            foreach (var saved in data.Accounts)
            {
                if (saved == null || saved.Id == null || !seen.Add(saved.Id) || !byId.TryGetValue(saved.Id, out var account)
                    || saved.Name != account.Name || saved.Profession != (int)account.Profession
                    || saved.Money < 0 || saved.Grain < 0 || saved.Bread < 0 || saved.Hunger < 0 || saved.Hunger > 100)
                    throw new ArgumentException("Invalid saved account.");
                if (saved.Thirst < 0 || saved.Thirst > 100)
                    throw new ArgumentException("Invalid saved needs or inventory.");
                if (saved.IsDead && (!account.IsAgent || saved.DeathPoint == null) || !saved.IsDead && saved.DeathPoint != null)
                    throw new ArgumentException("Invalid saved death state.");
                saved.DeathPoint?.Validate();
                // Validate the combined capacity/slots before restoring any account.
                var proposed = new ItemInventory(ItemCatalog.Prototype, account.Items.Capacity, account.Items.SlotCapacity);
                if (data.Version >= 7)
                {
                    if (saved.Items == null) throw new ArgumentException("Missing v7 inventory.");
                    var itemIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in saved.Items)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.Quantity <= 0 || !itemIds.Add(item.Id)
                            || !proposed.TryAdd(item.Id, item.Quantity, out _))
                            throw new ArgumentException("Invalid saved item inventory.");
                        if (item.Id != ItemCatalog.GrainId && item.Id != ItemCatalog.BreadId) foundGenericItem = true;
                    }
                    if (proposed.Quantity(ItemCatalog.GrainId) != saved.Grain || proposed.Quantity(ItemCatalog.BreadId) != saved.Bread)
                        throw new ArgumentException("Legacy stock fields do not match v7 inventory.");
                }
                else
                {
                    if (saved.Items != null && saved.Items.Count != 0) throw new ArgumentException("Generic inventory requires v7.");
                    if (saved.Grain > 0 && !proposed.TryAdd(ItemCatalog.GrainId, saved.Grain, out _)
                        || saved.Bread > 0 && !proposed.TryAdd(ItemCatalog.BreadId, saved.Bread, out _))
                        throw new ArgumentException("Invalid saved inventory capacity.");
                }
                total = checked(total + saved.Money);
            }
            if (data.Version >= 7 && !foundGenericItem) throw new ArgumentException("v7 requires a generic item.");
            if (total != data.InitialMoney) throw new ArgumentException("Saved money total does not match.");
            long previousTick = 0;
            for (int i = 0; i < data.Ledger.Count; i++)
            {
                var entry = data.Ledger[i];
                if (entry == null || entry.Sequence != i + 1L || entry.Tick < previousTick || entry.Tick > data.Tick
                    || string.IsNullOrWhiteSpace(entry.Kind) || string.IsNullOrWhiteSpace(entry.Reason)
                    || (entry.Success && (entry.Amount < 0 || entry.Quantity < 0)))
                    throw new ArgumentException("Invalid saved ledger.");
                previousTick = entry.Tick;
            }
            foreach (var saved in data.Accounts)
            {
                var account = byId[saved.Id]; account.Money = saved.Money; account.Hunger = saved.Hunger; account.Thirst = saved.Thirst;
                account.IsDead = saved.IsDead; account.deathPoint = saved.DeathPoint?.Copy();
                foreach (var id in new List<string>(account.Items.Quantities.Keys)) account.Store.SetQuantity(id, 0);
                if (data.Version >= 7)
                    foreach (var item in saved.Items) account.Store.SetQuantity(item.Id, item.Quantity);
                else
                {
                    account.SetStock(Good.Grain, saved.Grain); account.SetStock(Good.Bread, saved.Bread);
                }
            }
            ledger.Clear();
            foreach (var e in data.Ledger)
                ledger.Add(new LedgerEntry(e.Sequence, e.Tick, e.Kind, e.From, e.To,
                    e.Good == -1 ? (Good?)null : (Good)e.Good, e.Quantity, e.Amount, e.Success, e.Reason));
            Tick = data.Tick; InitialMoney = data.InitialMoney;
        }

        private string ValidateParties(string from, string to, out Resident payer, out Resident receiver, bool requireActive = true)
        {
            payer = null; receiver = null;
            if (from == null || to == null || !byId.TryGetValue(from, out payer) || !byId.TryGetValue(to, out receiver))
                return "Unknown participant";
            if (from == to) return "Participants must differ";
            return requireActive && (!payer.CanOperate || !receiver.CanOperate) ? "Participant inactive" : null;
        }

        private LedgerEntry Record(string kind, string from, string to, Good? good, int quantity,
            long amount, bool success, string reason)
        {
            var entry = new LedgerEntry(ledger.Count + 1L, Tick, kind, from, to, good, quantity, amount, success, reason);
            ledger.Add(entry); return entry;
        }
    }

    public static class PrototypeScenario
    {
        public static Economy Create(int seed = 42)
        {
            var random = new Random(seed);
            var residents = new List<Resident>();
            var names = new[] { "Марек", "Олена", "Томаш", "Анна", "Олег", "Ірина", "Данило", "Софія",
                "Петро", "Наталя", "Лев", "Марія", "Андрій", "Дарина", "Іван", "Оксана", "Роман", "Юлія", "Максим", "Катерина" };
            for (int i = 0; i < names.Length; i++)
            {
                var profession = i < 6 ? Profession.Farmer : i < 10 ? Profession.Baker : i < 12 ? Profession.Trader : Profession.None;
                residents.Add(new Resident($"npc-{i:00}", names[i], profession, random.Next(40, 121),
                    profession == Profession.Farmer ? 10 : 0, profession == Profession.Baker ? 10 : 3));
            }
            return new Economy(residents);
        }
    }
}
