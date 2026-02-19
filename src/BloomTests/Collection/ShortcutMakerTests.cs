// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System.IO;
using Bloom;
using Bloom.Collection;
using NUnit.Framework;
using SIL.IO;
using SIL.PlatformUtilities;

namespace BloomTests.Collection
{
    [TestFixture]
    public class ShortcutMakerTests
    {
        [Test]
        public void CreateDirectoryShortcut()
        {
            using (var targetPath = new SIL.TestUtilities.TemporaryFolder(Path.GetRandomFileName()))
            using (var directory = new SIL.TestUtilities.TemporaryFolder(Path.GetRandomFileName()))
            {
                ShortcutMaker.CreateDirectoryShortcut(targetPath.Path, directory.Path);

                var expectedFile =
                    Path.Combine(directory.Path, Path.GetFileName(targetPath.Path)) + ".lnk";
                Assert.That(
                    File.GetAttributes(expectedFile)
                        & (FileAttributes.Directory | FileAttributes.Normal),
                    Is.Not.Null
                );
                Assert.That(Shortcut.Resolve(expectedFile), Is.EqualTo(targetPath.Path));
            }
        }

        [Test]
        public void CreateDirectoryShortcut_FileExists()
        {
            using (var targetPath = new SIL.TestUtilities.TemporaryFolder(Path.GetRandomFileName()))
            using (var directory = new SIL.TestUtilities.TemporaryFolder(Path.GetRandomFileName()))
            {
                var existingDestination = new SIL.TestUtilities.TempFileFromFolder(
                    directory,
                    Path.GetFileName(targetPath.Path) + ".lnk",
                    string.Empty
                );

                ShortcutMaker.CreateDirectoryShortcut(targetPath.Path, directory.Path);

                var expectedFile =
                    Path.Combine(directory.Path, Path.GetFileName(targetPath.Path)) + ".lnk";
                Assert.That(
                    File.GetAttributes(expectedFile)
                        & (FileAttributes.Directory | FileAttributes.Normal),
                    Is.Not.Null
                );
                Assert.That(Shortcut.Resolve(expectedFile), Is.EqualTo(targetPath.Path));
            }
        }
    }
}
