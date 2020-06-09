using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SIL.Progress;

namespace Bloom.web
{
	/// <summary>
	/// Class that allows code expecting an SIL.Progress.IProgress object to use a WebSocketProgress instead.
	/// </summary>
	public class WebProgressAdapter : IProgress
	{
		private class NullProgressIndicator : IProgressIndicator
		{
			int _percent;
			public int PercentCompleted { get { return _percent; } set { _percent = value; } }
			public SynchronizationContext SyncContext { get { return null ; } set { return; } }

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

		private readonly WebSocketProgress _webProgress;

		public WebProgressAdapter(WebSocketProgress progress)
		{
			_webProgress = progress;
		}

		private bool _showVerbose;
		public bool ShowVerbose { set { _showVerbose = value; } }

		public bool CancelRequested { get { return false; } set { return; } }
		public bool ErrorEncountered { get { return false; } set { return; } }

		private IProgressIndicator _indicator = new NullProgressIndicator();
		public IProgressIndicator ProgressIndicator { get { return _indicator; } set { _indicator = value; } }

		public SynchronizationContext SyncContext { get { return null; } set { return; } }

		private List<string> _filters = new List<string>();
		public void AddFilter(string filter)
		{
			_filters.Add(filter);
		}

		private bool ShowMessage(string message)
		{
			return _filters.Count == 0 || _filters.Any(x => message.StartsWith(x));
		}

		public void WriteError(string message, params object[] args)
		{
			var msg = string.Format(message, args);
			if (ShowMessage(msg))
				_webProgress?.MessageWithoutLocalizing(msg, MessageKind.Error);
		}

		public void WriteException(Exception error)
		{
			_webProgress?.MessageWithoutLocalizing(error.Message, MessageKind.Error);
		}

		public void WriteMessage(string message, params object[] args)
		{
			var msg = string.Format(message, args);
			if (ShowMessage(msg))
				_webProgress?.MessageWithoutLocalizing(msg, MessageKind.Note);
		}

		public void WriteMessageWithColor(string colorName, string message, params object[] args)
		{
			var msg = string.Format(message, args);
			if (ShowMessage(msg))
				_webProgress?.MessageWithoutLocalizing(msg, MessageKind.Note);
		}

		public void WriteStatus(string message, params object[] args)
		{
			var msg = string.Format(message, args);
			if (ShowMessage(msg))
				_webProgress?.MessageWithoutLocalizing(msg, MessageKind.Progress);
		}

		public void WriteVerbose(string message, params object[] args)
		{
			if (!_showVerbose)
				return;
			var msg = string.Format(message, args);
			if (ShowMessage(msg))
				_webProgress?.MessageWithoutLocalizing(msg, MessageKind.Instruction);
		}

		public void WriteWarning(string message, params object[] args)
		{
			var msg = string.Format(message, args);
			if (ShowMessage(msg))
				_webProgress?.MessageWithoutLocalizing(msg, MessageKind.Warning);
		}
	}
}
