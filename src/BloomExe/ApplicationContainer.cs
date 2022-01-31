using System;
using System.Reflection;
using Autofac;
using Bloom.CollectionChoosing;
using Bloom.Properties;
using System.Linq;
using L10NSharp;
using System.Windows.Forms;
using Bloom.web.controllers;
using Bloom.Api;
using System.Globalization;

namespace Bloom
{
	/// <summary>
	/// This is sort of a wrapper around the DI container. I'm not thrilled with the name I've
	/// used (jh).
	/// </summary>
	public class ApplicationContainer : IDisposable
	{
		private IContainer _container;
		// We want a static singleton (BL-10731)
		private static BloomWebSocketServer _webSocketServer;
		// Used by AppApi so that the js side can get the port string through a BloomApi call.
		private static string _port;

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
			builder.Register<BloomWebSocketServer>(c => new BloomWebSocketServer()).SingleInstance();

			_container = builder.Build();

			_webSocketServer = _container.Resolve<BloomWebSocketServer>();
			_webSocketServer.Init();

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

		/// <summary>
		/// All Progress dialogs should use this web socket server.
		/// </summary>
		public static BloomWebSocketServer WebSocketServer => _webSocketServer;
		/// <summary>
		/// This will be something like: "ws://127.0.0.1:8102"
		/// </summary>
		public static string Port
		{
			get { return _port; }
			set { _port = value; }
		}

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
			_webSocketServer = null;

			GC.SuppressFinalize(this);
		}

		public ProjectContext CreateProjectContext(string projectPath, bool justEnoughForHtmlDialog = false)
		{
			return new ProjectContext(projectPath, _container, justEnoughForHtmlDialog);
		}
	}
}
