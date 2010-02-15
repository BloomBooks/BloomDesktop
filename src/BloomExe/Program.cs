using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autofac;
using Bloom.Edit;
using Bloom.Library;
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

			builder.Register<Project.ProjectModel>(c => new Project.ProjectModel(projectDirectory));
			builder.Register<LibraryModel>(c => new LibraryModel(c.Resolve<BookSelection>(), projectDirectory, FactoryCollectionsDirectory, c.Resolve<BookCollection.Factory>()));
			builder.Register<IFileLocator>(c => new FileLocator(new string[] { FactoryCollectionsDirectory }));

			builder.RegisterType<Book>().InstancePerDependency();
			builder.RegisterType<BookCollection>().InstancePerDependency();

			builder.RegisterGeneratedFactory(typeof(Project.ProjectView.Factory));
			builder.RegisterGeneratedFactory(typeof(LibraryListView.Factory));
			builder.RegisterGeneratedFactory(typeof(LibraryView.Factory));
			builder.RegisterGeneratedFactory(typeof(TemplateBookView.Factory));
			builder.RegisterGeneratedFactory(typeof(EditingView.Factory));
			builder.RegisterGeneratedFactory(typeof(EditingModel.Factory));
			builder.RegisterGeneratedFactory(typeof(PdfModel.Factory));
			builder.RegisterGeneratedFactory(typeof(PdfView.Factory));
			builder.RegisterGeneratedFactory(typeof(Book.Factory));
			builder.RegisterGeneratedFactory(typeof(BookCollection.Factory));

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
