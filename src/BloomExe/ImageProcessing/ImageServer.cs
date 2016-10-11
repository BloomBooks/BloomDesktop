using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Api;
using SIL.IO;
using SIL.Reporting;
using Bloom.Properties;

namespace Bloom.ImageProcessing
{
	/// <summary>
	/// This is a local http server which just serves up images. Its job is to take the original image
	/// and lower it to screen resolution, because gecko was having so much trouble dealing with hi-res
	/// images intended for print publications. While this could have been accomplished just making the
	/// img src attribute point to an alternate destination on disk, I did it this way so that we can
	/// generate lo-res images in an asynchronous fashion, which will degrade nicely on slower machines.
	/// That is, the browser is happy to show the picture later, when it is ready, if it is comming from
	/// an http request. In constrast, a file:// is just there or not there... no async about it.
	///
	///Hints
	/// To check what's in the url access control list on Vista and up: netsh http show urlacl
	///on XP: httpcfg query urlacl
	///
	///nb: had trouble with 8080. Remember to enable this with (windows 7 up): netsh http add urlacl url=http://localhost:8089/bloom user=everyone
	///on Windows XP, use httpcfg. I haven't tested this, but I think it may be: HTTPCFG set urlacl -u http://+:8089/bloom/ /a D:(A;;GX;;;WD)
	/// </summary>
	public class ImageServer : ServerBase
	{
		private RuntimeImageProcessor _cache;
		private bool _useCache;

		public ImageServer(RuntimeImageProcessor cache)
		{
			_cache = cache;
			_useCache = Settings.Default.ImageHandler != "off";
		}

		protected override void Dispose(bool fDisposing)
		{
			if (fDisposing)
			{
				if (_cache != null)
					_cache.Dispose();
				_cache = null;
			}

			base.Dispose(fDisposing);
		}


		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			if (!_useCache)
				return false;

			var imageFile = GetLocalPathWithoutQuery(info);

			// only process images
			var isSvg = imageFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
			if (!IsImageTypeThatCanBeDegraded(imageFile) && !isSvg)
				return false;

			imageFile = imageFile.Replace("thumbnail", "");

			var processImage = !isSvg;

			// This happens with the new way we are serving css files
			if (!RobustFile.Exists(imageFile))
			{
				var fileName = Path.GetFileName(imageFile);
				var sourceDir = FileLocator.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot);
				imageFile = Directory.EnumerateFiles(sourceDir, fileName, SearchOption.AllDirectories).FirstOrDefault();

				// image file not found
				if (string.IsNullOrEmpty(imageFile)) return false;

				// BL-2368: Do not process files from the BloomBrowserUI directory. These files are already in the state we
				//          want them. Running them through _cache.GetPathToResizedImage() is not necessary, and in PNG files
				//          it converts all white areas to transparent. This is resulting in icons which only contain white
				//          (because they are rendered on a dark background) becoming completely invisible.
				processImage = false;
			}

			if (processImage)
			{
				// thumbnail requests have the thumbnail parameter set in the query string
				var thumb = info.GetQueryParameters()["thumbnail"] != null;
				imageFile = _cache.GetPathToResizedImage(imageFile, thumb);

				if (string.IsNullOrEmpty(imageFile)) return false;
			}

			info.ReplyWithImage(imageFile);
			return true;
		}

		protected static bool IsImageTypeThatCanBeDegraded(string path)
		{
			var extension = Path.GetExtension(path);
			if(!string.IsNullOrEmpty(extension))
				extension = extension.ToLower();
			//note, we're omitting SVG
			return (new[] { ".png", ".jpg", ".jpeg"}.Contains(extension));
		}

		static HashSet<string> _imageExtentions = new HashSet<string>(new[] { ".jpg", "jpeg", ".png", ".svg" });

		internal static bool IsImageTypeThatCanBeReturned(string path)
		{
			return _imageExtentions.Contains((Path.GetExtension(path) ?? "").ToLowerInvariant());
		}
	}
}
