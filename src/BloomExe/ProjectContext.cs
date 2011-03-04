using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Autofac;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Project;
using Bloom.Publish;
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

		public ProjectContext(string rootDirectoryPath, IContainer parentContainer)
		{
			BuildSubContainerForThisProject(rootDirectoryPath, parentContainer);

			ProjectWindow = _scope.Resolve <Shell>();
		}

		/// ------------------------------------------------------------------------------------
		protected void BuildSubContainerForThisProject(string rootDirectoryPath, IContainer parentContainer)
		{
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
					typeof(PageListChangedEvent),  // REMOVE+++++++++++++++++++++++++++
					typeof(BookSelection),
					typeof(PageSelection)}.Contains(t));


				builder.Register<LibraryModel>(c => new LibraryModel(rootDirectoryPath, c.Resolve<BookSelection>(), c.Resolve<TemplateCollectionList>(), c.Resolve<BookCollection.Factory>())).InstancePerLifetimeScope();
				//builder.Register<PublishModel>(c => new PublishModel(c.Resolve<BookSelection>())).InstancePerLifetimeScope();


				builder.Register<IFileLocator>(c => new FileLocator(GetFileLocations())).InstancePerLifetimeScope();
				const int kListViewIconHeightAndSize = 70;
				builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer(kListViewIconHeightAndSize)).InstancePerLifetimeScope();

				builder.Register<TemplateCollectionList>(c =>
					 {
						 var l = new TemplateCollectionList(c.Resolve<Book.Factory>(), c.Resolve<BookStorage.Factory>());
						 l.RepositoryFolders = new string[] { FactoryCollectionsDirectory, InstalledCollectionsDirectory };
						 return l;
					 }).InstancePerLifetimeScope();

				builder.Register<ITemplateFinder>(c =>
					 {
						 return c.Resolve<TemplateCollectionList>();
					 }).InstancePerLifetimeScope();

				//TODO: this gave a stackoverflow exception
//				builder.Register<ProjectModel>(c => c.Resolve<ProjectModel.Factory>()(rootDirectoryPath)).InstancePerLifetimeScope();
				//so we're doing this
				builder.Register(c=>rootDirectoryPath).InstancePerLifetimeScope();

				builder.RegisterType<CreateFromTemplateCommand>().InstancePerLifetimeScope();
			});

		}
		private static IEnumerable<string> GetFileLocations()
		{
			yield return FactoryCollectionsDirectory;
			var templatesDir = Path.Combine(FactoryCollectionsDirectory, "Templates");
			foreach (var templateDir in Directory.GetDirectories(templatesDir))
			{
				yield return templateDir;
			}
		}
		private static string FactoryCollectionsDirectory
		{
			get { return FileLocator.GetDirectoryDistributedWithApplication("factoryCollections"); }
		}

		private static string InstalledCollectionsDirectory
		{
			get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Bloom"), "Collections"); }
		}

		/// ------------------------------------------------------------------------------------
		public void Dispose()
		{
			_scope.Dispose();
			_scope = null;
		}

	}
}
