using System.IO;
using Bloom.Utils;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;
using SIL.Progress;

namespace BloomTests.WebLibraryIntegration
{
    // Integration test: uploads a real zip to the live unit-test S3 bucket, so it needs
    // internet access. Exclude it from a quick local run with --filter TestCategory!=Integration
    [TestFixture]
    [Category("Integration")]
    public class ProblemBookUploaderTests
    {
        [Test]
        public void UploadSmokeTest()
        {
            using (var folder = new TemporaryFolder("Upload Smoke Test"))
            {
                File.WriteAllText(folder.Combine("hello there.txt"), "hello there");
                using (var bookZip = TempFile.WithFilenameInTempFolder("Upload Smoketest.zip"))
                {
                    var zip = new BloomZipFile(bookZip.Path);
                    zip.AddDirectory(folder.FolderPath);
                    zip.Save();

                    var progress = new StringBuilderProgress();
                    ProblemBookUploader.UploadBook(
                        BloomS3Client.UnitTestBucketName,
                        bookZip.Path,
                        progress
                    );
                    Assert.IsTrue(progress.Text.Contains("Success"), progress.Text);
                }
            }
        }
    }
}
