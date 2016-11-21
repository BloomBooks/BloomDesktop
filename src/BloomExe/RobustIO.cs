using System;
using System.Collections.Generic;
#if !__MonoCS__
using NAudio.Wave;
#endif
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
#if !__MonoCS__
		public static WaveFileReader CreateWaveFileReader(string wavFile)
		{
			return RetryUtility.Retry(() => new WaveFileReader(wavFile));
		}
#endif

		public static Document DocumentFromFile(string filePath)
		{
			return RetryUtility.Retry(() => Document.FromFile(filePath));
		}

		public static Metadata MetadataFromFile(string path)
		{
			return RetryUtility.Retry(() => Metadata.FromFile(path));
		}

		public static void SavePalasoImage(PalasoImage image, string path)
		{
			RetryUtility.Retry(() => image.Save(path));
		}
	}
}
