using System.Text.Json;
using BepInEx.Logging;
using UnityEngine;

namespace Weaponbreaker.Breaker.Configuration;

/// <summary>Host-owned runtime settings. Networking runs after vanilla loading, outside ZNet hooks.</summary>
internal sealed class WeaponbreakerConfigSync
{
    private const string RequestRpc = "Weaponbreaker.RequestConfigV2";
    private const string ConfigRpc = "Weaponbreaker.SyncConfigV2";
    private const string Protocol = "2:0.1.6";
    private const int MaximumMessageLength = 65536;
    private readonly WeaponbreakerOptions _localOptions;
    private readonly ManualLogSource _logger;
    private readonly bool _epicLoot;
    private readonly bool _dualWield;
    private readonly Dictionary<long, Subscription> _subscribers = new();
    private readonly Dictionary<long, string> _peerStages = new();
    private ZNet? _network;
    private ZRoutedRpc? _rpc;
    private string? _payload;
    private float _nextTick;
    private float _nextRequest;
    private float _nextError;
    private int _requests;
    private bool _received;
    private bool _localReady;
    private long _serverId;

    internal WeaponbreakerOptions RuntimeOptions { get; } = new();
    internal bool GameplayReady => _network != null && (_network.IsServer() || (_localReady && _received));
    internal event Action? SessionReset;

    internal WeaponbreakerConfigSync(WeaponbreakerOptions localOptions, ManualLogSource logger,
        bool epicLoot, bool dualWield)
    {
        _localOptions = localOptions;
        _logger = logger;
        _epicLoot = epicLoot;
        _dualWield = dualWield;
        RuntimeOptions.Enabled = false;
    }

    internal void Tick()
    {
        try
        {
            var net = ZNet.instance;
            var rpc = ZRoutedRpc.instance;
            if (net == null || rpc == null)
            {
                if (!ReferenceEquals(_network, null)) Reset();
                return;
            }
            if (!ReferenceEquals(_network, net) || !ReferenceEquals(_rpc, rpc))
            {
                Reset();
                _network = net;
                _rpc = rpc;
                rpc.Register<string>(RequestRpc, ReceiveRequest);
                rpc.Register<string>(ConfigRpc, ReceiveConfig);
                _logger.LogInfo($"[Network] Session started: role={(net.IsServer() ? "host/server" : "client")}; protocol={Protocol}.");
            }
            var now = Time.realtimeSinceStartup;
            if (now < _nextTick) return;
            _nextTick = now + 1f;
            ObserveLocalPlayer();
            if (net.IsServer()) TickServer(net);
            else TickClient(net, now);
        }
        catch (Exception exception) { LogError("network update", exception); }
    }

    internal void Reset()
    {
        if (!ReferenceEquals(_network, null)) _logger.LogInfo("[Network] Session ended; discarded remote settings and peer state.");
        _network = null;
        _rpc = null;
        _payload = null;
        _serverId = 0;
        _received = false;
        _localReady = false;
        _requests = 0;
        _nextTick = _nextRequest = 0;
        _subscribers.Clear();
        _peerStages.Clear();
        RuntimeOptions.Enabled = false;
        SessionReset?.Invoke();
    }

    private void ObserveLocalPlayer()
    {
        var player = Player.m_localPlayer;
        var ready = player != null && player.IsOwner() && player.GetZDOID() != ZDOID.None
            && player.GetComponent<ZNetView>() is { } view && view.IsValid();
        if (ready && !_localReady)
            _logger.LogInfo($"[Network] Local player ready: zdo={player!.GetZDOID()}, position={player.transform.position}. No position or inventory changes performed.");
        if (!ready && _localReady) _logger.LogInfo("[Network] Local player unavailable; combat suspended.");
        _localReady = ready;
    }

    private void TickServer(ZNet net)
    {
        var snapshot = new HostSettings { Protocol = Protocol, Options = _localOptions,
            EpicLoot = _epicLoot, DualWield = _dualWield };
        var payload = JsonSerializer.Serialize(snapshot);
        if (payload.Length > MaximumMessageLength) throw new InvalidDataException("Host configuration exceeds protocol limit.");
        if (!string.Equals(_payload, payload, StringComparison.Ordinal))
        {
            Apply(JsonSerializer.Deserialize<HostSettings>(payload)!);
            _payload = payload;
            _logger.LogInfo($"[Network] Host configuration updated ({payload.Length} characters); EpicLoot={_epicLoot}, DualWield={_dualWield}.");
        }
        var peers = net.GetPeers();
        foreach (var id in _peerStages.Keys.Union(_subscribers.Keys).Where(id => !peers.Any(p => p.m_uid == id)).ToArray())
        {
            _peerStages.Remove(id);
            _subscribers.Remove(id);
            _logger.LogInfo($"[Network] Peer disconnected: peer={id}.");
        }
        foreach (var peer in peers)
        {
            if (!peer.IsReady()) continue;
            var character = ZDOMan.instance?.GetZDO(peer.m_characterID);
            var ready = peer.m_characterID != ZDOID.None && character != null
                && character.IsValid() && character.GetOwner() == peer.m_uid;
            var stage = $"zdo={peer.m_characterID}; worldReady={ready}";
            if (!_peerStages.TryGetValue(peer.m_uid, out var previous) || previous != stage)
            {
                _peerStages[peer.m_uid] = stage;
                _logger.LogInfo($"[Network] Peer state: peer={peer.m_uid}; {stage}.");
            }
            if (!ready || !_subscribers.TryGetValue(peer.m_uid, out var subscription)) continue;
            if (!ReferenceEquals(subscription.Peer, peer)) { _subscribers.Remove(peer.m_uid); continue; }
            if (subscription.LastPayload == _payload) continue;
            _rpc!.InvokeRoutedRPC(peer.m_uid, ConfigRpc, _payload!);
            subscription.LastPayload = _payload;
            _logger.LogInfo($"[Network] Host configuration sent: peer={peer.m_uid}, character={peer.m_characterID}, characters={_payload!.Length}.");
        }
    }

    private void TickClient(ZNet net, float now)
    {
        var peer = net.GetServerPeer();
        if (peer == null || !peer.IsReady())
        {
            _serverId = 0;
            _received = false;
            _requests = 0;
            _nextRequest = 0;
            RuntimeOptions.Enabled = false;
            return;
        }
        if (_serverId != peer.m_uid)
        {
            _serverId = peer.m_uid;
            _received = false;
            _requests = 0;
            _nextRequest = 0;
        }
        if (!_localReady || _received || now < _nextRequest || _requests >= 3) return;
        _nextRequest = now + 10f;
        _requests++;
        _rpc!.InvokeRoutedRPC(peer.m_uid, RequestRpc, Protocol);
        _logger.LogInfo($"[Network] Requested host settings after player readiness (attempt {_requests}/3).");
        if (_requests == 3)
            _logger.LogWarning("[Network] Final config request sent. If no host settings arrive, Weaponbreaker stays inactive on this client; vanilla world loading continues.");
    }

    private void ReceiveRequest(long sender, string protocol)
    {
        try
        {
            if (_network == null || !_network.IsServer() || protocol != Protocol) return;
            var peer = _network.GetPeer(sender);
            if (peer == null || !peer.IsReady() || sender == ZNet.GetUID()) return;
            if (_subscribers.TryGetValue(sender, out var existing) && ReferenceEquals(existing.Peer, peer)) return;
            _subscribers[sender] = new Subscription(peer);
            _logger.LogInfo($"[Network] Compatible client registered: peer={sender}; waiting for valid player ZDO before config transfer.");
        }
        catch (Exception exception) { LogError("config request", exception); }
    }

    private void ReceiveConfig(long sender, string message)
    {
        try
        {
            // Settings updates are safe during respawn too; only initial requests wait for a player.
            if (_network == null || _network.IsServer() || _requests == 0) return;
            var server = _network.GetServerPeer();
            if (server == null || !server.IsReady() || sender != server.m_uid) return;
            if (string.IsNullOrEmpty(message) || message.Length > MaximumMessageLength)
                throw new InvalidDataException("Invalid host config message size.");
            var settings = JsonSerializer.Deserialize<HostSettings>(message);
            if (settings == null || settings.Protocol != Protocol || settings.Options == null)
                throw new InvalidDataException("Unsupported host config protocol.");
            Apply(settings);
            _received = true;
            _logger.LogInfo($"[Network] Authoritative host settings applied: server={sender}; enemies={RuntimeOptions.Enemies.Count}; EpicLoot={RuntimeOptions.AllowEpicLoot}; DualWield={RuntimeOptions.AllowDualWield}.");
        }
        catch (Exception exception) { LogError("config receive", exception); }
    }

    private void Apply(HostSettings settings)
    {
        Validate(settings.Options);
        RuntimeOptions.Enabled = settings.Options.Enabled;
        RuntimeOptions.EnemyDisarmEnabled = settings.Options.EnemyDisarmEnabled;
        RuntimeOptions.PlayerDisarmEnabled = settings.Options.PlayerDisarmEnabled;
        RuntimeOptions.Cooldowns = settings.Options.Cooldowns;
        RuntimeOptions.Enemies = settings.Options.Enemies;
        RuntimeOptions.AllowEpicLoot = settings.EpicLoot && _epicLoot;
        RuntimeOptions.AllowDualWield = settings.DualWield && _dualWield;
    }

    private static void Validate(WeaponbreakerOptions options)
    {
        if (options.Cooldowns == null || options.Enemies == null || options.Enemies.Count > 128)
            throw new InvalidDataException("Incomplete host settings.");
        bool InRange(float value, float max) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= max;
        if (!InRange(options.Cooldowns.EnemyDisarmSeconds, 3600f)
            || !InRange(options.Cooldowns.EnemyWeaponPickupSeconds, 3600f)
            || !InRange(options.Cooldowns.PlayerDisarmSeconds, 3600f)
            || options.Enemies.Any(pair => pair.Value == null
                || !InRange(pair.Value.EnemyWeaponDisarmChance, 1f)
                || !InRange(pair.Value.PlayerDisarmChance, 1f)
                || (pair.Value.EpicLootEnchantChance.HasValue && !InRange(pair.Value.EpicLootEnchantChance.Value, 1f))))
            throw new InvalidDataException("Host settings contain invalid chances or cooldowns.");
    }

    private void LogError(string operation, Exception exception)
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextError) return;
        _nextError = now + 10f;
        _logger.LogError($"[Network] Weaponbreaker {operation} failed; vanilla networking continues. {exception}");
    }

    private sealed class Subscription
    {
        internal readonly ZNetPeer Peer;
        internal string? LastPayload;
        internal Subscription(ZNetPeer peer) { Peer = peer; }
    }

    internal sealed class HostSettings
    {
        public HostSettings() { }
        public string Protocol { get; set; } = "";
        public WeaponbreakerOptions Options { get; set; } = new();
        public bool EpicLoot { get; set; }
        public bool DualWield { get; set; }
    }
}
