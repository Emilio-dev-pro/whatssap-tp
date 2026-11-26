using System.Globalization;
using CaramelChat.Models;
using CaramelChat.Services;
using Microsoft.Data.Sqlite;

namespace CaramelChat.Data;

public sealed record ContactRow(
    string PeerId,
    string RemoteName,
    string MachineName,
    string Ip,
    int Port,
    string? Alias,
    DateTimeOffset? LastSeen,
    string? AvatarVersion = null,
    string? AvatarFile = null);

public sealed record ConversationRow(ChatMessage Last, int Unread);

public sealed record StatusViewRow(string PeerId, string? ViewerName, DateTimeOffset ViewedAt);

public sealed record BlockRow(string PeerId, bool Chat, bool Status);

public sealed record StatusLikeRow(string PeerId, string? Name, DateTimeOffset At);

public sealed record StatusCommentRow(string Id, string AuthorPeerId, string? AuthorName, string Text, DateTimeOffset At);

/// <summary>Petit dépôt SQLite (aucun ORM) : schéma créé au démarrage.</summary>
public sealed class AppDatabase
{
    private const string MessageColumns =
        "Id, PeerId, Direction, Kind, Text, FileId, FileName, MimeType, FileSize, DurationMs, Status, IsRead, CreatedAt, IsForwarded, EditedAt, IsViewOnce, ViewOnceOpened";

    private const string StatusColumns =
        "Id, Kind, Text, Background, Caption, FileId, FileName, MimeType, FileSize, DurationMs, CreatedAt, ExpiresAt, Privacy, PrivacyList, Paused";

    private readonly string _connectionString;

    public string DataDirectory { get; }
    public string FilesDirectory { get; }

    public AppDatabase() : this(null)
    {
    }

    /// <summary>Chemin facultatif (tests d'intégration) ; sinon App_Data de l'application.</summary>
    public AppDatabase(string? dataDirectory)
    {
        DataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
            ? ChatPaths.DataDirectory
            : dataDirectory!;
        FilesDirectory = Path.Combine(DataDirectory, "files");
        Directory.CreateDirectory(FilesDirectory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(DataDirectory, "caramelchat.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Contacts (
                PeerId     TEXT PRIMARY KEY,
                RemoteName TEXT NOT NULL,
                MachineName TEXT NOT NULL,
                Ip         TEXT NOT NULL DEFAULT '',
                Port       INTEGER NOT NULL DEFAULT 0,
                Alias      TEXT NULL,
                LastSeen   TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Messages (
                Id         TEXT PRIMARY KEY,
                PeerId     TEXT NOT NULL,
                Direction  TEXT NOT NULL,
                Kind       TEXT NOT NULL,
                Text       TEXT NULL,
                FileId     TEXT NULL,
                FileName   TEXT NULL,
                MimeType   TEXT NULL,
                FileSize   INTEGER NOT NULL DEFAULT 0,
                DurationMs INTEGER NOT NULL DEFAULT 0,
                Status     TEXT NOT NULL,
                IsRead     INTEGER NOT NULL DEFAULT 0,
                CreatedAt  TEXT NOT NULL,
                EditedAt   TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Messages_Peer_Created ON Messages (PeerId, CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_Messages_File ON Messages (FileId);

            CREATE TABLE IF NOT EXISTS Calls (
                Id         TEXT PRIMARY KEY,
                PeerId     TEXT NOT NULL,
                Kind       TEXT NOT NULL,
                Direction  TEXT NOT NULL,
                Status     TEXT NOT NULL,
                DurationMs INTEGER NOT NULL DEFAULT 0,
                StartedAt  TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Calls_Started ON Calls (StartedAt);

            CREATE TABLE IF NOT EXISTS Statuses (
                Id         TEXT PRIMARY KEY,
                Kind       TEXT NOT NULL,
                Text       TEXT NULL,
                Background TEXT NULL,
                Caption    TEXT NULL,
                FileId     TEXT NULL,
                FileName   TEXT NULL,
                MimeType   TEXT NULL,
                FileSize   INTEGER NOT NULL DEFAULT 0,
                DurationMs INTEGER NOT NULL DEFAULT 0,
                CreatedAt  TEXT NOT NULL,
                ExpiresAt  TEXT NOT NULL,
                Privacy    TEXT NOT NULL DEFAULT 'all',
                PrivacyList TEXT NULL,
                Paused     INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_Statuses_Expires ON Statuses (ExpiresAt);

            CREATE TABLE IF NOT EXISTS StatusLikes (
                StatusId    TEXT NOT NULL,
                LikerPeerId TEXT NOT NULL,
                LikerName   TEXT NULL,
                LikedAt     TEXT NOT NULL,
                PRIMARY KEY (StatusId, LikerPeerId)
            );

            CREATE TABLE IF NOT EXISTS StatusComments (
                Id           TEXT PRIMARY KEY,
                StatusId     TEXT NOT NULL,
                AuthorPeerId TEXT NOT NULL,
                AuthorName   TEXT NULL,
                Text         TEXT NOT NULL,
                CreatedAt    TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_StatusComments_Status ON StatusComments (StatusId, CreatedAt);

            CREATE TABLE IF NOT EXISTS StatusViews (
                StatusId     TEXT NOT NULL,
                ViewerPeerId TEXT NOT NULL,
                ViewerName   TEXT NULL,
                ViewedAt     TEXT NOT NULL,
                PRIMARY KEY (StatusId, ViewerPeerId)
            );

            CREATE TABLE IF NOT EXISTS Blocks (
                PeerId      TEXT PRIMARY KEY,
                BlockChat   INTEGER NOT NULL DEFAULT 0,
                BlockStatus INTEGER NOT NULL DEFAULT 0,
                UpdatedAt   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Streaks (
                PeerId    TEXT PRIMARY KEY,
                Count     INTEGER NOT NULL DEFAULT 0,
                LastDate  TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TranslationCache (
                Key            TEXT PRIMARY KEY,
                SourceLang     TEXT NOT NULL,
                TargetLang     TEXT NOT NULL,
                OriginalText   TEXT NOT NULL,
                TranslatedText TEXT NOT NULL,
                Engine         TEXT NOT NULL,
                CreatedAt      TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // Colonnes ajoutées après la première version : migration douce.
        EnsureColumn(connection, "Contacts", "AvatarVersion", "TEXT NULL");
        EnsureColumn(connection, "Contacts", "AvatarFile", "TEXT NULL");
        EnsureColumn(connection, "Messages", "IsForwarded", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Statuses", "Privacy", "TEXT NOT NULL DEFAULT 'all'");
        EnsureColumn(connection, "Statuses", "PrivacyList", "TEXT NULL");
        EnsureColumn(connection, "Statuses", "Paused", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Messages", "EditedAt", "TEXT NULL");
        EnsureColumn(connection, "Messages", "IsViewOnce", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Messages", "ViewOnceOpened", "INTEGER NOT NULL DEFAULT 0");

        // Les envois interrompus par un arrêt de l'application repartent en file d'attente.
        using var reset = connection.CreateCommand();
        reset.CommandText = "UPDATE Messages SET Status = 'queued' WHERE Direction = 'outgoing' AND Status = 'sending'";
        reset.ExecuteNonQuery();

        // Les anciens contacts de démonstration ne font plus partie de l'application.
        using var cleanup = connection.CreateCommand();
        cleanup.CommandText = """
            DELETE FROM Messages WHERE PeerId LIKE 'demo-%';
            DELETE FROM Calls    WHERE PeerId LIKE 'demo-%';
            DELETE FROM Contacts WHERE PeerId LIKE 'demo-%';
            """;
        cleanup.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        var exists = false;
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- Settings

    public string? GetSetting(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- Contacts

    public void UpsertContact(PeerInfo peer)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Contacts (PeerId, RemoteName, MachineName, Ip, Port, LastSeen)
            VALUES ($peerId, $remoteName, $machineName, $ip, $port, $lastSeen)
            ON CONFLICT (PeerId) DO UPDATE SET
                RemoteName  = excluded.RemoteName,
                MachineName = excluded.MachineName,
                Ip          = excluded.Ip,
                Port        = excluded.Port,
                LastSeen    = excluded.LastSeen;
            """;
        command.Parameters.AddWithValue("$peerId", peer.PeerId);
        command.Parameters.AddWithValue("$remoteName", peer.Name);
        command.Parameters.AddWithValue("$machineName", peer.MachineName);
        command.Parameters.AddWithValue("$ip", peer.Ip);
        command.Parameters.AddWithValue("$port", peer.Port);
        command.Parameters.AddWithValue("$lastSeen", peer.LastSeen.ToString("O"));
        command.ExecuteNonQuery();
    }

    public ContactRow? GetContact(string peerId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT PeerId, RemoteName, MachineName, Ip, Port, Alias, LastSeen, AvatarVersion, AvatarFile FROM Contacts WHERE PeerId = $peerId";
        command.Parameters.AddWithValue("$peerId", peerId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadContact(reader) : null;
    }

    public List<ContactRow> GetAllContacts()
    {
        var contacts = new List<ContactRow>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT PeerId, RemoteName, MachineName, Ip, Port, Alias, LastSeen, AvatarVersion, AvatarFile FROM Contacts";
        using var reader = command.ExecuteReader();
        while (reader.Read()) contacts.Add(ReadContact(reader));
        return contacts;
    }

    private static ContactRow ReadContact(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8));

    public void SetAlias(string peerId, string? alias)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Contacts (PeerId, RemoteName, MachineName, Ip, Port, Alias, LastSeen)
            VALUES ($peerId, $peerId, '', '', 0, $alias, $now)
            ON CONFLICT (PeerId) DO UPDATE SET Alias = excluded.Alias;
            """;
        command.Parameters.AddWithValue("$peerId", peerId);
        command.Parameters.AddWithValue("$alias", (object?)alias ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void SetContactAvatar(string peerId, string? version, string? file)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Contacts (PeerId, RemoteName, MachineName, Ip, Port, Alias, LastSeen, AvatarVersion, AvatarFile)
            VALUES ($peerId, $peerId, '', '', 0, NULL, $now, $version, $file)
            ON CONFLICT (PeerId) DO UPDATE SET
                AvatarVersion = excluded.AvatarVersion,
                AvatarFile    = excluded.AvatarFile;
            """;
        command.Parameters.AddWithValue("$peerId", peerId);
        command.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        command.Parameters.AddWithValue("$file", (object?)file ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- Messages

    /// <summary>Insère le message. Retourne false si l'identifiant existe déjà (renvoi idempotent).</summary>
    public bool AddMessage(ChatMessage message)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT OR IGNORE INTO Messages ({MessageColumns})
            VALUES ($id, $peerId, $direction, $kind, $text, $fileId, $fileName, $mimeType,
                    $fileSize, $durationMs, $status, $isRead, $createdAt, $isForwarded, NULL,
                    $isViewOnce, $viewOnceOpened);
            """;
        command.Parameters.AddWithValue("$id", message.Id);
        command.Parameters.AddWithValue("$peerId", message.PeerId);
        command.Parameters.AddWithValue("$direction", message.Direction);
        command.Parameters.AddWithValue("$kind", message.Kind);
        command.Parameters.AddWithValue("$text", (object?)message.Text ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileId", (object?)message.FileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileName", (object?)message.FileName ?? DBNull.Value);
        command.Parameters.AddWithValue("$mimeType", (object?)message.MimeType ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileSize", message.FileSize);
        command.Parameters.AddWithValue("$durationMs", message.DurationMs);
        command.Parameters.AddWithValue("$status", message.Status);
        command.Parameters.AddWithValue("$isRead", message.IsRead ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$isForwarded", message.IsForwarded ? 1 : 0);
        command.Parameters.AddWithValue("$isViewOnce", message.IsViewOnce ? 1 : 0);
        command.Parameters.AddWithValue("$viewOnceOpened", message.ViewOnceOpened ? 1 : 0);
        return command.ExecuteNonQuery() > 0;
    }

    public ChatMessage? GetMessageById(string messageId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MessageColumns} FROM Messages WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", messageId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMessage(reader) : null;
    }

    /// <summary>Messages sortants à (re)livrer : destinataire hors ligne, envoi interrompu…</summary>
    public List<ChatMessage> GetPendingMessages(int limit = 40)
    {
        var messages = new List<ChatMessage>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {MessageColumns} FROM Messages
            WHERE Direction = 'outgoing' AND Status IN ('queued', 'sending')
            ORDER BY CreatedAt ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read()) messages.Add(ReadMessage(reader));
        return messages;
    }

    public void UpdateMessageStatus(string messageId, string status)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Messages SET Status = $status WHERE Id = $id";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", messageId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Passe le message à l'état donné seulement s'il est encore en attente :
    /// un message annulé entre-temps le reste.
    /// </summary>
    public bool UpdateMessageStatusIfPending(string messageId, string status)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Messages SET Status = $status
            WHERE Id = $id AND Status IN ('queued', 'sending', 'failed');
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", messageId);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Annule un envoi pas encore distribué. Retourne false si le message est déjà
    /// parti chez le destinataire (ou n'existe plus).
    /// </summary>
    public bool TryCancelMessage(string messageId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Messages SET Status = 'canceled'
            WHERE Id = $id AND Direction = 'outgoing' AND Status IN ('queued', 'sending', 'failed');
            """;
        command.Parameters.AddWithValue("$id", messageId);
        return command.ExecuteNonQuery() > 0;
    }

    // ------------------------------------------------------------------ Statuts

    public void AddStatus(StatusRecord status)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Statuses (Id, Kind, Text, Background, Caption, FileId, FileName, MimeType, FileSize, DurationMs, CreatedAt, ExpiresAt, Privacy, PrivacyList, Paused)
            VALUES ($id, $kind, $text, $background, $caption, $fileId, $fileName, $mimeType, $fileSize, $durationMs, $createdAt, $expiresAt, $privacy, $privacyList, $paused);
            """;
        command.Parameters.AddWithValue("$id", status.Id);
        command.Parameters.AddWithValue("$kind", status.Kind);
        command.Parameters.AddWithValue("$text", (object?)status.Text ?? DBNull.Value);
        command.Parameters.AddWithValue("$background", (object?)status.Background ?? DBNull.Value);
        command.Parameters.AddWithValue("$caption", (object?)status.Caption ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileId", (object?)status.FileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileName", (object?)status.FileName ?? DBNull.Value);
        command.Parameters.AddWithValue("$mimeType", (object?)status.MimeType ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileSize", status.FileSize);
        command.Parameters.AddWithValue("$durationMs", status.DurationMs);
        command.Parameters.AddWithValue("$createdAt", status.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$expiresAt", status.ExpiresAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$privacy", string.IsNullOrWhiteSpace(status.Privacy) ? "all" : status.Privacy);
        command.Parameters.AddWithValue("$privacyList", (object?)status.PrivacyList ?? DBNull.Value);
        command.Parameters.AddWithValue("$paused", status.Paused ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public List<StatusRecord> GetStatuses()
    {
        var statuses = new List<StatusRecord>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Kind, Text, Background, Caption, FileId, FileName, MimeType, FileSize, DurationMs, CreatedAt, ExpiresAt, Privacy, PrivacyList, Paused
            FROM Statuses WHERE ExpiresAt > $now ORDER BY CreatedAt ASC;
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        using var reader = command.ExecuteReader();
        while (reader.Read()) statuses.Add(ReadStatus(reader));
        return statuses;
    }

    public StatusRecord? GetStatus(string statusId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Kind, Text, Background, Caption, FileId, FileName, MimeType, FileSize, DurationMs, CreatedAt, ExpiresAt, Privacy, PrivacyList, Paused
            FROM Statuses WHERE Id = $id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", statusId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadStatus(reader) : null;
    }

    public void DeleteStatus(string statusId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StatusViews WHERE StatusId = $id; DELETE FROM Statuses WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", statusId);
        command.ExecuteNonQuery();
    }

    public List<StatusViewRow> GetStatusViews(string statusId)
    {
        var views = new List<StatusViewRow>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ViewerPeerId, ViewerName, ViewedAt FROM StatusViews WHERE StatusId = $id ORDER BY ViewedAt ASC";
        command.Parameters.AddWithValue("$id", statusId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            views.Add(new StatusViewRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseDate(reader.GetString(2))));
        }
        return views;
    }

    public bool HasStatusView(string statusId, string viewerPeerId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM StatusViews WHERE StatusId = $id AND ViewerPeerId = $viewer LIMIT 1";
        command.Parameters.AddWithValue("$id", statusId);
        command.Parameters.AddWithValue("$viewer", viewerPeerId);
        return command.ExecuteScalar() is not null;
    }

    public void MarkStatusViewed(string statusId, string viewerPeerId, string? viewerName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StatusViews (StatusId, ViewerPeerId, ViewerName, ViewedAt)
            VALUES ($id, $viewer, $name, $at)
            ON CONFLICT (StatusId, ViewerPeerId) DO UPDATE SET
                ViewerName = excluded.ViewerName,
                ViewedAt   = excluded.ViewedAt;
            """;
        command.Parameters.AddWithValue("$id", statusId);
        command.Parameters.AddWithValue("$viewer", viewerPeerId);
        command.Parameters.AddWithValue("$name", (object?)viewerName ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>Supprime les statuts expirés et retourne les fichiers à effacer.</summary>
    public List<string> DeleteExpiredStatuses()
    {
        var files = new List<string>();
        using var connection = OpenConnection();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT FileId FROM Statuses WHERE ExpiresAt <= $now AND FileId IS NOT NULL";
            list.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0)) files.Add(reader.GetString(0));
            }
        }

        using var delete = connection.CreateCommand();
        delete.CommandText = """
            DELETE FROM StatusViews WHERE StatusId IN (SELECT Id FROM Statuses WHERE ExpiresAt <= $now);
            DELETE FROM Statuses WHERE ExpiresAt <= $now;
            """;
        delete.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        delete.ExecuteNonQuery();
        return files;
    }

    private static StatusRecord ReadStatus(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Kind = reader.GetString(1),
        Text = reader.IsDBNull(2) ? null : reader.GetString(2),
        Background = reader.IsDBNull(3) ? null : reader.GetString(3),
        Caption = reader.IsDBNull(4) ? null : reader.GetString(4),
        FileId = reader.IsDBNull(5) ? null : reader.GetString(5),
        FileName = reader.IsDBNull(6) ? null : reader.GetString(6),
        MimeType = reader.IsDBNull(7) ? null : reader.GetString(7),
        FileSize = reader.GetInt64(8),
        DurationMs = reader.GetInt32(9),
        CreatedAt = ParseDate(reader.GetString(10)),
        ExpiresAt = ParseDate(reader.GetString(11)),
        Privacy = reader.IsDBNull(12) ? "all" : reader.GetString(12),
        PrivacyList = reader.IsDBNull(13) ? null : reader.GetString(13),
        Paused = !reader.IsDBNull(14) && reader.GetInt32(14) == 1
    };

    // ------------------------------------------------------------------ Blocage

    public BlockRow GetBlock(string peerId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT BlockChat, BlockStatus FROM Blocks WHERE PeerId = $peerId";
        command.Parameters.AddWithValue("$peerId", peerId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new BlockRow(peerId, false, false);
        return new BlockRow(peerId, reader.GetInt32(0) == 1, reader.GetInt32(1) == 1);
    }

    public List<BlockRow> GetBlocks()
    {
        var blocks = new List<BlockRow>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PeerId, BlockChat, BlockStatus FROM Blocks ORDER BY UpdatedAt DESC";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetInt32(1) == 0 && reader.GetInt32(2) == 0) continue;
            blocks.Add(new BlockRow(reader.GetString(0), reader.GetInt32(1) == 1, reader.GetInt32(2) == 1));
        }
        return blocks;
    }

    public BlockRow SetBlock(string peerId, bool? chat, bool? status)
    {
        var current = GetBlock(peerId);
        var blockChat = chat ?? current.Chat;
        var blockStatus = status ?? current.Status;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Blocks (PeerId, BlockChat, BlockStatus, UpdatedAt)
            VALUES ($peerId, $chat, $status, $now)
            ON CONFLICT (PeerId) DO UPDATE SET
                BlockChat   = excluded.BlockChat,
                BlockStatus = excluded.BlockStatus,
                UpdatedAt   = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$peerId", peerId);
        command.Parameters.AddWithValue("$chat", blockChat ? 1 : 0);
        command.Parameters.AddWithValue("$status", blockStatus ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return new BlockRow(peerId, blockChat, blockStatus);
    }

    public bool IsChatBlocked(string peerId) => GetBlock(peerId).Chat;

    public bool IsStatusHidden(string peerId) => GetBlock(peerId).Status;

    // ------------------------------------------------------------------- Appels

    public void AddCall(CallRecord call)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Calls (Id, PeerId, Kind, Direction, Status, DurationMs, StartedAt)
            VALUES ($id, $peerId, $kind, $direction, $status, $durationMs, $startedAt);
            """;
        command.Parameters.AddWithValue("$id", call.Id);
        command.Parameters.AddWithValue("$peerId", call.PeerId);
        command.Parameters.AddWithValue("$kind", call.Kind);
        command.Parameters.AddWithValue("$direction", call.Direction);
        command.Parameters.AddWithValue("$status", call.Status);
        command.Parameters.AddWithValue("$durationMs", call.DurationMs);
        command.Parameters.AddWithValue("$startedAt", call.StartedAt.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    public List<CallRecord> GetCalls(int limit = 80)
    {
        var calls = new List<CallRecord>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PeerId, Kind, Direction, Status, DurationMs, StartedAt FROM Calls
            ORDER BY StartedAt DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            calls.Add(new CallRecord
            {
                Id = reader.GetString(0),
                PeerId = reader.GetString(1),
                Kind = reader.GetString(2),
                Direction = reader.GetString(3),
                Status = reader.GetString(4),
                DurationMs = reader.GetInt32(5),
                StartedAt = ParseDate(reader.GetString(6))
            });
        }
        return calls;
    }

    public void MarkConversationRead(string peerId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Messages SET IsRead = 1 WHERE PeerId = $peerId AND Direction = 'incoming' AND IsRead = 0";
        command.Parameters.AddWithValue("$peerId", peerId);
        command.ExecuteNonQuery();
    }

    public List<ChatMessage> GetMessages(string peerId, int limit = 500)
    {
        var messages = new List<ChatMessage>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {MessageColumns} FROM (
                SELECT {MessageColumns} FROM Messages
                WHERE PeerId = $peerId
                ORDER BY CreatedAt DESC
                LIMIT $limit
            ) ORDER BY CreatedAt ASC;
            """;
        command.Parameters.AddWithValue("$peerId", peerId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read()) messages.Add(ReadMessage(reader));
        return messages;
    }

    /// <summary>Recherche dans une discussion (texte des messages et nom des fichiers).</summary>
    public List<ChatMessage> SearchMessages(string peerId, string query, int limit = 300)
    {
        var messages = new List<ChatMessage>();
        if (string.IsNullOrWhiteSpace(query)) return messages;

        var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {MessageColumns} FROM Messages
            WHERE PeerId = $peerId
              AND (Text LIKE $query ESCAPE '\' OR FileName LIKE $query ESCAPE '\')
            ORDER BY CreatedAt ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$peerId", peerId);
        command.Parameters.AddWithValue("$query", $"%{escaped}%");
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read()) messages.Add(ReadMessage(reader));
        return messages;
    }

    public List<ConversationRow> GetConversations()
    {
        var conversations = new List<ConversationRow>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.Id, m.PeerId, m.Direction, m.Kind, m.Text, m.FileId, m.FileName, m.MimeType,
                   m.FileSize, m.DurationMs, m.Status, m.IsRead, m.CreatedAt, m.IsForwarded, m.EditedAt,
                   m.IsViewOnce, m.ViewOnceOpened,
                   (SELECT COUNT(*) FROM Messages u
                     WHERE u.PeerId = m.PeerId AND u.Direction = 'incoming' AND u.IsRead = 0)
            FROM Messages m
            WHERE m.CreatedAt = (SELECT MAX(x.CreatedAt) FROM Messages x WHERE x.PeerId = m.PeerId)
            GROUP BY m.PeerId
            ORDER BY m.CreatedAt DESC;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            conversations.Add(new ConversationRow(ReadMessage(reader), reader.GetInt32(17)));
        }
        return conversations;
    }

    // --------------------------------------------------------------- Flammes

    /// <summary>
    /// Une flamme par jour où la conversation est active. Le compteur repart à 1
    /// après une journée sans message, et n'avance qu'une fois par jour.
    /// </summary>
    public int TouchStreak(string peerId, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(peerId)) return 0;

        var today = at.ToLocalTime().Date;
        var todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var yesterdayKey = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var connection = OpenConnection();

        var count = 0;
        string? last = null;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT Count, LastDate FROM Streaks WHERE PeerId = $peerId";
            read.Parameters.AddWithValue("$peerId", peerId);
            using var reader = read.ExecuteReader();
            if (reader.Read())
            {
                count = reader.GetInt32(0);
                last = reader.GetString(1);
            }
        }

        if (last == todayKey) return count;

        count = last == yesterdayKey ? count + 1 : 1;

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Streaks (PeerId, Count, LastDate) VALUES ($peerId, $count, $last)
            ON CONFLICT (PeerId) DO UPDATE SET Count = excluded.Count, LastDate = excluded.LastDate;
            """;
        command.Parameters.AddWithValue("$peerId", peerId);
        command.Parameters.AddWithValue("$count", count);
        command.Parameters.AddWithValue("$last", todayKey);
        command.ExecuteNonQuery();
        return count;
    }

    /// <summary>Flammes actives par contact (dernier message aujourd'hui ou hier).</summary>
    public Dictionary<string, int> GetActiveStreaks()
    {
        var streaks = new Dictionary<string, int>(StringComparer.Ordinal);
        var today = DateTimeOffset.Now.Date;
        var oldest = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PeerId, Count FROM Streaks WHERE LastDate >= $oldest";
        command.Parameters.AddWithValue("$oldest", oldest);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            streaks[reader.GetString(0)] = reader.GetInt32(1);
        }
        return streaks;
    }

    public ChatMessage? GetMessageByFileId(string fileId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MessageColumns} FROM Messages WHERE FileId = $fileId LIMIT 1";
        command.Parameters.AddWithValue("$fileId", fileId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMessage(reader) : null;
    }

    private static ChatMessage ReadMessage(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        PeerId = reader.GetString(1),
        Direction = reader.GetString(2),
        Kind = reader.GetString(3),
        Text = reader.IsDBNull(4) ? null : reader.GetString(4),
        FileId = reader.IsDBNull(5) ? null : reader.GetString(5),
        FileName = reader.IsDBNull(6) ? null : reader.GetString(6),
        MimeType = reader.IsDBNull(7) ? null : reader.GetString(7),
        FileSize = reader.GetInt64(8),
        DurationMs = reader.GetInt32(9),
        Status = reader.GetString(10),
        IsRead = reader.GetInt32(11) == 1,
        CreatedAt = ParseDate(reader.GetString(12)),
        IsForwarded = reader.GetInt32(13) == 1,
        EditedAt = reader.IsDBNull(14) ? null : ParseDate(reader.GetString(14)),
        IsViewOnce = reader.GetInt32(15) == 1,
        ViewOnceOpened = reader.GetInt32(16) == 1
    };

    /// <summary>Marque un média en vue unique comme consulté. Il ne pourra plus être rouvert.</summary>
    public bool MarkViewOnceOpened(string messageId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Messages SET ViewOnceOpened = 1
            WHERE Id = $id AND IsViewOnce = 1 AND ViewOnceOpened = 0;
            """;
        command.Parameters.AddWithValue("$id", messageId);
        return command.ExecuteNonQuery() > 0;
    }

    // ------------------------------------------------------- Suppression locale

    public void UpdateMessageText(string messageId, string? text, DateTimeOffset? editedAt = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = editedAt is null
            ? "UPDATE Messages SET Text = $text WHERE Id = $id"
            : "UPDATE Messages SET Text = $text, EditedAt = $editedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$text", (object?)text ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", messageId);
        if (editedAt is not null)
        {
            command.Parameters.AddWithValue("$editedAt", editedAt.Value.ToUniversalTime().ToString("O"));
        }
        command.ExecuteNonQuery();
    }

    /// <summary>Supprime un message. Retourne le fichier à effacer sur le disque, s'il y en a un.</summary>
    public string? DeleteMessage(string messageId)
    {
        using var connection = OpenConnection();
        string? fileId = null;
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT FileId FROM Messages WHERE Id = $id LIMIT 1";
            select.Parameters.AddWithValue("$id", messageId);
            fileId = select.ExecuteScalar() as string;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Messages WHERE Id = $id";
        command.Parameters.AddWithValue("$id", messageId);
        command.ExecuteNonQuery();
        return fileId;
    }

    /// <summary>Efface les messages de la discussion (les fichiers restent sur ce poste).</summary>
    public void ClearConversation(string peerId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Messages WHERE PeerId = $peerId";
        command.Parameters.AddWithValue("$peerId", peerId);
        command.ExecuteNonQuery();
    }

    /// <summary>Supprime la discussion : messages, appels et contact. Retourne les fichiers à effacer.</summary>
    public List<string> DeleteConversation(string peerId)
    {
        var files = new List<string>();
        using var connection = OpenConnection();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT FileId FROM Messages WHERE PeerId = $peerId AND FileId IS NOT NULL";
            list.Parameters.AddWithValue("$peerId", peerId);
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0)) files.Add(reader.GetString(0));
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Messages WHERE PeerId = $peerId;
            DELETE FROM Calls    WHERE PeerId = $peerId;
            DELETE FROM Blocks   WHERE PeerId = $peerId;
            DELETE FROM Contacts WHERE PeerId = $peerId;
            """;
        command.Parameters.AddWithValue("$peerId", peerId);
        command.ExecuteNonQuery();
        return files;
    }

    public bool DeleteCall(string callId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Calls WHERE Id = $id";
        command.Parameters.AddWithValue("$id", callId);
        return command.ExecuteNonQuery() > 0;
    }

    public void DeleteAllCalls()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Calls";
        command.ExecuteNonQuery();
    }

    // ------------------------------------------------ J'aime / commentaires

    public List<StatusLikeRow> GetStatusLikes(string statusId)
    {
        var likes = new List<StatusLikeRow>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LikerPeerId, LikerName, LikedAt FROM StatusLikes WHERE StatusId = $id ORDER BY LikedAt ASC";
        command.Parameters.AddWithValue("$id", statusId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            likes.Add(new StatusLikeRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseDate(reader.GetString(2))));
        }
        return likes;
    }

    public bool HasStatusLike(string statusId, string peerId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM StatusLikes WHERE StatusId = $id AND LikerPeerId = $peer LIMIT 1";
        command.Parameters.AddWithValue("$id", statusId);
        command.Parameters.AddWithValue("$peer", peerId);
        return command.ExecuteScalar() is not null;
    }

    public void SetStatusLike(string statusId, string peerId, string? name, bool liked)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = liked
            ? """
              INSERT INTO StatusLikes (StatusId, LikerPeerId, LikerName, LikedAt)
              VALUES ($id, $peer, $name, $at)
              ON CONFLICT (StatusId, LikerPeerId) DO UPDATE SET
                  LikerName = excluded.LikerName;
              """
            : "DELETE FROM StatusLikes WHERE StatusId = $id AND LikerPeerId = $peer";
        command.Parameters.AddWithValue("$id", statusId);
        command.Parameters.AddWithValue("$peer", peerId);
        if (liked)
        {
            command.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        }
        command.ExecuteNonQuery();
    }

    public List<StatusCommentRow> GetStatusComments(string statusId)
    {
        var comments = new List<StatusCommentRow>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, AuthorPeerId, AuthorName, Text, CreatedAt FROM StatusComments WHERE StatusId = $id ORDER BY CreatedAt ASC";
        command.Parameters.AddWithValue("$id", statusId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            comments.Add(new StatusCommentRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                ParseDate(reader.GetString(4))));
        }
        return comments;
    }

    public void AddStatusComment(string statusId, string authorPeerId, string? authorName, string text)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StatusComments (Id, StatusId, AuthorPeerId, AuthorName, Text, CreatedAt)
            VALUES ($id, $statusId, $authorId, $authorName, $text, $at);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$statusId", statusId);
        command.Parameters.AddWithValue("$authorId", authorPeerId);
        command.Parameters.AddWithValue("$authorName", (object?)authorName ?? DBNull.Value);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    // --------------------------------------------------------- Cache de traduction

    public (string Original, string Translated)? GetCachedTranslation(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT OriginalText, TranslatedText FROM TranslationCache WHERE Key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    public void SetCachedTranslation(
        string key,
        string sourceLang,
        string targetLang,
        string original,
        string translated,
        string engine)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TranslationCache (Key, SourceLang, TargetLang, OriginalText, TranslatedText, Engine, CreatedAt)
            VALUES ($key, $source, $target, $original, $translated, $engine, $createdAt)
            ON CONFLICT (Key) DO UPDATE SET
                TranslatedText = excluded.TranslatedText,
                Engine = excluded.Engine,
                CreatedAt = excluded.CreatedAt;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$source", sourceLang);
        command.Parameters.AddWithValue("$target", targetLang);
        command.Parameters.AddWithValue("$original", original);
        command.Parameters.AddWithValue("$translated", translated);
        command.Parameters.AddWithValue("$engine", engine);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public (int Count, long Characters) GetTranslationCacheStats()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*), COALESCE(SUM(LENGTH(OriginalText)), 0) FROM TranslationCache";
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt32(0), reader.GetInt64(1)) : (0, 0);
    }

    public void ClearTranslationCache()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TranslationCache";
        command.ExecuteNonQuery();
    }

    public void SetStatusPaused(string statusId, bool paused)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Statuses SET Paused = $paused WHERE Id = $id";
        command.Parameters.AddWithValue("$paused", paused ? 1 : 0);
        command.Parameters.AddWithValue("$id", statusId);
        command.ExecuteNonQuery();
    }

    public void SetStatusPrivacy(string statusId, string privacy, string? privacyList)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Statuses SET Privacy = $privacy, PrivacyList = $list WHERE Id = $id";
        command.Parameters.AddWithValue("$privacy", privacy);
        command.Parameters.AddWithValue("$list", (object?)privacyList ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", statusId);
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
}
