using BepInEx.Configuration;
using BepInEx.Logging;
using Weaponbreaker.Breaker.Rules;

namespace Weaponbreaker.Breaker.Configuration;

/// <summary>
/// Exposes one configuration section per enemy and keeps it synchronized with JSON options.
/// </summary>
public static class WeaponbreakerConfigEntries
{
    internal static WeaponbreakerOptions Bind(
        ConfigFile config,
        WeaponbreakerOptions options,
        WeaponRuleCatalog ruleCatalog,
        ManualLogSource logger)
    {
        var enabled = config.Bind(
            "General",
            "Enabled",
            options.Enabled,
            "Enables or disables all Weaponbreaker logic.");
        options.Enabled = enabled.Value;
        enabled.SettingChanged += (_, _) => options.Enabled = enabled.Value;

        var enemyDisarmEnabled = config.Bind(
            "General",
            "EnemyDisarmEnabled",
            options.EnemyDisarmEnabled,
            "Enables disarming weapons from supported enemies after a perfect parry or hidden bow hit.");
        options.EnemyDisarmEnabled = enemyDisarmEnabled.Value;
        enemyDisarmEnabled.SettingChanged += (_, _) => options.EnemyDisarmEnabled = enemyDisarmEnabled.Value;

        var playerDisarmEnabled = config.Bind(
            "General",
            "PlayerDisarmEnabled",
            options.PlayerDisarmEnabled,
            "Enables the reverse mechanic where enemies can disarm the player.");
        options.PlayerDisarmEnabled = playerDisarmEnabled.Value;
        playerDisarmEnabled.SettingChanged += (_, _) => options.PlayerDisarmEnabled = playerDisarmEnabled.Value;

        var enemyDisarmCooldown = config.Bind(
            "Cooldowns",
            "EnemyDisarmSeconds",
            options.Cooldowns.EnemyDisarmSeconds,
            new ConfigDescription(
                "Delay before the same enemy can be disarmed again.",
                new AcceptableValueRange<float>(0f, 3600f)));
        options.Cooldowns.EnemyDisarmSeconds = enemyDisarmCooldown.Value;
        enemyDisarmCooldown.SettingChanged += (_, _) =>
            options.Cooldowns.EnemyDisarmSeconds = enemyDisarmCooldown.Value;

        var enemyWeaponPickupCooldown = config.Bind(
            "Cooldowns",
            "EnemyWeaponPickupSeconds",
            options.Cooldowns.EnemyWeaponPickupSeconds,
            new ConfigDescription(
                "Delay before an enemy can return to its dropped weapon.",
                new AcceptableValueRange<float>(0f, 3600f)));
        options.Cooldowns.EnemyWeaponPickupSeconds = enemyWeaponPickupCooldown.Value;
        enemyWeaponPickupCooldown.SettingChanged += (_, _) =>
            options.Cooldowns.EnemyWeaponPickupSeconds = enemyWeaponPickupCooldown.Value;

        var playerDisarmCooldown = config.Bind(
            "Cooldowns",
            "PlayerDisarmSeconds",
            options.Cooldowns.PlayerDisarmSeconds,
            new ConfigDescription(
                "Delay before the player can be disarmed again.",
                new AcceptableValueRange<float>(0f, 3600f)));
        options.Cooldowns.PlayerDisarmSeconds = playerDisarmCooldown.Value;
        playerDisarmCooldown.SettingChanged += (_, _) =>
            options.Cooldowns.PlayerDisarmSeconds = playerDisarmCooldown.Value;

        foreach (var definition in ruleCatalog.Enemies)
        {
            var defaults = GetDefaults(config, options, ruleCatalog, definition);
            options.Enemies[definition.Id] = defaults;

            var section = $"Enemies.{definition.Id}";
            var ruleEnabled = config.Bind(
                section,
                "Enabled",
                defaults.Enabled,
                new ConfigDescription($"Enables the {definition.Id} enemy entry.", null,
                    new ConfigurationManagerAttributes { Order = 50 }));
            defaults.Enabled = ruleEnabled.Value;
            ruleEnabled.SettingChanged += (_, _) => defaults.Enabled = ruleEnabled.Value;

            if (definition.CanDropWeapon)
            {
                var enemyChance = config.Bind(
                    section,
                    "EnemyWeaponDisarmChance",
                    defaults.EnemyWeaponDisarmChance,
                    new ConfigDescription(
                        "Probability of disarming this enemy after a perfect parry with a one-handed weapon.",
                        new AcceptableValueRange<float>(0f, 1f),
                        new ConfigurationManagerAttributes { Order = 40 }));
                defaults.EnemyWeaponDisarmChance = enemyChance.Value;
                enemyChance.SettingChanged += (_, _) => defaults.EnemyWeaponDisarmChance = enemyChance.Value;

                var overrideEnchantChance = config.Bind(
                    section,
                    "OverrideEpicLootEnchantChance",
                    defaults.OverrideEpicLootEnchantChance,
                    new ConfigDescription(
                        "Use the custom enchantment chance for this enemy instead of EpicLoot's drop chance. "
                        + "Rarity and effects still come from EpicLoot. Without EpicLoot this setting has no effect: ordinary weapons are dropped. "
                        + "Applies to weapons whose loot has not been rolled yet.", null,
                        new ConfigurationManagerAttributes { Order = 30 }));
                defaults.OverrideEpicLootEnchantChance = overrideEnchantChance.Value;
                overrideEnchantChance.SettingChanged += (_, _) =>
                    defaults.OverrideEpicLootEnchantChance = overrideEnchantChance.Value;

                var enchantChance = config.Bind(
                    section,
                    "EpicLootEnchantChance",
                    defaults.EpicLootEnchantChance ?? definition.DefaultEpicLootEnchantChance,
                    new ConfigDescription(
                        "Chance that a successfully disarmed weapon is enchanted (0 = 0%, 1 = 100%). "
                        + "Requires EpicLoot and OverrideEpicLootEnchantChance. "
                        + "Replaces EpicLoot's drop chance/category ratios; uses its equipment rarity tables and effects.",
                        new AcceptableValueRange<float>(0f, 1f),
                        new ConfigurationManagerAttributes { Order = 20 }));
                defaults.EpicLootEnchantChance = enchantChance.Value;
                enchantChance.SettingChanged += (_, _) => defaults.EpicLootEnchantChance = enchantChance.Value;
            }

            var playerChance = config.Bind(
                section,
                "PlayerDisarmChance",
                defaults.PlayerDisarmChance,
                new ConfigDescription(
                    "Probability that this enemy disarms the player's weapon after real damage.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { Order = 10 }));
            defaults.PlayerDisarmChance = playerChance.Value;
            playerChance.SettingChanged += (_, _) => defaults.PlayerDisarmChance = playerChance.Value;
        }

        logger.LogInfo($"Registered {ruleCatalog.Enemies.Count} Weaponbreaker enemy entries.");
        return options;
    }

    private static EnemyOptions GetDefaults(
        ConfigFile config,
        WeaponbreakerOptions options,
        WeaponRuleCatalog ruleCatalog,
        EnemyDefinition definition)
    {
        if (options.Enemies.TryGetValue(definition.Id, out var configured))
        {
            return configured;
        }

        var legacy = ruleCatalog.Rules
            .Where(rule => string.Equals(rule.EnemyId, definition.Id, StringComparison.Ordinal))
            .Select(rule => ReadLegacyRule(config, rule.Id))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        if (legacy.Count == 0)
        {
            return new EnemyOptions
            {
                EnemyWeaponDisarmChance = definition.DefaultEnemyWeaponDisarmChance,
                PlayerDisarmChance = definition.DefaultPlayerDisarmChance
            };
        }

        return new EnemyOptions
        {
            Enabled = legacy.All(value => value.Enabled),
            EnemyWeaponDisarmChance = legacy.Max(value => value.EnemyWeaponDisarmChance),
            PlayerDisarmChance = legacy.Max(value => value.PlayerDisarmChance)
        };
    }

    private static EnemyOptions? ReadLegacyRule(ConfigFile config, string ruleId)
    {
        var section = $"Enemies.{ruleId}";
        if (!config.TryGetEntry(section, "Enabled", out ConfigEntry<bool> enabled)
            && !config.TryGetEntry(section, "EnemyWeaponDisarmChance", out ConfigEntry<float> enemyChance)
            && !config.TryGetEntry(section, "PlayerDisarmChance", out ConfigEntry<float> playerChance))
        {
            return null;
        }

        config.TryGetEntry(section, "Enabled", out enabled);
        config.TryGetEntry(section, "EnemyWeaponDisarmChance", out enemyChance);
        config.TryGetEntry(section, "PlayerDisarmChance", out playerChance);
        return new EnemyOptions
        {
            Enabled = enabled?.Value ?? true,
            EnemyWeaponDisarmChance = enemyChance?.Value ?? 0f,
            PlayerDisarmChance = playerChance?.Value ?? 0f
        };
    }
}
