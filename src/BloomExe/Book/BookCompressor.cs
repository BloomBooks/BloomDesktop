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
using Bloom.web.controllers;

namespace Bloom.Book
{
	public class BookCompressor
	{
		public const string BloomPubExtensionWithDot = ".bloomd";
		public static string LastVersionCode { get; private set; }

		// these image files may need to be reduced before being stored in the compressed output file
		internal static readonly string[] ImageFileExtensions = { ".tif", ".tiff", ".png", ".bmp", ".jpg", ".jpeg" };

		// these files (if encountered) won't be included in the compressed version
		internal static readonly string[] ExcludedFileExtensionsLowerCase = { ".db", ".pdf", ".bloompack", ".bak", ".userprefs", ".bloombookorder", ".map" };

		internal static void MakeSizedThumbnail(Book book, Color backColor, string destinationFolder, int heightAndWidth)
		{
			// If this fails to create a 'coverImage200.jpg', either the cover image is missing or it's only a placeholder.
			// If this is a new book, the file may exist already, but we want to make sure it's up-to-date.
			// If this is an older book, we need the .bloomd to have it so that Harvester will be able to access it.
			BookThumbNailer.GenerateImageForWeb(book);

			var coverImagePath = book.GetCoverImagePath();
			if (coverImagePath == null)
			{
				var blankImage = Path.Combine(FileLocationUtilities.DirectoryOfApplicationOrSolution, "DistFiles", "Blank.png");
				if (RobustFile.Exists(blankImage))
					coverImagePath = blankImage;
			}
			if(coverImagePath != null)
			{
				var thumbPath = Path.Combine(destinationFolder, "thumbnail.png");
				RuntimeImageProcessor.GenerateEBookThumbnail(coverImagePath, thumbPath, heightAndWidth, heightAndWidth, backColor);
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
		/// <param name="excludeAudio">If true, the contents of the audio directory will not be included</param>
		public static void CompressCollectionDirectory(string outputPath, string directoryToCompress, string dirNamePrefix, bool forReaderTools, bool excludeAudio)
		{
			CompressDirectory(outputPath, directoryToCompress, dirNamePrefix, forReaderTools, excludeAudio, depthFromCollection: 0);
		}

		/// <summary>
		/// Zips a directory containing a Bloom book, along with all files and subdirectories
		/// </summary>
		/// <param name="outputPath">The location to which to create the output zip file</param>
		/// <param name="directoryToCompress">The directory to add recursively</param>
		/// <param name="dirNamePrefix">string to prefix to the zip entry name</param>
		/// <param name="forReaderTools">If True, then some pre-processing will be done to the contents of decodable
		/// and leveled readers before they are added to the ZipStream</param>
		/// <param name="excludeAudio">If true, the contents of the audio directory will not be included</param>
		/// <param name="reduceImages">If true, image files are reduced in size to no larger than the max size before saving</para>
		/// <param name="omitMetaJson">If true, meta.json is excluded (typically for HTML readers).</param>
		public static void CompressBookDirectory(string outputPath, string directoryToCompress, string dirNamePrefix,
			bool forReaderTools = false, bool excludeAudio = false, bool reduceImages = false, bool omitMetaJson = false, bool wrapWithFolder = true,
			string pathToFileForSha = null)
		{
			CompressDirectory(outputPath, directoryToCompress, dirNamePrefix, forReaderTools, excludeAudio, reduceImages, omitMetaJson,
				wrapWithFolder, pathToFileForSha, depthFromCollection: 1);
		}

		private static void CompressDirectory(string outputPath, string directoryToCompress, string dirNamePrefix,
			bool forReaderTools = false, bool excludeAudio = false, bool reduceImages = false, bool omitMetaJson = false, bool wrapWithFolder = true,
			string pathToFileForSha = null, int depthFromCollection = 1)
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
					CompressDirectory(directoryToCompress, zipStream, dirNameOffset, dirNamePrefix, depthFromCollection, forReaderTools, excludeAudio, reduceImages, omitMetaJson, pathToFileForSha);

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
		/// <param name="depthFromCollection">int with the number of folders away it is from the collection folder. The collection folder itself is 0,
		/// a book is 1, a subfolder of the book is 2, etc.</param>
		/// <param name="forReaderTools">If True, then some pre-processing will be done to the contents of decodable
		/// and leveled readers before they are added to the ZipStream</param>
		/// <param name="excludeAudio">If true, the contents of the audio directory will not be included</param>
		/// <param name="reduceImages">If true, image files are reduced in size to no larger than the max size before saving</para>
		/// <param name="omitMetaJson">If true, meta.json is excluded (typically for HTML readers).</param>
		private static void CompressDirectory(string directoryToCompress, ZipOutputStream zipStream, int dirNameOffset, string dirNamePrefix,
			int depthFromCollection, bool forReaderTools, bool excludeAudio, bool reduceImages, bool omitMetaJson = false, string pathToFileForSha = null)
		{
			if (excludeAudio && Path.GetFileName(directoryToCompress).ToLowerInvariant() == "audio")
				return;
			var files = Directory.GetFiles(directoryToCompress);

			// Don't get distracted by HTML files in any folder other than the book folder.
			// These HTML files in other locations aren't generated by Bloom. They may not have the format Bloom expects,
			// causing needless parsing errors to be thrown if we attempt to read them using Bloom code.
			bool shouldScanHtml = depthFromCollection == 1;	// 1 means 1 level below the collection level, i.e. this is the book level
			var bookFile = shouldScanHtml ? BookStorage.FindBookHtmlInFolder(directoryToCompress) : null;
			XmlDocument dom = null;
			List<string> imagesToGiveTransparentBackgrounds = null;
			List<string> imagesToPreserveResolution = null;
			// Tests can also result in bookFile being null.
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
				imagesToPreserveResolution = FindImagesToPreserveResolution(dom);
				FindBackgroundAudioFiles(dom);
			}
			else
			{
				imagesToGiveTransparentBackgrounds = new List<string>();
				imagesToPreserveResolution = new List<string>();
			}

			// Some of the knowledge about ExcludedFileExtensions might one day move into this method.
			// But we'd have to check carefully the other places it is used.
			var localOnlyFiles = BookStorage.LocalOnlyFiles(directoryToCompress);
			foreach (var filePath in files)
			{
				if (ExcludedFileExtensionsLowerCase.Contains(Path.GetExtension(filePath.ToLowerInvariant())))
					continue; // BL-2246: skip putting this one into the BloomPack
				if (IsUnneededWaveFile(filePath, depthFromCollection))
					continue;
				if (localOnlyFiles.Contains(filePath))
					continue;
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
					fileName = Path.GetFileName(filePath);	// restore original capitalization
					if (imagesToPreserveResolution.Contains(fileName))
					{
						modifiedContent = RobustFile.ReadAllBytes(filePath);
					}
					else
					{
						// Cover images should be transparent if possible.  Others don't need to be.
						var makeBackgroundTransparent = imagesToGiveTransparentBackgrounds.Contains(fileName);
						modifiedContent = GetImageBytesForElectronicPub(filePath, makeBackgroundTransparent);
					}
					newEntry.Size = modifiedContent.Length;
				}
				else if (Path.GetExtension(filePath).ToLowerInvariant() == ".bloomcollection")
				{
					modifiedContent = Encoding.UTF8.GetBytes(GetBloomCollectionModifiedForTemplate(filePath));
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
						var sha = Book.ComputeHashForAllBookRelatedFiles(pathToFileForSha);
						var name = "version.txt"; // must match what BloomReader is looking for in NewBookListenerService.IsBookUpToDate()
						MakeExtraEntry(zipStream, name, sha);
						LastVersionCode = sha;
					}
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

				CompressDirectory(folder, zipStream, dirNameOffset, dirNamePrefix, depthFromCollection + 1, forReaderTools, excludeAudio, reduceImages);
			}
		}

		private static HashSet<string> _backgroundAudioFiles = new HashSet<string>();

		private static void FindBackgroundAudioFiles(XmlDocument dom)
		{
			_backgroundAudioFiles.Clear();
			foreach (var div in dom.DocumentElement.SafeSelectNodes("//div[@data-backgroundaudio]").Cast<XmlElement>())
			{
				var filename = div.GetStringAttribute("data-backgroundaudio");
				if (!String.IsNullOrEmpty(filename))
					_backgroundAudioFiles.Add(filename);
			}
		}

		/// <summary>
		/// We used to record narration in a .wav file that got converted to mp3 if the user set
		/// up LAME when we published the book.  LAME is now freely available so we automatically
		/// convert narration to mp3 as soon as we record it.  But we never want to upload old
		/// narration .wav files.  On the other hand, the user can select .wav files for background
		/// music, and we don't try to convert those so they do need to be uploaded.  We detect
		/// this situation by saving background audio (music) filenames in an earlier pass from
		/// scanning the XHTML file and saving the set of filenames found.
		/// </summary>
		private static bool IsUnneededWaveFile(string filePath, int depthFromCollection)
		{
			if (Path.GetExtension(filePath).ToLowerInvariant() != ".wav")
				return false;   // not a wave file
			var filename = Path.GetFileName(filePath);
			return !_backgroundAudioFiles.Contains(filename);
		}

		private const string kBackgroundImage = "background-image:url('";	// must match format string in HtmlDom.SetImageElementUrl()

		private static List<string> FindCoverImages (XmlDocument xmlDom)
		{
			var transparentImageFiles = new List<string>();
			foreach (var div in xmlDom.SafeSelectNodes("//div[contains(concat(' ',@class,' '),' coverColor ')]//div[contains(@class,'bloom-imageContainer')]").Cast<XmlElement>())
			{
				var style = div.GetAttribute("style");
				if (!String.IsNullOrEmpty(style) && style.Contains(kBackgroundImage))
				{
					System.Diagnostics.Debug.Assert(div.GetStringAttribute("class").Contains("bloom-backgroundImage"));
					// extract filename from the background-image style
					transparentImageFiles.Add(ExtractFilenameFromBackgroundImageStyleUrl(style));
				}
				else
				{
					// extract filename from child img element
					var img = div.SelectSingleNode("//img[@src]");
					if (img != null)
						transparentImageFiles.Add(System.Web.HttpUtility.UrlDecode(img.GetStringAttribute("src")));
				}
			}
			return transparentImageFiles;
		}

		private static List<string> FindImagesToPreserveResolution(XmlDocument dom)
		{
			var preservedImages = new List<string>();
			foreach (var div in dom.SafeSelectNodes("//div[contains(@class,'marginBox')]//div[contains(@class,'bloom-preserveResolution')]").Cast<XmlElement>())
			{
				var style = div.GetAttribute("style");
				if (!string.IsNullOrEmpty(style) && style.Contains(kBackgroundImage))
				{
					System.Diagnostics.Debug.Assert(div.GetStringAttribute("class").Contains("bloom-backgroundImage"));
					preservedImages.Add(ExtractFilenameFromBackgroundImageStyleUrl(style));
				}
			}
			foreach (var img in dom.SafeSelectNodes("//div[contains(@class,'marginBox')]//img[contains(@class,'bloom-preserveResolution')]").Cast<XmlElement>())
			{
				preservedImages.Add(System.Web.HttpUtility.UrlDecode(img.GetStringAttribute("src")));
			}
			return preservedImages;
		}

		private static string ExtractFilenameFromBackgroundImageStyleUrl(string style)
		{
			var filename = style.Substring(style.IndexOf(kBackgroundImage) + kBackgroundImage.Length);
			filename = filename.Substring(0, filename.IndexOf("'"));
			return System.Web.HttpUtility.UrlDecode(filename);
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
		/// Remove any SubscriptionCode element content and replace any BrandingProjectName element
		/// content with the text "Default".  We don't want to publish Bloom Enterprise subscription
		/// codes after all!
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-6938.
		/// </remarks>
		private static string GetBloomCollectionModifiedForTemplate(string filePath)
		{
			var dom = new XmlDocument();
			dom.PreserveWhitespace = true;
			dom.Load(filePath);
			foreach (var node in dom.SafeSelectNodes("//SubscriptionCode").Cast<XmlElement>().ToArray())
			{
				node.RemoveAll();	// should happen at most once
			}
			foreach (var node in dom.SafeSelectNodes("//BrandingProjectName").Cast<XmlElement>().ToArray())
			{
				node.RemoveAll();	// should happen at most once
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
					// Image files may have unknown formats which can be read, but not written.
					// See https://issues.bloomlibrary.org/youtrack/issue/BH-5812.
					imagePixelFormat = EnsureValidPixelFormat(imagePixelFormat);

					var needTransparencyConversion =
						!appearsToBeJpeg && needsTransparentBackground && !ImageUtils.HasTransparency(image);

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
								if (needTransparencyConversion)
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
							if (newBytes.Length < originalBytes.Length || needTransparencyConversion)
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
