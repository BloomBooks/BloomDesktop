using System;
using System.Diagnostics;
using System.Linq;
using Bloom.FreezeDoctor;
using BloomFreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomTests.FreezeDoctor
{
    /// <summary>
    /// Bloom's side of the protocol it shares with the Bloom Freeze Doctor
    /// (src/BloomFreezeDoctor, in this repository): the layout it expects, and the health it
    /// publishes through it.
    ///
    /// **What this fixture is for changed when the protocol became a package.** It used to guard against
    /// two hand-maintained copies of the same file drifting apart. There is only one definition now — the
    /// `BloomFreezeDoctor.Protocol` package — so drift in that sense is no longer possible.
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
        public void ShutdownPhasesAreWhatBloomWasBuiltAgainst()
        {
            // Bloom's own copy of the pin in the Doctor's DoctorChannelTests, for the same reason the
            // layout above is pinned twice: a package upgrade that renumbered these would otherwise make
            // Bloom write phases the Doctor reads as something else, silently. The NUMBER is what goes
            // into the shared page; the NAME is what goes into the session file. Neither may change.
            var expected = new[]
            {
                "None=0",
                "MessageLoopReturned=1",
                "SettingsSaved=2",
                "LogWritten=3",
                "ProjectContextDisposed=4",
            };
            Assert.That(
                Enum.GetValues<BloomShutdownPhase>().Select(p => $"{p}={(int)p}").ToArray(),
                Is.EqualTo(expected),
                "the shutdown phases, pinned by name AND number"
            );
        }

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
                "DebuggerLastDetached@328+8",
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
            // Equal to PayloadBytes for now, because generation 1 is unreleased and there is no older Bloom
            // writing less. Once a Bloom ships writing this page, this number freezes and appending a field
            // grows only PayloadBytes.
            Assert.That(
                DoctorChannelLayout.BaselinePayloadBytes,
                Is.EqualTo(336),
                "the generation-1 floor"
            );
        }

        [Test]
        public void NestedLongOperationsDoNotEndEachOthersPatience()
        {
            // The failure this guards against is silent and lasts the whole session. The Doctor waits five
            // minutes instead of one while a long operation runs; if an inner operation's exit cleared the
            // flag, the outer one would lose its patience half way through — and if an exit were MISSED,
            // the flag would stay set for ever and freeze detection would be off for the rest of the run.
            //
            // Nesting is not hypothetical: building an app makes a BloomPUB on the way, so those two scopes
            // are genuinely inside one another in production.
            Assert.That(
                FreezeDoctorSupport.LongOperationDepth,
                Is.Zero,
                "setup: nothing long is running yet"
            );

            using (FreezeDoctorSupport.LongOperation("building an app"))
            {
                Assert.That(FreezeDoctorSupport.LongOperationDepth, Is.EqualTo(1), "outer entered");

                using (FreezeDoctorSupport.LongOperation("making a BloomPUB"))
                {
                    Assert.That(
                        FreezeDoctorSupport.LongOperationDepth,
                        Is.EqualTo(2),
                        "inner entered"
                    );
                }

                Assert.That(
                    FreezeDoctorSupport.LongOperationDepth,
                    Is.EqualTo(1),
                    "the inner operation finishing must NOT end the outer one's patience"
                );
            }

            Assert.That(
                FreezeDoctorSupport.LongOperationDepth,
                Is.Zero,
                "and the outermost finishing must clear it, or freeze detection stays off for good"
            );
        }

        [Test]
        public void DisposingALongOperationTwiceDoesNotStealSomebodyElsesPatience()
        {
            // A double Dispose is easy to arrange by accident, and an unguarded decrement would drive the
            // count negative — after which the NEXT operation's exit would not clear the flag, leaving the
            // Doctor permanently patient. Guarded in the scope; pinned here.
            var outer = FreezeDoctorSupport.LongOperation("building an app");
            var inner = FreezeDoctorSupport.LongOperation("making a BloomPUB");
            inner.Dispose();
            inner.Dispose();

            Assert.That(
                FreezeDoctorSupport.LongOperationDepth,
                Is.EqualTo(1),
                "the second Dispose must be ignored, not counted again"
            );

            outer.Dispose();
            Assert.That(FreezeDoctorSupport.LongOperationDepth, Is.Zero);
        }

        [Test]
        public void ALongOperationEndsEvenWhenItThrows()
        {
            // `using` is the whole reason the API is a scope rather than paired calls: an exception on a
            // publish path must not leave the Doctor permanently patient.
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (FreezeDoctorSupport.LongOperation("making an ePUB"))
                {
                    throw new InvalidOperationException("pretend the publish failed");
                }
            });

            Assert.That(
                FreezeDoctorSupport.LongOperationDepth,
                Is.Zero,
                "an operation that threw must still have ended"
            );
        }

        [Test]
        public void TheNativeDebuggerCheckActuallyResolves()
        {
            // Bloom's debugger check swallows exceptions, because a diagnostic must never be able to break
            // the watchdog thread. That means a wrong DllImport signature would not fail loudly — it would
            // just report "no debugger" for ever, and the sticky flag would never be set on any machine.
            // Calling it here, outside that catch, is what turns a broken P/Invoke into a failing test.
            Assert.DoesNotThrow(
                () => FreezeDoctorSupport.IsDebuggerPresent(),
                "the kernel32 IsDebuggerPresent import should resolve"
            );

            // Whatever the environment, the combined answer must agree with the managed one when that says
            // a debugger is attached — this is the direction that matters, since it is what suppresses
            // reports while a developer is stepping through Bloom.
            if (Debugger.IsAttached)
                Assert.That(
                    FreezeDoctorSupport.IsDebuggerAttached(),
                    Is.True,
                    "a managed debugger must always count as attached"
                );
            else
                Assert.DoesNotThrow(() => FreezeDoctorSupport.IsDebuggerAttached());
        }

        [Test]
        public void ADebuggerThatHasComeAndGoneIsStillVisibleToTheDoctor()
        {
            // Bloom's end of the sticky flag. The Doctor's repo tests the mechanism; what is worth checking
            // here is that Bloom is publishing through the call that remembers, so that a debugger which
            // attached and left does not leave a heartbeat gap looking like a genuine freeze.
            using (var writer = new DoctorChannelWriter(TestProcessId))
            {
                Assert.That(writer.IsOpen, Is.True, "setup: the channel should have been created");

                writer.SetDebuggerAttached(true);
                writer.SetDebuggerAttached(false);

                Assert.That(DoctorChannelReader.TryRead(TestProcessId, out var snapshot), Is.True);
                Assert.That(
                    snapshot.DebuggerAttached,
                    Is.False,
                    "setup: it should have gone again"
                );
                Assert.That(
                    snapshot.DebuggerEverAttached,
                    Is.True,
                    "but the Doctor must still be able to tell that one was here"
                );
                Assert.That(
                    snapshot.DebuggerLastDetachedAge,
                    Is.LessThan(TimeSpan.FromMinutes(1)),
                    "and roughly when it left, so an unrelated freeze later is still reportable"
                );
            }
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
                writer.SetShutdownPhase(BloomShutdownPhase.SettingsSaved);

                Assert.That(
                    DoctorChannelReader.TryRead(TestProcessId, out var snapshot),
                    Is.True,
                    "a Doctor should be able to read what we just published"
                );
                Assert.That(snapshot.Activity, Is.EqualTo("Publishing to BloomPUB"));
                Assert.That(snapshot.LongOperationInProgress, Is.True);
                Assert.That(snapshot.UiTicks, Is.EqualTo(1));
                Assert.That(snapshot.WatchdogTicks, Is.EqualTo(1));
                Assert.That(snapshot.ShutdownPhase, Is.EqualTo(BloomShutdownPhase.SettingsSaved));
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
        public void AMisspeltSimulationKindArmsNothingAtAll()
        {
            // The failure this guards against is worse than the typo that causes it. An unrecognised kind
            // used to arm no simulation - the switch merely logged "not a kind it knows about" 45 seconds
            // later - while still recording in the session file that this Bloom was a deliberate
            // rehearsal. The Doctor reads that marker and declines to file a card, so one misspelt
            // environment variable silently turned off freeze reporting for the whole session, on a Bloom
            // that was behaving perfectly normally and might genuinely freeze.
            var saved = Environment.GetEnvironmentVariable(FreezeSimulator.EnvironmentVariable);
            try
            {
                // Sanity check first, so this test cannot pass merely because arming never works here.
                Environment.SetEnvironmentVariable(
                    FreezeSimulator.EnvironmentVariable,
                    "sleep:100000"
                );
                Assert.That(
                    FreezeSimulator.ArmIfRequested("Alpha"),
                    Is.True,
                    "setup: a known kind on an allowed channel must arm, or this test proves nothing"
                );
                FreezeSimulator.Disarm();

                Environment.SetEnvironmentVariable(
                    FreezeSimulator.EnvironmentVariable,
                    "slep:100000"
                );
                Assert.That(
                    FreezeSimulator.ArmIfRequested("Alpha"),
                    Is.False,
                    "a kind we do not recognise must refuse outright, not half-arm"
                );
            }
            finally
            {
                Environment.SetEnvironmentVariable(FreezeSimulator.EnvironmentVariable, saved);
                FreezeSimulator.Disarm();
            }
        }

        [Test]
        public void EverySimulationKindTheSwitchHandlesIsAlsoAcceptedByTheGuard()
        {
            // The guard and the switch are two lists that must not drift apart: a kind the switch knows
            // but the guard rejects is a simulation nobody can run, and the reverse is the silent-marker
            // bug above. Asserting every advertised kind arms is the cheap half of keeping them together.
            var saved = Environment.GetEnvironmentVariable(FreezeSimulator.EnvironmentVariable);
            try
            {
                foreach (var kind in FreezeSimulator.KnownKinds)
                {
                    Environment.SetEnvironmentVariable(
                        FreezeSimulator.EnvironmentVariable,
                        kind + ":100000"
                    );
                    Assert.That(
                        FreezeSimulator.ArmIfRequested("Alpha"),
                        Is.True,
                        $"'{kind}' is advertised as a kind, so it must arm"
                    );
                    FreezeSimulator.Disarm();
                }
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
