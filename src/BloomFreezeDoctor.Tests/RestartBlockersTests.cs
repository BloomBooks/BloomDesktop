using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Which running Blooms actually stand between the user and a new Bloom.
///
/// Worth testing on its own because both ways of getting it wrong are quiet. Include too much and the
/// Doctor offers to kill somebody's working Bloom; include too little and it starts a Bloom that finds the
/// token held and vanishes, which looks exactly like the "Bloom will not start" the user came about.
/// </summary>
[TestFixture]
public class RestartBlockersTests
{
    private static LiveBloom Bloom(int pid, TargetState state, bool? holdsToken) =>
        new(pid, state, holdsToken);

    [Test]
    public void The_Bloom_holding_the_token_is_in_the_way()
    {
        Assert.That(
            RestartBlockers.IsInTheWay(Bloom(100, TargetState.Frozen, holdsToken: true)),
            Is.True
        );
    }

    [Test]
    public void A_Bloom_that_holds_no_token_is_not_in_the_way()
    {
        // The case that made this class necessary: an --automation run bypasses the token, so a developer
        // with two worktrees open has a live Bloom that blocks nothing. Until the Doctor started watching
        // those at all it could not arise; now it can, and asking to kill a healthy Bloom would be a
        // regression the user would meet before any freeze did.
        Assert.That(
            RestartBlockers.IsInTheWay(Bloom(101, TargetState.Healthy, holdsToken: false)),
            Is.False
        );
    }

    [Test]
    public void A_Bloom_that_did_not_say_is_treated_as_being_in_the_way()
    {
        // The asymmetry is the whole design. A Bloom that cannot tell us is an OLD Bloom - no session file,
        // or one written before the field existed - and an old Bloom holds the shared token just as well as
        // a new one. Guess "no" and we leave the real blocker running.
        Assert.That(
            RestartBlockers.IsInTheWay(Bloom(102, TargetState.Frozen, holdsToken: null)),
            Is.True,
            "silence must not be read as 'not blocking'"
        );
    }

    [Test]
    public void Only_the_blockers_are_kept()
    {
        var watched = new[]
        {
            Bloom(1, TargetState.Healthy, holdsToken: false), // an --automation run
            Bloom(2, TargetState.Frozen, holdsToken: true), // the real blocker
            Bloom(3, TargetState.Healthy, holdsToken: false), // another worktree
            Bloom(4, TargetState.Zombie, holdsToken: null), // too old to say
        };

        var inTheWay = RestartBlockers.InTheWay(watched);

        Assert.That(
            inTheWay.Select(bloom => bloom.ProcessId),
            Is.EqualTo(new[] { 2, 4 }),
            "the token holder and the one we cannot rule out"
        );
    }

    [TestCase(TargetState.Healthy, "running normally")]
    [TestCase(TargetState.Frozen, "frozen")]
    [TestCase(TargetState.Zombie, "running with no window")]
    public void A_blocker_is_described_by_what_it_is_actually_doing(
        TargetState state,
        string expected
    )
    {
        // The old wording called every blocker "frozen". Telling somebody their working Bloom is frozen is
        // how they stop believing the rest of what the Doctor says.
        var described = RestartBlockers.Describe(Bloom(55, state, holdsToken: true));

        Assert.That(described, Does.Contain("process 55"));
        Assert.That(described, Does.Contain(expected));
    }

    [Test]
    public void A_Bloom_we_are_only_guessing_about_says_so()
    {
        var guessed = RestartBlockers.Describe(Bloom(56, TargetState.Frozen, holdsToken: null));
        var known = RestartBlockers.Describe(Bloom(57, TargetState.Frozen, holdsToken: true));

        Assert.That(
            guessed,
            Does.Contain("too old to tell us"),
            "ending this one may achieve nothing, so the person deciding should be told"
        );
        Assert.That(known, Does.Not.Contain("too old to tell us"));
    }
}
