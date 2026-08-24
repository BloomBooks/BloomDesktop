using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// The state of the machine around Bloom, and whether the network was working at the moment of the
/// freeze.
///
/// The network probe earns its place because of the instruction this whole project started from: a
/// freeze that a poor connection explains is not the bug we are hunting. BL-16697's own log shows DNS
/// failing minutes before the user gave up, so without this the reader of a card cannot tell the two
/// apart. Note it does not *suppress* anything — Bloom blocking its UI thread on a dead network is still
/// a bug worth fixing — it just puts the fact on the card.
/// </summary>
public sealed class SystemEvidenceCollector : IEvidenceCollector
{
    /// <inheritdoc />
    public string Title => "The machine, and the network";

    /// <inheritdoc />
    public TimeSpan Budget => TimeSpan.FromSeconds(25);

    /// <inheritdoc />
    public bool AppliesTo(GatherContext context) => true;

    /// <inheritdoc />
    public async Task<ReportSection> CollectAsync(
        GatherContext context,
        CancellationToken cancellation
    )
    {
        var timer = Stopwatch.StartNew();
        var text = new StringBuilder();

        AppendMachine(text);
        AppendDisks(text, context);
        AppendWebView2Version(text);
        AppendCloudSyncClients(text, context);
        var headline = await AppendNetworkAsync(text, cancellation).ConfigureAwait(false);

        return new ReportSection
        {
            Title = Title,
            Body = text.ToString(),
            Duration = timer.Elapsed,
            Headline = headline,
        };
    }

    private static void AppendMachine(StringBuilder text)
    {
        text.AppendLine("| | |");
        text.AppendLine("| --- | --- |");
        text.AppendLine($"| Windows | {Environment.OSVersion.Version} |");
        text.AppendLine($"| Processors | {Environment.ProcessorCount} |");
        text.AppendLine(
            $"| Uptime | {TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours:F1} hours |"
        );

        // Memory pressure is a real cause of apparent freezes: a machine that is paging heavily feels
        // identical to one that is deadlocked.
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            text.AppendLine(
                $"| RAM installed | {status.TotalPhysical / 1024.0 / 1024 / 1024:F1} GB |"
            );
            text.AppendLine(
                $"| RAM available | {status.AvailablePhysical / 1024.0 / 1024 / 1024:F1} GB "
                    + $"({100 - status.MemoryLoad}% free) |"
            );
            if (status.MemoryLoad >= 90)
                text.AppendLine(
                    "| **Memory pressure** | **high — a heavily paging machine feels exactly like a "
                        + "frozen one** |"
                );
        }
        text.AppendLine();
    }

    /// <summary>
    /// Free space on the drives Bloom actually uses. A full temp drive breaks Bloom in ways that look
    /// nothing like "the disk is full".
    /// </summary>
    private static void AppendDisks(StringBuilder text, GatherContext context)
    {
        text.AppendLine("**Disk space on the drives Bloom uses**");
        text.AppendLine();
        var roots = new[]
        {
            Path.GetPathRoot(Path.GetTempPath()),
            Path.GetPathRoot(context.Target.ExePath),
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        }
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            try
            {
                var drive = new DriveInfo(root!);
                var free = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                text.AppendLine(
                    $"- `{root}` — {free:F1} GB free of {drive.TotalSize / 1024.0 / 1024 / 1024:F0} GB"
                        + (free < 1 ? "  **← almost full**" : "")
                );
            }
            catch (Exception)
            {
                text.AppendLine($"- `{root}` — could not be read");
            }
        }
        text.AppendLine();
    }

    /// <summary>
    /// The WebView2 runtime version. Worth having because a WebView2 update is a plausible cause of a
    /// freeze that starts happening to many users at once, and it is the kind of thing nobody thinks to
    /// ask about.
    /// </summary>
    private static void AppendWebView2Version(StringBuilder text)
    {
        text.AppendLine("**WebView2 runtime**");
        text.AppendLine();
        var found = false;
        foreach (
            var key in new[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            }
        )
        {
            try
            {
                using var registry = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                var version = registry?.GetValue("pv") as string;
                if (string.IsNullOrEmpty(version))
                    continue;
                text.AppendLine($"- version {version}");
                found = true;
                break;
            }
            catch (Exception)
            {
                // Registry read failed; not worth a fuss.
            }
        }
        if (!found)
            text.AppendLine("- version could not be determined");
        text.AppendLine();
    }

    /// <summary>
    /// Whether a cloud-sync client is running, and whether Bloom's collections live inside a synced
    /// folder. Sync clients holding files open is a long-standing source of Bloom trouble, and it is
    /// invisible unless someone asks.
    /// </summary>
    private static void AppendCloudSyncClients(StringBuilder text, GatherContext context)
    {
        text.AppendLine("**Cloud sync**");
        text.AppendLine();
        string[] names = ["OneDrive", "Dropbox", "GoogleDriveFS", "igfxEM", "SyncBackPro"];
        var running = names.Where(n => Process.GetProcessesByName(n).Length > 0).ToList();
        text.AppendLine(
            running.Count == 0
                ? "- no known sync client is running"
                : "- running: " + string.Join(", ", running)
        );

        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var bloomFolder = Path.Combine(documents, "Bloom");
            var suspicious = new[] { "OneDrive", "Dropbox", "Google Drive" }.FirstOrDefault(
                marker => bloomFolder.Contains(marker, StringComparison.OrdinalIgnoreCase)
            );
            if (suspicious != null)
                text.AppendLine(
                    $"- **Bloom's collections folder sits inside {suspicious}** (`{bloomFolder}`). Note "
                        + "this is true of the *path* whether or not the sync client is running above: a "
                        + "redirected Documents folder keeps the path even when sync is idle. Sync clients "
                        + "holding book files open is a long-standing source of Bloom trouble, so it is "
                        + "worth ruling in or out."
                );
        }
        catch (Exception) { }
        text.AppendLine();
    }

    /// <summary>
    /// Was the network working at the moment of the freeze? Answers with a DNS lookup and an HTTP
    /// request to the host Bloom itself talks to, because "the internet was down" is the single most
    /// common innocent explanation and the card should say so rather than leave it to be guessed.
    /// </summary>
    private static async Task<string?> AppendNetworkAsync(
        StringBuilder text,
        CancellationToken cancellation
    )
    {
        text.AppendLine("**Network, as it was when this report was gathered**");
        text.AppendLine();

        var hasInterface = NetworkInterface.GetIsNetworkAvailable();
        text.AppendLine($"- a network interface is up: {hasInterface}");

        var dnsWorked = false;
        try
        {
            var timer = Stopwatch.StartNew();
            var addresses = await System
                .Net.Dns.GetHostAddressesAsync("bloomlibrary.org", cancellation)
                .ConfigureAwait(false);
            dnsWorked = addresses.Length > 0;
            text.AppendLine(
                $"- DNS for bloomlibrary.org resolved in {timer.ElapsedMilliseconds} ms ({addresses.Length} address(es))"
            );
        }
        catch (Exception e)
        {
            text.AppendLine(
                $"- **DNS for bloomlibrary.org FAILED**: {e.GetType().Name}: {e.Message}"
            );
        }

        var httpWorked = false;
        if (dnsWorked)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var timer = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(
                    HttpMethod.Head,
                    "https://bloomlibrary.org/"
                );
                using var response = await http.SendAsync(request, cancellation)
                    .ConfigureAwait(false);
                httpWorked = true;
                text.AppendLine(
                    $"- HTTPS to bloomlibrary.org answered {(int)response.StatusCode} in {timer.ElapsedMilliseconds} ms"
                );
            }
            catch (Exception e)
            {
                text.AppendLine($"- **HTTPS to bloomlibrary.org FAILED**: {e.GetType().Name}");
            }
        }
        text.AppendLine();

        if (!dnsWorked || !httpWorked)
        {
            text.AppendLine(
                "> The network was **not working** when this was gathered. That does not excuse the "
                    + "freeze — Bloom should not block its UI thread waiting on a dead network — but it "
                    + "does change what to look for: check the managed stacks for a thread inside a web "
                    + "request before assuming a deadlock."
            );
            text.AppendLine();
            return "The network was down when this was gathered; check the stacks for a blocked web request.";
        }
        return null;
    }

    #region interop

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    #endregion
}
