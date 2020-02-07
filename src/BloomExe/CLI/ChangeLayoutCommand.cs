using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.ToPalaso;
using CommandLine;
using L10NSharp;
using SIL.Progress;

namespace Bloom.CLI
{
	// Implements the changeLayout command-line command.
	// Typical command line:
	// changeLayout "C:\Users\Thomson\Documents\Bloom\français Books\français Books.bloomCollection" "<path to bloom install directory>\browser\templates\template books\Basic Book\Basic Book.html" 7b192144-527c-417c-a2cb-1fb5e78bf38a
	// This changes the layout of EVERY page in EVERY book in the collection to match the specified page,
	// except where data loss would occur because the initial page has more pictures or text blocks than the
	// new layout has places for.
	class ChangeLayoutCommand
	{
		public static int Handle(ChangeLayoutParameters options)
		{
			// This task will be all the program does. We need to do enough setup so that
			// books can be created, then do our work, then tear things down.
			Program.SetUpErrorHandling();
			try
			{
				using (var applicationContainer = new ApplicationContainer())
				{
					Program.SetUpLocalization(applicationContainer);
					Browser.SetUpXulRunner();
					Browser.XulRunnerShutdown += Program.OnXulRunnerShutdown;
					LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
					ChangeLayoutForAllContentPagesInAllBooks(options.CollectionPath, options.BookPath, options.PageGuid);
				}
				return 0;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
				return 1;
			}
		}

		public static void ChangeLayoutForAllContentPagesInAllBooks(string collectionPath, string bookPath, string pageGuid)
		{
			using (var dlg = new ProgressDialogBackground())
			{
				dlg.ShowAndDoWork((progress, args) => ChangeLayoutForAllContentPagesInAllBooks(progress, collectionPath, bookPath, pageGuid));
			}
		}

		public static void ChangeLayoutForAllContentPagesInAllBooks(IProgress progress, string collectionPath, string bookPath, string pageGuid)
		{
			if (!File.Exists(bookPath))
			{
				MessageBox.Show("Could not find template book " + bookPath);
				return;
			}
			if (!File.Exists(collectionPath))
			{
				MessageBox.Show("Could not find collection file " + collectionPath);
				return;
			}
			var problems = new StringBuilder();
			var collectionFolder = Path.GetDirectoryName(collectionPath);
			var collection = new BookCollection(collectionFolder, BookCollection.CollectionType.TheOneEditableCollection, new BookSelection());
			var collectionSettings = new CollectionSettings(collectionPath);
			XMatterPackFinder xmatterFinder = new XMatterPackFinder(new[] { BloomFileLocator.GetInstalledXMatterDirectory() });
			var locator = new BloomFileLocator(collectionSettings, xmatterFinder, ProjectContext.GetFactoryFileLocations(),
				ProjectContext.GetFoundFileLocations(), ProjectContext.GetAfterXMatterFileLocations());

			var templateBookInfo  = new BookInfo(Path.GetDirectoryName(bookPath), true);
			var templateBook = new Book.Book(templateBookInfo, new BookStorage(templateBookInfo.FolderPath, locator, new BookRenamedEvent(), collectionSettings),
				null, collectionSettings, null, null, new BookRefreshEvent(), new BookSavedEvent());

			var pageDictionary = templateBook.GetTemplatePagesIdDictionary();
			IPage page = null;
			if (!pageDictionary.TryGetValue(pageGuid, out page))
			{
				MessageBox.Show("Could not find template page " + pageGuid);
				return;
			}

			int i = 0;
			foreach (var bookInfo in collection.GetBookInfos())
			{
				i++;
				try
				{
					var book = new Book.Book(bookInfo,
						new BookStorage(bookInfo.FolderPath, locator, new BookRenamedEvent(), collectionSettings),
						null, collectionSettings, null, null, new BookRefreshEvent(), new BookSavedEvent());
					//progress.WriteMessage("Processing " + book.TitleBestForUserDisplay + " " + i + "/" + collection.GetBookInfos().Count());
					progress.ProgressIndicator.PercentCompleted = i * 100 / collection.GetBookInfos().Count();

					book.ChangeLayoutForAllContentPages(page);
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
					Console.WriteLine(ex.Message);
					Console.WriteLine(ex.StackTrace);
					problems.AppendLine(Path.GetFileName(bookInfo.FolderPath));
				}
			}
			if (problems.Length == 0)
			{
				MessageBox.Show("All books converted successfully");
			}
			else
			{
				MessageBox.Show("Bloom had problems converting the following books; please check them:\n" + problems);
			}
		}
	}


	// Used with https://github.com/gsscoder/commandline, which we get via nuget.
	// (using the beta of commandline 2.0, as of Bloom 3.8)

	[Verb("changeLayout", HelpText = "Change the layout of ALL pages in ALL books in collection")]
	public class ChangeLayoutParameters
	{
		[Value(0, MetaName = "collectionPath", HelpText = "Path to the .bloomCollection file in the folder to be updated.", Required = true)]
		public string CollectionPath { get; set; }

		[Value(1, MetaName = "bookPath", HelpText = "Path to the book that contains the desired page layout.", Required = true)]
		public string BookPath { get; set; }

		[Value(2, MetaName = "pageGuid", HelpText = "Guid of the page that has the required layout.", Required = true)]
		public string PageGuid { get; set; }
	}
}
