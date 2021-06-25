// COPIED FROM libpalaso to get some enahancements (color, at the moment).

// Copyright (c) 2010-2019 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Drawing;
using System.Threading;
using SIL.Extensions;
using SIL.Progress;

namespace Bloom.Utils
{
	public class ConsoleProgress : IProgress, IDisposable
	{
		public static int indent = 0;
		private bool _verbose;

		public ConsoleProgress()
		{
		}

		public ConsoleProgress(string message, params string[] args)
		{
			WriteStatus(message, args);
			indent++;
		}
		public bool ErrorEncountered { get; set; }

		public IProgressIndicator ProgressIndicator { get; set; }
		public SynchronizationContext SyncContext { get; set; }
		public virtual void WriteStatus(string message, params object[] args)
		{
			Console.Write("                          ".Substring(0, indent * 2));
			Console.WriteLine(message.FormatWithErrorStringInsteadOfException(args));
		}

		public void WriteMessage(string message, params object[] args)
		{
			WriteStatus(message, args);

		}

		public void WriteMessageWithColor(string colorName, string message, params object[] args)
		{
			Console.ForegroundColor = FromColor(colorName);
			WriteStatus(message, args);
			Console.ForegroundColor = ConsoleColor.Gray; // docs say gray is the default
		}


		public void WriteWarning(string message, params object[] args)
		{
			WriteMessageWithColor("Yellow","Warning: " + message, args);
		}

		public void WriteException(Exception error)
		{
			WriteError("Exception: ");
			WriteError(error.Message);
			WriteError(error.StackTrace);

			if (error.InnerException != null)
			{
				++indent;
				WriteError("Inner: ");
				WriteException(error.InnerException);
				--indent;
			}
			ErrorEncountered = true;
		}


		public void WriteError(string message, params object[] args)
		{
			WriteMessageWithColor("Red","Error: " + message, args);
			ErrorEncountered = true;
		}

		public void WriteVerbose(string message, params object[] args)
		{
			if (!_verbose)
				return;
			var lines = message.FormatWithErrorStringInsteadOfException(args);
			foreach (var line in lines.Split('\n'))
			{
				WriteStatus("    " + line);
			}

		}

		public bool ShowVerbose
		{
			set { _verbose = value; }
		}

		public bool CancelRequested { get; set; }

		///<summary>
		///Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
		///</summary>
		///<filterpriority>2</filterpriority>
		public void Dispose()
		{
			if (indent > 0)
				indent--;
		}


		public static System.ConsoleColor FromColor(string namedColor)
		{
			var c = Color.FromName(namedColor);
			// try to detect that this failed
			if (c == Color.Black && namedColor !="black")
			{
				c = Color.BlueViolet; // default to this
			}
			var index = (c.R > 128 | c.G > 128 | c.B > 128) ? 8 : 0; // Bright bit
			index |= (c.R > 64) ? 4 : 0; // Red bit
			index |= (c.G > 64) ? 2 : 0; // Green bit
			index |= (c.B > 64) ? 1 : 0; // Blue bit
			return (System.ConsoleColor)index;
		}

	}
}
