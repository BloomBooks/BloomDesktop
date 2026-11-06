using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.Publish.BloomPub;
using Bloom.SafeXml;
using Bloom.web.controllers;

namespace Bloom.Publish.Rab
{
    /// <summary>
    /// Estimates how big a book's BloomPUB will be without making it (BL-17062). Making a BloomPUB
    /// for every book in a collection just to show a size bar would take far too long, but the
    /// size of the book folder is a very poor stand-in: it includes files a BloomPUB leaves out
    /// (a PDF from an earlier publish, backups, ...), and it counts pictures at their original
    /// size, which can be many times bigger than the resized copies a BloomPUB holds.
    ///
    /// So we count only the files <see cref="BloomPubMaker.MakeFilter"/> lets into a BloomPUB, and
    /// count each picture at the size it is likely to have after
    /// <see cref="BloomPubMaker.CompressImages"/> shrinks it to the publish resolution. Reading a
    /// picture's dimensions only touches its header, so this is fast.
    ///
    /// A PNG's published size depends on whether it has transparent pixels: publishing turns an
    /// opaque PNG into a much smaller JPEG, but keeps a transparent one as a PNG. The PNG's header
    /// often settles it (no alpha channel and no tRNS chunk means opaque). When it doesn't, finding
    /// out means decoding the picture, about 0.1 seconds each, which would hold up the Apps screen
    /// for many seconds on a large collection. So such a PNG counts as transparent, the larger
    /// estimate; the user can get the exact size with "Measure exact sizes" (BL-17062).
    ///
    /// Results are remembered per book until one of the book's files changes.
    /// </summary>
    public class BloomPubSizeEstimator
    {
        /// <summary>
        /// The size we assume an opaque PNG takes per pixel once publishing has turned it into a
        /// JPEG. For the paintings and photos that make PNGs big that is typically 0.2-0.5 bytes
        /// per pixel; we pick the top of that range.
        /// </summary>
        internal const double kAssumedJpegBytesPerPixel = 0.5;

        /// <summary>
        /// The size we assume a PNG takes per pixel when publishing shrinks it but must keep it a
        /// PNG because it has transparency. A painting shrunk to 720x720 measured about 2 bytes per
        /// pixel (BL-17062): shrinking packs more detail into each pixel, so this is more per pixel
        /// than the original had. Line art comes out much smaller, so this errs high for it.
        /// </summary>
        internal const double kAssumedResizedPngBytesPerPixel = 2.0;

        private readonly object _cacheLock = new object();

        // Book folder path → the estimate, plus the key it was computed for.
        private readonly Dictionary<string, (string Key, long SizeBytes)> _cache = new Dictionary<
            string,
            (string, long)
        >(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Estimate the size of the BloomPUB that would be made from the book in
        /// <paramref name="bookFolderPath"/>, in bytes, with pictures limited to
        /// <paramref name="imageSettings"/>. <paramref name="appearanceSettings"/> (may be null)
        /// gives the cover color, which decides whether cover pictures are made transparent. The
        /// size is 0 if the folder doesn't exist.
        /// </summary>
        public long EstimateBloomPubSize(
            string bookFolderPath,
            ImagePublishSettings imageSettings,
            AppearanceSettings appearanceSettings = null
        )
        {
            if (!Directory.Exists(bookFolderPath))
                return 0;
            // BookFileFilter works out relative paths by chopping the folder path off the front of
            // each file's full path, so the two must be spelled the same way.
            bookFolderPath = Path.GetFullPath(bookFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var files = new DirectoryInfo(bookFolderPath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var key = MakeCacheKey(files, imageSettings);
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(bookFolderPath, out var cached) && cached.Key == key)
                    return cached.SizeBytes;
            }

            var estimate = ComputeEstimate(
                bookFolderPath,
                files,
                imageSettings,
                appearanceSettings
            );
            lock (_cacheLock)
            {
                _cache[bookFolderPath] = (key, estimate);
            }
            return estimate;
        }

        /// <summary>
        /// A string that changes whenever a file in the book is added, removed, resized or
        /// rewritten, or the publish resolution changes. Checking it costs only a directory listing.
        /// The appearance settings the estimate uses (the cover color and theme) need no place in
        /// it: changing them rewrites the book's appearance.json, which changes the file list
        /// (JohnThomson, PR 8531).
        /// </summary>
        private static string MakeCacheKey(List<FileInfo> files, ImagePublishSettings imageSettings)
        {
            var keyParts = files.Select(file =>
                $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"
            );
            return $"{imageSettings.MaxWidth}x{imageSettings.MaxHeight}\n"
                + string.Join("\n", keyParts);
        }

        private static long ComputeEstimate(
            string bookFolderPath,
            List<FileInfo> files,
            ImagePublishSettings imageSettings,
            AppearanceSettings appearanceSettings
        )
        {
            var filter = BloomPubMaker.MakeFilter(bookFolderPath);
            // ImagePublishSettings.MaxWidth/MaxHeight are in landscape orientation (width > height),
            // but GetDesiredImageSize expects portrait orientation, so pass Height as maxShortSide.
            // (This matches BloomPubMaker.CompressImages.)
            var maxShortSide = (int)imageSettings.MaxHeight;
            var maxLongSide = (int)imageSettings.MaxWidth;

            var dom = LoadBookDom(bookFolderPath);
            var picturesPublishingProcesses =
                dom == null
                    ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    : FindPicturesPublishingProcesses(dom, appearanceSettings);
            var picturesPublishingDeletes =
                dom == null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : FindPicturesPublishingDeletes(bookFolderPath, dom);

            long result = 0;
            foreach (var file in files)
            {
                if (!filter.ShouldAllow(file.FullName))
                    continue;
                if (
                    picturesPublishingDeletes.Contains(file.Name)
                    && string.Equals(
                        file.DirectoryName,
                        bookFolderPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    continue;
                var extension = file.Extension.ToLowerInvariant();
                if (
                    BookCompressor.CompressableImageFileExtensions.Contains(extension)
                    && picturesPublishingProcesses.TryGetValue(
                        file.Name,
                        out var mayBeMadeTransparent
                    )
                )
                    result += EstimatePublishedImageBytes(
                        file,
                        extension,
                        mayBeMadeTransparent,
                        maxShortSide,
                        maxLongSide
                    );
                else
                    // Everything else (the book's HTML, CSS, fonts, audio, video, and the pictures
                    // publishing keeps but leaves alone) goes in about as it is. Zipping shrinks the text
                    // files some, but they are rarely what makes a book big, and media files
                    // barely compress.
                    result += file.Length;
            }
            return result;
        }

        /// <summary>
        /// The names of the pictures that <see cref="BloomPubMaker.CompressImages"/> will shrink and
        /// re-encode: those that are the picture of an image container or canvas on a page (which
        /// <see cref="BloomPubMaker.ConvertImagesToBackground"/> turns into the background images
        /// CompressImages processes), less those marked bloom-preserveResolution. Any other picture
        /// the book still uses (branding, ...) goes into the BloomPUB at its full size, so it must
        /// be counted that way; pictures it doesn't use are left out altogether (see
        /// <see cref="FindPicturesPublishingDeletes"/>).
        /// Each name maps to whether publishing may make that picture transparent, which turns it
        /// into a PNG whatever its format (see BloomPubMaker.GetElementTransparencyMode): it has
        /// bloom-transparent, or sits on a colored page or as an overlay on a background picture.
        /// In the last two cases publishing makes it transparent only if it looks like line art,
        /// which we can't tell without decoding it, so we assume it might (the larger estimate).
        /// </summary>
        internal static Dictionary<string, bool> FindPicturesPublishingProcesses(
            string bookFolderPath,
            AppearanceSettings appearanceSettings = null
        )
        {
            var dom = LoadBookDom(bookFolderPath);
            return dom == null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : FindPicturesPublishingProcesses(dom, appearanceSettings);
        }

        /// <summary>
        /// <see cref="FindPicturesPublishingProcesses(string, AppearanceSettings)"/> for a book
        /// whose HTML is already loaded.
        /// </summary>
        private static Dictionary<string, bool> FindPicturesPublishingProcesses(
            SafeXmlDocument dom,
            AppearanceSettings appearanceSettings
        )
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            // Motion books in landscape have an all-black background, where publishing makes
            // nothing transparent (BL-6564).
            var body = dom.SafeSelectNodes("//body").Cast<SafeXmlElement>().FirstOrDefault();
            var fullScreenBlack = (body?.GetAttribute("data-bffullscreenpicture") ?? "").Contains(
                "bloomReader"
            );
            void Add(SafeXmlElement element, string name)
            {
                // Matches BloomPubMaker.FindImagesToPreserveResolution, which looks only inside
                // the page's marginBox. That limit may not have been intended (JohnThomson, PR
                // 8531), but the estimate has to follow what publishing actually does.
                if (
                    string.IsNullOrEmpty(name)
                    || element.HasClass("bloom-preserveResolution")
                        && element
                            .SafeSelectNodes("ancestor::div[contains(@class,'marginBox')]")
                            .Length > 0
                )
                    return;
                var page = element
                    .SafeSelectNodes("ancestor::div[contains(@class,'bloom-page')]")
                    .Cast<SafeXmlElement>()
                    .Last();
                var mayBeMadeTransparent =
                    !fullScreenBlack
                    && HtmlDom.GetImageTransparencyMode(
                        element,
                        HtmlDom.PageNeedsTransparentImages(page, appearanceSettings)
                    ) != ImageTransparencyMode.None;
                // A picture used in more than one place is processed for each.
                result[name] =
                    mayBeMadeTransparent || (result.TryGetValue(name, out var already) && already);
            }

            // The usual case: an img in an image container or canvas.
            foreach (
                var img in dom.SafeSelectNodes(
                        "//div[contains(@class,'bloom-page')]//div[contains(@class,'bloom-imageContainer') or contains(@class,'bloom-canvas')]/img[@src]"
                    )
                    .Cast<SafeXmlElement>()
            )
            {
                // An img @src is URL-encoded (see the encoding conventions in UrlPathString).
                Add(
                    img,
                    UrlPathString.CreateFromUrlEncodedString(img.GetAttribute("src")).NotEncoded
                );
            }
            // A few very old books hold a page picture as a background-image style instead, which
            // CompressImages also shrinks. We don't look for those: books are normally brought up
            // to date, and missing one only makes its estimate a little high (JohnThomson, PR 8531).
            return result;
        }

        // The rules BookStorage.CleanupUnusedImageFiles applies when publishing
        // (keepFilesForEditing: false): which top-level picture files it considers, and which it
        // keeps even though the book doesn't reference them.
        private static readonly string[] kCleanupPictureExtensions =
        {
            ".jpg",
            ".png",
            ".svg",
            ".gif",
        };
        private static readonly string[] kCleanupIgnoredNameStarts =
        {
            "thumbnail",
            "license",
            "video-placeholder",
            "coverImage200",
            "widget-placeholder",
        };
        private static readonly string[] kCleanupAlwaysKept =
        {
            "epub-thumbnail.png",
            "custom-thumbnail-256.png",
        };

        /// <summary>
        /// The names of the picture files in the book's folder that publishing deletes as unused
        /// before making the BloomPUB (BloomPubMaker calls BookStorage.CleanupUnusedSupportFiles),
        /// so they must not be counted (JohnThomson, PR 8531). This follows
        /// BookStorage.CleanupUnusedImageFiles. Publishing checks the copy of the book it has just
        /// brought up to date, while we check the book as saved; the two reference the same
        /// pictures unless updating the front or back matter adds one.
        /// </summary>
        internal static HashSet<string> FindPicturesPublishingDeletes(
            string bookFolderPath,
            SafeXmlDocument dom
        )
        {
            var used = new HashSet<string>(
                BookStorage
                    .GetImagePathsRelativeToBook(dom.DocumentElement)
                    .Select(path => path.Normalize(NormalizationForm.FormC)),
                StringComparer.OrdinalIgnoreCase
            );
            return new HashSet<string>(
                Directory
                    .EnumerateFiles(bookFolderPath)
                    .Select(Path.GetFileName)
                    .Where(name =>
                        kCleanupPictureExtensions.Contains(
                            Path.GetExtension(name).ToLowerInvariant()
                        )
                        && !kCleanupIgnoredNameStarts.Any(start =>
                            name.StartsWith(start, StringComparison.OrdinalIgnoreCase)
                        )
                        && !kCleanupAlwaysKept.Contains(name, StringComparer.OrdinalIgnoreCase)
                        && !used.Contains(name.Normalize(NormalizationForm.FormC))
                    ),
                StringComparer.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// The book's HTML, or null if the folder has none.
        /// </summary>
        private static SafeXmlDocument LoadBookDom(string bookFolderPath)
        {
            var htmlPath = BookStorage.FindBookHtmlInFolder(bookFolderPath);
            return string.IsNullOrEmpty(htmlPath)
                ? null
                : XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlPath, false);
        }

        /// <summary>
        /// The approximate size a picture is likely to have in a BloomPUB (see
        /// <see cref="EstimatePublishedImageBytes(int, int, long, bool, bool, bool, int, int)"/>). A
        /// PNG counts as transparent unless its header shows it can't be (see
        /// <see cref="ImageUtils.PngCanHaveTransparency"/>). A picture publishing may make transparent
        /// (<paramref name="mayBeMadeTransparent"/>) counts as a transparent PNG whatever its
        /// format. If we can't read the dimensions, count the file as it is.
        /// </summary>
        private static long EstimatePublishedImageBytes(
            FileInfo file,
            string lowerCaseExtension,
            bool mayBeMadeTransparent,
            int maxShortSide,
            int maxLongSide
        )
        {
            var (width, height) = ImageGalleryApi.GetImageDimensions(file.FullName);
            if (width <= 0 || height <= 0)
                return file.Length;
            var isPng = lowerCaseExtension == ".png";
            var isJpeg = lowerCaseExtension == ".jpg" || lowerCaseExtension == ".jpeg";

            long Estimate(bool hasTransparency) =>
                EstimatePublishedImageBytes(
                    width,
                    height,
                    file.Length,
                    isPng,
                    isJpeg,
                    hasTransparency,
                    maxShortSide,
                    maxLongSide
                );

            var sizeIfTransparent = Estimate(true);
            if (mayBeMadeTransparent)
                return sizeIfTransparent;
            var sizeIfOpaque = Estimate(false);
            if (!isPng)
                return sizeIfOpaque;
            if (
                sizeIfOpaque == sizeIfTransparent
                || !ImageUtils.PngCanHaveTransparency(file.FullName)
            )
                return sizeIfOpaque;
            return sizeIfTransparent;
        }

        /// <summary>
        /// The arithmetic behind the size of a published picture, separated so it can be tested
        /// without real picture files. Mirrors ImageUtils.AdjustImageForDisplay:
        /// - A picture bigger than the publish resolution is shrunk to fit it.
        /// - An opaque PNG becomes a JPEG if that is smaller: we assume
        ///   <see cref="kAssumedJpegBytesPerPixel"/>.
        /// - A shrunk JPEG is saved again at publishing's own JPEG quality, whatever the quality
        ///   of the original, so it too gets <see cref="kAssumedJpegBytesPerPixel"/>. (High-quality
        ///   originals of about 0.8 bytes per pixel came out at about 0.35; BL-17062.) A JPEG that
        ///   isn't shrunk goes in as it is.
        /// - A transparent PNG stays a PNG: if shrunk, we assume
        ///   <see cref="kAssumedResizedPngBytesPerPixel"/>; if not, it goes in as it is.
        /// - Any other picture publishing makes transparent (<paramref name="hasTransparency"/>
        ///   for a non-PNG) becomes a PNG at <see cref="kAssumedResizedPngBytesPerPixel"/>, which
        ///   can be bigger than the original.
        /// - Anything else that is shrunk keeps the same bytes per pixel as before.
        /// Apart from a picture that becomes a PNG, none is counted as bigger than the original.
        /// </summary>
        internal static long EstimatePublishedImageBytes(
            int width,
            int height,
            long originalBytes,
            bool isPng,
            bool isJpeg,
            bool hasTransparency,
            int maxShortSide,
            int maxLongSide
        )
        {
            var publishedSize = ImageUtils.GetDesiredImageSize(
                width,
                height,
                maxShortSide,
                maxLongSide
            );
            var originalPixels = (double)width * height;
            var publishedPixels = Math.Min(
                (double)publishedSize.Width * publishedSize.Height,
                originalPixels
            );
            var isShrunk = publishedPixels < originalPixels;

            double estimate;
            if (isPng && hasTransparency)
                estimate = isShrunk
                    ? Math.Min(originalBytes, publishedPixels * kAssumedResizedPngBytesPerPixel)
                    : originalBytes;
            else if (hasTransparency)
                // Any other picture made transparent is saved as a PNG, which can be bigger than
                // the original.
                estimate = publishedPixels * kAssumedResizedPngBytesPerPixel;
            else if (isPng || (isJpeg && isShrunk))
                estimate = Math.Min(
                    originalBytes * (publishedPixels / originalPixels),
                    publishedPixels * kAssumedJpegBytesPerPixel
                );
            else
                estimate = originalBytes * (publishedPixels / originalPixels);
            return (long)Math.Ceiling(estimate);
        }
    }
}
