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

// WPF Shell interceptor logic
// WebView2 is hosted here, catches fetch reqs from frontend and forwards them to backend

namespace StorageStandby.Desktop
{
    public partial class MainWindow : Window
    {
        
        private HttpClient? _pipeClient;

        public MainWindow()
        {
            try
            {
                Debug.WriteLine("[MainWindow] Initializing MainWindow");
                InitializeComponent();
                _ = InitializeWebViewBridge();  
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Exception: {ex}");
                throw;
            }
        }

        private async Task InitializeWebViewBridge()
        {
            Debug.WriteLine("[InitializeWebViewBridge] Starting");
            // initialize underlying Chromium engine
            await webView.EnsureCoreWebView2Async();
            Debug.WriteLine("[InitializeWebViewBridge] WebView2 ready");

            // native .NET IPC magic: setup HttpClient to tunnel requests down the Named Pipe
            var socketsHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var pipeClientStream = new NamedPipeClientStream(
                        serverName: ".",
                        pipeName: "StorageStandbyPipe",
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous
                    );

                    await pipeClientStream.ConnectAsync(2000, cancellationToken);
                    return pipeClientStream;
                }
            };

            _pipeClient = new HttpClient(socketsHandler) { BaseAddress = new Uri("http://storagestandby.local") };
            //_pipeClient.BaseAddress = new Uri("http://localhost:5000"); // Replace with your pipe server address

            // intercept browser web reqs (e.g. fetch()) from Vite frontend and tunnel them over the Windows kernel pipe
            webView.CoreWebView2.WebResourceRequested += async (sender, args) =>
            {
                var request = args.Request;
                var uri = new Uri(request.Uri);

                Debug.WriteLine($"[WebResourceRequested] Intercepted: {uri}");

                if (uri.Host == "storagestandby.local")
                {
                    Debug.WriteLine($"[WebResourceRequested] Matched storagestandby.local, getting deferral");
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
                        Debug.WriteLine($"[WebResourceRequested] Response Status: {response.StatusCode}");

                        string content = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"[WebResourceRequested] Raw response content: {content}");
                        Debug.WriteLine($"[WebResourceRequested] Content byte length: {content.Length}");
                        byte[] contentBytes = System.Text.Encoding.UTF8.GetBytes(content);

                        // Build headers - but REMOVE Transfer-Encoding and ADD Content-Length
                        string headerString = "";

                        foreach (var header in response.Headers)
                        {
                            // Skip Transfer-Encoding header - WebView2 needs Content-Length instead
                            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
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
                                // Skip Transfer-Encoding here too
                                if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                foreach (var value in header.Value)
                                {
                                    headerString += $"{header.Key}: {value}\r\n";
                                    Debug.WriteLine($"[Headers] Content: {header.Key}: {value}");
                                }
                            }
                        }

                        // Ensure Content-Type exists
                        if (!headerString.Contains("Content-Type"))
                        {
                            headerString += "Content-Type: application/json\r\n";
                            Debug.WriteLine($"[Headers] Added default Content-Type");
                        }

                        // Add CORS headers
                        headerString += "Access-Control-Allow-Origin: *\r\n";
                        headerString += "Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n";
                        headerString += "Access-Control-Allow-Headers: Content-Type\r\n";
                        Debug.WriteLine($"[Headers] Added CORS headers");

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
                        Debug.WriteLine($"[InitializeWebViewBridge] Error: {ex}");
                        MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

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

            // tell WebView2 to filter and catch any reuqests heading to our virtual API domain
            webView.CoreWebView2.AddWebResourceRequestedFilter("http://storagestandby.local/*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);

            // Set up virtual domain mapping in both DEBUG and Release
            // This tells WebView2: "If someone visits http://app.local, serve files from this physical folder."
            string virtualHostFolderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dist");

//#if DEBUG
//            // In DEBUG mode, dist doesn't exist yet, so use base directory as fallback
//            // (The WebResourceRequested handler will intercept requests anyway)
//            if (!Directory.Exists(virtualHostFolderPath))
//            {
//                virtualHostFolderPath = AppDomain.CurrentDomain.BaseDirectory;
//            }
//#endif

//            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
//                "storagestandby.local",
//                virtualHostFolderPath,
//                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
//            );

#if DEBUG
            // In Development (F5), use Vite's live server for instant hot-reloading
            webView.Source = new Uri("http://localhost:5173");
#else       
            // Point the browser to the new virtual local domain
            webView.Source = new Uri("http://storagestandby.local/index.html");
#endif

        }
    }
}
