using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LivingEconomy.Simulation
{
    public enum WorldAction { Harvest, Craft }

    public sealed class WorldActionResult
    {
        public bool Success { get; }
        public string Reason { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ItemId { get; }
        public int Quantity { get; }

        internal WorldActionResult(bool success, string reason, string actorId, string targetId, string itemId, int quantity)
        {
            Success = success; Reason = reason; ActorId = actorId; TargetId = targetId; ItemId = itemId; Quantity = quantity;
        }
    }

    public sealed class ResourceNode
    {
        public string Id { get; }
        public string OutputItemId { get; }
        public string RequiredToolId { get; }
        public int Capacity { get; }
        public int Available { get; private set; }
        public int HarvestAmount { get; }

        public ResourceNode(string id, string outputItemId, int capacity, int harvestAmount, string requiredToolId)
        {
            if (string.IsNullOrWhiteSpace(id) || !ItemCatalog.Prototype.TryGet(outputItemId, out _))
                throw new ArgumentException("Resource node needs a stable ID and known output item.");
            if (capacity <= 0 || harvestAmount <= 0 || harvestAmount > capacity)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (requiredToolId != null && !ItemCatalog.Prototype.TryGet(requiredToolId, out _))
                throw new ArgumentException("Unknown required tool.");
            Id = id; OutputItemId = outputItemId; Capacity = capacity; Available = capacity;
            HarvestAmount = harvestAmount; RequiredToolId = requiredToolId;
        }

        internal void Take(int amount)
        {
            if (amount <= 0 || amount > Available) throw new InvalidOperationException("Invalid harvest mutation.");
            Available -= amount;
        }
    }

    public sealed class RecipeDefinition
    {
        public string Id { get; }
        public string InputItemId { get; }
        public int InputQuantity { get; }
        public string OutputItemId { get; }
        public int OutputQuantity { get; }
        public string WorkstationId { get; }

        public RecipeDefinition(string id, string inputItemId, int inputQuantity, string outputItemId, int outputQuantity, string workstationId)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(workstationId)
                || !ItemCatalog.Prototype.TryGet(inputItemId, out _) || !ItemCatalog.Prototype.TryGet(outputItemId, out _)
                || inputItemId == outputItemId) throw new ArgumentException("Invalid recipe identity or items.");
            if (inputQuantity <= 0 || outputQuantity <= 0) throw new ArgumentOutOfRangeException();
            Id = id; InputItemId = inputItemId; InputQuantity = inputQuantity;
            OutputItemId = outputItemId; OutputQuantity = outputQuantity; WorkstationId = workstationId;
        }
    }

    public static class RecipeCatalog
    {
        public const string WorkbenchId = "wood-workbench-01";
        public const string PlanksId = "log-to-planks";
        public const string FirewoodId = "log-to-firewood";
        public const string SticksId = "log-to-sticks";
        private static readonly Dictionary<string, RecipeDefinition> definitions = new Dictionary<string, RecipeDefinition>(StringComparer.Ordinal) {
            { PlanksId, new RecipeDefinition(PlanksId, ItemCatalog.LogId, 1, ItemCatalog.PlankId, 4, WorkbenchId) },
            { FirewoodId, new RecipeDefinition(FirewoodId, ItemCatalog.LogId, 1, ItemCatalog.FirewoodId, 4, WorkbenchId) },
            { SticksId, new RecipeDefinition(SticksId, ItemCatalog.LogId, 1, ItemCatalog.StickId, 6, WorkbenchId) }
        };
        public static IReadOnlyDictionary<string, RecipeDefinition> Definitions { get; }
            = new ReadOnlyDictionary<string, RecipeDefinition>(definitions);
        public static bool TryGet(string id, out RecipeDefinition recipe)
        {
            recipe = null;
            return id != null && definitions.TryGetValue(id, out recipe);
        }
    }
}
