using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Bloom.web;
using Palaso.Code;
using Palaso.IO;
using Palaso.Reporting;
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
		private LowResImageCache _cache;
		private bool _useCache;

		public ImageServer(LowResImageCache cache)
		{
			_cache = cache;
			_useCache = Settings.Default.ImageHandler != "off";
		}

		protected override void Dispose(bool fDisposing)
		{
			//the container that gave us this will dispose of it: _cache.Dispose();
			_cache = null;

			base.Dispose(fDisposing);
		}

		protected override bool StartWithSetupIfNeeded(out Exception error)
		{
			var didStart = base.StartWithSetupIfNeeded(out error);

			if(!didStart)
			{
				var e = new ApplicationException("Could not start ImageServer", error);//passing this in will enable the details button
				ErrorReport.NotifyUserOfProblem(e, "What Happened{0}" +
					"Bloom could not start its image server, which keeps hi-res images from chewing up memory. You will still be able to work, but Bloom will take more memory, and hi-res images may not always show.{0}{0}" +
					"What caused this?{0}" +
					"Probably Bloom does not know how to get your specific {1} operating system to allow its image server to run.{0}{0}" +
					"What can you do?{0}" +
					"Click 'Details' and report the problem to the developers.", Environment.NewLine,
					Palaso.PlatformUtilities.Platform.IsWindows ? "Windows" : "Linux");
			}

			return didStart;
		}

		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			if (!_useCache)
				return false;

			var r = GetLocalPathWithoutQuery(info);
			if (r.EndsWith(".png") || r.EndsWith(".jpg"))
			{
				info.ContentType = r.EndsWith(".png") ? "image/png" : "image/jpeg";
				r = r.Replace("thumbnail", "");
				//if (r.Contains("thumb"))
				{
					if (File.Exists(r))
					{
						info.ReplyWithImage(_cache.GetPathToResizedImage(r));
						return true;
					}
				}
			}
			return false;
		}
	}
}
