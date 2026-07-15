using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Where and how to reach one S3 transaction's objects: the bucket/region/key-prefix plus the
    /// scoped, time-limited credentials an edge function handed out (checkin-start's write-scoped
    /// creds, or download-start's read-scoped ones) — CONTRACTS.md's `s3` response object.
    /// </summary>
    public class CloudS3Location
    {
        public string Bucket;
        public string Region;

        /// <summary>Key prefix for this book/collection, e.g.
        /// `tc/{collectionId}/books/{bookInstanceId}/` — CONTRACTS.md S3 layout. Every relative path
        /// this class transfers is appended to this verbatim.</summary>
        public string Prefix;
        public string AccessKeyId;
        public string SecretAccessKey;
        public string SessionToken;
    }

    /// <summary>Byte-level progress for one file within a multi-file transfer.</summary>
    public class CloudTransferProgress
    {
        public string RelativePath;
        public long BytesTransferred;
        public long TotalBytes;
    }

    /// <summary>
    /// One file to download, pinned to an exact S3 object version. CONTRACTS.md hard invariant:
    /// "Reads are ALWAYS by (path, s3VersionId) from the committed manifest — never 'latest'" —
    /// <see cref="CloudBookTransfer"/> enforces this by construction (see its class doc).
    /// </summary>
    public class PinnedFileDownload
    {
        public string RelativePath;
        public string S3VersionId;
        public string ExpectedSha256Hex;
        public long ExpectedSize;
    }

    /// <summary>Thrown when an upload or download can't complete after retrying. Carries the paths
    /// that failed so a caller can report/retry them specifically.</summary>
    public class CloudBookTransferException : ApplicationException
    {
        public IReadOnlyList<string> FailedPaths { get; }

        public CloudBookTransferException(
            string message,
            IReadOnlyList<string> failedPaths,
            Exception innerException = null
        )
            : base(message, innerException)
        {
            FailedPaths = failedPaths ?? Array.Empty<string>();
        }
    }

    public class CloudUploadResult
    {
        /// <summary>Paths actually PUT to S3 this call.</summary>
        public List<string> UploadedPaths { get; } = new List<string>();

        /// <summary>Paths NOT uploaded because their content already matches what's already
        /// committed under that path (hash-skip), or was already uploaded earlier in a resumed
        /// transaction.</summary>
        public List<string> SkippedPaths { get; } = new List<string>();
    }

    public class CloudDownloadResult
    {
        /// <summary>Paths actually fetched from S3 and swapped into the destination folder.</summary>
        public List<string> DownloadedPaths { get; } = new List<string>();

        /// <summary>Paths NOT downloaded because the destination already has exactly the pinned
        /// content (hash-skip).</summary>
        public List<string> SkippedPaths { get; } = new List<string>();
    }

    /// <summary>
    /// Uploads and downloads Cloud Team Collection book content, per CONTRACTS.md's S3 layout and
    /// the design doc's Send/Receive flows. Reuses <see cref="BloomS3Client"/>'s session-credential
    /// client construction (<see cref="BloomS3Client.CreateAmazonS3Client(Amazon.S3.AmazonS3Config,
    /// Amazon.S3.AmazonS3Credentials)"/>, extracted to `internal static` for exactly this reuse)
    /// rather than duplicating it; talks to <see cref="Amazon.S3.IAmazonS3"/> directly (PutObject/
    /// GetObject with an explicit sha256 checksum) rather than through
    /// <see cref="Amazon.S3.Transfer.TransferUtility"/>, since that gives this class the per-file
    /// checksum verification, byte-progress reporting, and mockability (via the constructor's
    /// client-factory seam) the acceptance tests need, without touching the publish path at all.
    ///
    /// Hard invariant (CONTRACTS.md): downloads are ALWAYS by pinned (path, s3VersionId), never
    /// "latest". <see cref="DownloadFiles"/> is the only place in this class (indeed, in the whole
    /// Cloud client) that constructs a <see cref="GetObjectRequest"/>, and it throws rather than
    /// issuing a request if a <see cref="PinnedFileDownload"/> lacks a version id — see
    /// <see cref="DownloadOneFileWithRetry"/>.
    /// </summary>
    public class CloudBookTransfer
    {
        /// <summary>Attempts per file before giving up and throwing.</summary>
        public const int MaxAttemptsPerFile = 3;

        private readonly Func<CloudS3Location, IAmazonS3> _clientFactory;

        public CloudBookTransfer()
            : this(BuildDefaultClient) { }

        /// <summary>Test-only seam: lets unit tests substitute a fake/mocked <see cref="IAmazonS3"/>
        /// instead of one built from real credentials.</summary>
        internal CloudBookTransfer(Func<CloudS3Location, IAmazonS3> clientFactory)
        {
            _clientFactory = clientFactory;
        }

        private static IAmazonS3 BuildDefaultClient(CloudS3Location location)
        {
            var env = CloudEnvironment.Current;
            var config = new AmazonS3Config
            {
                ServiceURL = env.S3Endpoint,
                ForcePathStyle = env.S3ForcePathStyle,
                AuthenticationRegion = location.Region,
                // AWSSDK v4 defaults both of these to WHEN_SUPPORTED, which makes the SDK add its
                // own CRC32/CRC64 request checksum (and validate a response checksum) on every
                // supported operation. This endpoint is always MinIO (dev/sandbox) or another
                // S3-compatible store (CloudEnvironment.S3Endpoint is never empty), and older/
                // differently-configured S3-compatible servers don't support the newer checksum
                // trailer format the SDK sends by default -- forcing WHEN_REQUIRED restores the
                // pre-v4 behavior (only compute/validate a checksum when the operation truly
                // requires one) and keeps this class's own explicit x-amz-checksum-sha256 header
                // (below, in UploadOneFileWithRetry) as the only checksum actually sent.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };
            var credentials = new AmazonS3Credentials
            {
                AccessKey = location.AccessKeyId,
                SecretAccessKey = location.SecretAccessKey,
                SessionToken = location.SessionToken,
            };
            return BloomS3Client.CreateAmazonS3Client(config, credentials);
        }

        /// <summary>
        /// Uploads the given candidate paths (typically checkin-start's authoritative `changedPaths`)
        /// from <paramref name="bookFolderPath"/>, skipping any whose current on-disk content exactly
        /// matches <paramref name="previousCommittedManifest"/> (hash-skip — belt-and-suspenders
        /// against re-sending unchanged bytes even if a candidate path turns out identical) or that
        /// are already recorded in <paramref name="alreadyUploadedThisTransaction"/> (resume support:
        /// a caller retrying an interrupted Send passes back what a prior attempt already finished,
        /// updated in place as this call succeeds, so a second retry needs even less work). Runs PUTs
        /// in parallel up to <paramref name="maxDegreeOfParallelism"/>, each carrying an explicit
        /// `x-amz-checksum-sha256` header (CONTRACTS.md: "Uploads carry x-amz-checksum-sha256").
        /// </summary>
        public CloudUploadResult UploadChangedFiles(
            CloudS3Location location,
            string bookFolderPath,
            IEnumerable<string> changedRelativePaths,
            BookVersionManifest previousCommittedManifest,
            ISet<string> alreadyUploadedThisTransaction,
            int maxDegreeOfParallelism,
            IProgress<CloudTransferProgress> progress,
            CancellationToken cancellationToken
        )
        {
            var client = _clientFactory(location);
            var result = new CloudUploadResult();
            var resultGate = new object();

            var candidatePaths = changedRelativePaths
                .Select(BookVersionManifest.NormalizePath)
                .Distinct()
                .ToList();

            try
            {
                Parallel.ForEach(
                    candidatePaths,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism),
                        CancellationToken = cancellationToken,
                    },
                    relativePath =>
                    {
                        // alreadyUploadedThisTransaction is a plain set shared across the retry
                        // calls of one transaction; reads AND writes of it happen on multiple
                        // Parallel.ForEach threads, so every access must go through resultGate
                        // (the same lock guarding the result sets) -- an unsynchronized Add here
                        // could corrupt the set or lose entries.
                        bool alreadyUploaded;
                        lock (resultGate)
                            alreadyUploaded =
                                alreadyUploadedThisTransaction != null
                                && alreadyUploadedThisTransaction.Contains(relativePath);
                        if (alreadyUploaded)
                        {
                            lock (resultGate)
                                result.SkippedPaths.Add(relativePath);
                            return;
                        }

                        var localFilePath = ToLocalPath(bookFolderPath, relativePath);
                        var (sha256, size) = BookVersionManifest.ComputeFileHash(localFilePath);

                        if (
                            previousCommittedManifest != null
                            && previousCommittedManifest.Entries.TryGetValue(
                                relativePath,
                                out var previousEntry
                            )
                            && previousEntry.Sha256 == sha256
                            && previousEntry.Size == size
                        )
                        {
                            lock (resultGate)
                                result.SkippedPaths.Add(relativePath);
                            return;
                        }

                        UploadOneFileWithRetry(
                            client,
                            location,
                            relativePath,
                            localFilePath,
                            sha256,
                            size,
                            progress,
                            cancellationToken
                        );

                        lock (resultGate)
                        {
                            result.UploadedPaths.Add(relativePath);
                            alreadyUploadedThisTransaction?.Add(relativePath);
                        }
                    }
                );
            }
            catch (AggregateException aggregate)
            {
                throw Unwrap(aggregate);
            }

            return result;
        }

        private void UploadOneFileWithRetry(
            IAmazonS3 client,
            CloudS3Location location,
            string relativePath,
            string localFilePath,
            string initialSha256Hex,
            long initialSize,
            IProgress<CloudTransferProgress> progress,
            CancellationToken cancellationToken
        )
        {
            Exception lastError = null;
            for (var attempt = 1; attempt <= MaxAttemptsPerFile; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Re-hash on every retry: if the previous attempt failed because the bytes we
                    // sent didn't match the checksum we claimed (corruption in transit, or the file
                    // changed under us), we must send what's actually on disk NOW, not stale
                    // hash/bytes from a failed attempt.
                    var (sha256Hex, size) =
                        attempt == 1
                            ? (initialSha256Hex, initialSize)
                            : BookVersionManifest.ComputeFileHash(localFilePath);

                    using (
                        var stream = RobustIO.GetFileStream(
                            localFilePath,
                            FileMode.Open,
                            FileAccess.Read
                        )
                    )
                    {
                        var request = new PutObjectRequest
                        {
                            BucketName = location.Bucket,
                            Key = location.Prefix + relativePath,
                            InputStream = stream,
                        };
                        // The checksum is set as a plain request header rather than via the SDK's
                        // native ChecksumSHA256/ChecksumAlgorithm request properties. This dates
                        // from when the project pinned an AWSSDK.S3 that predated those properties;
                        // now that we're on v4 they exist, but the header form is kept deliberately:
                        // it is live-verified against the local MinIO dev stack (task 04) — S3/MinIO
                        // store and correctly return it via GetObjectAttributes/HeadObject
                        // (ChecksumMode: ENABLED) exactly as if the property had set it, which is
                        // what supabase/functions/_shared/s3.ts's verifyUploadedObject reads back at
                        // checkin-finish — and switching to the property would also flip the SDK
                        // into its trailing-checksum/chunked-encoding path, an unnecessary behavior
                        // change for S3-compatible endpoints (see the WHEN_REQUIRED config in
                        // BuildDefaultClient above).
                        request.Headers["x-amz-checksum-sha256"] = HexToBase64(sha256Hex);
                        client.PutObjectAsync(request, cancellationToken).GetAwaiter().GetResult();
                    }

                    progress?.Report(
                        new CloudTransferProgress
                        {
                            RelativePath = relativePath,
                            BytesTransferred = size,
                            TotalBytes = size,
                        }
                    );
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Covers both a transport-level failure and S3 rejecting the PUT because the
                    // checksum we declared didn't match what it received.
                    lastError = ex;
                    Logger.WriteEvent(
                        $"CloudBookTransfer: upload attempt {attempt} of '{relativePath}' failed: {ex.Message}"
                    );
                }
            }

            throw new CloudBookTransferException(
                $"Failed to upload '{relativePath}' after {MaxAttemptsPerFile} attempts.",
                new[] { relativePath },
                lastError
            );
        }

        /// <summary>
        /// Downloads <paramref name="files"/> (each pinned to an exact S3 object version) into
        /// <paramref name="destinationFolderPath"/>, skipping any whose destination copy already has
        /// the pinned content (hash-skip — also what makes an interrupted-and-retried download
        /// naturally resume where it left off, with no separate bookkeeping needed). Every file is
        /// first downloaded into a private staging folder and verified against
        /// <see cref="PinnedFileDownload.ExpectedSha256Hex"/>/<see cref="PinnedFileDownload.ExpectedSize"/>;
        /// ONLY once every requested file has staged successfully are they moved into
        /// <paramref name="destinationFolderPath"/> (CONTRACTS.md: "download changed files ... into
        /// temp → atomic swap per book"). If anything fails after retries, the staging folder is
        /// discarded and <paramref name="destinationFolderPath"/> is left byte-for-byte as it was —
        /// nothing is ever partially written there.
        /// </summary>
        public CloudDownloadResult DownloadFiles(
            CloudS3Location location,
            IEnumerable<PinnedFileDownload> files,
            string destinationFolderPath,
            int maxDegreeOfParallelism,
            IProgress<CloudTransferProgress> progress,
            CancellationToken cancellationToken
        )
        {
            var client = _clientFactory(location);
            var result = new CloudDownloadResult();
            var toDownload = new List<PinnedFileDownload>();

            foreach (var file in files)
            {
                var normalizedPath = BookVersionManifest.NormalizePath(file.RelativePath);
                var destPath = ToLocalPath(destinationFolderPath, normalizedPath);
                if (RobustFile.Exists(destPath))
                {
                    var (sha256, size) = BookVersionManifest.ComputeFileHash(destPath);
                    if (sha256 == file.ExpectedSha256Hex && size == file.ExpectedSize)
                    {
                        result.SkippedPaths.Add(normalizedPath);
                        continue;
                    }
                }
                toDownload.Add(
                    new PinnedFileDownload
                    {
                        RelativePath = normalizedPath,
                        S3VersionId = file.S3VersionId,
                        ExpectedSha256Hex = file.ExpectedSha256Hex,
                        ExpectedSize = file.ExpectedSize,
                    }
                );
            }

            if (toDownload.Count == 0)
                return result;

            // The staging folder name must be UNIQUE PER CALL: TemporaryFolder("fixed-name") is a
            // fixed %TEMP% path shared by every download in every Bloom process, and its Dispose
            // deletes the whole folder -- so two concurrent downloads (two Bloom instances on one
            // machine, e.g. a shared-machine Team Collection or the two-instance E2E scenarios)
            // clobbered each other mid-copy: e2e-4 failed with "Could not find file ...\
            // BloomCloudTCDownload\<book>.htm" when the other instance's download completed first
            // and swept the folder away (10 Jul 2026).
            using (
                var staging = new TemporaryFolder(
                    "BloomCloudTCDownload-"
                        + Path.GetFileNameWithoutExtension(Path.GetRandomFileName())
                )
            )
            {
                try
                {
                    Parallel.ForEach(
                        toDownload,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism),
                            CancellationToken = cancellationToken,
                        },
                        file =>
                        {
                            var stagedPath = ToLocalPath(staging.FolderPath, file.RelativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));
                            DownloadOneFileWithRetry(
                                client,
                                location,
                                file,
                                stagedPath,
                                progress,
                                cancellationToken
                            );
                        }
                    );
                }
                catch (AggregateException aggregate)
                {
                    // Nothing below has touched destinationFolderPath yet — the staging TemporaryFolder
                    // is deleted by its Dispose() as this using block unwinds, so an interrupted
                    // download leaves no trace anywhere.
                    throw Unwrap(aggregate);
                }

                // Every file staged and verified successfully — only now do we touch the real
                // destination, and only with files we know are byte-correct.
                foreach (var file in toDownload)
                {
                    var stagedPath = ToLocalPath(staging.FolderPath, file.RelativePath);
                    var destPath = ToLocalPath(destinationFolderPath, file.RelativePath);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    if (RobustFile.Exists(destPath))
                        RobustFile.Delete(destPath);
                    RobustFile.Move(stagedPath, destPath);
                    result.DownloadedPaths.Add(file.RelativePath);
                }
            }

            return result;
        }

        private void DownloadOneFileWithRetry(
            IAmazonS3 client,
            CloudS3Location location,
            PinnedFileDownload file,
            string stagedPath,
            IProgress<CloudTransferProgress> progress,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrEmpty(file.S3VersionId))
                throw new ArgumentException(
                    $"Pinned download for '{file.RelativePath}' is missing an S3 version id — "
                        + "CONTRACTS.md requires downloads to always be by pinned (path, s3VersionId), never 'latest'.",
                    nameof(file)
                );

            Exception lastError = null;
            for (var attempt = 1; attempt <= MaxAttemptsPerFile; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // The one and only place a GetObjectRequest is built in this class: always
                    // carries VersionId, per the hard invariant in the class doc comment.
                    var request = new GetObjectRequest
                    {
                        BucketName = location.Bucket,
                        Key = location.Prefix + file.RelativePath,
                        VersionId = file.S3VersionId,
                    };

                    using (
                        var response = client
                            .GetObjectAsync(request, cancellationToken)
                            .GetAwaiter()
                            .GetResult()
                    )
                    using (var fileStream = RobustIO.GetFileStream(stagedPath, FileMode.Create))
                    {
                        response.ResponseStream.CopyTo(fileStream);
                    }

                    var (actualSha256, actualSize) = BookVersionManifest.ComputeFileHash(
                        stagedPath
                    );
                    if (actualSha256 != file.ExpectedSha256Hex || actualSize != file.ExpectedSize)
                    {
                        throw new IOException(
                            $"Checksum mismatch downloading '{file.RelativePath}': "
                                + $"expected sha256={file.ExpectedSha256Hex} size={file.ExpectedSize}, "
                                + $"got sha256={actualSha256} size={actualSize}."
                        );
                    }

                    progress?.Report(
                        new CloudTransferProgress
                        {
                            RelativePath = file.RelativePath,
                            BytesTransferred = actualSize,
                            TotalBytes = actualSize,
                        }
                    );
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (RobustFile.Exists(stagedPath))
                        RobustFile.Delete(stagedPath);
                    Logger.WriteEvent(
                        $"CloudBookTransfer: download attempt {attempt} of '{file.RelativePath}' failed: {ex.Message}"
                    );
                }
            }

            throw new CloudBookTransferException(
                $"Failed to download '{file.RelativePath}' after {MaxAttemptsPerFile} attempts.",
                new[] { file.RelativePath },
                lastError
            );
        }

        private static string ToLocalPath(string baseFolder, string relativePath) =>
            Path.Combine(baseFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>S3's `x-amz-checksum-sha256` is base64; this manifest's convention is lower-case
        /// hex (see <see cref="BookVersionManifestEntry.Sha256"/>) — convert at the point of use.</summary>
        private static string HexToBase64(string hex) =>
            Convert.ToBase64String(Convert.FromHexString(hex));

        private static Exception Unwrap(AggregateException aggregate)
        {
            var flattened = aggregate.Flatten();
            return flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : flattened;
        }
    }
}
