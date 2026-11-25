using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CaramelChat.Data;
using CaramelChat.Models;
using Microsoft.AspNetCore.StaticFiles;

namespace CaramelChat.Services;

/// <summary>
/// Logique métier : stockage local des messages, transfert HTTP direct vers le poste
/// destinataire (http://ip:port/api/inbox) et file d'attente quand il est hors ligne.
/// </summary>
public sealed class ChatService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly AppDatabase _database;
    private readonly PeerRegistry _registry;
    private readonly LocalIdentity _identity;
    private readonly RealtimeHub _hub;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ChatOptions _options;
    private readonly ILogger<ChatService> _logger;
    private int _retryGate;

    /// <summary>Messages en cours de transmission : ils ne peuvent plus être annulés.</summary>
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public ChatService(
        AppDatabase database,
        PeerRegistry registry,
        LocalIdentity identity,
        RealtimeHub hub,
        IHttpClientFactory httpClientFactory,
        ChatOptions options,
        ILogger<ChatService> logger)
    {
        _database = database;
        _registry = registry;
        _identity = identity;
        _hub = hub;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    /// <summary>Nom affiché pour la discussion « Messages à moi-même ».</summary>
    private const string SelfDisplayName = "Vous (moi-même)";

    /// <summary>Identifiant de ce poste : sert de destinataire à la discussion avec soi-même.</summary>
    public string SelfPeerId => _identity.PeerId;

    public bool IsSelf(string? peerId) =>
        !string.IsNullOrWhiteSpace(peerId) && string.Equals(peerId, _identity.PeerId, StringComparison.Ordinal);

    /// <summary>Vrai si l'utilisateur a bloqué ce contact : il ne peut plus nous contacter.</summary>
    public bool IsChatBlocked(string peerId) =>
        !string.IsNullOrWhiteSpace(peerId) && !IsSelf(peerId) && _database.IsChatBlocked(peerId);

    public List<object> GetBlocks()
    {
        var contacts = _database.GetAllContacts().ToDictionary(contact => contact.PeerId, StringComparer.Ordinal);
        return _database.GetBlocks().Select(block =>
        {
            contacts.TryGetValue(block.PeerId, out var contact);
            var live = _registry.Get(block.PeerId);
            var name = contact?.Alias is { Length: > 0 } alias
                ? alias
                : contact?.RemoteName ?? live?.Name ?? block.PeerId;
            return (object)new
            {
                peerId = block.PeerId,
                name,
                chat = block.Chat,
                status = block.Status,
                avatarVersion = contact?.AvatarVersion ?? live?.AvatarVersion
            };
        }).ToList();
    }

    public BlockRow SetBlock(string peerId, bool? chat, bool? status)
    {
        // On ne peut pas bloquer sa propre discussion.
        if (IsSelf(peerId)) return new BlockRow(peerId, false, false);

        var block = _database.SetBlock(peerId, chat, status);
        _ = _hub.BroadcastAsync(new { type = "blocks", peerId });
        return block;
    }

    // --------------------------------------------------------------- Lecture UI

    public List<object> GetPeers()
    {
        var contacts = _database.GetAllContacts().ToDictionary(c => c.PeerId, StringComparer.Ordinal);
        var online = new List<object>();
        var offline = new List<object>();

        // La discussion avec soi-même est toujours proposée, en tête de liste.
        online.Add(new
        {
            peerId = _identity.PeerId,
            name = SelfDisplayName,
            machineName = _identity.MachineName,
            ip = _identity.LocalIp,
            port = _options.HttpPort,
            online = true,
            alias = (string?)null,
            avatarVersion = (string?)null,
            blockChat = false,
            blockStatus = false,
            self = true,
            lastSeen = DateTimeOffset.UtcNow
        });

        foreach (var peer in _registry.All.Where(p => p.IsOnline && !IsSelf(p.PeerId)))
        {
            contacts.TryGetValue(peer.PeerId, out var contact);
            online.Add(new
            {
                peerId = peer.PeerId,
                name = DisplayName(peer.Name, contact?.Alias),
                machineName = string.IsNullOrWhiteSpace(peer.MachineName) ? peer.Name : peer.MachineName,
                ip = peer.Ip,
                port = peer.Port,
                online = true,
                alias = contact?.Alias,
                avatarVersion = contact?.AvatarVersion ?? peer.AvatarVersion,
                blockChat = _database.IsChatBlocked(peer.PeerId),
                blockStatus = _database.IsStatusHidden(peer.PeerId),
                lastSeen = peer.LastSeen
            });
        }

        foreach (var contact in contacts.Values)
        {
            if (IsSelf(contact.PeerId)) continue;
            var live = _registry.Get(contact.PeerId);
            if (live?.IsOnline == true) continue;
            if (string.IsNullOrWhiteSpace(contact.Ip) || contact.Port <= 0) continue;

            offline.Add(new
            {
                peerId = contact.PeerId,
                name = DisplayName(contact.RemoteName, contact.Alias),
                machineName = string.IsNullOrWhiteSpace(contact.MachineName) ? contact.RemoteName : contact.MachineName,
                ip = contact.Ip,
                port = contact.Port,
                online = false,
                alias = contact.Alias,
                avatarVersion = contact.AvatarVersion,
                blockChat = _database.IsChatBlocked(contact.PeerId),
                blockStatus = _database.IsStatusHidden(contact.PeerId),
                lastSeen = contact.LastSeen
            });
        }

        online.AddRange(offline);
        return online;
    }

    public List<object> GetConversations()
    {
        var contacts = _database.GetAllContacts().ToDictionary(c => c.PeerId, StringComparer.Ordinal);
        var streaks = _database.GetActiveStreaks();
        var conversations = new List<object>();

        foreach (var row in _database.GetConversations())
        {
            var last = row.Last;
            contacts.TryGetValue(last.PeerId, out var contact);
            var live = _registry.Get(last.PeerId);
            var isSelf = IsSelf(last.PeerId);

            var remoteName = contact?.RemoteName
                             ?? live?.Name
                             ?? last.PeerId;
            var name = isSelf ? SelfDisplayName : DisplayName(remoteName, contact?.Alias);
            var machineName = isSelf
                ? _identity.MachineName
                : contact?.MachineName is { Length: > 0 } machine
                    ? machine
                    : live?.MachineName ?? remoteName;

            conversations.Add(new
            {
                peerId = last.PeerId,
                name,
                machineName,
                ip = isSelf ? _identity.LocalIp : live?.Ip ?? contact?.Ip ?? "",
                port = isSelf ? _options.HttpPort : live is { Port: > 0 } ? live.Port : contact?.Port ?? 0,
                online = isSelf || live?.IsOnline == true,
                self = isSelf,
                unread = row.Unread,
                blockChat = !isSelf && _database.IsChatBlocked(last.PeerId),
                blockStatus = !isSelf && _database.IsStatusHidden(last.PeerId),
                avatarVersion = isSelf ? null : contact?.AvatarVersion ?? live?.AvatarVersion,
                streak = isSelf ? 0 : streaks.GetValueOrDefault(last.PeerId),
                lastMessage = new
                {
                    kind = last.Kind,
                    text = last.Text,
                    fileName = last.FileName,
                    direction = last.Direction,
                    status = last.Status,
                    isViewOnce = last.IsViewOnce,
                    viewOnceOpened = last.ViewOnceOpened,
                    createdAt = last.CreatedAt
                }
            });
        }

        return conversations;
    }

    public void MarkRead(string peerId) => _database.MarkConversationRead(peerId);

    public void SetAlias(string peerId, string? alias)
    {
        if (string.IsNullOrWhiteSpace(peerId)) return;
        _database.SetAlias(peerId, string.IsNullOrWhiteSpace(alias) ? null : alias.Trim());
    }

    private static string DisplayName(string remoteName, string? alias) =>
        string.IsNullOrWhiteSpace(alias) ? remoteName : alias!;

    // ------------------------------------------------------------------- Envoi

    public async Task<ChatMessage> SendTextAsync(string peerId, string text)
    {
        if (IsChatBlocked(peerId))
        {
            throw new InvalidOperationException("Vous avez bloqué ce contact : débloquez-le pour discuter.");
        }

        var selfChat = IsSelf(peerId);
        var message = new ChatMessage
        {
            Id = NewId(),
            PeerId = peerId,
            Direction = "outgoing",
            Kind = "text",
            Text = text,
            Status = selfChat ? "delivered" : "sending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _database.AddMessage(message);
        if (!selfChat) _database.TouchStreak(peerId, message.CreatedAt);
        await _hub.BroadcastAsync(new { type = "message", peerId, message });

        // « Messages à moi-même » : le message reste sur ce poste, déjà distribué.
        if (selfChat) return message;

        var outcome = await DeliverGuardedAsync(message, CancellationToken.None);
        message.Status = StatusForOutcome(outcome);
        _database.UpdateMessageStatusIfPending(message.Id, message.Status);

        var stored = _database.GetMessageById(message.Id) ?? message;
        await _hub.BroadcastAsync(new { type = "messageUpdated", peerId, message = stored });
        return stored;
    }

    // ------------------------------- Commentaires de statut dans la discussion

    /// <summary>
    /// Un commentaire de statut reçu apparaît dans la discussion avec son auteur,
    /// comme dans WhatsApp (bulle « a commenté votre statut »).
    /// </summary>
    public async Task AddIncomingStatusCommentAsync(
        string peerId,
        string? senderName,
        string text,
        string statusId)
    {
        if (string.IsNullOrWhiteSpace(peerId) || string.IsNullOrWhiteSpace(text)) return;

        var message = new ChatMessage
        {
            Id = NewId(),
            PeerId = peerId,
            Direction = "incoming",
            Kind = "statuscomment",
            Text = JsonSerializer.Serialize(new { text, statusId }, Json),
            Status = "delivered",
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (!_database.AddMessage(message)) return;
        await _hub.BroadcastAsync(new { type = "message", peerId, message });
    }

    /// <summary>Mon commentaire sur le statut d'un contact reste aussi dans la discussion.</summary>
    public async Task AddOutgoingStatusCommentAsync(string peerId, string text, string statusId)
    {
        if (string.IsNullOrWhiteSpace(peerId) || string.IsNullOrWhiteSpace(text)) return;

        var message = new ChatMessage
        {
            Id = NewId(),
            PeerId = peerId,
            Direction = "outgoing",
            Kind = "statuscomment",
            Text = JsonSerializer.Serialize(new { text, statusId }, Json),
            Status = "delivered",
            IsRead = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (!_database.AddMessage(message)) return;
        await _hub.BroadcastAsync(new { type = "message", peerId, message });
    }

    public async Task<UploadResult> SaveUploadAsync(string peerId, IFormFile file, string kind, int durationMs, bool viewOnce = false)
    {
        if (file.Length <= 0)
        {
            return UploadResult.Failure("Le fichier est vide.");
        }

        if (IsChatBlocked(peerId))
        {
            return UploadResult.Failure("Vous avez bloqué ce contact : débloquez-le pour envoyer un fichier.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return UploadResult.Failure($"Fichier trop volumineux : {_options.MaxFileSizeMb} Mo maximum.", tooLarge: true);
        }

        var fileName = SanitizeFileName(file.FileName);
        var normalizedKind = NormalizeKind(kind, file.ContentType, fileName);
        var fileId = NewId() + SafeExtension(fileName);
        var storedPath = Path.Combine(_database.FilesDirectory, fileId);
        var mimeType = string.IsNullOrWhiteSpace(file.ContentType)
            ? GuessContentType(fileName)
            : file.ContentType;

        await using (var target = File.Create(storedPath))
        {
            await file.CopyToAsync(target);
        }

        var selfChat = IsSelf(peerId);
        var message = new ChatMessage
        {
            Id = NewId(),
            PeerId = peerId,
            Direction = "outgoing",
            Kind = normalizedKind,
            FileId = fileId,
            FileName = fileName,
            MimeType = mimeType,
            FileSize = file.Length,
            DurationMs = Math.Max(0, durationMs),
            IsViewOnce = viewOnce && normalizedKind != "file",
            Status = selfChat ? "delivered" : "sending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _database.AddMessage(message);
        if (!selfChat) _database.TouchStreak(peerId, message.CreatedAt);
        await _hub.BroadcastAsync(new { type = "message", peerId, message });

        // « Messages à moi-même » : le fichier reste sur ce poste, déjà distribué.
        if (selfChat) return UploadResult.Success(message);

        var outcome = await DeliverGuardedAsync(message, CancellationToken.None);
        message.Status = StatusForOutcome(outcome);
        _database.UpdateMessageStatusIfPending(message.Id, message.Status);

        var stored = _database.GetMessageById(message.Id) ?? message;
        await _hub.BroadcastAsync(new { type = "messageUpdated", peerId, message = stored });
        return UploadResult.Success(stored);
    }

    private static string StatusForOutcome(DeliveryOutcome outcome) => outcome switch
    {
        DeliveryOutcome.Delivered => "delivered",
        DeliveryOutcome.Rejected => "failed",
        _ => "queued"
    };

    /// <summary>
    /// Livraison avec verrou : un message en cours de transmission ne peut pas être
    /// annulé au même instant (il est déjà en route).
    /// </summary>
    private async Task<DeliveryOutcome> DeliverGuardedAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        if (!_inFlight.TryAdd(message.Id, 0)) return DeliveryOutcome.Unreachable;
        try
        {
            return await TryDeliverAsync(message, cancellationToken);
        }
        finally
        {
            _inFlight.TryRemove(message.Id, out _);
        }
    }

    /// <summary>Annule l'envoi d'un message pas encore distribué.</summary>
    public async Task<(bool Ok, string? Error)> CancelMessageAsync(string messageId)
    {
        var message = _database.GetMessageById(messageId);
        if (message is null) return (false, "Message introuvable.");
        if (message.Direction != "outgoing") return (false, "Seuls vos propres messages peuvent être annulés.");
        if (message.Status == "canceled") return (true, null);
        if (message.Status == "delivered") return (false, "Ce message a déjà été distribué au destinataire.");
        if (_inFlight.ContainsKey(messageId)) return (false, "Envoi en cours : réessayez dans un instant.");

        if (!_database.TryCancelMessage(messageId))
        {
            return (false, "Ce message a déjà été distribué au destinataire.");
        }

        var updated = _database.GetMessageById(messageId) ?? message;
        updated.Status = "canceled";
        await _hub.BroadcastAsync(new { type = "messageUpdated", peerId = updated.PeerId, message = updated });
        return (true, null);
    }

    // ------------------------------------------------ Modifier / supprimer

    /// <summary>Modifie un message texte envoyé, puis prévient le destinataire.</summary>
    public async Task<(bool Ok, string? Error, ChatMessage? Message)> EditMessageAsync(
        string messageId,
        string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) return (false, "Le message ne peut pas être vide.", null);
        if (value.Length > 8000) return (false, "Message trop long (8000 caractères maximum).", null);

        var message = _database.GetMessageById(messageId);
        if (message is null) return (false, "Message introuvable.", null);
        if (message.Direction != "outgoing") return (false, "Seuls vos propres messages peuvent être modifiés.", null);
        if (message.Kind != "text") return (false, "Seuls les messages texte peuvent être modifiés.", null);
        if (message.Status == "canceled") return (false, "Ce message a été annulé.", null);

        _database.UpdateMessageText(messageId, value, DateTimeOffset.UtcNow);
        var updated = _database.GetMessageById(messageId) ?? message;
        await _hub.BroadcastAsync(new { type = "messageUpdated", peerId = updated.PeerId, message = updated });

        if (!IsSelf(updated.PeerId))
        {
            await RelayMessageChangeAsync(updated.PeerId, "message-edit", new
            {
                senderPeerId = _identity.PeerId,
                messageId,
                text = value
            });
        }

        return (true, null, updated);
    }

    /// <summary>Supprime un message : pour moi seulement, ou pour tout le monde s'il est sortant.</summary>
    public async Task<(bool Ok, string? Error)> DeleteMessageAsync(string messageId, bool forEveryone)
    {
        var message = _database.GetMessageById(messageId);
        if (message is null) return (false, "Message introuvable.");
        if (forEveryone && message.Direction != "outgoing")
        {
            return (false, "Un message reçu ne peut être supprimé que pour vous.");
        }

        if (forEveryone && !IsSelf(message.PeerId))
        {
            await RelayMessageChangeAsync(message.PeerId, "message-delete", new
            {
                senderPeerId = _identity.PeerId,
                messageId
            });
        }

        var fileId = _database.DeleteMessage(messageId);
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            try { File.Delete(Path.Combine(_database.FilesDirectory, Path.GetFileName(fileId))); }
            catch { /* fichier déjà supprimé ou verrouillé */ }
        }

        await _hub.BroadcastAsync(new
        {
            type = "messageDeleted",
            peerId = message.PeerId,
            messageId
        });
        return (true, null);
    }

    /// <summary>Un poste distant a modifié son message : on met à jour notre copie.</summary>
    public async Task AcceptMessageEditAsync(IncomingMessageEditRequest request)
    {
        var messageId = request.MessageId?.Trim();
        var peerId = request.SenderPeerId?.Trim();
        var text = (request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(peerId)) return;
        if (text.Length == 0) return;
        if (text.Length > 16000) text = text[..16000];

        var message = _database.GetMessageById(messageId);
        if (message is null || message.Direction != "incoming" || message.PeerId != peerId) return;
        if (message.Kind != "text") return;

        _database.UpdateMessageText(messageId, text, DateTimeOffset.UtcNow);
        var updated = _database.GetMessageById(messageId) ?? message;
        await _hub.BroadcastAsync(new { type = "messageUpdated", peerId, message = updated });
    }

    /// <summary>Un poste distant a supprimé son message pour tout le monde.</summary>
    public async Task AcceptMessageDeleteAsync(IncomingMessageDeleteRequest request)
    {
        var messageId = request.MessageId?.Trim();
        var peerId = request.SenderPeerId?.Trim();
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(peerId)) return;

        var message = _database.GetMessageById(messageId);
        if (message is null || message.Direction != "incoming" || message.PeerId != peerId) return;

        var fileId = _database.DeleteMessage(messageId);
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            try { File.Delete(Path.Combine(_database.FilesDirectory, Path.GetFileName(fileId))); }
            catch { /* ignoré */ }
        }

        await _hub.BroadcastAsync(new { type = "messageDeleted", peerId, messageId });
    }

    private async Task RelayMessageChangeAsync(string peerId, string route, object payload)
    {
        var endpoint = ResolveEndpoint(peerId);
        if (endpoint is null) return;

        try
        {
            var client = _httpClientFactory.CreateClient("peer");
            using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox/{route}", payload, Json);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Changement de message non transmis à {PeerId} ({Status})", peerId, response.StatusCode);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Changement de message non transmis à {PeerId}", peerId);
        }
    }

    /// <summary>Tente la livraison immédiate d'un message.</summary>
    private async Task<DeliveryOutcome> TryDeliverAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        if (IsChatBlocked(message.PeerId)) return DeliveryOutcome.Rejected;

        var endpoint = ResolveEndpoint(message.PeerId);
        if (endpoint is null) return DeliveryOutcome.Unreachable;

        try
        {
            var client = _httpClientFactory.CreateClient("peer");

            if (message.Kind is "text" or "poll" or "call")
            {
                using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox", new
                {
                    senderPeerId = _identity.PeerId,
                    senderName = _identity.DisplayName,
                    machineName = _identity.MachineName,
                    senderPort = _options.HttpPort,
                    text = message.Text,
                    sentAt = message.CreatedAt,
                    messageId = message.Id,
                    kind = message.Kind,
                    forwarded = message.IsForwarded,
                    durationMs = message.DurationMs
                }, Json, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    _logger.LogInformation("Le poste {PeerId} refuse les messages (contact bloqué)", message.PeerId);
                    return DeliveryOutcome.Rejected;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Le poste {PeerId} a répondu {StatusCode}", message.PeerId, (int)response.StatusCode);
                }
                return response.IsSuccessStatusCode ? DeliveryOutcome.Delivered : DeliveryOutcome.Unreachable;
            }

            if (string.IsNullOrWhiteSpace(message.FileId)) return DeliveryOutcome.Unreachable;

            var path = Path.Combine(_database.FilesDirectory, Path.GetFileName(message.FileId));
            if (!File.Exists(path)) return DeliveryOutcome.Unreachable;

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_identity.PeerId), "senderPeerId");
            form.Add(new StringContent(_identity.DisplayName), "senderName");
            form.Add(new StringContent(_identity.MachineName), "machineName");
            form.Add(new StringContent(_options.HttpPort.ToString()), "senderPort");
            form.Add(new StringContent(message.Id), "messageId");
            form.Add(new StringContent(message.Kind), "kind");
            form.Add(new StringContent(message.DurationMs.ToString()), "durationMs");
            form.Add(new StringContent(message.CreatedAt.ToUniversalTime().ToString("O")), "sentAt");
            if (message.IsForwarded) form.Add(new StringContent("1"), "forwarded");
            if (message.IsViewOnce) form.Add(new StringContent("1"), "viewOnce");

            var stream = new StreamContent(File.OpenRead(path));
            stream.Headers.ContentType = MediaTypeHeaderValue.Parse(message.MimeType ?? "application/octet-stream");
            form.Add(stream, "file", message.FileName ?? "fichier");

            using var upload = await client.PostAsync($"{endpoint}/api/inbox/file", form, cancellationToken);
            if (upload.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogInformation("Le poste {PeerId} refuse les fichiers (contact bloqué)", message.PeerId);
                return DeliveryOutcome.Rejected;
            }

            if (!upload.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transfert refusé par {PeerId} : {StatusCode}", message.PeerId, (int)upload.StatusCode);
            }
            return upload.IsSuccessStatusCode ? DeliveryOutcome.Delivered : DeliveryOutcome.Unreachable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DeliveryOutcome.Unreachable;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Envoi vers {PeerId} impossible, message gardé en attente", message.PeerId);
            return DeliveryOutcome.Unreachable;
        }
    }

    /// <summary>Renvoie les messages en attente à tous les postes redevenus joignables.</summary>
    public async Task RetryPendingAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _retryGate, 1) == 1) return;

        try
        {
            var pending = _database.GetPendingMessages();
            foreach (var message in pending)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Un envoi tout juste lancé par l'utilisateur ne doit pas être doublé.
                if (message.Status == "sending" && message.CreatedAt > DateTimeOffset.UtcNow.AddSeconds(-10)) continue;

                // L'état a pu changer (message annulé) depuis la lecture de la file.
                var current = _database.GetMessageById(message.Id);
                if (current is null || current.Status is "canceled" or "delivered") continue;

                var outcome = await DeliverGuardedAsync(current, cancellationToken);
                if (outcome == DeliveryOutcome.Unreachable) continue;

                var status = StatusForOutcome(outcome);
                if (!_database.UpdateMessageStatusIfPending(current.Id, status)) continue;

                current.Status = status;
                await _hub.BroadcastAsync(new { type = "messageUpdated", peerId = current.PeerId, message = current });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _retryGate, 0);
        }
    }

    // -------------------------------------------------------------- Indicateurs

    public async Task<bool> SendTypingAsync(string peerId, bool typing)
    {
        if (IsChatBlocked(peerId) || IsSelf(peerId)) return false;

        var endpoint = ResolveEndpoint(peerId);
        if (endpoint is null) return false;

        try
        {
            var client = _httpClientFactory.CreateClient("peer");
            using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox/typing", new
            {
                senderPeerId = _identity.PeerId,
                senderName = _identity.DisplayName,
                machineName = _identity.MachineName,
                senderPort = _options.HttpPort,
                typing
            }, Json);

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Indicateur de saisie non transmis à {PeerId}", peerId);
            return false;
        }
    }

    public async Task AcceptTypingAsync(IncomingTypingRequest request, string remoteIp)
    {
        var peerId = request.SenderPeerId?.Trim();
        if (string.IsNullOrWhiteSpace(peerId) || peerId == _identity.PeerId) return;

        RememberSender(peerId, request.SenderName, request.MachineName, request.SenderPort, remoteIp);

        var contact = _database.GetContact(peerId);
        var name = DisplayName(request.SenderName ?? contact?.RemoteName ?? peerId, contact?.Alias);

        await _hub.BroadcastAsync(new { type = "typing", peerId, name, typing = request.Typing == true });
    }

    // ------------------------------------------------------------ Appels WebRTC

    public async Task<bool> ForwardCallSignalAsync(string peerId, CallSignalRequest signal)
    {
        if (IsChatBlocked(peerId) || IsSelf(peerId)) return false;

        var endpoint = ResolveEndpoint(peerId);
        if (endpoint is null) return false;

        try
        {
            var client = _httpClientFactory.CreateClient("peer");
            using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox/signal", new
            {
                senderPeerId = _identity.PeerId,
                senderName = _identity.DisplayName,
                machineName = _identity.MachineName,
                senderPort = _options.HttpPort,
                callId = signal.CallId,
                kind = signal.Kind,
                action = signal.Action,
                sdp = signal.Sdp,
                candidate = signal.Candidate
            }, Json);

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Signal d'appel non transmis à {PeerId}", peerId);
            return false;
        }
    }

    public async Task AcceptCallSignalAsync(IncomingSignalRequest signal, string remoteIp)
    {
        var peerId = signal.SenderPeerId?.Trim();
        if (string.IsNullOrWhiteSpace(peerId) || peerId == _identity.PeerId) return;
        if (string.IsNullOrWhiteSpace(signal.CallId) || string.IsNullOrWhiteSpace(signal.Action)) return;

        RememberSender(peerId, signal.SenderName, signal.MachineName, signal.SenderPort, remoteIp);

        var contact = _database.GetContact(peerId);
        var peerName = DisplayName(signal.SenderName ?? contact?.RemoteName ?? peerId, contact?.Alias);

        await _hub.BroadcastAsync(new
        {
            type = "call",
            peerId,
            peerName,
            callId = signal.CallId,
            kind = signal.Kind ?? "audio",
            action = signal.Action,
            sdp = signal.Sdp,
            candidate = signal.Candidate
        });
    }

    // ---------------------------------------------------------------- Réception

    public async Task<ChatMessage> StoreIncomingTextAsync(IncomingTextRequest request, string remoteIp)
    {
        var peerId = request.SenderPeerId?.Trim();
        var text = request.Text?.Trim();

        if (string.IsNullOrWhiteSpace(peerId))
        {
            throw new ArgumentException("Identifiant d'expéditeur manquant.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message vide.");
        }

        if (text.Length > 16000) text = text[..16000];

        var kind = request.Kind is "poll" or "call" ? request.Kind! : "text";

        RememberSender(peerId, request.SenderName, request.MachineName, request.SenderPort, remoteIp);

        var message = new ChatMessage
        {
            Id = string.IsNullOrWhiteSpace(request.MessageId) ? NewId() : request.MessageId!,
            PeerId = peerId,
            Direction = "incoming",
            Kind = kind,
            Text = text,
            Status = "delivered",
            DurationMs = Math.Max(0, request.DurationMs),
            IsForwarded = request.Forwarded,
            CreatedAt = request.SentAt ?? DateTimeOffset.UtcNow
        };

        // Un renvoi du même message (file d'attente) ne crée pas de doublon.
        if (!_database.AddMessage(message))
        {
            return _database.GetMessageById(message.Id) ?? message;
        }

        if (kind != "statuscomment") _database.TouchStreak(peerId, message.CreatedAt);
        await _hub.BroadcastAsync(new { type = "message", peerId, message });
        return message;
    }

    public async Task<UploadResult> StoreIncomingFileAsync(
        IFormFile file,
        string? senderPeerId,
        string? senderName,
        string? machineName,
        int senderPort,
        string kind,
        int durationMs,
        string remoteIp,
        string? messageId = null,
        DateTimeOffset? sentAt = null,
        bool forwarded = false,
        bool viewOnce = false)
    {
        var peerId = senderPeerId?.Trim();
        if (string.IsNullOrWhiteSpace(peerId))
        {
            return UploadResult.Failure("Identifiant d'expéditeur manquant.");
        }

        if (file.Length <= 0)
        {
            return UploadResult.Failure("Le fichier est vide.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return UploadResult.Failure($"Fichier trop volumineux : {_options.MaxFileSizeMb} Mo maximum.", tooLarge: true);
        }

        RememberSender(peerId, senderName, machineName, senderPort, remoteIp);

        var fileName = SanitizeFileName(file.FileName);
        var normalizedKind = NormalizeKind(kind, file.ContentType, fileName);
        var fileId = NewId() + SafeExtension(fileName);
        var storedPath = Path.Combine(_database.FilesDirectory, fileId);
        var mimeType = string.IsNullOrWhiteSpace(file.ContentType)
            ? GuessContentType(fileName)
            : file.ContentType;

        await using (var target = File.Create(storedPath))
        {
            await file.CopyToAsync(target);
        }

        var message = new ChatMessage
        {
            Id = string.IsNullOrWhiteSpace(messageId) ? NewId() : messageId!,
            PeerId = peerId,
            Direction = "incoming",
            Kind = normalizedKind,
            FileId = fileId,
            FileName = fileName,
            MimeType = mimeType,
            FileSize = file.Length,
            DurationMs = Math.Max(0, durationMs),
            Status = "delivered",
            IsRead = false,
            IsForwarded = forwarded,
            IsViewOnce = viewOnce && normalizedKind != "file",
            CreatedAt = sentAt ?? DateTimeOffset.UtcNow
        };

        if (!_database.AddMessage(message))
        {
            // Message déjà reçu (renvoi automatique) : on supprime la copie inutile.
            try { File.Delete(storedPath); } catch { /* ignoré */ }
            return UploadResult.Success(_database.GetMessageById(message.Id) ?? message);
        }

        _database.TouchStreak(peerId, message.CreatedAt);
        await _hub.BroadcastAsync(new { type = "message", peerId, message });
        return UploadResult.Success(message);
    }

    /// <summary>Marque un média « vue unique » comme consulté et prévient l'interface.</summary>
    public async Task<ChatMessage?> MarkViewOnceOpenedAsync(string messageId)
    {
        var message = _database.GetMessageById(messageId);
        if (message is null || !message.IsViewOnce) return message;

        if (_database.MarkViewOnceOpened(messageId))
        {
            message.ViewOnceOpened = true;
            await _hub.BroadcastAsync(new { type = "messageUpdated", peerId = message.PeerId, message });
        }

        return message;
    }

    private void RememberSender(string peerId, string? name, string? machineName, int port, string ip)
    {
        if (peerId == _identity.PeerId) return;

        var existing = _database.GetContact(peerId);
        var remoteName = string.IsNullOrWhiteSpace(name) ? existing?.RemoteName ?? peerId : name.Trim();
        var machine = string.IsNullOrWhiteSpace(machineName) ? existing?.MachineName ?? remoteName : machineName.Trim();
        var effectivePort = port is > 0 and <= 65535 ? port : existing?.Port ?? 0;
        var avatarVersion = _registry.Get(peerId)?.AvatarVersion ?? existing?.AvatarVersion;

        _database.UpsertContact(new PeerInfo(peerId, remoteName, machine, ip, effectivePort, DateTimeOffset.UtcNow));

        if (effectivePort > 0)
        {
            _registry.Update(new PeerInfo(peerId, remoteName, machine, ip, effectivePort, DateTimeOffset.UtcNow, avatarVersion));
        }
    }

    // ------------------------------------------------------------ Historique des appels

    public CallRecord LogCall(CallLogRequest request)
    {
        var call = new CallRecord
        {
            Id = NewId(),
            PeerId = request.PeerId?.Trim() ?? "",
            Kind = string.Equals(request.Kind, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio",
            Direction = string.Equals(request.Direction, "incoming", StringComparison.OrdinalIgnoreCase)
                ? "incoming"
                : "outgoing",
            Status = request.Status?.ToLowerInvariant() switch
            {
                "missed" => "missed",
                "declined" => "declined",
                _ => "answered"
            },
            DurationMs = Math.Max(0, request.DurationMs),
            StartedAt = DateTimeOffset.UtcNow
        };

        _database.AddCall(call);

        // Comme dans WhatsApp, l'appel apparaît aussi dans la discussion correspondante.
        var callKind = call.Kind == "video" ? "video" : "audio";
        var callText = JsonSerializer.Serialize(new
        {
            kind = callKind,
            result = call.Status
        }, Json);

        var bubble = new ChatMessage
        {
            Id = NewId(),
            PeerId = call.PeerId,
            Direction = call.Direction,
            Kind = "call",
            Text = callText,
            DurationMs = call.DurationMs,
            Status = "delivered",
            IsRead = call.Direction == "outgoing",
            CreatedAt = call.StartedAt
        };
        _database.AddMessage(bubble);
        _ = _hub.BroadcastAsync(new { type = "message", peerId = call.PeerId, message = bubble });
        return call;
    }

    public bool DeleteCall(string callId) => _database.DeleteCall(callId);

    public void DeleteAllCalls() => _database.DeleteAllCalls();

    // ------------------------------------------------- Transfert et sondages

    /// <summary>Transfère des messages vers une autre discussion, avec l'indicateur « Transféré ».</summary>
    public async Task<(int Forwarded, string? Error)> ForwardAsync(string targetPeerId, List<string> messageIds)
    {
        if (IsChatBlocked(targetPeerId))
        {
            return (0, "Vous avez bloqué ce contact : débloquez-le pour transférer.");
        }

        var forwarded = 0;
        var selfChat = IsSelf(targetPeerId);

        foreach (var messageId in messageIds.Take(30))
        {
            var source = _database.GetMessageById(messageId);
            if (source is null) continue;
            if (source.Kind == "call") continue;

            var text = source.Text;
            if (source.Kind == "poll" && text is not null)
            {
                // Un sondage transféré repart sans les votes précédents.
                using var document = JsonDocument.Parse(text);
                var question = document.RootElement.TryGetProperty("question", out var q) ? q.GetString() : null;
                var optionsJson = document.RootElement.TryGetProperty("options", out var o) ? o.GetRawText() : "[]";
                text = $"{{\"question\":{JsonSerializer.Serialize(question)},\"options\":{optionsJson},\"votes\":[]}}";
            }

            var copy = new ChatMessage
            {
                Id = NewId(),
                PeerId = targetPeerId,
                Direction = "outgoing",
                Kind = source.Kind,
                Text = text,
                FileId = source.FileId,
                FileName = source.FileName,
                MimeType = source.MimeType,
                FileSize = source.FileSize,
                DurationMs = source.DurationMs,
                IsViewOnce = source.IsViewOnce,
                IsForwarded = true,
                Status = selfChat ? "delivered" : "sending",
                CreatedAt = DateTimeOffset.UtcNow
            };

            _database.AddMessage(copy);
            await _hub.BroadcastAsync(new { type = "message", peerId = targetPeerId, message = copy });
            forwarded++;

            if (selfChat) continue;

            var outcome = await DeliverGuardedAsync(copy, CancellationToken.None);
            var status = StatusForOutcome(outcome);
            _database.UpdateMessageStatusIfPending(copy.Id, status);
            copy.Status = status;
            await _hub.BroadcastAsync(new { type = "messageUpdated", peerId = targetPeerId, message = copy });
        }

        return (forwarded, null);
    }

    public async Task<(bool Ok, string? Error, ChatMessage? Message)> SendPollAsync(string peerId, PollRequest request)
    {
        var question = (request.Question ?? string.Empty).Trim();
        var options = (request.Options ?? new List<string>())
            .Select(option => (option ?? string.Empty).Trim())
            .Where(option => option.Length > 0)
            .Take(12)
            .ToList();

        if (question.Length == 0) return (false, "La question du sondage est vide.", null);
        if (options.Count < 2) return (false, "Ajoutez au moins deux options.", null);
        if (question.Length > 300) question = question[..300];

        var data = new
        {
            question,
            options = options.Select((text, index) => new { id = $"o{index}", text }),
            votes = Array.Empty<object>()
        };

        var selfChat = IsSelf(peerId);
        var message = new ChatMessage
        {
            Id = NewId(),
            PeerId = peerId,
            Direction = "outgoing",
            Kind = "poll",
            Text = JsonSerializer.Serialize(data, Json),
            Status = selfChat ? "delivered" : "sending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _database.AddMessage(message);
        await _hub.BroadcastAsync(new { type = "message", peerId, message });
        if (selfChat) return (true, null, message);

        var outcome = await DeliverGuardedAsync(message, CancellationToken.None);
        var status = StatusForOutcome(outcome);
        _database.UpdateMessageStatusIfPending(message.Id, status);
        var stored = _database.GetMessageById(message.Id) ?? message;
        await _hub.BroadcastAsync(new { type = "messageUpdated", peerId, message = stored });
        return (true, null, stored);
    }

    /// <summary>Enregistre mon vote sur un sondage et le transmet à l'autre poste.</summary>
    public async Task<bool> VotePollAsync(string messageId, string optionId, bool notifyPeer = true)
    {
        var message = _database.GetMessageById(messageId);
        if (message is null || message.Kind != "poll") return false;

        var updated = ApplyVote(message.Text, _identity.PeerId, _identity.DisplayName, optionId);
        if (updated is null) return false;

        _database.UpdateMessageText(message.Id, updated);
        await _hub.BroadcastAsync(new
        {
            type = "messageUpdated",
            peerId = message.PeerId,
            message = _database.GetMessageById(message.Id)
        });

        if (!notifyPeer || IsSelf(message.PeerId)) return true;

        var endpoint = ResolveEndpoint(message.PeerId);
        if (endpoint is null) return true;

        try
        {
            var client = _httpClientFactory.CreateClient("peer");
            using var response = await client.PostAsJsonAsync($"{endpoint}/api/inbox/poll-vote", new
            {
                senderPeerId = _identity.PeerId,
                senderName = _identity.DisplayName,
                messageId = message.Id,
                optionId
            }, Json);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Vote du sondage non transmis à {PeerId}", message.PeerId);
            return true;
        }
    }

    public async Task AcceptPollVoteAsync(IncomingPollVoteRequest request)
    {
        var peerId = request.SenderPeerId?.Trim();
        var messageId = request.MessageId?.Trim();
        var optionId = request.OptionId?.Trim();
        if (string.IsNullOrWhiteSpace(peerId) || string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(optionId)) return;

        var message = _database.GetMessageById(messageId);
        if (message is null || message.Kind != "poll") return;

        var updated = ApplyVote(message.Text, peerId, request.SenderName ?? peerId, optionId);
        if (updated is null) return;

        _database.UpdateMessageText(message.Id, updated);
        await _hub.BroadcastAsync(new
        {
            type = "messageUpdated",
            peerId = message.PeerId,
            message = _database.GetMessageById(message.Id)
        });
    }

    /// <summary>Ajoute ou remplace le vote d'un participant dans le JSON du sondage.</summary>
    private static string? ApplyVote(string? pollJson, string peerId, string? name, string optionId)
    {
        if (string.IsNullOrWhiteSpace(pollJson)) return null;

        try
        {
            var poll = JsonSerializer.Deserialize<PollDto>(pollJson, Json);
            if (poll?.options is null || poll.options.Count == 0) return null;
            if (!poll.options.Any(option => string.Equals(option.id, optionId, StringComparison.Ordinal))) return null;

            poll.votes.RemoveAll(vote => string.Equals(vote.peerId, peerId, StringComparison.Ordinal));
            poll.votes.Add(new PollVoteDto
            {
                peerId = peerId,
                name = string.IsNullOrWhiteSpace(name) ? peerId : name,
                option = optionId
            });

            return JsonSerializer.Serialize(poll, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class PollOptionDto
    {
        public string? id { get; set; }
        public string? text { get; set; }
    }

    private sealed class PollVoteDto
    {
        public string? peerId { get; set; }
        public string? name { get; set; }
        public string? option { get; set; }
    }

    private sealed class PollDto
    {
        public string? question { get; set; }
        public List<PollOptionDto> options { get; set; } = new();
        public List<PollVoteDto> votes { get; set; } = new();
    }

    public (bool Ok, string? Error) ClearConversation(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId)) return (false, "Contact inconnu.");
        _database.ClearConversation(peerId);
        _ = _hub.BroadcastAsync(new { type = "conversationCleared", peerId });
        return (true, null);
    }

    public (bool Ok, string? Error) DeleteConversation(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId)) return (false, "Contact inconnu.");
        if (IsSelf(peerId)) return (false, "La discussion avec vous-même ne peut pas être supprimée.");
        foreach (var fileId in _database.DeleteConversation(peerId))
        {
            try { File.Delete(Path.Combine(_database.FilesDirectory, Path.GetFileName(fileId))); } catch { /* ignoré */ }
        }
        _ = _hub.BroadcastAsync(new { type = "conversationDeleted", peerId });
        return (true, null);
    }

    /// <summary>Médias, liens et documents partagés dans une discussion.</summary>
    public object GetShared(string peerId)
    {
        var messages = _database.GetMessages(peerId, 2000);
        var media = new List<object>();
        var documents = new List<object>();
        var links = new List<object>();

        foreach (var message in messages)
        {
            if (message.Kind is "image" or "video" && message.FileId is { Length: > 0 })
            {
                media.Add(new
                {
                    id = message.Id,
                    kind = message.Kind == "video" ? "video" : "image",
                    fileId = message.FileId,
                    fileName = message.FileName,
                    mimeType = message.MimeType,
                    fileSize = message.FileSize,
                    durationMs = message.DurationMs,
                    createdAt = message.CreatedAt
                });
            }
            else if (message.Kind == "file" && message.FileId is { Length: > 0 })
            {
                documents.Add(new
                {
                    id = message.Id,
                    fileName = message.FileName,
                    mimeType = message.MimeType,
                    fileSize = message.FileSize,
                    createdAt = message.CreatedAt
                });
            }

            if (message.Kind == "text" && !string.IsNullOrWhiteSpace(message.Text))
            {
                foreach (System.Text.RegularExpressions.Match match in
                         System.Text.RegularExpressions.Regex.Matches(message.Text, @"https?://[^\s<>""']+"))
                {
                    links.Add(new { messageId = message.Id, url = match.Value, createdAt = message.CreatedAt });
                }
            }
        }

        return new { media, documents, links };
    }

    public List<object> GetCalls()
    {
        var contacts = _database.GetAllContacts().ToDictionary(c => c.PeerId, StringComparer.Ordinal);
        var calls = new List<object>();

        foreach (var call in _database.GetCalls())
        {
            contacts.TryGetValue(call.PeerId, out var contact);
            var live = _registry.Get(call.PeerId);
            var remoteName = contact?.RemoteName ?? live?.Name ?? call.PeerId;

            calls.Add(new
            {
                id = call.Id,
                peerId = call.PeerId,
                name = DisplayName(remoteName, contact?.Alias),
                kind = call.Kind,
                direction = call.Direction,
                status = call.Status,
                durationMs = call.DurationMs,
                startedAt = call.StartedAt,
                online = live?.IsOnline == true,
                avatarVersion = contact?.AvatarVersion ?? live?.AvatarVersion
            });
        }

        return calls;
    }

    // ------------------------------------------------------------------- Utils

    /// <summary>Adresse HTTP du poste : découverte récente, sinon dernière adresse connue.</summary>
    public string? ResolveEndpoint(string peerId)
    {
        var live = _registry.Get(peerId);
        if (live is { IsOnline: true, Port: > 0 } && !string.IsNullOrWhiteSpace(live.Ip))
        {
            return $"http://{live.Ip}:{live.Port}";
        }

        var contact = _database.GetContact(peerId);
        if (contact is { Port: > 0 } && !string.IsNullOrWhiteSpace(contact.Ip))
        {
            return $"http://{contact.Ip}:{contact.Port}";
        }

        return null;
    }

    private enum DeliveryOutcome
    {
        Delivered,
        Unreachable,
        /// <summary>Le destinataire a refusé l'envoi (nous sommes bloqués).</summary>
        Rejected
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    public static string SanitizeFileName(string? fileName)
    {
        var safe = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(safe)) safe = "fichier";
        return safe.Length > 180 ? safe[^180..] : safe;
    }

    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || extension.Length > 12) return "";
        return extension.All(character => char.IsLetterOrDigit(character) || character == '.') ? extension : "";
    }

    private static string NormalizeKind(string? kind, string? contentType, string fileName)
    {
        var value = (kind ?? "").Trim().ToLowerInvariant();
        if (value is "image" or "video" or "audio" or "file") return value;

        var type = contentType ?? GuessContentType(fileName);
        if (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "image";
        if (type.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return "video";
        if (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "audio";
        return "file";
    }

    private static string GuessContentType(string fileName) =>
        ContentTypes.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";
}

public sealed record UploadResult(bool Ok, bool TooLarge, string? Error, ChatMessage? Message)
{
    public static UploadResult Success(ChatMessage message) => new(true, false, null, message);
    public static UploadResult Failure(string error, bool tooLarge = false) => new(false, tooLarge, error, null);
}
