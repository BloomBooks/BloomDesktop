using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.Publish;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using SIL.IO;
using SIL.Progress;

namespace Bloom.Book
{
	public class BookCompressor
	{
		public const string ExtensionForDeviceBloomBook = ".bloomd";

		// these files (if encountered) won't be included the compressed version
		internal static readonly string[] ExcludedFileExtensionsLowerCase = { ".db", ".pdf", ".bloompack", ".bak", ".userprefs", ".wav" };

		public static string CompressBookForDevice(Book book)
		{
			// todo: probably not needed once this is being called from Publish
			{
				// In case we have any new settings since the last time we were in the Edit tab (BL-3881)
				book.BringBookUpToDate(new NullProgress());
				book.Save();
			}

			var bookFolderPath = book.FolderPath;
			// todo: there are already methods in PublishModel/PublishView to handle audio including displaying the Lame is missing page.
			// Once this is actually being called from Publish, this can probably be reworked a little.
			AudioProcessor.TryCompressingAudioAsNeeded(bookFolderPath, book.RawDom);

			using (var tempFile = TempFile.WithFilenameInTempFolder(book.Title + ExtensionForDeviceBloomBook))
			{
				CompressDirectory(tempFile.Path, bookFolderPath, "");

				// todo: if the code remains as is such that the caller just gets a path, it will be responsible
				// for ensuring we aren't leaving temp files hanging around. But it might be better to eventually have this
				// code handle moving/cleaning up the zip file once it has been created.
				tempFile.Detach();
				return tempFile.Path;
			}
		}

		public static void CompressDirectory(string outputPath, string directoryToCompress, string dirNamePrefix,
			bool forReaderTools = false, bool excludeAudio = false)
		{
			using (var fsOut = RobustFile.Create(outputPath))
			{
				using (var zipStream = new ZipOutputStream(fsOut))
				{
					zipStream.SetLevel(9);

					var rootName = Path.GetFileName(directoryToCompress);
					int dirNameOffset = directoryToCompress.Length - rootName.Length;
					CompressDirectory(directoryToCompress, zipStream, dirNameOffset, dirNamePrefix, forReaderTools, excludeAudio);

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
		/// <remarks>Protected for testing purposes</remarks>
		private static void CompressDirectory(string directoryToCompress, ZipOutputStream zipStream, int dirNameOffset, string dirNamePrefix,
			bool forReaderTools, bool excludeAudio)
		{
			if (excludeAudio && Path.GetFileName(directoryToCompress).ToLowerInvariant() == "audio")
				return;
			var files = Directory.GetFiles(directoryToCompress);
			var bookFile = BookStorage.FindBookHtmlInFolder(directoryToCompress);

			foreach (var filePath in files)
			{
				if (ExcludedFileExtensionsLowerCase.Contains(Path.GetExtension(filePath.ToLowerInvariant())))
					continue; // BL-2246: skip putting this one into the BloomPack
				if (Path.GetFileName(filePath).StartsWith(BookStorage.PrefixForCorruptHtmFiles))
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
				else if (forReaderTools && (Path.GetFileName(filePath)=="meta.json"))
				{
					modifiedContent = Encoding.UTF8.GetBytes(GetMetaJsonModfiedForTemplate(filePath));
					newEntry.Size = modifiedContent.Length;
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
						StreamUtils.Copy(memStream, zipStream, new byte[modifiedContent.Length]);
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

				CompressDirectory(folder, zipStream, dirNameOffset, dirNamePrefix, forReaderTools, excludeAudio);
			}
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
	}
}
