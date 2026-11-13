using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The dump taken when a Bloom first looks frozen, so a report gathered after the user has ended it still
/// has one (BL-17092: a minute-long freeze, ended by the user at about the moment the Doctor would have
/// dumped it, filed with no dump at all).
/// </summary>
[TestFixture]
public class EarlyFreezeDumpTests
{
    private string _root = null!;
    private int _dumpsWritten;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "EarlyFreezeDumpTests-" + Guid.NewGuid().ToString("N")
        );
        _dumpsWritten = 0;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>A keeper whose dumps are written at once, on the calling thread, as a small text file.</summary>
    private EarlyFreezeDump Keeper(bool writerFails = false) =>
        new(
            4242,
            _root,
            (processId, path) =>
            {
                _dumpsWritten++;
                if (writerFails)
                    throw new InvalidOperationException("the diagnostics pipe did not answer");
                File.WriteAllText(path, $"a dump of {processId}");
            },
            inBackground: work => Task.FromResult(work())
        );

    /// <summary>What a report claiming the dump ends up with: null whether none was held or none was made.</summary>
    private static async Task<EarlyFreezeDump.Taken?> Claim(EarlyFreezeDump keeper) =>
        keeper.Claim() is { } claim ? await claim : null;

    [Test]
    public void Claim_NothingHeld_ReturnsNullAtOnce()
    {
        // So that a report about a Bloom that never looked frozen has nothing to wait for.
        using var keeper = Keeper();

        Assert.That(keeper.Claim(), Is.Null);
    }

    [Test]
    public async Task Claim_AfterSuspect_ReturnsTheDumpTaken()
    {
        using var keeper = Keeper();

        keeper.Observe(TargetState.Suspect);
        var taken = await Claim(keeper);

        Assert.That(taken, Is.Not.Null, "becoming Suspect should have taken a dump");
        Assert.That(File.ReadAllText(taken!.DumpPath), Is.EqualTo("a dump of 4242"));
        Assert.That(Path.GetFileName(taken.DumpPath), Is.EqualTo("bloom-4242.dmp"));
    }

    [Test]
    public async Task Observe_StillSuspectThenFrozen_TakesOnlyOneDump()
    {
        using var keeper = Keeper();

        keeper.Observe(TargetState.Suspect);
        keeper.Observe(TargetState.Suspect);
        keeper.Observe(TargetState.Frozen);
        keeper.Observe(TargetState.Frozen);

        Assert.That(_dumpsWritten, Is.EqualTo(1), "one freeze should cost Bloom one early dump");
        Assert.That(await Claim(keeper), Is.Not.Null);
    }

    [Test]
    public async Task Observe_FrozenWithoutSuspect_TakesNoDump()
    {
        // A Bloom adopted when already frozen goes straight to Frozen, which gathers (and dumps) at once.
        using var keeper = Keeper();

        keeper.Observe(TargetState.Frozen);

        Assert.That(_dumpsWritten, Is.EqualTo(0));
        Assert.That(await Claim(keeper), Is.Null);
    }

    [Test]
    public async Task Observe_HealthyAfterSuspect_DiscardsTheDump()
    {
        using var keeper = Keeper();
        keeper.Observe(TargetState.Suspect);
        var folders = Directory.GetDirectories(_root);
        Assert.That(folders, Has.Length.EqualTo(1), "sanity check: the dump should be on disk");

        keeper.Observe(TargetState.Healthy);

        Assert.That(
            Directory.Exists(folders[0]),
            Is.False,
            "a freeze that recovered keeps no dump"
        );
        Assert.That(await Claim(keeper), Is.Null);
    }

    [Test]
    public async Task Observe_SuspectAgainAfterRecovering_TakesAFreshDump()
    {
        using var keeper = Keeper();
        keeper.Observe(TargetState.Suspect);
        keeper.Observe(TargetState.Healthy);

        keeper.Observe(TargetState.Suspect);

        Assert.That(_dumpsWritten, Is.EqualTo(2), "a second episode deserves its own dump");
        Assert.That(await Claim(keeper), Is.Not.Null);
    }

    [Test]
    public async Task Claim_Twice_SecondGetsNothing()
    {
        using var keeper = Keeper();
        keeper.Observe(TargetState.Suspect);

        var first = await Claim(keeper);
        var second = await Claim(keeper);

        Assert.That(first, Is.Not.Null, "sanity check");
        Assert.That(second, Is.Null, "the file belongs to whoever claimed it first");
    }

    [Test]
    public async Task Claim_WriterFailed_ReturnsNullAndLeavesNothingBehind()
    {
        using var keeper = Keeper(writerFails: true);

        keeper.Observe(TargetState.Suspect);
        var taken = await Claim(keeper);

        Assert.That(_dumpsWritten, Is.EqualTo(1), "sanity check: it should have tried");
        Assert.That(taken, Is.Null);
        Assert.That(Directory.GetDirectories(_root), Is.Empty);
    }

    [Test]
    public void Dispose_WithAnUnclaimedDump_DeletesIt()
    {
        var keeper = Keeper();
        keeper.Observe(TargetState.Suspect);
        Assert.That(Directory.GetDirectories(_root), Has.Length.EqualTo(1), "sanity check");

        keeper.Dispose();

        Assert.That(Directory.GetDirectories(_root), Is.Empty);
    }

    [Test]
    public async Task DeleteWhenWritten_AClaimedDump_RemovesItsFolder()
    {
        using var keeper = Keeper();
        keeper.Observe(TargetState.Suspect);
        var claimed = keeper.Claim()!;
        Assert.That(File.Exists((await claimed)!.DumpPath), Is.True, "sanity check");

        await EarlyFreezeDump.DeleteWhenWritten(claimed);

        Assert.That(Directory.GetDirectories(_root), Is.Empty);
    }

    [Test]
    public async Task DeleteWhenWritten_DumpStillBeingWritten_DeletesItOnceWritten()
    {
        // A report that gave up waiting for the dump must not leave it behind when it does arrive.
        var finishWriting = new TaskCompletionSource<EarlyFreezeDump.Taken?>();
        var folder = Path.Combine(_root, "early-4242-x");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "bloom-4242.dmp");

        var deleted = EarlyFreezeDump.DeleteWhenWritten(finishWriting.Task);
        File.WriteAllText(path, "a dump of 4242");
        Assert.That(Directory.Exists(folder), Is.True, "nothing should go before it is written");
        finishWriting.SetResult(new EarlyFreezeDump.Taken(path, DateTimeOffset.UtcNow));
        await deleted;

        Assert.That(Directory.Exists(folder), Is.False);
    }
}
