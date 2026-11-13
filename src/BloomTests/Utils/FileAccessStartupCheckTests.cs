using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Bloom.Utils;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.Utils
{
    [TestFixture]
    public class FileAccessStartupCheckTests
    {
        [Test]
        public void ProbeFolder_FolderWritable_ReturnsNullAndLeavesNothingBehind()
        {
            using (var folder = new TemporaryFolder("FileAccessStartupCheckTests"))
            {
                Assert.That(
                    Directory.GetFileSystemEntries(folder.Path),
                    Is.Empty,
                    "Test setup: the folder should start empty"
                );

                var problem = FileAccessStartupCheck.ProbeFolder(folder.Path, out _);

                Assert.That(problem, Is.Null);
                Assert.That(Directory.GetFileSystemEntries(folder.Path), Is.Empty);
            }
        }

        [Test]
        public void ProbeFolder_LeftoverProbeFolder_RemovesIt()
        {
            using (var folder = new TemporaryFolder("FileAccessStartupCheckTests"))
            {
                var leftover = Path.Combine(
                    folder.Path,
                    FileAccessStartupCheck.kProbeFolderPrefix + "12345678"
                );
                Directory.CreateDirectory(leftover);
                File.WriteAllText(Path.Combine(leftover, "probe.htm"), "left over");
                var book = Path.Combine(folder.Path, "My Book");
                Directory.CreateDirectory(book);

                FileAccessStartupCheck.ProbeFolder(folder.Path, out _);

                Assert.That(Directory.Exists(leftover), Is.False);
                Assert.That(
                    Directory.Exists(book),
                    Is.True,
                    "Only probe folders should be removed"
                );
            }
        }

        [Test]
        public void ProbeFolder_FolderRefusesNewItems_ReturnsTheProblem()
        {
            using (var folder = new TemporaryFolder("FileAccessStartupCheckTests"))
            {
                var denyCreate = new FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User,
                    FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles,
                    AccessControlType.Deny
                );
                var folderInfo = new DirectoryInfo(folder.Path);
                var security = folderInfo.GetAccessControl();
                security.AddAccessRule(denyCreate);
                folderInfo.SetAccessControl(security);
                try
                {
                    var problem = FileAccessStartupCheck.ProbeFolder(
                        folder.Path,
                        out var failedPath
                    );

                    Assert.That(problem, Is.InstanceOf<UnauthorizedAccessException>());
                    StringAssert.StartsWith(
                        Path.Combine(folder.Path, FileAccessStartupCheck.kProbeFolderPrefix),
                        failedPath
                    );
                }
                finally
                {
                    security.RemoveAccessRule(denyCreate);
                    folderInfo.SetAccessControl(security);
                }
            }
        }
    }
}
