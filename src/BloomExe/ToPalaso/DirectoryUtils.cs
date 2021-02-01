using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIL.IO;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// Additional directory utilities, perhaps not quite ready to promote to LibPalaso
	/// </summary>
	public class DirectoryUtils
	{
		/// <summary>
		/// Tests whether the two directories have the same content. Currently unit tested pretty well by MakeActivityTests.
		/// </summary>
		public static bool SameContent(string dir1, string dir2)
		{
			var files1 = Directory.EnumerateFiles(dir1).ToList();
			var files2 = Directory.EnumerateFiles(dir2).ToList();
			if (files1.Count != files2.Count)
				return false;
			foreach (var path in files1)
			{
				var path2 = Path.Combine(dir2, Path.GetFileName(path));
				if (!File.Exists(path2))
					return false;
				var content1 = File.ReadAllBytes(path);
				var content2 = File.ReadAllBytes(path2);
				if (content1.Length != content2.Length)
					return false;

				for (int i = 0; i < content1.Length; i++)
				{
					if (content1[i] != content2[i])
						return false;
				}
			}
			// Compare subdirectories.
			var dirs1 = Directory.EnumerateDirectories(dir1).ToList();
			var dirs2 = Directory.EnumerateDirectories(dir2).ToList();
			if (dirs1.Count != dirs2.Count)
				return false;
			foreach (var subdir in dirs1)
			{
				var subdir2 = Path.Combine(dir2, Path.GetFileName(subdir));
				if (!Directory.Exists(subdir))
					return false;
				if (!SameContent(subdir, subdir2))
					return false;
			}

			return true;
		}

		/// <summary>
		/// Copy a folder's content to another folder, creating the destination folder if needed.
		/// </summary>
		/// <remarks>
		/// It's hard to believe this isn't already standard somewhere.
		/// </remarks>
		public static void CopyFolder(string sourcePath, string destinationPath)
		{
			Directory.CreateDirectory(destinationPath);
			foreach (var filePath in Directory.GetFiles(sourcePath))
			{
				RobustFile.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
			}
		}

	}
}
