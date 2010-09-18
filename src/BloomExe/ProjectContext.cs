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
					.Where(t => t.GetInterfaces().Contains(typeof(Bloom.Event<>)));

				//Other classes which are also  singletons
				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
					.InstancePerLifetimeScope()
					.Where(t => new[]{
					typeof(TemplateInsertionCommand),
					typeof(BookSelection),
					typeof(PageSelection)}.Contains(t));

				builder.Register<LibraryModel>(c => new LibraryModel(rootDirectoryPath, c.Resolve<BookSelection>(), c.Resolve<TemplateCollectionList>(), c.Resolve<BookCollection.Factory>())).InstancePerLifetimeScope();
				builder.Register<IFileLocator>(c => new FileLocator(GetFileLocations())).InstancePerLifetimeScope();
				builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer(60)).InstancePerLifetimeScope();

				builder.Register<TemplateCollectionList>(c =>
					 {
						 var l = new TemplateCollectionList(c.Resolve<Book.Factory>(), c.Resolve<BookStorage.Factory>());
						 l.ReposistoryFolders = new string[] { FactoryCollectionsDirectory };
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
			get { return Path.Combine(GetTopAppDirectory(), "factoryCollections"); }
		}

		private static string GetTopAppDirectory()
		{
			string path = DirectoryOfTheApplicationExecutable;
			char sep = Path.DirectorySeparatorChar;
			int i = path.ToLower().LastIndexOf(sep + "output" + sep);

			if (i > -1)
			{
				path = path.Substring(0, i + 1);
			}
			return path;
		}


		public static string DirectoryOfTheApplicationExecutable
		{
			get
			{
				string path;
				bool unitTesting = Assembly.GetEntryAssembly() == null;
				if (unitTesting)
				{
					path = new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath;
					path = Uri.UnescapeDataString(path);
				}
				else
				{
					path = Application.ExecutablePath;
				}
				return Directory.GetParent(path).FullName;
			}
		}

		/// ------------------------------------------------------------------------------------
		public void Dispose()
		{
			_scope.Dispose();
			_scope = null;
		}

	}
}
