using System.Globalization;

namespace Weaponbreaker.Breaker.Rules;

/// <summary>
/// Explicitly supported enemy/weapon combinations for the first implementation slice.
/// </summary>
internal sealed class WeaponRuleCatalog
{
    private readonly IReadOnlyList<EnemyDefinition> _enemies = new[]
    {
        new EnemyDefinition("Skeleton", "Skeleton", true, 0.30f, 0.01f, 0.15f),
        new EnemyDefinition("Draugr", "Draugr", true, 0.30f, 0.02f, 0.20f),
        new EnemyDefinition("Fuling", "Fuling", true, 0.30f, 0.04f, 0.25f),
        new EnemyDefinition("Charred", "Charred", true, 0.30f, 0.12f, 0.35f),
        new EnemyDefinition("Dvergr", "Dvergr", true, 0.30f, 0.08f, 0.30f),
        new EnemyDefinition("Boar", "Boar", false, 0f, 0.005f),
        new EnemyDefinition("Deathsquito", "Deathsquito", false, 0f, 0.01f),
        new EnemyDefinition("Leech", "Leech", false, 0f, 0.01f),
        new EnemyDefinition("Neck", "Neck", false, 0f, 0.005f),
        new EnemyDefinition("Greydwarf", "Greydwarf", false, 0f, 0.005f),
        new EnemyDefinition("Troll", "Troll", false, 0f, 0.03f),
        new EnemyDefinition("Wolf", "Wolf", false, 0f, 0.01f),
        new EnemyDefinition("Lox", "Lox", false, 0f, 0.02f),
    };

    private readonly IReadOnlyList<WeaponRule> _rules = new[]
    {
        new WeaponRule("SkeletonSword", "Skeleton", "Skeleton", "sword", "SwordBronze", 0.30f, 0.005f),
        new WeaponRule("SkeletonMace", "Skeleton", "Skeleton", "mace", "MaceIron", 0.30f, 0.01f),
        new WeaponRule("DraugrAxe", "Draugr", "Draugr", "axe", "AxeIron", 0.30f, 0.015f),
        new WeaponRule("DraugrSword", "Draugr", "Draugr", "sword", "SwordIron", 0.30f, 0.04f),
        new WeaponRule("FulingClub", "Fuling", "Fuling", "club", "Club", 0.30f, 0.03f),
        new WeaponRule("FulingSword", "Fuling", "Fuling", "sword", "SwordBlackmetal", 0.30f, 0.04f),
        new WeaponRule("FulingSpear", "Fuling", "Fuling", "spear", "SpearWolfFang", 0.30f, 0.05f),
        new WeaponRule("CharredSword", "Charred", "Charred", "sword", "THSwordSlayer", 0.30f, 0.12f),
        new WeaponRule("DvergrSword", "Dvergr", "Dvergr", "sword", "SwordMistwalker", 0.30f, 0.08f),
        new WeaponRule("DvergrAxe", "Dvergr", "Dvergr", "axe", "Battleaxe", 0.30f, 0.10f),
    };

    public IReadOnlyList<EnemyDefinition> Enemies => _enemies;

    public IReadOnlyList<WeaponRule> Rules => _rules;

    public bool TryResolveEnemy(Character enemy, out EnemyDefinition definition)
    {
        var enemyName = Normalize(GetPrefabName(enemy));
        var match = _enemies.FirstOrDefault(candidate =>
            enemyName.IndexOf(Normalize(candidate.EnemyPrefabPart), StringComparison.Ordinal) >= 0);

        if (match is null)
        {
            definition = null!;
            return false;
        }

        definition = match;
        return true;
    }

    public bool TryResolve(Character enemy, ItemDrop.ItemData weapon, out WeaponRule rule)
    {
        var enemyName = Normalize(GetPrefabName(enemy));
        var weaponNames = GetWeaponNames(weapon)
            .Select(Normalize)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        var match = _rules.FirstOrDefault(candidate =>
            enemyName.IndexOf(Normalize(candidate.EnemyPrefabPart), StringComparison.Ordinal) >= 0
            && weaponNames.Any(name =>
                name.IndexOf(Normalize(candidate.WeaponNamePart), StringComparison.Ordinal) >= 0));

        if (match is null)
        {
            rule = null!;
            return false;
        }

        rule = match;
        return true;
    }

    private static string GetPrefabName(Character character)
    {
        var name = character.name;
        return name.Replace("(Clone)", string.Empty);
    }

    private static IEnumerable<string> GetWeaponNames(ItemDrop.ItemData weapon)
    {
        var sharedName = weapon.m_shared?.m_name;
        if (!string.IsNullOrWhiteSpace(sharedName))
        {
            yield return sharedName!;
        }

        if (weapon.m_dropPrefab is not null
            && !string.IsNullOrWhiteSpace(weapon.m_dropPrefab.name))
        {
            yield return weapon.m_dropPrefab.name;
        }
    }

    private static string Normalize(string value)
    {
        return value
            .ToLower(CultureInfo.InvariantCulture)
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
    }
}
