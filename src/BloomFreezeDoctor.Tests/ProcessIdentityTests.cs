using System.Diagnostics;
using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Whether a process id still means the process we think it does.
///
/// The Doctor spends its life holding ids of Blooms that are frozen, dying or already dead, and Windows
/// hands ids out of a pool. Every one of these tests is the difference between acting on our Bloom and
/// acting on whatever inherited its id.
/// </summary>
[TestFixture]
public class ProcessIdentityTests
{
    [Test]
    public void A_process_is_recognised_by_its_own_id_and_start_time()
    {
        var self = Process.GetCurrentProcess();

        Assert.That(ProcessIdentity.IsStillTheSameProcess(self.Id, self.StartTime), Is.True);
    }

    [Test]
    public void The_same_id_with_a_different_start_time_is_a_different_process()
    {
        // The whole point. The id is live and real - it is this very process - so anything that looked only
        // at the id would say yes. Only the start time reveals that this is not who we meant.
        var self = Process.GetCurrentProcess();

        Assert.That(
            ProcessIdentity.IsStillTheSameProcess(self.Id, self.StartTime.AddSeconds(-1)),
            Is.False,
            "a reused id must not pass as the process we were watching"
        );
    }

    [Test]
    public void Start_times_are_compared_exactly_rather_than_loosely()
    {
        // Deliberately pinned: a tolerance here would be the natural-looking mistake. Both values come from
        // the same kernel field so they agree to the tick, and a busy machine really can reuse an id within
        // a second - so any tolerance wide enough to "help" is wide enough to admit the case this guards.
        var self = Process.GetCurrentProcess();

        Assert.That(
            ProcessIdentity.IsStillTheSameProcess(self.Id, self.StartTime.AddMilliseconds(1)),
            Is.False,
            "even a millisecond's difference means it is not the process we recorded"
        );
    }

    [Test]
    public void An_id_that_belongs_to_nothing_is_not_ours()
    {
        Assert.That(ProcessIdentity.IsStillTheSameProcess(999_999_9, DateTime.Now), Is.False);
    }

    [Test]
    public void An_id_we_cannot_ask_about_is_treated_as_not_ours()
    {
        // Process id 0 is the System Idle Process, which cannot be opened. Failing to confirm has to mean
        // no: every caller is about to believe this process's evidence, or end it.
        Assert.That(ProcessIdentity.IsStillTheSameProcess(0, DateTime.Now), Is.False);
    }
}
