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
                    new[]
                    {
                        typeof(CommonApi),
                        typeof(NewCollectionWizardApi),
                        typeof(CollectionChooserApi),
                        typeof(I18NApi),
                    }.Contains(t)
                );

            _container = builder.Build();

            // Only listen for the application exiting when there IS an application in the GUI sense.
            // A command-line verb never calls Application.Run, so the only WinForms message loop in the
            // process belongs to some worker -- currently the dedicated thread of the off-screen browser
            // PublishHelper uses for page checks, created and disposed once per batch. When that loop
            // ends, WinForms decides the application is exiting and raises ApplicationExit, mid-run. Acting
            // on that disposed this container, the parent scope of the still-in-use ProjectContext, so the
            // next artifact step died with ObjectDisposedException (BL-16668). Not subscribing is safe
            // because in that flow the container's lifetime is already bounded by the `using` blocks in the
            // CLI command handlers, and the process exits as soon as those unwind.
            //
            // One knock-on worth knowing: OnApplicationExit is the only caller of
            // Program.FinishLocalizationHarvesting(), so a command-line verb no longer runs it. That is
            // #if DEBUG code which does nothing unless LocalizationManager.IgnoreExistingEnglishTranslationFiles
            // is set, so release CLI runs are unaffected -- but a DEBUG localization-harvesting run driven
            // through a CLI verb would no longer merge the English translation files. If we ever want that,
            // call it from the CLI path explicitly rather than by leaning on a shutdown event that is not
            // really telling us the application is shutting down.
            if (!Program.RunningInConsoleMode)
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
            _container.Resolve<CollectionChooserApi>().RegisterWithApiHandler(server.ApiHandler);
            _container.Resolve<I18NApi>().RegisterWithApiHandler(server.ApiHandler);
            server.ApiHandler.RecordApplicationLevelHandlers();
        }

        /// <summary>
        /// The application is really shutting down, so tear the container down. Only ever subscribed
        /// when Bloom is running as a GUI application -- see the constructor for why.
        /// </summary>
        private void OnApplicationExit(object sender, EventArgs e)
        {
            Application.ApplicationExit -= OnApplicationExit;
            Program.FinishLocalizationHarvesting();
            Dispose();
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
