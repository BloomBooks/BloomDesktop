using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Which of the Doctor's paths reports a Bloom that has died.
///
/// A whole fixture for one three-argument function because that function has been wrong twice, and in both
/// cases the symptom was silence - no report, or the wrong report - which no other test noticed and only a
/// manual run exposed. Each historical bug has a test here named after it.
/// </summary>
[TestFixture]
public class WhoReportsTheDeathTests
{
    [Test]
    public void An_ordinary_death_is_examined()
    {
        Assert.That(
            WhoReportsTheDeath.Decide(
                weEndedIt: false,
                aDumpIsBeingReported: false,
                alreadyClaimed: false
            ),
            Is.EqualTo(ExitExamination.Examine),
            "a Bloom that simply died, with nobody else reporting it, is the whole point of the Doctor"
        );
    }

    [Test]
    public void A_Bloom_we_ended_is_not_reported()
    {
        // We kill a zombie, or ask Bloom to quit so we can restart it. A killed process runs no
        // ProcessExit handler, so it leaves exactly the evidence an unexplained crash leaves - and would
        // get a card blaming Bloom for something we did.
        Assert.That(
            WhoReportsTheDeath.Decide(
                weEndedIt: true,
                aDumpIsBeingReported: false,
                alreadyClaimed: false
            ),
            Is.EqualTo(ExitExamination.WeCausedIt)
        );
    }

    [Test]
    public void The_crash_dump_path_owns_a_death_it_is_already_reporting()
    {
        // The bug a real crashthread run found. Bloom asked to be dumped as it crashed, so that path was
        // gathering a report WITH the dump; the exit examination ran anyway and gathered a second one
        // without it. Both were filed, the outbox's fingerprint dedup kept whichever finished first - the
        // dumpless one, as it happened, being the quicker to gather - and the dump-bearing report was
        // demoted to a "this happened again" comment, which deliberately attaches nothing. Net effect: we
        // held a dying Bloom open for three seconds to collect a dump, then left it on the user's machine.
        Assert.That(
            WhoReportsTheDeath.Decide(
                weEndedIt: false,
                aDumpIsBeingReported: true,
                alreadyClaimed: false
            ),
            Is.EqualTo(ExitExamination.TheDumpHasIt)
        );
    }

    [Test]
    public void A_death_already_claimed_is_left_alone()
    {
        Assert.That(
            WhoReportsTheDeath.Decide(
                weEndedIt: false,
                aDumpIsBeingReported: false,
                alreadyClaimed: true
            ),
            Is.EqualTo(ExitExamination.AlreadyClaimed)
        );
    }

    [Test]
    public void Claiming_a_death_before_deciding_would_silence_every_report()
    {
        // The other historical bug, and the worse of the two: fixing a race, the claim was made in the
        // discovery sweep and THEN this decision was consulted - which, seeing the claim, answered
        // AlreadyClaimed. So no crash was ever examined. This test states the property that makes that
        // shape a bug: for an otherwise-reportable death, the answer depends entirely on whether somebody
        // claimed it first, so a caller must decide before it claims, never after.
        Assert.That(
            WhoReportsTheDeath.Decide(
                weEndedIt: false,
                aDumpIsBeingReported: false,
                alreadyClaimed: false
            ),
            Is.EqualTo(ExitExamination.Examine),
            "sanity check: this death is reportable when nothing has claimed it"
        );
        Assert.That(
            WhoReportsTheDeath.Decide(
                weEndedIt: false,
                aDumpIsBeingReported: false,
                alreadyClaimed: true
            ),
            Is.Not.EqualTo(ExitExamination.Examine),
            "so claiming it first turns the very same death into a no-op - decide, then claim"
        );
    }

    [Test]
    public void Our_own_doing_outranks_the_dump()
    {
        // Both true happens when we killed a Bloom that was in the middle of crashing. Nothing should be
        // reported: the dump path files under MayFile, and this path must not file a card about our kill.
        Assert.That(
            WhoReportsTheDeath.Decide(
                weEndedIt: true,
                aDumpIsBeingReported: true,
                alreadyClaimed: false
            ),
            Is.EqualTo(ExitExamination.WeCausedIt)
        );
    }
}
