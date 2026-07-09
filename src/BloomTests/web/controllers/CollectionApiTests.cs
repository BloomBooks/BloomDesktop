using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for CollectionApi's pure not-yet-downloaded placeholder merge logic (dogfood batch 1,
    /// item 7, progressive join): ComputeNotYetDownloadedBookEntries decides, given the local book
    /// folder names already scanned from disk and a Cloud Team Collection's full repo book list
    /// (folder name + stable instance id), which repo books get a placeholder entry in the
    /// collections/books JSON. Follows CollectionChooserApiTests'/SharingApiTests' pattern of
    /// testing internal static pure logic directly, no filesystem/network/repo access required.
    /// </summary>
    [TestFixture]
    public class CollectionApiTests
    {
        [Test]
        public void ComputeNotYetDownloadedBookEntries_NoRepoBooks_ReturnsEmpty()
        {
            var result = CollectionApi.ComputeNotYetDownloadedBookEntries(
                new HashSet<string>(),
                new List<(string name, string instanceId)>(),
                "C:/Collections/My Collection"
            );

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ComputeNotYetDownloadedBookEntries_RepoBookWithNoLocalFolder_GetsAPlaceholder()
        {
            var result = CollectionApi.ComputeNotYetDownloadedBookEntries(
                new HashSet<string>(), // nothing local at all yet -- fresh progressive join
                new List<(string name, string instanceId)> { ("Remote Book", "instance-1") },
                "C:/Collections/My Collection"
            );

            Assert.That(result.Count, Is.EqualTo(1));
            var entry = result[0];
            Assert.That(entry.notYetDownloaded, Is.True);
            Assert.That(entry.id, Is.EqualTo("instance-1"));
            Assert.That(entry.title, Is.EqualTo("Remote Book"));
            Assert.That(entry.folderName, Is.EqualTo("Remote Book"));
            Assert.That(entry.collectionId, Is.EqualTo("C:/Collections/My Collection"));
            Assert.That(
                entry.folderPath,
                Is.EqualTo(System.IO.Path.Combine("C:/Collections/My Collection", "Remote Book"))
            );
        }

        [Test]
        public void ComputeNotYetDownloadedBookEntries_RepoBookAlreadyLocal_NoPlaceholder()
        {
            var result = CollectionApi.ComputeNotYetDownloadedBookEntries(
                new HashSet<string> { "Already Here" },
                new List<(string name, string instanceId)> { ("Already Here", "instance-1") },
                "C:/Collections/My Collection"
            );

            Assert.That(
                result,
                Is.Empty,
                "a repo book that already has a local folder must not get a placeholder"
            );
        }

        [Test]
        public void ComputeNotYetDownloadedBookEntries_LocalFolderMatchRespectsCaseInsensitiveSet()
        {
            // The pure function itself is comparer-agnostic (it just calls Contains on whatever
            // set it's given); the real caller (CollectionApi.GetNotYetDownloadedBookEntries)
            // builds its local-names set with StringComparer.OrdinalIgnoreCase, matching the rest
            // of the TC code's case-insensitive folder-name handling. This test pins THAT contract
            // by using the same kind of set a real caller would.
            var result = CollectionApi.ComputeNotYetDownloadedBookEntries(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "already here" }, // different case
                new List<(string name, string instanceId)> { ("Already Here", "instance-1") },
                "C:/Collections/My Collection"
            );

            Assert.That(
                result,
                Is.Empty,
                "a case-insensitively-matching local folder must suppress the placeholder"
            );
        }

        [Test]
        public void ComputeNotYetDownloadedBookEntries_MixOfDownloadedAndNotYetDownloaded_OnlyPlaceholdersTheMissingOnes()
        {
            var result = CollectionApi.ComputeNotYetDownloadedBookEntries(
                new HashSet<string> { "Book A" },
                new List<(string name, string instanceId)>
                {
                    ("Book A", "instance-a"),
                    ("Book B", "instance-b"),
                    ("Book C", "instance-c"),
                },
                "C:/Collections/My Collection"
            );

            Assert.That(
                result.Select(e => e.folderName),
                Is.EquivalentTo(new[] { "Book B", "Book C" })
            );
            Assert.That(result.All(e => e.notYetDownloaded), Is.True);
        }

        [Test]
        public void ComputeNotYetDownloadedBookEntries_MissingInstanceId_FallsBackToNameAsId()
        {
            // A book whose instance id isn't known yet (shouldn't normally happen, but the id must
            // never be null/empty -- the client uses it as a React key and a websocket-message
            // correlation id).
            var result = CollectionApi.ComputeNotYetDownloadedBookEntries(
                new HashSet<string>(),
                new List<(string name, string instanceId)> { ("No Instance Id Book", null) },
                "C:/Collections/My Collection"
            );

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].id, Is.EqualTo("No Instance Id Book"));
        }
    }
}
