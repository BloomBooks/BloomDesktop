using System.Diagnostics;
using BloomFreezeDoctor.Outbox;
using Velopack;

namespace BloomFreezeDoctor;

/// <summary>
/// The Doctor's entry point, including the rendezvous that lets Bloom launch it without a handshake.
/// </summary>
internal static class Program
{
    /// <summary>
    /// The name of the mutex that decides which instance is "the" Doctor. Deliberately in the `Local\`
    /// namespace rather than `Global\`: a Doctor cannot open processes in another Windows session anyway,
    /// so a machine-wide singleton would be wrong across fast user switching and RDP.
    /// </summary>
    private const string SingletonMutexName = @"Local\BloomFreezeDoctor-singleton";

    [STAThread]
    private static int Main(string[] args)
    {
        // FIRST, before anything else whatsoever. Velopack runs this same exe with its own hook
        // arguments at install, update and uninstall time, and Run() is what recognizes those, does the
        // hook's work, and exits the process. Everything below would get in the way of that: the
        // command-line parser does not know Velopack's switches, the queue-inspection switches return
        // early, and the singleton mutex would make an install hook exit as "another Doctor is already
        // running" during an update - when the Doctor genuinely IS running and is being replaced.
        //
        // `vpk pack` refuses to build an installer at all if it cannot find this call, which is how its
        // absence surfaced.
        VelopackApp.Build().Run();

        var options = CommandLineOptions.Parse(args);

        // The queue-inspection switches are for support and for testing, and must work whether or not
        // another Doctor is running, so they are handled before the singleton dance.
        if (options.ListQueue)
            return ListQueue(options);
        if (options.DrainOnly)
            return Drain(options);

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // The rendezvous, and the reason there is no handshake protocol: whoever holds this mutex is the
        // Doctor. Bloom's job is only to make sure *a* Doctor is running; if one already is, this instance
        // exits and the existing one adopts the new Bloom through its own discovery sweep.
        //
        // We WAIT briefly rather than peeking, and treat an abandoned mutex as ours, so we do not lose the
        // race against an owner that is in the middle of dying.
        using var singleton = new Mutex(initiallyOwned: false, SingletonMutexName, out _);
        var owned = false;
        try
        {
            owned = singleton.WaitOne(TimeSpan.FromSeconds(2));
        }
        catch (AbandonedMutexException)
        {
            // The previous Doctor died holding it. That makes us the Doctor.
            owned = true;
        }

        if (!owned)
        {
            // Another Doctor is already watching. Nothing to do — and this is the normal path every time
            // Bloom starts while the Doctor is already running.
            return 0;
        }

        try
        {
            return Run(options);
        }
        finally
        {
            try
            {
                singleton.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned any more; nothing to release.
            }
        }
    }

    private static int Run(CommandLineOptions options)
    {
        using var supervisor = new DoctorSupervisor(
            options.Project,
            options.TargetProcessName,
            options.ForceFiling
        );

        // Launched by Bloom, we start out of the way in the tray; launched by a person, we show ourselves,
        // because they went looking for us and want to see something.
        using var window = new StatusForm(
            supervisor,
            startMinimised: options.AdoptProcessId.HasValue
        );

        supervisor.NothingLeftToDo += (_, _) =>
        {
            try
            {
                window.BeginInvoke(() => Application.Exit());
            }
            catch (Exception)
            {
                Application.Exit();
            }
        };

        if (options.AdoptProcessId.HasValue)
        {
            supervisor.Adopt(options.AdoptProcessId.Value);
            var facts = Gathering.GatherContextBuilder.DescribeRunningProcess(
                options.AdoptProcessId.Value
            );
            if (facts != null)
                window.RememberBloomPath(facts.ExePath);
        }

        supervisor.Start();

        if (options.ReportNowProcessId.HasValue)
        {
            // Deliberate one-shot: gather, file, and let the window show the result.
            _ = Task.Run(() =>
                supervisor.ReportNowAsync(options.ReportNowProcessId.Value, CancellationToken.None)
            );
        }

        Application.Run(window);
        return 0;
    }

    /// <summary>
    /// Prints the queue, so support can see at a glance whether a user's reports are stuck and why. Text
    /// output on a WinExe needs a console attached, which is why this bothers.
    /// </summary>
    private static int ListQueue(CommandLineOptions options)
    {
        AttachConsole(-1);
        var outbox = new ReportOutbox();
        var bundles = outbox.List();
        Console.WriteLine($"Outbox: {outbox.Root}");
        if (bundles.Count == 0)
        {
            Console.WriteLine("  (empty)");
            return 0;
        }
        foreach (var bundle in bundles)
        {
            var m = bundle.Metadata;
            Console.WriteLine(
                $"  {m.GatheredAtUtc:yyyy-MM-dd HH:mm}Z  {m.State, -18} {m.Fingerprint}  "
                    + $"occurrences={m.Occurrences} attempts={m.AttemptCount} {m.IssueId ?? ""}"
            );
            Console.WriteLine($"      {m.Summary}");
            if (m.LastError != null)
                Console.WriteLine($"      last error: {m.LastError}");
        }
        return 0;
    }

    private static int Drain(CommandLineOptions options)
    {
        AttachConsole(-1);
        var outbox = new ReportOutbox();
        var pending = outbox.Pending().Count;
        Console.WriteLine($"{pending} report(s) pending in {outbox.Root}");
        if (pending == 0)
            return 0;
        var filed = outbox.DrainAsync(new YouTrackSubmitter()).GetAwaiter().GetResult();
        Console.WriteLine($"filed {filed}");
        foreach (var bundle in outbox.List())
            Console.WriteLine(
                $"  {bundle.Metadata.State, -18} {bundle.Metadata.IssueId ?? bundle.Metadata.LastError ?? ""}"
            );
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}

/// <summary>The Doctor's command line. Small on purpose; the window is the interface.</summary>
internal sealed record CommandLineOptions
{
    /// <summary>`--adopt &lt;pid&gt;`: Bloom telling us which process it is. Also means "start minimised".</summary>
    public int? AdoptProcessId { get; init; }

    /// <summary>`--report-now &lt;pid&gt;`: gather and file a report immediately, frozen or not.</summary>
    public int? ReportNowProcessId { get; init; }

    /// <summary>`--list-queue`: print the outbox and exit.</summary>
    public bool ListQueue { get; init; }

    /// <summary>`--drain`: try to send everything queued, then exit.</summary>
    public bool DrainOnly { get; init; }

    /// <summary>`--project X`: which tracker project to file into. `AUT` is the test project.</summary>
    public string Project { get; init; } = "BL";

    /// <summary>
    /// `--target-name X`: which process name to watch. Overridable so the freeze stub can stand in for
    /// Bloom while testing, which is the only way to exercise the whole app without breaking a real Bloom.
    /// </summary>
    public string TargetProcessName { get; init; } = "Bloom";

    /// <summary>
    /// `--force`: file even from a developer build. For deliberate end-to-end tests only — without it, a
    /// developer machine gathers to disk and never files, which is what keeps our own work off the tracker.
    /// </summary>
    public bool ForceFiling { get; init; }

    /// <summary>Parses the command line, ignoring anything it does not recognise.</summary>
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--adopt" when i + 1 < args.Length && int.TryParse(args[i + 1], out var adopt):
                    options = options with { AdoptProcessId = adopt };
                    i++;
                    break;
                case "--report-now"
                    when i + 1 < args.Length && int.TryParse(args[i + 1], out var now):
                    options = options with { ReportNowProcessId = now };
                    i++;
                    break;
                case "--list-queue":
                    options = options with { ListQueue = true };
                    break;
                case "--drain":
                    options = options with { DrainOnly = true };
                    break;
                case "--project" when i + 1 < args.Length:
                    options = options with { Project = args[i + 1] };
                    i++;
                    break;
                case "--target-name" when i + 1 < args.Length:
                    options = options with { TargetProcessName = args[i + 1] };
                    i++;
                    break;
                case "--force":
                    options = options with { ForceFiling = true };
                    break;
            }
        }
        return options;
    }
}
