using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
		public int TargetDimension=400;
		private Dictionary<string,string> _paths;
		private TemporaryFolder _cacheFolder;

		public LowResImageCache()
		{
			_paths = new Dictionary<string, string>();
			_cacheFolder = new TemporaryFolder("Bloom");
		}

		public void Dispose()
		{
			if (_paths == null)
				return;

			foreach (var path in _paths.Values)
			{
				try
				{
					if(File.Exists(path))
					{
						File.Delete(path);
						Debug.WriteLine("LowResImageCache Successfully deleted: "+path);
					}
				}
				catch (Exception e)
				{
					Debug.WriteLine("LowResImageCache Dispose(): "+e.Message);
				}
			}
			_paths = null;

			//NB: this turns out to be dangerous. Without it, we still delete all we can, leave some files around
			//each time, and then deleting them on the next run
			//			_cacheFolder.Dispose();
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

					using (var img = new Bitmap(original, (int)(shrinkFactor * original.Width), (int)(shrinkFactor * original.Height)))
					{
						var temp = _cacheFolder.GetPathForNewTempFile(false, Path.GetExtension(path));
						img.Save(temp, img.RawFormat);
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
