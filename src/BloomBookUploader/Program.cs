using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BloomBookUploader
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static int Main(string[] arguments)
		{
			if (arguments.Length != 1)
			{
				Console.WriteLine("Usage: BloomBookUploader path-to-folder-containing-books");
				return 1;
			}
			if (!Directory.Exists(arguments[0]))
			{
				Console.WriteLine(arguments[0]+" not found");
				return 3;
			}

			var t = new Bloom.WebLibraryIntegration.S3Transfer();
			t.UploadBook(Guid.NewGuid().ToString(), arguments[0]);

			return 0;
		}
	}
}
