using System;
using System.IO;
using System.Threading;

namespace Bloom.ToPalaso
{
	class DirectoryUtilities
	{
		/// <summary>
		/// There are various things which can prevent a simple directory deletion, mostly timing related things which are hard to debug.
		/// This method uses all the tricks to do its best.
		///
		/// *****************************************************************************************************************************
		/// NOTE: This method actual resides in libpalaso. However, there was a fix made to it which we needed in 3.7, and in an
		/// attempt to prevent destabilizing 3.7, we decided not to include the 20-something changes made to libpalaso after our
		/// 3.7 tag on the Teamcity build of libpalaso. So, we simply copied the method here which includes the fix.
		/// Since this change is only for 3.7, we shouldn't ever need to remove it as 3.8+ use the libpalaso version.
		/// http://issues.bloomlibrary.org/youtrack/issue/BL-4141
		/// *****************************************************************************************************************************
		///
		/// </summary>
		/// <returns>returns true if the directory is fully deleted</returns>
		public static bool DeleteDirectoryRobust(string path, bool overrideReadOnly = true)
		{
			// ReSharper disable EmptyGeneralCatchClause
			for (int i = 0; i < 40; i++) // each time, we sleep a little. This will try for up to 2 seconds (40*50ms)
			{
				if (!Directory.Exists(path))
					break;

				try
				{
					Directory.Delete(path, true);
				}
				catch (Exception)
				{
				}

				if (!Directory.Exists(path))
					break;

				try
				{
					//try to clear it out a bit
					string[] dirs = Directory.GetDirectories(path);
					string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
					foreach (string filePath in files)
					{
						try
						{
							if (overrideReadOnly)
							{
								File.SetAttributes(filePath, FileAttributes.Normal);
							}
							File.Delete(filePath);
						}
						catch (Exception)
						{
						}
					}
					foreach (var dir in dirs)
					{
						DeleteDirectoryRobust(dir);
					}

				}
				catch (Exception)//yes, even these simple queries can throw exceptions, as stuff suddenly is deleted based on our prior request
				{
				}
				//sleep and let some OS things catch up
				Thread.Sleep(50);
			}

			return !Directory.Exists(path);
			// ReSharper restore EmptyGeneralCatchClause
		}
	}
}
