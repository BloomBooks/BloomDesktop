using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// Asks Bloom's WebView2 whether it is still alive, and collects what it will tell us.
///
/// The headline question here is worth more than everything else in this section: **does the renderer
/// answer while the window is hung?** If it does, the freeze is in the .NET UI thread and the browser is
/// an innocent bystander. If it does not, the freeze is in JavaScript. Those are completely different
/// investigations, and knowing which one you are in before you start is the difference between an hour
/// and a day.
///
/// The port is discovered through <see cref="WebView2Processes"/> rather than assumed, because the
/// arithmetic differs by Bloom version — 6.4 and later use `httpPort + 2`, 6.3 hardcodes 9222.
/// </summary>
public sealed class WebViewCollector : IEvidenceCollector
{
    /// <summary>
    /// How long to give the renderer to answer a trivial question. Short: we are asking whether it is
    /// alive, and a renderer that needs five seconds to add two numbers has already answered "no".
    /// </summary>
    private static readonly TimeSpan ResponsivenessTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How long to listen for console and network events. CDP keeps no history, so this is a window into
    /// the future, not the past; it catches the errors a wedged or thrashing page is still producing.
    /// </summary>
    private static readonly TimeSpan ListenWindow = TimeSpan.FromSeconds(8);

    /// <inheritdoc />
    public string Title => "WebView2 (the browser inside Bloom)";

    /// <inheritdoc />
    public TimeSpan Budget => TimeSpan.FromSeconds(40);

    /// <inheritdoc />
    ///
    /// <remarks>
    /// **Only for a process that was still alive**, which is the same guard ManagedStacksCollector,
    /// ProcessEvidenceCollector and WaitChainCollector already carry - this one was the odd one out.
    ///
    /// A port number outlives the process that owned it. Connect to one after Bloom has gone and the
    /// answers come from whatever holds that port NOW: on a developer machine, quite possibly their own
    /// browser, whose page titles and URLs would then be quoted onto a Bloom card. Worse, the reply reads
    /// as evidence - "WebView2 answers normally, so the block is in Bloom's .NET UI thread" - attributed
    /// to a process that had already exited.
    /// </remarks>
    public bool AppliesTo(GatherContext context) =>
        context.ProcessWasAlive && context.CdpPort.HasValue;

    /// <inheritdoc />
    public async Task<ReportSection> CollectAsync(
        GatherContext context,
        CancellationToken cancellation
    )
    {
        var timer = Stopwatch.StartNew();
        var port = context.CdpPort!.Value;
        var text = new StringBuilder();

        text.AppendLine($"Debugging port {port}.");
        text.AppendLine();
        text.AppendLine(
            "> **Read this section as being about one WebView2, not about all of Bloom's.** Bloom gives "
                + "each WebView2 its own user data folder, so each runs in its own browser process, and it "
                + "passes them all the same debugging port — only one can bind it, and which one is a race. "
                + "So a healthy answer here does not clear every view, and the view we reached may not be "
                + "the interesting one."
        );
        var version = await CdpClient
            .ReadBrowserVersionAsync(port, TimeSpan.FromSeconds(5), cancellation)
            .ConfigureAwait(false);
        text.AppendLine(
            version == null
                ? "The browser endpoint did not answer, which suggests the whole WebView2 is gone or wedged."
                : $"Browser: {version}."
        );
        text.AppendLine();

        List<CdpTarget> targets;
        try
        {
            targets = await CdpClient
                .ListTargetsAsync(port, TimeSpan.FromSeconds(5), cancellation)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            return ReportSection.Failed(
                Title,
                $"could not list debug targets on port {port}: {e.GetType().Name}",
                timer.Elapsed
            );
        }

        var pages = targets.Where(t => t.Type == "page" && t.WebSocketUrl != null).ToList();
        text.AppendLine($"**{targets.Count} debug target(s), {pages.Count} of them pages**");
        text.AppendLine();
        foreach (var target in targets.Take(20))
            text.AppendLine($"- {target.Type}: \"{target.Title}\" — `{Shorten(target.Url)}`");
        text.AppendLine();

        var headline = await ProbePagesAsync(
                context.IsAboutTheUiBeingStuck,
                text,
                pages,
                cancellation
            )
            .ConfigureAwait(false);

        return new ReportSection
        {
            Title = Title,
            Body = text.ToString(),
            Duration = timer.Elapsed,
            Headline = headline,
        };
    }

    /// <summary>
    /// Asks each page to evaluate a trivial expression, then listens on the first one that answers.
    /// Returns the headline for the report's summary.
    /// </summary>
    /// <param name="theUiMightBeStuck">
    /// Whether this report is about a UI thread suspected of being stuck, which decides whether a healthy
    /// browser lets us CONCLUDE anything or is merely an observation. See GatherContext.IsAboutTheUiBeingStuck.
    /// </param>
    private static async Task<string?> ProbePagesAsync(
        bool theUiMightBeStuck,
        StringBuilder text,
        List<CdpTarget> pages,
        CancellationToken cancellation
    )
    {
        if (pages.Count == 0)
        {
            text.AppendLine("No page targets to question.");
            return null;
        }

        text.AppendLine("**Is the renderer still answering?**");
        text.AppendLine();

        CdpTarget? firstResponsive = null;
        var anyUnresponsive = false;

        foreach (var page in pages.Take(4))
        {
            cancellation.ThrowIfCancellationRequested();
            var (answered, elapsed) = await AskTrivialQuestionAsync(page, cancellation)
                .ConfigureAwait(false);
            text.AppendLine(
                answered
                    ? $"- \"{page.Title}\" answered in {elapsed.TotalMilliseconds:F0} ms"
                    : $"- **\"{page.Title}\" did NOT answer within {ResponsivenessTimeout.TotalSeconds:F0}s**"
            );
            if (answered)
                firstResponsive ??= page;
            else
                anyUnresponsive = true;
        }
        text.AppendLine();

        string? headline;
        if (anyUnresponsive && firstResponsive == null)
        {
            text.AppendLine(
                "> **No page answered.** The freeze is inside the browser — most likely JavaScript stuck "
                    + "in a loop or a synchronous call — rather than in Bloom's .NET UI thread. Look at the "
                    + "renderer's CPU in the process section above: a renderer using a whole core confirms "
                    + "it."
            );
            headline =
                "No WebView2 page answered, so the freeze is in the browser rather than in .NET.";
        }
        else if (anyUnresponsive)
        {
            text.AppendLine(
                "> Some pages answered and some did not, so one view is wedged while the rest are fine."
            );
            headline = "One WebView2 page is unresponsive while others answer.";
        }
        else
        {
            text.AppendLine(
                "> **Every page answered promptly.** The browser is healthy, so a frozen Bloom is frozen "
                    + "in its own .NET UI thread and the managed stacks are where the answer is."
            );
            // The conclusion only follows if something IS blocked. On a zombie report - Bloom alive,
            // pumping, and window-less - "the block is in Bloom's .NET UI thread" asserts a block that does
            // not exist and sends the reader after a deadlock instead of a missing window.
            headline = theUiMightBeStuck
                ? "WebView2 answers normally, so the block is in Bloom's .NET UI thread, not the browser."
                : "WebView2 answers normally.";
        }

        if (firstResponsive != null)
            await ListenForTroubleAsync(text, firstResponsive, cancellation).ConfigureAwait(false);

        return headline;
    }

    private static async Task<(bool Answered, TimeSpan Elapsed)> AskTrivialQuestionAsync(
        CdpTarget page,
        CancellationToken cancellation
    )
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await using var client = new CdpClient();
            await client.ConnectAsync(page.WebSocketUrl!, cancellation).ConfigureAwait(false);
            var reply = await client
                .SendAsync(
                    "Runtime.evaluate",
                    new JsonObject { ["expression"] = "1+1", ["returnByValue"] = true },
                    ResponsivenessTimeout,
                    cancellation
                )
                .ConfigureAwait(false);
            return (reply != null, timer.Elapsed);
        }
        catch (Exception)
        {
            return (false, timer.Elapsed);
        }
    }

    /// <summary>
    /// Turns on the console and network domains and listens briefly, so the report can carry whatever
    /// the page is complaining about right now — failed requests especially, since a Bloom waiting on a
    /// request that will never complete is a freeze with an obvious cause once you can see it.
    /// </summary>
    private static async Task ListenForTroubleAsync(
        StringBuilder text,
        CdpTarget page,
        CancellationToken cancellation
    )
    {
        text.AppendLine();
        text.AppendLine(
            $"**Console and network, watched for {ListenWindow.TotalSeconds:F0}s on \"{page.Title}\"**"
        );
        text.AppendLine();
        text.AppendLine(
            "*(CDP keeps no history, so this is only what happened from the moment we attached. Earlier "
                + "errors are not recoverable this way.)*"
        );
        text.AppendLine();

        try
        {
            await using var client = new CdpClient();
            await client.ConnectAsync(page.WebSocketUrl!, cancellation).ConfigureAwait(false);
            var quick = TimeSpan.FromSeconds(3);
            await client
                .SendAsync("Runtime.enable", null, quick, cancellation)
                .ConfigureAwait(false);
            await client.SendAsync("Log.enable", null, quick, cancellation).ConfigureAwait(false);
            await client
                .SendAsync("Network.enable", null, quick, cancellation)
                .ConfigureAwait(false);
            await client.ListenAsync(ListenWindow, cancellation).ConfigureAwait(false);

            var interesting = 0;
            foreach (var evt in client.Events)
            {
                var method = evt["method"]?.GetValue<string>() ?? "";
                var line = Summarise(method, evt);
                if (line == null)
                    continue;
                text.AppendLine("- " + line);
                if (++interesting >= 40)
                {
                    text.AppendLine("- *(more events followed; truncated)*");
                    break;
                }
            }
            if (interesting == 0)
                text.AppendLine("Nothing was logged and no request failed while we watched.");
        }
        catch (Exception e)
        {
            text.AppendLine($"*(could not listen: {e.GetType().Name})*");
        }
        text.AppendLine();
    }

    /// <summary>
    /// Renders the few event kinds worth putting on a card, and ignores the rest — a page load produces
    /// hundreds of events and almost none of them mean anything to a person reading a bug report.
    /// </summary>
    private static string? Summarise(string method, JsonNode evt)
    {
        var parameters = evt["params"];
        switch (method)
        {
            case "Runtime.consoleAPICalled":
            {
                var type = parameters?["type"]?.GetValue<string>() ?? "log";
                if (type is not ("error" or "warning" or "assert"))
                    return null;
                var args = parameters?["args"] as JsonArray;
                var first = args?.FirstOrDefault()?["value"]?.ToString() ?? "(object)";
                return $"console.{type}: {Shorten(first, 200)}";
            }
            case "Log.entryAdded":
            {
                var level = parameters?["entry"]?["level"]?.GetValue<string>() ?? "";
                if (level is not ("error" or "warning"))
                    return null;
                return $"log {level}: {Shorten(parameters?["entry"]?["text"]?.GetValue<string>() ?? "", 200)}";
            }
            case "Runtime.exceptionThrown":
                return "uncaught exception: "
                    + Shorten(
                        parameters?["exceptionDetails"]?["text"]?.GetValue<string>() ?? "(no text)",
                        200
                    );
            case "Network.loadingFailed":
                return $"request FAILED: {parameters?["errorText"]?.GetValue<string>()} "
                    + $"({parameters?["type"]?.GetValue<string>()})";
            default:
                return null;
        }
    }

    private static string Shorten(string value, int max = 120) =>
        value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
