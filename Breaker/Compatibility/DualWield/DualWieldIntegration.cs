using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Weaponbreaker.Breaker.Compatibility.DualWield;

/// <summary>
/// Keeps the vanilla equipment refresh path intact when DualWield promotes an
/// off-hand weapon after the player's active weapon is removed.
/// </summary>
internal sealed class DualWieldIntegration
{
    internal const string PluginGuid = "org.bepinex.plugins.dualwield";

    private readonly ManualLogSource _logger;
    private readonly System.Reflection.MethodInfo? _updateEquipment;
    private readonly MethodInfo? _getSkillFactor;
    private readonly FieldInfo? _leftItem;
    private readonly FieldInfo? _rightItem;
    private bool _logged;

    internal DualWieldIntegration(ManualLogSource logger)
    {
        _logger = logger;
        _updateEquipment = AccessTools.Method(
            typeof(Humanoid),
            "UpdateEquipment",
            new[] { typeof(float) });

        _leftItem = AccessTools.Field(typeof(Humanoid), "m_leftItem");
        _rightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem");

        if (Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin)
            && plugin.Instance is not null)
        {
            var skillExtensions = plugin.Instance.GetType().Assembly.GetType("SkillManager.SkillExtensions");
            _getSkillFactor = skillExtensions?.GetMethod(
                "GetSkillFactor",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Character), typeof(string) },
                modifiers: null);
        }
    }

    internal bool IsInstalled => Chainloader.PluginInfos.ContainsKey(PluginGuid);

    internal bool TryGetDefensiveSkillMultiplier(
        Player player,
        ItemDrop.ItemData activeWeapon,
        out float multiplier)
    {
        multiplier = 1f;
        if (!IsInstalled || _getSkillFactor is null || _leftItem is null || _rightItem is null)
        {
            return false;
        }

        var left = _leftItem.GetValue(player) as ItemDrop.ItemData;
        var right = _rightItem.GetValue(player) as ItemDrop.ItemData;
        if (!IsDualWieldPair(left, right) || !ReferenceEquals(activeWeapon, left) && !ReferenceEquals(activeWeapon, right))
        {
            return false;
        }

        var skillName = ResolveSkillName(activeWeapon);
        try
        {
            if (!TryGetSkillFactor(player, skillName, out var skillFactor)
                && !TryGetSkillFactor(player, "Dual Offhand", out skillFactor))
            {
                return false;
            }

            multiplier = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(skillFactor));
            if (!_logged)
            {
                _logger.LogInfo("DualWield detected; dual-wield skill is used for player disarm calculations when two weapons are equipped.");
                _logged = true;
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"DualWield skill lookup failed; falling back to the ordinary weapon skill. {exception.Message}");
            return false;
        }
    }

    private bool TryGetSkillFactor(Player player, string skillName, out float factor)
    {
        factor = 0f;
        try
        {
            factor = Convert.ToSingle(_getSkillFactor!.Invoke(null, new object[] { player, skillName }));
            return !float.IsNaN(factor) && !float.IsInfinity(factor);
        }
        catch
        {
            return false;
        }
    }

    internal void RefreshAfterPlayerDisarm(Player player)
    {
        if (!IsInstalled || _updateEquipment is null)
        {
            return;
        }

        _updateEquipment.Invoke(player, new object[] { 0f });
        if (!_logged)
        {
            _logger.LogInfo(
                "DualWield detected; refreshed player equipment after disarm so the off-hand weapon can be promoted safely.");
            _logged = true;
        }
    }

    private static bool IsDualWieldPair(ItemDrop.ItemData? left, ItemDrop.ItemData? right)
    {
        return left?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon
            && right?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon
            && left.m_shared.m_skillType == right.m_shared.m_skillType;
    }

    private static string ResolveSkillName(ItemDrop.ItemData weapon)
    {
        return weapon.m_shared?.m_skillType.ToString() switch
        {
            "Axes" => "Dual Axes",
            "Clubs" => "Dual Clubs",
            "Knives" => "Dual Knives",
            "Swords" => "Dual Swords",
            _ => "Dual Offhand"
        };
    }
}
