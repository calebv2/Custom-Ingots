using System;
using System.Collections.Generic;

namespace CustomIngots.Config.Integration;

internal static class IngotConsumptionPolicy
{
    // A dock with no live Pickup can only be consumed if its entire stored count is used.
    // This preserves the behavior confirmed by the standalone Crystal Ingot test.
    internal static bool TryPlan(
        IReadOnlyList<uint> dockItemHashes,
        IReadOnlyList<int> dockCounts,
        IReadOnlyList<bool> dockHasPickups,
        IReadOnlyDictionary<uint, int> ingredientCosts,
        out int[] amountsByDock,
        out int blockedDockIndex)
    {
        amountsByDock = Array.Empty<int>();
        blockedDockIndex = -1;
        if (dockItemHashes == null || dockCounts == null || dockHasPickups == null
            || dockItemHashes.Count != dockCounts.Count
            || dockItemHashes.Count != dockHasPickups.Count
            || ingredientCosts == null || ingredientCosts.Count == 0)
            return false;

        amountsByDock = new int[dockItemHashes.Count];
        for (var index = 0; index < dockCounts.Count; index++)
        {
            if (dockCounts[index] < 0) return false;
        }

        foreach (var ingredient in ingredientCosts)
        {
            if (ingredient.Key == 0 || ingredient.Value <= 0) return false;
            var remaining = ingredient.Value;
            for (var index = 0; index < dockCounts.Count; index++)
            {
                if (dockItemHashes[index] != ingredient.Key || dockCounts[index] == 0 || remaining == 0) continue;

                var amount = Math.Min(dockCounts[index], remaining);
                if (!dockHasPickups[index] && dockCounts[index] != amount)
                {
                    blockedDockIndex = index;
                    return false;
                }

                amountsByDock[index] = amount;
                remaining -= amount;
            }

            if (remaining != 0) return false;
        }

        return true;
    }
}
