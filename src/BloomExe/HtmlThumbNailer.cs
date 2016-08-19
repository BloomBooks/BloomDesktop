using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.ImageProcessing;
using Bloom.Api;
using DesktopAnalytics;
using Gecko;
using Gecko.Events;
using Gecko.Utils;
using SIL.Code;
using SIL.Reporting;
using Bloom.Book;
using Bloom.Properties;
using BloomTemp;
using SIL.IO;

namespace Bloom
{
	public class HtmlThumbNailer : IDisposable
	{
		Dictionary<string, Image> _images = new Dictionary<string, Image>();

		// This is used to synchronize browser access between different
		// instances of Gecko which are used in various classes.
		private readonly NavigationIsolator _isolator;
		private Color _backgroundColorOfResult;
		private static HtmlThumbNailer _theOnlyOneAllowed;

		/// <summary>
		/// This controls helps to call geckofx methods on the correct thread (UI thread).
		/// We expect that our c'tor gets called on the UI thread and since we then create
		/// _syncControl on the UI thread we can use it to invoke geckofx methods on the UI thread.
		/// </summary>
		private Control _syncControl;

		/// <summary>
		/// This is to overcome a problem with XULRunner 1.9 (or my use of it)this will always give
		/// us the size it was on the first page we navigated to, so that if the book size changes,
		/// our thumbnails are all wrong.
		/// </summary>
		private Dictionary<string, GeckoWebBrowser> _browserCacheForDifferentPaperSizes = new Dictionary<string, GeckoWebBrowser>();

		private bool _disposed;

		public HtmlThumbNailer(NavigationIsolator isolator)
		{
			if (_theOnlyOneAllowed != null)
			{
				Debug.Fail("Something tried to make a second HtmlThumbnailer; there should only be one.");
				throw new ApplicationException("Something tried to make a second HtmlThumbnailer; there should only be one.");
			}

			_theOnlyOneAllowed = this;

			_isolator = isolator;

			_syncControl = new Control();
			_syncControl.CreateControl();
		}

		public void RemoveFromCache(string key)
		{
			if (_images.ContainsKey(key))
			{
				_images.Remove(key);
			}
		}

		public class ThumbnailOptions
		{
			public const int DefaultHeight = 70;
			public enum BorderStyles
			{
				Solid, Dashed, None
			}
			public Color BackgroundColor = Color.White;
			public Color BorderColor = Color.Transparent;
			public BorderStyles BorderStyle;

			/// <summary>
			/// Use this when all thumbnails need to be the centered in the same size png.
			/// Unfortunately as long as we're using the winform listview, we seem to need to make the icons
			/// the same size otherwise the title-captions don't line up.
			/// </summary>
			public bool CenterImageUsingTransparentPadding = true;

			public int Width = 70;
			public int Height = DefaultHeight;
			public string FileName = "thumbnail.png";
		}


		/// <summary>
		/// A synchronous version of getting thumbnails, currently used by the image server
		/// to get thumbnails that are used in the add page dialog.
		/// </summary>
		/// <param name="key">Used to retrieve the thumbnail from a dictionary if we are asked for
		/// the same one repeatedly</param>
		/// <param name="document">Whose rendering will produce the thumbnail content.</param>
		/// <param name="options"></param>
		/// <returns></returns>
		public Image GetThumbnail(string key, HtmlDom document, ThumbnailOptions options)
		{
			Image image;
			Image thumbnail = null;
			lock (this)
			{
				//In our cache?
				if (!String.IsNullOrWhiteSpace(key) && _images.TryGetValue(key, out image))
				{
					Debug.WriteLine("Thumbnail Cache HIT: "+ key + " thread=" + Thread.CurrentThread.ManagedThreadId);
					return image;
				}
				Debug.WriteLine("Thumbnail Cache MISS: " + key + " thread=" + Thread.CurrentThread.ManagedThreadId);

				_backgroundColorOfResult = options.BackgroundColor;
				XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(document.RawDom);

				var browser = GetBrowserForPaperSize(document.RawDom);
				if (browser == null)
					return Resources.PagePlaceHolder;

				var order = new ThumbnailOrder()
				{
					Options = options,
					Document = document
				};
				for (int i = 0; i < 4; i++)
				{
					if (CreateThumbNail(order, browser, out thumbnail))
						break;
					// For some reason...possibly another navigation was in progress...we can't do this just now.
					// Try a few times.
				}
				if (thumbnail == null) // just can't get it.
				{
					return Resources.PagePlaceHolder; // but don't save it...try again if we get another request.
				}
				if (!String.IsNullOrWhiteSpace(key))
					_images[key] = thumbnail;
			}
			return thumbnail;
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="key">whatever system you want... just used for caching</param>
		/// <param name="document"></param>
		/// <param name="backgroundColorOfResult">use Color.Transparent if you'll be composing in onto something else</param>
		/// <param name="drawBorderDashed"></param>
		/// <returns></returns>
		public void GetThumbnailAsync(string folderForThumbNailCache, string key, HtmlDom document,
			ThumbnailOptions options, Action<Image> callback, Action<Exception> errorCallback)
		{
			GetThumbnail(folderForThumbNailCache, key, document, options, callback, errorCallback, true);
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="key">whatever system you want... just used for caching</param>
		/// <param name="document"></param>
		/// <param name="backgroundColorOfResult">use Color.Transparent if you'll be composing in onto something else</param>
		/// <param name="drawBorderDashed"></param>
		/// <returns></returns>
		public void GetThumbnail(string folderForThumbNailCache, string key, HtmlDom document,
			ThumbnailOptions options, Action<Image> callback, Action<Exception> errorCallback, bool async)
		{
			//review: old code had it using "key" in one place(checking for existing), thumbNailFilePath in another (adding new)

			string thumbNailFilePath = null;
			if (!string.IsNullOrEmpty(folderForThumbNailCache))
				thumbNailFilePath = Path.Combine(folderForThumbNailCache, options.FileName);

			//In our cache?
			Image image;
			if (!String.IsNullOrWhiteSpace(key) && _images.TryGetValue(key, out image))
			{
				callback(image);
				return;
			}

			//Sitting on disk?
			if (!string.IsNullOrEmpty(folderForThumbNailCache))
			{
				if (RobustFile.Exists(thumbNailFilePath))
				{
					var thumbnail = ImageUtils.GetImageFromFile(thumbNailFilePath);
					thumbnail.Tag = thumbNailFilePath;
					if (!String.IsNullOrWhiteSpace(key))
						_images.Add(key, thumbnail);
					callback(thumbnail);
					return;
				}
			}

			var order = new ThumbnailOrder() {
				ThumbNailFilePath = thumbNailFilePath,
				Options = options,
				Callback = callback,
				ErrorCallback = errorCallback,
				Document = document,
				FolderForThumbNailCache = folderForThumbNailCache,
				Key = key
			};
			if (async)
				QueueOrder(order);
			else
			{
				ProcessOrder(order);
			}
		}

		private void QueueOrder(ThumbnailOrder order)
		{
			ThreadPool.QueueUserWorkItem(new WaitCallback(ProcessOrder), order);
		}

#if DEBUG
		private static bool _thumbnailTimeoutAlreadyDisplayed;
#endif

		private bool OpenTempFileInBrowser(GeckoWebBrowser browser, string filePath)
		{
			var order = (ThumbnailOrder) browser.Tag;
			bool navigationHappened = true;
			if (_syncControl.InvokeRequired)
			{
				using (var waitHandle = new AutoResetEvent(false))
				{
					order.WaitHandle = waitHandle;
					_syncControl.BeginInvoke(new Action<string>(path =>
					{
						if (!_isolator.NavigateIfIdle(browser, path))
							navigationHappened = false; // some browser is busy, try again later.
					}), filePath);
					waitHandle.WaitOne(10000);
				}
				if (_disposed || !navigationHappened)
					return false;
				if (!order.Done)
				{
					Logger.WriteEvent("HtmlThumbNailer ({1}): Timed out on ({0})", order.ThumbNailFilePath,
						Thread.CurrentThread.ManagedThreadId);
#if DEBUG
				if (!_thumbnailTimeoutAlreadyDisplayed)
				{
					_thumbnailTimeoutAlreadyDisplayed = true;
					_syncControl.Invoke((Action) (() => Debug.Fail("(debug only) Make thumbnail timed out (won't show again)")));
				}
#endif
					return false;
				}
			}
			else
			{
				_isolator.Navigate(browser, filePath); // main thread, not async, we really need it now.
				while (!order.Done)
					Application.DoEvents();
			}

			return true;
		}

		private Size SetWidthAndHeight(GeckoWebBrowser browser)
		{
			try
			{
				if (_syncControl.InvokeRequired)
				{
					return (Size) _syncControl.Invoke(new Func<GeckoWebBrowser, Size>(SetWidthAndHeight), browser);
				}
			}
			catch (Exception e)
			{
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Could not make thumbnail","Ref bl-524", e);
				return new Size(0,0); // this tells the caller we failed
			}

			Guard.AgainstNull(browser.Document.ActiveElement, "browser.Document.ActiveElement");
			var div = browser.Document.ActiveElement.EvaluateXPath("//div[contains(@class, 'bloom-page')]").GetNodes().FirstOrDefault() as GeckoElement;
						if (div == null)
						{
				var order = (ThumbnailOrder)browser.Tag;
				Logger.WriteEvent("HtmlThumNailer ({1}):  found no div with a class of bloom-Page ({0})", order.ThumbNailFilePath,
					Thread.CurrentThread.ManagedThreadId);
							throw new ApplicationException("thumbnails found no div with a class of bloom-Page");
						}
						browser.Height = div.ClientHeight;
						browser.Width = div.ClientWidth;
			// This is probably not needed...width zero came about in debugging incomplete code where a stylesheet
			// did not take effect..but it seems like a reasonable bit of defensive programming to keep.
			if (browser.Width == 0)
				browser.Width = browser.Height/2; // arbitrary way of avoiding crash
			return new Size(browser.Width, browser.Height);
		}

		private Image CreateImage(GeckoWebBrowser browser)
						{
			if (_syncControl.InvokeRequired)
			{
				return (Image)_syncControl.Invoke(new Func<GeckoWebBrowser, Image>(CreateImage), browser);
			}

#if __MonoCS__
			var offscreenBrowser = browser as OffScreenGeckoWebBrowser;
			Debug.Assert(offscreenBrowser != null);

			return offscreenBrowser.GetBitmap(browser.Width, browser.Height);
#else
			var creator = new ImageCreator(browser);
			byte[] imageBytes = creator.CanvasGetPngImage((uint)browser.Width, (uint)browser.Height);
			// Ensure image is still valid after the MemoryStream closes.
			using (var stream = new MemoryStream(imageBytes))
			{
				using (var image = new Bitmap(stream))
				{
					return new Bitmap((image));
				}
			}
#endif
		}

		/// <summary>
		/// Returns true if it make some attempt at an image, false if navigation is currently suppressed.
		/// </summary>
		/// <param name="order"></param>
		/// <param name="browser"></param>
		/// <param name="thumbnail"></param>
		/// <returns></returns>
		private bool CreateThumbNail(ThumbnailOrder order, GeckoWebBrowser browser, out Image thumbnail)
		{
			// runs on threadpool thread
			thumbnail = null;
			using (var temp = EnhancedImageServer.MakeSimulatedPageFileInBookFolder(order.Document))
			{
				order.Done = false;
				browser.Tag = order;
				if (!OpenTempFileInBrowser(browser, temp.Key))
					return false;

				var browserSize = SetWidthAndHeight(browser);
				if (browserSize.Height == 0) //happens when we run into the as-yet-unreproduced-or-fixed bl-254
					return false; // will try again later

				try
				{
					Logger.WriteMinorEvent("HtmlThumNailer ({2}): browser.GetBitmap({0},{1})", browserSize.Width,
						(uint) browserSize.Height,
						Thread.CurrentThread.ManagedThreadId);
					//BUG (April 2013) found that the initial call to GetBitMap always had a zero width, leading to an exception which
					//the user doesn't see and then all is well. So at the moment, we avoid the exception, and just leave with
					//the placeholder thumbnail.
					if (browserSize.Width == 0 || browserSize.Height == 0)
					{
						var paperSizeName = GetPaperSizeName(order.Document.RawDom);
						throw new ApplicationException("Problem getting thumbnail browser for document with Paper Size: " + paperSizeName);
					}
					using (Image fullsizeImage = CreateImage(browser))
					{
						if (_disposed)
							return false;
						thumbnail = MakeThumbNail(fullsizeImage, order.Options);
						return true;
					}
				}
				catch (Exception error)
				{
					Logger.WriteEvent("HtmlThumbNailer ({0}) got {1}", Thread.CurrentThread.ManagedThreadId, error.Message);
					Logger.WriteEvent("Disposing of all browsers in hopes of getting a fresh start on life");
					foreach (var browserCacheForDifferentPaperSize in _browserCacheForDifferentPaperSizes)
					{
						try
						{
							Logger.WriteEvent("Disposing of browser {0}", browserCacheForDifferentPaperSize.Key);
							browserCacheForDifferentPaperSize.Value.Dispose();
						}
						catch (Exception e2)
						{
							Logger.WriteEvent(
								"While trying to dispose of thumbnailer browsers as a result of an exception, go another: " + e2.Message);
						}
					}
					_browserCacheForDifferentPaperSizes.Clear();
#if DEBUG
					_syncControl.Invoke((Action) (() => Debug.Fail(error.Message)));
#endif
				}
			}
			return false;
		}

		private void ProcessOrder(object stateInfo)
		{
			// called on threadpool thread
			var order = stateInfo as ThumbnailOrder;
			if (order == null)
				return;

			if (Program.ApplicationExiting)
				return;

			Image pendingThumbnail = null;

			lock (this)
			{
				Logger.WriteMinorEvent("HtmlThumbNailer ({1}): starting work on thumbnail ({0})", order.ThumbNailFilePath,
					Thread.CurrentThread.ManagedThreadId);

				_backgroundColorOfResult = order.Options.BackgroundColor;
				XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(order.Document.RawDom);

				var browser = GetBrowserForPaperSize(order.Document.RawDom);
				if (browser == null)
					return;

				if (!CreateThumbNail(order, browser, out pendingThumbnail))
				{
					// For some reason...possibly another navigation was in progress...we can't do this just now.
					// Try it again later.
					// Enhance: should we have some limit after which we give up?
					QueueOrder(order);
				}
				if (pendingThumbnail == null)
				{
					pendingThumbnail = Resources.PagePlaceHolder;
				}
				else if (!string.IsNullOrEmpty(order.ThumbNailFilePath))
				{
					try
					{
						//gives a blank         _pendingThumbnail.Save(thumbNailFilePath);
						using (Bitmap b = new Bitmap(pendingThumbnail))
						{
							SIL.IO.RobustIO.SaveImage(b, order.ThumbNailFilePath);
						}
					}
					catch (Exception)
					{
						//this is going to fail if we don't have write permission
					}
				}

				pendingThumbnail.Tag = order.ThumbNailFilePath; //usefull if we later know we need to clear out that file

				Debug.WriteLine("Thumbnail created with dimensions ({0},{1})", browser.Width, browser.Height);

				try
				//I saw a case where this threw saying that the key was already in there, even though back at the beginning of this function, it wasn't.
				{
					if (_images.ContainsKey(order.Key))
						_images.Remove(order.Key);
					_images.Add(order.Key, pendingThumbnail);
				}
				catch (Exception error)
				{
					Logger.WriteMinorEvent("Skipping minor error: " + error.Message);
					//not worth crashing over, at this point in Bloom's life, since it's just a cache. But since then, I did add a lock() around all this.
				}
			}

			//order.ResultingThumbnail = pendingThumbnail;
			if (_disposed)
				return;
			Logger.WriteMinorEvent("HtmlThumNailer ({1}): finished work on thumbnail ({0})", order.ThumbNailFilePath,
				Thread.CurrentThread.ManagedThreadId);
			order.Callback(pendingThumbnail);
		}

		private void _browser_OnDocumentCompleted(object sender, GeckoDocumentCompletedEventArgs geckoDocumentCompletedEventArgs)
		{
			Debug.WriteLine("_browser_OnDocumentCompleted ({0})", Thread.CurrentThread.ManagedThreadId);
			var order = (ThumbnailOrder)((GeckoWebBrowser)sender).Tag;
			order.Done = true;
			if (order.WaitHandle != null)
				order.WaitHandle.Set();
		}

		private GeckoWebBrowser GetBrowserForPaperSize(XmlDocument document)
		{
			var paperSizeName = GetPaperSizeName(document);

			GeckoWebBrowser b;
			if (_browserCacheForDifferentPaperSizes.TryGetValue(paperSizeName, out b))
				return b;

			if (_syncControl.InvokeRequired)
			{
				b = (GeckoWebBrowser)_syncControl.Invoke(new Func<GeckoWebBrowser>(MakeNewBrowser));
			}
			else
				b = MakeNewBrowser();

			if (b != null)
				_browserCacheForDifferentPaperSizes.Add(paperSizeName, b);
			return b;
		}

		private static string GetPaperSizeName(XmlDocument document)
		{
			string paperSizeName = SizeAndOrientation.GetSizeAndOrientation(document, "A5Portrait").ToString();
			return paperSizeName;
		}


		private GeckoWebBrowser MakeNewBrowser()
		{
			if (Program.ApplicationExiting)
				return null;

			Debug.WriteLine("making browser ({0})", Thread.CurrentThread.ManagedThreadId);
#if !__MonoCS__
			var browser = new GeckoWebBrowser();
#else
			var browser = new OffScreenGeckoWebBrowser();
#endif
			browser.CreateControl();
			browser.DocumentCompleted += _browser_OnDocumentCompleted;
			return browser;
		}

		private Image MakeThumbNail(Image bmp, ThumbnailOptions options)
		{
			if (bmp == null)
				return null;

			int contentWidth;
			int contentHeight;

#if !__MonoCS__
			int horizontalOffset = 0;
			int verticalOffset = 0;
			int thumbnailWidth = options.Width;
			int thumbnailHeight = options.Height;
#endif
			//unfortunately as long as we're using the winform listview, we seem to need to make the icons
			//the same size regardless of the book's shape, otherwise the title-captions don't line up.

			if (options.CenterImageUsingTransparentPadding)
			{
				if (bmp.Width > bmp.Height)//landscape
				{
					contentWidth = options.Width;
					contentHeight = (int)(Math.Ceiling(((float)bmp.Height / (float)bmp.Width) * (float)contentWidth));
				}
				else if (bmp.Width < bmp.Height) //portrait
				{
					contentHeight = options.Height;
					contentWidth = (int) (Math.Ceiling(((float) bmp.Width/(float) bmp.Height)*(float) contentHeight));
				}
				else //square page
				{
					contentWidth = options.Width;
					contentHeight = options.Height;
				}
#if !__MonoCS__
				horizontalOffset = (options.Width / 2) - (contentWidth / 2);
				verticalOffset = (options.Height / 2) - (contentHeight / 2);
#endif
			}
			else
			{
				contentHeight = options.Height;
				contentWidth = (int)Math.Floor((float)options.Height * (float)bmp.Width / (float)bmp.Height);
#if !__MonoCS__
				thumbnailHeight = contentHeight;
				thumbnailWidth = contentWidth;
#endif
			}

#if !__MonoCS__
			var thumbnail = new Bitmap(thumbnailWidth, thumbnailHeight, PixelFormat.Format64bppPArgb);
			using (var graphics = Graphics.FromImage(thumbnail))
			{
				graphics.PixelOffsetMode = PixelOffsetMode.None;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

				var destRect = new Rectangle(horizontalOffset, verticalOffset, contentWidth,contentHeight);

			   graphics.DrawImage(bmp,
						destRect,
						0,0, bmp.Width, bmp.Height, //source
						GraphicsUnit.Pixel, WhiteToBackground);

				if (options.BorderStyle != ThumbnailOptions.BorderStyles.None)
				{
					using (var pn = new Pen(Color.Black, 1))
					{
						if (options.BorderStyle == ThumbnailOptions.BorderStyles.Dashed)
						{
							pn.DashStyle = DashStyle.Dash;
							pn.Width = 2;
						}
					destRect.Height--; //hack, we were losing the bottom
						destRect.Width--;
						graphics.DrawRectangle(pn, destRect);
					}
				}
			}
			return thumbnail;
#else
			int skipMarginH = 30;
			int skipMarginV = 30;
			Bitmap croppedImage = (bmp as Bitmap).Clone(new Rectangle(new Point(skipMarginH, skipMarginV),
				new Size(bmp.Width - 2 * skipMarginH, bmp.Height - 2 * skipMarginV)), bmp.PixelFormat);
			return croppedImage.GetThumbnailImage(contentWidth, contentHeight, null, IntPtr.Zero);
#endif
		}


		/// <summary>
		/// Make the result look like it's on a colored paper, or make it transparent for composing on top
		/// of some other image.
		/// </summary>
		private ImageAttributes WhiteToBackground
		{
			get
			{
				ImageAttributes imageAttributes = new ImageAttributes();
				ColorMap map = new ColorMap();
				map.OldColor = Color.White;
				map.NewColor = _backgroundColorOfResult;
				imageAttributes.SetRemapTable(new ColorMap[] { map });
				return imageAttributes;
			}
		}

		/// <summary>
		/// How this page looks has changed, so remove from our cache
		/// </summary>
		/// <param name="id"></param>
		public void PageChanged(string id)
		{
			Image image;
			if (_images.TryGetValue(id, out image))
			{
				_images.Remove(id);
				if (image.Tag != null)
				{
					string thumbnailPath = image.Tag as string;
					if (!string.IsNullOrEmpty(thumbnailPath))
					{
						if (RobustFile.Exists(thumbnailPath))
						{
							try
							{
								RobustFile.Delete(thumbnailPath);
							}
							catch (Exception)
							{
								Debug.Fail("Could not delete path (would not see this in release version)");
								//oh well, couldn't delete it);
								throw;
							}
						}
					}
				}
				image.Dispose();
			}
		}

		public void Dispose()
		{
			_disposed = true;
			foreach (var browser in _browserCacheForDifferentPaperSizes)
			{
				if (browser.Value.InvokeRequired)
				{
					browser.Value.Invoke((Action)(() => {
						browser.Value.DocumentCompleted -= _browser_OnDocumentCompleted;
						browser.Value.Dispose();
					}));
				}
				else
				{
					browser.Value.DocumentCompleted -= _browser_OnDocumentCompleted;
					browser.Value.Dispose();
				}
			}
			_browserCacheForDifferentPaperSizes.Clear();
			_theOnlyOneAllowed = null;

			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// This is a trick that processes waiting for thumbnails can use in situations where
		/// Application.Idle is not being invoked. Such uses must pass a non-null control
		/// created in the thread where Application_Idle should be invoked (i.e., the UI thread)
		/// </summary>
		internal void Advance(Control invokeTarget)
		{
			// apparently obsolete
		}
	}

	public class ThumbnailOrder
	{
		public Image ResultingThumbnail;
		public Action<Image> Callback;
		public Action<Exception> ErrorCallback;
		public HtmlDom Document;
		public string FolderForThumbNailCache;
		public string Key;
		public bool Done;
		public string ThumbNailFilePath;
		public HtmlThumbNailer.ThumbnailOptions Options;
		public AutoResetEvent WaitHandle;
	}
}
