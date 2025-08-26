using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autofac;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionChoosing;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.web.controllers;
using L10NSharp;

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
            builder
                .RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetConstructors().Any());

            builder
                .RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetInterfaces().Contains(typeof(ICommand)))
                .InstancePerLifetimeScope();

            if (Settings.Default.MruProjects == null)
            {
                Settings.Default.MruProjects = new MostRecentPathsList();
            }
            builder.RegisterInstance(Settings.Default.MruProjects).SingleInstance();

            builder.Register<HtmlThumbNailer>(c => new HtmlThumbNailer()).SingleInstance();
            builder
                .Register<BookThumbNailer>(c => new BookThumbNailer(c.Resolve<HtmlThumbNailer>()))
                .SingleInstance();

            var bookRenameEvent = new BookRenamedEvent();
            builder.Register(c => bookRenameEvent).AsSelf().InstancePerLifetimeScope();
            builder.Register<BookSelection>(c => new BookSelection()).SingleInstance();
            builder
                .Register<BloomServer>(c => new BloomServer(
                    new RuntimeImageProcessor(bookRenameEvent),
                    c.Resolve<BookSelection>()
                ))
                .SingleInstance();

            //Other classes which are also singletons for the whole application
            builder
                .RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                // Not InstancePerLifetimeScope! Although that would make for a singleton at the application level,
                // if one of these objects is requested from the child scope ProjectContext, it would make an independent
                // instance (possibly every time it is asked for one, since ProjectContext has not been told to only make
                // one). Singleton seems to be a much stronger constraint that forces a single one for this and all child
                // containers, which is what we want for all the application singletons.
                .SingleInstance()
                .Where(t =>
                    new[] { typeof(CommonApi), typeof(NewCollectionWizardApi) }.Contains(t)
                );

            _container = builder.Build();

            Application.ApplicationExit += OnApplicationExit;

            // Register the API Handlers that are global to the application (not dependent on knowing a particular project).
            // Note: it is is a work in progress to transfer more API handlers from ProjectContext to here.
            // Ideally, nothing in BloomServer, and hence not in any API handler, would know the current project.
            // Any API call whose answer is project-dependent would pass a project identifier. Then all the
            // handlers could all be registered here (and created by the ApplicationContainer). It's likely
            // that a lot more could already be moved, but so far we just did enough for the handful of dialogs
            // that need to work independent of a project.
            var server = _container.Resolve<BloomServer>();
            _container.Resolve<CommonApi>().RegisterWithApiHandler(server.ApiHandler);
            _container.Resolve<NewCollectionWizardApi>().RegisterWithApiHandler(server.ApiHandler);
            server.ApiHandler.RecordApplicationLevelHandlers();
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

        public HtmlThumbNailer HtmlThumbnailer => _container.Resolve<HtmlThumbNailer>();

        public BookThumbNailer BookThumbNailer => _container.Resolve<BookThumbNailer>();

        internal ProblemReportApi ProblemReportApi => _container.Resolve<ProblemReportApi>();

        public BloomServer BloomServer => _container.Resolve<BloomServer>();

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

        public ProjectContext CreateProjectContext(
            string projectPath,
            bool justEnoughForHtmlDialog = false
        )
        {
            return new ProjectContext(projectPath, _container, justEnoughForHtmlDialog);
        }
    }
}
