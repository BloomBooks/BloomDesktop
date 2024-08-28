using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Utils;
using BloomTemp;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Progress;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Code;
using TagLib;
using TagLib.Png;
using TagLib.Xmp;
using Encoder = System.Drawing.Imaging.Encoder;
using Logger = SIL.Reporting.Logger;
using TempFile = SIL.IO.TempFile;
using Bloom.ToPalaso;
using SIL.CommandLineProcessing;
using SIL.Windows.Forms.ClearShare;
using Bloom.ErrorReporter;
using L10NSharp;

namespace Bloom.ImageProcessing
{
    static class ImageUtils
    {
        public const int MaxLength = 3840; // equals Ultra HD ("4K") long dimension (max width for landscape, height for portrait)
        public const int MaxBreadth = 2800; // balanced lesser size for print, larger than Ultra HD short dimension (max height for landscape, width for portrait)
        public const double MaxImageAspectPortrait = 3840.0 / 2800.0;
        public const double MaxImageAspectLandscape = 2800.0 / 3840.0;

        public static bool AppearsToBeJpeg(PalasoImage imageInfo)
        {
            // A user experienced a crash due to a null object in this section of the code.
            // I've added a couple of checks to prevent that kind of crash here.
            if (imageInfo == null || imageInfo.Image == null)
                return false;
            /*
             * Note, each guid is VERY SIMILAR. The difference is only in the last 2 digits of the 1st group.
               Undefined  B96B3CA9
                MemoryBMP  B96B3CAA
                BMP    B96B3CAB
                EMF    B96B3CAC
                WMF    B96B3CAD
                JPEG    B96B3CAE
                PNG    B96B3CAF
                GIF    B96B3CB0
                TIFF    B96B3CB1
                EXIF    B96B3CB2
                Icon    B96B3CB5
             */
            if (ImageFormat.Jpeg.Guid == imageInfo.Image.RawFormat.Guid)
                return true;

            // If it's been saved with a current path assigned, it may have been saved as a JPEG file,
            // and the caller will be using the current file, not the in-memory Image.
            var currentPath = imageInfo.GetCurrentFilePath();
            if (!String.IsNullOrEmpty(currentPath))
                return IsJpegFile(currentPath);

            // Don't trust the filename extension if the RawFormat and current file don't tell us.
            return false;
        }

        private class ColorInfo
        {
            public Color color;
            public bool isGrayish;
            public bool isNearWhite;
        }

        /// <summary>
        /// Check whether we should try to make the background of this image transparent.
        /// Return true only if this is a two-color image with one of the colors being white.
        /// (or a grayscale picture with one of the colors being white)
        /// Return false also if any pixel encountered in scanning the picture is transparent
        /// at all.
        /// </summary>
        public static bool ShouldMakeBackgroundTransparent(PalasoImage imageInfo)
        {
            // We want to make the white background of Black and White pictures transparent.
            // JPEG pictures generally never meet that criteria and cannot be made transparent anyway.
            if (!AppearsToBePng(imageInfo))
                return false;
            var colors = new List<ColorInfo>();
            if ((imageInfo.Image.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
            {
                var palette = imageInfo.Image.Palette;
                if (palette != null && palette.Entries != null)
                {
                    bool whiteFound = false;
                    foreach (var color in palette.Entries)
                    {
                        if (color.A < 255)
                            return false; // already have transparent pixels
                        if (!IsThisColorForLineDrawing(color, colors, ref whiteFound))
                            return false; // have a 3rd distinct non-gray color
                    }
                    return colors.Count == 2 && whiteFound;
                }
            }
            // Harder to check if not indexed...
            if (imageInfo.Image is Bitmap bitmapImage)
            {
                var whiteFound = false;
                // Yes, this is as expensive as it looks.  But we only sample 100 pixels
                // spread through the picture, stopping as soon as we hit either a
                // transparent pixel or a 3rd distinct non-gray color.
                int yDelta = Math.Max(bitmapImage.Height / 10, 2);
                int xDelta = Math.Max(bitmapImage.Width / 10, 2);
                var randomXFix = GenerateRandomAdjustments(271828182, xDelta);
                var randomYFix = GenerateRandomAdjustments(271828182, yDelta);
                for (int j = 0, y = yDelta / 2; y < bitmapImage.Height; y += yDelta, ++j)
                {
                    j = Math.Min(j, 9);
                    for (int i = 0, x = xDelta / 2; x < bitmapImage.Width; x += xDelta, ++i)
                    {
                        i = Math.Min(i, 9);
                        var y1 = y + randomYFix[j, i];
                        var x1 = x + randomXFix[j, i];
                        y1 = Math.Min(Math.Max(y1, 0), bitmapImage.Height - 1);
                        x1 = Math.Min(Math.Max(x1, 0), bitmapImage.Width - 1);
                        var color = bitmapImage.GetPixel(x1, y1);
                        if (color.A < 255)
                            return false; // already have transparent pixels
                        if (!IsThisColorForLineDrawing(color, colors, ref whiteFound))
                            return false; // have a 3rd distinct non-gray color
                    }
                }
                // At least two colors encountered, likely black and white or greyscale in intent.
                // But if none of the colors is white, return false. (Our code wouldn't make anything
                // transparent anyway.)
                return colors.Count == 2 && whiteFound;
            }
            // we can't tell, so err on the side of caution.
            return false;
        }

        /// <summary>
        /// Check whether this color is near white or grayish, and store the first two colors
        /// encountered.  Return false if we encounter a third color and any of the three colors
        /// are neither near white nor grayish.  (If only two colors are encountered, one of them
        /// must be near white, but the other does not have to be grayish.)  It would be nice to
        /// allow, for example, shades of purple, but that's too hard to do reliably.
        /// </summary>
        private static bool IsThisColorForLineDrawing(
            Color color,
            List<ColorInfo> colors,
            ref bool whiteFound
        )
        {
            var whitish = IsNearWhite(color);
            var grayish = IsGrayish(color);
            if (colors.Count == 0)
            {
                colors.Add(
                    new ColorInfo
                    {
                        color = color,
                        isGrayish = grayish,
                        isNearWhite = whitish
                    }
                );
            }
            else if (colors.Count == 1 && color != colors[0].color)
            {
                colors.Add(
                    new ColorInfo
                    {
                        color = color,
                        isGrayish = grayish,
                        isNearWhite = whitish
                    }
                );
            }
            else if (colors.Count == 2 && color != colors[0].color && color != colors[1].color)
            {
                // NearWhite is not guaranteed to be Grayish, so we have to check both.
                if (
                    !(colors[0].isGrayish || colors[0].isNearWhite)
                    || !(colors[1].isGrayish || colors[1].isNearWhite)
                    || !(grayish || whitish)
                )
                {
                    // we have at least 3 colors, at least one of which is neither white nor gray
                    return false;
                }
            }
            // Enhance: store all distinct colors encountered, not just the first two, and store a
            // count of how often they were found (for the bitmap check).  Then the caller could
            // check all of them for grayishness and whiteness, or do a more sophisticated analysis
            // for being shades of a given color, or (for the bitmap) look at the ratio of white vs
            // non-white colors for line drawing detection.  (Of course, then the name of the method
            // might no longer be appropriate and the return value wouldn't exist.)
            whiteFound |= whitish;
            return true;
        }

        private static int[,] GenerateRandomAdjustments(int seed, int range)
        {
            var rand = new Random(seed);
            var adjustments = new int[10, 10];
            for (var i = 0; i < 10; ++i)
                for (var j = 0; j < 10; ++j)
                    adjustments[i, j] = rand.Next(range) - (range / 2);
            return adjustments;
        }

        /// <summary>
        /// Detect the color range that we would consider "white" and make transparent.
        /// </summary>
        internal static bool IsNearWhite(Color color)
        {
            return color.R >= 253
                && color.R <= 255
                && color.G >= 253
                && color.G <= 255
                && color.B >= 253
                && color.B <= 255;
        }

        internal static bool IsGrayish(Color color)
        {
            return color.R == color.G && color.G == color.B;
        }

        public static bool IsJpegFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
                return false;
            byte[] bytes = new byte[10];
            using (var file = RobustFile.OpenRead(path))
            {
                file.Read(bytes, 0, 10);
            }
            // see https://www.sparkhound.com/blog/detect-image-file-types-through-byte-arrays
            var jpeg = new byte[] { 255, 216, 255, 224 }; // jpeg
            var jpeg2 = new byte[] { 255, 216, 255, 225 }; // jpeg canon

            return jpeg.SequenceEqual(bytes.Take(jpeg.Length))
                || jpeg2.SequenceEqual(bytes.Take(jpeg2.Length));
        }

        public static bool HasJpegExtension(string filename)
        {
            return new[] { ".jpg", ".jpeg" }.Contains(
                Path.GetExtension(filename)?.ToLowerInvariant()
            );
        }

        public static bool AppearsToBePng(PalasoImage imageInfo)
        {
            if (imageInfo == null || imageInfo.Image == null)
                return false;
            if (ImageFormat.Png.Guid == imageInfo.Image.RawFormat.Guid)
                return true;

            // If it's been saved with a current path assigned, it may have been saved as a PNG file,
            // and the caller will be using the current file, not the in-memory Image.
            var currentPath = imageInfo.GetCurrentFilePath();
            if (!String.IsNullOrEmpty(currentPath))
                return IsPngFile(currentPath);

            // Don't trust the original filename extension if the RawFormat and current file don't tell us.
            return false;
        }

        public static bool IsPngFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
                return false;
            byte[] bytes = new byte[10];
            using (var file = RobustFile.OpenRead(path))
            {
                file.Read(bytes, 0, 10);
            }
            // see https://www.sparkhound.com/blog/detect-image-file-types-through-byte-arrays
            var png = new byte[] { 137, 80, 78, 71 }; // PNG

            return png.SequenceEqual(bytes.Take(png.Length));
        }

        public static void ReportImageMetadataProblem(string filePath, Exception ex)
        {
            var msgFmt = LocalizationManager.GetString(
                "EditTab.ImageMetadata.Corrupt",
                "Bloom had a problem with {0}. The file may be corrupted. Please try another image, or try with Bloom 6.0 or newer."
            );
            var msg = string.Format(msgFmt, Path.GetFileName(filePath));
            var btnLabel = LocalizationManager.GetString(
                "EditTab.ImageMetadata.MoreInfo",
                "More Information"
            );
            var settings = new NotifyUserOfProblemSettings(
                AllowSendReport.Disallow,
                btnLabel,
                (str, ex) =>
                {
                    SIL.Program.Process.SafeStart(
                        "https://docs.bloomlibrary.org/image-license-problem"
                    );
                }
            );
            BloomErrorReport.NotifyUserOfProblem(msg, ex, settings);
        }

        /// <summary>
        /// Ensure the image does not exceed the maximum size we've set with MaxLength and MaxBreadth.
        /// Ensure that non-jpeg files have an opaque background.
        /// Make the image a png if it's not a jpeg.  Make large png images into jpeg images to save space.
        /// Save the processed image in the book's folder.
        ///
        /// If the image has a filename, that name is used in creating any new files.
        /// WARNING: imageInfo.Image could be replaced (causing the original to be disposed)
        /// </summary>
        /// <returns>The name of the file, now in the book's folder.</returns>
        public static string ProcessAndSaveImageIntoFolder(
            PalasoImage imageInfo,
            string bookFolderPath,
            bool isSameFile
        )
        {
            LogMemoryUsage();

            // If we go through all the processing and saving machinations for the placeholder image,
            // we just get more and more placeholders when we cut images (BL-9011).
            // And the normal update process that a book goes through when selecting it (in the Collection tab)
            // for editing ensures that the placeHolder.png file is present in the book.
            if (
                !string.IsNullOrEmpty(imageInfo.OriginalFilePath)
                && imageInfo.OriginalFilePath.ToLowerInvariant().EndsWith("placeholder.png")
            )
            {
                return Path.GetFileName(imageInfo.OriginalFilePath);
            }
            if (!Directory.Exists(bookFolderPath))
                throw new DirectoryNotFoundException(bookFolderPath + " does not exist"); // may as well check this early
            bool isEncodedAsJpeg = false;
            try
            {
                var size = GetDesiredImageSize(imageInfo.Image.Width, imageInfo.Image.Height);

                if (
                    size.Width < imageInfo.Image.Width
                    || size.Height < imageInfo.Image.Height
                    || !(AppearsToBeJpeg(imageInfo) || AppearsToBePng(imageInfo))
                )
                {
                    // Either need to shrink the image since it's larger than our maximum allowed size,
                    // or need to convert from a BMP or TIFF file to a PNG file (or both).
                    // NB: the original imageInfo.Image is disposed of in the setter below.
                    // As of now (9/2016) this is safe because there are no other references to it higher in the stack.
                    var img = TryResizeImageWithGraphicsMagick(imageInfo, size);
                    if (img != null)
                    {
                        imageInfo.Image = img;
                    }
                }
                var needToStripMetadata = imageInfo.Metadata.ExceptionCaughtWhileLoading != null;

                isEncodedAsJpeg = AppearsToBeJpeg(imageInfo);
                bool isEncodedAsPng = !isEncodedAsJpeg && AppearsToBePng(imageInfo);

                string jpegFilePath = Path.Combine(
                    bookFolderPath,
                    GetFileNameToUseForSavingImage(bookFolderPath, imageInfo, true)
                );
                var convertedToJpeg =
                    !isEncodedAsJpeg
                    && !HasTransparency(imageInfo.Image)
                    && TryChangeFormatToJpegIfHelpful(imageInfo, jpegFilePath);
                if (convertedToJpeg)
                    return Path.GetFileName(jpegFilePath);

                string imageFileName;
                if (isSameFile)
                    imageFileName = imageInfo.FileName;
                else
                    imageFileName = GetFileNameToUseForSavingImage(
                        bookFolderPath,
                        imageInfo,
                        isEncodedAsJpeg
                    );
                var sourcePath = imageInfo.GetCurrentFilePath();
                var destinationPath = Path.Combine(bookFolderPath, imageFileName);
                if (isEncodedAsJpeg || isEncodedAsPng)
                {
                    if (needToStripMetadata)
                    {
                        if (
                            !TryStripMetadataWithGraphicsMagick(
                                imageInfo,
                                sourcePath,
                                destinationPath
                            )
                        )
                            imageInfo.Image.Save(
                                destinationPath,
                                isEncodedAsJpeg ? ImageFormat.Jpeg : ImageFormat.Png
                            );
                    }
                    // I _think_ isSameFile is true only when we copy an image and paste it back in the same place.
                    // In that case, we don't need to save it again. I checked that when we
                    // use the old cropping tool to create a different image, it doesn't take this path.
                    // As far as I can tell isSameFile is only true if we are copying the file on top of
                    // itself, and that can't ever be useful.
                    else if (!isSameFile)
                    {
                        // Pasting an image can result in sourcePath being null.
                        if (sourcePath == null)
                            imageInfo.Image.Save(
                                destinationPath,
                                isEncodedAsJpeg ? ImageFormat.Jpeg : ImageFormat.Png
                            );
                        else
                            RobustFile.Copy(sourcePath, destinationPath);
                    }
                }
                else
                {
                    imageInfo.Image.Save(destinationPath, ImageFormat.Png); // destinationPath already has .png extension
                }
                if (_createdTempImageFile != null)
                {
                    if (RobustFile.Exists(_createdTempImageFile))
                        RobustFile.Delete(_createdTempImageFile);
                    _createdTempImageFile = null;
                }
                return imageFileName;
            }
            catch (IOException)
            {
                throw; //these are informative on their own
            }
            /* No. OutOfMemory is almost meaningless when it comes to image errors. Better not to confuse people
         * catch (OutOfMemoryException error)
        {
            //Enhance: it would be great if we could bring up that problem dialog ourselves, and offer this picture as an attachment
            throw new ApplicationException("Bloom ran out of memory while trying to import the picture. We suggest that you quit Bloom, run it again, and then try importing this picture again. If that fails, please go to the Help menu and choose 'Report a Problem'", error);
        }*/
            catch (Exception error)
            {
                if (
                    !String.IsNullOrEmpty(imageInfo.FileName)
                    && RobustFile.Exists(imageInfo.OriginalFilePath)
                )
                {
                    var megs = new FileInfo(imageInfo.OriginalFilePath).Length / (1024 * 1000);
                    if (megs > 2)
                    {
                        var msg = String.Format(
                            "Bloom was not able to prepare that picture for including in the book. \r\nThis is a rather large image to be adding to a book --{0} Megs--.",
                            megs
                        );
                        if (isEncodedAsJpeg)
                        {
                            msg +=
                                "\r\nNote, this file is a jpeg, which is normally used for photographs, and complex color artwork. Bloom can handle smallish jpegs, large ones are difficult to handle, especially if memory is limited.";
                        }
                        throw new ApplicationException(msg, error);
                    }
                }

                throw new ApplicationException(
                    "Bloom was not able to prepare that picture for including in the book. We'd like to investigate, so if possible, would you please email it to issues@bloomlibrary.org?"
                        + Environment.NewLine
                        + imageInfo.FileName,
                    error
                );
            }
        }

        /// <summary>
        /// Try to strip the metadata from the image using GraphicsMagick.
        /// </summary>
        /// <returns>true if successful, false if an error occurs or GraphicsMagick can't be found</returns>
        private static bool TryStripMetadataWithGraphicsMagick(
            PalasoImage imageInfo,
            string sourcePath,
            string destinationPath
        )
        {
            var graphicsMagickPath = GetGraphicsMagickPath();
            if (RobustFile.Exists(graphicsMagickPath))
            {
                // http://www.graphicsmagick.org/GraphicsMagick.html#details-profile states:
                // Use +profile profile_name to remove the respective profile.
                // For example, +profile '!icm,*' strips all profiles except for the ICM profile.
                var profiles = "!icm,*"; // strip all metadata except color profile
                if (
                    imageInfo.Metadata?.ExceptionCaughtWhileLoading?.StackTrace != null
                    && imageInfo.Metadata.ExceptionCaughtWhileLoading.StackTrace.Contains(
                        "ReadAPP13Segment"
                    )
                )
                {
                    profiles = "iptc"; // We know taglib-sharp doesn't handle IPTC profiles very well.
                }
                var options = new GraphicsMagickOptions
                {
                    Size = new Size(0, 0),
                    MakeOpaque = false,
                    MakeTransparent = false,
                    JpegQuality = 0,
                    ProfilesToStrip = profiles
                };
                var result = RunGraphicsMagick(sourcePath, destinationPath, options);
                var resultExitCode = result.ExitCode;
                if (resultExitCode != 0)
                {
                    Logger.WriteEvent(
                        $"Failed to strip image metadata from {sourcePath}. ExitCode = {resultExitCode}. StandardError = {result.StandardError}"
                    );
                }
                return resultExitCode == 0;
            }

            Logger.WriteEvent(
                $"Failed to strip image metadata from {sourcePath}. Could not find GraphicsMagick. graphicsMagickPath = {graphicsMagickPath}."
            );
            return false;
        }

        public static void StripMetadataFromImageFile(PalasoImage imageInfo)
        {
            using (
                var tempFile = TempFile.WithExtension(
                    Path.GetExtension(imageInfo.GetCurrentFilePath())
                )
            )
            {
                if (
                    !TryStripMetadataWithGraphicsMagick(
                        imageInfo,
                        imageInfo.GetCurrentFilePath(),
                        tempFile.Path
                    )
                )
                {
                    imageInfo.Image.Save(
                        tempFile.Path,
                        AppearsToBeJpeg(imageInfo) ? ImageFormat.Jpeg : ImageFormat.Png
                    );
                }
                RobustFile.Copy(tempFile.Path, imageInfo.GetCurrentFilePath(), true);
            }
        }

        /// <summary>
        /// Return the largest image size that either matches the original width and height or
        /// is bounded by our predetermined maximums based on Ultra HD and paper sizes at 300dpi.
        /// </summary>
        internal static Size GetDesiredImageSize(int width, int height)
        {
            return GetDesiredImageSize(width, height, MaxBreadth, MaxLength);
        }

        /// <summary>
        /// Determine the largest image size that either matches the original width and height or
        /// fits within the given maximums.  The aspect ratio of the original image is preserved.
        /// </summary>
        /// <param name="width">original width (unknown orientation)</param>
        /// <param name="height">original height (unknown orientation)</param>
        /// <param name="maxShortSide">smaller maximum dimension (portrait orientation width)</param>
        /// <param name="maxLongSide">larger maximum dimension (portrait orientation height)</param>
        /// <returns>maximum size of image that fits within given maximums while keeping the aspect ratio</returns>
        internal static Size GetDesiredImageSize(
            int width,
            int height,
            int maxShortSide,
            int maxLongSide
        )
        {
            var aspect = (double)height / (double)width;
            if (height > width)
            {
                // We're going to determine the size based on fitting it on a portrait-oriented page
                var portraitAspect = (double)maxLongSide / (double)maxShortSide;
                if (height > maxLongSide || width > maxShortSide)
                {
                    if (aspect <= portraitAspect)
                        // closer to square than a standard page, the size is limited by the smaller dimension, the width of a portrait page
                        return new Size(maxShortSide, (int)(aspect * (double)maxShortSide));
                    else
                        // Tall, skinny picture's size is limited by the larger dimension, the height of a portrait page
                        return new Size((int)((double)maxLongSide / aspect), maxLongSide);
                }
            }
            else if (width > height)
            {
                // We're going to determine the size based on fitting it on a landscape-oriented page
                var landscapeAspect = (double)maxShortSide / (double)maxLongSide;
                if (height > maxShortSide || width > maxLongSide)
                {
                    if (aspect > landscapeAspect)
                        // Closer to square than the page, the size is limited by the smaller dimension of the page, which is the height in landscape
                        return new Size((int)((double)maxShortSide / aspect), maxShortSide);
                    else
                        // Low, wide picture, the size is limited by the larger page dimension, which is the width in landscape.
                        return new Size(maxLongSide, (int)(aspect * (double)maxLongSide));
                }
            }
            else
            {
                // square picture
                if (width > maxShortSide)
                    return new Size(maxShortSide, maxShortSide);
            }
            return new Size(width, height);
        }

        private static string GetFileNameToUseForSavingImage(
            string bookFolderPath,
            PalasoImage imageInfo,
            bool isJpeg
        )
        {
            var extension = isJpeg ? ".jpg" : ".png";
            // Some images, like from a scanner or camera, won't have a name yet.  Some will need a number
            // in order to differentiate from what is already there. We don't try and be smart somehow and
            // know when to just replace the existing one with the same name... some other process will have
            // to remove unused images.
            string basename;
            if (String.IsNullOrEmpty(imageInfo.FileName) || imageInfo.FileName.StartsWith("tmp"))
            {
                basename = "image";
            }
            else
            {
                // Even pictures that aren't obviously unnamed or temporary may have the same name.
                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-2627 ("Weird Image Problem").
                basename = Path.GetFileNameWithoutExtension(imageInfo.FileName);
            }
            return GetUnusedFilename(bookFolderPath, basename, extension);
        }

        /// <summary>
        /// Get an unused filename in the given folder based on the basename and extension. "extension" must
        /// start with a period. As well as being unused, the name will be truncated enough to minimize
        /// the danger of exceeding the maximum path length for Windows.  Multiple consecutive spaces will
        /// be collapsed to a single space, and leading and trailing spaces will be removed.
        /// </summary>
        internal static string GetUnusedFilename(
            string bookFolderPath,
            string basenameIn,
            string extension,
            string defaultName = "image"
        )
        {
            if (extension == null)
                extension = "";
            var basename = MiscUtils.TruncateFileBasename(basenameIn, extension, defaultName);
            // basename may already end in one or more digits. Try to strip off digits, parse and increment.
            try
            {
                int i;
                var newBasename = ParseFilename(basename, out i);
                while (
                    RobustFile.Exists(ConstructFilename(bookFolderPath, newBasename, i, extension))
                )
                {
                    ++i;
                }
                return newBasename + GetCounterString(i) + extension;
            }
            catch (System.OverflowException)
            {
                // Image filenames can look like "FB_IMG_1629606288606.jpg", with a huge number at the end.
                // For these files, try appending a hyphen and a number.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-10307.
                var newBasename = basename + "-";
                int i = 1;
                while (
                    RobustFile.Exists(ConstructFilename(bookFolderPath, newBasename, i, extension))
                )
                {
                    ++i;
                }
                return newBasename + GetCounterString(i) + extension;
            }
        }

        private static string ConstructFilename(
            string folderPath,
            string basename,
            int currentNum,
            string extension
        )
        {
            return Path.Combine(folderPath, basename + GetCounterString(currentNum) + extension);
        }

        private static string GetCounterString(int currentCounter)
        {
            return currentCounter == 0
                ? string.Empty
                : currentCounter.ToString(CultureInfo.InvariantCulture);
        }

        private static string ParseFilename(string basename, out int versionNumber)
        {
            const string digits = "0123456789";
            var length = basename.Length;
            var i = length;
            while (i > 0 && digits.Contains(basename[i - 1]))
            {
                --i;
            }
            // i will be the index of the first digit
            if (i == length)
            {
                // In this case, there are no digits to be had
                versionNumber = 0;
                return basename;
            }
            if (i == 0)
            {
                // In this case, the whole filename is digits
                versionNumber = int.Parse(basename);
                return string.Empty;
            }
            // We have some combination of letters with digits at the end.
            var newBasename = basename.Substring(0, i);
            versionNumber = int.Parse(basename.Substring(i, length - i));
            return newBasename;
        }

        private static void LogMemoryUsage()
        {
            using (var proc = Process.GetCurrentProcess())
            {
                const int bytesPerMegabyte = 1048576;
                Logger.WriteEvent(
                    "Paged Memory: " + proc.PagedMemorySize64 / bytesPerMegabyte + " MB"
                );
                Logger.WriteEvent(
                    "Peak Paged Memory: " + proc.PeakPagedMemorySize64 / bytesPerMegabyte + " MB"
                );
                Logger.WriteEvent(
                    "Peak Virtual Memory: "
                        + proc.PeakVirtualMemorySize64 / bytesPerMegabyte
                        + " MB"
                );
                Logger.WriteEvent(
                    "GC Total Memory: " + GC.GetTotalMemory(false) / bytesPerMegabyte + " MB"
                );
            }
        }

        /// <summary>
        /// Check whether any images are too big and need to be reduced in size.
        /// </summary>
        public static bool NeedToShrinkImages(string folderPath)
        {
            var startTime = DateTime.Now;
            try
            {
                var filePaths = Directory.GetFiles(folderPath, "*.*");
                var pngFiles = filePaths
                    .Where(path => path.ToLowerInvariant().EndsWith(".png"))
                    .ToArray();
                var jpgFiles = filePaths
                    .Where(
                        path =>
                            path.ToLowerInvariant().EndsWith(".jpg")
                            || path.ToLowerInvariant().EndsWith(".jpeg")
                    )
                    .ToArray();
                foreach (string path in pngFiles)
                {
                    if (Path.GetFileName(path)?.ToLowerInvariant() == "placeholder.png")
                        continue;
                    // Very large PNG files can cause "out of memory" errors here, while making thumbnails,
                    // and when creating ePUBs or BloomPub books.  So, we check for sizes bigger than our
                    // maximum and return true if any are found.
                    try
                    {
                        var tagFile = RobustFileIO.CreateTaglibFile(path);
                        if (
                            tagFile.Properties != null
                            && tagFile.Properties.Description.Contains("PNG")
                        )
                        {
                            if (
                                IsImageSizeTooBig(
                                    tagFile.Properties.PhotoWidth,
                                    tagFile.Properties.PhotoHeight
                                )
                            )
                                return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);
                        continue; // if something goes wrong, well, we'll just hope this image isn't too big.
                    }
                }

                foreach (string path in jpgFiles)
                {
                    // Very large JPG files can cause "out of memory" errors while making thumbnails and
                    // when creating ePUBs or BloomPub books.  So, we check for sizes bigger than our
                    // maximum and return true if any are found.
                    try
                    {
                        var tagFile = RobustFileIO.CreateTaglibFile(path);
                        if (
                            tagFile.Properties != null
                            && tagFile.Properties.Description.Contains("JFIF")
                        )
                        {
                            if (
                                IsImageSizeTooBig(
                                    tagFile.Properties.PhotoWidth,
                                    tagFile.Properties.PhotoHeight
                                )
                            )
                                return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);
                        continue; // if something goes wrong, well, we'll just hope this image isn't too big.
                    }
                }

                return false;
            }
            finally
            {
                var endTime = DateTime.Now;
                Debug.WriteLine(
                    $"DEBUG: ImageUtils.NeedToShrinkImages({folderPath}) took {endTime - startTime}"
                );
            }
        }

        private static bool IsImageSizeTooBig(int width, int height)
        {
            if (height > width) // portrait orientation
            {
                return (height > MaxLength || width > MaxBreadth);
            }
            else if (width > height) // landscape orientation
            {
                return (height > MaxBreadth || width > MaxLength);
            }
            else // square picture
            {
                return (width > MaxBreadth);
            }
        }

        /// <summary>
        /// Shrink any images in the folder that are bigger than our maximum reasonable size (3840x2800)
        /// to fit within that size.
        /// Also remove transparency on desired images (which there usually won't be any).
        /// </summary>
        /// <remarks>
        /// Up through Bloom 3.0, we would make white areas transparent when importing images, in order to make them
        /// look good against the colored background of a book cover.  This caused problems with some PDF viewers, so
        /// in Bloom 3.1, we switched to only making them transparent at runtime.  This method allows us to undo that
        /// transparency-making.
        /// This method also allows to shrink enormous PNG and JPEG files to our desired maximum size (but not
        /// converting PNG files to JPEG as can sometimes happen when initially setting the image files from the
        /// Image Chooser dialog).  Some books have acquired large images that cause frequent "out of memory"
        /// errors, some of which are hidden from the user.
        /// Transparency is removed only from images the caller wants to be fixed. (BL-8819)
        /// None of the current callers will want transparency to be removed. (BL-8846)
        /// </remarks>
        public static void FixSizeAndTransparencyOfImagesInFolder(
            string folderPath,
            IEnumerable<string> namesOfFilesToFixTransparency,
            IProgress progress
        )
        {
            // On Windows, "*.png" and "*.jp?g" would work to collect the desired image files using the
            // Directory.GetFiles method.  These fail on Linux for two reasons: case sensitivity and the ?
            // wildcard character represents a single character on Linux instead of an optional character.
            // So we collect a larger set of file paths and extract the ones we want to the separate lists
            // in a system independent way.
            var filePaths = Directory.GetFiles(folderPath, "*.*");
            var pngFiles = filePaths
                .Where(path => path.ToLowerInvariant().EndsWith(".png"))
                .ToArray();
            var jpgFiles = filePaths
                .Where(
                    path =>
                        path.ToLowerInvariant().EndsWith(".jpg")
                        || path.ToLowerInvariant().EndsWith(".jpeg")
                )
                .ToArray();
            int completed = 0;
            int totalFileCount = pngFiles.Length + jpgFiles.Length;
            foreach (string path in pngFiles)
            {
                progress.ProgressIndicator.PercentCompleted = (int)(
                    100.0 * (float)completed / (float)totalFileCount
                );
                if (Path.GetFileName(path)?.ToLowerInvariant() == "placeholder.png")
                {
                    ++completed;
                    continue;
                }
                // Very large PNG files can cause "out of memory" errors here, while making thumbnails,
                // and when creating ePUBs or BloomPub books.  So, we check for sizes bigger than our
                // maximum and reduce the image here if needed.
                var tagFile = RobustFileIO.CreateTaglibFile(path);
                if (tagFile.Properties != null && tagFile.Properties.Description.Contains("PNG"))
                {
                    var size = GetDesiredImageSize(
                        tagFile.Properties.PhotoWidth,
                        tagFile.Properties.PhotoHeight
                    );
                    if (
                        size.Width != tagFile.Properties.PhotoWidth
                        || size.Height != tagFile.Properties.PhotoHeight
                    )
                    {
                        var makeOpaque = namesOfFilesToFixTransparency.Contains(
                            Path.GetFileName(path)
                        );
                        if (
                            ResizeImageFileWithOptionalTransparency(
                                path,
                                size,
                                makeOpaque,
                                false,
                                tagFile,
                                progress
                            )
                        )
                        {
                            ++completed;
                            continue;
                        }
                    }
                }
                // Remove transparency only from images the caller wants to be fixed.
                // (This is largely for the benefit of not changing branding images.  See BL-8819.)
                if (!namesOfFilesToFixTransparency.Contains(Path.GetFileName(path)))
                {
                    ++completed;
                    continue;
                }
                using (var pi = PalasoImage.FromFileRobustly(path))
                {
                    // If the image isn't jpeg (which it shouldn't be), and we can't be sure it's already
                    // opaque, change the image to be opaque.  As explained above, some PDF viewers don't
                    // handle transparent images very well.
                    // But the PDF should not be displaying the thumbnail or placeholder images, so leave
                    // them alone.  See https://issues.bloomlibrary.org/youtrack/issue/BL-8700.
                    if (
                        !AppearsToBeJpeg(pi)
                        && !IsIndexedAndOpaque(pi.Image)
                        && Path.GetFileName(path).ToLowerInvariant() != "thumbnail.png"
                        && Path.GetFileName(path).ToLowerInvariant() != "placeholder.png"
                    )
                    {
                        RemoveTransparency(pi, path, progress);
                    }
                }
                completed++;
            }
            foreach (string path in jpgFiles)
            {
                progress.ProgressIndicator.PercentCompleted = (int)(
                    100.0 * (float)completed / (float)totalFileCount
                );
                // Very large JPG files can cause "out of memory" errors while making thumbnails and
                // when creating ePUBs or BloomPub books.  So, we check for sizes bigger than our
                // maximum and reduce the image here if needed.
                var tagFile = RobustFileIO.CreateTaglibFile(path);
                if (tagFile.Properties != null && tagFile.Properties.Description.Contains("JFIF"))
                {
                    var size = GetDesiredImageSize(
                        tagFile.Properties.PhotoWidth,
                        tagFile.Properties.PhotoHeight
                    );
                    if (
                        size.Width != tagFile.Properties.PhotoWidth
                        || size.Height != tagFile.Properties.PhotoHeight
                    )
                    {
                        ResizeImageFileWithOptionalTransparency(
                            path,
                            size,
                            false,
                            false,
                            tagFile,
                            progress
                        );
                    }
                }
                ++completed;
            }
        }

        /// <summary>
        /// Use GraphicsMagick to replace a PNG (or JPEG) file with one of the given size optionally
        /// having an opaque or transparent background.
        /// </summary>
        /// <returns>true if successful, false if GraphicsMagick doesn't exist or didn't work</returns>
        internal static bool ResizeImageFileWithOptionalTransparency(
            string path,
            Size size,
            bool makeOpaque,
            bool forUseOnColoredBackground,
            TagLib.File oldMetaData,
            IProgress progress = null
        )
        {
            if (progress != null)
            {
                var msgFmt = L10NSharp.LocalizationManager.GetString(
                    "ImageUtils.PreparingImage",
                    "Preparing image: {0}",
                    "{0} is a placeholder for the image file name"
                );
                var msg = string.Format(msgFmt, Path.GetFileName(path));
                progress.WriteStatus(msg);
            }
            var exeGraphicsMagick = GetGraphicsMagickPath();
            if (!RobustFile.Exists(exeGraphicsMagick))
                return false;
            var tempCopy = TempFileUtils.GetTempFilepathWithExtension(Path.GetExtension(path));

            bool makeTransparent = false;
            if (forUseOnColoredBackground)
            {
                try
                {
                    using (var originalImage = PalasoImage.FromFileRobustly(path))
                    {
                        makeTransparent = ShouldMakeBackgroundTransparent(originalImage);
                    }
                }
                catch (Exception e)
                {
                    string messageString =
                        $"Error loading image from {path} for ShouldMakeBackgroundTransparent check. Will not make transparent. "
                        + e.Message;
                    Logger.WriteEvent(messageString);
                    Console.WriteLine(messageString);
                }
            }
            try
            {
                var options = new GraphicsMagickOptions
                {
                    Size = size,
                    MakeOpaque = makeOpaque,
                    MakeTransparent = makeTransparent,
                    JpegQuality = 0,
                    ProfilesToStrip = null
                };
                var result = RunGraphicsMagick(path, tempCopy, options);
                if (result.ExitCode == 0)
                {
                    RobustFile.Copy(tempCopy, path, true);
                    // Copy metadata from older file to the new one.  GraphicsMagick does a poor job on metadata.
                    var newMeta = RobustFileIO.CreateTaglibFile(path);
                    CopyTags(oldMetaData, newMeta);
                    newMeta.Save();
                    if (progress != null)
                        Application.DoEvents(); // allow progress report to work
                    return true;
                }
                else
                {
                    LogGraphicsMagickFailure(result);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                // Ignore any errors deleting temp files.  If we leak, we leak...
                try
                {
                    RobustFile.Delete(tempCopy); // don't need this any longer
                }
                catch (Exception e)
                {
                    // log and ignore
                    Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
                }
            }
            return false;
        }

        /// <summary>
        /// Copy the metadata from one image file to another using TagLib.File objects to represent the two files.
        /// Note that PNG files uses both PNG tags and XMP tags and JPEG files use XMP tags.  (JPEG files may also
        /// use other types of tags, but in practice the XMP tags carry all the intellectual property information
        /// we care about.)
        /// </summary>
        private static void CopyTags(TagLib.File originalTags, TagLib.File newTags)
        {
            if ((originalTags.TagTypes & TagTypes.Png) == TagTypes.Png)
            {
                if (
                    originalTags.GetTag(TagTypes.Png) is PngTag tag
                    && newTags.GetTag(TagTypes.Png, true) is PngTag newTag
                )
                {
                    foreach (KeyValuePair<string, string> kvp in tag)
                        newTag.SetKeyword(kvp.Key, kvp.Value);
                }
            }
            if ((originalTags.TagTypes & TagTypes.XMP) == TagTypes.XMP)
            {
                if (
                    originalTags.GetTag(TagTypes.XMP) is XmpTag tag
                    && newTags.GetTag(TagTypes.XMP, true) is XmpTag newTag
                )
                {
                    // Don't bother copying camera/scanner related information.
                    // We just want the creator/copyright/description type information.
                    foreach (var node in tag.NodeTree.Children)
                    {
                        if (
                            node.Namespace == "http://purl.org/dc/elements/1.1/"
                            || node.Namespace == "http://creativecommons.org/ns#"
                            || node.Namespace
                                == "http://www.metadataworkinggroup.com/schemas/collections/"
                            || (
                                node.Namespace == "http://ns.adobe.com/exif/1.0/"
                                && node.Name == "UserComment"
                            )
                        )
                        {
                            newTag.NodeTree.AddChild(node);
                        }
                    }
                }
            }
        }

        private static void RemoveTransparency(
            PalasoImage original,
            string path,
            IProgress progress
        )
        {
            progress.WriteStatus("RemovingTransparency from image: " + Path.GetFileName(path));
            using (var b = new Bitmap(original.Image.Width, original.Image.Height))
            {
                DrawImageWithWhiteBackground(original.Image, b);
                original.Image = b;
                PalasoImage.SaveImageRobustly(original, path); // BL-4148: this method preserves existing metadata
            }
        }

        /// <summary>
        /// Check whether this image has an indexed format, and if so, whether all of its colors are totally opaque.
        /// (If so, we won't need to create a copy of the image without any transparency.
        /// </summary>
        /// <remarks>
        /// It's too hard/expensive to check all the pixels in a non-indexed image to see if they're all opaque.
        /// </remarks>
        public static bool IsIndexedAndOpaque(Image image)
        {
            if ((image.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
            {
                foreach (var color in image.Palette.Entries)
                {
                    if (color.A != 255)
                        return false; // 255 == opaque, other values varying amount of transparency
                }
                return true;
            }
            // Too hard / expensive to determine transparency/opacity
            return false;
        }

        /// <summary>
        /// Check whether the image has any transparency.
        /// If it becomes too difficult or expensive to determine, we punt and return false.
        /// </summary>
        public static bool HasTransparency(Image image)
        {
            if ((image.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
            {
                foreach (var color in image.Palette.Entries)
                {
                    if (color.A != 255)
                        return true; // 255 == opaque, other values varying amount of transparency
                }
                return false;
            }

            if (image is Bitmap bitmapImage)
            {
                // Yes, this is as expensive as it looks. But we take advantage of the fact that almost all
                // transparent images which someone would use in Bloom would be transparent in the corner.
                // Leave a little fudge for a non-transparent border.
                int maxPixelsFromCorner = 15;
                for (int y = 0; y < bitmapImage.Height && y < maxPixelsFromCorner; ++y)
                    for (int x = 0; x < bitmapImage.Width && x < maxPixelsFromCorner; ++x)
                        if (bitmapImage.GetPixel(x, y).A != 255)
                            return true;

                return false;
            }

            // Too hard / expensive to determine transparency/opacity
            return false;
        }

        /// <summary>
        /// If GraphicsMagick exists, use it to resize the image, optionally making it opaque in the process.
        /// If GraphicsMagick cannot be found, use the C# .Net code for the desired operation.
        /// </summary>
        /// <remarks>
        /// The reason for using GraphicsMagick is that some images are just too big to handle without getting
        /// an "out of memory" error.
        /// </remarks>
        /// <returns>Returns null if the resize wouldn't make the file smaller,
        /// else the new image.</returns>
        private static Image TryResizeImageWithGraphicsMagick(
            PalasoImage imageInfo,
            Size size,
            bool makeOpaque = false
        )
        {
            var graphicsMagickPath = GetGraphicsMagickPath();
            if (RobustFile.Exists(graphicsMagickPath))
            {
                var sourcePath = imageInfo.GetCurrentFilePath();
                var isJpegImage = AppearsToBeJpeg(imageInfo);
                if (String.IsNullOrEmpty(sourcePath) || !RobustFile.Exists(sourcePath))
                {
                    sourcePath = CreateSourceFileForImage(imageInfo, isJpegImage);
                }
                var destPath = TempFileUtils.GetTempFilepathWithExtension(
                    isJpegImage ? ".jpg" : ".png"
                );
                try
                {
                    var options = new GraphicsMagickOptions
                    {
                        Size = size,
                        MakeOpaque = makeOpaque,
                        MakeTransparent = false,
                        JpegQuality = 0,
                        ProfilesToStrip = null
                    };
                    var result = RunGraphicsMagick(sourcePath, destPath, options);
                    if (result.ExitCode == 0)
                    {
                        if (new FileInfo(destPath).Length > new FileInfo(sourcePath).Length)
                        {
                            // The new file is actually larger (BL-11441) so bail out
                            Debug.WriteLine(
                                $"New file for {Path.GetFileName(sourcePath)} at {size} is larger than original at {imageInfo.Image.Size}."
                            );
                            return null;
                        }
                        imageInfo.SetCurrentFilePath(destPath);
                        return RobustImageIO.GetImageFromFile(destPath);
                    }
                    else
                    {
                        LogGraphicsMagickFailure(result);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                finally
                {
                    // Ignore any errors deleting temp files.  If we leak, we leak...
                    try
                    {
                        // don't need this any longer if it's a temp file and not used for the current image
                        if (
                            sourcePath.StartsWith(Path.GetTempPath())
                            && sourcePath != imageInfo.GetCurrentFilePath()
                        )
                            RobustFile.Delete(sourcePath);
                    }
                    catch (Exception e)
                    {
                        // log and ignore
                        Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
                    }
                }
            }
            // GraphicsMagick is not working (or doesn't exist).  Try the old way with System.Drawing operations.
            var bm = new Bitmap(size.Width, size.Height);
            var rect = new Rectangle(Point.Empty, size);
            using (var g = Graphics.FromImage(bm))
            {
                if (makeOpaque)
                    g.Clear(Color.White);
                g.DrawImage(imageInfo.Image, rect);
            }
            return bm;
        }

        static string _createdTempImageFile;

        private static string CreateSourceFileForImage(PalasoImage imageInfo, bool isJpegImage)
        {
            // This must be from a paste instead of the ImageChooser dialog.
            var sourcePath = Path.GetTempFileName();
            RobustFile.Delete(sourcePath);
            if (isJpegImage)
            {
                sourcePath += ".jpg";
                imageInfo.Image.Save(sourcePath, ImageFormat.Jpeg);
            }
            else
            {
                sourcePath += ".png";
                imageInfo.Image.Save(sourcePath, ImageFormat.Png);
            }
            imageInfo.SetCurrentFilePath(sourcePath);
            if (_createdTempImageFile != null)
            {
                if (RobustFile.Exists(_createdTempImageFile))
                    RobustFile.Delete(_createdTempImageFile);
            }
            _createdTempImageFile = sourcePath;
            return sourcePath;
        }

        private static void LogGraphicsMagickFailure(ExecutionResult result)
        {
            var standardOutput = result.StandardOutput;
            var standardError = result.StandardError;
            var msgBldr = new StringBuilder();
            msgBldr.AppendLine("GraphicsMagick failed to convert an image file.");
            msgBldr.AppendFormat("{0} {1}", result.ExePath, result.Arguments);
            msgBldr.AppendLine();
            msgBldr.AppendFormat("GraphicsMagick exit code = {0}", result.ExitCode);
            msgBldr.AppendLine();
            msgBldr.AppendLine("stderr =");
            msgBldr.AppendLine(standardError);
            msgBldr.AppendLine("stdout =");
            msgBldr.AppendLine(standardOutput);
            Logger.WriteEvent(msgBldr.ToString());
            Console.Write(msgBldr.ToString());
        }

        private struct GraphicsMagickOptions
        {
            internal Size Size; // if (0,0), don't resize
            internal bool MakeOpaque;
            internal bool MakeTransparent;
            internal int JpegQuality; // 0 means use input jpeg's quality
            internal string ProfilesToStrip; // null means don't strip any profiles
        }

        private static ExecutionResult RunGraphicsMagick(
            string sourcePath,
            string destPath,
            GraphicsMagickOptions options
        )
        {
            Debug.Assert(
                !(options.MakeOpaque && options.MakeTransparent),
                "makeOpaque and makeTransparent cannot both be true."
            );
            // We have no idea what the input (and output) file names are, and whether they can be safely represented
            // in the user's codepage.  We also have no idea what the user's codepage is.  This has a major impact
            // on whether the spawned GraphicsMagick process will succeed in reading/writing the files.  We can
            // assume that ASCII file paths are safe regardless of the user's default codepage.
            var safeSourcePath = sourcePath;
            if (!IsAsciiFilepath(sourcePath))
            {
                safeSourcePath = TempFileUtils.GetTempFilepathWithExtension(
                    Path.GetExtension(sourcePath)
                );
                RobustFile.Copy(sourcePath, safeSourcePath);
            }
            var safeDestPath = destPath;
            if (!IsAsciiFilepath(destPath))
                safeDestPath = TempFileUtils.GetTempFilepathWithExtension(
                    Path.GetExtension(destPath)
                );
            try
            {
                var argsBldr = new StringBuilder();
                argsBldr.AppendFormat("convert \"{0}\"", sourcePath);
                if (options.MakeOpaque)
                    argsBldr.Append(" -background white -extent 0x0 +matte");
                else if (options.MakeTransparent)
                    argsBldr.Append(
                        " -transparent \"#ffffff\" -transparent \"#fefefe\" -transparent \"#fdfdfd\""
                    );

                // http://www.graphicsmagick.org/GraphicsMagick.html#details-profile states:
                // Use +profile profile_name to remove the respective profile.
                // For example, +profile '!icm,*' strips all profiles except for the ICM profile.
                if (!String.IsNullOrEmpty(options.ProfilesToStrip))
                    argsBldr.AppendFormat(" +profile \"{0}\"", options.ProfilesToStrip);

                // GraphicsMagick quality numbers: http://www.graphicsmagick.org/GraphicsMagick.html#details-quality
                // For PNG files, "quality" really means "compression".  75 is the default value used in GraphicsMagick
                // for .png files, and tests out as having a good balance between speed and resulting file size.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-11441 for an extensive discussion of this.
                if (destPath.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase))
                    argsBldr.Append(" -quality 75"); // no lossage in output, use adaptive filter in changing image dimensions
                else if (destPath.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (options.JpegQuality == 0 && sourcePath.EndsWith(".jpg"))
                        argsBldr.Append(" -define jpeg:preserve-settings"); // preserve input quality and sampling factor
                    else if (options.JpegQuality > 0)
                        argsBldr.AppendFormat(" -quality {0}", options.JpegQuality);
                    else
                        argsBldr.Append(" -quality 90"); // high quality (but not extreme) if we don't know any better
                }
                argsBldr.Append(" -density 96"); // GraphicsMagick defaults to 72 dpi, which is rather low
                if (options.Size.Height > 0 && options.Size.Width > 0)
                    // -resize would do a better job than -scale, but it can be much (~10x) slower on large images.
                    argsBldr.AppendFormat(
                        " -scale {0}x{1}",
                        options.Size.Width,
                        options.Size.Height
                    );
                argsBldr.AppendFormat(" \"{0}\"", safeDestPath);
                var arguments = argsBldr.ToString();

                var result = CommandLineRunnerExtra.RunWithInvariantCulture(
                    GetGraphicsMagickPath(),
                    arguments,
                    "",
                    600,
                    new NullProgress()
                );

                if (result.ExitCode == 0 && destPath != safeDestPath)
                    RobustFile.Copy(safeDestPath, destPath, true);
                return result;
            }
            finally
            {
                // remove unneeded copies
                if (sourcePath != safeSourcePath)
                    RobustFile.Delete(safeSourcePath);
                if (destPath != safeDestPath)
                    RobustFile.Delete(safeDestPath);
            }
        }

        /// <summary>
        /// Check whether the give file path contains any non-ASCII characters.  We don't know what
        /// the user's default codepage is, and when we spawn a process to run an external program,
        /// the filename characters may not be representable in that codepage.  We have to assume
        /// that ASCII is a subset of every codepage.  (Too much would break if that weren't true!)
        /// </summary>
        private static bool IsAsciiFilepath(string path)
        {
            for (int i = 0; i < path.Length; ++i)
            {
                if (path[i] < 0x20 || path[i] > 0x7E)
                    return false;
            }
            return true;
        }

        private static string GetGraphicsMagickPath()
        {
            if (Platform.IsLinux)
            {
                return "/usr/bin/gm";
            }
            else
            {
                var codeBaseDir = BloomFileLocator.GetCodeBaseFolder();
                return Path.Combine(codeBaseDir, "gm", "gm.exe");
            }
        }

        private static void DrawImageWithWhiteBackground(Image source, Bitmap target)
        {
            // Color.White is not a constant value, so it can't be used as a default method parameter value.
            DrawImageWithOpaqueBackground(source, target, Color.White);
        }

        public static void DrawImageWithOpaqueBackground(Image source, Bitmap target, Color color)
        {
            Rectangle rect = new Rectangle(Point.Empty, source.Size);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.Clear(color);
                g.DrawImageUnscaledAndClipped(source, rect);
            }
        }

        /// <summary>
        /// When images are copied from LibreOffice, images that were jpegs there are converted to bitmaps for the clipboard.
        /// So when we just saved them as bitmaps (pngs), we dramatically inflated the size of user's image files (and
        /// this then led to memory problems).
        /// So the idea here is to detect whether we would be better off saving the image as a jpeg, and to save the
        /// jpeg file at the indicated path if so.
        /// Note that even at 100%, we're still going to lose some quality. So this method is only going to return true
        /// with the file existing at the given path if the file size would be at least 50% smaller as a jpeg.
        /// WARNING: a previously existing file at the given path may cause this method to fail with a GraphicsMagick
        /// failure.  If the process works, but the decision is to not use jpeg, any file at the given path will be
        /// deleted.
        /// </summary>
        public static bool TryChangeFormatToJpegIfHelpful(PalasoImage image, string jpegFilePath)
        {
            try
            {
                var graphicsMagickPath = GetGraphicsMagickPath();
                if (RobustFile.Exists(graphicsMagickPath))
                {
                    var path = image.GetCurrentFilePath();
                    if (path == null || !RobustFile.Exists(path))
                        path = CreateSourceFileForImage(image, false); // already know it's not jpeg
                    var options = new GraphicsMagickOptions
                    {
                        Size = new Size(0, 0), // preserve current size (no scaling)
                        MakeOpaque = false,
                        MakeTransparent = false,
                        JpegQuality = 92, // High quality (but not extreme)
                        ProfilesToStrip = null
                    };

                    var result = RunGraphicsMagick(path, jpegFilePath, options);
                    if (result.ExitCode == 0)
                    {
                        var pngInfo = new FileInfo(path);
                        var jpegInfo = new FileInfo(jpegFilePath);
                        // this is just our heuristic.
                        const double fractionOfTheOriginalThatWouldWarrantChangingToJpeg = .5;
                        if (
                            jpegInfo.Length
                            < (
                                pngInfo.Length
                                * (1.0 - fractionOfTheOriginalThatWouldWarrantChangingToJpeg)
                            )
                        )
                        {
                            return true;
                        }
                        RobustFile.Delete(jpegFilePath); // don't need it after all.
                    }
                    else
                    {
                        LogGraphicsMagickFailure(result);
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                // Don't bother reporting this to the user, but log it in case it might be relevant for a real bug.
                Logger.WriteError(
                    $"ImageUtils.TryChangeFormatToJpegIfHelpful({image.GetCurrentFilePath() ?? _createdTempImageFile}) caught an exception.",
                    e
                );
                return false;
            }
        }

        /// <summary>
        /// Save the image (of any format) to a jpeg file with 100 quality
        /// Note that this is still going to introduce some errors if the input is a bitmap.
        /// </summary>
        /// <remarks>Will throw if the destination is locked and the user tells us to give up. </remarks>
        public static void SaveAsTopQualityJpeg(Image image, string destinationPath)
        {
            var jpgEncoder = ImageCodecInfo
                .GetImageDecoders()
                .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            var encoder = Encoder.Quality;

            //nb: there are cases (notably http://jira.palaso.org/issues/browse/WS-34711, after cropping a jpeg) where we get out of memory if we are not operating on a copy

            if (!Directory.Exists(Path.GetDirectoryName(destinationPath)))
            {
                // Most likely the book is newly created and being renamed. We'll try again later in the right folder.
                return;
            }
            // Use a temporary file pathname in the destination folder.  This is needed to ensure proper permissions are granted
            // to the resulting file later after FileUtils.ReplaceFileWithUserInteractionIfNeeded is called.  That method may call
            // RobustFile.Replace which replaces both the file content and the file metadata (permissions).  The result of that if we use
            // the user's temp directory is described in http://issues.bloomlibrary.org/youtrack/issue/BL-3954.
            using (var temp = TempFile.InFolderOf(destinationPath))
            using (var safetyImage = new Bitmap(image))
            {
                using (var parameters = new EncoderParameters(1))
                {
                    //0 = max compression, 100 = least
                    parameters.Param[0] = new EncoderParameter(encoder, 100L);
                    SIL.IO.RobustImageIO.SaveImage(safetyImage, temp.Path, jpgEncoder, parameters);
                }
                FileUtils.ReplaceFileWithUserInteractionIfNeeded(temp.Path, destinationPath, null);
            }
        }

        /// <summary>
        /// 'input' is usually '#' + 6 hex digits, but could also rarely be a color word, like 'black'.
        /// </summary>
        public static bool TryCssColorFromString(string input, out Color result)
        {
            result = Color.White; // some default in case of error.
            if (input.Length < 3) // I don't think there are any 2-letter color words.
                return false; // arbitrary failure
            try
            {
                result = ColorTranslator.FromHtml(input);
            }
            catch (Exception e)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
                return false;
            }
            return true;
        }

        public static Image ResizeImageIfNecessary(
            Size maxSize,
            Image image,
            bool shouldAddDashedBorder
        )
        {
            return DrawResizedImage(maxSize, image, false, shouldAddDashedBorder);
        }

        public static Image CenterImageIfNecessary(
            Size size,
            Image image,
            bool shouldAddDashedBorder
        )
        {
            return DrawResizedImage(size, image, true, shouldAddDashedBorder);
        }

        /// <summary>
        /// Return a possibly resized and possibly centered image.  If no change is needed,
        /// a new copy of the image is returned. Also possibly add a dashed border (for templates).
        /// </summary>
        /// <remarks>
        /// Always returning a new image simplifies keeping track of when to dispose the original
        /// image.
        /// Note that this method never returns a larger image than the original: only one the
        /// same size or smaller.
        /// </remarks>
        private static Image DrawResizedImage(
            Size maxSize,
            Image image,
            bool centerImage,
            bool shouldAddDashedBorder
        )
        {
            // adapted from https://www.c-sharpcorner.com/article/resize-image-in-c-sharp/
            var desiredHeight = maxSize.Height;
            var desiredWidth = maxSize.Width;
            if (image.Width == desiredWidth && image.Height == desiredHeight)
            {
                if (shouldAddDashedBorder)
                    return AddDashedBorderToOtherwiseUnchangedImage(image);
                return new Bitmap(image); // exact match already
            }
            int newHeight;
            int newWidth;
            if (image.Height <= desiredHeight && image.Width <= desiredWidth)
            {
                if (!centerImage)
                {
                    if (shouldAddDashedBorder)
                        return AddDashedBorderToOtherwiseUnchangedImage(image);
                    return new Bitmap(image);
                }
                newHeight = image.Height; // not really new...
                newWidth = image.Width;
            }
            else
            {
                // Try resizing to desired width first
                newHeight = image.Height * desiredWidth / image.Width;
                newWidth = desiredWidth;
                if (newHeight > desiredHeight)
                {
                    // Resize to desired height instead
                    newWidth = image.Width * desiredHeight / image.Height;
                    newHeight = desiredHeight;
                }
            }
            Image newImage = centerImage
                ? new Bitmap(desiredWidth, desiredHeight)
                : new Bitmap(newWidth, newHeight);
            using (var graphic = Graphics.FromImage(newImage))
            {
                // I tried using HighSpeed settings in here with no appreciable difference in loading speed.
                // However, the "High Quality" settings can greatly increase memory use, possibly causing "Out of Memory"
                // errors when creating thumbnail images.  So we use the default settings for drawing the image here.
                // Some thumbnails may be a bit uglier, but they're supposed to just give an idea of what the front cover
                // looks like: they're not works of art themselves.
                // See https://stackoverflow.com/questions/15438509/graphics-drawimage-throws-out-of-memory-exception?lq=1
                // (the second answer).
                // The following x and y are only non-zero when centering an image and then one or the other will be (non-zero).
                var x = (newImage.Width - newWidth) / 2;
                var y = (newImage.Height - newHeight) / 2;
                graphic.DrawImage(image, x, y, newWidth, newHeight);
                if (shouldAddDashedBorder)
                {
                    var pen = GetDashedBlackPen();
                    graphic.DrawRectangle(pen, new Rectangle(x, y, newWidth, newHeight));
                }
            }
            return newImage;
        }

        private static Pen GetDashedBlackPen()
        {
            return new Pen(Brushes.Black, 2) { DashStyle = DashStyle.Dash };
        }

        private static Image AddDashedBorderToOtherwiseUnchangedImage(Image source)
        {
            using (var graphic = Graphics.FromImage(source))
            {
                var pen = GetDashedBlackPen();
                graphic.DrawRectangle(pen, new Rectangle(0, 0, source.Width, source.Height));
            }

            return source;
        }

        /// <summary>
        /// Store the current file paths for PalasoImage objects.
        /// </summary>
        static Dictionary<string, string> _currentFilePaths = new Dictionary<string, string>();

        /// <summary>
        /// Extend PalasoImage to store a "current file path" for the image.
        /// </summary>
        public static void SetCurrentFilePath(this PalasoImage image, string filePath)
        {
            var key = image.GetFileKey();
            if (filePath == null)
                _currentFilePaths.Remove(key);
            else
                _currentFilePaths[key] = filePath;
        }

        /// <summary>
        /// Extend PalasoImage to retrieve a "current file path" for the image.
        /// </summary>
        public static string GetCurrentFilePath(this PalasoImage image)
        {
            var key = image.GetFileKey();
            if (_currentFilePaths.TryGetValue(key, out string filePath))
                return filePath;
            return image.OriginalFilePath;
        }

        /// <summary>
        /// Get the key for storing the current file path for a PalasoImage object.
        /// </summary>
        private static string GetFileKey(this PalasoImage image)
        {
            return image.OriginalFilePath == null
                ? image.GetHashCode().ToString()
                : image.OriginalFilePath;
        }

        /// <summary>
        /// Save the Image data to path as a PNG image, or delete the file if image is null.
        /// </summary>
        public static void SaveOrDeletePngImageToPath(System.Drawing.Image image, string imagePath)
        {
            var originalReadOnly = FileAttributes.Normal;
            try
            {
                // Almost all of the reports for BL-3227 that have been generated are for an UnauthorizedAccessException
                // in the FileStream constructor (which is different than the original error reported in BL-3227).  This
                // can happen if the file has become read-only for some reason.  Changing the FileAttribute is easy.  The
                // more complicated permission settings are probably too difficult to fix, and fixing them even likelier
                // to not be allowed.
                if (RobustFile.Exists(imagePath))
                {
                    var originalFileAttributes = RobustFile.GetAttributes(imagePath);
                    originalReadOnly = originalFileAttributes & FileAttributes.ReadOnly;
                    if (originalReadOnly == FileAttributes.ReadOnly)
                        RobustFile.SetAttributes(imagePath, FileAttributes.Normal);
                }
                if (image != null)
                {
                    using (Stream fs = RobustIO.GetFileStream(imagePath, FileMode.Create))
                    {
                        SIL.IO.RobustImageIO.SaveImage(image, fs, ImageFormat.Png);
                    }
                    if (originalReadOnly == FileAttributes.ReadOnly)
                    {
                        // This may be useful to know even if only reported with other issues happening elsewhere.
                        Logger.WriteEvent(
                            $"Updating {imagePath} required turning off the ReadOnly attribute (BL-3227)."
                        );
                    }
                }
                else
                {
                    if (RobustFile.Exists(imagePath))
                        RobustFile.Delete(imagePath);
                }
            }
            catch (Exception error)
            {
                // BL-3227 Occasionally get The process cannot access the file '...\license.png' because it is being used by another process.
                // That's worth a toast, since the user might like a hint why the license image isn't up to date.  Note that these reports
                // don't always involve license.png.  They may involve branding.png, placeHolder.png, or thumbnail.png (or possibly other PNG
                // files).
                // BL-9533: these errors keep happening, but we can't help users who respond to a toast and send in an error report.
                // Logging it will allow us to possibly correlate an error here with another problem that does get reported.
                var message = $"Could not update PNG image (BL-3227) at {imagePath}";
                string details;
                details = MiscUtils.GetExtendedFileCopyErrorInformation(imagePath);
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    message,
                    details,
                    exception: error,
                    showSendReport: false,
                    showRequestDetails: true
                );
            }
        }

        public static void SaveImageMetadataIfNeeded(
            Metadata metadata,
            string folderPath,
            string filename
        )
        {
            using (
                var imageBeingModified = ImageUpdater.GetImageInfoSafelyFromFilePath(
                    folderPath,
                    filename
                )
            )
            {
                imageBeingModified.Metadata = metadata;
                imageBeingModified.Metadata.StoreAsExemplar(Metadata.FileCategory.Image);
                try
                {
                    imageBeingModified.SaveUpdatedMetadataIfItMakesSense();
                }
                catch (Exception e)
                {
                    if (e.Source == "TagLibSharp")
                    {
                        // The metadata is corrupt in a way that prevents writing it back to the file.
                        // Try to remove the old, corrupt metadata from the file and write the new metadata.
                        ImageUtils.StripMetadataFromImageFile(imageBeingModified);
                        imageBeingModified.SaveUpdatedMetadataIfItMakesSense();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        public static void SaveImageMetadata(PalasoImage imageInfo, string imagePath)
        {
            try
            {
                // It would seem more natural to use a metadata-saving method on imageInfo,
                // but the imageInfo has the source file's path locked into it, and the API
                // gives us no way to change it, so such a save would go to the wrong file.
                imageInfo.Metadata.Write(imagePath);
            }
            catch (Exception ex)
            {
                // We must have bad metadata in the original file.
                if (ex.Source == "TagLibSharp")
                {
                    // The metadata is corrupt in a way that prevents writing it back to the file.
                    // Try to remove the old, corrupt metadata from the file and write the new metadata.
                    imageInfo.SetCurrentFilePath(imagePath);
                    ImageUtils.StripMetadataFromImageFile(imageInfo);
                    imageInfo.Metadata.Write(imagePath);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
