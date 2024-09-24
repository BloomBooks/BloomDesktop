using System.IO;
using System.Linq;
using Amazon.S3.Model;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;
using SIL.Code;

namespace BloomTests.WebLibraryIntegration
{
    /// <summary>
    /// This file now contains only edge cases. For a more efficient upload/download test,
    /// the more standard tests have been separated out into BloomS3StandardUpDownloadTests.
    /// </summary>
    [TestFixture]
    public class BloomS3ClientTests
    {
        private BloomS3Client _client;
        private BloomS3Client _clientWhichMustWorkAroundUploadPermissions;
        private TemporaryFolder _workFolder;

        [SetUp]
        public void Setup()
        {
            _workFolder = new TemporaryFolder("unittest");
            var workFolderPath = _workFolder.FolderPath;
            Assert.AreEqual(
                0,
                Directory.GetDirectories(workFolderPath).Count(),
                "Some stuff was left over from a previous test"
            );
            Assert.AreEqual(
                0,
                Directory.GetFiles(workFolderPath).Count(),
                "Some stuff was left over from a previous test"
            );

            _client = new BloomS3Client(BloomS3Client.UnitTestBucketName);
            _clientWhichMustWorkAroundUploadPermissions = new BloomS3ClientTestDouble(
                BloomS3Client.UnitTestBucketName,
                shouldWorkAroundUploadPermissions: true
            );
        }

        [TearDown]
        public void TearDown()
        {
            _workFolder.Dispose();
            _client.Dispose();
            _clientWhichMustWorkAroundUploadPermissions.Dispose();
        }

        [Test]
        public void DownloadBook_DoesNotExist_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(
                () =>
                    _client.DownloadBook(
                        BloomS3Client.UnitTestBucketName,
                        "notthere",
                        _workFolder.FolderPath
                    )
            );
        }

        [TestCase("bucket/my.pdf")]
        [TestCase("bucket/my.PDF")]
        [TestCase("bucket/thumbs.db")]
        [TestCase("bucket/Basic Book.css.map")]
        [TestCase("bucket/collectionfiles/book.uploadCollectionSettings")]
        public void AvoidThisFile_ShouldAvoid(string objectKey)
        {
            Assert.True(BloomS3Client.AvoidThisFile(objectKey, false));
        }

        [TestCase("bucket/abc.def.ghi")]
        // This is the main file for which forEdit makes a difference.
        [TestCase("bucket/collectionfiles/book.uploadCollectionSettings")]
        public void AvoidThisFile_ForEdit_ShouldNotAvoid(string objectKey)
        {
            Assert.False(BloomS3Client.AvoidThisFile(objectKey, true));
        }

        [TestCase("bucket/abc.def")]
        [TestCase("bucket/abc.def.ghi")]
        [TestCase("bucket/Basic Book.css")]
        [TestCase("bucket/music.mp3")]
        [TestCase("bucket/music.MP3")]
        [TestCase("bucket/music.wav")]
        [TestCase("bucket/music.WAV")]
        [TestCase("bucket/music.ogg")]
        [TestCase("bucket/music.OGG")]
        [TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.css")]
        // We now DO download narration audio (for now)
        [TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.mp3")]
        [TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.MP3")]
        [TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.wav")]
        [TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.WAV")]
        [TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.ogg")]
        [TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.OGG")]
        // We sometimes prepend a guid which starts with a number with "i" to make epubs happy.
        [TestCase("bucket/i1EC14CB3-CAEC-4C83-8092-74C78CA7C515.mp3")]
        public void AvoidThisFile_ShouldNotAvoid(string objectKey)
        {
            Assert.False(BloomS3Client.AvoidThisFile(objectKey, false));
            Assert.False(BloomS3Client.AvoidThisFile(objectKey, true));
        }
    }

    public class BloomS3ClientTestDouble : BloomS3Client
    {
        public BloomS3ClientTestDouble(
            string bucketName,
            bool shouldWorkAroundUploadPermissions = false
        )
            : base(bucketName)
        {
            Guard.AssertThat(bucketName == UnitTestBucketName, "This class is only for unit tests");

            // Some tests exercise the upload code which needs the temporary credentials we usually get
            // from the API but don't exercise the higher level code which actually calls the API.
            // For those cases, we use the AWS user with full permissions to the unit test bucket.
            if (shouldWorkAroundUploadPermissions)
                SetTemporaryCredentialsForBookUpload(GetAccessKeyCredentials(UnitTestBucketName));
        }

        internal int GetBookFileCountForUnitTest(string prefix)
        {
            return GetMatchingItems(UnitTestBucketName, prefix).Count;
        }

        public void DeleteFromUnitTestBucket(string prefix)
        {
            var amazonS3 = GetAmazonS3WithAccessKey(UnitTestBucketName);

            var listMatchingObjectsRequest = new ListObjectsV2Request()
            {
                BucketName = UnitTestBucketName,
                Prefix = prefix
            };

            ListObjectsV2Response matchingFilesResponse;
            do
            {
                // Note: ListObjects can only return 1,000 objects at a time,
                //       and DeleteObjects can only delete 1,000 objects at a time.
                //       So a loop is needed if the book contains 1,001+ objects.
                matchingFilesResponse = amazonS3.ListObjectsV2(listMatchingObjectsRequest);
                if (matchingFilesResponse.S3Objects.Count == 0)
                    return;

                var deleteObjectsRequest = new DeleteObjectsRequest()
                {
                    BucketName = UnitTestBucketName,
                    Objects = matchingFilesResponse.S3Objects
                        .Select(s3Object => new KeyVersion() { Key = s3Object.Key })
                        .ToList()
                };

                var response = amazonS3.DeleteObjects(deleteObjectsRequest);
                System.Diagnostics.Debug.Assert(response.DeleteErrors.Count == 0);

                // Prep the next request (if needed)
                listMatchingObjectsRequest.ContinuationToken =
                    matchingFilesResponse.NextContinuationToken;
            } while (matchingFilesResponse.IsTruncated); // Returns true if haven't reached the end yet
        }
    }
}
