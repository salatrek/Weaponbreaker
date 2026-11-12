using BepInEx;
using HarmonyLib;
using Weaponbreaker.Breaker.Compatibility.EpicLoot;
using Weaponbreaker.Breaker.Compatibility.DualWield;
using Weaponbreaker.Breaker.Configuration;
using Weaponbreaker.Breaker.Detection;
using Weaponbreaker.Breaker.Rules;
using Weaponbreaker.Breaker.Services.Disarming;
using Weaponbreaker.Breaker.Services.Loot;
using Weaponbreaker.Breaker.Services.Weapons;

namespace Weaponbreaker.Breaker;

/// <summary>
/// BepInEx entrypoint for Weaponbreaker.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(EpicLootIntegration.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(DualWieldIntegration.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class WeaponbreakerPlugin : BaseUnityPlugin
{
    private const string PluginGuid = "salatrek.Weaponbreaker";
    private const string PluginName = "Weaponbreaker";
    private const string PluginVersion = "0.1.6";

    private readonly Harmony _harmony = new(PluginGuid);
    private BattleService? _battleService;
    private WeaponbreakerConfigSync? _configSync;
    private ParryEventRouter? _parryRouter;

    private void Awake()
    {
        const string configurationFileName = "weaponbreakerConfigs.json";
        var configurationPath = Path.Combine(Paths.ConfigPath, configurationFileName);
        WeaponbreakerOptionsFile.EnsureDefaults(configurationPath, Logger);
        var options = WeaponbreakerOptionsFile.Load(configurationPath, Logger);
        var ruleCatalog = new WeaponRuleCatalog();
        options = WeaponbreakerConfigEntries.Bind(Config, options, ruleCatalog, Logger);
        var epicLoot = EpicLootIntegration.Create(Logger);
        var dualWield = new DualWieldIntegration(Logger);
        var configSync = new WeaponbreakerConfigSync(options, Logger, epicLoot is not null, dualWield.IsInstalled);
        _configSync = configSync;
        options = configSync.RuntimeOptions;
        var weaponProvider = new WeaponProvider(options, ruleCatalog);
        var enemyLoot = new EnemyWeaponLootService(options, epicLoot, Logger);
        var weaponState = new WeaponStateService(options, weaponProvider, enemyLoot, dualWield, Logger);
        configSync.SessionReset += weaponState.ResetSession;
        var enemyDisarm = new EnemyDisarmService(options, weaponProvider, weaponState, Logger);
        _parryRouter = new ParryEventRouter(() => configSync.GameplayReady, enemyDisarm.TryOwnedPerfectParry, Logger);
        enemyDisarm.ForwardParry = _parryRouter.Forward;
        configSync.SessionReset += _parryRouter.Reset;
        var playerDisarm = new PlayerDisarmService(options, weaponProvider, weaponState, dualWield, Logger);
        _battleService = new BattleService(enemyDisarm, playerDisarm);

        WeaponbreakerPatches.Initialize(_battleService, weaponState, configSync, Logger);
        WeaponbreakerPatches.ApplyPatches(_harmony);

        Logger.LogInfo("Weaponbreaker initialized.");
    }

    private void OnDestroy()
    {
        _configSync?.Reset();
        _configSync = null;
        _parryRouter = null;
        WeaponbreakerPatches.Shutdown();
        _harmony.UnpatchSelf();
        _battleService = null;
    }

    private void Update()
    {
        _configSync?.Tick();
        _parryRouter?.Tick();
    }
}
