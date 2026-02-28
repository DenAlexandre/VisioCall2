using VisioCall.Maui.PageModels;

namespace VisioCall.Maui.Pages;

public partial class VideoCallPage : ContentPage
{
    private readonly VideoCallPageModel _pageModel;

    public VideoCallPage(VideoCallPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = _pageModel = pageModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Wait for WebView to load, then initialize the call
        WebRtcView.Navigated += async (_, _) =>
        {
            await _pageModel.InitializeCallAsync(WebRtcView);
        };

        var html = await LoadWebRtcHtmlAsync();

#if WINDOWS
        // Write HTML to app data and load via virtual host for secure context (getUserMedia requires HTTPS)
        var webrtcDir = Path.Combine(FileSystem.AppDataDirectory, "webrtc");
        Directory.CreateDirectory(webrtcDir);
        var htmlPath = Path.Combine(webrtcDir, "index.html");
        await File.WriteAllTextAsync(htmlPath, html);
        WebRtcView.Source = new UrlWebViewSource
        {
            Url = $"https://{Platforms.Windows.Handlers.WebViewPermissionHandler.VirtualHost}/index.html"
        };
#else
        WebRtcView.Source = new HtmlWebViewSource { Html = html };
#endif
    }

    private static async Task<string> LoadWebRtcHtmlAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("webrtc/index.html");
        using var reader = new StreamReader(stream);
        var html = await reader.ReadToEndAsync();

        // Inline the CSS and JS
        try
        {
            using var cssStream = await FileSystem.OpenAppPackageFileAsync("webrtc/webrtc.css");
            using var cssReader = new StreamReader(cssStream);
            var css = await cssReader.ReadToEndAsync();
            html = html.Replace("/*INLINE_CSS*/", css);
        }
        catch { }

        try
        {
            using var jsStream = await FileSystem.OpenAppPackageFileAsync("webrtc/webrtc.js");
            using var jsReader = new StreamReader(jsStream);
            var js = await jsReader.ReadToEndAsync();
            html = html.Replace("/*INLINE_JS*/", js);
        }
        catch { }

        return html;
    }
}
