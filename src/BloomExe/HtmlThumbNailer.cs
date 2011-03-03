using System;
using System.Collections.Generic;
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
using Bloom.Properties;
using BloomTemp;
using Palaso.Xml;
using Skybound.Gecko;

namespace Bloom
{
	public class HtmlThumbNailer
	{
		GeckoWebBrowser _browser;
		private Image _pendingThumbnail;
		Dictionary<string, Image> _images = new Dictionary<string, Image>();
		private readonly int _sizeInPixels =70;
		private Color _backgroundColorOfResult;
		private bool _browserHandleCreated;

		/// <summary>
		///This is to overcome a problem with XULRunner 1.9 (or my use of it)this will always give us the size it was on the first page we navigated to,
		//so that if the book size changes, our thumbnails are all wrong.
		/// </summary>
		private Dictionary<string, GeckoWebBrowser> _browserCacheForDifferentPaperSizes = new Dictionary<string, GeckoWebBrowser>();


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
		/// <param name="drawBorderDashed"></param>
		/// <returns></returns>
		public Image GetThumbnail(string folderForThumbNailCache,string key, XmlDocument document, Color backgroundColorOfResult, bool drawBorderDashed)
		{
			Image image;
			if (_images.TryGetValue(key, out image))
			{
				return image;
			}

			string thumbNailFilePath=null;
			if (!string.IsNullOrEmpty(folderForThumbNailCache))
			{
				//var folderName = Path.GetFileName(folderForThumbNailCache);
				thumbNailFilePath = Path.Combine(folderForThumbNailCache, "thumbnail.png");
				if (File.Exists(thumbNailFilePath))
				{
					//this FromFile thing locks the file until the image is disposed of. Therefore, we copy the image and dispose of the original.
					using (image = Image.FromFile(thumbNailFilePath))
					{
					   return new Bitmap(image);
					}
				}
			}


			_backgroundColorOfResult = backgroundColorOfResult;

			MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(document);

			_pendingThumbnail = null;

			ConfigureBrowserForPaperSize(document);

			//_browser.DocumentCompleted += OnThumbNailBrowser_DocumentCompleted;//review: there's also a "navigated"

			using (var temp = TempFile.CreateHtm(document))
			{

				// this is firing before it looks ready (no active element), so we'll just poll for
				//the correct state, below.    //_browser.Navigated += OnThumbNailBrowser_DocumentCompleted;//don't want to hear about the preceding navigating to about:blank

				//_browser.Navigated += new GeckoNavigatedEventHandler(_browser_Navigated);

				_browser.Navigate(temp.Path);
				var giveUpTime = DateTime.Now.AddSeconds(4);// this can take a long time if the image on the front page is big.

				while (_pendingThumbnail == null && DateTime.Now < giveUpTime)
				{
					//_browser.Document.ActiveElement!=null
					if (_browser.Url.AbsolutePath.EndsWith(Path.GetFileName(temp.Path)))
					{
						try
						{
							 OnThumbNailBrowser_DocumentCompleted(drawBorderDashed, null);
						}
						catch (Exception)
						{
							//we often land here, but I've tested and the second time through the try, we succeed.
							//so this suggests that the AbsolutePath changes, but it's not quit ready to get
							//at the document.
						}

					}
					//TODO: could lead to hard to reproduce bugs
					Application.DoEvents();

					Thread.Sleep(100);
				}
			}
			if (_pendingThumbnail == null)
			{
				_pendingThumbnail = Resources.GenericPage32x32;
			}
			else if (!string.IsNullOrEmpty(thumbNailFilePath))
			{
				try
				{
					//gives a blank         _pendingThumbnail.Save(thumbNailFilePath);
					using (Bitmap b = new Bitmap(_pendingThumbnail))
					{
						b.Save(thumbNailFilePath);
					}
				}
				catch(Exception)
				{
					//this is going to fail if we don't have write permission
				}
			}

			_pendingThumbnail.Tag = thumbNailFilePath;//usefull if we later know we need to clear out that file
			_images.Add(key, _pendingThumbnail);


			return _pendingThumbnail;
		}


		private void ConfigureBrowserForPaperSize(XmlDocument document)
		{
//            string paperSizeTemplate=string.Empty;
//            string[] paperSizeTemplateNames = new string[]{"A5Portrait", "A4Landscape", "A5LandScape", "A4Portrait"};
//            foreach (var name in paperSizeTemplateNames)
//            {
				 string paperSizeName;
				XmlNodeList safeSelectNodes = document.SafeSelectNodes("html/body/img");
				if (safeSelectNodes.Count == 1)
				{
					paperSizeName = safeSelectNodes[0].GetStringAttribute("src");
				}
				else
				{
					var paperStyleSheets = document.SafeSelectNodes(
						"html/head/link[contains(@href, 'Landscape') or contains(@href, 'Portrait')]");
					if (paperStyleSheets.Count == 0)
					{
						Debug.Fail(
							"THumbnailer could not identify paper size. In Release version, this would still work, just  slower & more memory");

						_browser = MakeNewBrowser();
						return;
					}

					paperSizeName = Path.GetFileNameWithoutExtension(paperStyleSheets[0].GetStringAttribute("href"));
				}
			if (!_browserCacheForDifferentPaperSizes.TryGetValue(paperSizeName, out _browser))
				{
					_browser = MakeNewBrowser();
					_browserCacheForDifferentPaperSizes.Add(paperSizeName, _browser);
				}
				return;
		}

		private GeckoWebBrowser MakeNewBrowser()
		{
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
		private void OnThumbNailBrowser_DocumentCompleted(object drawBorderDashed, EventArgs e)
		{
			//NB: this will always give us the size it was on the first page we navigated to,
			//so that if the book size changes, our thumbnails are all wrong. I don't know
			//how to fix it, so I'm using different browsers at the moment.
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
				_pendingThumbnail = MakeThumbNail(docImage, _sizeInPixels, _sizeInPixels, Color.Transparent,(bool)drawBorderDashed);
			}
		}


		private Image MakeThumbNail(Image bmp, int destinationWidth, int destinationHeight, Color borderColor, bool drawBorderDashed)
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
	}
}
