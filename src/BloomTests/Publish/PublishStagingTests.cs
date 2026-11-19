using System;
using System.IO;
using Bloom.Publish;
using NUnit.Framework;

namespace BloomTests.Publish
{
    [TestFixture]
    public class PublishStagingTests
    {
        /// <summary>
        /// Two publications made one after the other get different, empty folders, each directly
        /// under the staging root, with a five-character name and Bloom's marker file beside it.
        /// </summary>
        [Test]
        public void CreateFolder_MakesDistinctEmptyFoldersWithAMarkerBesideEach()
        {
            using (var first = PublishStaging.CreateFolder())
            using (var second = PublishStaging.CreateFolder())
            {
                Assert.That(first.FolderPath, Is.Not.EqualTo(second.FolderPath));
                foreach (var folder in new[] { first.FolderPath, second.FolderPath })
                {
                    Assert.That(Directory.Exists(folder), Is.True, folder);
                    Assert.That(Directory.GetFileSystemEntries(folder), Is.Empty, folder);
                    Assert.That(File.Exists(Marker(folder)), Is.True, folder);
                    Assert.That(
                        Path.GetDirectoryName(folder),
                        Is.EqualTo(PublishStaging.RootPath.TrimEnd(Path.DirectorySeparatorChar))
                    );
                    Assert.That(Path.GetFileName(folder).Length, Is.EqualTo(5), folder);
                }
            }
        }

        /// <summary>
        /// While a staging folder is in use its marker is held open, so nobody else can take it
        /// over; that is what tells other Blooms the folder is in use.
        /// </summary>
        [Test]
        public void CreateFolder_HoldsTheMarkerOpenUntilDisposed()
        {
            using (var folder = PublishStaging.CreateFolder())
            {
                Assert.Throws<IOException>(
                    () => OpenExclusively(Marker(folder.FolderPath)).Dispose(),
                    "the marker of a folder in use should not open exclusively"
                );
            }
        }

        /// <summary>
        /// The exclusive create that claims a staging folder gives null, rather than an error or
        /// a stream, when the file already exists (another Bloom claimed that name first).
        /// </summary>
        [Test]
        public void TryCreateNewExclusive_ReturnsNullWhenTheFileExists()
        {
            using (var folder = PublishStaging.CreateFolder())
            {
                var path = Path.Combine(folder.FolderPath, "claim.txt");
                using (var first = Bloom.RobustFileIO.TryCreateNewExclusive(path))
                {
                    Assert.That(first, Is.Not.Null);
                    Assert.That(Bloom.RobustFileIO.TryCreateNewExclusive(path), Is.Null);
                }
                Assert.That(Bloom.RobustFileIO.TryCreateNewExclusive(path), Is.Null);
            }
        }

        /// <summary>
        /// Disposing a staging folder deletes it, along with whatever the publication put in it,
        /// and its marker.
        /// </summary>
        [Test]
        public void CreateFolder_DisposeDeletesTheFolderAndItsMarker()
        {
            string path;
            using (var folder = PublishStaging.CreateFolder())
            {
                path = folder.FolderPath;
                File.WriteAllText(Path.Combine(path, "index.htm"), "<html></html>");
            }
            Assert.That(Directory.Exists(path), Is.False);
            Assert.That(File.Exists(Marker(path)), Is.False);
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
        /// The exit cleanup deletes a folder whose marker no Bloom holds (left by a Bloom that
        /// crashed), and its marker, but keeps a folder another Bloom is still using, however old
        /// its marker: a preview left open for days must keep its files.
        /// </summary>
        [Test]
        public void DeleteAbandonedFolders_DeletesOnlyFoldersNoBloomIsUsing()
        {
            var abandoned = MakeAbandonedFolder("abnd1");
            using (var inUse = PublishStaging.CreateFolder())
            {
                File.WriteAllText(Path.Combine(inUse.FolderPath, "page.png"), "x");
                File.SetCreationTimeUtc(
                    Marker(inUse.FolderPath),
                    DateTime.UtcNow - TimeSpan.FromDays(30)
                );

                PublishStaging.DeleteAbandonedFolders();

                Assert.That(Directory.Exists(abandoned), Is.False);
                Assert.That(File.Exists(Marker(abandoned)), Is.False);
                Assert.That(File.Exists(Path.Combine(inUse.FolderPath, "page.png")), Is.True);
                Assert.That(File.Exists(Marker(inUse.FolderPath)), Is.True);
            }
        }

        /// <summary>
        /// If an abandoned folder cannot be deleted yet (a file in it is still open), the cleanup
        /// keeps its marker, so a later cleanup finishes the job once the file is released.
        /// </summary>
        [Test]
        public void DeleteAbandonedFolders_KeepsTheMarkerOfAFolderItCannotDeleteYet()
        {
            var abandoned = MakeAbandonedFolder("abnd2");
            var lockedFile = Path.Combine(abandoned, "video.mp4");
            File.WriteAllText(lockedFile, "x");
            using (OpenExclusively(lockedFile))
            {
                PublishStaging.DeleteAbandonedFolders();

                Assert.That(File.Exists(Marker(abandoned)), Is.True);
            }

            PublishStaging.DeleteAbandonedFolders();

            Assert.That(Directory.Exists(abandoned), Is.False);
            Assert.That(File.Exists(Marker(abandoned)), Is.False);
        }

        /// <summary>
        /// A folder under the staging root that Bloom did not create (no marker file) is left
        /// alone, since another program may own it.
        /// </summary>
        [Test]
        public void DeleteAbandonedFolders_LeavesFoldersBloomDidNotCreate()
        {
            var foreign = Path.Combine(PublishStaging.RootPath, "other-program");
            Directory.CreateDirectory(foreign);
            try
            {
                File.WriteAllText(Path.Combine(foreign, "work.dat"), "x");

                PublishStaging.DeleteAbandonedFolders();

                Assert.That(File.Exists(Path.Combine(foreign, "work.dat")), Is.True);
            }
            finally
            {
                Directory.Delete(foreign, true);
            }
        }

        // The marker file that claims a staging folder sits beside it.
        private static string Marker(string folder) => folder + ".bloom-staging";

        private static FileStream OpenExclusively(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // What a Bloom that crashed leaves behind: a folder with files in it and a marker nobody
        // holds open.
        private static string MakeAbandonedFolder(string name)
        {
            var folder = Path.Combine(PublishStaging.RootPath, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "index.htm"), "<html></html>");
            File.WriteAllText(Marker(folder), "");
            return folder;
        }
    }
}
