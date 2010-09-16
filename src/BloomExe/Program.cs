using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autofac;
using Bloom.Edit;
using Bloom.Library;
using Palaso.IO;

namespace Bloom
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			Skybound.Gecko.Xpcom.Initialize(Path.Combine(DirectoryOfTheApplicationExecutable, "xulrunner"));

			var projectDirectory = Path.Combine(GetTopAppDirectory(), "userProject");
			var builder = new Autofac.ContainerBuilder();

			//default to InstancePerDependency, i.e., they it will make a new
			//one each time someone asks for one
			builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly());

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


			//builder.Register<Project.ProjectModel>(c => new Project.ProjectModel(projectDirectory));
			builder.Register<LibraryModel>(c => new LibraryModel(c.Resolve<BookSelection>(), projectDirectory, c.Resolve<TemplateCollectionList>(), c.Resolve<BookCollection.Factory>())).InstancePerLifetimeScope();
			builder.Register<IFileLocator>(c => new FileLocator( GetFileLocations())).InstancePerLifetimeScope();
			builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer(60)).InstancePerLifetimeScope();

			builder.Register<TemplateCollectionList>(c=>
				 {
					 var l = new TemplateCollectionList(c.Resolve<Book.Factory>(), c.Resolve<BookStorage.Factory>());
					 l.ReposistoryFolders = new string[] {FactoryCollectionsDirectory };
					 return l;
				 }).InstancePerLifetimeScope();

			builder.Register<ITemplateFinder>(c =>
				 {
					 return c.Resolve<TemplateCollectionList>();
				 }).InstancePerLifetimeScope();

   //       didn't give me the same one  builder.RegisterType<TemplateCollectionList>().As<ITemplateFinder>().InstancePerLifetimeScope();

			var container = builder.Build();
			Application.Run(container.Resolve<Shell>());
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

	}


}
