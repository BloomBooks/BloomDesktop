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
using Gecko;
using Segmentio;

namespace Bloom
{
    public class HtmlThumbNailer : IDisposable
    {
        Dictionary<string, Image> _images = new Dictionary<string, Image>();
        private readonly int _widthInPixels = 70;
        private readonly int _heightInPixels = 70;
        private readonly MonitorTarget _monitorObjectForBrowserNavigation;
        private Color _backgroundColorOfResult;
        private bool _browserHandleCreated;
        private Queue<ThumbnailOrder> _orders = new Queue<ThumbnailOrder>();

        /// <summary>
        ///This is to overcome a problem with XULRunner 1.9 (or my use of it)this will always give us the size it was on the first page we navigated to,
        //so that if the book size changes, our thumbnails are all wrong. 
        /// </summary>
        private Dictionary<string, GeckoWebBrowser> _browserCacheForDifferentPaperSizes = new Dictionary<string, GeckoWebBrowser>();

        private bool _disposed;

        public HtmlThumbNailer(int widthInPixels, int heightInPixels, MonitorTarget monitorObjectForBrowserNavigation)
        {
            _widthInPixels = widthInPixels;
            _heightInPixels = heightInPixels;
            _monitorObjectForBrowserNavigation = monitorObjectForBrowserNavigation;
            Application.Idle += new EventHandler(Application_Idle);
        }

        void Application_Idle(object sender, EventArgs e)
        {
            if (_orders.Count > 0)
            {
                if (Monitor.TryEnter(_monitorObjectForBrowserNavigation))
                //don't try to work with the browser while other processes are doing it to... maybe can remove this when we clear out any Application.DoEvents() that our current old version of Geckofx is forcing us to use because of unreliable end-of-navigation detection
                {
                    try
                    {
                        ThumbnailOrder thumbnailOrder = _orders.Dequeue();
                        try
                        {
                            ProcessOrder(thumbnailOrder);
                        }
                        catch (Exception error)
                        {
                            //putting up a green box here, because, say, the page was messed up, is bad manners
                            Logger.WriteEvent("HtmlThumbNailer reported exception:{0}", error.Message);
                            thumbnailOrder.ErrorCallback(error);
                        }
                    }
                    finally
                    {
                        // Ensure that the lock is released.
                        Monitor.Exit(_monitorObjectForBrowserNavigation);
                    }
                }
            }
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
            public Color BackgroundColor = Color.White;
            public Color BorderColor = Color.Transparent;
            public bool DrawBorderDashed = false;

            /// <summary>
            /// Use this when all thumbnails need to be the centered in the same size png.
            /// Unfortunately as long as we're using the winform listview, we seem to need to make the icons
            /// the same size otherwise the title-captions don't line up.
            /// </summary>
            public bool CenterImageUsingTransparentPadding = true;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="key">whatever system you want... just used for caching</param>
        /// <param name="document"></param>
        /// <param name="backgroundColorOfResult">use Color.Transparent if you'll be composing in onto something else</param>
        /// <param name="drawBorderDashed"></param>
        /// <returns></returns>
        public void GetThumbnailAsync(string folderForThumbNailCache, string key, XmlDocument document, ThumbnailOptions options, Action<Image> callback, Action<Exception> errorCallback)
        {
            //review: old code had it using "key" in one place(checking for existing), thumbNailFilePath in another (adding new)

            string thumbNailFilePath = null;
            if (!string.IsNullOrEmpty(folderForThumbNailCache))
                thumbNailFilePath = Path.Combine(folderForThumbNailCache, "thumbnail.png");

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
                Options = options,
                Callback = callback,
                ErrorCallback = errorCallback,
                Document = document,
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
                Logger.WriteMinorEvent("HtmlThumbNailer: starting work on thumbnail ({0})", order.ThumbNailFilePath);

                _backgroundColorOfResult = order.Options.BackgroundColor;
                XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(order.Document);


                var browser = GetBrowserForPaperSize(order.Document);
                lock (browser)
                {
                    using (var temp = TempFileUtils.CreateHtm5FromXml(order.Document))
                    {
                        order.Done = false;
                        browser.Tag = order;

                        browser.Navigate(temp.Path);

                        var minimumTime = DateTime.Now.AddSeconds(0); //was 1 second, with geckofx11. For geckofx22, we're trying out no minumum time. Will need testing on slow machines to get confidence.
                        var stopTime = DateTime.Now.AddSeconds(5);
                        while (!_disposed && (DateTime.Now < minimumTime || !order.Done || browser.Document.ActiveElement == null) && DateTime.Now < stopTime)
                        {
                            Application.DoEvents(); //TODO: avoid this
                        }
                        if (_disposed)
                            return;
                        if (!order.Done)
                        {
                            Logger.WriteEvent("HtmlThumNailer: Timed out on ({0})", order.ThumbNailFilePath);
                            Debug.Fail("(debug only) Make thumbnail timed out");
                            return;
                        }

                        Guard.AgainstNull(browser.Document.ActiveElement, "browser.Document.ActiveElement");
#if __MonoCS__
						Application.RaiseIdle(null);
#endif

                        /* saw crash here, shortly after startup: 
                         * 1) opened an existing book
                         * 2) added a title
                         * 3) added a dog from aor
                         * 4) got this crash
                         *    at Gecko.nsIDOMXPathEvaluator.CreateNSResolver(nsIDOMNode nodeResolver)
                               at Gecko.GeckoNode.GetElements(String xpath) in C:\dev\geckofx11hatton\Skybound.Gecko\DOM\GeckoNode.cs:line 222
                               at Bloom.HtmlThumbNailer.ProcessOrder(ThumbnailOrder order) in C:\dev\Bloom\src\BloomExe\HtmlThumbNailer.cs:line 167
                               at Bloom.HtmlThumbNailer.Application_Idle(Object sender, EventArgs e) in C:\dev\Bloom\src\BloomExe\HtmlThumbNailer.cs:line 53
                         */
                        var div = browser.Document.ActiveElement.GetElements("//div[contains(@class, 'bloom-page')]").FirstOrDefault();
                        if (div == null)
                        {
                            Logger.WriteEvent("HtmlThumNailer:  found no div with a class of bloom-Page ({0})", order.ThumbNailFilePath);
                            throw new ApplicationException("thumbnails found no div with a class of bloom-Page");
                        }

                        browser.Height = div.ClientHeight;
                        browser.Width = div.ClientWidth;

                        try
                        {
                            Logger.WriteMinorEvent("HtmlThumNailer: browser.GetBitmap({0},{1})", browser.Width,
                                                   (uint)browser.Height);


                            //BUG (April 2013) found that the initial call to GetBitMap always had a zero width, leading to an exception which
                            //the user doesn't see and then all is well. So at the moment, we avoid the exception, and just leave with
                            //the placeholder thumbnail.
                            if (browser.Width == 0 || browser.Height == 0)
                            {
                                var paperSizeName = GetPaperSizeName(order.Document);
                                throw new ApplicationException(
                                    "Problem getting thumbnail browser for document with Paper Size: " + paperSizeName);
                            }
                            /*							When we were using geckofx11, we used this approach, which stopped working when we moved to geckofx22:
                                                        var docImage = new Bitmap(browser.Width, browser.Height);
                                                        browser.DrawToBitmap(docImage, new Rectangle(0, 0, browser.Width, browser.Height));
                                                        if (_disposed)
                                                            return;
                                                        pendingThumbnail = MakeThumbNail(fullsizeImage, _widthInPixels, _heightInPixels,
                                                            Color.Transparent,
                                                            order.DrawBorderDashed);
                             */

                            var creator = new ImageCreator(browser);
                            byte[] imageBytes = creator.CanvasGetPngImage((uint)browser.Width, (uint)browser.Height);

                            using (var stream = new System.IO.MemoryStream(imageBytes))
                            using (Image fullsizeImage = Image.FromStream(stream))
                            {
                                if (_disposed)
                                    return;
                                int width = _widthInPixels;
                                int height = _heightInPixels;
                                if (!order.Options.CenterImageUsingTransparentPadding)
                                {
                                    // Adjust height and width so image does not end up with extra blank area
                                    if (fullsizeImage.Width < fullsizeImage.Height)
                                    {
                                        width = Math.Min(width, height * fullsizeImage.Width / fullsizeImage.Height + 2);
                                        // +2 seems to be needed (at least for 70 pix height) so nothing is clipped
                                    }
                                    else if (fullsizeImage.Width > fullsizeImage.Height)
                                    {
                                        height = Math.Min(height, width * fullsizeImage.Height / fullsizeImage.Width + 2);
                                    }
                                }
                                pendingThumbnail = MakeThumbNail(fullsizeImage, width, height, order.Options);
                            }
                        }
                        catch (Exception error)
                        {
#if DEBUG
                            Debug.Fail(error.Message);
#endif
                            Logger.WriteEvent("HtmlThumbNailer got " + error.Message);
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
                                    Logger.WriteEvent("While trying to dispose of thumbnailer browsers as a result of an exception, go another: " + e2.Message);
                                }
                            }
                            _browserCacheForDifferentPaperSizes.Clear();
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

                Debug.WriteLine("THumbnail created with dimensions ({0},{1})", browser.Width, browser.Height);

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
            Logger.WriteMinorEvent("HtmlThumNailer: finished work on thumbnail ({0})", order.ThumbNailFilePath);
            order.Callback(pendingThumbnail);
        }
        void _browser_Navigated(object sender, GeckoNavigatedEventArgs e)
        {
            ThumbnailOrder order = (ThumbnailOrder)((GeckoWebBrowser)sender).Tag;
            order.Done = true;
        }


        private GeckoWebBrowser GetBrowserForPaperSize(XmlDocument document)
        {
            var paperSizeName = GetPaperSizeName(document);

            GeckoWebBrowser b;
            if (!_browserCacheForDifferentPaperSizes.TryGetValue(paperSizeName, out b))
            {
                b = MakeNewBrowser();
                b.Navigated += new EventHandler<GeckoNavigatedEventArgs>(_browser_Navigated);
                _browserCacheForDifferentPaperSizes.Add(paperSizeName, b);
            }
            return b;
        }

        private static string GetPaperSizeName(XmlDocument document)
        {
            string paperSizeName = SizeAndOrientation.GetSizeAndOrientation(document, "A5Portrait").ToString();
            return paperSizeName;
        }


        private GeckoWebBrowser MakeNewBrowser()
        {
            Debug.WriteLine("making browser");
#if !__MonoCS__

            var browser = new GeckoWebBrowser();
#else
			var browser = new OffScreenGeckoWebBrowser();
#endif
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
            _browserHandleCreated = true;
        }



        private Image MakeThumbNail(Image bmp, int destinationWidth, int destinationHeight, ThumbnailOptions options)
        {
            if (bmp == null)
                return null;
            //get the lesser of the desired and original size
            destinationWidth = bmp.Width > destinationWidth ? destinationWidth : bmp.Width;
            destinationHeight = bmp.Height > destinationHeight ? destinationHeight : bmp.Height;

            int actualWidth = destinationWidth;
            int actualHeight = destinationHeight;

            if (bmp.Width > bmp.Height)
                actualHeight = (int)(Math.Ceiling(((float)bmp.Height / (float)bmp.Width) * (float)actualWidth));
            else if (bmp.Width < bmp.Height)
                actualWidth = (int)(Math.Ceiling(((float)bmp.Width / (float)bmp.Height) * (float)actualHeight));


            int horizontalOffset = 0;
            int verticalOffset = 0;

            if (options.CenterImageUsingTransparentPadding)
            {
                horizontalOffset = (destinationWidth / 2) - (actualWidth / 2);
                verticalOffset = (destinationHeight / 2) - (actualHeight / 2);
            }

#if !__MonoCS__
            Bitmap thumbnail = new Bitmap(destinationWidth, destinationHeight, System.Drawing.Imaging.PixelFormat.Format64bppPArgb);
            using (Graphics graphics = Graphics.FromImage(thumbnail))
            {
                graphics.PixelOffsetMode = PixelOffsetMode.None;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                var destRect = new Rectangle(horizontalOffset, verticalOffset, actualWidth, actualHeight);


                //leave out the grey boarder which is in the browser, and zoom in some
                int skipMarginH = 0;// 30; //
                int skipMarginV = 0;
                graphics.DrawImage(bmp, destRect, skipMarginH, skipMarginV,
                        bmp.Width - (skipMarginH * 2), bmp.Height - (skipMarginV * 2),
                        GraphicsUnit.Pixel, WhiteToBackground);

                Pen pn = new Pen(Color.Black, 1);
                if (options.DrawBorderDashed)
                {
                    pn.DashStyle = DashStyle.Dash;
                    pn.Width = 2;
                }
                destRect.Height--;//hack, we were losing the bottom
                destRect.Width--;
                graphics.DrawRectangle(pn, destRect);
                //                else
                //                {
                //
                //                    Pen pn = new Pen(options.BorderColor, 1);
                //                    graphics.DrawRectangle(pn, 0, 0, thumbnail.Width - 1, thumbnail.Height - 1);
                //                }
            }
            return thumbnail;
#else
			int skipMarginH = 30;
			int skipMarginV = 30;
			Bitmap croppedImage = (bmp as Bitmap).Clone(new Rectangle(new Point(skipMarginH, skipMarginV), new Size(bmp.Width - 2 * skipMarginH, bmp.Height - 2 * skipMarginV)), bmp.PixelFormat);
			return croppedImage.GetThumbnailImage(destinationWidth, destinationHeight, null, System.IntPtr.Zero);			
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
                        if (File.Exists(thumbnailPath))
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
                browser.Value.Invoke((Action)(() =>
                {
                    browser.Value.Navigated -= _browser_Navigated;
                    browser.Value.Dispose();
                }));
            }
            _browserCacheForDifferentPaperSizes.Clear();
        }

        /// <summary>
        /// This is a trick that processes waiting for thumbnails can use in situations where
        /// Application.Idle is not being invoked. Such uses must pass a non-null control
        /// created in the thread where Application_Idle should be invoked (i.e., the UI thread)
        /// </summary>
        internal void Advance(Control invokeTarget)
        {
            if (_orders.Count == 0)
                return;
            if (invokeTarget != null)
                invokeTarget.Invoke((Action)(() => Application_Idle(this, new EventArgs())));
        }
    }

    public class ThumbnailOrder
    {
        public Image ResultingThumbnail;
        public Action<Image> Callback;
        public Action<Exception> ErrorCallback;
        public XmlDocument Document;
        public string FolderForThumbNailCache;
        public string Key;
        public bool Done;
        public string ThumbNailFilePath;
        public HtmlThumbNailer.ThumbnailOptions Options;
    }
}
