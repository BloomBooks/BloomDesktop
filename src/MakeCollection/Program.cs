using System.Collections.Generic;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Palaso.IO;

namespace MakeCollection
{
	class Program
	{
		const string kpathToSHRPTemplates = @"C:\dev\Bloom Custom Template Work\UgandaSHARP\";

		static void Main(string[] args)
		{
			var root = @"c:\dev\temp\uganda";
			if(Directory.Exists(root))
				Directory.Delete(root,true);

			Directory.CreateDirectory(root);



			MakeCollection(root,  "Lëbacoli", "ach");
			MakeCollection(root, "Lugbarati", "lgg");
			MakeCollection(root, "Lumasaaba", "myx");
			MakeCollection(root, "Runyoro-Rutooro", "ttj");
		}

		private static void MakeCollection(string root, string language, string language1Iso639Code)
		{

			var spec = new NewCollectionSettings()
				{
					PathToSettingsFile = CollectionSettings.GetPathForNewSettings(root, language + " P1 Teacher's Guide"),
					AllowNewBooks = false,
					Country = "Uganda",
					DefaultLanguage1FontName = language,
					Language1Iso639Code = language1Iso639Code,
					IsSourceCollection = false,
					Language2Iso639Code = "en"
				};

			var collectionSettings = new CollectionSettings(spec);
			collectionSettings.DefaultLanguage1FontName = "Calibri";
			collectionSettings.Save();

			var folio = MakeBook(collectionSettings, kpathToSHRPTemplates+"UgandaSHARP-P1GuideFolio");

			//The Teacher's Guide has a set of vernacular labels that are inserted via custom style sheets. This copies
			//and renames the one for this language.
			File.Copy(kpathToSHRPTemplates+"UgandaSHARP-P1TeacherGuide/"+language+"Labels.css", Path.Combine(collectionSettings.FolderPath,"customCollectionStyles.css"));

			for (int term = 1; term < 4; term++)
			{
				var termIntro = MakeBook(collectionSettings, kpathToSHRPTemplates + "UgandaSHARP-P1TeacherGuideTermIntro");
				termIntro.SetDataItem("term", term.ToString(), "en");
				termIntro.Save();

				for (int week = 2; week < 3 ; week++)
				{
					var weekBook = MakeBook(collectionSettings, kpathToSHRPTemplates+"UgandaSHARP-P1TeacherGuide");
					weekBook.SetDataItem("term", term.ToString(), "en");
					weekBook.SetDataItem("week", week.ToString(), "en");
					weekBook.Save();
				}
			}
		}

		private static Book MakeBook(CollectionSettings collectionSettings, string sourceBookFolderPath)
		{
			var xmatterLocations = new List<string>();
			xmatterLocations.Add(ProjectContext.XMatterAppDataFolder);
			xmatterLocations.Add(FileLocator.GetDirectoryDistributedWithApplication( kpathToSHRPTemplates));
			xmatterLocations.Add(FileLocator.GetDirectoryDistributedWithApplication("xMatter"));
			var locator = new BloomFileLocator(collectionSettings, new XMatterPackFinder(xmatterLocations), new string[] {});

			var starter = new BookStarter(locator,
										  path =>
										  new BookStorage(path, locator, new BookRenamedEvent(),
														  collectionSettings),
										  collectionSettings);
			var pathToFolderOfNewBook = starter.CreateBookOnDiskFromTemplate(sourceBookFolderPath, collectionSettings.FolderPath);

			var newBookInfo = new BookInfo(pathToFolderOfNewBook, false /*saying not editable works us around a langdisplay issue*/);

			BookStorage bookStorage = new BookStorage(pathToFolderOfNewBook, locator, new BookRenamedEvent(), collectionSettings);
			var book = new Book(newBookInfo, bookStorage, null,
							collectionSettings, null, null, null, null);
			book.SetDataItem("theme","en","test theme");
			return book;
		}
	}
}
