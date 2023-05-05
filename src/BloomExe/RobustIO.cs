#if !__MonoCS__
using NAudio.Wave;
#endif
using System.IO;
using SIL.Code;
using SIL.IO;
using SIL.Windows.Forms.ClearShare;
using TidyManaged;
using Bloom.ImageProcessing;
using System;

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

		/// <summary>
		/// Get the image metadata from the file as reliably as possible.
		/// </summary>
		public static Metadata MetadataFromFile(string path)
		{
			// Books sometimes use image files that are mislabeled.  JPEG files sometimes have been given
			// .png extensions, and it's likely that PNG files have been given .jpg extensions.  TagLib crashes
			// trying to read the metadata in such cases, so we prevent that particular crash in this method.
			if (Path.GetExtension(path).ToLowerInvariant() == ".png" && ImageUtils.IsJpegFile(path))
			{
				using (var jpegFile = TempFile.WithExtension(".jpg"))
				{
					RobustFile.Copy(path, jpegFile.Path, true);
					return MetadataFromFileInternal(jpegFile.Path);
				}
			}
			if (ImageUtils.HasJpegExtension(path) && ImageUtils.IsPngFile(path))
			{
				using (var pngFile = TempFile.WithExtension(".png"))
				{
					RobustFile.Copy(path, pngFile.Path, true);
					return MetadataFromFileInternal(pngFile.Path);
				}
			}
			// Assume everything is okay.
			return MetadataFromFileInternal(path);
		}

		private static Metadata MetadataFromFileInternal(string path)
		{
			return RetryUtility.Retry(() => Metadata.FromFile(path));
		}
	}
}
