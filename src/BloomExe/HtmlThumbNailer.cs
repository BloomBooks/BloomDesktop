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
using Palaso.Reporting;
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
			lock (this)
			{
				Image image;
				if (_images.TryGetValue(key, out image))
				{
					return image;
				}

				string thumbNailFilePath = null;
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

				using (var temp = TempFile.CreateHtm5FromXml(document))
				{
					_browser.Navigate(temp.Path);
					_browser.NavigateFinishedNotifier.BlockUntilNavigationFinished();


/* this served us well until we got squeaky-clean standards compliant (including, crucuially, the html5 <!DOCTYPE HTML> line. Then suddenly  ActiveElement.ScrollWidth was 0
 *
							//NB: this will always give us the size it was on the first page we navigated to,
						//so that if the book size changes, our thumbnails are all wrong. I don't know
						//how to fix it, so I'm using different browsers at the moment.
						_browser.Height = _browser.Document.ActiveElement.ScrollHeight;
						_browser.Width = _browser.Document.ActiveElement.ScrollWidth; //NB: 0 here at one time was traced to the html header <!DOCTYPE html> was enought to get us to 0
*/

					var div = _browser.Document.ActiveElement.GetElements("//div[contains(@class, '-bloom-page')]").First();
					if (div == null)
						throw new ApplicationException("thumbnails found now div with a class of -Bloom-Page");

					_browser.Height = div.ScrollHeight;
					_browser.Width = div.ScrollWidth;


					try
					{
						var docImage = _browser.GetBitmap((uint)_browser.Width, (uint)_browser.Height);
						//docImage.Save(@"c:\dev\temp\zzzz.bmp");
						_pendingThumbnail = MakeThumbNail(docImage, _sizeInPixels, _sizeInPixels, Color.Transparent, drawBorderDashed);
					}
// ReSharper disable EmptyGeneralCatchClause
					catch
// ReSharper restore EmptyGeneralCatchClause
					{
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
					catch (Exception)
					{
						//this is going to fail if we don't have write permission
					}
				}

				_pendingThumbnail.Tag = thumbNailFilePath; //usefull if we later know we need to clear out that file

				try
					//I saw a case where this threw saying that the key was already in there, even though back at the beginning of this function, it wasn't.
				{
					_images.Add(key, _pendingThumbnail);
				}
				catch (Exception error)
				{
					Logger.WriteMinorEvent("Skipping minor error: " + error.Message);
					//not worth crashing over, at this point in Bloom's life, since it's just a cache. But since then, I did add a lock() around all this.
				}
			}
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
	}
}
