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
using Bloom.web;
using BloomTemp;
using SIL.Progress;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Xml;
using System.Collections.Generic;
using Bloom.web.controllers;

namespace Bloom.Book
{
	public class BookCompressor
	{
		public const string ExtensionForDeviceBloomBook = ".bloomd";
		public static string LastVersionCode { get; private set; }

		// these image files may need to be reduced before being stored in the compressed output file
		internal static readonly string[] ImageFileExtensions = { ".tif", ".tiff", ".png", ".bmp", ".jpg", ".jpeg" };

		// these files (if encountered) won't be included in the compressed version
		internal static readonly string[] ExcludedFileExtensionsLowerCase = { ".db", ".pdf", ".bloompack", ".bak", ".userprefs", ".wav", ".bloombookorder" };

		public static void CompressBookForDevice(string outputPath, Book book, BookServer bookServer, Color backColor, IWebSocketProgress progress)
		{
			using(var temp = new TemporaryFolder())
			{
				var modifiedBook = BloomReaderFileMaker.PrepareBookForBloomReader(book, bookServer, temp, backColor, progress);
				// We want at least 256 for Bloom Reader, because the screens have a high pixel density. And (at the moment) we are asking for
				// 64dp in Bloom Reader.

				MakeSizedThumbnail(modifiedBook, backColor, modifiedBook.FolderPath, 256);

				CompressDirectory(outputPath, modifiedBook.FolderPath, "", reduceImages: true, omitMetaJson: false, wrapWithFolder: false,
					pathToFileForSha: BookStorage.FindBookHtmlInFolder(book.FolderPath));
			}
		}

		private static void MakeSizedThumbnail(Book book, Color backColor, string destinationFolder, int heightAndWidth)
		{
			var coverImagePath = book.GetCoverImagePath();
			if(coverImagePath != null)
			{
				var thumbPath = Path.Combine(destinationFolder, "thumbnail.png");
				RuntimeImageProcessor.GenerateEBookThumbnail(coverImagePath, thumbPath, heightAndWidth, heightAndWidth, backColor);
			}
			// else, BR shows a default thumbnail for the book
		}

		/// <summary>
		/// tempFolderPath is where to put the book. Note that a few files (e.g., customCollectionStyles.css)
		/// are copied into its parent in order to be in the expected location relative to the book,
		/// so that needs to be a folder we can write in.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="bookServer"></param>
		/// <param name="tempFolderPath"></param>
		/// <returns></returns>
		public static Book MakeDeviceXmatterTempBook(Book book, BookServer bookServer, string tempFolderPath)
		{
			BookStorage.CopyDirectory(book.FolderPath, tempFolderPath);
			// We will later copy these into the book's own folder and adjust the style sheet refs.
			// But in some cases (at least, where the book's primary stylesheet does not provide
			// the information SizeAndOrientation.GetLayoutChoices() is looking for), we need them
			// to exist in the originally expected lcoation: the book's parent directory for
			// BringBookUpToDate to succeed.
			BookStorage.CopyCollectionStyles(book.FolderPath, Path.GetDirectoryName(tempFolderPath));
			var bookInfo = new BookInfo(tempFolderPath, true);
			bookInfo.XMatterNameOverride = "Device";
			var modifiedBook = bookServer.GetBookFromBookInfo(bookInfo);
			modifiedBook.BringBookUpToDate(new NullProgress(), true);
			modifiedBook.AdjustCollectionStylesToBookFolder();
			modifiedBook.RemoveNonPublishablePages();
			modifiedBook.Save();
			modifiedBook.Storage.UpdateSupportFiles();
			// Copy the possibly modified stylesheets after UpdateSupportFiles so that they don't
			// get replaced by the factory versions.
			BookStorage.CopyCollectionStyles(book.FolderPath, tempFolderPath);
			return modifiedBook;
		}

		public static void CompressDirectory(string outputPath, string directoryToCompress, string dirNamePrefix,
			bool forReaderTools = false, bool excludeAudio = false, bool reduceImages = false, bool omitMetaJson = false, bool wrapWithFolder = true,
			string pathToFileForSha = null)
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
						// a zip, as with .bloomd files)
						dirNameOffset = directoryToCompress.Length + 1;
					}
					CompressDirectory(directoryToCompress, zipStream, dirNameOffset, dirNamePrefix, forReaderTools, excludeAudio, reduceImages, omitMetaJson, pathToFileForSha);

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
		/// <param name="dirNameOffset">This number of characters will be removed from the full directory or file name
		/// before creating the zip entry name</param>
		/// <param name="dirNamePrefix">string to prefix to the zip entry name</param>
		/// <param name="forReaderTools">If True, then some pre-processing will be done to the contents of decodable
		/// and leveled readers before they are added to the ZipStream</param>
		/// <param name="excludeAudio">If true, the contents of the audio directory will not be included</param>
		/// <param name="reduceImages">If true, images will be compressed before being added to the zip file</param>
		/// <param name="omitMetaJson">If true, meta.json is excluded (typically for HTML readers).</param>
		/// <para> name="reduceImages">If true, image files are reduced in size to no larger than 300x300 before saving</para>
		/// <remarks>Protected for testing purposes</remarks>
		private static void CompressDirectory(string directoryToCompress, ZipOutputStream zipStream, int dirNameOffset, string dirNamePrefix,
			bool forReaderTools, bool excludeAudio, bool reduceImages, bool omitMetaJson = false, string pathToFileForSha = null)
		{
			if (excludeAudio && Path.GetFileName(directoryToCompress).ToLowerInvariant() == "audio")
				return;
			var files = Directory.GetFiles(directoryToCompress);
			var bookFile = BookStorage.FindBookHtmlInFolder(directoryToCompress);
			XmlDocument dom = null;
			List<string> imagesToGiveTransparentBackgrounds = null;
			// Tests can result in bookFile being null.
			if (!String.IsNullOrEmpty(bookFile))
			{
				var originalContent = File.ReadAllText(bookFile, Encoding.UTF8);
				dom = XmlHtmlConverter.GetXmlDomFromHtml(originalContent);
				var fullScreenAttr = dom.GetElementsByTagName("body").Cast<XmlElement>().First().Attributes["data-bffullscreenpicture"]?.Value;
				if (fullScreenAttr != null && fullScreenAttr.IndexOf("bloomReader", StringComparison.InvariantCulture) >= 0)
				{
					// This feature (currently used for motion books in landscape mode) triggers an all-black background,
					// due to a rule in bookFeatures.less.
					// Making white pixels transparent on an all-black background makes line-art disappear,
					// which is bad (BL-6564), so just make an empty list in this case.
					imagesToGiveTransparentBackgrounds = new List<string>();
				}
				else
				{
					imagesToGiveTransparentBackgrounds = FindCoverImages(dom);
				}
			}
			else
			{
				imagesToGiveTransparentBackgrounds = new List<string>();
			}
			foreach (var filePath in files)
			{
				if (ExcludedFileExtensionsLowerCase.Contains(Path.GetExtension(filePath.ToLowerInvariant())))
					continue; // BL-2246: skip putting this one into the BloomPack
				var fileName = Path.GetFileName(filePath).ToLowerInvariant();
				if (fileName.StartsWith(BookStorage.PrefixForCorruptHtmFiles))
					continue;
				// Various stuff we keep in the book folder that is useful for editing or bloom library
				// or displaying collections but not needed by the reader. The most important is probably
				// eliminating the pdf, which can be very large. Note that we do NOT eliminate the
				// basic thumbnail.png, as we want eventually to extract that to use in the Reader UI.
				if (fileName == "thumbnail-70.png" || fileName == "thumbnail-256.png")
					continue;
				if (fileName == "meta.json" && omitMetaJson)
					continue;

				FileInfo fi = new FileInfo(filePath);

				var entryName = dirNamePrefix + filePath.Substring(dirNameOffset);  // Makes the name in zip based on the folder
				entryName = ZipEntry.CleanName(entryName);          // Removes drive from name and fixes slash direction
				ZipEntry newEntry = new ZipEntry(entryName)
				{
					DateTime = fi.LastWriteTime,
					IsUnicodeText = true
				};
				// encode filename and comment in UTF8
				byte[] modifiedContent = {};

				// if this is a ReaderTools book, call GetBookReplacedWithTemplate() to get the contents
				if (forReaderTools && (bookFile == filePath))
				{
					modifiedContent = Encoding.UTF8.GetBytes(GetBookReplacedWithTemplate(filePath));
					newEntry.Size = modifiedContent.Length;
				}
				else if (forReaderTools && (Path.GetFileName(filePath) == "meta.json"))
				{
					modifiedContent = Encoding.UTF8.GetBytes(GetMetaJsonModfiedForTemplate(filePath));
					newEntry.Size = modifiedContent.Length;
				}
				else if (reduceImages && ImageFileExtensions.Contains(Path.GetExtension(filePath.ToLowerInvariant())))
				{
					// Cover images should be transparent if possible.  Others don't need to be.
					var makeBackgroundTransparent = imagesToGiveTransparentBackgrounds.Contains(Path.GetFileName(filePath));
					modifiedContent = GetImageBytesForElectronicPub(filePath, makeBackgroundTransparent);
					newEntry.Size = modifiedContent.Length;
				}
				// CompressBookForDevice is always called with reduceImages set.
				else if (reduceImages && bookFile == filePath)
				{
					SignLanguageApi.ProcessVideos(HtmlDom.SelectChildVideoElements(dom.DocumentElement).Cast<XmlElement>(), directoryToCompress);
					var newContent = XmlHtmlConverter.ConvertDomToHtml5(dom);
					modifiedContent = Encoding.UTF8.GetBytes(newContent);
					newEntry.Size = modifiedContent.Length;

					if (pathToFileForSha != null)
					{
						// Make an extra entry containing the sha
						var sha = Book.MakeVersionCode(File.ReadAllText(pathToFileForSha, Encoding.UTF8), pathToFileForSha);
						var name = "version.txt"; // must match what BloomReader is looking for in NewBookListenerService.IsBookUpToDate()
						MakeExtraEntry(zipStream, name, sha);
						LastVersionCode = sha;
					}
					MakeExtraEntry(zipStream, "readerStyles.css",
						File.ReadAllText(FileLocationUtilities.GetFileDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"publish","android","readerStyles.css"))));
				}
				else
				{
					newEntry.Size = fi.Length;
				}

				zipStream.PutNextEntry(newEntry);

				if (modifiedContent.Length > 0)
				{
					using (var memStream = new MemoryStream(modifiedContent))
					{
						// There is some minimum buffer size (44 was too small); I don't know exactly what it is,
						// but 1024 makes it happy.
						StreamUtils.Copy(memStream, zipStream, new byte[Math.Max(modifiedContent.Length, 1024)]);
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
				var dirName = Path.GetFileName(folder);
				if ((dirName == null) || (dirName.ToLowerInvariant() == "sample texts"))
					continue; // Don't want to bundle these up

				CompressDirectory(folder, zipStream, dirNameOffset, dirNamePrefix, forReaderTools, excludeAudio, reduceImages);
			}
		}

		private static List<string> FindCoverImages (XmlDocument xmlDom)
		{
			var transparentImageFiles = new List<string>();
			foreach (var img in xmlDom.SafeSelectNodes("//div[contains(concat(' ',@class,' '),' coverColor ')]//div[contains(@class,'bloom-imageContainer')]//img[@src]").Cast<XmlElement>())
				transparentImageFiles.Add(System.Web.HttpUtility.UrlDecode(img.GetStringAttribute("src")));
			return transparentImageFiles;
		}

		private static void MakeExtraEntry(ZipOutputStream zipStream, string name, string content)
		{
			ZipEntry entry = new ZipEntry(name);
			var shaBytes = Encoding.UTF8.GetBytes(content);
			entry.Size = shaBytes.Length;
			zipStream.PutNextEntry(entry);
			using (var memStream = new MemoryStream(shaBytes))
			{
				StreamUtils.Copy(memStream, zipStream, new byte[1024]);
			}
			zipStream.CloseEntry();
		}

		private static string GetMetaJsonModfiedForTemplate(string path)
		{
			var meta = BookMetaData.FromString(RobustFile.ReadAllText(path));
			meta.IsSuitableForMakingShells = true;
			return meta.Json;
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
			var regex = new Regex("\\s*<meta\\s+name=(['\\\"])lockedDownAsShell\\1 content=(['\\\"])true\\2>(</meta>)? *");
			var match = regex.Match(text);
			if (match.Success)
				text = text.Substring(0, match.Index) + text.Substring(match.Index + match.Length);

			// BL-2476: Readers made from BloomPacks should have the formatting dialog disabled
			regex = new Regex("\\s*<meta\\s+name=(['\\\"])pageTemplateSource\\1 content=(['\\\"])(Leveled|Decodable) Reader\\2>(</meta>)? *");
			match = regex.Match(text);
			if (match.Success)
			{
				// has the lockFormatting meta tag been added already?
				var regexSuppress = new Regex("\\s*<meta\\s+name=(['\\\"])lockFormatting\\1 content=(['\\\"])(.*)\\2>(</meta>)? *");
				var matchSuppress = regexSuppress.Match(text);
				if (matchSuppress.Success)
				{
					// the meta tag already exists, make sure the value is "true"
					if (matchSuppress.Groups[3].Value.ToLower() != "true")
					{
						text = text.Substring(0, matchSuppress.Groups[3].Index) + "true"
								+  text.Substring(matchSuppress.Groups[3].Index + matchSuppress.Groups[3].Length);
					}
				}
				else
				{
					// the meta tag has not been added, add it now
					text = text.Insert(match.Index + match.Length,
						"\r\n    <meta name=\"lockFormatting\" content=\"true\"></meta>");
				}
			}

			return text;
		}

		// See discussion in BL-5385
		private const int kMaxWidth = 600;
		private const int kMaxHeight = 600;

		/// <summary>
		/// For electronic books, we want to limit the dimensions of images since they'll be displayed
		/// on small screens.  More importantly, we want to limit the size of the image file since it
		/// will often be transmitted over slow network connections.  So rather than merely zipping up
		/// an image file, we set its dimensions to within our desired limit (currently 600x600px) and
		/// generate the bytes in the desired format.  If the original image is already small enough, we
		/// retain its dimensions.  We also make png images have transparent backgrounds if requested so
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
		internal static byte[] GetImageBytesForElectronicPub(string filePath, bool needsTransparentBackground)
		{
			var originalBytes = RobustFile.ReadAllBytes(filePath);
			using (var originalImage = PalasoImage.FromFileRobustly(filePath))
			{
				var image = originalImage.Image;
				int originalWidth = image.Width;
				int originalHeight = image.Height;
				var appearsToBeJpeg = ImageUtils.AppearsToBeJpeg(originalImage);
				if (originalWidth > kMaxWidth || originalHeight > kMaxHeight || !appearsToBeJpeg)
				{
					// Preserve the aspect ratio
					float scaleX = (float)kMaxWidth / (float)originalWidth;
					float scaleY = (float)kMaxHeight / (float)originalHeight;
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
								if (!appearsToBeJpeg && needsTransparentBackground)
								{
									// This specifies that all white or very-near-white pixels (all color components at least 253/255)
									// will be made transparent.
									imageAttributes.SetColorKey(Color.FromArgb(253, 253, 253), Color.White);
								}
								var destRect = new Rectangle(0, 0, newWidth, newHeight);
								graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height,
									GraphicsUnit.Pixel, imageAttributes);
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
							var metadata = SIL.Windows.Forms.ClearShare.Metadata.FromFile(filePath);
							if (!metadata.IsEmpty)
								metadata.Write(tempFile.Path);
							var newBytes = RobustFile.ReadAllBytes(tempFile.Path);
							if (newBytes.Length < originalBytes.Length || (needsTransparentBackground && !appearsToBeJpeg))
								return newBytes;
						}
					}
				}
			}
			return originalBytes;
		}
	}
}
