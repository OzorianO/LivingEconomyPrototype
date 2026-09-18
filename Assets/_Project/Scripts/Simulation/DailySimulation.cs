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
        public int LastFed { get; private set; }
        public int LastPaid { get; private set; }
        public int LastBread { get; private set; }
        private readonly int farmYield;
        private readonly long reserve;

        public DailySimulation(int seed = 42, int farmYield = 2, long capital = 120)
        {
            if (farmYield < 0 || farmYield > 100 || capital < 0 || capital > 200) throw new ArgumentOutOfRangeException();
            this.farmYield = farmYield; reserve = capital;
            var source = PrototypeScenario.Create(seed);
            var initial = new List<Resident>();
            foreach (var npc in source.Residents)
                initial.Add(new Resident(npc.Id, npc.Name, npc.Profession,
                    npc.Id == "npc-00" || npc.Id == "npc-06" ? 200 : npc.Money, 0, 0));
            Economy = new Economy(initial);
            Farm = Economy.AddBusiness("farm", "Farm (owner npc-00)");
            Bakery = Economy.AddBusiness("bakery", "Bakery (owner npc-06)");
            if (capital > 0)
            {
                Economy.Transfer("npc-00", Farm.Id, capital, "Owner investment");
                Economy.Transfer("npc-06", Bakery.Id, capital, "Owner investment");
            }
            var jobs = new Dictionary<string, string>();
            foreach (var npc in Economy.Residents)
                if (npc.Id != "npc-00" && npc.Id != "npc-06") jobs.Add(npc.Id, jobs.Count < 9 ? Farm.Id : Bakery.Id);
            Jobs = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(jobs);
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
            bool owner = id == "npc-00" || id == "npc-06";
            string employer = owner ? (id == "npc-00" ? Farm.Id : Bakery.Id) : Jobs[id];
            if (!owner)
            {
                if (!Economy.Transfer(employer, id, 6, "Daily wage on work arrival").Success) return false;
                LastPaid++;
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
            DrawProfit(Farm, "npc-00"); DrawProfit(Bakery, "npc-06");
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
            foreach (var job in Jobs) data.Jobs.Add(new SavedJob { Resident = job.Key, Employer = job.Value });
            foreach (var e in Economy.Ledger)
                data.Ledger.Add(new SavedEntry { Sequence = e.Sequence, Tick = e.Tick, Kind = e.Kind, From = e.From,
                    To = e.To, Good = e.Good.HasValue ? (int)e.Good.Value : -1, Quantity = e.Quantity,
                    Amount = e.Amount, Success = e.Success, Reason = e.Reason });
            return data;
        }

        public static DailySimulation FromSave(SaveData data)
        {
            if (data == null || data.Version != 1 || data.Jobs == null || data.LastPaid < 0 || data.LastPaid > 18
                || data.LastFed < 0 || data.LastFed > 20 || data.LastBread < 0 || data.LastBread > 20)
                throw new ArgumentException("Unsupported or invalid save.");
            var restored = new DailySimulation(farmYield: data.FarmYield, capital: data.Reserve);
            var seen = new HashSet<string>();
            if (data.Jobs.Count != restored.Jobs.Count) throw new ArgumentException("Invalid saved jobs.");
            foreach (var job in data.Jobs)
                if (job == null || job.Resident == null || !seen.Add(job.Resident)
                    || !restored.Jobs.TryGetValue(job.Resident, out var employer) || employer != job.Employer)
                    throw new ArgumentException("Invalid saved job assignment.");
            restored.Economy.Restore(data);
            restored.LastPaid = data.LastPaid; restored.LastFed = data.LastFed; restored.LastBread = data.LastBread;
            return restored;
        }
    }
}

