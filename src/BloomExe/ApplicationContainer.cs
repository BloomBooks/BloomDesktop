using System;
using System.Reflection;
using Autofac;
using Bloom.CollectionChoosing;
using Bloom.Properties;
using System.Linq;
using L10NSharp;
using System.Windows.Forms;
using Bloom.web.controllers;

namespace Bloom
{
		/// <summary>
		/// This is sort of a wrapper around the DI container. I'm not thrilled with the name I've
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

				builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer()).SingleInstance();
				builder.Register<BookThumbNailer>(c => new BookThumbNailer(c.Resolve<HtmlThumbNailer>())).SingleInstance();

				_container = builder.Build();

				Application.ApplicationExit += OnApplicationExit;
			}

			private void OnApplicationExit(object sender, EventArgs e)
			{
				Application.ApplicationExit -= OnApplicationExit;
				Program.FinishLocalizationHarvesting();
				Dispose();
			}

			public OpenAndCreateCollectionDialog OpenAndCreateCollectionDialog()
			{
				return _container.Resolve<OpenAndCreateCollectionDialog>();
			}

			public ILocalizationManager LocalizationManager;

			public HtmlThumbNailer HtmlThumbnailer => _container.Resolve<HtmlThumbNailer>();

			public BookThumbNailer BookThumbNailer => _container.Resolve<BookThumbNailer>();

			internal ProblemReportApi ProblemReportApi => _container.Resolve<ProblemReportApi>();

			public void Dispose()
			{
				// Disposing the container results in disposing of the objects that
				// support requests to localize strings. But sometimes such a request
				// is still pending, perhaps from a browser queued in our server.
				// We don't want an exception thrown if the request reaches the LM
				// after things are disposed.
				L10NSharp.LocalizationManager.ThrowIfManagerDisposed = false;
				_container?.Dispose();
				_container = null;

				GC.SuppressFinalize(this);
			}

			public ProjectContext CreateProjectContext(string projectPath, bool justEnoughForHtmlDialog = false)
			{
				return new ProjectContext(projectPath, _container, justEnoughForHtmlDialog);
			}
		}
	}
