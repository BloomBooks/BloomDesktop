using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ImageToolbox;

namespace Bloom.ImageProcessing
{
	/// <summary>
	/// Currently the only processing we're doing it to make PNGs with lots of whitespace look good against our colored background pages
	/// Previously, we also shrunk images to improve performance when we were handing out file paths. Now that we are giving images
	/// over http, gecko may do well enough without the shrinking.
	/// </summary>
	public class RuntimeImageProcessor : IDisposable
	{
		private readonly BookRenamedEvent _bookRenamedEvent;
		public int TargetDimension = 500;
		private Dictionary<string, string> _originalPathToProcessedVersionPath;
		private string _cacheFolder;

		private readonly ImageAttributes _convertWhiteToTransparent;

		// BL-970: Used to check for image objects with no color space on Linux
		private const int CheckPngFlags = (int)(ImageFlags.ColorSpaceCmyk | ImageFlags.ColorSpaceGray
			| ImageFlags.ColorSpaceRgb | ImageFlags.ColorSpaceYcbcr | ImageFlags.ColorSpaceYcck);

		public RuntimeImageProcessor(BookRenamedEvent bookRenamedEvent)
		{
			_bookRenamedEvent = bookRenamedEvent;
			_originalPathToProcessedVersionPath = new Dictionary<string, string>();
			_cacheFolder = Path.Combine(Path.GetTempPath(), "Bloom");
			_bookRenamedEvent.Subscribe(OnBookRenamed);
			_convertWhiteToTransparent = new ImageAttributes();
			_convertWhiteToTransparent.SetColorKey(Color.FromArgb(253, 253, 253), Color.White);
		}

		private void OnBookRenamed(KeyValuePair<string, string> fromPathAndToPath)
		{
			//Note, we don't pay attention to what the change was, we just purge the whole cache

			TryToDeleteCachedImages();
			_originalPathToProcessedVersionPath = new Dictionary<string, string>();
		}

		public void Dispose()
		{
			if (_originalPathToProcessedVersionPath == null)
				return;

			TryToDeleteCachedImages();
			_originalPathToProcessedVersionPath = null;

			//NB: this turns out to be dangerous. Without it, we still delete all we can, leave some files around
			//each time, and then deleting them on the next run
			//			_cacheFolder.Dispose();

			GC.SuppressFinalize(this);
		}

		private void TryToDeleteCachedImages()
		{
			lock (this)
			{
				foreach(var path in _originalPathToProcessedVersionPath.Values)
				{
					try
					{
						if (File.Exists(path))
						{
							File.Delete(path);
							Debug.WriteLine("RuntimeImageProcessor Successfully deleted: " + path);
						}
					}
					catch (Exception e)
					{
						Debug.WriteLine("RuntimeImageProcessor Dispose(): " + e.Message);
					}
				}
				_originalPathToProcessedVersionPath.Clear();
			}
		}

		public string GetPathToResizedImage(string originalPath)
		{
			//don't mess with Bloom UI images
			if (new[] {"/img/", "placeHolder", "Button"}.Any(s => originalPath.Contains(s)))
				return originalPath;

			lock (this)
			{
				string pathToProcessedVersion;
				if (_originalPathToProcessedVersionPath.TryGetValue(originalPath, out pathToProcessedVersion))
				{
					if (File.Exists(pathToProcessedVersion) &&
					    new FileInfo(originalPath).LastWriteTimeUtc <= new FileInfo(pathToProcessedVersion).LastWriteTimeUtc)
					{
						return pathToProcessedVersion;
					}
					else
					{
						_originalPathToProcessedVersionPath.Remove(originalPath);
					}
				}

				using (var originalImage = PalasoImage.FromFile(originalPath))
				{
					// BL-970: Some indexed png files are not loaded correctly on Linux, probably because libgdiplus
					// does not correctly identify the color space, so do not attempt to save them using Image.Save.
					if (Palaso.PlatformUtilities.Platform.IsLinux
						&& originalPath.EndsWith(".png")
						&& ((originalImage.Image.Flags & CheckPngFlags) == 0))
					{
						_originalPathToProcessedVersionPath.Add(originalPath, originalPath);
						return originalPath;
					}

					//if it's a jpeg, we don't resize, we don't mess with transparency, nothing. These things
					//are scary in .net. Just send the original back and wash our hands of it.
					if (ImageUtils.AppearsToBeJpeg(originalImage))
					{
						return originalPath;
					}

					double shrinkFactor = 1.0;

#if ShrinkLargeImages // at the moment, we're suspecting that it may be better to let the browser do the shrinking

					//if its a small image, like a creative commons logo, we don't try and resize it
					if (originalImage.Image.Width > TargetDimension || originalImage.Image.Height > TargetDimension)
					{
						var maxDimension = Math.Max(originalImage.Image.Width, originalImage.Image.Height);
						//enhance: if we had a way of knowing what the target dimension actually was, we'd use that, of course
						shrinkFactor = (TargetDimension/(double) maxDimension);
					}
#endif
					var destWidth = (int) (shrinkFactor*originalImage.Image.Width);
					var destHeight = (int) (shrinkFactor*originalImage.Image.Height);

					using (var processedBitmap = new Bitmap(destWidth, destHeight))
					{
						using (Graphics g = Graphics.FromImage((Image) processedBitmap))
						{
#if ShrinkLargeImages 
							//in version 1.0, we used .NearestNeighbor. But if there is a border line down the right size (as is common for thumbnails that,
							//are, for example, re-inserted into Teacher's Guides), then the line gets cut off. So I switched it to HighQualityBicubic
							g.InterpolationMode = InterpolationMode.HighQualityBicubic; //.NearestNeighbor;//or smooth it: HighQualityBicubic
#endif
							var destRect = new Rectangle(0, 0, destWidth, destHeight);
							lock (_convertWhiteToTransparent)
							{
								g.DrawImage(originalImage.Image, destRect, 0, 0, originalImage.Image.Width, originalImage.Image.Height,
									GraphicsUnit.Pixel, _convertWhiteToTransparent);
							}
						}

						var pathToProcessedImage = Path.Combine(_cacheFolder, Path.GetRandomFileName() + Path.GetExtension(originalPath));

						//Hatton July 2012:
						//Once or twice I saw a GDI+ error on the Save below, when the app 1st launched.
						//I verified that if there is an IO error, that's what you get (a GDI+ error).
						//I looked once, and the %temp%/Bloom directory wasn't there, so that's what I think caused the error.
						//It's not clear why the temp/bloom directory isn't there... possibly it was there a moment ago
						//but then some startup thread cleared and deleted it? (we are now running on a thread responding to the http request)

						Exception error = null;
						for (int i = 0; i < 5; i++) //try up to five times, a second apart
						{
							try
							{
								error = null;

								if (!Directory.Exists(Path.GetDirectoryName(pathToProcessedImage)))
								{
									Directory.CreateDirectory(Path.GetDirectoryName(pathToProcessedImage));
								}
								processedBitmap.Save(pathToProcessedImage, originalImage.Image.RawFormat);
								break;
							}
							catch (Exception e)
							{
								Logger.WriteEvent("Error in RuntimeImageProcessor while trying to write image.");
								Logger.WriteEvent(e.Message);
								error = e;
								Thread.Sleep(1000); //wait a second before trying again
							}
						}
						if (error != null)
						{
							//NB: I tested that even though we're in a non-UI thread, this shows up fine (libpalaso marshalls it to the UI thread)
							ErrorReport.NotifyUserOfProblem(error,
								"Bloom is having problem saving a processed version to your temp directory, at " + pathToProcessedImage +
								"\r\n\r\nYou might want to quit and restart Bloom. In the meantime, Bloom will use unprocessed image.");
							return originalPath;
						}

						try
						{
							_originalPathToProcessedVersionPath.Add(originalPath, pathToProcessedImage); //remember it so we can reuse if they show it again, and later delete
						}
						catch (ArgumentException)
						{
							// it happens sometimes that though it wasn't in the _originalPathToProcessedVersionPath when we entered, it is now
							// I haven't tracked it down... possibly we get a new request for the image while we're busy compressing it?
						}
						return pathToProcessedImage;
					}
				}
			}
		}
	}
}