namespace CaramelChat.Services;

/// <summary>
/// Emplacement des données (base SQLite, fichiers, certificat). On garde le dossier
/// <c>App_Data</c> là où l’application est lancée (dossier du projet en développement,
/// dossier de l’exécutable pour une application de bureau), avec un repli dans
/// %LOCALAPPDATA% si ce dossier n’est pas accessible en écriture.
/// </summary>
public static class ChatPaths
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string FilesDirectory => Path.Combine(DataDirectory, "files");

    private const string MachineMarker = "machine.txt";

    /// <summary>
    /// Le dossier de données peut être copié d'un PC à l'autre avec l'application.
    /// Pour qu'un nouveau poste ne réutilise pas l'identité de l'expéditeur, on note
    /// la machine propriétaire des données : si le dossier vient d'une autre machine,
    /// il est mis de côté et l'application repart sur une identité neuve (nom du PC).
    /// </summary>
    public static void EnsureMachineOwnership()
    {
        var marker = Path.Combine(DataDirectory, MachineMarker);
        var machine = Environment.MachineName;

        try
        {
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(marker, machine);
                return;
            }

            // Installation existante (créée avant cette vérification) : on l'adopte.
            if (!File.Exists(marker))
            {
                File.WriteAllText(marker, machine);
                return;
            }

            var owner = File.ReadAllText(marker).Trim();
            if (owner.Length == 0 || string.Equals(owner, machine, StringComparison.OrdinalIgnoreCase)) return;

            // Dossier vide livré avec l'application : rien à récupérer, on adopte le poste.
            if (!File.Exists(Path.Combine(DataDirectory, "caramelchat.db")))
            {
                File.WriteAllText(marker, machine);
                return;
            }

            // Dossier venant d'un autre poste : on l'écarte (sauvegarde) et on repart à zéro.
            var safeMachine = new string(machine.Where(char.IsLetterOrDigit).ToArray());
            if (safeMachine.Length == 0) safeMachine = "poste";
            var backup = $"{DataDirectory}.ancien-{safeMachine}-{DateTime.Now:yyyyMMdd-HHmmss}";

            Directory.Move(DataDirectory, backup);
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(marker, machine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Dossier verrouillé ou non déplaçable : on ne touche pas aux données.
        }
    }

    private static string ResolveDataDirectory()
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        // Lancement depuis le projet (dotnet run, Visual Studio) ou depuis le dossier
        // de l’exécutable : les données restent à côté de l’application.
        if (File.Exists(Path.Combine(workingDirectory, "appsettings.json")))
        {
            return Path.Combine(workingDirectory, "App_Data");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhatsUP",
            "App_Data");
    }
}
