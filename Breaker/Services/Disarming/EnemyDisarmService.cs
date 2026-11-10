using BepInEx.Logging;
using UnityEngine;
using Weaponbreaker.Breaker.Configuration;
using Weaponbreaker.Breaker.Rules;
using Weaponbreaker.Breaker.Services.Weapons;

namespace Weaponbreaker.Breaker.Services.Disarming;

/// <summary>
/// Handles mechanics that remove weapons from supported enemies.
/// </summary>
internal sealed class EnemyDisarmService
{
    private const float HiddenBowDisarmChanceMultiplier = 2f;

    private readonly WeaponbreakerOptions _configuration;
    private readonly WeaponProvider _weaponProvider;
    private readonly WeaponStateService _weaponState;
    private readonly ManualLogSource _logger;

    public EnemyDisarmService(
        WeaponbreakerOptions configuration,
        WeaponProvider weaponProvider,
        WeaponStateService weaponState,
        ManualLogSource logger)
    {
        _configuration = configuration;
        _weaponProvider = weaponProvider;
        _weaponState = weaponState;
        _logger = logger;
    }

    public void TryFromPerfectParry(
        Humanoid defender,
        HitData hit,
        Character attacker,
        ItemDrop.ItemData blocker)
    {
        if (!_configuration.Enabled
            || !_configuration.EnemyDisarmEnabled
            || hit is null
            || attacker is not Humanoid enemy
            || enemy.IsPlayer()
            || enemy.IsDead()
            || !enemy.IsOwner()
            || _weaponState.HasDisarmedFallbackWeapon(enemy)
            || hit.m_ranged
            || blocker?.m_shared is null)
        {
            return;
        }

        if (!_weaponProvider.TryGetEnemyWeapon(
                enemy,
                out var weapon,
                out var rule,
                out var ruleOptions))
        {
            _logger.LogWarning(
                $"Perfect parry target '{enemy.name}' has no supported weapon rule. "
                + $"weaponName='{enemy.GetCurrentWeapon()?.m_shared?.m_name}', "
                + $"weaponPrefab='{enemy.GetCurrentWeapon()?.m_dropPrefab?.name}', "
                + $"weaponType='{enemy.GetCurrentWeapon()?.m_shared?.m_itemType}'.");
            return;
        }

        var chance = ruleOptions.EnemyWeaponDisarmChance
            * GetParryChanceMultiplier(blocker)
            * WeaponProvider.GetOffensiveSkillMultiplier(defender, blocker);
        TryDisarm(enemy, weapon, rule, chance, "perfect parry");
    }

    public void TryFromHiddenBowHit(
        Character target,
        HitData hit,
        Character attacker)
    {
        if (!_configuration.Enabled
            || !_configuration.EnemyDisarmEnabled
            || target is not Humanoid enemy
            || enemy.IsPlayer()
            || enemy.IsDead()
            || !enemy.IsOwner()
            || _weaponState.HasDisarmedFallbackWeapon(enemy)
            || attacker is not Player player
            || !player.IsSneaking()
            || !hit.m_ranged
            || hit.m_backstabBonus <= 1f
            || player.GetCurrentWeapon()?.m_shared?.m_itemType
                != ItemDrop.ItemData.ItemType.Bow
            || !_weaponProvider.TryGetEnemyWeapon(
                enemy,
                out var weapon,
                out var rule,
                out var ruleOptions))
        {
            return;
        }

        var playerWeapon = player.GetCurrentWeapon();
        var chance = ruleOptions.EnemyWeaponDisarmChance
            * HiddenBowDisarmChanceMultiplier
            * WeaponProvider.GetOffensiveSkillMultiplier(player, playerWeapon!);
        TryDisarm(enemy, weapon, rule, chance, "hidden bow hit");
    }

    private void TryDisarm(
        Humanoid enemy,
        ItemDrop.ItemData weapon,
        WeaponRule rule,
        float chance,
        string reason)
    {
        if (chance <= 0f || UnityEngine.Random.value > Mathf.Clamp01(chance))
        {
            return;
        }

        if (_weaponState.TryDropEnemyWeapon(enemy, weapon, rule))
        {
            _logger.LogInfo($"Disarmed '{enemy.name}' by {reason}.");
        }
    }

    private static float GetParryChanceMultiplier(ItemDrop.ItemData blocker)
    {
        return blocker.m_shared!.m_itemType switch
        {
            ItemDrop.ItemData.ItemType.Shield => 0.25f,
            ItemDrop.ItemData.ItemType.TwoHandedWeapon
                or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                or ItemDrop.ItemData.ItemType.Attach_Atgeir
                => 1.25f,
            ItemDrop.ItemData.ItemType.OneHandedWeapon => 1f,
            _ => 0f
        };
    }
}
