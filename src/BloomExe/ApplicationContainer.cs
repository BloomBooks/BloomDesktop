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
using Bloom.Utils;


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
				// We filter classes that don't have any (public) constructors because, where earlier versions just
				// ignored them, Autofac 6 crashes.
				builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly()).Where(t=> t.GetConstructors().Any());

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
			// June 2018: we decided that actually no code other than the Browser class even needs to know that
			// this thing exists, so lots of code using this can be removed. But that will be done with BL-6069.
			builder.Register(c =>  NavigationIsolator.GetOrCreateTheOneNavigationIsolator()).InstancePerLifetimeScope();

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

			public ILocalizationManager LocalizationManager;


			public HtmlThumbNailer HtmlThumbnailer { get { return _container.Resolve<HtmlThumbNailer>();}}

			public BookThumbNailer BookThumbNailer { get { return _container.Resolve<BookThumbNailer>(); }}

			public void Dispose()
			{
				if (_container != null)
					_container.Dispose();
				_container = null;

				GC.SuppressFinalize(this);
			}

			public ProjectContext CreateProjectContext(string projectPath, bool justEnoughForHtmlDialog = false)
			{
				return new ProjectContext(projectPath, _container, justEnoughForHtmlDialog);
			}
		}
	}
