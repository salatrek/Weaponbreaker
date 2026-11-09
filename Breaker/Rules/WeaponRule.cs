namespace Weaponbreaker.Breaker.Rules;

/// <summary>
/// Code-owned whitelist entry. DropPrefab is always a registered player item.
/// </summary>
internal sealed class WeaponRule
{
    public WeaponRule(
        string id,
        string enemyId,
        string enemyPrefabPart,
        string weaponNamePart,
        string dropPrefab,
        float defaultEnemyWeaponDisarmChance,
        float defaultPlayerDisarmChance)
    {
        Id = id;
        EnemyId = enemyId;
        EnemyPrefabPart = enemyPrefabPart;
        WeaponNamePart = weaponNamePart;
        DropPrefab = dropPrefab;
        DefaultEnemyWeaponDisarmChance = defaultEnemyWeaponDisarmChance;
        DefaultPlayerDisarmChance = defaultPlayerDisarmChance;
    }

    public string Id { get; }

    public string EnemyId { get; }

    public string EnemyPrefabPart { get; }

    public string WeaponNamePart { get; }

    public string DropPrefab { get; }

    public float DefaultEnemyWeaponDisarmChance { get; }

    public float DefaultPlayerDisarmChance { get; }
}

/// <summary>
/// One configuration section for a whole enemy prefab family.
/// </summary>
internal sealed class EnemyDefinition
{
    public EnemyDefinition(
        string id,
        string enemyPrefabPart,
        bool canDropWeapon,
        float defaultEnemyWeaponDisarmChance,
        float defaultPlayerDisarmChance)
    {
        Id = id;
        EnemyPrefabPart = enemyPrefabPart;
        CanDropWeapon = canDropWeapon;
        DefaultEnemyWeaponDisarmChance = defaultEnemyWeaponDisarmChance;
        DefaultPlayerDisarmChance = defaultPlayerDisarmChance;
    }

    public string Id { get; }

    public string EnemyPrefabPart { get; }

    public bool CanDropWeapon { get; }

    public float DefaultEnemyWeaponDisarmChance { get; }

    public float DefaultPlayerDisarmChance { get; }
}
