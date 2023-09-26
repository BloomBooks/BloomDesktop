﻿using System;
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
	public class PerformanceMeasurement :IDisposable
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
			apiHandler.RegisterEndpointLegacy("performance/showCsvFile", (request) =>
			{
				ProcessExtra.SafeStartInFront(_csvFilePath);
				request.PostSucceeded();
			}, false);
			apiHandler.RegisterEndpointLegacy("performance/applicationInfo", (request) =>
			{
				request.ReplyWithText($"Bloom {Shell.GetShortVersionInfo()} {ApplicationUpdateSupport.ChannelName}");
				
			}, false);
			apiHandler.RegisterEndpointLegacy("performance/allMeasurements", (request) =>
			{
				List<object> l = new List<object>();
				foreach (var measurement in _measurements)
				{
					l.Add(measurement.GetSummary());
				}
				request.ReplyWithJson(l.ToArray());
				
			}, false);
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
			using (Measure("Initial Memory Reading"))
			{
			}
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
		}

		/// <summary>
		/// This is the main public method, called anywhere in the c# code that we want to measure something.
		/// What we're measuring is the memory used and the time it took from when this is called until
		/// the return value is disposed.
		/// </summary>
		/// <returns>an object that should be disposed of to end the measurement</returns>
		public IDisposable MeasureMaybe(Boolean doMeasure, string actionLabel, string actionDetails = "")
		{
			if (doMeasure) return Measure(actionLabel, actionDetails);
			else return new Lifespan(null,null);
		}
		public IDisposable Measure(string actionLabel, string actionDetails = "")
		{
			if (!CurrentlyMeasuring)
				return null;

			// skip nested measurements
			if (_measurement !=null)
			{
				// there are too many of these to keep bugging us

				//NonFatalProblem.Report(ModalIf.None, PassiveIf.All,$"Performance measurement cannot handle nested actions ('{action}' inside of '{_measurement._action}')");

				return new Lifespan(null,null);
			}

			var previousSize = _previousMeasurement?.LastKnownSize ?? 0L;
			Measurement m = null;
			m = new Measurement(actionLabel, actionDetails, previousSize);

			_previousMeasurement = m;
			_measurement = m;
			return new Lifespan(m, MeasurementEnded);
		}

		// This is only called if there is a Lifespan generated (and it gets disposed) and that will only happen
		// if Measure() decided that we are in measuring mode.
		private void MeasurementEnded(Measurement measure)
		{
			_stream.WriteLine(measure.GetCsv());
			_webSocketServer.SendString(kWebsocketContext, "event", JsonConvert.SerializeObject(measure.GetSummary()));
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

		public long LastKnownSize => _end?.privateBytesKb ?? _start?.privateBytesKb ?? 0L;

		public Measurement(string actionLabel, string actionDetails, long previousPrivateBytesKb)
		{
			_actionLabel = actionLabel;
			_actionDetails = actionDetails;
			_previousPrivateBytesKb = previousPrivateBytesKb;
			try
			{
				_start = new PerfPoint();
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
				_end = new PerfPoint();
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

				TimeSpan diff = _end.when - _start.when;
				
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
			TimeSpan diff = _end.when - _start.when;
			var time = diff.ToString(@"ss\.ff");
			return $"{_actionLabel},{_actionDetails},{time},{_end.privateBytesKb},{(_end.privateBytesKb - _previousPrivateBytesKb)}";
		}

		public override string ToString()
		{
			if (!IsComplete)
				return $"Measurement: details=\"{_actionDetails}\"; measurement failed";

			// For a ToString() summary, the delta/previousSizeKb is not important.
			return $"Measurement: details=\"{_actionDetails}\"; start={_start.privateBytesKb}KB ({_start.when}); end={_end?.privateBytesKb}KB ({_end?.when})";
		}

		public class PerfPoint
		{
			const int bytesPerMegabyte = 1048576;
			public long pagedMemoryMb;
			public long workingSetKb;
			public DateTime when;
			public long workingSetPrivateKb;
			public long privateBytesKb;

			public PerfPoint()
			{
				this.when = DateTime.Now;
				using (var proc = Process.GetCurrentProcess())
				{
					pagedMemoryMb = proc.PagedMemorySize64 / bytesPerMegabyte;
					workingSetKb = GetWorkingSetInKB(proc);
					workingSetPrivateKb = GetWorkingSetPrivateInKB(proc);
					privateBytesKb = GetPrivateBytesInKB(proc);

					var subProcesses = new List<Process>();
					try
					{
						var subsubProcs = GetSubProcesses(new List<Process> { proc });
						while (subsubProcs.Any())
						{
							subProcesses.AddRange(subsubProcs);
							subsubProcs = GetSubProcesses(subsubProcs);
						}
						// Enhance: we could report the bytes of each sub-process, but that would be a lot of data.
						// Or: we could report the total bytes of all sub-processes, but would that be helpful?
						// Or: we could report the maximum bytes of all sub-processes, but would that be helpful?
						pagedMemoryMb += subProcesses.Sum(p => p.PagedMemorySize64 / bytesPerMegabyte);
						workingSetKb += subProcesses.Sum(p => GetWorkingSetInKB(p));
						workingSetPrivateKb += subProcesses.Sum(p => GetWorkingSetPrivateInKB(p));
						privateBytesKb += subProcesses.Sum(p => GetPrivateBytesInKB(p));
					}
					finally
					{
						foreach (var subProc in subProcesses)
							subProc.Dispose();
					}
				}
			}

			private static List<Process> GetSubProcesses(List<Process> processes)
			{
				var subProcesses = new List<Process>();
				foreach (var proc in processes)
				{
					var listMOs = new List<ManagementObject>();
					try
					{
						listMOs.AddRange(new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={proc.Id}")
							.Get()
							.Cast<ManagementObject>());
						var subProcs = listMOs.Select(mo => Process.GetProcessById(Convert.ToInt32(mo["ProcessID"])));
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

			// Significance: This counter indicates the current number of bytes allocated to this process that cannot be shared with
			// other processes.This counter is used for identifying memory leaks.
			private long GetPrivateBytesInKB(Process proc)
			{
				if (SIL.PlatformUtilities.Platform.IsLinux)
				{
					return proc.PrivateMemorySize64 / 1024;
				}
				using (var perfCounter = new PerformanceCounter("Process", "Private Bytes", proc.ProcessName))
				{
					return perfCounter.RawValue / 1024;
				}
			}

			// Significance: The working set is the set of memory pages currently loaded in RAM.
			// If the system has sufficient memory, it can maintain enough space in the working
			// set so that it does not need to perform the disk operations.
			// However, if there is insufficient memory, the system tries to reduce the working
			// set by taking away the memory from the processes which results in an increase in page faults.
			// When the rate of page faults rises, the system tries to increase the working set of the process.
			// If you observe wide fluctuations in the working set, it might indicate a memory shortage.
			// Higher values in the working set may also be due to multiple assemblies in your application.
			// You can improve the working set by using assemblies shared in the global assembly cache.
			private long GetWorkingSetInKB(Process proc)
			{
				if (SIL.PlatformUtilities.Platform.IsLinux)
				{
					return proc.WorkingSet64 / 1024;
				}
				using (var perfCounter = new PerformanceCounter("Process", "Working Set", proc.ProcessName))
				{
					return perfCounter.RawValue / 1024;
				}
			}

			private long GetWorkingSetPrivateInKB(Process proc)
			{
				if (SIL.PlatformUtilities.Platform.IsLinux)
				{
					// Can't get "private" working set on Linux.
					return proc.WorkingSet64 / 1024;
				}
				using (var perfCounter = new PerformanceCounter("Process", "Working Set - Private",
					proc.ProcessName))
				{
					return perfCounter.RawValue / 1024;
				}
			}
		}
	}
}
