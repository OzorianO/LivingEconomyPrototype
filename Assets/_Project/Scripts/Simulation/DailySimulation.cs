using System;
using System.Collections.Generic;

namespace LivingEconomy.Simulation
{
    public sealed class DailySimulation
    {
        public Economy Economy { get; }
        public Resident Farm { get; }
        public Resident Bakery { get; }
        public IReadOnlyDictionary<string, string> Jobs { get; }
        public IReadOnlyList<BusinessDefinition> Businesses { get; }
        private readonly Dictionary<string, string> jobs = new Dictionary<string, string>();
        public int LastFed { get; private set; }
        public int LastPaid { get; private set; }
        public int LastBread { get; private set; }
        private readonly int farmYield;
        private readonly long reserve;

        public DailySimulation(int seed = 42, int farmYield = 2, long capital = 120, IReadOnlyList<BusinessDefinition> businesses = null)
        {
            if (farmYield < 0 || farmYield > 100 || capital < 0 || capital > 200) throw new ArgumentOutOfRangeException();
            this.farmYield = farmYield; reserve = capital;
            var definitions = new List<BusinessDefinition>(businesses ?? EmploymentScenario.Businesses());
            if (definitions.Count != 2 || definitions[0] == null || definitions[1] == null
                || definitions[0].Id != "farm" || definitions[1].Id != "bakery" || definitions[0].Owner == definitions[1].Owner)
                throw new ArgumentException("This scenario requires one farm and one bakery with distinct owners.");
            Businesses = definitions.AsReadOnly();
            var source = PrototypeScenario.Create(seed);
            foreach (var business in Businesses)
            {
                bool found = false;
                foreach (var npc in source.Residents) if (npc.Id == business.Owner) found = true;
                if (!found) throw new ArgumentException("Unknown business owner.");
            }
            var initial = new List<Resident>();
            foreach (var npc in source.Residents)
                initial.Add(new Resident(npc.Id, npc.Name, npc.Profession,
                    IsOwner(npc.Id) ? 200 : npc.Money, 0, 0));
            Economy = new Economy(initial);
            Farm = Economy.AddBusiness("farm", "Farm");
            Bakery = Economy.AddBusiness("bakery", "Bakery");
            if (capital > 0)
            {
                Economy.Transfer(Businesses[0].Owner, Farm.Id, capital, "Owner investment");
                Economy.Transfer(Businesses[1].Owner, Bakery.Id, capital, "Owner investment");
            }

            foreach (var npc in Economy.Residents)
                if (!IsOwner(npc.Id))
                    foreach (var business in Businesses)
                        if (EmployeeCount(business.Id) < business.Capacity) { jobs.Add(npc.Id, business.Id); break; }
            Jobs = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(jobs);
        }

        public BusinessDefinition Business(string id)
        {
            foreach (var b in Businesses) if (b.Id == id) return b;
            return null;
        }
        public bool IsOwner(string id)
        {
            foreach (var b in Businesses) if (b.Owner == id) return true;
            return false;
        }
        public string EmployerOf(string id)
        {
            foreach (var b in Businesses) if (b.Owner == id) return b.Id;
            return jobs.TryGetValue(id, out var employer) ? employer : null;
        }
        public int EmployeeCount(string business)
        {
            int count = 0;
            foreach (var job in jobs) if (job.Value == business) count++;
            return count;
        }
        // A failed switch preserves the previous position. null means resignation.
        public bool AssignJob(string resident, string employer, out string reason)
        {
            reason = null;
            if (DayInProgress) reason = "Wait until the day finishes.";
            else if (FindResident(resident) == null) reason = "Unknown resident.";
            else if (IsOwner(resident)) reason = "Business owners already have work.";
            else if (employer != null && Business(employer) == null) reason = "Unknown business.";
            else if (employer != null && EmployerOf(resident) != employer && EmployeeCount(employer) >= Business(employer).Capacity)
                reason = "No vacancy.";
            if (reason != null) return false;
            if (employer == null) jobs.Remove(resident);
            else jobs[resident] = employer;
            return true;
        }

        private readonly HashSet<string> worked = new HashSet<string>();
        private readonly HashSet<string> shopped = new HashSet<string>();
        private readonly HashSet<string> ate = new HashSet<string>();
        private int bakingCapacity;
        private bool workFinished;
        public bool DayInProgress { get; private set; }

        public void BeginDay()
        {
            if (DayInProgress) throw new InvalidOperationException("Finish the current day first.");
            Economy.AdvanceTick(); LastPaid = 0; LastFed = 0; LastBread = 0;
            worked.Clear(); shopped.Clear(); ate.Clear(); bakingCapacity = 0;
            workFinished = false; DayInProgress = true;
        }

        private Resident FindResident(string id)
        {
            foreach (var npc in Economy.Residents) if (npc.Id == id) return npc;
            return null;
        }

        public bool ArriveAtWork(string id)
        {
            if (!DayInProgress || workFinished || FindResident(id) == null || !worked.Add(id)) return false;
            bool owner = IsOwner(id);
            string employer = EmployerOf(id);
            if (employer == null) return false; // Unemployment counts as a completed work attempt.
            if (!owner)
            {
                if (Business(employer).Wage > 0 && !Economy.Transfer(employer, id, Business(employer).Wage, "Daily wage on work arrival").Success) return false;
                if (Business(employer).Wage > 0) LastPaid++;
            }
            if (employer == Farm.Id)
            {
                if (farmYield > 0) Economy.Produce(Farm.Id, Good.Grain, farmYield);
            }
            else bakingCapacity += 2;
            return true;
        }

        public bool FinishWork()
        {
            if (!DayInProgress || workFinished || worked.Count != Economy.Residents.Count) return false;
            int purchase = (int)Math.Min(bakingCapacity, Math.Min(Farm.Stock(Good.Grain), Bakery.Money / 3));
            if (purchase > 0) Economy.Buy(Bakery.Id, Farm.Id, Good.Grain, purchase, 3);
            int bread = Math.Min(bakingCapacity, Bakery.Stock(Good.Grain));
            if (bread > 0 && Economy.Produce(Bakery.Id, Good.Bread, bread, Good.Grain).Success) LastBread = bread;
            workFinished = true;
            return true;
        }

        public bool ArriveAtBakery(string id)
        {
            var npc = FindResident(id);
            if (!DayInProgress || !workFinished || npc == null || !shopped.Add(id)) return false;
            return npc.Stock(Good.Bread) > 0 || Economy.Buy(id, Bakery.Id, Good.Bread, 1, 6).Success;
        }

        public bool ArriveAtHome(string id)
        {
            var npc = FindResident(id);
            if (!DayInProgress || npc == null || !shopped.Contains(id) || !ate.Add(id)) return false;
            bool fed = Economy.Eat(npc);
            if (fed) LastFed++;
            return fed;
        }

        public bool FinishDay()
        {
            if (!DayInProgress || ate.Count != Economy.Residents.Count) return false;
            DrawProfit(Farm, Businesses[0].Owner); DrawProfit(Bakery, Businesses[1].Owner);
            DayInProgress = false;
            return true;
        }

        public void Step()
        {
            BeginDay();
            foreach (var npc in Economy.Residents) ArriveAtWork(npc.Id);
            FinishWork();
            // Fast mode uses rotated arrival priority; animated mode uses actual arrivals.
            for (int i = 0; i < Economy.Residents.Count; i++)
                ArriveAtBakery(Economy.Residents[(i + (int)(Economy.Tick % Economy.Residents.Count)) % Economy.Residents.Count].Id);
            foreach (var npc in Economy.Residents) ArriveAtHome(npc.Id);
            FinishDay();
        }
        private void DrawProfit(Resident business, string owner)
        {
            long amount = Math.Min(6, Math.Max(0, business.Money - reserve));
            if (amount > 0) Economy.Transfer(business.Id, owner, amount, "Owner profit above working reserve");
        }

        public SaveData Capture()
        {
            if (DayInProgress) throw new InvalidOperationException("Save after the day has finished.");
            var data = new SaveData { Tick = Economy.Tick, InitialMoney = Economy.InitialMoney, Reserve = reserve,
                FarmYield = farmYield, LastPaid = LastPaid, LastFed = LastFed, LastBread = LastBread };
            var accounts = new List<Resident>(Economy.Residents); accounts.Add(Farm); accounts.Add(Bakery);
            foreach (var a in accounts)
                data.Accounts.Add(new SavedAccount { Id = a.Id, Name = a.Name, Profession = (int)a.Profession,
                    Money = a.Money, Grain = a.Stock(Good.Grain), Bread = a.Stock(Good.Bread), Hunger = a.Hunger });
            data.Version = 2;
            foreach (var b in Businesses) data.Businesses.Add(new SavedBusiness { Id = b.Id, Owner = b.Owner, Capacity = b.Capacity, Wage = b.Wage });
            foreach (var job in Jobs) data.Jobs.Add(new SavedJob { Resident = job.Key, Employer = job.Value });
            foreach (var e in Economy.Ledger)
                data.Ledger.Add(new SavedEntry { Sequence = e.Sequence, Tick = e.Tick, Kind = e.Kind, From = e.From,
                    To = e.To, Good = e.Good.HasValue ? (int)e.Good.Value : -1, Quantity = e.Quantity,
                    Amount = e.Amount, Success = e.Success, Reason = e.Reason });
            return data;
        }

        public static DailySimulation FromSave(SaveData data)
        {
            if (data == null || (data.Version != 1 && data.Version != 2) || data.Jobs == null || data.LastPaid < 0 || data.LastPaid > 18
                || data.LastFed < 0 || data.LastFed > 20 || data.LastBread < 0 || data.LastBread > 40)
                throw new ArgumentException("Unsupported or invalid save.");
            List<BusinessDefinition> definitions = null;
            if (data.Version == 2)
            {
                if (data.Businesses == null) throw new ArgumentException("Missing businesses.");
                definitions = new List<BusinessDefinition>();
                foreach (var b in data.Businesses)
                {
                    if (b == null) throw new ArgumentException("Invalid business.");
                    definitions.Add(new BusinessDefinition(b.Id, b.Owner, b.Capacity, b.Wage));
                }
            }
            var restored = new DailySimulation(farmYield: data.FarmYield, capital: data.Reserve, businesses: definitions);
            var seen = new HashSet<string>();
            restored.jobs.Clear();
            foreach (var job in data.Jobs)
                if (job == null || job.Resident == null || !seen.Add(job.Resident)
                    || !restored.AssignJob(job.Resident, job.Employer, out _))
                    throw new ArgumentException("Invalid saved job assignment.");
            // v1 business account names included the fixed owner; migrate only those exact names.
            if (data.Version == 1)
                foreach (var a in data.Accounts)
                    if (a != null && (a.Id == "farm" || a.Id == "bakery"))
                    {
                        string expected = a.Id == "farm" ? "Farm (owner npc-00)" : "Bakery (owner npc-06)";
                        if (a.Name != expected && a.Name != (a.Id == "farm" ? "Farm" : "Bakery"))
                            throw new ArgumentException("Invalid legacy business account.");
                        a.Name = a.Id == "farm" ? "Farm" : "Bakery";
                    }
            restored.Economy.Restore(data);
            restored.LastPaid = data.LastPaid; restored.LastFed = data.LastFed; restored.LastBread = data.LastBread;
            return restored;
        }
    }
}
