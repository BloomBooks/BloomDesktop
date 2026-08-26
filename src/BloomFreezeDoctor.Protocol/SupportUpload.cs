using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using SIL.IO;

namespace BloomFreezeDoctor.Protocol;

/// <summary>
/// The access keys for the support-uploads bucket, read from the file the installer places beside the
/// executable.
///
/// This lives here, in the project both Bloom and the Freeze Doctor reference, because both now need it
/// and neither should have its own idea of where the keys are or which lines of that file they occupy.
/// Bloom's <c>AccessKeys</c> reads the file through this class rather than opening it itself, so there is
/// one definition of the format — and adding a line to it in future cannot silently shift the meaning of
/// the keys for one of the two consumers and not the other.
/// </summary>
public static class SupportUploadCredentials
{
    /// <summary>
    /// The file the keys live in. Named `.dll` so it looks uninteresting; it is a plain text file, one
    /// value per line. The installer puts it in the executable's directory, and a developer build finds
    /// it in the repository's DistFiles.
    /// </summary>
    public const string ConnectionsFileName = "connections.dll";

    /// <summary>
    /// Which lines hold the keys for the support-uploads bucket. Third and fourth, shared with the sandbox
    /// and unit-test buckets — see Bloom's AccessKeys, which is where this numbering comes from and which
    /// now defers to this class for it.
    /// </summary>
    private const int AccessKeyLine = 2;
    private const int SecretLine = 3;

    /// <summary>
    /// The keys, or null if the file is missing or too short — which is a real possibility rather than a
    /// theoretical one: the file is not in source control's build output, so it is found beside the
    /// installed exe or in the repository, and a stripped-down deployment may have neither. Callers treat
    /// null as "cannot upload" and say so, rather than failing.
    /// </summary>
    public static (string AccessKey, string Secret)? ForSupportUploads()
    {
        var lines = TryReadLines();
        if (lines == null || lines.Length <= SecretLine)
            return null;
        var accessKey = lines[AccessKeyLine].Trim();
        var secret = lines[SecretLine].Trim();
        if (accessKey.Length == 0 || secret.Length == 0)
            return null;
        return (accessKey, secret);
    }

    /// <summary>
    /// The lines of the connections file, or null if it cannot be found or read. Public so Bloom's own
    /// AccessKeys, which needs other lines for other buckets, can share the locating and reading of it.
    /// </summary>
    public static string[]? TryReadLines()
    {
        foreach (var path in CandidatePaths())
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && RobustFile.Exists(path))
                    return RobustFile.ReadAllLines(path);
            }
            catch (Exception)
            {
                // Unreadable is the same as absent as far as the caller is concerned; try the next place.
            }
        }
        return null;
    }

    /// <summary>
    /// Where to look, in order. The first is where an installed Bloom and an installed Doctor both find it,
    /// since they share a directory. The rest cover a developer build, whose executables sit in
    /// `output/Debug/&lt;platform&gt;` while the file stays in the repository's DistFiles — libpalaso's
    /// solution-walking lookup is tried first for that, and the hand-rolled walk is the fallback for when
    /// this assembly is loaded somewhere that helper cannot reason about.
    /// </summary>
    private static IEnumerable<string?> CandidatePaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, ConnectionsFileName);

        string? viaPalaso = null;
        try
        {
            viaPalaso = FileLocationUtilities.GetFileDistributedWithApplication(
                true,
                ConnectionsFileName
            );
        }
        catch (Exception)
        {
            // Its dev-mode search throws if it cannot work out where the solution is.
        }
        yield return viaPalaso;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var up = 0; up < 6 && directory != null; up++, directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, "DistFiles", ConnectionsFileName);
        }
    }
}

/// <summary>
/// Uploads one file to the support bucket and returns a link to it.
///
/// **Why this exists at all**, rather than attaching the file to the tracker card: YouTrack will not take
/// an attachment much over 10 MB (measured at about that in July 2020 and recorded in Bloom's own
/// ProblemReportApi), and a minidump of a real Bloom is 16-17 MB. Bloom's problem reporter reached the
/// same wall years ago and answered it the same way — its `AttachFileToExistingIssue` call is still there,
/// commented out, above the S3 upload that replaced it. So the Doctor's dumps go to the bucket and the
/// card carries a link.
///
/// Objects are uploaded **public-read**, exactly as Bloom's problem-book uploads are: the bucket serves
/// them over plain HTTPS to whoever has the URL, and the URL is the only protection. That is a deliberate,
/// accepted trade — agreed 26 August 2026 — on the grounds that the link only ever appears on a tracker
/// card that is itself private. But it does mean the key must not be guessable, which is why
/// <see cref="MakeUnguessableKey"/> exists and why this does not simply use the file name.
/// </summary>
public static class SupportFileUploader
{
    /// <summary>
    /// The bucket, the same one Bloom uploads problem books to. Kept as a literal here rather than
    /// referenced from Bloom's BloomS3Client, because this project deliberately does not depend on Bloom.
    /// </summary>
    public const string BucketName = "bloom-problem-books";

    /// <summary>
    /// A key nobody can guess, and nobody else will collide with.
    ///
    /// Both halves matter, and Bloom's existing single-file upload has neither: it uses the bare file name
    /// as the key, so two users uploading `bloom-1234.dmp` overwrite each other, and anybody who knows the
    /// naming pattern can fetch other people's uploads from a public-read bucket by trying names. A random
    /// prefix fixes both at no cost, and the readable suffix keeps the URL self-describing for whoever
    /// opens the card.
    /// </summary>
    public static string MakeUnguessableKey(string fileName) =>
        $"freeze-doctor/{Guid.NewGuid():N}/{Path.GetFileName(fileName)}";

    /// <summary>
    /// Uploads the file and returns its URL, or null if it could not be uploaded — no credentials, no
    /// network, a bucket that refuses us. Never throws: this runs while filing a report about somebody's
    /// freeze, and losing the whole report because an attachment would not go is a poor trade.
    /// </summary>
    public static async Task<string?> TryUploadAsync(
        string path,
        CancellationToken cancellation = default
    )
    {
        try
        {
            if (!RobustFile.Exists(path))
                return null;
            var credentials = SupportUploadCredentials.ForSupportUploads();
            if (credentials == null)
                return null;

            using var client = new AmazonS3Client(
                credentials.Value.AccessKey,
                credentials.Value.Secret,
                Amazon.RegionEndpoint.USEast1
            );
            using var transfer = new TransferUtility(client);
            var key = MakeUnguessableKey(path);
            var request = new TransferUtilityUploadRequest
            {
                BucketName = BucketName,
                FilePath = path,
                Key = key,
                CannedACL = S3CannedACL.PublicRead,
            };
            request.Headers.CacheControl = "no-cache";
            await transfer.UploadAsync(request, cancellation).ConfigureAwait(false);
            return $"https://s3.amazonaws.com/{BucketName}/{Uri.EscapeDataString(key)}";
        }
        catch (Exception)
        {
            return null;
        }
    }
}
