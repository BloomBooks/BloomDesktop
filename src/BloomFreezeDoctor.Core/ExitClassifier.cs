using BloomFreezeDoctor.Protocol;

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
    /// Bloom left proof that its shutdown ran (a <c>ProcessExit</c> handler wrote it).
    ///
    /// Read only as an EXEMPTION - proof present means stay quiet - and never as a trigger. Its absence
    /// says nothing: a Bloom too old to leave one, a Task Manager kill and a real crash all look alike
    /// from here. Null means we could not tell, which is again no reason to act.
    /// </summary>
    public bool? CleanExitProofPresent { get; init; }

    /// <summary>How far Bloom's shutdown got, when the proof records it.</summary>
    public BloomShutdownPhase? ShutdownPhaseReached { get; init; }

    /// <summary>
    /// Bloom left an exit record, and that record says the orderly shutdown never began — a hard failure
    /// taking <c>Environment.Exit</c> straight out.
    ///
    /// This is why <see cref="CleanExitProofPresent"/> cannot simply mean "there is an exit record".
    /// Bloom writes one on the way out of a hard failure too; read as proof of a clean exit, the loudest
    /// thing Bloom can tell us becomes silence, and the card says "Bloom shut down properly (shutdown
    /// phase 0)".
    ///
    /// Note that this is narrower than the record's own "forced" flag, which also covers a Doctor asking
    /// a healthy Bloom to quit — an orderly shutdown that nobody should get a card about. See where the
    /// supervisor fills this in.
    /// </summary>
    public bool ExitRecordedAsForced { get; init; }

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
    /// Which crash this was - the exception type and top frames of the FAULTING thread, from the .NET
    /// Runtime event, or null when there was no managed crash entry to read one from.
    ///
    /// Not used to decide anything: <see cref="HasEventLogCrashEntry"/> is the evidence, and this is
    /// identity. It exists because the report fingerprint hashes the UI thread's stack, which is the right
    /// answer for a freeze and nearly useless for a crash - the fault is on another thread, so every crash
    /// on a given build would look like the same problem. See <see cref="CrashSignature"/>.
    /// </summary>
    public string? CrashSignature { get; init; }

    /// <summary>A developer build or headless run, which is never filed whatever else is true.</summary>
    public bool NeverFile { get; init; }
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
/// the fact.
/// </summary>
public static class ExitClassifier
{
    /// <summary>Unhandled managed exception.</summary>
    public const int ExitCodeUnhandledManagedException = unchecked((int)0xE0434352);

    /// <summary><c>Environment.FailFast</c>.</summary>
    public const int ExitCodeFailFast = unchecked((int)0x80131623);

    /// <summary>Access violation.</summary>
    public const int ExitCodeAccessViolation = unchecked((int)0xC0000005);

    /// <summary>
    /// Judges one exit.
    ///
    /// **The rule is "something must say this went wrong", never "nothing says it went right".** That is
    /// narrower than it sounds, and it is narrower on purpose.
    ///
    /// The obvious alternative - treating the ABSENCE of Bloom's clean-exit proof as evidence of a crash -
    /// is deliberately not used:
    ///
    /// * Absence has many causes: a Bloom too old to leave proof, a Task Manager kill, a debugger stop, a
    ///   machine losing power. Reasoning from it means recognising and excusing every innocent one, and
    ///   each check for that ("the machine went down", "a debugger was attached") is a place for subtle
    ///   mistakes, such as a startup crash read as a machine restart.
    /// * It cannot tell a crash from somebody closing Bloom in Task Manager, so its card has to admit it
    ///   may be about nothing - and a card like that trains people to ignore the next one.
    /// * It is not needed for the case that would justify it. Measured, an unhandled exception thrown on a
    ///   thread the application does not control - the exit that leaves no clean-exit proof, and the reason
    ///   this whole path exists - leaves:
    ///
    ///       ProcessExit handler ran     no
    ///       process exit code           0xE0434352 (ExitCodeUnhandledManagedException)
    ///       .NET Runtime event 1026     yes
    ///       Application Error 1000      yes
    ///       WER report folder           yes
    ///
    ///   Four independent signals, the first of them from the CLR rather than from any Windows feature an
    ///   administrator could switch off. A `TerminateProcess` kill, by contrast - which is what both "Stop
    ///   Debugging" and Task Manager do - leaves exit code -1, no ProcessExit, no event and no WER report:
    ///   nothing that says anything went wrong, so it is rightly quiet.
    ///
    /// What remains is ordered "Bloom's own account first": its first-hand statements outrank what Windows
    /// noticed afterwards, because they say what Bloom was doing rather than what the OS observed.
    /// </summary>
    public static ExitConclusion Classify(ExitEvidence evidence)
    {
        // NOTE what is deliberately NOT here: a check on NeverFile. Whether a report may be *filed* is a
        // different question from whether this exit is worth reporting on, and it is settled elsewhere: a
        // developer run's evidence is gathered to disk and then declined for filing. Folding the two
        // together here would instead gather nothing at all while logging that it had. One axis per
        // question.

        // Bloom's own forced exit identifies itself in the log, and this is the ONLY thing that identifies
        // it: ProgramExit's safety net ends with Environment.Exit, so the ProcessExit handler runs and
        // records a shutdown phase, and the session file therefore describes a perfectly clean exit. Drop
        // this check and a stalled shutdown becomes invisible.
        if (evidence.LogShowsForcedShutdown)
            return Conclude(
                ExitVerdict.ForcedAfterStalledShutdown,
                true,
                "Bloom's shutdown stalled and its 20-second safety net forced the process to exit"
            );

        // Bloom's own account of a forced exit, which outranks the crash signals below because it is
        // first-hand: it says what Bloom was doing, not what Windows noticed afterwards.
        if (evidence.ExitRecordedAsForced)
            return Conclude(
                ExitVerdict.NoOrderlyShutdown,
                true,
                "Bloom recorded its own exit as forced rather than orderly"
                    + (
                        evidence.ShutdownPhaseReached.HasValue
                            ? $" ({evidence.ShutdownPhaseReached.Value.Describe()})"
                            : ""
                    )
            );

        if (evidence.CleanExitProofPresent == true)
            return Conclude(
                ExitVerdict.Clean,
                false,
                evidence.ShutdownPhaseReached.HasValue
                    ? $"Bloom shut down properly ({evidence.ShutdownPhaseReached.Value.Describe()})"
                    : "Bloom shut down properly"
            );

        var crashSignals = DescribeCrashSignals(evidence);
        if (crashSignals.Count > 0)
            return Conclude(
                ExitVerdict.Crashed,
                true,
                "Bloom crashed: " + string.Join("; ", crashSignals)
            );

        // Nothing says this went wrong, so we say nothing. A bare exit with no crash signal and no
        // first-hand account from Bloom is indistinguishable from somebody closing Bloom in a way we
        // cannot see, and we do not guess.
        return Conclude(ExitVerdict.NoOrderlyShutdown, false, ExplainWhyWeAreQuiet(evidence));
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
    private static string ExplainWhyWeAreQuiet(ExitEvidence evidence)
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
