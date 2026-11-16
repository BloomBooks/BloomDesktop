using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.WinForms;
using SIL.Reporting;

namespace Bloom.Utils
{
    /// <summary>
    /// The "Simulate a slow computer" testing mode (BL-17084). Many people run Bloom on machines at
    /// least five times slower than a developer's, so something that takes 650 ms for us takes three
    /// or four seconds for them. Turned on, this makes Bloom run roughly as it would on such a machine:
    /// - every WebView2 gets Chromium's CPU throttling (the DevTools command
    ///   Emulation.setCPUThrottlingRate, as in the Performance panel of DevTools), which slows
    ///   JavaScript, layout and painting in all its frames;
    /// - Bloom's own process is limited to two cores and put in a Windows job object with a hard CPU
    ///   rate cap of 3/10 of one core in all, which a single busy thread can use alone but all of
    ///   Bloom's threads must share.
    /// The choice lasts for this run of Bloom only, so that nobody is left with a slow Bloom after
    /// forgetting it was on.
    /// </summary>
    /// <remarks>
    /// A CPU cap is not the same as a slower CPU: a capped process runs at full speed until it has
    /// used its share of each scheduling interval, then waits. Total times stretch about as they would
    /// on a slow machine, but short tasks may stutter rather than slow down evenly.
    /// Nothing here slows the disk or the network.
    /// The job object lets child processes break away from it, so processes Bloom starts (including
    /// the WebView2 runtime's msedgewebview2.exe processes) are not capped by it; the DevTools
    /// throttling is what slows the web front end. But every process Bloom starts while the
    /// simulation is on inherits the two-core affinity, as it would on a two-core machine. That
    /// includes a Bloom that replaces this one (a restart, or Velopack applying an update), which
    /// would otherwise run on two cores with the simulation off; see
    /// <see cref="UndoLimitInheritedFromParent"/>.
    /// </remarks>
    public static class SlowComputerSimulation
    {
        // How many times DevTools slows each WebView2's CPU work.
        private const int kWebViewSlowdownFactor = 5;

        // Old machines often have two cores.
        private const int kSimulatedCoreCount = 2;

        // How much of one core Bloom's own process may use in all, in tenths. See LimitThisProcess.
        private const int kCoreShareTenths = 3;

        // Guards s_webViews, and the pairing of s_isOn with it: off-screen WebView2s register on
        // threads of their own, while SetOn runs on the UI thread.
        private static readonly object s_lock = new object();
        private static volatile bool s_isOn;
        private static IntPtr s_job = IntPtr.Zero;
        private static IntPtr s_originalAffinity;

        // While the simulation is on, holds the affinity Bloom had before it, for any Bloom that this
        // one starts. Environment variables are inherited, so it reaches a Bloom started by way of
        // another program (Velopack's Update.exe) as well as one started directly.
        private const string kAffinityBeforeSimulationVariable =
            "BLOOM_AFFINITY_BEFORE_SLOW_COMPUTER_SIMULATION";

        /// <summary>
        /// Called first thing at startup. If the Bloom that started this one (directly or through
        /// another program) had the simulation on, this process inherited its two-core affinity;
        /// put back the affinity that Bloom had before the simulation. The simulation itself always
        /// starts off.
        /// </summary>
        public static void UndoLimitInheritedFromParent()
        {
            var saved = Environment.GetEnvironmentVariable(kAffinityBeforeSimulationVariable);
            if (saved == null)
                return;
            Environment.SetEnvironmentVariable(kAffinityBeforeSimulationVariable, null);
            Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)long.Parse(saved);
        }

        // The WebView2s that have finished initializing, so that turning the simulation on or off
        // can reach each of them. Weak, so that this list never keeps a WebView2 alive: it is filled
        // for every WebView2 in every run of Bloom, whether or not anyone uses this mode.
        private static readonly List<WeakReference<WebView2>> s_webViews =
            new List<WeakReference<WebView2>>();

        /// <summary>
        /// Whether the simulation is on in this run of Bloom.
        /// </summary>
        public static bool IsOn => s_isOn;

        /// <summary>
        /// Turn the simulation on or off: cap or uncap Bloom's own process, and throttle or
        /// unthrottle every live WebView2. WebView2s created later pick up the setting in
        /// <see cref="RegisterWebView"/>.
        /// </summary>
        public static void SetOn(bool value)
        {
            if (value == s_isOn)
                return;
            if (value)
                LimitThisProcess();
            else
                UnlimitThisProcess();
            var webViews = new List<WebView2>();
            lock (s_lock)
            {
                s_isOn = value;
                foreach (var reference in s_webViews)
                {
                    if (reference.TryGetTarget(out var webView))
                        webViews.Add(webView);
                }
            }
            Logger.WriteEvent($"Simulate a slow computer: {(value ? "on" : "off")}");
            foreach (var webView in webViews)
                ApplyThrottlingTo(webView, value);
        }

        /// <summary>
        /// Called when a WebView2 has finished initializing, on the thread that owns it. Throttles it
        /// at once if the simulation is on, and remembers it so that turning the simulation on or off
        /// later reaches it too.
        /// </summary>
        public static void RegisterWebView(WebView2 webView)
        {
            bool isOn;
            lock (s_lock)
            {
                // Drop the entries for WebView2s that have been collected, so the list stays as
                // short as the number of live ones.
                s_webViews.RemoveAll(reference => !reference.TryGetTarget(out _));
                s_webViews.Add(new WeakReference<WebView2>(webView));
                isOn = s_isOn;
            }
            if (isOn)
                ApplyThrottlingTo(webView, true);
        }

        /// <summary>
        /// Send the DevTools CPU throttling command to one WebView2. A rate of 1 means no throttling.
        /// The command must be sent on the thread that owns the WebView2: the UI thread for those on
        /// screen, a thread of its own for an off-screen one (see OffScreenBrowser). So from any
        /// other thread this posts itself to the owner's message loop.
        /// </summary>
        private static async void ApplyThrottlingTo(WebView2 webView, bool throttle)
        {
            try
            {
                if (webView.InvokeRequired)
                {
                    webView.BeginInvoke(new Action(() => ApplyThrottlingTo(webView, throttle)));
                    return;
                }
                // Null once the control has been disposed.
                var core = webView.CoreWebView2;
                if (core == null)
                    return;
                var rate = throttle ? kWebViewSlowdownFactor : 1;
                await core.CallDevToolsProtocolMethodAsync(
                    "Emulation.setCPUThrottlingRate",
                    $"{{\"rate\":{rate}}}"
                );
            }
            catch (Exception e) when (e is InvalidOperationException || e is COMException)
            {
                // InvalidOperationException: the WebView2 was disposed (or its thread has finished)
                // between our taking the list of WebView2s and getting here, so there is no window
                // to post to or no CoreWebView2 to call. COMException: it closed, or its browser
                // process died, while the command was on its way. This method is async void, so
                // anything not caught here would surface as an error report. None of these is worth
                // more than a log entry in a testing mode.
                Logger.WriteEvent(
                    $"Simulate a slow computer: could not set the CPU throttling rate of a WebView2: {e.Message}"
                );
            }
        }

        /// <summary>
        /// Limit Bloom's own process to <see cref="kSimulatedCoreCount"/> cores and cap it, through a
        /// job object, to 3/10 of what one core could give it (<see cref="kCoreShareTenths"/>).
        /// </summary>
        private static void LimitThisProcess()
        {
            var process = Process.GetCurrentProcess();
            if (s_job == IntPtr.Zero)
                s_job = CreateJobForThisProcess(process);
            s_originalAffinity = process.ProcessorAffinity;
            Environment.SetEnvironmentVariable(
                kAffinityBeforeSimulationVariable,
                s_originalAffinity.ToInt64().ToString()
            );
            var coreCount = Math.Min(kSimulatedCoreCount, Environment.ProcessorCount);
            process.ProcessorAffinity = (IntPtr)((1L << coreCount) - 1);
            // The rate is a share of the cycles of the whole machine, in hundredths of a percent,
            // and it caps the process as a whole, not each thread. So no cap is right for every
            // kind of work. A fifth of one core slows a lone busy thread (usually the UI thread)
            // five times, but leaves all the other threads competing for that same fifth, which
            // slows Bloom far more than a real slow machine would. A fifth of each of the two
            // cores lets a lone thread run at two fifths of full speed, which is hardly slow at
            // all. We split the difference: 3/10 of a core in all (3 / 10 / 8 of an eight-core
            // machine).
            var rate = 10000 * kCoreShareTenths / (10 * Environment.ProcessorCount);
            SetCpuRateControl(
                JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
                rate
            );
        }

        /// <summary>
        /// Undo <see cref="LimitThisProcess"/>. A process cannot leave a job object, so Bloom stays in
        /// the job, but with the job's cap switched off.
        /// </summary>
        private static void UnlimitThisProcess()
        {
            SetCpuRateControl(0, 0);
            Process.GetCurrentProcess().ProcessorAffinity = s_originalAffinity;
            Environment.SetEnvironmentVariable(kAffinityBeforeSimulationVariable, null);
        }

        /// <summary>
        /// Make a job object that lets child processes break away silently, and put this process in it.
        /// The handle stays open for the life of the process, since closing it would end the job.
        /// </summary>
        private static IntPtr CreateJobForThisProcess(Process process)
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            ThrowIfFailed(job != IntPtr.Zero);
            // Windows accepts the breakaway flags only in the extended form of the limits.
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK,
                },
            };
            ThrowIfFailed(
                SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformation,
                    ref limits,
                    (uint)Marshal.SizeOf(limits)
                )
            );
            ThrowIfFailed(AssignProcessToJobObject(job, process.Handle));
            return job;
        }

        /// <summary>
        /// Set the job's CPU rate control: the flags, and the rate in hundredths of a percent of the
        /// whole machine. Flags of 0 switch the control off.
        /// </summary>
        private static void SetCpuRateControl(uint flags, int rate)
        {
            var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = flags,
                CpuRate = (uint)rate,
            };
            ThrowIfFailed(
                SetInformationJobObject(
                    s_job,
                    JobObjectCpuRateControlInformation,
                    ref info,
                    (uint)Marshal.SizeOf(info)
                )
            );
        }

        /// <summary>
        /// Throw the error of the Windows call that just failed, if it did.
        /// </summary>
        private static void ThrowIfFailed(bool succeeded)
        {
            if (!succeeded)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private const int JobObjectExtendedLimitInformation = 9;
        private const int JobObjectCpuRateControlInformation = 15;
        private const uint JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x1000;
        private const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
        private const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;

            // IO_COUNTERS: six 64-bit counts, unused here.
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        // In the Windows header the second field is a union of CpuRate, Weight, and a pair of
        // 16-bit MinRate/MaxRate; we only use CpuRate.
        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            public uint ControlFlags;
            public uint CpuRate;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int infoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info,
            uint infoLength
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int infoClass,
            ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info,
            uint infoLength
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}
