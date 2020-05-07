using System;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using SIL.IO;

namespace Bloom.Utils
{
	/// <summary>
	/// A place to put zip-related common code
	/// </summary>
	public class ZipUtils
	{
		public static void ExpandZip(string srcPath, string destFolderPath)
		{
			var zipFile = new ZipFile(srcPath);
			try
			{
				byte[] buffer = new byte[4096]; // 4K is optimum
				foreach (ZipEntry entry in zipFile)
				{
					var fullOutputPath = Path.Combine(destFolderPath, entry.Name);
					if (entry.IsDirectory)
					{
						Directory.CreateDirectory(fullOutputPath);
						// In the SharpZipLib code, IsFile and IsDirectory are not defined exactly as inverse: a third
						// (or fourth) type of entry might be possible.  I don't think this will be an issue in widget files.
						continue;
					}

					var directoryName = Path.GetDirectoryName(fullOutputPath);
					if (!String.IsNullOrEmpty(directoryName))
						Directory.CreateDirectory(directoryName);
					using (var instream = zipFile.GetInputStream(entry))
					using (var writer = RobustFile.Create(fullOutputPath))
					{
						ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
					}
				}
			}
			finally
			{
				zipFile.Close();
			}
		}
	}
}
