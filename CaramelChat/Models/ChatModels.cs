namespace CaramelChat.Models;

/// <summary>Réglages lus depuis appsettings.json (section "Chat").</summary>
public sealed class ChatOptions
{
    public int HttpPort { get; set; } = 5080;
    /// <summary>Port HTTPS (certificat auto-signé) : microphone, vocaux et appels.</summary>
    public int HttpsPort { get; set; } = 5443;
    public int DiscoveryPort { get; set; } = 50001;
    public int MaxFileSizeMb { get; set; } = 64;

    public long MaxFileSizeBytes => Math.Max(1, MaxFileSizeMb) * 1024L * 1024L;
}

/// <summary>Un message stocké en SQLite (texte, image, vocal ou fichier).</summary>
public sealed class ChatMessage
{
    public string Id { get; set; } = "";
    public string PeerId { get; set; } = "";
    /// <summary>"outgoing" ou "incoming".</summary>
    public string Direction { get; set; } = "outgoing";
    /// <summary>"text", "image", "video", "audio" ou "file".</summary>
    public string Kind { get; set; } = "text";
    public string? Text { get; set; }
    /// <summary>Identifiant du fichier stocké sur le disque (avec extension).</summary>
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public long FileSize { get; set; }
    public int DurationMs { get; set; }
    /// <summary>
    /// "sending" (en cours), "queued" (destinataire hors ligne, renvoi automatique),
    /// "delivered", "failed" ou "canceled" (envoi annulé avant distribution).
    /// </summary>
    public string Status { get; set; } = "sending";
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Message transféré depuis une autre discussion (« Transféré »).</summary>
    public bool IsForwarded { get; set; }
    /// <summary>Date de la dernière modification (message modifié, comme WhatsApp).</summary>
    public DateTimeOffset? EditedAt { get; set; }
    /// <summary>Photo, vidéo, audio ou fichier visible une seule fois.</summary>
    public bool IsViewOnce { get; set; }
    /// <summary>Le média en vue unique a déjà été consulté : il ne peut plus être rouvert.</summary>
    public bool ViewOnceOpened { get; set; }
}

/// <summary>Un poste découvert sur le réseau local.</summary>
public sealed record PeerInfo(
    string PeerId,
    string Name,
    string MachineName,
    string Ip,
    int Port,
    DateTimeOffset LastSeen,
    string? AvatarVersion = null)
{
    public bool IsOnline => DateTimeOffset.UtcNow - LastSeen < TimeSpan.FromSeconds(15);
}

public sealed record SendTextRequest(string? Text);

public sealed record RenameRequest(string? Name);

public sealed record IncomingTextRequest(
    string? SenderPeerId,
    string? SenderName,
    string? MachineName,
    int SenderPort,
    string? Text,
    DateTimeOffset? SentAt,
    string? MessageId,
    string? Kind = null,
    bool Forwarded = false,
    int DurationMs = 0,
    string? CallStatus = null);

/// <summary>Transfert d'un ou plusieurs messages vers une autre discussion.</summary>
public sealed record ForwardRequest(List<string>? MessageIds);

/// <summary>Modification d'un message texte déjà envoyé.</summary>
public sealed record EditMessageRequest(string? Text);

/// <summary>Modification reçue d'un autre poste (mon message a été modifié chez lui).</summary>
public sealed record IncomingMessageEditRequest(
    string? SenderPeerId,
    string? MessageId,
    string? Text);

/// <summary>Suppression reçue d'un autre poste (« supprimer pour tout le monde »).</summary>
public sealed record IncomingMessageDeleteRequest(
    string? SenderPeerId,
    string? MessageId);

/// <summary>Sondage envoyé dans une discussion.</summary>
public sealed record PollRequest(string? Question, List<string>? Options);

/// <summary>Vote pour une option d'un sondage.</summary>
public sealed record PollVoteRequest(string? OptionId, string? VoterName);

/// <summary>Vote reçu d'un autre poste.</summary>
public sealed record IncomingPollVoteRequest(
    string? SenderPeerId,
    string? SenderName,
    string? MessageId,
    string? OptionId);

/// <summary>Demande envoyée par mon navigateur à mon propre serveur.</summary>
public sealed record TypingRequest(bool? Typing);

/// <summary>Signal « est en train d'écrire » reçu d'un autre poste.</summary>
public sealed record IncomingTypingRequest(
    string? SenderPeerId,
    string? SenderName,
    string? MachineName,
    int SenderPort,
    bool? Typing);

/// <summary>Entrée de l'historique des appels (onglet « Appels »).</summary>
public sealed class CallRecord
{
    public string Id { get; set; } = "";
    public string PeerId { get; set; } = "";
    /// <summary>"audio" ou "video".</summary>
    public string Kind { get; set; } = "audio";
    /// <summary>"outgoing" ou "incoming".</summary>
    public string Direction { get; set; } = "outgoing";
    /// <summary>"answered", "missed" ou "declined".</summary>
    public string Status { get; set; } = "answered";
    public int DurationMs { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Appel terminé, envoyé par le navigateur pour l'historique.</summary>
public sealed record CallLogRequest(
    string? PeerId,
    string? Kind,
    string? Direction,
    string? Status,
    int DurationMs);

/// <summary>Signal d'appel envoyé par mon navigateur vers un autre poste.</summary>
public sealed record CallSignalRequest(
    string? CallId,
    string? Kind,
    string? Action,
    string? Sdp,
    string? Candidate);

/// <summary>Statut (texte sur fond de couleur, photo ou vidéo) publié par ce poste.</summary>
public sealed class StatusRecord
{
    public string Id { get; set; } = "";
    /// <summary>"text", "image" ou "video".</summary>
    public string Kind { get; set; } = "text";
    public string? Text { get; set; }
    /// <summary>Fond de couleur d'un statut texte (#RRGGBB).</summary>
    public string? Background { get; set; }
    public string? Caption { get; set; }
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public long FileSize { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
    /// <summary>"all", "except" (tout le monde sauf…) ou "only" (ne partager qu'avec…).</summary>
    public string Privacy { get; set; } = "all";
    /// <summary>Identifiants des postes concernés par la confidentialité (JSON).</summary>
    public string? PrivacyList { get; set; }
    /// <summary>Statut mis en pause : il reste visible par son auteur mais n'est plus partagé.</summary>
    public bool Paused { get; set; }
}

/// <summary>Statut texte publié depuis l'interface.</summary>
public sealed record StatusTextRequest(string? Text, string? Background, string? Caption, string? Privacy = null, List<string>? PrivacyList = null);

/// <summary>Confidentialité d'un statut : tout le monde, sauf…, ou seulement…</summary>
public sealed record StatusPrivacyRequest(string? Privacy, List<string>? Peers);

/// <summary>J'aime / je n'aime plus un statut.</summary>
public sealed record StatusLikeRequest(bool? Liked);

/// <summary>Commentaire publié sur un statut.</summary>
public sealed record StatusCommentRequest(string? Text);

/// <summary>J'aime ou commente le statut d'un autre poste.</summary>
public sealed record IncomingStatusLikeRequest(
    string? SenderPeerId,
    string? SenderName,
    string? StatusId,
    bool Liked);

public sealed record IncomingStatusCommentRequest(
    string? SenderPeerId,
    string? SenderName,
    string? StatusId,
    string? Text);

/// <summary>Consultation d'un statut par un poste du réseau (notifiée au propriétaire).</summary>
public sealed record StatusViewRequest(
    string? SenderPeerId,
    string? ViewerPeerId,
    string? ViewerName,
    string? StatusId);

// ------------------------------------------------------------- Traduction

/// <summary>Texte à traduire (message, conversation ou brouillon).</summary>
public sealed record TranslationRequest(string? Text, string? From, string? To);

/// <summary>Langues de l'application (ma langue / langue de traduction).</summary>
public sealed record TranslationSettingsRequest(
    string? SourceLang,
    string? TargetLang,
    string? PythonPath,
    string? ArgosPath);

/// <summary>Installation d'un modèle Argos hors ligne.</summary>
public sealed record TranslationInstallRequest(string? From, string? To);

/// <summary>Blocage d'un contact : discussion et/ou visibilité de mes statuts.</summary>
public sealed record BlockRequest(bool? Chat, bool? Status);

/// <summary>Signal d'appel reçu d'un autre poste (offre, réponse, candidat ICE, fin…).</summary>
public sealed record IncomingSignalRequest(
    string? SenderPeerId,
    string? SenderName,
    string? MachineName,
    int SenderPort,
    string? CallId,
    string? Kind,
    string? Action,
    string? Sdp,
    string? Candidate);
