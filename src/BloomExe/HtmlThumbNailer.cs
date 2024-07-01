using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private Dictionary<string, Browser> _browserCacheForDifferentPaperSizes =
            new Dictionary<string, Browser>();

        private bool _disposed;

        public HtmlThumbNailer()
        {
            if (_theOnlyOneAllowed != null)
            {
                Debug.Fail(
                    "Something tried to make a second HtmlThumbnailer; there should only be one."
                );
                throw new ApplicationException(
                    "Something tried to make a second HtmlThumbnailer; there should only be one."
                );
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
                Solid,
                Dashed,
                None
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
        public async Task<Image> GetThumbnail(
            string key,
            HtmlDom document,
            ThumbnailOptions options
        )
        {
            Image image;
            Image thumbnail = null;
            lock (this)
            {
                //In our cache?
                if (
                    !options.MustRegenerate
                    && !String.IsNullOrWhiteSpace(key)
                    && _images.TryGetValue(key, out image)
                )
                {
                    Debug.WriteLine(
                        "Thumbnail Cache HIT: "
                            + key
                            + " thread="
                            + Thread.CurrentThread.ManagedThreadId
                    );
                    return image;
                }

                Debug.WriteLine(
                    "Thumbnail Cache MISS: "
                        + key
                        + " thread="
                        + Thread.CurrentThread.ManagedThreadId
                );
            }
            // We'd prefer to have all this inside the lock, but we can't have awaited code inside a lock.
            // The worst consequence is a chance (very small) that we'll do the work twice.

            _backgroundColorOfResult = options.BackgroundColor;
            XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(document.RawDom);

            var browser = GetBrowserForPaperSize(document.RawDom);
            if (browser == null)
                return Resources.PagePlaceHolder;

            var order = new ThumbnailOrder() { Options = options, Document = document };
            for (int i = 0; i < 4; i++)
            {
                thumbnail = await CreateThumbNail(order, browser);
                if (thumbnail != null)
                    break;
                // For some reason...possibly another navigation was in progress...we can't do this just now.
                // Try a few times.
            }
            if (thumbnail == null) // just can't get it.
            {
                return Resources.PagePlaceHolder; // but don't save it...try again if we get another request.
            }

            if (!String.IsNullOrWhiteSpace(key))
            {
                lock (this)
                {
                    _images[key] = thumbnail;
                }
            }

            return thumbnail;
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
            var order = (ThumbnailOrder)browser.Tag;

            var success = browser.NavigateAndWaitTillDone(
                dom,
                100000,
                InMemoryHtmlFileSource.Thumb,
                null,
                false
            );
            if (!success)
            {
                Logger.WriteEvent(
                    "HtmlThumbNailer ({1}): Timed out on ({0})",
                    order.ThumbNailFilePath,
                    Thread.CurrentThread.ManagedThreadId
                );
#if DEBUG
                if (!_thumbnailTimeoutAlreadyDisplayed)
                {
                    _thumbnailTimeoutAlreadyDisplayed = true;
                    _syncControl.Invoke(
                        (Action)(
                            () =>
                                Debug.Fail(
                                    "(debug only) Make thumbnail timed out (won't show again)"
                                )
                        )
                    );
                }
#endif
            }

            return true;
        }

        private async Task<Size> SetWidthAndHeight(Browser browser)
        {
            try
            {
                if (_syncControl.InvokeRequired)
                {
                    throw new ApplicationException(
                        "HtmlThumbNailer.SetWidthAndHeight should not be called on a background thread"
                    );
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.Alpha,
                    "Could not make thumbnail",
                    "Ref bl-524",
                    e
                );
                return new Size(0, 0); // this tells the caller we failed
            }
            var script =
                @"
const page = document.getElementsByClassName('bloom-page')[0]; page.clientHeight.toString() + ' ' + page.clientWidth.toString();";
            var result = await browser.GetStringFromJavascriptAsync(script);
            var parts = result.Split(' ');
            var height = (int)Math.Round(double.Parse(parts[0]));
            var width = (int)Math.Round(double.Parse(parts[1]));

            browser.Height = height;
            browser.Width = width;
            // This is probably not needed...width zero came about in debugging incomplete code where a stylesheet
            // did not take effect..but it seems like a reasonable bit of defensive programming to keep.
            if (browser.Width == 0)
                browser.Width = browser.Height / 2; // arbitrary way of avoiding crash
            return new Size(browser.Width, browser.Height);
        }

        private async Task<Image> CreateImage(Browser browser)
        {
            using (var image = await browser.CapturePreview())
            {
                // Note: In GeckoFx days, we had very complex code here to try to determine whether the page
                // had been fully rendered. It doesn't seem to be needed with WebView2.
                return new Bitmap(image);
            }
        }

        // The order we most recently started working on.
        // I'm not sure we can't be working on more than one at a time, so use with care.
        // Currently only used to try to abort orders; if that fails, it's not too serious.
        private ThumbnailOrder _currentOrder;

        /// <summary>
        /// Returns an image if possible, null if for some reason we can't.
        /// </summary>
        /// <param name="order"></param>
        /// <param name="browser"></param>
        /// <returns></returns>
        private async Task<Image> CreateThumbNail(ThumbnailOrder order, Browser browser)
        {
            // runs on UI thread
            _currentOrder = order;

            order.Done = false;
            browser.Tag = order;
            Color coverColor;
            ImageUtils.TryCssColorFromString(
                Book.Book.GetCoverColorFromDom(order.Document.RawDom),
                out coverColor
            );
            if (!OpenTempFileInBrowser(browser, order.Document))
                return null;

            var browserSize = await SetWidthAndHeight(browser);
            if (browserSize.Height == 0) //happens when we run into the as-yet-unreproduced-or-fixed bl-254
                return null; // will try again later

            try
            {
                Logger.WriteMinorEvent(
                    "HtmlThumbNailer.CreateThumbNail: (threadId={2}) width={0} height={1}",
                    browserSize.Width,
                    (uint)browserSize.Height,
                    Thread.CurrentThread.ManagedThreadId
                );

                if (browserSize.Width == 0 || browserSize.Height == 0)
                {
                    var paperSizeName = GetPaperSizeName(order.Document.RawDom);
                    throw new ApplicationException(
                        "Problem getting thumbnail browser for document with Paper Size: "
                            + paperSizeName
                    );
                }

                using (Image fullsizeImage = await CreateImage(browser))
                {
                    if (_disposed)
                        return null;
                    return MakeThumbNail(fullsizeImage, order.Options);
                }
            }
            catch (Exception error)
            {
                Logger.WriteEvent(
                    "HtmlThumbNailer ({0}) got {1}",
                    Thread.CurrentThread.ManagedThreadId,
                    error.Message
                );
                Logger.WriteEvent(
                    "Disposing of all browsers in hopes of getting a fresh start on life"
                );
                foreach (
                    var browserCacheForDifferentPaperSize in _browserCacheForDifferentPaperSizes
                )
                {
                    try
                    {
                        Logger.WriteEvent(
                            "Disposing of browser {0}",
                            browserCacheForDifferentPaperSize.Key
                        );
                        browserCacheForDifferentPaperSize.Value.Dispose();
                    }
                    catch (Exception e2)
                    {
                        Logger.WriteEvent(
                            "While trying to dispose of thumbnailer browsers as a result of an exception, go another: "
                                + e2.Message
                        );
                    }
                }

                _browserCacheForDifferentPaperSizes.Clear();
#if DEBUG
                _syncControl.Invoke((Action)(() => Debug.Fail(error.Message)));
#endif
            }

            return null;
        }

        private void _browser_OnDocumentCompleted(
            object sender,
            EventArgs geckoDocumentCompletedEventArgs
        )
        {
            Debug.WriteLine(
                "_browser_OnDocumentCompleted ({0})",
                Thread.CurrentThread.ManagedThreadId
            );
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
            string paperSizeName = SizeAndOrientation
                .GetSizeAndOrientation(document, "A5Portrait")
                .ToString();
            return paperSizeName;
        }

        private Browser MakeNewBrowser()
        {
            Debug.WriteLine(
                "making browser for HtmlThumbNailer ({0})",
                Thread.CurrentThread.ManagedThreadId
            );
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
                if (bmp.Width > bmp.Height) //landscape
                {
                    contentWidth = options.Width;
                    contentHeight = (int)(
                        Math.Ceiling(((float)bmp.Height / (float)bmp.Width) * (float)contentWidth)
                    );
                }
                else if (bmp.Width < bmp.Height) //portrait
                {
                    contentHeight = options.Height;
                    contentWidth = (int)(
                        Math.Ceiling(((float)bmp.Width / (float)bmp.Height) * (float)contentHeight)
                    );
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
                contentWidth = (int)
                    Math.Floor((float)options.Height * (float)bmp.Width / (float)bmp.Height);
#if !__MonoCS__
                thumbnailHeight = contentHeight;
                thumbnailWidth = contentWidth;
#endif
            }

#if !__MonoCS__
            var thumbnail = new Bitmap(
                thumbnailWidth,
                thumbnailHeight,
                PixelFormat.Format64bppPArgb
            );
            using (var graphics = Graphics.FromImage(thumbnail))
            {
                graphics.PixelOffsetMode = PixelOffsetMode.None;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                var destRect = new Rectangle(
                    horizontalOffset,
                    verticalOffset,
                    contentWidth,
                    contentHeight
                );

                graphics.DrawImage(
                    bmp,
                    destRect,
                    0,
                    0,
                    bmp.Width,
                    bmp.Height, //source
                    GraphicsUnit.Pixel,
                    WhiteToBackground
                );

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
            Bitmap croppedImage = (bmp as Bitmap).Clone(
                new Rectangle(
                    new Point(skipMarginH, skipMarginV),
                    new Size(bmp.Width - 2 * skipMarginH, bmp.Height - 2 * skipMarginV)
                ),
                bmp.PixelFormat
            );
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
                                Debug.Fail(
                                    "Could not delete path (would not see this in release version)"
                                );
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
                    browser.Value.Invoke(
                        (Action)(
                            () =>
                            {
                                browser.Value.DocumentCompleted -= _browser_OnDocumentCompleted;
                                browser.Value.Dispose();
                            }
                        )
                    );
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
