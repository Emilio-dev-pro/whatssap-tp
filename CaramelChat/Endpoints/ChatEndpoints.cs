using System.Net;
using CaramelChat.Data;
using CaramelChat.Models;
using CaramelChat.Services;

namespace CaramelChat.Endpoints;

/// <summary>Toutes les routes HTTP (interface + échanges entre postes).</summary>
public static class ChatEndpoints
{
    public static void MapChatApi(this WebApplication app)
    {
        // ------------------------------------------------------------- Mon poste

        app.MapGet("/api/me", (LocalIdentity me, ChatOptions options, AvatarStore avatars) => Results.Ok(new
        {
            peerId = me.PeerId,
            displayName = me.DisplayName,
            machineName = me.MachineName,
            ip = me.LocalIp,
            port = options.HttpPort,
            httpsPort = options.HttpsPort,
            httpsUrl = $"https://{me.LocalIp}:{options.HttpsPort}",
            maxFileSizeMb = options.MaxFileSizeMb,
            discoveryPort = options.DiscoveryPort,
            avatarVersion = avatars.LocalVersion
        }));

        app.MapPut("/api/me", async (RenameRequest request, LocalIdentity me, RealtimeHub hub) =>
        {
            var name = (request.Name ?? "").Trim();
            if (name.Length == 0) return Results.BadRequest(new { error = "Le nom ne peut pas être vide." });

            me.SetDisplayName(name);
            await hub.BroadcastAsync(new { type = "me", displayName = me.DisplayName });
            return Results.Ok(new { displayName = me.DisplayName });
        });

        // -------------------------------------------------- Photos de profil

        app.MapGet("/api/me/avatar", (AvatarStore avatars) =>
        {
            var path = avatars.LocalFilePath;
            return path is null
                ? Results.NotFound()
                : Results.File(path, avatars.LocalContentType);
        });

        app.MapPut("/api/me/avatar", async (HttpRequest request, AvatarStore avatars) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Requête multipart attendue." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { error = "Aucune image reçue." });

            var result = await avatars.SaveLocalAsync(file);
            if (!result.Ok) return Results.BadRequest(new { error = result.Error });

            return Results.Ok(new { avatarVersion = avatars.LocalVersion });
        });

        app.MapGet("/api/peers/{peerId}/avatar", (string peerId, AvatarStore avatars) =>
        {
            var path = avatars.GetPeerFilePath(peerId);
            return path is null
                ? Results.NotFound()
                : Results.File(path, avatars.GetPeerContentType(peerId));
        });

        // ------------------------------------------------------------ Les pairs

        app.MapGet("/api/peers", (ChatService chat) => Results.Ok(chat.GetPeers()));

        app.MapPut("/api/peers/{peerId}/alias", (string peerId, RenameRequest request, ChatService chat) =>
        {
            var alias = (request.Name ?? "").Trim();
            chat.SetAlias(peerId, alias.Length == 0 ? null : alias);
            return Results.Ok(new { peerId, alias = alias.Length == 0 ? null : alias });
        });

        // --------------------------------------------------------- Discussions

        app.MapGet("/api/conversations", (ChatService chat) => Results.Ok(chat.GetConversations()));

        app.MapGet("/api/peers/{peerId}/messages", (string peerId, ChatService chat, AppDatabase database) =>
        {
            chat.MarkRead(peerId);
            return Results.Ok(database.GetMessages(peerId));
        });

        // Recherche dans une discussion, comme la loupe de WhatsApp.
        app.MapGet("/api/peers/{peerId}/search", (string peerId, string? q, AppDatabase database) =>
        {
            if (string.IsNullOrWhiteSpace(peerId))
            {
                return Results.BadRequest(new { error = "Contact inconnu." });
            }

            var query = (q ?? "").Trim();
            if (query.Length > 200) query = query[..200];

            return Results.Ok(database.SearchMessages(peerId, query));
        });

        app.MapPost("/api/peers/{peerId}/read", (string peerId, ChatService chat) =>
        {
            chat.MarkRead(peerId);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/peers/{peerId}/messages", async (string peerId, SendTextRequest request, ChatService chat) =>
        {
            var text = (request.Text ?? "").Trim();
            if (text.Length == 0) return Results.BadRequest(new { error = "Le message est vide." });
            if (text.Length > 8000) return Results.BadRequest(new { error = "Message trop long (8000 caractères maximum)." });
            if (string.IsNullOrWhiteSpace(peerId)) return Results.BadRequest(new { error = "Destinataire inconnu." });
            if (chat.IsChatBlocked(peerId))
            {
                return Results.BadRequest(new { error = "Vous avez bloqué ce contact : débloquez-le pour discuter." });
            }

            try
            {
                var message = await chat.SendTextAsync(peerId, text);
                return Results.Ok(message);
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        // Annulation d'un message envoyé par erreur : seulement tant qu'il n'est pas distribué.
        app.MapPost("/api/messages/{messageId}/cancel", async (string messageId, ChatService chat) =>
        {
            var result = await chat.CancelMessageAsync(messageId);
            return result.Ok
                ? Results.Ok(new { ok = true })
                : Results.BadRequest(new { error = result.Error });
        });

        // Média « vue unique » : une fois consulté, il ne peut plus être rouvert.
        app.MapPost("/api/messages/{messageId}/viewed", async (string messageId, ChatService chat) =>
        {
            var message = await chat.MarkViewOnceOpenedAsync(messageId);
            return message is null
                ? Results.NotFound(new { error = "Message introuvable." })
                : Results.Ok(message);
        });

        // Modifier un message texte déjà envoyé (comme « Modifier » dans WhatsApp).
        app.MapPut("/api/messages/{messageId}", async (string messageId, EditMessageRequest request, ChatService chat) =>
        {
            var result = await chat.EditMessageAsync(messageId, request.Text);
            return result.Ok
                ? Results.Ok(result.Message)
                : Results.BadRequest(new { error = result.Error });
        });

        // Supprimer un message : pour moi, ou pour tout le monde s'il est sortant.
        app.MapDelete("/api/messages/{messageId}", async (string messageId, string? forEveryone, ChatService chat) =>
        {
            var everyone = string.Equals(forEveryone, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(forEveryone, "true", StringComparison.OrdinalIgnoreCase);
            var result = await chat.DeleteMessageAsync(messageId, everyone);
            return result.Ok
                ? Results.Ok(new { ok = true })
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapPost("/api/peers/{peerId}/upload", async (string peerId, HttpRequest request, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(peerId)) return Results.BadRequest(new { error = "Destinataire inconnu." });
            if (chat.IsChatBlocked(peerId))
            {
                return Results.BadRequest(new { error = "Vous avez bloqué ce contact : débloquez-le pour envoyer un fichier." });
            }
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Requête multipart attendue." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { error = "Aucun fichier reçu." });

            var kind = form["kind"].ToString();
            _ = int.TryParse(form["durationMs"], out var durationMs);
            var viewOnce = form["viewOnce"].ToString() == "1";

            var result = await chat.SaveUploadAsync(peerId, file, kind, durationMs, viewOnce);
            if (!result.Ok)
            {
                var status = result.TooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest;
                return Results.Json(new { error = result.Error }, statusCode: status);
            }

            return Results.Ok(result.Message);
        });

        // ------------------------------------------ Indicateur « est en train d'écrire »

        // ------------------------------------------------------ Sondages

        app.MapPost("/api/peers/{peerId}/poll", async (string peerId, PollRequest request, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(peerId)) return Results.BadRequest(new { error = "Destinataire inconnu." });
            if (chat.IsChatBlocked(peerId))
            {
                return Results.BadRequest(new { error = "Vous avez bloqué ce contact : débloquez-le pour envoyer un sondage." });
            }

            var result = await chat.SendPollAsync(peerId, request);
            return result.Ok ? Results.Ok(result.Message) : Results.BadRequest(new { error = result.Error });
        });

        app.MapPost("/api/messages/{messageId}/vote", async (string messageId, PollVoteRequest request, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(request.OptionId))
            {
                return Results.BadRequest(new { error = "Option inconnue." });
            }
            var ok = await chat.VotePollAsync(messageId, request.OptionId);
            return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = "Vote impossible." });
        });

        // ------------------------------------------------------ Transfert

        app.MapPost("/api/peers/{peerId}/forward", async (string peerId, ForwardRequest request, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(peerId)) return Results.BadRequest(new { error = "Destinataire inconnu." });
            var ids = (request.MessageIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            if (ids.Count == 0) return Results.BadRequest(new { error = "Aucun message à transférer." });

            var result = await chat.ForwardAsync(peerId, ids);
            return result.Error is null
                ? Results.Ok(new { ok = true, forwarded = result.Forwarded })
                : Results.BadRequest(new { error = result.Error });
        });

        // ------------------------------------ Médias, liens et documents partagés

        app.MapGet("/api/peers/{peerId}/shared", (string peerId, ChatService chat) =>
            Results.Ok(chat.GetShared(peerId)));

        // ------------------------------- Effacer / supprimer une discussion

        app.MapDelete("/api/peers/{peerId}/messages", (string peerId, ChatService chat) =>
        {
            var result = chat.ClearConversation(peerId);
            return result.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = result.Error });
        });

        app.MapDelete("/api/peers/{peerId}", (string peerId, ChatService chat) =>
        {
            var result = chat.DeleteConversation(peerId);
            return result.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = result.Error });
        });

        app.MapPost("/api/peers/{peerId}/typing", async (string peerId, TypingRequest request, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(peerId)) return Results.BadRequest(new { error = "Destinataire inconnu." });
            var ok = await chat.SendTypingAsync(peerId, request.Typing == true);
            return Results.Ok(new { ok });
        });

        // ------------------------------------------------------------- Blocage

        app.MapGet("/api/blocks", (ChatService chat) => Results.Ok(chat.GetBlocks()));

        app.MapPut("/api/peers/{peerId}/block", (string peerId, BlockRequest request, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(peerId)) return Results.BadRequest(new { error = "Contact inconnu." });
            var block = chat.SetBlock(peerId, request.Chat, request.Status);
            return Results.Ok(new { peerId, chat = block.Chat, status = block.Status });
        });

        // ------------------------------------------------------------- Statuts

        app.MapGet("/api/statuses", async (StatusService statuses) => Results.Ok(await statuses.GetFeedAsync()));

        app.MapPost("/api/statuses", (StatusTextRequest request, StatusService statuses) =>
        {
            var result = statuses.AddText(request);
            return result.Ok
                ? Results.Ok(result.Status)
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapPost("/api/statuses/upload", async (HttpRequest request, StatusService statuses) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Requête multipart attendue." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { error = "Aucun fichier reçu." });

            _ = int.TryParse(form["durationMs"], out var durationMs);
            var privacyPeers = new List<string>();
            if (!string.IsNullOrWhiteSpace(form["privacyList"]))
            {
                try
                {
                    privacyPeers = System.Text.Json.JsonSerializer.Deserialize<List<string>>(form["privacyList"].ToString()) ?? new();
                }
                catch (System.Text.Json.JsonException)
                {
                    privacyPeers = new List<string>();
                }
            }
            var result = await statuses.AddMediaAsync(file, form["caption"], durationMs, form["privacy"], privacyPeers);
            if (!result.Ok) return Results.BadRequest(new { error = result.Error });
            return Results.Ok(result.Status);
        });

        app.MapDelete("/api/statuses/{statusId}", (string statusId, StatusService statuses) =>
            statuses.Delete(statusId) ? Results.Ok(new { ok = true }) : Results.NotFound());

        // Confidentialité choisie pour un statut, et mise en pause.
        app.MapPut("/api/statuses/{statusId}/privacy", (string statusId, StatusPrivacyRequest request, StatusService statuses) =>
            statuses.SetPrivacy(statusId, request)
                ? Results.Ok(new { ok = true })
                : Results.NotFound());

        app.MapPost("/api/statuses/{statusId}/pause", (string statusId, bool? paused, StatusService statuses) =>
            statuses.SetPaused(statusId, paused == true)
                ? Results.Ok(new { ok = true, paused = paused == true })
                : Results.NotFound());

        // J'aime et commentaires sur mes propres statuts (stockage local).
        app.MapPost("/api/statuses/{statusId}/like", async (string statusId, StatusLikeRequest request, StatusService statuses) =>
        {
            await statuses.RecordLocalLikeAsync(statusId, request.Liked != false);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/statuses/{statusId}/comment", async (string statusId, StatusCommentRequest request, StatusService statuses) =>
        {
            var ok = await statuses.RecordLocalCommentAsync(statusId, request.Text);
            return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = "Commentaire vide." });
        });

        // J'aime / commentaire envoyés au propriétaire d'un statut du réseau.
        app.MapPost("/api/statuses/peer/{peerId}/{statusId}/like", async (string peerId, string statusId, StatusLikeRequest request, StatusService statuses) =>
        {
            var ok = await statuses.LikePeerStatusAsync(peerId, statusId, request.Liked != false);
            return Results.Ok(new { ok });
        });

        app.MapPost("/api/statuses/peer/{peerId}/{statusId}/comment", async (string peerId, string statusId, StatusCommentRequest request, StatusService statuses) =>
        {
            var okay = await statuses.CommentPeerStatusAsync(peerId, statusId, request.Text ?? string.Empty);
            return Results.Ok(new { ok = okay });
        });

        app.MapPost("/api/inbox/status-like", async (IncomingStatusLikeRequest request, StatusService statuses) =>
        {
            await statuses.RecordLikeAsync(request);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/inbox/status-comment", async (IncomingStatusCommentRequest request, StatusService statuses) =>
        {
            await statuses.RecordCommentAsync(request);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/inbox/poll-vote", async (IncomingPollVoteRequest request, ChatService chat) =>
        {
            await chat.AcceptPollVoteAsync(request);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/statuses/feed", (string? viewer, StatusService statuses) =>
            Results.Ok(statuses.FeedForPeer(viewer ?? string.Empty)));

        app.MapGet("/api/statuses/{statusId}/media", (string statusId, string? viewer, StatusService statuses) =>
        {
            var media = statuses.GetMedia(statusId, viewer);
            return media is null
                ? Results.NotFound()
                : Results.File(media.Value.Path, media.Value.ContentType, enableRangeProcessing: true);
        });

        app.MapGet("/api/statuses/peer/{peerId}/{statusId}/media", async (string peerId, string statusId, StatusService statuses) =>
        {
            var media = await statuses.OpenPeerMediaAsync(peerId, statusId);
            return media is null
                ? Results.NotFound()
                : Results.Stream(media.Value.Stream, media.Value.ContentType, enableRangeProcessing: true);
        });

        app.MapPost("/api/statuses/peer/{peerId}/{statusId}/view", async (string peerId, string statusId, StatusService statuses) =>
        {
            var ok = await statuses.MarkPeerStatusViewedAsync(peerId, statusId);
            return Results.Ok(new { ok });
        });

        app.MapPost("/api/inbox/status-view", async (StatusViewRequest request, StatusService statuses) =>
        {
            await statuses.RecordViewAsync(request);
            return Results.Ok(new { ok = true });
        });

        // ------------------------------------------------------- Appels audio/vidéo

        app.MapPost("/api/peers/{peerId}/call", async (string peerId, CallSignalRequest signal, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(peerId)) return Results.BadRequest(new { error = "Destinataire inconnu." });
            if (chat.IsChatBlocked(peerId))
            {
                return Results.BadRequest(new { error = "Vous avez bloqué ce contact : débloquez-le pour l'appeler." });
            }
            if (string.IsNullOrWhiteSpace(signal.CallId) || string.IsNullOrWhiteSpace(signal.Action))
            {
                return Results.BadRequest(new { error = "Signal d'appel incomplet." });
            }

            var ok = await chat.ForwardCallSignalAsync(peerId, signal);
            return Results.Ok(new { ok });
        });

        // --------------------------------------------------- Historique des appels

        app.MapGet("/api/calls", (ChatService chat) => Results.Ok(chat.GetCalls()));

        app.MapPost("/api/calls", (CallLogRequest request, ChatService chat) =>
        {
            if (string.IsNullOrWhiteSpace(request.PeerId))
            {
                return Results.BadRequest(new { error = "Contact inconnu." });
            }

            return Results.Ok(chat.LogCall(request));
        });

        app.MapDelete("/api/calls/{callId}", (string callId, ChatService chat) =>
            chat.DeleteCall(callId) ? Results.Ok(new { ok = true }) : Results.NotFound());

        app.MapDelete("/api/calls", (ChatService chat) =>
        {
            chat.DeleteAllCalls();
            return Results.Ok(new { ok = true });
        });

        // ------------------------------------------------------------- Fichiers

        // « download » accepte 1/true : le pont de bureau et le navigateur construisent
        // l'adresse avec ?download=1, que la liaison booléenne d'ASP.NET refuse.
        app.MapGet("/api/files/{fileId}", (string fileId, string? download, AppDatabase database) =>
        {
            var message = database.GetMessageByFileId(fileId);
            if (message is null) return Results.NotFound();

            var path = Path.Combine(database.FilesDirectory, Path.GetFileName(fileId));
            if (!File.Exists(path)) return Results.NotFound();

            var contentType = string.IsNullOrWhiteSpace(message.MimeType)
                ? "application/octet-stream"
                : message.MimeType;

            var wantsDownload = string.Equals(download, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(download, "true", StringComparison.OrdinalIgnoreCase);
            var downloadName = wantsDownload ? message.FileName ?? "fichier" : null;
            return Results.File(path, contentType, downloadName, enableRangeProcessing: true);
        });

        // -------------------------------------------- Réception entre les postes

        app.MapPost("/api/inbox", async (IncomingTextRequest request, HttpContext context, ChatService chat) =>
        {
            if (chat.IsChatBlocked(request.SenderPeerId ?? string.Empty))
            {
                return Results.Json(new { error = "Ce contact est bloqué." }, statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var message = await chat.StoreIncomingTextAsync(request, RemoteIp(context));
                return Results.Ok(new { ok = true, id = message.Id });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPost("/api/inbox/file", async (HttpRequest request, HttpContext context, ChatService chat) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Requête multipart attendue." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { error = "Aucun fichier reçu." });

            if (chat.IsChatBlocked(form["senderPeerId"].ToString()))
            {
                return Results.Json(new { error = "Ce contact est bloqué." }, statusCode: StatusCodes.Status403Forbidden);
            }

            _ = int.TryParse(form["senderPort"], out var senderPort);
            _ = int.TryParse(form["durationMs"], out var durationMs);

            DateTimeOffset? sentAt = null;
            if (DateTimeOffset.TryParse(form["sentAt"], out var parsed)) sentAt = parsed;

            var result = await chat.StoreIncomingFileAsync(
                file,
                form["senderPeerId"],
                form["senderName"],
                form["machineName"],
                senderPort,
                form["kind"].ToString(),
                durationMs,
                RemoteIp(context),
                form["messageId"],
                sentAt,
                form["forwarded"].ToString() == "1",
                form["viewOnce"].ToString() == "1");

            if (!result.Ok)
            {
                var status = result.TooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest;
                return Results.Json(new { error = result.Error }, statusCode: status);
            }

            return Results.Ok(new { ok = true, id = result.Message!.Id });
        });

        app.MapPost("/api/inbox/message-edit", async (IncomingMessageEditRequest request, ChatService chat) =>
        {
            await chat.AcceptMessageEditAsync(request);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/inbox/message-delete", async (IncomingMessageDeleteRequest request, ChatService chat) =>
        {
            await chat.AcceptMessageDeleteAsync(request);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/inbox/typing", async (IncomingTypingRequest request, HttpContext context, ChatService chat) =>
        {
            if (chat.IsChatBlocked(request.SenderPeerId ?? string.Empty))
            {
                return Results.Json(new { error = "Ce contact est bloqué." }, statusCode: StatusCodes.Status403Forbidden);
            }

            await chat.AcceptTypingAsync(request, RemoteIp(context));
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/inbox/signal", async (IncomingSignalRequest signal, HttpContext context, ChatService chat) =>
        {
            if (chat.IsChatBlocked(signal.SenderPeerId ?? string.Empty))
            {
                return Results.Json(new { error = "Ce contact est bloqué." }, statusCode: StatusCodes.Status403Forbidden);
            }

            await chat.AcceptCallSignalAsync(signal, RemoteIp(context));
            return Results.Ok(new { ok = true });
        });

        // ---------------------------------------------------------- Traduction

        app.MapGet("/api/translation/settings", (TranslationService translation) =>
            Results.Ok(translation.GetSettingsDto()));

        app.MapPut("/api/translation/settings", (TranslationSettingsRequest request, TranslationService translation) =>
            Results.Ok(translation.SaveSettings(request)));

        app.MapGet("/api/translation/status", async (TranslationService translation) =>
            Results.Ok(await translation.GetStatusAsync()));

        app.MapGet("/api/translation/languages", async (TranslationService translation) =>
            Results.Ok(await translation.GetLanguagesAsync()));

        app.MapPost("/api/translation/translate", async (TranslationRequest request, TranslationService translation) =>
        {
            try
            {
                return Results.Ok(await translation.TranslateAsync(request.Text, request.From, request.To));
            }
            catch (TranslationException exception)
            {
                return Results.BadRequest(new { ok = false, error = exception.Message });
            }
        });

        app.MapGet("/api/translation/models", async (TranslationService translation) =>
            Results.Ok(await translation.GetModelsAsync()));

        app.MapPost("/api/translation/models/install", (TranslationInstallRequest request, TranslationService translation) =>
        {
            try
            {
                return Results.Ok(translation.StartInstall(request.From, request.To));
            }
            catch (TranslationException exception)
            {
                return Results.BadRequest(new { ok = false, error = exception.Message });
            }
        });

        app.MapGet("/api/translation/models/jobs/{jobId}", (string jobId, TranslationService translation) =>
        {
            var job = translation.GetInstallJob(jobId);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });

        app.MapPost("/api/translation/models/cancel", (TranslationService translation) =>
            translation.CancelInstall()
                ? Results.Ok(new { ok = true })
                : Results.BadRequest(new { error = "Aucun téléchargement en cours." }));

        app.MapDelete("/api/translation/models", async (string? from, string? to, TranslationService translation) =>
        {
            // La suppression passe par le pont (suppression du dossier du modèle).
            var result = await translation.RemoveModelAsync(from, to);
            return result.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = result.Error });
        });

        app.MapGet("/api/translation/cache", (TranslationService translation) =>
            Results.Ok(translation.GetCacheStats()));

        app.MapDelete("/api/translation/cache", (TranslationService translation) =>
        {
            translation.ClearCache();
            return Results.Ok(new { ok = true });
        });
    }

    private static string RemoteIp(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return "0.0.0.0";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString();
    }
}
