using System;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using L10NSharp;
using SIL.Progress;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// this differs from Book Transfer in that it knows nothing of Parse or the Bloom Library. It simply knows
	/// how to push a zip to an s3 bucket and give hopefully helpful error messages.
	/// It is used for sending us problem books
	/// </summary>
	public class ProblemBookUploader
	{
		public static string UploadBook(string bucketName, string bookZipPath, IProgress progress)
		{
			try
			{
				using(var s3Client = new BloomS3Client(bucketName))
				{
					var url = s3Client.UploadSingleFile(bookZipPath, progress);
					progress.WriteMessage("Upload Success");
					return url;
				}

			}
			catch (WebException e)
			{
				progress.WriteError("There was a problem uploading your book: "+e.Message);
				throw;
			}
			catch (AmazonS3Exception e)
			{
				if (e.Message.Contains("The difference between the request time and the current time is too large"))
				{
					progress.WriteError(LocalizationManager.GetString("PublishTab.Upload.TimeProblem",
						"There was a problem uploading your book. This is probably because your computer is set to use the wrong timezone or your system time is badly wrong. See http://www.di-mgt.com.au/wclock/help/wclo_setsysclock.html for how to fix this."));
				}
				else
				{
					progress.WriteError("There was a problem uploading your book: " + e.Message);
				}
				throw;
			}
			catch (AmazonServiceException e)
			{
				progress.WriteError("There was a problem uploading your book: " + e.Message);
				throw;
			}
			catch (Exception e)
			{
				progress.WriteError("There was a problem uploading your book.");
				progress.WriteError(e.Message.Replace("{", "{{").Replace("}", "}}"));
				progress.WriteVerbose(e.StackTrace);
				throw;
			}
		}
	}
}
