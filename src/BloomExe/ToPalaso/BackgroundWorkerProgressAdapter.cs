using SIL.Progress;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// Allow code called by a BackgroundWorker object to report progress via an SIL.Progress.IProgess interface.
	/// </summary>
	public class BackgroundWorkerProgressAdapter : IProgress
	{
		private readonly BackgroundWorker _worker;
		public BackgroundWorkerProgressAdapter(BackgroundWorker worker)
		{
			_worker = worker;
			ProgressIndicator = new WorkerProgressIndicator(worker);
		}

		private class WorkerProgressIndicator : IProgressIndicator
		{
			private readonly BackgroundWorker _worker;
			public WorkerProgressIndicator(BackgroundWorker worker)
			{
				_worker = worker;
			}

			int _percent;
			public int PercentCompleted
			{
				get
				{
					return _percent;
				}
				set
				{
					_percent = value;
					_worker.ReportProgress(value);
				}
			}

			public SynchronizationContext SyncContext { get; set; }

			public void Finish()
			{
			}

			public void IndicateUnknownProgress()
			{
			}

			public void Initialize()
			{
			}
		}

		private bool _showVerbose;
		public bool ShowVerbose { set { _showVerbose = value; } }
		public bool CancelRequested { get; set; }
		public bool ErrorEncountered { get; set; }
		public IProgressIndicator ProgressIndicator { get; set; }
		public SynchronizationContext SyncContext { get; set; }

		private List<string> _filters = new List<string>();
		public void AddFilter(string filter)
		{
			_filters.Add(filter);
		}

		private void SendProgressReport(string message, params object[] args)
		{
			string msg = message;
			if (args != null && args.Length > 0)
				msg = string.Format(message, args);
			if (_filters.Count == 0 || _filters.Any(x => message.StartsWith(x)))
			{
				_worker.ReportProgress(ProgressIndicator.PercentCompleted, msg);
			}
		}

		public void WriteError(string message, params object[] args)
		{
			SendProgressReport(message, args);
		}

		public void WriteException(Exception error)
		{
			SendProgressReport(error.Message, null);
		}

		public void WriteMessage(string message, params object[] args)
		{
			SendProgressReport(message, args);
		}

		public void WriteMessageWithColor(string colorName, string message, params object[] args)
		{
			SendProgressReport(message, args);
		}

		public void WriteStatus(string message, params object[] args)
		{
			SendProgressReport(message, args);
		}

		public void WriteVerbose(string message, params object[] args)
		{
			if (_showVerbose)
				SendProgressReport(message, args);
		}

		public void WriteWarning(string message, params object[] args)
		{
			SendProgressReport(message, args);
		}
	}
}
