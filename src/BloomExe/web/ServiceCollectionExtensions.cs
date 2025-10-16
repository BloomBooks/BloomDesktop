using System;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.web.controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bloom.web
{
    /// <summary>
    /// Extension methods for registering Bloom services with ASP.NET Core DI container.
    /// This provides service registration for KestrelBloomServer without replacing the
    /// existing Autofac-based ApplicationContainer.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Register core Bloom services that are needed for the application lifetime.
        /// These are application-level services that don't depend on a specific project/collection.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddBloomApplicationServices(
            this IServiceCollection services
        )
        {
            // Register events
            services.AddSingleton<BookRenamedEvent>();

            // Register core singletons
            services.AddSingleton<BookSelection>();
            services.AddSingleton<RuntimeImageProcessor>(sp =>
            {
                var bookRenamedEvent = sp.GetRequiredService<BookRenamedEvent>();
                return new RuntimeImageProcessor(bookRenamedEvent);
            });

            // Register API handler
            services.AddSingleton<BloomApiHandler>();

            // Register thumbnailers
            services.AddSingleton<HtmlThumbNailer>();
            services.AddSingleton<BookThumbNailer>(sp =>
            {
                var htmlThumbnailer = sp.GetRequiredService<HtmlThumbNailer>();
                return new BookThumbNailer(htmlThumbnailer);
            });

            // Register application-level API controllers
            services.AddSingleton<CommonApi>();
            services.AddSingleton<NewCollectionWizardApi>();

            return services;
        }

        /// <summary>
        /// Register project/collection-level services that depend on a specific collection.
        /// These should be registered in a scoped lifetime when a project is loaded.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="collectionSettings">The collection settings for the current project</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddBloomProjectServices(
            this IServiceCollection services,
            CollectionSettings collectionSettings
        )
        {
            if (collectionSettings == null)
            {
                throw new ArgumentNullException(nameof(collectionSettings));
            }

            // Register CollectionSettings as a scoped singleton for this project
            services.AddScoped<CollectionSettings>(sp => collectionSettings);

            // Register BloomFileLocator (project-dependent)
            services.AddScoped<BloomFileLocator>(sp =>
            {
                var settings = sp.GetRequiredService<CollectionSettings>();
                // Note: In a full implementation, we would need to resolve the XMatterPackFinder
                // and other dependencies. For now, this is a placeholder that shows the pattern.
                // The actual implementation should match the constructor call pattern from
                // the existing code (see ProjectContext.BuildSubContainerForThisProject).
                return new BloomFileLocator(
                    settings,
                    null, // XMatterPackFinder - will need proper initialization
                    ProjectContext.GetFactoryFileLocations(),
                    ProjectContext.GetFoundFileLocations(),
                    ProjectContext.GetAfterXMatterFileLocations()
                );
            });

            return services;
        }

        /// <summary>
        /// Configure logging for Bloom services.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddBloomLogging(this IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Information);

#if DEBUG
                builder.SetMinimumLevel(LogLevel.Debug);
#endif
            });

            return services;
        }

        /// <summary>
        /// Register middleware-related services (request adapters, etc.)
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddBloomMiddlewareServices(
            this IServiceCollection services
        )
        {
            // Register file location service (Phase 6.2)
            services.AddSingleton<IFileLocationService, FileLocationService>();

            return services;
        }

        /// <summary>
        /// Helper method to initialize and register API handlers with BloomApiHandler.
        /// This maintains compatibility with the existing handler registration pattern.
        /// </summary>
        /// <param name="serviceProvider">The service provider</param>
        /// <param name="apiHandler">The BloomApiHandler instance</param>
        /// <param name="isApplicationLevel">True for application-level handlers, false for project-level</param>
        public static void RegisterApiHandlers(
            IServiceProvider serviceProvider,
            BloomApiHandler apiHandler,
            bool isApplicationLevel
        )
        {
            if (isApplicationLevel)
            {
                // Register application-level API handlers
                var commonApi = serviceProvider.GetService<CommonApi>();
                commonApi?.RegisterWithApiHandler(apiHandler);

                var wizardApi = serviceProvider.GetService<NewCollectionWizardApi>();
                wizardApi?.RegisterWithApiHandler(apiHandler);

                // Mark these as application-level so they persist across project changes
                apiHandler.RecordApplicationLevelHandlers();
            }
            else
            {
                // Project-level handlers would be registered here
                // This would be called when a project context is established
                // Future: Add project-level API handler registration
            }
        }
    }
}
