using BloomFreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The session file: the half of the contract that has to outlive the process.
///
/// Shared memory vanishes when the last handle closes, so a Bloom that crashes while no Doctor was watching
/// leaves nothing behind. These files are what a Doctor installed *after* the fact reads — which is one of
/// the explicit requirements — so the tests care particularly about what survives and what is kept.
/// </summary>
[TestFixture]
public class DoctorSessionTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "FreezeDoctorTests",
            "sessions-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (Exception) { }
    }

    [Test]
    public void The_shutdown_phase_is_written_as_a_name_not_a_number()
    {
        // The name is the compatibility surface for THIS file, so it is worth asserting on the bytes
        // rather than only on a round trip: a round trip would keep passing if the phases were renamed,
        // since both halves would move together, while every session file already on disk became
        // unreadable. Reading is tolerant of either form, so only writing needs pinning.
        var session = Session(777) with
        {
            Exit = new DoctorSessionExit
            {
                AtUtc = DateTimeOffset.UtcNow,
                ShutdownPhase = BloomShutdownPhase.SettingsSaved,
            },
        };

        DoctorSessionStore.TryWrite(session, _directory);

        var onDisk = File.ReadAllText(DoctorSessionStore.PathFor(777, _directory));
        Assert.That(
            onDisk,
            Does.Contain("\"SettingsSaved\""),
            "a person opening this file should read the phase, and an older file must stay readable"
        );
        Assert.That(onDisk, Does.Not.Contain("\"ShutdownPhase\": 2"), "not the bare number");
    }

    private DoctorSession Session(int pid, DateTimeOffset? started = null) =>
        new()
        {
            ProcessId = pid,
            StartedAtUtc = started ?? DateTimeOffset.UtcNow,
            ExePath = @"C:\Users\jt\AppData\Local\Bloom\current\Bloom.exe",
            Version = "6.5.0",
            Channel = "Release",
            CommandLine = "\"Bloom.exe\"",
            LogPath = @"C:\Temp\SIL\Bloom\Log.txt",
            HttpPort = 8089,
            CdpPort = 8091,
            CollectionName = "Fulfulde de l'ouest",
        };

    [Test]
    public void A_session_survives_a_round_trip()
    {
        Assert.That(DoctorSessionStore.TryWrite(Session(4242), _directory), Is.True);

        var read = DoctorSessionStore.TryRead(4242, _directory);

        Assert.That(read, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(read!.LogPath, Is.EqualTo(@"C:\Temp\SIL\Bloom\Log.txt"));
            Assert.That(read.CdpPort, Is.EqualTo(8091));
            Assert.That(read.HttpPort, Is.EqualTo(8089));
            Assert.That(read.CollectionName, Is.EqualTo("Fulfulde de l'ouest"));
            Assert.That(read.Exit, Is.Null, "a running Bloom has not exited");
        });
    }

    [Test]
    public void An_unreadable_file_is_absent_rather_than_fatal()
    {
        File.WriteAllText(DoctorSessionStore.PathFor(99, _directory), "{ not json at all");

        Assert.That(DoctorSessionStore.TryRead(99, _directory), Is.Null);
        Assert.That(
            DoctorSessionStore.ReadAll(_directory),
            Is.Empty,
            "and it must not stop the others being listed"
        );
    }

    [Test]
    public void A_schema_we_do_not_understand_is_refused_rather_than_misread()
    {
        // Better to fall back to watching from outside than to build a report on fields we have
        // misinterpreted.
        var fromTheFuture = Session(77) with
        {
            SchemaVersion = DoctorSessionStore.SchemaVersion + 1,
        };
        DoctorSessionStore.TryWrite(fromTheFuture, _directory);

        Assert.That(DoctorSessionStore.TryRead(77, _directory), Is.Null);
    }

    [Test]
    public void An_exit_record_says_how_far_shutdown_got()
    {
        var session = Session(555) with
        {
            Exit = new DoctorSessionExit
            {
                AtUtc = DateTimeOffset.UtcNow,
                ShutdownPhase = BloomShutdownPhase.LogWritten,
            },
        };
        DoctorSessionStore.TryWrite(session, _directory);

        var read = DoctorSessionStore.TryRead(555, _directory);

        Assert.That(read!.Exit, Is.Not.Null);
        Assert.That(read.Exit!.ShutdownPhase, Is.EqualTo(BloomShutdownPhase.LogWritten));
        Assert.That(read.Exit.ForcedByDoctor, Is.False, "this was an ordinary shutdown");
        Assert.That(read.BloomAlreadyReported, Is.False);
    }

    [Test]
    public void Bloom_can_record_that_it_already_reported_the_problem_itself()
    {
        // The point of this flag: a user filing a problem report by hand and a Doctor noticing the same
        // trouble is exactly the situation that would otherwise produce two cards about one problem.
        var session = Session(556) with
        {
            BloomAlreadyReported = true,
            ReportedId = "BL-16697",
        };
        DoctorSessionStore.TryWrite(session, _directory);

        var read = DoctorSessionStore.TryRead(556, _directory);

        Assert.That(read!.BloomAlreadyReported, Is.True);
        Assert.That(read.ReportedId, Is.EqualTo("BL-16697"));
    }

    [Test]
    public void Saying_Bloom_already_reported_a_problem_does_not_say_Bloom_has_ended()
    {
        // This is the shape of a bug Devin caught on the PR. The already-reported note used to be written
        // *inside* the exit record, so a user who filed a report and then carried on working left a live
        // Bloom described on disk as finished — which a reader takes as proof of an orderly shutdown, for a
        // process that may still go on to crash. The two facts are independent and now live in separate
        // fields.
        var session = Session(557) with
        {
            BloomAlreadyReported = true,
            ReportedId = "BL-16697",
        };
        DoctorSessionStore.TryWrite(session, _directory);

        var read = DoctorSessionStore.TryRead(557, _directory);

        Assert.That(read!.BloomAlreadyReported, Is.True, "the note is recorded");
        Assert.That(
            read.Exit,
            Is.Null,
            "but the run has NOT ended, and nothing may claim it has while Bloom is still running"
        );
    }

    [Test]
    public void An_exit_the_Doctor_forced_is_not_recorded_as_an_orderly_one()
    {
        // Ending a zombie goes through Environment.Exit, which runs the same shutdown path as a clean quit.
        // Without this distinction, a Bloom we had to end would look like one that shut down properly.
        var session = Session(558) with
        {
            Exit = new DoctorSessionExit
            {
                AtUtc = DateTimeOffset.UtcNow,
                ShutdownPhase = BloomShutdownPhase.None,
                ForcedByDoctor = true,
            },
        };
        DoctorSessionStore.TryWrite(session, _directory);

        var read = DoctorSessionStore.TryRead(558, _directory);

        Assert.That(read!.Exit!.ForcedByDoctor, Is.True);
    }

    [Test]
    public void Pruning_keeps_an_unexplained_exit_and_discards_an_explained_one()
    {
        // This is the rule that makes "a Doctor installed the day after a crash" work: the file with no
        // exit record is the evidence, so it must be the one that survives.
        var explained = Session(1001) with
        {
            Exit = new DoctorSessionExit
            {
                AtUtc = DateTimeOffset.UtcNow,
                ShutdownPhase = BloomShutdownPhase.ProjectContextDisposed,
            },
        };
        var unexplained = Session(1002);
        DoctorSessionStore.TryWrite(explained, _directory);
        DoctorSessionStore.TryWrite(unexplained, _directory);

        // Neither process is alive any more.
        DoctorSessionStore.Prune(_ => false, TimeSpan.FromDays(7), _directory);

        var remaining = DoctorSessionStore.ReadAll(_directory);
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(
            remaining[0].ProcessId,
            Is.EqualTo(1002),
            "the unexplained exit is precisely what a later Doctor comes looking for"
        );
    }

    [Test]
    public void Pruning_leaves_a_live_Bloom_alone_and_eventually_drops_an_ancient_one()
    {
        var live = Session(2001);
        var ancient = Session(2002, DateTimeOffset.UtcNow - TimeSpan.FromDays(30));
        DoctorSessionStore.TryWrite(live, _directory);
        DoctorSessionStore.TryWrite(ancient, _directory);

        DoctorSessionStore.Prune(pid => pid == 2001, TimeSpan.FromDays(7), _directory);

        var remaining = DoctorSessionStore.ReadAll(_directory).Select(s => s.ProcessId).ToList();
        Assert.That(remaining, Does.Contain(2001), "a running Bloom's file must never be removed");
        Assert.That(remaining, Does.Not.Contain(2002), "but an ancient one is not worth keeping");
    }
}
