using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.IO;
using System.IO.Pipes;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

// WPF Shell interceptor logic
// WebView2 is hosted here, catches fetch reqs from frontend and forwards them to backend

namespace StorageStandby.Desktop
{
    public partial class MainWindow : Window
    {
        private HttpClient? _pipeClient;
        private Process? _backendProcess;
        private readonly Forms.NotifyIcon _trayIcon;
        private bool _isExiting;

        public MainWindow()
        {
            try
            {
                Debug.WriteLine("[MainWindow] Initializing MainWindow");
                InitializeComponent();
                _trayIcon = CreateTrayIcon();
#if !DEBUG
                StartBackendProcess();
#endif
                _ = InitializeWebViewBridge().ContinueWith(task =>
                {
                    if (!task.IsFaulted)
                    {
                        return;
                    }

                    Exception exception = task.Exception?.GetBaseException()
                        ?? new InvalidOperationException("WebView2 initialization failed.");
                    Debug.WriteLine($"[InitializeWebViewBridge] Fatal error: {exception}");
                    Dispatcher.Invoke(() => System.Windows.MessageBox.Show(
                        $"StorageStandby could not initialize its web interface.\n\n{exception}",
                        "StorageStandby startup error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error));
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Exception: {ex}");
                throw;
            }
        }

        private Forms.NotifyIcon CreateTrayIcon()
        {
            var trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("Open Storage-Standby", null, (_, _) => ShowWindow());
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

            var trayIcon = new Forms.NotifyIcon
            {
                Icon = Drawing.SystemIcons.Application,
                Text = "StorageStandby",
                Visible = true,
                ContextMenuStrip = trayMenu
            };

            trayIcon.DoubleClick += (_, _) => ShowWindow();
            return trayIcon;
        }

        private void ShowWindow()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }

        private void ExitApplication()
        {
            _isExiting = true;
            Close();
        }

        private void StartBackendProcess()
        {
            string backendPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "Backend",
                "StorageStandby.Backend.exe"));

            if (!File.Exists(backendPath))
            {
                throw new FileNotFoundException("StorageStandby backend was not found.", backendPath);
            }

            _backendProcess = Process.Start(new ProcessStartInfo
            {
                FileName = backendPath,
                WorkingDirectory = System.IO.Path.GetDirectoryName(backendPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        private static void WriteBridgeDiagnostic(string message)
        {
            try
            {
                string logDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StorageStandby");
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(
                    System.IO.Path.Combine(logDirectory, "desktop-bridge.log"),
                    $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never affect request handling.
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(
                    2000,
                    "StorageStandby is still running",
                    "The app was minimized to the system tray.",
                    Forms.ToolTipIcon.Info);
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_backendProcess is { HasExited: false })
            {
                _backendProcess.Kill(entireProcessTree: true);
                _backendProcess.Dispose();
            }

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            base.OnClosed(e);
        }

        private async Task InitializeWebViewBridge()
        {
            Debug.WriteLine("[InitializeWebViewBridge] Starting");
#if DEBUG
            const string webViewProfileName = "WebView2-Development";
#else
            const string webViewProfileName = "WebView2";
#endif
            string webViewDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StorageStandby",
                webViewProfileName);
            Directory.CreateDirectory(webViewDataPath);
            WriteBridgeDiagnostic($"Initializing WebView bridge. Profile: {webViewDataPath}");

            // Keep Chromium's writable profile outside Program Files inside the %LOCALAPPDATA% folder
            CoreWebView2Environment webViewEnvironment =
                await CoreWebView2Environment.CreateAsync(userDataFolder: webViewDataPath);
            await webView.EnsureCoreWebView2Async(webViewEnvironment);
            Debug.WriteLine("[InitializeWebViewBridge] WebView2 ready");
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // native .NET IPC magic: setup HttpClient to tunnel requests down the Named Pipe
#if DEBUG
            const string storageStandbyPipeName = "StorageStandbyPipe-Development";
#else
            const string storageStandbyPipeName = "StorageStandbyPipe";
#endif
            var socketsHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var pipeClientStream = new NamedPipeClientStream(
                        serverName: ".",
                        pipeName: storageStandbyPipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous
                    );

                    await pipeClientStream.ConnectAsync(30000, cancellationToken);
                    return pipeClientStream;
                }
            };

            _pipeClient = new HttpClient(socketsHandler) { BaseAddress = new Uri("http://storagestandby-api.local") };
            //_pipeClient.BaseAddress = new Uri("http://localhost:5000"); // Replace with your pipe server address

            // intercept browser web reqs (e.g. fetch()) from Vite frontend and tunnel them over the Windows kernel pipe
            webView.CoreWebView2.WebResourceRequested += async (sender, args) =>
            {
                var request = args.Request;
                var uri = new Uri(request.Uri);

                Debug.WriteLine($"[WebResourceRequested] Intercepted: {uri}");
                WriteBridgeDiagnostic($"Web resource: {request.Method} {uri}");

                if (uri.Host == "storagestandby-api.local" &&
                    uri.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    WriteBridgeDiagnostic($"API request: {request.Method} {uri.PathAndQuery}");
                    Debug.WriteLine($"[WebResourceRequested] Matched storagestandby-api.local, getting deferral");
                    var deferral = args.GetDeferral();

                    try
                    {
                        Debug.WriteLine($"[WebResourceRequested] Processing request: {args.Request.Method} {uri.PathAndQuery}");
                        // map incoming WebView request to our pipeline client
                        var method = new HttpMethod(args.Request.Method);

                        // Create request relative to BaseAddress
                        var requestUri = uri.PathAndQuery;
                        Debug.WriteLine($"[WebResourceRequested] Creating request with URI: {requestUri}");

                        using var outboundRequest = new HttpRequestMessage(method, requestUri);

                        // Forward CORS-related headers from the WebView request down the pipe.
                        // The backend's CORS middleware (app.UseCors("AllowFrontend")) needs to
                        // see Origin + Access-Control-Request-* to evaluate the origin and answer
                        // preflight (OPTIONS) requests itself with the proper Access-Control-Allow-*
                        // headers -- this lets the backend own CORS instead of this Desktop app.
                        if (args.Request.Headers.Contains("Origin"))
                        {
                            outboundRequest.Headers.TryAddWithoutValidation("Origin", args.Request.Headers.GetHeader("Origin"));
                        }
                        if (args.Request.Headers.Contains("Access-Control-Request-Method"))
                        {
                            outboundRequest.Headers.TryAddWithoutValidation("Access-Control-Request-Method", args.Request.Headers.GetHeader("Access-Control-Request-Method"));
                        }
                        if (args.Request.Headers.Contains("Access-Control-Request-Headers"))
                        {
                            outboundRequest.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", args.Request.Headers.GetHeader("Access-Control-Request-Headers"));
                        }

                        // forward the payload down the pipe if it's a POST/PUT request
                        if (args.Request.Content != null)
                        {
                            outboundRequest.Content = new StreamContent(args.Request.Content);

                            // Copy the Content-Type header from the original WebView request.
                            // Without this, the backend sees an empty Content-Type and fails
                            // JSON model binding with "Expected a supported JSON media type but got \"\"."
                            var contentType = args.Request.Headers.GetHeader("Content-Type");
                            if (!string.IsNullOrEmpty(contentType))
                            {
                                outboundRequest.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                                Debug.WriteLine($"[WebResourceRequested] Forwarded Content-Type: {contentType}");
                            }
                        }


                        Debug.WriteLine($"[WebResourceRequested] Sending through Named Pipe...");
                        // send it down the Named Pipe
                        var response = await _pipeClient.SendAsync(outboundRequest);
                        WriteBridgeDiagnostic($"API response: {(int)response.StatusCode} {uri.PathAndQuery}");
                        Debug.WriteLine($"[WebResourceRequested] Response Status: {response.StatusCode}");

                        string content = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"[WebResourceRequested] Raw response content: {content}");
                        Debug.WriteLine($"[WebResourceRequested] Content byte length: {content.Length}");
                        byte[] contentBytes = System.Text.Encoding.UTF8.GetBytes(content);

                        // Build headers - but REMOVE Transfer-Encoding and ADD Content-Length
                        string headerString = "";

                        foreach (var header in response.Headers)
                        {
                            // Rebuild framing headers below for WebView2.
                            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                                continue;

                            foreach (var value in header.Value)
                            {
                                headerString += $"{header.Key}: {value}\r\n";
                                Debug.WriteLine($"[Headers] Response: {header.Key}: {value}");
                            }
                        }

                        // Add content headers
                        if (response.Content.Headers != null)
                        {
                            foreach (var header in response.Content.Headers)
                            {
                                // Rebuild framing headers below for WebView2.
                                if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                                    header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                foreach (var value in header.Value)
                                {
                                    headerString += $"{header.Key}: {value}\r\n";
                                    Debug.WriteLine($"[Headers] Content: {header.Key}: {value}");
                                }
                            }
                        }

                        // Ensure Content-Type exists (only meaningful when there's a body;
                        // a 204 preflight response from the CORS middleware has none)
                        if (contentBytes.Length > 0 && !headerString.Contains("Content-Type"))
                        {
                            headerString += "Content-Type: application/json\r\n";
                            Debug.WriteLine($"[Headers] Added default Content-Type");
                        }

                        // NOTE: CORS headers are no longer injected here. The backend's CORS
                        // middleware (app.UseCors("AllowFrontend")) now supplies the proper
                        // Access-Control-Allow-* headers on every response (the Origin /
                        // Access-Control-Request-* request headers forwarded above let it
                        // evaluate each request), so the whole app shares one CORS policy.

                        // Add Content-Length
                        headerString += $"Content-Length: {contentBytes.Length}\r\n";
                        headerString += "\r\n";
                        Debug.WriteLine($"[Headers] Added Content-Length: {contentBytes.Length}");
                        Debug.WriteLine($"[WebResourceRequested] Final header string (with escapes shown):\r\n{headerString.Replace("\r\n", "\\r\\n")}");

                        var memoryStream = new System.IO.MemoryStream(contentBytes);
                        memoryStream.Position = 0;  // Make sure it's at the start

                        Debug.WriteLine($"[WebResourceRequested] Response details:");
                        Debug.WriteLine($"  Status: {(int)response.StatusCode}");
                        Debug.WriteLine($"  Stream length: {memoryStream.Length}");
                        Debug.WriteLine($"  Stream position: {memoryStream.Position}");
                        Debug.WriteLine($"  Headers:\r\n{headerString.Replace("\r\n", " | ")}");

                        // inject C# response directly back up into the React UI
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            memoryStream,
                            (int)response.StatusCode,
                            response.ReasonPhrase ?? "OK",
                            headerString
                        );
                        Debug.WriteLine($"[WebResourceRequested] Response created successfully");
                    }
                    catch (Exception ex)
                    {
                        WriteBridgeDiagnostic($"API bridge error for {uri}: {ex}");
                        Debug.WriteLine($"[InitializeWebViewBridge] Error: {ex}");
                        System.Windows.MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                        Debug.WriteLine($"[WebResourceRequested] Exception: {ex.Message}\n{ex.StackTrace}");
                        string errorJson = $"{{\"error\": \"Backend service unavailable\", \"details\": \"{ex.Message}\"}}";
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(errorJson)),
                            503,
                            "Service Unavailable",
                            "Content-Type: application/json"
                        );

                    }
                    finally {
                        Debug.WriteLine($"[WebResourceRequested] Completing deferral");
                        deferral.Complete();
                    }
                }

            };

            // Observe the virtual host broadly, but only tunnel API requests through the pipe.
            // Non-API requests fall through to the virtual host folder mapping below.
            webView.CoreWebView2.AddWebResourceRequestedFilter("http://storagestandby-api.local/*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
            WriteBridgeDiagnostic("WebResourceRequested filter registered.");

#if !DEBUG
            // In Release, serve the compiled frontend from the desktop publish folder.
            string virtualHostFolderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dist");

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "storagestandby.local",
                virtualHostFolderPath,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
            );
            WriteBridgeDiagnostic($"Virtual host mapped to: {virtualHostFolderPath}");
#endif

            webView.CoreWebView2.NavigationCompleted += (_, args) =>
                WriteBridgeDiagnostic($"Navigation completed: {webView.Source} success={args.IsSuccess} error={args.WebErrorStatus}");

#if DEBUG
            // In Development (F5), use Vite's live server for instant hot-reloading
            webView.Source = new Uri("http://localhost:5173");
#else       
            // Point the browser to the new virtual local domain
            webView.Source = new Uri("http://storagestandby.local/index.html");
#endif

        }

        private void OnWebMessageReceived(
            object? sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            var message = args.TryGetWebMessageAsString();

            if (message != "pick-folder")
            {
                return;
            }

            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Select a folder to back up",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                webView.CoreWebView2.PostWebMessageAsJson(
                    JsonSerializer.Serialize(new
                    {
                        type = "folder-picker-cancelled",
                    }));

                return;
            }

            webView.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new
                {
                    type = "folder-selected",
                    path = dialog.SelectedPath,
                }));
        }
    }
}
