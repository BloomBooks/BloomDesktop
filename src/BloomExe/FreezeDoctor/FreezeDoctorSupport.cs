using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using SIL.Reporting;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Publishes just enough about Bloom's health for the Bloom Freeze Doctor
    /// (https://github.com/BloomBooks/bloom-freeze-doctor) to tell a frozen Bloom from a busy one, and a
    /// crash from an orderly shutdown.
    ///
    /// **Why Bloom needs to help at all.** Watching from outside cannot detect the freeze we most expect.
    /// A UI thread blocked in a managed wait on an STA thread — `WaitHandle.WaitOne`, `Task.Wait`,
    /// anything that ends in `CoWaitForMultipleHandles` — keeps dispatching *sent* messages so that COM
    /// still works. The window therefore answers `SendMessageTimeout`, `IsHungAppWindow` reports it as
    /// healthy, and `Process.Responding` says `true`, while Bloom is completely stuck. This was measured,
    /// not assumed. `WM_TIMER` is *not* dispatched by those restricted pumps, so a timer that stops
    /// ticking is the one signal that gives the freeze away — and since Bloom's UI thread awaits WebView2
    /// constantly, we should expect this shape of freeze to be common rather than exotic.
    ///
    /// **The rules this code lives by**, because it runs on the UI thread and in the shutdown path of an
    /// application whose shutdown has historically been fragile:
    ///
    /// - It must never throw. Every entry point swallows its own failures.
    /// - It must never block. Nothing here waits on anything.
    /// - It must never matter. If the channel cannot be created, Bloom carries on exactly as before, and
    ///   the Doctor falls back to watching from outside as it does for every Bloom already in the field.
    /// </summary>
    public static class FreezeDoctorSupport
    {
        /// <summary>
        /// How often the UI thread reports in. Frequent enough that a freeze is obvious within the
        /// Doctor's shortest threshold, cheap enough to be invisible: this is one `WM_TIMER` and a
        /// handful of writes to a page of memory already resident.
        /// </summary>
        private static readonly TimeSpan UiHeartbeatInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// How often the background thread reports in. Its purpose is comparison: if the UI heartbeat is
        /// stale and this one is fresh, the UI thread is blocked; if both are stale, the whole process is
        /// wedged — a garbage collection that will not finish, or a suspended process.
        /// </summary>
        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

        private static DoctorChannelWriter _channel;

        // Explicitly the WinForms timer, and that is the whole point: it delivers WM_TIMER through the
        // message loop, so it stops ticking exactly when the UI thread stops pumping. A
        // System.Threading.Timer would keep ticking right through the freeze we are trying to detect.
        private static System.Windows.Forms.Timer _uiHeartbeat;
        private static Thread _watchdog;
        private static int _shutdownPhase;
        private static bool _started;

        /// <summary>
        /// Starts publishing. Call once, on the UI thread, immediately before entering the message loop:
        /// the UI heartbeat is a WinForms timer, so it only ticks once messages are being pumped, which is
        /// exactly the property that makes it a useful signal.
        /// </summary>
        public static void Start()
        {
            if (_started)
                return;
            _started = true;
            try
            {
                _channel = new DoctorChannelWriter(Process.GetCurrentProcess().Id);
                if (!_channel.IsOpen)
                    return; // Another process owns the name, or the OS refused. Nothing more to do.

                _channel.SetActivity("starting up");
                // Authoritative, and worth more than the Doctor's own guess: it is why a developer
                // stopping their debugger never produces a report.
                _channel.SetDebuggerAttached(Debugger.IsAttached);

                _uiHeartbeat = new System.Windows.Forms.Timer
                {
                    Interval = (int)UiHeartbeatInterval.TotalMilliseconds,
                };
                _uiHeartbeat.Tick += (sender, args) => SafeRecordUiTick();
                _uiHeartbeat.Start();

                _watchdog = new Thread(WatchdogLoop)
                {
                    Name = "Freeze Doctor watchdog",
                    // Background, so it can never be the reason Bloom fails to exit. The Doctor treats a
                    // vanished heartbeat as the process being gone, which by then it is.
                    IsBackground = true,
                    // Slightly above normal so that a machine thrashing under load does not starve the
                    // very signal we use to distinguish thrashing from a deadlock.
                    Priority = ThreadPriority.AboveNormal,
                };
                _watchdog.Start();

                // ProcessExit is the whole of the clean-exit proof, and the reason it is a hook rather
                // than an edit to each exit path: it runs for a normal return from Main and for
                // Environment.Exit, and NOT for FailFast, TerminateProcess, or an access violation. That
                // is precisely the line the Doctor wants drawn, and no future exit path in Program.cs can
                // forget to honour it.
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => RecordCleanExit();

                Logger.WriteEvent(
                    "Freeze Doctor channel opened; publishing health to any Doctor watching"
                );
            }
            catch (Exception e)
            {
                // Deliberately swallowed. Diagnostics that can break the application they diagnose are
                // worse than no diagnostics at all.
                TryLog("Freeze Doctor channel could not be opened", e);
            }
        }

        /// <summary>
        /// Says what Bloom is doing, in words fit to appear at the top of a bug report — "Publishing to
        /// BloomPUB", "Saving Foo.htm". Truncated if very long; safe to call from any thread.
        /// </summary>
        public static void SetActivity(string activity)
        {
            try
            {
                _channel?.SetActivity(activity);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Marks work that legitimately blocks the UI for a long time — publishing, uploading, making a
        /// PDF. This buys Bloom patience rather than silence: the Doctor raises its threshold from one
        /// minute to five, and still reports if Bloom is stuck beyond that.
        /// </summary>
        public static void SetLongOperation(bool inProgress)
        {
            try
            {
                _channel?.SetLongOperation(inProgress);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records how far shutdown has got. Called at the points Bloom passes through on its way out, so
        /// that a Bloom which dies mid-shutdown can say *where* it stopped rather than merely that it did
        /// — which is the same evidence the "UI gone but process alive" case needs.
        /// </summary>
        public static void SetShutdownPhase(int phase)
        {
            try
            {
                _shutdownPhase = phase;
                _channel?.SetShutdownPhase(phase);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records that shutdown ran to completion. Its ABSENCE is what the Doctor treats as evidence, so
        /// this must be written only when Bloom really did shut down in an orderly way.
        /// </summary>
        private static void RecordCleanExit()
        {
            try
            {
                _channel?.SetShutdownPhase(_shutdownPhase);
                _channel?.RecordCleanExit();
            }
            catch (Exception)
            {
                // Losing the proof means the Doctor may report an exit that was in fact clean. Annoying,
                // but far better than delaying or breaking Bloom's shutdown to avoid it.
            }
        }

        /// <summary>
        /// The background heartbeat. Deliberately does almost nothing: it exists to prove the process as a
        /// whole is still scheduling threads, so anything it did beyond that would only add ways for it to
        /// stop for reasons that are not the ones we care about.
        /// </summary>
        private static void WatchdogLoop()
        {
            while (true)
            {
                try
                {
                    _channel?.RecordWatchdogTick();
                    Thread.Sleep(WatchdogInterval);
                }
                catch (Exception)
                {
                    // Never let this thread die of an exception; the Doctor reads its silence as the
                    // process being wedged, which would be a false alarm.
                    try
                    {
                        Thread.Sleep(WatchdogInterval);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            }
        }

        private static void SafeRecordUiTick()
        {
            try
            {
                _channel?.RecordUiTick();
            }
            catch (Exception) { }
        }

        private static void TryLog(string message, Exception e)
        {
            try
            {
                Logger.WriteError(message, e);
            }
            catch (Exception) { }
        }
    }
}
