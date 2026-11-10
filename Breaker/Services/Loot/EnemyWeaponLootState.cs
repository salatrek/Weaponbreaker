namespace Weaponbreaker.Breaker.Services.Loot;

/// <summary>A persistent result, including an ordinary (non-magic) weapon roll.</summary>
internal sealed class EnemyWeaponLootState
{
    public EnemyWeaponLootState() { }

    public int Version { get; set; } = 1;
    public string Prefab { get; set; } = "";
    public int Quality { get; set; }
    public float Durability { get; set; }
    public int Variant { get; set; }
    public Dictionary<string, string> CustomData { get; set; } = new(StringComparer.Ordinal);

    internal static EnemyWeaponLootState Capture(string prefab, ItemDrop.ItemData item) => new()
    {
        Prefab = prefab,
        Quality = item.m_quality,
        Durability = item.m_durability,
        Variant = item.m_variant,
        CustomData = new Dictionary<string, string>(item.m_customData, StringComparer.Ordinal)
    };

    internal void Restore(ItemDrop.ItemData item)
    {
        item.m_quality = Quality;
        item.m_durability = Durability;
        item.m_variant = Variant;
        // EpicLoot stores magic data here and restores its component lazily, as it
        // does for vanilla inventory/ZDO loading. Never share mutable dictionaries.
        item.m_customData = new Dictionary<string, string>(CustomData, StringComparer.Ordinal);
    }
}
