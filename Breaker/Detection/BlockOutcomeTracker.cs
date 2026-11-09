using System.Runtime.CompilerServices;

namespace Weaponbreaker.Breaker.Detection;

/// <summary>Tracks vanilla's successful block result for the exact incoming hit.</summary>
internal static class BlockOutcomeTracker
{
    private static readonly ConditionalWeakTable<HitData, Outcome> Outcomes = new();

    internal static void Record(Humanoid defender, HitData hit, ItemDrop.ItemData blocker)
    {
        Clear(hit);
        Outcomes.Add(hit, new Outcome(defender,
            blocker.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield));
    }

    internal static bool WasBlockedBy(Humanoid defender, HitData hit)
    {
        return Outcomes.TryGetValue(hit, out var outcome)
            && ReferenceEquals(outcome.Defender, defender);
    }

    internal static bool WasBlockedByShield(Character defender, HitData hit)
    {
        return Outcomes.TryGetValue(hit, out var outcome)
            && ReferenceEquals(outcome.Defender, defender)
            && outcome.UsedShield;
    }

    internal static void Clear(HitData hit) => Outcomes.Remove(hit);

    private sealed class Outcome
    {
        internal Outcome(Humanoid defender, bool usedShield)
        {
            Defender = defender;
            UsedShield = usedShield;
        }

        internal Humanoid Defender { get; }
        internal bool UsedShield { get; }
    }
}
