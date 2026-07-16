using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection.Cloud
{
    [TestFixture]
    public class CloudBookTransferTests
    {
        private TemporaryFolder _bookFolder;
        private string _bookFolderPath;
        private TemporaryFolder _destFolder;
        private string _destFolderPath;
        private Mock<IAmazonS3> _s3Mock;
        private CloudBookTransfer _transfer;
        private CloudS3Location _location;

        [SetUp]
        public void SetUp()
        {
            _bookFolder = new TemporaryFolder("CloudBookTransferTests_Book");
            _bookFolderPath = _bookFolder.FolderPath;
            _destFolder = new TemporaryFolder("CloudBookTransferTests_Dest");
            _destFolderPath = _destFolder.FolderPath;
            _s3Mock = new Mock<IAmazonS3>();
            _transfer = new CloudBookTransfer(_ => _s3Mock.Object);
            _location = new CloudS3Location
            {
                Bucket = "test-bucket",
                Region = "us-east-1",
                Prefix = "tc/collection-1/books/instance-1/",
                AccessKeyId = "AK",
                SecretAccessKey = "SK",
                SessionToken = "ST",
            };
        }

        [TearDown]
        public void TearDown()
        {
            _bookFolder.Dispose();
            _destFolder.Dispose();
        }

        private string WriteBookFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(_bookFolderPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            RobustFile.WriteAllText(fullPath, content);
            return fullPath;
        }

        private string WriteDestFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(_destFolderPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            RobustFile.WriteAllText(fullPath, content);
            return fullPath;
        }

        private static PinnedFileDownload MakePinnedFile(
            string relativePath,
            string content,
            string versionId
        )
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new PinnedFileDownload
            {
                RelativePath = relativePath,
                S3VersionId = versionId,
                ExpectedSha256Hex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ExpectedSize = bytes.Length,
            };
        }

        /// <summary>Configures the mock so GetObjectAsync serves back whatever content was written to
        /// the matching local book file (keyed off the object key's trailing path segment) — used by
        /// tests where the actual returned bytes don't matter beyond round-tripping correctly.</summary>
        private void SetupGetObjectToReturnContent(Dictionary<string, string> contentByRelativePath)
        {
            _s3Mock
                .Setup(s =>
                    s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .Returns<GetObjectRequest, CancellationToken>(
                    (req, ct) =>
                    {
                        var relativePath = req.Key.Substring(_location.Prefix.Length);
                        var bytes = Encoding.UTF8.GetBytes(contentByRelativePath[relativePath]);
                        return Task.FromResult(
                            new GetObjectResponse
                            {
                                ResponseStream = new MemoryStream(bytes),
                                HttpStatusCode = HttpStatusCode.OK,
                                VersionId = req.VersionId,
                            }
                        );
                    }
                );
        }

        // ------------------------------------------------------------------
        // Upload: hash-skip
        // ------------------------------------------------------------------

        [Test]
        public void UploadChangedFiles_SkipsFileMatchingPreviouslyCommittedManifest()
        {
            var localPath = WriteBookFile("book.htm", "same content");
            var (sha256, size) = BookVersionManifest.ComputeFileHash(localPath);
            var previousManifest = new BookVersionManifest(
                new Dictionary<string, BookVersionManifestEntry>
                {
                    ["book.htm"] = new BookVersionManifestEntry(sha256, size, "v-old"),
                }
            );

            var result = _transfer.UploadChangedFiles(
                _location,
                _bookFolderPath,
                new[] { "book.htm" },
                previousManifest,
                null,
                2,
                null,
                CancellationToken.None
            );

            Assert.That(result.SkippedPaths, Has.Member("book.htm"));
            Assert.That(result.UploadedPaths, Is.Empty);
            _s3Mock.Verify(
                s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "identical content under an unchanged path must never be re-uploaded"
            );
        }

        [Test]
        public void UploadChangedFiles_UsesLocalManifestHashInsteadOfReHashing()
        {
            // The whole point of the localManifest parameter (E4): when the caller has already
            // hashed the current on-disk content, UploadChangedFiles must use THAT hash rather
            // than hashing the file a second time. We prove it by handing in a localManifest whose
            // hash equals the previous commit while the real bytes on disk are different: a
            // re-hashing implementation would see the changed bytes and upload; the hash-reuse
            // path trusts the manifest, matches the previous commit, and skips.
            WriteBookFile("book.htm", "the actual on-disk content");
            var (realSha, _) = BookVersionManifest.ComputeFileHash(
                Path.Combine(_bookFolderPath, "book.htm")
            );
            const string staleSha =
                "0000000000000000000000000000000000000000000000000000000000000000";
            const long staleSize = 5;
            Assert.That(
                realSha,
                Is.Not.EqualTo(staleSha),
                "test setup: the stale manifest hash must differ from the real file's hash"
            );
            var manifest = new BookVersionManifest(
                new Dictionary<string, BookVersionManifestEntry>
                {
                    ["book.htm"] = new BookVersionManifestEntry(staleSha, staleSize, "v-old"),
                }
            );

            var result = _transfer.UploadChangedFiles(
                _location,
                _bookFolderPath,
                new[] { "book.htm" },
                manifest, // previousCommittedManifest
                manifest, // localManifest — claims the file still matches the old commit
                2,
                null,
                CancellationToken.None
            );

            Assert.That(
                result.SkippedPaths,
                Has.Member("book.htm"),
                "the caller-supplied localManifest hash (== previous) must be trusted over the changed on-disk bytes"
            );
            Assert.That(result.UploadedPaths, Is.Empty);
            _s3Mock.Verify(
                s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "no PUT when the localManifest says the content is unchanged (no second hash of the file)"
            );
        }

        [Test]
        public void UploadChangedFiles_UploadsChangedFile_WithChecksumHeaderAndCorrectKey()
        {
            WriteBookFile("book.htm", "changed content");
            var capturedRequests = new List<PutObjectRequest>();
            _s3Mock
                .Setup(s =>
                    s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .Callback<PutObjectRequest, CancellationToken>(
                    (req, ct) => capturedRequests.Add(req)
                )
                .ReturnsAsync(
                    new PutObjectResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        VersionId = "v-new",
                    }
                );

            var result = _transfer.UploadChangedFiles(
                _location,
                _bookFolderPath,
                new[] { "book.htm" },
                null,
                null,
                2,
                null,
                CancellationToken.None
            );

            Assert.That(result.UploadedPaths, Has.Member("book.htm"));
            Assert.That(
                capturedRequests,
                Has.Count.EqualTo(1),
                "sanity check: exactly one PUT happened"
            );
            Assert.That(
                capturedRequests[0].Key,
                Is.EqualTo("tc/collection-1/books/instance-1/book.htm")
            );
            Assert.That(
                capturedRequests[0].Headers["x-amz-checksum-sha256"],
                Is.Not.Null.And.Not.Empty,
                "CONTRACTS.md: uploads carry x-amz-checksum-sha256"
            );
        }

        // ------------------------------------------------------------------
        // Upload: checksum-mismatch / transient-failure retry
        // ------------------------------------------------------------------

        [Test]
        public void UploadChangedFiles_TransientFailure_RetriesThenSucceeds()
        {
            WriteBookFile("book.htm", "content");
            var callCount = 0;
            _s3Mock
                .Setup(s =>
                    s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .Returns<PutObjectRequest, CancellationToken>(
                    (req, ct) =>
                    {
                        callCount++;
                        if (callCount == 1)
                            throw new AmazonS3Exception("simulated checksum mismatch");
                        return Task.FromResult(
                            new PutObjectResponse
                            {
                                HttpStatusCode = HttpStatusCode.OK,
                                VersionId = "v",
                            }
                        );
                    }
                );

            var result = _transfer.UploadChangedFiles(
                _location,
                _bookFolderPath,
                new[] { "book.htm" },
                null,
                null,
                1,
                null,
                CancellationToken.None
            );

            Assert.That(
                callCount,
                Is.EqualTo(2),
                "must retry exactly once after the simulated failure"
            );
            Assert.That(result.UploadedPaths, Has.Member("book.htm"));
        }

        [Test]
        public void UploadChangedFiles_AllAttemptsFail_ThrowsWithFailedPathAfterMaxAttempts()
        {
            WriteBookFile("book.htm", "content");
            _s3Mock
                .Setup(s =>
                    s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .ThrowsAsync(new AmazonS3Exception("boom"));

            var ex = Assert.Throws<CloudBookTransferException>(() =>
                _transfer.UploadChangedFiles(
                    _location,
                    _bookFolderPath,
                    new[] { "book.htm" },
                    null,
                    null,
                    1,
                    null,
                    CancellationToken.None
                )
            );

            Assert.That(ex.FailedPaths, Has.Member("book.htm"));
            _s3Mock.Verify(
                s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(CloudBookTransfer.MaxAttemptsPerFile)
            );
        }

        // ------------------------------------------------------------------
        // Download: pinned-version invariant
        // ------------------------------------------------------------------

        [Test]
        public void DownloadFiles_PinnedFileMissingVersionId_NeverCallsS3()
        {
            var files = new[]
            {
                new PinnedFileDownload
                {
                    RelativePath = "book.htm",
                    S3VersionId = null,
                    ExpectedSha256Hex = "abc",
                    ExpectedSize = 1,
                },
            };

            Assert.Throws<ArgumentException>(() =>
                _transfer.DownloadFiles(
                    _location,
                    files,
                    _destFolderPath,
                    1,
                    null,
                    CancellationToken.None
                )
            );

            _s3Mock.Verify(
                s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "a pinned download with no version id must never reach S3 at all"
            );
        }

        [Test]
        public void DownloadFiles_EveryGetObjectRequest_CarriesTheExactPinnedVersionId()
        {
            var fileA = MakePinnedFile("a.txt", "AAAA", "v-a");
            var fileB = MakePinnedFile("b.txt", "BBBB", "v-b");
            var seenRequests = new List<GetObjectRequest>();
            var gate = new object();
            _s3Mock
                .Setup(s =>
                    s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .Returns<GetObjectRequest, CancellationToken>(
                    (req, ct) =>
                    {
                        lock (gate)
                            seenRequests.Add(req);
                        var content = req.Key.EndsWith("a.txt") ? "AAAA" : "BBBB";
                        return Task.FromResult(
                            new GetObjectResponse
                            {
                                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                                HttpStatusCode = HttpStatusCode.OK,
                                VersionId = req.VersionId,
                            }
                        );
                    }
                );

            _transfer.DownloadFiles(
                _location,
                new[] { fileA, fileB },
                _destFolderPath,
                2,
                null,
                CancellationToken.None
            );

            Assert.That(
                seenRequests,
                Has.Count.EqualTo(2),
                "sanity check: both files were actually fetched"
            );
            Assert.That(
                seenRequests,
                Has.All.Matches<GetObjectRequest>(r => !string.IsNullOrEmpty(r.VersionId)),
                "no code path may issue an unversioned GET"
            );
            Assert.That(
                seenRequests.Select(r => r.VersionId),
                Is.EquivalentTo(new[] { "v-a", "v-b" })
            );
        }

        // ------------------------------------------------------------------
        // Download: hash-skip / resume
        // ------------------------------------------------------------------

        [Test]
        public void DownloadFiles_SkipsFileAlreadyCorrectAtDestination()
        {
            var destPath = WriteDestFile("book.htm", "same content");
            var (sha256, size) = BookVersionManifest.ComputeFileHash(destPath);
            var files = new[]
            {
                new PinnedFileDownload
                {
                    RelativePath = "book.htm",
                    S3VersionId = "v1",
                    ExpectedSha256Hex = sha256,
                    ExpectedSize = size,
                },
            };

            var result = _transfer.DownloadFiles(
                _location,
                files,
                _destFolderPath,
                2,
                null,
                CancellationToken.None
            );

            Assert.That(result.SkippedPaths, Has.Member("book.htm"));
            _s3Mock.Verify(
                s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Never
            );
        }

        [Test]
        public void DownloadFiles_ResumeAfterPartialSuccess_SkipsAlreadyCorrectFilesOnRetry()
        {
            var fileA = MakePinnedFile("a.txt", "AAAA", "v-a");
            var fileB = MakePinnedFile("b.txt", "BBBB", "v-b");
            SetupGetObjectToReturnContent(
                new Dictionary<string, string> { ["a.txt"] = "AAAA", ["b.txt"] = "BBBB" }
            );

            var firstAttempt = _transfer.DownloadFiles(
                _location,
                new[] { fileA, fileB },
                _destFolderPath,
                2,
                null,
                CancellationToken.None
            );
            Assert.That(
                firstAttempt.DownloadedPaths,
                Is.EquivalentTo(new[] { "a.txt", "b.txt" }),
                "sanity check on the first, uninterrupted attempt"
            );
            _s3Mock.Invocations.Clear();

            var resumedAttempt = _transfer.DownloadFiles(
                _location,
                new[] { fileA, fileB },
                _destFolderPath,
                2,
                null,
                CancellationToken.None
            );

            Assert.That(resumedAttempt.SkippedPaths, Is.EquivalentTo(new[] { "a.txt", "b.txt" }));
            _s3Mock.Verify(
                s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "already-correct files from a prior attempt must not be re-fetched on resume"
            );
        }

        // ------------------------------------------------------------------
        // Download: checksum-mismatch retry
        // ------------------------------------------------------------------

        [Test]
        public void DownloadFiles_ChecksumMismatch_RetriesThenSucceeds()
        {
            var goodBytes = Encoding.UTF8.GetBytes("correct bytes");
            var badBytes = Encoding.UTF8.GetBytes("WRONG bytes!!");
            var expectedSha256 = Convert.ToHexString(SHA256.HashData(goodBytes)).ToLowerInvariant();
            var callCount = 0;

            _s3Mock
                .Setup(s =>
                    s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .Returns<GetObjectRequest, CancellationToken>(
                    (req, ct) =>
                    {
                        callCount++;
                        var bytes = callCount == 1 ? badBytes : goodBytes;
                        return Task.FromResult(
                            new GetObjectResponse
                            {
                                ResponseStream = new MemoryStream(bytes),
                                HttpStatusCode = HttpStatusCode.OK,
                                VersionId = req.VersionId,
                            }
                        );
                    }
                );

            var files = new[]
            {
                new PinnedFileDownload
                {
                    RelativePath = "book.htm",
                    S3VersionId = "v1",
                    ExpectedSha256Hex = expectedSha256,
                    ExpectedSize = goodBytes.Length,
                },
            };

            var result = _transfer.DownloadFiles(
                _location,
                files,
                _destFolderPath,
                1,
                null,
                CancellationToken.None
            );

            Assert.That(
                callCount,
                Is.EqualTo(2),
                "must retry after the first attempt's checksum mismatch"
            );
            Assert.That(result.DownloadedPaths, Has.Member("book.htm"));
            Assert.That(
                RobustFile.ReadAllBytes(Path.Combine(_destFolderPath, "book.htm")),
                Is.EqualTo(goodBytes)
            );
        }

        // ------------------------------------------------------------------
        // Download: interrupted transfer leaves the destination untouched
        // ------------------------------------------------------------------

        [Test]
        public void DownloadFiles_OneFileFailsAfterRetries_LeavesDestinationFolderCompletelyUntouched()
        {
            // A pre-existing file proves the destination folder is untouched, not merely that the
            // two requested files are absent from it.
            WriteDestFile("sentinel.txt", "pre-existing");
            var goodBytes = Encoding.UTF8.GetBytes("good");
            var goodSha256 = Convert.ToHexString(SHA256.HashData(goodBytes)).ToLowerInvariant();

            _s3Mock
                .Setup(s =>
                    s.GetObjectAsync(
                        It.Is<GetObjectRequest>(r => r.Key.EndsWith("good.txt")),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(() =>
                    new GetObjectResponse
                    {
                        ResponseStream = new MemoryStream(goodBytes),
                        HttpStatusCode = HttpStatusCode.OK,
                        VersionId = "v-good",
                    }
                );
            _s3Mock
                .Setup(s =>
                    s.GetObjectAsync(
                        It.Is<GetObjectRequest>(r => r.Key.EndsWith("bad.txt")),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ThrowsAsync(new AmazonS3Exception("simulated failure"));

            var files = new[]
            {
                new PinnedFileDownload
                {
                    RelativePath = "good.txt",
                    S3VersionId = "v-good",
                    ExpectedSha256Hex = goodSha256,
                    ExpectedSize = goodBytes.Length,
                },
                new PinnedFileDownload
                {
                    RelativePath = "bad.txt",
                    S3VersionId = "v-bad",
                    ExpectedSha256Hex = "irrelevant-because-the-get-itself-always-throws",
                    ExpectedSize = 1,
                },
            };

            Assert.Throws<CloudBookTransferException>(() =>
                _transfer.DownloadFiles(
                    _location,
                    files,
                    _destFolderPath,
                    2,
                    null,
                    CancellationToken.None
                )
            );

            var remainingFiles = Directory
                .GetFiles(_destFolderPath, "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .ToList();
            Assert.That(
                remainingFiles,
                Is.EquivalentTo(new[] { "sentinel.txt" }),
                "neither the failed file nor the one that staged successfully may appear — the "
                    + "whole batch is all-or-nothing from the destination folder's point of view"
            );
        }
    }
}
