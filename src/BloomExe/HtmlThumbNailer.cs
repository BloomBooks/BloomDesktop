using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.Properties;
using Palaso.Xml;
using Skybound.Gecko;

namespace Bloom
{
	public class HtmlThumbNailer
	{
		GeckoWebBrowser _browser;
		private Image _pendingThumbnail;
		Dictionary<string, Image> _images = new Dictionary<string, Image>();
		private readonly int _sizeInPixels =60;
		private Color _backgroundColorOfResult;
		private bool _browserHandleCreated;

		public HtmlThumbNailer(int sizeInPixels)
		{
			_sizeInPixels = sizeInPixels;
		}



		/// <summary>
		///
		/// </summary>
		/// <param name="key">whatever system you want... just used for caching</param>
		/// <param name="document"></param>
		/// <param name="backgroundColorOfResult">use Color.Transparent if you'll be composing in onto something else</param>
		/// <returns></returns>
		public Image GetThumbnail(string key, XmlDocument document, Color backgroundColorOfResult)
		{
			_backgroundColorOfResult = backgroundColorOfResult;

			MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(document);

			Image image;
			if(_images.TryGetValue(key, out image))
			{
				return image;
			}
			_pendingThumbnail = null;
			if(_browser==null)
			{
				_browser = new GeckoWebBrowser();
				_browser.HandleCreated += new EventHandler(_browser_HandleCreated);
				_browser.CreateControl();
				while (!_browserHandleCreated)
				{
					//TODO: could lead to hard to reproduce bugs
					Application.DoEvents();
					//TODO:  could be stuck here forever
					Thread.Sleep(100);
				}
			}


				//_browser.DocumentCompleted += OnThumbNailBrowser_DocumentCompleted;//review: there's also a "navigated"

				using (var temp = TempFile.CreateHtm(document))
				{

					// this is firing before it looks ready (no active element), so we'll just poll for
					//the correct state, below.    //_browser.Navigated += OnThumbNailBrowser_DocumentCompleted;//don't want to hear about the preceding navigating to about:blank

					//_browser.Navigated += new GeckoNavigatedEventHandler(_browser_Navigated);
					_browser.Navigate(temp.Path);
					while (_pendingThumbnail == null)
					{
						//_browser.Document.ActiveElement!=null
						if( _browser.Url.AbsolutePath.EndsWith(Path.GetFileName(temp.Path)))
						{
							try
							{
								var x = _browser.Document.ActiveElement;
								OnThumbNailBrowser_DocumentCompleted(null, null);
							}
							catch (Exception)
							{
							}

						}
						//TODO: could lead to hard to reproduce bugs
						Application.DoEvents();
						//TODO:  could be stuck here forever
						Thread.Sleep(100);
					}
				}
				_images.Add(key, _pendingThumbnail);

			return _pendingThumbnail;
		}

		void _browser_Navigated(object sender, GeckoNavigatedEventArgs e)
		{

		}

		/// <summary>
		/// we need to wait for this to happen before we can proceed to navigate to the page
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void _browser_HandleCreated(object sender, EventArgs e)
		{
			_browserHandleCreated =true;
		}

		private void MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(XmlDocument dom)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//textarea"))
			{
				if (string.IsNullOrEmpty(node.InnerText))
				{
					node.InnerText = " ";
				}
			}
		}
		private void OnThumbNailBrowser_DocumentCompleted(object sender, EventArgs e)
		{
			var width = _browser.Document.ActiveElement.ScrollWidth;
			var height = _browser.Document.ActiveElement.ScrollHeight;

			using (Bitmap docImage = new Bitmap(width, height))
			{
				_browser.Height = height;
				_browser.Width = width;

				_browser.DrawToBitmap(docImage,
												   new Rectangle(0,//_browser.Location.X,
																 0,//_browser.Location.Y,
																 width, height));

				_pendingThumbnail = MakeThumbNail(docImage, _sizeInPixels, _sizeInPixels, Color.Transparent);
			}
		}


		private Image MakeThumbNail(Image bmp, int destinationWidth, int destinationHeight, Color borderColor)
		{
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

				//grp.DrawImage(bmp, horizontalOffset, verticalOffset, actualWidth, actualHeight);

				Pen pn = new Pen(borderColor, 1);
				graphics.DrawRectangle(pn, 0, 0, thumbnail.Width - 1, thumbnail.Height - 1);
			}
//			bmp.Save(@"c:\dev\temp\page.png", ImageFormat.Png);
//			thumbnail.Save(@"c:\dev\temp\pagethum.png", ImageFormat.Png);
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

		//just remove from our cache
		public void PageChanged(string id)
		{
			if(_images.ContainsKey(id))
			{
				_images.Remove(id);
			}
		}
	}
}
