using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BloomTests
{
    /// <summary>
    /// Lets a Windows-only test make a folder that Bloom may read but not write, as a read-only
    /// folder or a share without write permission would be.
    /// </summary>
    public static class FolderAccessForTests
    {
        /// <summary>
        /// Deny the current user write access to folder and everything in it, until disposed.
        /// (Only the write rights: reading, and so opening files at all, still works.)
        /// </summary>
        public static IDisposable DenyWrite(DirectoryInfo folder)
        {
            var security = folder.GetAccessControl();
            var denyWrite = new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().Name,
                FileSystemRights.Write,
                InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                PropagationFlags.None,
                AccessControlType.Deny
            );
            security.AddAccessRule(denyWrite);
            folder.SetAccessControl(security);
            return new RestoreAccess(folder, security, denyWrite);
        }

        /// <summary>
        /// Removes the rule DenyWrite added.
        /// </summary>
        private sealed class RestoreAccess : IDisposable
        {
            private readonly DirectoryInfo _folder;
            private readonly DirectorySecurity _security;
            private readonly FileSystemAccessRule _rule;

            public RestoreAccess(
                DirectoryInfo folder,
                DirectorySecurity security,
                FileSystemAccessRule rule
            )
            {
                _folder = folder;
                _security = security;
                _rule = rule;
            }

            public void Dispose()
            {
                _security.RemoveAccessRule(_rule);
                _folder.SetAccessControl(_security);
            }
        }
    }
}
