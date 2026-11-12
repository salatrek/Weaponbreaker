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
    internal Action<Humanoid, Humanoid, ItemDrop.ItemData>? ForwardParry { get; set; }

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
        if (hit is null
            || attacker is not Humanoid enemy
            || enemy.IsPlayer()
            || enemy.IsDead()
            || hit.m_ranged
            || blocker?.m_shared is null)
        {
            return;
        }

        // BlockAttack runs on the defending player's peer, which need not own the attacker.
        // Do not execute on mirrored/non-owned player objects or claim ownership of the enemy.
        if (!defender.IsPlayer() || !defender.IsOwner()) return;
        if (!enemy.IsOwner())
        {
            ForwardParry?.Invoke(defender, enemy, blocker);
            return;
        }
        TryOwnedPerfectParry(enemy, blocker, WeaponProvider.GetOffensiveSkillMultiplier(defender, blocker));
    }

    internal void TryOwnedPerfectParry(Humanoid enemy, ItemDrop.ItemData blocker, float skillMultiplier)
    {
        if (!_configuration.Enabled || !_configuration.EnemyDisarmEnabled)
        {
            _logger.LogInfo("[Parry] Skipped: host configuration disables enemy disarming.");
            return;
        }
        if (!enemy.IsOwner() || enemy.IsDead() || enemy.IsPlayer())
        {
            _logger.LogInfo($"[Parry] Skipped: enemy unavailable or owned by another peer; enemy={enemy.GetZDOID()}.");
            return;
        }
        if (_weaponState.HasDisarmedFallbackWeapon(enemy))
        {
            _logger.LogInfo($"[Parry] Skipped: enemy={enemy.GetZDOID()} already disarmed.");
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
            * skillMultiplier;
        _logger.LogInfo($"[Parry] Chance: enemy={enemy.GetZDOID()}, base={ruleOptions.EnemyWeaponDisarmChance:F3}, blockerMultiplier={GetParryChanceMultiplier(blocker):F3}, skillMultiplier={skillMultiplier:F3}, final={Mathf.Clamp01(chance):F3}.");
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
        var roll = UnityEngine.Random.value;
        if (chance <= 0f || roll > Mathf.Clamp01(chance))
        {
            _logger.LogInfo($"[Parry] Roll failed: enemy={enemy.GetZDOID()}, reason={reason}, chance={Mathf.Clamp01(chance):F3}, roll={roll:F3}.");
            return;
        }

        if (_weaponState.TryDropEnemyWeapon(enemy, weapon, rule, out var failure))
        {
            _logger.LogInfo($"Disarmed '{enemy.name}' by {reason}.");
        }
        else _logger.LogInfo($"[Parry] Drop skipped: enemy={enemy.GetZDOID()}, reason={failure}.");
    }

    internal static float GetParryChanceMultiplier(ItemDrop.ItemData blocker)
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
