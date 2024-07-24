using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
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
            "SelectedTabChangedEvent"
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
                    duration = 0
                };
            }
            return new
            {
                action = _actionLabel,
                details = _actionDetails,
                privateBytes = _end.privateBytesKb,
                duration = Duration
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
                        var subsubProcs = GetSubProcesses(new List<Process> { proc });
                        while (subsubProcs.Any())
                        {
                            _subProcesses.AddRange(subsubProcs);
                            subsubProcs = GetSubProcesses(subsubProcs);
                        }
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

            private static List<Process> GetSubProcesses(List<Process> processes)
            {
                var subProcesses = new List<Process>();
                foreach (var proc in processes)
                {
                    var listMOs = new List<ManagementObject>();
                    try
                    {
                        listMOs.AddRange(
                            new ManagementObjectSearcher(
                                $"Select * From Win32_Process Where ParentProcessID={proc.Id}"
                            )
                                .Get()
                                .Cast<ManagementObject>()
                        );
                        var subProcs = listMOs.Select(
                            mo => Process.GetProcessById(Convert.ToInt32(mo["ProcessID"]))
                        );
                        if (subProcs.Any())
                            subProcesses.AddRange(subProcs);
                    }
                    finally
                    {
                        foreach (var mo in listMOs)
                            mo.Dispose();
                    }
                }
                return subProcesses;
            }

            internal static void CleanupSubprocessList()
            {
                foreach (var subProc in _subProcesses)
                    subProc.Dispose();
                _subProcesses.Clear();
            }
        }
    }
}
