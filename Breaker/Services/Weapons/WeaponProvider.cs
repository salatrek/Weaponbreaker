using UnityEngine;
using Weaponbreaker.Breaker.Configuration;
using Weaponbreaker.Breaker.Rules;

namespace Weaponbreaker.Breaker.Services.Weapons;

/// <summary>
/// Resolves equipped weapons, whitelist rules and registered fallback item prefabs.
/// </summary>
internal sealed class WeaponProvider
{
    private readonly WeaponbreakerOptions _configuration;
    private readonly WeaponRuleCatalog _ruleCatalog;

    public WeaponProvider(
        WeaponbreakerOptions configuration,
        WeaponRuleCatalog ruleCatalog)
    {
        _configuration = configuration;
        _ruleCatalog = ruleCatalog;
    }

    public bool TryGetEnemyWeapon(
        Humanoid enemy,
        out ItemDrop.ItemData weapon,
        out WeaponRule rule,
        out EnemyOptions ruleOptions)
    {
        weapon = enemy.GetCurrentWeapon()!;
        rule = null!;
        ruleOptions = null!;

        if (!IsEnemyWeapon(weapon)
            || !_ruleCatalog.TryResolve(enemy, weapon, out rule)
            || !_configuration.Enemies.TryGetValue(rule.EnemyId, out ruleOptions)
            || !ruleOptions.Enabled)
        {
            return false;
        }

        return true;
    }

    public bool TryGetEnemyConfig(
        Humanoid enemy,
        out EnemyDefinition definition,
        out EnemyOptions options)
    {
        definition = null!;
        options = null!;
        if (!_ruleCatalog.TryResolveEnemy(enemy, out definition)
            || !_configuration.Enemies.TryGetValue(definition.Id, out options)
            || !options.Enabled)
        {
            return false;
        }

        return true;
    }

    public static float GetOffensiveSkillMultiplier(
        Character character,
        ItemDrop.ItemData item)
    {
        return GetSkillMultiplier(character.GetSkillLevel(GetSkillType(item)), true);
    }

    public static float GetDefensiveSkillMultiplier(
        Character character,
        ItemDrop.ItemData item)
    {
        var skillLevel = Mathf.Clamp(
            character.GetSkillLevel(GetSkillType(item)),
            0f,
            100f);

        // Player disarm chance decreases smoothly from 100% of its configured
        // base value at skill 0 to 25% at skill 100.
        return Mathf.Lerp(1f, 0.25f, skillLevel / 100f);
    }

    public ItemDrop.ItemData? CreateEnemyDropData(
        ItemDrop.ItemData sourceWeapon,
        WeaponRule rule)
    {
        // Enemy weapon ItemData can point to an unrelated or enemy-only drop prefab.
        // The code-owned rule mapping is the source of truth for the player-visible item.
        var fallbackPrefab = ObjectDB.instance?.GetItemPrefab(rule.DropPrefab);
        var fallbackDrop = fallbackPrefab?.GetComponent<ItemDrop>();
        if (fallbackDrop?.m_itemData is null)
        {
            return null;
        }

        var fallbackData = fallbackDrop.m_itemData.Clone();
        fallbackData.m_dropPrefab = fallbackPrefab;
        fallbackData.m_quality = Mathf.Clamp(
            sourceWeapon.m_quality,
            1,
            fallbackData.m_shared?.m_maxQuality ?? 1);
        fallbackData.m_durability = Mathf.Min(
            sourceWeapon.m_durability,
            fallbackData.GetMaxDurability());
        fallbackData.m_variant = sourceWeapon.m_variant;
        fallbackData.m_stack = 1;
        return fallbackData;
    }

    public ItemDrop.ItemData? CreatePlayerDropData(ItemDrop.ItemData sourceWeapon)
    {
        return sourceWeapon.m_dropPrefab?.GetComponent<ItemDrop>() is not null
            ? sourceWeapon.Clone()
            : null;
    }

    public void RestoreEnemyCombatData(
        ItemDrop.ItemData returnedWeapon,
        ItemDrop.ItemData originalCombatWeapon)
    {
        // The ground item is a player-usable substitute. MonsterAI must retain
        // the original enemy attack, damage, range, angle and attack interval.
        // Assign SharedData; never mutate the shared player prefab settings.
        returnedWeapon.m_shared = originalCombatWeapon.m_shared;
        returnedWeapon.m_quality = originalCombatWeapon.m_quality;
        returnedWeapon.m_durability = originalCombatWeapon.m_durability;
        returnedWeapon.m_variant = originalCombatWeapon.m_variant;
    }

    public static bool IsEnemyWeapon(ItemDrop.ItemData? item)
    {
        return item?.m_shared?.m_itemType is
            ItemDrop.ItemData.ItemType.OneHandedWeapon
            or ItemDrop.ItemData.ItemType.TwoHandedWeapon
            or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
            or ItemDrop.ItemData.ItemType.Attach_Atgeir;
    }

    public static bool IsPlayerDisarmableWeapon(ItemDrop.ItemData? item)
    {
        return IsEnemyWeapon(item)
            || item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow;
    }

    private static Skills.SkillType GetSkillType(ItemDrop.ItemData item)
    {
        if (item.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield)
        {
            return Skills.SkillType.Blocking;
        }

        return item.m_shared?.m_skillType ?? Skills.SkillType.Unarmed;
    }

    private static float GetSkillMultiplier(float skillLevel, bool offensive)
    {
        const float NeutralSkillLevel = 25f;
        const float MaximumSkillLevel = 100f;
        const float MinimumMultiplier = 0.75f;
        const float MaximumMultiplier = 1.25f;

        skillLevel = Mathf.Clamp(skillLevel, 0f, MaximumSkillLevel);
        if (skillLevel <= NeutralSkillLevel)
        {
            var lowerProgress = skillLevel / NeutralSkillLevel;
            return offensive
                ? Mathf.Lerp(MinimumMultiplier, 1f, lowerProgress)
                : Mathf.Lerp(MaximumMultiplier, 1f, lowerProgress);
        }

        var upperProgress = (skillLevel - NeutralSkillLevel)
            / (MaximumSkillLevel - NeutralSkillLevel);
        return offensive
            ? Mathf.Lerp(1f, MaximumMultiplier, upperProgress)
            : Mathf.Lerp(1f, MinimumMultiplier, upperProgress);
    }
}
