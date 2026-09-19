using System;
using System.Collections.Generic;

namespace LivingEconomy.Simulation
{
    public enum DayStage { Work, Shopping, Home }
    public enum AgentAction { BuyBread, ConsumeBread }

    public sealed class DailySimulation
    {
        public Economy Economy { get; }
        public Resident Farm { get; }
        public Resident Bakery { get; }
        public Resident Hero { get; private set; }
        private readonly List<ResourceNode> resourceNodes = new List<ResourceNode>();
        private readonly HashSet<string> workstationIds = new HashSet<string>(StringComparer.Ordinal) { RecipeCatalog.WorkbenchId };
        public IReadOnlyList<ResourceNode> ResourceNodes { get; }
        public bool HasWorkstation(string id) => id != null && workstationIds.Contains(id);
        public Resident Agent(string id) => id == null ? null : Hero?.Id == id ? Hero : FindResident(id);
        public bool BusinessActive(string id)
        {
            var business = Business(id);
            return business != null && Agent(business.Owner)?.IsDead == false;
        }
        public LedgerEntry KillAgent(string id, SavedPoint position)
        {
            if (DayInProgress) return Economy.RefuseAction(id, "Wait until the day finishes");
            if (Agent(id) == null) return Economy.RefuseAction(id, "Unknown agent");
            var result = Economy.KillAgent(Agent(id), position);
            if (result.Success) ApplyDeathPolicy();
            return result;
        }
        private void ApplyDeathPolicy()
        {
            Farm.Suspended = !BusinessActive(Farm.Id); Bakery.Suspended = !BusinessActive(Bakery.Id);
            foreach (var job in new Dictionary<string, string>(jobs))
                if (Agent(job.Key)?.IsDead == true || !BusinessActive(job.Value)) jobs.Remove(job.Key);
        }
        public LedgerEntry Loot(string actorId, string corpseId, Good? good, int quantity, bool takeMoney, bool accessGranted)
            => Economy.Loot(actorId, corpseId, good, quantity, takeMoney, accessGranted);
        public WorldActionResult ExecuteWorldAction(string actorId, WorldAction action, string targetId, string workstationId = null)
        {
            var actor = Agent(actorId);
            string error = actor == null ? "Unknown agent" : !actor.CanOperate ? "Agent cannot act"
                : DayInProgress ? "Wait until the day finishes" : null;
            if (error != null) return new WorldActionResult(false, error, actorId, targetId, null, 0);
            if (action == WorldAction.Harvest) return Harvest(actor, targetId);
            if (action == WorldAction.Craft) return Craft(actor, targetId, workstationId);
            return new WorldActionResult(false, "Unknown world action", actorId, targetId, null, 0);
        }

        private WorldActionResult Harvest(Resident actor, string nodeId)
        {
            var node = resourceNodes.Find(candidate => candidate.Id == nodeId);
            string error = node == null ? "Unknown resource node"
                : node.Available < node.HarvestAmount ? "Resource depleted"
                : node.RequiredToolId != null && actor.Items.Quantity(node.RequiredToolId) == 0 ? "Required tool missing" : null;
            if (error == null && !actor.Store.CanAdd(node.OutputItemId, node.HarvestAmount, out error)) { }
            if (error != null) return new WorldActionResult(false, error, actor.Id, nodeId, node?.OutputItemId, 0);
            node.Take(node.HarvestAmount); actor.Store.TryAdd(node.OutputItemId, node.HarvestAmount, out _);
            Economy.Note("Harvest", actor.Id, nodeId, true, node.OutputItemId + " collected");
            return new WorldActionResult(true, "Resource harvested", actor.Id, nodeId, node.OutputItemId, node.HarvestAmount);
        }

        private WorldActionResult Craft(Resident actor, string recipeId, string workstationId)
        {
            RecipeCatalog.TryGet(recipeId, out var recipe);
            string error = recipe == null ? "Unknown recipe"
                : workstationId != recipe.WorkstationId || !HasWorkstation(workstationId) ? "Required workstation missing"
                : actor.Items.Quantity(recipe.InputItemId) < recipe.InputQuantity ? "Required ingredients missing" : null;
            if (error == null)
            {
                var proposed = new ItemInventory(actor.Items.Catalog, actor.Items.Capacity, actor.Items.SlotCapacity);
                foreach (var item in actor.Items.Quantities)
                    if (!proposed.TryAdd(item.Key, item.Value, out error)) break;
                if (error == null && !proposed.TryRemove(recipe.InputItemId, recipe.InputQuantity, out error)) { }
                if (error == null && !proposed.TryAdd(recipe.OutputItemId, recipe.OutputQuantity, out error)) { }
            }
            if (error != null) return new WorldActionResult(false, error, actor.Id, recipeId, recipe?.OutputItemId, 0);
            actor.Store.TryRemove(recipe.InputItemId, recipe.InputQuantity, out _);
            actor.Store.TryAdd(recipe.OutputItemId, recipe.OutputQuantity, out _);
            Economy.Note("Craft", actor.Id, recipe.Id, true, recipe.OutputItemId + " crafted");
            return new WorldActionResult(true, "Recipe crafted", actor.Id, recipe.Id, recipe.OutputItemId, recipe.OutputQuantity);
        }
        private SavedHeroPose heroPose;
        public SavedHeroPose HeroPose => heroPose?.Copy();
        public void SetHeroPose(SavedHeroPose pose)
        {
            if (Hero == null || pose == null) throw new ArgumentException("Missing hero or pose.");
            pose.Validate(); heroPose = pose.Copy();
        }
        public Resident EnableHero()
        {
            if (Hero == null)
            {
                if (DayInProgress) throw new InvalidOperationException("Enable the hero between days.");
                Hero = Economy.AddPlayer();
            }
            return Hero;
        }

        // Controllers choose commands; the simulation owns identity, recipe/price and mutation.
        public LedgerEntry ExecuteAction(string actorId, AgentAction action)
        {
            if (actorId == null || (FindResident(actorId) == null && (Hero == null || Hero.Id != actorId)))
                return Economy.RefuseAction(actorId, "Unknown agent");
            if (Agent(actorId).IsDead) return Economy.RefuseAction(actorId, "Agent is dead");
            if (action == AgentAction.BuyBread) return Economy.Buy(actorId, Bakery.Id, Good.Bread, 1, 6);
            if (action == AgentAction.ConsumeBread) return Economy.ConsumeBread(actorId);
            return Economy.RefuseAction(actorId, "Unknown action");
        }

        public static DailySimulation HeroDemo()
        {
            var demo = new DailySimulation();
            demo.EnableHero();
            // Explicit developer fixture, not free money or a gameplay quest.
            demo.Economy.Transfer(demo.Businesses[1].Owner, demo.Hero.Id, 12, "Developer demo: owner gift");
            demo.Economy.Produce(demo.Farm.Id, Good.Grain, 2);
            demo.Economy.Buy(demo.Bakery.Id, demo.Farm.Id, Good.Grain, 2, 3);
            demo.Economy.Produce(demo.Bakery.Id, Good.Bread, 2, Good.Grain);
            demo.Economy.AdvanceNeeds(demo.Hero, 50, 20);
            return demo;
        }
        public IReadOnlyDictionary<string, string> Jobs { get; }
        public IReadOnlyList<BusinessDefinition> Businesses { get; }
        private readonly Dictionary<string, string> jobs = new Dictionary<string, string>();
        public int LastFed { get; private set; }
        public int LastPaid { get; private set; }
        public int LastBread { get; private set; }
        public int LastUnpaid { get; private set; }
        public int LastUnreachable { get; private set; }
        public bool AutoEmployment { get; }
        private readonly Dictionary<string, string> decisions = new Dictionary<string, string>();
        public string DecisionOf(string id) => decisions.TryGetValue(id, out var reason) ? reason : "No decision yet.";
        private readonly int farmYield;
        private readonly long reserve;

        public DailySimulation(int seed = 42, int farmYield = 2, long capital = 120, IReadOnlyList<BusinessDefinition> businesses = null, bool autoEmployment = true)
        {
            AutoEmployment = autoEmployment;
            ResourceNodes = resourceNodes.AsReadOnly();
            resourceNodes.Add(new ResourceNode("tree-01", ItemCatalog.LogId, 4, 1, ItemCatalog.AxeId));
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
            else if (Agent(resident).IsDead) reason = "Agent is dead.";
            else if (IsOwner(resident)) reason = "Business owners already have work.";
            else if (employer != null && Business(employer) == null) reason = "Unknown business.";
            else if (employer != null && !BusinessActive(employer)) reason = "Business suspended after owner death.";
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
        private readonly Dictionary<string, string> failedWork = new Dictionary<string, string>();
        private readonly Dictionary<string, string> failedShopping = new Dictionary<string, string>();
        private readonly Dictionary<string, string> failedHome = new Dictionary<string, string>();
        private int bakingCapacity;
        private bool workFinished;
        private bool shoppingFinished;
        public bool DayInProgress { get; private set; }

        // Daily rotating priority is shared by hiring, wages, shopping and meals.
        private IEnumerable<Resident> Priority(long tick)
        {
            int count = Economy.Residents.Count;
            for (int i = 0; i < count; i++)
                yield return Economy.Residents[(i + (int)(tick % count)) % count];
        }

        private void Decide(string id, string target, bool success, string reason)
        {
            decisions[id] = reason;
            Economy.Note("JobDecision", id, target, success, reason);
        }

        private void SeekJobs()
        {
            foreach (var npc in Priority(Economy.Tick))
            {
                if (npc.IsDead) continue;
                if (EmployerOf(npc.Id) != null) continue;
                BusinessDefinition best = null;
                bool vacancy = false;
                foreach (var business in Businesses)
                {
                    if (!BusinessActive(business.Id)) continue;
                    if (EmployeeCount(business.Id) >= business.Capacity) continue;
                    vacancy = true;
                    var account = business.Id == Farm.Id ? Farm : Bakery;
                    if (account.Money < business.Wage) continue;
                    if (best == null || business.Wage > best.Wage
                        || (business.Wage == best.Wage && string.CompareOrdinal(business.Id, best.Id) < 0)) best = business;
                }
                if (best == null)
                    Decide(npc.Id, null, false, vacancy ? "Vacancies exist, but employers cannot fund a wage. Retry tomorrow." : "No vacancy. Retry tomorrow.");
                else if (AssignJob(npc.Id, best.Id, out var reason))
                    Decide(npc.Id, best.Id, true, "Accepted " + best.Id + ": highest available funded wage; ties use business ID.");
                else Decide(npc.Id, best.Id, false, reason);
            }
        }

        public void BeginDay()
        {
            if (DayInProgress) throw new InvalidOperationException("Finish the current day first.");
            Economy.AdvanceTick(); LastPaid = 0; LastFed = 0; LastBread = 0;
            LastUnpaid = 0; LastUnreachable = 0;
            if (AutoEmployment) SeekJobs();
            worked.Clear(); shopped.Clear(); ate.Clear(); bakingCapacity = 0;
            failedWork.Clear(); failedShopping.Clear(); failedHome.Clear();
            // Dead agents resolve all phase bookkeeping without pretending to arrive.
            foreach (var npc in Economy.Residents)
                if (npc.IsDead) { worked.Add(npc.Id); shopped.Add(npc.Id); ate.Add(npc.Id); }
            workFinished = false; shoppingFinished = false; DayInProgress = true;
        }

        private Resident FindResident(string id)
        {
            foreach (var npc in Economy.Residents) if (npc.Id == id) return npc;
            return null;
        }

        public bool ArriveAtWork(string id)
        {
            return DayInProgress && !workFinished && FindResident(id) != null && !Agent(id).IsDead && worked.Add(id);
        }

        private void SettleWork(Resident npc)
        {
            if (npc.IsDead) return;
            string id = npc.Id;
            if (failedWork.TryGetValue(id, out var failure))
            {
                Economy.Note("Unreachable", id, EmployerOf(id), false, failure);
                return;
            }
            bool owner = IsOwner(id);
            string employer = EmployerOf(id);
            if (employer == null) return;
            if (!BusinessActive(employer)) return;
            if (!owner)
            {
                if (Business(employer).Wage > 0 && !Economy.Transfer(employer, id, Business(employer).Wage, "Daily wage after work arrivals").Success)
                {
                    LastUnpaid++;
                    Decide(id, employer, false, "Unpaid: employer lacks money. No production today; keep job and retry tomorrow.");
                    return;
                }
                if (Business(employer).Wage > 0) LastPaid++;
            }
            if (employer == Farm.Id)
            {
                if (farmYield > 0) Economy.Produce(Farm.Id, Good.Grain, farmYield);
            }
            else bakingCapacity += 2;
        }

        public bool FinishWork()
        {
            if (!DayInProgress || workFinished || worked.Count != Economy.Residents.Count) return false;
            foreach (var npc in Priority(Economy.Tick)) SettleWork(npc);
            int purchase = (int)Math.Min(bakingCapacity, Math.Min(Farm.Stock(Good.Grain), Bakery.Money / 3));
            if (purchase > 0) Economy.Buy(Bakery.Id, Farm.Id, Good.Grain, purchase, 3);
            int bread = Math.Min(bakingCapacity, Bakery.Stock(Good.Grain));
            if (bread > 0 && Economy.Produce(Bakery.Id, Good.Bread, bread, Good.Grain).Success) LastBread = bread;
            workFinished = true;
            return true;
        }

        public bool ArriveAtBakery(string id)
        {
            return DayInProgress && workFinished && !shoppingFinished && FindResident(id) != null && !Agent(id).IsDead && shopped.Add(id);
        }

        public bool FinishShopping()
        {
            if (!DayInProgress || !workFinished || shoppingFinished || shopped.Count != Economy.Residents.Count) return false;
            foreach (var npc in Priority(Economy.Tick))
            {
                if (npc.IsDead) continue;
                if (failedShopping.TryGetValue(npc.Id, out var failure))
                    Economy.Note("Unreachable", npc.Id, Bakery.Id, false, failure);
                else if (npc.Stock(Good.Bread) == 0) ExecuteAction(npc.Id, AgentAction.BuyBread);
            }
            shoppingFinished = true;
            return true;
        }

        public bool ArriveAtHome(string id)
        {
            var npc = FindResident(id);
            return DayInProgress && shoppingFinished && npc != null && !npc.IsDead && ate.Add(id);
        }

        // Failure resolves one attempt, without inventing an arrival or duplicating an action.
        public bool ReportUnreachable(string id, DayStage stage, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return false;
            bool accepted;
            Dictionary<string, string> failures;
            if (stage == DayStage.Work) { accepted = ArriveAtWork(id); failures = failedWork; }
            else if (stage == DayStage.Shopping) { accepted = ArriveAtBakery(id); failures = failedShopping; }
            else if (stage == DayStage.Home) { accepted = ArriveAtHome(id); failures = failedHome; }
            else return false;
            if (!accepted) return false;
            failures.Add(id, reason); LastUnreachable++;
            return true;
        }

        public bool FinishDay()
        {
            if (!DayInProgress || !shoppingFinished || ate.Count != Economy.Residents.Count) return false;
            foreach (var npc in Priority(Economy.Tick))
            {
                if (npc.IsDead) continue;
                if (failedHome.TryGetValue(npc.Id, out var failure)) Economy.MissMeal(npc, failure);
                else if (Economy.Eat(npc)) LastFed++;
            }
            DrawProfit(Farm, Businesses[0].Owner); DrawProfit(Bakery, Businesses[1].Owner);
            if (Hero != null) Economy.AdvanceNeeds(Hero, 10, 10);
            DayInProgress = false;
            return true;
        }

        public void Step()
        {
            BeginDay();
            foreach (var npc in Economy.Residents) ArriveAtWork(npc.Id);
            FinishWork();
            foreach (var npc in Economy.Residents) ArriveAtBakery(npc.Id);
            FinishShopping();
            foreach (var npc in Economy.Residents) ArriveAtHome(npc.Id);
            FinishDay();
        }
        private void DrawProfit(Resident business, string owner)
        {
            if (!BusinessActive(business.Id)) return;
            long amount = Math.Min(6, Math.Max(0, business.Money - reserve));
            if (amount > 0) Economy.Transfer(business.Id, owner, amount, "Owner profit above working reserve");
        }

        public SaveData Capture()
        {
            if (DayInProgress) throw new InvalidOperationException("Save after the day has finished.");
            var data = new SaveData { Tick = Economy.Tick, InitialMoney = Economy.InitialMoney, Reserve = reserve,
                FarmYield = farmYield, LastPaid = LastPaid, LastFed = LastFed, LastBread = LastBread };
            var accounts = new List<Resident>(Economy.Residents); accounts.Add(Farm); accounts.Add(Bakery);
            if (Hero != null) accounts.Add(Hero);
            bool hasGenericItems = false;
            foreach (var a in accounts)
            {
                var saved = new SavedAccount { Id = a.Id, Name = a.Name, Profession = (int)a.Profession,
                    Money = a.Money, Grain = a.Stock(Good.Grain), Bread = a.Stock(Good.Bread), Hunger = a.Hunger, Thirst = a.Thirst,
                    IsDead = a.IsDead, DeathPoint = a.DeathPoint };
                foreach (var item in a.Items.Quantities)
                    if (item.Key != ItemCatalog.GrainId && item.Key != ItemCatalog.BreadId) hasGenericItems = true;
                data.Accounts.Add(saved);
            }
            data.Version = Hero == null ? 3 : heroPose == null ? 4 : 5; data.AutoEmployment = AutoEmployment;
            foreach (var account in accounts) if (account.IsDead) data.Version = 6;
            if (hasGenericItems)
            {
                data.Version = 7;
                for (int i = 0; i < accounts.Count; i++)
                {
                    var ids = new List<string>(accounts[i].Items.Quantities.Keys); ids.Sort(StringComparer.Ordinal);
                    foreach (var id in ids) data.Accounts[i].Items.Add(new SavedItem { Id = id, Quantity = accounts[i].Items.Quantity(id) });
                }
            }
            data.HeroEnabled = Hero != null;
            data.HeroPose = heroPose?.Copy();
            data.LastUnpaid = LastUnpaid; data.LastUnreachable = LastUnreachable;
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
            if (data == null || (data.Version < 1 || data.Version > 7) || data.Jobs == null || data.LastPaid < 0 || data.LastPaid > 18
                || data.LastFed < 0 || data.LastFed > 20 || data.LastBread < 0 || data.LastBread > 40)
                throw new ArgumentException("Unsupported or invalid save.");
            if (data.LastUnpaid < 0 || data.LastUnpaid > 18 || data.LastUnreachable < 0 || data.LastUnreachable > 60)
                throw new ArgumentException("Invalid saved diagnostics.");
            List<BusinessDefinition> definitions = null;
            if (data.Version >= 2)
            {
                if (data.Businesses == null) throw new ArgumentException("Missing businesses.");
                definitions = new List<BusinessDefinition>();
                foreach (var b in data.Businesses)
                {
                    if (b == null) throw new ArgumentException("Invalid business.");
                    definitions.Add(new BusinessDefinition(b.Id, b.Owner, b.Capacity, b.Wage));
                }
            }
            var restored = new DailySimulation(farmYield: data.FarmYield, capital: data.Reserve, businesses: definitions,
                autoEmployment: data.Version >= 3 && data.AutoEmployment);
            if ((data.Version >= 4 && data.Version <= 5) || (data.Version >= 6 && data.HeroEnabled)) restored.EnableHero();
            if (data.Version == 5 || (data.Version >= 6 && data.HeroPose != null))
            {
                if (data.HeroPose == null) throw new ArgumentException("Missing v5 hero pose.");
                restored.SetHeroPose(data.HeroPose);
            }
            else if (data.HeroPose != null) throw new ArgumentException("Hero pose requires v5.");
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
            if (data.Version < 6)
                foreach (var account in data.Accounts) if (account.IsDead || account.DeathPoint != null)
                    throw new ArgumentException("Death state requires v6.");
            if (data.Version >= 6)
            {
                foreach (var job in restored.jobs)
                    if (restored.Agent(job.Key).IsDead || !restored.BusinessActive(job.Value))
                        throw new ArgumentException("Saved job belongs to dead agent or suspended business.");
                restored.ApplyDeathPolicy();
            }
            restored.LastPaid = data.LastPaid; restored.LastFed = data.LastFed; restored.LastBread = data.LastBread;
            restored.LastUnpaid = data.LastUnpaid; restored.LastUnreachable = data.LastUnreachable;
            foreach (var entry in restored.Economy.Ledger)
                if (entry.Kind == "JobDecision" && entry.From != null) restored.decisions[entry.From] = entry.Reason;
            return restored;
        }
    }
}
