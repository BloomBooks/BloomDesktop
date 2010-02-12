using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autofac;

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

			builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly());

			builder.RegisterGeneratedFactory(typeof(Project.ProjectView.Factory));

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

	}
}
