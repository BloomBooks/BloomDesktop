using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using BloomTemp;

namespace Bloom.ImageProcessing
{
	/// <summary>
	/// Gecko struggles with hi-res images intented for printing. Gecko chews up memory, makes for slow drawing,
	/// or even gives up on displaying the image altogether (worse on slow machines).
	/// This cache takes requests for images and returns lo-res versions of them.
	/// </summary>
	public class LowResImageCache :IDisposable
	{
		private readonly BookRenamedEvent _bookRenamedEvent;
		public int TargetDimension=500;
		private Dictionary<string,string> _paths;
		private TemporaryFolder _cacheFolder;

		public LowResImageCache(BookRenamedEvent bookRenamedEvent)
		{
			_bookRenamedEvent = bookRenamedEvent;
			_paths = new Dictionary<string, string>();
			_cacheFolder = new TemporaryFolder("Bloom");
			_bookRenamedEvent.Subscribe(OnBookRenamed);
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
						Debug.WriteLine("LowResImageCache Successfully deleted: " + path);
					}
				}
				catch (Exception e)
				{
					Debug.WriteLine("LowResImageCache Dispose(): " + e.Message);
				}
			}
			_paths = null;
		}

		public string GetPathToResizedImage(string path)
		{
			string resizedPath;
			if(_paths.TryGetValue(path, out resizedPath))
			{
				if(File.Exists(resizedPath))
					return resizedPath;
				else
				{
					_paths.Remove(path);
				}

			}

			var original = Image.FromFile(path);
			try
			{
				if (original.Width > TargetDimension || original.Height > TargetDimension)
				{
					var maxDimension = Math.Max(original.Width, original.Height);
					double shrinkFactor = (TargetDimension/(double) maxDimension);

					var destWidth = (int) (shrinkFactor*original.Width);
					var destHeight = (int) (shrinkFactor*original.Height);
					using (var b = new Bitmap(destWidth, destHeight))
					{

						using (Graphics g = Graphics.FromImage((Image)b))
						{
							g.InterpolationMode = InterpolationMode.NearestNeighbor;//or smooth it: HighQualityBicubic
							g.DrawImage(original, 0, 0, destWidth, destHeight);
						}

						var temp = _cacheFolder.GetPathForNewTempFile(false, Path.GetExtension(path));
						b.Save(temp, original.RawFormat);
						_paths.Add(path, temp);//remember it so we can reuse if they show it again, and later delete
						return temp;
					}


				}
				else
				{
					return path;
				}
			}
			finally
			{
				original.Dispose();
			}
		}
	}
}
