using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
// The wire format Bloom shares with the Freeze Doctor: one project, src/BloomFreezeDoctor.Protocol,
// compiled into both sides from the same source, so the two cannot hold copies that disagree.
using BloomFreezeDoctor.Protocol;
using SIL.Reporting;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Publishes just enough about Bloom's health for the Bloom Freeze Doctor (which lives in this
    /// repository, under src/BloomFreezeDoctor) to tell a frozen Bloom from a busy one, and a crash from an
    /// orderly shutdown.
    ///
    /// **Why Bloom has to help at all.** Watching from outside cannot detect the freeze we most expect.
    /// A UI thread blocked in a managed wait on an STA thread — `WaitHandle.WaitOne`, `Task.Wait`, anything
    /// ending in `CoWaitForMultipleHandles` — keeps dispatching *sent* messages so that COM still works, so
    /// the window answers `SendMessageTimeout`, `IsHungAppWindow` calls it healthy and `Process.Responding`
    /// returns true while Bloom is completely stuck. Measured on a real Bloom: nine minutes frozen,
    /// reported responsive throughout. `WM_TIMER` is *not* dispatched by those restricted pumps, so a timer
    /// that stops ticking is the one signal that gives the freeze away — and since Bloom's UI thread awaits
    /// WebView2 constantly, this is the common shape of freeze rather than an exotic one.
    ///
    /// **The rules this code lives by**, since it runs on the UI thread and in Bloom's shutdown path:
    ///
    /// - It must never throw. Every entry point swallows its own failures; see <see cref="Publish"/>.
    /// - It must never block. Nothing here waits on anything.
    /// - It must never matter. If the channel cannot be created, Bloom carries on exactly as before and the
    ///   Doctor falls back to watching from outside, as it does for every Bloom already in the field.
    /// </summary>
    public static class FreezeDoctorSupport
    {
        /// <summary>
        /// How often the UI thread reports in. One `WM_TIMER` and a few writes to a resident page, so the
        /// cost is invisible.
        ///
        /// **Twice as often as the watchdog below, deliberately.** This is not a liveness check but the
        /// measurement the whole tool rests on — its staleness *is* the freeze signal — so its interval sets
        /// the resolution of that signal and the floor under how tight the Doctor's threshold can safely be
        /// (5 seconds, ten of these). It is also the fragile one, since `WM_TIMER` is the lowest-priority
        /// message there is and a busy-but-live UI can starve it, so ticking twice as often means a
        /// moment's starvation must swallow two ticks rather than one before it looks like a freeze.
        /// </summary>
        private static readonly TimeSpan UiHeartbeatInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// How often the background thread reports in. Its purpose is comparison: if the UI heartbeat is
        /// stale and this one is fresh, the UI thread is blocked; if both are stale, the whole process is
        /// wedged — a garbage collection that will not finish, or a suspended process.
        ///
        /// Slower than the UI heartbeat because nothing is measured against *its* resolution: it needs to
        /// be reliably alive, not finely sampled. It also does real work each tick — publishes what Bloom is
        /// doing, polls for a debugger, and every tenth time rewrites the session file. It doubles as the
        /// latency of the Doctor's quit request, which this thread waits on rather than sleeping blindly, so
        /// one second is also how long a stuck Bloom takes to notice it has been asked to leave.
        /// </summary>
        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

        private static DoctorChannelWriter _channel;

        // Explicitly the WinForms timer, and that is the whole point: it delivers WM_TIMER through the
        // message loop, so it stops ticking exactly when the UI thread stops pumping. A
        // System.Threading.Timer would keep ticking right through the freeze we are trying to detect.
        private static System.Windows.Forms.Timer _uiHeartbeat;
        private static Thread _watchdog;

        /// <summary>Set once the first caller has asked for a crash dump. See RequestDumpBeforeDying.</summary>
        private static int _dumpAlreadyRequested;

        private static int _shutdownPhase;
        private static bool _started;
        private static DoctorSession _session;

        /// <summary>
        /// True once we have exited because the Doctor asked us to. Stops the ProcessExit handler recording
        /// that as an orderly shutdown, which it very much was not.
        /// </summary>
        private static bool _endedAtDoctorsRequest;

        /// <summary>
        /// Serialises every change to <see cref="_session"/>.
        ///
        /// Without it the "Bloom already reported this problem" note could be silently lost: the watchdog
        /// thread rewrites the session every ten seconds by read-modify-write, and if it read the record
        /// before the note was applied and wrote its copy afterwards, the note would vanish — from memory and
        /// from disk. That note is the only thing stopping the Doctor filing a second report about a problem
        /// the user has already reported themselves.
        /// </summary>
        private static readonly object _sessionLock = new object();

        /// <summary>
        /// What Bloom's own code last said it was doing, via <see cref="SetActivity"/>. Kept separately from
        /// the request-derived text so the watchdog can combine the two rather than one erasing the other.
        /// </summary>
        private static string _statedActivity = "";

        /// <summary>
        /// What Bloom says it is doing before it has done anything. Named because two places have to agree
        /// on it: the one that states it, and <see cref="ComposeCurrentActivity"/>, which retires it.
        /// </summary>
        internal const string StartupActivity = "starting up";

        /// <summary>
        /// How often the session file is rewritten. It carries facts that barely change, so this is not
        /// about freshness — it is so that a Doctor installed *after* Bloom started, or one that had not
        /// yet been running when Bloom did, still finds a file describing the Bloom in front of it.
        /// </summary>
        private static readonly TimeSpan SessionRefreshInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How long an unexplained session file is kept. Long enough that a Doctor installed the day after
        /// a crash can still find out about it, short enough not to accumulate.
        /// </summary>
        private static readonly TimeSpan SessionRetention = TimeSpan.FromDays(7);

        /// <summary>
        /// How long a dying Bloom waits for the Doctor to say it has taken up the dump request. The Doctor
        /// polls once a second, so this is generous for merely picking something up; the point of keeping it
        /// short is that a Doctor which is alive but not working cannot hold a crashing Bloom for long.
        /// </summary>
        private static readonly TimeSpan PatienceForPickup = TimeSpan.FromSeconds(3);

        /// <summary>
        /// How long to wait once a dump is known to be underway. Generous because it can afford to be: the
        /// wait ends the moment the Doctor stops existing, so this ceiling only binds a Doctor that is alive
        /// and taking its time — which is the case we want to be patient with, since a dump of a real Bloom
        /// takes seconds and longer on a slow or loaded machine.
        /// </summary>
        private static readonly TimeSpan PatienceForTheDumpItself = TimeSpan.FromSeconds(60);

        /// <summary>How often that wait re-checks whether the Doctor is still there.</summary>
        private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// How far Bloom's shutdown has got, as recorded by <see cref="SetShutdownPhase"/>. A Bloom that
        /// dies part way out can then say *where* it stopped rather than only that it did, and zero — never
        /// having reached the first of these — is how a hard failure is told from an orderly exit.
        ///
        /// Named rather than passed as bare numbers at the call sites, so that the sequence documents itself
        /// and cannot drift away from a comment listing what each number meant.
        /// </summary>
        public static class ShutdownPhase
        {
            public const int MessageLoopReturned = 1;
            public const int SettingsSaved = 2;
            public const int LogWritten = 3;
            public const int ProjectContextDisposed = 4;
        }

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

            // The session file goes first, and happens even if the shared-memory channel cannot be
            // created: it is what a Doctor reads about a Bloom that has already died, so it is the more
            // important of the two for the case where nobody was watching at the time.
            WriteSessionFile();

            // Before anything that can bail out. The on-disk half of the contract does not depend on shared
            // memory, and a session file with no exit record reads as "this run did not shut down
            // properly" - so returning early without this hook would manufacture the very false positive
            // the proof exists to prevent.
            //
            // It runs for a normal return from Main and for Environment.Exit, and NOT for FailFast,
            // TerminateProcess or an access violation, which is exactly the line the Doctor wants drawn and
            // one no future exit path can forget to honour.
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => RecordCleanExit();

            try
            {
                _channel = new DoctorChannelWriter(Process.GetCurrentProcess().Id);
                if (!_channel.IsOpen)
                    return; // Another process owns the name, or the OS refused. Nothing more to do.

                // Through the static wrapper, not _channel directly: the wrapper is what records the text in
                // _statedActivity, which the watchdog composes from. Writing straight to the channel leaves
                // that empty, and the next watchdog tick replaces "starting up" with "no request in flight"
                // - discarding the only clue available about a Bloom that wedges during startup.
                // ComposeCurrentActivity retires this once Bloom has handled a request.
                SetActivity(StartupActivity);
                // Worth more than the Doctor's own guess, and why a developer stopping their debugger never
                // produces a report. Refreshed every second by the watchdog rather than read once, because a
                // debugger can be attached to a running Bloom and detached again - exactly the cases that
                // would otherwise produce a bogus report.
                _channel.SetDebuggerAttached(IsDebuggerAttached());

                _uiHeartbeat = new System.Windows.Forms.Timer
                {
                    Interval = (int)UiHeartbeatInterval.TotalMilliseconds,
                };
                _uiHeartbeat.Tick += (sender, args) => RecordUiTick();
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

                // Ask for a dump on the way down. Hooked here as well as at Program.Run's catch blocks,
                // which see only TargetInvocationException and AccessViolationException from the message
                // loop, whereas this fires for an unhandled exception on ANY thread.
                //
                // It cannot cover Environment.FailFast, which by design runs no managed handlers at all.
                // Those crashes are still recognisable from Windows Error Reporting and the absence of a
                // clean-exit proof; we just get no dump of our own.
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                    RequestDumpBeforeDying();

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
        public static void SetActivity(string activity) =>
            Publish(() =>
            {
                // Remembered as well as published, because the watchdog rewrites the same slot once a
                // second; published alone, the message would survive less than a second. The watchdog
                // composes this with the in-flight request text instead.
                Volatile.Write(ref _statedActivity, activity ?? "");
                _channel?.SetActivity(activity);
            });

        /// <summary>
        /// Runs one publishing step, swallowing whatever it throws.
        ///
        /// Every entry point here goes through this. Diagnostics that can break the application they
        /// diagnose are worse than no diagnostics, and there is nothing any caller could usefully do with a
        /// failure to write a health field — so rather than each method repeating the same empty catch and
        /// the same explanation, the rule lives here once.
        /// </summary>
        private static void Publish(Action step)
        {
            try
            {
                step();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Marks work that legitimately blocks the UI for a long time — publishing, uploading, making a
        /// PDF. This buys Bloom patience rather than silence: the Doctor raises its threshold from one
        /// minute to five, and still reports if Bloom is stuck beyond that.
        ///
        /// Call it through <see cref="LongOperation"/> rather than directly, so the nesting count is kept
        /// and a description of the work goes with it.
        ///
        /// Anything *not* marked that blocks the UI thread for over a minute *without pumping messages* is
        /// filed as a freeze, which is the false positive this exists to prevent. Work behind a modal
        /// progress dialog is safe either way, since ShowDialog runs a nested message loop and the UI
        /// heartbeat keeps ticking.
        ///
        /// Which operations to mark is a judgement about how Bloom really behaves, and cannot be inferred
        /// automatically: "a request has run for a minute" is the same signal as the freeze itself, so it
        /// cannot tell legitimately-slow from wedged. Hence a deliberate call at the few places that know.
        /// </summary>
        public static void SetLongOperation(bool inProgress) =>
            Publish(() => _channel?.SetLongOperation(inProgress));

        /// <summary>
        /// How many long operations are running. Counted rather than a bare boolean because these nest —
        /// publishing an app builds a BloomPUB on the way — and a plain <c>SetLongOperation(false)</c> from
        /// an inner operation would take the patience away while the outer one was still going.
        /// </summary>
        private static int _longOperationDepth;

        /// <summary>
        /// How many long operations are currently running. Exposed for the test that pins the nesting
        /// arithmetic, which is the part with teeth: the flag reaching the shared page is one line and is
        /// covered by the protocol round-trip test, whereas getting the counting wrong is silent and lasts
        /// the whole session — too few decrements and freeze detection stays off for good.
        /// </summary>
        internal static int LongOperationDepth => Volatile.Read(ref _longOperationDepth);

        /// <summary>
        /// Marks a stretch of work that is *deliberately* slow, so the Doctor waits five minutes rather than
        /// one before deciding Bloom has frozen, and says what Bloom is doing while it runs.
        ///
        /// Use it with `using`. That is not style: the alternative is paired calls, and a paired call that
        /// is skipped by an early return or an exception leaves the Doctor permanently patient — which
        /// silently disables freeze detection for the rest of the session, the worst possible failure for
        /// this particular flag.
        ///
        /// Which operations get this is a judgement about how Bloom behaves, not something to infer from the
        /// code: "a request has run for a minute" is the same signal as a freeze, so nothing automatic can
        /// tell legitimately-slow from wedged. The set is deliberately just the major publishing operations — BloomPUB, ePUB, video and app creation, and uploading to or downloading
        /// from Bloom Library — because those routinely take minutes on a large book or a slow connection
        /// and are the ones that would otherwise be reported as freezes.
        /// </summary>
        /// <param name="whatBloomIsDoing">
        /// Plain words for the report, e.g. "making a BloomPUB". This becomes the activity line, so it is
        /// worth writing for whoever reads the card rather than for the code.
        /// </param>
        public static IDisposable LongOperation(string whatBloomIsDoing)
        {
            return new LongOperationScope(whatBloomIsDoing);
        }

        private sealed class LongOperationScope : IDisposable
        {
            private readonly string _previousActivity;

            /// <summary>
            /// What this scope itself put in the slot, so that on the way out it can tell whether the slot
            /// is still its to restore. Two long operations on different threads do not have to nest
            /// tidily, and without this check the one that finished first put back the activity from
            /// *before* it started — overwriting the description of an operation that was still running,
            /// and then being overwritten in turn by a description of work already finished.
            /// </summary>
            private readonly string _whatIPublished;
            private bool _disposed;

            public LongOperationScope(string whatBloomIsDoing)
            {
                _previousActivity = Volatile.Read(ref _statedActivity);
                _whatIPublished = whatBloomIsDoing ?? "";
                Publish(() =>
                {
                    SetActivity(whatBloomIsDoing);
                    if (Interlocked.Increment(ref _longOperationDepth) == 1)
                        SetLongOperation(true);
                });
            }

            public void Dispose()
            {
                if (_disposed)
                    return; // a double Dispose must not decrement twice and cancel somebody else's patience
                _disposed = true;
                Publish(() =>
                {
                    if (Interlocked.Decrement(ref _longOperationDepth) == 0)
                        SetLongOperation(false);
                    // Only put the old activity back if the slot still holds what we put there. If
                    // somebody else has since described their own operation, theirs is the one still
                    // running and ours is the stale news — see _whatIPublished.
                    if (
                        Interlocked.CompareExchange(
                            ref _statedActivity,
                            _previousActivity,
                            _whatIPublished
                        ) == _whatIPublished
                    )
                        _channel?.SetActivity(_previousActivity);
                });
            }
        }

        /// <summary>
        /// Records how far shutdown has got. Called at the points Bloom passes through on its way out, so
        /// that a Bloom which dies mid-shutdown can say *where* it stopped rather than merely that it did
        /// — which is the same evidence the "UI gone but process alive" case needs.
        /// </summary>
        public static void SetShutdownPhase(int phase) =>
            Publish(() =>
            {
                _shutdownPhase = phase;
                _channel?.SetShutdownPhase(phase);
            });

        /// <summary>
        /// Records that shutdown ran to completion. Its ABSENCE is what the Doctor treats as evidence, so
        /// this must be written only when Bloom really did shut down in an orderly way.
        /// </summary>
        private static void RecordCleanExit()
        {
            try
            {
                _channel?.SetShutdownPhase(_shutdownPhase);
                // Only claim a clean exit when the shutdown sequence actually ran.
                //
                // ProcessExit fires for EVERY Environment.Exit, and Bloom has several that are hard failures
                // rather than shutdowns — WebView2 failing to initialise, the non-message-loop branch of the
                // fatal exception handler, and the Doctor asking a zombie to go. None of those reach
                // Program.Run's phase markers, so a phase of 0 means the orderly path was never walked. Using
                // that as the test needs no cooperation from each exit site, which matters because the next
                // hard-failure exit somebody adds will be covered automatically.
                if (_shutdownPhase > 0 && !_endedAtDoctorsRequest)
                    _channel?.RecordCleanExit();
                // Also on disk, because the shared-memory page vanishes once the last handle closes and a
                // Doctor may not have been watching. The file is what a Doctor started tomorrow will read.
                WriteSessionExit();
            }
            catch (Exception)
            {
                // Losing the proof means the Doctor may report an exit that was in fact clean. Annoying,
                // but far better than delaying or breaking Bloom's shutdown to avoid it.
            }
        }

        /// <summary>
        /// Records that Bloom's own reporting has already told us about a problem this run, so the Doctor
        /// does not file a second report about the same thing. Called when a **tracker card** goes out
        /// successfully — the one place a duplicate is possible.
        ///
        /// Deliberately NOT called for a Sentry event. A Sentry event creates no card, so a Doctor report
        /// about the same trouble is not a duplicate of it — it is the only thing that would put the problem
        /// on the board at all, and suppressing on Sentry would lose it.
        /// </summary>
        public static void NoteBloomReportedAProblem(string reportedId)
        {
            try
            {
                lock (_sessionLock)
                {
                    if (_session == null)
                        return;
                    // On the session itself, NOT inside an Exit record. A user can file a problem report and
                    // then carry on working for hours; writing an exit here would describe a running Bloom as
                    // finished, which a later reader would take as proof it shut down properly.
                    _session = _session with
                    {
                        BloomAlreadyReported = true,
                        ReportedId = reportedId,
                    };
                    DoctorSessionStore.TryWrite(_session);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records that this Bloom has been deliberately told to break, and how, so a Doctor can tell a
        /// rehearsal from the real thing.
        ///
        /// Called when the simulator ARMS rather than when the environment variable is seen: the variable
        /// alone proves nothing, since a Beta or Release channel refuses and so does an unrecognised kind.
        ///
        /// Written into the session file rather than the shared page because it has to survive the process:
        /// `failfast`, `crashthread` and `zombie` are exactly the cases where the wreckage is all there is.
        /// </summary>
        public static void NoteSimulatedFailureArmed(string kind)
        {
            try
            {
                lock (_sessionLock)
                {
                    if (_session == null)
                        return;
                    _session = _session with { SimulatedFailure = kind };
                    DoctorSessionStore.TryWrite(_session);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records the facts a Doctor needs but cannot reliably work out from outside — above all the log
        /// file we are actually writing to, and the ports.
        /// </summary>
        private static void WriteSessionFile()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var httpPort = Api.BloomServer.portForHttp;
                // Under the same lock as every other mutation. This one runs before the watchdog thread
                // exists, so nothing can race it today — but a rule with an exception in it is a rule
                // somebody will follow into a bug later.
                var session = new DoctorSession
                {
                    ProcessId = process.Id,
                    StartedAtUtc = process.StartTime.ToUniversalTime(),
                    ExePath = SafeExePath(process),
                    Version = Shell.GetShortVersionInfo(),
                    Channel = ApplicationUpdateSupport.ChannelName,
                    CommandLine = Environment.CommandLine,
                    // The point of the whole file: Bloom recreates Log.txt each run and only falls back to
                    // a random name when another Bloom holds it, so from outside the newest log is the
                    // wrong answer exactly when it matters most.
                    LogPath = Logger.LogPath ?? "",
                    HttpPort = httpPort,
                    // Kept in step with WebView2Browser.RemoteDebuggingPort rather than recomputed
                    // independently, so the two cannot drift apart.
                    CdpPort = httpPort > 0 ? Api.BloomServer.RemoteDebuggingPort : 0,
                    CollectionName = SafeCollectionName(),
                };
                lock (_sessionLock)
                {
                    _session = session;
                    DoctorSessionStore.TryWrite(_session);
                }
                // Note what is NOT done here: pruning old session files. This method runs on the UI thread
                // during startup, and enumerating and deleting a directory's worth of files is exactly the
                // sort of synchronous I/O that has no business on Bloom's startup path. The watchdog thread
                // does it instead, on its first beat.
            }
            catch (Exception e)
            {
                TryLog("Freeze Doctor session file could not be written", e);
            }
        }

        /// <summary>
        /// Rewrites the session file with how this run ended. Called from the same place as the clean-exit
        /// flag, so the on-disk record and the shared-memory record agree.
        /// </summary>
        private static void WriteSessionExit()
        {
            try
            {
                lock (_sessionLock)
                {
                    if (_session == null)
                        return;
                    // Always recorded, even when Bloom has already reported a problem: "the user reported
                    // something" and "we then shut down properly" are independent facts, both worth
                    // knowing, and they live in separate fields.
                    _session = _session with
                    {
                        Exit = new DoctorSessionExit
                        {
                            AtUtc = DateTimeOffset.UtcNow,
                            ShutdownPhase = _shutdownPhase,
                            // Anything that did not walk the orderly path is marked as forced, by the same
                            // phase test as the shared-memory flag above — so a WebView2 startup failure is
                            // not filed away as a tidy shutdown.
                            ForcedByDoctor = _endedAtDoctorsRequest || _shutdownPhase == 0,
                        },
                    };
                    DoctorSessionStore.TryWrite(_session);
                }
            }
            catch (Exception) { }
        }

        private static bool ProcessIsAlive(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SafeExePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string SafeCollectionName()
        {
            try
            {
                var path = Bloom.Properties.Settings.Default.MruProjects?.Latest;
                return string.IsNullOrEmpty(path)
                    ? ""
                    : System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// The background heartbeat. Deliberately does almost nothing beyond proving the process as a whole
        /// is still scheduling threads — anything more would only add ways for it to stop for reasons that
        /// are not the ones we care about. It does also refresh the session file, which is cheap and keeps
        /// that work off the UI thread.
        /// </summary>
        /// <summary>
        /// Reads the PEB's BeingDebugged flag for this process. Wanted alongside
        /// <see cref="Debugger.IsAttached"/> because that one only sees a MANAGED debugger: a native one
        /// (WinDbg without SOS, say) can attach to a running Bloom and stop it while IsAttached stays false
        /// the whole time, which is precisely the case that would otherwise be reported as a crash.
        /// </summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool
        )]
        internal static extern bool IsDebuggerPresent();

        /// <summary>
        /// Whether anything is debugging us, managed or native.
        ///
        /// Cheap enough to poll every second, which is what the watchdog does: IsAttached reads a runtime
        /// flag, and IsDebuggerPresent reads a single byte out of our own process's PEB. Neither is a
        /// blocking call and neither touches Bloom's UI thread.
        ///
        /// What it cannot see, for the record: a NON-INVASIVE attach (`windbg -pv`) sets neither flag, and a
        /// debugger that attaches and detaches entirely between two polls is missed — though one that
        /// actually breaks or kills lasts far longer than a second.
        /// </summary>
        internal static bool IsDebuggerAttached()
        {
            if (Debugger.IsAttached)
                return true;
            try
            {
                return IsDebuggerPresent();
            }
            catch (Exception)
            {
                // A P/Invoke that cannot be resolved must not be able to break the watchdog; the managed
                // answer above is still worth having.
                return false;
            }
        }

        private static void WatchdogLoop()
        {
            var sinceSessionRefresh = TimeSpan.Zero;
            // The Doctor sets this to ask us to exit under our own power when our UI is gone but we are
            // still running. Waiting on it HERE is the point: this thread is still alive long after the UI
            // thread has stopped, which is exactly the situation in which the request gets made.
            var quitRequest = DoctorSignals.TryCreate(
                DoctorSignals.QuitRequestName(Process.GetCurrentProcess().Id)
            );

            // Tidy up previous runs' session files here rather than at startup: this is file enumeration and
            // deletion, and it belongs on a background thread rather than on Bloom's startup path. An
            // unexplained session survives until it ages out, since that is precisely the evidence a Doctor
            // installed after a crash comes looking for.
            try
            {
                DoctorSessionStore.Prune(ProcessIsAlive, SessionRetention);
            }
            catch (Exception) { }

            while (true)
            {
                try
                {
                    _channel?.RecordWatchdogTick();
                    PublishWhatBloomIsDoing();
                    _channel?.SetDebuggerAttached(IsDebuggerAttached());
                    sinceSessionRefresh += WatchdogInterval;
                    if (sinceSessionRefresh >= SessionRefreshInterval)
                    {
                        sinceSessionRefresh = TimeSpan.Zero;
                        RefreshSessionFile();
                    }

                    // Sleep on the quit request rather than sleeping blindly, so one thread does both jobs
                    // and a request is acted on within a second rather than whenever we next wake.
                    if (quitRequest != null)
                    {
                        if (quitRequest.WaitOne(WatchdogInterval))
                        {
                            ExitAtDoctorsRequest();
                            return;
                        }
                        continue;
                    }
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

        /// <summary>
        /// Publishes what Bloom is doing right now, worked out from the in-flight API requests, along with
        /// the server's worker counts.
        ///
        /// This is where the Doctor's most useful sentence comes from. A stack trace says the UI thread is
        /// waiting; this says *which request* has been running for 47 seconds, which is usually the answer.
        /// Computed on this thread rather than in the request path, so the cost falls on a once-a-second
        /// thread instead of on every request.
        /// </summary>
        /// <summary>
        /// Composes what Bloom's own code last said it was doing with what the request table says, rather
        /// than letting either clobber the other.
        ///
        /// Two mistakes to avoid, one on each side. Leaving the last interesting value in place means the
        /// field keeps naming a request that finished minutes ago, and a freeze report blames work that had
        /// already completed. But overwriting it every second makes the public <see cref="SetActivity"/>
        /// entry point useless — "starting up" would survive less than a second, so a Bloom that wedged
        /// during startup would report "no request in flight", which is both wrong and the least helpful
        /// thing it could say.
        ///
        /// Separated out, and internal, so a test can pin that chain: every version of this has been got
        /// wrong once — first by the refresh overwriting the stated text, then by <see cref="Start"/> writing
        /// to the channel directly so there was nothing to carry forward, then by "starting up" never being
        /// retired and so describing an idle Bloom hours later.
        /// </summary>
        internal static string ComposeCurrentActivity() =>
            Compose(
                Volatile.Read(ref _statedActivity),
                ApiActivityTracker.DescribeCurrentActivity(),
                ApiActivityTracker.HasHandledARequest
            );

        /// <summary>
        /// The composition rule on its own, with nothing static in it, so both of its failure directions can
        /// be tested deterministically. Called by <see cref="ComposeCurrentActivity"/> with the live values.
        /// </summary>
        internal static string Compose(string stated, string request, bool bloomHasHandledARequest)
        {
            // "starting up" is true until it isn't, and nothing else was ever going to replace it: Bloom
            // states it once and has no natural "finished starting" moment to clear it at. Handling an API
            // request IS that moment — the UI is up and talking to the server — and deciding it here means no
            // future startup path has to remember to say so.
            if (stated == StartupActivity && bloomHasHandledARequest)
                stated = "";
            return (string.IsNullOrEmpty(stated), request == null) switch
            {
                (false, false) => stated + " | " + request,
                (false, true) => stated,
                (true, false) => request,
                _ => "no request in flight",
            };
        }

        private static void PublishWhatBloomIsDoing()
        {
            try
            {
                _channel?.SetActivity(ComposeCurrentActivity());

                var server = Api.BloomServer._theOneInstance;
                if (server != null)
                    _channel?.SetServerWorkerCounts(
                        server.BusyWorkerCount,
                        server.BlockedWorkerCount
                    );
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Rewrites the session file with anything that may have changed since startup — the ports, which
        /// are assigned after we first wrote it, and the collection, which changes when the user switches.
        /// </summary>
        private static void RefreshSessionFile()
        {
            try
            {
                lock (_sessionLock)
                {
                    if (_session == null)
                        return;
                    var httpPort = Api.BloomServer.portForHttp;
                    // Composed on the CURRENT value inside the lock, so a note written by another thread a
                    // moment ago is carried forward rather than overwritten.
                    var refreshed = _session with
                    {
                        LogPath = Logger.LogPath ?? _session.LogPath,
                        HttpPort = httpPort,
                        CdpPort = httpPort > 0 ? Api.BloomServer.RemoteDebuggingPort : 0,
                        CollectionName = SafeCollectionName(),
                    };
                    // Only touch the disk when something actually changed: this runs every ten seconds for
                    // the life of the process, and a pointless write every ten seconds is a pointless write.
                    if (refreshed == _session)
                        return;
                    _session = refreshed;
                    DoctorSessionStore.TryWrite(_session);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Exits because the Doctor asked us to, having decided our UI is gone and we are in the user's way.
        ///
        /// We exit *ourselves* rather than being killed, which is worth the round trip: `Environment.Exit`
        /// runs the `ProcessExit` handlers, so Bloom's single-instance token is released properly and its own
        /// record of the shutdown is written. Being killed from outside would leave both undone. Note we do
        /// NOT try to shut down gracefully — a Bloom in this state has already failed to do that, which is
        /// why anyone is asking.
        /// </summary>
        private static void ExitAtDoctorsRequest()
        {
            try
            {
                Logger.WriteEvent(
                    "The Bloom Freeze Doctor asked this process to exit (its UI is gone and it is holding "
                        + "the single-instance token). Exiting."
                );
            }
            catch (Exception) { }

            // Mark this as what it is BEFORE exiting. Environment.Exit runs the ProcessExit handler, which
            // writes the clean-exit proof — and this was emphatically not a clean exit. Without this flag a
            // zombie we had to end would be recorded as having shut down properly, which is exactly the
            // conclusion the proof exists to prevent anyone drawing.
            _endedAtDoctorsRequest = true;

            try
            {
                // 1 rather than 0: this was not a normal exit, and the exit code should say so.
                Environment.Exit(1);
            }
            catch (Exception)
            {
                // If even that fails, the Doctor's fallback is to kill us, which is what it is there for.
            }
        }

        /// <summary>
        /// Asks a watching Doctor to dump this process, and waits briefly for it. Call from a crash path, as
        /// late as possible: a dump taken from outside is worth more than one a dying process takes of
        /// itself, but only if the process is still there to dump.
        ///
        /// **The zero-timeout check comes first, and it is the important part.** Nearly every user has no
        /// Doctor installed, and an unconditional pause here would make every crash worse for all of them to
        /// benefit the few. So: if nobody is watching, this returns immediately and costs nothing.
        ///
        /// **And when nobody is watching, that is the end of it — Bloom does NOT dump itself.** That is
        /// deliberate, not an omission: with no Doctor running there is nothing to file the
        /// dump, so it would sit on a user's disk at 15-20 MB a crash waiting for somebody to think of
        /// asking for it. If that ever looks worth building, the shape that fits what already exists is to
        /// record the path in the session file - which is designed to outlive the process - so that a Doctor
        /// started or installed later finds the crash and attaches it; not a parallel collection mechanism.
        /// </summary>
        public static void RequestDumpBeforeDying()
        {
            // Once per process, whoever asks. Two routes reach here for one crash - Bloom's fatal handler
            // and the AppDomain hook - and only the first should pay the wait.
            if (Interlocked.Exchange(ref _dumpAlreadyRequested, 1) != 0)
                return;
            try
            {
                var pid = Process.GetCurrentProcess().Id;
                if (!DoctorSignals.Exists(DoctorSignals.WatchingName(pid)))
                    return; // No Doctor. Do not delay this crash by even a millisecond.

                // Past that check a Doctor is there, so the logging below costs only the few users who have
                // one - and on a path whose entire purpose is gathering diagnostics, silence is the one
                // unaffordable failure. Each outcome says which it was.
                Logger.WriteEvent("Asking the Bloom Freeze Doctor for a dump of this crash");

                if (!DoctorSignals.TrySignal(DoctorSignals.DumpRequestName(pid)))
                {
                    Logger.WriteEvent(
                        "Could not signal the Bloom Freeze Doctor for a dump; it may have stopped watching"
                    );
                    return;
                }

                // Wait briefly for the Doctor to say it has started. Its tick is a second, so this is
                // generous for merely picking a request up; if nothing does, there is nothing to wait for
                // and a crashing Bloom should not be held any longer.
                if (!DoctorSignals.WaitFor(DoctorSignals.DumpStartedName(pid), PatienceForPickup))
                {
                    Logger.WriteEvent(
                        "Asked the Bloom Freeze Doctor for a crash dump, but nothing picked it up"
                    );
                    return;
                }

                // Underway, so be patient: a dump of a real Bloom takes seconds, longer on the slow machines
                // that need it most, and giving up early does not merely delay it but loses it, since the
                // dying process is what writes the dump over the diagnostics pipe. Patience is safe here
                // because the wait ends as soon as the Doctor stops existing - see
                // DoctorSignals.WaitWhileTheOtherSideLives for how that is known.
                var dumped = DoctorSignals.WaitWhileTheOtherSideLives(
                    DoctorSignals.DumpCompleteName(pid),
                    DoctorSignals.WatchingName(pid),
                    PatienceForTheDumpItself,
                    WaitSlice
                );
                Logger.WriteEvent(
                    dumped
                        ? "The Bloom Freeze Doctor captured a dump of this crash"
                        : "The Bloom Freeze Doctor began a dump of this crash but did not finish it"
                );
            }
            catch (Exception)
            {
                // A crash path is the last place to introduce a new way to fail.
            }
        }

        private static void RecordUiTick() => Publish(() => _channel?.RecordUiTick());

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
