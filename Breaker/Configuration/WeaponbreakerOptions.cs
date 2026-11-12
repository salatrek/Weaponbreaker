namespace Weaponbreaker.Breaker.Configuration;

/// <summary>
/// Editable settings loaded from weaponbreakerConfigs.json.
/// </summary>
public sealed class WeaponbreakerOptions
{
    /// <summary>Enables or disables all Weaponbreaker logic.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Enables disarming supported enemies after a player parry.</summary>
    public bool EnemyDisarmEnabled { get; set; } = true;

    /// <summary>Enables the reverse mechanic where enemies disarm players.</summary>
    public bool PlayerDisarmEnabled { get; set; } = true;

    /// <summary>Cooldowns for disarm and weapon return behavior.</summary>
    public CooldownOptions Cooldowns { get; set; } = new();

    /// <summary>Per enemy probabilities and reverse-disarm probabilities.</summary>
    public Dictionary<string, EnemyOptions> Enemies { get; set; } = new();
}

/// <summary>
/// Per enemy settings. Armed and unarmed enemies share this model; unarmed enemies expose only PlayerDisarmChance.
/// </summary>
public sealed class EnemyOptions
{
    /// <summary>Enables this enemy entry.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Baseline probability of disarming this enemy after a one-handed weapon parry.
    /// The shield/two-handed multipliers are applied on top of this value.
    /// </summary>
    public float EnemyWeaponDisarmChance { get; set; } = 0.3f;

    /// <summary>Uses EpicLootEnchantChance instead of EpicLoot's drop chance for this enemy.</summary>
    public bool OverrideEpicLootEnchantChance { get; set; }

    /// <summary>Chance among disarmed weapons. Missing values use the enemy family's default.</summary>
    public float? EpicLootEnchantChance { get; set; }

    /// <summary>
    /// Base probability that this enemy disarms the player's active weapon after a real hit.
    /// The weapon skill scales this from 100% of the base value at skill 0 to 25% at skill 100.
    /// </summary>
    public float PlayerDisarmChance { get; set; } = 0.05f;
}

/// <summary>
/// User-configurable cooldowns. The perfect parry window remains a vanilla hardcoded rule.
/// </summary>
public sealed class CooldownOptions
{
    /// <summary>Minimum delay before the same enemy can be disarmed again.</summary>
    public float EnemyDisarmSeconds { get; set; } = 8f;

    /// <summary>Delay before an enemy may return to its dropped weapon.</summary>
    public float EnemyWeaponPickupSeconds { get; set; } = 8f;

    /// <summary>Minimum delay before the player can be disarmed again.</summary>
    public float PlayerDisarmSeconds { get; set; } = 15f;
}
