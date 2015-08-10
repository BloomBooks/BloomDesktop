using System.Drawing.Imaging;
using Palaso.Extensions;
using Palaso.UI.WindowsForms.ClearShare;
using System;
using System.IO;

namespace Bloom.Book
{
	public class BookSelection
	{
		private Book _currentSelection;
		public event EventHandler SelectionChanged;


		public void SelectBook(Book book)
		{
			if (_currentSelection == book)
				return;

			//enhance... send out cancellable pre-change event

		   _currentSelection = book;
			if (book != null)
				CreateLicenseImageIfNeeded(book);

			InvokeSelectionChanged();
		}

		/// <summary>
		/// The default license (established in jade/html) is now CC-BY.  This requires a license
		/// image file to display correctly.  So we create one if it's needed.  (We also remove
		/// one that's not needed, but that's just for completeness.)  Note that the default
		/// license affects all books that have not been given an explicit license, not just
		/// newly created books.
		/// </summary>
		private static void CreateLicenseImageIfNeeded(Book book)
		{
			Metadata metadata = book.GetLicenseMetadata();
			if (metadata != null && metadata.License != null)
			{
				var licenseImage = metadata.License.GetImage();
				string imagePath = book.FolderPath.CombineForPath("license.png");
				if (licenseImage != null)
				{
					if (!File.Exists(imagePath))
					{
						using (Stream fs = new FileStream(imagePath, FileMode.Create))
						{
							licenseImage.Save(fs, ImageFormat.Png);
						}
					}
				}
				else
				{
					if (File.Exists(imagePath))
						File.Delete(imagePath);
				}
			}
		}



		public Book CurrentSelection
		{
			get { return _currentSelection; }
		}

		private void InvokeSelectionChanged()
		{
			EventHandler handler = SelectionChanged;
			if (handler != null)
			{
				handler(this, null);
			}
		}
	}
}