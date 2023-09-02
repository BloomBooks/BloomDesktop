using System;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using SIL.Reporting;

namespace Bloom.Utils
{
	internal class PathTooLongException : System.IO.PathTooLongException
	{
		public string Path;
		public string AdditionalInfo;
		public PathTooLongException(string path, string additionaInfo = "")
		{
			Path = path;
			AdditionalInfo = additionaInfo;
		}
		public override string Message => $"{base.Message} Path was '{Path}. Additional Info:{AdditionalInfo}";
	}

	internal static class LongPathAware
	{
		const int kmaxPath = 255; // not 256 becuase that includes the C++ null terminator

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		static extern uint GetLongPathName(
			[MarshalAs(UnmanagedType.LPTStr)] string lpszShortPath,
			[MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszLongPath,
			[MarshalAs(UnmanagedType.U4)] int cchBuffer);


		/// <summary>
		/// Converts a short path (8.3) to a long path. We can be given these kinds of paths by
		/// Windows File Explorer when the user double-clicks on a .bloomCollection that has
		/// a really long path.
		/// </summary>
		/// <param name="path">A path that may or may not be an 8.3-style path.</param>
		/// <returns>The long path.  Null or empty if the input is null or empty.</returns>
		internal static string GetLongPath(string path)
		{
			if (String.IsNullOrEmpty(path))
			{
				return path;
			}
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				return path;
			}
			if (!GetIsPossiblyShortenedPath(path))
			{
				return path;
			}

			StringBuilder builder = new StringBuilder(2000);  // randomly chosen max
			var result = Convert.ToInt32(GetLongPathName(path, builder, builder.Capacity));
			if (result == 0)
			{
				throw new FileNotFoundException("The file was not found", path);
			}
			if (result >= builder.Capacity)
			{
				throw new ApplicationException("GetLongPath() exceeded capacity.");
			}

			return builder.ToString(0, result);
		}

		/// <summary>
		/// This doesn't do any fancy regex, so it could give false positives. Which is fine, becuase it's just a time saver.
		/// </summary>
		/// <param name="path"></param>
		/// <returns>True if it is worth asking the OS to expand the path in case it is an 8.3-style one.</returns>
		internal static bool GetIsPossiblyShortenedPath(string path)
		{
			if (path == null) return false;
			if (SIL.PlatformUtilities.Platform.IsLinux) return false;
			return path.Contains("~");
		}

		internal static bool GetExceedsMaxPath(string path)
		{
			if (path == null) return false;
			if (GetIsPossiblyShortenedPath(path))
			{
				return GetLongPath(path).Length > kmaxPath;
			} else return path.Length > kmaxPath;
		}

		internal static void ThrowIfExceedsMaxPath(string path)
		{
			if (GetExceedsMaxPath(path))
			{
				throw new PathTooLongException(path);
			}
		}
		internal static bool FileExistsThrowIfTooLong(string path)
		{
			ThrowIfExceedsMaxPath(path);
			return Bloom.Utils.PatientFile.Exists(path);
		}
		private static string GetGenericPathTooLongMessage()
		{
			return L10NSharp.LocalizationManager.GetString("Error.PathTooLong", "Please move your collection closer to the root of your hard drive and try again. A file Bloom was working with had a path that was too long. This is usually caused by your collection being too deeply nested in many folders on your hard drive.");
		}

		/// <summary>
		/// The idea here is to show a helpful notice, with the option to click "REPORT" if they need help.
		/// </summary>
		/// <param name="e">either a raw PathTooLongException or our own enhanced subclass</param>
		internal static void ReportLongPath(System.IO.PathTooLongException e)
		{
			// If we have our own subclass, it will know the path of the offending file and maybe more helpful info.
			if (e is Bloom.Utils.PathTooLongException)
			{
				var x = (Bloom.Utils.PathTooLongException)e;
				ErrorReport.NotifyUserOfProblem(x, $"{GetGenericPathTooLongMessage()} <br> <span style='font-size:7pt'>Path was '{x.Path}'. {x.AdditionalInfo}</span>");
			}
			else
			{
				ErrorReport.NotifyUserOfProblem(e, $"{GetGenericPathTooLongMessage()}");
			}			
		}

		internal static void ReportLongPath(string path)
		{
			ErrorReport.NotifyUserOfProblem($"{GetGenericPathTooLongMessage()} <br> <span style='font-size:7pt'>Path was '{path}'.</span>");
		}


		/* This function is NOT IN USE because this was for BloomServer but I ran screaming at the complexity in there and 
		 * so it will have to wait for another day.
		 * internal static IEnumerable<string> EnumerateDirectoryFilesThrowIfTooLong(string dir, string fileName, SearchOption searchOption)
			{
				// It would take some extra effort to catch a path that became too long only in a sub directoryy
				// and we're not doing that yet. But we can at least test the top directory...
				Utils.LongPathAware.ThrowIfExceedsMaxPath(Path.Combine(dir, fileName));
				try
				{
					return Directory.EnumerateFiles(dir, fileName, searchOption //  here filename is the searchPattern 
	}
			catch (Exception ex)
			{
				// ...and then if it still fails, we can guess that the problem was length
				if (dir.Length + fileName.Length > 220)
				{
					throw new Utils.PathTooLongException(fileName, "It is not certain that this was too long. We were doing Directory.EnumerateFiles in this directory and its sub directories: " + dir);
				}
				throw ex;
			}
		}*/
	}

}
