using System;
using System.IO;
using System.Text;
using SIL.IO;

namespace Bloom.Utils
{
	public static class MiscUtils
	{
		public static string CollectFilePermissionInformation(string imagePath)
		{
			var bldr = new StringBuilder();
			try
			{
				var fileAttributes = RobustFile.GetAttributes(imagePath);
				bldr.AppendLine($"{imagePath} current ReadOnly attribute is {(fileAttributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly}");
				if (SIL.PlatformUtilities.Platform.IsWindows)
				{
					var acl = File.GetAccessControl(imagePath);
					var rules = acl.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
					var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
					var principal = new System.Security.Principal.WindowsPrincipal(currentUser);
					bool isInRoleWithAccess = false;
					bool accessDenied = false;
					bool accessAllowed = false;
					System.Security.AccessControl.FileSystemRights accessRights = System.Security.AccessControl.FileSystemRights.Write;
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
									bldr.AppendLine($"current user is denied write access to {imagePath} by {ntAccount.Value}{(rule.IsInherited ? " (inherited)":"")}");
									accessDenied = true;
								}
								if (fsAccessRule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow)
								{
									bldr.AppendLine($"current user is allowed write access to {imagePath} by {ntAccount.Value}{(rule.IsInherited ? " (inherited)":"")}");
									accessAllowed = true;
								}
								isInRoleWithAccess = true;
							}
						}
					}
					if (isInRoleWithAccess)
					{
						if (!accessAllowed)
							bldr.AppendLine($"current user is not explicitly allowed write access to {imagePath}");
						if (!accessDenied)
							bldr.AppendLine($"current user is not explicitly denied write access to {imagePath}");
					}
					else
					{
						bldr.AppendLine($"user is not explicitly given access to {imagePath}");
					}
				}
			}
			catch (Exception e)
			{
				bldr.AppendLine($"Caught exception {e} while trying to collect information about {imagePath}");
			}
			return bldr.ToString();
		}

		public static string InstalledAntivirusPrograms()
		{
			string result = "";
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
//#if !__MonoCS__
				string wmipathstr = @"\\" + Environment.MachineName + @"\root\SecurityCenter2";
				try
				{
					var searcher =
						new System.Management.ManagementObjectSearcher(wmipathstr, "SELECT * FROM AntivirusProduct");
					var instances = searcher.Get();
					foreach (var instance in instances)
					{
						result += instance.GetText(System.Management.TextFormat.Mof).ToString() + Environment.NewLine;
					}
				}
				catch (Exception error)
				{
					return error.Message;
				}
//#endif
			}
			return result;
		}
	}
}
