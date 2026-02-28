using Microsoft.Maui.Handlers;
using Microsoft.Web.WebView2.Core;

namespace VisioCall.Maui.Platforms.Windows.Handlers;

/// <summary>
/// Grants camera and microphone permissions to WebView2, sets up
/// virtual host mapping for secure context, and bridges JS postMessage to C#.
/// </summary>
public class WebViewPermissionHandler
{
    public const string VirtualHost = "webrtc.local";

    /// <summary>
    /// Fired when JS calls window.chrome.webview.postMessage(url).
    /// </summary>
    public static event Action<string>? OnWebMessageReceived;

    public static void Configure()
    {
        WebViewHandler.Mapper.AppendToMapping("WebRtcPermissions", (handler, view) =>
        {
            var webView2 = handler.PlatformView;

            webView2.CoreWebView2Initialized += (s, e) =>
            {
                var core = webView2.CoreWebView2;

                // Grant camera and microphone permissions
                core.PermissionRequested += (sender, args) =>
                {
                    if (args.PermissionKind is CoreWebView2PermissionKind.Camera
                        or CoreWebView2PermissionKind.Microphone)
                    {
                        args.State = CoreWebView2PermissionState.Allow;
                    }
                };

                // Map virtual host to local folder for secure context (getUserMedia)
                var webrtcDir = Path.Combine(FileSystem.AppDataDirectory, "webrtc");
                Directory.CreateDirectory(webrtcDir);

                core.SetVirtualHostNameToFolderMapping(
                    VirtualHost,
                    webrtcDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                // Bridge JS postMessage → C# event
                core.WebMessageReceived += (sender, args) =>
                {
                    var message = args.TryGetWebMessageAsString();
                    if (message is not null)
                        OnWebMessageReceived?.Invoke(message);
                };
            };
        });
    }
}
