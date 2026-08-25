using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SIL.Reporting;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Makes Bloom fail on purpose, so that the Freeze Doctor's detection can be tested against a real
    /// Bloom rather than only against a stand-in.
    ///
    /// This exists because the alternative is worse. Without it, testing the Doctor against Bloom means
    /// waiting for a real freeze — which is precisely the thing we cannot reproduce, and the reason the
    /// Doctor is being built at all.
    ///
    /// **Two safeguards, because this deliberately breaks Bloom.** It does nothing unless the
    /// `BLOOM_SIMULATE_FREEZE` environment variable is set, and it does nothing on a Release channel. So
    /// there is no menu item, no API endpoint and no keystroke that could set it off on a user's machine:
    /// somebody has to have deliberately arranged both conditions.
    ///
    /// Usage: set `BLOOM_SIMULATE_FREEZE=&lt;kind&gt;[:&lt;delaySeconds&gt;]` before launching Bloom.
    /// Kinds: `sleep`, `stawait`, `spin`, `failfast`, `throw`, `crashthread`, `zombie`, `mutexchain` — each
    /// documented at its case below, with the state it imitates.
    /// </summary>
    public static class FreezeSimulator
    {
        /// <summary>
        /// Keeps the countdown timer alive. A WinForms timer referenced only by a local would be eligible for
        /// collection the moment the method returns, and a simulator that sometimes forgets to fire is worse
        /// than no simulator: it looks like the Doctor failing to detect something.
        /// </summary>
        private static System.Windows.Forms.Timer _countdown;

        /// <summary>The environment variable that arms this. Absent means absent; there is no other route in.</summary>
        public const string EnvironmentVariable = "BLOOM_SIMULATE_FREEZE";

        /// <summary>
        /// The plain kernel wait, used by the `mutexchain` kind. See that case for why the managed
        /// <c>Mutex.WaitOne</c> will not do.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        /// <summary>How long to wait before misbehaving, if the variable does not say.</summary>
        private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(45);

        /// <summary>
        /// The kinds this understands. Kept beside <see cref="Simulate"/>'s switch and checked BEFORE
        /// arming, so an unrecognised value refuses outright instead of half-arming — see the check in
        /// <see cref="ArmIfRequested"/> for why that distinction matters.
        /// </summary>
        internal static readonly string[] KnownKinds =
        {
            "sleep",
            "stawait",
            "spin",
            "mutexchain",
            "failfast",
            "throw",
            "crashthread",
            "zombie",
        };

        /// <summary>
        /// Channels on which the simulator is allowed to act.
        ///
        /// Developer builds are obvious. **Alpha is here deliberately**: reproducing a freeze usually means
        /// working with somebody who is actually experiencing it, and those people are on Alpha, not running
        /// a build from source. Excluding it would have left the simulator useful only on the machines where
        /// we can least often reproduce the problem.
        ///
        /// Beta and Release are excluded, and the internal channels are not listed either — add them here if
        /// that ever proves inconvenient, rather than loosening the test.
        /// </summary>
        private static bool IsChannelWhereWeMayBreakBloomDeliberately(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                return false;
            return channelName.StartsWith("Developer", StringComparison.OrdinalIgnoreCase)
                || channelName.Equals("Alpha", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Arms the simulator if it has been asked for. Call once during startup; it returns immediately
        /// and does its damage later, so that Bloom has time to finish starting and the Doctor has time to
        /// adopt it.
        /// </summary>
        /// <param name="channelName">
        /// Bloom's release channel. Anything outside
        /// <see cref="IsChannelWhereWeMayBreakBloomDeliberately"/> is refused, so that a stray environment
        /// variable on an ordinary user's machine cannot break their Bloom.
        /// </param>
        /// <returns>
        /// True if the simulator is now armed. Returned so a test can tell "armed" from "declined" without
        /// waiting for Bloom to actually break.
        /// </returns>
        public static bool ArmIfRequested(string channelName)
        {
            string request;
            try
            {
                request = Environment.GetEnvironmentVariable(EnvironmentVariable);
            }
            catch (Exception)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(request))
                return false;

            if (!IsChannelWhereWeMayBreakBloomDeliberately(channelName))
            {
                Logger.WriteEvent(
                    $"{EnvironmentVariable} is set but this is the '{channelName}' channel, so it is being "
                        + "ignored. Deliberate breakage is for developer and Alpha builds only."
                );
                return false;
            }

            var parts = request.Split(':');
            var kind = parts[0].Trim().ToLowerInvariant();
            var delay =
                parts.Length > 1 && int.TryParse(parts[1], out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : DefaultDelay;

            // Refuse a kind we do not know, HERE, before anything else happens.
            //
            // This check has to precede the marker below, and getting that order wrong was a real defect:
            // an unrecognised value - a typo like `slep`, or a stale name - used to arm nothing at all
            // (the switch simply logged "not a kind it knows about" 45 seconds later) while still telling
            // the Doctor this Bloom was a rehearsal. The Doctor then declined to file a card for every
            // GENUINE freeze for the rest of that session. A misspelled environment variable silently
            // turning off freeze reporting is far worse than the typo it came from.
            if (Array.IndexOf(KnownKinds, kind) < 0)
            {
                Logger.WriteEvent(
                    $"*** FreezeSimulator: '{kind}' is not a kind it knows about, so nothing is armed. "
                        + $"Expected one of: {string.Join(", ", KnownKinds)}."
                );
                return false;
            }

            Logger.WriteEvent(
                $"*** FreezeSimulator armed: will simulate '{kind}' in {delay.TotalSeconds:F0} seconds. "
                    + $"This Bloom is deliberately going to misbehave. Unset {EnvironmentVariable} to stop it."
            );

            // Tell any Doctor that this Bloom is a rehearsal, so it does not file a card about a freeze we
            // asked for. Said here rather than where the variable is read, because only now do we know the
            // simulator will actually act: the channel check and the kind have both been accepted.
            FreezeDoctorSupport.NoteSimulatedFailureArmed(kind);

            // Arming twice would otherwise abandon the first timer still running, leaving a simulation
            // nobody asked for to go off later. Startup only calls this once, but the tests call it per
            // channel, and a booby trap that only appears under test is still a booby trap.
            Disarm();

            // A UI-thread timer, because most of these have to happen ON the UI thread to be the failure
            // we are imitating. A background timer would produce a quite different (and uninteresting)
            // kind of stuck.
            _countdown = new System.Windows.Forms.Timer
            {
                Interval = (int)Math.Max(1000, delay.TotalMilliseconds),
            };
            _countdown.Tick += (sender, args) =>
            {
                _countdown.Stop();
                Simulate(kind);
            };
            _countdown.Start();
            return true;
        }

        /// <summary>
        /// Cancels a pending simulation. Exists for the tests: they arm the simulator to prove that a
        /// channel is allowed to, and an armed timer left behind would be a Bloom-breaking booby trap
        /// waiting for whichever later test happens to pump messages.
        /// </summary>
        public static void Disarm()
        {
            _countdown?.Stop();
            _countdown?.Dispose();
            _countdown = null;
        }

        /// <summary>
        /// Carries out one kind of failure. Each corresponds to a state the Doctor claims to detect; the
        /// comments name which, because that correspondence is the whole purpose of this class.
        /// </summary>
        private static void Simulate(string kind)
        {
            Logger.WriteEvent($"*** FreezeSimulator: simulating '{kind}' NOW");
            switch (kind)
            {
                // The freeze an outside watcher CAN see: the UI thread stops pumping altogether.
                case "sleep":
                    Thread.Sleep(TimeSpan.FromMinutes(10));
                    break;

                // The freeze an outside watcher CANNOT see, and the reason this whole channel exists. A
                // managed wait on the STA thread keeps dispatching sent messages, so the window still
                // answers probes and Windows reports Bloom as responsive while it is entirely stuck. Only
                // the UI heartbeat gives it away.
                case "stawait":
                    using (var never = new ManualResetEventSlim(false))
                        never.Wait(TimeSpan.FromMinutes(10));
                    break;

                // Frozen but burning a core, which the report should tell apart from a deadlock.
                case "spin":
                    var until = Stopwatch.StartNew();
                    while (until.Elapsed < TimeSpan.FromMinutes(10)) { }
                    break;

                // A hard crash that runs no orderly shutdown: no ProcessExit, so no clean-exit proof.
                case "failfast":
                    Environment.FailFast("FreezeSimulator was asked to fail fast");
                    break;

                // An exception on the UI thread. Note this does NOT kill Bloom: Bloom's own error handling
                // catches it and reports it, which is the correct behaviour and is also the case where the
                // Doctor should stay quiet because Bloom has already told us.
                case "throw":
                    throw new ApplicationException("FreezeSimulator was asked to throw");

                // A genuinely fatal crash: an exception on a plain background thread, which nothing catches.
                // AppDomain.UnhandledException fires and the process dies — the path the Doctor's
                // crash-dump handshake exists for, and the only one we can actually exercise, since a direct
                // FailFast runs no managed handlers at all.
                case "crashthread":
                    new Thread(() =>
                    {
                        Thread.Sleep(500);
                        throw new ApplicationException(
                            "FreezeSimulator was asked to crash a background thread"
                        );
                    })
                    {
                        IsBackground = false,
                        Name = "FreezeSimulator crashing thread",
                    }.Start();
                    break;

                // A freeze WINDOWS ITSELF can explain, which none of the others are, and the only way to
                // exercise the Doctor's wait-chain reading against real data.
                //
                // The Wait Chain Traversal API - what Resource Monitor's "Analyze Wait Chain" runs on - is
                // blind to `Monitor`, `SemaphoreSlim` and async waits, so every other kind here produces
                // an empty chain. That code once reported thread ids that were pure garbage (the native
                // structure's second half is a union) and nothing noticed, because nothing had ever fed
                // it a wait it could see. A mutex is a genuine kernel object whose owner the kernel
                // tracks, so this produces a real chain: this thread, blocked on a mutex, owned by that
                // thread - a thread id that can be checked against the managed stacks in the same report.
                case "mutexchain":
                {
                    var mutex = new Mutex(false);
                    var holderHasIt = new ManualResetEventSlim(false);
                    // A Mutex has thread affinity, so the holder must take it itself rather than be handed
                    // it. Foreground, so the process cannot quietly exit from under the wait.
                    new Thread(() =>
                    {
                        mutex.WaitOne();
                        holderHasIt.Set();
                        // Never released. This Bloom is not going to recover on purpose.
                        Thread.Sleep(TimeSpan.FromMinutes(10));
                    })
                    {
                        IsBackground = false,
                        Name = "FreezeSimulator mutex holder",
                    }.Start();
                    // Do not block until the other thread genuinely owns it, or the UI thread would take
                    // the mutex itself and simulate nothing at all.
                    holderHasIt.Wait(TimeSpan.FromSeconds(5));
                    // Deliberately the raw Win32 wait rather than Mutex.WaitOne. On an STA thread the
                    // managed wait becomes CoWaitForMultipleHandles, which pumps messages and may leave
                    // the thread looking to the kernel like it is waiting on COM rather than on the mutex.
                    // Since the entire point here is to hand WCT a wait it can attribute, we ask for the
                    // plain kernel wait and leave nothing to interpretation.
                    WaitForSingleObject(
                        mutex.SafeWaitHandle.DangerousGetHandle(),
                        (uint)TimeSpan.FromMinutes(10).TotalMilliseconds
                    );
                    break;
                }

                // The UI goes away but the process lives on, held up by a foreground thread: the state
                // whose symptom users report as "Bloom won't start".
                case "zombie":
                    new Thread(() => Thread.Sleep(TimeSpan.FromMinutes(20)))
                    {
                        IsBackground = false,
                        Name = "FreezeSimulator zombie keeper",
                    }.Start();
                    foreach (Form form in Application.OpenForms)
                        form.Hide();
                    break;

                default:
                    Logger.WriteEvent(
                        $"*** FreezeSimulator: '{kind}' is not a kind it knows about"
                    );
                    break;
            }
        }
    }
}
