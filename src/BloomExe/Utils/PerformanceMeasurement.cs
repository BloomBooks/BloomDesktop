using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Bloom.Api;
using BloomTemp;
using SIL.IO;

namespace Bloom.Utils
{
	delegate void EndOfLifeCallback(Measurement step);

	/* usage:using (PerformanceMeasurement.Global.Measure("select page")) { ..do something }
	*/
	public class PerformanceMeasurement :IDisposable
	{
		private readonly BloomWebSocketServer _webSocketServer;
		public static PerformanceMeasurement Global;

		private string _file;
		private StreamWriter _stream;
		private const string kWebsocketContext = "performance";
		public bool CurrentlyMeasuring { get; private set; }

		// The only instance of this is created by autofac
		public PerformanceMeasurement(BloomWebSocketServer webSocketServer)
		{
			_webSocketServer = webSocketServer;
			Global = this; // note, this is changed if we change collections and the ProjectContext makes a new one
		}
		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("performance/start", HandleStartMeasuring, false);
		}


		// If nothing calls this, then the rest just doesn't do anything.
		// If it is called a second time, it will start a new file.
		public void HandleStartMeasuring(ApiRequest request)
		{
			this.CurrentlyMeasuring = true;

			var columnNames = "Action,Details,Seconds,Private Bytes,Wk Set,Wk Set Private,Paged";
			_webSocketServer.SendString(kWebsocketContext, "columns", columnNames);


			
						if (_stream != null)
						{
							_stream.Close();
							_stream.Dispose();
							// no, leave it and its contents around: _folder.Dispose();
						}



						_file = TempFileUtils.GetTempFilepathWithExtension(".csv");
						_stream = RobustFile.CreateText(_file);
						_stream.AutoFlush = true;

						try
						{
							_stream.WriteLine(Form.ActiveForm.Text);
						}
						catch (Exception)
						{
							// swallow. This happens when we call from firefox, while debugging.
						}

						_stream.WriteLine(columnNames);
						System.Diagnostics.Process.Start(_file); // open in some editor

						

			request.PostSucceeded();
		}

		/// <summary>
		/// Called (by the winforms dialog closing event) when the user closes the dialog. If it is never
		/// called, that's fine.
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
		/// This is the main public method, called anywhere in the c# code that we want to measure something
		/// </summary>
		/// <returns>an object that should be disposed of to end the measurement</returns>
		public IDisposable Measure(string action, string details ="")
		{
			if (!CurrentlyMeasuring)
				return null;
			
			return new Lifespan(new Measurement(action, details), StepEnded);
		}

		// This is only called if there is a Lifespan generated (and it gets disposed) and that will only happen
		// if Measure() decided that we are in measuring mode.
		private void StepEnded(Measurement step)
		{
			_stream.WriteLine(step.GetCsv());
			_webSocketServer.SendString(kWebsocketContext, "event", step.GetCsv());
			//Debug.WriteLine(step.GetCsv());
		}

		public void Dispose()
		{
			_stream?.Close();
			_stream = null;
			CurrentlyMeasuring = false;
		}
	}

	/// <summary>
	/// Just something to stop a particular measurement at then end of a using() block.
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
			_measurement.Finish();
			_callback(_measurement);
		}
	}

	public class Measurement
	{
		private readonly string _action;
		private readonly string _details;
		private readonly PerfPoint _start;
		private PerfPoint _end;

		public Measurement(string action, string details)
		{
			_action = action;
			_details = details;
			_start = new PerfPoint();
		}

		public void Finish()
		{
			this._end = new PerfPoint();
		}
	

		public string GetCsv()
		{
			TimeSpan diff = _end.when - _start.when;
			var time = diff.ToString(@"ss\.f");
			return $"{_action},{_details},{time},{(_end.privateBytesMb - _start.privateBytesMb)},{(_end.workingSetMb - _start.workingSetMb)},{(_end.workingSetPrivateMb - _start.workingSetPrivateMb)},{(_end.pagedMemoryMb - _start.pagedMemoryMb)}";
		}

		public class PerfPoint
	{
		const int bytesPerMegabyte = 1048576;
		public long pagedMemoryMb;
		public long workingSetMb;
		public DateTime when;
		public long workingSetPrivateMb;
		public long privateBytesMb;

		public PerfPoint()
		{
			this.when = DateTime.Now;
			using (var proc = Process.GetCurrentProcess())
			{
				pagedMemoryMb = proc.PagedMemorySize64 / bytesPerMegabyte;
			}

			this.workingSetMb = GetWorkingSet();
			this.workingSetPrivateMb = GetWorkingSetPrivate();
			privateBytesMb = GetPrivateBytes();
		}

		// Significance: This counter indicates the current number of bytes allocated to this process that cannot be shared with
		// other processes.This counter is used for identifying memory leaks.
		private long GetPrivateBytes()
		{
			using (var perfCounter = new PerformanceCounter("Process", "Private Bytes",
				Process.GetCurrentProcess().ProcessName))
			{
				return perfCounter.RawValue / 1024;
			}
		}

		/* Significance: The working set is the set of memory pages currently loaded in RAM. If the system has sufficient memory, it can maintain enough space in the working set so that it does not need to perform the disk operations. However, if there is insufficient memory, the system tries to reduce the working set by taking away the memory from the processes which results in an increase in page faults. When the rate of page faults rises, the system tries to increase the working set of the process. If you observe wide fluctuations in the working set, it might indicate a memory shortage. Higher values in the working set may also be due to multiple assemblies in your application. You can improve the working set by using assemblies shared in the global assembly cache.
		 */
		private long GetWorkingSet()
		{
			using (var perfCounter = new PerformanceCounter("Process", "Working Set",
				Process.GetCurrentProcess().ProcessName))
			{
				return perfCounter.RawValue / 1024;
			}
		}
		private long GetWorkingSetPrivate()
		{
			using (var perfCounter = new PerformanceCounter("Process", "Working Set - Private",
				Process.GetCurrentProcess().ProcessName))
			{
				return perfCounter.RawValue / 1024;
			}
		}


		//Gave 0 except for the very start of page switching
		// private long GetWorkingSetPeak()
		//{
		//	using (var perfCounter = new PerformanceCounter("Process", "Working Set Peak",
		//		Process.GetCurrentProcess().ProcessName))
		//	{
		//		return perfCounter.RawValue / 1024;
		//	}
		}
	}
}
