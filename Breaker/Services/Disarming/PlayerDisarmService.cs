using BepInEx.Logging;
using UnityEngine;
using Weaponbreaker.Breaker.Configuration;
using Weaponbreaker.Breaker.Compatibility.DualWield;
using Weaponbreaker.Breaker.Services.Weapons;

namespace Weaponbreaker.Breaker.Services.Disarming;

/// <summary>
/// Handles the reverse mechanic where a supported enemy can disarm the player.
/// </summary>
internal sealed class PlayerDisarmService
{
    private readonly WeaponbreakerOptions _configuration;
    private readonly WeaponProvider _weaponProvider;
    private readonly WeaponStateService _weaponState;
    private readonly DualWieldIntegration _dualWield;
    private readonly ManualLogSource _logger;

    public PlayerDisarmService(
        WeaponbreakerOptions configuration,
        WeaponProvider weaponProvider,
        WeaponStateService weaponState,
        DualWieldIntegration dualWield,
        ManualLogSource logger)
    {
        _configuration = configuration;
        _weaponProvider = weaponProvider;
        _weaponState = weaponState;
        _dualWield = dualWield;
        _logger = logger;
    }

    public void TryFromRealDamage(
        Player player,
        HitData hit,
        Character attacker,
        bool blockedByShield)
    {
        if (blockedByShield
            || !_configuration.Enabled
            || !_configuration.PlayerDisarmEnabled
            || player is null
            || hit is null
            || attacker is not Humanoid enemy
            || enemy.IsPlayer()
            || enemy.IsDead()
            || !player.IsOwner()
            || !_weaponProvider.TryGetEnemyConfig(
                enemy,
                out _,
                out var enemyOptions)
            || !WeaponProvider.IsPlayerDisarmableWeapon(player.GetCurrentWeapon())
            || enemyOptions.PlayerDisarmChance <= 0f)
        {
            return;
        }

        var playerWeapon = player.GetCurrentWeapon();
        var multiplier = _dualWield.TryGetDefensiveSkillMultiplier(player, playerWeapon!, out var dualMultiplier)
            ? dualMultiplier
            : WeaponProvider.GetDefensiveSkillMultiplier(player, playerWeapon!);
        var chance = enemyOptions.PlayerDisarmChance * multiplier;
        if (UnityEngine.Random.value > Mathf.Clamp01(chance))
        {
            return;
        }

        if (_weaponState.TryDropPlayerWeapon(player, enemy))
        {
            _logger.LogInfo($"Enemy '{enemy.name}' disarmed the player.");
        }
    }
}
