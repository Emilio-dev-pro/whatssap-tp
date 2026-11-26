using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace CaramelChat.Desktop;

/// <summary>Lance la boucle WPF de l’application de bureau.</summary>
public static class DesktopShell
{
    public static void Run(string startUrl)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new MainWindow(startUrl);
        application.Run(window);
    }
}

/// <summary>
/// Fenêtre WhatsApp sans cadre système : le serveur C# sert l’interface sur la boucle
/// locale, et WebView2 l’affiche. Les boutons de fenêtre, les boîtes « Ouvrir » /
/// « Enregistrer sous » et les téléchargements passent par un petit pont C#.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };

    private readonly string _startUrl;
    private readonly string _downloadsDirectory;
    private readonly WebView2 _webView = new();
    private CoreWebView2? _core;
    private NotificationToast? _toast;
    private int _lastBadgeCount = -1;

    public MainWindow(string startUrl)
    {
        _startUrl = startUrl;
        _downloadsDirectory = ResolveDownloadsDirectory();

        Title = "vibeChat";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/whatsup.ico"));
        Width = 1280;
        Height = 820;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(11, 20, 26));
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        Content = _webView;

        SourceInitialized += (_, _) => ApplyNativeWindowStyle();
        Loaded += async (_, _) => await InitializeWebViewAsync();
        StateChanged += (_, _) => NotifyWindowState();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WhatsUP",
                "WebView2");
            Directory.CreateDirectory(dataFolder);

            // Les candidats ICE doivent exposer l'IP locale du poste : l'obfuscation mDNS
            // de Chromium empêche sinon les appels WebRTC entre deux PC du réseau local.
            var environmentOptions = new CoreWebView2EnvironmentOptions(
                "--disable-features=WebRtcHideLocalIpsWithMdns");
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder, environmentOptions);
            await _webView.EnsureCoreWebView2Async(environment);
            _core = _webView.CoreWebView2;

            var settings = _core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;

            _core.PermissionRequested += OnPermissionRequested;
            _core.NewWindowRequested += OnNewWindowRequested;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NavigationCompleted += (_, _) => NotifyWindowState();

            _webView.Source = new Uri(_startUrl);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "L’interface de vibeChat n’a pas pu être chargée.\n\n" +
                "Installez le « WebView2 Runtime » (Microsoft Edge WebView2) puis relancez l’application.\n\n" +
                exception.Message,
                "vibeChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    // ---------------------------------------------------------------- WebView2

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        // Micro, caméra (vocaux et appels) : autorisés dans l’application de bureau.
        if (e.PermissionKind is CoreWebView2PermissionKind.Microphone
            or CoreWebView2PermissionKind.Camera
            or CoreWebView2PermissionKind.ClipboardRead)
        {
            e.State = CoreWebView2PermissionState.Allow;
            e.Handled = true;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            OpenWithShell(uri.AbsoluteUri);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? rpcId = null;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            var type = ReadString(root, "type");
            rpcId = ReadString(root, "rpcId");

            switch (type)
            {
                case "window:minimize":
                    WindowState = WindowState.Minimized;
                    break;

                case "window:maximize":
                    ToggleMaximize();
                    break;

                case "window:close":
                    Close();
                    break;

                case "window:drag":
                    StartDrag();
                    break;

                case "app.badge":
                {
                    var count = 0;
                    if (root.TryGetProperty("count", out var countElement)) countElement.TryGetInt32(out count);
                    SetUnreadBadge(Math.Max(0, count));
                    Reply(rpcId, new Dictionary<string, object?> { ["ok"] = true });
                    break;
                }

                case "app.notify":
                    ShowNotification(root);
                    Reply(rpcId, new Dictionary<string, object?> { ["ok"] = true });
                    break;

                case "app.activate":
                    ActivateFromNotification(null);
                    Reply(rpcId, new Dictionary<string, object?> { ["ok"] = true });
                    break;

                case "app.info":
                    Reply(rpcId, new Dictionary<string, object?>
                    {
                        ["ok"] = true,
                        ["desktop"] = true,
                        ["version"] = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                        ["downloadsDirectory"] = _downloadsDirectory
                    });
                    break;

                case "app.openExternal":
                {
                    var target = ReadString(root, "url");
                    if (Uri.TryCreate(target, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        OpenWithShell(uri.AbsoluteUri);
                        Reply(rpcId, new Dictionary<string, object?> { ["ok"] = true });
                    }
                    else
                    {
                        ReplyError(rpcId, "Adresse web invalide.");
                    }
                    break;
                }

                case "file.save":
                    await SaveFileAsync(root, rpcId);
                    break;

                case "file.open":
                    await OpenFileAsync(root, rpcId);
                    break;
            }
        }
        catch (Exception exception)
        {
            ReplyError(rpcId, exception.Message);
        }
    }

    // -------------------------------------------------- Badge et notifications

    /// <summary>
    /// Pastille de la barre des tâches : nombre de conversations comportant des
    /// messages non lus, comme la pastille de WhatsApp Desktop.
    /// </summary>
    private void SetUnreadBadge(int count)
    {
        if (count == _lastBadgeCount) return;
        _lastBadgeCount = count;

        TaskbarItemInfo ??= new TaskbarItemInfo();
        if (count <= 0)
        {
            TaskbarItemInfo.Overlay = null;
            TaskbarItemInfo.Description = "vibeChat";
            return;
        }

        TaskbarItemInfo.Overlay = CreateBadgeImage(count);
        TaskbarItemInfo.Description = count == 1
            ? "1 conversation avec des messages non lus"
            : $"{count} conversations avec des messages non lus";
    }

    private static BitmapSource CreateBadgeImage(int count)
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0x00, 0xA8, 0x84)),
                new Pen(new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x1A)), 2),
                new Point(size / 2.0, size / 2.0), 14.5, 14.5);

            var text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                text.Length > 2 ? 11 : 15,
                Brushes.White,
                1.0);
            context.DrawText(
                formatted,
                new Point((size - formatted.Width) / 2.0, (size - formatted.Height) / 2.0));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Notification de bureau, dans l'esprit de WhatsApp Desktop.</summary>
    private void ShowNotification(JsonElement root)
    {
        var title = ReadString(root, "title") ?? "vibeChat";
        var body = ReadString(root, "body") ?? string.Empty;
        var peerId = ReadString(root, "peerId");

        try { _toast?.Close(); } catch { /* déjà fermée */ }

        var toast = new NotificationToast(title, body, () => ActivateFromNotification(peerId));
        _toast = toast;
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_toast, toast)) _toast = null;
        };
        toast.Show();
    }

    private void ActivateFromNotification(string? peerId)
    {
        try
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            Focus();

            if (!string.IsNullOrWhiteSpace(peerId) && _core is not null)
            {
                var argument = JsonSerializer.Serialize(peerId);
                _ = _core.ExecuteScriptAsync(
                    $"window.__whatsUpOpenConversation && window.__whatsUpOpenConversation({argument});");
            }
        }
        catch
        {
            // fenêtre en cours de fermeture
        }
    }

    /// <summary>Petite fenêtre de notification en bas à droite, sans voler le focus.</summary>
    private sealed class NotificationToast : Window
    {
        private readonly DispatcherTimer _timer;

        public NotificationToast(string title, string body, Action onClick)
        {
            Width = 360;
            Height = 102;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 18;
            Top = workArea.Bottom - Height - 18;

            var logo = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0xA8, 0x84)),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "W",
                    Foreground = Brushes.White,
                    FontSize = 21,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var heading = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var message = new TextBlock
            {
                Text = body,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xD9)),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 42,
                Margin = new Thickness(0, 3, 0, 0)
            };

            var main = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            main.Children.Add(heading);
            main.Children.Add(message);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(logo, 0);
            Grid.SetColumn(main, 1);
            grid.Children.Add(logo);
            grid.Children.Add(main);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x2C, 0x33)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x39, 0x42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Child = grid,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            card.MouseLeftButtonUp += (_, _) =>
            {
                Close();
                onClick();
            };

            Content = card;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Close();
            };
            Loaded += (_, _) => _timer.Start();
        }
    }

    // -------------------------------------------------------- Fichiers / bureau

    private async Task SaveFileAsync(JsonElement root, string? rpcId)
    {
        var fileName = SanitizeFileName(ReadString(root, "fileName"));
        var saveAs = root.TryGetProperty("saveAs", out var saveElement) && saveElement.ValueKind == JsonValueKind.True;

        if (!TryLocalUri(ReadString(root, "url"), out var uri))
        {
            ReplyError(rpcId, "Téléchargement refusé : ce fichier ne vient pas de votre poste.");
            return;
        }

        string target;
        if (saveAs)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Enregistrer sous",
                FileName = fileName,
                InitialDirectory = _downloadsDirectory,
                AddExtension = false,
                OverwritePrompt = true,
                Filter = "Tous les fichiers (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                Reply(rpcId, new Dictionary<string, object?> { ["ok"] = true, ["canceled"] = true });
                return;
            }

            target = dialog.FileName;
        }
        else
        {
            Directory.CreateDirectory(_downloadsDirectory);
            target = UniquePath(Path.Combine(_downloadsDirectory, fileName));
        }

        await DownloadToFileAsync(uri, target);

        Reply(rpcId, new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["path"] = target,
            ["fileName"] = Path.GetFileName(target),
            ["folder"] = Path.GetDirectoryName(target) ?? _downloadsDirectory
        });
    }

    private async Task OpenFileAsync(JsonElement root, string? rpcId)
    {
        var fileName = SanitizeFileName(ReadString(root, "fileName"));
        var fileId = SanitizeToken(ReadString(root, "fileId"));
        var path = ReadString(root, "path");

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            if (!TryLocalUri(ReadString(root, "url"), out var uri))
            {
                ReplyError(rpcId, "Ouverture refusée : ce fichier ne vient pas de votre poste.");
                return;
            }

            var folder = Path.Combine(Path.GetTempPath(), "WhatsUP", fileId.Length > 0 ? fileId : "fichier");
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
            {
                await DownloadToFileAsync(uri, path);
            }
        }

        OpenWithShell(path);
        Reply(rpcId, new Dictionary<string, object?> { ["ok"] = true, ["path"] = path });
    }

    private static async Task DownloadToFileAsync(Uri uri, string target)
    {
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var partial = target + ".part";
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = File.Create(partial))
            {
                await source.CopyToAsync(destination);
            }

            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
            catch
            {
                // fichier temporaire déjà supprimé
            }
        }
    }

    private static void OpenWithShell(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string ResolveDownloadsDirectory()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        if (Directory.Exists(downloads)) return downloads;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : Path.GetTempPath();
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{name} ({Guid.NewGuid():N}){extension}");
    }

    private static string SanitizeFileName(string? value)
    {
        var name = Path.GetFileName(value ?? string.Empty);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim();
        if (name.Length == 0) name = "fichier";
        return name.Length > 180 ? name[^180..] : name;
    }

    private static string SanitizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var safe = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return safe.Length > 80 ? safe[..80] : safe;
    }

    private static bool TryLocalUri(string? raw, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (!parsed.IsLoopback) return false;

        uri = parsed;
        return true;
    }

    // -------------------------------------------------------------- Fenêtre

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        NotifyWindowState();
    }

    private void NotifyWindowState()
    {
        if (_core is null) return;

        var maximized = WindowState == WindowState.Maximized ? "true" : "false";
        _ = _core.ExecuteScriptAsync($"document.body.classList.toggle('maximized', {maximized});");
    }

    private void StartDrag()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
    }

    private void ApplyNativeWindowStyle()
    {
        // Coins arrondis Windows 11 (sans effet sur Windows 10).
        var handle = new WindowInteropHelper(this).Handle;
        var preference = DwmwcpRound;
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    // --------------------------------------------------------------- Pont RPC

    private void Reply(string? rpcId, Dictionary<string, object?> payload)
    {
        if (_core is null || string.IsNullOrEmpty(rpcId)) return;
        payload["rpcId"] = rpcId;
        _core.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
    }

    private void ReplyError(string? rpcId, string message) =>
        Reply(rpcId, new Dictionary<string, object?> { ["ok"] = false, ["error"] = message });

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ------------------------------------------------------------- Win32

    private const int WmNcLeftButtonDown = 0xA1;
    private const int HtCaption = 0x2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
