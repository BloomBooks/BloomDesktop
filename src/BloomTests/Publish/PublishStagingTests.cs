using System;
using System.IO;
using System.Linq;
using Bloom.Publish;
using NUnit.Framework;

namespace BloomTests.Publish
{
    [TestFixture]
    public class PublishStagingTests
    {
        /// <summary>
        /// Two BloomPUBs made one after the other get different folders, each directly under the
        /// staging root, with a five-character name and nothing in it but Bloom's marker file.
        /// </summary>
        [Test]
        public void CreateFolder_MakesDistinctFoldersHoldingOnlyTheMarker()
        {
            using (var first = PublishStaging.CreateFolder())
            using (var second = PublishStaging.CreateFolder())
            {
                Assert.That(first.FolderPath, Is.Not.EqualTo(second.FolderPath));
                foreach (var folder in new[] { first.FolderPath, second.FolderPath })
                {
                    Assert.That(Directory.Exists(folder), Is.True, folder);
                    Assert.That(
                        Directory.GetFileSystemEntries(folder).Select(Path.GetFileName),
                        Is.EqualTo(new[] { ".bloom-staging" }),
                        folder
                    );
                    Assert.That(
                        Path.GetDirectoryName(folder),
                        Is.EqualTo(PublishStaging.RootPath.TrimEnd(Path.DirectorySeparatorChar))
                    );
                    Assert.That(Path.GetFileName(folder).Length, Is.EqualTo(5), folder);
                }
            }
        }

        /// <summary>
        /// Disposing a staging folder deletes it, along with whatever the BloomPUB put in it.
        /// </summary>
        [Test]
        public void CreateFolder_DisposeDeletesTheFolder()
        {
            string path;
            using (var folder = PublishStaging.CreateFolder())
            {
                path = folder.FolderPath;
                File.WriteAllText(Path.Combine(path, "index.htm"), "<html></html>");
            }
            Assert.That(Directory.Exists(path), Is.False);
        }

        /// <summary>
        /// Paths inside the staging root are recognized whatever their slashes and case; a
        /// sibling folder whose name merely starts with the root's name is not.
        /// </summary>
        [Test]
        public void IsInStagingFolder_RecognizesPathsInsideTheRootOnly()
        {
            var root = PublishStaging.RootPath.TrimEnd(Path.DirectorySeparatorChar);
            var inside = Path.Combine(root, "abcde", "My Book", "index.htm");
            Assert.That(PublishStaging.IsInStagingFolder(inside), Is.True);
            Assert.That(PublishStaging.IsInStagingFolder(inside.Replace('\\', '/')), Is.True);
            Assert.That(PublishStaging.IsInStagingFolder(inside.ToUpperInvariant()), Is.True);
            Assert.That(
                PublishStaging.IsInStagingFolder(root + "x" + Path.DirectorySeparatorChar + "a"),
                Is.False
            );
            Assert.That(
                PublishStaging.IsInStagingFolder(Path.Combine(Path.GetTempPath(), "other")),
                Is.False
            );
        }

        /// <summary>
        /// The exit cleanup deletes Bloom's staging folders that are more than a day old and keeps
        /// newer ones, which another running Bloom may still be using.
        /// </summary>
        [Test]
        public void DeleteOldFolders_DeletesOnlyFoldersOlderThanADay()
        {
            var old = PublishStaging.CreateFolder();
            using (var recent = PublishStaging.CreateFolder())
            {
                try
                {
                    File.WriteAllText(Path.Combine(old.FolderPath, "leftover.txt"), "x");
                    BackDate(old.FolderPath);

                    PublishStaging.DeleteOldFolders();

                    Assert.That(Directory.Exists(old.FolderPath), Is.False);
                    Assert.That(Directory.Exists(recent.FolderPath), Is.True);
                }
                finally
                {
                    old.Dispose();
                }
            }
        }

        /// <summary>
        /// A folder under the staging root that Bloom did not create (no marker file) is left
        /// alone however old it is, since another program may own it.
        /// </summary>
        [Test]
        public void DeleteOldFolders_LeavesFoldersBloomDidNotCreate()
        {
            var foreign = Path.Combine(PublishStaging.RootPath, "other-program");
            Directory.CreateDirectory(foreign);
            try
            {
                File.WriteAllText(Path.Combine(foreign, "work.dat"), "x");
                BackDate(foreign);

                PublishStaging.DeleteOldFolders();

                Assert.That(File.Exists(Path.Combine(foreign, "work.dat")), Is.True);
            }
            finally
            {
                Directory.Delete(foreign, true);
            }
        }

        private static void BackDate(string folder)
        {
            Directory.SetCreationTimeUtc(folder, DateTime.UtcNow - TimeSpan.FromDays(2));
            Assert.That(
                Directory.GetCreationTimeUtc(folder),
                Is.LessThan(DateTime.UtcNow - TimeSpan.FromDays(1)),
                "setup should have back-dated the folder"
            );
        }
    }
}
