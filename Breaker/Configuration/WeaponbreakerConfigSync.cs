using System.Text.Json;
using BepInEx.Logging;

namespace Weaponbreaker.Breaker.Configuration;

/// <summary>
/// Synchronizes the host's Weaponbreaker options to every connected client.
/// </summary>
internal sealed class WeaponbreakerConfigSync
{
    private const string SyncRpcName = "Weaponbreaker.SyncConfig";
    private const int SyncVersion = 1;
    private const double BroadcastIntervalSeconds = 1d;

    private readonly WeaponbreakerOptions _options;
    private readonly ManualLogSource _logger;
    private ZRoutedRpc? _registeredRpc;
    private bool _isServer;
    private double _nextBroadcastAt;
    private string? _lastBroadcastPayload;

    public WeaponbreakerConfigSync(
        WeaponbreakerOptions options,
        ManualLogSource logger)
    {
        _options = options;
        _logger = logger;
    }

    public void Initialize()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc is null)
        {
            return;
        }

        if (!ReferenceEquals(_registeredRpc, routedRpc))
        {
            routedRpc.Register<string>(SyncRpcName, ReceiveConfig);
            _registeredRpc = routedRpc;
        }

        _isServer = ZNet.instance?.IsServer() == true;
        if (_isServer)
        {
            BroadcastIfChanged(force: true);
        }
    }

    public void OnNewConnection(ZNetPeer _)
    {
        Initialize();
        if (_isServer)
        {
            BroadcastIfChanged(force: true);
        }
    }

    public void Tick()
    {
        Initialize();
        if (!_isServer || ZNet.instance is null)
        {
            return;
        }

        var now = ZNet.instance.GetTimeSeconds();
        if (now >= _nextBroadcastAt)
        {
            _nextBroadcastAt = now + BroadcastIntervalSeconds;
            // Keep clients authoritative to the host even if a client changes its
            // local Configuration Manager values while connected.
            BroadcastIfChanged(force: true);
        }
    }

    private void BroadcastIfChanged(bool force)
    {
        var routedRpc = _registeredRpc;
        if (!_isServer || routedRpc is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(_options);
        if (!force && string.Equals(payload, _lastBroadcastPayload, StringComparison.Ordinal))
        {
            return;
        }

        routedRpc.InvokeRoutedRPC(
            ZRoutedRpc.Everybody,
            SyncRpcName,
            $"{SyncVersion}:{payload}");
        _lastBroadcastPayload = payload;
    }

    private void ReceiveConfig(long sender, string message)
    {
        if (ZNet.instance?.IsServer() == true
            || ZRoutedRpc.instance is null
            || sender != GetServerPeerId())
        {
            return;
        }

        var separator = message.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(message.Substring(0, separator), out var version)
            || version != SyncVersion)
        {
            _logger.LogWarning("Received an unsupported Weaponbreaker config version.");
            return;
        }

        var payload = message.Substring(separator + 1);
        var serverOptions = JsonSerializer.Deserialize<WeaponbreakerOptions>(payload);
        if (serverOptions is null)
        {
            _logger.LogWarning("Received an empty Weaponbreaker host configuration.");
            return;
        }

        serverOptions.Enemies ??= new Dictionary<string, EnemyOptions>();
        serverOptions.Cooldowns ??= new CooldownOptions();
        _options.Enabled = serverOptions.Enabled;
        _options.EnemyDisarmEnabled = serverOptions.EnemyDisarmEnabled;
        _options.PlayerDisarmEnabled = serverOptions.PlayerDisarmEnabled;
        _options.Cooldowns = serverOptions.Cooldowns;
        _options.Enemies = serverOptions.Enemies;
        _logger.LogInfo("Applied Weaponbreaker configuration received from the host.");
    }

    private static long GetServerPeerId()
    {
        var method = HarmonyLib.AccessTools.Method(typeof(ZRoutedRpc), "GetServerPeerID");
        return method?.Invoke(ZRoutedRpc.instance, null) is long peerId ? peerId : 0L;
    }
}
