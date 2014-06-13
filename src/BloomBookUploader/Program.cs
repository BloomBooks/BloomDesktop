using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.WebLibraryIntegration;
using Palaso.Progress;

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

			var t = new Bloom.WebLibraryIntegration.BloomS3Client(BloomS3Client.SandboxBucketName);
			t.UploadBook(Guid.NewGuid().ToString(), arguments[0], new NullProgress());

			return 0;
		}
	}
}
