using SIL.IO;

namespace Bloom.Book
{
	public class BookCompactor
	{
		public const string ExtensionForDeviceBloomBook = ".bloomd";

		public static string CompactBookForDevice(Book book)
		{
			book.Save();

			using (var tempFile = TempFile.WithFilenameInTempFolder(book.Title + ExtensionForDeviceBloomBook))
			{
				var zip = new BloomZipFile(tempFile.Path);
				zip.AddDirectory(book.FolderPath);
				zip.Save();

				tempFile.Detach();
				return tempFile.Path;
			}
		}
	}
}
