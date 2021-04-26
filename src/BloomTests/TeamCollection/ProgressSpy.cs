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
		public List<Tuple<string, ProgressKind>> Messages = new List<Tuple<string, ProgressKind>>();

		public List<String> Warnings =>
			Messages.Where(m => m.Item2 == ProgressKind.Warning).Select(m => m.Item1).ToList();
		public List<String> Errors =>
			Messages.Where(m => m.Item2 == ProgressKind.Error).Select(m => m.Item1).ToList();
		public List<String> ProgressMessages =>
			Messages.Where(m => m.Item2 == ProgressKind.Progress).Select(m => m.Item1).ToList();
		public void MessageWithoutLocalizing(string message, ProgressKind kind = ProgressKind.Progress)
		{
			Messages.Add(Tuple.Create(message, kind));
		}

		public void Message(string idSuffix, string comment, string message, ProgressKind progressKind = ProgressKind.Progress,
			bool useL10nIdPrefix = true)
		{
			Messages.Add(Tuple.Create(message, progressKind));
		}

		public void Message(string idSuffix, string message, ProgressKind kind = ProgressKind.Progress, bool useL10nIdPrefix = true)
		{
			Messages.Add(Tuple.Create(message,kind));
		}

		public void MessageWithParams(string idSuffix, string comment, string message, ProgressKind kind, params object[] parameters)
		{
			throw new NotImplementedException();
		}
	}
}
