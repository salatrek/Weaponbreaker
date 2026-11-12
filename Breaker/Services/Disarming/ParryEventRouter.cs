using System.Text.Json;
using BepInEx.Logging;
using UnityEngine;
using Weaponbreaker.Breaker.Services.Weapons;

namespace Weaponbreaker.Breaker.Services.Disarming;

/// <summary>Relays a locally confirmed player parry to the enemy's existing ZDO owner.</summary>
internal sealed class ParryEventRouter
{
    internal const string RpcName = "Weaponbreaker.ConfirmedParryV1";
    private readonly Func<bool> _ready;
    private readonly Action<Humanoid, ItemDrop.ItemData, float> _apply;
    private readonly ManualLogSource _logger;
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private ZRoutedRpc? _rpc;
    private float _nextError;
    private float _nextReject;

    internal ParryEventRouter(Func<bool> ready, Action<Humanoid, ItemDrop.ItemData, float> apply, ManualLogSource logger)
    {
        _ready = ready;
        _apply = apply;
        _logger = logger;
    }

    internal void Tick()
    {
        try
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _rpc)) return;
            rpc.Register<string>(RpcName, Receive);
            _rpc = rpc;
        }
        catch (Exception exception) { LogError(exception); }
    }

    internal void Reset()
    {
        _rpc = null;
        _seen.Clear();
        _seenOrder.Clear();
    }

    internal void Forward(Humanoid defender, Humanoid enemy, ItemDrop.ItemData blocker)
    {
        try
        {
            if (!_ready() || defender is not Player || !defender.IsOwner()) return;
            var defenderZdo = ZDOMan.instance?.GetZDO(defender.GetZDOID());
            var enemyZdo = ZDOMan.instance?.GetZDO(enemy.GetZDOID());
            if (defenderZdo == null || !defenderZdo.IsValid() || !defenderZdo.IsOwner()
                || enemyZdo == null || !enemyZdo.IsValid() || enemyZdo.GetOwner() == 0
                || blocker.m_dropPrefab == null || ZNet.instance == null || _rpc == null)
            {
                _logger.LogWarning("[Parry] Forward skipped: missing valid player/enemy ZDO, blocker prefab or routed RPC.");
                return;
            }
            var parry = new ParryMessage
            {
                EventId = Guid.NewGuid().ToString("N"),
                DefenderUser = defenderZdo.m_uid.UserID, DefenderId = defenderZdo.m_uid.ID,
                EnemyUser = enemyZdo.m_uid.UserID, EnemyId = enemyZdo.m_uid.ID,
                BlockerPrefab = blocker.m_dropPrefab.name,
                SkillMultiplier = WeaponProvider.GetOffensiveSkillMultiplier(defender, blocker),
                SentAt = ZNet.instance.GetTimeSeconds()
            };
            _rpc.InvokeRoutedRPC(enemyZdo.GetOwner(), RpcName, JsonSerializer.Serialize(parry));
            _logger.LogInfo($"[Parry] Forwarded: event={parry.EventId}, defender={defenderZdo.m_uid}, enemy={enemyZdo.m_uid}, owner={enemyZdo.GetOwner()}, skillMultiplier={parry.SkillMultiplier:F3}.");
        }
        catch (Exception exception) { LogError(exception); }
    }

    private void Receive(long sender, string message)
    {
        try
        {
            if (!_ready() || ZNet.instance == null) { Reject("local gameplay/config not ready"); return; }
            if (string.IsNullOrEmpty(message) || message.Length > 2048) { Reject("invalid message size"); return; }
            var parry = JsonSerializer.Deserialize<ParryMessage>(message);
            if (parry == null || parry.Version != 1 || !Guid.TryParseExact(parry.EventId, "N", out _)
                || !ValidMultiplier(parry.SkillMultiplier) || double.IsNaN(parry.SentAt) || double.IsInfinity(parry.SentAt)
                || Math.Abs(ZNet.instance.GetTimeSeconds() - parry.SentAt) > 5d)
            { Reject("invalid or expired event"); return; }
            var defenderId = new ZDOID(parry.DefenderUser, parry.DefenderId);
            var enemyId = new ZDOID(parry.EnemyUser, parry.EnemyId);
            var defenderZdo = ZDOMan.instance?.GetZDO(defenderId);
            var enemyZdo = ZDOMan.instance?.GetZDO(enemyId);
            if (defenderId == ZDOID.None || enemyId == ZDOID.None || sender == 0
                || defenderZdo == null || !defenderZdo.IsValid() || defenderZdo.GetOwner() != sender
                || enemyZdo == null || !enemyZdo.IsValid() || !enemyZdo.IsOwner())
            { Reject($"ZDO ownership mismatch: sender={sender}, defender={defenderId}, enemy={enemyId}, owner={enemyZdo?.GetOwner()}"); return; }
            var defender = ZNetScene.instance?.FindInstance(defenderId)?.GetComponent<Player>();
            var enemy = ZNetScene.instance?.FindInstance(enemyId)?.GetComponent<Humanoid>();
            if (defender == null || defender.IsDead() || enemy == null || enemy.IsPlayer() || enemy.IsDead() || !enemy.IsOwner()
                || Vector3.Distance(defender.transform.position, enemy.transform.position) > 12f)
            { Reject("characters unavailable, dead or outside parry range"); return; }
            var blocker = ObjectDB.instance?.GetItemPrefab(parry.BlockerPrefab)?.GetComponent<ItemDrop>()?.m_itemData;
            if (blocker?.m_shared == null || blocker.m_shared.m_timedBlockBonus <= 1f
                || EnemyDisarmService.GetParryChanceMultiplier(blocker) <= 0f)
            { Reject("unregistered or non-parrying blocker"); return; }
            var key = sender + ":" + parry.EventId;
            if (!_seen.Add(key)) { Reject("duplicate event"); return; }
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > 256) _seen.Remove(_seenOrder.Dequeue());
            _logger.LogInfo($"[Parry] Received by enemy owner: event={parry.EventId}, sender={sender}, enemy={enemyId}, owner={enemyZdo.GetOwner()}, skillMultiplier={parry.SkillMultiplier:F3}.");
            _apply(enemy, blocker, parry.SkillMultiplier);
        }
        catch (Exception exception) { LogError(exception); }
    }

    private static bool ValidMultiplier(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0.75f && value <= 1.25f;
    private void Reject(string reason)
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextReject) return;
        _nextReject = now + 1f;
        _logger.LogInfo($"[Parry] Rejected: {reason}.");
    }
    private void LogError(Exception exception)
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextError) return;
        _nextError = now + 10f;
        _logger.LogError($"[Parry] Relay failed; vanilla combat continues: {exception}");
    }

    internal sealed class ParryMessage
    {
        public ParryMessage() { }
        public int Version { get; set; } = 1;
        public string EventId { get; set; } = "";
        public long DefenderUser { get; set; }
        public uint DefenderId { get; set; }
        public long EnemyUser { get; set; }
        public uint EnemyId { get; set; }
        public string BlockerPrefab { get; set; } = "";
        public float SkillMultiplier { get; set; }
        public double SentAt { get; set; }
    }
}
