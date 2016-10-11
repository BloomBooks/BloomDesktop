using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.ImageProcessing;
using System.Text.RegularExpressions;
using Bloom.Edit;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;

namespace Bloom.Book
{
	/// <summary>
	/// A BookInfo has everything needed to display a title and thumbnail, and (eventually) do common searching filtering operations, without accessing the actual contents of the book.
	/// A related responsibility is to wrap the meta.json file which stores this search/display data in a form suitable for uploading to our web server.
	/// </summary>
	public class BookInfo
	{
		private BookMetaData _metadata;

		private BookMetaData MetaData
		{
			get { return _metadata ?? (_metadata = new BookMetaData()); }
		}

		//for use by ErrorBookInfo
		protected BookInfo()
		{
			
		}
		public BookInfo(string folderPath, bool isEditable)
		{
			FolderPath = folderPath;

			//NB: This was coded in an unfortunate way such that touching almost any property causes a new metadata to be quietly created.
			//So It's vital that we not touch properties that could create a blank metadata, before attempting to load the existing one.
			
			var jsonPath = MetaDataPath;
			if (RobustFile.Exists(jsonPath))
			{
				_metadata = BookMetaData.FromString(RobustFile.ReadAllText(jsonPath)); // Enhance: error handling?
			}
			else
			{
				// Look for old tags files not yet migrated
				var oldTagsPath = Path.Combine(folderPath, "tags.txt");
				if (RobustFile.Exists(oldTagsPath))
				{
					Book.ConvertTagsToMetaData(oldTagsPath, this);
				}
			}

			//TODO
			Type = Book.BookType.Publication;
			IsEditable = isEditable;

			FixDefaultsIfAppropriate();
		}

		public string Id
		{
			get { return MetaData.Id; }
			set { MetaData.Id = value; }
		}

		public string FolderPath { get; set; }

		public bool AllowUploading
		{
			get { return MetaData.AllowUploadingToBloomLibrary; }
			set { MetaData.AllowUploadingToBloomLibrary = value; }
		}

		//there was a beta version that would introduce the .json files with the incorrect defaults
		//we don't have a good way of differentiating when these defaults were set automatically
		//vs. when someone actually set them to false. So this method is only used if a certain
		//environment variable is set, so that our librarian (who ran into this) can fix her
		//affected collections.
		public void FixDefaultsIfAppropriate()
		{
			if (Environment.GetEnvironmentVariable("FixBloomMetaInfo") != "true")
				return;
			MetaData.AllowUploadingToBloomLibrary = true;
			MetaData.BookletMakingIsAppropriate = true;
		}

		public bool BookletMakingIsAppropriate
		{
			get
			{
				return MetaData.BookletMakingIsAppropriate;
			}
		}

		public bool IsSuitableForMakingShells
		{
			get { return MetaData.IsSuitableForMakingShells; }
			set { MetaData.IsSuitableForMakingShells = value; }
		}

		public bool IsSuitableForVernacularLibrary
		{
			get { return MetaData.IsSuitableForVernacularLibrary; }
			set { MetaData.IsSuitableForVernacularLibrary = value; }
		}

		//SeeAlso: commented IsExperimental on Book
		public bool IsExperimental
		{
			get { return MetaData.IsExperimental; }
			set { MetaData.IsExperimental = value; }
		}

		/// <summary>
		/// A "Folio" document is one that acts as a wrapper for a number of other books
		/// </summary>
		public bool IsFolio
		{
			get { return MetaData.IsFolio; }
			set { MetaData.IsFolio = value; }
		}

		// Todo: multilingual
		public string Title
		{
			get { return MetaData.Title; }
			set
			{
				var titleStr = Book.RemoveXmlMarkup(value);
				MetaData.Title = titleStr;
			}
		}

		/// <summary>
		/// A possibly-temporary expedient to get multilingual title data into the json, and thus into parse.com
		/// This stores a Json string representing lang:title pairs, e.g.,
		/// {"en":"my nice title","de":"Mein schönen Titel","es":"мy buen título"}.
		/// </summary>
		public string AllTitles
		{
			get { return MetaData.AllTitles; }
			set { MetaData.AllTitles = value; }
		}

		// Todo: this is currently not used. It is intended to be filled in when we upload the json.
		// Not sure what it needs to be. Locally the thumbnail is always called just thumbnail.png.
		// What we upload needs to be a functional URL (probably relative to our site root).
		public string Thumbnail
		{
			get { return MetaData.BaseUrl; }
			set { MetaData.BaseUrl = value; }
		}

		public string Isbn
		{
			get { return MetaData.Isbn; }
			set { MetaData.Isbn = value; }
		}

		public string BookLineage
		{
			get { return MetaData.BookLineage; }
			set { MetaData.BookLineage = value; }
		}

		// This indicates the kind of license in use. For Creative Commons licenses, it is the Abbreviation of the CreativeCommonsLicense
		// object, the second-last (before version number) element of the licenseUrl. Other known values are 'ask' (no license granted,
		// ask the copyright holder for permission to use) 'custom' (rights presumably specified in licenseNotes)
		// Review: would it help with filtering if this field contained some indication of whether licenseNotes contains anything
		// (e.g., so we can search for CC licenses with no non-standard encumbrance)?
		public string License
		{
			get { return MetaData.License; }
			set { MetaData.License = value; }
		}

		public string FormatVersion
		{
			get { return MetaData.FormatVersion; }
			set { MetaData.FormatVersion = value; }
		}

		// When license is 'custom' this contains the license information. For other types in may contain additional permissions
		// (or possibly restrictions).
		// Review: do we need this, or just a field indicating whether there ARE additional notes, or just some modifier in license indicating that?
		public string LicenseNotes
		{
			get { return MetaData.LicenseNotes; }
			set { MetaData.LicenseNotes = value; }
		}

		public string Copyright
		{
			get { return MetaData.Copyright; }
			set { MetaData.Copyright = value; }
		}

		public bool IsEditable { get; private set; }

		public Book.BookType Type { get; set; }

		/// <summary>
		/// This one knows nothing of what language the user speaks... currently using that requires actually reading in the html, which is beyond what this class can do
		/// </summary>
		public string QuickTitleUserDisplay
		{
			get { return Path.GetFileName(FolderPath); }
		}

		public bool TryGetPremadeThumbnail(out Image image)
		{
			string path = Path.Combine(FolderPath, "thumbnail.png");
			if (RobustFile.Exists(path))
			{
				try
				{
					image = ImageUtils.GetImageFromFile(path);
					return true;
				}
				catch(Exception e) // If that file became corrupted, we would not want to lock user out of their book.
				{
					NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All,"Could not read thumbnail.png", "Could not read thumbnail.png at "+FolderPath);
					//The file will be re-generate now.
				}
			}
			image = null;
			return false;
		}

		public void Save()
		{
			// https://jira.sil.org/browse/BL-354 "The requested operation cannot be performed on a file with a user-mapped section open"
			var count = 0;

			do
			{
				try
				{
					RobustFile.WriteAllText(MetaDataPath, MetaData.Json);
					return;
				}
				catch (IOException e)
				{
					Thread.Sleep(500);
					count++;

					// stop trying after 5 attempts to save the file.
					if (count > 4)
					{
						Debug.Fail("Reproduction of BL-354 that we have taken steps to avoid");

						var msg = LocalizationManager.GetDynamicString("Bloom", "BookEditor.ErrorSavingPage", "Bloom wasn't able to save the changes to the page.");
						ErrorReport.NotifyUserOfProblem(e, msg);
					}
				}

			} while (count < 5);
		}

		internal string MetaDataPath
		{
			get { return Path.Combine(FolderPath, MetaDataFileName); }
		}

		public const string MetaDataFileName = "meta.json";

		public string Credits
		{
			get { return MetaData.Credits; }
			set { MetaData.Credits = value; }
		}

		public string Summary
		{
			get { return MetaData.Summary; }
			set { MetaData.Summary = value; }
		}


		string[] SplitList(string list)
		{
			if (list == null)
			{
				return new string[0];
			}
			return list.Split(',').Select(item => item.Trim()).Where(item => !string.IsNullOrEmpty(item)).ToArray();
		}

		public string TagsList
		{
			get { return MetaData.Tags == null ? "" : string.Join(", ", MetaData.Tags); }
			set
			{
				MetaData.Tags = SplitList(value);
			}
		}

		public int PageCount
		{
			get { return MetaData.PageCount; }
			set { MetaData.PageCount = value; }
		}

		/// <summary>
		/// So far, this is just a way of getting at the metadata field. It is only set during book upload.
		/// </summary>
		public ParseDotComObjectPointer[] LanguageTableReferences
		{
			get { return MetaData.LanguageTableReferences; }
			set { MetaData.LanguageTableReferences = value; }
		}

		/// <summary>
		/// The Parse.com object ID of the person who uploaded the book.
		/// </summary>
		public string Uploader
		{
			get
			{
				if (MetaData.Uploader == null)
					return "";
				return MetaData.Uploader.ObjectId;
			}

			set
			{
				MetaData.SetUploader(value);
			}
		}

		public List<ToolboxTool> Tools
		{
			get
			{
				if (MetaData.Tools == null)
					MetaData.Tools = new List<ToolboxTool>();
				return MetaData.Tools;
			}
			set { MetaData.Tools = value; }
		}

		public string CurrentTool
		{
			get { return MetaData.CurrentTool; }
			set { MetaData.CurrentTool = value; }
		}

		// Whether we should allow the reader tools initially. (Was, whether to show at all. As of BL-2907, they are always an option).
		public bool ToolboxIsOpen
		{
			get { return MetaData.ToolboxIsOpen; }
			set { MetaData.ToolboxIsOpen = value; }
		}

		public static IEnumerable<string> TopicsKeys
		{
			get
			{
				//If you modify any of these, consider modifying/updating the localization files; the localization ids for these are just the current English (which is fragile)
				//If you make changes/additions here, also synchronize with the bloomlibrary source in services.js

				return new[] { "Agriculture", "Animal Stories", "Business", "Culture", "Community Living", "Dictionary", "Environment", "Fiction", "Health", "How To", "Math", "Non Fiction", "Spiritual", "Personal Development", "Primer", "Science", "Story Book", "Traditional Story" };
			}
		}

		public void SetLicenseAndCopyrightMetadata(Metadata metadata)
		{
			License = metadata.License.Token;
			Copyright = metadata.CopyrightNotice;
			// obfuscate any emails in the license notes.
			var notes = metadata.License.RightsStatement;
			if (notes != null)
			{
				// recommended at http://www.regular-expressions.info/email.html.
				// This purposely does not handle non-ascii emails, or ones with special characters, which he says few servers will handle anyway.
				// It is also not picky about exactly valid top-level domains (or country codes), and will exclude the rare 'museum' top-level domain.
				// There are several more complex options we could use there. Just be sure to add () around the bit up to (and including) the @,
				// and another pair around the rest.
				var regex = new Regex("\\b([A-Z0-9._%+-]+@)([A-Z0-9.-]+.[A-Z]{2,4})\\b", RegexOptions.IgnoreCase);
				// We keep the existing email up to 2 characters after the @, and replace the rest with a message.
				// Not making the message localizable as yet, since the web site isn't, and I'm not sure what we would need
				// to put to make it so. A fixed string seems more likely to be something we can replace with a localized version,
				// in the language of the web site user rather than the language of the uploader.
				notes = regex.Replace(notes,
					new MatchEvaluator(
						m =>
							m.Groups[1].Value + m.Groups[2].Value.Substring(0, 2) +
							"(download book to read full email address)"));
				LicenseNotes = notes;
			}
		}
	}

	public class ErrorBookInfo : BookInfo
	{
		public ErrorBookInfo(string folderPath, Exception exception) //No: our known-bad contents could crash that: base(folderPath,false)
		{
			FolderPath = folderPath;
			Exception = exception;
		}

		public Exception Exception { get; set; }
	}

	/// <summary>
	/// This just wraps the stuff we put in the json file.
	/// It is tempting to just serialize/deserialize the BookInfo itself.
	/// However, that would require us to refactor all the code that creates BookInfos, since
	/// it expects to use a constructor taking a pathname, while the Json code expects to
	/// create the object for us out of the pathname.
	/// Also, separating them like this means we don't have to be careful to mark things we don't want in the json.
	/// </summary>
	internal class BookMetaData
	{
		public BookMetaData()
		{
			IsExperimental = false;
			AllowUploadingToBloomLibrary = true;
			BookletMakingIsAppropriate = true;
			IsSuitableForVernacularLibrary = true;
			Id = Guid.NewGuid().ToString();
		}
		public static BookMetaData FromString(string input)
		{
			var result = JsonConvert.DeserializeObject<BookMetaData>(input);
			if(result == null)
			{
				throw new ApplicationException("meta.json of this book may be corrupt");
			}
			if (result.Tools != null)
			{
				foreach (var tool in result.Tools.Where(t => t is UnknownTool).ToArray())
					result.Tools.Remove(tool);
			}
			return result;
		}

		public static BookMetaData FromFolder(string bookFolderPath)
		{
			return FromString(RobustFile.ReadAllText(MetaDataPath(bookFolderPath)));
		}

		public static string MetaDataPath(string bookFolderPath)
		{
			return bookFolderPath.CombineForPath(BookInfo.MetaDataFileName);
		}

		public void WriteToFolder(string bookFolderPath)
		{
			RobustFile.WriteAllText(MetaDataPath(bookFolderPath), Json);
		}

		[JsonIgnore]
		public string Json
		{
			get
			{
				return JsonConvert.SerializeObject(this);
			}
		}

		/// <summary>
		/// Get the reduced Json string that we upload to set the database entry for the book on our website.
		/// This leaves out some of the metadata that we use while working on the book.
		/// Note that the full metadata is currently uploaded to S3 as part of the book content;
		/// this reduced subset is just for the website itself.
		/// Note that if you add a property to the upload set here, you must manually add a corresponding field to
		/// the Book table in Parse.com. This is very important. Currently, the field will auto-add to
		/// the Parse databases used for unit testing and even (I think) the one for sandbox testing,
		/// but not to the live site; so if you forget to do this uploading will suddenly break.
		/// It is for this reason that we deliberately don't automatically add new fields to the upload set.
		/// Note that it is desirable that the name you give each property in the anonymous object which
		/// get jsonified here matches the JsonProperty name used to deserialize it.
		/// That allows the WebDataJson to be a valid Json representation of this class with just
		/// some fields left out. At least one unit test will fail if the names don't match.
		/// (Though, I don't think anything besides that test currently attempts to create
		/// a BookMetaData object from a WebDataJson string.)
		/// It is of course vital that the names in the anonymous object match the fields in parse.com.
		/// </summary>
		[JsonIgnore]
		public string WebDataJson
		{
			get
			{
				return JsonConvert.SerializeObject(
					new
					{
						bookInstanceId = Id, // our master key; worth uploading though BloomLibrary doesn't use directly.
						suitableForMakingShells = IsSuitableForMakingShells, // not yet used by BL, potentially useful filter
						suitableForVernacularLibrary = IsSuitableForVernacularLibrary,  // not yet used by BL, potentially useful filter
						experimental = IsExperimental,  // not yet used by BL (I think), potentially useful filter
						title = Title,
						allTitles = AllTitles, // created for BL to search, though it doesn't yet.
						baseUrl = BaseUrl, // how web site finds image and download
						bookOrder = BookOrder, // maybe obsolete? Keep uploading until sure.
						isbn = Isbn,
						bookLineage = BookLineage,
						//downloadSource = DownloadSource, // seems to be obsolete
						license = License,
						formatVersion = FormatVersion,
						licenseNotes = LicenseNotes,
						copyright = Copyright,
						credits = Credits,
						tags = Tags,
						summary = Summary,
						pageCount = PageCount,
						langPointers = LanguageTableReferences,
						uploader = Uploader
						// Other fields are not needed by the web site and we don't expect they will be.
					});
			}
		}

		[JsonProperty("bookInstanceId")]
		public string Id { get; set; }

		[JsonProperty("suitableForMakingShells")]
		public bool IsSuitableForMakingShells { get; set; }

		[JsonProperty("suitableForVernacularLibrary")]
		public bool IsSuitableForVernacularLibrary { get; set; }

		//SeeAlso: commeted IsExperimental on Book
		[JsonProperty("experimental")]
		public bool IsExperimental { get; set; }

		/// <summary>
		/// A "Folio" document is one that acts as a wrapper for a number of other books
		/// </summary>
		[JsonProperty("folio")]
		public bool IsFolio { get; set; }

		// Enhance: multilingual?
		// BL-3774 was caused by a book with a meta.json value for Title of null.
		// So here let's just ensure we have store strings in that situation.
		private string _title = string.Empty;
		[JsonProperty("title")]
		public string Title {
			get { return _title; }
			set { _title = value == null ? string.Empty : value; }
		}

		[JsonProperty("allTitles")]
		public string AllTitles { get; set; }

		// This is filled in when we upload the json. It is not used locally, but becomes a field on parse.com
		// containing the actual url where we can grab the thumbnails, pdfs, etc.
		[JsonProperty("baseUrl")]
		public string BaseUrl { get; set; }

		// This is filled in when we upload the json. It is not used locally, but becomes a field on parse.com
		// containing the actual url where we can grab the book order file which when opened by Bloom causes it
		// to download the book.
		[JsonProperty("bookOrder")]
		public string BookOrder { get; set; }

		[JsonProperty("isbn")]
		public string Isbn { get; set; }

		[JsonProperty("bookLineage")]
		public string BookLineage { get; set; }

		// This tells Bloom where the data files can be found.
		// Strictly it is the first argument that needs to be passed to BookTransfer.DownloadBook in order to get the entire book data.
		[JsonProperty("downloadSource")]
		public string DownloadSource { get; set; }

		// This indicates the kind of license in use. For Creative Commons licenses, it is the Abbreviation of the CreativeCommonsLicense
		// object, the second-last (before version number) element of the licenseUrl. Other known values are 'ask' (no license granted,
		// ask the copyright holder for permission to use) 'custom' (rights presumably specified in licenseNotes)
		// Review: would it help with filtering if this field contained some indication of whether licenseNotes contains anything
		// (e.g., so we can search for CC licenses with no non-standard encumbrance)?
		[JsonProperty("license")]
		public string License { get; set; }

		[JsonProperty("formatVersion")]
		public string FormatVersion { get; set; }

		// When license is 'custom' this contains the license information. For other types in may contain additional permissions
		// (or possibly restrictions).
		// Review: do we need this, or just a field indicating whether there ARE additional notes, or just some modifier in license indicating that?
		[JsonProperty("licenseNotes")]
		public string LicenseNotes { get; set; }

		[JsonProperty("copyright")]
		public string Copyright { get; set; }

		[JsonProperty("credits")]
		public string Credits { get; set; }

		/// <summary>
		/// This is intended to be a list of strings, possibly from a restricted domain, indicating kinds of content
		/// the book contains. Currently it only ever contains one member of the Topics list.
		/// </summary>
		[JsonProperty("tags")]
		public string[] Tags { get; set; }

		[JsonProperty("pageCount")]
		public int PageCount { get; set; }

		// This is obsolete but loading old Json files fails if we don't have a setter for it.
		[JsonProperty("languages")]
		public string[] Languages { get { return new string[0]; } set {}}

		[JsonProperty("langPointers")]
		public ParseDotComObjectPointer[] LanguageTableReferences { get; set; }

		[JsonProperty("summary")]
		public string Summary { get; set; }

		// This is set to true in situations where the materials that are not permissively licensed and the creator doesn't want derivative works being uploaded.
		// Currently we don't need this property in Parse.com, so we don't upload it.
		[JsonProperty("allowUploadingToBloomLibrary",DefaultValueHandling = DefaultValueHandling.Populate)]
		[DefaultValue(true)]
		public bool AllowUploadingToBloomLibrary { get; set; }

		[JsonProperty("bookletMakingIsAppropriate", DefaultValueHandling = DefaultValueHandling.Populate)]
		[DefaultValue(true)]
		public bool BookletMakingIsAppropriate { get; set; }

		public void SetUploader(string id)
		{
			// The uploader is stored in a way that makes the json that parse.com requires for a 'pointer'
			// to an object in another table: in this case the special table of users.
			if (Uploader == null)
				Uploader = new ParseDotComObjectPointer() { ClassName = "_User" };
			Uploader.ObjectId = id;
		}

		/// <summary>
		/// The Parse.com ID of the person who uploaded the book.
		/// This is stored in a special way that parse.com requires for cross-table pointers.
		/// </summary>
		[JsonProperty("uploader")]
		public ParseDotComObjectPointer Uploader { get; set; }

		/// <summary>These panels are being displayed in the toolbox for this book</summary>
		/// <example>["decodableReader", "leveledReader", "pageElements"]</example>
		[JsonProperty("tools",ItemConverterType = typeof(ToolboxToolConverter))]
		public List<ToolboxTool> Tools { get; set; }

		[JsonProperty("currentTool", NullValueHandling = NullValueHandling.Ignore)]
		public string CurrentTool { get; set; }

		[JsonProperty("toolboxIsOpen")]
		[DefaultValue(false)]
		public bool ToolboxIsOpen { get; set; }
	}

	/// <summary>
	/// This is the required structure for a parse.com pointer to an object in another table.
	/// </summary>
	public class ParseDotComObjectPointer
	{
		public ParseDotComObjectPointer()
		{
			Type = "Pointer"; // Required for all parse.com pointers.
		}

		[JsonProperty("__type")]
		public string Type { get; set; }

		[JsonProperty("className")]
		public string ClassName { get; set; }

		[JsonProperty("objectId")]
		public string ObjectId { get; set; }
	}

	/// <summary>
	/// This class represents the parse.com Language class (for purposes of generating json)
	/// </summary>
	public class LanguageDescriptor
	{
		[JsonIgnore]
		public string Json
		{
			get
			{
				return JsonConvert.SerializeObject(this);
			}
		}

		[JsonProperty("isoCode")]
		public string IsoCode { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("ethnologueCode")]
		public string EthnologueCode { get; set; }
	}
}
