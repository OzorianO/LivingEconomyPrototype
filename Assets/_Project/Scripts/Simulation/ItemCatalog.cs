using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LivingEconomy.Simulation
{
    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public int MaxStack { get; }

        public ItemDefinition(string id, string name, int maxStack)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An item needs a stable ID and name.");
            if (maxStack <= 0) throw new ArgumentOutOfRangeException(nameof(maxStack));
            Id = id; Name = name; MaxStack = maxStack;
        }
    }

    public sealed class ItemCatalog
    {
        public const string GrainId = "grain";
        public const string BreadId = "bread";
        private readonly Dictionary<string, ItemDefinition> definitions;
        public IReadOnlyDictionary<string, ItemDefinition> Definitions { get; }

        // Legacy stocks can exceed normal gameplay stacks. Keep their quantities intact;
        // finite-slot containers still use the same stack calculation and API.
        public static ItemCatalog Prototype { get; } = new ItemCatalog(new[] {
            new ItemDefinition(GrainId, "Grain", 100),
            new ItemDefinition(BreadId, "Bread", 20) });

        public ItemCatalog(IEnumerable<ItemDefinition> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            definitions = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null || definitions.ContainsKey(item.Id))
                    throw new ArgumentException("Missing or duplicate item definition.");
                definitions.Add(item.Id, item);
            }
            Definitions = new ReadOnlyDictionary<string, ItemDefinition>(definitions);
        }

        public bool TryGet(string id, out ItemDefinition item)
        {
            item = null;
            return id != null && definitions.TryGetValue(id, out item);
        }

        public static string IdFor(Good good)
        {
            switch (good)
            {
                case Good.Grain: return GrainId;
                case Good.Bread: return BreadId;
                default: throw new ArgumentException("Unknown good.", nameof(good));
            }
        }
    }
}
