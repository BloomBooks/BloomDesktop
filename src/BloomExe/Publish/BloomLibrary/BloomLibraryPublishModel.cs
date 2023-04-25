using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Properties;
using BookInstance = Bloom.Book.Book;
using Bloom.WebLibraryIntegration;
using SIL.Windows.Forms.ClearShare;
using BloomTemp;
using System.IO;
using System.Diagnostics;
using Bloom.Utils;
using Bloom.web;
using SIL.IO;
using SIL.Progress;
using Bloom.Book;
using System.Globalization;
using Bloom.ImageProcessing;
using System.Drawing;

namespace Bloom.Publish.BloomLibrary
{
	/// <summary>
	/// Puts all of the business logic of whether a book's metadata is complete enough to publish and handling some of the login
	/// process in one place so that the regular single-book Publish tab upload and the command line bulk upload can use the
	/// same verification logic.
	/// </summary>
	public class BloomLibraryPublishModel
	{
		private LicenseInfo _license;
		private readonly BookUpload _uploader;
		private readonly PublishModel _publishModel;

		public BloomLibraryPublishModel(BookUpload uploader, BookInstance book, PublishModel model)
		{
			Book = book;
			InitializeLanguages();

			_uploader = uploader;
			_publishModel = model;

			var licenseMetadata = Book.GetLicenseMetadata();
			// This is usually redundant, but might not be on old books where the license was set before the new
			// editing code was written.
			Book.SetMetadata(licenseMetadata);
			_license = licenseMetadata.License;

			EnsureBookAndUploaderId();
		}

		internal BookInstance Book { get; }

		internal string LicenseRights => _license.RightsStatement??string.Empty;

		// ReSharper disable once InconsistentNaming
		internal string CCLicenseUrl => (_license as CreativeCommonsLicense)?.Url;

		internal string LicenseToken => _license.Token.ToUpperInvariant();

		internal string Credits => Book.BookInfo.Credits;

		internal string Title => Book.BookInfo.Title;

		internal string Copyright => Book.BookInfo.Copyright;

		internal bool HasOriginalCopyrightInfoInSourceCollection => Book.HasOriginalCopyrightInfoInSourceCollection;

		internal bool IsTemplate => Book.BookInfo.IsSuitableForMakingShells;

		internal string Summary
		{
			get { return Book.BookInfo.Summary; }
			set
			{
				Book.BookInfo.Summary = value;
				Book.BookInfo.Save();
			}
		}

		/// <summary>
		/// This is a difficult concept to implement. The current usage of this is in creating metadata indicating which languages
		/// the book contains. How are we to decide whether it contains enough of a particular language to be useful?
		/// Based on BL-2017, we now return a Dictionary of booleans indicating whether a language should be uploaded by default.
		/// The dictionary contains an entry for every language where the book contains non-x-matter text.
		/// The value is true if every non-x-matter field which contains text in any language contains text in this.
		/// </summary>
		internal Dictionary<string, bool> AllLanguages => Book.AllPublishableLanguages();

		/// <summary>
		/// Gets a user-friendly language name.
		/// </summary>
		internal string PrettyLanguageName(string code)
		{
			return Book.PrettyPrintLanguage(code);
		}

		// This is awkward. We really just want to always get the latest license info.
		// But Book.GetLicenseMetadata() is not a trivial operation.
		// The original code assumed that the book's license would not change during the lifetime of the model.
		// But now the user can open the CopyrightAndLicenseDialog from Publish tab.
		internal void EnsureUpToDateLicense()
		{
			_license = Book.GetLicenseMetadata().License;
		}

		/// <summary>
		/// Whether the most recent PDF generation succeeded.
		/// </summary>
		public bool PdfGenerationSucceeded
		{
			get { return _publishModel.PdfGenerationSucceeded; }
		}

		private void EnsureBookAndUploaderId()
		{
			if (string.IsNullOrEmpty(Book.BookInfo.Id))
			{
				Book.BookInfo.Id = Guid.NewGuid().ToString();
			}
			Book.BookInfo.Uploader = Uploader;
		}

		internal bool IsBookPublicDomain => _license?.Url != null && _license.Url.StartsWith("http://creativecommons.org/publicdomain/zero/");

		internal bool BookIsAlreadyOnServer => LoggedIn && _uploader.IsBookOnServer(Book.FolderPath);

		internal dynamic ConflictingBookInfo => _uploader.GetBookOnServer(Book.FolderPath);

		private string Uploader => _uploader.UserId;

		/// <summary>
		/// The model alone cannot determine whether a book is OK to upload, because the language requirements
		/// are different for single book upload and bulk upload (which use this same model).
		/// For bulk upload, a book is okay to upload if this property is true AND it has ANY language
		/// with complete data (meaning all non-xmatter fields have something in the language).
		/// For single book upload, a book is okay to upload if this property is true AND it is EITHER
		/// OkToUploadWithNoLanguages OR the user has checked a language checkbox.
		/// This property just determines whether the book's metadata is complete enough to publish
		/// LoggedIn is not part of this, because the two users of the model check for login status in different
		/// parts of the process.
		/// </summary>
		internal bool MetadataIsReadyToPublish =>
		    // Copyright info is not required if the book has been put in the public domain
			// Also, (BL-5563) if there is an original copyright and we're publishing from a source collection,
			// we don't need to have a copyright.
		    (IsBookPublicDomain || !string.IsNullOrWhiteSpace(Copyright) || HasOriginalCopyrightInfoInSourceCollection) &&
		    !string.IsNullOrWhiteSpace(Title);

		internal bool LoggedIn => _uploader.LoggedIn;

		/// <summary>
		/// Stored Web user Id
		/// </summary>
		///  Best not to store its own value, because the username/password can be changed if the user logs into a different account.
		internal string WebUserId { get { return Settings.Default.WebUserId; } }

		/// <summary>
		/// We would like users to be able to publish picture books that don't have any text.  Historically, we've required
		/// non-empty books with text unless the book is marked as being a template.  This restriction is too severe, so for
		/// now, we require either a template or a pure picture book.  (No text boxes apart from image description boxes on
		/// content pages.)  (See https://issues.bloomlibrary.org/youtrack/issue/BL-7514 for the initial user request, and
		/// https://issues.bloomlibrary.org/youtrack/issue/BL-7799 for why we made this property non-trivial.)
		/// </summary>
		internal bool OkToUploadWithNoLanguages => Book.BookInfo.IsSuitableForMakingShells || Book.HasOnlyPictureOnlyPages();

		internal bool IsThisVersionAllowedToUpload => _uploader.IsThisVersionAllowedToUpload();

		internal string UploadOneBook(BookInstance book, IProgress progress, PublishModel publishModel, bool excludeMusic, out string parseId)
		{
			using (var tempFolder = new TemporaryFolder(Path.Combine("BloomUpload", Path.GetFileName(book.FolderPath))))
			{
				BookUpload.PrepareBookForUpload(ref book, _publishModel.BookServer, tempFolder.FolderPath, progress);
				var bookParams = new BookUploadParameters
				{
					ExcludeMusic = excludeMusic,
					PreserveThumbnails = false,
				};
				return _uploader.FullUpload(book, progress, publishModel, bookParams, out parseId);
			}
		}

		internal void LogIn()
		{
			BloomLibraryAuthentication.LogIn();
		}

		internal void LogOut()
		{
			_uploader.Logout();
		}

		internal LicenseState LicenseType
		{
			get
			{
				if (_license is CreativeCommonsLicense)
				{
					return LicenseState.CreativeCommons;
				}
				if (_license is NullLicense)
				{
					return LicenseState.Null;
				}
				return LicenseState.Custom;
			}
		}

		/// <summary>
		/// Used by bulk uploader to tell the user why we aren't uploading their book.
		/// </summary>
		/// <returns></returns>
		public string GetReasonForNotUploadingBook()
		{
			const string couldNotUpload = "Could not upload book. ";
			// It might be because we're missing required metadata.
			if (!MetadataIsReadyToPublish)
			{
				if (string.IsNullOrWhiteSpace(Title))
				{
					return couldNotUpload + "Required book Title is empty.";
				}
				if (string.IsNullOrWhiteSpace(Copyright))
				{
					return couldNotUpload + "Required book Copyright is empty.";
				}
			}
			// Or it might be because a non-template book doesn't have any 'complete' languages.
			// every non-x - matter field which contains text in any language contains text in this
			return couldNotUpload + "A non-template book needs at least one language where every non-xmatter field contains text in that language.";
		}

		public void UpdateBookMetadataFeatures(bool isTalkingBook, bool isSignLanguage)
		{
			var allowedLanguages = Book.BookInfo.PublishSettings.BloomLibrary.TextLangs.IncludedLanguages()
				.Union(Book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages());

			Book.UpdateMetadataFeatures(
				isTalkingBookEnabled: isTalkingBook,
				isSignLanguageEnabled: isSignLanguage,
				allowedLanguages);
		}

		public void SaveTextLanguageSelection(string langCode, bool include)
		{
			Book.BookInfo.PublishSettings.BloomLibrary.TextLangs[langCode] = include ? InclusionSetting.Include : InclusionSetting.Exclude;
			Book.BookInfo.Save();   // We updated the BookInfo, so need to persist the changes. (but only the bookInfo is necessary, not the whole book)
		}

		public void SaveAudioLanguageSelection(bool include)
		{
			// Currently, audio language selection is all or nothing for Bloom Library publish
			foreach (var langCode in Book.BookInfo.PublishSettings.BloomLibrary.AudioLangs.Keys.ToList())
			{
				Book.BookInfo.PublishSettings.BloomLibrary.AudioLangs[langCode] = include ? InclusionSetting.Include : InclusionSetting.Exclude;
			}
			Book.BookInfo.Save();   // We updated the BookInfo, so need to persist the changes. (but only the bookInfo is necessary, not the whole book)
		}

		public void InitializeLanguages()
		{
			InitializeLanguages(Book);
		}

		public static void InitializeLanguages(BookInstance book)
		{
			var allLanguages = book.AllPublishableLanguages();

			var bookInfo = book.BookInfo;
			Debug.Assert(bookInfo?.MetaData != null, "Precondition: MetaData must not be null");

			// reinitialize our list of which languages to publish, defaulting to the ones that are complete.
			foreach (var kvp in allLanguages)
			{
				var langCode = kvp.Key;
				var isRequiredLang = book.IsRequiredLanguage(langCode);

				// First, check if the user has already explicitly set the value. If so, we'll just use that value and be done.
				if (bookInfo.PublishSettings.BloomLibrary.TextLangs.TryGetValue(langCode, out InclusionSetting checkboxValFromSettings))
				{
					if (checkboxValFromSettings.IsSpecified())
					{
						// ...unless the check box will be disabled and checked, in which case we better make sure it isn't Exclude
						if (isRequiredLang && checkboxValFromSettings == InclusionSetting.Exclude)
							bookInfo.PublishSettings.BloomLibrary.TextLangs[langCode] = InclusionSetting.IncludeByDefault;

						continue;
					}
				}

				// Nope, either no value exists or the value was some kind of default value.
				// Compute (or recompute) what the value should default to.
				bool shouldBeChecked = kvp.Value || isRequiredLang;

				var newInitialValue = shouldBeChecked ? InclusionSetting.IncludeByDefault : InclusionSetting.ExcludeByDefault;
				bookInfo.PublishSettings.BloomLibrary.TextLangs[langCode] = newInitialValue;
			}

			// Initialize the Talking Book Languages settings

			var allLangCodes = allLanguages.Select(x => x.Key);

			// This is tricky, because an earlier version of our UI did not support different settings for different languages.
			// Thus, an older version of this code only did this init the first time, when AudioLangs was null.
			// However, at least one book (BL-11784) got into a state where AudioLangs is an empty set.
			// Then there is NO item in the set to be switched on or off, so effectively, it remains off.
			// (The old IncludeAudio was true if at least one is Include or IncludeByDefault).
			// It is therefore necessary, minimally, to make sure the set has at least one language
			// if allLangCodes has any. But properly, it is intended to  have a value for every language in
			// allLangCodes...just in the old version they would all be the same.
			// It seems more future-proof to add any languages that are missing, while not changing the
			// setting for any we already have. But then, what setting should we use for any language
			// that is missing? To help preserve the old behavior, if all the old ones are exclude
			// we'll go with that, otherwise, include.
			var settingForNewLang = InclusionSetting.IncludeByDefault;
			if (bookInfo.PublishSettings.BloomLibrary.AudioLangs.Any() &&
			    bookInfo.PublishSettings.BloomLibrary.AudioLangs.All(kvp => !kvp.Value.IsIncluded()))
				settingForNewLang = InclusionSetting.ExcludeByDefault;

			foreach (var langCode in allLangCodes)
			{
				if (!bookInfo.PublishSettings.BloomLibrary.AudioLangs.ContainsKey(langCode))
					bookInfo.PublishSettings.BloomLibrary.AudioLangs[langCode] = settingForNewLang;
			}

			var collectionSignLangCode = book.CollectionSettings.SignLanguageTag;
			// User may have unset or modified the sign language for the collection in which case we need to exclude the old one it if it was previously included.
			foreach (var includedSignLangCode in bookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages().ToList())
			{
				if (includedSignLangCode != collectionSignLangCode)
				{
					bookInfo.PublishSettings.BloomLibrary.SignLangs[includedSignLangCode] = InclusionSetting.ExcludeByDefault;
				}
			}
			// Include the collection sign language by default unless the user set it definitely.
			if (!string.IsNullOrEmpty(collectionSignLangCode))
			{
				if (!bookInfo.PublishSettings.BloomLibrary.SignLangs.ContainsKey(collectionSignLangCode) ||
					bookInfo.PublishSettings.BloomLibrary.SignLangs[collectionSignLangCode] == InclusionSetting.ExcludeByDefault)
				{
					bookInfo.PublishSettings.BloomLibrary.SignLangs[collectionSignLangCode] = InclusionSetting.IncludeByDefault;
				}
			}

			// The metadata may have been changed, so save it.
			bookInfo.Save();
		}

		public void ClearSignLanguageToPublish()
		{
			foreach (var includedSignLangCode in Book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages().ToList()) {
				Book.BookInfo.PublishSettings.BloomLibrary.SignLangs[includedSignLangCode] = InclusionSetting.Exclude;
			}
			Book.BookInfo.Save();
		}

		public void SetOnlySignLanguageToPublish(string langCode)
		{
			Book.BookInfo.PublishSettings.BloomLibrary.SignLangs = new Dictionary<string, InclusionSetting>
			{
				[langCode] = InclusionSetting.Include
			};
			Book.BookInfo.Save();
		}

		public bool IsPublishSignLanguage()
		{
			return Book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages().Any();
		}

		public bool L1SupportsVisuallyImpaired
		{
			get
			{
				return Book.BookInfo.MetaData.Feature_Blind_LangCodes.Contains(Book.Language1Tag);
			}
			set
			{
				Book.UpdateBlindFeature(value);
				Book.BookInfo.Save();
			}
		}

		public string CheckBookBeforeUpload()
		{
			return new LicenseChecker().CheckBook(Book, TextLanguagesToUpload.ToArray());
		}

		public IEnumerable<string> TextLanguagesToUpload => Book.BookInfo.PublishSettings.BloomLibrary.TextLangs
			.Where(l => l.Value.IsIncluded())
			.Select(l => l.Key);

		public void AddHistoryRecordForLibraryUpload(string url)
		{
			Book.AddHistoryRecordForLibraryUpload(url);
		}

		public void BulkUpload(string rootFolderPath, IProgress progress)
		{
			var target = BookUpload.UseSandbox ? UploadDestination.Development : UploadDestination.Production;

			var bloomExePath = Program.BloomExePath;
			var command = $"\"{bloomExePath}\" upload \"{rootFolderPath}\" -u {WebUserId} -d {target}";
			if (SIL.PlatformUtilities.Platform.IsLinux)
				command = $"/opt/mono5-sil/bin/mono {command}";

			ProcessStartInfo startInfo;
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				startInfo = new ProcessStartInfo()
				{
					FileName = "cmd.exe",
					Arguments = $"/k {MiscUtils.EscapeForCmd(command)}",

					WorkingDirectory = Path.GetDirectoryName(bloomExePath)
				};
			}
			else
			{
				string program = GetLinuxTerminalProgramAndAdjustCommand(ref command);
				if (String.IsNullOrEmpty(program))
				{
					progress.WriteMessage("Cannot bulk upload because unable to find terminal window for output messages.");
					return;
				}
				startInfo = new ProcessStartInfo()
				{
					FileName = program,
					Arguments = command,
					WorkingDirectory = Path.GetDirectoryName(bloomExePath)
				};
				// LD_PRELOAD is a Linux environment variable for a shared library that should be loaded before any other library is
				// loaded by a program that is starting up.  It is rarely needed, but the mozilla code used by Geckofx is one place
				// where this feature is used, specifically to load a xulrunner patch (libgeckofix.so) that must be in place before
				// xulrunner can be initialized for GeckoFx60 on Linux.  This must be in place in the environment before launching
				// any process (such as Bloom, here) that will initialize xulrunner, but may cause problems for other programs so
				// it is best not to have it in the environment unless we know it is needed.  In particular having LD_PRELOAD set to
				// load libgeckofix.so is known to cause problems when running some programs (possibly only BloomPdfMaker.exe) using
				// CommandLineRunner.  To guard against this Program.Main() removes it from the environment, but here we need to
				// temporarily restore it so it can be inherited by the instance of Bloom we are about to launch. Fortunately, it's
				// easy to reconstruct.
				var xulRunner = Environment.GetEnvironmentVariable("XULRUNNER");
				if (!String.IsNullOrEmpty("xulRunner"))
					Environment.SetEnvironmentVariable("LD_PRELOAD", $"{xulRunner}/libgeckofix.so");
			}

			Process.Start(startInfo);
			progress.WriteMessage("Starting bulk upload in a terminal window...");
			progress.WriteMessage("This process will skip books if it can tell that nothing has changed since the last bulk upload.");
			progress.WriteMessage("When the upload is complete, there will be a file named 'BloomBulkUploadLog.txt' in your collection folder.");
			var url = $"{BloomLibraryUrls.BloomLibraryUrlPrefix}/{Book.CollectionSettings.DefaultBookshelf}";
			progress.WriteMessage("Your books will show up at {0}", url);
			if (SIL.PlatformUtilities.Platform.IsLinux) // LD_PRELOAD interferes with CommandLineRunner and GeckoFx60 on Linux
				Environment.SetEnvironmentVariable("LD_PRELOAD", null);
		}

		private string QuoteQuotes(string command)
		{
			return command.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		private string GetLinuxTerminalProgramAndAdjustCommand(ref string command)
		{
			// See https://askubuntu.com/questions/484993/run-command-on-anothernew-terminal-window

			if (RobustFile.Exists("/usr/bin/gnome-terminal"))   // standard for GNOME (Ubuntu/Wasta)
			{
				// /usr/bin/gnome-terminal -- /bin/bash -c "bloom upload \"folder\" -u user -d dest; read line"
				command = $"-- /bin/bash -c \"{QuoteQuotes(command)}; read line\"";
				return "/usr/bin/gnome-terminal";
			}
			if (RobustFile.Exists("/usr/bin/terminator")) // popular alternative
			{
				// /usr/bin/terminator -x /bin/bash -c "bloom upload \"folder\" -u user -d dest; read line"
				command = $"-x /bin/bash -c \"{QuoteQuotes(command)}; read line\"";
				return "/usr/bin/terminator";
			}
			if (RobustFile.Exists("/usr/bin/xfce4-terminal"))    // standard for XFCE4 (XUbuntu)
			{
				// /usr/bin/xterm -hold -x /bin/bash -c "bloom upload \"folder\" -u user -d dest"
				command = $"-T \"Bloom upload\" --hold -x /bin/bash -c \"{QuoteQuotes(command)}\"";
				return "/usr/bin/xfce4-terminal";
			}
			if (RobustFile.Exists("/usr/bin/xterm"))    // antique original (slightly better than nothing)
			{
				// /usr/bin/xterm -hold -x /bin/bash -c "bloom upload \"folder\" -u user -d dest"
				command = $"-T \"Bloom upload\" -hold -e /bin/bash -c \"{QuoteQuotes(command)}\"";
				return "/usr/bin/xterm";
			}
			// Neither konsole nor qterminal will launch with Bloom.  The ones above have been tested on Wasta 20.
			// symbol lookup error: /usr/lib/x86_64-linux-gnu/qt5/plugins/styles/libqgtk2style.so: undefined symbol: gtk_combo_box_entry_new
			// I suspect because they're still linking with GTK2 while Bloom has to use GTK3 with Geckofx60.

			// Give up.
			return null;
		}

		public dynamic GetUploadCollisionDialogProps(IEnumerable<string> languagesToUpload, bool signLanguageFeatureSelected)
		{
			var newThumbPath = ChooseBestUploadingThumbnailPath(Book).ToLocalhost();
			var newTitle = Book.TitleBestForUserDisplay;
			var newLanguages = ConvertLanguageCodesToNames(languagesToUpload, Book.BookData);
			if (signLanguageFeatureSelected && !string.IsNullOrEmpty(CurrentSignLanguageName))
			{
				var newLangs = newLanguages.ToList();
				if (!newLangs.Contains(CurrentSignLanguageName))
					newLangs.Add(CurrentSignLanguageName);
				newLanguages = newLangs;
			}

			var existingBookInfo = ConflictingBookInfo;
			var updatedDateTime = (DateTime)existingBookInfo.updatedAt;
			var createdDateTime = (DateTime)existingBookInfo.createdAt;
			// Find the best title available (BL-11027)
			// Users can click on this title to bring up the existing book's page.
			var existingTitle = existingBookInfo.title?.Value;
			if (String.IsNullOrEmpty(existingTitle))
			{
				// If title is undefined (which should not be the case), then use the first title from allTitles.
				var allTitlesString = existingBookInfo.allTitles?.Value;
				if (!String.IsNullOrEmpty(allTitlesString))
				{
					try
					{
						var allTitles = Newtonsoft.Json.Linq.JObject.Parse(allTitlesString);
						foreach (var title in allTitles)
						{
							// title.Value is dynamic language code / title string pair
							// title.Value.Value is the actual book title in the associated language
							if (title?.Value?.Value != null)
							{
								existingTitle = title.Value.Value;
								break;
							}
						}
					}
					catch
					{
						// ignore parse failure -- should never happen at this point.
					}
				}
			}
			// If neither title nor allTitles are defined, just give a placeholder value.
			if (String.IsNullOrEmpty(existingTitle))
				existingTitle = "Unknown";
			var existingId = existingBookInfo.objectId.ToString();
			var existingBookUrl = BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(existingId);

			var existingLanguages = ConvertLanguagePointerObjectsToNames(existingBookInfo.langPointers);
			var createdDate = createdDateTime.ToString("d", CultureInfo.CurrentCulture);
			var updatedDate = updatedDateTime.ToString("d", CultureInfo.CurrentCulture);
			var existingThumbUrl = GetBloomLibraryThumbnailUrl(existingBookInfo);

			// Must match IUploadCollisionDlgProps in uploadCollisionDlg.tsx.
			return new
			{
				shouldShow = BookIsAlreadyOnServer,
				userEmail = LoggedIn ? WebUserId : "",
				newThumbUrl = newThumbPath,
				newTitle,
				newLanguages,
				existingTitle,
				existingLanguages,
				existingCreatedDate = createdDate,
				existingUpdatedDate = updatedDate,
				existingBookUrl,
				existingThumbUrl
			};
		}

		// We are trying our best to end up with a thumbnail whose height/width ratio
		// is the same as the original image. This allows the Uploading and Already in Bloom Library
		// thumbs to top-align.
		private string ChooseBestUploadingThumbnailPath(Book.Book book)
		{
			// If this exists, it will have the original image's ratio of height to width.
			var thumb70Path = Path.Combine(book.FolderPath, "thumbnail-70.png");
			if (RobustFile.Exists(thumb70Path))
				return thumb70Path;
			var coverImagePath = book.GetCoverImagePath();
			if (coverImagePath == null)
			{
				return book.ThumbnailPath;
			}
			else
			{
				RuntimeImageProcessor.GenerateThumbnail(book.GetCoverImagePath(),
					book.NonPaddedThumbnailPath, 70, ColorTranslator.FromHtml(book.GetCoverColor()));
				return book.NonPaddedThumbnailPath;

			}
		}
		private static string GetBloomLibraryThumbnailUrl(dynamic existingBookInfo)
		{
			// Code basically copied from bloomlibrary2 Book.ts
			var baseUrl = existingBookInfo.baseUrl.ToString();
			var harvestState = existingBookInfo.harvestState.ToString();
			var updatedTime = existingBookInfo.updatedAt.ToString();
			var harvesterThumbnailUrl = GetHarvesterThumbnailUrl(baseUrl, harvestState, updatedTime);
			if (harvesterThumbnailUrl != null)
			{
				return harvesterThumbnailUrl;
			}
			// Try "legacy" version (the one uploaded with the book)
			return CreateThumbnailUrlFromBase(baseUrl, updatedTime);
		}

		// Code modified from bloomlibrary2 Book.ts.
		// Bloomlibrary2 code still (as of 11/11/2021) checks the harvest date to see if the thumbnail is useful,
		// But now there are no books in circulation on bloomlibrary that haven't been harvested since that date.
		// So now we can consider any harvester thumbnail as valid, as long as the harvestState is "Done".
		private static string GetHarvesterThumbnailUrl(string baseUrl, string harvestState, string lastUpdate)
		{
			if (harvestState != "Done")
				return null;

			var harvesterBaseUrl = GetHarvesterBaseUrl(baseUrl);
			return CreateThumbnailUrlFromBase(harvesterBaseUrl, lastUpdate);
		}

		private static string CreateThumbnailUrlFromBase(string baseUrl, string lastUpdate)
		{
			// The bloomlibrary2 code calls Book.getCloudFlareUrl(), but it just passes the parameter through
			// at this point with a bunch of comments on why we don't do anything there.
			return baseUrl + "thumbnails/thumbnail-256.png?version=" + lastUpdate;
		}

		// Code basically copied from bloomlibrary2 Book.ts
		private static string GetHarvesterBaseUrl(string baseUrl)
		{
			const string slash = "%2f";
			var folderWithoutLastSlash = baseUrl;
			if (baseUrl.EndsWith(slash))
			{
				folderWithoutLastSlash = baseUrl.Substring(0, baseUrl.Length - 3);
			}

			var index = folderWithoutLastSlash.LastIndexOf(slash, StringComparison.InvariantCulture);
			var pathWithoutBookName = folderWithoutLastSlash.Substring(0, index);
			return pathWithoutBookName.Replace("BloomLibraryBooks-Sandbox", "bloomharvest-sandbox")
					   .Replace("BloomLibraryBooks", "bloomharvest") + "/";
		}

		private IEnumerable<string> ConvertLanguageCodesToNames(IEnumerable<string> languageCodesToUpload, BookData bookData)
		{
			foreach (var langCode in languageCodesToUpload)
			{
				yield return bookData.GetDisplayNameForLanguage(langCode);
			}
		}

		private IEnumerable<string> ConvertLanguagePointerObjectsToNames(IEnumerable<dynamic> langPointers)
		{
			foreach (var languageObject in langPointers)
			{
				yield return languageObject.name;
			}
		}

		internal void ChangeBookId(IProgress progress)
		{
			progress.WriteMessage("Setting new instance ID...");
			Book.BookInfo.Id = BookInfo.InstallFreshInstanceGuid(Book.FolderPath);
			progress.WriteMessage("ID is now " + Book.BookInfo.Id);
		}

		internal void UpdateLangDataCache()
		{
			Book.BookData.UpdateCache();
		}

		private string CurrentSignLanguageName
		{
			get
			{
				return Book.CollectionSettings.SignLanguage.Name;
			}
		}
	}

	internal enum LicenseState
	{
		Null,
		CreativeCommons,
		Custom
	}
}
