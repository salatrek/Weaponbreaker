using System.Reflection;
using System.Text.Json;
using BepInEx.Logging;

namespace Weaponbreaker.Breaker.Configuration;

/// <summary>
/// Loads and normalizes the editable JSON configuration.
/// </summary>
internal static class WeaponbreakerOptionsFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static WeaponbreakerOptions Load(string path, ManualLogSource logger)
    {
        var json = File.ReadAllText(path);
        var options = JsonSerializer.Deserialize<WeaponbreakerOptions>(json, SerializerOptions);
        if (options is null)
        {
            throw new InvalidDataException($"Configuration file '{path}' is empty.");
        }

        options.Enemies ??= new Dictionary<string, EnemyOptions>();
        options.Cooldowns ??= new CooldownOptions();
        options.Cooldowns.EnemyDisarmSeconds = Clamp(options.Cooldowns.EnemyDisarmSeconds, 0f, 3600f);
        options.Cooldowns.EnemyWeaponPickupSeconds = Clamp(options.Cooldowns.EnemyWeaponPickupSeconds, 0f, 3600f);
        options.Cooldowns.PlayerDisarmSeconds = Clamp(options.Cooldowns.PlayerDisarmSeconds, 0f, 3600f);
        foreach (var pair in options.Enemies.ToList())
        {
            var enemyOptions = pair.Value ?? new EnemyOptions();
            enemyOptions.EnemyWeaponDisarmChance = Clamp(enemyOptions.EnemyWeaponDisarmChance, 0f, 1f);
            enemyOptions.PlayerDisarmChance = Clamp(enemyOptions.PlayerDisarmChance, 0f, 1f);
            options.Enemies[pair.Key] = enemyOptions;
        }

        logger.LogInfo($"Loaded Weaponbreaker configuration from '{Path.GetFileName(path)}'.");
        return options;
    }

    public static void EnsureDefaults(
        string path,
        ManualLogSource logger)
    {
        if (File.Exists(path))
        {
            return;
        }

        using var template = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Weaponbreaker.weaponbreakerConfigs.json");
        if (template is null)
        {
            throw new InvalidDataException("Embedded weaponbreakerConfigs.json was not found.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var configuration = File.Create(path);
        template.CopyTo(configuration);
        logger.LogInfo($"Created default configuration at '{path}'.");
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }
}
