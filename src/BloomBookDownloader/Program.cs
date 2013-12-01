using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.WebLibraryIntegration;

namespace BloomBookDownloader
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
                Console.WriteLine("Usage: BloomBookDownloader keyOnAmazonS3");
                return 1;
            }

            var t = new Bloom.WebLibraryIntegration.BloomS3Client(BloomS3Client.SandboxBucketName);

            var destinationPath = Path.Combine(Path.GetTempPath(), "BloomBookDownloader");
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            t.DownloadBook(arguments[0], destinationPath);

            return 0;
        }
    }
}
