using System.Text.Json;
using BepInEx.Logging;
using Weaponbreaker.Breaker.Compatibility.EpicLoot;
using Weaponbreaker.Breaker.Configuration;

namespace Weaponbreaker.Breaker.Services.Loot;

/// <summary>Rolls once per enemy weapon and retains the result across ZDO ownership changes.</summary>
internal sealed class EnemyWeaponLootService
{
    private const string StateKeyPrefix = "weaponbreaker.loot.";
    private readonly EpicLootIntegration? _epicLoot;
    private readonly WeaponbreakerOptions _options;
    private readonly ManualLogSource _logger;

    internal EnemyWeaponLootService(WeaponbreakerOptions options, EpicLootIntegration? epicLoot, ManualLogSource logger)
    {
        _epicLoot = epicLoot;
        _options = options;
        _logger = logger;
    }

    internal ItemDrop.ItemData Prepare(Humanoid enemy, ZDO enemyZdo, string enemyId, string prefab, ItemDrop.ItemData ordinaryItem)
    {
        if (!enemy || !enemy.IsOwner() || !enemyZdo.IsValid() || !enemyZdo.IsOwner()
            || enemyZdo.m_uid == ZDOID.None || enemyZdo.m_uid != enemy.GetZDOID())
            return ordinaryItem;
        var key = StateKeyPrefix + prefab;
        var saved = enemyZdo.GetString(key, "");
        if (!string.IsNullOrEmpty(saved))
        {
            try
            {
                var state = JsonSerializer.Deserialize<EnemyWeaponLootState>(saved);
                if (state is null || state.Version != 1 || state.Prefab != prefab || state.CustomData is null
                    || state.Quality < 1 || state.Quality > (ordinaryItem.m_shared?.m_maxQuality ?? 1)
                    || state.Durability < 0 || float.IsNaN(state.Durability) || float.IsInfinity(state.Durability))
                {
                    throw new InvalidDataException("Invalid saved weapon loot state.");
                }

                state.Restore(ordinaryItem);
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidDataException)
            {
                // Keep the latch even for corrupt/unknown data: loading must not
                // become another chance at a legendary item.
                _logger.LogWarning($"Could not restore saved loot for '{prefab}'; dropping an ordinary weapon without rerolling. {exception.Message}");
            }

            return ordinaryItem;
        }

        // Read current host options, since network sync replaces the enemy dictionary.
        _options.Enemies.TryGetValue(enemyId, out var options);
        var result = (_options.AllowEpicLoot ? _epicLoot?.Enchant(enemy, ordinaryItem, options) : null) ?? ordinaryItem;
        enemyZdo.Set(key, JsonSerializer.Serialize(EnemyWeaponLootState.Capture(prefab, result)));
        return result;
    }
}
