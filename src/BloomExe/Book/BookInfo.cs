using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Collection;
using Bloom.Properties;
using Newtonsoft.Json;
using Palaso.Extensions;

namespace Bloom.Book
{
	/// <summary>
	/// A BookInfo has everything needed to display a title and thumbnail, and (eventually) do common searching filtering operations, without accessing the actual contents of the book.
	/// A related responsibility is to wrap the meta.json file which stores this search/display data in a form suitable for uploading to our web server.
	/// </summary>
	public class BookInfo
	{
		public static Color[] CoverColors = new Color[] { Color.FromArgb(228, 140, 132), Color.FromArgb(176, 222, 228), Color.FromArgb(152, 208, 185), Color.FromArgb(194, 166, 191) };
		private static int _coverColorIndex = 0;

		private BookMetaData _metadata;

		private BookMetaData MetaData
		{
			get { return _metadata ?? (_metadata = new BookMetaData()); }
		}

		public BookInfo(string folderPath, bool isEditable)
		{
			IsSuitableForVernacularLibrary = true; // default
			FolderPath = folderPath;
			Id = Guid.NewGuid().ToString();
			CoverColor = NextBookColor();

			var jsonPath = MetaDataPath;
			if (File.Exists(jsonPath))
			{
				_metadata = BookMetaData.FromString(File.ReadAllText(jsonPath)); // Enhance: error handling?
			}
			else
			{
				// Look for old tags files not yet migrated
				var oldTagsPath = Path.Combine(folderPath, "tags.txt");
				if (File.Exists(oldTagsPath))
				{
					Book.ConvertTagsToMetaData(oldTagsPath, this);
				}
			}

			//TODO
			Type = Book.BookType.Publication;
			IsEditable = isEditable;
		}

		public string Id
		{
			get { return MetaData.Id; }
			set { MetaData.Id = value; }
		}

		public Color CoverColor { get; set; }

		public string FolderPath { get; set; }

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

		//SeeAlso: commeted IsExperimental on Book
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
			set { MetaData.Title = value; }
		}

		// Todo: this is currently not used. It is intended to be filled in when we upload the json.
		// Not sure what it needs to be. Locally the thumbnail is always called just thumbnail.png.
		// What we upload needs to be a functional URL (probably relative to our site root).
		public string Thumbnail
		{
			get { return MetaData.Thumbnail; }
			set { MetaData.Thumbnail = value; }
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

		// Todo: this needs to be set to some suitable person/organization, possibly based on our parse.com login, when something is actually uploaed.
		// As yet it is not used.
		public string UploadedBy
		{
			get { return MetaData.UploadedBy; }
			set { MetaData.UploadedBy = value; }
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
			if (File.Exists(path))
			{
				//this FromFile thing locks the file until the image is disposed of. Therefore, we copy the image and dispose of the original.
				using (var tempImage = Image.FromFile(path))
				{
					image = new Bitmap(tempImage);
				}
				return true;
			}
			image = null;
			return false;
		}

		public static Color NextBookColor()
		{
			return CoverColors[_coverColorIndex++%CoverColors.Length];
		}

		public void Save()
		{
			File.WriteAllText(MetaDataPath, MetaData.Json);
		}

		internal string MetaDataPath
		{
			get { return Path.Combine(FolderPath, MetaDataFileName); }
		}

		public const string MetaDataFileName = "meta.json";

		public string AuthorList
		{
			get { return MetaData.Authors == null ? "" : string.Join(", ", MetaData.Authors); }
			set
			{
				MetaData.Authors= SplitList(value);
			}
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
		public string[] Languages
		{
			get { return MetaData.Languages; }
			set { MetaData.Languages = value; }
		}
	}

	public class ErrorBookInfo : BookInfo
	{
		public ErrorBookInfo(string folderPath, Exception exception) : base(folderPath,false/*review*/)
		{
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
		public static BookMetaData FromString(string input)
		{
			return JsonConvert.DeserializeObject<BookMetaData>(input);
		}

		public static BookMetaData FromFolder(string bookFolderPath)
		{
			return FromString(File.ReadAllText(MetaDataPath(bookFolderPath)));
		}

		public static string MetaDataPath(string bookFolderPath)
		{
			return bookFolderPath.CombineForPath(BookInfo.MetaDataFileName);
		}

		public void WriteToFolder(string bookFolderPath)
		{
			File.WriteAllText(MetaDataPath(bookFolderPath), Json);
		}

		[JsonIgnore]
		public string Json
		{
			get
			{
				return JsonConvert.SerializeObject(this);
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

		// Todo: multilingual
		[JsonProperty("title")]
		public string Title { get; set; }

		// This is filled in when we upload the json. It is not used locally, but becomes a field on parse.com
		// containing the actual url where we can grab the thumbnail.
		// Locally the thumbnail is always called just thumbnail.png.
		[JsonProperty("thumbnail")]
		public string Thumbnail { get; set; }

		// This is filled in when we upload the json. It is not used locally, but becomes a field on parse.com
		// containing the actual url where we can grab the book order file which when opened by Bloom causes it
		// to download the book.
		[JsonProperty("bookOrder")]
		public string BookOrder { get; set; }

		[JsonProperty("isbn")]
		public string Isbn { get; set; }

		[JsonProperty("bookLineage")]
		public string BookLineage { get; set; }

		// Todo: this needs to be set to some suitable person/organization, possibly based on our parse.com login, when something is actually uploaed.
		// As yet it is not used.
		[JsonProperty("uploadedBy")]
		public string UploadedBy { get; set; }

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

		[JsonProperty("authors")]
		public string[] Authors { get; set; }

		/// <summary>
		/// This is intended to be a list of strings, possibly from a restricted domain, indicating kinds of content
		/// the book contains. Currently it only ever contains one member of the Topics list.
		/// </summary>
		[JsonProperty("tags")]
		public string[] Tags { get; set; }

		[JsonProperty("pageCount")]
		public int PageCount { get; set; }

		[JsonProperty("languages")]
		public string[] Languages { get; set; }
	}
}
