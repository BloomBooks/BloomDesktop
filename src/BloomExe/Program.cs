using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autofac;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Project;
using Bloom.Publish;
using Palaso.IO;

namespace Bloom
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

//            var programfiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
//            Skybound.Gecko.Xpcom.Initialize(Path.Combine(programfiles, "Mozilla Firefox")); //@"%ProgramFiles%\Mozilla Firefox");
			Skybound.Gecko.Xpcom.Initialize(Path.Combine(DirectoryOfTheApplicationExecutable, "xulrunner1.9.1"));

			var builder = new Autofac.ContainerBuilder();

			builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly()).InstancePerLifetimeScope();
			var projectDirectory = Path.Combine(GetTopAppDirectory(), "userProject");

			builder.RegisterType<Book>().InstancePerDependency();

			//builder.Register<Project.ProjectModel>(c => new Project.ProjectModel(projectDirectory));
			builder.Register<LibraryModel>(c => new LibraryModel(c.Resolve<BookSelection>(), projectDirectory, c.Resolve<TemplateCollectionList>(), c.Resolve<BookCollection.Factory>()));
			builder.Register<IFileLocator>(c => new FileLocator(new string[] { FactoryCollectionsDirectory }));

			builder.RegisterType<BookCollection>().InstancePerDependency();
			builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer(60)).InstancePerLifetimeScope();
			builder.RegisterType<PageListView>().InstancePerDependency();
			builder.RegisterType<TemplatePagesView>().InstancePerDependency();
			builder.RegisterType<ThumbNailList>().InstancePerDependency();
			builder.Register<TemplateCollectionList>(c=>
				 {
					 var l = new TemplateCollectionList(c.Resolve<Book.Factory>());
					 l.ReposistoryFolders = new string[] {FactoryCollectionsDirectory };
					 return l;
				 }).InstancePerLifetimeScope();

			builder.Register<ITemplateFinder>(c =>
				 {
					 return c.Resolve<TemplateCollectionList>();
				 }).InstancePerLifetimeScope();

   //       didn't give me the same one  builder.RegisterType<TemplateCollectionList>().As<ITemplateFinder>().InstancePerLifetimeScope();

			builder.RegisterGeneratedFactory(typeof(Project.ProjectView.Factory));
			builder.RegisterGeneratedFactory(typeof(Project.ProjectModel.Factory));
			builder.RegisterGeneratedFactory(typeof(LibraryListView.Factory));
			builder.RegisterGeneratedFactory(typeof(LibraryView.Factory));
			builder.RegisterGeneratedFactory(typeof(TemplateBookView.Factory));
			builder.RegisterGeneratedFactory(typeof(EditingView.Factory));
			builder.RegisterGeneratedFactory(typeof(EditingModel.Factory));
			builder.RegisterGeneratedFactory(typeof(PdfView.Factory));
			builder.RegisterGeneratedFactory(typeof(BookCollection.Factory));

			builder.RegisterGeneratedFactory(typeof(Book.Factory));
			var container = builder.Build();
			Application.Run(container.Resolve<Shell>());
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
