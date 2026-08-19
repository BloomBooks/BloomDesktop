using System;
using System.Diagnostics;
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
    /// Kinds: `sleep`, `stawait`, `spin`, `failfast`, `throw`, `crashthread`, `zombie` — each documented at
    /// its case below, with the state it imitates.
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

        /// <summary>How long to wait before misbehaving, if the variable does not say.</summary>
        private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(45);

        /// <summary>
        /// Arms the simulator if it has been asked for. Call once during startup; it returns immediately
        /// and does its damage later, so that Bloom has time to finish starting and the Doctor has time to
        /// adopt it.
        /// </summary>
        /// <param name="channelName">
        /// Bloom's release channel. Anything that is not a developer channel is refused, so that a stray
        /// environment variable on a user's machine cannot break their Bloom.
        /// </param>
        public static void ArmIfRequested(string channelName)
        {
            string request;
            try
            {
                request = Environment.GetEnvironmentVariable(EnvironmentVariable);
            }
            catch (Exception)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(request))
                return;

            var isDeveloperChannel =
                channelName != null
                && channelName.StartsWith("Developer", StringComparison.OrdinalIgnoreCase);
            if (!isDeveloperChannel)
            {
                Logger.WriteEvent(
                    $"{EnvironmentVariable} is set but this is the '{channelName}' channel, so it is being "
                        + "ignored. Deliberate breakage is for developer builds only."
                );
                return;
            }

            var parts = request.Split(':');
            var kind = parts[0].Trim().ToLowerInvariant();
            var delay =
                parts.Length > 1 && int.TryParse(parts[1], out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : DefaultDelay;

            Logger.WriteEvent(
                $"*** FreezeSimulator armed: will simulate '{kind}' in {delay.TotalSeconds:F0} seconds. "
                    + $"This Bloom is deliberately going to misbehave. Unset {EnvironmentVariable} to stop it."
            );

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
