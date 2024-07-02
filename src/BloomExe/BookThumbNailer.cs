using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.SafeXml;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom
{
    /// <summary>
    /// This class is a place to gather the methods that have to do with making thumbnails of pages of books.
    /// Three of the methods were previously methods of Book itself, but the fourth (MakeThumbnailOfCover)
    /// needed to do too much UI stuff to belong in a model class, so it seemed worth pulling all this
    /// out to a new class.
    /// In live code there is typically only one instance of this created by the ApplicationServer.
    /// In test code they may be created as needed; nothing requires this to be a singleton.
    /// Indeed, it could be a static class except that it requires the HtmlThumbNailer.
    /// </summary>
    public class BookThumbNailer
    {
        private readonly HtmlThumbNailer _thumbnailProvider;

        public BookThumbNailer(HtmlThumbNailer thumbNailer)
        {
            _thumbnailProvider = thumbNailer;
        }

        public void CancelOrder(Guid requestId)
        {
            _thumbnailProvider.CancelOrder(requestId);
        }

        public HtmlThumbNailer HtmlThumbNailer
        {
            get { return _thumbnailProvider; }
        }

        private void GetThumbNailOfBookCover(
            Book.Book book,
            HtmlThumbNailer.ThumbnailOptions thumbnailOptions,
            Action<Image> callback,
            Action<Exception> errorCallback,
            bool async
        )
        {
            if (book is ErrorBook)
            {
                callback(Resources.Error70x70);
                return;
            }
            try
            {
                if (book.HasFatalError) //NB: we might not know yet... we don't fully load every book just to show its thumbnail
                {
                    callback(Resources.Error70x70);
                    return;
                }
                GenerateImageForWeb(book);

                Image thumb;
                if (book.Storage.TryGetPremadeThumbnail(thumbnailOptions.FileName, out thumb))
                {
                    callback(thumb);
                    return;
                }

#if USE_HTMLTHUMBNAILER_FOR_COVER
                var dom = book.GetPreviewXmlDocumentForFirstPage();
                if (dom == null)
                {
                    callback(Resources.Error70x70);
                    return;
                }
                string folderForCachingThumbnail;

                folderForCachingThumbnail = book.StoragePageFolder;
                _thumbnailProvider.GetThumbnail(
                    folderForCachingThumbnail,
                    book.Storage.Key,
                    dom,
                    thumbnailOptions,
                    callback,
                    errorCallback,
                    async
                );
#else
                if (!CreateThumbnailOfCoverImage(book, thumbnailOptions, callback))
                {
                    callback(Resources.Error70x70);
                }
#endif
            }
            catch (Exception err)
            {
                callback(Resources.Error70x70);
                errorCallback(err);
                Debug.Fail(err.Message);
            }
        }

        /// <summary>
        /// Generates a web thumbnail image from the book's front cover image.
        /// The resulting image will fit into a 200px x 200px box with no loss of aspect ratio.
        /// This means that either the height or the width will be 200px.
        /// </summary>
        /// <param name="book"></param>
        public static void GenerateImageForWeb(Book.Book book)
        {
            HtmlThumbNailer.ThumbnailOptions options = new HtmlThumbNailer.ThumbnailOptions
            {
                Height = 200,
                Width = 200,
                CenterImageUsingTransparentPadding = false,
                FileName = "coverImage200.jpg",
                BorderStyle = GetThumbnailBorderStyle(book)
            };
            CreateThumbnailOfCoverImage(book, options);
        }

        /// <summary>
        /// Generates the cover image for a book, e.g. the thumbnail used on Bloom Library
        /// </summary>
        /// <param name="book"></param>
        /// <param name="requestedSize">The maximum size of either dimension</param>
        public static string GenerateCoverImageOfRequestedMaxSize(Book.Book book, int requestedSize)
        {
            if (requestedSize == 256)
            {
                // Use the custom thumbnail file if one is provided.
                var customFile = Path.Combine(book.FolderPath, "custom-thumbnail-256.png");
                if (RobustFile.Exists(customFile))
                {
                    var thumbnailFile = Path.Combine(book.FolderPath, "thumbnail-256.png");
                    RobustFile.Copy(customFile, thumbnailFile, true);
                    return thumbnailFile;
                }
            }
            var thumbnailOptions = GetCoverThumbnailOptions(requestedSize, new Guid());
            CreateThumbnailOfCoverImage(book, thumbnailOptions);

            var thumbnailDestination = Path.Combine(book.FolderPath, thumbnailOptions.FileName);
            return thumbnailDestination;
        }

        /// <summary>
        /// Generates a thumbnail suitable for sharing on Facebook
        /// </summary>
        /// <param name="book"></param>
        /// <returns></returns>
        public static string GenerateSocialMediaSharingThumbnail(Book.Book book)
        {
            // 300 x 300 is picked so that FB will consistently generate a thumbnail that is:
            // * to the left of the link
            // * a fixed aspect ratio (happens to be square)
            // * has enough pixels to work with high res screens with devicePixelRatio up to 4. (FB Mobile App was showing it as 75 CSS pixels)
            // See BL-8406
            int size = 300;
            var options = new HtmlThumbNailer.ThumbnailOptions
            {
                CenterImageUsingTransparentPadding = true,
                RequestId = new Guid(),
                Height = size,
                Width = size,
            };

            options.FileName = $"thumbnail-{options.Width}x{options.Height}.png";

            CreateThumbnailOfCoverImage(book, options);

            var thumbnailDestination = Path.Combine(book.FolderPath, options.FileName);
            return thumbnailDestination;
        }

        /// <summary>
        /// Check if the cover image is valid
        /// </summary>
        /// <returns>True if it's valid. It may be invalid if imageSrc is missing. In certain scenarios, if the imageSrc is "placeHolder.png", that's not valid either.</returns>
        internal static bool IsCoverImageSrcValid(
            string imageSrc,
            HtmlThumbNailer.ThumbnailOptions options
        )
        {
            if (string.IsNullOrEmpty(imageSrc))
                return false;
            else if (Path.GetFileName(imageSrc) == "placeHolder.png")
            {
                // Valid examples:
                // thumbnail.png
                // thumbnail-256.png
                // thumbnail-300x300.png
                if (Regex.IsMatch(options.FileName, "thumbnail(-[0-9]+(x[0-9]+)?)?\\.png"))
                    return true;
                else
                    return false;
            }
            else
                return true;
        }

        private static readonly HashSet<Type> kExceptionsToRetryWhenSavingImage = new HashSet<Type>
        {
            Type.GetType("System.IO.IOException"),
            Type.GetType("System.Runtime.InteropServices.ExternalException"),
            // PalasoImage.SaveImageSafely can also throw ApplicationExceptions
            // (See https://github.com/sillsdev/libpalaso/blob/f2482a5b3c6c75b50ec5672b1eb731b1a040a05a/SIL.Windows.Forms/ImageToolbox/PalasoImage.cs#L155)
            // This very well may be temporary (if it's a different Bloom thread that has it locked) and retrying it would likely succeed.
            // For ideas about more fundamental fixes, see https://issues.bloomlibrary.org/youtrack/issue/BL-12359/The-program-could-not-replace-the-image-C...Book-2thumbnail.png-perhaps-because-this-program-or-another-locked-it#focus=Comments-102-50093.0-0
            Type.GetType("System.ApplicationException"),
        };

        /// <summary>
        /// Creates a thumbnail of just the cover image (no title, language name, etc.)
        /// </summary>
        /// <returns>Returns true if successful; false otherwise. </returns>
        internal static bool CreateThumbnailOfCoverImage(
            Book.Book book,
            HtmlThumbNailer.ThumbnailOptions options,
            Action<Image> callback = null
        )
        {
            // If the book has been renamed we'll get a new request for the thumbnail of the new name.
            // If it's been deleted, we don't need a thumbnail for it.
            // If something else is wrong...well, at worst we use a generic thumbnail.
            if (!Directory.Exists(book.FolderPath))
                return false;
            var imageSrc = book.GetCoverImagePath();
            if (!IsCoverImageSrcValid(imageSrc, options))
            {
                Debug.WriteLine(book.StoragePageFolder + " does not have a cover image.");
                return false;
            }
            var size = Math.Max(options.Width, options.Height);
            var destFilePath = Path.Combine(book.StoragePageFolder, options.FileName);
            // Writing a transparent image to a file, then reading it in again appears to be the only
            // way to get the thumbnail image to draw with the book's cover color background reliably.
            var transparentImageFile = Path.Combine(
                Path.GetTempPath(),
                "Bloom",
                "Transparent",
                Path.GetFileName(imageSrc)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(transparentImageFile));
            try
            {
                if (
                    RuntimeImageProcessor.MakePngBackgroundTransparentIfDesirable(
                        imageSrc,
                        transparentImageFile
                    )
                )
                    imageSrc = transparentImageFile;
                using (var coverImage = PalasoImage.FromFileRobustly(imageSrc))
                {
                    if (
                        imageSrc == transparentImageFile
                        || ImageUtils.HasTransparency(coverImage.Image)
                    )
                        coverImage.Image = MakeImageOpaque(coverImage.Image, book.GetCoverColor());
                    var shouldAddDashedBorder =
                        options.BorderStyle == HtmlThumbNailer.ThumbnailOptions.BorderStyles.Dashed;
                    coverImage.Image = options.CenterImageUsingTransparentPadding
                        ? ImageUtils.CenterImageIfNecessary(
                            new Size(size, size),
                            coverImage.Image,
                            shouldAddDashedBorder
                        )
                        : ImageUtils.ResizeImageIfNecessary(
                            new Size(size, size),
                            coverImage.Image,
                            shouldAddDashedBorder
                        );
                    switch (Path.GetExtension(destFilePath).ToLowerInvariant())
                    {
                        case ".jpg":
                        case ".jpeg":
                            ImageUtils.SaveAsTopQualityJpeg(coverImage.Image, destFilePath);
                            break;
                        default:
                            coverImage.SaveImageRobustly(
                                destFilePath,
                                kExceptionsToRetryWhenSavingImage
                            );
                            break;
                    }
                    if (callback != null)
                        callback(coverImage.Image.Clone() as Image); // don't leave GC to chance
                }
            }
            finally
            {
                if (RobustFile.Exists(transparentImageFile))
                    RobustFile.Delete(transparentImageFile);
            }
            return true;
        }

        private static Image MakeImageOpaque(Image source, string coverColorString)
        {
            var target = new Bitmap(source.Width, source.Height);
            Color coverColor;
            ImageUtils.TryCssColorFromString(coverColorString, out coverColor);
            ImageUtils.DrawImageWithOpaqueBackground(source, target, coverColor);
            return target;
        }

        /// <summary>
        /// Make a thumbnail image of a book's front cover.
        /// </summary>
        /// <param name="book"></param>
        /// <param name="height">Optional parameter. If unspecified, use defaults</param>
        public void MakeThumbnailOfCover(
            Book.Book book,
            int height = -1,
            Guid requestId = new Guid()
        )
        {
            HtmlThumbNailer.ThumbnailOptions options = GetCoverThumbnailOptions(height, requestId);
            RebuildThumbNailNow(book, options);
        }

        /// <summary>
        /// Gets the default thumbnail options to use when creating a thumbnail of a book's cover
        /// </summary>
        public static HtmlThumbNailer.ThumbnailOptions GetCoverThumbnailOptions(
            int height,
            Guid requestId
        )
        {
            var options = new HtmlThumbNailer.ThumbnailOptions
            {
                //since this is destined for HTML, it's much easier to handle if there is no pre-padding
                CenterImageUsingTransparentPadding = false,
                RequestId = requestId
            };

            if (height != -1)
            {
                options.Height = height;
                options.Width = -1;
                options.FileName = "thumbnail-" + height + ".png";
            }
            // else use the defaults

            return options;
        }

        ///   <summary>
        ///   Currently used by the image server
        ///   to get thumbnails that are used in the add page dialog. Since this dialog can show
        ///   an enlarged version of the page, we generate these at a higher resolution than usual.
        ///   Also, to make more realistic views of template pages we insert fake text wherever
        ///   there is an empty edit block.
        ///
        ///   The result is cached for possible future use so the caller should not dispose of it.
        ///   </summary>
        public async Task<Image> GetThumbnailForPage(
            Book.Book book,
            IPage page,
            bool isLandscape,
            bool isSquare,
            bool mustRegenerate = false
        )
        {
            var pageDom = book.GetThumbnailXmlDocumentForPage(page);
            var thumbnailOptions = new HtmlThumbNailer.ThumbnailOptions()
            {
                BackgroundColor = Color.White, // matches the hand-made previews.
                BorderStyle = HtmlThumbNailer.ThumbnailOptions.BorderStyles.None, // allows the HTML to add its preferred border in the larger preview
                CenterImageUsingTransparentPadding = true,
                MustRegenerate = mustRegenerate
            };
            var pageDiv = pageDom.RawDom
                .SafeSelectNodes("descendant-or-self::div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            // The actual page size is rather arbitrary, but we want the right ratio for A4.
            // Using the actual A4 sizes in mm makes a big enough image to look good in the larger
            // preview box on the right as well as giving exactly the ratio we want.
            // We need to make the image the right shape to avoid some sort of shadow/box effects
            // that I can't otherwise find a way to get rid of.
            if (isSquare)
            {
                thumbnailOptions.Width = 210; // Image is square, but otherwise displayed similarly to Landscape
                thumbnailOptions.Height = 210;
                SetSizeAndOrientationClass(pageDiv, "Cm13Landscape");
            }
            else if (isLandscape)
            {
                thumbnailOptions.Width = 297;
                thumbnailOptions.Height = 210;
                SetSizeAndOrientationClass(pageDiv, "A4Landscape");
            }
            else
            {
                thumbnailOptions.Width = 210;
                thumbnailOptions.Height = 297;
                // On the offchance someone makes a template with by-default-landscape pages...
                SetSizeAndOrientationClass(pageDiv, "A4Portrait");
            }
            // In different books (or even the same one) in the same session we may have portrait and landscape
            // versions of the same template page. So we must use different IDs.
            return await _thumbnailProvider.GetThumbnail(
                page.Id + (isSquare ? "S" : (isLandscape ? "L" : "")),
                pageDom,
                thumbnailOptions
            );
        }

        private void SetSizeAndOrientationClass(SafeXmlElement pageDiv, string sizeOrientationClass)
        {
            var classes = pageDiv.GetAttribute("class");
            if (string.IsNullOrWhiteSpace(classes))
            {
                pageDiv.SetAttribute("class", sizeOrientationClass);
                return;
            }
            var parts = classes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var classBldr = new StringBuilder();
            foreach (var part in parts)
            {
                if (
                    part.ToLowerInvariant().EndsWith("portrait")
                    || part.ToLowerInvariant().EndsWith("landscape")
                )
                    continue;
                if (classBldr.Length > 0)
                    classBldr.Append(" ");
                classBldr.Append(part);
            }
            if (classBldr.Length > 0)
                classBldr.Append(" ");
            classBldr.Append(sizeOrientationClass);
            pageDiv.SetAttribute("class", classBldr.ToString());
        }

        /// <summary>
        /// Will call either 'callback' or 'errorCallback' UNLESS the thumbnail is readonly, in which case it will do neither.
        /// </summary>
        /// <param name="book"></param>
        /// <param name="thumbnailOptions"></param>
        /// <param name="callback"></param>
        /// <param name="errorCallback"></param>
        public void RebuildThumbNailAsync(
            Book.Book book,
            HtmlThumbNailer.ThumbnailOptions thumbnailOptions,
            Action<BookInfo, Image> callback,
            Action<BookInfo, Exception> errorCallback
        )
        {
            RebuildThumbNail(book, thumbnailOptions, callback, errorCallback, true);
        }

        /// <summary>
        /// Will make a new thumbnail (or throw) UNLESS the thumbnail is readonly, in which case it will do nothing.
        /// </summary>
        /// <param name="book"></param>
        /// <param name="thumbnailOptions"></param>
        private void RebuildThumbNailNow(
            Book.Book book,
            HtmlThumbNailer.ThumbnailOptions thumbnailOptions
        )
        {
            RebuildThumbNail(
                book,
                thumbnailOptions,
                (info, image) => { },
                (info, ex) =>
                {
                    throw ex;
                },
                false
            );
        }

        private static HtmlThumbNailer.ThumbnailOptions.BorderStyles GetThumbnailBorderStyle(
            Book.Book book
        )
        {
            return book.IsTemplateBook
                ? HtmlThumbNailer.ThumbnailOptions.BorderStyles.Dashed
                : HtmlThumbNailer.ThumbnailOptions.BorderStyles.None;
        }

        /// <summary>
        /// Will call either 'callback' or 'errorCallback' UNLESS the thumbnail is readonly, in which case it will do neither.
        /// </summary>
        /// <param name="book"></param>
        /// <param name="thumbnailOptions"></param>
        /// <param name="callback"></param>
        /// <param name="errorCallback"></param>
        private void RebuildThumbNail(
            Book.Book book,
            HtmlThumbNailer.ThumbnailOptions thumbnailOptions,
            Action<BookInfo, Image> callback,
            Action<BookInfo, Exception> errorCallback,
            bool async
        )
        {
            try
            {
                if (!book.Storage.RemoveBookThumbnail(thumbnailOptions.FileName))
                {
                    // thumbnail is marked readonly, so just use it
                    Image thumb;
                    book.Storage.TryGetPremadeThumbnail(thumbnailOptions.FileName, out thumb);
                    callback(book.BookInfo, thumb);
                    return;
                }

                _thumbnailProvider.RemoveFromCache(book.Storage.Key);

                thumbnailOptions.BorderStyle = GetThumbnailBorderStyle(book);
                GetThumbNailOfBookCover(
                    book,
                    thumbnailOptions,
                    image => callback(book.BookInfo, image),
                    error =>
                    {
                        //Enhance; this isn't a very satisfying time to find out, because it's only going to happen if we happen to be rebuilding the thumbnail.
                        //It does help in the case where things are bad, so no thumbnail was created, but by then probably the user has already had some big error.
                        //On the other hand, given that they have this bad book in their collection now, it's good to just remind them that it's broken and not
                        //keep showing green error boxes.
                        book.CheckForErrors();
                        errorCallback(book.BookInfo, error);
                    },
                    async
                );
            }
            catch (Exception error)
            {
                NonFatalProblem.Report(
                    ModalIf.Alpha,
                    PassiveIf.All,
                    "Problem creating book thumbnail ",
                    exception: error
                );
            }
        }
    }
}
