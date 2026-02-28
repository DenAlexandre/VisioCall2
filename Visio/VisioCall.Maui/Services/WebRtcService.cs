using System.Text.Json;
using VisioCall.Shared.Models;

namespace VisioCall.Maui.Services;

/// <summary>
/// Bridge between C# and WebRTC JavaScript running in a WebView.
/// C# -> JS: EvaluateJavaScriptAsync
/// JS -> C#: URL interception on Android/iOS, polling on Windows
/// </summary>
public class WebRtcService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private WebView? _webView;
    private readonly SignalingService _signaling;
    private string? _remoteUserId;
    private CancellationTokenSource? _pollCts;

    private Action<SessionDescription>? _onReceiveOffer;
    private Action<SessionDescription>? _onReceiveAnswer;
    private Action<IceCandidate>? _onReceiveIceCandidate;

    public event Action<string>? OnError;

    public WebRtcService(SignalingService signaling)
    {
        _signaling = signaling;
    }

    public void AttachWebView(WebView webView, string remoteUserId)
    {
        _webView = webView;
        _remoteUserId = remoteUserId;
        _webView.Navigating += OnWebViewNavigating;

#if WINDOWS
        StartPolling();
#endif

        // Wire SignalR events to JS
        _onReceiveOffer = async offer =>
            await EvalJsAsync($"receiveOffer({JsonSerializer.Serialize(offer, JsonOptions)})");
        _onReceiveAnswer = async answer =>
            await EvalJsAsync($"receiveAnswer({JsonSerializer.Serialize(answer, JsonOptions)})");
        _onReceiveIceCandidate = async candidate =>
            await EvalJsAsync($"receiveIceCandidate({JsonSerializer.Serialize(candidate, JsonOptions)})");

        _signaling.OnReceiveOffer += _onReceiveOffer;
        _signaling.OnReceiveAnswer += _onReceiveAnswer;
        _signaling.OnReceiveIceCandidate += _onReceiveIceCandidate;
    }

    public void DetachWebView()
    {
#if WINDOWS
        StopPolling();
#endif
        if (_onReceiveOffer is not null) _signaling.OnReceiveOffer -= _onReceiveOffer;
        if (_onReceiveAnswer is not null) _signaling.OnReceiveAnswer -= _onReceiveAnswer;
        if (_onReceiveIceCandidate is not null) _signaling.OnReceiveIceCandidate -= _onReceiveIceCandidate;

        if (_webView is not null)
        {
            _webView.Navigating -= OnWebViewNavigating;
            _webView = null;
        }
    }

    public async Task CreateOfferAsync() =>
        await EvalJsAsync("createOffer()");

    public async Task CreateAnswerAsync() =>
        await EvalJsAsync("createAnswer()");

    public async Task ToggleMuteAsync() =>
        await EvalJsAsync("toggleMute()");

    public async Task ToggleCameraAsync() =>
        await EvalJsAsync("toggleCamera()");

    public async Task CloseConnectionAsync() =>
        await EvalJsAsync("closeConnection()");

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith("visiocall://")) return;
        e.Cancel = true;
        _ = HandleNativeMessageAsync(e.Url);
    }

#if WINDOWS
    private void StartPolling()
    {
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
                await PollMessagesAsync();
            }
        }, token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollMessagesAsync()
    {
        if (_webView is null) return;

        try
        {
            var json = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    return await _webView.EvaluateJavaScriptAsync("flushMessages()");
                }
                catch
                {
                    return null;
                }
            });

            if (string.IsNullOrEmpty(json) || json == "null") return;

            // EvaluateJavaScriptAsync may wrap strings in extra quotes — strip them
            if (json.StartsWith('"') && json.EndsWith('"'))
            {
                json = JsonSerializer.Deserialize<string>(json) ?? json;
            }

            var messages = JsonSerializer.Deserialize<List<PolledMessage>>(json, JsonOptions);
            if (messages is null) return;

            foreach (var msg in messages)
            {
                await ProcessPolledMessageAsync(msg.Action, msg.Data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke($"Poll error: {ex.Message}");
        }
    }

    private async Task ProcessPolledMessageAsync(string action, JsonElement data)
    {
        try
        {
            switch (action)
            {
                case "offer":
                    var offer = data.Deserialize<SessionDescription>(JsonOptions)!;
                    await _signaling.SendOfferAsync(_remoteUserId!, offer);
                    break;
                case "answer":
                    var answer = data.Deserialize<SessionDescription>(JsonOptions)!;
                    await _signaling.SendAnswerAsync(_remoteUserId!, answer);
                    break;
                case "ice-candidate":
                    var candidate = data.Deserialize<IceCandidate>(JsonOptions)!;
                    await _signaling.SendIceCandidateAsync(_remoteUserId!, candidate);
                    break;
                case "error":
                    OnError?.Invoke(data.GetString() ?? "Unknown JS error");
                    break;
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"WebRTC bridge error: {ex.Message}");
        }
    }

    private class PolledMessage
    {
        public string Action { get; set; } = "";
        public JsonElement Data { get; set; }
    }
#endif

    /// <summary>
    /// Handles a visiocall:// message from JS.
    /// Called by Navigating event (Android/iOS).
    /// </summary>
    public async Task HandleNativeMessageAsync(string url)
    {
        if (!url.StartsWith("visiocall://")) return;

        try
        {
            var uri = new Uri(url);
            var action = uri.Host;
            var data = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

            switch (action)
            {
                case "offer":
                    var offer = JsonSerializer.Deserialize<SessionDescription>(data, JsonOptions)!;
                    await _signaling.SendOfferAsync(_remoteUserId!, offer);
                    break;
                case "answer":
                    var answer = JsonSerializer.Deserialize<SessionDescription>(data, JsonOptions)!;
                    await _signaling.SendAnswerAsync(_remoteUserId!, answer);
                    break;
                case "ice-candidate":
                    var candidate = JsonSerializer.Deserialize<IceCandidate>(data, JsonOptions)!;
                    await _signaling.SendIceCandidateAsync(_remoteUserId!, candidate);
                    break;
                case "error":
                    OnError?.Invoke(data);
                    break;
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"WebRTC bridge error: {ex.Message}");
        }
    }

    private async Task EvalJsAsync(string js)
    {
        if (_webView is null) return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await _webView.EvaluateJavaScriptAsync(js);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"JS eval error: {ex.Message}");
            }
        });
    }
}
