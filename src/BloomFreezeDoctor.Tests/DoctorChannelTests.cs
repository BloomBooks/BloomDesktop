using System;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using BloomFreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Tests for the shared-memory contract between Bloom and the Doctor.
///
/// **The layout is pinned here BY VALUE, on purpose.** There is one definition of it now — this project,
/// which BloomDesktop references as a package — so the two sides can no longer hold copies that disagree.
/// What they can still do is get out of step over a *version*: this repo changes the layout, Bloom carries
/// on referencing the version before it, and the mismatch shows up as reports full of plausible nonsense
/// rather than as an error, because Bloom publishes to one set of offsets and the Doctor reads another.
///
/// Pinning the numbers means changing the layout has to be deliberate here, and BloomDesktop's own test
/// pins the layout it was compiled against — so Bloom's build fails rather than Bloom quietly publishing
/// to the wrong place.
///
/// **Which change you are making decides what you do here.** Adding a field is not a version bump: append
/// it, grow `PayloadBytes`, and add a line to the pinned list. Moving, resizing or repurposing one breaks
/// every reader already deployed, so it bumps `SchemaVersion` — and because the version is part of the
/// section's name, old Doctors then stop finding the channel instead of misreading it.
///
/// Four tests guard the additive path, which is the one that gets used: the offsets are pinned by value,
/// `PayloadBytes` is checked to really be the end of the last field, the writer is checked never to touch a
/// byte beyond it, and the reader is checked to accept a writer that recorded a smaller extent than this
/// build knows about. The last of those is the case the mechanism exists for — a self-updating Doctor
/// reading a Bloom too old to write everything it knows about.
/// </summary>
[TestFixture]
public class DoctorChannelTests
{
    /// <summary>A pid unlikely to collide with a real Bloom while tests run.</summary>
    private const int TestProcessId = 999_001;

    /// <summary>
    /// Where PayloadBytes sits, taken from the public field table rather than from the internal constant.
    /// The table is the layout's public description of itself, so a test in another assembly can work from
    /// it without the layout having to expose every offset — and doing it this way means these tests
    /// exercise the table that Bloom's own pinned test also relies on.
    /// </summary>
    private static int PayloadBytesOffset =>
        DoctorChannelLayout
            .Fields.Single(f => f.Name == nameof(DoctorChannelLayout.PayloadBytes))
            .Offset;

    [Test]
    public void The_layout_is_pinned_so_a_change_cannot_pass_unnoticed()
    {
        // If you are here because this test failed: the layout changed. Bump SchemaVersion, update these
        // numbers, and publish a version Bloom can move up to — Bloom's own pinned test will fail until
        // its side is updated to match, which is the point.
        Assert.Multiple(() =>
        {
            Assert.That(DoctorChannelLayout.SchemaVersion, Is.EqualTo(1), "schema version");
            Assert.That(DoctorChannelLayout.Size, Is.EqualTo(4096), "page size");
            Assert.That(
                DoctorChannelLayout.NameFor(1234),
                Is.EqualTo(@"Local\BloomFreezeDoctor.v1.1234"),
                "the name must stay in the Local namespace, and must include pid and version"
            );

            // Every field, by value. This is the check the whole additive-growth scheme rests on: it is
            // only safe to append a field without bumping SchemaVersion if the existing fields have
            // genuinely not moved, and until now nothing verified that at all.
            //
            // Appending a field means adding a line HERE as well, which is the intended amount of
            // friction. MOVING any number already in this list means every deployed reader is wrong, so
            // it is a SchemaVersion bump, not an edit to the number.
            var expected = new (string Name, int Offset, int Size)[]
            {
                ("SchemaVersion", 0, 4),
                ("PayloadBytes", 4, 4),
                ("WriteSequence", 8, 8),
                ("ProcessId", 16, 4),
                ("ShutdownPhase", 20, 4),
                ("UiTicks", 24, 8),
                ("UiTimestamp", 32, 8),
                ("WatchdogTicks", 40, 8),
                ("WatchdogTimestamp", 48, 8),
                ("Flags", 56, 4),
                ("ServerBusy", 60, 4),
                ("ServerBlocked", 64, 4),
                ("Reserved", 68, 4),
                ("Activity", 72, 256),
                ("DebuggerLastDetached", 328, 8),
                ("ServerWorkers", 336, 4),
                ("ServerQueued", 340, 4),
            };
            Assert.That(
                DoctorChannelLayout.Fields.Select(f => (f.Name, f.Offset, f.Size)),
                Is.EqualTo(expected),
                "the field layout, pinned by value"
            );

            Assert.That(DoctorChannelLayout.ActivityMaxBytes, Is.EqualTo(256), "activity room");
            Assert.That(DoctorChannelLayout.PayloadBytes, Is.EqualTo(344), "payload extent");
            // Equal to PayloadBytes only because generation 1 is still unreleased, so there is no older
            // vintage writing less. It freezes as soon as a Bloom ships writing this page; after that,
            // appending a field grows PayloadBytes and leaves this alone.
            Assert.That(
                DoctorChannelLayout.BaselinePayloadBytes,
                Is.EqualTo(344),
                "the generation-1 floor"
            );
        });
    }

    [Test]
    public void The_shutdown_phases_are_what_both_sides_were_built_against()
    {
        // <see cref="BloomShutdownPhase"/> has TWO frozen surfaces, and they break in opposite
        // directions, so both halves are pinned here:
        //
        //   * the NUMBER travels in the shared page as a raw int, so RENUMBERING makes every deployed
        //     reader misread the field. That is a SchemaVersion bump, not an edit to this list.
        //   * the NAME travels in the session file, which is JSON written with names, so RENAMING makes
        //     a session written by an older Bloom unreadable. There is no version to bump for that: the
        //     name is the compatibility surface, so it simply must not change.
        //
        // Appending a phase is the safe change, and means adding a line here — the intended friction.
        var expected = new[]
        {
            "None=0",
            "MessageLoopReturned=1",
            "SettingsSaved=2",
            "LogWritten=3",
            "ProjectContextDisposed=4",
        };
        Assert.That(
            Enum.GetValues<BloomShutdownPhase>().Select(p => $"{p}={(int)p}").ToArray(),
            Is.EqualTo(expected),
            "the shutdown phases, pinned by name AND number"
        );
    }

    [Test]
    public void A_debugger_that_has_come_and_gone_is_still_remembered()
    {
        // The case this exists for. A developer attaches, sits at a breakpoint, detaches; Bloom carries on.
        // Nothing is attached any more, but there is now a long hole in the UI heartbeat that looks exactly
        // like a freeze. Without a memory of the debugger, that is a report about nothing.
        using var writer = new DoctorChannelWriter(TestProcessId);

        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var fresh), Is.True);
        Assert.That(fresh!.DebuggerAttached, Is.False, "setup: nothing attached yet");
        Assert.That(fresh.DebuggerEverAttached, Is.False, "setup: and none ever has been");
        Assert.That(
            fresh.DebuggerLastDetachedAge,
            Is.EqualTo(TimeSpan.MaxValue),
            "setup: never detached, because never attached"
        );

        writer.SetDebuggerAttached(true);
        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var during), Is.True);
        Assert.That(during!.DebuggerAttached, Is.True);
        Assert.That(during.DebuggerEverAttached, Is.True);
        Assert.That(
            during.DebuggerLastDetachedAge,
            Is.EqualTo(TimeSpan.MaxValue),
            "still attached, so it has not been detached"
        );

        writer.SetDebuggerAttached(false);
        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var after), Is.True);
        Assert.That(after!.DebuggerAttached, Is.False, "it really has gone");
        Assert.That(
            after.DebuggerEverAttached,
            Is.True,
            "but the run is still one in which a debugger was attached"
        );
        Assert.That(
            after.DebuggerLastDetachedAge,
            Is.LessThan(TimeSpan.FromMinutes(1)),
            "and we know roughly when it went"
        );
    }

    [Test]
    public void A_run_with_no_debugger_never_looks_as_though_one_just_left()
    {
        // The mistake worth guarding against: recording the detach timestamp on every call with false
        // rather than on the transition. It would then always read "detached a moment ago", which would
        // excuse every freeze ever reported — a suppression that fires for everybody instead of nobody.
        using var writer = new DoctorChannelWriter(TestProcessId);

        for (var i = 0; i < 5; i++)
            writer.SetDebuggerAttached(false);

        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var snapshot), Is.True);
        Assert.That(snapshot!.DebuggerEverAttached, Is.False);
        Assert.That(
            snapshot.DebuggerLastDetachedAge,
            Is.EqualTo(TimeSpan.MaxValue),
            "a debugger that was never there cannot have just detached"
        );
    }

    [Test]
    public void Re_attaching_moves_the_detach_time_forward_rather_than_keeping_the_first_one()
    {
        // Attach, detach, attach, detach. What matters for judging a gap is the LAST time a debugger left,
        // not the first, so a stale timestamp must not survive a second visit.
        using var writer = new DoctorChannelWriter(TestProcessId);

        // Note this compares AGES, each measured at the moment of its own read, so two fresh detaches both
        // read as roughly zero. What distinguishes them is letting the first one get old first: the age has
        // to grow while nothing happens, then drop back when a second departure replaces the timestamp.
        writer.SetDebuggerAttached(true);
        writer.SetDebuggerAttached(false);

        Thread.Sleep(80);
        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var aged), Is.True);
        var agedAge = aged!.DebuggerLastDetachedAge;
        Assert.That(
            agedAge,
            Is.GreaterThan(TimeSpan.FromMilliseconds(30)),
            "setup: the recorded time should stay put and therefore age"
        );
        Assert.That(agedAge, Is.Not.EqualTo(TimeSpan.MaxValue), "setup: a detach was recorded");

        writer.SetDebuggerAttached(true);
        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var reattached), Is.True);
        Assert.That(reattached!.DebuggerAttached, Is.True, "attached again");

        writer.SetDebuggerAttached(false);
        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var second), Is.True);
        Assert.That(
            second!.DebuggerLastDetachedAge,
            Is.LessThan(agedAge),
            "the second departure should replace the first, not be ignored in favour of it"
        );
    }

    [Test]
    public void PayloadBytes_is_the_end_of_the_last_field()
    {
        // The mistake this catches is appending a field and forgetting to grow PayloadBytes. The field
        // would then sit outside the written region, so every reader would correctly conclude it was not
        // there — a new field that silently never arrives, with nothing failing anywhere.
        var end = DoctorChannelLayout.Fields.Max(f => f.Offset + f.Size);

        Assert.That(
            DoctorChannelLayout.PayloadBytes,
            Is.EqualTo(end),
            "PayloadBytes must be one past the last byte any field occupies"
        );
        Assert.That(
            DoctorChannelLayout.PayloadBytes % 8,
            Is.Zero,
            "the payload must end 8-byte aligned, so an appended 64-bit field is aligned too"
        );
        Assert.That(
            DoctorChannelLayout.BaselinePayloadBytes,
            Is.LessThanOrEqualTo(DoctorChannelLayout.PayloadBytes),
            "the baseline is a floor; the layout can only grow past it"
        );
    }

    [Test]
    public void No_two_fields_overlap_and_none_escapes_the_page()
    {
        // Guards the other half of "existing fields never move": a new field appended at the wrong offset,
        // or one sized wrongly, would quietly corrupt its neighbour on every write.
        var ordered = DoctorChannelLayout.Fields.OrderBy(f => f.Offset).ToList();

        Assert.That(
            ordered,
            Is.EqualTo(DoctorChannelLayout.Fields),
            "fields should be declared in order"
        );

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            Assert.That(
                current.Offset,
                Is.GreaterThanOrEqualTo(previous.Offset + previous.Size),
                $"{current.Name} overlaps {previous.Name}"
            );
        }

        Assert.That(
            ordered[^1].Offset + ordered[^1].Size,
            Is.LessThanOrEqualTo(DoctorChannelLayout.Size),
            "the last field must fit inside the page"
        );
    }

    [Test]
    public void The_writer_never_touches_a_byte_beyond_PayloadBytes()
    {
        // The other way to get PayloadBytes wrong: append a field, write it, and forget to grow the
        // constant. The layout test above catches that only if the field was added to the Fields table;
        // this catches it from the other end, by watching what the writer actually does to the page.
        //
        // Fill everything past PayloadBytes with a sentinel, exercise every writer method, and require the
        // tail to come back untouched.
        using var writer = new DoctorChannelWriter(TestProcessId);
        Assert.That(writer.IsOpen, Is.True, "setup: the channel should have been created");

        var tailStart = DoctorChannelLayout.PayloadBytes;
        var tailLength = DoctorChannelLayout.Size - tailStart;
        Assert.That(tailLength, Is.GreaterThan(0), "setup: there should be spare room to watch");

        using var file = MemoryMappedFile.OpenExisting(
            DoctorChannelLayout.NameFor(TestProcessId),
            MemoryMappedFileRights.ReadWrite
        );
        using var view = file.CreateViewAccessor(
            0,
            DoctorChannelLayout.Size,
            MemoryMappedFileAccess.ReadWrite
        );

        var sentinel = new byte[tailLength];
        for (var i = 0; i < sentinel.Length; i++)
            sentinel[i] = 0xAB;
        view.WriteArray(tailStart, sentinel, 0, sentinel.Length);

        // Sanity check the sentinel actually landed, so a test that cannot fail is not mistaken for a pass.
        var planted = new byte[tailLength];
        view.ReadArray(tailStart, planted, 0, planted.Length);
        Assert.That(planted, Is.EqualTo(sentinel), "setup: the sentinel should have been written");

        // Everything the writer can do, including an activity far longer than its field, which is the most
        // likely thing to run off the end.
        writer.RecordUiTick();
        writer.RecordWatchdogTick();
        writer.SetActivity(new string('x', DoctorChannelLayout.ActivityMaxBytes * 4));
        writer.SetActivity("Publishing to BloomPUB");
        writer.SetLongOperation(true);
        writer.SetDebuggerAttached(true);
        writer.SetShutdownPhase(BloomShutdownPhase.LogWritten);
        writer.SetServerWorkerCounts(5, 6, workers: 8, queued: 2);
        writer.RecordCleanExit();

        var after = new byte[tailLength];
        view.ReadArray(tailStart, after, 0, after.Length);
        Assert.That(
            after,
            Is.EqualTo(sentinel),
            "the writer wrote past PayloadBytes: either grow the constant or fix the write"
        );
    }

    [Test]
    public void A_reader_rejects_a_page_whose_payload_extent_is_impossible()
    {
        // A created-but-uninitialised section is zero-filled, and a page of zeroes otherwise looks settled
        // and sane. It must never be read as data.
        using var writer = new DoctorChannelWriter(TestProcessId);
        Assert.That(
            DoctorChannelReader.TryRead(TestProcessId, out var good),
            Is.True,
            "setup: a real channel should read"
        );
        Assert.That(
            good!.PayloadBytes,
            Is.EqualTo(DoctorChannelLayout.PayloadBytes),
            "setup: the writer should have recorded its extent"
        );

        using var file = MemoryMappedFile.OpenExisting(
            DoctorChannelLayout.NameFor(TestProcessId),
            MemoryMappedFileRights.ReadWrite
        );
        using var view = file.CreateViewAccessor(
            0,
            DoctorChannelLayout.Size,
            MemoryMappedFileAccess.ReadWrite
        );

        // Too small to hold the generation-1 fields we are about to read.
        view.Write(PayloadBytesOffset, DoctorChannelLayout.BaselinePayloadBytes - 1);
        Assert.That(
            DoctorChannelReader.TryRead(TestProcessId, out _),
            Is.False,
            "a payload smaller than the baseline cannot be trusted"
        );

        // Zero, which is what an uninitialised page says.
        view.Write(PayloadBytesOffset, 0);
        Assert.That(
            DoctorChannelReader.TryRead(TestProcessId, out _),
            Is.False,
            "an uninitialised page must not read as data"
        );

        // Bigger than the page itself.
        view.Write(PayloadBytesOffset, DoctorChannelLayout.Size + 8);
        Assert.That(
            DoctorChannelReader.TryRead(TestProcessId, out _),
            Is.False,
            "a payload larger than the page is impossible"
        );

        // And it recovers once the value is sane again, so the rejection is about the value and not a
        // one-way latch.
        view.Write(PayloadBytesOffset, DoctorChannelLayout.PayloadBytes);
        Assert.That(
            DoctorChannelReader.TryRead(TestProcessId, out _),
            Is.True,
            "a sane extent should read again"
        );
    }

    [Test]
    public void A_reader_built_against_a_larger_layout_still_accepts_an_older_writer()
    {
        // The case the whole scheme exists for: a Doctor that knows about fields a Bloom is too old to
        // write. It must read what IS there rather than rejecting the page. Simulated by writing an extent
        // equal to the baseline, which is what such a Bloom would record.
        using var writer = new DoctorChannelWriter(TestProcessId);
        writer.SetActivity("Saving Foo.htm");
        writer.RecordUiTick();

        using var file = MemoryMappedFile.OpenExisting(
            DoctorChannelLayout.NameFor(TestProcessId),
            MemoryMappedFileRights.ReadWrite
        );
        using var view = file.CreateViewAccessor(
            0,
            DoctorChannelLayout.Size,
            MemoryMappedFileAccess.ReadWrite
        );
        view.Write(PayloadBytesOffset, DoctorChannelLayout.BaselinePayloadBytes);

        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var snapshot), Is.True);
        Assert.That(
            snapshot!.PayloadBytes,
            Is.EqualTo(DoctorChannelLayout.BaselinePayloadBytes),
            "the reader should report what the writer claimed, not its own extent"
        );
        Assert.That(
            snapshot.Activity,
            Is.EqualTo("Saving Foo.htm"),
            "the fields that ARE present must still be read"
        );
        Assert.That(snapshot.UiTicks, Is.EqualTo(1));
    }

    [Test]
    public void What_Bloom_writes_is_what_the_Doctor_reads()
    {
        using var writer = new DoctorChannelWriter(TestProcessId);
        Assert.That(writer.IsOpen, Is.True, "setup: the channel should have been created");

        writer.SetActivity("Publishing to BloomPUB: compressing images");
        writer.SetLongOperation(true);
        writer.SetDebuggerAttached(false);
        writer.SetServerWorkerCounts(busy: 7, blocked: 3, workers: 9, queued: 0);
        writer.SetShutdownPhase(BloomShutdownPhase.None);
        writer.RecordUiTick();
        writer.RecordWatchdogTick();

        Assert.That(
            DoctorChannelReader.TryRead(TestProcessId, out var snapshot),
            Is.True,
            "the Doctor should be able to read what Bloom just wrote"
        );
        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.ProcessId, Is.EqualTo(TestProcessId));
            Assert.That(
                snapshot.Activity,
                Is.EqualTo("Publishing to BloomPUB: compressing images")
            );
            Assert.That(snapshot.LongOperationInProgress, Is.True);
            Assert.That(snapshot.DebuggerAttached, Is.False);
            Assert.That(snapshot.ServerBusyWorkers, Is.EqualTo(7));
            Assert.That(snapshot.ServerBlockedWorkers, Is.EqualTo(3));
            Assert.That(snapshot.ServerWorkers, Is.EqualTo(9));
            Assert.That(snapshot.ServerQueuedRequests, Is.EqualTo(0));
            Assert.That(snapshot.UiTicks, Is.EqualTo(1));
            Assert.That(snapshot.WatchdogTicks, Is.EqualTo(1));
            Assert.That(snapshot.CleanExitRecorded, Is.False);
        });
    }

    [Test]
    public void The_heartbeat_ages_as_time_passes()
    {
        // The age, not the count, is what the detector uses — so it has to be real. Both sides read
        // Environment.TickCount64, which is comparable across processes, unlike a wall clock.
        using var writer = new DoctorChannelWriter(TestProcessId);
        writer.RecordUiTick();

        DoctorChannelReader.TryRead(TestProcessId, out var fresh);
        Thread.Sleep(120);
        DoctorChannelReader.TryRead(TestProcessId, out var older);

        Assert.That(fresh!.UiHeartbeatAge, Is.LessThan(TimeSpan.FromMilliseconds(100)));
        Assert.That(
            older!.UiHeartbeatAge,
            Is.GreaterThan(fresh.UiHeartbeatAge),
            "a heartbeat that is not being bumped must look older as time passes"
        );
    }

    [Test]
    public void A_heartbeat_that_never_ticked_reads_as_infinitely_old_rather_than_as_fresh()
    {
        // The dangerous failure direction: an unticked heartbeat must not read as "just now", or a Bloom
        // that wedged during startup would look perfectly healthy forever.
        using var writer = new DoctorChannelWriter(TestProcessId);
        writer.SetActivity("starting up");

        DoctorChannelReader.TryRead(TestProcessId, out var snapshot);

        Assert.That(snapshot!.UiHeartbeatAge, Is.EqualTo(TimeSpan.MaxValue));
        Assert.That(snapshot.WatchdogHeartbeatAge, Is.EqualTo(TimeSpan.MaxValue));
    }

    [Test]
    public void A_Bloom_that_publishes_nothing_is_simply_absent_not_an_error()
    {
        // Every Bloom in the field today is this case, so it must be quiet and cheap: the Doctor falls
        // back to watching from outside.
        var read = DoctorChannelReader.TryRead(TestProcessId + 12345, out var snapshot);

        Assert.That(read, Is.False);
        Assert.That(snapshot, Is.Null);
    }

    [Test]
    public void The_clean_exit_proof_and_shutdown_phase_survive_for_the_reader()
    {
        using var writer = new DoctorChannelWriter(TestProcessId);
        writer.SetShutdownPhase(BloomShutdownPhase.LogWritten);
        writer.RecordCleanExit();

        DoctorChannelReader.TryRead(TestProcessId, out var snapshot);

        Assert.That(snapshot!.ShutdownPhase, Is.EqualTo(BloomShutdownPhase.LogWritten));
        Assert.That(snapshot.CleanExitRecorded, Is.True);
    }

    [Test]
    public void An_over_long_activity_string_is_truncated_rather_than_corrupting_the_page()
    {
        // Activity text comes from Bloom's own breadcrumbs, which include file paths; a long one must not
        // run over the next field.
        using var writer = new DoctorChannelWriter(TestProcessId);
        writer.SetActivity(new string('x', DoctorChannelLayout.ActivityMaxBytes * 3));
        writer.SetServerWorkerCounts(busy: 5, blocked: 1, workers: 6, queued: 0);

        DoctorChannelReader.TryRead(TestProcessId, out var snapshot);

        Assert.That(
            snapshot!.Activity.Length,
            Is.LessThan(DoctorChannelLayout.ActivityMaxBytes),
            "it must have been truncated"
        );
        Assert.That(
            snapshot.ServerBusyWorkers,
            Is.EqualTo(5),
            "and it must not have overwritten the field that follows it"
        );
    }

    [Test]
    public void An_activity_string_with_multibyte_characters_survives_being_written_and_read()
    {
        // Regression test. Truncating on a character boundary was added so a cut book title could not leave a
        // broken byte in the page — and the first version of that check read one byte past the end whenever no
        // truncation was needed. The resulting exception was swallowed, which left the write sequence at an odd
        // value, which every reader treats as "a write is in progress" — silently disabling the channel for the
        // rest of the run. So this test covers both the short case and the over-long one.
        using var writer = new DoctorChannelWriter(TestProcessId);

        writer.SetActivity("Publishing “Ekkitaaki Fulfulde” — étape 2");
        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var shortOne), Is.True);
        Assert.That(shortOne!.Activity, Is.EqualTo("Publishing “Ekkitaaki Fulfulde” — étape 2"));

        // Over-long, ending mid-character if cut naively.
        writer.SetActivity(new string('é', DoctorChannelLayout.ActivityMaxBytes));
        Assert.That(
            DoctorChannelReader.TryRead(TestProcessId, out var longOne),
            Is.True,
            "the channel must still be readable after a truncating write"
        );
        Assert.That(
            longOne!.Activity,
            Does.Not.Contain("�"),
            "and must not contain a replacement character from a half-written multi-byte sequence"
        );

        // The channel must still work afterwards — the point of keeping the sequence even.
        writer.RecordUiTick();
        Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var after), Is.True);
        Assert.That(after!.UiTicks, Is.EqualTo(1));
    }

    [Test]
    public void Writing_never_throws_even_when_the_channel_could_not_be_created()
    {
        // Two writers for one pid: the second cannot create the section. Bloom must survive that without
        // noticing, because publishing diagnostics is never worth failing a startup over.
        using var first = new DoctorChannelWriter(TestProcessId);
        using var second = new DoctorChannelWriter(TestProcessId);

        Assert.That(
            second.IsOpen,
            Is.False,
            "setup: the second writer should have failed to create it"
        );
        Assert.DoesNotThrow(() =>
        {
            second.RecordUiTick();
            second.SetActivity("this goes nowhere");
            second.RecordCleanExit();
        });
    }
}
