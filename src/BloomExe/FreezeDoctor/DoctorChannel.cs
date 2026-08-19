// Explicit usings and an explicit nullable context, rather than relying on the project's settings.
// This file is copied into BloomDesktop, which has neither ImplicitUsings nor nullable enabled, so
// depending on them would mean the copy could not be byte-identical — and a file that has to be edited
// on the way in is a file that will drift.
#nullable enable
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Bloom.FreezeDoctor;

// =====================================================================================================
//  THIS FILE IS THE CONTRACT BETWEEN BLOOM AND THE FREEZE DOCTOR, AND IT IS COPIED INTO BOTH REPOS.
//
//  Source of truth: BloomBooks/bloom-freeze-doctor, src/BloomFreezeDoctor.Core/Contract/DoctorChannel.cs
//  The copy in BloomDesktop must stay byte-identical apart from its namespace. Both repos have a test
//  that pins SchemaVersion and the field offsets, so a drift breaks a build rather than silently
//  producing reports full of nonsense.
//
//  Why shared memory rather than a pipe, a socket, or Bloom's own web server: the Doctor has to be able
//  to read this when Bloom is wedged. A request/response channel needs Bloom to be well enough to
//  answer, which is exactly what we cannot assume — and Bloom's HTTP server in particular can be
//  deadlocked or starved of worker threads, which is one of the failures we are hunting. Reading a page
//  of memory needs nothing from Bloom at all.
//
//  A memory-mapped section also outlives the process that created it for as long as any handle stays
//  open, so the Doctor, which holds one, can still read Bloom's final state after Bloom has gone. That
//  is what makes the clean-exit flag here (rather than only on disk) useful.
// =====================================================================================================

/// <summary>
/// A consistent snapshot of what Bloom last published about itself.
/// </summary>
public sealed record DoctorChannelSnapshot
{
    /// <summary>Layout version Bloom wrote with.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>The process this describes.</summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// How many times Bloom's UI-thread timer has fired. Its *staleness* is the freeze signal — see
    /// <see cref="UiHeartbeatAge"/>.
    /// </summary>
    public required long UiTicks { get; init; }

    /// <summary>
    /// How long since the UI thread last ticked. This is the only signal that catches a UI thread blocked
    /// in a managed wait on an STA thread, where the window still answers messages and every outside
    /// probe reports the application as healthy.
    /// </summary>
    public required TimeSpan UiHeartbeatAge { get; init; }

    /// <summary>How many times Bloom's background watchdog thread has ticked.</summary>
    public required long WatchdogTicks { get; init; }

    /// <summary>
    /// How long since the watchdog thread last ticked. Compare with <see cref="UiHeartbeatAge"/>: a stale
    /// UI heartbeat with a healthy watchdog means the UI thread is blocked, while both stale means the
    /// whole process is wedged (a GC that will not finish, or a suspended process).
    /// </summary>
    public required TimeSpan WatchdogHeartbeatAge { get; init; }

    /// <summary>What Bloom says it is doing, for the report's opening lines.</summary>
    public required string Activity { get; init; }

    /// <summary>
    /// How far shutdown has got, or 0 if it has not started. Lets a Bloom that dies mid-shutdown say
    /// *where* it stopped rather than merely that it did.
    /// </summary>
    public required int ShutdownPhase { get; init; }

    /// <summary>True once Bloom has recorded that its shutdown ran to completion.</summary>
    public required bool CleanExitRecorded { get; init; }

    /// <summary>
    /// True if Bloom sees a debugger attached. Authoritative, unlike our outside guess, and the reason a
    /// developer stopping their debugger never produces a report.
    /// </summary>
    public required bool DebuggerAttached { get; init; }

    /// <summary>
    /// True while Bloom is deliberately busy — publishing, uploading, making a PDF. Raises the Doctor's
    /// patience rather than silencing it.
    /// </summary>
    public required bool LongOperationInProgress { get; init; }

    /// <summary>Server worker threads currently doing work, when Bloom reports it.</summary>
    public required int ServerBusyWorkers { get; init; }

    /// <summary>
    /// Server worker threads currently blocked. Bloom already tracks this for its own deadlock
    /// avoidance; surfacing it costs nothing and says a great deal about a frozen publish.
    /// </summary>
    public required int ServerBlockedWorkers { get; init; }
}

/// <summary>
/// The layout of the shared page, and the names used to find it. Both the writer (in Bloom) and the
/// reader (in the Doctor) work from these constants, so there is one description of the format rather
/// than two.
/// </summary>
public static class DoctorChannelLayout
{
    /// <summary>
    /// Bump this only for an incompatible change. The reader refuses anything it does not recognise
    /// rather than misreading it, so an old Doctor meeting a new Bloom degrades to Tier A instead of
    /// reporting rubbish.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// One page is ample and keeps the whole record on a single page of memory. `Local\` scope, not
    /// `Global\`: the Doctor cannot open processes in another Windows session anyway.
    /// </summary>
    public const int Size = 4096;

    /// <summary>The name for a given Bloom process.</summary>
    public static string NameFor(int processId) =>
        $@"Local\BloomFreezeDoctor.v{SchemaVersion}.{processId}";

    // The layout. Offsets are explicit rather than computed so a change is visible in a diff.
    internal const int OffsetSchemaVersion = 0;
    internal const int OffsetProcessId = 4;

    /// <summary>
    /// A sequence number, incremented before and after every write. Odd means a write is in progress.
    /// The reader takes it before and after reading and retries if it changed, which is what stops it
    /// seeing half of one update and half of the next — mattering most for the strings, since a torn
    /// activity name would be gibberish on a card.
    /// </summary>
    internal const int OffsetWriteSequence = 8;

    internal const int OffsetUiTicks = 16;
    internal const int OffsetUiTimestamp = 24;
    internal const int OffsetWatchdogTicks = 32;
    internal const int OffsetWatchdogTimestamp = 40;
    internal const int OffsetShutdownPhase = 48;
    internal const int OffsetFlags = 52;
    internal const int OffsetServerBusy = 56;
    internal const int OffsetServerBlocked = 60;
    internal const int OffsetActivity = 64;

    /// <summary>
    /// How much room the activity string gets. Public because it is part of the contract a caller has to
    /// respect: anything longer is truncated rather than allowed to run into the next field.
    /// </summary>
    public const int ActivityMaxBytes = 256;

    internal const int FlagCleanExitRecorded = 1 << 0;
    internal const int FlagDebuggerAttached = 1 << 1;
    internal const int FlagLongOperation = 1 << 2;
}

/// <summary>
/// Reads what a Bloom has published. Used by the Doctor; harmless if Bloom is an older version that
/// publishes nothing, in which case <see cref="TryRead"/> simply returns false and the Doctor falls back
/// to watching from outside.
/// </summary>
public static class DoctorChannelReader
{
    /// <summary>
    /// Reads a consistent snapshot, or returns false if this Bloom publishes no channel (an older
    /// version), the schema is one we do not understand, or the data would not settle.
    /// </summary>
    public static bool TryRead(int processId, out DoctorChannelSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using var file = MemoryMappedFile.OpenExisting(
                DoctorChannelLayout.NameFor(processId),
                MemoryMappedFileRights.Read
            );
            using var view = file.CreateViewAccessor(
                0,
                DoctorChannelLayout.Size,
                MemoryMappedFileAccess.Read
            );

            // Retry a few times: a write in progress is momentary, and giving up after three attempts
            // is better than looping while Bloom is busy.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var before = view.ReadInt64(DoctorChannelLayout.OffsetWriteSequence);
                if (before % 2 != 0)
                    continue; // a write is in flight

                var candidate = ReadFields(view, processId);
                var after = view.ReadInt64(DoctorChannelLayout.OffsetWriteSequence);
                if (before != after)
                    continue; // it changed under us

                if (candidate == null)
                    return false; // schema we do not understand: better nothing than nonsense
                snapshot = candidate;
                return true;
            }
        }
        catch (FileNotFoundException)
        {
            // No channel: an older Bloom, or one that has not got that far in startup. Expected.
        }
        catch (Exception)
        {
            // Anything else (permissions, a half-created section) also means "no channel".
        }
        return false;
    }

    private static DoctorChannelSnapshot? ReadFields(MemoryMappedViewAccessor view, int processId)
    {
        var schema = view.ReadInt32(DoctorChannelLayout.OffsetSchemaVersion);
        if (schema != DoctorChannelLayout.SchemaVersion)
            return null;

        var now = Environment.TickCount64;
        var flags = view.ReadInt32(DoctorChannelLayout.OffsetFlags);
        var activityBytes = new byte[DoctorChannelLayout.ActivityMaxBytes];
        view.ReadArray(DoctorChannelLayout.OffsetActivity, activityBytes, 0, activityBytes.Length);

        return new DoctorChannelSnapshot
        {
            SchemaVersion = schema,
            ProcessId = view.ReadInt32(DoctorChannelLayout.OffsetProcessId),
            UiTicks = view.ReadInt64(DoctorChannelLayout.OffsetUiTicks),
            // Both sides use Environment.TickCount64, which counts since the machine booted and is
            // therefore directly comparable between processes — unlike a wall clock, which a time
            // change or a sleep would skew.
            UiHeartbeatAge = AgeOf(view.ReadInt64(DoctorChannelLayout.OffsetUiTimestamp), now),
            WatchdogTicks = view.ReadInt64(DoctorChannelLayout.OffsetWatchdogTicks),
            WatchdogHeartbeatAge = AgeOf(
                view.ReadInt64(DoctorChannelLayout.OffsetWatchdogTimestamp),
                now
            ),
            Activity = DecodeString(activityBytes),
            ShutdownPhase = view.ReadInt32(DoctorChannelLayout.OffsetShutdownPhase),
            CleanExitRecorded = (flags & DoctorChannelLayout.FlagCleanExitRecorded) != 0,
            DebuggerAttached = (flags & DoctorChannelLayout.FlagDebuggerAttached) != 0,
            LongOperationInProgress = (flags & DoctorChannelLayout.FlagLongOperation) != 0,
            ServerBusyWorkers = view.ReadInt32(DoctorChannelLayout.OffsetServerBusy),
            ServerBlockedWorkers = view.ReadInt32(DoctorChannelLayout.OffsetServerBlocked),
        };
    }

    private static TimeSpan AgeOf(long timestamp, long now) =>
        timestamp <= 0
            ? TimeSpan.MaxValue
            : TimeSpan.FromMilliseconds(Math.Max(0, now - timestamp));

    private static string DecodeString(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = bytes.Length;
        return length == 0 ? "" : Encoding.UTF8.GetString(bytes, 0, length);
    }
}

/// <summary>
/// Publishes Bloom's state into the shared page. Lives in Bloom; here in the Doctor's repo only so that
/// the two sides share one description of the format, and so the Doctor's own tests can write a channel
/// to read back.
///
/// **Every method must be safe to call from anywhere and must never throw**, because the callers are
/// Bloom's UI thread and Bloom's shutdown path. Diagnostics that can break the application they
/// diagnose are worse than no diagnostics.
/// </summary>
public sealed class DoctorChannelWriter : IDisposable
{
    private readonly MemoryMappedFile? _file;
    private readonly MemoryMappedViewAccessor? _view;

    /// <summary>
    /// Serialises the whole of <see cref="Write"/>.
    ///
    /// This is not belt-and-braces: without it the sequence protocol is broken, and broken in a way that
    /// silently disables the channel for the rest of the run. Two threads publish here — the UI-thread timer
    /// and the watchdog thread — and `++_writeSequence` is a non-atomic read-modify-write, so a lost update
    /// can leave the counter resting on an ODD value, which every reader interprets as "a write is in
    /// progress" and gives up on, for ever. Even with an atomic counter, two overlapping writers can let a
    /// reader see an unchanged even sequence around a half-written state, which is precisely the torn read
    /// the sequence exists to prevent.
    ///
    /// A private lock cannot deadlock anything we are diagnosing: only these two diagnostic callers ever take
    /// it, neither holds another lock, and the critical section is a handful of writes to a resident page.
    /// </summary>
    private readonly object _writeLock = new();

    private long _writeSequence;
    private long _uiTicks;
    private long _watchdogTicks;

    /// <summary>
    /// Creates the channel for this process. If it cannot be created, every method afterwards does
    /// nothing: publishing diagnostics is never worth failing a startup over.
    /// </summary>
    public DoctorChannelWriter(int processId)
    {
        try
        {
            _file = MemoryMappedFile.CreateNew(
                DoctorChannelLayout.NameFor(processId),
                DoctorChannelLayout.Size,
                MemoryMappedFileAccess.ReadWrite
            );
            _view = _file.CreateViewAccessor(
                0,
                DoctorChannelLayout.Size,
                MemoryMappedFileAccess.ReadWrite
            );
            _view.Write(DoctorChannelLayout.OffsetSchemaVersion, DoctorChannelLayout.SchemaVersion);
            _view.Write(DoctorChannelLayout.OffsetProcessId, processId);
        }
        catch (Exception)
        {
            _file = null;
            _view = null;
        }
    }

    /// <summary>True if the channel was created and is being published.</summary>
    public bool IsOpen => _view != null;

    /// <summary>
    /// Records that the UI thread is alive. Call from a UI-thread timer; the Doctor watches how long ago
    /// this last happened.
    /// </summary>
    public void RecordUiTick() =>
        Write(view =>
        {
            view.Write(DoctorChannelLayout.OffsetUiTicks, ++_uiTicks);
            view.Write(DoctorChannelLayout.OffsetUiTimestamp, Environment.TickCount64);
        });

    /// <summary>
    /// Records that the background watchdog thread is alive, which is how the Doctor distinguishes "the
    /// UI thread is blocked" from "the whole process is wedged".
    /// </summary>
    public void RecordWatchdogTick() =>
        Write(view =>
        {
            view.Write(DoctorChannelLayout.OffsetWatchdogTicks, ++_watchdogTicks);
            view.Write(DoctorChannelLayout.OffsetWatchdogTimestamp, Environment.TickCount64);
        });

    /// <summary>Says what Bloom is doing, in words fit for a bug report.</summary>
    public void SetActivity(string activity) =>
        Write(view =>
        {
            var bytes = new byte[DoctorChannelLayout.ActivityMaxBytes];
            var encoded = Encoding.UTF8.GetBytes(activity ?? "");
            var length = Math.Min(encoded.Length, bytes.Length - 1);
            // Truncate on a character boundary. Activity text can carry a book title or a file path, so
            // cutting mid-sequence through a multi-byte character would leave the reader decoding a broken
            // byte — and the report quoting a mangled name. Only when we actually truncated: `encoded[length]`
            // is past the end otherwise, and the resulting exception used to leave the write sequence odd for
            // ever, which silently disabled the whole channel.
            if (length < encoded.Length)
            {
                while (length > 0 && (encoded[length] & 0xC0) == 0x80)
                    length--;
            }
            Array.Copy(encoded, bytes, length);
            view.WriteArray(DoctorChannelLayout.OffsetActivity, bytes, 0, bytes.Length);
        });

    /// <summary>Marks a deliberately long operation, which buys Bloom patience rather than silence.</summary>
    public void SetLongOperation(bool inProgress) =>
        SetFlag(DoctorChannelLayout.FlagLongOperation, inProgress);

    /// <summary>Publishes whether a debugger is attached, which is authoritative and stops false reports.</summary>
    public void SetDebuggerAttached(bool attached) =>
        SetFlag(DoctorChannelLayout.FlagDebuggerAttached, attached);

    /// <summary>Records how far shutdown has got.</summary>
    public void SetShutdownPhase(int phase) =>
        Write(view => view.Write(DoctorChannelLayout.OffsetShutdownPhase, phase));

    /// <summary>Records that shutdown ran to completion — the proof whose absence is itself evidence.</summary>
    public void RecordCleanExit() => SetFlag(DoctorChannelLayout.FlagCleanExitRecorded, true);

    /// <summary>Publishes the server's worker counts, which say a great deal about a frozen publish.</summary>
    public void SetServerWorkerCounts(int busy, int blocked) =>
        Write(view =>
        {
            view.Write(DoctorChannelLayout.OffsetServerBusy, busy);
            view.Write(DoctorChannelLayout.OffsetServerBlocked, blocked);
        });

    private void SetFlag(int flag, bool value) =>
        Write(view =>
        {
            var flags = view.ReadInt32(DoctorChannelLayout.OffsetFlags);
            flags = value ? flags | flag : flags & ~flag;
            view.Write(DoctorChannelLayout.OffsetFlags, flags);
        });

    /// <summary>
    /// Performs one update between two increments of the sequence number, so a reader can tell whether
    /// it saw a settled state. Swallows everything: see the class comment.
    /// </summary>
    private void Write(Action<MemoryMappedViewAccessor> update)
    {
        var view = _view;
        if (view == null)
            return;
        try
        {
            lock (_writeLock)
            {
                view.Write(DoctorChannelLayout.OffsetWriteSequence, ++_writeSequence);
                try
                {
                    update(view);
                }
                finally
                {
                    // Always leave the sequence EVEN, even if the update threw. An odd resting value means
                    // "a write is in progress" to every reader, for ever — so one failed write would silently
                    // disable the channel for the rest of the run, and the Doctor would fall back to watching
                    // from outside with no indication why. A reader may see one inconsistent state; that is a
                    // far smaller loss.
                    view.Write(DoctorChannelLayout.OffsetWriteSequence, ++_writeSequence);
                }
            }
        }
        catch (Exception)
        {
            // Never let publishing state break the thing whose state we are publishing.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _view?.Dispose();
        _file?.Dispose();
    }
}
