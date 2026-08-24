using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BloomFreezeDoctor.Gathering;

/// <summary>One debuggable target inside Bloom's WebView2 — a page, a worker, or the browser itself.</summary>
public sealed record CdpTarget
{
    /// <summary>Chromium's id for the target.</summary>
    public required string Id { get; init; }

    /// <summary>"page", "iframe", "worker", "service_worker"…</summary>
    public required string Type { get; init; }

    /// <summary>The document title, which for Bloom names the screen the user was on.</summary>
    public required string Title { get; init; }

    /// <summary>The URL, which for Bloom says which of its own pages this is.</summary>
    public required string Url { get; init; }

    /// <summary>Where to connect to drive this target, if it is debuggable.</summary>
    public string? WebSocketUrl { get; init; }
}

/// <summary>
/// A deliberately small Chrome DevTools Protocol client: enough to list targets, ask one whether it is
/// still alive, and listen briefly for console and network events.
///
/// It exists rather than a library because the interesting question is a *timeout*: whether the renderer
/// answers at all. A full-featured client optimises for the case where everything works, which is the
/// case we are least interested in.
/// </summary>
public sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private int _nextId;

    /// <summary>
    /// Lists the debuggable targets on a port. Cheap HTTP, and it answers even when a page is wedged,
    /// because the browser process serves it rather than the renderer.
    /// </summary>
    public static async Task<List<CdpTarget>> ListTargetsAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellation
    )
    {
        using var http = new HttpClient { Timeout = timeout };
        var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", cancellation)
            .ConfigureAwait(false);
        var targets = new List<CdpTarget>();
        if (JsonNode.Parse(json) is not JsonArray array)
            return targets;
        foreach (var item in array)
        {
            if (item == null)
                continue;
            targets.Add(
                new CdpTarget
                {
                    Id = item["id"]?.GetValue<string>() ?? "",
                    Type = item["type"]?.GetValue<string>() ?? "",
                    Title = item["title"]?.GetValue<string>() ?? "",
                    Url = item["url"]?.GetValue<string>() ?? "",
                    WebSocketUrl = item["webSocketDebuggerUrl"]?.GetValue<string>(),
                }
            );
        }
        return targets;
    }

    /// <summary>Reads the browser's own version string, which identifies the WebView2 build in use.</summary>
    public static async Task<string?> ReadBrowserVersionAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellation
    )
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout };
            var json = await http.GetStringAsync(
                    $"http://127.0.0.1:{port}/json/version",
                    cancellation
                )
                .ConfigureAwait(false);
            return JsonNode.Parse(json)?["Browser"]?.GetValue<string>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Connects to one target.</summary>
    public async Task ConnectAsync(string webSocketUrl, CancellationToken cancellation)
    {
        await _socket.ConnectAsync(new Uri(webSocketUrl), cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a command and waits for its reply. Returns null if the target did not answer in time —
    /// which is not a failure of this method but the very thing we are measuring.
    /// </summary>
    public async Task<JsonNode?> SendAsync(
        string method,
        JsonObject? parameters,
        TimeSpan timeout,
        CancellationToken cancellation
    )
    {
        var id = ++_nextId;
        var request = new JsonObject { ["id"] = id, ["method"] = method };
        if (parameters != null)
            request["params"] = parameters;

        var bytes = Encoding.UTF8.GetBytes(request.ToJsonString());
        await _socket
            .SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellation)
            .ConfigureAwait(false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        try
        {
            // Replies and events are interleaved on one socket; skip events until our id comes back.
            while (!deadline.Token.IsCancellationRequested)
            {
                var message = await ReceiveAsync(deadline.Token).ConfigureAwait(false);
                if (message == null)
                    return null;
                var node = JsonNode.Parse(message);
                if (node?["id"]?.GetValue<int>() == id)
                    return node;
                if (node != null)
                    Events.Add(node);
            }
        }
        catch (OperationCanceledException)
        {
            // The point of the exercise: no answer.
        }
        catch (WebSocketException)
        {
            // Connection died under us; treat as no answer.
        }
        return null;
    }

    /// <summary>
    /// Listens for events for a while, collecting them into <see cref="Events"/>. CDP keeps no history,
    /// so this only ever sees what happens from now on — which is why the plan does not promise console
    /// history unless the Doctor was already attached.
    /// </summary>
    public async Task ListenAsync(TimeSpan duration, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(duration);
        try
        {
            while (!deadline.Token.IsCancellationRequested)
            {
                var message = await ReceiveAsync(deadline.Token).ConfigureAwait(false);
                if (message == null)
                    return;
                var node = JsonNode.Parse(message);
                if (node?["method"] != null)
                    Events.Add(node);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    /// <summary>Events seen so far, in order.</summary>
    public List<JsonNode> Events { get; } = new();

    private async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();
        while (true)
        {
            var result = await _socket
                .ReceiveAsync(new ArraySegment<byte>(buffer), cancellation)
                .ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage)
                return text.ToString();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Closing a socket to a wedged renderer can itself fail; nothing to do about it.
        }
        _socket.Dispose();
    }
}
