// Bloom Cloud Team Collections — MinIO/S3 parity spike
//
// PURPOSE: Verify that the four S3 operations Bloom's cloud TC relies on
// work correctly against MinIO (SNSD, versioning ON) before we build the
// full client.  Run this BEFORE implementing BloomS3Client.
//
// WHAT IS TESTED:
//   (a) PUT with x-amz-checksum-sha256 (AWS SDK ChecksumAlgorithm.SHA256)
//   (b) Read the stored checksum back server-side via GetObjectAttributes
//   (c) Capture the version-id from the PUT response
//   (d) GET the object by (key, versionId) — not "get latest"
//
// HOW TO RUN:
//   1. Start MinIO: docker compose -f server/dev/docker-compose.yml up -d
//   2. dotnet run --project server/dev/parity-check/ParityCheck.csproj
//
// ENVIRONMENT (all optional — defaults match docker-compose.yml):
//   MINIO_ENDPOINT   MinIO S3 API URL         (default: http://localhost:9000)
//   MINIO_ACCESS_KEY MinIO root access key     (default: minioadmin)
//   MINIO_SECRET_KEY MinIO root secret key     (default: minioadmin)
//   MINIO_BUCKET     Bucket to test against    (default: bloom-teams-local)
//
// EXIT CODES:
//   0 — all checks passed
//   1 — one or more checks failed (fallback strategies documented in output)
//
// NOTE: This project is NOT added to Bloom.sln.  It is a standalone spike tool.

using System.Security.Cryptography;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace BloomDev.ParityCheck;

internal static class Program
{
    // -----------------------------------------------------------------------
    // Configuration — read from env vars with safe defaults.
    // -----------------------------------------------------------------------
    private static readonly string Endpoint = Env("MINIO_ENDPOINT", "http://localhost:9000");
    private static readonly string AccessKey = Env("MINIO_ACCESS_KEY", "minioadmin");
    private static readonly string SecretKey = Env("MINIO_SECRET_KEY", "minioadmin");
    private static readonly string Bucket = Env("MINIO_BUCKET", "bloom-teams-local");

    // Test object key — use a tc/-prefix path that mirrors the production S3 layout.
    private const string TestKeyPrefix = "tc/parity-check-spike/books/test-instance/";
    private static readonly string TestKey = TestKeyPrefix + $"parity-{Guid.NewGuid():N}.txt";

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------
    internal static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Bloom Cloud TC — MinIO/S3 Parity Spike ===");
        Console.WriteLine($"  Endpoint : {Endpoint}");
        Console.WriteLine($"  Bucket   : {Bucket}");
        Console.WriteLine($"  Test key : {TestKey}");
        Console.WriteLine();

        using var s3 = CreateS3Client();

        var results = new List<(string check, bool pass, string? note)>();

        // ------------------------------------------------------------------
        // (a) PUT with x-amz-checksum-sha256
        // ------------------------------------------------------------------
        string? versionId = null;
        string expectedChecksum;
        byte[] content = Encoding.UTF8.GetBytes("Hello, Bloom parity spike!\n");

        Console.WriteLine("--- Check (a): PUT with x-amz-checksum-sha256 ---");
        try
        {
            expectedChecksum = ComputeSha256Base64(content);
            Console.WriteLine($"  Computed SHA-256 (base64): {expectedChecksum}");

            using var ms = new MemoryStream(content);
            var putRequest = new PutObjectRequest
            {
                BucketName = Bucket,
                Key = TestKey,
                InputStream = ms,
                ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
                // Note: the AWS SDK automatically computes the checksum when
                // ChecksumAlgorithm is set AND the stream is provided.
                // We also set it explicitly to verify round-trip.
                ChecksumSHA256 = expectedChecksum,
            };

            var putResponse = await s3.PutObjectAsync(putRequest);
            versionId = putResponse.VersionId;

            bool aPass = putResponse.HttpStatusCode == System.Net.HttpStatusCode.OK;
            Console.WriteLine($"  HTTP status: {(int)putResponse.HttpStatusCode}");
            Console.WriteLine($"  Version-id : {versionId ?? "(null — versioning may be off?)"}");
            results.Add(("(a) PUT with x-amz-checksum-sha256", aPass, null));
        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            Console.WriteLine($"  Fallback: if MinIO rejects the checksum header, disable");
            Console.WriteLine(
                $"    ChecksumAlgorithm in dev mode and verify checksums client-side."
            );
            expectedChecksum = string.Empty;
            results.Add(
                ("(a) PUT with x-amz-checksum-sha256", false, "AmazonS3Exception: " + ex.Message)
            );
        }

        // ------------------------------------------------------------------
        // (b) Read stored checksum server-side via GetObjectAttributes
        // ------------------------------------------------------------------
        Console.WriteLine("\n--- Check (b): Read stored checksum via GetObjectAttributes ---");
        if (versionId != null)
        {
            try
            {
                var attrRequest = new GetObjectAttributesRequest
                {
                    BucketName = Bucket,
                    Key = TestKey,
                    VersionId = versionId,
                    ObjectAttributes = new List<ObjectAttributes>
                    {
                        ObjectAttributes.Checksum,
                        ObjectAttributes.ObjectSize,
                    },
                };

                var attrResponse = await s3.GetObjectAttributesAsync(attrRequest);
                string? returnedChecksum = attrResponse.Checksum?.ChecksumSHA256;

                Console.WriteLine($"  Returned checksum (SHA-256): {returnedChecksum ?? "(null)"}");
                Console.WriteLine($"  Expected checksum           : {expectedChecksum}");

                bool bPass =
                    !string.IsNullOrEmpty(returnedChecksum) && returnedChecksum == expectedChecksum;
                results.Add(
                    (
                        "(b) Read stored checksum server-side",
                        bPass,
                        bPass
                            ? null
                            : $"Expected={expectedChecksum}, Got={returnedChecksum ?? "null"}"
                    )
                );

                if (!bPass)
                {
                    Console.WriteLine("  Fallback: if MinIO does not return the checksum via");
                    Console.WriteLine(
                        "    GetObjectAttributes, Bloom can verify by downloading the"
                    );
                    Console.WriteLine("    object and computing the checksum client-side on the");
                    Console.WriteLine(
                        "    received bytes. This is slightly slower but functionally"
                    );
                    Console.WriteLine("    equivalent for correctness checking.");
                }
            }
            catch (AmazonS3Exception ex)
            {
                Console.WriteLine($"  FAIL: {ex.Message}");
                Console.WriteLine("  Fallback: verify checksums client-side (download + compute).");
                results.Add(
                    (
                        "(b) Read stored checksum server-side",
                        false,
                        "AmazonS3Exception: " + ex.Message
                    )
                );
            }
        }
        else
        {
            Console.WriteLine("  SKIP: no version-id from PUT (check (a) likely failed).");
            results.Add(("(b) Read stored checksum server-side", false, "Skipped — no versionId"));
        }

        // ------------------------------------------------------------------
        // (c) version-id captured from PUT
        // ------------------------------------------------------------------
        Console.WriteLine("\n--- Check (c): version-id captured on PUT ---");
        {
            bool cPass = !string.IsNullOrEmpty(versionId);
            Console.WriteLine(
                $"  version-id: {versionId ?? "(null)"}  => {(cPass ? "PASS" : "FAIL")}"
            );
            if (!cPass)
            {
                Console.WriteLine("  Fallback: if MinIO does not return a version-id in the PUT");
                Console.WriteLine("    response, do a HEAD request immediately after to read the");
                Console.WriteLine(
                    "    x-amz-version-id response header. If that also fails, versioning"
                );
                Console.WriteLine(
                    "    may not be enabled on the bucket (check docker-compose.yml init job)."
                );
            }
            results.Add(
                ("(c) Capture version-id on PUT", cPass, cPass ? null : "versionId was null")
            );
        }

        // ------------------------------------------------------------------
        // (d) GET by (key, versionId)
        // ------------------------------------------------------------------
        Console.WriteLine("\n--- Check (d): GET by (key, versionId) ---");
        if (!string.IsNullOrEmpty(versionId))
        {
            try
            {
                var getRequest = new GetObjectRequest
                {
                    BucketName = Bucket,
                    Key = TestKey,
                    VersionId = versionId,
                };

                using var getResponse = await s3.GetObjectAsync(getRequest);
                using var reader = new StreamReader(getResponse.ResponseStream);
                string body = await reader.ReadToEndAsync();
                byte[] receivedBytes = Encoding.UTF8.GetBytes(body);
                string receivedChecksum = ComputeSha256Base64(receivedBytes);

                Console.WriteLine($"  Retrieved version-id    : {getResponse.VersionId}");
                Console.WriteLine($"  Body length (bytes)     : {receivedBytes.Length}");
                Console.WriteLine($"  SHA-256 of received body: {receivedChecksum}");
                Console.WriteLine($"  Expected SHA-256        : {expectedChecksum}");

                bool dPass =
                    getResponse.VersionId == versionId && receivedChecksum == expectedChecksum;
                results.Add(
                    (
                        "(d) GET by (key, versionId)",
                        dPass,
                        dPass
                            ? null
                            : $"versionId match={getResponse.VersionId == versionId}, checksum match={receivedChecksum == expectedChecksum}"
                    )
                );
            }
            catch (AmazonS3Exception ex)
            {
                Console.WriteLine($"  FAIL: {ex.Message}");
                results.Add(
                    ("(d) GET by (key, versionId)", false, "AmazonS3Exception: " + ex.Message)
                );
            }
        }
        else
        {
            Console.WriteLine("  SKIP: no version-id (check (c) failed).");
            results.Add(("(d) GET by (key, versionId)", false, "Skipped — no versionId"));
        }

        // ------------------------------------------------------------------
        // Cleanup — delete the test object (all versions)
        // ------------------------------------------------------------------
        Console.WriteLine("\n--- Cleanup ---");
        try
        {
            if (!string.IsNullOrEmpty(versionId))
            {
                await s3.DeleteObjectAsync(
                    new DeleteObjectRequest
                    {
                        BucketName = Bucket,
                        Key = TestKey,
                        VersionId = versionId,
                    }
                );
                Console.WriteLine($"  Deleted test object (versionId={versionId}).");
            }
        }
        catch (AmazonS3Exception ex)
        {
            // Non-fatal: cleanup failure does not affect test results.
            Console.WriteLine($"  Cleanup warning: {ex.Message}");
        }

        // ------------------------------------------------------------------
        // Summary
        // ------------------------------------------------------------------
        Console.WriteLine("\n=== RESULTS ===");
        int passed = 0,
            failed = 0;
        foreach (var (check, pass, note) in results)
        {
            string status = pass ? "PASS" : "FAIL";
            Console.WriteLine($"  [{status}] {check}");
            if (!pass && note != null)
                Console.WriteLine($"        Detail: {note}");
            if (pass)
                passed++;
            else
                failed++;
        }

        Console.WriteLine($"\n  {passed} passed, {failed} failed.");

        if (failed > 0)
        {
            Console.WriteLine("\n  See fallback strategies printed above for each failure.");
            Console.WriteLine("  Document any failures in server/dev/DEV-CREDENTIALS.md");
            Console.WriteLine("  under 'MinIO S3 parity: known gaps vs production AWS'.");
            return 1;
        }

        Console.WriteLine("\n  All checks PASSED. MinIO is parity-compatible with the S3");
        Console.WriteLine("  features Bloom Cloud TC requires.");
        return 0;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates an AmazonS3Client configured for MinIO:
    /// - ServiceURL points at the local MinIO endpoint.
    /// - ForcePathStyle = true (MinIO uses path-style: http://host:port/bucket/key).
    /// - Region is set to us-east-1 (MinIO accepts this; the actual value does not matter).
    /// - Static credentials with NO session token: MinIO VALIDATES session tokens (they are
    ///   JWTs minted by its own STS), so a fabricated token is rejected with
    ///   "security token invalid". Dev-mode edge functions must therefore either mint real
    ///   temporary creds via MinIO's AssumeRole or return static creds with no token —
    ///   see DEV-CREDENTIALS.md.
    /// </summary>
    private static AmazonS3Client CreateS3Client()
    {
        var credentials = new BasicAWSCredentials(AccessKey, SecretKey);

        var config = new AmazonS3Config
        {
            ServiceURL = Endpoint,
            ForcePathStyle = true,
            // Disable the default us-east-1 redirect behaviour; MinIO is not AWS.
            AuthenticationRegion = "us-east-1",
        };

        return new AmazonS3Client(credentials, config);
    }

    /// <summary>
    /// Computes the SHA-256 hash of <paramref name="data"/> and returns it
    /// as a base64 string — the format the AWS SDK sends in x-amz-checksum-sha256.
    /// </summary>
    private static string ComputeSha256Base64(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToBase64String(hash);
    }

    /// <summary>Returns the environment variable <paramref name="name"/>, or
    /// <paramref name="fallback"/> if it is not set.</summary>
    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) ?? fallback;
}
