using System.Net;
using System.Windows;
using CaramelChat.Data;
using CaramelChat.Desktop;
using CaramelChat.Endpoints;
using CaramelChat.Models;
using CaramelChat.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace CaramelChat;

/// <summary>
/// Application de bureau vibeChat : le serveur C# (API, SQLite, découverte réseau,
/// transfert entre postes) démarre dans le processus, puis la fenêtre Windows affiche
/// l’interface WhatsApp intégrée (WebView2) sur l’adresse locale du serveur.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Les fichiers de l’interface (wwwroot) et appsettings.json sont copiés à côté
        // de l’exécutable : la fenêtre fonctionne même lancée par double-clic.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        var chatOptions = builder.Configuration.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
        builder.Services.AddSingleton(chatOptions);

        // Limite d'upload = taille maximale d'un fichier + marge (multipart, en-têtes).
        var maxRequestBytes = (chatOptions.MaxFileSizeMb + 10L) * 1024L * 1024L;

        // Un dossier copié depuis un autre PC ne doit pas hériter de son identité :
        // on repart alors sur un compte neuf au nom de cette machine.
        ChatPaths.EnsureMachineOwnership();

        // Certificat auto-signé : conservé pour l'accès navigateur en HTTPS et les appels
        // depuis un autre poste. La fenêtre de bureau, servie sur localhost, est déjà
        // une origine sécurisée pour le micro et la webcam.
        var certificate = CertificateFactory.LoadOrCreate(ChatPaths.DataDirectory);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = maxRequestBytes;

            foreach (var endpoint in ParseHttpEndpoints(builder.Configuration["urls"]))
            {
                options.Listen(endpoint);
            }

            if (chatOptions.HttpsPort is > 0 and <= 65535)
            {
                options.ListenAnyIP(chatOptions.HttpsPort, listen => listen.UseHttps(certificate));
            }
        });

        builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxRequestBytes);

        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton<LocalIdentity>();
        builder.Services.AddSingleton<PeerRegistry>();
        builder.Services.AddSingleton<RealtimeHub>();
        builder.Services.AddSingleton<AvatarStore>();
        builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton<StatusService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<StatusService>());
        // Traduction hors ligne Argos : le moteur Python est lancé à la demande.
        builder.Services.AddSingleton<TranslationService>();
        builder.Services.AddHostedService<DiscoveryService>();
        // File d'attente : renvoie les messages en attente dès qu'un poste redevient joignable.
        builder.Services.AddHostedService<OutboxService>();
        builder.Services.AddHttpClient("peer", client => client.Timeout = TimeSpan.FromSeconds(120));
        // Les statuts sont interrogés souvent : délai court pour ne pas bloquer l'interface.
        builder.Services.AddHttpClient("status", client => client.Timeout = TimeSpan.FromSeconds(4));

        var app = builder.Build();

        // Base SQLite créée dans App_Data/ au premier démarrage.
        app.Services.GetRequiredService<AppDatabase>().Initialize();

        // Le port annoncé aux autres postes est celui réellement écouté en HTTP.
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                var addresses = app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()?.Addresses;
                var first = addresses?.FirstOrDefault(address => address.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
                if (first is not null && Uri.TryCreate(first, UriKind.Absolute, out var uri) && uri.Port > 0)
                {
                    chatOptions.HttpPort = uri.Port;
                }
            }
            catch
            {
                // on garde le port de appsettings.json
            }
        });

        app.UseWebSockets();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Canal temps réel (WebSocket natif, aucune librairie JS requise).
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/ws"))
            {
                var hub = context.RequestServices.GetRequiredService<RealtimeHub>();
                await hub.HandleAsync(context);
                return;
            }

            await next();
        });

        app.MapChatApi();

        try
        {
            app.Start();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "vibeChat n’a pas pu démarrer le serveur local.\n\n" +
                $"Vérifiez que le port {chatOptions.HttpPort} (TCP) et le port {chatOptions.HttpsPort} (TCP) " +
                "ne sont pas déjà utilisés par une autre instance de l’application.\n\n" +
                exception.Message,
                "vibeChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            // L’interface est servie par le processus lui-même : aucun navigateur externe.
            DesktopShell.Run($"http://127.0.0.1:{chatOptions.HttpPort}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "La fenêtre de vibeChat n’a pas pu s’ouvrir.\n\n" +
                "Le composant Microsoft Edge WebView2 est nécessaire : installez le " +
                "« WebView2 Runtime » depuis le site de Microsoft, puis relancez l’application.\n\n" +
                exception.Message,
                "vibeChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                app.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // arrêt déjà effectué
            }

            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // Écoute HTTP demandée (--urls, ASPNETCORE_URLS, appsettings) : on garde le port
    // et l'interface d'écoute choisis par l'utilisateur, puis on ajoute HTTPS.
    private static IEnumerable<IPEndPoint> ParseHttpEndpoints(string? urls)
    {
        var endpoints = new List<IPEndPoint>();

        foreach (var raw in (urls ?? "http://0.0.0.0:5080").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) continue;
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)) continue;

            var address = uri.Host switch
            {
                "0.0.0.0" or "*" or "+" => IPAddress.Any,
                "localhost" => IPAddress.Loopback,
                _ => IPAddress.TryParse(uri.Host, out var parsed) ? parsed : IPAddress.Any
            };

            var port = uri.Port > 0 ? uri.Port : 80;
            if (endpoints.All(endpoint => endpoint.Port != port || !endpoint.Address.Equals(address)))
            {
                endpoints.Add(new IPEndPoint(address, port));
            }
        }

        if (endpoints.Count == 0) endpoints.Add(new IPEndPoint(IPAddress.Any, 5080));
        return endpoints;
    }
}
