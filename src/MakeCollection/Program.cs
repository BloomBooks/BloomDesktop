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
			collectionSettings.DefaultLanguage1FontName = "Andika";
			collectionSettings.Save();

			var folio = MakeBook(collectionSettings, kpathToSHRPTemplates + "UgandaSHARP-P1GuideFolio");

			//The Teacher's Guide has a set of vernacular labels that are inserted via custom style sheets. This copies
			//and renames the one for this language.
			File.Copy(kpathToSHRPTemplates + "UgandaSHARP-P1TeacherGuide/" + language + "Labels.css",
				Path.Combine(collectionSettings.FolderPath, "customCollectionStyles.css"));

			var themes = "Our school,Our school,Our school,Our home,Our home,Our home,Our community,Our community,Our community,Human body and health,Human body and health,Weather,Weather,Weather,Accidents and safety,Accidents and safety,Accidents and safety,Living together,Living together,Living together,Food and Nutrition,Food and Nutrition,Transport,Transport,Transport,Things we make,Things we make,Things we make,Our environment,Our environment,Our environment,Peace and security,Peace and security"
				.Split(new[] {','});

			var themeNumbers =
				"1,1,1,2,2,2,3,3,3,4,4,5,5,5,6,6,6,7,7,7,8,8,9,9,9,10,10,10,11,11,11,12,12".Split(new[] {','});
			var subThemes =
				"People in our school,Things in our school,Activities in our school,People in our home,Roles and responsibilities of family members,Things in our home and their uses,People in our community,Activities in our community,Important places in our community,Parts of the body,Personal hygiene,Elements and types of weather,Activities for different seasons,Effects and management of weather,Accidents and safety at home,Accidents and safety on the way,Accidents and safety at school,The family,Ways of living together in the school,Ways of living together in the community,Names and sources of food,Uses of food,Types and means of transport,Importance of transport,Measures related to transport,Things we make at home and school,Materials we use and their sources,Importance of things we make,Components and importance of things in our environment,Factors that damage our environment,Conservation of our environment,Peace and security in our home,Peace and security in our school"
					.Split(new[] {','});
			var subThemeNumbers = "1,2,3,1,2,3,1,2,3,1,2,1,2,3,1,2,3,1,2,3,1,2,1,2,3,1,2,3,1,2,3,1,2".Split(new[] {','});

			var subthemeIndex = 0;
//            for (int term = 1; term < 4; term++)
			for (int term = 1; term < 4; term++)
			{

				var termIntro = MakeBook(collectionSettings, kpathToSHRPTemplates + "UgandaSHARP-P1TeacherGuideTermIntro");
				termIntro.SetDataItem("term", term.ToString(), "en");
				termIntro.Save();

				Book weekBook;

				// Make the special Week 1 Book
				if (term == 1 || term == 3)
				{
					weekBook = MakeBook(collectionSettings, kpathToSHRPTemplates + "UgandaSHARP-P1TeacherGuideWeek1");

					weekBook.SetDataItem("term", term.ToString(), "*");
					weekBook.SetDataItem("week", "1", "*");

					SetThemeStuff(weekBook, themes[subthemeIndex], themeNumbers[subthemeIndex], subThemes[subthemeIndex],
						subThemeNumbers[subthemeIndex]);
					weekBook.Save();
				}

				++subthemeIndex; // even in term 2, we have a subtheme to skip (weird, but that's my understanding at the moment)

				for (int week = 2; week < 12; week++)
				//for (int week = 2; week < 3; week++)
				{
					weekBook = MakeBook(collectionSettings, kpathToSHRPTemplates + "UgandaSHARP-P1TeacherGuide");
					weekBook.SetDataItem("term", term.ToString(), "*");
					weekBook.SetDataItem("week", week.ToString(), "*");

					SetThemeStuff(weekBook, themes[subthemeIndex], themeNumbers[subthemeIndex], subThemes[subthemeIndex],
						subThemeNumbers[subthemeIndex]);
					weekBook.Save();
					++subthemeIndex;
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

			return book;
		}

		private static void SetThemeStuff(Book book, string theme, string themeNumber, string subtheme, string subthemeNumber)
		{
			book.SetDataItem("theme",  theme, "en");
			book.SetDataItem("subtheme",  subtheme, "en");
			book.SetDataItem("themeNumber",  themeNumber, "*");
			book.SetDataItem("subthemeNumber",  subthemeNumber, "*");
		}
	}
}
