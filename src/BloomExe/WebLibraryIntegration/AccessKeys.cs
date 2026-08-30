using System;
using BloomFreezeDoctor.Protocol;
using SIL.IO;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// This class is responsible for the key bits of information that are needed to access our backend sites.
    /// These keys are not very secret and could easily be found, for example, by packet snooping.
    /// However, we want to keep them out of source code where someone might be able to do a google search
    /// and easily find our keys and use our storage.
    /// The keys are currently stored in a file called connections.dll. The installer must place a version of this
    /// in the EXE directory. Developers get it automatically, along with other dependencies.
    /// Which line holds which key is named once, in <see cref="SupportUploadCredentials"/>, because the
    /// Freeze Doctor reads the same file.
    /// </summary>
    public class AccessKeys
    {
        public string S3AccessKey { get; private set; }
        public string S3SecretAccessKey { get; private set; }

        private AccessKeys(string s3AccessKey, string s3Secret)
        {
            S3AccessKey = s3AccessKey;
            S3SecretAccessKey = s3Secret;
        }

        //Factory
        public static AccessKeys GetAccessKeys(string bucket)
        {
            // Located and read by the project Bloom and the Freeze Doctor share, which needs the same keys
            // to upload a minidump too large for a tracker attachment. One definition of where this file is
            // and what its lines mean: two independent readers of an undocumented line-ordered file is how
            // a line added at the top comes to mean different things in two programs.
            var lines =
                SupportUploadCredentials.TryReadLines()
                ?? throw new ApplicationException(
                    "Could not find or read " + SupportUploadCredentials.ConnectionsFileName
                );
            switch (bucket)
            {
                case BloomS3Client.SandboxBucketName:
                    // S3 'uploaderDev' user, who has permission to use the BloomLibraryBooks-Sandbox bucket.
                    if (BookUpload.IsDryRun)
                        return new AccessKeys(null, null);
                    return UploaderDev(lines);
                case BloomS3Client.UnitTestBucketName:
                case BloomS3Client.ProblemBookUploadsBucketName:
                    return UploaderDev(lines);
                case BloomS3Client.ProductionBucketName:
                    //S3 'uploader' user, who has permission to use the BloomLibraryBooks bucket
                    if (BookUpload.IsDryRun)
                        return new AccessKeys(null, null);
                    return new AccessKeys(
                        lines[SupportUploadCredentials.UploaderAccessKeyLine],
                        lines[SupportUploadCredentials.UploaderSecretLine]
                    );
                case BloomS3Client.BloomDesktopFiles:
                    // For now, this is public read, and no one needs to write.
                    return new AccessKeys(null, null);

                default:
                    throw new ApplicationException("Bucket name not recognized: " + bucket);
            }
        }

        /// <summary>
        /// The <c>uploaderDev</c> credentials, which three of the buckets above share.
        /// </summary>
        private static AccessKeys UploaderDev(string[] lines) =>
            new AccessKeys(
                lines[SupportUploadCredentials.UploaderDevAccessKeyLine],
                lines[SupportUploadCredentials.UploaderDevSecretLine]
            );
    }
}
