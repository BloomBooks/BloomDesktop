namespace BloomFreezeDoctor.Protocol;

/// <summary>
/// How far Bloom's orderly shutdown had got. Bloom records this as it goes out, so that a Bloom which
/// dies part way can say *where* it stopped rather than merely that it did.
///
/// <see cref="None"/> is load-bearing rather than merely a default: Bloom has several exits that are hard
/// failures rather than shutdowns — WebView2 failing to initialise, the non-message-loop branch of the
/// fatal exception handler, the Doctor asking a zombie to go — and none of them passes a phase marker. So
/// "still None" is how a hard failure is told from an orderly exit, and it needs no cooperation from each
/// exit site, which is what makes the next such exit somebody adds covered automatically.
///
/// **Both the numbers and the names are frozen, and they break in opposite directions.** The NUMBER
/// travels in the shared page as a raw int, so renumbering leaves every offset intact and every reader
/// wrong — which makes it a <c>SchemaVersion</c> bump, not an edit. The NAME travels in the session file,
/// which is JSON written with names, so renaming makes a session an older Bloom wrote unreadable, and
/// there is no version to bump for that. Both halves are pinned by a test on each side; appending a later
/// phase is the safe change, and a Doctor too old to know it says so rather than guessing (see
/// <see cref="ShutdownPhaseDescription.Describe"/>).
/// </summary>
public enum BloomShutdownPhase
{
    /// <summary>The orderly shutdown never began.</summary>
    None = 0,

    /// <summary>The message loop has returned, so Bloom is on its way out.</summary>
    MessageLoopReturned = 1,

    /// <summary>Settings have been saved.</summary>
    SettingsSaved = 2,

    /// <summary>The log has been written.</summary>
    LogWritten = 3,

    /// <summary>The project context has been disposed — the last of the phases.</summary>
    ProjectContextDisposed = 4,
}

/// <summary>Puts a shutdown phase into words, for the reports a person actually reads.</summary>
public static class ShutdownPhaseDescription
{
    /// <summary>
    /// The phase in plain words, e.g. "settings had been saved". Written for whoever reads the card: a bare
    /// number there means nothing without this file open beside it.
    /// </summary>
    public static string Describe(this BloomShutdownPhase phase) =>
        phase switch
        {
            BloomShutdownPhase.None => "the orderly shutdown never began",
            BloomShutdownPhase.MessageLoopReturned => "the message loop had returned",
            BloomShutdownPhase.SettingsSaved => "settings had been saved",
            BloomShutdownPhase.LogWritten => "the log had been written",
            BloomShutdownPhase.ProjectContextDisposed => "the project context had been disposed",
            // A phase added to Bloom after this Doctor was built. Saying so beats inventing a description,
            // and beats hiding a number the reader could still look up.
            _ => $"an unrecognised phase ({(int)phase}), newer than this Doctor knows about",
        };
}
