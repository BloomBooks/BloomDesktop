using System;
using System.IO;
using System.Text;
using SIL.IO;

namespace Bloom.Utils
{
	/// <summary>
	/// Collection of static utility methods that don't fit comfortably anywhere else.
	/// </summary>
	public static class MiscUtils
	{
		public static string CollectFilePermissionInformation(string filePath)
		{
			var bldr = new StringBuilder();
			try
			{
				if (SIL.PlatformUtilities.Platform.IsWindows)
				{
					var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
					bldr.AppendLine($"current user is {currentUser.Name}");
					var principal = new System.Security.Principal.WindowsPrincipal(currentUser);
					bool isInRoleWithAccess = false;
					bool accessDenied = false;
					bool accessAllowed = false;
					System.Security.AccessControl.FileSystemRights accessRights = System.Security.AccessControl.FileSystemRights.Write;
					var acl = File.GetAccessControl(filePath);
					var rules = acl.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
					var sid = acl.GetOwner(typeof(System.Security.Principal.SecurityIdentifier));
					var acct = sid.Translate(typeof(System.Security.Principal.NTAccount)) as System.Security.Principal.NTAccount;
					if (acct != null)
						bldr.AppendLine($"owner of \"{filePath}\" is {acct.Value}");
					var fileAttributes = RobustFile.GetAttributes(filePath);
					bldr.AppendLine($"{filePath} current ReadOnly attribute of {filePath} is {(fileAttributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly}");
					foreach (System.Security.AccessControl.AuthorizationRule rule in rules)
					{
						var fsAccessRule = rule as System.Security.AccessControl.FileSystemAccessRule;
						if (fsAccessRule == null)
							continue;
						if ((fsAccessRule.FileSystemRights & accessRights) > 0)
						{
							var ntAccount = rule.IdentityReference as System.Security.Principal.NTAccount;
							if (ntAccount == null)
								continue;
							if (principal.IsInRole(ntAccount.Value))
							{
								if (fsAccessRule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny)
								{
									bldr.AppendLine($"current user is denied write access to {filePath} by {ntAccount.Value}{(rule.IsInherited ? " (inherited)":"")}");
									accessDenied = true;
								}
								if (fsAccessRule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow)
								{
									bldr.AppendLine($"current user is allowed write access to {filePath} by {ntAccount.Value}{(rule.IsInherited ? " (inherited)":"")}");
									accessAllowed = true;
								}
								isInRoleWithAccess = true;
							}
						}
					}
					if (isInRoleWithAccess)
					{
						if (!accessAllowed)
							bldr.AppendLine($"current user is not explicitly allowed write access to {filePath}");
						if (!accessDenied)
							bldr.AppendLine($"current user is not explicitly denied write access to {filePath}");
					}
					else
					{
						bldr.AppendLine($"current user is not explicitly given access to {filePath}");
					}
				}
				else
				{
					var folder = Path.GetDirectoryName(filePath);
					var fileInfo = new Mono.Unix.UnixFileInfo(filePath);
					var dirInfo = new Mono.Unix.UnixDirectoryInfo(folder);
					var userInfo = Mono.Unix.UnixUserInfo.GetRealUser();
					bldr.AppendLine($"current user is {userInfo.UserName}");
					bldr.AppendLine($"owner of \"{filePath}\" is {fileInfo.OwnerUser.UserName}");
					bldr.AppendLine($"permissions of \"{filePath}\" = {fileInfo.FileAccessPermissions.ToString()}");
					bldr.AppendLine($"owner of \"{folder}\" is {dirInfo.OwnerUser.UserName}");
					bldr.AppendLine($"permissions of \"{folder}\" = {dirInfo.FileAccessPermissions.ToString()}");
				}
			}
			catch (Exception e)
			{
				bldr.AppendLine($"Caught exception {e} while trying to collect information about {filePath}");
			}
			return bldr.ToString();
		}

		public static string InstalledAntivirusPrograms()
		{
			string result = "";
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				string wmipathstr = @"\\" + Environment.MachineName + @"\root\SecurityCenter2";
				try
				{
					var searcher =
						new System.Management.ManagementObjectSearcher(wmipathstr, "SELECT * FROM AntivirusProduct");
					var instances = searcher.Get();
					foreach (var instance in instances)
					{
						result += instance.GetText(System.Management.TextFormat.Mof) + Environment.NewLine;
					}
				}
				catch (Exception error)
				{
					return error.Message;
				}
			}
			return result;
		}

		/// <summary>
		/// Get what information we can about why a given file could not be written to the filesystem.
		/// This includes the identity of the current user, the identity of the file owner, the file
		/// permission information, and (grasping at straws) the system's anti-virus programs, if any.
		/// This information may be helpful for local technical support people even if we can't help
		/// users at a distance.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-9533.
		/// </remarks>
		public static string GetExtendedFileCopyErrorInformation(string path, string firstLine=null)
		{
			var bldr = new StringBuilder();
			if (!String.IsNullOrEmpty(firstLine))
				bldr.AppendLine(firstLine);
			if (RobustFile.Exists(path))
			{
				bldr.AppendLine($"You may find help for this problem at https://community.software.sil.org/t/when-bloom-is-prevented-from-changing-png-image-files/4445.");
				bldr.AppendLine($"The following specific information may also be helpful.");
				bldr.Append(CollectFilePermissionInformation(path));
			}
			else
			{
				bldr.AppendLine($"The file ({path}) does not exist!?");
				bldr.AppendLine($"The following specific information may be helpful.");
			}
			bldr.Append(InstalledAntivirusPrograms());
			return bldr.ToString();
		}
	}
}
