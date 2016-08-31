using System.IO;
using Bloom;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;
using SIL.Progress;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class ProblemBookUploaderTests
	{
		[Test]
		public void UploadSmokeTest()
		{
			using(var folder = new TemporaryFolder("Upload Smoke Test"))
			{
				File.WriteAllText(folder.Combine("hello there.txt"), "hello there");
				using(var bookZip = TempFile.WithFilenameInTempFolder("Upload Smoketest.zip"))
				{
					var zip = new BloomZipFile(bookZip.Path);
					zip.AddDirectory(folder.FolderPath);
					zip.Save();

					var progress = new StringBuilderProgress();
					ProblemBookUploader.UploadBook(BloomS3Client.UnitTestBucketName, bookZip.Path,progress);
					Assert.IsTrue(progress.Text.Contains("Success"), progress.Text);
				}
			}
		}
	}
}
