using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CaramelChat.Data;

namespace CaramelChat.Services;

/// <summary>Identité de ce poste (identifiant stable, nom affiché modifiable, IP LAN).</summary>
public sealed class LocalIdentity
{
    private readonly AppDatabase _database;
    private string _displayName;

    public string PeerId { get; }
    public string MachineName { get; }
    public string LocalIp { get; }

    public event Action? DisplayNameChanged;

    public LocalIdentity(AppDatabase database)
    {
        _database = database;
        MachineName = Environment.MachineName;

        var storedId = database.GetSetting("peerId");
        if (string.IsNullOrWhiteSpace(storedId))
        {
            storedId = Guid.NewGuid().ToString("N");
            database.SetSetting("peerId", storedId);
        }
        PeerId = storedId;

        _displayName = database.GetSetting("displayName") is { Length: > 0 } saved ? saved : MachineName;
        LocalIp = DetectLocalIp();
    }

    public string DisplayName => _displayName;

    public void SetDisplayName(string name)
    {
        var cleaned = name.Trim();
        if (cleaned.Length == 0) return;
        if (cleaned.Length > 60) cleaned = cleaned[..60];

        _displayName = cleaned;
        _database.SetSetting("displayName", cleaned);
        DisplayNameChanged?.Invoke();
    }

    public static string DetectLocalIp()
    {
        try
        {
            var candidates = new List<IPAddress>();
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up) continue;
                if (network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    if (unicast.Address.ToString().StartsWith("169.254.")) continue;
                    candidates.Add(unicast.Address);
                }
            }

            var preferred = candidates.FirstOrDefault(ip => IsPrivate(ip)) ?? candidates.FirstOrDefault();
            if (preferred is not null) return preferred.ToString();
        }
        catch
        {
            // ignoré : on retombe sur la boucle locale
        }

        return "127.0.0.1";
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
               || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
               || (bytes[0] == 192 && bytes[1] == 168);
    }
}
