using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.ToPalaso;
using BloomTemp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Utils
{
    delegate void EndOfLifeCallback(Measurement step);

    // usage:
    // using (PerformanceMeasurement.Global.Measure("select page")) { ..do something }
    public class PerformanceMeasurement : IDisposable
    {
        private readonly BloomWebSocketServer _webSocketServer;
        public static PerformanceMeasurement Global;

        private string _csvFilePath;
        private StreamWriter _stream;
        private const string kWebsocketContext = "performance";
        public bool CurrentlyMeasuring { get; private set; }
        private Measurement _measurement;
        private Measurement _previousMeasurement;
        private List<Measurement> _measurements = new List<Measurement>();

        // The only instance of this is created by autofac
        public PerformanceMeasurement(BloomWebSocketServer webSocketServer)
        {
            _webSocketServer = webSocketServer;
            Global = this; // note, this is changed if we change collections and the ProjectContext makes a new one
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "performance/showCsvFile",
                (request) =>
                {
                    ProcessExtra.SafeStartInFront(_csvFilePath);
                    request.PostSucceeded();
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                "performance/applicationInfo",
                (request) =>
                {
                    request.ReplyWithText(
                        $"Bloom {Shell.GetShortVersionInfo()} {ApplicationUpdateSupport.ChannelName}"
                    );
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                "performance/allMeasurements",
                (request) =>
                {
                    List<object> l = new List<object>();
                    foreach (var measurement in _measurements)
                    {
                        l.Add(measurement.GetSummary());
                    }
                    request.ReplyWithJson(l.ToArray());
                },
                false
            );
        }

        public void StartMeasuring()
        {
            CurrentlyMeasuring = true;

            if (_stream != null)
            {
                _stream.Close();
                _stream.Dispose();
                // no, leave it and its contents around: _folder.Dispose();
            }

            _csvFilePath = TempFileUtils.GetTempFilepathWithExtension(".csv");
            _stream = RobustFile.CreateText(_csvFilePath);
            _stream.AutoFlush = true;

            try
            {
                _stream.WriteLine(Form.ActiveForm.Text);
            }
            catch (Exception)
            {
                // swallow. This happens when we call from a browser, while debugging.
            }
            using (Measure("Initial Memory Reading", refreshSubprocessList: true)) { }
        }

        /// <summary>
        /// If this is never called, that's fine.
        /// </summary>
        public void StopMeasuring()
        {
            CurrentlyMeasuring = false;
            if (_stream != null)
            {
                _stream.Close();
                _stream.Dispose();
                // no, leave it and its contents around: _folder.Dispose();
                _stream = null;
            }
            Measurement.PerfPoint.CleanupSubprocessList();
        }

        readonly string[] _majorActions = new string[]
        {
            "EditBookCommand",
            "SelectedTabChangedEvent",
        };

        /// <summary>
        /// This is the main public method, called anywhere in the c# code that we want to measure something.
        /// What we're measuring is the memory used and the time it took from when this is called until
        /// the return value is disposed.
        /// </summary>
        /// <returns>an object that should be disposed of to end the measurement</returns>
        public IDisposable MeasureMaybe(
            Boolean doMeasure,
            string actionLabel,
            string actionDetails = ""
        )
        {
            if (doMeasure)
                return Measure(
                    actionLabel,
                    actionDetails,
                    refreshSubprocessList: _majorActions.Contains(actionLabel)
                );
            else
                return new Lifespan(null, null);
        }

        public IDisposable Measure(
            string actionLabel,
            string actionDetails = "",
            bool refreshSubprocessList = false
        )
        {
            if (!CurrentlyMeasuring)
                return null;

            // skip nested measurements
            if (_measurement != null)
            {
                // there are too many of these to keep bugging us

                //NonFatalProblem.Report(ModalIf.None, PassiveIf.All,$"Performance measurement cannot handle nested actions ('{action}' inside of '{_measurement._action}')");

                return new Lifespan(null, null);
            }

            var previousSize = _previousMeasurement?.LastKnownSize ?? 0L;
            Measurement m = null;
            m = new Measurement(actionLabel, actionDetails, previousSize, refreshSubprocessList);

            _previousMeasurement = m;
            _measurement = m;
            return new Lifespan(m, MeasurementEnded);
        }

        // This is only called if there is a Lifespan generated (and it gets disposed) and that will only happen
        // if Measure() decided that we are in measuring mode.
        private void MeasurementEnded(Measurement measure)
        {
            _stream.WriteLine(measure.GetCsv());
            _webSocketServer.SendString(
                kWebsocketContext,
                "event",
                JsonConvert.SerializeObject(measure.GetSummary())
            );
            _measurement = null;
            _measurements.Add(measure);
        }

        public void Dispose()
        {
            _stream?.Close();
            _stream = null;
            CurrentlyMeasuring = false;
        }
    }

    /// <summary>
    /// Just something to stop a particular measurement at the end of a using() block.
    /// </summary>
    class Lifespan : IDisposable
    {
        private readonly Measurement _measurement;
        private readonly EndOfLifeCallback _callback;

        public Lifespan(Measurement measurement, EndOfLifeCallback callback)
        {
            _measurement = measurement;
            _callback = callback;
        }

        public void Dispose()
        {
            _measurement?.Finish();
            _callback?.Invoke(_measurement);
        }
    }

    public class Measurement
    {
        public readonly string _actionLabel;
        private readonly string _actionDetails;
        private readonly PerfPoint _start;
        private PerfPoint _end;
        private readonly long _previousPrivateBytesKb;
        private readonly bool _refreshSubprocessList;

        public long LastKnownSize => _end?.privateBytesKb ?? _start?.privateBytesKb ?? 0L;

        public Measurement(
            string actionLabel,
            string actionDetails,
            long previousPrivateBytesKb,
            bool refreshSubprocessList = false
        )
        {
            _actionLabel = actionLabel;
            _actionDetails = actionDetails;
            _previousPrivateBytesKb = previousPrivateBytesKb;
            _refreshSubprocessList = refreshSubprocessList;
            try
            {
                _start = new PerfPoint(false); // Use the old list of subprocesses, if any, for the starting point.
            }
            catch (Exception e)
            {
                // The OS can get into a state where we can't load performance counters (BL-10689).
                // If we're in such a state, just don't report performance.
                Logger.WriteError("Failed to create PerfPoint", e);
                // Reporting will do the right thing if _start is null
            }
        }

        public void Finish()
        {
            try
            {
                _end = new PerfPoint(_refreshSubprocessList);
            }
            catch (Exception e)
            {
                // The OS can get into a state where we can't load performance counters (BL-10689).
                // If we're in such a state, just don't report performance.
                Logger.WriteError("Failed to create Measurement", e);
                // Reporting on the measurement will indicate failure if _end stays null
            }
        }

        public object GetSummary()
        {
            if (!IsComplete)
            {
                return new
                {
                    action = _actionLabel + " measurement failed",
                    details = _actionDetails,
                    privateBytes = 0,
                    duration = 0,
                };
            }
            return new
            {
                action = _actionLabel,
                details = _actionDetails,
                privateBytes = _end.privateBytesKb,
                duration = Duration,
            };
        }

        public double Duration
        {
            get
            {
                if (!IsComplete)
                    return 0;

                TimeSpan diff = _end.when - _start.whenReady;

                return Math.Round(diff.TotalMilliseconds / 1000, 2);
            }
        }

        public bool IsComplete => _start != null && _end != null;

        public string GetCsv()
        {
            if (!IsComplete)
            {
                // I'm trying to make this look enough like the usual message to indicate something went wrong,
                // but not to crash any Javascript looking for the usual message.
                return $"{_actionLabel} measurement failed,{_actionDetails},0:00,0,0";
            }
            TimeSpan diff = _end.when - _start.whenReady;
            var time = diff.ToString(@"ss\.ff");
            return $"{_actionLabel},{_actionDetails},{time},{_end.privateBytesKb},{(_end.privateBytesKb - _previousPrivateBytesKb)}";
        }

        public override string ToString()
        {
            if (!IsComplete)
                return $"Measurement: details=\"{_actionDetails}\"; measurement failed";

            // For a ToString() summary, the delta/previousSizeKb is not important.
            return $"Measurement: details=\"{_actionDetails}\"; start={_start.privateBytesKb}KB ({_start.whenReady}); end={_end?.privateBytesKb}KB ({_end?.when})";
        }

        public class PerfPoint
        {
            public long pagedMemoryKb;
            public long workingSetKb;
            public DateTime when;
            public DateTime whenReady;
            public long privateBytesKb;

            static readonly List<Process> _subProcesses = new List<Process>();

            public PerfPoint(bool refreshSubprocessList)
            {
                this.when = DateTime.Now;
                using (var proc = Process.GetCurrentProcess())
                {
                    var afterGetProcess = DateTime.Now;
                    proc.Refresh(); // ensure current measurements
                    // From observation, PagedMemorySize64, WorkingSet64, and PrivateMemorySize64 are all multiples of 1024,
                    // so we don't actually lose any information by these divisions.
                    pagedMemoryKb = proc.PagedMemorySize64 / 1024;
                    workingSetKb = proc.WorkingSet64 / 1024;
                    privateBytesKb = proc.PrivateMemorySize64 / 1024;
                    if (refreshSubprocessList)
                    {
                        CleanupSubprocessList();
                        var descendants = GetAllDescendantProcesses(proc);
                        if (descendants.Any())
                            _subProcesses.AddRange(descendants);
                    }
                    // Enhance: we could report the bytes of each sub-process, but that would be a lot of data.
                    // Or: we could report the total bytes of all sub-processes, but would that be helpful?
                    // Or: we could report the maximum bytes of all sub-processes, but would that be helpful?
                    foreach (var p in _subProcesses)
                    {
                        if (p.HasExited)
                            continue;
                        p.Refresh(); // ensure current measurements
                        pagedMemoryKb += p.PagedMemorySize64 / 1024;
                        workingSetKb += p.WorkingSet64 / 1024;
                        privateBytesKb += p.PrivateMemorySize64 / 1024;
                    }
                }
                this.whenReady = DateTime.Now;
                Debug.WriteLine($"PerfPoint created in {(whenReady - when).TotalMilliseconds}ms");
            }

            private static List<Process> GetAllDescendantProcesses(Process parent)
            {
                try
                {
                    if (SIL.PlatformUtilities.Platform.IsWindows)
                        return GetAllDescendantProcessesWindows(parent.Id);
                }
                catch
                {
                    // If the fast Windows approach fails for any reason, fall back to WMI below.
                    Debug.WriteLine(
                        $"Failed to get descendant processes for {parent.Id} using Windows API"
                    );
                }

                try
                {
                    return GetAllDescendantProcessesWmi(parent.Id);
                }
                catch
                {
                    // Just give up. Our memory report won't include subprocesses.
                    Debug.WriteLine(
                        $"Failed to get descendant processes for {parent.Id} at all. Memory used report won't be accurate"
                    );
                    return new List<Process>();
                }
            }

            private static List<Process> GetAllDescendantProcessesWindows(int parentPid)
            {
                // Build a parent->children map in one pass over a toolhelp snapshot, then walk descendants.
                var childMap = GetChildProcessMapWindows();
                var descendantPids = new HashSet<int>();
                var queue = new Queue<int>();
                queue.Enqueue(parentPid);

                while (queue.Count > 0)
                {
                    var pid = queue.Dequeue();
                    if (!childMap.TryGetValue(pid, out var children))
                        continue;

                    foreach (var childPid in children)
                    {
                        if (descendantPids.Add(childPid))
                            queue.Enqueue(childPid);
                    }
                }

                var result = new List<Process>();
                foreach (var pid in descendantPids)
                {
                    try
                    {
                        result.Add(Process.GetProcessById(pid));
                    }
                    catch
                    {
                        // It is possible that the process has exited since we took the snapshot.
                    }
                }
                return result;
            }

            private static Dictionary<int, List<int>> GetChildProcessMapWindows()
            {
                var map = new Dictionary<int, List<int>>();
                var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
                if (snapshot == IntPtr.Zero || snapshot == INVALID_HANDLE_VALUE)
                    return map;

                try
                {
                    var entry = new PROCESSENTRY32();
                    entry.dwSize = (uint)Marshal.SizeOf(entry);

                    if (!Process32First(snapshot, ref entry))
                        return map;

                    do
                    {
                        var pid = unchecked((int)entry.th32ProcessID);
                        var parentPid = unchecked((int)entry.th32ParentProcessID);
                        if (!map.TryGetValue(parentPid, out var children))
                        {
                            children = new List<int>();
                            map[parentPid] = children;
                        }
                        children.Add(pid);
                    } while (Process32Next(snapshot, ref entry));
                }
                finally
                {
                    CloseHandle(snapshot);
                }

                return map;
            }

            /// <summary>
            /// Code generated by ChatGPT 5.2, probably never tried; the version above should work on Windows,
            /// which is the only platform we currently support.
            /// </summary>
            private static List<Process> GetAllDescendantProcessesWmi(int parentPid)
            {
                // Slower fallback: still avoid querying "per generation" by doing a single WMI query for all processes,
                // then building a parent->children map and walking it.
                var map = new Dictionary<int, List<int>>();
                var listMOs = new List<ManagementObject>();
                try
                {
                    using (
                        var searcher = new ManagementObjectSearcher(
                            "Select ProcessId, ParentProcessId From Win32_Process"
                        )
                    )
                        listMOs.AddRange(searcher.Get().Cast<ManagementObject>());

                    foreach (var mo in listMOs)
                    {
                        int pid;
                        int ppid;
                        try
                        {
                            pid = Convert.ToInt32(mo["ProcessId"]);
                            ppid = Convert.ToInt32(mo["ParentProcessId"]);
                        }
                        catch
                        {
                            continue;
                        }

                        if (!map.TryGetValue(ppid, out var children))
                        {
                            children = new List<int>();
                            map[ppid] = children;
                        }
                        children.Add(pid);
                    }
                }
                finally
                {
                    foreach (var mo in listMOs)
                        mo.Dispose();
                }

                var descendantPids = new HashSet<int>();
                var queue = new Queue<int>();
                queue.Enqueue(parentPid);
                while (queue.Count > 0)
                {
                    var pid = queue.Dequeue();
                    if (!map.TryGetValue(pid, out var children))
                        continue;
                    foreach (var childPid in children)
                    {
                        if (descendantPids.Add(childPid))
                            queue.Enqueue(childPid);
                    }
                }

                var result = new List<Process>();
                foreach (var pid in descendantPids)
                {
                    try
                    {
                        result.Add(Process.GetProcessById(pid));
                    }
                    catch
                    {
                        // process exited
                    }
                }
                return result;
            }

            private const uint TH32CS_SNAPPROCESS = 0x00000002;
            private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct PROCESSENTRY32
            {
                public uint dwSize;
                public uint cntUsage;
                public uint th32ProcessID;
                public IntPtr th32DefaultHeapID;
                public uint th32ModuleID;
                public uint cntThreads;
                public uint th32ParentProcessID;
                public int pcPriClassBase;
                public uint dwFlags;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szExeFile;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr hObject);

            internal static void CleanupSubprocessList()
            {
                foreach (var subProc in _subProcesses)
                    subProc.Dispose();
                _subProcesses.Clear();
            }
        }
    }
}
