using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using BloomTests.Book;
using Bloom.SubscriptionAndFeatures;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;

// It would be nice to have some tests for the new FirebaseLogin code, too, but so far
// we have not been able to get that code to run except after starting Bloom fully.

namespace BloomTests.WebLibraryIntegration
{
    public class BloomLibraryBookApiClientTestDouble : BloomLibraryBookApiClient
    {
        // Fake log in so that the unit tests can upload, since we haven't been able to get the new firebase login to work
        // except when Bloom is actually running.
        public void TestOnly_SetUserAccountInfo()
        {
            Account = "unittest@example.com";
            _authenticationToken = "fakeTokenForUnitTests"; //azure function will ignore auth token for unit tests
        }

        public void TestOnly_DeleteBook(string bookObjectId)
        {
            var request = MakeDeleteRequest(bookObjectId);
            AzureRestClient.Execute(request);
        }

        // Will throw an exception if there is any reason we can't make a successful query, including if there is no internet.
        public dynamic TestOnly_GetSingleBookRecord(string id, bool includeLanguageInfo = false)
        {
            var json = GetBookRecords(id, includeLanguageInfo);
            Assert.That(json.Count, Is.EqualTo(1));
            return json[0];
        }
    }

    [TestFixture]
    public class BookUploadAndDownloadTests
    {
        private TemporaryFolder _workFolder;
        private string _workFolderPath;
        private BookUpload _uploader;
        private BookDownload _downloader;
        private BloomLibraryBookApiClientTestDouble _bloomLibraryBookApiClient;
        List<BookInfo> _downloadedBooks = new List<BookInfo>();
        private HtmlThumbNailer _htmlThumbNailer;
        private string _thisTestId;

        [SetUp]
        public void Setup()
        {
            _thisTestId = Guid.NewGuid().ToString().Replace('-', '_');

            _workFolder = new TemporaryFolder("unittest-" + _thisTestId);
            _workFolderPath = _workFolder.FolderPath;
            Assert.AreEqual(
                0,
                Directory.GetDirectories(_workFolderPath).Count(),
                "Some stuff was left over from a previous test"
            );
            Assert.AreEqual(
                0,
                Directory.GetFiles(_workFolderPath).Count(),
                "Some stuff was left over from a previous test"
            );
            // Todo: Make sure the S3 unit test bucket is empty.
            // Todo: Make sure the parse.com unit test book table is empty
            _bloomLibraryBookApiClient = new BloomLibraryBookApiClientTestDouble();
            _htmlThumbNailer = new HtmlThumbNailer();
            _uploader = new BookUpload(
                _bloomLibraryBookApiClient,
                new BloomS3Client(BloomS3Client.UnitTestBucketName),
                new BookThumbNailer(_htmlThumbNailer)
            );
            BookUpload.IsDryRun = false;
            _downloader = new BookDownload(new BloomS3Client(BloomS3Client.UnitTestBucketName));
            _downloader.BookDownLoaded += (sender, args) => _downloadedBooks.Add(args.BookDetails);
        }

        [TearDown]
        public void TearDown()
        {
            _htmlThumbNailer.Dispose();
            _workFolder.Dispose();
        }

        private string MakeBook(
            string bookName,
            string id,
            string uploader,
            string data,
            bool makeCorruptFile = false,
            string htmName = "one.htm"
        )
        {
            var f = new TemporaryFolder(_workFolder, bookName);
            File.WriteAllText(Path.Combine(f.FolderPath, htmName), data);
            File.WriteAllText(Path.Combine(f.FolderPath, "one.css"), @"test");
            File.WriteAllText(Path.Combine(f.FolderPath, "unmodified.css"), @"test");

            // Subdirectory test:
            // We're using the "activities" directory out of convenience.
            // Files in the activities folder get an automatic pass when copying to staging.
            // In other words, we don't have to make an explicit reference to it in the .htm
            // to prevent them getting cleaned up (like we would for audio or other files).
            Directory.CreateDirectory(Path.Combine(f.FolderPath, "activities"));
            File.WriteAllText(
                Path.Combine(f.FolderPath, "activities", "file-to-replace.txt"),
                "file to replace in subdirectory"
            );
            File.WriteAllText(
                Path.Combine(f.FolderPath, "activities", "file-to-modify.txt"),
                "file to modify in subdirectory"
            );
            File.WriteAllText(
                Path.Combine(f.FolderPath, "activities", "unmodified.txt"),
                @"unmodified subdirectory file"
            );

            if (makeCorruptFile)
                File.WriteAllText(
                    Path.Combine(f.FolderPath, BookStorage.PrefixForCorruptHtmFiles + ".htm"),
                    @"rubbish"
                );

            File.WriteAllText(
                Path.Combine(f.FolderPath, "meta.json"),
                "{\"bookInstanceId\":\""
                    + id
                    + _thisTestId
                    + "\",\"uploadedBy\":\""
                    + uploader
                    + "\"}"
            );

            return f.FolderPath;
        }

        /// <summary>
        /// Review: this is fragile and expensive. We're doing real internet traffic and creating real objects on S3 and parse-server
        /// which (to a very small extent) costs us real money. This will be slow. Also, under S3 eventual consistency rules,
        /// there is no guarantee that the data we just created will actually be retrievable immediately.
        /// </summary>
        public (string bookObjectId, string newBookFolder) UploadAndDownLoadNewBook(
            string bookName,
            string id,
            string uploader,
            string data,
            bool isTemplate = false
        )
        {
            //  Create a book folder with meta.json that includes an uploader and id and some other files.
            var originalBookFolder = MakeBook(bookName, id, uploader, data, true);
            if (isTemplate)
            {
                var metadata = BookMetaData.FromFolder(originalBookFolder);
                metadata.IsSuitableForMakingShells = true;
                metadata.WriteToFolder(originalBookFolder);
            }
            // The files that actually get uploaded omit some of the ones in the folder.
            // The only omitted one that messes up current unit tests is meta.bak
            var filesToUpload = Directory
                .GetFiles(originalBookFolder, "*.*", SearchOption.AllDirectories)
                .Where(
                    p => !p.EndsWith(".bak") && !p.Contains(BookStorage.PrefixForCorruptHtmFiles)
                );
            int fileCount = filesToUpload.Count();
            _bloomLibraryBookApiClient.TestOnly_SetUserAccountInfo();
            //HashSet<string> notifications = new HashSet<string>();

            var progress = new SIL.Progress.StringBuilderProgress();
            var bookObjectId = _uploader.UploadBook_ForUnitTest(
                originalBookFolder,
                out var s3PrefixUploadedTo,
                progress
            );
            Assert.That(string.IsNullOrEmpty(s3PrefixUploadedTo), Is.False);

            var uploadMessages = progress.Text.Split(
                new string[] { Environment.NewLine },
                StringSplitOptions.RemoveEmptyEntries
            );

            var expectedFileCount = fileCount + 3; // "Preparing...", "Finishing...", and "Finished"
#if DEBUG
            ++expectedFileCount; // and if in debug mode, then plus one for S3 key message
#endif
            Assert.That(
                uploadMessages.Length,
                Is.EqualTo(expectedFileCount),
                "Uploaded file counts do not match"
            );
            Assert.That(progress.Text, Does.Contain(Path.GetFileName(filesToUpload.First())));

            WaitUntilS3DataIsOnServer(s3PrefixUploadedTo, originalBookFolder);
            var dest = _workFolderPath.CombineForPath("output");
            Directory.CreateDirectory(dest);
            _downloadedBooks.Clear();
            var url =
                BookUpload.BloomS3UrlPrefix
                + BloomS3Client.UnitTestBucketName
                + "/"
                + GetParentOfS3Prefix(s3PrefixUploadedTo);
            var newBookFolder = _downloader.HandleDownloadWithoutProgress(url, dest);

            Assert.That(
                Directory.GetFiles(newBookFolder, "*.*", SearchOption.AllDirectories).Length,
                Is.EqualTo(fileCount)
            );

            Assert.That(_downloadedBooks.Count, Is.EqualTo(1));
            Assert.That(_downloadedBooks[0].FolderPath, Is.EqualTo(newBookFolder));
            // Todo: verify that metadata was transferred to parse-server
            return (bookObjectId, newBookFolder);
        }

        /// <summary>
        /// Regression test. Using ChangeExtension to append the PDF truncates the name when there is a period.
        /// </summary>
        [Test]
        public void BookWithPeriodInTitle_DoesNotGetTruncatedPdfName()
        {
#if __MonoCS__
            Assert.That(
                BookUpload.UploadPdfPath("/somewhere/Look at the sky. What do you see"),
                Is.EqualTo(
                    "/somewhere/Look at the sky. What do you see/Look at the sky. What do you see.pdf"
                )
            );
#else
            Assert.That(
                BookUpload.UploadPdfPath(@"c:\somewhere\Look at the sky. What do you see"),
                Is.EqualTo(
                    @"c:\somewhere\Look at the sky. What do you see\Look at the sky. What do you see.pdf"
                )
            );
#endif
        }

        [Test]
        public void UploadBooks_SimilarIds_DoNotOverwrite()
        {
            var firstPair = UploadAndDownLoadNewBook("first", "book1", "Jack", "Jack's data");
            var secondPair = UploadAndDownLoadNewBook("second", "book1", "Jill", "Jill's data");
            var thirdPair = UploadAndDownLoadNewBook("third", "book2", "Jack", "Jack's other data");
            try
            {
                // Data uploaded with the same id but a different uploader should form a distinct book; the Jill data
                // should not overwrite the Jack data. Likewise, data uploaded with a distinct Id by the same uploader should be separate.
                var jacksFirstData = File.ReadAllText(
                    firstPair.newBookFolder.CombineForPath("one.htm")
                );
                // We use stringContaining here because upload does make some changes.
                Assert.That(jacksFirstData, Does.Contain("Jack's data"));
                var jillsData = File.ReadAllText(
                    secondPair.newBookFolder.CombineForPath("one.htm")
                );
                Assert.That(jillsData, Does.Contain("Jill's data"));
                var jacksSecondData = File.ReadAllText(
                    thirdPair.newBookFolder.CombineForPath("one.htm")
                );
                Assert.That(jacksSecondData, Does.Contain("Jack's other data"));
            }
            finally
            {
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(firstPair.bookObjectId);
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(secondPair.bookObjectId);
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(thirdPair.bookObjectId);
            }
        }

        [Test]
        public void UploadBook_SameId_Replaces()
        {
            var bookFolder = MakeBook("unittest", "myId", "me", "something");
            var jsonPath = bookFolder.CombineForPath(BookInfo.MetaDataFileName);
            var json = File.ReadAllText(jsonPath);
            var jsonStart = json.Substring(0, json.Length - 1);
            var newJson = jsonStart + ",\"bookLineage\":\"original\"}";
            File.WriteAllText(jsonPath, newJson);
            _bloomLibraryBookApiClient.TestOnly_SetUserAccountInfo();

            var bookObjectId = _uploader.UploadBook_ForUnitTest(bookFolder);
            try
            {
                Assert.That(string.IsNullOrEmpty(bookObjectId), Is.False);
                File.Delete(bookFolder.CombineForPath("one.css"));
                File.Delete(bookFolder.CombineForPath("activities", "file-to-replace.txt"));
                File.WriteAllText(Path.Combine(bookFolder, "one.htm"), "something new");
                File.WriteAllText(Path.Combine(bookFolder, "two.css"), @"test");
                File.WriteAllText(
                    Path.Combine(bookFolder, "activities", "replacement-file.txt"),
                    "another fake activity file"
                );
                File.WriteAllText(
                    Path.Combine(bookFolder, "activities", "file-to-modify.txt"),
                    "modified file content"
                );

                // Tweak the json, but don't change the ID.
                newJson = jsonStart + ",\"bookLineage\":\"other\"}";
                File.WriteAllText(jsonPath, newJson);

                _uploader.UploadBook_ForUnitTest(
                    bookFolder,
                    out var s3PrefixUploadedTo,
                    existingBookObjectId: bookObjectId
                );

                var s3PrefixParent = GetParentOfS3Prefix(s3PrefixUploadedTo);

                var dest = _workFolderPath.CombineForPath("output");
                Directory.CreateDirectory(dest);
                var newBookFolder = _downloader.DownloadBook(
                    BloomS3Client.UnitTestBucketName,
                    s3PrefixParent,
                    dest
                );

                var firstData = File.ReadAllText(newBookFolder.CombineForPath("one.htm"));
                Assert.That(
                    firstData,
                    Does.Contain("something new"),
                    "We should have overwritten the changed file"
                );
                Assert.That(
                    File.Exists(newBookFolder.CombineForPath("unmodified.css")),
                    Is.True,
                    "We should have kept the unmodified file"
                );
                Assert.That(
                    File.Exists(newBookFolder.CombineForPath("activities", "unmodified.txt")),
                    Is.True,
                    "We should have kept the unmodified file in a subdirectory"
                );
                Assert.That(
                    File.Exists(newBookFolder.CombineForPath("two.css")),
                    Is.True,
                    "We should have added the new file"
                );
                Assert.That(
                    File.Exists(newBookFolder.CombineForPath("activities", "replacement-file.txt")),
                    Is.True,
                    "We should have added the new file in a subdirectory"
                );

                Assert.That(
                    File.Exists(newBookFolder.CombineForPath("activities", "file-to-modify.txt")),
                    Is.True,
                    "We should have kept the modified file in a subdirectory"
                );
                Assert.That(
                    File.ReadAllText(
                        newBookFolder.CombineForPath("activities", "file-to-modify.txt")
                    ),
                    Is.EqualTo("modified file content"),
                    "We should have the new contents of the file in a subdirectory"
                );

                Assert.That(
                    File.Exists(newBookFolder.CombineForPath("one.css")),
                    Is.False,
                    "We should have deleted the obsolete file"
                );
                Assert.That(
                    File.Exists(newBookFolder.CombineForPath("activities", "file-to-replace.txt")),
                    Is.False,
                    "We should have deleted the obsolete file in a subdirectory"
                );

                // Verify that metadata was overwritten, new record not created.
                var records = _bloomLibraryBookApiClient.GetBookRecords(
                    "myId" + _thisTestId,
                    false
                );
                Assert.That(
                    records.Count,
                    Is.EqualTo(1),
                    "Should have overwritten parse server record, not added or deleted"
                );
                var bookRecord = records[0];
                Assert.That(bookRecord.bookLineage.Value, Is.EqualTo("other"));
            }
            finally
            {
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(bookObjectId);
            }
        }

        [Test]
        public void UploadBook_SetsInterestingParseFieldsCorrectly()
        {
            _bloomLibraryBookApiClient.TestOnly_SetUserAccountInfo();

            var bookFolder = MakeBook(
                MethodBase.GetCurrentMethod().Name,
                "myId",
                "me",
                "something"
            );
            var initialBookObjectId = _uploader.UploadBook_ForUnitTest(bookFolder);
            try
            {
                var bookInstanceId = "myId" + _thisTestId;
                var bookRecord = _bloomLibraryBookApiClient.TestOnly_GetSingleBookRecord(
                    bookInstanceId
                );

                // Verify new upload
                Assert.That(bookRecord.harvestState.Value, Is.EqualTo("New"));
                Assert.That(
                    bookRecord.tags[0].Value,
                    Is.EqualTo("system:Incoming"),
                    "New books should always get the system:Incoming tag."
                );
                Assert.That(
                    bookRecord.updateSource.Value.StartsWith("BloomDesktop "),
                    Is.True,
                    "updateSource should start with BloomDesktop when uploaded"
                );
                Assert.That(
                    bookRecord.updateSource.Value,
                    Is.Not.EqualTo("BloomDesktop old"),
                    "updateSource should not equal 'BloomDesktop old' when uploaded from current Bloom"
                );
                DateTime lastUploadedDateTime = bookRecord.lastUploaded.Value;
                var differenceBetweenNowAndCreationOfJson = DateTime.UtcNow - lastUploadedDateTime;
                Assert.That(
                    differenceBetweenNowAndCreationOfJson,
                    // Since this is actually set on the server, clocks could be off by several seconds.
                    Is.GreaterThan(TimeSpan.FromSeconds(-20)),
                    "lastUploaded should be a valid date representing now-ish"
                );
                Assert.That(
                    differenceBetweenNowAndCreationOfJson,
                    Is.LessThan(TimeSpan.FromSeconds(20)),
                    "lastUploaded should be a valid date representing now-ish"
                );
                var bookObjectId = bookRecord.id.Value;
                Assert.That(
                    string.IsNullOrEmpty(bookObjectId),
                    Is.False,
                    "book objectId should be set"
                );
                Assert.That(
                    string.IsNullOrEmpty(bookRecord.uploadPendingTimestamp.Value),
                    Is.True,
                    "uploadPendingTimestamp should not be set for a successful upload"
                );
                Assert.That(
                    bookRecord.inCirculation.Value,
                    Is.True,
                    "new books should default to being in circulation"
                );

                // re-upload the book
                _uploader.UploadBook_ForUnitTest(
                    bookFolder,
                    out string _,
                    existingBookObjectId: bookObjectId
                );
                bookRecord = _bloomLibraryBookApiClient.TestOnly_GetSingleBookRecord(
                    bookInstanceId
                );

                // Verify re-upload
                Assert.That(bookRecord.harvestState.Value, Is.EqualTo("Updated"));
                Assert.That(
                    bookRecord.tags[0].Value,
                    Is.EqualTo("system:Incoming"),
                    "Re-uploaded books should always get the system:Incoming tag."
                );
                Assert.That(
                    bookRecord.updateSource.Value.StartsWith("BloomDesktop "),
                    Is.True,
                    "updateSource should start with BloomDesktop when re-uploaded"
                );
                Assert.That(
                    bookRecord.updateSource.Value,
                    Is.Not.EqualTo("BloomDesktop old"),
                    "updateSource should not equal 'BloomDesktop old' when uploaded from current Bloom"
                );
                lastUploadedDateTime = bookRecord.lastUploaded.Value;
                differenceBetweenNowAndCreationOfJson = DateTime.UtcNow - lastUploadedDateTime;
                Assert.That(
                    differenceBetweenNowAndCreationOfJson,
                    // Since this is actually set on the server, clocks could be off by several seconds.
                    Is.GreaterThan(TimeSpan.FromSeconds(-20)),
                    "lastUploaded should be a valid date representing now-ish"
                );
                Assert.That(
                    differenceBetweenNowAndCreationOfJson,
                    Is.LessThan(TimeSpan.FromSeconds(20)),
                    "lastUploaded should be a valid date representing now-ish"
                );
            }
            finally
            {
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(initialBookObjectId);
            }
        }

        [Test]
        public void UploadBook_FillsInMetaData()
        {
            var bookFolder = MakeBook("My incomplete book", "", "", "data");
            File.WriteAllText(
                Path.Combine(bookFolder, "thumbnail.png"),
                @"this should be a binary picture"
            );

            _bloomLibraryBookApiClient.TestOnly_SetUserAccountInfo();

            var bookObjectId = _uploader.UploadBook_ForUnitTest(
                bookFolder,
                out var s3PrefixUploadedTo
            );
            try
            {
                Assert.That(string.IsNullOrEmpty(bookObjectId), Is.False);
                Assert.That(bookObjectId == "quiet", Is.False);
                Assert.That(string.IsNullOrEmpty(s3PrefixUploadedTo), Is.False);
                WaitUntilS3DataIsOnServer(s3PrefixUploadedTo, bookFolder);
                var dest = _workFolderPath.CombineForPath("output");
                Directory.CreateDirectory(dest);
                var newBookFolder = _downloader.DownloadBook(
                    BloomS3Client.UnitTestBucketName,
                    GetParentOfS3Prefix(s3PrefixUploadedTo),
                    dest
                );
                var metadata = BookMetaData.FromString(
                    File.ReadAllText(Path.Combine(newBookFolder, BookInfo.MetaDataFileName))
                );
                Assert.That(
                    string.IsNullOrEmpty(metadata.Id),
                    Is.False,
                    "should have filled in missing ID"
                );

                var record = _bloomLibraryBookApiClient.TestOnly_GetSingleBookRecord(metadata.Id);
                string baseUrl = record.baseUrl;
                Assert.That(
                    baseUrl.StartsWith("https://s3.amazonaws.com/BloomLibraryBooks"),
                    "baseUrl should start with s3 prefix"
                );

                Assert.IsFalse(
                    File.Exists(Path.Combine(newBookFolder, "My incomplete book.BloomBookOrder")),
                    "Should not have created, uploaded or downloaded a book order file; these are obsolete"
                );
            }
            finally
            {
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(bookObjectId);
            }
        }

        [Test]
        public void DownloadUrl_GetsDocument_AndSettings()
        {
            var id = Guid.NewGuid().ToString();
            var bookFolder = MakeBook("My Url Book", id, "someone", "My content");
            int fileCount = Directory.GetFiles(bookFolder).Length;
            _bloomLibraryBookApiClient.TestOnly_SetUserAccountInfo();

            var settings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        _workFolder.FolderPath,
                        "myCollection"
                    ),
                    Language1Tag = "dmx", // Dema language of Mozambique (arbitrary choice)
                    Language2Tag = "en",
                    Language3Tag = "fr",
                    Subscription = new Subscription("Test-Expired-005691-4935") // expired 2/3/2025
                }
            );
            var bookObjectId = _uploader.UploadBook_ForUnitTest(
                bookFolder,
                out var s3PrefixUploadedTo,
                collectionSettings: settings
            );
            try
            {
                Assert.That(string.IsNullOrEmpty(bookObjectId), Is.False);
                Assert.That(bookObjectId == "quiet", Is.False);
                Assert.That(string.IsNullOrEmpty(s3PrefixUploadedTo), Is.False);
                WaitUntilS3DataIsOnServer(s3PrefixUploadedTo, bookFolder);
                var dest = _workFolderPath.CombineForPath("output");
                Directory.CreateDirectory(dest);

                var newBookFolder = _downloader.DownloadFromOrderUrl(
                    BloomLinkArgs.kBloomUrlPrefix
                        + BloomLinkArgs.kOrderFile
                        + "="
                        + "BloomLibraryBooks-UnitTests/"
                        + GetParentOfS3Prefix(s3PrefixUploadedTo),
                    dest,
                    "nonsense"
                );
                Assert.That(Directory.GetFiles(newBookFolder).Length, Is.EqualTo(fileCount));

                var newBookFolder2 = _downloader.DownloadFromOrderUrl(
                    BloomLinkArgs.kBloomUrlPrefix
                        + BloomLinkArgs.kOrderFile
                        + "="
                        + "BloomLibraryBooks-UnitTests/"
                        + GetParentOfS3Prefix(s3PrefixUploadedTo)
                        + "&forEdit=true",
                    dest,
                    "nonsense",
                    true
                );
                var collectionPath = Path.GetDirectoryName(newBookFolder2);
                var collectionName = Path.GetFileName(collectionPath);
                Assert.That(collectionName, Is.EqualTo("From Bloom Library - one"));
                var settings2 = new CollectionSettings(
                    Path.Combine(collectionPath, collectionName + ".bloomCollection")
                );
                Assert.That(settings2.Language1Tag, Is.EqualTo("dmx"));
                Assert.That(settings2.Language2Tag, Is.EqualTo("en"));
                Assert.That(settings2.Language3Tag, Is.EqualTo("fr"));
                Assert.That(Directory.GetFiles(newBookFolder2).Length, Is.EqualTo(fileCount));
                Assert.That(
                    Directory.Exists(Path.Combine(newBookFolder2, "collectionFiles")),
                    Is.False
                );
            }
            finally
            {
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(bookObjectId);
            }
        }

        [Test]
        public void DownloadUrl_NoCollectionSettings_GetsDocument_AndSettings()
        {
            var id = Guid.NewGuid().ToString();
            var bookFolder = MakeBook(
                "This ངའ་ཁས་འབབ needs encoding and is quite a bit too long",
                id,
                "someone",
                CollectionSettingsReconstructorTests.GetTriLingualHtml(),
                htmName: "This ངའ་ཁས་འབབ needs encoding and is quite a bit too long.htm"
            );
            int fileCount = Directory.GetFiles(bookFolder).Length;
            _bloomLibraryBookApiClient.TestOnly_SetUserAccountInfo();

            var bookObjectId = _uploader.UploadBook_ForUnitTest(
                bookFolder,
                out var s3PrefixUploadedTo
            );
            try
            {
                Assert.That(string.IsNullOrEmpty(bookObjectId), Is.False);
                Assert.That(bookObjectId == "quiet", Is.False);
                Assert.That(string.IsNullOrEmpty(s3PrefixUploadedTo), Is.False);
                WaitUntilS3DataIsOnServer(s3PrefixUploadedTo, bookFolder);
                var dest = _workFolderPath.CombineForPath("output");
                Directory.CreateDirectory(dest);

                var newBookFolder2 = _downloader.DownloadFromOrderUrl(
                    BloomLinkArgs.kBloomUrlPrefix
                        + BloomLinkArgs.kOrderFile
                        + "="
                        + "BloomLibraryBooks-UnitTests/"
                        + HttpUtility.UrlEncode(GetParentOfS3Prefix(s3PrefixUploadedTo))
                        + "&forEdit=true",
                    dest,
                    "nonsense",
                    true
                );
                var collectionPath = Path.GetDirectoryName(newBookFolder2);
                var collectionName = Path.GetFileName(collectionPath);
                // xk is not a real language tag, so Bloom comes up with this as the language name.
                // Another test tries the happy path where we actually know a likely name for the language.
                Assert.That(
                    collectionName,
                    Does.StartWith("From Bloom Library - This ངའ་ཁས་འབབ needs")
                );
                var settings2 = new CollectionSettings(
                    Path.Combine(collectionPath, collectionName + ".bloomCollection")
                );
                Assert.That(settings2.Language1Tag, Is.EqualTo("xk"));
                Assert.That(settings2.Language2Tag, Is.EqualTo("fr"));
                Assert.That(settings2.Language3Tag, Is.EqualTo("de"));
                Assert.That(Directory.GetFiles(newBookFolder2).Length, Is.EqualTo(fileCount));
                Assert.That(
                    Directory.Exists(Path.Combine(newBookFolder2, "collectionFiles")),
                    Is.False
                );
            }
            finally
            {
                _bloomLibraryBookApiClient.TestOnly_DeleteBook(bookObjectId);
            }
        }

        [Test]
        public void SmokeTest_DownloadKnownBookFromProductionSite()
        {
            var dest = _workFolderPath.CombineForPath("output");
            Directory.CreateDirectory(dest);

            //if this fails, don't panic... maybe the book is gone. If so, just pick another one.
            var url =
                BloomLinkArgs.kBloomUrlPrefix
                + BloomLinkArgs.kOrderFile
                + "="
                + "BloomLibraryBooks/cara_ediger%40sil-lead.org%2ff0665264-4f1f-43d3-aa7e-fc832fe45dd0";
            var destBookFolder = _downloader.DownloadFromOrderUrl(url, dest, "nonsense");
            Assert.That(Directory.GetFiles(destBookFolder).Length, Is.GreaterThan(3));
        }

        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=1.0")]
        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=5.6")]
        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=6.0")]
        [TestCase(
            "bloom://localhost/order?orderFile=BloomLibraryBooks/test%40gmail.com%2f6dfa6c12-ae0c-433c-a384-35792e946eb8%2f&title=%D0%AD%D0%BC%D0%BD%D0%B5%20%D0%BA%D0%B0%D0%BD%D0%B0%D1%82%D1%8B%20%D0%B6%D0%BE%D0%BA%20%D1%83%D1%87%D0%B0%20%D0%B0%D0%BB%D0%B0%D1%82%3F&minVersion=6.0"
        )]
        public void IsThisVersionAllowedToDownload_IsAllowed_ReturnsTrue(string url)
        {
            Assert.True(BookDownload.IsThisVersionAllowedToDownloadInner(url, "6.0"));
        }

        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=6.1")]
        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=7.0")]
        [TestCase(
            "bloom://localhost/order?orderFile=BloomLibraryBooks/test%40gmail.com%2f6dfa6c12-ae0c-433c-a384-35792e946eb8%2f&title=%D0%AD%D0%BC%D0%BD%D0%B5%20%D0%BA%D0%B0%D0%BD%D0%B0%D1%82%D1%8B%20%D0%B6%D0%BE%D0%BA%20%D1%83%D1%87%D0%B0%20%D0%B0%D0%BB%D0%B0%D1%82%3F&minVersion=6.1"
        )]
        public void IsThisVersionAllowedToDownload_IsNotAllowed_ReturnsFalse(string url)
        {
            Assert.False(BookDownload.IsThisVersionAllowedToDownloadInner(url, "6.0"));
        }

        [TestCase("bloom://localhost/order?orderFile=blah")]
        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=")]
        [TestCase(
            "bloom://localhost/order?orderFile=BloomLibraryBooks/test%40gmail.com%2f6dfa6c12-ae0c-433c-a384-35792e946eb8%2f&title=%D0%AD%D0%BC%D0%BD%D0%B5%20%D0%BA%D0%B0%D0%BD%D0%B0%D1%82%D1%8B%20%D0%B6%D0%BE%D0%BA%20%D1%83%D1%87%D0%B0%20%D0%B0%D0%BB%D0%B0%D1%82%3F&minVersion="
        )]
        public void IsThisVersionAllowedToDownload_MissingParam_ReturnsTrue(string url)
        {
            Assert.True(BookDownload.IsThisVersionAllowedToDownloadInner(url, "6.0"));
        }

        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=5")]
        [TestCase("bloom://localhost/order?orderFile=blah&minVersion=abc")]
        public void IsThisVersionAllowedToDownload_InvalidParam_ReturnsTrue(string url)
        {
            // One could argue this either way.
            // Since we control both ends, we don't expect this to happen.
            Assert.True(BookDownload.IsThisVersionAllowedToDownloadInner(url, "6.0"));
        }

        [TestCase("")]
        [TestCase("x")]
        public void IsThisVersionAllowedToDownload_InvalidUrl_ReturnsTrue(string url)
        {
            // You might be able to argue for returning true or false if the url is invalid.
            // The method is coded to return true for this case so we don't display a message
            // indicating the user needs a new version if the problem is actually something else.
            // They should get other indicators when other things go badly.
            Assert.True(BookDownload.IsThisVersionAllowedToDownloadInner(url, "6.0"));
        }

        [Test]
        public void SanitizeCollectionSettingsForUpload_ShouldRedactSubscriptionCode()
        {
            using (var tempFile = TempFile.WithExtension(".bloomCollection"))
            {
                File.WriteAllText(
                    tempFile.Path,
                    @"<?xml version='1.0' encoding='utf-8'?>
<Collection version='0.2'>
  <Language1Tag>en</Language1Tag>
<BrandingProjectName>foo-bar</BrandingProjectName>
  <SubscriptionCode>foo-bar-123456-1234</SubscriptionCode>
  <Language2Tag>es</Language2Tag>
</Collection>"
                );

                var doc = BookUpload.SanitizeCollectionSettingsForUpload(tempFile.Path);
                var subscriptionNode = doc.SelectSingleNode("/Collection/SubscriptionCode");
                Assert.That(subscriptionNode.InnerText, Is.EqualTo("foo-bar-***-***"));

                // also, don't include the unused "BrandingProjectName" anymore
                var brandingNode = doc.SelectSingleNode("/Collection/BrandingProjectName");
                Assert.That(
                    brandingNode,
                    Is.Null,
                    "BrandingProjectName should not be included in upload"
                );
            }
        }

        [Test]
        public void SanitizeCollectionSettingsForUpload_ShouldRemoveAiLanguages()
        {
            using (var tempFile = TempFile.WithExtension(".bloomCollection"))
            {
                File.WriteAllText(
                    tempFile.Path,
                    @"<?xml version='1.0' encoding='utf-8'?>
<Collection version='0.2'>
  <Languages>
    <Language><languageiso639code>en</languageiso639code></Language>
    <Language><languageiso639code>fr-x-ai</languageiso639code></Language>
    <Language><languageiso639code>es</languageiso639code></Language>
  </Languages>
</Collection>"
                );

                var doc = BookUpload.SanitizeCollectionSettingsForUpload(tempFile.Path);
                var languages = doc.SafeSelectNodes("/Collection/Languages/Language");
                Assert.That(languages.Count, Is.EqualTo(2));
                var langCodes = languages
                    .Select(l => l.SelectSingleNode("languageiso639code").InnerText)
                    .ToList();
                Assert.That(langCodes, Is.EquivalentTo(new[] { "en", "es" }));
            }
        }

        // Wait (up to three seconds) for data uploaded to become available.
        // I have no idea whether 3s is an adequate time to wait for 'eventual consistency'. So far it seems to work.
        internal void WaitUntilS3DataIsOnServer(string s3Prefix, string bookPath)
        {
            // There's a few files we don't upload, but meta.bak is the only one that regularly messes up the count.
            // Some tests also deliberately include a _broken_ file to check they aren't uploaded,
            // so we'd better not wait for that to be there, either.
            var count = Directory
                .GetFiles(bookPath)
                .Count(
                    p => !p.EndsWith(".bak") && !p.Contains(BookStorage.PrefixForCorruptHtmFiles)
                );
            for (int i = 0; i < 30; i++)
            {
                var uploaded = new BloomS3ClientTestDouble(
                    BloomS3Client.UnitTestBucketName
                ).GetBookFileCountForUnitTest(s3Prefix);
                if (uploaded >= count)
                    return;
                Thread.Sleep(100);
            }
            throw new ApplicationException("S3 is very slow today");
        }

        private string GetParentOfS3Prefix(string prefix)
        {
            // Get everything up to and including the penultimate /
            int lastSlashIndex = prefix.LastIndexOf('/');
            int secondLastSlashIndex = prefix.Substring(0, lastSlashIndex).LastIndexOf('/');
            return prefix.Substring(0, secondLastSlashIndex + 1);
        }
    }
}
