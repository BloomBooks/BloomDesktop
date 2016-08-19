using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SIL.Extensions;
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
	/// You can see what keys are stored in what order by checking the constructor.
	/// That will work for Parse.com, where we currently have different API keys for the sandbox.
	/// For the S3 data, we currently just use different buckets. If we wanted to, we could put the
	/// 'real data' bucket name here also, and put the appropriate one into the testing version of connections.dll.
	/// Todo: Some of the real keys are still in our version control history. Before we go live, we may want
	/// to change the keys so any keys discovered in our version control are obsolete.
	/// </summary>
	public class AccessKeys
	{
		public string S3AccessKey { get; private set; }
		public string S3SecretAccessKey { get; private set; }
		public string ParseApiKey { get; private set; }
		public string ParseApplicationKey { get; private set; }

		private AccessKeys(string s3AccessKey, string s3Secret, string parseApiKey, string parseAppKey)
		{
			S3AccessKey = s3AccessKey;
			S3SecretAccessKey = s3Secret;
			ParseApiKey = parseApiKey;
			ParseApplicationKey = parseAppKey;
		}

		//Factory
		public static AccessKeys GetAccessKeys(string bucket)
		{
			var connectionsPath = FileLocator.GetFileDistributedWithApplication("connections.dll");
			var lines = RobustFile.ReadAllLines(connectionsPath);
			switch(bucket)
			{
				case BloomS3Client.SandboxBucketName:
					// S3 'uploaderDev' user, who has permission to use the BloomLibraryBooks-Sandbox bucket.
					//parse.com silbloomlibrarysandbox
					return new AccessKeys(lines[2], lines[3],lines[6],lines[7]);
				case BloomS3Client.UnitTestBucketName:
					return new AccessKeys(lines[2], lines[3], lines[8], lines[9]);
				case BloomS3Client.ProductionBucketName:
					//S3 'uploader' user, who has permission to use the BloomLibraryBooks bucket
					//parse.com silbloomlibrary
					return new AccessKeys(lines[0], lines[1], lines[4], lines[5]);
				case BloomS3Client.ProblemBookUploadsBucketName:
					return new AccessKeys(lines[2], lines[3], null,null);
				case BloomS3Client.BloomDesktopFiles:
					// For now, this is public read, and no one needs to write.
					return new AccessKeys(null, null, null, null);

				default: throw new ApplicationException("Bucket name not recognized: " + bucket);
			}
		}
	}
}
