using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ImageToolbox;

namespace Bloom.ImageProcessing
{
	/// <summary>
	/// Gecko struggles with hi-res images intented for printing. Gecko chews up memory, makes for slow drawing,
	/// or even gives up on displaying the image altogether (worse on slow machines).
	/// This cache takes requests for images and returns lo-res versions of them.
	/// </summary>
	public class RuntimeImageProcessor :IDisposable
	{
		private readonly BookRenamedEvent _bookRenamedEvent;
		public int TargetDimension=500;
		private Dictionary<string,string> _paths;
		private string _cacheFolder;

		private ImageAttributes _transparentImageAttributes;

		public RuntimeImageProcessor(BookRenamedEvent bookRenamedEvent)
		{
			_bookRenamedEvent = bookRenamedEvent;
			_paths = new Dictionary<string, string>();
			_cacheFolder = Path.Combine(Path.GetTempPath(), "Bloom");
			_bookRenamedEvent.Subscribe(OnBookRenamed);
			_transparentImageAttributes = new ImageAttributes();
			_transparentImageAttributes.SetColorKey(Color.FromArgb(253, 253, 253), Color.White);
		}

		private void OnBookRenamed(KeyValuePair<string, string> fromPathAndToPath)
		{
			//Note, we don't pay attention to what the change was, we just purge the whole cache

			TryToDeleteCachedImages();
			_paths = new Dictionary<string, string>();
		}

		public void Dispose()
		{
			if (_paths == null)
				return;

			TryToDeleteCachedImages();

			//NB: this turns out to be dangerous. Without it, we still delete all we can, leave some files around
			//each time, and then deleting them on the next run
			//			_cacheFolder.Dispose();

			GC.SuppressFinalize(this);
		}

		private void TryToDeleteCachedImages()
		{
//operate on a copy to avoid "Collection was modified; enumeration operation may not execute"
			//if someone is still using use while we're being disposed
			var pathsToDelete = new List<string>();
			pathsToDelete.AddRange(_paths.Values);
			foreach (var path in pathsToDelete)
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
			_paths = null;
		}

		private DateTime GetModifiedDateTime(string path)
		{
			var f = new FileInfo(path);
			return f.LastWriteTimeUtc;
		}

		public string GetPathToResizedImage(string originalPath)
		{
			//don't mess with Bloom UI images
			if (new[] {"/img/","placeHolder", "Button"}.Any(s => originalPath.Contains(s)))
				return originalPath;

			string resizedPath;
//			if(_paths.TryGetValue(originalPath, out resizedPath))
//			{
//				if (File.Exists(resizedPath) && new FileInfo(originalPath).LastWriteTimeUtc <= new FileInfo(resizedPath).LastWriteTimeUtc)
//				{
//						return resizedPath;
//				}
//				else
//				{
//					_paths.Remove(originalPath);
//				}
//			}
			using (var originalImage = PalasoImage.FromFile(originalPath))
			{
				if (ImageUtils.AppearsToBeJpeg(originalImage))
				{
					return originalImage.OriginalFilePath;
				}
				double shrinkFactor = 1.0;
				//if its a small image, like a creative commons logo, we don't try and resize it
				if (originalImage.Image.Width > TargetDimension || originalImage.Image.Height > TargetDimension)
				{
					var maxDimension = Math.Max(originalImage.Image.Width, originalImage.Image.Height);
					//enhance: if we had a way of knowing what the target dimension actually was, we'd use that, of course
					shrinkFactor = (TargetDimension/(double) maxDimension);
				}

				var destWidth = (int) (shrinkFactor*originalImage.Image.Width);
				var destHeight = (int) (shrinkFactor*originalImage.Image.Height);
				using (var b = new Bitmap(destWidth, destHeight))
				{
					using (Graphics g = Graphics.FromImage((Image) b))
					{
						//in version 1.0, we used .NearestNeighbor. But if there is a border line down the right size (as is common for thumbnails that,
						//are, for example, re-inserted into Teacher's Guides), then the line gets cut off. So I switched it to HighQualityBicubic
						g.InterpolationMode = InterpolationMode.HighQualityBicubic; //.NearestNeighbor;//or smooth it: HighQualityBicubic
						var destRect = new Rectangle(0, 0, destWidth, destHeight);
						lock(_transparentImageAttributes)
						{
							g.DrawImage(originalImage.Image, destRect, 0, 0, originalImage.Image.Width, originalImage.Image.Height,
								GraphicsUnit.Pixel, _transparentImageAttributes);
						}
					}

					var temp = Path.Combine(_cacheFolder, Path.GetRandomFileName() + Path.GetExtension(originalPath));


					//Hatton July 2012:
					//Once or twice I saw a GDI+ error on the Save below, when the app 1st launched.
					//I verified that if there is an IO error, that's what it you get (a GDI+ error).
					//I looked once, and the %temp%/Bloom directory wasn't there, so that's what I think caused the error.
					//It's not clear why the temp/bloom directory isn't there... possibly it was there a moment ago
					//but then some startup thread cleared and deleted it? (we are now running on a thread responding to the http request)

					Exception error = null;
					for (int i = 0; i < 5; i++) //try up to five times, a second apart
					{
						try
						{
							error = null;

							if (!Directory.Exists(Path.GetDirectoryName(temp)))
							{
								Directory.CreateDirectory(Path.GetDirectoryName(temp));
							}
							b.Save(temp, originalImage.Image.RawFormat);
							break;
						}
						catch (Exception e)
						{
							Logger.WriteEvent("Error in LowResImage while trying to write image.");
							Logger.WriteEvent(e.Message);
							error = e;
							Thread.Sleep(1000); //wait a second before trying again
						}
					}
					if (error != null)
					{
						//NB: this will be on a non-UI thread, so it probably won't work well!
						ErrorReport.NotifyUserOfProblem(error,
							"Bloom is having problem saving a low-res version to your temp directory, at " + temp +
							"\r\n\r\nYou might want to quit and restart Bloom. In the meantime, Bloom will try to use the full-res images.");
						return originalPath;
					}

//					try
//					{
//						_paths.Add(originalPath, temp); //remember it so we can reuse if they show it again, and later delete
//					}
//					catch (ArgumentException)
//					{
//						// it happens sometimes that though it wasn't in the _paths when we entered, it is now
//						// I haven't tracked it down... possibly we get a new request for the image while we're busy compressing it?
//					}

					return temp;
				}
			}
		}
	}
}
