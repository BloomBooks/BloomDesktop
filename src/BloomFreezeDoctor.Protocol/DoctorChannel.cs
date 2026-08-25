using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace BloomFreezeDoctor.Protocol;

// =====================================================================================================
//  THIS FILE IS THE CONTRACT BETWEEN BLOOM AND THE FREEZE DOCTOR.
//
//  Both programs must agree about this layout exactly, so there is deliberately only ONE definition of
//  it: this project is published as a NuGet package and BloomDesktop references it. It used to be a copy
//  in each repository, and they drifted — which is a failure that shows up as confident, wrong reports
//  rather than as an error, because Bloom writes one set of offsets and the Doctor reads another.
//
//  There are two ways this format changes, and only one of them is a version bump:
//
//    * ADDING a field is the likely case. Append it, grow PayloadBytes, leave SchemaVersion alone. Old
//      readers ignore it; new readers can tell whether an older Bloom wrote it. See the long note on
//      DoctorChannelLayout.
//    * MOVING, RESIZING or REPURPOSING a field breaks every existing reader, so it bumps SchemaVersion —
//      which also changes the section's name, so old Doctors stop finding the channel rather than
//      misreading it.
//
//  Both are pinned by tests rather than trusted to discipline: the tests in this repo assert every offset
//  by value, assert that PayloadBytes really is the end of the last field, and assert that the writer
//  never touches a byte beyond it. BloomDesktop has its own test asserting the layout it was compiled
//  against, so a change Bloom has not caught up with fails Bloom's build rather than going quietly.
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

    /// <summary>
    /// How far into the page the Bloom that wrote this actually wrote — one past its last byte. Nothing
    /// reads it yet, because every Bloom writing this generation writes all of it; it is here so that when
    /// the layout does grow, a reader can tell a field an older Bloom never wrote from a real zero. A field
    /// is present only if <c>offset + size &lt;= PayloadBytes</c>. See the note in
    /// <see cref="DoctorChannelLayout"/>.
    /// </summary>
    public required int PayloadBytes { get; init; }

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
    /// True if Bloom saw a debugger attached when it last looked (once a second). Authoritative, unlike our
    /// outside guess, and the reason a developer stopping their debugger never produces a report.
    ///
    /// Covers a NATIVE debugger as well as a managed one: Bloom checks the PEB flag alongside
    /// <c>Debugger.IsAttached</c>, which only sees managed debuggers.
    /// </summary>
    public required bool DebuggerAttached { get; init; }

    /// <summary>
    /// True if a debugger has been attached at any point in this run, whether or not one is now. **This is
    /// usually the flag worth acting on, not <see cref="DebuggerAttached"/>**: a debugger that has come and
    /// gone leaves behind exactly the evidence of a freeze or a crash, and there is nothing left attached to
    /// explain it.
    /// </summary>
    public required bool DebuggerEverAttached { get; init; }

    /// <summary>
    /// How long ago a debugger was last detached, or <see cref="TimeSpan.MaxValue"/> if none ever has been
    /// (including while one is still attached).
    ///
    /// This is what makes <see cref="DebuggerEverAttached"/> usable without writing off a whole run: compare
    /// it with the length of the gap being judged, and a debugger that detached hours before a genuine
    /// freeze stops being an excuse for ignoring it.
    /// </summary>
    public required TimeSpan DebuggerLastDetachedAge { get; init; }

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
    /// The compatibility GENERATION. Bump this only for a change that an older reader could not survive:
    /// moving a field, resizing one, or reusing it for something else. **Do not bump it to add a field** —
    /// see <see cref="PayloadBytes"/>, which is how growth is meant to happen.
    ///
    /// Note that this number is part of the section's NAME, so a bump does not merely make readers reject
    /// the page: an old Doctor stops finding a channel at all and degrades to watching from outside, which
    /// is the right outcome but a total loss of the good data. A reader that wants to support two
    /// generations has to look for both names deliberately.
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

    // =================================================================================================
    //  HOW THIS FORMAT IS MEANT TO GROW
    //
    //  Adding a field is by far the likeliest change, so it has its own mechanism and does NOT need a
    //  SchemaVersion bump. Within one generation the rules are:
    //
    //    * Existing fields never move, never change size, and never change meaning. (A test pins every
    //      offset by value, so breaking this fails the build rather than the field.)
    //    * New fields are APPENDED at the current PayloadBytes, and PayloadBytes grows to match.
    //
    //  The writer records how far it wrote, so a reader can tell what is actually there. The direction
    //  that needs this is a NEW Doctor reading an OLD Bloom: the page is zero-filled at creation, so a
    //  field the writer never wrote reads as 0, which is indistinguishable from a real 0. Without
    //  PayloadBytes a new Doctor would confidently report "0" where the truth is "this Bloom is too old
    //  to say" — exactly the plausible-but-wrong report this whole design exists to avoid. (The opposite
    //  direction needs nothing: an old Doctor only ever looks at offsets that append-only growth leaves
    //  untouched.)
    //
    //  That case is the common one in the field, not a curiosity: the Doctor updates itself through
    //  Velopack while Bloom versions linger on people's machines, and the planned 6.4 backport
    //  guarantees more than one Bloom vintage writing this page at any given time.
    //
    //  The rule for a reader, when there is eventually more than one vintage to handle, is on the END of
    //  the field and not its start — a field that begins inside the written region but runs past the end
    //  of it must be treated as absent, not read half-way:
    //
    //        readable  <=>  fieldOffset + fieldSize <= snapshot.PayloadBytes
    //
    //  Nothing needs that yet, because every Bloom that writes generation 1 writes all of it. It is
    //  written down, and PayloadBytes is recorded and surfaced on the snapshot, so that the day it is
    //  needed the data is already there to work from.
    // =================================================================================================

    // The layout. Offsets are explicit rather than computed so a change is visible in a diff.
    internal const int OffsetSchemaVersion = 0;

    /// <summary>
    /// Where <see cref="PayloadBytes"/> itself lives. In the header, before anything that can grow,
    /// because every reader of every future vintage has to be able to find it.
    /// </summary>
    internal const int OffsetPayloadBytes = 4;

    /// <summary>
    /// A sequence number, incremented before and after every write. Odd means a write is in progress.
    /// The reader takes it before and after reading and retries if it changed, which is what stops it
    /// seeing half of one update and half of the next — mattering most for the strings, since a torn
    /// activity name would be gibberish on a card.
    ///
    /// Deliberately 8-byte aligned, which is why the two ints above it come first and ProcessId moved
    /// below it: an unaligned 64-bit read is not guaranteed to be atomic.
    /// </summary>
    internal const int OffsetWriteSequence = 8;

    internal const int OffsetProcessId = 16;
    internal const int OffsetShutdownPhase = 20;
    internal const int OffsetUiTicks = 24;
    internal const int OffsetUiTimestamp = 32;
    internal const int OffsetWatchdogTicks = 40;
    internal const int OffsetWatchdogTimestamp = 48;
    internal const int OffsetFlags = 56;
    internal const int OffsetServerBusy = 60;
    internal const int OffsetServerBlocked = 64;

    /// <summary>
    /// Four bytes nobody writes, so that the payload ENDS on an 8-byte boundary. Without it the next
    /// field appended would start at 324 and a 64-bit one would be unaligned, which the writer above
    /// explains is not safe to read atomically. Cheap now; awkward to retrofit, because fixing it later
    /// would mean moving a field, which is a generation bump.
    /// </summary>
    internal const int OffsetReserved = 68;

    internal const int OffsetActivity = 72;

    /// <summary>
    /// When a debugger was last detached, as <see cref="Environment.TickCount64"/>, or 0 if none ever has
    /// been. Appended after the activity block, which is why the four reserved bytes above matter: this is
    /// a 64-bit field and it lands 8-byte aligned.
    /// </summary>
    internal const int OffsetDebuggerLastDetached = 328;

    /// <summary>
    /// How much room the activity string gets. Public because it is part of the contract a caller has to
    /// respect: anything longer is truncated rather than allowed to run into the next field.
    ///
    /// Changing this number resizes an existing field, so it is a <see cref="SchemaVersion"/> bump and not
    /// an additive change.
    /// </summary>
    public const int ActivityMaxBytes = 256;

    /// <summary>
    /// One past the last byte a writer of THIS build ever writes, recorded in the page so a reader can
    /// tell how much of it is real. Grows as fields are appended; see the long note above.
    /// </summary>
    public const int PayloadBytes = OffsetDebuggerLastDetached + sizeof(long);

    /// <summary>
    /// The floor for generation 1: the least any writer of this generation provides, so a reader can read
    /// everything up to here without checking whether it is present.
    ///
    /// It is raised to match <see cref="PayloadBytes"/> while generation 1 is **unreleased** — no Bloom has
    /// shipped writing this page, so there is no older vintage to stay compatible with, and requiring the
    /// whole layout is simpler and stricter than tolerating a shorter one nobody can produce.
    ///
    /// **It freezes the moment a Bloom ships writing this page.** After that, appending a field grows
    /// PayloadBytes and leaves this alone, and a reader that wants the new field has to check for it — see
    /// the note above. Raising it after release would make a newer Doctor reject every older Bloom.
    /// </summary>
    public const int BaselinePayloadBytes = PayloadBytes;

    /// <summary>One field's position and extent, for the tests that pin the layout.</summary>
    public readonly record struct FieldExtent(string Name, int Offset, int Size);

    /// <summary>
    /// Every field, in order. This exists so the layout can be *checked* rather than merely described:
    /// the tests assert each offset by value, that no two fields overlap, that everything fits inside
    /// <see cref="Size"/>, and that <see cref="PayloadBytes"/> really is the end of the last field — which
    /// is what catches appending a field and forgetting to grow PayloadBytes.
    /// </summary>
    public static IReadOnlyList<FieldExtent> Fields { get; } =
        new[]
        {
            new FieldExtent(nameof(SchemaVersion), OffsetSchemaVersion, sizeof(int)),
            new FieldExtent(nameof(PayloadBytes), OffsetPayloadBytes, sizeof(int)),
            new FieldExtent("WriteSequence", OffsetWriteSequence, sizeof(long)),
            new FieldExtent("ProcessId", OffsetProcessId, sizeof(int)),
            new FieldExtent("ShutdownPhase", OffsetShutdownPhase, sizeof(int)),
            new FieldExtent("UiTicks", OffsetUiTicks, sizeof(long)),
            new FieldExtent("UiTimestamp", OffsetUiTimestamp, sizeof(long)),
            new FieldExtent("WatchdogTicks", OffsetWatchdogTicks, sizeof(long)),
            new FieldExtent("WatchdogTimestamp", OffsetWatchdogTimestamp, sizeof(long)),
            new FieldExtent("Flags", OffsetFlags, sizeof(int)),
            new FieldExtent("ServerBusy", OffsetServerBusy, sizeof(int)),
            new FieldExtent("ServerBlocked", OffsetServerBlocked, sizeof(int)),
            new FieldExtent("Reserved", OffsetReserved, sizeof(int)),
            new FieldExtent("Activity", OffsetActivity, ActivityMaxBytes),
            new FieldExtent("DebuggerLastDetached", OffsetDebuggerLastDetached, sizeof(long)),
        };

    internal const int FlagCleanExitRecorded = 1 << 0;
    internal const int FlagDebuggerAttached = 1 << 1;
    internal const int FlagLongOperation = 1 << 2;

    /// <summary>
    /// Set the first time a debugger is seen and never cleared. "Is a debugger attached right now" is not
    /// the question worth answering: a developer who attaches, sits at a breakpoint for five minutes, and
    /// detaches leaves a five-minute hole in the UI heartbeat and no debugger in sight, which is a freeze
    /// report about nothing. Same for a process a debugger terminated — that is a TerminateProcess, so no
    /// clean exit is recorded and it otherwise looks exactly like an unreported crash.
    /// </summary>
    internal const int FlagDebuggerEverAttached = 1 << 3;
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

            // Retry a few times: a write in progress is momentary, and giving up after a handful of
            // attempts is better than looping while Bloom is busy.
            //
            // **The yield matters as much as the count.** Three back-to-back reads all land inside one
            // scheduling quantum, so a writer that was preempted mid-write (holding the sequence odd)
            // defeats every attempt and TryRead returns false. Callers cannot tell that from "this Bloom
            // publishes nothing": the report then states that Bloom published no health channel, which is
            // untrue, and the check for work in progress answers "no" - one of the guards against ending
            // a Bloom that is part-way through saving. So give the writer a chance to be rescheduled, and
            // take a few more attempts while we are at it; the whole loop is still bounded and brief.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (attempt > 0)
                    Thread.Yield();

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

        // How much of the page the writer filled in. Two things are being rejected here, and the first is
        // the one that matters most: a section that has been CREATED but not yet initialised is
        // zero-filled, so it would otherwise present itself as a settled, sane-looking page of zeroes.
        // (The schema check above catches that too; this is the same guard on the field that will
        // eventually be load-bearing.) The upper bound catches a value that cannot be true of any writer.
        //
        // Note it is compared against the BASELINE and not against our own PayloadBytes: a Doctor built
        // against a later, larger layout must still accept an older Bloom that wrote less. Requiring our
        // own extent here would throw away the whole page for the sake of one field we happen to know
        // about and it does not.
        var payloadBytes = view.ReadInt32(DoctorChannelLayout.OffsetPayloadBytes);
        if (
            payloadBytes < DoctorChannelLayout.BaselinePayloadBytes
            || payloadBytes > DoctorChannelLayout.Size
        )
            return null;

        var now = Environment.TickCount64;
        var flags = view.ReadInt32(DoctorChannelLayout.OffsetFlags);
        var activityBytes = new byte[DoctorChannelLayout.ActivityMaxBytes];
        view.ReadArray(DoctorChannelLayout.OffsetActivity, activityBytes, 0, activityBytes.Length);

        return new DoctorChannelSnapshot
        {
            SchemaVersion = schema,
            PayloadBytes = payloadBytes,
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
            DebuggerEverAttached = (flags & DoctorChannelLayout.FlagDebuggerEverAttached) != 0,
            DebuggerLastDetachedAge = AgeOf(
                view.ReadInt64(DoctorChannelLayout.OffsetDebuggerLastDetached),
                now
            ),
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
            // How far this build writes, so a future reader can tell what is really here rather than
            // reading zero-filled space as data. Written once, at creation, and never changed.
            _view.Write(DoctorChannelLayout.OffsetPayloadBytes, DoctorChannelLayout.PayloadBytes);
            _view.Write(DoctorChannelLayout.OffsetProcessId, processId);
            // The heartbeats are deliberately NOT seeded here, and a test pins that
            // (A_heartbeat_that_never_ticked_reads_as_infinitely_old_rather_than_as_fresh). Seeding them
            // to "now" would read as healthy, which sounds tidier and is the dangerous direction: a Bloom
            // that hangs BEFORE its first tick - during startup, which is a real freeze - would then look
            // perfectly well. "Never ticked" erring towards alarm is the correct bias.
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

    /// <summary>
    /// Publishes whether a debugger is attached, which is authoritative and stops false reports.
    ///
    /// This method REMEMBERS, which is the point of it: the first attach also sets a sticky "ever attached"
    /// flag that is never cleared, and a detach records when it happened. Call it repeatedly — it detects the
    /// transitions by comparing against what is already in the page, so the caller does not have to track any
    /// state of its own.
    ///
    /// Why remembering matters: a debugger that has come and gone leaves behind precisely the evidence of a
    /// freeze (a long hole in the UI heartbeat) or of a crash (no clean exit, because terminating from a
    /// debugger is a TerminateProcess), with nothing still attached to account for it.
    /// </summary>
    public void SetDebuggerAttached(bool attached) =>
        Write(view =>
        {
            var flags = view.ReadInt32(DoctorChannelLayout.OffsetFlags);
            var wasAttached = (flags & DoctorChannelLayout.FlagDebuggerAttached) != 0;

            if (attached)
            {
                flags |=
                    DoctorChannelLayout.FlagDebuggerAttached
                    | DoctorChannelLayout.FlagDebuggerEverAttached;
            }
            else
            {
                flags &= ~DoctorChannelLayout.FlagDebuggerAttached;
                // Only on the transition. Writing it every time we are called with false would keep moving
                // the timestamp forward for a run that never had a debugger at all, and "last detached: one
                // second ago" would then excuse every freeze there ever was.
                if (wasAttached)
                    view.Write(
                        DoctorChannelLayout.OffsetDebuggerLastDetached,
                        Environment.TickCount64
                    );
            }

            view.Write(DoctorChannelLayout.OffsetFlags, flags);
        });

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
                // EVERY increment is inside this try, and the finally restores parity from whatever value we
                // actually reached. That ordering is the whole point, and getting it wrong here is unusually
                // expensive: an odd resting value means "a write is in progress" to every reader, for ever, so
                // the channel silently disables itself for the rest of the run and the Doctor falls back to
                // watching from outside with no indication why.
                //
                // The earlier version incremented the counter in the same statement that wrote it, from
                // outside the inner try. A throw from that one write then left the counter odd with no finally
                // to correct it — and from then on every write published an even value while in progress and
                // came to rest on an odd one, which is exactly backwards. Letting a reader see one
                // inconsistent state is a far smaller loss than that.
                try
                {
                    _writeSequence++; // now odd: a write is in progress
                    view.Write(DoctorChannelLayout.OffsetWriteSequence, _writeSequence);
                    update(view);
                }
                finally
                {
                    if (_writeSequence % 2 != 0)
                        _writeSequence++;
                    view.Write(DoctorChannelLayout.OffsetWriteSequence, _writeSequence);
                }
            }
        }
        catch (Exception)
        {
            // Never let publishing state break the thing whose state we are publishing. Note that the
            // counter itself is even by now whatever happened above, so even a failure that stopped the
            // final view.Write is repaired by the next successful write rather than lasting for ever.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _view?.Dispose();
        _file?.Dispose();
    }
}
