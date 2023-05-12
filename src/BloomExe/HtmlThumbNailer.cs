using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.ImageProcessing;
using Bloom.Api;
using SIL.Reporting;
using Bloom.Book;
using Bloom.Properties;
using SIL.IO;

namespace Bloom
{
	public class HtmlThumbNailer : IDisposable
	{
		readonly Dictionary<string, Image> _images = new Dictionary<string, Image>();

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
		private Dictionary<string, Browser> _browserCacheForDifferentPaperSizes = new Dictionary<string, Browser>();

		private bool _disposed;

		public HtmlThumbNailer()
		{
			if (_theOnlyOneAllowed != null)
			{
				Debug.Fail("Something tried to make a second HtmlThumbnailer; there should only be one.");
				throw new ApplicationException("Something tried to make a second HtmlThumbnailer; there should only be one.");
			}

			_theOnlyOneAllowed = this;

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
			public bool MustRegenerate = false; // true if a cached image may not be returned.
			public Guid RequestId;
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
				if (!options.MustRegenerate && !String.IsNullOrWhiteSpace(key) && _images.TryGetValue(key, out image))
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
		/// <param name="folderForThumbNailCache"></param>
		/// <param name="key">whatever system you want... just used for caching</param>
		/// <param name="document"></param>
		/// <param name="options"></param>
		/// <param name="callback"></param>
		/// <param name="errorCallback"></param>
		/// <returns></returns>
		public void GetThumbnailAsync(string folderForThumbNailCache, string key, HtmlDom document,
			ThumbnailOptions options, Action<Image> callback, Action<Exception> errorCallback)
		{
			GetThumbnail(folderForThumbNailCache, key, document, options, callback, errorCallback, true);
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="folderForThumbNailCache"></param>
		/// <param name="key">whatever system you want... just used for caching</param>
		/// <param name="document"></param>
		/// <param name="options"></param>
		/// <param name="callback"></param>
		/// <param name="errorCallback"></param>
		/// <param name="async"></param>
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
			if (!string.IsNullOrWhiteSpace(key) && _images.TryGetValue(key, out image))
			{
				callback(image);
				return;
			}

			//Sitting on disk?
			if (!string.IsNullOrEmpty(folderForThumbNailCache))
			{
				if (RobustFile.Exists(thumbNailFilePath))
				{
					var thumbnail = ToPalaso.RobustImageIO.GetImageFromFile(thumbNailFilePath);
					thumbnail.Tag = thumbNailFilePath;
					if (!string.IsNullOrWhiteSpace(key))
						_images.Add(key, thumbnail);
					callback(thumbnail);
					return;
				}
			}

			var order = new ThumbnailOrder {
				ThumbNailFilePath = thumbNailFilePath,
				Options = options,
				Callback = callback,
				ErrorCallback = errorCallback,
				Document = document,
				FolderForThumbNailCache = folderForThumbNailCache,
				CancelToken = Program.BloomThreadCancelService,
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

		public void CancelOrder(Guid requestId)
		{
			var order = _currentOrder; // local copy in case another thread changes it
			if (order != null && order.Options.RequestId == requestId && order.WaitHandle != null)
			{
				order.Canceled = true;
				order.WaitHandle.Set();
			}
			// enhance: could search PendingOrders for matching items. However, currently
			// cancellation is only used for synchronous orders.
		}

		private bool OpenTempFileInBrowser(Browser browser, HtmlDom dom)
		{
			var order = (ThumbnailOrder) browser.Tag;
			if (_syncControl.InvokeRequired)
			{
				// Note: the new WebView2 code here had some testing before, in the course of changing other things,
				// we stopped calling this method on background threads. There have been further changes to the code
				// since then, particularly, we stopped passing a filePath (really a URL) argument and switched
				// to creating the in memory page file here (or in NavigateAndWaitTillDonw).
					bool success = false;
					_syncControl.Invoke((Action)(() =>
					{
						success =browser.NavigateAndWaitTillDone(dom, 100000, InMemoryHtmlFileSource.Thumb, null, false);
						if (!success)
						{
							Logger.WriteEvent("HtmlThumbNailer ({1}): Timed out on ({0})", order.ThumbNailFilePath,
								Thread.CurrentThread.ManagedThreadId);
#if DEBUG
							if (!_thumbnailTimeoutAlreadyDisplayed)
							{
								_thumbnailTimeoutAlreadyDisplayed = true;
								_syncControl.Invoke((Action)(() =>
									Debug.Fail("(debug only) Make thumbnail timed out (won't show again)")));
							}
#endif
						}
					}));
					return true;
			}
			else
			{
				using (var temp = BloomServer.MakeInMemoryHtmlFileInBookFolder(order.Document,
					       source: InMemoryHtmlFileSource.Thumb))
				{
					browser.Navigate(temp.Key, false);

					while (!order.Done)
					{
						Application.DoEvents();
						Application.RaiseIdle(
							new EventArgs()); // needed on Linux to avoid deadlock starving browser navigation
					}
				}
			}

			return true;
		}

		private Size SetWidthAndHeight(Browser browser)
		{
			try
			{
				if (_syncControl.InvokeRequired)
				{
					return (Size)_syncControl.Invoke(new Func<Browser, Size>(SetWidthAndHeight), browser);
				}
			}
			catch (Exception e)
			{
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Could not make thumbnail", "Ref bl-524", e);
				return new Size(0, 0); // this tells the caller we failed
			}

			var height =
				(int)Math.Round(double.Parse(
					browser.RunJavaScript("document.getElementsByClassName('bloom-page')[0].clientHeight.toString()")));
			var width = (int)Math.Round(
				double.Parse(browser.RunJavaScript("document.getElementsByClassName('bloom-page')[0].clientWidth.toString()")));

			browser.Height = height;
			browser.Width = width;
			// This is probably not needed...width zero came about in debugging incomplete code where a stylesheet
			// did not take effect..but it seems like a reasonable bit of defensive programming to keep.
			if (browser.Width == 0)
				browser.Width = browser.Height / 2; // arbitrary way of avoiding crash
			return new Size(browser.Width, browser.Height);
		}

		private Image CreateImage(Browser browser, Color coverColor, int top, int bottom)
		{
			if (_syncControl.InvokeRequired)
			{
				return (Image) _syncControl.Invoke(new Func<Browser, Color, int, int, Image>(CreateImage), browser,
					coverColor, top, bottom);
			}
			// It's REALLY tricky to get the thumbnail created so that it reliably shows the image.
			// See BL-4170 and then BL-6257. We tried all kinds of tricks to tell when the image is
			// loaded into the document, such as checking image.completed and image.naturalWidth > 0
			// and waiting for two cycles of window.requestAnimationFrame (which is supposed to cover
			// starting and completing painting images) after the onload event fires. None of this
			// helped appreciably. A 400ms delay was enough for most images on a fast desktop, but
			// there was no way to tell how long would be enough on a slow laptop.
			// So, we finally came up with this technique, which is to examine the actual bitmap
			// that is produced to see whether there is something drawn in the image region of the
			// page. Even that did not prove to be enough: it's quite possible (especially with a tall
			// PNG image) for CanvasGetPngImage to return an image with the cover picture only partly
			// drawn. Fortunately, it seems to consistently draw from the top down, so we can be pretty
			// sure there is no more image to come if we do two cycles and there is no change in the
			// last line that is drawn.
			// On a typical tall PNG this loop has three iterations.
			// image is partly drawn (~40ms); ~140ms to find last line
			// image is fully drawn (~80ms); 1ms to find last line
			// image is fully drawn again (~28ms); 1ms to find last line
			// On a typical wide png, 2 iterations:
			// ~40 to draw image, ~80 to find last line (and repeat)
			// For jpg we seem to typically have three also:
			// ~20ms to make image; ~200ms to determine nothing is there
			// ~600ms to make image; ~40 to find last line
			// ~120 to redraw; ~40 to find last line
			// Todo WebView2: I don't know whether all this complication is needed in WebView2, or whether
			// its document-completed event is enough so that we can get a good image, or perhaps
			// its GetPreview is good enough to wait till everything is drawn. For now, I'm keeping things
			// as similar as possible on both tracks.
			// Note: This code seems designed for a cover, with only one image. I see no reason why, if we
			// really still need this, we might not need to make sure that all images are fully drawn.
			// So if it really is still relevant, it might need some work. However, this is of much more
			// minor importance now that it's only used for thumbnails of new template pages rather than
			// the main thumbnail of a book.
			while (true) // Exit when we settle on an image and return it.
			{
				var watch = new Stopwatch();
				watch.Start();
				int lastLineOfImage = -1;
				while (true)
				{

					using (var image = browser.GetPreview())
					{
						if (watch.ElapsedMilliseconds > 5000)
						{
							// Maybe there's no image or it's color perfectly mathches the background?
							// When we used to do this with a simple delay 400ms was usually enough;
							// if we can't get it in 5s give up, and use whatever we've got.
							watch.Stop();
							Debug.WriteLine("returned possibly incomplete thumnail after more than 5000ms");
							return new Bitmap(image);
						}

						if (top < 0)
						{
							// no image to wait for, go with what we have.
							return new Bitmap(image);
						}

						// I'm not sure how top or bottom can get to be greater than image.Height, but if they do,
						// it causes a crash. It makes no sense to look for the last line drawn beyond the end of the
						// image, so if something tries, just start looking at the bottom.
						var newLastLine = GetLastLineOfImage(coverColor, Math.Min(top, image.Height),
							Math.Min(bottom, image.Height), image);

						// If nothing has been drawn yet, we want to keep trying until something is.
						// If something has been drawn, we want to keep trying until a cycle when
						// nothing more gets added.
						if (newLastLine == -1 || newLastLine > lastLineOfImage)
						{
							lastLineOfImage = newLastLine;
							// This is meant to give the browser more of a chance to load it.
							// It may well have been working on it anyway in another thread
							// while we were checking pixels. Not too sure about the best length
							// for this delay; longer might mean less time wasted checking pixels,
							// but the minimum delay is two times this interval, so we don't want
							// it too long. 50ms, at least on my desktop, usually doesn't result
							// in any wasted iterations, nor much delay.
							Thread.Sleep(50);
							continue; // try again
						}

						// No more image drawn than the last iteration, and we got something, so assume we have the whole thing.
						watch.Stop();
						Debug.WriteLine("Got image after waiting " + watch.ElapsedMilliseconds);
						return new Bitmap(image);
					}
				}
			}
		}

		// Find the last line of the image between top and bottom which contains a pixel not
		// matching coverColor. If no such pixel is found, return -1.
		// (If there's no picture on the cover, somehow, top and bottom will be -1, and this
		// routine will return -1 after doing no iterations. We'll give up after 5s.
		// Could make this special case faster, but it complicates things for little benefit.
		// Bloom makes it pretty hard to have no cover picture. It may not even be possible.
		private static int GetLastLineOfImage(Color coverColor, int top, int bottom, Bitmap image)
		{
			for (int i = bottom - 1; i >= top; i--)
			{
				for (int j = 0; j < image.Width; j++)
				{
					var color = image.GetPixel(j, i);
					if (color != coverColor)
						return i;
				}
			}

			return -1;
		}

		// The order we most recently started working on.
		// I'm not sure we can't be working on more than one at a time, so use with care.
		// Currently only used to try to abort orders; if that fails, it's not too serious.
		private ThumbnailOrder _currentOrder;

		/// <summary>
		/// Returns true if it make some attempt at an image, false if navigation is currently suppressed.
		/// </summary>
		/// <param name="order"></param>
		/// <param name="browser"></param>
		/// <param name="thumbnail"></param>
		/// <returns></returns>
		private bool CreateThumbNail(ThumbnailOrder order, Browser browser, out Image thumbnail)
		{
			// runs on threadpool thread
			_currentOrder = order;
			thumbnail = null;

			order.Done = false;
			browser.Tag = order;
			Color coverColor;
			ImageUtils.TryCssColorFromString(Book.Book.GetCoverColorFromDom(order.Document.RawDom), out coverColor);
			if (!OpenTempFileInBrowser(browser, order.Document))
				return false;

			var browserSize = SetWidthAndHeight(browser);
			if (browserSize.Height == 0) //happens when we run into the as-yet-unreproduced-or-fixed bl-254
				return false; // will try again later

			try
			{
				Logger.WriteMinorEvent("HtmlThumbNailer.CreateThumbNail: (threadId={2}) width={0} height={1}", browserSize.Width,
					(uint)browserSize.Height,
					Thread.CurrentThread.ManagedThreadId);

				if (browserSize.Width == 0 || browserSize.Height == 0)
				{
					var paperSizeName = GetPaperSizeName(order.Document.RawDom);
					throw new ApplicationException("Problem getting thumbnail browser for document with Paper Size: " +
					                               paperSizeName);
				}

				int topOfCoverImage = -1;
				int bottomOfCoverImage = -1;
				_syncControl.Invoke((Action)(() =>
				{
					// There's probably some way to get all of this in a single RunJavaScript call, but note that ?. is not supported
					// in GeckoFx60, and I've had some trouble with scripts of more than a single expression in WebView2.
					var imageCount =
						int.Parse(browser.RunJavaScript(
							"document.getElementsByClassName('bloom-imageContainer').length.toString()"));
					if (imageCount != 0)
					{
						topOfCoverImage = (int)Math.Round(double.Parse(
							browser.RunJavaScript(
								"document.getElementsByClassName('bloom-imageContainer')[0].offsetTop.toString()")));
						bottomOfCoverImage = topOfCoverImage + (int)Math.Round(double.Parse(
							browser.RunJavaScript(
								"document.getElementsByClassName('bloom-imageContainer')[0].offsetHeight.toString()")));
					}
				}));

				using (Image fullsizeImage = CreateImage(browser, coverColor, topOfCoverImage, bottomOfCoverImage))
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
							"While trying to dispose of thumbnailer browsers as a result of an exception, go another: " +
							e2.Message);
					}
				}

				_browserCacheForDifferentPaperSizes.Clear();
#if DEBUG
				_syncControl.Invoke((Action)(() => Debug.Fail(error.Message)));
#endif
			}

			return false;
		}

		private void ProcessOrder(object stateInfo)
		{
			// called on threadpool thread
			var order = stateInfo as ThumbnailOrder;
			if (order == null)
				return;

			try
			{	// This doesn't have an IsDisposed() method, so catch a possible exception.
				// Note that tests won't have a order.CancelToken set.
				if (order.CancelToken != null && order.CancelToken.IsCancellationRequested)
					return;
			}
			catch (ObjectDisposedException)
			{
				return;
			}

			Image pendingThumbnail = null;

			// Don't try to make thumbnails on the UI thread. It's easy to get into a situation like BL-6208,
			// where the UI thread is waiting for this lock, but another thread that holds the lock is waiting
			// to do something (e.g., navigate a browser) that can only be done on the UI thread.
			Debug.Assert(!Program.RunningOnUiThread, "Thumbnails must not be made on the UI thread");

			lock (this)
			{
				Logger.WriteMinorEvent("HtmlThumbNailer ({1}): starting work on thumbnail ({0})", order.ThumbNailFilePath,
					Thread.CurrentThread.ManagedThreadId);

				_backgroundColorOfResult = order.Options.BackgroundColor;
				XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(order.Document.RawDom);

				var browser = GetBrowserForPaperSize(order.Document.RawDom);
				if (browser == null)
					return;

				if (!CreateThumbNail(order, browser, out pendingThumbnail) && !order.Canceled)
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
							RobustImageIO.SaveImage(b, order.ThumbNailFilePath);
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

		private void _browser_OnDocumentCompleted(object sender, EventArgs geckoDocumentCompletedEventArgs)
		{
			Debug.WriteLine("_browser_OnDocumentCompleted ({0})", Thread.CurrentThread.ManagedThreadId);
			var order = (ThumbnailOrder)((Browser)sender).Tag;
			order.Done = true;
			if (order.WaitHandle != null)
				order.WaitHandle.Set();
		}

		private Browser GetBrowserForPaperSize(XmlDocument document)
		{
			var paperSizeName = GetPaperSizeName(document);

			Browser b;
			if (_browserCacheForDifferentPaperSizes.TryGetValue(paperSizeName, out b))
				return b;

			if (_syncControl.InvokeRequired)
			{
				b = (Browser)_syncControl.Invoke(new Func<Browser>(MakeNewBrowser));
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


		private Browser MakeNewBrowser()
		{
			Debug.WriteLine("making browser for HtmlThumbNailer ({0})", Thread.CurrentThread.ManagedThreadId);
			var browser = BrowserMaker.MakeBrowser();
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
		public CancellationTokenSource CancelToken;
		public bool Canceled;
	}
}
