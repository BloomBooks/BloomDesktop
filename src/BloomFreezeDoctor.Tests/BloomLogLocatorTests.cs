using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The log-matching rules. The headline test here reproduces a real situation measured on a
/// developer's machine during the spike, where choosing the newest log would have attached the wrong
/// one.
/// </summary>
[TestFixture]
public class BloomLogLocatorTests
{
    private static BloomLogCandidate Candidate(
        string path,
        string launchedAt,
        string commandLine,
        string lastWrite
    ) =>
        new()
        {
            Path = path,
            LaunchedAtTimeOfDay = TimeSpan.Parse(launchedAt),
            LaunchCommandLine = commandLine,
            LastWriteTime = DateTime.Parse(lastWrite),
        };

    [Test]
    public void A_Bloom_started_just_before_midnight_still_finds_its_log()
    {
        // The log line carries a time of day and nothing else, so the comparison has to go the short way
        // round the clock. Subtracting literally made these fifty seconds look like nearly twenty-four
        // hours, putting every Bloom launched within a minute or so of midnight outside the tolerance -
        // and the report then said, wrongly and with no hint of why, that no log could be found.
        var candidates = new[]
        {
            Candidate(
                @"C:\Temp\SIL\Bloom\Log.txt",
                "00:00:20",
                @"C:\Program Files\Bloom\Bloom.dll",
                "2026-08-18 00:05"
            ),
        };

        var chosen = BloomLogLocator.ChooseFor(
            candidates,
            @"C:\Program Files\Bloom\Bloom.exe",
            DateTime.Parse("2026-08-17 23:59:30")
        );

        Assert.That(
            chosen,
            Is.Not.Null,
            "fifty seconds apart across midnight is a match, not a day's difference"
        );
        Assert.That(chosen!.Value.Path, Does.EndWith("Log.txt"));
    }

    [Test]
    public void Picks_the_log_whose_launch_line_matches_the_process()
    {
        var candidates = new[]
        {
            Candidate(
                @"C:\Temp\SIL\Bloom\Log.txt",
                "11:43:06",
                @"C:\github\BloomDesktop\output\Debug\x64\Bloom.dll",
                "2026-08-17 16:24"
            ),
            Candidate(
                @"C:\Temp\SIL\Bloom\Log-tmpother.txt",
                "12:02:17",
                @"C:\github\BloomDesktop.worktrees\Version6.4\output\Debug\AnyCPU\Bloom.dll --automation",
                "2026-08-17 17:16"
            ),
        };

        var chosen = BloomLogLocator.ChooseFor(
            candidates,
            @"C:\github\BloomDesktop\output\Debug\x64\Bloom.exe",
            DateTime.Parse("2026-08-17 11:43:05")
        );

        Assert.That(chosen, Is.Not.Null, "a matching log should have been found");
        Assert.That(chosen!.Value.Path, Does.EndWith("Log.txt"));
    }

    [Test]
    public void The_newest_log_is_NOT_the_answer_and_that_is_the_whole_point()
    {
        // Measured during the spike: the most recently modified log on the machine belonged to a
        // Bloom in a different worktree (modified 17:16), while the log belonging to the live process
        // was written nearly an hour earlier (16:24). Bloom recreates Log.txt each run and only falls
        // back to Log-tmpXXXX.txt when another Bloom already holds Log.txt — so in the
        // restart-after-a-freeze case, which is the case we exist for, newest-wins is systematically
        // wrong rather than merely unlucky.
        var newest = Candidate(
            @"C:\Temp\SIL\Bloom\Log-tmpnewest.txt",
            "12:02:17",
            @"C:\other\worktree\output\Debug\AnyCPU\Bloom.dll",
            "2026-08-17 17:16"
        );
        var older = Candidate(
            @"C:\Temp\SIL\Bloom\Log.txt",
            "11:43:06",
            @"C:\github\BloomDesktop\output\Debug\x64\Bloom.dll",
            "2026-08-17 16:24"
        );

        // Sanity check on the fixture itself, so this test cannot pass for the wrong reason.
        Assert.That(
            newest.LastWriteTime,
            Is.GreaterThan(older.LastWriteTime),
            "fixture must actually present the wrong log as the newest"
        );

        var chosen = BloomLogLocator.ChooseFor(
            new[] { newest, older },
            @"C:\github\BloomDesktop\output\Debug\x64\Bloom.exe",
            DateTime.Parse("2026-08-17 11:43:05")
        );

        Assert.That(
            chosen!.Value.Path,
            Does.EndWith("Log.txt"),
            "must match on identity, not recency"
        );
    }

    [Test]
    public void A_log_from_the_same_build_but_a_different_run_is_rejected()
    {
        // Same folder, but launched hours from this process's start: a previous session's log.
        var candidates = new[]
        {
            Candidate(
                @"C:\Temp\SIL\Bloom\Log.txt",
                "08:15:00",
                @"C:\github\BloomDesktop\output\Debug\x64\Bloom.dll",
                "2026-08-17 09:00"
            ),
        };

        var chosen = BloomLogLocator.ChooseFor(
            candidates,
            @"C:\github\BloomDesktop\output\Debug\x64\Bloom.exe",
            DateTime.Parse("2026-08-17 14:00:00")
        );

        Assert.That(chosen, Is.Null, "better no log than a log from the wrong run");
    }

    [Test]
    public void Matching_tolerates_the_delay_between_process_start_and_the_log_line()
    {
        // The log line is written after the process starts, and startup work happens in between.
        var candidates = new[]
        {
            Candidate(
                @"C:\Temp\SIL\Bloom\Log.txt",
                "11:43:50",
                @"C:\Bloom\current\Bloom.dll",
                "2026-08-17 11:44"
            ),
        };

        var chosen = BloomLogLocator.ChooseFor(
            candidates,
            @"C:\Bloom\current\Bloom.exe",
            DateTime.Parse("2026-08-17 11:43:05")
        );

        Assert.That(chosen, Is.Not.Null, "45s of startup is well within tolerance");
    }

    [Test]
    public void When_two_runs_of_one_build_both_match_the_closer_launch_time_wins()
    {
        var closer = Candidate(
            @"C:\Temp\SIL\Bloom\Log.txt",
            "11:43:10",
            @"C:\Bloom\current\Bloom.dll",
            "2026-08-17 11:50"
        );
        var further = Candidate(
            @"C:\Temp\SIL\Bloom\Log-tmpb.txt",
            "11:44:20",
            @"C:\Bloom\current\Bloom.dll",
            "2026-08-17 12:10"
        );

        var chosen = BloomLogLocator.ChooseFor(
            new[] { further, closer },
            @"C:\Bloom\current\Bloom.exe",
            DateTime.Parse("2026-08-17 11:43:05")
        );

        Assert.That(chosen!.Value.Path, Does.EndWith("Log.txt"));
    }

    [Test]
    public void A_log_with_no_launch_line_is_ignored_rather_than_guessed_at()
    {
        var candidates = new[]
        {
            new BloomLogCandidate
            {
                Path = @"C:\Temp\SIL\Bloom\Log-truncated.txt",
                LaunchedAtTimeOfDay = null,
                LaunchCommandLine = null,
                LastWriteTime = DateTime.Parse("2026-08-17 17:00"),
            },
        };

        var chosen = BloomLogLocator.ChooseFor(
            candidates,
            @"C:\Bloom\current\Bloom.exe",
            DateTime.Parse("2026-08-17 11:43:05")
        );

        Assert.That(chosen, Is.Null);
    }
}
