using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Weaponbreaker.Breaker.Configuration;
using Weaponbreaker.Breaker.Rules;

namespace Weaponbreaker.Breaker.Services;

/// <summary>
/// Owns the persistent state of weapon drops, pickup policy and enemy return behavior.
/// </summary>
internal sealed class WeaponStateService
{
    private const double EnemyPickupSearchIntervalSeconds = 0.5d;
    private const float EnemyPickupDistance = 1.5f;
    private const float CombatTargetRestoreRadius = 30f;
    private const float DisarmedFallbackDamageMultiplier = 0.15f;

    private const string DisarmedWeaponKey = "weaponbreaker.disarmed";
    private const string PlayerDisarmedWeaponKey = "weaponbreaker.playerDisarmed";
    private const string SourceEnemyKey = "weaponbreaker.sourceEnemy";
    private const string DisarmedByEnemyKey = "weaponbreaker.disarmedByEnemy";
    private const string PickupLockedUntilKey = "weaponbreaker.pickupLockedUntil";
    private const string EnemyDisarmCooldownUntilKey = "weaponbreaker.disarmCooldownUntil";
    private const string PlayerDisarmCooldownUntilKey = "weaponbreaker.playerDisarmCooldownUntil";
    private const string PendingDropUserKey = "weaponbreaker.pendingDropUser";
    private const string PendingDropIdKey = "weaponbreaker.pendingDropId";
    private const string PendingReturnPositionKey = "weaponbreaker.pendingReturnPosition";
    private const string NextPickupAttemptKey = "weaponbreaker.nextPickupAttempt";

    private readonly WeaponbreakerOptions _configuration;
    private readonly WeaponProvider _weaponProvider;
    private readonly ManualLogSource _logger;
    private readonly Dictionary<string, Character> _combatTargets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _returningEnemies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ItemDrop.ItemData> _fallbackWeapons = new(StringComparer.Ordinal);

    public WeaponStateService(
        WeaponbreakerOptions configuration,
        WeaponProvider weaponProvider,
        ManualLogSource logger)
    {
        _configuration = configuration;
        _weaponProvider = weaponProvider;
        _logger = logger;
    }

    public bool TryDropEnemyWeapon(
        Humanoid enemy,
        ItemDrop.ItemData weapon,
        WeaponRule rule)
    {
        var enemyZdo = GetZdo(enemy);
        var now = GetNetworkTimeSeconds();
        if (enemyZdo is null
            || enemyZdo.GetFloat(EnemyDisarmCooldownUntilKey, 0f) > now)
        {
            return false;
        }

        var enemyKey = enemy.GetZDOID().ToString();
        var combatTarget = enemy.GetBaseAI()?.GetTargetCreature();
        if (combatTarget is not null && !combatTarget.IsDead())
        {
            _combatTargets[enemyKey] = combatTarget;
        }

        var dropData = _weaponProvider.CreateEnemyDropData(weapon, rule);
        if (dropData?.m_dropPrefab is null)
        {
            _logger.LogWarning(
                $"Could not create a registered weapon drop for '{enemy.name}' / '{rule.DropPrefab}'.");
            return false;
        }

        var droppedWeapon = ItemDrop.DropItem(
            dropData,
            1,
            enemy.transform.position + Vector3.up * 0.5f,
            Quaternion.identity);
        if (droppedWeapon is null)
        {
            return false;
        }

        enemy.UnequipItem(weapon, false);
        enemy.GetInventory()?.RemoveItem(weapon);
        InstallDisarmedFallbackWeapon(enemy, weapon);
        enemyZdo.Set(
            EnemyDisarmCooldownUntilKey,
            (float)(now + _configuration.Cooldowns.EnemyDisarmSeconds));

        var unarmedWeapon = GetUnarmedWeaponData(enemy);
        _logger.LogInfo(
            $"Enemy '{enemy.name}' weapon damage: {FormatDamage(weapon)}; "
            + $"unarmed damage: {FormatDamage(unarmedWeapon)}; "
            + $"fallbackMultiplier={DisarmedFallbackDamageMultiplier:F2}, "
            + $"effectiveFallbackTotal={weapon.GetDamage().GetTotalDamage() * DisarmedFallbackDamageMultiplier:F1}, "
            + $"dropPrefab='{rule.DropPrefab}'.");

        MarkEnemyDrop(droppedWeapon, enemy, now, enemyZdo);
        var monsterAi = enemy.GetComponent<MonsterAI>();
        if (monsterAi is not null
            && combatTarget is not null
            && !combatTarget.IsDead())
        {
            ResetAttackState(monsterAi);
            SetMonsterTarget(monsterAi, combatTarget);
            SetMonsterAlerted(monsterAi, true);
        }
        return true;
    }

    public bool TryDropPlayerWeapon(
        Player player,
        Humanoid disarmingEnemy)
    {
        var playerZdo = GetZdo(player);
        var now = GetNetworkTimeSeconds();
        if (playerZdo is null
            || playerZdo.GetFloat(PlayerDisarmCooldownUntilKey, 0f) > now)
        {
            return false;
        }

        var weapon = player.GetCurrentWeapon();
        var dropData = weapon is null
            ? null
            : _weaponProvider.CreatePlayerDropData(weapon);
        if (weapon is null || dropData?.m_dropPrefab is null)
        {
            return false;
        }

        var droppedWeapon = ItemDrop.DropItem(
            dropData,
            1,
            player.transform.position + Vector3.up * 0.5f,
            Quaternion.identity);
        if (droppedWeapon is null)
        {
            return false;
        }

        player.UnequipItem(weapon, false);
        player.GetInventory()?.RemoveItem(weapon);
        playerZdo.Set(
            PlayerDisarmCooldownUntilKey,
            (float)(now + _configuration.Cooldowns.PlayerDisarmSeconds));
        MarkPlayerDrop(droppedWeapon, disarmingEnemy, now);
        return true;
    }

    public void ApplyPickupPolicy(ItemDrop itemDrop)
    {
        var zdo = GetZdo(itemDrop);
        if (zdo is not null
            && (zdo.GetBool(DisarmedWeaponKey, false)
                || zdo.GetBool(PlayerDisarmedWeaponKey, false)))
        {
            itemDrop.m_autoPickup = false;
        }
    }

    public void ScaleDisarmedEnemyDamage(HitData hit)
    {
        if (hit?.GetAttacker() is not Humanoid enemy
            || !_fallbackWeapons.ContainsKey(enemy.GetZDOID().ToString()))
        {
            return;
        }

        hit.m_damage.m_blunt *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_slash *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_pierce *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_chop *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_pickaxe *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_fire *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_frost *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_lightning *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_poison *= DisarmedFallbackDamageMultiplier;
        hit.m_damage.m_spirit *= DisarmedFallbackDamageMultiplier;
    }

    public bool HasDisarmedFallbackWeapon(Humanoid enemy)
    {
        return _fallbackWeapons.ContainsKey(enemy.GetZDOID().ToString());
    }

    public bool TryReturnToDroppedWeapon(Humanoid enemy)
    {
        if (!_configuration.Enabled
            || !_configuration.EnemyDisarmEnabled
            || enemy is null
            || enemy.IsPlayer()
            || enemy.IsDead()
            || !enemy.IsOwner()
            || (WeaponProvider.IsEnemyWeapon(enemy.GetCurrentWeapon())
                && !_fallbackWeapons.ContainsKey(enemy.GetZDOID().ToString())))
        {
            return false;
        }

        var enemyZdo = GetZdo(enemy);
        if (enemyZdo is null)
        {
            return false;
        }

        var monsterAi = enemy.GetComponent<MonsterAI>();
        if (monsterAi is null)
        {
            return false;
        }

        var baseAi = enemy.GetBaseAI();
        if (baseAi is null)
        {
            return false;
        }

        var enemyKey = enemy.GetZDOID().ToString();

        var pendingUser = enemyZdo.GetLong(PendingDropUserKey, 0L);
        var pendingId = enemyZdo.GetInt(PendingDropIdKey, 0);
        if (pendingUser == 0L && pendingId == 0)
        {
            return false;
        }

        var savedTarget = GetSavedCombatTarget(enemyKey);
        if (!_returningEnemies.Contains(enemyKey)
            && savedTarget is not null
            && CanRestoreCombatTarget(enemy, savedTarget))
        {
            // Keep the player as the active target during the pickup lock. This
            // prevents vanilla AI from wandering away just because it is unarmed.
            SetMonsterTarget(monsterAi, savedTarget);
        }

        var now = GetNetworkTimeSeconds();
        if (enemyZdo.GetFloat(NextPickupAttemptKey, 0f) > now)
        {
            return _returningEnemies.Contains(enemyKey);
        }

        enemyZdo.Set(NextPickupAttemptKey, (float)(now + EnemyPickupSearchIntervalSeconds));
        var dropId = new ZDOID(pendingUser, unchecked((uint)pendingId));
        var dropObject = ZNetScene.instance?.FindInstance(dropId);
        var droppedWeapon = dropObject?.GetComponent<ItemDrop>();
        if (droppedWeapon is null)
        {
            // The player may have picked up the exact pending item while the enemy
            // was returning. Restore the suspended combat target immediately.
            RestoreCombatTarget(enemy, monsterAi);
            ClearPendingDrop(enemyZdo);
            return false;
        }

        var dropZdo = GetZdo(droppedWeapon);
        if (dropZdo is null
            || !dropZdo.GetBool(DisarmedWeaponKey, false)
            || !string.Equals(
                dropZdo.GetString(SourceEnemyKey, string.Empty),
                enemy.GetZDOID().ToString(),
                StringComparison.Ordinal))
        {
            RestoreCombatTarget(enemy, monsterAi);
            ClearPendingDrop(enemyZdo);
            return false;
        }

        if (dropZdo.GetFloat(PickupLockedUntilKey, 0f) > now)
        {
            return false;
        }

        if (!_returningEnemies.Contains(enemyKey))
        {
            if (savedTarget is null)
            {
                savedTarget = enemy.GetBaseAI()?.GetTargetCreature();
                if (savedTarget is not null && !savedTarget.IsDead())
                {
                    _combatTargets[enemyKey] = savedTarget;
                }
            }

            _returningEnemies.Add(enemyKey);
            if (savedTarget is not null && !savedTarget.IsDead())
            {
                _logger.LogInfo(
                    $"Enemy '{enemy.name}' temporarily switched target to its dropped weapon.");
            }
        }

        // MonsterAI has no native ItemDrop target, so suspend its character target
        // while this service drives the return movement to the exact dropped item.
        SetMonsterTarget(monsterAi, null);

        var returnPosition = enemyZdo.GetVec3(
            PendingReturnPositionKey,
            droppedWeapon.transform.position);
        if (Vector3.Distance(enemy.transform.position, returnPosition) > EnemyPickupDistance)
        {
            var moveTo = AccessTools.Method(
                typeof(BaseAI),
                "MoveTo",
                new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) });
            moveTo?.Invoke(
                baseAi,
                new object[] { Time.deltaTime, returnPosition, EnemyPickupDistance, true });
            return true;
        }

        if (!droppedWeapon.CanPickup(false))
        {
            droppedWeapon.RequestOwn();
            return true;
        }

        var fallbackWeapon = RemoveDisarmedFallbackWeapon(enemy);
        var pickedWeapon = droppedWeapon.m_itemData;
        if (enemy.Pickup(droppedWeapon.gameObject, true, false))
        {
            EquipPickedWeapon(enemy, pickedWeapon, fallbackWeapon);
            RestoreCombatTarget(enemy, monsterAi);
            ClearPendingDrop(enemyZdo);
            _logger.LogInfo($"Enemy '{enemy.name}' returned and picked up its weapon.");
            return false;
        }
        else if (fallbackWeapon is not null)
        {
            InstallDisarmedFallbackWeapon(enemy, fallbackWeapon);
        }

        return true;
    }

    private void MarkEnemyDrop(
        ItemDrop droppedWeapon,
        Humanoid enemy,
        double now,
        ZDO enemyZdo)
    {
        droppedWeapon.m_autoPickup = false;
        var dropZdo = GetZdo(droppedWeapon);
        if (dropZdo is null)
        {
            return;
        }

        var enemyId = enemy.GetZDOID();
        var dropId = GetZdoId(dropZdo);
        dropZdo.Set(DisarmedWeaponKey, true);
        dropZdo.Set(SourceEnemyKey, enemyId.ToString());
        dropZdo.Set(
            PickupLockedUntilKey,
            (float)(now + _configuration.Cooldowns.EnemyWeaponPickupSeconds));

        enemyZdo.Set(PendingDropUserKey, (long)dropId.UserID);
        enemyZdo.Set(PendingDropIdKey, unchecked((int)dropId.ID));
        enemyZdo.Set(PendingReturnPositionKey, droppedWeapon.transform.position);
    }

    private static void MarkPlayerDrop(
        ItemDrop droppedWeapon,
        Humanoid enemy,
        double now)
    {
        droppedWeapon.m_autoPickup = false;
        var zdo = GetZdo(droppedWeapon);
        if (zdo is null)
        {
            return;
        }

        zdo.Set(DisarmedWeaponKey, true);
        zdo.Set(PlayerDisarmedWeaponKey, true);
        zdo.Set(DisarmedByEnemyKey, enemy.GetZDOID().ToString());
        zdo.Set(PickupLockedUntilKey, (float)now);
    }

    private static void ClearPendingDrop(ZDO enemyZdo)
    {
        enemyZdo.Set(PendingDropUserKey, 0L);
        enemyZdo.Set(PendingDropIdKey, 0);
        enemyZdo.Set(PendingReturnPositionKey, Vector3.zero);
        enemyZdo.Set(NextPickupAttemptKey, 0f);
    }

    private Character? GetSavedCombatTarget(string enemyKey)
    {
        if (!_combatTargets.TryGetValue(enemyKey, out var target))
        {
            return null;
        }

        if (target is null || target.IsDead())
        {
            _combatTargets.Remove(enemyKey);
            return null;
        }

        return target;
    }

    private void RestoreCombatTarget(Humanoid enemy, MonsterAI monsterAi)
    {
        var enemyKey = enemy.GetZDOID().ToString();
        _returningEnemies.Remove(enemyKey);
        var target = GetSavedCombatTarget(enemyKey);
        _combatTargets.Remove(enemyKey);

        if (target is not null
            && !target.IsDead()
            && CanRestoreCombatTarget(enemy, target))
        {
            ResetAttackState(monsterAi);
            SetMonsterTarget(monsterAi, target);
            SetMonsterAlerted(monsterAi, true);
            _logger.LogInfo($"Enemy '{enemy.name}' restored its previous combat target.");
        }
        else if (target is not null && !target.IsDead())
        {
            _logger.LogInfo(
                $"Enemy '{enemy.name}' did not restore its previous target because it is beyond {CombatTargetRestoreRadius} metres.");
        }
    }

    private static void SetMonsterTarget(MonsterAI monsterAi, Character? target)
    {
        // Vanilla SetTarget only accepts a non-null target when no target is
        // already assigned. It cannot suspend or replace an existing target.
        AccessTools.Field(typeof(MonsterAI), "m_targetCreature").SetValue(monsterAi, target);
        AccessTools.Field(typeof(MonsterAI), "m_targetStatic").SetValue(monsterAi, null);
        if (target is not null)
        {
            AccessTools.Field(typeof(MonsterAI), "m_lastKnownTargetPos")
                .SetValue(monsterAi, target.transform.position);
            AccessTools.Field(typeof(MonsterAI), "m_beenAtLastPos").SetValue(monsterAi, false);
        }
    }

    private static void SetMonsterAlerted(MonsterAI monsterAi, bool alerted)
    {
        var setAlerted = AccessTools.Method(
            typeof(MonsterAI),
            "SetAlerted",
            new[] { typeof(bool) });
        setAlerted?.Invoke(monsterAi, new object[] { alerted });
    }

    private static ItemDrop.ItemData? GetUnarmedWeaponData(Humanoid enemy)
    {
        var unarmedWeapon = AccessTools.Field(typeof(Humanoid), "m_unarmedWeapon")?.GetValue(enemy)
            as ItemDrop;
        return unarmedWeapon?.m_itemData;
    }

    private void InstallDisarmedFallbackWeapon(
        Humanoid enemy,
        ItemDrop.ItemData sourceWeapon)
    {
        var fallback = sourceWeapon.Clone();
        fallback.m_dropPrefab = null;
        fallback.m_stack = 1;
        var enemyKey = enemy.GetZDOID().ToString();
        _fallbackWeapons[enemyKey] = fallback;

        var inventory = enemy.GetInventory();
        if (inventory is not null && !inventory.ContainsItem(fallback))
        {
            inventory.AddItem(fallback);
        }

        var rightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        rightItem?.SetValue(enemy, fallback);
        RefreshEquipment(enemy);
    }

    private ItemDrop.ItemData? RemoveDisarmedFallbackWeapon(Humanoid enemy)
    {
        var enemyKey = enemy.GetZDOID().ToString();
        if (!_fallbackWeapons.TryGetValue(enemyKey, out var fallback))
        {
            return null;
        }

        _fallbackWeapons.Remove(enemyKey);
        enemy.GetInventory()?.RemoveItem(fallback);
        AccessTools.Field(typeof(Humanoid), "m_rightItem")?.SetValue(enemy, null);
        RefreshEquipment(enemy);
        return fallback;
    }

    private static void RefreshEquipment(Humanoid enemy)
    {
        var updateEquipment = AccessTools.Method(
            typeof(Humanoid),
            "UpdateEquipment",
            new[] { typeof(float) });
        updateEquipment?.Invoke(enemy, new object[] { 0f });
    }

    private void EquipPickedWeapon(
        Humanoid enemy,
        ItemDrop.ItemData? pickedWeapon,
        ItemDrop.ItemData? originalCombatWeapon)
    {
        if (pickedWeapon is null)
        {
            return;
        }

        var inventory = enemy.GetInventory();
        var inventoryWeapon = inventory?.GetAllItems()
            .FirstOrDefault(item => ReferenceEquals(item, pickedWeapon));
        var weaponToEquip = inventoryWeapon ?? pickedWeapon;
        if (originalCombatWeapon is not null)
        {
            _weaponProvider.RestoreEnemyCombatData(weaponToEquip, originalCombatWeapon);
        }
        var equipResult = true;
        if (!ReferenceEquals(enemy.GetCurrentWeapon(), weaponToEquip))
        {
            equipResult = enemy.EquipItem(weaponToEquip, false);
        }

        if (!ReferenceEquals(enemy.GetCurrentWeapon(), weaponToEquip))
        {
            AccessTools.Field(typeof(Humanoid), "m_rightItem")?.SetValue(enemy, weaponToEquip);
        }

        RefreshEquipment(enemy);
        _logger.LogInfo(
            $"Enemy '{enemy.name}' equipped returned weapon: "
            + $"name='{weaponToEquip.m_shared?.m_name}', "
            + $"prefab='{weaponToEquip.m_dropPrefab?.name}', "
            + $"equipResult={equipResult}, "
            + $"primaryAttack={weaponToEquip.HavePrimaryAttack()}, "
            + $"originalCombatData={originalCombatWeapon is not null}, "
            + $"aiRange={weaponToEquip.m_shared?.m_aiAttackRange}, "
            + $"aiAngle={weaponToEquip.m_shared?.m_aiAttackMaxAngle}, "
            + $"damage={weaponToEquip.GetDamage().GetTotalDamage()}.");
    }

    private static void ResetAttackState(MonsterAI monsterAi)
    {
        AccessTools.Field(typeof(MonsterAI), "m_unableToAttackTargetTimer")?.SetValue(monsterAi, 0f);
        AccessTools.Field(typeof(MonsterAI), "m_timeSinceAttacking")?.SetValue(monsterAi, 0f);
        AccessTools.Field(typeof(MonsterAI), "m_updateWeaponTimer")?.SetValue(monsterAi, 0f);
        AccessTools.Field(typeof(MonsterAI), "m_updateTargetTimer")?.SetValue(monsterAi, 0f);
        AccessTools.Field(typeof(MonsterAI), "m_timeSinceSensedTargetCreature")?.SetValue(monsterAi, 0f);
    }

    private static bool CanRestoreCombatTarget(
        Humanoid enemy,
        Character target)
    {
        if (Vector3.Distance(enemy.transform.position, target.transform.position)
            > CombatTargetRestoreRadius)
        {
            return false;
        }

        return true;
    }

    private static string FormatDamage(ItemDrop.ItemData? item)
    {
        if (item?.m_shared is null)
        {
            return "none";
        }

        var damage = item.GetDamage();
        var attackMultiplier = item.m_shared.m_attack?.m_damageMultiplier ?? 1f;
        return $"name='{item.m_shared.m_name}', total={damage.GetTotalDamage():F1}, "
            + $"blunt={damage.m_blunt:F1}, slash={damage.m_slash:F1}, pierce={damage.m_pierce:F1}, "
            + $"fire={damage.m_fire:F1}, frost={damage.m_frost:F1}, poison={damage.m_poison:F1}, "
            + $"spirit={damage.m_spirit:F1}, attackMultiplier={attackMultiplier:F2}";
    }

    private static ZDO? GetZdo(Character character)
    {
        var view = AccessTools.Field(typeof(Character), "m_nview")?.GetValue(character) as ZNetView;
        return view?.GetZDO();
    }

    private static ZDO? GetZdo(ItemDrop itemDrop)
    {
        var view = AccessTools.Field(typeof(ItemDrop), "m_nview")?.GetValue(itemDrop) as ZNetView;
        return view?.GetZDO();
    }

    private static ZDOID GetZdoId(ZDO zdo)
    {
        var field = AccessTools.Field(typeof(ZDO), "m_uid");
        return field?.GetValue(zdo) is ZDOID id ? id : new ZDOID();
    }

    private static double GetNetworkTimeSeconds()
    {
        return ZNet.instance?.GetTimeSeconds() ?? Time.time;
    }
}
