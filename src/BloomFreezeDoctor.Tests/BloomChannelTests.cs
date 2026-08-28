using System.Linq;
using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

[TestFixture]
public class BloomChannelTests
{
    [TestCase(
        @"C:\Users\jt\AppData\Local\Bloom\current\Bloom.exe",
        "Release",
        Description = "an ordinary installed Bloom"
    )]
    [TestCase(@"C:\Users\jt\AppData\Local\BloomBeta\current\BloomBeta.exe", "Beta")]
    [TestCase(@"C:\Users\jt\AppData\Local\BloomAlpha\current\BloomAlpha.exe", "Alpha")]
    [TestCase(
        @"C:\Users\jt\AppData\Local\BloomBetaInternal\current\BloomBetaInternal.exe",
        "BetaInternal"
    )]
    [TestCase(@"C:\github\BloomDesktop\output\Debug\x64\Bloom.exe", "Developer/Debug")]
    [TestCase(@"C:\github\BloomDesktop\output\Release\x64\Bloom.exe", "Developer/Release")]
    [TestCase(
        @"C:\github\BloomDesktop.worktrees\Version6.4\output\Debug\AnyCPU\Bloom.exe",
        "Developer/Debug",
        Description = "a worktree build, which is where much of our development happens"
    )]
    public void Channel_comes_out_of_the_path(string exePath, string expected)
    {
        Assert.That(BloomChannel.DeriveFromExePath(exePath), Is.EqualTo(expected));
    }

    /// <summary>Where an installed Bloom of the given channel lives.</summary>
    private static string InstalledPath(string processName) =>
        $@"C:\Users\jt\AppData\Local\{processName}\current\{processName}.exe";

    [Test]
    public void Every_installed_channel_has_a_process_name_the_Doctor_sweeps_for()
    {
        // The list is what the Doctor searches for to find Blooms nobody told it about, and what "Restart
        // Bloom" searches to relaunch one. A channel missing from it is a channel the Doctor never watches
        // at all - which is silent, because there is nothing to see when a tool correctly does nothing.
        // ReleaseInternal was missing exactly that way.
        //
        // Pinned by value, so adding a channel is a deliberate edit here as well. The right-hand side is
        // what Bloom's own ApplicationUpdateSupport.ChannelName reports for that install.
        var expected = new[]
        {
            "Bloom=Release",
            "BloomAlpha=Alpha",
            "BloomBeta=Beta",
            "BloomBetaInternal=BetaInternal",
            "BloomReleaseInternal=ReleaseInternal",
        };

        // Derived from the installed layout, so this also proves the names and the channel derivation agree:
        // a name in the list that DeriveFromExePath read differently would be a Bloom we watch and then
        // mislabel on its own card.
        var actual = BloomChannel
            .InstalledBloomProcessNames.Select(name =>
                $"{name}={BloomChannel.DeriveFromExePath(InstalledPath(name))}"
            )
            .ToArray();

        Assert.That(
            actual,
            Is.EqualTo(expected),
            "the installed channels the Doctor knows how to find"
        );
    }

    [Test]
    public void No_swept_channel_is_mistaken_for_a_developer_build()
    {
        // Every name here is an INSTALLED Bloom, so none of them may look like a developer build: that
        // would silently stop the Doctor filing from a real user's machine.
        foreach (var name in BloomChannel.InstalledBloomProcessNames)
        {
            var channel = BloomChannel.DeriveFromExePath(InstalledPath(name));
            Assert.That(
                BloomChannel.IsDeveloperChannel(channel),
                Is.False,
                $"{name} derived the channel '{channel}', which reads as a developer build"
            );
        }
    }

    [Test]
    public void A_developer_build_is_recognised_from_its_exe_not_only_its_dll()
    {
        // The trap this guards: Bloom's own ChannelName tests for a path ending in "Bloom.dll",
        // because it asks about its entry assembly. From outside we see the process, whose main
        // module is Bloom.exe. Requiring ".dll" here would call every developer build "Release" —
        // the dangerous direction, since that is what would file cards from a `pnpm go` session.
        var exe = BloomChannel.DeriveFromExePath(
            @"C:\github\BloomDesktop\output\Debug\x64\Bloom.exe"
        );
        var dll = BloomChannel.DeriveFromExePath(
            @"C:\github\BloomDesktop\output\Debug\x64\Bloom.dll"
        );

        Assert.That(exe, Is.EqualTo("Developer/Debug"), "the .exe form must be recognised");
        Assert.That(dll, Is.EqualTo("Developer/Debug"), "and so must the .dll form");
        Assert.That(BloomChannel.IsDeveloperChannel(exe), Is.True);
    }

    [TestCase("Release", false)]
    [TestCase("Beta", false)]
    [TestCase("Developer/Debug", true)]
    [TestCase("Developer/Release", true)]
    public void Developer_channels_are_flagged(string channel, bool expected)
    {
        Assert.That(BloomChannel.IsDeveloperChannel(channel), Is.EqualTo(expected));
    }

    [Test]
    public void Headless_console_verb_runs_are_recognised()
    {
        // These legitimately have no window, so without this check every one of them would look
        // like the zombie of plan section 3.6.
        Assert.That(
            BloomChannel.IsHeadlessRun(@"""C:\...\Bloom.exe"" hydrate --bookpath foo"),
            Is.True
        );
        Assert.That(BloomChannel.IsHeadlessRun(@"""C:\...\Bloom.exe"" upload C:\books"), Is.True);
    }

    [Test]
    public void An_automation_run_is_not_treated_as_headless()
    {
        // `--automation` says nothing about whether there is a window: in Bloom it means multi-instance,
        // print the ports for the launcher, and show them in the title. It shows the ordinary window.
        //
        // This is pinned because getting it wrong was silent and expensive: `go.sh` passes the flag on
        // EVERY launch, so calling it headless made the Doctor ignore the one Bloom a developer watches
        // their own changes in, and nothing anywhere looked broken.
        Assert.That(
            BloomChannel.IsHeadlessRun(
                @"""C:\...\Bloom.dll"" --automation --label /x/ --vite-port 50928"
            ),
            Is.False
        );
    }

    [Test]
    public void A_go_sh_Bloom_is_watched_but_still_never_files()
    {
        // The pair of facts that has to hold together after `--automation` stopped meaning headless:
        // we watch such a Bloom (previous test), and it still cannot reach the tracker. The guard that
        // does the second job is the CHANNEL - a source build lives in output/Debug - so it holds no
        // matter what the command line says.
        var facts = new BloomTargetFacts
        {
            ProcessId = 1234,
            ExePath = @"C:\github\BloomDesktop\output\Debug\AnyCPU\Bloom.exe",
            Channel = BloomChannel.DeriveFromExePath(
                @"C:\github\BloomDesktop\output\Debug\AnyCPU\Bloom.exe"
            ),
            CommandLine =
                @"""C:\github\BloomDesktop\output\Debug\AnyCPU\Bloom.dll"" --automation --label go/ --vite-port 50928",
            StartTime = new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Local),
        };

        Assert.That(
            BloomChannel.IsHeadlessRun(facts.CommandLine),
            Is.False,
            "a go.sh Bloom must be watched"
        );
        Assert.That(facts.NeverFile, Is.True, "and must still never file");
    }

    [Test]
    public void An_ordinary_launch_is_not_mistaken_for_a_headless_run()
    {
        Assert.That(
            BloomChannel.IsHeadlessRun(@"""C:\Users\jt\AppData\Local\Bloom\current\Bloom.exe"" "),
            Is.False
        );
    }

    [Test]
    public void A_collection_path_containing_a_verb_word_does_not_silence_a_real_Bloom()
    {
        // "upload" as a whole argument means the console verb; inside a path it means nothing. Get
        // this wrong and a user with an unlucky folder name gets no reports at all.
        Assert.That(
            BloomChannel.IsHeadlessRun(
                @"""C:\Bloom\current\Bloom.exe"" ""C:\Users\jt\Documents\Bloom\upload\my.bloomCollection"""
            ),
            Is.False
        );
    }
}
