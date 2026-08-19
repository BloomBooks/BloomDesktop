// Explicit usings and nullable context: this file is copied into BloomDesktop, which has neither
// ImplicitUsings nor nullable enabled. See DoctorChannel.cs for the full explanation.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bloom.FreezeDoctor;

// =====================================================================================================
//  SECOND HALF OF THE CONTRACT BETWEEN BLOOM AND THE FREEZE DOCTOR. COPIED INTO BOTH REPOS.
//
//  Source of truth: BloomBooks/bloom-freeze-doctor, src/BloomFreezeDoctor.Core/Contract/DoctorSession.cs
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
    /// How this run ended, once it has. Null while Bloom is running — and null *after* Bloom has gone is
    /// itself the evidence that it did not shut down properly. Nothing may set this while Bloom is still
    /// running; see <see cref="BloomAlreadyReported"/> for what used to get that wrong.
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
            File.WriteAllText(temp, JsonSerializer.Serialize(session, Options));
            // A rename within one volume is atomic; a copy is not, and this file is rewritten repeatedly.
            File.Move(temp, path, overwrite: true);
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
            if (!File.Exists(path))
                return null;
            var session = JsonSerializer.Deserialize<DoctorSession>(
                File.ReadAllText(path),
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
                        File.ReadAllText(path),
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
                var explained = session.Exit != null;
                if (tooOld || explained)
                    File.Delete(PathFor(session.ProcessId, directory));
            }
            catch (Exception)
            {
                // Locked or already gone; the next pass will deal with it.
            }
        }
    }
}
