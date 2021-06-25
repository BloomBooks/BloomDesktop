// COPIED FROM libpalaso to get some enahancements (color, at the moment).

// Copyright (c) 2010-2019 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Drawing;
using System.IO;
using System.Threading;
using SIL.Extensions;
using SIL.Progress;

namespace Bloom.Utils
{
	public class FileProgress : ConsoleProgress
	{
		private StreamWriter Output;

		public FileProgress(string path)
		{
			this.Output = System.IO.File.CreateText(path);
		}

		public override void WriteStatus(string message, params object[] args)
		{
			Output.Write("                          ".Substring(0, indent * 2));
			Output.WriteLine(message.FormatWithErrorStringInsteadOfException(args));
			Output.Flush();
		}
		public void Dispose()
		{
			Output.Close();
		}
	}
}
