using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.ImageProcessing
{
    /// <summary>
    /// Currently the only processing we're doing is to make PNGs with lots of whitespace look good against our colored background pages
    /// Previously, we also shrunk images to improve performance when we were handing out file paths. Now that we are giving images
    /// over http, gecko may do well enough without the shrinking.
    /// </summary>
    public class RuntimeImageProcessor : IDisposable
    {
        private readonly BookRenamedEvent _bookRenamedEvent;
        public int TargetDimension = 500;

        // the ConcurrentDictionary is thread-safe
        private ConcurrentDictionary<string, string> _originalPathToProcessedVersionPath;

        // using a ConcurrentDictionary because there isn't a thread-safe List in .Net 4.0
        private ConcurrentDictionary<string, bool> _imageFilesToReturnUnprocessed;

        private string _cacheFolder;

        private static ImageAttributes _convertWhiteToTransparent;

        public RuntimeImageProcessor(BookRenamedEvent bookRenamedEvent)
        {
            _bookRenamedEvent = bookRenamedEvent;
            _originalPathToProcessedVersionPath = new ConcurrentDictionary<string, string>();
            _imageFilesToReturnUnprocessed = new ConcurrentDictionary<string, bool>();
            _cacheFolder = Path.Combine(Path.GetTempPath(), "Bloom");
            _bookRenamedEvent.Subscribe(OnBookRenamed);
        }

        private static ImageAttributes ConvertWhiteToTransparent
        {
            get
            {
                if (_convertWhiteToTransparent == null)
                {
                    _convertWhiteToTransparent = new ImageAttributes();
                    _convertWhiteToTransparent.SetColorKey(
                        Color.FromArgb(253, 253, 253),
                        Color.White
                    );
                }
                return _convertWhiteToTransparent;
            }
        }

        private void OnBookRenamed(KeyValuePair<string, string> fromPathAndToPath)
        {
            //Note, we don't pay attention to what the change was, we just purge the whole cache

            TryToDeleteCachedImages();
            _originalPathToProcessedVersionPath = new ConcurrentDictionary<string, string>();
            _imageFilesToReturnUnprocessed = new ConcurrentDictionary<string, bool>();
        }

        public void Dispose()
        {
            if (_originalPathToProcessedVersionPath == null)
                return;

            TryToDeleteCachedImages();
            _originalPathToProcessedVersionPath = null;

            //NB: this turns out to be dangerous. Without it, we still delete all we can, leave some files around
            //each time, and then deleting them on the next run
            //			_cacheFolder.Dispose();

            GC.SuppressFinalize(this);
        }

        private void TryToDeleteCachedImages()
        {
            lock (this)
            {
                foreach (var path in _originalPathToProcessedVersionPath.Values)
                {
                    try
                    {
                        if (RobustFile.Exists(path))
                        {
                            RobustFile.Delete(path);
                            Debug.WriteLine("RuntimeImageProcessor Successfully deleted: " + path);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("RuntimeImageProcessor Dispose(): " + e.Message);
                    }
                }
                _originalPathToProcessedVersionPath.Clear();
            }
        }

        public string GetPathToAdjustedImage(
            string originalPath,
            bool getThumbnail = false,
            bool isForCover = false
        )
        {
            //don't mess with Bloom UI images
            if (new[] { "/img/", "placeHolder", "Button" }.Any(s => originalPath.Contains(s)))
                return originalPath;

            var cacheFileName = originalPath;

            if (getThumbnail)
            {
                cacheFileName = "thumbnail_" + cacheFileName;
            }

            // check if this image is in the do-not-process list
            bool test;
            if (_imageFilesToReturnUnprocessed.TryGetValue(cacheFileName, out test))
                return originalPath;

            lock (this)
            {
                // if there is a cached version, return it
                string pathToProcessedVersion;
                if (
                    _originalPathToProcessedVersionPath.TryGetValue(
                        cacheFileName,
                        out pathToProcessedVersion
                    )
                )
                {
                    if (
                        RobustFile.Exists(pathToProcessedVersion)
                        && new FileInfo(originalPath).LastWriteTimeUtc
                            <= new FileInfo(pathToProcessedVersion).LastWriteTimeUtc
                    )
                    {
                        return pathToProcessedVersion;
                    }

                    // the file has changed, remove from cache
                    string valueRemoved;
                    _originalPathToProcessedVersionPath.TryRemove(cacheFileName, out valueRemoved);
                }

                // there is not a cached version, try to make one
                var pathToProcessedImage = Path.Combine(
                    _cacheFolder,
                    Path.GetRandomFileName() + Path.GetExtension(originalPath)
                );

                if (!Directory.Exists(Path.GetDirectoryName(pathToProcessedImage)))
                    Directory.CreateDirectory(Path.GetDirectoryName(pathToProcessedImage));

                // BL-1112: images not loading in page thumbnails
                var success = true;
                var wantOriginal = !getThumbnail && !isForCover;
                if (getThumbnail)
                {
                    // The HTML div that contains the thumbnails is 80 pixels wide, so make the thumbnails 80 pixels wide
                    success = GenerateThumbnail(originalPath, pathToProcessedImage, 80);
                }
                else if (isForCover)
                {
                    success = MakePngBackgroundTransparentIfDesirable(
                        originalPath,
                        pathToProcessedImage
                    );
                }

                if (wantOriginal || !success)
                {
                    // add this image to the do-not-process list so we don't waste time doing this again
                    if (!success)
                        _imageFilesToReturnUnprocessed.TryAdd(cacheFileName, true);
                    return originalPath;
                }

                _originalPathToProcessedVersionPath.TryAdd(cacheFileName, pathToProcessedImage); //remember it so we can reuse if they show it again, and later delete

                return pathToProcessedImage;
            }
        }

        // Overload of the below method that defaults to NOT changing the background color of the thumb.
        public static bool GenerateThumbnail(
            string originalPath,
            string pathToProcessedImage,
            int newWidth
        )
        {
            return GenerateThumbnail(originalPath, pathToProcessedImage, newWidth, Color.Empty);
        }

        // Make a thumbnail of the input image. newWidth and newHeight are both limits; the image will not
        // be larger than original, but if necessary will be shrunk to fit within the indicated rectangle.
        // If parameter 'backColor' is not Empty, we fill the background of the thumbnail with that color.
        public static bool GenerateThumbnail(
            string originalPath,
            string pathToProcessedImage,
            int newWidth,
            Color backColor
        )
        {
            using (var originalImage = PalasoImage.FromFileRobustly(originalPath))
            {
                // check if it needs resized
                if (originalImage.Image.Width <= newWidth)
                    return false;

                // calculate dimensions
                var newW =
                    (originalImage.Image.Width > newWidth) ? newWidth : originalImage.Image.Width;
                // allow for proper rounding from the division
                var newH =
                    (newW * originalImage.Image.Height + (originalImage.Image.Width / 2))
                    / originalImage.Image.Width;

                var thumbnail = new Bitmap(newW, newH);

                var g = Graphics.FromImage(thumbnail);
                if (backColor != Color.Empty)
                {
                    using (var brush = new SolidBrush(backColor))
                    {
                        g.FillRectangle(brush, new Rectangle(0, 0, newW, newH));
                    }
                }
                Image imageToDraw = originalImage.Image;
                bool makeTransparentImage = ImageUtils.ShouldMakeBackgroundTransparent(
                    originalImage
                );
                if (makeTransparentImage)
                {
                    imageToDraw = MakePngBackgroundTransparent(originalImage);
                }
                var destRect = new Rectangle(0, 0, newW, newH);
                // Note the image size may change when the background is made transparent.
                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-5632.
                g.DrawImage(
                    imageToDraw,
                    destRect,
                    new Rectangle(0, 0, imageToDraw.Width, imageToDraw.Height),
                    GraphicsUnit.Pixel
                );
                if (makeTransparentImage)
                    imageToDraw.Dispose();
                RobustImageIO.SaveImage(thumbnail, pathToProcessedImage);
            }

            return true;
        }

        // Make a thumbnail of the input image. newWidth and newHeight are both limits; the image will not be larger than original,
        // but if necessary will be shrunk to fit within the indicated rectangle.
        public static bool GenerateEBookThumbnail(
            string coverImagePath,
            string pathToProcessedImage,
            int thumbnailWidth,
            int thumbnailHeight,
            Color backColor,
            bool padImageToRequestedSize = true
        )
        {
            if (ImageUtils.IsPlaceholderImageFilename(coverImagePath))
            {
                coverImagePath = Path.Combine(
                    FileLocationUtilities.DirectoryOfApplicationOrSolution,
                    "DistFiles",
                    "Blank.png"
                );
            }
            using (var coverImage = PalasoImage.FromFileRobustly(coverImagePath))
            {
                var coverImageWidth = coverImage.Image.Width;
                var coverImageHeight = coverImage.Image.Height;

                // We want to see a small border of background color, even if the image is a photo.
                const int kborder = 1;
                var availableThumbnailWidth = thumbnailWidth - (2 * kborder);
                var availableThumbnailHeight = thumbnailHeight - (2 * kborder);

                // Calculate how big the image can be while keeping its original proportions.
                // First assume the width is the limiting factor
                var targetImageWidth =
                    (coverImageWidth > availableThumbnailWidth)
                        ? availableThumbnailWidth
                        : coverImage.Image.Width;
                var targetImageHeight = targetImageWidth * coverImageHeight / coverImageWidth;

                // if actually the height is the limiting factor, maximize height and re-compute the width
                if (targetImageHeight > availableThumbnailHeight)
                {
                    targetImageHeight = availableThumbnailHeight;
                    targetImageWidth = targetImageHeight * coverImageWidth / coverImageHeight;
                }

                // pad to center the cover image
                var horizontalPadding = (availableThumbnailWidth - targetImageWidth) / 2;
                var verticalPadding = (availableThumbnailHeight - targetImageHeight) / 2;
                if (!padImageToRequestedSize)
                {
                    horizontalPadding = 0;
                    verticalPadding = 0;
                }
                var destRect = new Rectangle(
                    kborder + horizontalPadding,
                    kborder + verticalPadding,
                    targetImageWidth,
                    targetImageHeight
                );

                // the decision here is just a heuristic based on the observation that line-drawings seem to look better in nice square block of color,
                // while full-color (usually jpeg) books look better with a thin (or no) border. We could put this under user control eventually.

                Rectangle backgroundAndBorderRect;
                var appearsToBeJpeg = ImageUtils.AppearsToBeJpeg(coverImage);
                if (appearsToBeJpeg || !padImageToRequestedSize)
                {
                    backgroundAndBorderRect = destRect;
                    backgroundAndBorderRect.Inflate(kborder * 2, kborder * 2);
                }
                else
                {
                    // or, if we decide to always deliver the full thing:
                    backgroundAndBorderRect = new Rectangle(0, 0, thumbnailWidth, thumbnailHeight);
                }
                var newWidth = thumbnailWidth;
                var newHeight = thumbnailHeight;
                if (!padImageToRequestedSize)
                {
                    newWidth = Math.Min(newWidth, targetImageWidth + kborder * 2);
                    newHeight = Math.Min(newHeight, targetImageHeight + kborder * 2);
                }

                using (var thumbnail = new Bitmap(newWidth, newHeight))
                using (var g = Graphics.FromImage(thumbnail))
                using (var brush = new SolidBrush(backColor))
                {
                    g.FillRectangle(brush, backgroundAndBorderRect);

                    lock (ConvertWhiteToTransparent)
                    {
                        var imageAttributes =
                            (
                                appearsToBeJpeg
                                || !ImageUtils.ShouldMakeBackgroundTransparent(coverImage)
                            )
                                ? null
                                : ConvertWhiteToTransparent;
                        g.DrawImage(
                            coverImage.Image, // finally, draw the cover image
                            destRect, // with a scaled and centered destination
                            0,
                            0,
                            coverImageWidth,
                            coverImageHeight, // from the entire cover image,
                            GraphicsUnit.Pixel,
                            imageAttributes
                        ); // changing white to transparent if a B&W/greyscale png
                    }
                    if (!appearsToBeJpeg || Path.GetExtension(pathToProcessedImage) == ".png")
                        ImageUtils.SaveOrDeletePngImageToPath(thumbnail, pathToProcessedImage);
                    else
                        RobustImageIO.SaveImage(thumbnail, pathToProcessedImage);
                    // PNG thumbnails created from jpeg files seem to often be way too big, so try to save them as jpeg
                    // files instead if it saves space.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-5605.
                    if (
                        appearsToBeJpeg
                        && Path.GetFileName(pathToProcessedImage) == "thumbnail.png"
                    )
                    {
                        var jpgPath = Path.ChangeExtension(pathToProcessedImage, "jpg");
                        RobustImageIO.SaveImage(thumbnail, jpgPath, ImageFormat.Jpeg);
                        var infoPng = new FileInfo(pathToProcessedImage);
                        var infoJpg = new FileInfo(jpgPath);
                        //Debug.WriteLine(String.Format("thumbnail.png size={0}; thumbnail.jpg size={1} (using smaller)", infoPng.Length, infoJpg.Length));
                        if (infoJpg.Length < infoPng.Length)
                            RobustFile.Delete(pathToProcessedImage);
                        else
                            RobustFile.Delete(jpgPath);
                    }
                }
            }

            return true;
        }

        public static Image MakePngBackgroundTransparent(PalasoImage originalImage)
        {
            if (ImageUtils.HasTransparency(originalImage.Image))
                return originalImage.Image.Clone() as Image;

            // If the image is indexed and opaque, convert the palette to have a transparent background.  This produces
            // a much smaller image file than the process of redrawing the image with a transparent conversion.  That
            // process always produces a 32-bit RGBA format image.  Changing the palette should also be faster than
            // redrawing in a new size and format.
            // libgdiplus on Linux doesn't handle Alpha (transparency) information for indexed format images.
            if (
                SIL.PlatformUtilities.Platform.IsWindows
                && ImageUtils.IsIndexedAndOpaque(originalImage.Image)
            )
            {
                var revisedBitmap = originalImage.Image.Clone() as Image;
                revisedBitmap.Palette = GivePaletteTransparentBackground(revisedBitmap);
                return revisedBitmap;
            }
            //impose a maximum size because in BL-2871 "Opposites" had about 6k x 6k and we got an ArgumentException
            //from the new BitMap()
            var destinationWidth = Math.Min(1000, originalImage.Image.Width);
            var destinationHeight = (int)(
                (float)originalImage.Image.Height
                * ((float)destinationWidth / (float)originalImage.Image.Width)
            );
            var processedBitmap = new Bitmap(destinationWidth, destinationHeight);
            using (var g = Graphics.FromImage(processedBitmap))
            {
                var destRect = new Rectangle(0, 0, destinationWidth, destinationHeight);
                lock (ConvertWhiteToTransparent)
                {
                    g.DrawImage(
                        originalImage.Image,
                        destRect,
                        0,
                        0,
                        originalImage.Image.Width,
                        originalImage.Image.Height,
                        GraphicsUnit.Pixel,
                        ConvertWhiteToTransparent
                    );
                }
            }
            return processedBitmap;
        }

        private static ColorPalette GivePaletteTransparentBackground(Image bitmap)
        {
            Debug.Assert((bitmap.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed);
            var palette = bitmap.Palette;
            for (var i = 0; i < palette.Entries.Length; i++)
            {
                var color = palette.Entries[i];
                if (ImageUtils.IsNearWhite(color))
                {
                    palette.Entries[i] = Color.FromArgb(0, color.R, color.G, color.B);
                }
            }
            return palette; // assigning this back to the bitmap will actually update it.
        }

        /// <summary>
        /// Make the image background transparent if the image is PNG and appears to be a black
        /// and white drawing.
        /// </summary>
        /// <returns>true if an image with transparent background is created</returns>
        public static bool MakePngBackgroundTransparentIfDesirable(
            string originalPath,
            string pathToProcessedImage
        )
        {
            try
            {
                //if it's a jpeg, we don't resize, we don't mess with transparency, nothing. These things
                //are scary in .net. Just send the original back and wash our hands of it.
                //If the filename extension claims to be jpeg, assume it's not lying to us and quit.
                if (ImageUtils.HasJpegExtension(originalPath))
                    return false;

                using (var originalImage = PalasoImage.FromFileRobustly(originalPath))
                {
                    // double check whether the file extension was misleading us...
                    if (ImageUtils.AppearsToBeJpeg(originalImage))
                        return false;

                    // Check whether a transparent background is actually needed.
                    if (!ImageUtils.ShouldMakeBackgroundTransparent(originalImage))
                        return false;

                    using (var processedBitmap = MakePngBackgroundTransparent(originalImage))
                    {
                        //Hatton July 2012:
                        //Once or twice I saw a GDI+ error on the Save below, when the app 1st launched.
                        //I verified that if there is an IO error, that's what you get (a GDI+ error).
                        //I looked once, and the %temp%/Bloom directory wasn't there, so that's what I think caused the error.
                        //It's not clear why the temp/bloom directory isn't there... possibly it was there a moment ago
                        //but then some startup thread cleared and deleted it? (we are now running on a thread responding to the http request)

                        Exception error = null;
                        for (var i = 0; i < 3; i++) //try three times
                        {
                            try
                            {
                                error = null;
                                RobustImageIO.SaveImage(
                                    processedBitmap,
                                    pathToProcessedImage,
                                    originalImage.Image.RawFormat
                                );
                                break;
                            }
                            catch (Exception e)
                            {
                                Logger.WriteEvent(
                                    "***Error in RuntimeImageProcessor while trying to write image."
                                );
                                Logger.WriteEvent(e.Message);
                                error = e;
                                //in setting the sleep time, keep in mind that this may be one of 20 images
                                //so if the problem happens to all of them, then you're looking 20*retries*sleep-time,
                                //which will look like hung program.
                                //Meanwhile, this transparency thing is actually just a nice-to-have. If we give
                                //up, it's ok.
                                Thread.Sleep(100); //wait a 1/5 second before trying again
                            }
                        }

                        if (error != null)
                        {
                            throw error; //will be caught below
                        }
                    }
                }

                return true;
            }
            // We want to gracefully degrade if this fails (as it did once, see comment in BL-2871)
            // We can't help users who respond to a toast and send in an error report.
            // Logging it will allow us to possibly correlate an error here with another problem that does get reported.
            catch (TagLib.CorruptFileException e)
            {
                // Don't bother reporting this to the user, but log it in case it might be relevant for a real bug.
                Logger.WriteError(
                    $"Problem with image metadata: MakePngBackgroundTransparentIfDesirable({originalPath}) caught an exception.",
                    e
                );
                return false;
            }
            catch (Exception e)
            {
                Logger.WriteError(
                    $"Problem making image transparent: MakePngBackgroundTransparentIfDesirable({originalPath}) caught an exception.",
                    e
                );
                return false;
            }
        }
    }
}
