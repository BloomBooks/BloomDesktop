using System;
using System.Collections.Generic;
using System.IO;
#if !__MonoCS__
using System.Security.AccessControl;
#endif

namespace Bloom.ToPalaso
{
	public static class BloomFileAccess
	{
		/// <summary>
		/// Ensure that a file inside a book (or collection) folder continues to have at least the access that
		/// its parent folder has.  Inherited access rights are being lost in some circumstances.
		/// </summary>
		/// <param name="path">full path to a file</param>
		/// <remarks>See http://issues.bloomlibrary.org/youtrack/issue/BL-3954 for details.</remarks>
		public static void EnsureInheritedAccessRights(string path)
		{
#if __MonoCS__
			// It's possible that this isn't a problem on Linux, so we'll wait until somebody complains to implement anything.
			// And I"m not sure how well Linux would support providing a Bloom fileshare for Windows (or other Linux) machines.
			// I'm sure Samba is up to it, but I haven't played with it for some time.
#else
			try
			{
				// Build a dictionary of access rules for the parent directory
				var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(path));
				var directorySecurity = directoryInfo.GetAccessControl();
				var directoryAcl = directorySecurity.GetAccessRules(true, true, typeof (System.Security.Principal.NTAccount));
				var rulesToMatch = new Dictionary<string, FileSystemAccessRule>();
				foreach (FileSystemAccessRule access in directoryAcl)
				{
					var key = GenerateKey(access);
					if (!rulesToMatch.ContainsKey(key))	// access list entries may differ only by inherited or not
						rulesToMatch.Add(key, access);
				}
				// Match the parent directory's access rules against the file's access rules, removing any in common.
				var fileInfo = new FileInfo(path);
				var fileSecurity = fileInfo.GetAccessControl();
				var fileAcl = fileSecurity.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
				foreach (FileSystemAccessRule access in fileAcl)
				{
					var key = GenerateKey(access);
					if (rulesToMatch.ContainsKey(key))
						rulesToMatch.Remove(key);
				}
				// Add any access rules from the parent directory that the file itself lacks.
				if (rulesToMatch.Count > 0)
				{
					foreach (var access in rulesToMatch.Values)
					{
						// We can't set any inheritance flags on this new rules.  Otherwise an exception occurs two lines below.
						var rule = new FileSystemAccessRule(access.IdentityReference, access.FileSystemRights, access.AccessControlType);
						fileSecurity.AddAccessRule(rule);
					}
					fileInfo.SetAccessControl(fileSecurity);
					Console.WriteLine("Adjusted access to {0}", path);
				}
				//else
				//{
				//	Console.WriteLine("DEBUG: access control to {0} is already okay.", path);
				//}
			}
			catch (Exception e)
			{
				Console.WriteLine("Exception thrown trying to adjust access to {0}: {1}", path, e.Message);
			}
#endif
		}

#if !__MonoCS__
		private static string GenerateKey(FileSystemAccessRule access)
		{
			return String.Format("{0} - {1} - {2}", access.IdentityReference.Value, access.AccessControlType, access.FileSystemRights);
		}
#endif
	}
}
