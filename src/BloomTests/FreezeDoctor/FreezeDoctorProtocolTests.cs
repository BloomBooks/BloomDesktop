using System;
using System.Linq;
using Bloom.FreezeDoctor;
using BloomBooks.FreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomTests.FreezeDoctor
{
    /// <summary>
    /// Bloom's side of the protocol it shares with the Bloom Freeze Doctor
    /// (https://github.com/BloomBooks/bloom-freeze-doctor): the layout it expects, and the health it
    /// publishes through it.
    ///
    /// **What this fixture is for changed when the protocol became a package.** It used to guard against
    /// two hand-maintained copies of the same file drifting apart. There is only one definition now — the
    /// `BloomBooks.FreezeDoctor.Protocol` package — so drift in that sense is no longer possible.
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
    public class FreezeDoctorProtocolTests
    {
        /// <summary>A process id no real Bloom will have, so a test run cannot collide with a live channel.</summary>
        private const int TestProcessId = 999_002;

        [Test]
        public void LayoutIsWhatBloomWasBuiltAgainst()
        {
            // If you are here because this failed, a package upgrade changed the layout. Do NOT just update
            // these numbers to match: check what moved and why. Adding a field should never reach this test
            // (see the next one); anything that MOVES a field is a SchemaVersion bump, and Bloom's side of
            // the protocol needs looking at before the numbers here are touched.
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

            // Every field, by value, from the layout's own published description of itself. This is what
            // turns "the package quietly moved a field" from a silent wrong-offset bug into a failed build.
            var expected = new[]
            {
                "SchemaVersion@0+4",
                "PayloadBytes@4+4",
                "WriteSequence@8+8",
                "ProcessId@16+4",
                "ShutdownPhase@20+4",
                "UiTicks@24+8",
                "UiTimestamp@32+8",
                "WatchdogTicks@40+8",
                "WatchdogTimestamp@48+8",
                "Flags@56+4",
                "ServerBusy@60+4",
                "ServerBlocked@64+4",
                "Reserved@68+4",
                "Activity@72+256",
            };
            Assert.That(
                DoctorChannelLayout.Fields.Select(f => $"{f.Name}@{f.Offset}+{f.Size}").ToArray(),
                Is.EqualTo(expected),
                "the field layout Bloom publishes to"
            );
        }

        [Test]
        public void AddingAFieldToTheProtocolDoesNotBreakBloom()
        {
            // The counterpart to the test above, and the reason it can be strict without being a nuisance.
            //
            // The protocol is allowed to GROW without a version bump: new fields are appended and
            // PayloadBytes grows to match. When that happens the pinned list above gains an entry, but
            // nothing Bloom does changes — Bloom keeps writing the fields it knows about, at the offsets it
            // knows, and an older Doctor keeps reading them.
            //
            // So what is pinned here is not a number that may not change; it is the *invariant* that makes
            // growth safe. If this fails, the layout has been rearranged rather than extended, and every
            // Doctor already installed is reading Bloom's page wrongly.
            var end = DoctorChannelLayout.Fields.Max(f => f.Offset + f.Size);

            Assert.That(
                DoctorChannelLayout.PayloadBytes,
                Is.EqualTo(end),
                "PayloadBytes must be one past the last field, or a new field is outside what Bloom claims to have written"
            );
            Assert.That(
                DoctorChannelLayout.PayloadBytes,
                Is.GreaterThanOrEqualTo(DoctorChannelLayout.BaselinePayloadBytes),
                "the layout may only grow past the generation-1 baseline"
            );
            Assert.That(
                DoctorChannelLayout.BaselinePayloadBytes,
                Is.EqualTo(328),
                "the baseline is frozen for the life of schema version 1"
            );
        }

        [Test]
        public void WhatBloomPublishesSaysHowMuchOfItIsReal()
        {
            // A Doctor newer than this Bloom needs to be able to tell a field Bloom never wrote from a real
            // zero. That only works if Bloom actually records its extent, so it is worth asserting that it
            // reaches the page rather than trusting the constant.
            using (var writer = new DoctorChannelWriter(TestProcessId))
            {
                Assert.That(writer.IsOpen, Is.True, "setup: the channel should have been created");

                Assert.That(
                    DoctorChannelReader.TryRead(TestProcessId, out var snapshot),
                    Is.True,
                    "setup: the channel should be readable"
                );
                Assert.That(
                    snapshot.PayloadBytes,
                    Is.EqualTo(DoctorChannelLayout.PayloadBytes),
                    "Bloom must record how far it wrote, or a newer Doctor cannot tell absent from zero"
                );
            }
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
