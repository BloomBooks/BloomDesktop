using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Text;
using System.Xml;
using Bloom.Api;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish;
using Bloom.MiscUI;
using Bloom.web;
using L10NSharp;
using SIL.Code;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Xml;

namespace Bloom.Book
{
	/* The role of this class is simply to isolate the actual storage mechanism (e.g. file system)
	 * to a single place.  All the other classes can then just pass around DOMs.
	 */
	public interface IBookStorage
	{
		//TODO Covert this most of this section to something like IBookDescriptor, which has enough display in a catalog, do some basic filtering, etc.
		string Key { get; }
		string FileName { get; }
		string FolderPath { get; }
		string PathToExistingHtml { get; }
		bool TryGetPremadeThumbnail(string fileName, out Image image);
		//bool DeleteBook();
		bool RemoveBookThumbnail(string fileName);
		string ErrorMessagesHtml { get; }
		string GetBrokenBookRecommendationHtml();

		// REQUIRE INTIALIZATION (AVOID UNLESS USER IS WORKING WITH THIS BOOK SPECIFICALLY)
		bool GetLooksOk();
		HtmlDom Dom { get; }
		void Save();
		HtmlDom GetRelocatableCopyOfDom();
		HtmlDom MakeDomRelocatable(HtmlDom dom);
		string SaveHtml(HtmlDom bookDom);
		void SetBookName(string name);
		string GetValidateErrors();
		void CheckBook(IProgress progress,string pathToFolderOfReplacementImages = null);
		void UpdateBookFileAndFolderName(CollectionSettings settings);
		string HandleRetiredXMatterPacks(HtmlDom dom, string nameOfXMatterPack);
		IFileLocator GetFileLocator();
		event EventHandler FolderPathChanged;
		void CleanupUnusedImageFiles();
		void CleanupUnusedAudioFiles();
        BookInfo BookInfo { get; set; }
		string NormalBaseForRelativepaths { get; }
		string InitialLoadErrors { get; }
		void UpdateSupportFiles();
		void Update(string fileName, string factoryPath = "");
	}

	public class BookStorage : IBookStorage
	{
		public delegate BookStorage Factory(string folderPath, bool forSelectedBook = false);//autofac uses this

		/// <summary>
		/// History of this number:
		///		0.4 had version 0.4
		///		0.8, 0.9, 1.0 had version 0.8
		///		1.1 had version 1.1
		///     Bloom 1.0 went out with format version 0.8, but meanwhile compatible books were made (by hand) with "1.1."
		///     We didn't notice because Bloom didn't actually check this number, it just produced it.
		///     At that point, books with a newer format version (2.0) started becoming availalbe, so we patched Bloom 1.0
		///     to reject those. At that point, we also changed Bloom 1.0's kBloomFormatVersion to 1.1 so that it would
		///     not reject those format version 1.1 books.
		///     For the next version of Bloom (expected to be called version 2, but unknown at the moment) we went with
		///     (coincidentally) kBloomFormatVersion = "2.0"
		internal const string kBloomFormatVersion = "2.0";

		public const string PrefixForCorruptHtmFiles = "_broken_";
		private  string _folderPath;
		private IChangeableFileLocator _fileLocator;
		private BookRenamedEvent _bookRenamedEvent;
		private readonly CollectionSettings _collectionSettings;
		private static bool _alreadyNotifiedAboutOneFailedCopy;
		private BookInfo _metaData;
		private bool _errorAlreadyContainsInstructions;

		public event EventHandler FolderPathChanged;

		// Returns any errors reported while loading the book (during 'expensive initialization').
		public string InitialLoadErrors { get; private set; }

		public BookInfo BookInfo
		{
			get
			{
				if (_metaData == null)
					_metaData = new BookInfo(_folderPath, false);
				return _metaData;
			}
			set { _metaData = value; }
		}

		public BookStorage(string folderPath, SIL.IO.IChangeableFileLocator baseFileLocator,
						   BookRenamedEvent bookRenamedEvent, CollectionSettings collectionSettings)
			:this(folderPath, true, baseFileLocator, bookRenamedEvent, collectionSettings)
		{ }

		public BookStorage(string folderPath, bool forSelectedBook, SIL.IO.IChangeableFileLocator baseFileLocator,
						   BookRenamedEvent bookRenamedEvent, CollectionSettings collectionSettings)
		{
			_folderPath = folderPath;

			//we clone becuase we'll be customizing this for use by just this book
			_fileLocator = (IChangeableFileLocator) baseFileLocator.CloneAndCustomize(new string[]{});
			_bookRenamedEvent = bookRenamedEvent;
			_collectionSettings = collectionSettings;

			ExpensiveInitialization(forSelectedBook);
		}

		public string PathToExistingHtml
		{
			get { return FindBookHtmlInFolder(_folderPath); }
		}

		public string FileName
		{
			get { return Path.GetFileNameWithoutExtension(_folderPath); }
		}

		public string FolderPath
		{
			get { return _folderPath; }
		}

		public string Key
		{
			get
			{
				return _folderPath;
			}
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="fileName"></param>
		/// <returns>false if we shouldn't mess with the thumbnail</returns>
		public bool RemoveBookThumbnail(string fileName)
		{
			string path = Path.Combine(_folderPath, fileName);
			if(RobustFile.Exists(path) &&
			 (new System.IO.FileInfo(path).IsReadOnly)) //readonly is good when you've put in a custom thumbnail
			{
				return false;
			}
			if (RobustFile.Exists(path))
			{
				RobustFile.Delete(path);
			}
			return true;
		}

		public string ErrorMessagesHtml { get; private set; }

		/// <summary>
		/// this is a method because it wasn't clear if we will eventually generate it on the fly (book paths do change as they are renamed)
		/// </summary>
		/// <returns></returns>
		public IFileLocator GetFileLocator()
		{
			return _fileLocator;
		}

		public bool TryGetPremadeThumbnail(string fileName, out Image image)
		{
			string path = Path.Combine(_folderPath, fileName);
			if (RobustFile.Exists(path))
			{
				image = ImageUtils.GetImageFromFile(path);
				return true;
			}
			image = null;
			return false;
		}

		public HtmlDom Dom { get; private set; }

		public static string GetHtmlMessageIfVersionIsIncompatibleWithThisBloom(HtmlDom dom,string path)
		{
			var versionString = dom.GetMetaValue("BloomFormatVersion", "").Trim();
			if (string.IsNullOrEmpty(versionString))
				return "";// "This file lacks the following required element: <meta name='BloomFormatVersion' content='x.y'>";

			float versionFloat = 0;
			if (!float.TryParse(versionString, NumberStyles.Float, CultureInfo.InvariantCulture, out versionFloat))
				return "This file claims a version number that isn't really a number: " + versionString;

			if (versionFloat > float.Parse(kBloomFormatVersion, CultureInfo.InvariantCulture))
			{
				var msg = LocalizationManager.GetString("Errors.NeedNewerVersion",
					"{0} requires a newer version of Bloom. Download the latest version of Bloom from {1}","{0} will get the name of the book, {1} will give a link to open the Bloom Library Web page.");
				msg = string.Format(msg, path, string.Format("<a href='{0}'>BloomLibrary.org</a>", UrlLookup.LookupUrl(UrlType.LibrarySite)));
				msg += string.Format(". (Format {0} vs. {1})",versionString, kBloomFormatVersion);
				return msg;
			}

			return null;
		}

		public bool GetLooksOk()
		{
			return RobustFile.Exists(PathToExistingHtml) && string.IsNullOrEmpty(ErrorMessagesHtml);
		}

		public void Save()
		{
			if (!string.IsNullOrEmpty(ErrorMessagesHtml))
			{
				return; //too dangerous to try and save
			}
			Logger.WriteEvent("BookStorage.Saving... (eventual destination: {0})", PathToExistingHtml);

			Dom.UpdateMetaElement("Generator", "Bloom " + ErrorReport.GetVersionForErrorReporting());
			if (!Program.RunningUnitTests)
			{
				var ver = Assembly.GetEntryAssembly().GetName().Version;
				Dom.UpdateMetaElement("BloomFormatVersion", kBloomFormatVersion);
			}
			BookInfo.FormatVersion = kBloomFormatVersion;
			var watch = Stopwatch.StartNew();
			string tempPath = SaveHtml(Dom);
			watch.Stop();
			TroubleShooterDialog.Report($"Saving xml to html took {watch.ElapsedMilliseconds} milliseconds");

			watch = Stopwatch.StartNew();
			string errors = ValidateBook(Dom, tempPath);
			watch.Stop();
			TroubleShooterDialog.Report($"Validating book took {watch.ElapsedMilliseconds} milliseconds");

			if (!string.IsNullOrEmpty(errors))
			{
				Logger.WriteEvent("Errors saving book {0}: {1}", PathToExistingHtml, errors);
				var badFilePath = PathToExistingHtml + ".bad";
				RobustFile.Copy(tempPath, badFilePath, true);
				// delete the temporary file since we've made a copy of it.
				RobustFile.Delete(tempPath);
				//hack so we can package this for palaso reporting
				errors += string.Format("{0}{0}{0}Contents:{0}{0}{1}", Environment.NewLine,
					RobustFile.ReadAllText(badFilePath));
				var ex = new XmlSyntaxException(errors);

				SIL.Reporting.ErrorReport.NotifyUserOfProblem(ex, "Before saving, Bloom did an integrity check of your book, and found something wrong. This doesn't mean your work is lost, but it does mean that there is a bug in the system or templates somewhere, and the developers need to find and fix the problem (and your book).  Please click the 'Details' button and send this report to the developers.  Bloom has saved the bad version of this book as " + badFilePath + ".  Bloom will now exit, and your book will probably not have this recent damage.  If you are willing, please try to do the same steps again, so that you can report exactly how to make it happen.");
				Process.GetCurrentProcess().Kill();
			}
			else
			{
				Logger.WriteMinorEvent("ReplaceFileWithUserInteractionIfNeeded({0},{1})", tempPath, PathToExistingHtml);
				if (!string.IsNullOrEmpty(tempPath))
					FileUtils.ReplaceFileWithUserInteractionIfNeeded(tempPath, PathToExistingHtml, GetBackupFilePath());
			}

			BookInfo.Save();
		}

		private string GetBackupFilePath()
		{
			try
			{
				return Path.Combine(Path.GetDirectoryName(PathToExistingHtml), "bookhtml.bak");
			}
			catch (Exception ex)
			{
				// Following up BL-5636, which involves a "Path is not of a legal form" here, we'd like to see
				// the exact path that has the problem.
				throw new ArgumentException("Failed to get backup file path for " + PathToExistingHtml, ex);
			}
		}

		/// <summary>
		/// Determine whether the folder contains static content that might be read-only.
		/// For example, template books are read-only on Linux and Windows --allUsers.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-5061.
		/// </remarks>
		public static bool IsStaticContent(string folderPath)
		{
			// Checking just /browser/ or just /templates/ might accidently trigger on
			// a book named "browser" or "templates".
			// Convert \ to / for Window's sake.  It shouldn't exist in a Linux folder path.
			return folderPath.Replace('\\','/').Contains("/browser/templates/");
		}

		/// <summary>
		/// Compare the images we find in the top level of the book folder to those referenced
		/// in the dom, and remove any unreferenced ones.
		/// </summary>
		public void CleanupUnusedImageFiles()
		{
			if (IsStaticContent(_folderPath))
				return;
			//Collect up all the image files in our book's directory
			var imageFiles = new List<string>();
			var imageExtentions = new HashSet<string>(new []{ ".jpg", ".png", ".svg" });
			var ignoredFilenameStarts = new HashSet<string>(new [] { "thumbnail", "placeholder", "license","video-placeholder" });
			foreach (var path in Directory.EnumerateFiles(this._folderPath).Where(
				s => imageExtentions.Contains(Path.GetExtension(s).ToLowerInvariant())))
			{
				var filename = Path.GetFileName(path);
				if (ignoredFilenameStarts.Any(s=>filename.StartsWith(s, StringComparison.InvariantCultureIgnoreCase)))
					continue;
				imageFiles.Add(Path.GetFileName(GetNormalizedPathForOS(path)));
			}
			//Remove from that list each image actually in use
			var element = Dom.RawDom.DocumentElement;
			var toRemove = GetImagePathsRelativeToBook(element);

			//also, remove from the doomed list anything referenced in the datadiv that looks like an image
			//This saves us from deleting, for example, cover page images if this is called before the front-matter
			//has been applied to the document.
			toRemove.AddRange(from XmlElement dataDivImage in Dom.RawDom.SelectNodes("//div[@id='bloomDataDiv']//div[contains(text(),'.png') or contains(text(),'.jpg') or contains(text(),'.svg')]")
							  select UrlPathString.CreateFromUrlEncodedString(dataDivImage.InnerText.Trim()).PathOnly.NotEncoded);
			foreach (var fileName in toRemove)
			{
				imageFiles.Remove(GetNormalizedPathForOS(fileName));   //Remove just returns false if it's not in there, which is fine
			}

			//Delete any files still in the list
			foreach (var fileName in imageFiles)
			{
				var path = Path.Combine(_folderPath, fileName);
				try
				{
					Debug.WriteLine("Removed unused image: "+path);
					Logger.WriteEvent("Removed unused image: " + path);
					RobustFile.Delete(path);
				}
				catch (Exception)
				{
					Debug.WriteLine("Could not remove unused image: " + path);
					Logger.WriteEvent("Could not remove unused image: " + path);
					//It's not worth bothering the user about, we'll get it someday.
					//We're not even doing a Debug.Fail because that makes it harder to unit test this condition.
				}
			}
		}

		/// <summary>
		/// Compare the audio we find in the top level of the book folder to those referenced
		/// in the dom, and remove any unreferenced ones.
		/// </summary>
		public void CleanupUnusedAudioFiles()
		{

			if (IsStaticContent(_folderPath))
				return;
			//Collect up all the audio files in our book's audio directory
			var audioFolderPath = AudioProcessor.GetAudioFolderPath(_folderPath);
			var audioFilesToDeleteIfNotUsed = new List<string>();
			var audioExtensions = new HashSet<string>(new []{ ".wav", ".mp3" });  // .ogg, .wav, ...?

			if (Directory.Exists(audioFolderPath))
			{
				foreach (var path in Directory.EnumerateFiles(audioFolderPath).Where(
					s => audioExtensions.Contains(Path.GetExtension(s).ToLowerInvariant())))
				{
					audioFilesToDeleteIfNotUsed.Add(Path.GetFileName(GetNormalizedPathForOS(path)));
				}
			}

			//Remove from that list each audio file actually in use
			var element = Dom.RawDom.DocumentElement;
			var usedAudioPaths = GetAudioPathsRelativeToBook(element);

			//also, ensure that nothing to be removed is actually used in a datadiv as audio-sentence or backgroundaudio.
			//This prevents us from deleting, for example, cover page audios if this is called before the front-matter
			//has been applied to the document.
			usedAudioPaths.AddRange(from XmlElement dataDivAudio in Dom.RawDom.SelectNodes("//div[@id='bloomDataDiv']//div[contains(text(),'audio-sentence')"+
							" or contains(text(),'data-backgroundaudio')]")
							  select UrlPathString.CreateFromUrlEncodedString(dataDivAudio.InnerText.Trim()).PathOnly.NotEncoded);
			foreach (var fileName in usedAudioPaths)
			{
				if (Path.GetExtension(fileName).Length > 0)
				{
					if (audioExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
					{
						audioFilesToDeleteIfNotUsed.Remove(GetNormalizedPathForOS(fileName));  //This call just returns false if not found, which is fine.
					}
				}
				foreach (var ext in audioExtensions)
				{
					var tempfileName = Path.ChangeExtension(fileName, ext);
					audioFilesToDeleteIfNotUsed.Remove(GetNormalizedPathForOS(tempfileName));
				}
			}
			//Delete any files still in the list
			foreach (var fileName in audioFilesToDeleteIfNotUsed)
			{
				var path = Path.Combine(audioFolderPath, fileName);
				try
				{
					Debug.WriteLine("Removed unused audio file: "+path);
					Logger.WriteEvent("Removed unused audio file: " + path);
					RobustFile.Delete(path);
				}
				catch (Exception)
				{
					Debug.WriteLine("Could not remove unused audio file: " + path);
					Logger.WriteEvent("Could not remove unused audio file: " + path);
					//It's not worth bothering the user about, we'll get it someday.
					//We're not even doing a Debug.Fail because that makes it harder to unit test this condition.
				}
			}
		}

		/// <summary>
		/// Return the paths, relative to the book folder, of all the images referred to in the element.
		/// </summary>
		/// <param name="element"></param>
		/// <returns></returns>
		internal static List<string> GetImagePathsRelativeToBook(XmlElement element)
		{
			return (from XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(element)
				select HtmlDom.GetImageElementUrl(img).PathOnly.NotEncoded).Distinct().ToList();
		}

		internal static List<string> GetAudioPathsRelativeToBook(XmlElement element)
		{
			return (from XmlElement audio in HtmlDom.SelectChildAudioAndBackgroundMusicElements(element)
					select HtmlDom.GetAudioElementUrl(audio).PathOnly.NotEncoded).Distinct().ToList();
		}

		private string GetNormalizedPathForOS(string path)
		{
			return Environment.OSVersion.Platform == PlatformID.Win32NT
						? path.ToLowerInvariant()
						: path;
		}

		private void AssertIsAlreadyInitialized()
		{
			if (Dom == null)
				throw new ApplicationException("BookStorage was at a place that should have been initialized earlier, but wasn't.");
		}

		public string SaveHtml(HtmlDom dom)
		{
			AssertIsAlreadyInitialized();
			string tempPath = GetNameForATempFileInStorageFolder();
			MakeCssLinksAppropriateForStoredFile(dom);
			SetBaseForRelativePaths(dom, string.Empty);// remove any dependency on this computer, and where files are on it.
			//CopyXMatterStylesheetsIntoFolder
			return XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, tempPath);
		}

		/// <summary>
		/// Get a temporary file pathname in the current book's folder.  This is needed to ensure proper permissions are granted
		/// to the resulting file later after FileUtils.ReplaceFileWithUserInteractionIfNeeded is called.  That method may call
		/// RobustFile.Replace which replaces both the file content and the file metadata (permissions).  The result of that if we use
		/// the user's temp directory is described in http://issues.bloomlibrary.org/youtrack/issue/BL-3954.
		/// </summary>
		private string GetNameForATempFileInStorageFolder()
		{
			using (var temp = TempFile.InFolderOf(PathToExistingHtml))
			{
				return temp.Path;
			}
		}

		/// <summary>
		/// creates a relocatable copy of our main HtmlDom
		/// </summary>
		/// <returns></returns>
		public HtmlDom GetRelocatableCopyOfDom()
		{

			HtmlDom relocatableDom = Dom.Clone();

			SetBaseForRelativePaths(relocatableDom, _folderPath);
			EnsureHasLinksToStylesheets(relocatableDom);

			return relocatableDom;
		}

		/// <summary>
		/// this one works on the dom passed to it
		/// </summary>
		/// <param name="dom"></param>
		/// <returns></returns>
		public HtmlDom MakeDomRelocatable(HtmlDom dom)
		{
			var relocatableDom = dom.Clone();

			SetBaseForRelativePaths(relocatableDom, _folderPath);
			EnsureHasLinksToStylesheets(relocatableDom);

			return relocatableDom;
		}

		public void SetBookName(string name)
		{
			if (!Directory.Exists(_folderPath)) //bl-290 (user had 4 month-old version, so the bug may well be long gone)
			{
				var msg = LocalizationManager.GetString("BookStorage.FolderMoved",
					"It appears that some part of the folder path to this book has been moved or renamed. As a result, Bloom cannot save your changes to this page, and will need to exit now. If you haven't been renaming or moving things, please click Details below and report the problem to the developers.");
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(
					new ApplicationException(
						string.Format(
							"In SetBookName('{0}'), BookStorage thinks the existing folder is '{1}', but that does not exist. (ref bl-290)",
							name, _folderPath)),
					msg);
				// Application.Exit() is not drastic enough to terminate all the call paths here and all the code
				// that tries to make sure we save on exit. Get lots of flashing windows during shutdown.
				Environment.Exit(-1);
			}
			name = SanitizeNameForFileSystem(name);

			var currentFilePath = PathToExistingHtml;
			//REVIEW: This doesn't immediately make sense; if this function is told to call it Foo but it's current Foo1... why does this just return?

			if (Path.GetFileNameWithoutExtension(currentFilePath).StartsWith(name)) //starts with because maybe we have "myBook1"
				return;

			//figure out what name we're really going to use (might need to add a number suffix)
			var newFolderPath = Path.Combine(Directory.GetParent(FolderPath).FullName, name);
			newFolderPath = GetUniqueFolderPath(newFolderPath);

			Logger.WriteEvent("Renaming html from '{0}' to '{1}.htm'", currentFilePath, newFolderPath);

			//next, rename the file
			RobustFile.Move(currentFilePath, Path.Combine(FolderPath, Path.GetFileName(newFolderPath) + ".htm"));

			//next, rename the enclosing folder
			var fromToPair = new KeyValuePair<string, string>(FolderPath, newFolderPath);
			try
			{
				Logger.WriteEvent("Renaming folder from '{0}' to '{1}'", FolderPath, newFolderPath);

				//This one can't handle network paths and isn't necessary, since we know these are on the same volume:
				//SIL.IO.DirectoryUtilities.MoveDirectorySafely(FolderPath, newFolderPath);
				SIL.IO.RobustIO.MoveDirectory(FolderPath, newFolderPath);

				_fileLocator.RemovePath(FolderPath);
				_fileLocator.AddPath(newFolderPath);

				_folderPath = newFolderPath;
			}
			catch (Exception e)
			{
				Logger.WriteEvent("Failed folder rename: " + e.Message);
				Debug.Fail("(debug mode only): could not rename the folder");
			}

			_bookRenamedEvent.Raise(fromToPair);

			OnFolderPathChanged();
		}

		protected virtual void OnFolderPathChanged()
		{
			var handler = FolderPathChanged;
			if (handler != null) handler(this, EventArgs.Empty);
		}


		public string GetValidateErrors()
		{
			if (!string.IsNullOrEmpty(ErrorMessagesHtml))
				return "";
			if (!Directory.Exists(_folderPath))
			{
				return "The directory (" + _folderPath + ") could not be found.";
			}
			if (!RobustFile.Exists(PathToExistingHtml))
			{
				return "Could not find an html file to use.";
			}


			return ValidateBook(Dom, PathToExistingHtml);
		}

		/// <summary>
		///
		/// </summary>
		/// <remarks>The image-replacement feature is perhaps a one-off for a project where an advisor replaced the folders
		/// with a version that lacked most of the images (perhaps because dropbox copies small files first and didn't complete the sync)</remarks>
		/// <param name="progress"></param>
		/// <param name="pathToFolderOfReplacementImages">We'll find any matches in the entire folder, regardless of sub-folder name</param>
		public void CheckBook(IProgress progress, string pathToFolderOfReplacementImages = null)
		{
			var error = GetValidateErrors();
			if(!string.IsNullOrEmpty(error))
				progress.WriteError(error);

			//check for missing images

			foreach (XmlElement imgNode in HtmlDom.SelectChildImgAndBackgroundImageElements(Dom.Body))
			{
				var imageFileName = HtmlDom.GetImageElementUrl(imgNode).PathOnly.NotEncoded;
				if (string.IsNullOrEmpty(imageFileName))
				{
					var classNames=imgNode.GetAttribute("class");
					if (classNames == null || !classNames.Contains("licenseImage"))//bit of hack... it's ok for licenseImages to be blank
					{
						progress.WriteWarning("image src is missing");
						//review: this, we could fix with a new placeholder... maybe in the javascript edit stuff?
					}
					continue;
				}
				// Certain .svg files (cogGrey.svg, FontSizeLetter.svg) aren't really part of the book and are stored elsewhere.
				// Also, at present the user can't insert them into a book. Don't report them.
				// TODO: if we ever allow the user to add .svg files, we'll need to change this
				if (Path.HasExtension(imageFileName) && Path.GetExtension(imageFileName).ToLowerInvariant() == ".svg")
					continue;

				// Branding images are handled in a special way in BrandingApi.cs.
				// Without this, we get "Warning: Image /bloom/api/branding/image is missing from the folder xxx" (see BL-3975)
				if (imageFileName.EndsWith(BrandingApi.kBrandingImageUrlPart))
					continue;

				//trim off the end of "license.png?123243"
				var startOfDontCacheHack = imageFileName.IndexOf('?');
				if (startOfDontCacheHack > -1)
					imageFileName = imageFileName.Substring(0, startOfDontCacheHack);

				while (Uri.UnescapeDataString(imageFileName) != imageFileName)
					imageFileName = Uri.UnescapeDataString(imageFileName);

				if (!RobustFile.Exists(Path.Combine(_folderPath, imageFileName)))
				{
					if (!string.IsNullOrEmpty(pathToFolderOfReplacementImages))
					{
						if (!AttemptToReplaceMissingImage(imageFileName, pathToFolderOfReplacementImages, progress))
						{
							progress.WriteWarning(string.Format("Could not find replacement for image {0} in {1}", imageFileName, _folderPath));
						}
					}
					else
					{
						progress.WriteWarning(string.Format("Image {0} is missing from the folder {1}", imageFileName, _folderPath));
					}
				}
			}
		}

		private bool AttemptToReplaceMissingImage(string missingFile, string pathToFolderOfReplacementImages, IProgress progress)
		{
			try
			{
				foreach (var imageFilePath in Directory.GetFiles(pathToFolderOfReplacementImages, missingFile))
				{
					RobustFile.Copy(imageFilePath, Path.Combine(_folderPath, missingFile));
					progress.WriteMessage(string.Format("Replaced image {0} from a copy in {1}", missingFile,
														pathToFolderOfReplacementImages));
					return true;
				}
				foreach (var dir in Directory.GetDirectories(pathToFolderOfReplacementImages))
				{
//				    doesn't really matter
//					if (dir == _folderPath)
//				    {
//						progress.WriteMessage("Skipping the directory of this book");
//				    }
					if (AttemptToReplaceMissingImage(missingFile, dir, progress))
						return true;
				}
				return false;
			}
			catch (Exception error)
			{
				progress.WriteException(error);
				return false;
			}
		}

		public void UpdateBookFileAndFolderName(CollectionSettings collectionSettings)
		{
			var title = Dom.Title;
			if (title != null)
			{
				SetBookName(title);
			}
		}


		#region Static Helper Methods
		public static string FindBookHtmlInFolder(string folderPath)
		{
			string p = Path.Combine(folderPath, Path.GetFileName(folderPath) + ".htm");
			if (RobustFile.Exists(p))
				return p;
			p = Path.Combine(folderPath, Path.GetFileName(folderPath) + ".html");
			if (RobustFile.Exists(p))
				return p;

			if (!Directory.Exists(folderPath)) //bl-291 (user had 4 month-old version, so the bug may well be long gone)
			{
				//in version 1.012 I got this because I had tried to delete the folder on disk that had a book
				//open in Bloom.
				SIL.Reporting.ErrorReport.NotifyUserOfProblem("There's a problem; Bloom can't save this book. Did you perhaps delete or rename the folder that this book is (was) in?");
				throw new ApplicationException(string.Format("In FindBookHtmlInFolder('{0}'), the folder does not exist. (ref bl-291)", folderPath));
			}

			//ok, so maybe they changed the name of the folder and not the htm. Can we find a *single* html doc?
			// BL-3572 when the only file in the directory is "Big Book.html", it matches both filters in Windows (tho' not in Linux?)
			// so Union works better here. (And we'll change the name of the book too.)
			var candidates = new List<string>(Directory.GetFiles(folderPath, "*.htm").Union(Directory.GetFiles(folderPath, "*.html")));
			var decoyMarkers = new string[] {"configuration",
				PrefixForCorruptHtmFiles, // Used to rename corrupt htm files before restoring backup
				"_conflict", // owncloud
				"[conflict]", // Google Drive
				"conflicted copy" // Dropbox
			};
			candidates.RemoveAll((name) => decoyMarkers.Any(d => name.ToLowerInvariant().Contains(d)));
			if (candidates.Count == 1)
				return candidates[0];

			//template
			p = Path.Combine(folderPath, "templatePages.htm");
			if (RobustFile.Exists(p))
				return p;
			p = Path.Combine(folderPath, "templatePages.html");
			if (RobustFile.Exists(p))
				return p;

			return string.Empty;
		}

		public static void SetBaseForRelativePaths(HtmlDom dom, string folderPath)
		{
			dom.BaseForRelativePaths = GetBaseForRelativePaths(folderPath);
		}

		/// <summary>
		/// Base for relative paths when editing the book (not generating PDF or anything special).
		/// </summary>
		public string NormalBaseForRelativepaths
		{
			get {  return GetBaseForRelativePaths(_folderPath);}
		}

		private static string GetBaseForRelativePaths(string folderPath)
		{
			string path = "";
			if (!string.IsNullOrEmpty(folderPath))
			{
				//this is only used by relative paths, and only img src's are left relative.
				//we are redirecting through our build-in httplistener in order to make white backgrounds transparent
				// and possibly shrink
				//big images before giving them to gecko which has trouble with really hi-res ones
				//Some clients don't want low-res images and can suppress this by setting HtmlDom.UseOriginalImages.
				var uri = folderPath + Path.DirectorySeparatorChar;
				path = uri.ToLocalhost();
			}
			return path;
		}


		public string ValidateBook(string path)
		{
			var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path, false));//with throw if there are errors
			return ValidateBook(dom, path);
		}

		private string ValidateBook(HtmlDom dom, string path)
		{
			Debug.WriteLine(string.Format("ValidateBook({0})", path));
			var msg= GetHtmlMessageIfVersionIsIncompatibleWithThisBloom(dom,path);
			return !string.IsNullOrEmpty(msg) ? msg : dom.ValidateBook(path, !BookInfo.IsSuitableForMakingTemplates);
		}



		#endregion


		/// <summary>
		/// Do whatever is needed to do more than just show a title and thumbnail
		/// </summary>
		private void ExpensiveInitialization(bool forSelectedBook = false)
		{
			Debug.WriteLine(string.Format("ExpensiveInitialization({0})", _folderPath));
			Dom = new HtmlDom();
			//the fileLocator we get doesn't know anything about this particular book.
			_fileLocator.AddPath(_folderPath);
			RequireThat.Directory(_folderPath).Exists();
			string pathToExistingHtml;
			try
			{
				pathToExistingHtml = PathToExistingHtml;
			}
            catch (UnauthorizedAccessException error)
			{
				ProcessAccessDeniedError(error);
				return;
			}
			var backupPath = GetBackupFilePath();

			if (!RobustFile.Exists(pathToExistingHtml))
			{
				var files = new List<string>(Directory.GetFiles(_folderPath));
				var b = new StringBuilder();
				b.AppendLine("Could not determine which html file in the folder to use.");
				if (files.Count == 0)
					b.AppendLine("***There are no files.");
				else
				{
					b.AppendLine("Files in this book are:");
					foreach (var f in files)
					{
						b.AppendLine("  " + f);
					}
				}
				throw new ApplicationException(b.ToString());
			}
			else
			{
				XmlDocument xmlDomFromHtmlFile;
				try
				{
					xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(PathToExistingHtml, false);
				}
				catch (UnauthorizedAccessException error)
				{
					ProcessAccessDeniedError(error);
					return;
				}
				catch (Exception error)
				{
					InitialLoadErrors = error.Message;
					// If the user is actually trying to look at this book and it's broken, we will try to restore a backup.
					// The main reason not to do this otherwise is that we think we should notify the user that we
					// are restoring a backup, and we don't want to bother him with such notifications about books
					// he isn't looking at currently.
					if (forSelectedBook && TryGetValidXmlDomFromHtmlFile(backupPath, out xmlDomFromHtmlFile))
					{
						RestoreBackup(pathToExistingHtml, error);
					}
					else
					{
						ErrorMessagesHtml = WebUtility.HtmlEncode(error.Message);
						Logger.WriteEvent("*** ERROR in " + PathToExistingHtml);
						Logger.WriteEvent("*** ERROR: " + error.Message.Replace("{", "{{").Replace("}", "}}"));
						return;
					}
				}

				Dom = new HtmlDom(xmlDomFromHtmlFile); //with throw if there are errors
				// Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
				Dom.RawDom.PreserveWhitespace = true;

				// An earlier comment warned that this was taking 1/3 of startup time. However, it was being done anyway
				// at some point where the Book constructor wanted to know whether the book was editable (which
				// triggers a check since books that don't validate aren't editable).
				// Hopefully this is OK since another old comment said,
				// we did in fact change things so that storage isn't used until we've shown all the thumbnails we can (then we go back and update in background)
				InitialLoadErrors = ValidateBook(Dom, pathToExistingHtml);
				if (forSelectedBook && !string.IsNullOrEmpty(InitialLoadErrors))
				{
					XmlDocument possibleBackupDom;
					if (TryGetValidXmlDomFromHtmlFile(backupPath, out possibleBackupDom))
					{
						RestoreBackup(pathToExistingHtml, new ApplicationException("main html file was not valid: " + InitialLoadErrors));
						xmlDomFromHtmlFile = possibleBackupDom;
						Dom = new HtmlDom(xmlDomFromHtmlFile);
					}
				}

				//For now, we really need to do this check, at least. This will get picked up by the Book later (feeling kludgy!)
				//I assume the following will never trigger (I also note that the dom isn't even loaded):

				if (!string.IsNullOrEmpty(ErrorMessagesHtml))
				{
					Dom.RawDom.LoadXml(
						"<html><body>There is a problem with the html structure of this book which will require expert help.</body></html>");
					Logger.WriteEvent(
						"{0}: There is a problem with the html structure of this book which will require expert help: {1}",
						PathToExistingHtml, ErrorMessagesHtml);
				}
					//The following is a patch pushed out on the 25th build after 1.0 was released in order to give us a bit of backwards version protection (I should have done this originally and in a less kludgy fashion than I'm forced to do now)
				else
				{
					var incompatibleVersionMessage = GetHtmlMessageIfVersionIsIncompatibleWithThisBloom(Dom,this.PathToExistingHtml);
					if (!string.IsNullOrWhiteSpace(incompatibleVersionMessage))
					{
						ErrorMessagesHtml = incompatibleVersionMessage;
						Logger.WriteEvent("*** ERROR: " + incompatibleVersionMessage);
						_errorAlreadyContainsInstructions = true;
						return;
					}
					else
					{
						Logger.WriteEvent("BookStorage Loading Dom from {0}", PathToExistingHtml);
					}
				}

				// probably not needed at runtime if !forSelectedBook, but one unit test relies on it having been done, and is very fast, so ok.
				Dom.UpdatePageDivs();

				// If the book isn't selected, then we're just here to do minimal things, hopefully quick things, to
				// show the title and thumbnail. Of course anything we *don't* do could effect the thumbnail. But
				// let's wait and deal with that if it seems like a real problem. At the moment it seems to me that
				// having to select the book to gets its thumbnail updated is a small price to pay.
				// In particular, things can go wrong when doing UpdateSupportFiles() because the book may call for
				// xmatter that this user doesn't have installed; when we're just showing all the thumbnails as quickly
				// as we can, that's really the wrong time to be putting up errors about the xmatter of particular books.
				if (forSelectedBook)
				{
					UpdateSupportFiles();
					CleanupUnusedImageFiles();
					CleanupUnusedAudioFiles();
				}
			}
		}

		private void RestoreBackup(string pathToExistingHtml, Exception error)
		{
			var backupPath = GetBackupFilePath();
			string corruptFilePath = GetUniqueFileName(FolderPath, PrefixForCorruptHtmFiles, "htm");
			RobustFile.Move(PathToExistingHtml, corruptFilePath);
			RobustFile.Move(backupPath, pathToExistingHtml);
			var msg = LocalizationManager.GetString("BookStorage.CorruptBook",
				"Bloom had a problem reading this book and recovered by restoring a recent backup. Please check recent changes to this book. If this happens for no obvious reason, please click Details below and report it to us.");
			ErrorReport.NotifyUserOfProblem(error, msg);
			// We've restored a validated backup, so as far as the caller is concerned we have a good book.
			InitialLoadErrors = "";
		}

		private bool TryGetValidXmlDomFromHtmlFile(string path, out XmlDocument xmlDomFromHtmlFile)
		{
			xmlDomFromHtmlFile = null;
			if (!RobustFile.Exists(path))
				return false;
			try
			{
				xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(path, false);
				return string.IsNullOrEmpty(ValidateBook(new HtmlDom(xmlDomFromHtmlFile), path));
			}
			catch (Exception error)
			{
				return false;
			}
			return true;
		}

		private void ProcessAccessDeniedError(UnauthorizedAccessException error)
		{
			var message = LocalizationManager.GetString("Errors.DeniedAccess",
				"Your computer denied Bloom access to the book. You may need technical help in setting the operating system permissions for this file.");
			message += Environment.NewLine + error.Message;
			Logger.WriteEvent("*** ERROR: " + message);
			message = WebUtility.HtmlEncode(message);
			var helpUrl = @"http://community.bloomlibrary.org/t/how-to-fix-file-permissions-problems/78";
			var seeAlso = WebUtility.HtmlEncode(LocalizationManager.GetString("Common.SeeWebPage", "See {0}."));
			message += "<br></br>" + string.Format(seeAlso, "<a href='" + helpUrl + "'>" + helpUrl + "</a>");
			ErrorMessagesHtml = message;
			_errorAlreadyContainsInstructions = true;
		}

		/// <summary>
		/// we update these so that the file continues to look the same when you just open it in firefox
		/// </summary>
		public void UpdateSupportFiles()
		{
			if (IsPathReadonly(_folderPath))
			{
				Logger.WriteEvent("Not updating files in folder {0} because the directory is read-only.", _folderPath);
			}
			else
			{
				Update("placeHolder.png");
				Update("basePage.css");
				Update("previewMode.css");
				Update("origami.css");
				Update("languageDisplay.css");

				foreach (var path in Directory.GetFiles(_folderPath, "*.css"))
				{
					var file = Path.GetFileName(path);

					// In BL-5824, we got bit by design decisions we made that allow stylesheets installed via bloompack and by new Bloom versions
					// to replace local ones. This was done so that we could send out new Bloom implementation stylesheets via bloompack and in new Bloom versions
					// and have those used in all the books. This works well for most stylesheets.
					// But customBookStyles.css needs to be an exception; it's whole purpose is to let the local book override Bloom's normal
					// behavior or anything in a bloompack.
					// So customBookStyles.css is not overridden (EnhancedImageServer) or replaced (here)..
					if (!file.ToLowerInvariant().Contains("custombookstyles"))
					{
						Update(file);
					}
				}
			}

			try
			{
				var path = PathToXMatterStylesheet;
				Update(Path.GetFileName(path), path);
			}
			catch (Exception error)
			{
				ErrorMessagesHtml = WebUtility.HtmlEncode(error.Message);
			}
		}

		private string PathToXMatterStylesheet
		{
			get
			{
				var nameOfCollectionXMatterPack = BookInfo.XMatterNameOverride ?? _collectionSettings.XMatterPackName;

				nameOfCollectionXMatterPack = HandleRetiredXMatterPacks(Dom, nameOfCollectionXMatterPack);

				//Here the xmatter Helper may come back loaded with the xmatter from the collection settings, but if the book
				//specifies a different one, it will come back with that (if it can be found).
				return new XMatterHelper(Dom, nameOfCollectionXMatterPack, _fileLocator).PathToXMatterStylesheet;
			}
		}

		private bool IsPathReadonly(string path)
		{
			return (RobustFile.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
		}

		public void Update(string fileName, string factoryPath = "")
		{
			if (!IsUserOrTempFolder)
			{
				if (fileName.ToLowerInvariant().Contains("xmatter") && !fileName.ToLower().StartsWith("factory-xmatter"))
				{
					return; //we don't want to copy custom xmatters around to the program files directory, template directories, the Bloom src code folders, etc.
				}
			}

			// do not attempt to copy files to the installation directory
			var targetDirInfo = new DirectoryInfo(_folderPath);
			if (SIL.PlatformUtilities.Platform.IsMono)
			{
				// do not attempt to copy files to the "/usr" directory
				if (targetDirInfo.FullName.StartsWith("/usr")) return;
			}
			else
			{
				// do not attempt to copy files to the "Program Files" directory
				var programFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
				if (!string.IsNullOrEmpty(programFolderPath))
				{
					var programsDirInfo = new DirectoryInfo(programFolderPath);
					if (String.Compare(targetDirInfo.FullName, programsDirInfo.FullName, StringComparison.InvariantCultureIgnoreCase) == 0) return;
				}

				// do not attempt to copy files to the "Program Files (x86)" directory either
				if (Environment.Is64BitOperatingSystem)
				{
					programFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
					if (!string.IsNullOrEmpty(programFolderPath))
					{
						var programsDirInfo = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
						if (String.Compare(targetDirInfo.FullName, programsDirInfo.FullName, StringComparison.InvariantCultureIgnoreCase) == 0) return;
					}
				}
			}

			string documentPath="notSet";
			try
			{
				if(string.IsNullOrEmpty(factoryPath))
				{
					factoryPath = _fileLocator.LocateFile(fileName);
				}
				if(string.IsNullOrEmpty(factoryPath))//happens during unit testing
					return;

				documentPath = Path.Combine(_folderPath, fileName);
				if(!RobustFile.Exists(documentPath))
				{
					Logger.WriteMinorEvent("BookStorage.Update() Copying missing file {0} to {1}", factoryPath, documentPath);

					// get rid of previous xmatter stylesheets
					if (fileName.ToLowerInvariant().Contains("xmatter"))
						RemoveExistingFilesBySuffix("XMatter.css");

					RobustFile.Copy(factoryPath, documentPath);
					return;
				}
				// due to BL-2166, we no longer compare times since downloaded books often have
				// more recent times than the DistFiles versions we want to use
				// var documentTime = RobustFile.GetLastWriteTimeUtc(documentPath);
				if (factoryPath == documentPath)
					return; // no point in trying to update self!
				if (IsPathReadonly(documentPath))
				{
					var msg = string.Format("Could not update one of the support files in this document ({0}) because the destination was marked ReadOnly.", documentPath);
					Logger.WriteEvent(msg);
					SIL.Reporting.ErrorReport.NotifyUserOfProblem(msg);
					return;
				}
				Logger.WriteMinorEvent("BookStorage.Update() Copying file {0} to {1}", factoryPath, documentPath);

				RobustFile.Copy(factoryPath, documentPath, true);
				//if the source was locked, don't copy the lock over
				RobustFile.SetAttributes(documentPath, FileAttributes.Normal);
			}
			catch (Exception e)
			{
				if(documentPath.Contains(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles))
					|| documentPath.ToLowerInvariant().Contains("program"))//english only
				{
					Logger.WriteEvent("Could not update file {0} because it was in the program directory.", documentPath);
					return;
				}
				if(_alreadyNotifiedAboutOneFailedCopy)
					return;//don't keep bugging them
				_alreadyNotifiedAboutOneFailedCopy = true;
				var msg = String.Format("Could not update one of the support files in this document ({0} to {1}).", documentPath, factoryPath);
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Can't Update Support File", msg, exception: e);
			}
		}

		private void RemoveExistingFilesBySuffix(string suffix)
		{
			foreach(var file in Directory.GetFiles(_folderPath, "*"+suffix))
			{
				try
				{
					RobustFile.Delete(file);
				}
				catch(Exception e)
				{
					// not worth bothering the user about, but we can log it in case it needs investigation
					Debug.Fail(e.Message);
					Logger.WriteError("Could not remove "+file, e);
				}
			}
		}

		/// <summary>
		/// user folder (or a temp folder, typically one where we've copied a user book to update for publishing)
		/// as opposed to our program installation folder or some template
		/// </summary>
		private bool IsUserOrTempFolder
		{
			get
			{
				if(_folderPath.Contains(Path.GetTempPath()))
					return true;
				if(string.IsNullOrEmpty(_collectionSettings.FolderPath))
				{
					//this happens when we are just hydrating the book via a command-line command
					return true;
				}
				else return _folderPath.Contains(_collectionSettings.FolderPath);
			}
		}

		// NB: this knows nothing of book-specific css's... even "basic book.css"
		private void EnsureHasLinksToStylesheets(HtmlDom dom)
		{
			//clear out any old ones
			Dom.RemoveXMatterStyleSheets();

			EnsureHasLinkToStyleSheet(dom, Path.GetFileName(PathToXMatterStylesheet));

			// Don't use Path.DirectorySeparatorChar here...we're going to use this path in an href
			// where it should definitely be forward slash. And it works fine in a Windows path too.
			string autocssFilePath = "../"+"settingsCollectionStyles.css";
			if (RobustFile.Exists(Path.Combine(_folderPath,autocssFilePath)))
				EnsureHasLinkToStyleSheet(dom, autocssFilePath);

			var customCssFilePath = "../" + "customCollectionStyles.css";
			if (RobustFile.Exists(Path.Combine(_folderPath, customCssFilePath)))
				EnsureHasLinkToStyleSheet(dom, customCssFilePath);

			if (RobustFile.Exists(Path.Combine(_folderPath, "customBookStyles.css")))
				EnsureHasLinkToStyleSheet(dom, "customBookStyles.css");
			else
				EnsureDoesntHaveLinkToStyleSheet(dom, "customBookStyles.css");
		}

		public string HandleRetiredXMatterPacks(HtmlDom dom, string nameOfXMatterPack)
		{
			var currentXmatterName = XMatterHelper.MigrateXMatterName(nameOfXMatterPack);

			if(currentXmatterName != nameOfXMatterPack)
			{
				const string xmatterSuffix = "-XMatter.css";
				EnsureDoesntHaveLinkToStyleSheet(dom, nameOfXMatterPack + xmatterSuffix);
				EnsureHasLinkToStyleSheet(dom, nameOfXMatterPack + xmatterSuffix);
				// Since HtmlDom.GetMetaValue() is always called with the collection's xmatter pack as default,
				// we can just remove this wrong meta element.
				dom.RemoveMetaElement("xmatter");
			}
			return nameOfXMatterPack;
		}

		private void EnsureDoesntHaveLinkToStyleSheet(HtmlDom dom, string path)
		{
			foreach (XmlElement link in dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				if (fileName == path)
					dom.RemoveStyleSheetIfFound(path);
			}
		}

		private void EnsureHasLinkToStyleSheet(HtmlDom dom, string path)
		{
			foreach (XmlElement link in dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				if (fileName == path)
					return;
				// We may also have an obsolete link with a Windows-specific path.
				if (fileName.Replace('\\', '/') == path)
				{
					// We want a link that will work on both platforms, so correct it.
					link.SetAttribute("href", path);
					return;
				}
			}
			dom.AddStyleSheet(path);
		}

//		/// <summary>
//		/// Creates a relative path from one file or folder to another.
//		/// </summary>
//		/// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
//		/// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
//		/// <param name="dontEscape">Boolean indicating whether to add uri safe escapes to the relative path</param>
//		/// <returns>The relative path from the start directory to the end path.</returns>
//		/// <exception cref="ArgumentNullException"></exception>
//		public static String MakeRelativePath(String fromPath, String toPath)
//		{
//			if (String.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
//			if (String.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");
//
//			//the stuff later on needs to see directory names trailed by a "/" or "\".
//			fromPath = fromPath.Trim();
//			if (!fromPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
//			{
//				if (Directory.Exists(fromPath))
//				{
//					fromPath = fromPath + Path.DirectorySeparatorChar;
//				}
//			}
//			Uri fromUri = new Uri(fromPath);
//			Uri toUri = new Uri(toPath);
//
//			Uri relativeUri = fromUri.MakeRelativeUri(toUri);
//			String relativePath = Uri.UnescapeDataString(relativeUri.ToString());
//
//			return relativePath.Replace('/', Path.DirectorySeparatorChar);
//		}

		//while in Bloom, we could have and edit style sheet or (someday) other modes. But when stored,
		//we want to make sure it's ready to be opened in a browser.
		private void MakeCssLinksAppropriateForStoredFile(HtmlDom dom)
		{
			dom.RemoveModeStyleSheets();
			dom.AddStyleSheet("previewMode.css");
			dom.AddStyleSheet("basePage.css");
			dom.AddStyleSheet("origami.css");
			dom.AddStyleSheet("languageDisplay.css");
			EnsureHasLinksToStylesheets(dom);
			dom.SortStyleSheetLinks();
			dom.RemoveFileProtocolFromStyleSheetLinks();
		}


		internal static string SanitizeNameForFileSystem(string name)
		{
			// First make sure it's not too long.
			const int MAX = 50;	//arbitrary
			if (name.Length > MAX)
				name = name.Substring(0, MAX);
			// Then replace invalid characters with spaces and trim off characters
			// that shouldn't start or finish a directory name.
			name = RemoveDangerousCharacters(name);
			if (name.Length == 0)
			{
				// The localized default book name could itself have dangerous characters.
				name = RemoveDangerousCharacters(BookStarter.UntitledBookName);
				if (name.Length == 0)
					name = "Book";	// This should absolutely never be needed, but let's be paranoid.
			}
			return name;
		}

		private static string RemoveDangerousCharacters(string name)
		{
			var dangerousCharacters = new List<char>();
			dangerousCharacters.AddRange(PathUtilities.GetInvalidOSIndependentFileNameChars());
			// NBSP also causes problems.  See https://issues.bloomlibrary.org/youtrack/issue/BL-5212.
			dangerousCharacters.Add('\u00a0');
			//dangerousCharacters.Add('.'); Moved this to a trim because SHRP uses names like "SHRP 2.3" (term 2, week 3)
			foreach (char c in dangerousCharacters)
			{
				name = name.Replace(c, ' ');
			}
			name = name.TrimStart(new [] {'.',' ','\t'});
			// Windows does not allow directory names ending in period.
			// If we give it a chance, it will make a directory without the dots,
			// but all our code that thinks the folder name has the dots will break (e.g., BL-
			name = name.TrimEnd(new[] {'.'});
			return name.Trim();
		}

		/// <summary>
		/// if necessary, append a number to make the folder path unique
		/// </summary>
		private static string GetUniqueFolderPath(string folderPath)
		{
			var parent = Directory.GetParent(folderPath).FullName;
			var name = GetUniqueFolderName(parent, Path.GetFileName(folderPath));
			return Path.Combine(parent, name);
		}

		/// <summary>
		/// if necessary, append a number to make the subfolder name unique within the given folder
		/// </summary>
		internal static string GetUniqueFolderName(string parentPath, string name)
		{
			int i = 0;
			string suffix = "";
			while (Directory.Exists(Path.Combine(parentPath, name + suffix)))
			{
				++i;
				suffix = i.ToString(CultureInfo.InvariantCulture);
			}
			return name + suffix;
		}

		/// <summary>
		/// if necessary, append a number to make the file name unique within the given folder
		/// </summary>
		internal static string GetUniqueFileName(string parentPath, string name, string ext)
		{
			int i = 0;
			string suffix = "";
			string result;
			do
			{
				result = Path.ChangeExtension(Path.Combine(parentPath, name + suffix), ext);
				++i;
				suffix = i.ToString(CultureInfo.InvariantCulture);
			} while (RobustFile.Exists(result));
			return result;
		}

		public string GetBrokenBookRecommendationHtml()
		{
			string s = "";
			if (!this._errorAlreadyContainsInstructions)
			{
				s = GenericBookProblemNotice;
			}
			return s + "<p>" + ErrorMessagesHtml + "</p>";
		}

		public static string GenericBookProblemNotice
		{
			get
			{
				return "<p>" + LocalizationManager.GetString("Errors.BookProblem",
					"Bloom had a problem showing this book. This doesn't mean your work is lost, but it does mean that something is out of date, is missing, or has gone wrong.")
				       + "</p>";
			}
		}

		//enchance: move to SIL.IO.RobustIO
		public static void CopyDirectory(string sourceDir, string targetDir)
		{
			Directory.CreateDirectory(targetDir);

			foreach (var file in Directory.GetFiles(sourceDir))
				RobustFile.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));

			foreach (var directory in Directory.GetDirectories(sourceDir))
				CopyDirectory(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
		}

		/// <summary>
		/// Copy the collection level style files to the given folder.
		/// </summary>
		public static void CopyCollectionStyles(string bookDir, string targetDir)
		{
			var collectionDir = Path.GetDirectoryName(bookDir);
			var settings = Path.Combine(collectionDir, "settingsCollectionStyles.css");
			if (File.Exists(settings))
				RobustFile.Copy(settings, Path.Combine(targetDir,"settingsCollectionStyles.css"));
			var custom = Path.Combine(collectionDir, "customCollectionStyles.css");
			if (File.Exists(custom))
				RobustFile.Copy(custom, Path.Combine(targetDir,"customCollectionStyles.css"));
		}
	}
}
