using System.Text.Json;
using CaramelChat.Data;
using CaramelChat.Models;
using Microsoft.AspNetCore.StaticFiles;

namespace CaramelChat.Services;

/// <summary>
/// Statuts (texte sur fond de couleur, photo, vidéo) : publication locale, diffusion
/// aux postes du réseau, consultation, vues et expiration au bout de 24 heures.
/// </summary>
public sealed class StatusService : IHostedService
{
    public const int LifetimeHours = 24;
    private const int MaxCaptionLength = 700;
    private const int MaxTextLength = 700;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly AppDatabase _database;
    private readonly PeerRegistry _registry;
    private readonly LocalIdentity _identity;
    private readonly ChatService _chat;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ChatOptions _options;
    private readonly RealtimeHub _hub;
    private readonly ILogger<StatusService> _logger;

    private CancellationTokenSource? _cancellation;
    private Task? _cleanupLoop;

    public StatusService(
        AppDatabase database,
        PeerRegistry registry,
        LocalIdentity identity,
        ChatService chat,
        IHttpClientFactory httpClientFactory,
        ChatOptions options,
        RealtimeHub hub,
        ILogger<StatusService> logger)
    {
        _database = database;
        _registry = registry;
        _identity = identity;
        _chat = chat;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _hub = hub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellation = new CancellationTokenSource();
        _cleanupLoop = CleanupLoopAsync(_cancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellation?.Cancel();
        if (_cleanupLoop is not null)
        {
            try { await _cleanupLoop; } catch { /* arrêt normal */ }
        }
    }

    public string MaxFileSizeLabel => $"{_options.MaxFileSizeMb} Mo";

    // ------------------------------------------------------------- Mes statuts

    public List<object> GetMine()
    {
        return _database.GetStatuses().Select(status =>
        {
            var views = _database.GetStatusViews(status.Id);
            return (object)new
            {
                id = status.Id,
                kind = status.Kind,
                text = status.Text,
                background = status.Background,
                caption = status.Caption,
                mimeType = status.MimeType,
                durationMs = status.DurationMs,
                createdAt = status.CreatedAt,
                expiresAt = status.ExpiresAt,
                viewCount = views.Count,
                paused = status.Paused,
                privacy = status.Privacy,
                privacyList = ParsePrivacyList(status.PrivacyList),
                likes = LikesDto(status.Id),
                likeCount = _database.GetStatusLikes(status.Id).Count,
                comments = CommentsDto(status.Id),
                views = views.Select(view => new
                {
                    peerId = view.PeerId,
                    name = view.ViewerName ?? view.PeerId,
                    at = view.ViewedAt
                })
            };
        }).ToList();
    }

    public (bool Ok, string? Error, object? Status) AddText(StatusTextRequest request)
    {
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length == 0) return (false, "Le statut est vide.", null);
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];

        var background = NormalizeBackground(request.Background);
        var status = new StatusRecord
        {
            Id = NewId(),
            Kind = "text",
            Text = text,
            Background = background,
            Caption = TrimCaption(request.Caption),
            Privacy = NormalizePrivacy(request.Privacy),
            PrivacyList = SerializePrivacyList(request.PrivacyList),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(LifetimeHours)
        };

        _database.AddStatus(status);
        _ = _hub.BroadcastAsync(new { type = "status", mine = true });
        return (true, null, ToDto(status, viewed: true));
    }

    public async Task<(bool Ok, string? Error, object? Status)> AddMediaAsync(
        IFormFile file,
        string? caption,
        int durationMs,
        string? privacy = null,
        List<string>? privacyList = null)
    {
        if (file.Length <= 0) return (false, "Le fichier est vide.", null);
        if (file.Length > _options.MaxFileSizeBytes)
        {
            return (false, $"Fichier trop volumineux : {_options.MaxFileSizeMb} Mo maximum.", null);
        }

        var contentType = (file.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        var kind = contentType.StartsWith("image/", StringComparison.Ordinal)
            ? "image"
            : contentType.StartsWith("video/", StringComparison.Ordinal) ? "video" : string.Empty;

        var fileName = ChatService.SanitizeFileName(file.FileName);
        var guessed = ContentTypes.TryGetContentType(fileName, out var byExtension) ? byExtension : string.Empty;
        if (kind.Length == 0)
        {
            contentType = guessed;
            kind = contentType.StartsWith("image/", StringComparison.Ordinal)
                ? "image"
                : contentType.StartsWith("video/", StringComparison.Ordinal) ? "video" : string.Empty;
        }

        if (kind.Length == 0) return (false, "Choisissez une photo ou une vidéo.", null);
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "application/octet-stream";

        var fileId = NewId() + SafeExtension(fileName);
        var storedPath = Path.Combine(_database.FilesDirectory, fileId);
        await using (var target = File.Create(storedPath))
        {
            await file.CopyToAsync(target);
        }

        var status = new StatusRecord
        {
            Id = NewId(),
            Kind = kind,
            Caption = TrimCaption(caption),
            FileId = fileId,
            FileName = fileName,
            MimeType = contentType,
            FileSize = file.Length,
            DurationMs = Math.Max(0, durationMs),
            Privacy = NormalizePrivacy(privacy),
            PrivacyList = SerializePrivacyList(privacyList),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(LifetimeHours)
        };

        _database.AddStatus(status);
        _ = _hub.BroadcastAsync(new { type = "status", mine = true });
        return (true, null, ToDto(status, viewed: true));
    }

    public bool Delete(string statusId)
    {
        var status = _database.GetStatus(statusId);
        if (status is null) return false;

        _database.DeleteStatus(statusId);
        DeleteStatusFile(status.FileId);
        _ = _hub.BroadcastAsync(new { type = "status", mine = true });
        return true;
    }

    // ----------------------------------------------- Statuts vus par les postes

    /// <summary>Statuts servis à un poste du réseau (filtrés s'il est masqué).</summary>
    public List<object> FeedForPeer(string viewerPeerId)
    {
        if (string.IsNullOrWhiteSpace(viewerPeerId)) return new List<object>();
        if (viewerPeerId == _identity.PeerId) return new List<object>();
        if (_database.IsStatusHidden(viewerPeerId)) return new List<object>();

        return _database.GetStatuses()
            .Where(status => !status.Paused && CanView(status, viewerPeerId))
            .Select(status => (object)new
        {
            id = status.Id,
            kind = status.Kind,
            text = status.Text,
            background = status.Background,
            caption = status.Caption,
            mimeType = status.MimeType,
            durationMs = status.DurationMs,
            createdAt = status.CreatedAt,
            expiresAt = status.ExpiresAt,
            viewed = _database.HasStatusView(status.Id, viewerPeerId)
        }).ToList();
    }

    /// <summary>Fichier d'un statut. Null si introuvable ou masqué pour ce spectateur.</summary>
    public (string Path, string ContentType)? GetMedia(string statusId, string? viewerPeerId)
    {
        var status = _database.GetStatus(statusId);
        if (status?.FileId is not { Length: > 0 } fileId) return null;

        if (!string.IsNullOrWhiteSpace(viewerPeerId)
            && viewerPeerId != _identity.PeerId
            && (_database.IsStatusHidden(viewerPeerId) || status.Paused || !CanView(status, viewerPeerId)))
        {
            return null;
        }

        var path = Path.Combine(_database.FilesDirectory, Path.GetFileName(fileId));
        if (!File.Exists(path)) return null;
        return (path, string.IsNullOrWhiteSpace(status.MimeType) ? "application/octet-stream" : status.MimeType);
    }

    /// <summary>Enregistre qu'un poste du réseau a vu un de mes statuts.</summary>
    public async Task RecordViewAsync(StatusViewRequest request)
    {
        var statusId = request.StatusId?.Trim();
        var viewer = request.ViewerPeerId?.Trim();
        if (string.IsNullOrWhiteSpace(statusId) || string.IsNullOrWhiteSpace(viewer)) return;
        if (viewer == _identity.PeerId) return;
        if (_database.IsStatusHidden(viewer)) return;
        var status = _database.GetStatus(statusId);
        if (status is null || status.Paused || !CanView(status, viewer)) return;

        _database.MarkStatusViewed(statusId, viewer, request.ViewerName);
        await _hub.BroadcastAsync(new { type = "statusViews", statusId });
    }

    // ------------------------------------------------------ Statuts du réseau

    /// <summary>Agrège mes statuts et ceux des postes joignables du réseau.</summary>
    public async Task<object> GetFeedAsync()
    {
        var contacts = _database.GetAllContacts().ToDictionary(contact => contact.PeerId, StringComparer.Ordinal);
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var peer in _registry.All)
        {
            if (peer.PeerId == _identity.PeerId) continue;
            if (string.IsNullOrWhiteSpace(peer.Ip) || peer.Port <= 0) continue;
            targets[peer.PeerId] = $"http://{peer.Ip}:{peer.Port}";
        }

        foreach (var contact in contacts.Values)
        {
            if (contact.PeerId == _identity.PeerId || targets.ContainsKey(contact.PeerId)) continue;
            if (string.IsNullOrWhiteSpace(contact.Ip) || contact.Port <= 0) continue;
            targets[contact.PeerId] = $"http://{contact.Ip}:{contact.Port}";
        }

        var client = _httpClientFactory.CreateClient("status");
        var tasks = targets.Take(48).Select(async target =>
        {
            try
            {
                using var response = await client.GetAsync(
                    $"{target.Value}/api/statuses/feed?viewer={Uri.EscapeDataString(_identity.PeerId)}");
                if (!response.IsSuccessStatusCode) return null;

                var statuses = await response.Content.ReadFromJsonAsync<List<PeerStatusDto>>(Json);
                if (statuses is null || statuses.Count == 0) return null;

                contacts.TryGetValue(target.Key, out var contact);
                var live = _registry.Get(target.Key);
                var name = contact is null
                    ? live?.Name ?? target.Key
                    : string.IsNullOrWhiteSpace(contact.Alias) ? contact.RemoteName : contact.Alias!;

                return (object)new
                {
                    peerId = target.Key,
                    name,
                    avatarVersion = contact?.AvatarVersion ?? live?.AvatarVersion,
                    statuses = statuses
                        .Where(status => !string.IsNullOrWhiteSpace(status.Id))
                        .OrderBy(status => status.CreatedAt)
                        .Select(status => new
                        {
                            id = status.Id,
                            kind = status.Kind ?? "text",
                            text = status.Text,
                            background = status.Background,
                            caption = status.Caption,
                            mimeType = status.MimeType,
                            durationMs = status.DurationMs,
                            createdAt = status.CreatedAt,
                            expiresAt = status.ExpiresAt,
                            viewed = status.Viewed
                        }).ToList()
                };
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Statuts du poste {PeerId} indisponibles", target.Key);
                return null;
            }
        });

        var peers = (await Task.WhenAll(tasks))
            .Where(feed => feed is not null)
            .ToList();

        return new { mine = GetMine(), peers };
    }

    /// <summary>Signale au propriétaire que j'ai vu son statut.</summary>
    public async Task<bool> MarkPeerStatusViewedAsync(string peerId, string statusId)
    {
        var endpoint = _chat.ResolveEndpoint(peerId);
        if (endpoint is null) return false;

        try
        {
            var client = _httpClientFactory.CreateClient("status");
            using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox/status-view", new
            {
                senderPeerId = _identity.PeerId,
                viewerPeerId = _identity.PeerId,
                viewerName = _identity.DisplayName,
                statusId
            }, Json);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Vue du statut non transmise à {PeerId}", peerId);
            return false;
        }
    }

    /// <summary>Récupère le média d'un statut d'un autre poste (relais local, sans mixed content).</summary>
    public async Task<(Stream Stream, string ContentType)?> OpenPeerMediaAsync(string peerId, string statusId)
    {
        var endpoint = _chat.ResolveEndpoint(peerId);
        if (endpoint is null) return null;

        try
        {
            var client = _httpClientFactory.CreateClient("status");
            var url = $"{endpoint}/api/statuses/{Uri.EscapeDataString(statusId)}/media" +
                      $"?viewer={Uri.EscapeDataString(_identity.PeerId)}";
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return (stream, contentType);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Média du statut {StatusId} indisponible", statusId);
            return null;
        }
    }

    // -------------------------------------------------------------------- Utils

    private object ToDto(StatusRecord status, bool viewed) => new
    {
        id = status.Id,
        kind = status.Kind,
        text = status.Text,
        background = status.Background,
        caption = status.Caption,
        mimeType = status.MimeType,
        durationMs = status.DurationMs,
        createdAt = status.CreatedAt,
        expiresAt = status.ExpiresAt,
        viewed,
        paused = status.Paused,
        privacy = status.Privacy,
        privacyList = ParsePrivacyList(status.PrivacyList),
        viewCount = _database.GetStatusViews(status.Id).Count,
        likes = LikesDto(status.Id),
        likeCount = _database.GetStatusLikes(status.Id).Count,
        comments = CommentsDto(status.Id),
        views = _database.GetStatusViews(status.Id).Select(view => new
        {
            peerId = view.PeerId,
            name = view.ViewerName ?? view.PeerId,
            at = view.ViewedAt
        })
    };

    // ------------------------------------------------- Confidentialité et pause

    private static bool CanView(StatusRecord status, string viewerPeerId)
    {
        var list = ParsePrivacyList(status.PrivacyList);
        return status.Privacy switch
        {
            "except" => !list.Contains(viewerPeerId, StringComparer.Ordinal),
            "only" => list.Contains(viewerPeerId, StringComparer.Ordinal),
            _ => true
        };
    }

    public bool SetPrivacy(string statusId, StatusPrivacyRequest request)
    {
        var status = _database.GetStatus(statusId);
        if (status is null) return false;
        _database.SetStatusPrivacy(
            statusId,
            NormalizePrivacy(request.Privacy),
            SerializePrivacyList(request.Peers));
        _ = _hub.BroadcastAsync(new { type = "status", mine = true });
        return true;
    }

    public bool SetPaused(string statusId, bool paused)
    {
        var status = _database.GetStatus(statusId);
        if (status is null) return false;
        _database.SetStatusPaused(statusId, paused);
        _ = _hub.BroadcastAsync(new { type = "status", mine = true });
        return true;
    }

    // ------------------------------------------------------- J'aime / commenter

    public async Task<bool> LikePeerStatusAsync(string peerId, string statusId, bool liked)
    {
        var endpoint = _chat.ResolveEndpoint(peerId);
        if (endpoint is null) return false;

        try
        {
            var client = _httpClientFactory.CreateClient("status");
            using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox/status-like", new
            {
                senderPeerId = _identity.PeerId,
                senderName = _identity.DisplayName,
                statusId,
                liked
            }, Json);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "J'aime non transmis à {PeerId}", peerId);
            return false;
        }
    }

    public async Task<bool> CommentPeerStatusAsync(string peerId, string statusId, string text)
    {
        var endpoint = _chat.ResolveEndpoint(peerId);
        if (endpoint is null) return false;

        try
        {
            var client = _httpClientFactory.CreateClient("status");
            using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox/status-comment", new
            {
                senderPeerId = _identity.PeerId,
                senderName = _identity.DisplayName,
                statusId,
                text
            }, Json);
            if (!response.IsSuccessStatusCode) return false;

            // Le commentaire reste aussi dans la discussion, comme dans WhatsApp.
            await _chat.AddOutgoingStatusCommentAsync(peerId, text, statusId);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Commentaire non transmis à {PeerId}", peerId);
            return false;
        }
    }

    public async Task RecordLikeAsync(IncomingStatusLikeRequest request)
    {
        var statusId = request.StatusId?.Trim();
        var peerId = request.SenderPeerId?.Trim();
        if (string.IsNullOrWhiteSpace(statusId) || string.IsNullOrWhiteSpace(peerId)) return;
        if (_database.GetStatus(statusId) is null) return;

        _database.SetStatusLike(statusId, peerId, request.SenderName, request.Liked);
        await _hub.BroadcastAsync(new { type = "statusViews", statusId });
    }

    public async Task RecordCommentAsync(IncomingStatusCommentRequest request)
    {
        var statusId = request.StatusId?.Trim();
        var peerId = request.SenderPeerId?.Trim();
        var text = (request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(statusId) || string.IsNullOrWhiteSpace(peerId) || text.Length == 0) return;
        if (_database.GetStatus(statusId) is null) return;
        if (text.Length > 500) text = text[..500];

        _database.AddStatusComment(statusId, peerId, request.SenderName, text);
        await _hub.BroadcastAsync(new { type = "statusViews", statusId });

        // Comme dans WhatsApp, le commentaire apparaît aussi dans la discussion
        // avec la personne qui a commenté le statut.
        await _chat.AddIncomingStatusCommentAsync(peerId, request.SenderName, text, statusId);
    }

    /// <summary>
    /// Comme dans WhatsApp, l'auteur ne peut ni aimer ni commenter son propre statut.
    /// Ces routes restent présentes pour les anciennes interfaces, mais ne font rien.
    /// </summary>
    public Task RecordLocalLikeAsync(string statusId, bool liked) => Task.CompletedTask;

    public Task<bool> RecordLocalCommentAsync(string statusId, string? text) => Task.FromResult(false);

    private object LikesDto(string statusId) => _database.GetStatusLikes(statusId).Select(like => new
    {
        peerId = like.PeerId,
        name = like.Name ?? like.PeerId,
        at = like.At
    });

    private object CommentsDto(string statusId) => _database.GetStatusComments(statusId).Select(comment => new
    {
        id = comment.Id,
        peerId = comment.AuthorPeerId,
        name = comment.AuthorName ?? comment.AuthorPeerId,
        text = comment.Text,
        at = comment.At
    });

    private static string NormalizePrivacy(string? privacy) =>
        privacy is "except" or "only" ? privacy : "all";

    private static List<string> ParsePrivacyList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string? SerializePrivacyList(List<string>? peers)
    {
        if (peers is null || peers.Count == 0) return null;
        return JsonSerializer.Serialize(peers.Distinct().Take(500).ToList());
    }

    private static string NormalizeBackground(string? background)
    {
        var value = (background ?? string.Empty).Trim();
        if (value.Length == 7 && value[0] == '#'
            && value[1..].All(character => Uri.IsHexDigit(character)))
        {
            return value.ToUpperInvariant();
        }
        return "#0A7C5A";
    }

    private static string? TrimCaption(string? caption)
    {
        var value = (caption ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        return value.Length > MaxCaptionLength ? value[..MaxCaptionLength] : value;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || extension.Length > 12) return string.Empty;
        return extension.All(character => char.IsLetterOrDigit(character) || character == '.') ? extension : string.Empty;
    }

    private void DeleteStatusFile(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId)) return;
        try
        {
            File.Delete(Path.Combine(_database.FilesDirectory, Path.GetFileName(fileId)));
        }
        catch
        {
            // fichier déjà supprimé ou verrouillé, sans conséquence
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach (var fileId in _database.DeleteExpiredStatuses())
                {
                    DeleteStatusFile(fileId);
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Nettoyage des statuts impossible");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private sealed record PeerStatusDto(
        string? Id,
        string? Kind,
        string? Text,
        string? Background,
        string? Caption,
        string? MimeType,
        int DurationMs,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        bool Viewed);
}
