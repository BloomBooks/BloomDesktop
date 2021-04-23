using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.web;
using SIL.Progress;

namespace BloomTests.TeamCollection
{
	public class ProgressSpy : IWebSocketProgress
	{
		public List<Tuple<string, MessageKind>> Messages = new List<Tuple<string, MessageKind>>();

		public List<String> Warnings =>
			Messages.Where(m => m.Item2 == MessageKind.Warning).Select(m => m.Item1).ToList();
		public List<String> Errors =>
			Messages.Where(m => m.Item2 == MessageKind.Error).Select(m => m.Item1).ToList();
		public List<String> ProgressMessages =>
			Messages.Where(m => m.Item2 == MessageKind.Progress).Select(m => m.Item1).ToList();
		public void MessageWithoutLocalizing(string message, MessageKind kind = MessageKind.Progress)
		{
			Messages.Add(Tuple.Create(message, kind));
		}

		public void Message(string idSuffix, string comment, string message, MessageKind progressKind = MessageKind.Progress,
			bool useL10nIdPrefix = true)
		{
			Messages.Add(Tuple.Create(message, progressKind));
		}

		public void Message(string idSuffix, string message, MessageKind kind = MessageKind.Progress, bool useL10nIdPrefix = true)
		{
			Messages.Add(Tuple.Create(message,kind));
		}

		public void MessageWithParams(string idSuffix, string comment, string message, MessageKind kind, params object[] parameters)
		{
			throw new NotImplementedException();
		}
	}
}
