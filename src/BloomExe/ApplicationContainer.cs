using System;
using System.Reflection;
using Autofac;
using Bloom.CollectionChoosing;
using Bloom.Properties;
using Bloom.ToPalaso;
using System.Linq;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using NetSparkle;
using Palaso.Reporting;


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

				builder.Register<Sparkle>(c =>
				{
					string url;
					try
					{
						var updateTable = new UpdateVersionTable();
						url = updateTable.GetAppcastUrl();
					}
					catch (Exception)
					{
						url = "";
						Logger.WriteEvent("Could not retrieve UpdateVersionTable from the internet");
					}
					var s =new Sparkle(url,Resources.Bloom);
					s.CustomInstallerArguments = "/qb";
					s.DoLaunchAfterUpdate = false;
					return s;
				}).InstancePerLifetimeScope();

				builder.Register(c => LocalizationManager).SingleInstance();
				builder.Register(c => new DownloadOrderList()).SingleInstance();

				if (Settings.Default.MruProjects==null)
				{
					Settings.Default.MruProjects = new MostRecentPathsList();
				}
				builder.RegisterInstance(Settings.Default.MruProjects).SingleInstance();

				//this is to prevent some problems we were getting while waiting for a browser to navigate and being forced to call Application.DoEvents().
				//HtmlThumbnailer & ConfigurationDialog, at least, use this.
				builder.Register(c => new MonitorTarget()).InstancePerLifetimeScope();

				const int kListViewIconHeightAndWidth = 70;
				builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer(kListViewIconHeightAndWidth, kListViewIconHeightAndWidth, c.Resolve<MonitorTarget>())).SingleInstance();

				_container = builder.Build();
			}

			public OpenAndCreateCollectionDialog OpenAndCreateCollectionDialog()
			{
				return _container.Resolve<OpenAndCreateCollectionDialog>();
			}

			public Sparkle ApplicationUpdator
			{
				get { return _container.Resolve<Sparkle>(); }
			}

			public LocalizationManager LocalizationManager;

			public DownloadOrderList DownloadOrderList
			{
				get { return _container.Resolve<DownloadOrderList>(); }
			}

			public HtmlThumbNailer HtmlThumbnailer { get { return _container.Resolve<HtmlThumbNailer>();}}

			public void Dispose()
			{
				_container.Dispose();
				_container = null;
			}

			public ProjectContext CreateProjectContext(string projectPath)
			{
				return new ProjectContext(projectPath, _container);
			}
		}
	}
