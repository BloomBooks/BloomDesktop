namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// Finding the UI thread among a process's threads, and saying honestly what its stack shows.
///
/// Both halves came out of running a simulated SPIN and reading what the Doctor produced. The report named
/// the thread burning a whole core, which was right and useful, and then said nothing whatever about where
/// it was spinning - which is the only thing a developer actually needs. Two separate faults:
///
/// - **The UI thread was not recognised.** It was found by looking for a "RunMessageLoop" frame, and on a
///   thread that is RUNNING rather than waiting the stack walk yields "(native)" where those frames should
///   be. So the report had no UI-thread section at all, for the one failure where the UI thread is the
///   whole story.
/// - **And naming it would have made things worse.** The old description fell through to the first frame
///   beginning "Bloom.", which on such a stack is <c>Bloom.Program.Run</c> - the bottom of every UI thread
///   there has ever been. The report would have announced "The UI thread is blocked in Bloom.Program.Run",
///   which is wrong twice over: it is not blocked, and that frame means nothing.
///
/// A stack that cannot be read is a fact worth reporting as one. The dump is attached; saying which thread
/// to open it at is worth more than a confident sentence about a frame that carries no information.
/// </summary>
public static class UiThreadStack
{
    /// <summary>The frames every UI thread ends with, which therefore identify nothing about a fault.</summary>
    private static readonly string[] TheBottomOfEveryUiThread =
    {
        "Bloom.Program.Run",
        "Bloom.Program.Main",
    };

    /// <summary>
    /// Frames that are an artefact of walking the stack rather than anything the program did. A stack made
    /// only of these has not been read, whatever its length suggests.
    /// </summary>
    public static bool IsPlumbing(string frame) =>
        frame.Length == 0
        || frame.StartsWith("(native)", StringComparison.Ordinal)
        || frame.StartsWith("InlinedCallFrame", StringComparison.Ordinal)
        || frame.Contains("IL_STUB", StringComparison.Ordinal)
        || frame.StartsWith("DebuggerU2MCatchHandlerFrame", StringComparison.Ordinal);

    /// <summary>
    /// Whether this looks like the thread running the message loop.
    ///
    /// The message-loop frame is the good answer and is used first. The fallback exists because that frame
    /// disappears exactly when it is most needed: a spinning thread's upper frames do not survive the walk.
    /// Every Bloom UI thread bottoms out in <c>Bloom.Program.Main</c>, and no other thread does, so it
    /// identifies the thread even when nothing above it can be read.
    /// </summary>
    public static bool LooksLikeTheUiThread(IReadOnlyList<string> frames) =>
        frames.Any(f => f.Contains("RunMessageLoop", StringComparison.Ordinal))
        || frames.Any(f => f.Contains("Bloom.Program.Main", StringComparison.Ordinal));

    /// <summary>
    /// Whether this stack tells us anything at all about what the thread was doing - that is, whether it
    /// has any frame that is neither walking plumbing nor the bottom every UI thread shares.
    /// </summary>
    public static bool SaysAnythingUseful(IReadOnlyList<string> frames) =>
        frames.Any(f =>
            !IsPlumbing(f)
            && !TheBottomOfEveryUiThread.Any(bottom => f.Contains(bottom, StringComparison.Ordinal))
        );

    /// <summary>
    /// One sentence about what the UI thread's stack shows, for the top of the report.
    /// </summary>
    /// <param name="frames">Innermost first.</param>
    /// <param name="isAboutAFreeze">
    /// A freeze makes "not blocked" a finding worth stating. On a crash report it is merely where the
    /// thread happened to be, and stating it as a verdict makes the report appear to argue with its own
    /// headline.
    /// </param>
    /// <param name="threadId">Named when the stack cannot be read, so the reader knows where to look.</param>
    public static string Describe(IReadOnlyList<string> frames, bool isAboutAFreeze, int threadId)
    {
        // Bloom's fatal handler asks the Doctor for a dump and waits for it, so on a crash report the UI
        // thread is genuinely blocked - in OUR OWN request. Reporting that as "blocked in
        // WaitHandle.WaitOneCore" is true and useless, and invites a reader to go hunting a deadlock that
        // is really this tool doing its job.
        if (frames.Any(f => f.Contains("RequestDumpBeforeDying", StringComparison.Ordinal)))
            return "The UI thread is inside Bloom's own fatal-error handler, waiting for this dump - so "
                + "the stack below is the path to the crash, not a deadlock.";

        if (!SaysAnythingUseful(frames))
            return $"The UI thread's stack could not be read - which is itself a clue, because a stack "
                + $"walk fails on a thread that is RUNNING far more often than on one that is waiting. "
                + $"Thread {threadId} in the attached dump is the one to open.";

        var blocking = DescribeBlockingCall(frames);
        if (blocking != null)
            return $"The UI thread is blocked in {blocking}.";
        return isAboutAFreeze
            ? "The UI thread is in its message loop (idle or pumping)."
            : "The UI thread was in its message loop when this snapshot was taken.";
    }

    /// <summary>
    /// The innermost frame that looks like a reason to be stuck, or null for a healthy idle pump.
    /// </summary>
    public static string? DescribeBlockingCall(IReadOnlyList<string> frames)
    {
        // Frames run innermost-first. The innermost interesting thing is what it is stuck in.
        foreach (var frame in frames)
        {
            if (frame.Contains("WaitMessage", StringComparison.Ordinal))
                return null; // a healthy idle pump, not a block
            // The bottom of every UI thread is not a blocking call, and saying it was produced the
            // nonsense "blocked in Bloom.Program.Run" on the one stack that had nothing else in it.
            if (TheBottomOfEveryUiThread.Any(b => frame.Contains(b, StringComparison.Ordinal)))
                continue;
            if (
                frame.StartsWith("Bloom.", StringComparison.Ordinal)
                || frame.Contains("Monitor.", StringComparison.Ordinal)
                || frame.Contains("WaitHandle", StringComparison.Ordinal)
                || frame.Contains("ManualResetEvent", StringComparison.Ordinal)
                || frame.Contains("SemaphoreSlim", StringComparison.Ordinal)
                || frame.Contains("Task.Wait", StringComparison.Ordinal)
                || frame.Contains("Thread.Sleep", StringComparison.Ordinal)
                || frame.Contains("Socket", StringComparison.Ordinal)
                || frame.Contains("HttpClient", StringComparison.Ordinal)
            )
                return frame;
        }
        // Nothing matched. Fall back to the innermost frame that says anything - excluding, again, the
        // bottom every UI thread shares, or a stack with nothing else left in it would report that as the
        // blocking call and read as a confident diagnosis of nothing at all.
        return frames.FirstOrDefault(f =>
            !IsPlumbing(f)
            && !TheBottomOfEveryUiThread.Any(bottom => f.Contains(bottom, StringComparison.Ordinal))
        );
    }
}
