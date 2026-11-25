using System.Collections.Concurrent;
using CaramelChat.Data;
using CaramelChat.Models;

namespace CaramelChat.Services;

/// <summary>
/// Photos de profil : enregistrement de la mienne (App_Data/avatars) et cache local
/// des photos des autres postes, récupérées automatiquement après la découverte.
/// </summary>
public sealed class AvatarStore
{
    private const int MaxAvatarBytes = 4 * 1024 * 1024;
    private static readonly string[] AllowedTypes = { "image/png", "image/jpeg", "image/webp", "image/gif" };
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(25);

    private readonly AppDatabase _database;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RealtimeHub _hub;
    private readonly ILogger<AvatarStore> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAttempt = new(StringComparer.Ordinal);

    public string AvatarsDirectory { get; }

    public AvatarStore(
        AppDatabase database,
        IHttpClientFactory httpClientFactory,
        RealtimeHub hub,
        ILogger<AvatarStore> logger)
    {
        _database = database;
        _httpClientFactory = httpClientFactory;
        _hub = hub;
        _logger = logger;

        AvatarsDirectory = Path.Combine(database.DataDirectory, "avatars");
        Directory.CreateDirectory(AvatarsDirectory);

        // Les contacts de démonstration ont été retirés de l'application.
        foreach (var old in Directory.EnumerateFiles(AvatarsDirectory, "demo-*"))
        {
            TryDelete(old);
        }
    }

    public string LocalVersion => _database.GetSetting("avatarVersion") ?? "";

    public string? LocalFilePath
    {
        get
        {
            var file = _database.GetSetting("avatarFile");
            if (string.IsNullOrWhiteSpace(file)) return null;
            var path = Path.Combine(AvatarsDirectory, Path.GetFileName(file));
            return File.Exists(path) ? path : null;
        }
    }

    public string LocalContentType => GuessContentType(LocalFilePath);

    public async Task<(bool Ok, string? Error)> SaveLocalAsync(IFormFile file)
    {
        if (file.Length <= 0) return (false, "L'image est vide.");
        if (file.Length > MaxAvatarBytes) return (false, "Photo trop volumineuse : 4 Mo maximum.");

        var contentType = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(contentType))
        {
            return (false, "Format non pris en charge : utilisez une image PNG, JPEG, WebP ou GIF.");
        }

        var version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var name = $"local-{version}{ExtensionFor(contentType)}";
        var path = Path.Combine(AvatarsDirectory, name);

        await using (var target = File.Create(path))
        {
            await file.CopyToAsync(target);
        }

        foreach (var old in Directory.EnumerateFiles(AvatarsDirectory, "local-*"))
        {
            if (!string.Equals(old, path, StringComparison.Ordinal)) TryDelete(old);
        }

        _database.SetSetting("avatarFile", name);
        _database.SetSetting("avatarVersion", version);

        await _hub.BroadcastAsync(new { type = "me", displayName = (string?)null, avatarVersion = version });
        return (true, null);
    }

    /// <summary>Télécharge la photo d'un poste découvert (une seule fois par version).</summary>
    public async Task SyncPeerAsync(PeerInfo peer)
    {
        var version = peer.AvatarVersion;
        if (string.IsNullOrWhiteSpace(version)) return;
        if (peer.Port <= 0 || string.IsNullOrWhiteSpace(peer.Ip)) return;

        var contact = _database.GetContact(peer.PeerId);
        if (string.Equals(contact?.AvatarVersion, version, StringComparison.Ordinal) && GetPeerFilePath(peer.PeerId) is not null)
        {
            return;
        }

        if (_lastAttempt.TryGetValue(peer.PeerId, out var last) && DateTimeOffset.UtcNow - last < RetryDelay) return;
        _lastAttempt[peer.PeerId] = DateTimeOffset.UtcNow;

        try
        {
            var client = _httpClientFactory.CreateClient("peer");
            using var response = await client.GetAsync($"http://{peer.Ip}:{peer.Port}/api/me/avatar");
            if (!response.IsSuccessStatusCode) return;

            var contentType = (response.Content.Headers.ContentType?.MediaType ?? "").ToLowerInvariant();
            if (!AllowedTypes.Contains(contentType)) return;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length is <= 0 or > MaxAvatarBytes) return;

            var name = $"{peer.PeerId}{ExtensionFor(contentType)}";
            var path = Path.Combine(AvatarsDirectory, name);
            await File.WriteAllBytesAsync(path, bytes);

            foreach (var old in Directory.EnumerateFiles(AvatarsDirectory, $"{peer.PeerId}.*"))
            {
                if (!string.Equals(old, path, StringComparison.Ordinal)) TryDelete(old);
            }

            _database.SetContactAvatar(peer.PeerId, version, name);
            await _hub.BroadcastAsync(new { type = "peerAvatar", peerId = peer.PeerId, version });
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Photo de profil de {PeerId} indisponible", peer.PeerId);
        }
    }

    public string? GetPeerFilePath(string peerId)
    {
        var contact = _database.GetContact(peerId);
        if (contact?.AvatarFile is not { Length: > 0 } file) return null;
        var path = Path.Combine(AvatarsDirectory, Path.GetFileName(file));
        return File.Exists(path) ? path : null;
    }

    public string GetPeerContentType(string peerId) => GuessContentType(GetPeerFilePath(peerId));

    private static string GuessContentType(string? path)
    {
        var extension = Path.GetExtension(path ?? "").ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".png"
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // fichier encore utilisé : sera nettoyé plus tard
        }
    }
}
