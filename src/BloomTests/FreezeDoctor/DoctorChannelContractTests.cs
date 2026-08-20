using System;
using Bloom.FreezeDoctor;
using NUnit.Framework;

namespace BloomTests.FreezeDoctor
{
    /// <summary>
    /// Pins the shared-memory contract Bloom publishes for the Bloom Freeze Doctor
    /// (https://github.com/BloomBooks/bloom-freeze-doctor).
    ///
    /// **The point of this fixture is to make drift break a build.** `DoctorChannel.cs` is a copy of a
    /// file in the Doctor's repository, and the two must agree byte for byte about the layout. If they
    /// ever disagree, nothing fails loudly: Bloom writes one set of offsets, the Doctor reads another, and
    /// the result is a stream of reports full of plausible nonsense that nobody can tell from real ones.
    /// So the schema version, the page size and the name format are asserted here BY VALUE, and the
    /// equivalent fixture in the Doctor's repository asserts the same numbers. Changing the layout should
    /// therefore require changing two tests in two repositories, which is the intended amount of friction.
    /// </summary>
    [TestFixture]
    public class DoctorChannelContractTests
    {
        /// <summary>A process id no real Bloom will have, so a test run cannot collide with a live channel.</summary>
        private const int TestProcessId = 999_002;

        [Test]
        public void LayoutMatchesTheDoctorsCopy()
        {
            // If you are here because this failed: the layout changed. Update the copy of
            // DoctorChannel.cs in BloomBooks/bloom-freeze-doctor, bump SchemaVersion in both, and update
            // the pinned numbers in both repositories' tests.
            Assert.That(DoctorChannelLayout.SchemaVersion, Is.EqualTo(1), "schema version");
            Assert.That(DoctorChannelLayout.Size, Is.EqualTo(4096), "page size");
            Assert.That(
                DoctorChannelLayout.ActivityMaxBytes,
                Is.EqualTo(256),
                "room for the activity string"
            );
            Assert.That(
                DoctorChannelLayout.NameFor(1234),
                Is.EqualTo(@"Local\BloomFreezeDoctor.v1.1234"),
                "the name must stay in the Local namespace and carry both version and pid"
            );
        }

        [Test]
        public void WhatBloomWritesCanBeReadBack()
        {
            using (var writer = new DoctorChannelWriter(TestProcessId))
            {
                Assert.That(writer.IsOpen, Is.True, "setup: the channel should have been created");

                writer.SetActivity("Publishing to BloomPUB");
                writer.SetLongOperation(true);
                writer.RecordUiTick();
                writer.RecordWatchdogTick();
                writer.SetShutdownPhase(2);

                Assert.That(
                    DoctorChannelReader.TryRead(TestProcessId, out var snapshot),
                    Is.True,
                    "a Doctor should be able to read what we just published"
                );
                Assert.That(snapshot.Activity, Is.EqualTo("Publishing to BloomPUB"));
                Assert.That(snapshot.LongOperationInProgress, Is.True);
                Assert.That(snapshot.UiTicks, Is.EqualTo(1));
                Assert.That(snapshot.WatchdogTicks, Is.EqualTo(1));
                Assert.That(snapshot.ShutdownPhase, Is.EqualTo(2));
                Assert.That(snapshot.CleanExitRecorded, Is.False, "we have not exited");
            }
        }

        [Test]
        public void AnUntickedHeartbeatReadsAsInfinitelyOldRatherThanFresh()
        {
            // The dangerous direction: if an unticked heartbeat read as "just now", a Bloom that wedged
            // during startup would look healthy for ever.
            using (var writer = new DoctorChannelWriter(TestProcessId))
            {
                writer.SetActivity("starting up");

                DoctorChannelReader.TryRead(TestProcessId, out var snapshot);

                Assert.That(snapshot.UiHeartbeatAge, Is.EqualTo(TimeSpan.MaxValue));
            }
        }

        [Test]
        public void PublishingNeverThrowsEvenWhenTheChannelCouldNotBeCreated()
        {
            // Two writers for one process id: the second cannot create the section. Bloom must not care,
            // because publishing diagnostics is never worth failing a startup over.
            using (var first = new DoctorChannelWriter(TestProcessId))
            using (var second = new DoctorChannelWriter(TestProcessId))
            {
                Assert.That(
                    second.IsOpen,
                    Is.False,
                    "setup: the second writer should have failed to create the section"
                );
                Assert.DoesNotThrow(() =>
                {
                    second.RecordUiTick();
                    second.SetActivity("this goes nowhere");
                    second.RecordCleanExit();
                });
            }
        }

        [Test]
        public void StatedActivitySurvivesTheWatchdogsOncePerSecondRefresh()
        {
            // Regression test for a bug that has now been introduced twice, in two different ways, so it is
            // worth a test rather than a comment. What Bloom says it is doing must survive the watchdog's
            // refresh: the first version let the refresh overwrite it, and the second recorded "starting up"
            // straight into the shared page instead of through SetActivity, so there was nothing for the
            // refresh to carry forward. Either way a Bloom that wedged during startup — the one case where
            // this string is the only clue there is — reported "no request in flight".
            // Explicitly, rather than trusting the initial value: this is static state shared with every
            // other test in the assembly, so anything else that ever states an activity would otherwise
            // make this test pass or fail depending on the order they ran in.
            FreezeDoctorSupport.SetActivity(null);
            Assert.That(
                FreezeDoctorSupport.ComposeCurrentActivity(),
                Is.EqualTo("no request in flight"),
                "sanity check: nothing stated and no request in flight yet"
            );

            FreezeDoctorSupport.SetActivity("starting up");

            Assert.That(
                FreezeDoctorSupport.ComposeCurrentActivity(),
                Is.EqualTo("starting up"),
                "the stated activity must still be there a refresh later"
            );

            // And it must be replaceable, or a stale "starting up" would outlive startup for ever.
            FreezeDoctorSupport.SetActivity("Publishing to BloomPUB");
            Assert.That(
                FreezeDoctorSupport.ComposeCurrentActivity(),
                Is.EqualTo("Publishing to BloomPUB")
            );

            FreezeDoctorSupport.SetActivity(null);
            Assert.That(
                FreezeDoctorSupport.ComposeCurrentActivity(),
                Is.EqualTo("no request in flight"),
                "clearing it must go back to idle rather than leaving the last value in place"
            );
        }

        [Test]
        public void FreezeSimulatorIsInertOnReleaseChannels()
        {
            // The safeguard that matters: this class deliberately breaks Bloom, so a stray environment
            // variable on a user's machine must not be able to set it off. Arming it for a Release channel
            // must do nothing at all, and must certainly not throw.
            var saved = Environment.GetEnvironmentVariable(FreezeSimulator.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(FreezeSimulator.EnvironmentVariable, "sleep:1");

                Assert.That(
                    FreezeSimulator.ArmIfRequested("Release"),
                    Is.False,
                    "a stray environment variable must not be able to break a Release Bloom"
                );
                Assert.That(FreezeSimulator.ArmIfRequested("Beta"), Is.False, "nor a Beta one");
            }
            finally
            {
                Environment.SetEnvironmentVariable(FreezeSimulator.EnvironmentVariable, saved);
            }
        }

        [Test]
        public void FreezeSimulatorArmsOnAlphaAndDeveloperChannels()
        {
            // Alpha is allowed on purpose: reproducing a freeze usually means working with somebody who is
            // actually experiencing one, and those people are running Alpha, not a build from source. This
            // is the other half of the safeguard above — the pair of tests together say exactly which
            // channels may be broken deliberately, so widening or narrowing that set breaks a test.
            var saved = Environment.GetEnvironmentVariable(FreezeSimulator.EnvironmentVariable);
            try
            {
                // A delay far longer than the test run, so nothing ever actually fires. There is no message
                // loop here either, so the UI-thread timer it arms cannot tick.
                Environment.SetEnvironmentVariable(
                    FreezeSimulator.EnvironmentVariable,
                    "sleep:100000"
                );

                Assert.That(FreezeSimulator.ArmIfRequested("Alpha"), Is.True, "Alpha");
                Assert.That(
                    FreezeSimulator.ArmIfRequested("Developer/Debug"),
                    Is.True,
                    "developer"
                );
            }
            finally
            {
                Environment.SetEnvironmentVariable(FreezeSimulator.EnvironmentVariable, saved);
                FreezeSimulator.Disarm();
            }
        }

        [Test]
        public void FreezeSimulatorStaysInertWithNoEnvironmentVariableEvenOnAlpha()
        {
            // Belt and braces: the channel is a gate, not a trigger. Nobody on Alpha gets a broken Bloom
            // unless they asked for one.
            var saved = Environment.GetEnvironmentVariable(FreezeSimulator.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(FreezeSimulator.EnvironmentVariable, null);

                Assert.That(FreezeSimulator.ArmIfRequested("Alpha"), Is.False);
            }
            finally
            {
                Environment.SetEnvironmentVariable(FreezeSimulator.EnvironmentVariable, saved);
            }
        }
    }
}
