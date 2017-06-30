using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using SIL.Progress;

namespace Bloom.web
{

	class WebSocketProgress : SIL.Progress.IProgress
	{
		private readonly BloomWebSocketServer _bloomWebSocketServer;
		public WebSocketProgress(BloomWebSocketServer bloomWebSocketServer)
		{
			_bloomWebSocketServer = bloomWebSocketServer;
		}
		bool IProgress.ShowVerbose
		{
			set { }
		}
		bool IProgress.CancelRequested
		{
			get { return false; }
			set { }
		}
		bool IProgress.ErrorEncountered
		{
			get { return false; }
			set { }
		}
		IProgressIndicator IProgress.ProgressIndicator
		{
			get { return null; }
			set { }
		}
		SynchronizationContext IProgress.SyncContext
		{
			get { return null; }
			set { }
		}

		public void WriteError(string message, params object[] args)
		{
			WriteMessage($"<span style='color:red'>{message}</span>", args);
		}

		public void WriteException(Exception error)
		{
			//Enhance?
			WriteError(error.Message);
		}

		public void WriteMessage(string message, params object[] args)
		{
			_bloomWebSocketServer.Send("progress", message);
		}

		public void WriteMessageWithColor(string colorName, string message, params object[] args)
		{
			//TODO mark with a color?
			WriteMessage(message, args);
		}

		public void WriteStatus(string message, params object[] args)
		{
			WriteMessage(message, args);
		}

		public void WriteVerbose(string message, params object[] args)
		{
			//TODO mark with a verbose class
			WriteMessage(message, args);
		}

		public void WriteWarning(string message, params object[] args)
		{
			//TODO mark with a warning class
			WriteMessage(message, args);
		}
	}
}
