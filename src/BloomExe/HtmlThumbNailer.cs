using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.Properties;
using Palaso.Xml;

namespace Bloom
{
	public class HtmlThumbNailer
	{
		WebBrowser _browser ;
		private Image _pendingThumbnail;
		Dictionary<string, Image> _images = new Dictionary<string, Image>();
		private readonly int _sizeInPixels =60;

		public HtmlThumbNailer(int sizeInPixels)
		{
			_sizeInPixels = sizeInPixels;
		}

		public Image GetThumbnail(string key, XmlDocument document)
		{
			MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(document);

			Image image;
			if(_images.TryGetValue(key, out image))
			{
				return image;
			}
			_pendingThumbnail = null;
			using (_browser = new WebBrowser())
			{

				_browser.DocumentCompleted += OnThumbNailBrowser_DocumentCompleted;//review: there's also a "navigated"

				using (var temp = TempFile.CreateHtm(document))
				{
					_browser.Navigate(temp.Path);
					while (_pendingThumbnail == null)
					{
						//TODO: could lead to hard to reproduce bugs
						Application.DoEvents();
						//TODO:  could be stuck here forever
						Thread.Sleep(100);
					}
				}
				_images.Add(key, _pendingThumbnail);
			}
			_browser = null;
			return _pendingThumbnail;
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
		private void OnThumbNailBrowser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
		{
			var width = _browser.Document.ActiveElement.ScrollRectangle.Width;
			var height = _browser.Document.ActiveElement.ScrollRectangle.Height;

			using (Bitmap docImage = new Bitmap(width, height))
			{
				_browser.Height = height;
				_browser.Width = width;

				_browser.DrawToBitmap(docImage,
												   new Rectangle(_browser.Location.X,
																 _browser.Location.Y,
																 width, height));

//                int w = _sizeInPixels;
//                int h = _sizeInPixels;
//                if (height > width)
//                {
//                    w = (int)Math.Floor(((float)_sizeInPixels) * ((float)width / (float)height));
//                }
//                else
//                {
//                    h = (int) Math.Floor(((float)_sizeInPixels)*((float) height/(float) width));
//                }
//                _pendingThumbnail = new Bitmap(docImage, w, h);
//                _pendingThumbnail.
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

			Bitmap retBmp = new Bitmap(destinationWidth, destinationHeight, System.Drawing.Imaging.PixelFormat.Format64bppPArgb);
			Graphics grp = Graphics.FromImage(retBmp);
			grp.PixelOffsetMode = PixelOffsetMode.None;
			grp.InterpolationMode = InterpolationMode.HighQualityBicubic;

			grp.DrawImage(bmp, horizontalOffset, verticalOffset, actualWidth, actualHeight);

			Pen pn = new Pen(borderColor, 1); //Color.Wheat


			grp.DrawRectangle(pn, 0, 0, retBmp.Width - 1, retBmp.Height - 1);

			return retBmp;
#endif
		}
	}
}
