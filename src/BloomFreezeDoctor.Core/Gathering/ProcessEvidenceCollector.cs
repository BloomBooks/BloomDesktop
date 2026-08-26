using System.Diagnostics;
using System.Text;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// The cheap, read-only facts about the process that a human triaging a freeze wants next after the
/// stacks: is it spinning or blocked, what windows does it have, and is anything unexpected loaded
/// into it.
///
/// All of this was validated in the Phase 0 spike against real Blooms, needs no privilege beyond
/// same-user, and cannot perturb the target.
/// </summary>
public sealed class ProcessEvidenceCollector : IEvidenceCollector
{
    /// <summary>
    /// How long to sample CPU over. This is the measurement that separates a spin loop from a
    /// deadlock, and it needs a real interval to mean anything.
    /// </summary>
    private static readonly TimeSpan CpuSampleWindow = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public string Title => "Process state";

    /// <inheritdoc />
    public TimeSpan Budget => TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public bool AppliesTo(GatherContext context) => context.ProcessWasAlive;

    /// <inheritdoc />
    public async Task<ReportSection> CollectAsync(
        GatherContext context,
        CancellationToken cancellation
    )
    {
        var started = Stopwatch.StartNew();
        Process process;
        try
        {
            process = Process.GetProcessById(context.Target.ProcessId);
        }
        catch (ArgumentException)
        {
            return ReportSection.Failed(
                Title,
                "the process had already exited when we came to look at it",
                started.Elapsed
            );
        }

        var text = new StringBuilder();
        string? headline = null;

        AppendBasics(text, process);
        AppendRespondingCaveat(text, process, context);
        var cpu = await SampleCpuAsync(process, cancellation).ConfigureAwait(false);
        headline = AppendCpu(text, cpu, context.IsAboutAFreeze);
        AppendWindows(text, process);
        AppendWebViewChildren(text, context.Target.ProcessId);
        AppendUnexplainedModules(text, process);

        return new ReportSection
        {
            Title = Title,
            Body = text.ToString(),
            Duration = started.Elapsed,
            Headline = headline,
        };
    }

    private static void AppendBasics(StringBuilder text, Process process)
    {
        text.AppendLine("| | |");
        text.AppendLine("| --- | --- |");
        Row(text, "Process id", process.Id.ToString());
        TryRow(text, "Threads", () => process.Threads.Count.ToString());
        TryRow(text, "Handles", () => process.HandleCount.ToString("N0"));
        TryRow(text, "Working set", () => $"{process.WorkingSet64 / 1024.0 / 1024.0:F0} MB");
        TryRow(
            text,
            "Private bytes",
            () => $"{process.PrivateMemorySize64 / 1024.0 / 1024.0:F0} MB"
        );
        TryRow(
            text,
            "Total CPU used",
            () => $"{process.TotalProcessorTime.TotalSeconds:F0} s since launch"
        );
        TryRow(text, "Responding (as .NET sees it)", () => process.Responding.ToString());
        text.AppendLine();
    }

    /// <summary>
    /// Explains the most confusing line in the report before anyone has to puzzle over it. When
    /// Windows says the window is responsive but we are reporting a freeze, that contradiction is not
    /// noise — it is the fingerprint of a UI thread blocked in an STA managed wait, which the spike
    /// measured as completely invisible from outside. Naming the class saves the reader the trip.
    /// </summary>
    private static void AppendRespondingCaveat(
        StringBuilder text,
        Process process,
        GatherContext context
    )
    {
        bool windowsSaysResponsive;
        try
        {
            windowsSaysResponsive = process.Responding;
        }
        catch (Exception)
        {
            return;
        }

        var weSayFrozen =
            context.Verdict.Report is ReportReason.Frozen or ReportReason.RecoveredFromFreeze;
        if (!windowsSaysResponsive || !weSayFrozen)
            return;

        text.AppendLine(
            "> **Windows reports this window as responsive, and it is not.** That combination is the "
                + "signature of a UI thread blocked in a managed wait on an STA thread: "
                + "`CoWaitForMultipleHandles` keeps dispatching *sent* messages so the window answers "
                + "probes, while the application is entirely stuck. No outside probe can detect this; "
                + "it was caught by a signal that does not depend on the message queue. The managed "
                + "stack above is the authority on what is actually happening."
        );
        text.AppendLine();
    }

    /// <summary>
    /// Samples every thread's processor time twice, so the report can say whether anything is actually
    /// burning CPU. This is the difference between "spinning in a loop" and "deadlocked", and no
    /// amount of stack reading distinguishes them as clearly.
    /// </summary>
    private static async Task<Dictionary<int, TimeSpan>> SampleCpuAsync(
        Process process,
        CancellationToken cancellation
    )
    {
        var before = SnapshotThreadTimes(process);
        try
        {
            await Task.Delay(CpuSampleWindow, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Report whatever we can from a single sample rather than nothing.
        }
        try
        {
            process.Refresh();
        }
        catch (Exception) { }
        var after = SnapshotThreadTimes(process);

        var deltas = new Dictionary<int, TimeSpan>();
        foreach (var (id, then) in after)
        {
            if (before.TryGetValue(id, out var was) && then > was)
                deltas[id] = then - was;
        }
        return deltas;
    }

    private static string? AppendCpu(
        StringBuilder text,
        Dictionary<int, TimeSpan> deltas,
        bool isAboutAFreeze
    )
    {
        text.AppendLine($"**CPU used per thread over {CpuSampleWindow.TotalSeconds:F0} seconds**");
        text.AppendLine();
        var busy = deltas
            .Where(d => d.Value > TimeSpan.FromMilliseconds(50))
            .OrderByDescending(d => d.Value)
            .ToList();

        if (busy.Count == 0)
        {
            // The same observation, but only a freeze makes it a deduction. A crashing or exiting Bloom
            // has nothing to spin, so "this is a wait rather than a spin" would be arguing with the
            // report's own headline. See GatherContext.IsAboutAFreeze.
            if (isAboutAFreeze)
            {
                text.AppendLine(
                    "No thread used measurable CPU. That is consistent with a deadlock or a thread waiting "
                        + "on something, and rules out a spin loop."
                );
                text.AppendLine();
                return "No thread is burning CPU, so this is a wait rather than a spin.";
            }
            text.AppendLine("No thread used measurable CPU during the sample.");
            text.AppendLine();
            return null;
        }

        foreach (var (id, delta) in busy.Take(10))
            text.AppendLine(
                $"- thread {id}: {delta.TotalMilliseconds:F0} ms "
                    + $"({delta.TotalMilliseconds / CpuSampleWindow.TotalMilliseconds:P0} of one core)"
            );
        text.AppendLine();

        var hottest = busy[0];
        var share = hottest.Value.TotalMilliseconds / CpuSampleWindow.TotalMilliseconds;
        return share > 0.8
            ? $"Thread {hottest.Key} is using a whole core, so this looks like a spin rather than a deadlock."
            : null;
    }

    /// <summary>
    /// Lists the process's top-level windows. Two things to look for: a visible-but-disabled main
    /// window means a modal dialog is up somewhere, and an off-screen or hidden dialog is a Bloom bug
    /// in its own right.
    ///
    /// Note that a healthy Bloom always has an invisible window of its own — its splash screen is
    /// hidden rather than closed — so an invisible window here is normal and not evidence of anything.
    /// </summary>
    private static void AppendWindows(StringBuilder text, Process process)
    {
        text.AppendLine("**Top-level windows**");
        text.AppendLine();
        try
        {
            var probe = new WindowsTargetProbe(process);
            var main = probe.FindMainWindow();
            var any = false;
            var noise = 0;
            foreach (var window in WindowsTargetProbe.EnumerateTopLevel(process.Id))
            {
                // Every WinForms process carries a handful of infrastructure windows (input-method
                // helpers, the GDI+ hook, the broadcast-event sink). Listing them buries the one or two
                // windows that matter, so they are counted rather than named.
                if (window != main && IsInfrastructureWindow(WindowsTargetProbe.ClassOf(window)))
                {
                    noise++;
                    continue;
                }
                any = true;
                var flags = new List<string>();
                if (window == main)
                    flags.Add("**main**");
                if (!WindowsTargetProbe.IsVisible(window))
                    flags.Add("hidden");
                if (!WindowsTargetProbe.IsEnabled(window))
                    flags.Add("disabled");
                text.AppendLine(
                    $"- `{WindowsTargetProbe.ClassOf(window)}` \"{WindowsTargetProbe.TitleOf(window)}\""
                        + (flags.Count > 0 ? " — " + string.Join(", ", flags) : "")
                );
            }
            if (!any)
                text.AppendLine(
                    "None of consequence. For a Bloom that should be showing a UI, this is what state 3 "
                        + "looks like — note that a healthy Bloom keeps an invisible window all session "
                        + "(its splash screen is hidden rather than closed), so the absence of a *visible* "
                        + "window is the signal, not the absence of windows."
                );
            if (noise > 0)
                text.AppendLine(
                    $"\n({noise} infrastructure window(s) omitted: input-method helpers and the like.)"
                );
            if (main != IntPtr.Zero && !WindowsTargetProbe.IsEnabled(main))
                text.AppendLine();
            if (main != IntPtr.Zero && !WindowsTargetProbe.IsEnabled(main))
                text.AppendLine(
                    "The main window is **disabled**, which means a modal dialog is up. If none is "
                        + "visible above, it is off-screen or behind the main window — itself a bug."
                );
        }
        catch (Exception e)
        {
            text.AppendLine($"(could not enumerate windows: {e.GetType().Name})");
        }
        text.AppendLine();
    }

    /// <summary>
    /// Window classes every WinForms process has and nobody needs to read about. Kept as a list rather
    /// than a regex so it is obvious what is being hidden.
    /// </summary>
    private static bool IsInfrastructureWindow(string className) =>
        className is "IME" or "MSCTFIME UI" or "GDI+ Hook Window Class"
        || className.StartsWith(".NET-BroadcastEventWindow", StringComparison.Ordinal);

    /// <summary>
    /// Bloom's WebView2 children, with their CPU. A renderer using a whole core while Bloom's own
    /// threads are idle means the freeze is in JavaScript, not in .NET — a completely different
    /// investigation, and worth knowing before anyone starts.
    /// </summary>
    private static void AppendWebViewChildren(StringBuilder text, int parentId)
    {
        text.AppendLine("**WebView2 child processes**");
        text.AppendLine();
        try
        {
            var children = WebView2Processes.FindChildrenOf(parentId);
            if (children.Count == 0)
            {
                text.AppendLine("None found.");
                text.AppendLine();
                return;
            }
            foreach (var child in children)
            {
                var cpu = "?";
                var memory = "?";
                try
                {
                    using var process = Process.GetProcessById(child.ProcessId);
                    cpu = $"{process.TotalProcessorTime.TotalSeconds:F0} s";
                    memory = $"{process.WorkingSet64 / 1024.0 / 1024.0:F0} MB";
                }
                catch (Exception) { }
                text.AppendLine(
                    $"- pid {child.ProcessId} ({child.Kind}): {cpu} CPU total, {memory} working set"
                );
            }
        }
        catch (Exception e)
        {
            text.AppendLine($"(could not enumerate WebView2 children: {e.GetType().Name})");
        }
        text.AppendLine();
    }

    /// <summary>
    /// Lists modules loaded into Bloom that are not Windows', Bloom's own, or the .NET runtime's.
    /// Antivirus and shell-extension DLLs inject themselves into processes and are a genuine cause of
    /// hangs, so an unexpected name here can be the whole answer.
    /// </summary>
    private static void AppendUnexplainedModules(StringBuilder text, Process process)
    {
        text.AppendLine("**Modules loaded from unexpected places**");
        text.AppendLine();
        try
        {
            var appFolder = Path.GetDirectoryName(process.MainModule?.FileName ?? "") ?? "";
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var unexplained = new List<string>();
            var total = 0;
            foreach (ProcessModule module in process.Modules)
            {
                total++;
                var file = module.FileName ?? "";
                if (
                    file.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
                    || (
                        appFolder.Length > 0
                        && file.StartsWith(appFolder, StringComparison.OrdinalIgnoreCase)
                    )
                    || file.Contains(@"\Program Files\dotnet\", StringComparison.OrdinalIgnoreCase)
                    || file.Contains(@"\Microsoft\EdgeWebView\", StringComparison.OrdinalIgnoreCase)
                    || file.Contains(@"\Microsoft\Edge", StringComparison.OrdinalIgnoreCase)
                )
                    continue;
                unexplained.Add(file);
            }
            if (unexplained.Count == 0)
                text.AppendLine($"None ({total} modules loaded, all from expected locations).");
            else
            {
                text.AppendLine(
                    $"{unexplained.Count} of {total} modules came from elsewhere. Third-party code "
                        + "injected into Bloom is a known cause of hangs:"
                );
                foreach (var file in unexplained.Take(40))
                    text.AppendLine($"- `{file}`");
            }
        }
        catch (Exception e)
        {
            text.AppendLine($"(could not enumerate modules: {e.GetType().Name})");
        }
        text.AppendLine();
    }

    private static Dictionary<int, TimeSpan> SnapshotThreadTimes(Process process)
    {
        var times = new Dictionary<int, TimeSpan>();
        try
        {
            foreach (ProcessThread thread in process.Threads)
            {
                try
                {
                    times[thread.Id] = thread.TotalProcessorTime;
                }
                catch (Exception)
                {
                    // A thread can exit between enumeration and the read.
                }
            }
        }
        catch (Exception) { }
        return times;
    }

    private static void Row(StringBuilder text, string name, string value) =>
        text.AppendLine($"| {name} | {value} |");

    private static void TryRow(StringBuilder text, string name, Func<string> read)
    {
        try
        {
            Row(text, name, read());
        }
        catch (Exception)
        {
            Row(text, name, "(unavailable)");
        }
    }
}
