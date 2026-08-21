namespace BloomFreezeDoctor;

/// <summary>
/// Everything we could learn about why a Bloom process is no longer running. Collected by
/// <c>WindowsExitEvidenceCollector</c>; kept as a plain record so the judgement below can be tested
/// exhaustively without an Event Log or a filesystem.
/// </summary>
public sealed record ExitEvidence
{
    /// <summary>The process's exit code, if we still had a handle when it died.</summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Phase 3 only: Bloom left proof that its shutdown ran (a <c>ProcessExit</c> handler wrote it).
    /// Null means we are in Phase 1 and no proof mechanism existed, which is NOT the same as false —
    /// see <see cref="ExitReportPolicy"/>.
    /// </summary>
    public bool? CleanExitProofPresent { get; init; }

    /// <summary>How far Bloom's shutdown got, when the proof records it.</summary>
    public int? ShutdownPhaseReached { get; init; }

    /// <summary>
    /// A Windows "Application Error" (1000), "Application Hang" (1002) or .NET Runtime entry naming
    /// this process. Strong evidence of a crash.
    /// </summary>
    public bool HasEventLogCrashEntry { get; init; }

    /// <summary>Windows Error Reporting left a report for this process.</summary>
    public bool HasWerReport { get; init; }

    /// <summary>
    /// Bloom's own log ends with the line <c>ProgramExit</c> writes when a clean shutdown stalls for
    /// 20 seconds and it forces the process out. That gets its own verdict rather than being written
    /// off as a user kill, because it is a real bug we would otherwise never hear about.
    /// </summary>
    public bool LogShowsForcedShutdown { get; init; }

    /// <summary>
    /// The machine went down unexpectedly around the time the process vanished (Event Log 6008, or a
    /// boot later than the process's death). Nothing to do with Bloom, and the Doctor died too, so
    /// this is discovered when reconciling an orphaned session rather than watched live.
    /// </summary>
    public bool MachineWentDown { get; init; }

    /// <summary>
    /// A debugger can account for this exit — one was attached when the process died, or left too recently
    /// to be ruled out. **Not** merely "was debugged at some point in its life": a debugger detached hours
    /// earlier does not explain a crash now, and treating it as though it did meant a developer's machine
    /// never reported anything again for the rest of the run.
    /// </summary>
    public bool DebuggerCouldExplainIt { get; init; }

    /// <summary>A developer or automation run, which is never filed whatever else is true.</summary>
    public bool NeverFile { get; init; }
}

/// <summary>Which of decision D4's two regimes to judge by.</summary>
public enum ExitReportPolicy
{
    /// <summary>
    /// Phase 1: Bloom leaves no proof of a clean exit, so an exit is only reportable when something
    /// corroborates a crash. Silence is the default.
    /// </summary>
    RequiresCorroboratingEvidence,

    /// <summary>
    /// Phase 3: Bloom proves its clean exits, so the absence of proof is itself the evidence and the
    /// default flips to reporting.
    /// </summary>
    RequiresProofOfCleanExit,
}

/// <summary>What we concluded about an exit.</summary>
public enum ExitVerdict
{
    /// <summary>Bloom shut down properly. Nothing to see.</summary>
    Clean,

    /// <summary>Crashed, with Windows or WER evidence to say so.</summary>
    Crashed,

    /// <summary>
    /// Bloom's clean shutdown stalled and its own safety net forced the process out after 20 seconds.
    /// A distinct bug, and one we would otherwise mistake for a user kill.
    /// </summary>
    ForcedAfterStalledShutdown,

    /// <summary>
    /// No orderly shutdown happened and nothing explains why: a hard kill, a debugger stop, or a crash
    /// that left no trace. Honestly labelled rather than called a crash.
    /// </summary>
    NoOrderlyShutdown,

    /// <summary>The machine went down. Not Bloom's doing.</summary>
    MachineWentDown,

    /// <summary>We know too little to say anything useful.</summary>
    Unknown,
}

/// <summary>The classifier's answer.</summary>
public readonly record struct ExitConclusion
{
    /// <summary>What happened.</summary>
    public required ExitVerdict Verdict { get; init; }

    /// <summary>Whether this exit is worth a report under the policy we were asked to apply.</summary>
    public required bool ShouldReport { get; init; }

    /// <summary>
    /// Plain-language reasoning, which becomes the card's opening line. It must never overclaim: a
    /// kill with no crash evidence says so rather than being dressed up as a crash.
    /// </summary>
    public required string Explanation { get; init; }
}

/// <summary>
/// Decides whether a Bloom that has gone away deserves a report. This is deliberately separate from
/// <see cref="FreezeDetector"/>, which refuses to guess: the answer depends on evidence gathered after
/// the fact, and on which phase's rules apply (decision D4).
/// </summary>
public static class ExitClassifier
{
    /// <summary>Unhandled managed exception. Confirmed by measurement in the Phase 0 spike.</summary>
    public const int ExitCodeUnhandledManagedException = unchecked((int)0xE0434352);

    /// <summary><c>Environment.FailFast</c>. Confirmed by measurement.</summary>
    public const int ExitCodeFailFast = unchecked((int)0x80131623);

    /// <summary>Access violation.</summary>
    public const int ExitCodeAccessViolation = unchecked((int)0xC0000005);

    /// <summary>
    /// Judges one exit. Order matters here, and the order is "explain it away before blaming Bloom":
    /// a machine that went down, a run we never file, and a debugged process all win over any crash
    /// evidence, because reporting those is worse than missing them.
    /// </summary>
    public static ExitConclusion Classify(ExitEvidence evidence, ExitReportPolicy policy)
    {
        if (evidence.MachineWentDown)
            return Conclude(
                ExitVerdict.MachineWentDown,
                false,
                "the machine shut down unexpectedly while Bloom was running, which explains the missing shutdown"
            );

        if (evidence.DebuggerCouldExplainIt)
            return Conclude(
                ExitVerdict.NoOrderlyShutdown,
                false,
                "a debugger was attached to this Bloom around the time it went, so its exit tells us nothing"
            );

        // NOTE what is deliberately NOT here: a check on NeverFile. Whether a report may be *filed* is a
        // different question from whether this exit is worth reporting on, and it is settled elsewhere — the
        // freeze path has always gathered a developer run's evidence to disk while declining to file it, and
        // this path used to fold the two together and so gathered nothing at all, while logging that it had.
        // One axis per question.

        // Bloom's own forced exit identifies itself in the log, and is worth a card in either regime.
        if (evidence.LogShowsForcedShutdown)
            return Conclude(
                ExitVerdict.ForcedAfterStalledShutdown,
                true,
                "Bloom's shutdown stalled and its 20-second safety net forced the process to exit"
            );

        if (evidence.CleanExitProofPresent == true)
            return Conclude(
                ExitVerdict.Clean,
                false,
                evidence.ShutdownPhaseReached.HasValue
                    ? $"Bloom shut down properly (shutdown phase {evidence.ShutdownPhaseReached})"
                    : "Bloom shut down properly"
            );

        var crashSignals = DescribeCrashSignals(evidence);
        if (crashSignals.Count > 0)
            return Conclude(
                ExitVerdict.Crashed,
                true,
                "Bloom crashed: " + string.Join("; ", crashSignals)
            );

        // Nothing says crash. What that means depends entirely on whether Bloom was capable of
        // proving a clean exit.
        if (policy == ExitReportPolicy.RequiresProofOfCleanExit)
        {
            // Phase 3: proof was available and is absent, so this is reportable — but described for
            // what it is, since a user ending a healthy Bloom in Task Manager looks identical.
            if (evidence.CleanExitProofPresent == false)
                return Conclude(
                    ExitVerdict.NoOrderlyShutdown,
                    true,
                    "no orderly shutdown; no crash evidence; may be a user-initiated kill"
                        + (
                            evidence.ShutdownPhaseReached.HasValue
                                ? $" (shutdown had reached phase {evidence.ShutdownPhaseReached})"
                                : ""
                        )
                );

            return Conclude(
                ExitVerdict.Unknown,
                false,
                "expected a clean-exit proof from this Bloom but found neither proof nor its absence"
            );
        }

        // Phase 1: silence is the default, because a bare exit is indistinguishable from the user
        // closing Bloom in a way we cannot see.
        return Conclude(ExitVerdict.NoOrderlyShutdown, false, ExplainQuietPhase1(evidence));
    }

    private static List<string> DescribeCrashSignals(ExitEvidence evidence)
    {
        var signals = new List<string>();
        if (evidence.HasEventLogCrashEntry)
            signals.Add("Windows logged an application error or hang for it");
        if (evidence.HasWerReport)
            signals.Add("Windows Error Reporting left a report");
        switch (evidence.ExitCode)
        {
            case ExitCodeUnhandledManagedException:
                signals.Add("it exited with an unhandled managed exception (0xE0434352)");
                break;
            case ExitCodeFailFast:
                signals.Add("it called FailFast (0x80131623)");
                break;
            case ExitCodeAccessViolation:
                signals.Add("it hit an access violation (0xC0000005)");
                break;
        }
        return signals;
    }

    /// <summary>
    /// Says why we are staying quiet, so the local record explains itself when someone comes looking
    /// for the report that never arrived.
    /// </summary>
    private static string ExplainQuietPhase1(ExitEvidence evidence)
    {
        var code = evidence.ExitCode;
        if (code == 0)
            return "exited with code 0 and nothing suggests a crash";
        if (code is null)
            return "exited without our seeing its exit code, and nothing suggests a crash";
        // 1 is the ambiguous one: Bloom's own forced exit uses it, and so does Task Manager. Without
        // the log line that identifies the forced path, we cannot tell, and guessing wrong in this
        // direction spams the tracker.
        if (code == 1)
            return "exited with code 1, which Bloom uses for several handled failures as well as its "
                + "forced shutdown, and no crash evidence was found";
        return $"exited with code {code} (0x{unchecked((uint)code.Value):X8}) but nothing corroborates a crash";
    }

    private static ExitConclusion Conclude(ExitVerdict verdict, bool report, string explanation) =>
        new()
        {
            Verdict = verdict,
            ShouldReport = report,
            Explanation = explanation,
        };
}
