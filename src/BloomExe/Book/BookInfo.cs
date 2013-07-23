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
		private string _tags;
		public static Color[] CoverColors = new Color[] { Color.FromArgb(228, 140, 132), Color.FromArgb(176, 222, 228), Color.FromArgb(152, 208, 185), Color.FromArgb(194, 166, 191) };
		private static int _coverColorIndex = 0;

		public BookInfo(string folderPath,bool isEditable)
		{
			FolderPath = folderPath;
			Id = Guid.NewGuid().ToString();
			CoverColor = NextBookColor();

			var tagsPath = Path.Combine(FolderPath, "tags.txt");
			if (File.Exists(tagsPath))
			{
				_tags = File.ReadAllText(tagsPath);
			}
			else
			{
				_tags = "";
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
			get { return _tags.Contains("suitableForMakingShells"); }
		}

		public bool IsSuitableForVernacularLibrary
		{
			get { return true; } //TODO
		}


		//SeeAlso: commeted IsExperimental on Book
		public bool IsExperimental
		{
			get
			{
				return _tags.Contains("experimental");
			}
		}

		/// <summary>
		/// A "Folio" document is one that acts as a wrapper for a number of other books
		/// </summary>
		public bool IsFolio
		{
			get
			{
				return _tags.Contains("folio");
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
