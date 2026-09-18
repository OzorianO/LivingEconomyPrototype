using System;
using System.Collections.Generic;

namespace LivingEconomy.Simulation
{
    // Derived snapshot: no money, inventory, jobs or save state is mutated.
    public sealed class SettlementReport
    {
        public long Day { get; private set; }
        public bool DayInProgress { get; private set; }
        public int Residents { get; private set; }
        public int Employees { get; private set; }
        public int Owners { get; private set; }
        public int Unemployed { get; private set; }
        public int Vacancies { get; private set; }
        public int Hungry { get; private set; }
        public int SevereHunger { get; private set; }
        public int Unpaid { get; private set; }
        public int FailedRoutes { get; private set; }
        public int FoodStockRefusals { get; private set; }
        public int FoodMoneyRefusals { get; private set; }
        public int JobSearchRefusals { get; private set; }
        public long TotalMoney { get; private set; }
        public long InitialMoney { get; private set; }
        public long ResidentMoney { get; private set; }
        public long BusinessMoney { get; private set; }
        public long HeroMoney { get; private set; }
        public long MinimumWallet { get; private set; }
        public long MaximumWallet { get; private set; }
        public decimal MedianWallet { get; private set; }
        public decimal AverageWallet { get; private set; }
        public decimal TopFifthWalletShare { get; private set; }
        public string Poorest { get; private set; }
        public string Richest { get; private set; }
        public long Grain { get; private set; }
        public long Bread { get; private set; }
        public bool MoneyConserved => TotalMoney == InitialMoney && TotalMoney == ResidentMoney + BusinessMoney + HeroMoney;

        public static SettlementReport Capture(DailySimulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            var economy = simulation.Economy;
            var report = new SettlementReport {
                Day = economy.Tick, DayInProgress = simulation.DayInProgress,
                Residents = economy.Residents.Count, Employees = simulation.Jobs.Count,
                TotalMoney = economy.TotalMoney(), InitialMoney = economy.InitialMoney,
                Unpaid = simulation.LastUnpaid, FailedRoutes = simulation.LastUnreachable
            };
            var wallets = new List<Resident>(economy.Residents);
            foreach (var npc in wallets)
            {
                report.ResidentMoney = checked(report.ResidentMoney + npc.Money);
                report.Grain += npc.Stock(Good.Grain); report.Bread += npc.Stock(Good.Bread);
                if (simulation.IsOwner(npc.Id)) report.Owners++;
                else if (simulation.EmployerOf(npc.Id) == null) report.Unemployed++;
                if (npc.Hunger > 0) report.Hungry++;
                if (npc.Hunger >= 75) report.SevereHunger++;
            }
            foreach (var business in simulation.Businesses)
            {
                var account = business.Id == simulation.Farm.Id ? simulation.Farm : simulation.Bakery;
                report.BusinessMoney = checked(report.BusinessMoney + account.Money);
                report.Grain += account.Stock(Good.Grain); report.Bread += account.Stock(Good.Bread);
                report.Vacancies += Math.Max(0, business.Capacity - simulation.EmployeeCount(business.Id));
            }
            if (simulation.Hero != null)
            {
                report.HeroMoney = simulation.Hero.Money;
                report.Grain += simulation.Hero.Stock(Good.Grain); report.Bread += simulation.Hero.Stock(Good.Bread);
            }
            wallets.Sort((a, b) => a.Money != b.Money ? a.Money.CompareTo(b.Money) : string.CompareOrdinal(a.Id, b.Id));
            if (wallets.Count > 0)
            {
                report.MinimumWallet = wallets[0].Money; report.Poorest = wallets[0].Name;
                report.MaximumWallet = wallets[wallets.Count - 1].Money; report.Richest = wallets[wallets.Count - 1].Name;
                report.MedianWallet = wallets.Count % 2 == 1 ? wallets[wallets.Count / 2].Money
                    : wallets[wallets.Count / 2 - 1].Money / 2m + wallets[wallets.Count / 2].Money / 2m;
                report.AverageWallet = report.ResidentMoney / (decimal)wallets.Count;
                long richestMoney = 0;
                int topCount = (wallets.Count + 4) / 5;
                for (int i = wallets.Count - topCount; i < wallets.Count; i++) richestMoney = checked(richestMoney + wallets[i].Money);
                report.TopFifthWalletShare = report.ResidentMoney == 0 ? 0 : richestMoney * 100m / report.ResidentMoney;
            }
            // Count only this day's attempts; historical refusals must not inflate today's report.
            for (int i = economy.Ledger.Count - 1; i >= 0; i--)
            {
                var entry = economy.Ledger[i];
                if (entry.Tick < report.Day) break;
                if (entry.Success) continue;
                if (entry.Kind == "Purchase" && entry.Good == Good.Bread)
                {
                    if (entry.Reason == "Insufficient stock") report.FoodStockRefusals++;
                    else if (entry.Reason == "Insufficient money") report.FoodMoneyRefusals++;
                }
                if (entry.Kind == "JobDecision" && entry.To == null) report.JobSearchRefusals++;
            }
            return report;
        }
    }
}
