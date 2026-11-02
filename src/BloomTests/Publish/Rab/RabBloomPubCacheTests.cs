using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Book;
using Bloom.Publish.BloomPub;
using Bloom.Publish.Rab;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.Rab
{
    /// <summary>
    /// Tests for deciding whether a BloomPUB made earlier by the Apps screen still matches its
    /// book (BL-17062).
    /// </summary>
    public class RabBloomPubCacheTests
    {
        private const string kSettingsKey = "en|fr|False|1280|720";

        private TemporaryFolder _tempFolder;
        private string _bookFolder;
        private string _htmlPath;
        private string _bloomPubPath;
        private RabBloomPubCache _cache;

        [SetUp]
        public void SetUp()
        {
            _tempFolder = new TemporaryFolder("RabBloomPubCacheTests");
            _bookFolder = Path.Combine(_tempFolder.Path, "My Book");
            Directory.CreateDirectory(_bookFolder);
            _htmlPath = Path.Combine(_bookFolder, "My Book.htm");
            RobustFile.WriteAllText(_htmlPath, "<html><body><p>Hello</p></body></html>");
            RobustFile.WriteAllBytes(Path.Combine(_bookFolder, "picture.png"), new byte[300]);
            _bloomPubPath = Path.Combine(_tempFolder.Path, "My Book.bloompub");
            _cache = new RabBloomPubCache();
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
            var snapshot = _cache.TakeSnapshot(_bookFolder);
            RobustFile.WriteAllBytes(_bloomPubPath, new byte[1000]);
            _cache.RecordNewBloomPub(_bloomPubPath, snapshot, kSettingsKey);
        }

        [Test]
        public void IsCurrent_JustRecorded_True()
        {
            MakeRecordedBloomPub();
            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.True);
        }

        [Test]
        public void IsCurrent_NoBloomPub_False()
        {
            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.False);
        }

        [Test]
        public void IsCurrent_BloomPubWithoutRecord_False()
        {
            RobustFile.WriteAllBytes(_bloomPubPath, new byte[1000]);
            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.False);
        }

        [Test]
        public void IsCurrent_DamagedRecord_False()
        {
            MakeRecordedBloomPub();
            RobustFile.WriteAllText(RabBloomPubCache.GetRecordPath(_bloomPubPath), "{ not json");
            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.False);
        }

        [Test]
        public void IsCurrent_DifferentSettings_False()
        {
            MakeRecordedBloomPub();
            Assert.That(
                _cache.IsCurrent(_bloomPubPath, _bookFolder, "en|False|1280|720"),
                Is.False
            );
        }

        [Test]
        public void IsCurrent_BookTextChanged_False()
        {
            MakeRecordedBloomPub();
            RobustFile.WriteAllText(_htmlPath, "<html><body><p>Goodbye</p></body></html>");
            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.False);
        }

        [Test]
        public void IsCurrent_PictureReplaced_False()
        {
            MakeRecordedBloomPub();
            RobustFile.WriteAllBytes(Path.Combine(_bookFolder, "picture.png"), new byte[500]);
            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.False);
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

            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.False);
        }

        [Test]
        public void IsCurrent_BookResavedWithoutChanges_TrueAndRemembersNewSignature()
        {
            MakeRecordedBloomPub();
            var signatureBefore = RabBloomPubCache.MakeFileSignature(_bookFolder);
            var recordBefore = RobustFile.ReadAllText(
                RabBloomPubCache.GetRecordPath(_bloomPubPath)
            );

            // Bloom often saves a book that hasn't changed; that only moves the modified time.
            File.SetLastWriteTimeUtc(_htmlPath, DateTime.UtcNow.AddMinutes(5));
            Assert.That(
                RabBloomPubCache.MakeFileSignature(_bookFolder),
                Is.Not.EqualTo(signatureBefore),
                "test setup: touching the file should change the signature"
            );

            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.True);
            Assert.That(
                RobustFile.ReadAllText(RabBloomPubCache.GetRecordPath(_bloomPubPath)),
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
            Assert.That(_cache.IsCurrent(_bloomPubPath, _bookFolder, kSettingsKey), Is.True);
        }

        [Test]
        public void Delete_RemovesBloomPubAndRecord()
        {
            MakeRecordedBloomPub();
            var recordPath = RabBloomPubCache.GetRecordPath(_bloomPubPath);
            Assert.That(RobustFile.Exists(recordPath), Is.True, "test setup: record should exist");

            RabBloomPubCache.Delete(_bloomPubPath);

            Assert.That(RobustFile.Exists(_bloomPubPath), Is.False);
            Assert.That(RobustFile.Exists(recordPath), Is.False);
        }

        [Test]
        public void MakeSettingsKey_IgnoresLanguageOrder_ButNotLanguagesOrResolution()
        {
            BloomPubPublishSettings Make(string[] langs, uint width)
            {
                return new BloomPubPublishSettings
                {
                    LanguagesToInclude = new HashSet<string>(langs),
                    ImagePublishSettings = new ImagePublishSettings
                    {
                        MaxWidth = width,
                        MaxHeight = 720,
                    },
                };
            }
            var key = RabBloomPubCache.MakeSettingsKey(Make(new[] { "en", "fr" }, 1280));

            Assert.That(
                RabBloomPubCache.MakeSettingsKey(Make(new[] { "fr", "en" }, 1280)),
                Is.EqualTo(key)
            );
            Assert.That(
                RabBloomPubCache.MakeSettingsKey(Make(new[] { "en" }, 1280)),
                Is.Not.EqualTo(key)
            );
            Assert.That(
                RabBloomPubCache.MakeSettingsKey(Make(new[] { "en", "fr" }, 1920)),
                Is.Not.EqualTo(key)
            );
        }
    }
}
