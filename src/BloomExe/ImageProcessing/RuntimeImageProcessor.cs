using System;
using System.Collections.Concurrent;
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

		// the ConcurrentDictionary is thread-safe
		private ConcurrentDictionary<string, string> _originalPathToProcessedVersionPath;

		// using a ConcurrentDictionary because there isn't a thread-safe List in .Net 4.0
		private ConcurrentDictionary<string, bool> _imageFilesToReturnUnprocessed;

		private string _cacheFolder;

		private readonly ImageAttributes _convertWhiteToTransparent;

		public RuntimeImageProcessor(BookRenamedEvent bookRenamedEvent)
		{
			_bookRenamedEvent = bookRenamedEvent;
			_originalPathToProcessedVersionPath = new ConcurrentDictionary<string, string>();
			_imageFilesToReturnUnprocessed = new ConcurrentDictionary<string, bool>();
			_cacheFolder = Path.Combine(Path.GetTempPath(), "Bloom");
			_bookRenamedEvent.Subscribe(OnBookRenamed);
			_convertWhiteToTransparent = new ImageAttributes();
			_convertWhiteToTransparent.SetColorKey(Color.FromArgb(253, 253, 253), Color.White);
		}

		private void OnBookRenamed(KeyValuePair<string, string> fromPathAndToPath)
		{
			//Note, we don't pay attention to what the change was, we just purge the whole cache

			TryToDeleteCachedImages();
			_originalPathToProcessedVersionPath = new ConcurrentDictionary<string, string>();
			_imageFilesToReturnUnprocessed = new ConcurrentDictionary<string, bool>();
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

		public string GetPathToResizedImage(string originalPath, bool getThumbnail = false)
		{
			//don't mess with Bloom UI images
			if (new[] {"/img/", "placeHolder", "Button"}.Any(s => originalPath.Contains(s)))
				return originalPath;

			// check if this image is in the do-not-process list
			bool test;
			if (_imageFilesToReturnUnprocessed.TryGetValue(originalPath, out test)) return originalPath;

			lock (this)
			{
				var cacheFileName = originalPath;

				if (getThumbnail)
				{
					cacheFileName = "thumbnail_" + cacheFileName;
				}

				// if there is a cached version, return it
				string pathToProcessedVersion;
				if (_originalPathToProcessedVersionPath.TryGetValue(cacheFileName, out pathToProcessedVersion))
				{
					if (File.Exists(pathToProcessedVersion) &&
						new FileInfo(originalPath).LastWriteTimeUtc <= new FileInfo(pathToProcessedVersion).LastWriteTimeUtc)
					{
						return pathToProcessedVersion;
					}

					// the file has changed, remove from cache
					string valueRemoved;
					_originalPathToProcessedVersionPath.TryRemove(cacheFileName, out valueRemoved);
				}

				// there is not a cached version, try to make one
				var pathToProcessedImage = Path.Combine(_cacheFolder, Path.GetRandomFileName() + Path.GetExtension(originalPath));

				if (!Directory.Exists(Path.GetDirectoryName(pathToProcessedImage)))
					Directory.CreateDirectory(Path.GetDirectoryName(pathToProcessedImage));

				// BL-1112: images not loading in page thumbnails
				bool success;
				if (getThumbnail)
				{
					// The HTML div that contains the thumbnails is 80 pixels wide, so make the thumbnails 80 pixels wide
					success = GenerateThumbnail(originalPath, pathToProcessedImage, 80);
				}
				else
				{
					success = MakePngBackgroundTransparent(originalPath, pathToProcessedImage);
				}

				if (!success)
				{
					// add this image to the do-not-process list so we don't waste time doing this again
					_imageFilesToReturnUnprocessed.TryAdd(originalPath, true);
					return originalPath;
				}

				_originalPathToProcessedVersionPath.TryAdd(cacheFileName, pathToProcessedImage); //remember it so we can reuse if they show it again, and later delete

				return pathToProcessedImage;
			}
		}

		private static bool GenerateThumbnail(string originalPath, string pathToProcessedImage, int newWidth)
		{
			using (var originalImage = PalasoImage.FromFile(originalPath))
			{
				// check if it needs resized
				if (originalImage.Image.Width <= newWidth) return false;

				// calculate dimensions
				var newW = (originalImage.Image.Width > newWidth) ? newWidth : originalImage.Image.Width;
				var newH = newW * originalImage.Image.Height / originalImage.Image.Width;

				using (var newImg = originalImage.Image.GetThumbnailImage(newW, newH, () => false, IntPtr.Zero))
				{
					newImg.Save(pathToProcessedImage);
				}
			}

			return true;
		}


		private bool MakePngBackgroundTransparent(string originalPath, string pathToProcessedImage)
		{
			using (var originalImage = PalasoImage.FromFile(originalPath))
			{
				//if it's a jpeg, we don't resize, we don't mess with transparency, nothing. These things
				//are scary in .net. Just send the original back and wash our hands of it.
				if (ImageUtils.AppearsToBeJpeg(originalImage))
				{
					return false;
				}

				using (var processedBitmap = new Bitmap(originalImage.Image.Width, originalImage.Image.Height))
				{
					using (var g = Graphics.FromImage(processedBitmap))
					{
						var destRect = new Rectangle(0, 0, originalImage.Image.Width, originalImage.Image.Height);
						lock (_convertWhiteToTransparent)
						{
							g.DrawImage(originalImage.Image, destRect, 0, 0, originalImage.Image.Width, originalImage.Image.Height,
								GraphicsUnit.Pixel, _convertWhiteToTransparent);
						}
					}

					//Hatton July 2012:
					//Once or twice I saw a GDI+ error on the Save below, when the app 1st launched.
					//I verified that if there is an IO error, that's what you get (a GDI+ error).
					//I looked once, and the %temp%/Bloom directory wasn't there, so that's what I think caused the error.
					//It's not clear why the temp/bloom directory isn't there... possibly it was there a moment ago
					//but then some startup thread cleared and deleted it? (we are now running on a thread responding to the http request)

					Exception error = null;
					for (var i = 0; i < 5; i++) //try up to five times, a second apart
					{
						try
						{
							error = null;
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
						return false;
					}
				}
			}

			return true;
		}
	}
}