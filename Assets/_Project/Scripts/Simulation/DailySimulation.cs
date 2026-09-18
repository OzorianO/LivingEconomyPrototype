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

        public void Step()
        {
            Economy.AdvanceTick(); LastPaid = 0; LastFed = 0; LastBread = 0;
            // Owners work themselves. Employees work only after an actual funded wage.
            int farmers = 1, bakers = 1;
            foreach (var job in Jobs)
                if (Economy.Transfer(job.Value, job.Key, 6, "Daily wage").Success)
                {
                    LastPaid++;
                    if (job.Value == Farm.Id) farmers++; else bakers++;
                }
            int grain = farmers * farmYield;
            if (grain > 0) Economy.Produce(Farm.Id, Good.Grain, grain);
            int needed = bakers * 2;
            int purchase = (int)Math.Min(needed, Math.Min(Farm.Stock(Good.Grain), Bakery.Money / 3));
            if (purchase > 0) Economy.Buy(Bakery.Id, Farm.Id, Good.Grain, purchase, 3);
            int bread = Math.Min(needed, Bakery.Stock(Good.Grain));
            if (bread > 0 && Economy.Produce(Bakery.Id, Good.Bread, bread, Good.Grain).Success) LastBread = bread;
            // Rotate purchase priority so scarce food is not always reserved for the same IDs.
            for (int i = 0; i < Economy.Residents.Count; i++)
            {
                var npc = Economy.Residents[(i + (int)(Economy.Tick % Economy.Residents.Count)) % Economy.Residents.Count];
                if (npc.Stock(Good.Bread) == 0) Economy.Buy(npc.Id, Bakery.Id, Good.Bread, 1, 6);
                if (Economy.Eat(npc)) LastFed++;
            }
            DrawProfit(Farm, "npc-00"); DrawProfit(Bakery, "npc-06");
        }

        private void DrawProfit(Resident business, string owner)
        {
            long amount = Math.Min(6, Math.Max(0, business.Money - reserve));
            if (amount > 0) Economy.Transfer(business.Id, owner, amount, "Owner profit above working reserve");
        }
    }
}
