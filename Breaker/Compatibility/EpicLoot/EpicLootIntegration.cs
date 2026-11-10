using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using Weaponbreaker.Breaker.Configuration;

namespace Weaponbreaker.Breaker.Compatibility.EpicLoot;

/// <summary>
/// Optional EpicLoot bridge. Only this class knows its runtime types; Weaponbreaker
/// can load without EpicLoot.dll. Table planning has no supported API yet, so the
/// small reflection contract is checked once and fails closed on incompatible versions.
/// </summary>
internal sealed class EpicLootIntegration
{
    internal const string PluginGuid = "randyknapp.mods.epicloot";
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.Instance;

    private readonly ManualLogSource _logger;
    private readonly Dictionary<string, MethodInfo> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<(Type, string), MemberInfo> _members = new();
    private readonly FieldInfo _globalDropRate;
    private readonly FieldInfo _itemSets;
    private readonly MethodInfo _promoteRarity;
    private readonly Type _rarityType;
    private readonly Func<float> _random;
    private bool _failed;

    internal EpicLootIntegration(Assembly assembly, ManualLogSource logger, Func<float> random)
    {
        _logger = logger;
        _random = random;
        Bind(assembly, "EpicLoot.API", "CanBeMagicItem", 1);
        Bind(assembly, "EpicLoot.API", "TryMakeMagicItem", 4);
        Bind(assembly, "EpicLoot.API", "GetLuckFactor", 1);
        Bind(assembly, "EpicLoot.EpicLoot", "GetCharacterCleanName", 1);
        Bind(assembly, "EpicLoot.EpicLoot", "GetGatedItemTypeMode", 0);
        Bind(assembly, "EpicLoot.GatedItemType.GatedItemTypeHelper", "GetGatedItemNameFromItemOrType", 2);
        Bind(assembly, "EpicLoot.LootDenyList", "IsDenied", 1);
        Bind(assembly, "EpicLoot.LootRoller", "GetLootTable", 1);
        Bind(assembly, "EpicLoot.LootRoller", "PlanRoll", 3);
        Bind(assembly, "EpicLoot.LootRoller", "ToDropList", 1);
        Bind(assembly, "EpicLoot.LootRoller", "ResolveLootDrop", 4);
        Bind(assembly, "EpicLoot.LootRoller", "SelectDropType", 3);
        Bind(assembly, "EpicLoot.LootRoller", "GetRarityWeights", 2);
        Bind(assembly, "EpicLoot.LootRoller", "TryResolveTableReference", 4);
        Bind(assembly, "EpicLoot.LootRoller", "SelectRarityItem", 3);
        Bind(assembly, "EpicLoot.LootScalingMath", "ScaleDropChance", 2);
        Bind(assembly, "EpicLoot.LootScalingMath", "ApplyDropRate", 2);
        Bind(assembly, "EpicLoot.LootScalingMath", "AddBonusDrops", 4);
        Bind(assembly, "EpicLoot.LootScalingMath", "ScaledWeight", 2);
        Bind(assembly, "EpicLoot.Magic.MagicItemEffects.Prosperity", "RollBonusRolls", 1);

        _globalDropRate = assembly.GetType("EpicLoot.Config.ELConfig", true)!
            .GetField("GlobalDropRateModifier", StaticFlags)
            ?? throw new MissingFieldException("EpicLoot GlobalDropRateModifier");
        _itemSets = assembly.GetType("EpicLoot.LootRoller", true)!.GetField("ItemSets", StaticFlags)
            ?? throw new MissingFieldException("EpicLoot ItemSets");
        _promoteRarity = assembly.GetType("EpicLoot.LootScalingContext", true)!.GetMethod("Promote")
            ?? throw new MissingMethodException("EpicLoot Promote");
        _rarityType = assembly.GetType("EpicLoot.ItemRarity", true)!;
        // Validate every field/property used by the planner before enabling integration.
        RequireMembers(assembly, "EpicLoot.LootRollPlan", "DropsAnchor", "LootAnchor", "DropChanceAdd", "DropRate", "BonusDrops", "Scaling");
        RequireMembers(assembly, "EpicLoot.LeveledLootDef", "Drops", "Loot");
        RequireMembers(assembly, "EpicLoot.LootScalingContext", "LootSteps", "Scaling");
        RequireMembers(assembly, "EpicLoot.LootScalingMath+ScalingParams", "MaxDrops");
        RequireMembers(assembly, "EpicLoot.LootDrop", "Item", "Weight", "Rarity", "RarityItems");
        RequireMembers(assembly, "EpicLoot.LootItemSet", "Loot");
    }

    internal static EpicLootIntegration? Create(ManualLogSource logger)
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin)
            || plugin.Instance is null)
        {
            return null;
        }

        try
        {
            var integration = new EpicLootIntegration(plugin.Instance.GetType().Assembly, logger,
                () => UnityEngine.Random.value);
            logger.LogInfo($"EpicLoot {plugin.Metadata.Version} integration enabled; using its loaded loot tables.");
            return integration;
        }
        catch (Exception exception)
        {
            logger.LogWarning($"EpicLoot integration unavailable; ordinary weapon drops remain enabled. {exception.Message}");
            return null;
        }
    }

    internal ItemDrop.ItemData Enchant(Humanoid enemy, ItemDrop.ItemData ordinaryItem, EnemyOptions? options)
    {
        if (_failed || enemy.IsTamed())
        {
            return ordinaryItem;
        }

        try
        {
            if (!(bool)Call("CanBeMagicItem", ordinaryItem))
            {
                return ordinaryItem;
            }

            var position = enemy.GetCenterPoint();
            var name = (string)Call("GetCharacterCleanName", enemy);
            var luck = (float)Call("GetLuckFactor", position);
            var passes = options?.OverrideEpicLootEnchantChance == true
                ? 1 : 1 + Math.Max(0, (int)Call("RollBonusRolls", position));
            for (var pass = 0; pass < passes; pass++)
            {
                if (!TryRollConfiguredRarity(name, enemy.GetLevel(), luck, ResolveItem, options, out var rarity))
                {
                    continue;
                }

                // An API failure must never leave a partially enchanted ordinary item.
                return MakeMagicItem(ordinaryItem, rarity, luck);
            }
        }
        catch (Exception exception)
        {
            _failed = true;
            var reason = exception is TargetInvocationException invocation
                ? invocation.InnerException ?? exception
                : exception;
            _logger.LogWarning($"EpicLoot integration disabled for this session after a loot error; ordinary weapon drops remain enabled. {reason.Message}");
        }

        return ordinaryItem;
    }

    internal bool TryRollConfiguredRarity(string name, int level, float luck,
        Func<string, ItemDrop.ItemData?> resolveItem, EnemyOptions? options, out int rarity)
    {
        if (options?.OverrideEpicLootEnchantChance != true)
        {
            return TryRollRarity(name, level, luck, resolveItem, out rarity);
        }

        // Exactly one chance roll; never multiply by EpicLoot's drop chance again.
        rarity = -1;
        var chance = options.EpicLootEnchantChance ?? 0f;
        return !float.IsNaN(chance) && chance > 0f && (chance >= 1f || _random() < chance)
            && TryRollOverrideRarity(name, level, luck, resolveItem, out rarity);
    }

    private ItemDrop.ItemData MakeMagicItem(ItemDrop.ItemData ordinaryItem, int rarity, float luck)
    {
        var enchanted = ordinaryItem.Clone();
        return (bool)Call("TryMakeMagicItem", enchanted, rarity, luck, "") ? enchanted : ordinaryItem;
    }

    // The override supplies P(enchantment). Enumerate only eligible equipment so
    // P(no loot), materials and category ratios do not impose a second chance.
    // Each table contributes its conditional equipment rarity distribution.
    internal bool TryRollOverrideRarity(string name, int level, float luck,
        Func<string, ItemDrop.ItemData?> resolveItem, out int rarity)
    {
        var candidates = new List<(float Weight, float[] Rarity)>();
        foreach (var table in (IEnumerable)Call("GetLootTable", name))
        {
            var plan = Call("PlanRoll", table, level, name);
            var anchor = Read(plan, "LootAnchor");
            if (anchor is not null)
            {
                CollectEntries((IEnumerable)Read(anchor, "Loot")!, Read(plan, "Scaling")!, null,
                    1f, true, luck, resolveItem, candidates, 0);
            }
        }

        rarity = -1;
        return TryPick(candidates.ToArray(), candidate => candidate.Weight, out var selected)
            && TryPickRarity(selected.Rarity, luck, out rarity);
    }

    private void CollectEntries(IEnumerable source, object context, float[]? inheritedRarity,
        float share, bool scaleWeights, float luck, Func<string, ItemDrop.ItemData?> resolveItem,
        List<(float Weight, float[] Rarity)> candidates, int depth)
    {
        if (depth > 32) return;
        var entries = source.Cast<object>().ToArray();
        var weights = entries.Select(entry => Math.Max(0f, scaleWeights
            ? (float)Call("ScaledWeight", entry, Read(context, "LootSteps"))
            : (float)Read(entry, "Weight")!)).ToArray();
        var total = weights.Sum();
        if (total <= 0f) return;
        for (var index = 0; index < entries.Length; index++)
        {
            if (weights[index] <= 0f) continue;
            var entry = entries[index];
            var rarity = inheritedRarity;
            if (rarity is null && Read(entry, "Rarity") is float[] authored && authored.Length > 0)
            {
                rarity = (float[])_promoteRarity.Invoke(context, new object[] { authored });
            }

            var weight = share * weights[index] / total;
            var name = Read(entry, "Item") as string;
            if (Read(entry, "RarityItems") is IDictionary map && map.Count > 0)
            {
                var distribution = GetRarityWeights(rarity ?? new[] { 1f }, luck);
                var rarityTotal = distribution.Sum(pair => Math.Max(0f, (float)pair.Value));
                foreach (var pair in distribution)
                {
                    if ((float)pair.Value <= 0f || rarityTotal <= 0f) continue;
                    var arguments = new[] { (object)map, pair.Key, Enum.ToObject(_rarityType, 0) };
                    var mapped = _methods["SelectRarityItem"].Invoke(null, arguments) as string;
                    var single = new float[Enum.GetValues(_rarityType).Length];
                    single[Convert.ToInt32(arguments[2])] = 1f;
                    CollectName(string.IsNullOrEmpty(mapped) ? name : mapped, context, single,
                        weight * (float)pair.Value / rarityTotal, luck, resolveItem, candidates, depth + 1);
                }
            }
            else
            {
                CollectName(name, context, rarity, weight, luck, resolveItem, candidates, depth + 1);
            }
        }
    }

    private void CollectName(string? name, object context, float[]? rarity, float share, float luck,
        Func<string, ItemDrop.ItemData?> resolveItem, List<(float Weight, float[] Rarity)> candidates, int depth)
    {
        if (depth > 32 || string.IsNullOrEmpty(name)) return;
        var sets = (IDictionary)_itemSets.GetValue(null)!;
        if (sets.Contains(name))
        {
            CollectEntries((IEnumerable)Read(sets[name]!, "Loot")!, context, rarity, share,
                false, luck, resolveItem, candidates, depth + 1);
            return;
        }

        var arguments = new object?[] { name, context, null, null };
        if ((bool)_methods["TryResolveTableReference"].Invoke(null, arguments))
        {
            CollectEntries((IEnumerable)arguments[2]!, arguments[3]!, rarity, share,
                true, luck, resolveItem, candidates, depth + 1);
            return;
        }

        if (rarity is null || !rarity.Any(value => value > 0f) || (bool)Call("IsDenied", name)) return;
        var item = resolveItem(name!);
        if (item is not null && (bool)Call("CanBeMagicItem", item))
        {
            candidates.Add((share, rarity));
        }
    }

    private DictionaryEntry[] GetRarityWeights(float[] weights, float luck)
    {
        var entries = new List<DictionaryEntry>();
        var enumerator = ((IDictionary)Call("GetRarityWeights", weights, luck)).GetEnumerator();
        while (enumerator.MoveNext()) entries.Add(enumerator.Entry);
        return entries.ToArray();
    }

    private bool TryPickRarity(float[] weights, float luck, out int rarity)
    {
        rarity = -1;
        if (!TryPick(GetRarityWeights(weights, luck), pair => (float)pair.Value, out var picked)) return false;
        rarity = Convert.ToInt32(picked.Key);
        return true;
    }

    /// <summary>
    /// Samples the loaded tables without creating throwaway items or firing loot
    /// events. The first eligible equipment roll supplies the one weapon's rarity.
    /// Materials, shards and unidentified substitutions do not enchant the weapon.
    /// </summary>
    internal bool TryRollRarity(string name, int level, float luck,
        Func<string, ItemDrop.ItemData?> resolveItem, out int rarity)
    {
        rarity = -1;
        foreach (var table in (IEnumerable)Call("GetLootTable", name))
        {
            var plan = Call("PlanRoll", table, level, name);
            var dropsAnchor = Read(plan, "DropsAnchor");
            var lootAnchor = Read(plan, "LootAnchor");
            if (dropsAnchor is null || lootAnchor is null)
            {
                continue;
            }

            var drops = Call("ToDropList", Read(dropsAnchor, "Drops"));
            drops = Call("ScaleDropChance", drops, Read(plan, "DropChanceAdd"));
            var globalRate = (_globalDropRate.GetValue(null) as ConfigEntry<float>)?.Value ?? 1f;
            var rate = Math.Max(0f, Math.Min(4f, globalRate)) * (float)Read(plan, "DropRate")!;
            drops = Call("ApplyDropRate", drops, rate);
            var counts = ((IEnumerable<KeyValuePair<int, float>>)drops).ToArray();
            if (!TryPick(counts, pair => pair.Value, out var count))
            {
                continue;
            }

            var context = Read(plan, "Scaling")!;
            var scaling = Read(context, "Scaling")!;
            var dropCount = (int)Call("AddBonusDrops", count.Key, Read(plan, "BonusDrops"),
                _random(), Read(scaling, "MaxDrops"));
            var entries = ((IEnumerable)Read(lootAnchor, "Loot")!).Cast<object>().ToArray();
            // ResolveLootDrop uses EpicLoot's shared scratch collections. Materialize
            // our candidates first so resolving a set cannot change the parent roll.
            var weights = entries.Select(entry => (float)Call("ScaledWeight", entry, Read(context, "LootSteps"))).ToArray();
            for (var index = 0; index < dropCount; index++)
            {
                if (!TryPick(Enumerable.Range(0, entries.Length).ToArray(), i => weights[i], out var selected))
                {
                    break;
                }

                var resolved = Call("ResolveLootDrop", entries[selected], luck, true, context);
                var itemName = Read(resolved, "Item") as string;
                var rarities = Read(resolved, "Rarity") as float[];
                if (string.IsNullOrEmpty(itemName) || rarities is null || rarities.Length == 0
                    || (bool)Call("IsDenied", itemName))
                {
                    continue;
                }

                // Only enchantable equipment can supply a weapon enchantment, so
                // keepAsIs is false here. Honour the configured category ratios.
                var item = resolveItem(itemName!);
                if (item is null || !(bool)Call("CanBeMagicItem", item)
                    || !string.Equals(Call("SelectDropType", resolved, false, false).ToString(), "Item", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryPickRarity(rarities, luck, out rarity))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private ItemDrop.ItemData? ResolveItem(string name)
    {
        var gatedName = Call("GetGatedItemNameFromItemOrType", name, Call("GetGatedItemTypeMode")) as string;
        return string.IsNullOrEmpty(gatedName)
            ? null
            : ObjectDB.instance?.GetItemPrefab(gatedName)?.GetComponent<ItemDrop>()?.m_itemData;
    }

    private bool TryPick<T>(T[] entries, Func<T, float> weight, out T selected)
    {
        selected = default!;
        var total = entries.Sum(entry => Math.Max(0d, weight(entry)));
        if (total <= 0 || double.IsNaN(total) || double.IsInfinity(total))
        {
            return false;
        }

        var remaining = Math.Max(0d, Math.Min(1d, _random())) * total;
        foreach (var entry in entries)
        {
            var value = weight(entry);
            if (value <= 0)
            {
                continue;
            }

            selected = entry;
            remaining -= value;
            if (remaining < 0)
            {
                return true;
            }
        }

        return true; // Random.value may be exactly 1; use the last positive entry.
    }

    private void Bind(Assembly assembly, string typeName, string name, int parameterCount)
    {
        var type = assembly.GetType(typeName, true)!;
        _methods.Add(name, type.GetMethods(StaticFlags).Single(method => method.Name == name
            && method.GetParameters().Length == parameterCount));
    }

    private object Call(string name, params object?[] arguments) => _methods[name].Invoke(null, arguments)!;

    private void RequireMembers(Assembly assembly, string typeName, params string[] names)
    {
        var type = assembly.GetType(typeName, true)!;
        foreach (var name in names)
        {
            _members.Add((type, name), (MemberInfo?)type.GetField(name, InstanceFlags)
                ?? type.GetProperty(name, InstanceFlags)
                ?? throw new MissingMemberException(typeName, name));
        }
    }

    private object? Read(object instance, string name)
    {
        var member = _members[(instance.GetType(), name)];
        return member is FieldInfo field ? field.GetValue(instance) : ((PropertyInfo)member).GetValue(instance);
    }
}
