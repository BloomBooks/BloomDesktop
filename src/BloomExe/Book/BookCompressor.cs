using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using SIL.IO;
using System.Drawing;
using System;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Xml;
using Bloom.ImageProcessing;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Xml;
using System.Collections.Generic;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Utils;
using Bloom.SafeXml;

namespace Bloom.Book
{
    public class BookCompressor
    {
        // these image files may need to be reduced before being stored in the compressed output file
        public static readonly string[] CompressableImageFileExtensions =
        {
            ".tif",
            ".tiff",
            ".png",
            ".bmp",
            ".jpg",
            ".jpeg"
        };

        internal static void MakeSizedThumbnail(
            Book book,
            string destinationFolder,
            int heightAndWidth
        )
        {
            // If this fails to create a 'coverImage200.jpg', either the cover image is missing or it's only a placeholder.
            // If this is a new book, the file may exist already, but we want to make sure it's up-to-date.
            // If this is an older book, we need the .bloompub to have it so that Harvester will be able to access it.
            BookThumbNailer.GenerateImageForWeb(book);

            var coverImagePath = book.GetCoverImagePath();
            if (coverImagePath == null)
            {
                var blankImage = Path.Combine(
                    FileLocationUtilities.DirectoryOfApplicationOrSolution,
                    "DistFiles",
                    "Blank.png"
                );
                if (RobustFile.Exists(blankImage))
                    coverImagePath = blankImage;
            }
            if (coverImagePath != null)
            {
                var thumbPath = Path.Combine(destinationFolder, "thumbnail.png");

                RuntimeImageProcessor.GenerateEBookThumbnail(
                    coverImagePath,
                    thumbPath,
                    heightAndWidth,
                    heightAndWidth,
                    System.Drawing.ColorTranslator.FromHtml(book.GetCoverColor())
                );
            }
        }

        /// <summary>
        /// Zips a directory containing a Bloom collection, along with all files and subdirectories
        /// </summary>
        /// <param name="outputPath">The location to which to create the output zip file</param>
        /// <param name="directoryToCompress">The directory to add recursively</param>
        /// <param name="dirNamePrefix">string to prefix to the zip entry name</param>
        /// <param name="forReaderTools">If True, then some pre-processing will be done to the contents of decodable
        /// and leveled readers before they are added to the ZipStream</param>
        public static void CompressCollectionDirectory(
            string outputPath,
            string directoryToCompress,
            string dirNamePrefix,
            bool forReaderTools
        )
        {
            var overrides = new Dictionary<string, byte[]>();
            var filter = new CollectionFileFilter();
            if (forReaderTools)
            {
                filter = new ReaderToolsBloomPackCollectionFileFilter();

                foreach (var folderPath in Directory.GetDirectories(directoryToCompress))
                {
                    CollectReaderTemplateBookOverrides(folderPath, overrides);
                }

                foreach (
                    var collectionFile in Directory.GetFiles(
                        directoryToCompress,
                        "*.bloomcollection"
                    )
                )
                {
                    overrides[collectionFile] = Encoding.UTF8.GetBytes(
                        GetBloomCollectionModifiedForTemplate(collectionFile)
                    );
                }
            }

            foreach (var bookFolderPath in Directory.GetDirectories(directoryToCompress))
            {
                filter.AddBookFilter(CollectionModel.MakeBloomPackBookFileFilter(bookFolderPath));
            }

            CompressDirectory(outputPath, directoryToCompress, filter, dirNamePrefix, overrides);
        }

        /// <summary>
        /// Zips a directory containing a Bloom book, along with all files and subdirectories
        /// </summary>
        /// <param name="outputPath">The location to which to create the output zip file</param>
        /// <param name="directoryToCompress">The directory to add recursively</param>
        /// <param name="filter">A BookFileFilter configured to accept the desired files</param>
        /// <param name="dirNamePrefix">string to prefix to the zip entry name</param>
        /// <param name="forReaderTools">If True, then some pre-processing will be done to the contents of decodable
        /// and leveled readers before they are added to the ZipStream</param>
        public static void CompressBookDirectory(
            string outputPath,
            string directoryToCompress,
            IFilter filter,
            string dirNamePrefix,
            bool forReaderTools = false,
            bool wrapWithFolder = true
        )
        {
            var overrides = new Dictionary<string, byte[]>();
            if (forReaderTools)
            {
                CollectReaderTemplateBookOverrides(directoryToCompress, overrides);
            }

            CompressDirectory(
                outputPath,
                directoryToCompress,
                filter,
                dirNamePrefix,
                overrides,
                wrapWithFolder
            );
        }

        /// <summary>
        /// "Collect" (collector pattern) replacements for any files in the book folder that
        /// should be modified for a reader template.
        /// </summary>
        private static void CollectReaderTemplateBookOverrides(
            string bookFolderPath,
            Dictionary<string, byte[]> overrides
        )
        {
            var htmlPath = BookStorage.FindBookHtmlInFolder(bookFolderPath);
            if (string.IsNullOrEmpty(htmlPath))
                return;
            overrides[htmlPath] = Encoding.UTF8.GetBytes(GetBookReplacedWithTemplate(htmlPath));
            var metaPath = Path.Combine(bookFolderPath, "meta.json");
            overrides[metaPath] = Encoding.UTF8.GetBytes(GetMetaJsonModfiedForTemplate(metaPath));
        }

        private static void CompressDirectory(
            string outputPath,
            string directoryToCompress,
            IFilter filter,
            string dirNamePrefix,
            Dictionary<string, byte[]> overrides = null,
            bool wrapWithFolder = true
        )
        {
            using (var fsOut = RobustFile.Create(outputPath))
            {
                using (var zipStream = new ZipOutputStream(fsOut))
                {
                    zipStream.SetLevel(9);

                    int dirNameOffset;
                    if (wrapWithFolder)
                    {
                        // zip entry names will start with the compressed folder name (zip will contain the
                        // compressed folder as a folder...we do this in bloompacks, not sure why).
                        var rootName = Path.GetFileName(directoryToCompress);
                        dirNameOffset = directoryToCompress.Length - rootName.Length;
                    }
                    else
                    {
                        // zip entry names will start with the files or directories at the root of the book folder
                        // (zip root will contain the folder contents...suitable for compressing a single book into
                        // a zip, as with .bloompub files)
                        dirNameOffset = directoryToCompress.Length + 1;
                    }
                    CompressDirectory(
                        directoryToCompress,
                        zipStream,
                        filter,
                        dirNameOffset,
                        dirNamePrefix,
                        overrides
                    );

                    zipStream.IsStreamOwner = true; // makes the Close() also close the underlying stream
                    zipStream.Close();
                }
            }
        }

        /// <summary>
        /// Adds a directory, along with all files and subdirectories, to the ZipStream.
        /// </summary>
        /// <param name="directoryToCompress">The directory to add recursively</param>
        /// <param name="zipStream">The ZipStream to which the files and directories will be added</param>
        /// <param name="dirNameOffset">This number of characters will be removed from the full directory or file path
        /// before creating the zip entry name</param>
        /// <param name="dirNamePrefix">string to prefix to the zip entry name</param>
        /// <param name="overrides">Any file whose path is found in overrides will be entered in the zip with the corresponding
        /// value instead of its actual content</param>
        private static void CompressDirectory(
            string directoryToCompress,
            ZipOutputStream zipStream,
            IFilter filter,
            int dirNameOffset,
            string dirNamePrefix,
            Dictionary<string, byte[]> overrides = null
        )
        {
            var files = Directory.GetFiles(directoryToCompress);

            foreach (var filePath in files)
            {
                if (!filter.Filter(filePath))
                    continue;

                FileInfo fi = new FileInfo(filePath);

                var entryName = dirNamePrefix + filePath.Substring(dirNameOffset); // Makes the name in zip based on the folder
                entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                ZipEntry newEntry = new ZipEntry(entryName)
                {
                    // Don't try to further compress videos, audio, or certain image types, they are already compressed.
                    CompressionMethod = BloomZipFile.ShouldCompressByFiletype(filePath)
                        ? CompressionMethod.Deflated
                        : CompressionMethod.Stored,
                    DateTime = fi.LastWriteTime,
                    IsUnicodeText = true
                };

                byte[] modifiedContent = null;

                if (overrides != null && overrides.TryGetValue(filePath, out modifiedContent))
                    newEntry.Size = modifiedContent.Length;
                else
                    newEntry.Size = fi.Length;

                zipStream.PutNextEntry(newEntry);

                if (modifiedContent != null)
                {
                    using (var memStream = new MemoryStream(modifiedContent))
                    {
                        // There is some minimum buffer size (44 was too small); I don't know exactly what it is,
                        // but 1024 makes it happy.
                        StreamUtils.Copy(
                            memStream,
                            zipStream,
                            new byte[Math.Max(modifiedContent.Length, 1024)]
                        );
                    }
                }
                else
                {
                    // Zip the file in buffered chunks
                    byte[] buffer = new byte[4096];
                    using (var streamReader = RobustFile.OpenRead(filePath))
                    {
                        StreamUtils.Copy(streamReader, zipStream, buffer);
                    }
                }

                zipStream.CloseEntry();
            }

            var folders = Directory.GetDirectories(directoryToCompress);

            foreach (var folder in folders)
            {
                CompressDirectory(
                    folder,
                    zipStream,
                    filter,
                    dirNameOffset,
                    dirNamePrefix,
                    overrides
                );
            }
        }

        private static string GetMetaJsonModfiedForTemplate(string path)
        {
            var meta = BookMetaData.FromFile(path);
            meta.IsSuitableForMakingShells = true;
            return meta.Json;
        }

        /// <summary>
        /// Remove any SubscriptionCode element content and replace any BrandingProjectName element
        /// content with the text "Default".  We don't want to publish Bloom Enterprise subscription
        /// codes after all!
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-6938.
        /// </remarks>
        private static string GetBloomCollectionModifiedForTemplate(string filePath)
        {
            var dom = SafeXmlDocument.Create();
            dom.PreserveWhitespace = true;
            dom.Load(filePath);
            foreach (
                var node in dom.SafeSelectNodes("//SubscriptionCode").Cast<SafeXmlElement>().ToArray()
            )
            {
                node.RemoveAll(); // should happen at most once
            }
            foreach (
                var node in dom.SafeSelectNodes("//BrandingProjectName")
                    .Cast<SafeXmlElement>()
                    .ToArray()
            )
            {
                node.RemoveAll(); // should happen at most once
                node.AppendChild(dom.CreateTextNode("Default"));
            }
            return dom.OuterXml;
        }

        /// <summary>
        /// Does some pre-processing on reader files
        /// </summary>
        /// <param name="bookPath"></param>
        private static string GetBookReplacedWithTemplate(string bookPath)
        {
            //TODO: the following, which is the original code before late in 3.6, just modified the tags in the HTML
            //Whereas currently, we use the meta.json as the authoritative source.
            //TODO Should we just get rid of these tags in the HTML? Can they be accessed from javascript? If so,
            //then they will be needed eventually (as we involve c# less in the UI)
            var text = RobustFile.ReadAllText(bookPath, Encoding.UTF8);
            // Note that we're getting rid of preceding newline but not following one. Hopefully we cleanly remove a whole line.
            // I'm not sure the </meta> ever occurs in html files, but just in case we'll match if present.
            // Remove the lockedDownAsShell HTML metadata setting if present.
            var regex = new Regex(
                "\\s*<meta\\s+name=(['\\\"])lockedDownAsShell\\1 content=(['\\\"])true\\2>(</meta>)? *"
            );
            var match = regex.Match(text);
            if (match.Success)
                text = text.Substring(0, match.Index) + text.Substring(match.Index + match.Length);

            // BL-2476: Readers made from BloomPacks should have the formatting dialog disabled
            regex = new Regex(
                "\\s*<meta\\s+name=(['\\\"])pageTemplateSource\\1 content=(['\\\"])(Leveled|Decodable) Reader\\2>(</meta>)? *"
            );
            match = regex.Match(text);
            if (match.Success)
            {
                // has the lockFormatting meta tag been added already?
                var regexSuppress = new Regex(
                    "\\s*<meta\\s+name=(['\\\"])lockFormatting\\1 content=(['\\\"])(.*)\\2>(</meta>)? *"
                );
                var matchSuppress = regexSuppress.Match(text);
                if (matchSuppress.Success)
                {
                    // the meta tag already exists, make sure the value is "true"
                    if (matchSuppress.Groups[3].Value.ToLower() != "true")
                    {
                        text =
                            text.Substring(0, matchSuppress.Groups[3].Index)
                            + "true"
                            + text.Substring(
                                matchSuppress.Groups[3].Index + matchSuppress.Groups[3].Length
                            );
                    }
                }
                else
                {
                    // the meta tag has not been added, add it now
                    text = text.Insert(
                        match.Index + match.Length,
                        "\r\n    <meta name=\"lockFormatting\" content=\"true\"></meta>"
                    );
                }
            }

            return text;
        }

        internal static void CopyResizedImageFile(
            string srcPath,
            string dstPath,
            ImagePublishSettings imagePublishSettings,
            bool forUseOnColoredBackground
        )
        {
            using (var tagFile = RobustFileIO.CreateTaglibFile(srcPath))
            {
                // ImagePublishSettings.MaxWidth and MaxHeight are in landscape orientation (width > height), but
                // ImageUtils.GetDesiredImageSize() expects portrait orientation (height > width) for the maximums.
                var newSize = ImageUtils.GetDesiredImageSize(
                    tagFile.Properties.PhotoWidth,
                    tagFile.Properties.PhotoHeight,
                    (int)imagePublishSettings.MaxHeight,
                    (int)imagePublishSettings.MaxWidth
                );
                if (srcPath != dstPath)
                    RobustFile.Copy(srcPath, dstPath);
                if (
                    ImageUtils.ResizeImageFileWithOptionalTransparency(
                        dstPath,
                        newSize,
                        false,
                        forUseOnColoredBackground,
                        tagFile
                    )
                )
                    return;
            }
            // Use the standard C# image handling to shrink the image and possibly make it transparent.
            // This produces files with fixed quality of 75, which is not ideal, but it's better than nothing.
            var modifiedContent = BookCompressor.GetImageBytesForElectronicPub(
                srcPath,
                forUseOnColoredBackground,
                imagePublishSettings
            );
            RobustFile.WriteAllBytes(dstPath, modifiedContent);
        }

        /// <summary>
        /// For electronic books, we want to limit the dimensions of images since they'll be displayed
        /// on small screens.  More importantly, we want to limit the size of the image file since it
        /// will often be transmitted over slow network connections.  So rather than merely zipping up
        /// an image file, we set its dimensions to within our desired limit (currently 600x600px) and
        /// generate the bytes in the desired format.  If the original image is already small enough, we
        /// retain its dimensions.  We also make two-color or graysceale png images have transparent backgrounds if requested so
        /// that they will work for cover pages.  If transparency is not needed, the original image file
        /// bytes are returned if that results in a smaller final image file.
        /// </summary>
        /// <remarks>
        /// Note that we have to write png files with 32-bit color even if the orginal file used 1-bit
        /// 4-bit, or 8-bit grayscale.  So .png files may come out bigger even when the dimensions
        /// shrink, and likely will be bigger when the dimensions stay the same.  This might be a
        /// limitation of the underlying .Net/Windows and Mono/Linux code, or might be needed for
        /// transparent backgrounds.
        /// </remarks>
        /// <returns>The bytes of the (possibly) adjusted image.</returns>
        internal static byte[] GetImageBytesForElectronicPub(
            string filePath,
            bool forUseOnColoredBackground,
            ImagePublishSettings imagePublishSettings
        )
        {
            var maxWidth = imagePublishSettings.MaxWidth;
            var maxHeight = imagePublishSettings.MaxHeight;

            var originalBytes = RobustFile.ReadAllBytes(filePath);
            using (var originalImage = PalasoImage.FromFileRobustly(filePath))
            {
                var image = originalImage.Image;
                int originalWidth = image.Width;
                int originalHeight = image.Height;
                var appearsToBeJpeg = ImageUtils.AppearsToBeJpeg(originalImage);
                if (originalWidth > maxWidth || originalHeight > maxHeight || !appearsToBeJpeg)
                {
                    // Preserve the aspect ratio
                    float scaleX = (float)maxWidth / (float)originalWidth;
                    float scaleY = (float)maxHeight / (float)originalHeight;
                    // no point in ever expanding, even if we're making a new image just for transparency.
                    float scale = Math.Min(1.0f, Math.Min(scaleX, scaleY));

                    // New width and height maintaining the aspect ratio
                    int newWidth = (int)(originalWidth * scale);
                    int newHeight = (int)(originalHeight * scale);
                    var imagePixelFormat = image.PixelFormat;
                    switch (imagePixelFormat)
                    {
                        // These three formats are not supported for bitmaps to be drawn on using Graphics.FromImage.
                        // So use the default bitmap format.
                        // Enhance: if these are common it may be worth research to find out whether there are better options.
                        // - possibly the 'reduced' image might not be reduced...even though smaller, the indexed format
                        // might be so much more efficient that it is smaller. However, even if that is true, it doesn't
                        // necessarily follow that it takes less memory to render on the device. So it's not obvious that
                        // we should keep the original just because it's a smaller file.
                        // - possibly we don't need a 32-bit bitmap? Unfortunately the 1bpp/4bpp/8bpp only tells us
                        // that the image uses two, 16, or 256 distinct colors, not what they are or what precision they have.
                        case PixelFormat.Format1bppIndexed:
                        case PixelFormat.Format4bppIndexed:
                        case PixelFormat.Format8bppIndexed:
                            imagePixelFormat = PixelFormat.Format32bppArgb;
                            break;
                    }
                    // OTOH, always using 32-bit format for .png files keeps us from having problems in BloomReader
                    // like BL-5740 (where 24bit format files came out in BR with black backgrounds).
                    if (!appearsToBeJpeg)
                        imagePixelFormat = PixelFormat.Format32bppArgb;
                    // Image files may have unknown formats which can be read, but not written.
                    // See https://issues.bloomlibrary.org/youtrack/issue/BH-5812.
                    imagePixelFormat = EnsureValidPixelFormat(imagePixelFormat);

                    // Making white pixels transparent for non-two-tone PNG files is not a good idea.  See BL-12570.
                    var makeTransparent =
                        forUseOnColoredBackground
                        && ImageUtils.ShouldMakeBackgroundTransparent(originalImage);

                    using (var newImage = new Bitmap(newWidth, newHeight, imagePixelFormat))
                    {
                        // Draws the image in the specified size with quality mode set to HighQuality
                        using (Graphics graphics = Graphics.FromImage(newImage))
                        {
                            graphics.CompositingQuality = CompositingQuality.HighQuality;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            using (var imageAttributes = new ImageAttributes())
                            {
                                // See https://stackoverflow.com/a/11850971/7442826
                                // Fixes the 50% gray border issue on bright white or dark images
                                imageAttributes.SetWrapMode(WrapMode.TileFlipXY);

                                // In addition to possibly scaling, we want PNG images to have transparent backgrounds.
                                if (makeTransparent)
                                {
                                    // This specifies that all white or very-near-white pixels (all color components at least 253/255)
                                    // will be made transparent.
                                    imageAttributes.SetColorKey(
                                        Color.FromArgb(253, 253, 253),
                                        Color.White
                                    );
                                }
                                var destRect = new Rectangle(0, 0, newWidth, newHeight);
                                graphics.DrawImage(
                                    image,
                                    destRect,
                                    0,
                                    0,
                                    image.Width,
                                    image.Height,
                                    GraphicsUnit.Pixel,
                                    imageAttributes
                                );
                            }
                        }
                        // Save the file in the same format as the original, and return its bytes.
                        using (var tempFile = TempFile.WithExtension(Path.GetExtension(filePath)))
                        {
                            // This uses default quality settings for jpgs...one site says this is
                            // 75 quality on a scale that runs from 0-100. For most images, this
                            // should give a quality barely distinguishable from uncompressed and still save
                            // about 7/8 of the file size. Lower quality settings rapidly lose quality
                            // while only saving a little space; higher ones rapidly use more space
                            // with only marginal quality improvement.
                            // See  https://photo.stackexchange.com/questions/30243/what-quality-to-choose-when-converting-to-jpg
                            // for more on quality and  https://docs.microsoft.com/en-us/dotnet/framework/winforms/advanced/how-to-set-jpeg-compression-level
                            // for how to control the quality setting if we decide to (RobustImageIO has
                            // suitable overloads).
                            RobustImageIO.SaveImage(newImage, tempFile.Path, image.RawFormat);
                            // Copy the metadata from the original file to the new file.
                            var metadata = RobustFileIO.MetadataFromFile(filePath);
                            if (!metadata.IsEmpty)
                                metadata.Write(tempFile.Path);
                            var newBytes = RobustFile.ReadAllBytes(tempFile.Path);
                            if (newBytes.Length < originalBytes.Length || makeTransparent)
                                return newBytes;
                        }
                    }
                }
            }
            return originalBytes;
        }

        private static PixelFormat EnsureValidPixelFormat(PixelFormat imagePixelFormat)
        {
            // If it's a standard, known format, return the input value.
            // Otherwise, return our old standby, 32bppArgb.
            switch (imagePixelFormat)
            {
                case PixelFormat.Format1bppIndexed:
                case PixelFormat.Format4bppIndexed:
                case PixelFormat.Format8bppIndexed:
                case PixelFormat.Format16bppArgb1555:
                case PixelFormat.Format16bppGrayScale:
                case PixelFormat.Format16bppRgb555:
                case PixelFormat.Format16bppRgb565:
                case PixelFormat.Format24bppRgb:
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                case PixelFormat.Format48bppRgb:
                case PixelFormat.Format64bppArgb:
                case PixelFormat.Format64bppPArgb:
                    return imagePixelFormat;
                default:
                    //Console.WriteLine("EnsureValidPixelFormat({0}) changed to {1}", imagePixelFormat, PixelFormat.Format32bppArgb);
                    return PixelFormat.Format32bppArgb;
            }
        }
    }
}
