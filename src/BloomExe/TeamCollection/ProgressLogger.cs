using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.web;
using SIL.Progress;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// This class supports logging reports made through the IWebSocketProgress interface.
	/// It wraps another instance of the interface, to which it forwards messages
	/// (not implemented for all members yet).
	/// In addition to forwarding them, it appends a representation of them to a file.
	/// </summary>
	public class ProgressLogger : IWebSocketProgress, IDisposable
	{
		private StreamWriter _writer;
		private IWebSocketProgress _innerProgress;

		public ProgressLogger(string filePath, IWebSocketProgress innerProgress)
		{
			_writer = File.AppendText(filePath);
			_innerProgress = innerProgress;
		}

		public void MessageWithoutLocalizing(string message, MessageKind kind = MessageKind.Progress)
		{
			_innerProgress.MessageWithoutLocalizing(message, kind);
			Log(message, kind);
		}

		public void Message(string idSuffix, string comment, string message, MessageKind kind = MessageKind.Progress,
			bool useL10nIdPrefix = true)
		{
			_innerProgress.Message(idSuffix, comment, message, kind, useL10nIdPrefix);
			Log(message, kind); // enhance: maybe we want to log the localized version?
		}

		public void Log(string message, MessageKind kind = MessageKind.Progress)
		{
			_writer.WriteLine(kind.ToString().ToLowerInvariant() + ":");
			_writer.WriteLine("\t" + message);
		}

		public void Message(string idSuffix, string message, MessageKind kind = MessageKind.Progress, bool useL10nIdPrefix = true)
		{
			throw new NotImplementedException();
		}

		public void MessageWithParams(string idSuffix, string comment, string message, MessageKind kind, params object[] parameters)
		{
			throw new NotImplementedException();
		}

		public void Dispose()
		{
			_writer.Dispose();
		}
	}
}
