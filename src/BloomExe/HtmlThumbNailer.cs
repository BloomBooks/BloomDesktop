using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Properties;
using BloomTemp;
using Palaso.Code;
using Palaso.Reporting;
using Palaso.Xml;
using Skybound.Gecko;

namespace Bloom
{
	public class HtmlThumbNailer: IDisposable
	{
		Dictionary<string, Image> _images = new Dictionary<string, Image>();
		private readonly int _sizeInPixels =70;
		private Color _backgroundColorOfResult;
		private bool _browserHandleCreated;
		private Queue<ThumbnailOrder> _orders= new Queue<ThumbnailOrder>();

		/// <summary>
		///This is to overcome a problem with XULRunner 1.9 (or my use of it)this will always give us the size it was on the first page we navigated to,
		//so that if the book size changes, our thumbnails are all wrong.
		/// </summary>
		private Dictionary<string, GeckoWebBrowser> _browserCacheForDifferentPaperSizes = new Dictionary<string, GeckoWebBrowser>();

		private bool _disposed;

		public HtmlThumbNailer(int sizeInPixels)
		{
			_sizeInPixels = sizeInPixels;
			Application.Idle += new EventHandler(Application_Idle);
		}

		void Application_Idle(object sender, EventArgs e)
		{
			if (_orders.Count > 0)
			{
				ProcessOrder(_orders.Dequeue());
			}
		}

		public void RemoveFromCache(string key)
		{
			if (_images.ContainsKey(key))
			{
				_images.Remove(key);
			}
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="key">whatever system you want... just used for caching</param>
		/// <param name="document"></param>
		/// <param name="backgroundColorOfResult">use Color.Transparent if you'll be composing in onto something else</param>
		/// <param name="drawBorderDashed"></param>
		/// <returns></returns>
		public void GetThumbnailAsync(string folderForThumbNailCache,string key, XmlDocument document, Color backgroundColorOfResult, bool drawBorderDashed, Action<Image> callback)
		{
			//review: old code had it using "key" in one place(checking for existing), thumbNailFilePath in another (adding new)

			string thumbNailFilePath = Path.Combine(folderForThumbNailCache, "thumbnail.png");

			//In our cache?
			Image image;
			if (_images.TryGetValue(key, out image))
			{
				callback(image);
				return;
			}

			//Sitting on disk?
			if (!string.IsNullOrEmpty(folderForThumbNailCache))
			{

				if (File.Exists(thumbNailFilePath))
				{
					//this FromFile thing locks the file until the image is disposed of. Therefore, we copy the image and dispose of the original.
					using (image = Image.FromFile(thumbNailFilePath))
					{
						var thumbnail = new Bitmap(image) { Tag = thumbNailFilePath };
						_images.Add(key, thumbnail);
						callback(thumbnail);
						return;
					}
				}
			}
			//!!!!!!!!! geckofx doesn't work in its own thread, so we're using the Application's idle event instead.

			_orders.Enqueue(new ThumbnailOrder()
							{
								ThumbNailFilePath = thumbNailFilePath,
								BackgroundColorOfResult = backgroundColorOfResult,
								Callback = callback,
								Document = document,
								DrawBorderDashed = drawBorderDashed,
								FolderForThumbNailCache = folderForThumbNailCache,
								Key = key
							});
		}

		void ProcessOrder(ThumbnailOrder order)
		{
			//e.Result = order;
			//Thread.CurrentThread.Name = "Thumbnailer" + order.Key;
			Image pendingThumbnail = null;

			lock (this)
			{
				_backgroundColorOfResult = order.BackgroundColorOfResult;
				XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(order.Document);


				var browser = GetBrowserForPaperSize(order.Document);
				lock (browser)
				{
					using (var temp = TempFile.CreateHtm5FromXml(order.Document))
					{
						order.Done = false;
						browser.Tag = order;

						browser.Navigate(temp.Path);

						var stopTime = DateTime.Now.AddSeconds(5);
						while (!_disposed && (!order.Done || browser.Document.ActiveElement == null )&& DateTime.Now < stopTime)
						{
							Application.DoEvents(); //TODO: avoid this
							//Thread.Sleep(100);
						}
						if (_disposed)
							return;
						if (!order.Done)
						{
							Debug.Fail("(debug only) Make thumbnail timed out");
							return;
						}

						Guard.AgainstNull(browser.Document.ActiveElement, "browser.Document.ActiveElement");
						var div = browser.Document.ActiveElement.GetElements("//div[contains(@class, 'bloom-page')]").First();
						if (div == null)
							throw new ApplicationException("thumbnails found now div with a class of bloom-Page");

						browser.Height = div.ScrollHeight;
						browser.Width = div.ScrollWidth;

						try
						{
							var docImage = browser.GetBitmap((uint) browser.Width, (uint) browser.Height);
							//docImage.Save(@"c:\dev\temp\zzzz.bmp");
							if (_disposed)
								return;
							pendingThumbnail = MakeThumbNail(docImage, _sizeInPixels, _sizeInPixels, Color.Transparent,
															 order.DrawBorderDashed);
						}
							// ReSharper disable EmptyGeneralCatchClause
						catch
							// ReSharper restore EmptyGeneralCatchClause
						{
						}
					}
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
							b.Save(order.ThumbNailFilePath);
						}
					}
					catch (Exception)
					{
						//this is going to fail if we don't have write permission
					}
				}

				pendingThumbnail.Tag = order.ThumbNailFilePath; //usefull if we later know we need to clear out that file

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
			Debug.WriteLine("Created new thumbnail: "+order.Key);
			//order.ResultingThumbnail = pendingThumbnail;
			if (_disposed)
				return;
			order.Callback(pendingThumbnail);
		}

		void _browser_Navigated(object sender, GeckoNavigatedEventArgs e)
		{
			ThumbnailOrder order = (ThumbnailOrder) ((GeckoWebBrowser) sender).Tag;
			order.Done = true;
		}


		private GeckoWebBrowser GetBrowserForPaperSize(XmlDocument document)
		{
		   string paperSizeName = SizeAndOrientation.GetSizeAndOrientation(document, "A5Portrait").ToString();

			GeckoWebBrowser b;
			if (!_browserCacheForDifferentPaperSizes.TryGetValue(paperSizeName, out b))
				{
					 b = MakeNewBrowser();
					 b.Navigated += new GeckoNavigatedEventHandler(_browser_Navigated);
					_browserCacheForDifferentPaperSizes.Add(paperSizeName, b);
				}
				return b;
		}

		private GeckoWebBrowser MakeNewBrowser()
		{
			Debug.WriteLine("making browser");

			var browser = new GeckoWebBrowser();
			browser.HandleCreated += new EventHandler(OnBrowser_HandleCreated);
			browser.CreateControl();
			var giveUpTime = DateTime.Now.AddSeconds(2);
			while (!_browserHandleCreated && DateTime.Now < giveUpTime)
			{
				//TODO: could lead to hard to reproduce bugs
				Application.DoEvents();
				Thread.Sleep(100);
			}
			return browser;
		}


		/// <summary>
		/// we need to wait for this to happen before we can proceed to navigate to the page
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void OnBrowser_HandleCreated(object sender, EventArgs e)
		{
			_browserHandleCreated =true;
		}



		private Image MakeThumbNail(Image bmp, int destinationWidth, int destinationHeight, Color borderColor, bool drawBorderDashed)
		{
			if (bmp == null)
				return null;
			//get the lesser of the desired and original size
			destinationWidth = bmp.Width > destinationWidth ? destinationWidth : bmp.Width;
			destinationHeight = bmp.Height > destinationHeight ? destinationHeight : bmp.Height;

			int actualWidth = destinationWidth;
			int actualHeight = destinationHeight;

			if (bmp.Width > bmp.Height)
				actualHeight = (int)(((float)bmp.Height / (float)bmp.Width) * actualWidth);
			else if (bmp.Width < bmp.Height)
				actualWidth = (int)(((float)bmp.Width / (float)bmp.Height) * actualHeight);

			int horizontalOffset = (destinationWidth / 2) - (actualWidth / 2);
			int verticalOffset = (destinationHeight / 2) - (actualHeight / 2);

#if MONO
//    this worked but didn't incorporate the offsets, so when it went back to the caller, it got displayed
//            out of proportion.
//            Image x = bmp.GetThumbnailImage(destinationWidth, destinationHeight, callbackOnAbort, System.IntPtr.Zero);
//            return x;


			Bitmap retBmp = new Bitmap(destinationWidth, destinationHeight);//, System.Drawing.Imaging.PixelFormat.Format64bppPArgb);
			Graphics grp = Graphics.FromImage(retBmp);
			//grp.PixelOffsetMode = PixelOffsetMode.None;
		 //guessing that this is the problem?   grp.InterpolationMode = InterpolationMode.HighQualityBicubic;

			grp.DrawImage(bmp, horizontalOffset, verticalOffset, actualWidth, actualHeight);

//            Pen pn = new Pen(borderColor, 1); //Color.Wheat
//
//
//            grp.DrawRectangle(pn, 0, 0, retBmp.Width - 1, retBmp.Height - 1);

			return retBmp;
#else

			Bitmap thumbnail = new Bitmap(destinationWidth, destinationHeight, System.Drawing.Imaging.PixelFormat.Format64bppPArgb);
			using (Graphics graphics = Graphics.FromImage(thumbnail))
			{
				graphics.PixelOffsetMode = PixelOffsetMode.None;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

				var destRect = new Rectangle(horizontalOffset, verticalOffset, actualWidth,actualHeight);


				//leave out the grey boarder which is in the browser, and zoom in some
				int skipMarginH = 30;
				int skipMarginV = 30;
				graphics.DrawImage(bmp, destRect, skipMarginH, skipMarginV,
						bmp.Width - (skipMarginH * 2), bmp.Height - (skipMarginV * 2),
						GraphicsUnit.Pixel, WhiteToBackground);

					Pen pn = new Pen(Color.Black, 1);
				if (drawBorderDashed)
				{
					pn.DashStyle = DashStyle.Dash;
					pn.Width = 2;
				}
				destRect.Height--;//hack, we were losing the bottom
				graphics.DrawRectangle(pn, destRect);
//                else
//                {
//
//                    Pen pn = new Pen(borderColor, 1);
//                    graphics.DrawRectangle(pn, 0, 0, thumbnail.Width - 1, thumbnail.Height - 1);
//                }
			}
			return thumbnail;
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
			if(_images.TryGetValue(id,out image))
			{
				_images.Remove(id);
				if(image.Tag!=null)
				{
					string thumbnailPath = image.Tag as string;
					if(!string.IsNullOrEmpty(thumbnailPath))
					{
						if(File.Exists(thumbnailPath))
						{
							try
							{
								File.Delete(thumbnailPath);
							}
							catch (Exception)
							{
								Debug.Fail("Could not delete path (would not see this in release version)");
								//oh well, couldn't delet it);
								throw;
							}
						}
					}
				}
			}
		}

		public void Dispose()
		{
			_disposed = true;
			Application.Idle -= Application_Idle;
			_orders.Clear();
			foreach (var browser in _browserCacheForDifferentPaperSizes)
			{
				browser.Value.Navigated -= _browser_Navigated;
				browser.Value.Dispose();
			}
			_browserCacheForDifferentPaperSizes.Clear();
		}
	}

	public class ThumbnailOrder
	{
		public Image ResultingThumbnail;
		public Action<Image> Callback;
		public bool DrawBorderDashed;
		public XmlDocument Document;
		public Color BackgroundColorOfResult;
		public string FolderForThumbNailCache;
		public string Key;
		public bool Done;
		public string ThumbNailFilePath;
	}
}
