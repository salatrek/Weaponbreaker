using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using Weaponbreaker.Breaker.Configuration;
using Weaponbreaker.Breaker.Services.Disarming;
using Weaponbreaker.Breaker.Services.Weapons;

namespace Weaponbreaker.Breaker.Detection;

/// <summary>
/// Thin Harmony adapter from Valheim callbacks to the combat and state services.
/// </summary>
internal static class WeaponbreakerPatches
{
    private static BattleService? _battleService;
    private static WeaponStateService? _weaponState;
    private static WeaponbreakerConfigSync? _configSync;
    private static ManualLogSource? _logger;

    public static void Initialize(
        BattleService battleService,
        WeaponStateService weaponState,
        WeaponbreakerConfigSync configSync,
        ManualLogSource logger)
    {
        _battleService = battleService
            ?? throw new ArgumentNullException(nameof(battleService));
        _weaponState = weaponState
            ?? throw new ArgumentNullException(nameof(weaponState));
        _configSync = configSync
            ?? throw new ArgumentNullException(nameof(configSync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static void Shutdown()
    {
        _battleService = null;
        _weaponState = null;
        _configSync = null;
        _logger = null;
    }

    public static void ApplyPatches(Harmony harmony)
    {
        Patch(
            harmony,
            AccessTools.Method(
                typeof(Humanoid),
                "BlockAttack",
                new[] { typeof(HitData), typeof(Character) }),
            nameof(HumanoidBlockAttackPostfix),
            transpilerName: nameof(HumanoidBlockAttackTranspiler));

        Patch(
            harmony,
            AccessTools.Method(
                typeof(Character),
                nameof(Character.Damage),
                new[] { typeof(HitData) }),
            null,
            nameof(CharacterDamagePrefix));

        Patch(
            harmony,
            AccessTools.Method(typeof(Character), "RPC_Damage", new[] { typeof(long), typeof(HitData) }),
            nameof(CharacterRpcDamagePostfix),
            nameof(CharacterRpcDamagePrefix));

        Patch(
            harmony,
            AccessTools.Method(
                typeof(MonsterAI),
                nameof(MonsterAI.UpdateAI),
                new[] { typeof(float) }),
            null,
            nameof(MonsterAIUpdateAIPrefix));

        Patch(
            harmony,
            AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.Load)),
            nameof(ItemDropLoadPostfix));

        Patch(
            harmony,
            AccessTools.Method(typeof(ZNet), "Start"),
            nameof(ZNetStartPostfix));

        Patch(
            harmony,
            AccessTools.Method(typeof(ZNet), "OnNewConnection"),
            nameof(ZNetOnNewConnectionPostfix));

        Patch(
            harmony,
            AccessTools.Method(typeof(ZNet), "Update"),
            nameof(ZNetUpdatePostfix));
    }

    private static void Patch(
        Harmony harmony,
        System.Reflection.MethodBase? original,
        string? postfixName,
        string? prefixName = null,
        string? transpilerName = null)
    {
        if (original is null)
        {
            _logger?.LogWarning(
                $"Could not find a patch target for '{postfixName ?? prefixName ?? "unknown"}'.");
            return;
        }

        var postfix = postfixName is null
            ? null
            : new HarmonyMethod(typeof(WeaponbreakerPatches), postfixName);
        var prefix = prefixName is null
            ? null
            : new HarmonyMethod(typeof(WeaponbreakerPatches), prefixName);
        var transpiler = transpilerName is null
            ? null
            : new HarmonyMethod(typeof(WeaponbreakerPatches), transpilerName);
        harmony.Patch(original, prefix, postfix, transpiler);
    }

    private static IEnumerable<CodeInstruction> HumanoidBlockAttackTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var blockDamage = typeof(HitData).GetMethod(nameof(HitData.BlockDamage), new[] { typeof(float) });
        var record = typeof(WeaponbreakerPatches).GetMethod(nameof(RecordSuccessfulBlock),
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = new List<CodeInstruction>();
        var matches = 0;
        foreach (var instruction in instructions)
        {
            result.Add(instruction);
            if (!instruction.Calls(blockDamage))
            {
                continue;
            }

            // Vanilla reaches BlockDamage only after stamina and stagger checks
            // succeed. Its BlockAttack return value alone does not prove success.
            result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Ldarg_1));
            result.Add(new CodeInstruction(OpCodes.Call, record));
            matches++;
        }

        if (matches != 1)
        {
            throw new InvalidOperationException("Could not identify the unique successful block branch in Humanoid.BlockAttack.");
        }

        return result;
    }

    private static void RecordSuccessfulBlock(Humanoid defender, HitData hit)
    {
        var blocker = AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker")
            .Invoke(defender, null) as ItemDrop.ItemData;
        if (blocker is not null)
        {
            BlockOutcomeTracker.Record(defender, hit, blocker);
        }
    }

    private static void HumanoidBlockAttackPostfix(
        Humanoid __instance,
        HitData hit,
        Character attacker,
        bool __result,
        float ___m_blockTimer)
    {
        const float VanillaPerfectBlockIntervalSeconds = 0.25f;

        if (_battleService is null
            || !__result
            || hit is null
            || !BlockOutcomeTracker.WasBlockedBy(__instance, hit)
            || attacker is null
            || ___m_blockTimer < 0f
            || ___m_blockTimer >= VanillaPerfectBlockIntervalSeconds)
        {
            return;
        }

        var getCurrentBlocker = AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker");
        var blocker = getCurrentBlocker?.Invoke(__instance, null) as ItemDrop.ItemData;
        if (blocker?.m_shared is null
            || blocker.m_shared.m_timedBlockBonus <= 1f
            || hit.m_ranged)
        {
            return;
        }

        _logger?.LogInfo(
            $"Perfect parry detected: attacker='{attacker.name}', blockerType={blocker.m_shared.m_itemType}, blockTimer={___m_blockTimer:F3}.");

        try
        {
            _battleService.OnPerfectParry(__instance, hit, attacker, blocker);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Weaponbreaker parry handling failed: {exception}");
        }
    }

    private static void CharacterDamagePrefix(
        HitData hit)
    {
        _weaponState?.ScaleDisarmedEnemyDamage(hit);
    }

    private static void CharacterRpcDamagePrefix(
        Character __instance,
        HitData hit,
        out float __state)
    {
        __state = __instance.GetHealth();
        if (hit is not null)
        {
            BlockOutcomeTracker.Clear(hit);
        }
    }

    private static void CharacterRpcDamagePostfix(
        Character __instance,
        HitData hit,
        float __state)
    {
        if (hit is null)
        {
            return;
        }

        var blockedByShield = BlockOutcomeTracker.WasBlockedByShield(__instance, hit);
        BlockOutcomeTracker.Clear(hit);
        if (_battleService is null
            || __instance.GetHealth() >= __state - 0.001f)
        {
            return;
        }

        try
        {
            var attacker = hit.GetAttacker();
            if (attacker is not null)
            {
                _battleService.OnHealthReduced(__instance, hit, attacker, blockedByShield);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Weaponbreaker damage handling failed: {exception}");
        }
    }

    private static bool MonsterAIUpdateAIPrefix(MonsterAI __instance)
    {
        if (_weaponState is null)
        {
            return true;
        }

        var humanoid = __instance.GetComponent<Humanoid>();
        return humanoid is null || !_weaponState.TryReturnToDroppedWeapon(humanoid);
    }

    private static void ItemDropLoadPostfix(ItemDrop __instance)
    {
        try
        {
            _weaponState?.ApplyPickupPolicy(__instance);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Weaponbreaker item marker handling failed: {exception}");
        }
    }

    private static void ZNetStartPostfix()
    {
        _configSync?.Initialize();
    }

    private static void ZNetOnNewConnectionPostfix(ZNetPeer peer)
    {
        _configSync?.OnNewConnection(peer);
    }

    private static void ZNetUpdatePostfix()
    {
        _configSync?.Tick();
    }
}
