using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.IO;

namespace BloomFreezeDoctor.Protocol;

// =====================================================================================================
//  SECOND HALF OF THE CONTRACT BETWEEN BLOOM AND THE FREEZE DOCTOR.
//
//  DoctorChannel.cs carries what changes moment to moment, in shared memory. This file carries the facts
//  that do not change, and — crucially — the ones that must OUTLIVE the process. Shared memory dies when
//  the last handle closes, so a Bloom that crashes while no Doctor is watching leaves nothing behind. A
//  file survives a crash, a Doctor restart, and a reboot.
//
//  Everything here also removes a guess the Doctor would otherwise have to make from outside. Two are
//  worth naming, because both were measured as getting it wrong:
//
//   * WHICH LOG IS THIS BLOOM'S. Bloom recreates Log.txt every run and falls back to a randomly-named
//     Log-tmpXXXX.txt only when another Bloom already holds it — so in the restart-after-a-freeze case,
//     the frozen Bloom owns Log.txt and the healthy new one owns the tmp file, and "newest file wins"
//     picks the wrong one. Bloom simply telling us the path retires that whole problem.
//   * WHICH DEBUG PORT. The arithmetic differs by Bloom version, Bloom's own HTTP port belongs to http.sys
//     rather than to Bloom, and a machine running two Blooms can hand the Doctor the wrong browser.
// =====================================================================================================

/// <summary>
/// What Bloom records about itself when it starts, for any Doctor that comes looking — including one
/// installed after the fact, or started after Bloom has already died.
/// </summary>
public sealed record DoctorSession
{
    /// <summary>Layout version, so a newer Doctor can read an older machine's files.</summary>
    public int SchemaVersion { get; init; } = DoctorSessionStore.SchemaVersion;

    /// <summary>The process this describes.</summary>
    public int ProcessId { get; init; }

    /// <summary>When it started, which is how a log file is matched to it.</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Full path to Bloom's executable.</summary>
    public string ExePath { get; init; } = "";

    /// <summary>Bloom's version.</summary>
    public string Version { get; init; } = "";

    /// <summary>Release channel: Release, Beta, Developer/Debug…</summary>
    public string Channel { get; init; } = "";

    /// <summary>The command line, which reveals an automation or headless run.</summary>
    public string CommandLine { get; init; } = "";

    /// <summary>
    /// The log file Bloom is actually writing to. The single most valuable field here: see the note at the
    /// top of this file about why guessing it from outside is systematically wrong.
    /// </summary>
    public string LogPath { get; init; } = "";

    /// <summary>Bloom's own HTTP port, which cannot be discovered from outside because http.sys owns it.</summary>
    public int HttpPort { get; init; }

    /// <summary>The WebView2 debugging port, so the Doctor need not infer it.</summary>
    public int CdpPort { get; init; }

    /// <summary>The collection in use, for the report's context.</summary>
    public string CollectionName { get; init; } = "";

    /// <summary>
    /// True when Bloom's own reporting has already told us about a problem this run — a Sentry event or a
    /// tracker card. The Doctor defers to it rather than filing a second report about the same thing.
    ///
    /// This lives on the session rather than inside <see cref="Exit"/>, and that placement is the whole
    /// point: a user can file a problem report and then carry on working for hours. Recording it as an exit
    /// would describe a running Bloom as finished, which then reads as proof of an orderly shutdown for a
    /// process that may go on to crash.
    /// </summary>
    public bool BloomAlreadyReported { get; init; }

    /// <summary>The card or event Bloom filed, if it filed one.</summary>
    public string? ReportedId { get; init; }

    /// <summary>
    /// The kind of failure Bloom was deliberately told to simulate — `stawait`, `zombie`, `mutexchain` and
    /// so on — or null, which is every real Bloom.
    ///
    /// Bloom publishes this so the Doctor can tell a rehearsal from the real thing, and it lives HERE
    /// rather than in the shared-memory page for the usual reason: the page dies with the process, and
    /// three of the simulated kinds (`failfast`, `crashthread`, `zombie`) are precisely the cases where
    /// what remains afterwards is all we have.
    ///
    /// It records that the simulator ARMED, not merely that somebody set the environment variable. Those
    /// differ: a Beta or Release channel refuses, and so does an unrecognised kind, and marking a Bloom
    /// that was never going to misbehave would be worse than not marking at all.
    ///
    /// **What may depend on this: whether a report is filed, and a line in the report saying so. Nothing
    /// else.** Detection, gathering and the zombie-ending policy must behave exactly as they would for a
    /// real freeze, or a simulated run stops testing the thing it exists to test.
    /// </summary>
    public string? SimulatedFailure { get; init; }

    /// <summary>
    /// How this run ended, once it has. Null while Bloom is running — and null *after* Bloom has gone is
    /// itself the evidence that it did not shut down properly, which is why nothing may set it while Bloom
    /// is still running: an exit record on a live Bloom reads as proof it shut down cleanly.
    /// </summary>
    public DoctorSessionExit? Exit { get; init; }
}

/// <summary>How a Bloom run ended, written on the way out.</summary>
public sealed record DoctorSessionExit
{
    /// <summary>When it ended.</summary>
    public DateTimeOffset AtUtc { get; init; }

    /// <summary>How far shutdown got. See Bloom's Program.Run for what the numbers mean.</summary>
    public int ShutdownPhase { get; init; }

    /// <summary>
    /// True when this exit was forced by the Doctor asking Bloom to go, rather than being an orderly
    /// shutdown. Recorded so that ending a zombie is not later mistaken for proof that it shut down properly.
    /// </summary>
    public bool ForcedByDoctor { get; init; }
}

/// <summary>
/// Reads and writes the session files. Both sides use this, so there is one description of where the
/// files live and how they are named.
/// </summary>
public static class DoctorSessionStore
{
    /// <summary>Bump only for an incompatible change; readers ignore versions they do not know.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Where the files live. Under the user's own local application data, because that is writable
    /// without any privilege and is per-user, which matches who can watch whose processes anyway.
    /// </summary>
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SIL",
            "BloomFreezeDoctor",
            "sessions"
        );

    /// <summary>
    /// The file for one process.
    ///
    /// Keyed by process id, which Windows reuses — so a reader that finds a file for a *live* pid must check
    /// <see cref="DoctorSession.StartedAtUtc"/> against that process's actual start time before believing the
    /// file describes it. The alternative (a unique id in the name) would stop a Doctor finding the file for a
    /// pid it is watching, which is the common case this has to be good at.
    /// </summary>
    public static string PathFor(int processId, string? directory = null) =>
        Path.Combine(directory ?? DefaultDirectory, $"bloom-{processId}.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes a session file, temp-then-rename so that a reader never sees half a JSON document. Returns
    /// false rather than throwing: Bloom must not fail because a diagnostic file could not be written.
    /// </summary>
    public static bool TryWrite(DoctorSession session, string? directory = null)
    {
        try
        {
            var path = PathFor(session.ProcessId, directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            RobustFile.WriteAllText(temp, JsonSerializer.Serialize(session, Options));
            // A rename within one volume is atomic; a copy is not, and this file is rewritten repeatedly.
            //
            // Replace-or-Move rather than an overwriting Move, because RobustFile.Move has no overwrite
            // overload. Replace is the better primitive anyway when the target exists: it swaps the file
            // in one step, so a reader never sees the path missing, which an unlink-then-rename would
            // allow. Move covers the first write, when there is nothing to replace.
            if (RobustFile.Exists(path))
                RobustFile.Replace(temp, path, null);
            else
                RobustFile.Move(temp, path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Reads one session file, or null if it is absent or unreadable.</summary>
    public static DoctorSession? TryRead(int processId, string? directory = null)
    {
        try
        {
            var path = PathFor(processId, directory);
            if (!RobustFile.Exists(path))
                return null;
            var session = JsonSerializer.Deserialize<DoctorSession>(
                RobustFile.ReadAllText(path),
                Options
            );
            // Refuse a schema we do not understand rather than misreading it.
            return session != null && session.SchemaVersion == SchemaVersion ? session : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Every session file on the machine, for a Doctor that has just started and wants to know what has
    /// been happening — including sessions whose processes are long gone, which is how an unreported crash
    /// is discovered after the fact.
    /// </summary>
    public static List<DoctorSession> ReadAll(string? directory = null)
    {
        var sessions = new List<DoctorSession>();
        try
        {
            var folder = directory ?? DefaultDirectory;
            if (!Directory.Exists(folder))
                return sessions;
            foreach (var path in Directory.GetFiles(folder, "bloom-*.json"))
            {
                try
                {
                    var session = JsonSerializer.Deserialize<DoctorSession>(
                        RobustFile.ReadAllText(path),
                        Options
                    );
                    if (session != null && session.SchemaVersion == SchemaVersion)
                        sessions.Add(session);
                }
                catch (Exception)
                {
                    // One unreadable file must not hide the rest.
                }
            }
        }
        catch (Exception) { }
        return sessions;
    }

    /// <summary>
    /// Deletes session files that are of no further interest: their process is gone and either they
    /// recorded a clean exit or they are older than the cutoff. Keeping an unexplained exit around is the
    /// point, so those survive until they age out.
    /// </summary>
    public static void Prune(
        Func<int, bool> processIsAlive,
        TimeSpan maxAge,
        string? directory = null
    )
    {
        foreach (var session in ReadAll(directory))
        {
            try
            {
                if (processIsAlive(session.ProcessId))
                    continue;
                var tooOld = DateTimeOffset.UtcNow - session.StartedAtUtc > maxAge;
                // "Explained" means an ORDERLY exit. An exit that was forced — a hard failure, or the Doctor
                // ending a zombie — is not an explanation, it is the evidence; deleting it early would throw
                // away the record of the very thing we exist to report.
                var explained = session.Exit != null && !session.Exit.ForcedByDoctor;
                if (tooOld || explained)
                    RobustFile.Delete(PathFor(session.ProcessId, directory));
            }
            catch (Exception)
            {
                // Locked or already gone; the next pass will deal with it.
            }
        }
    }
}
