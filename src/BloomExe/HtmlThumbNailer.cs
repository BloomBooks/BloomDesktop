using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Bloom
{
	public class HtmlThumbNailer
	{
		WebBrowser _browser = new WebBrowser();
		private Image _pendingThumbnail;
		Dictionary<string, Image> _images = new Dictionary<string, Image>();
		private readonly int _sizeInPixels =60;

		public HtmlThumbNailer(int sizeInPixels)
		{
			_sizeInPixels = sizeInPixels;
			_browser.DocumentCompleted += new WebBrowserDocumentCompletedEventHandler(OnThumbNailBrowser_DocumentCompleted);
		}

		public Image GetThumbnail(string key, string url)
		{
			Image image;
			if(_images.TryGetValue(key, out image))
			{
				return image;
			}
			_pendingThumbnail = null;
			_browser.Navigate(url);
			while(_pendingThumbnail ==null)
			{
				Application.DoEvents();
				Thread.Sleep(100);
			}
			_images.Add(key, _pendingThumbnail);
			return _pendingThumbnail;
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

				int w = _sizeInPixels;
				int h = _sizeInPixels;
				if (height > width)
				{
					w = (int)Math.Floor(((float)_sizeInPixels) * ((float)width / (float)height));
				}
				else
				{
					h = (int) Math.Floor(((float)_sizeInPixels)*((float) height/(float) width));
				}
				_pendingThumbnail = new Bitmap(docImage, w, h);
			}
		}

	}
}
