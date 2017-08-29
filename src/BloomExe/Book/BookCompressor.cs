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
using Bloom.ImageProcessing;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.Book
{
	public class BookCompressor
	{
		public const string ExtensionForDeviceBloomBook = ".bloomd";

		// these image files may need to be reduced before being stored in the compressed output file
		internal static readonly string[] ImageFileExtensions = { ".tif", ".tiff", ".png", ".bmp", ".jpg", ".jpeg" };

		// these files (if encountered) won't be included in the compressed version
		internal static readonly string[] ExcludedFileExtensionsLowerCase = { ".db", ".pdf", ".bloompack", ".bak", ".userprefs", ".wav", ".bloombookorder" };

		public static void CompressBookForDevice(string outputPath, Book book)
		{
			CompressDirectory(outputPath, book.FolderPath, "", reduceImages:true, omitMetaJson: true, wrapWithFolder:false);
		}

		public static void CompressDirectory(string outputPath, string directoryToCompress, string dirNamePrefix,
			bool forReaderTools = false, bool excludeAudio = false, bool reduceImages = false, bool omitMetaJson = false, bool wrapWithFolder = true)
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
					CompressDirectory(directoryToCompress, zipStream, dirNameOffset, dirNamePrefix, forReaderTools, excludeAudio, reduceImages, omitMetaJson);

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
			bool forReaderTools, bool excludeAudio, bool reduceImages, bool omitMetaJson = false)
		{
			if (excludeAudio && Path.GetFileName(directoryToCompress).ToLowerInvariant() == "audio")
				return;
			var files = Directory.GetFiles(directoryToCompress);
			var bookFile = BookStorage.FindBookHtmlInFolder(directoryToCompress);

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
				if (fileName=="thumbnail-70.png" || fileName=="thumbnail-256.png")
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
					modifiedContent = GetBytesOfReducedImage(filePath);
					newEntry.Size = modifiedContent.Length;
				}
				else if (reduceImages && (bookFile == filePath))
				{
					var originalContent = File.ReadAllText(bookFile, Encoding.UTF8);
					var content = StripImagesWithMissingSrc(originalContent, bookFile);
					content = StripContentEditable(content);
					content = InsertReaderStylesheet(content);
					modifiedContent = Encoding.UTF8.GetBytes(content);
					newEntry.Size = modifiedContent.Length;

					// Make an extra entry containing the sha
					var sha = Book.MakeVersionCode(originalContent);
					var name = "version.txt"; // must match what BloomReader is looking for in NewBookListenerService.IsBookUpToDate()
					MakeExtraEntry(zipStream, name, sha);
					MakeExtraEntry(zipStream, "readerStyles.css",
						File.ReadAllText(FileLocator.GetFileDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"publish","android","readerStyles.css"))));
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

		private static string StripImagesWithMissingSrc(string input, string bookFile)
		{
			// Suspect this is faster than reading the whole thing into a DOM and using xpath.
			// That might be marginally more robust, but I think this is good enough.
			// The main purpose of this is to remove stubs put in to support optional branding files.
			// The javascript that hides the element if the file is not found doesn't work in the reader.
			var content = input;
			// Note that whitespace is not valid in places like < img> or <img / > or < / img>.
			// I expect tidy will make sure we really don't have any, so I'm not checking for it in
			// those spots.
			// Note that the src path can end either with the closing quote of the src attr or with a
			// question mark, signifying options (like ?optional=true for branding images) which are
			// not part of the file we want to search for.
			var regex = new Regex("<img[^>]*src\\s*=\\s*(['\"])(.*?)(\\1|\\?)[^>]*(/>|>\\s*</img\\s*>)");
			var match = regex.Match(content);
			var folderPath = Path.GetDirectoryName(bookFile);
			while (match.Success)
			{
				var file = match.Groups[2].Value;
				if (!File.Exists(Path.Combine(folderPath, file)))
				{
					content = content.Substring(0, match.Index) + content.Substring(match.Index + match.Length);
					match = regex.Match(content, match.Index);
				}
				else
				{
					match = regex.Match(content, match.Index + match.Length);
				}
			}
			return content;
		}

		private static string StripContentEditable(string input)
		{
			var regex = new Regex("\\s*contenteditable\\s*=\\s*(['\"]).*?\\1");
			return regex.Replace(input, "");
		}

		private static string GetMetaJsonModfiedForTemplate(string path)
		{
			var meta = BookMetaData.FromString(RobustFile.ReadAllText(path));
			meta.IsSuitableForMakingShells = true;
			return meta.Json;
		}

		private static string InsertReaderStylesheet(string input)
		{
			return input.Replace("</head", "<link rel=\"stylesheet\" href=\"readerStyles.css\" type=\"text/css\"></link></head");
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

		private const int kMaxWidth = 300;
		private const int kMaxHeight = 300;

		/// <summary>
		/// For electronic books, we want to minimize the actual size of images since they'll
		/// be displayed on small screens anyway.  So before zipping up the file, we replace its
		/// bytes with the bytes of a reduced copy of itself.  If the original image is already
		/// small enough, we return its bytes directly.
		/// We also make png images have transparent backgrounds. This is currently only necessary
		/// for cover pages, but it's an additional complication to detect which those are,
		/// and doesn't seem likely to cost much extra to do.
		/// </summary>
		/// <returns>The bytes of the (possibly) reduced image.</returns>
		internal static byte[] GetBytesOfReducedImage(string filePath)
		{
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
					using (var newImage = new Bitmap(newWidth, newHeight, imagePixelFormat))
					{
						// Draws the image in the specified size with quality mode set to HighQuality
						using (Graphics graphics = Graphics.FromImage(newImage))
						{
							graphics.CompositingQuality = CompositingQuality.HighQuality;
							graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
							graphics.SmoothingMode = SmoothingMode.HighQuality;
							if (appearsToBeJpeg)
							{
								graphics.DrawImage(image, 0, 0, newWidth, newHeight);
							}
							else
							{
								// In addition to possibly scaling, we want PNG images to have transparent backgrounds.
								ImageAttributes convertWhiteToTransparent = new ImageAttributes();
								// This specifies that all white or very-near-white pixels (all color components at least 253/255)
								// will be made transparent.
								convertWhiteToTransparent.SetColorKey(Color.FromArgb(253, 253, 253), Color.White);
								var destRect = new Rectangle(0, 0, newWidth, newHeight);
								graphics.DrawImage(image, destRect, 0,0, image.Width, image.Height,
									GraphicsUnit.Pixel, convertWhiteToTransparent);
							}
						}
						// Save the file in the same format as the original, and return its bytes.
						using (var tempFile = TempFile.WithExtension(Path.GetExtension(filePath)))
						{
							RobustImageIO.SaveImage(newImage, tempFile.Path, image.RawFormat);
							// Copy the metadata from the original file to the new file.
							var metadata = SIL.Windows.Forms.ClearShare.Metadata.FromFile(filePath);
							if (!metadata.IsEmpty)
								metadata.Write(tempFile.Path);
							return RobustFile.ReadAllBytes(tempFile.Path);
						}
					}
				}
			}
			return RobustFile.ReadAllBytes(filePath);
		}
	}
}
