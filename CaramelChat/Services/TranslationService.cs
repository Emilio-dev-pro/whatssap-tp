using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CaramelChat.Data;
using CaramelChat.Models;

namespace CaramelChat.Services;

/// <summary>Erreur lisible remontée à l'interface (moteur absent, langue non installée…).</summary>
public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
}

public sealed record TranslationSettings(
    string SourceLang,
    string TargetLang,
    string PythonPath,
    string ArgosPath);

public sealed record TranslationLanguage(string Code, string Name);

/// <summary>
/// Traduction hors ligne avec Argos Translate.
///
/// Le paquet Python est piloté par un petit pont local (<c>tools/argos_bridge.py</c>)
/// qui communique en JSON ligne par ligne sur les flux standard du processus : aucun
/// port réseau n'est ouvert, le service n'est donc pas exposé sur Internet. Argos est
/// démarré à la première traduction, redémarré s'il s'arrête, et tué à la fermeture.
/// Les traductions sont mises en cache dans SQLite.
/// </summary>
public sealed class TranslationService : IDisposable
{
    public const string SourceKey = "translation.sourceLang";
    public const string TargetKey = "translation.targetLang";
    public const string PythonKey = "translation.python";
    public const string ArgosKey = "translation.argosPath";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Langues proposées dans l'interface (Argos utilise les codes ISO 639-1).</summary>
    public static readonly TranslationLanguage[] Languages =
    {
        new("fr", "Français"), new("en", "Anglais"), new("es", "Espagnol"),
        new("de", "Allemand"), new("it", "Italien"), new("pt", "Portugais"),
        new("nl", "Néerlandais"), new("ar", "Arabe"), new("zh", "Chinois"),
        new("ja", "Japonais"), new("ru", "Russe"), new("hi", "Hindi"),
        new("tr", "Turc"), new("pl", "Polonais"), new("ko", "Coréen"),
        new("sv", "Suédois"), new("id", "Indonésien"), new("uk", "Ukrainien"),
        new("vi", "Vietnamien"), new("th", "Thaï"), new("he", "Hébreu"),
        new("el", "Grec"), new("ro", "Roumain"), new("cs", "Tchèque"),
        new("da", "Danois"), new("fi", "Finnois"), new("no", "Norvégien"),
        new("hu", "Hongrois"), new("bg", "Bulgare"), new("fa", "Persan")
    };

    private static readonly Dictionary<string, string> LanguageNames =
        Languages.ToDictionary(language => language.Code, language => language.Name, StringComparer.Ordinal);

    private readonly AppDatabase _database;
    private readonly ILogger<TranslationService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;
    private int _requestId;
    private bool _engineAvailable;
    private string? _engineError;
    private string? _engineVersion;

    private InstallJob? _job;
    private readonly object _jobLock = new();

    public TranslationService(AppDatabase database, ILogger<TranslationService> logger)
    {
        _database = database;
        _logger = logger;
    }

    // ----------------------------------------------------------- Réglages

    public TranslationSettings GetSettings()
    {
        var source = NormalizeLanguage(_database.GetSetting(SourceKey)) ?? "fr";
        var target = NormalizeLanguage(_database.GetSetting(TargetKey)) ?? "en";

        // Ne jamais exposer deux fois la même langue : les anciennes versions
        // pouvaient enregistrer une paire identique et bloquaient alors la
        // traduction avec « les langues source et cible doivent être différentes ».
        if (source == target) target = source == "en" ? "fr" : "en";

        return new TranslationSettings(
            source,
            target,
            _database.GetSetting(PythonKey) ?? string.Empty,
            _database.GetSetting(ArgosKey) ?? string.Empty);
    }

    public object GetSettingsDto()
    {
        var settings = GetSettings();
        return new
        {
            sourceLang = settings.SourceLang,
            sourceName = LanguageName(settings.SourceLang),
            targetLang = settings.TargetLang,
            targetName = LanguageName(settings.TargetLang),
            pythonPath = settings.PythonPath,
            argosPath = settings.ArgosPath,
            languages = Languages
        };
    }

    public object SaveSettings(TranslationSettingsRequest request)
    {
        var current = GetSettings();
        var source = NormalizeLanguage(request.SourceLang) ?? current.SourceLang;
        var target = NormalizeLanguage(request.TargetLang) ?? current.TargetLang;

        // Choisir une langue déjà utilisée par l'autre champ ne doit jamais bloquer :
        // les deux langues sont simplement échangées, comme dans WhatsApp.
        if (source == target)
        {
            if (request.SourceLang is not null && request.TargetLang is null)
            {
                target = current.SourceLang;
            }
            else if (request.TargetLang is not null && request.SourceLang is null)
            {
                source = current.TargetLang;
            }
            else
            {
                // Les deux champs valent la même langue : on garde deux langues
                // distinctes pour que la traduction reste possible.
                target = source == "en" ? "fr" : "en";
            }
        }

        _database.SetSetting(SourceKey, source);
        _database.SetSetting(TargetKey, target);

        var pathsChanged = false;
        if (request.PythonPath is not null)
        {
            pathsChanged |= (current.PythonPath ?? string.Empty) != request.PythonPath.Trim();
            _database.SetSetting(PythonKey, request.PythonPath.Trim());
        }

        if (request.ArgosPath is not null)
        {
            pathsChanged |= (current.ArgosPath ?? string.Empty) != request.ArgosPath.Trim();
            _database.SetSetting(ArgosKey, request.ArgosPath.Trim());
        }

        // Un changement d'emplacement doit être pris en compte au prochain
        // appel : on arrête le processus pour qu'il redémarre avec les
        // nouveaux chemins.
        if (pathsChanged) StopProcess();

        return GetSettingsDto();
    }

    public static string LanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "—";
        return LanguageNames.TryGetValue(code, out var name) ? name : code.ToUpperInvariant();
    }

    private static string? NormalizeLanguage(string? code)
    {
        var value = (code ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length is < 2 or > 8) return null;
        return value.All(character => char.IsLetterOrDigit(character) || character == '-') ? value : null;
    }

    // ------------------------------------------------------------- Moteur

    public async Task<object> GetStatusAsync()
    {
        var settings = GetSettings();
        var paths = new
        {
            python = ResolvePython().File,
            argosPath = ResolveArgosPath() ?? string.Empty,
            script = ResolveScript()
        };

        try
        {
            var status = await SendAsync(new { op = "status" }, TimeSpan.FromSeconds(20));
            var available = status.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            var error = status.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            var version = status.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
            _engineVersion = version;
            _engineAvailable = available;
            _engineError = error;

            return new
            {
                available,
                version,
                error,
                engine = "Argos Translate",
                settings,
                paths,
                installedPairs = ReadPairs(status),
                installedLanguages = ReadLanguages(status)
            };
        }
        catch (Exception exception)
        {
            _engineAvailable = false;
            _engineError = exception.Message;
            return new
            {
                available = false,
                version = (string?)null,
                error = exception.Message,
                engine = "Argos Translate",
                settings,
                paths,
                installedPairs = Array.Empty<object>(),
                installedLanguages = Array.Empty<object>()
            };
        }
    }

    public async Task<object> GetLanguagesAsync()
    {
        var status = await GetStatusAsync();
        return new
        {
            languages = Languages,
            settings = GetSettingsDto(),
            status
        };
    }

    public async Task<object> GetModelsAsync()
    {
        try
        {
            var response = await SendAsync(new { op = "list" }, TimeSpan.FromMinutes(2));
            var ok = response.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                return new
                {
                    ok = false,
                    error = ReadString(response, "error") ?? "Liste des modèles indisponible.",
                    installed = ReadPairs(response),
                    available = Array.Empty<object>()
                };
            }

            var available = new List<object>();
            if (response.TryGetProperty("available", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var from = ReadString(item, "from");
                    var to = ReadString(item, "to");
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;
                    available.Add(new
                    {
                        from,
                        to,
                        fromName = LanguageName(from),
                        toName = LanguageName(to),
                        version = ReadString(item, "version"),
                        sizeBytes = item.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes)
                            ? bytes
                            : (long?)null
                    });
                }
            }

            return new
            {
                ok = true,
                installed = ReadPairs(response),
                available,
                engine = new { available = true, version = _engineVersion }
            };
        }
        catch (Exception exception)
        {
            return new
            {
                ok = false,
                error = exception.Message,
                installed = Array.Empty<object>(),
                available = Array.Empty<object>(),
                engine = new { available = false, version = (string?)null }
            };
        }
    }

    public async Task<object> TranslateAsync(string? rawText, string? from, string? to)
    {
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0) throw new TranslationException("Le texte à traduire est vide.");
        if (text.Length > 8000) throw new TranslationException("Texte trop long : 8000 caractères maximum.");

        var settings = GetSettings();
        var source = NormalizeLanguage(from) ?? settings.SourceLang;
        var target = NormalizeLanguage(to) ?? settings.TargetLang;
        if (source == target)
        {
            throw new TranslationException("Les langues source et cible doivent être différentes.");
        }

        var key = CacheKey(source, target, text);
        var cached = _database.GetCachedTranslation(key);
        if (cached is not null)
        {
            return new
            {
                ok = true,
                text = cached.Value.Translated,
                from = source,
                to = target,
                fromName = LanguageName(source),
                toName = LanguageName(target),
                cached = true,
                engine = "Argos Translate"
            };
        }

        var response = await SendAsync(new
        {
            op = "translate",
            text,
            from = source,
            to = target
        }, TimeSpan.FromMinutes(1));

        var ok = response.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            throw new TranslationException(
                ReadString(response, "error") ??
                $"Le modèle {LanguageName(source)} → {LanguageName(target)} n'est pas installé.");
        }

        var translated = ReadString(response, "text");
        if (string.IsNullOrWhiteSpace(translated))
        {
            throw new TranslationException("Le moteur n'a renvoyé aucune traduction.");
        }

        _database.SetCachedTranslation(key, source, target, text, translated, "argos");
        return new
        {
            ok = true,
            text = translated,
            from = source,
            to = target,
            fromName = LanguageName(source),
            toName = LanguageName(target),
            cached = false,
            engine = "Argos Translate"
        };
    }

    public object GetCacheStats()
    {
        var stats = _database.GetTranslationCacheStats();
        return new { count = stats.Count, characters = stats.Characters };
    }

    public void ClearCache() => _database.ClearTranslationCache();

    // -------------------------------------------------- Téléchargement des modèles

    public object StartInstall(string? from, string? to)
    {
        var source = NormalizeLanguage(from);
        var target = NormalizeLanguage(to);
        if (source is null || target is null)
        {
            throw new TranslationException("Choisissez une paire de langues.");
        }

        if (source == target)
        {
            throw new TranslationException("Les langues source et cible doivent être différentes.");
        }

        lock (_jobLock)
        {
            if (_job is { State: "running" })
            {
                throw new TranslationException("Un téléchargement est déjà en cours.");
            }

            var job = new InstallJob
            {
                Id = Guid.NewGuid().ToString("N"),
                From = source,
                To = target,
                State = "running",
                StartedAt = DateTimeOffset.UtcNow
            };
            _job = job;
            _ = Task.Run(() => RunInstallAsync(job));
            return Snapshot(job);
        }
    }

    public object? GetInstallJob(string? jobId)
    {
        lock (_jobLock)
        {
            if (_job is null) return null;
            if (!string.IsNullOrWhiteSpace(jobId) && _job.Id != jobId) return null;
            return Snapshot(_job);
        }
    }

    public bool CancelInstall()
    {
        lock (_jobLock)
        {
            if (_job is not { State: "running" }) return false;
            _job.State = "canceled";
            _job.Error = "Téléchargement annulé.";
        }

        StopProcess();
        return true;
    }

    public async Task<(bool Ok, string? Error)> RemoveModelAsync(string? from, string? to)
    {
        var source = NormalizeLanguage(from);
        var target = NormalizeLanguage(to);
        if (source is null || target is null) return (false, "Choisissez une paire de langues.");

        try
        {
            var response = await SendAsync(new { op = "remove", from = source, to = target }, TimeSpan.FromMinutes(1));
            var ok = response.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            return ok
                ? (true, null)
                : (false, ReadString(response, "error") ?? "Suppression impossible.");
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    private async Task RunInstallAsync(InstallJob job)
    {
        try
        {
            var response = await SendAsync(new
            {
                op = "install",
                from = job.From,
                to = job.To,
                jobId = job.Id
            }, TimeSpan.FromMinutes(30));

            var ok = response.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            lock (_jobLock)
            {
                if (job.State == "canceled") return;
                job.State = ok ? "done" : "failed";
                job.Percent = ok ? 100 : job.Percent;
                job.Error = ok ? null : ReadString(response, "error") ?? "Installation impossible.";
            }
        }
        catch (Exception exception)
        {
            lock (_jobLock)
            {
                if (job.State == "canceled") return;
                job.State = "failed";
                job.Error = exception.Message;
            }
        }
    }

    private void UpdateJobProgress(string? jobId, double percent)
    {
        lock (_jobLock)
        {
            if (_job is null || _job.State != "running") return;
            if (!string.IsNullOrWhiteSpace(jobId) && _job.Id != jobId) return;
            _job.Percent = Math.Clamp(percent, 0, 99.9);
        }
    }

    private static object Snapshot(InstallJob job) => new
    {
        id = job.Id,
        from = job.From,
        to = job.To,
        fromName = LanguageName(job.From),
        toName = LanguageName(job.To),
        state = job.State,
        percent = Math.Round(job.Percent, 1),
        error = job.Error,
        startedAt = job.StartedAt
    };

    private sealed class InstallJob
    {
        public string Id { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string State { get; set; } = "running";
        public double Percent { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset StartedAt { get; set; }
    }

    // --------------------------------------------------------- Pont Python

    private async Task<JsonElement> SendAsync(object request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync(cancellationToken);
            return await SendRawAsync(request, timeout, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _input is not null && _output is not null)
        {
            if (!_engineAvailable)
            {
                throw new TranslationException(_engineError ?? "Le moteur Argos Translate n'est pas disponible.");
            }
            return;
        }

        StartProcess();
        var status = await SendRawAsync(new { op = "status" }, TimeSpan.FromSeconds(25), cancellationToken);

        _engineAvailable = status.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        _engineVersion = ReadString(status, "version");
        if (!_engineAvailable)
        {
            _engineError = ReadString(status, "error")
                ?? "Argos Translate n'est pas installé. Lancez tools\\setup-argos.ps1 puis redémarrez vibeChat.";
            throw new TranslationException(_engineError);
        }
    }

    private void StartProcess()
    {
        StopProcess();

        var script = ResolveScript();
        if (!File.Exists(script))
        {
            throw new TranslationException(
                "Le pont Argos Translate (tools\\argos_bridge.py) est introuvable. Réinstallez l'application.");
        }

        var (python, prefix) = ResolvePython();
        var info = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in prefix) info.ArgumentList.Add(argument);
        info.ArgumentList.Add("-u");
        info.ArgumentList.Add(script);

        var argosPath = ResolveArgosPath();
        if (!string.IsNullOrWhiteSpace(argosPath))
        {
            info.ArgumentList.Add("--argos-path");
            info.ArgumentList.Add(argosPath);
        }

        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUTF8"] = "1";

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new TranslationException("Impossible de démarrer le moteur de traduction Python.");
        }

        _process = process;
        _input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        _output = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8);

        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                {
                    _logger.LogDebug("argos: {Line}", line);
                }
            }
            catch
            {
                // processus arrêté
            }
        });
    }

    private async Task<JsonElement> SendRawAsync(object request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_input is null || _output is null)
        {
            throw new TranslationException("Le moteur de traduction n'est pas démarré.");
        }

        var id = Interlocked.Increment(ref _requestId).ToString();
        var node = JsonSerializer.SerializeToNode(request, Json)?.AsObject() ?? new JsonObject();
        node["id"] = id;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _input.WriteLineAsync(node.ToJsonString(Json).AsMemory(), timeoutSource.Token);

            while (true)
            {
                var line = await _output.ReadLineAsync(timeoutSource.Token);
                if (line is null)
                {
                    throw new TranslationException(
                        "Le moteur de traduction s'est arrêté. Vérifiez l'installation d'Argos Translate.");
                }

                line = line.Trim();
                if (line.Length == 0 || line[0] != '{') continue;

                JsonElement root;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    root = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (root.TryGetProperty("event", out var eventElement)
                    && eventElement.GetString() == "progress")
                {
                    var percent = root.TryGetProperty("percent", out var percentElement)
                        && percentElement.TryGetDouble(out var value) ? value : 0;
                    UpdateJobProgress(ReadString(root, "jobId"), percent);
                    continue;
                }

                if (ReadString(root, "id") == id) return root;
            }
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            StopProcess();
            throw new TranslationException(
                "Le moteur de traduction ne répond pas (délai dépassé). Réessayez ou redémarrez vibeChat.");
        }
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        _input = null;
        _output = null;

        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // déjà arrêté
        }
        finally
        {
            process.Dispose();
        }
    }

    private (string File, List<string> Prefix) ResolvePython()
    {
        var configured = (_database.GetSetting(PythonKey) ?? string.Empty).Trim();
        if (configured.Length > 0 && File.Exists(configured))
        {
            return (configured, new List<string>());
        }

        var candidates = new[]
        {
            Path.Combine(ChatPaths.DataDirectory, "argos-venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "argos-venv", "Scripts", "python.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WhatsUP", "argos-venv", "Scripts", "python.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return (candidate, new List<string>());
        }

        return ("python", new List<string>());
    }

    private string? ResolveArgosPath()
    {
        var configured = (_database.GetSetting(ArgosKey) ?? string.Empty).Trim();
        if (configured.Length > 0 && Directory.Exists(configured)) return configured;

        var candidates = new[]
        {
            @"C:\Users\LeMarchandElectronic\Desktop\argos-translate",
            Path.Combine(ChatPaths.DataDirectory, "argos-translate"),
            Path.Combine(AppContext.BaseDirectory, "argos-translate"),
            Path.Combine(AppContext.BaseDirectory, "tools", "argos-translate"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WhatsUP", "argos-translate")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string ResolveScript()
    {
        var besideApp = Path.Combine(AppContext.BaseDirectory, "tools", "argos_bridge.py");
        if (File.Exists(besideApp)) return besideApp;
        return Path.Combine(Directory.GetCurrentDirectory(), "tools", "argos_bridge.py");
    }

    private static List<object> ReadPairs(JsonElement root)
    {
        var pairs = new List<object>();
        if (!root.TryGetProperty("pairs", out var array) || array.ValueKind != JsonValueKind.Array) return pairs;

        foreach (var item in array.EnumerateArray())
        {
            var from = ReadString(item, "from");
            var to = ReadString(item, "to");
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;
            pairs.Add(new
            {
                from,
                to,
                fromName = LanguageName(from),
                toName = LanguageName(to)
            });
        }

        return pairs;
    }

    private static List<object> ReadLanguages(JsonElement root)
    {
        var languages = new List<object>();
        if (!root.TryGetProperty("languages", out var array) || array.ValueKind != JsonValueKind.Array) return languages;

        foreach (var item in array.EnumerateArray())
        {
            var code = ReadString(item, "code");
            if (string.IsNullOrWhiteSpace(code)) continue;
            languages.Add(new
            {
                code,
                name = ReadString(item, "name") ?? LanguageName(code)
            });
        }

        return languages;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string CacheKey(string from, string to, string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"argos|{from}|{to}|{text}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        StopProcess();
        _gate.Dispose();
    }
}
