using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using CaramelChat.Data;
using CaramelChat.Models;

namespace CaramelChat.Services;

/// <summary>
/// Découverte automatique des postes du réseau local : annonce UDP en broadcast
/// toutes les 3 secondes + réponse immédiate aux sondes de démarrage.
/// </summary>
public sealed class DiscoveryService : BackgroundService
{
    private const string AppTag = "caramelchat";
    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(2);

    private readonly PeerRegistry _registry;
    private readonly LocalIdentity _identity;
    private readonly AppDatabase _database;
    private readonly AvatarStore _avatars;
    private readonly ChatOptions _options;
    private readonly ILogger<DiscoveryService> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, (string Fingerprint, DateTimeOffset At)> _persisted = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private UdpClient? _udp;

    public DiscoveryService(
        PeerRegistry registry,
        LocalIdentity identity,
        AppDatabase database,
        AvatarStore avatars,
        ChatOptions options,
        ILogger<DiscoveryService> logger)
    {
        _registry = registry;
        _identity = identity;
        _database = database;
        _avatars = avatars;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
            _udp.EnableBroadcast = true;
            _logger.LogInformation("Découverte UDP active sur le port {Port}", _options.DiscoveryPort);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Découverte UDP indisponible sur le port {Port} (port déjà utilisé ?).", _options.DiscoveryPort);
            return;
        }

        _identity.DisplayNameChanged += OnDisplayNameChanged;

        try
        {
            // Laisse le serveur HTTP démarrer avant la première annonce.
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            await Task.WhenAll(AnnounceLoopAsync(stoppingToken), ReceiveLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // arrêt normal
        }
        finally
        {
            _identity.DisplayNameChanged -= OnDisplayNameChanged;
            _udp?.Dispose();
        }
    }

    private void OnDisplayNameChanged() => _ = SendAnnouncementAsync(CancellationToken.None);

    private async Task AnnounceLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SendAnnouncementAsync(stoppingToken);
            try
            {
                await Task.Delay(AnnounceInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken stoppingToken)
    {
        var nextPrune = DateTimeOffset.UtcNow.AddSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp!.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception)
            {
                _logger.LogWarning(exception, "Erreur de réception UDP");
                continue;
            }

            try
            {
                var address = result.RemoteEndPoint.Address;
                if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
                if (IPAddress.IsLoopback(address)) address = IPAddress.Parse(_identity.LocalIp);

                HandlePacket(result.Buffer, address, result.RemoteEndPoint);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Paquet de découverte ignoré");
            }

            if (DateTimeOffset.UtcNow >= nextPrune)
            {
                nextPrune = DateTimeOffset.UtcNow.AddSeconds(30);
                _registry.Prune(TimeSpan.FromMinutes(5));
            }
        }
    }

    private void HandlePacket(byte[] buffer, IPAddress address, IPEndPoint remote)
    {
        DiscoveryPacket? packet;
        try
        {
            packet = JsonSerializer.Deserialize<DiscoveryPacket>(buffer, _json);
        }
        catch (JsonException)
        {
            return;
        }

        if (packet is null) return;
        if (!string.Equals(packet.App, AppTag, StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(packet.PeerId)) return;
        if (packet.PeerId == _identity.PeerId) return;

        if (string.Equals(packet.Type, "probe", StringComparison.OrdinalIgnoreCase))
        {
            _ = SendAnnouncementToAsync(remote, CancellationToken.None);
            return;
        }

        if (packet.Port is <= 0 or > 65535) return;

        var name = string.IsNullOrWhiteSpace(packet.Name) ? packet.MachineName ?? "Poste inconnu" : packet.Name!;
        var machine = string.IsNullOrWhiteSpace(packet.MachineName) ? name : packet.MachineName!;
        var previous = _registry.Get(packet.PeerId!);
        var peer = new PeerInfo(
            packet.PeerId!,
            name,
            machine,
            address.ToString(),
            packet.Port,
            DateTimeOffset.UtcNow,
            packet.AvatarVersion);

        _registry.Update(peer);
        PersistContact(peer);

        // Nouvelle photo de profil annoncée : on la récupère en arrière-plan.
        if (!string.IsNullOrWhiteSpace(packet.AvatarVersion)
            && !string.Equals(previous?.AvatarVersion, packet.AvatarVersion, StringComparison.Ordinal))
        {
            _ = _avatars.SyncPeerAsync(peer);
        }
    }

    private void PersistContact(PeerInfo peer)
    {
        var fingerprint = $"{peer.Name}|{peer.MachineName}|{peer.Ip}|{peer.Port}";
        if (_persisted.TryGetValue(peer.PeerId, out var previous))
        {
            if (previous.Fingerprint == fingerprint && DateTimeOffset.UtcNow - previous.At < PersistInterval) return;
        }

        _persisted[peer.PeerId] = (fingerprint, DateTimeOffset.UtcNow);
        try
        {
            _database.UpsertContact(peer);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Contact {PeerId} non enregistré", peer.PeerId);
        }
    }

    private async Task SendAnnouncementAsync(CancellationToken cancellationToken)
    {
        if (_udp is null) return;

        var packet = BuildAnnouncement("announce");
        var payload = JsonSerializer.SerializeToUtf8Bytes(packet, _json);
        var targets = GetBroadcastAddresses();
        if (targets.Count == 0) return;

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            foreach (var address in targets)
            {
                try
                {
                    await _udp.SendAsync(payload, new IPEndPoint(address, _options.DiscoveryPort), cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Broadcast impossible vers {Address}", address);
                }
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendAnnouncementToAsync(IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (_udp is null) return;
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(BuildAnnouncement("announce"), _json);
            await _udp.SendAsync(payload, remote, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Réponse de découverte impossible vers {Remote}", remote);
        }
    }

    private DiscoveryPacket BuildAnnouncement(string type) => new(
        AppTag,
        type,
        _identity.PeerId,
        _identity.DisplayName,
        _identity.MachineName,
        _options.HttpPort,
        _avatars.LocalVersion);

    private static List<IPAddress> GetBroadcastAddresses()
    {
        var addresses = new List<IPAddress> { IPAddress.Broadcast };

        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up) continue;
                if (network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    if (unicast.IPv4Mask is null) continue;

                    var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                    if (maskBytes.All(b => b == 0)) continue;

                    addresses.Add(ComputeBroadcast(unicast.Address, maskBytes));
                }
            }
        }
        catch
        {
            // on garde au minimum 255.255.255.255
        }

        return addresses.Distinct().ToList();
    }

    private static IPAddress ComputeBroadcast(IPAddress address, byte[] mask)
    {
        var bytes = address.GetAddressBytes();
        for (var index = 0; index < 4; index++)
        {
            bytes[index] = (byte)(bytes[index] | (byte)~mask[index]);
        }
        return new IPAddress(bytes);
    }

    private sealed record DiscoveryPacket(
        string? App,
        string? Type,
        string? PeerId,
        string? Name,
        string? MachineName,
        int Port,
        string? AvatarVersion = null);
}
