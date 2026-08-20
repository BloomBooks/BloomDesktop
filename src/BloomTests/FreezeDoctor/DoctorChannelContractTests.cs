using System;
using Bloom.FreezeDoctor;
using BloomFreezeDoctor.Contract;
using NUnit.Framework;

namespace BloomTests.FreezeDoctor
{
    /// <summary>
    /// Pins the shared-memory contract Bloom publishes for the Bloom Freeze Doctor
    /// (https://github.com/BloomBooks/bloom-freeze-doctor).
    ///
    /// **What this fixture is for changed when the contract became a package.** It used to guard against
    /// two hand-maintained copies of the same file drifting apart. There is only one copy now — the
    /// `BloomFreezeDoctor.Contract` package — so drift in that sense is no longer possible.
    ///
    /// It still earns its place, for a different reason: it pins the layout Bloom *expects* against the
    /// layout the referenced package version actually has. A package upgrade that changed an offset or
    /// the schema version would otherwise be silent — Bloom would compile, run, and publish its health to
    /// offsets the Doctor no longer reads, and the reports would be plausible nonsense that nobody could
    /// tell from real ones. Asserting the numbers BY VALUE here turns that into a failed build.
    ///
    /// So: if this fails after a version bump, the layout changed, and Bloom's side needs looking at
    /// rather than the numbers here being updated to match.
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
        public void WhatBloomIsDoingIsComposedFromBothSourcesRatherThanEitherWinning()
        {
            // Regression test for a bug introduced three times, in three different ways, which is what earns
            // it a test rather than a comment. Every version got one of the two directions wrong: the refresh
            // overwrote what Bloom stated ("starting up" survived less than a second); then "starting up" went
            // straight into the shared page so there was nothing for the refresh to carry forward; then it was
            // carried forward for ever and described an idle Bloom hours later. The two failures are opposite,
            // so both directions are pinned here.
            const string startup = FreezeDoctorSupport.StartupActivity;

            Assert.Multiple(() =>
            {
                // Nothing stated, nothing running.
                Assert.That(
                    FreezeDoctorSupport.Compose(null, null, false),
                    Is.EqualTo("no request in flight")
                );

                // Direction 1: what Bloom stated must not be lost. This is the startup-freeze case, where the
                // stated text is the only clue there is.
                Assert.That(
                    FreezeDoctorSupport.Compose(startup, null, false),
                    Is.EqualTo(startup),
                    "the startup note must survive until Bloom has actually started"
                );

                // Direction 2: it must not outlive its truth. Handling a request means Bloom is up.
                Assert.That(
                    FreezeDoctorSupport.Compose(startup, null, true),
                    Is.EqualTo("no request in flight"),
                    "once Bloom has handled a request it is no longer starting up"
                );

                // A stated activity that is not the startup note is the caller's business and is never
                // retired on their behalf — a long publish must keep saying so.
                Assert.That(
                    FreezeDoctorSupport.Compose("Publishing to BloomPUB", null, true),
                    Is.EqualTo("Publishing to BloomPUB")
                );

                // Neither source silences the other.
                Assert.That(
                    FreezeDoctorSupport.Compose(
                        "Publishing to BloomPUB",
                        "api/publish running 9s",
                        true
                    ),
                    Is.EqualTo("Publishing to BloomPUB | api/publish running 9s")
                );
                Assert.That(
                    FreezeDoctorSupport.Compose(null, "api/publish running 9s", true),
                    Is.EqualTo("api/publish running 9s")
                );
            });
        }

        [Test]
        public void SetActivityIsWhatTheComposerReads()
        {
            // The chain the second version of the bug broke: Start() wrote to the shared page directly, so
            // SetActivity had never recorded anything and the composer had nothing to carry forward.
            FreezeDoctorSupport.SetActivity("Saving Foo.htm");
            try
            {
                Assert.That(
                    FreezeDoctorSupport.ComposeCurrentActivity(),
                    Does.Contain("Saving Foo.htm"),
                    "SetActivity must reach the composer, not just the shared page"
                );
            }
            finally
            {
                // Static state shared with the rest of the assembly.
                FreezeDoctorSupport.SetActivity(null);
            }
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
