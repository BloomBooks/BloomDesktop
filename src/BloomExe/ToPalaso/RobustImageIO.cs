using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using SIL.Code;
using SIL.IO;

namespace Bloom.ToPalaso
{
	public class RobustImageIO
	{
		/// <summary>
		/// Read a bitmap image from a file.  The file must be known to exist before calling this method.
		/// </summary>
		/// <remarks>
		/// Image.FromFile and Image.FromStream lock the file until the image is disposed of.  Therefore,
		/// we copy the image and dispose of the original.  On Windows, Image.FromFile leaks file handles,
		/// so we use FromStream instead.  For details, see the last answer to
		/// http://stackoverflow.com/questions/16055667/graphics-drawimage-out-of-memory-exception
		///
		/// This method should be moved to libpalaso SIL.IO.RobustImageIO, but that's too painful for
		/// immediate implementation there and use in a beta about to be released.
		/// </remarks>
		public static Image GetImageFromFile(string path)
		{
			Debug.Assert(RobustFile.Exists(path), String.Format("{0} does not exist for ImageUtils.GetImageFromFile()?!", path));
			return RetryUtility.Retry(() =>
				GetImageFromFileInternal(path),
				RetryUtility.kDefaultMaxRetryAttempts,
				RetryUtility.kDefaultRetryDelay,
				new HashSet<Type>
				{
					Type.GetType("System.IO.IOException"),
					Type.GetType("System.Runtime.InteropServices.ExternalException")
				});
		}

		private static Image GetImageFromFileInternal(string path)
		{
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				using (var image = new Bitmap(stream))
				{
					return new Bitmap(image);
				}
			}
		}

	}
}
