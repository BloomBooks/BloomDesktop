using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Utils;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Utils
{
    [TestFixture]
    class BloomArchiveFileTests
    {
        private const string kTestFolderName = "BloomArchiveFileTests";

        [Test]
        public void AddDirectoryContents_SomeExtensionsExcluded_ExcludedExtensionsSkipped()
        {
            // Setup
            using (var testFolder = new TemporaryFolder(kTestFolderName))
            {
                using (var f1 = RobustFile.Create(Path.Combine(testFolder.Path, "hello.txt")))
                {
                    using (var f2 = RobustFile.Create(Path.Combine(testFolder.Path, "temp.tmp")))
                    {
                        var archive = new MockBloomArchiveSubclass();
                        var extensionsToExclude = new string[] { ".tmp" };

                        // System under test
                        archive.AddDirectoryContents(testFolder.Path, extensionsToExclude);

                        // Verification
                        var entryNames = archive.AddFileCallParams.Select(tuple => tuple.Item2);
                        CollectionAssert.AreEquivalent(new string[] { "hello.txt" }, entryNames);
                    }
                }
            }
        }

        [Test]
        public void AddDirectory_ProgressCallbackSpecified_CorrectProgressReported()
        {
            // Setup
            using (var testFolder = new TemporaryFolder(kTestFolderName))
            {
                var filenames = new string[] { "1.txt", "2.txt", "temp.tmp" };
                foreach (var filename in filenames)
                {
                    var fs = RobustFile.Create(Path.Combine(testFolder.Path, filename));
                    fs.Dispose();
                }

                var archive = new MockBloomArchiveSubclass();
                var extensionsToExclude = new string[] { ".tmp" }; // Check that the count takes into account excluded files

                var progressUpdates = new List<float>();
                Action<float> progressCallback = (progressProportion) =>
                {
                    progressUpdates.Add(progressProportion);
                };

                // System under test
                archive.AddDirectory(
                    testFolder.Path,
                    testFolder.Path.Length,
                    extensionsToExclude,
                    progressCallback
                );

                // Verification
                CollectionAssert.AreEqual(new float[] { 0.5f, 1f }, progressUpdates);
            }
        }

        [Test]
        public void AddDirectory_DotPrefixedSubfolder_ExcludedFromArchive()
        {
            // Setup: a normal file at the root and in a normal subfolder should be archived,
            // but anything under a dot-prefixed metadata folder (e.g. the AI image editor's
            // .ai-image-editor working folder, or .git) must be skipped.
            using (var testFolder = new TemporaryFolder(kTestFolderName))
            {
                RobustFile.Create(Path.Combine(testFolder.Path, "root.txt")).Dispose();

                var normalSub = Directory.CreateDirectory(Path.Combine(testFolder.Path, "images"));
                RobustFile.Create(Path.Combine(normalSub.FullName, "pic.png")).Dispose();

                var hiddenSub = Directory.CreateDirectory(
                    Path.Combine(testFolder.Path, ".ai-image-editor")
                );
                RobustFile.Create(Path.Combine(hiddenSub.FullName, "state.json")).Dispose();

                var archive = new MockBloomArchiveSubclass();

                // System under test
                archive.AddDirectory(testFolder.Path, testFolder.Path.Length, null);

                // Verification
                var entryNames = archive
                    .AddFileCallParams.Select(tuple => tuple.Item2.Replace('\\', '/'))
                    .ToList();
                // Sanity check: the normal files did get archived, so a negative result below
                // means "excluded", not "AddDirectory added nothing".
                Assert.That(
                    entryNames.Any(n => n.EndsWith("root.txt")),
                    Is.True,
                    "root file should have been archived"
                );
                Assert.That(
                    entryNames.Any(n => n.EndsWith("images/pic.png")),
                    Is.True,
                    "normal subfolder contents should have been archived"
                );
                // The dot-folder's contents must NOT be archived.
                Assert.That(
                    entryNames.Any(n => n.Contains(".ai-image-editor")),
                    Is.False,
                    "dot-prefixed metadata folders must be excluded from the archive"
                );
            }
        }
    }

    /// <summary>
    /// A simple subclass of BloomArchive that lets us see the function calls.
    /// </summary>
    /// <remarks>I think it's easier to implement a simple Mock than use the Moq framework because AddFile is protected.</remarks>
    class MockBloomArchiveSubclass : BloomArchiveFile
    {
        internal List<Tuple<string, string, bool>> AddFileCallParams =
            new List<Tuple<string, string, bool>>();

        public override void Save() { }

        protected override void AddFile(string path, string entryName, bool compress = true)
        {
            AddFileCallParams.Add(Tuple.Create(path, entryName, compress));
        }

        protected override string CleanName(string entryName) => entryName;

        protected override bool ShouldCompress(string fileFullPath) => false;
    }
}
