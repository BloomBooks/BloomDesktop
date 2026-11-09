using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Publish.BloomPub;
using Bloom.SubscriptionAndFeatures;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.BloomPub
{
    /// <summary>
    /// Tests for deciding whether a BloomPUB made earlier still matches its book, and where kept
    /// BloomPUBs live (BL-17062).
    /// </summary>
    public class BloomPubCacheTests
    {
        private const string kSettingsKey = "en|fr|False|1280|720";

        private TemporaryFolder _tempFolder;
        private string _bookFolder;
        private string _htmlPath;
        private string _bloomPubPath;

        [SetUp]
        public void SetUp()
        {
            _tempFolder = new TemporaryFolder("BloomPubCacheTests");
            _bookFolder = Path.Combine(_tempFolder.Path, "My Book");
            Directory.CreateDirectory(_bookFolder);
            _htmlPath = Path.Combine(_bookFolder, "My Book.htm");
            RobustFile.WriteAllText(_htmlPath, "<html><body><p>Hello</p></body></html>");
            RobustFile.WriteAllBytes(Path.Combine(_bookFolder, "picture.png"), new byte[300]);
            _bloomPubPath = Path.Combine(_tempFolder.Path, "My Book.bloompub");
        }

        [TearDown]
        public void TearDown()
        {
            _tempFolder.Dispose();
        }

        /// <summary>
        /// Write a stand-in BloomPUB and its record, as the Apps screen does after making one.
        /// </summary>
        private void MakeRecordedBloomPub()
        {
            var snapshot = BloomPubCache.TakeSnapshot(_bookFolder);
            RobustFile.WriteAllBytes(_bloomPubPath, new byte[1000]);
            BloomPubCache.RecordNewBloomPub(_bloomPubPath, snapshot, kSettingsKey);
        }

        [Test]
        public void IsCurrent_JustRecorded_True()
        {
            MakeRecordedBloomPub();
            Assert.That(BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.True);
        }

        [Test]
        public void IsCurrent_NoBloomPub_False()
        {
            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_BloomPubWithoutRecord_False()
        {
            RobustFile.WriteAllBytes(_bloomPubPath, new byte[1000]);
            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_DamagedRecord_False()
        {
            MakeRecordedBloomPub();
            RobustFile.WriteAllText(BloomPubCache.GetRecordPath(_bloomPubPath), "{ not json");
            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_DifferentSettings_False()
        {
            MakeRecordedBloomPub();
            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, "en|False|1280|720"),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_BookTextChanged_False()
        {
            MakeRecordedBloomPub();
            RobustFile.WriteAllText(_htmlPath, "<html><body><p>Goodbye</p></body></html>");
            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_PictureReplaced_False()
        {
            MakeRecordedBloomPub();
            RobustFile.WriteAllBytes(Path.Combine(_bookFolder, "picture.png"), new byte[500]);
            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_CollectionSettingsChanged_False()
        {
            var collectionFile = Path.Combine(_tempFolder.Path, "My Collection.bloomCollection");
            RobustFile.WriteAllText(collectionFile, "<Collection/>");
            MakeRecordedBloomPub();

            RobustFile.WriteAllText(
                collectionFile,
                "<Collection><Language1Iso639Code/></Collection>"
            );

            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_BookResavedWithoutChanges_TrueAndRemembersNewSignature()
        {
            MakeRecordedBloomPub();
            var signatureBefore = BloomPubCache.MakeFileSignature(_bookFolder);
            var recordBefore = RobustFile.ReadAllText(BloomPubCache.GetRecordPath(_bloomPubPath));

            // Bloom often saves a book that hasn't changed; that only moves the modified time.
            File.SetLastWriteTimeUtc(_htmlPath, DateTime.UtcNow.AddMinutes(5));
            Assert.That(
                BloomPubCache.MakeFileSignature(_bookFolder),
                Is.Not.EqualTo(signatureBefore),
                "test setup: touching the file should change the signature"
            );

            Assert.That(BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.True);
            Assert.That(
                RobustFile.ReadAllText(BloomPubCache.GetRecordPath(_bloomPubPath)),
                Is.Not.EqualTo(recordBefore),
                "the record should now hold the new signature"
            );
        }

        [Test]
        public void IsCurrent_PdfAdded_True()
        {
            // A BloomPUB leaves PDFs out, and so does the content hash.
            MakeRecordedBloomPub();
            RobustFile.WriteAllBytes(Path.Combine(_bookFolder, "My Book.pdf"), new byte[5000]);
            Assert.That(BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.True);
        }

        [Test]
        public void IsCurrent_MetadataChanged_False()
        {
            // meta.json goes into the BloomPUB, though the content hash ignores it (Devin, PR 8531).
            var metaPath = Path.Combine(_bookFolder, "meta.json");
            RobustFile.WriteAllText(metaPath, "{\"summary\":\"old\",\"currentTool\":\"a\"}");
            MakeRecordedBloomPub();

            RobustFile.WriteAllText(
                metaPath,
                "{\"summary\":\"a new summary\",\"currentTool\":\"a\"}"
            );

            Assert.That(
                BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_OnlyEditorStateInMetadataChanged_True()
        {
            var metaPath = Path.Combine(_bookFolder, "meta.json");
            RobustFile.WriteAllText(
                metaPath,
                "{\"summary\":\"same\",\"currentTool\":\"talkingBookTool\",\"toolboxIsOpen\":false}"
            );
            MakeRecordedBloomPub();

            RobustFile.WriteAllText(
                metaPath,
                "{\"summary\":\"same\",\"currentTool\":\"imageDescriptionTool\",\"toolboxIsOpen\":true}"
            );

            Assert.That(BloomPubCache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.True);
        }

        [Test]
        public void Delete_RemovesBloomPubAndRecord()
        {
            MakeRecordedBloomPub();
            var recordPath = BloomPubCache.GetRecordPath(_bloomPubPath);
            Assert.That(RobustFile.Exists(recordPath), Is.True, "test setup: record should exist");

            BloomPubCache.Delete(_bloomPubPath);

            Assert.That(RobustFile.Exists(_bloomPubPath), Is.False);
            Assert.That(RobustFile.Exists(recordPath), Is.False);
        }

        private static BloomPubPublishSettings MakeSettings(
            string[] langs,
            uint width = 1280,
            string distributionTag = null
        )
        {
            return new BloomPubPublishSettings
            {
                LanguagesToInclude = new HashSet<string>(langs),
                ImagePublishSettings = new ImagePublishSettings
                {
                    MaxWidth = width,
                    MaxHeight = 720,
                },
                DistributionTag = distributionTag,
            };
        }

        private static string MakeKey(
            BloomPubPublishSettings settings,
            string creator = "bloom",
            SubscriptionTier tier = SubscriptionTier.Pro
        )
        {
            return BloomPubCache.MakeSettingsKey(
                settings,
                creator,
                Subscription.CreateTempSubscriptionForTier(tier)
            );
        }

        [Test]
        public void MakeSettingsKey_IgnoresLanguageOrder_ButNotLanguagesOrResolution()
        {
            var key = MakeKey(MakeSettings(new[] { "en", "fr" }));
            Assert.That(
                MakeKey(MakeSettings(new[] { "en", "fr" })),
                Is.EqualTo(key),
                "test setup: the same inputs should give the same key"
            );

            Assert.That(MakeKey(MakeSettings(new[] { "fr", "en" })), Is.EqualTo(key));
            Assert.That(MakeKey(MakeSettings(new[] { "en" })), Is.Not.EqualTo(key));
            Assert.That(MakeKey(MakeSettings(new[] { "en", "fr" }, 1920)), Is.Not.EqualTo(key));
        }

        [Test]
        public void MakeSettingsKey_DependsOnDistributionCreatorAndSubscription()
        {
            // A BloomPUB made for one purpose must not be reused for another: these all change
            // what goes into it.
            var key = MakeKey(MakeSettings(new[] { "en" }));

            Assert.That(
                MakeKey(MakeSettings(new[] { "en" }, distributionTag: "bloom-direct")),
                Is.Not.EqualTo(key)
            );
            Assert.That(MakeKey(MakeSettings(new[] { "en" }), "harvester"), Is.Not.EqualTo(key));
            Assert.That(
                MakeKey(MakeSettings(new[] { "en" }), tier: SubscriptionTier.Basic),
                Is.Not.EqualTo(key)
            );
        }

        [Test]
        public void GetCacheFolder_IsInTempFolder_AndNotRemovedWhenBloomExits()
        {
            var folder = BloomPubCache.GetCacheFolder(Path.Combine(_tempFolder.Path, "Stories"));

            Assert.That(folder, Does.StartWith(BloomPubCache.CacheRoot));
            Assert.That(BloomPubCache.CacheRoot, Does.StartWith(Path.GetTempPath()));
            // TempFile.CleanupTempFolder deletes every temp folder starting with "bloom" when Bloom
            // exits, but kept BloomPUBs must last until the next run.
            var topFolder = BloomPubCache
                .CacheRoot.Substring(Path.GetTempPath().Length)
                .Split(Path.DirectorySeparatorChar)[0];
            Assert.That(topFolder, Does.Not.StartWith("bloom").IgnoreCase);
            Assert.That(Path.GetFileName(folder), Does.StartWith("Stories-"));
        }

        [Test]
        public void GetCacheFolder_SameNameDifferentPlace_DifferentFolders()
        {
            var first = BloomPubCache.GetCacheFolder(
                Path.Combine(_tempFolder.Path, "A", "Stories")
            );
            var second = BloomPubCache.GetCacheFolder(
                Path.Combine(_tempFolder.Path, "B", "Stories")
            );

            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(
                BloomPubCache.GetCacheFolder(Path.Combine(_tempFolder.Path, "A", "Stories")),
                Is.EqualTo(first),
                "the same collection should always get the same folder"
            );
        }

        [Test]
        public void GetCacheFolder_NonAsciiName_GivesAsciiFolderName()
        {
            var folder = BloomPubCache.GetCacheFolder(Path.Combine(_tempFolder.Path, "Histórias"));

            Assert.That(Path.GetFileName(folder), Does.StartWith("Hist_rias-"));
            Assert.That(Path.GetFileName(folder).All(c => c < 128), Is.True);
        }
    }
}
