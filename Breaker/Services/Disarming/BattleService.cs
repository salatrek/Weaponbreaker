namespace Weaponbreaker.Breaker.Services.Disarming;

/// <summary>
/// Orchestrates combat events and delegates mechanics to focused services.
/// </summary>
internal sealed class BattleService
{
    private readonly EnemyDisarmService _enemyDisarm;
    private readonly PlayerDisarmService _playerDisarm;

    public BattleService(
        EnemyDisarmService enemyDisarm,
        PlayerDisarmService playerDisarm)
    {
        _enemyDisarm = enemyDisarm;
        _playerDisarm = playerDisarm;
    }

    public void OnPerfectParry(
        Humanoid defender,
        HitData hit,
        Character attacker,
        ItemDrop.ItemData blocker)
    {
        _enemyDisarm.TryFromPerfectParry(defender, hit, attacker, blocker);
    }

    public void OnHealthReduced(
        Character target,
        HitData hit,
        Character attacker,
        bool blockedByShield)
    {
        if (target is Player player)
        {
            _playerDisarm.TryFromRealDamage(player, hit, attacker, blockedByShield);
            return;
        }

        _enemyDisarm.TryFromHiddenBowHit(target, hit, attacker);
    }
}
