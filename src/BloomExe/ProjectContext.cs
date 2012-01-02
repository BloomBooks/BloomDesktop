using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using Bloom.Book;
using Bloom.Edit;
using Bloom.Library;
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
		public Shell ProjectWindow { get; private set; }

		public ProjectContext(string projectSettingsPath, IContainer parentContainer)
		{

			BuildSubContainerForThisProject(projectSettingsPath, parentContainer);

			ProjectWindow = _scope.Resolve <Shell>();
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
					typeof(PageListChangedEvent),  // REMOVE+++++++++++++++++++++++++++
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


				builder.Register<LibraryModel>(c => new LibraryModel(rootDirectoryPath, c.Resolve<BookSelection>(), c.Resolve<TemplateCollectionList>(), c.Resolve<BookCollection.Factory>())).InstancePerLifetimeScope();
				//builder.Register<PublishModel>(c => new PublishModel(c.Resolve<BookSelection>())).InstancePerLifetimeScope();


				builder.Register<IFileLocator>(c => new FileLocator(GetFileLocations())).InstancePerLifetimeScope();
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

				builder.Register<TemplateCollectionList>(c =>
					 {
						 var l = new TemplateCollectionList(c.Resolve<Book.Book.Factory>(), c.Resolve<BookStorage.Factory>());
						 l.RepositoryFolders = new string[] { FactoryCollectionsDirectory, InstalledCollectionsDirectory };
						 return l;
					 }).InstancePerLifetimeScope();

				builder.Register<ITemplateFinder>(c =>
					 {
						 return c.Resolve<TemplateCollectionList>();
					 }).InstancePerLifetimeScope();

				//TODO: this gave a stackoverflow exception
//				builder.Register<WorkspaceModel>(c => c.Resolve<WorkspaceModel.Factory>()(rootDirectoryPath)).InstancePerLifetimeScope();
				//so we're doing this
				builder.Register(c=>rootDirectoryPath).InstancePerLifetimeScope();

				builder.RegisterType<CreateFromTemplateCommand>().InstancePerLifetimeScope();
			});

		}
		private static IEnumerable<string> GetFileLocations()
		{
			yield return FileLocator.GetDirectoryDistributedWithApplication("root");
			yield return FileLocator.GetDirectoryDistributedWithApplication("widgets");
			yield return FileLocator.GetDirectoryDistributedWithApplication("xMatter");
			yield return FileLocator.GetDirectoryDistributedWithApplication("xMatter", "Factory-XMatter");
			yield return FactoryCollectionsDirectory;
			var templatesDir = Path.Combine(FactoryCollectionsDirectory, "Templates");


			yield return templatesDir;  //currently, this is where factory-xmatter.htm lives

			foreach (var templateDir in Directory.GetDirectories(templatesDir))
			{
				yield return templateDir;
			}
//			TODO: Add, in the list of places we look, this libary's "regional libary" (when such a concept comes into being)
//			so that things like IndonesiaA5Portrait.css work just the same as the Factory "A5Portrait.css"
//			var templateCollectionList = parentContainer.Resolve<TemplateCollectionList>();
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
			_scope.Dispose();
			_scope = null;
		}

	}
}
