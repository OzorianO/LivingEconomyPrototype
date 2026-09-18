using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LivingEconomy.Simulation
{
    public enum Profession { Farmer, Baker, Trader, None }
    public enum Good { Grain, Bread }

    public sealed class Resident
    {
        private readonly Dictionary<Good, int> inventory = new Dictionary<Good, int>();
        public string Id { get; }
        public string Name { get; }
        public Profession Profession { get; }
        public long Money { get; internal set; }
        public int Hunger { get; internal set; }
        public IReadOnlyDictionary<Good, int> Inventory { get; }

        public Resident(string id, string name, Profession profession, long money, int grain, int bread)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A resident needs an ID and name.");
            if (money < 0 || grain < 0 || bread < 0) throw new ArgumentOutOfRangeException();
            if (!Enum.IsDefined(typeof(Profession), profession)) throw new ArgumentException("Unknown profession.");
            Id = id; Name = name; Profession = profession; Money = money;
            inventory[Good.Grain] = grain; inventory[Good.Bread] = bread;
            Inventory = new ReadOnlyDictionary<Good, int>(inventory);
        }

        public int Stock(Good good) => inventory.TryGetValue(good, out var amount) ? amount : 0;
        internal void SetStock(Good good, int amount) => inventory[good] = amount;
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
                    source.Stock(Good.Grain), source.Stock(Good.Bread));
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
            byId.Add(id, account);
            Record("Business", "scenario", id, null, 0, 0, true, "Separate business account");
            return account;
        }

        public LedgerEntry Produce(string id, Good output, int quantity, Good? input = null)
        {
            Resident account;
            string error = id == null || !byId.TryGetValue(id, out account) ? "Unknown participant" : null;
            account = error == null ? byId[id] : null;
            if (error == null && (!Enum.IsDefined(typeof(Good), output) || quantity <= 0
                || (input.HasValue && (!Enum.IsDefined(typeof(Good), input.Value) || input == output)))) error = "Invalid recipe";
            if (error == null && input.HasValue && account.Stock(input.Value) < quantity) error = "Insufficient ingredients";
            int stock = 0;
            if (error == null)
                try { stock = checked(account.Stock(output) + quantity); }
                catch (OverflowException) { error = "Stock overflow"; }
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
            bool fed = resident.Stock(Good.Bread) > 0;
            if (fed) resident.SetStock(Good.Bread, resident.Stock(Good.Bread) - 1);
            resident.Hunger = fed ? Math.Max(0, resident.Hunger - 25) : Math.Min(100, resident.Hunger + 25);
            Record("Meal", resident.Id, "consumption", Good.Bread, fed ? 1 : 0, 0, fed, fed ? "Bread eaten" : "No food");
            return fed;
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
            if (error != null) return Record("Purchase", buyerId, sellerId, good, quantity, amount, false, error);
            buyer.Money -= amount; seller.Money = sellerBalance;
            seller.SetStock(good, seller.Stock(good) - quantity); buyer.SetStock(good, buyerStock);
            return Record("Purchase", buyerId, sellerId, good, quantity, amount, true, "Goods transferred seller to buyer");
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
            foreach (var saved in data.Accounts)
            {
                if (saved == null || saved.Id == null || !seen.Add(saved.Id) || !byId.TryGetValue(saved.Id, out var account)
                    || saved.Name != account.Name || saved.Profession != (int)account.Profession
                    || saved.Money < 0 || saved.Grain < 0 || saved.Bread < 0 || saved.Hunger < 0 || saved.Hunger > 100)
                    throw new ArgumentException("Invalid saved account.");
                total = checked(total + saved.Money);
            }
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
                var account = byId[saved.Id]; account.Money = saved.Money; account.Hunger = saved.Hunger;
                account.SetStock(Good.Grain, saved.Grain); account.SetStock(Good.Bread, saved.Bread);
            }
            ledger.Clear();
            foreach (var e in data.Ledger)
                ledger.Add(new LedgerEntry(e.Sequence, e.Tick, e.Kind, e.From, e.To,
                    e.Good == -1 ? (Good?)null : (Good)e.Good, e.Quantity, e.Amount, e.Success, e.Reason));
            Tick = data.Tick; InitialMoney = data.InitialMoney;
        }

        private string ValidateParties(string from, string to, out Resident payer, out Resident receiver)
        {
            payer = null; receiver = null;
            if (from == null || to == null || !byId.TryGetValue(from, out payer) || !byId.TryGetValue(to, out receiver))
                return "Unknown participant";
            return from == to ? "Participants must differ" : null;
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
