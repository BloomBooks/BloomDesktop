using BloomFreezeDoctor.Gathering;
using SIL.IO;

namespace BloomFreezeDoctor;

/// <summary>
/// A dump of one Bloom taken when it first looks frozen, kept in case it is the only one we ever get.
///
/// The Doctor reports a freeze, and so takes its dump, only once the UI has been stuck for a minute. People
/// do not wait that long: somewhere between thirty seconds and a minute they reach for Task Manager, and
/// then the report is gathered after the process has gone, when no dump can be taken. That is how a real
/// minute-long freeze (BL-17092) produced a card with no dump at all, and so no way to tell what Bloom was
/// stuck in. So when a watched Bloom becomes <see cref="TargetState.Suspect"/> we dump it at once and hold
/// the file. A report gathered later about the same Bloom claims it, and uses it if it cannot take a fresh
/// one; if Bloom starts responding again before anything is reported, the dump is thrown away.
///
/// One dump per episode: staying Suspect, or going on to Frozen, takes no more. Taking it costs the target
/// a second or two of being suspended while its runtime writes the file, which a Bloom that has not
/// answered for twenty seconds will not notice.
/// </summary>
public sealed class EarlyFreezeDump : IDisposable
{
    private readonly string _directoryRoot;
    private readonly Action<int, string> _writeDump;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<Func<Taken?>, Task<Taken?>> _inBackground;
    private readonly object _lock = new();

    /// <summary>
    /// The dump for the current episode, being taken or taken, while this still owns it; null when there is
    /// none or a report has claimed it.
    /// </summary>
    private Task<Taken?>? _current;

    /// <summary>
    /// Whether this episode has had its dump, whoever owns it now. Separate from <see cref="_current"/> so
    /// that a report claiming the dump while Bloom is still Suspect - "Report now" - does not lead to
    /// another one on the next tick.
    /// </summary>
    private bool _dumpedThisEpisode;

    /// <summary>
    /// What a successful early dump left behind: the file, and when it was written, so a report can say how
    /// long before it was gathered the dump was taken.
    /// </summary>
    public sealed record Taken(string DumpPath, DateTimeOffset TakenAtUtc);

    /// <summary>The Bloom this dumps.</summary>
    public int ProcessId { get; }

    /// <param name="processId">The Bloom to dump.</param>
    /// <param name="directoryRoot">
    /// Where to put the dump. Each one goes in a folder of its own beneath this, which is deleted with it.
    /// </param>
    /// <param name="writeDump">Writes a dump of the process (first argument) to the path (second). May throw.</param>
    /// <param name="now">The clock, for tests.</param>
    /// <param name="inBackground">
    /// Runs the slow file work - writing a dump, and deleting one - off the calling thread, which is the
    /// watcher's timer. A test passes one that runs it at once, so that what follows is deterministic.
    /// </param>
    public EarlyFreezeDump(
        int processId,
        string directoryRoot,
        Action<int, string> writeDump,
        Func<DateTimeOffset>? now = null,
        Func<Func<Taken?>, Task<Taken?>>? inBackground = null
    )
    {
        ProcessId = processId;
        _directoryRoot = directoryRoot;
        _writeDump = writeDump;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _inBackground = inBackground ?? (work => Task.Run(work));
    }

    /// <summary>
    /// Tells this what the detector now thinks of the Bloom. Becoming Suspect starts a dump if this episode
    /// has none; becoming Healthy throws the episode's dump away. Returns at once.
    /// </summary>
    public void Observe(TargetState state)
    {
        lock (_lock)
        {
            if (state == TargetState.Suspect && !_dumpedThisEpisode)
            {
                _dumpedThisEpisode = true;
                _current = _inBackground(Take);
            }
            else if (state == TargetState.Healthy)
            {
                _dumpedThisEpisode = false;
                if (_current != null)
                    DeleteWhenDone(_current);
                _current = null;
            }
        }
    }

    /// <summary>
    /// Hands the episode's dump to a report that is being gathered. Null at once if this episode has none;
    /// otherwise the dump, which may still be being written, and gives null if writing it failed.
    ///
    /// The dump is the caller's from now on. It need only await the task if it turns out to need the dump,
    /// and how long to wait for one still being written is its call, made when it knows it needs it. Once
    /// it has moved what it wants out, it must pass the task to <see cref="DeleteWhenWritten"/>.
    /// </summary>
    public Task<Taken?>? Claim()
    {
        lock (_lock)
        {
            var claimed = _current;
            _current = null;
            return claimed;
        }
    }

    /// <summary>
    /// Deletes a claimed dump's folder, with anything still in it, once the dump has been written: at once
    /// if it has, otherwise when it is. Never throws. Returns without waiting; the task it returns is the
    /// delete, which nothing need wait for.
    /// </summary>
    public static Task DeleteWhenWritten(Task<Taken?> claimed) =>
        claimed.ContinueWith(
            t =>
            {
                if (t.Result != null)
                    DeleteFolderOf(t.Result.DumpPath);
            },
            TaskScheduler.Default
        );

    /// <summary>Throws away whatever dump this is holding.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_current != null)
                DeleteWhenDone(_current);
            _current = null;
        }
    }

    private Taken? Take()
    {
        var path = Path.Combine(
            _directoryRoot,
            $"early-{ProcessId}-{Guid.NewGuid():N}",
            ManagedStacksCollector.DumpFileName(ProcessId)
        );
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _writeDump(ProcessId, path);
            return new Taken(path, _now());
        }
        catch (Exception)
        {
            // Most likely the process went, or its diagnostics pipe is not answering. A report gathered
            // later will try for itself; all this loses is the head start.
            DeleteFolderOf(path);
            return null;
        }
    }

    private static void DeleteFolderOf(string dumpPath)
    {
        try
        {
            var folder = Path.GetDirectoryName(dumpPath);
            if (folder != null && Directory.Exists(folder))
                RobustIO.DeleteDirectoryAndContents(folder);
        }
        catch (Exception)
        {
            // It is in the temp folder; leaving it behind costs some disk space and nothing else.
        }
    }

    /// <summary>
    /// Deletes a dump once it has been written, without waiting for that here. Callers hold locks, and the
    /// delete itself can be slow: a virus scanner is quite likely to be reading a file just written.
    /// </summary>
    private void DeleteWhenDone(Task<Taken?> dump)
    {
        // Through _inBackground when it is already written, which is the usual case, so that a test sees the
        // delete happen.
        if (!dump.IsCompleted)
            DeleteWhenWritten(dump);
        else if (dump.Result != null)
            _inBackground(() =>
            {
                DeleteFolderOf(dump.Result.DumpPath);
                return null;
            });
    }
}
