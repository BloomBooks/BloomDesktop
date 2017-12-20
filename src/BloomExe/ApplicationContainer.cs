using System;
using System.Reflection;
using Autofac;
using Bloom.CollectionChoosing;
using Bloom.Properties;
using Bloom.ToPalaso;
using System.Linq;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using SIL.Reporting;
using System.Windows.Forms;


namespace Bloom
{
		/// <summary>
		/// This is sortof a wrapper around the DI container. I'm not thrilled with the name I've
		/// used (jh).
		/// </summary>
		public class ApplicationContainer : IDisposable
		{
			private IContainer _container;

			public ApplicationContainer()
			{
				var builder = new ContainerBuilder();
				//builder.RegisterModule<WhiteboxProfilingModule>();

				//default to InstancePerDependency, i.e., they it will make a new
				//one each time someone asks for one
				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly());

				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
					.Where(t => t.GetInterfaces().Contains(typeof(ICommand))).InstancePerLifetimeScope();

				builder.Register(c => LocalizationManager).SingleInstance();

				if (Settings.Default.MruProjects==null)
				{
					Settings.Default.MruProjects = new MostRecentPathsList();
				}
				builder.RegisterInstance(Settings.Default.MruProjects).SingleInstance();

				//this is to prevent some problems we were getting while waiting for a browser to navigate and being forced to call Application.DoEvents().
				//HtmlThumbnailer & ConfigurationDialog, at least, use this.
				builder.Register(c => new NavigationIsolator()).InstancePerLifetimeScope();

				builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer(c.Resolve<NavigationIsolator>())).SingleInstance();
				builder.Register<BookThumbNailer>(c => new BookThumbNailer(c.Resolve<HtmlThumbNailer>())).SingleInstance();

				_container = builder.Build();

				Application.ApplicationExit += OnApplicationExit;
			}

			private void OnApplicationExit(object sender, EventArgs e)
			{
				Application.ApplicationExit -= OnApplicationExit;
				Bloom.Program.FinishLocalizationHarvesting();
				Dispose();
			}

			public OpenAndCreateCollectionDialog OpenAndCreateCollectionDialog()
			{
				return _container.Resolve<OpenAndCreateCollectionDialog>();
			}

			public LocalizationManager LocalizationManager;

			public HtmlThumbNailer HtmlThumbnailer { get { return _container.Resolve<HtmlThumbNailer>();}}

			public BookThumbNailer BookThumbNailer { get { return _container.Resolve<BookThumbNailer>(); }}

			public void Dispose()
			{
				if (_container != null)
					_container.Dispose();
				_container = null;

				GC.SuppressFinalize(this);
			}

			public ProjectContext CreateProjectContext(string projectPath)
			{
				return new ProjectContext(projectPath, _container);
			}
		}
	}
