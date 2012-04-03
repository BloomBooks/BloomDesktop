using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autofac;
using Bloom.Book;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Workspace;
using Bloom.web;
using Palaso.Extensions;
using Palaso.IO;

namespace Bloom
{
	public class ProjectContext : IDisposable
	{
		/// <summary>
		/// Any resources which belong only to this project will be tracked by this,
		/// and disposed of along with this ProjectContext class
		/// </summary>
		private ILifetimeScope _scope;

		private BloomServer _bloomServer;
		public Form ProjectWindow { get; private set; }

		public ProjectContext(string projectSettingsPath, IContainer parentContainer)
		{

			BuildSubContainerForThisProject(projectSettingsPath, parentContainer);

			ProjectWindow = _scope.Resolve <Shell>();

			if(Path.GetFileNameWithoutExtension(projectSettingsPath).ToLower().Contains("web"))
			{
				var libraryCollection =_scope.Resolve<BookCollection.Factory>()(Path.GetDirectoryName(projectSettingsPath), BookCollection.CollectionType.TheOneEditableCollection);
				var storeCollectionList = _scope.Resolve<StoreCollectionList>();
				_bloomServer = new BloomServer(_scope.Resolve<LibrarySettings>(), libraryCollection, storeCollectionList, _scope.Resolve<HtmlThumbNailer>());
				_bloomServer.Start();
			}
		}

		/// ------------------------------------------------------------------------------------
		protected void BuildSubContainerForThisProject(string projectSettingsPath, IContainer parentContainer)
		{
			var rootDirectoryPath = Path.GetDirectoryName(projectSettingsPath);
			_scope = parentContainer.BeginLifetimeScope(builder =>
			{
				//BloomEvents are by nature, singletons (InstancePerLifetimeScope)
				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
					.InstancePerLifetimeScope()
					// Didn't work .Where(t => t.GetInterfaces().Contains(typeof(Bloom.Event<>)));
					.Where(t => t is IEvent);

				//Other classes which are also  singletons
				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
					.InstancePerLifetimeScope()
					.Where(t => new[]{
					typeof(TemplateInsertionCommand),
					typeof(DeletePageCommand),
					typeof(EditBookCommand),
					typeof(SelectedTabChangedEvent),
					typeof(LibraryClosing),
					typeof(PageListChangedEvent),  // REMOVE+++++++++++++++++++++++++++
					typeof(BookRefreshEvent),
					typeof(BookSelection),
					typeof(RelocatePageEvent),
					typeof(PageSelection),
					typeof(EditingModel)}.Contains(t));


				///LibrarySettings = _scope.Resolve<Func<string, LibrarySettings>>()(projectSettingsPath);
			  //  LibrarySettings = new LibrarySettings(projectSettingsPath);
				try
				{
					builder.Register<LibrarySettings>(c => new LibrarySettings(projectSettingsPath)).InstancePerLifetimeScope();
				}
				catch(Exception)
				{
					return;
				}


				builder.Register<LibraryModel>(c => new LibraryModel(rootDirectoryPath, c.Resolve<BookSelection>(), c.Resolve<StoreCollectionList>(), c.Resolve<BookCollection.Factory>(), c.Resolve<EditBookCommand>())).InstancePerLifetimeScope();
				//builder.Register<PublishModel>(c => new PublishModel(c.Resolve<BookSelection>())).InstancePerLifetimeScope();
				//builder.Register<BookCollection>(c => c.Resolve<BookCollection>());

				builder.Register<IChangeableFileLocator>(c => new BloomFileLocator(c.Resolve<LibrarySettings>(), c.Resolve<XMatterPackFinder>(), GetFileLocations())).InstancePerLifetimeScope();

				const int kListViewIconHeightAndSize = 70;
				builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer(kListViewIconHeightAndSize)).InstancePerLifetimeScope();

				builder.Register<LanguageSettings>(c =>
													{
														var librarySettings = c.Resolve<LibrarySettings>();
														var preferredSourceLanguagesInOrder = new List<string>();
														preferredSourceLanguagesInOrder.Add(librarySettings.NationalLanguage1Iso639Code);
														if (!string.IsNullOrEmpty(librarySettings.NationalLanguage2Iso639Code)
															&& librarySettings.NationalLanguage2Iso639Code != librarySettings.NationalLanguage1Iso639Code)
															preferredSourceLanguagesInOrder.Add(librarySettings.NationalLanguage2Iso639Code);

														return new LanguageSettings(librarySettings.VernacularIso639Code, preferredSourceLanguagesInOrder);
													});
				builder.Register<XMatterPackFinder>(c =>
														{
															var locations = new List<string>();
															locations.Add(FileLocator.GetDirectoryDistributedWithApplication("xMatter"));
															locations.Add(XMatterAppDataFolder);
															return new XMatterPackFinder(locations);
														});

				builder.Register<StoreCollectionList>(c =>
					 {
						 var l = new StoreCollectionList(c.Resolve<Book.Book.Factory>(), c.Resolve<BookStorage.Factory>(),c.Resolve<BookCollection.Factory>());
						 l.RepositoryFolders = new string[] { FactoryCollectionsDirectory, InstalledCollectionsDirectory };
						 return l;
					 }).InstancePerLifetimeScope();

				builder.Register<ITemplateFinder>(c =>
					 {
						 return c.Resolve<StoreCollectionList>();
					 }).InstancePerLifetimeScope();

				//TODO: this gave a stackoverflow exception
//				builder.Register<WorkspaceModel>(c => c.Resolve<WorkspaceModel.Factory>()(rootDirectoryPath)).InstancePerLifetimeScope();
				//so we're doing this
				builder.Register(c=>rootDirectoryPath).InstancePerLifetimeScope();

				builder.RegisterType<CreateFromTemplateCommand>().InstancePerLifetimeScope();


				builder.Register<Func<WorkspaceView>>(c => ()=>
													{
														var factory = c.Resolve<WorkspaceView.Factory>();
														if (projectSettingsPath.ToLower().Contains("web"))
														{
															return factory(c.Resolve<WebLibraryView>());
														}
														else
														{
															return factory(c.Resolve<LibraryView>());
														}
													});
			});

		}
		private static IEnumerable<string> GetFileLocations()
		{
			yield return Path.GetDirectoryName(FileLocator.GetDirectoryDistributedWithApplication("root"));//hack to get the distfiles folder itself
			yield return FileLocator.GetDirectoryDistributedWithApplication("root");
			yield return FileLocator.GetDirectoryDistributedWithApplication("widgets");
			yield return FileLocator.GetDirectoryDistributedWithApplication("xMatter");
			//yield return FileLocator.GetDirectoryDistributedWithApplication("xMatter", "Factory-XMatter");
			var templatesDir = Path.Combine(FactoryCollectionsDirectory, "Templates");

			yield return templatesDir;  //currently, this is where factory-xmatter.htm lives

			foreach (var templateDir in Directory.GetDirectories(templatesDir))
			{
				yield return templateDir;
			}

			yield return FactoryCollectionsDirectory;
			var samplesDir = Path.Combine(FactoryCollectionsDirectory, "Sample Shells");

			foreach (var dir in Directory.GetDirectories(samplesDir))
			{
				yield return dir;
			}

			foreach (var dir in Directory.GetDirectories(InstalledCollectionsDirectory))
			{
				yield return dir;
			}


//			TODO: Add, in the list of places we look, this library's "regional library" (when such a concept comes into being)
//			so that things like IndonesiaA5Portrait.css work just the same as the Factory "A5Portrait.css"
//			var templateCollectionList = parentContainer.Resolve<StoreCollectionList>();
//			foreach (var repo in templateCollectionList.RepositoryFolders)
//			{
//				foreach (var directory in Directory.GetDirectories(repo))
//				{
//					yield return directory;
//				}
//			}
		}
		private static string FactoryCollectionsDirectory
		{
			get { return FileLocator.GetDirectoryDistributedWithApplication("factoryCollections"); }
		}

		private static string InstalledCollectionsDirectory
		{
			get
			{
				//we want this path of directories sitting there, waiting for the user
				var d = GetBloomAppDataFolder();
				var collections = d.CombineForPath("Collections");
				if (!Directory.Exists(d))
					Directory.CreateDirectory(d);
				return collections;
			}
		}

		private static string XMatterAppDataFolder
		{
			get
			{
				//we want this path of directories sitting there, waiting for the user
				var d = GetBloomAppDataFolder();
				if (!Directory.Exists(d))
					Directory.CreateDirectory(d);
				d = d.CombineForPath("XMatter");
				if (!Directory.Exists(d))
					Directory.CreateDirectory(d);
				return d;
			}
		}

		private static string GetBloomAppDataFolder()
		{
			var d = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).CombineForPath("SIL");
			if (!Directory.Exists(d))
				Directory.CreateDirectory(d);
			d = d.CombineForPath("Bloom");
			if (!Directory.Exists(d))
				Directory.CreateDirectory(d);
			return d;
		}

		/// ------------------------------------------------------------------------------------
		public void Dispose()
		{
			if(_bloomServer!=null)
				_bloomServer.Dispose();
			_bloomServer = null;
			_scope.Dispose();
			_scope = null;
		}

	}
}
