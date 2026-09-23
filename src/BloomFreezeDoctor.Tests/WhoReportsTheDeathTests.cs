using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Which of the Doctor's paths reports a Bloom that has died.
///
/// A whole fixture for one three-argument function because every way of getting it wrong is silent - no
/// report, or the wrong report - which no other test notices.
/// </summary>
[TestFixture]
public class WhoReportsTheDeathTests
{
    [Test]
    public void Decide_OrdinaryDeath_Examine()
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
    public void Decide_WeEndedIt_WeCausedIt()
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
    public void Decide_DumpBeingReported_TheDumpHasIt()
    {
        // Bloom asks to be dumped as it crashes, so that path gathers a report WITH the dump. If the exit
        // examination ran anyway it would gather a second one without it; both filed, the outbox's
        // fingerprint dedup would keep whichever finished first - the dumpless one, being quicker to
        // gather - and the dump-bearing report would be demoted to a "this happened again" comment, which
        // deliberately attaches nothing. Net effect: a dying Bloom held open for three seconds to collect
        // a dump that is then left on the user's machine.
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
    public void Decide_AlreadyClaimed_AlreadyClaimed()
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
    public void Decide_ClaimedBeforeDeciding_NotExamined()
    {
        // The worse failure: a caller that claims the death first (to close a race in the discovery sweep,
        // say) and THEN consults this decision sees its own claim and gets AlreadyClaimed, so no crash is
        // ever examined. This test states the property that makes that shape a bug: for an
        // otherwise-reportable death, the answer depends entirely on whether somebody claimed it first, so
        // a caller must decide before it claims, never after.
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
    public void Decide_AlreadyClaimedWithAnyReason_AlreadyClaimed()
    {
        // Two passes can reach the decision for one death a second apart. If the reasons were tested before
        // the claim, the second would re-take the dump branch instead of standing down - logging again and
        // disposing a process handle that the first pass had already released.
        //
        // A stand-down is not just a decision, it is an action: it logs, it claims, and it releases the
        // handle. So once claimed, every reason must give the same answer.
        foreach (var weEndedIt in new[] { false, true })
        foreach (var aDumpIsBeingReported in new[] { false, true })
        {
            Assert.That(
                WhoReportsTheDeath.Decide(weEndedIt, aDumpIsBeingReported, alreadyClaimed: true),
                Is.EqualTo(ExitExamination.AlreadyClaimed),
                $"weEndedIt={weEndedIt}, aDumpIsBeingReported={aDumpIsBeingReported} must not "
                    + "re-take its own branch once the death is claimed"
            );
        }
    }

    [Test]
    public void Decide_WeEndedItAndDumpBeingReported_WeCausedIt()
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
