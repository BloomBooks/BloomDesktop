using System;
using System.Collections.Generic;
using NAudio.Wave;
using SIL.Code;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using TidyManaged;

namespace Bloom
{
	/// <summary>
	/// Provides a more robust version of various IO methods.
	/// The original intent of this class is to attempt to mitigate issues
	/// where we attempt IO but the file is locked by another application.
	/// Our theory is that some anti-virus software locks files while it scans them.
	/// 
	/// There is a similar class in SIL.IO, but that handles more generic calls
	/// which would not require additional dependencies.
	/// </summary>
	public class RobustIO
	{
		public static WaveFileReader CreateWaveFileReader(string wavFile)
		{
			return RetryUtility.Retry(() => new WaveFileReader(wavFile));
		}

		public static Document DocumentFromFile(string filePath)
		{
			return RetryUtility.Retry(() => Document.FromFile(filePath));
		}

		public static Metadata MetadataFromFile(string path)
		{
			return RetryUtility.Retry(() => Metadata.FromFile(path));
		}

		public static PalasoImage PalasoImageFromFile(string path)
		{
			return RetryUtility.Retry(() => PalasoImage.FromFile(path),
				RetryUtility.kDefaultMaxRetryAttempts,
				RetryUtility.kDefaultRetryDelay,
				new HashSet<Type>
				{
					Type.GetType("System.IO.IOException"),
					// Odd type to catch... but it seems that Image.FromFile (which is called in the bowels of PalasoImage.FromFile)
					// throws OutOfMemoryException when the file is inaccessible.
					// See http://stackoverflow.com/questions/2610416/is-there-a-reason-image-fromfile-throws-an-outofmemoryexception-for-an-invalid-i
					Type.GetType("System.OutOfMemoryException")
				});
		}

		public static void SavePalasoImage(PalasoImage image, string path)
		{
			RetryUtility.Retry(() => image.Save(path));
		}
	}
}
