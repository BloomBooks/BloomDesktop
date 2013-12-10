using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Collection;
using Bloom.Properties;

namespace Bloom.Book
{
	/// <summary>
	/// A BookInfo has everything needed to display a title and thumbnail, and (eventually) do common searching filtering operations, without accessing the actual contents of the book.
	/// </summary>
	public class BookInfo
	{
		public static Color[] CoverColors = new Color[] { Color.FromArgb(228, 140, 132), Color.FromArgb(176, 222, 228), Color.FromArgb(152, 208, 185), Color.FromArgb(194, 166, 191) };
		private static int _coverColorIndex = 0;

		private BookMetaData _metadata;

		// Be careful. BookInfo sometimes exists independently of a Book or a BookStorage (e.g., in building a collection of available books)
		// and needs independent metadata. So it has its own. But this is redundant with the main MetaData in the BookStorage, for a real book.
		// The metadata here is not necessarily updated to have all the same information as the main one, though an attempt is made
		// to cause it to be the same object when a book is created.
		internal BookMetaData MetaData
		{
			set { _metadata = value; }
			get { return _metadata ?? (_metadata = new BookMetaData()); }
		}

		public BookInfo(string folderPath,bool isEditable)
		{
			FolderPath = folderPath;
			Id = Guid.NewGuid().ToString();
			CoverColor = NextBookColor();

			var jsonPath = Path.Combine(folderPath, BookStorage.MetaDataFileName);
			if (File.Exists(jsonPath))
			{
				_metadata = BookMetaData.Deserialize(File.ReadAllText(jsonPath)); // Enhance: error handling?
			}
			else
			{
				// Look for old tags files not yet migrated
				var oldTagsPath = Path.Combine(folderPath, "tags.txt");
				if (File.Exists(oldTagsPath))
				{
					Book.ConvertTagsToMetaData(oldTagsPath, MetaData);
				}
			}

			//TODO
			Type = Book.BookType.Publication;
			IsEditable = isEditable;
		}

		public string Id { get; set; }

		public Color CoverColor { get; set; }

		public string FolderPath { get; set; }

		public bool IsSuitableForMakingShells
		{
			get { return MetaData.bloom.suitableForMakingShells; }
		}

		public bool IsSuitableForVernacularLibrary
		{
			get { return true; } //TODO -- MetaData.bloom.suitableForMakingVernacularBooks?
		}


		//SeeAlso: commeted IsExperimental on Book
		public bool IsExperimental
		{
			get
			{
				return MetaData.bloom.experimental;
			}
		}

		/// <summary>
		/// A "Folio" document is one that acts as a wrapper for a number of other books
		/// </summary>
		public bool IsFolio
		{
			get
			{
				return MetaData.bloom.folio;
			}
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
			return BookInfo.CoverColors[_coverColorIndex++ % BookInfo.CoverColors.Length];
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
}
