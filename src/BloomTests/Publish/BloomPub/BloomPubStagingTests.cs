using System;
using System.IO;
using Bloom.Publish.BloomPub;
using NUnit.Framework;

namespace BloomTests.Publish.BloomPub
{
    [TestFixture]
    public class BloomPubStagingTests
    {
        [Test]
        public void CreateFolder_MakesDistinctEmptyFoldersUnderTheRoot()
        {
            using (var first = BloomPubStaging.CreateFolder())
            using (var second = BloomPubStaging.CreateFolder())
            {
                Assert.That(first.FolderPath, Is.Not.EqualTo(second.FolderPath));
                foreach (var folder in new[] { first.FolderPath, second.FolderPath })
                {
                    Assert.That(Directory.Exists(folder), Is.True, folder);
                    Assert.That(Directory.GetFileSystemEntries(folder), Is.Empty, folder);
                    Assert.That(
                        Path.GetDirectoryName(folder),
                        Is.EqualTo(BloomPubStaging.RootPath.TrimEnd(Path.DirectorySeparatorChar))
                    );
                    Assert.That(Path.GetFileName(folder).Length, Is.EqualTo(5), folder);
                }
            }
        }

        [Test]
        public void CreateFolder_DisposeDeletesTheFolder()
        {
            string path;
            using (var folder = BloomPubStaging.CreateFolder())
            {
                path = folder.FolderPath;
                File.WriteAllText(Path.Combine(path, "index.htm"), "<html></html>");
            }
            Assert.That(Directory.Exists(path), Is.False);
        }

        [Test]
        public void IsInStagingFolder_RecognizesPathsInsideTheRootOnly()
        {
            var root = BloomPubStaging.RootPath.TrimEnd(Path.DirectorySeparatorChar);
            var inside = Path.Combine(root, "abcde", "My Book", "index.htm");
            Assert.That(BloomPubStaging.IsInStagingFolder(inside), Is.True);
            Assert.That(BloomPubStaging.IsInStagingFolder(inside.Replace('\\', '/')), Is.True);
            Assert.That(BloomPubStaging.IsInStagingFolder(inside.ToUpperInvariant()), Is.True);
            // A sibling whose name merely starts with the root's name is not inside it.
            Assert.That(
                BloomPubStaging.IsInStagingFolder(root + "x" + Path.DirectorySeparatorChar + "a"),
                Is.False
            );
            Assert.That(
                BloomPubStaging.IsInStagingFolder(Path.Combine(Path.GetTempPath(), "other")),
                Is.False
            );
        }

        [Test]
        public void DeleteOldFolders_DeletesOnlyFoldersOlderThanADay()
        {
            var old = BloomPubStaging.CreateFolder();
            using (var recent = BloomPubStaging.CreateFolder())
            {
                try
                {
                    File.WriteAllText(Path.Combine(old.FolderPath, "leftover.txt"), "x");
                    Directory.SetCreationTimeUtc(
                        old.FolderPath,
                        DateTime.UtcNow - TimeSpan.FromDays(2)
                    );
                    Assert.That(
                        Directory.GetCreationTimeUtc(old.FolderPath),
                        Is.LessThan(DateTime.UtcNow - TimeSpan.FromDays(1)),
                        "setup should have back-dated the old folder"
                    );

                    BloomPubStaging.DeleteOldFolders();

                    Assert.That(Directory.Exists(old.FolderPath), Is.False);
                    Assert.That(Directory.Exists(recent.FolderPath), Is.True);
                }
                finally
                {
                    old.Dispose();
                }
            }
        }
    }
}
