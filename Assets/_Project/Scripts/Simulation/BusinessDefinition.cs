using System;

namespace LivingEconomy.Simulation
{
    // Immutable rules for the current grain-to-bread scenario; no Unity dependency.
    public sealed class BusinessDefinition
    {
        public string Id { get; }
        public string Owner { get; }
        public int Capacity { get; }
        public long Wage { get; }
        public BusinessDefinition(string id, string owner, int capacity, long wage)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(owner) || capacity < 0 || capacity > 20 || wage < 0)
                throw new ArgumentException("Invalid business definition.");
            Id = id; Owner = owner; Capacity = capacity; Wage = wage;
        }
    }
    public static class EmploymentScenario
    {
        public static BusinessDefinition[] Businesses() => new[] {
            new BusinessDefinition("farm", "npc-00", 9, 6),
            new BusinessDefinition("bakery", "npc-06", 9, 6) };
    }
}
