using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CaramelChat.Data;
using CaramelChat.Models;
using CaramelChat.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaramelChat.Tests;

/// <summary>
/// Tests d'intégration sans framework externe : identité permanente, mise à jour
/// de l'adresse réseau, historique, file d'attente, migration et découverte UDP.
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main()
    {
        Console.WriteLine("Tests d'identité What’sUP");
        Console.WriteLine("=========================");

        var root = Path.Combine(Path.GetTempPath(), "whatsup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var installA = Path.Combine(root, "install-a");
            var installB = Path.Combine(root, "install-b");
            Directory.CreateDirectory(installA);
            Directory.CreateDirectory(installB);

            var databaseA = new AppDatabase(installA);
            databaseA.Initialize();
            var identityA = new LocalIdentity(databaseA);
            var peerIdA = identityA.PeerId;

            Check("1. PeerId généré au premier démarrage (UUID)",
                Guid.TryParse(peerIdA, out _), peerIdA);

            var restartedDatabase = new AppDatabase(installA);
            restartedDatabase.Initialize();
            var restarted = new LocalIdentity(restartedDatabase);
            Check("1b. PeerId identique après redémarrage",
                restarted.PeerId == peerIdA, restarted.PeerId);

            var databaseB = new AppDatabase(installB);
            databaseB.Initialize();
            var peerIdB = new LocalIdentity(databaseB).PeerId;
            Check("2. Deux installations ont des PeerId différents",
                peerIdA != peerIdB, $"{peerIdA} / {peerIdB}");

            // ------------------------------------------------ Changement d'adresse

            var registry = new PeerRegistry();
            var firstSeen = DateTimeOffset.UtcNow;
            registry.Update(new PeerInfo(peerIdB, "Poste B", "PC-B", "192.168.1.20", 5080, firstSeen));
            registry.Update(new PeerInfo(peerIdB, "Poste B", "PC-B", "192.168.1.77", 5099, firstSeen.AddSeconds(5)));

            Check("3. Changement d'IP : aucun nouveau pair dans le registre",
                registry.All.Count(peer => peer.PeerId == peerIdB) == 1);
            Check("4. Le pair existant reçoit la nouvelle IP et le nouveau port",
                registry.Get(peerIdB) is { Ip: "192.168.1.77", Port: 5099 },
                $"{registry.Get(peerIdB)?.Ip}:{registry.Get(peerIdB)?.Port}");

            databaseB.UpsertContact(new PeerInfo(peerIdB, "Poste B", "PC-B", "192.168.1.20", 5080, firstSeen));
            databaseB.UpsertContact(new PeerInfo(peerIdB, "Poste B", "PC-B", "192.168.1.77", 5099, firstSeen.AddSeconds(5)));

            var contacts = databaseB.GetAllContacts().Where(contact => contact.PeerId == peerIdB).ToList();
            Check("4b. Un seul contact en base, avec la nouvelle IP",
                contacts.Count == 1 && contacts[0].Ip == "192.168.1.77" && contacts[0].Port == 5099,
                string.Join(", ", contacts.Select(contact => $"{contact.Ip}:{contact.Port}")));

            databaseB.AddMessage(new ChatMessage
            {
                Id = "msg-history-1",
                PeerId = peerIdB,
                Direction = "incoming",
                Kind = "text",
                Text = "Message avant changement d'IP",
                Status = "delivered",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
            });
            databaseB.UpsertContact(new PeerInfo(peerIdB, "Poste B", "PC-B", "10.0.0.42", 5081, DateTimeOffset.UtcNow));

            var history = databaseB.GetMessages(peerIdB);
            Check("5. L'historique reste associé au même PeerId après changement d'IP",
                history.Count == 1 && history[0].Text == "Message avant changement d'IP");

            databaseB.AddMessage(new ChatMessage
            {
                Id = "msg-pending-1",
                PeerId = peerIdB,
                Direction = "outgoing",
                Kind = "text",
                Text = "En attente de livraison",
                Status = "queued",
                CreatedAt = DateTimeOffset.UtcNow
            });
            databaseB.UpsertContact(new PeerInfo(peerIdB, "Poste B", "PC-B", "10.0.0.99", 5082, DateTimeOffset.UtcNow));

            var pending = databaseB.GetPendingMessages();
            Check("6. Le message en attente reste associé au PeerId",
                pending.Any(message => message.Id == "msg-pending-1" && message.PeerId == peerIdB));

            // ------------------------------------------------------- Migration

            var migrationDirectory = Path.Combine(root, "migration");
            Directory.CreateDirectory(migrationDirectory);
            var legacy = new AppDatabase(migrationDirectory);
            legacy.Initialize();
            legacy.SetSetting("peerId", peerIdA);
            legacy.SetSetting("displayName", "Alice");
            legacy.UpsertContact(new PeerInfo("ancien-contact", "Ancien", "PC", "192.168.1.5", 5080, DateTimeOffset.UtcNow));
            legacy.AddMessage(new ChatMessage
            {
                Id = "old-message",
                PeerId = "ancien-contact",
                Direction = "incoming",
                Kind = "text",
                Text = "Ancienne donnée",
                Status = "delivered"
            });
            legacy.AddCall(new CallRecord
            {
                Id = "old-call",
                PeerId = "ancien-contact",
                Kind = "audio",
                Direction = "incoming",
                Status = "missed"
            });

            using (var connection = legacy.OpenConnection())
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Settings WHERE Key = 'schemaVersion'";
                command.ExecuteNonQuery();
            }

            var migrated = new AppDatabase(migrationDirectory);
            migrated.Initialize();

            var backups = Directory.Exists(Path.Combine(migrationDirectory, "backups"))
                ? Directory.GetFiles(Path.Combine(migrationDirectory, "backups"), "*.db")
                : Array.Empty<string>();

            Check("9. Migration : sauvegarde créée avant réinitialisation", backups.Length == 1,
                string.Join(", ", backups));
            Check("9b. Migration : contenus réinitialisés",
                migrated.GetAllContacts().Count == 0
                && migrated.GetMessages("ancien-contact").Count == 0
                && migrated.GetCalls().Count == 0);
            Check("9c. Migration : PeerId et nom conservés",
                migrated.GetSetting("peerId") == peerIdA && migrated.GetSetting("displayName") == "Alice",
                $"{migrated.GetSetting("peerId")} / {migrated.GetSetting("displayName")}");

            // ------------------------------------------------ Découverte UDP locale

            var discoveryPort = FindFreeUdpPort();
            var discoveryDatabase = new AppDatabase(Path.Combine(root, "discovery"));
            discoveryDatabase.Initialize();
            var discoveryIdentity = new LocalIdentity(discoveryDatabase);
            var discoveryRegistry = new PeerRegistry();
            var options = new ChatOptions { HttpPort = 5099, DiscoveryPort = discoveryPort };
            var avatars = new AvatarStore(
                discoveryDatabase,
                new SingleHttpClientFactory(),
                new RealtimeHub(NullLogger<RealtimeHub>.Instance),
                NullLogger<AvatarStore>.Instance);

            var discovery = new DiscoveryService(
                discoveryRegistry,
                discoveryIdentity,
                discoveryDatabase,
                avatars,
                options,
                NullLogger<DiscoveryService>.Instance);

            await discovery.StartAsync(CancellationToken.None);
            var udpPeerId = Guid.NewGuid().ToString("N");

            try
            {
                var announced = await SendAnnouncementUntilSeenAsync(
                    discoveryPort, discoveryRegistry, udpPeerId, "Poste UDP", 51234);

                Check("7. La découverte UDP locale fonctionne toujours", announced,
                    $"pair {udpPeerId} non reçu");

                if (announced)
                {
                    await SendAnnouncementUntilSeenAsync(
                        discoveryPort, discoveryRegistry, udpPeerId, "Poste UDP renommé", 51235,
                        peer => peer.Port == 51235 && peer.Name == "Poste UDP renommé");

                    Check("7b. Une nouvelle annonce met à jour le même pair (pas de doublon)",
                        discoveryRegistry.All.Count(peer => peer.PeerId == udpPeerId) == 1
                        && discoveryRegistry.Get(udpPeerId) is { Port: 51235 });
                }
                else
                {
                    Check("7b. Mise à jour UDP (non exécutée : découverte indisponible)", false);
                }
            }
            finally
            {
                await discovery.StopAsync(CancellationToken.None);
            }

            // ------------------------------------- Absence de découverte distante

            var repoSrc = FindProjectRoot();
            var discoverySource = File.ReadAllText(Path.Combine(repoSrc, "CaramelChat", "Services", "DiscoveryService.cs"));
            var appSource = File.ReadAllText(Path.Combine(repoSrc, "CaramelChat", "wwwroot", "js", "app.js"));

            Check("8. Aucune découverte distante (pas d'URL, STUN ou TURN dans la découverte)",
                !discoverySource.Contains("http://", StringComparison.OrdinalIgnoreCase)
                && !discoverySource.Contains("https://", StringComparison.OrdinalIgnoreCase)
                && !discoverySource.Contains("stun:", StringComparison.OrdinalIgnoreCase)
                && !discoverySource.Contains("turn:", StringComparison.OrdinalIgnoreCase));
            Check("8b. Aucun serveur STUN/TURN pour les appels (réseau local uniquement)",
                appSource.Contains("iceServers: []", StringComparison.Ordinal)
                && !appSource.Contains("stun:", StringComparison.OrdinalIgnoreCase)
                && !appSource.Contains("turn:", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine($"[ÉCHEC] Exception : {exception.Message}");
            Console.WriteLine(exception);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* fichiers verrouillés */ }
        }

        Console.WriteLine();
        Console.WriteLine($"{_passed} test(s) réussi(s), {_failed} échec(s).");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"[OK] {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"[ÉCHEC] {name}{(string.IsNullOrWhiteSpace(detail) ? "" : $" — {detail}")}");
        }
    }

    private static async Task<bool> SendAnnouncementUntilSeenAsync(
        int port,
        PeerRegistry registry,
        string peerId,
        string name,
        int httpPort,
        Func<PeerInfo, bool>? predicate = null)
    {
        using var sender = new UdpClient();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            app = "caramelchat",
            type = "announce",
            peerId,
            name,
            machineName = "PC-TEST",
            port = httpPort,
            avatarVersion = (string?)null,
            ip = "127.0.0.1"
        }, Json);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(6);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await sender.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, port));
            await Task.Delay(250);

            var peer = registry.Get(peerId);
            if (peer is not null && (predicate is null || predicate(peer))) return true;
        }

        var last = registry.Get(peerId);
        return last is not null && (predicate is null || predicate(last));
    }

    private static int FindFreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CaramelChat.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Racine du projet introuvable.");
    }

    private sealed class SingleHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client = new();
        public HttpClient CreateClient(string name) => _client;
    }
}
