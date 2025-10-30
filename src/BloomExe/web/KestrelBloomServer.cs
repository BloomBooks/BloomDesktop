// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using DesktopAnalytics;
using L10NSharp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIL.PlatformUtilities;
using SIL.Reporting;

namespace Bloom.web
{
    /// <summary>
    /// ASP.NET Kestrel-based implementation of the Bloom local HTTP server.
    /// Phase 2.1 Implementation: Minimal server with port discovery and lifecycle.
    /// </summary>
    public class KestrelBloomServer : IBloomServer, IDisposable
    {
        #region Static Properties

        public static int portForHttp;
        public static string ServerUrl =>
            "http://localhost:" + portForHttp.ToString(CultureInfo.InvariantCulture);
        public static string ServerUrlEndingInSlash => ServerUrl + "/";
        public static string ServerUrlWithBloomPrefixEndingInSlash => ServerUrl + "/bloom/";
        public static bool ServerIsListening { get; internal set; }
        internal static KestrelBloomServer _theOneInstance { get; private set; }

        #endregion

        #region Instance Fields

        private IHost _host;
        private CancellationTokenSource _shutdownTokenSource;
        private readonly RuntimeImageProcessor _cache;
        private readonly BookSelection _bookSelection;
        private BloomFileLocator _fileLocator;
        private readonly BloomApiHandler _apiHandler;
        public BloomApiHandler ApiHandler
        {
            get
            {
                if (_apiHandler != null)
                    return _apiHandler;

                if (_host != null)
                {
                    var resolvedHandler = _host.Services.GetService<BloomApiHandler>();
                    if (resolvedHandler != null)
                        return resolvedHandler;
                }

                throw new InvalidOperationException(
                    "BloomApiHandler is not available. Provide one via the constructor before accessing it."
                );
            }
        }

        public CollectionSettings CurrentCollectionSettings { get; private set; }

        #endregion

        #region Constants

        private const int kNumberOfPortsToTry = 20;

        #endregion

        #region Constructor

        public KestrelBloomServer(
            RuntimeImageProcessor cache,
            BookSelection bookSelection,
            BloomFileLocator fileLocator,
            BloomApiHandler apiHandler = null
        )
        {
            _cache = cache;
            _bookSelection = bookSelection;
            _fileLocator = fileLocator;
            _apiHandler = apiHandler;
            _theOneInstance = this;
        }

        public void SetCollectionSettingsDuringInitialization(CollectionSettings collectionSettings)
        {
            CurrentCollectionSettings = collectionSettings;
            ApiHandler.SetCollectionSettingsDuringInitialization(collectionSettings);
        }

        internal void SetFileLocator(BloomFileLocator fileLocator)
        {
            _fileLocator = fileLocator;
        }

        internal BloomFileLocator FileLocatorForTests => _fileLocator;

        #endregion

        #region Lifecycle Management

        /// <summary>
        /// Starts the server listening on an available port (8089+).
        /// This method is idempotent - calling it multiple times will not restart the server.
        /// </summary>
        public virtual void EnsureListening()
        {
            if (ServerIsListening)
                return;

            if (_fileLocator == null)
            {
                throw new InvalidOperationException(
                    "BloomFileLocator has not been configured. Call SetFileLocator before starting the Kestrel server."
                );
            }

            var success = false;
            const int kStartingPort = 8089;

            // Try to start server on ports 8089, 8091, 8093, 8095, etc. (by 2s for WebSocket compatibility)
            for (var i = 0; !success && i < kNumberOfPortsToTry; i++)
            {
                portForHttp = kStartingPort + (i * 2);
                success = AttemptToStartServer();
            }

            if (!success)
            {
                CheckForZoneAlarm();
                var message =
                    $"Could not start the Bloom server. Attempted {kNumberOfPortsToTry} ports.";
                ErrorReport.NotifyUserOfProblem(message);
                throw new Exception(message);
            }

            VerifyWeAreNowListening();
            ServerIsListening = true;
        }

        /// <summary>
        /// Attempts to start the ASP.NET Core Kestrel server on the current portForHttp.
        /// </summary>
        private bool AttemptToStartServer()
        {
            try
            {
                _shutdownTokenSource = new CancellationTokenSource();

                var hostBuilder = Host.CreateDefaultBuilder()
                    .ConfigureServices(services =>
                    {
                        // Phase 4.1: Register core Bloom services using extension methods
                        services.AddBloomApplicationServices();
                        services.AddBloomLogging();
                        services.AddBloomMiddlewareServices();

                        // Override with instance-specific services if provided to constructor
                        // This maintains backward compatibility with tests that inject specific instances
                        if (_bookSelection != null)
                            services.AddSingleton(_bookSelection);
                        if (_cache != null)
                            services.AddSingleton(_cache);
                        if (_fileLocator != null)
                            services.AddSingleton(_fileLocator);
                        if (_apiHandler != null)
                            services.AddSingleton(_apiHandler);
                    })
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder
                            .ConfigureServices(services =>
                            {
                                // Add routing services for endpoint routing to work
                                services.AddRouting();
                            })
                            .ConfigureKestrel(serverOptions =>
                            {
                                // Existing IRequestInfo implementations perform synchronous writes.
                                // AllowSynchronousIO keeps that behavior working while we migrate APIs.
                                serverOptions.AllowSynchronousIO = true;
                                serverOptions.ListenLocalhost(portForHttp);
                            })
                            .Configure(app =>
                            {
                                var apiHandler =
                                    app.ApplicationServices.GetRequiredService<BloomApiHandler>();
                                ServiceCollectionExtensions.RegisterApiHandlers(
                                    app.ApplicationServices,
                                    apiHandler,
                                    isApplicationLevel: true
                                );

                                app.UseRouting();

                                app.UseKestrelRecursiveRequestMiddleware();
                                app.UseMiddleware<KestrelApiMiddleware>();
                                app.UseMiddleware<KestrelCssProcessingMiddleware>();
                                app.UseMiddleware<KestrelStaticFileMiddleware>();

                                app.UseEndpoints(endpoints =>
                                {
                                    endpoints.MapGet(
                                        "/",
                                        async context =>
                                        {
                                            context.Response.ContentType = "text/html";
                                            await context.Response.WriteAsync(
                                                "<html><body><h1>Bloom Server</h1></body></html>"
                                            );
                                        }
                                    );

                                    endpoints.MapGet(
                                        "/testconnection",
                                        async context =>
                                        {
                                            context.Response.ContentType = "text/plain";
                                            await context.Response.WriteAsync("OK");
                                        }
                                    );
                                });
                            });
                    });

                _host = hostBuilder.Build();
                var startTask = _host.StartAsync(_shutdownTokenSource.Token);

                // Wait up to 5 seconds for the server to start
                if (startTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    // Check if the task completed successfully (no exceptions)
                    if (startTask.IsCompletedSuccessfully)
                    {
                        // Give the server a moment to be fully ready to accept connections
                        Thread.Sleep(100);
                        return true;
                    }
                    else
                    {
                        // StartAsync completed but with an error
                        Debug.WriteLine(
                            $"[ERROR] StartAsync failed: {startTask.Exception?.GetBaseException()?.Message}"
                        );
                        _host?.Dispose();
                        _host = null;
                        return false;
                    }
                }

                // Timeout waiting for server to start
                _host?.Dispose();
                _host = null;
                return false;
            }
            catch
            {
                // Port is probably in use, return false to try the next port
                _host?.Dispose();
                _host = null;
                return false;
            }
        }

        /// <summary>
        /// Verifies that the server is now listening by making a test connection.
        /// Retries a few times to account for startup delays.
        /// </summary>
        private void VerifyWeAreNowListening()
        {
            const int maxRetries = 5;
            const int retryDelayMs = 200;
            Exception lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (
                        var client = new System.Net.Http.HttpClient
                        {
                            Timeout = TimeSpan.FromSeconds(2),
                        }
                    )
                    {
                        var task = client.GetAsync($"{ServerUrl}/testconnection");
                        var completed = task.Wait(TimeSpan.FromSeconds(3));
                        Debug.WriteLine(
                            $"[VERIFY] Attempt {i + 1}: completed={completed}, status={task.Status}"
                        );

                        if (!completed)
                        {
                            lastException = new TimeoutException(
                                "HTTP request did not complete in allotted time"
                            );
                        }
                        else if (task.IsFaulted)
                        {
                            lastException =
                                task.Exception?.GetBaseException()
                                ?? new Exception("HTTP request faulted");
                        }
                        else if (task.Result.IsSuccessStatusCode)
                        {
                            Debug.WriteLine("[VERIFY] Server responded successfully.");
                            return; // Success!
                        }
                        else
                        {
                            lastException = new Exception(
                                $"Server returned status code {(int)task.Result.StatusCode}"
                            );
                        }
                    }
                }
                catch (Exception e)
                {
                    lastException = e;
                }

                if (i < maxRetries - 1)
                {
                    Thread.Sleep(retryDelayMs);
                }
            }

            throw new Exception(
                $"Failed to verify server is listening on {ServerUrl} after {maxRetries} attempts",
                lastException
            );
        }

        /// <summary>
        /// Stops the server gracefully.
        /// </summary>
        public virtual void Stop()
        {
            try
            {
                if (_host != null)
                {
                    _shutdownTokenSource?.Cancel();
                    _host.StopAsync().Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Error stopping server: {e.Message}");
            }
        }

        /// <summary>
        /// Disposes all resources used by the server.
        /// </summary>
        public virtual void Dispose()
        {
            Stop();

            try
            {
                _host?.Dispose();
                _shutdownTokenSource?.Dispose();
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Error disposing server: {e.Message}");
            }

            ServerIsListening = false;
        }

        #endregion

        #region IBloomServer Implementation

        public virtual void RegisterThreadBlocking()
        {
            // Kestrel handles threading automatically, so this is a no-op
        }

        public virtual void RegisterThreadUnblocked()
        {
            // Kestrel handles threading automatically, so this is a no-op
        }

        #endregion

        #region Private Utilities

        /// <summary>
        /// Checks for Zone Alarm firewall and logs an error if found.
        /// </summary>
        private void CheckForZoneAlarm()
        {
            try
            {
                // Check if ZoneAlarm is running
                var zoneAlarmProcess = System
                    .Diagnostics.Process.GetProcessesByName("ZoneAlarm")
                    .FirstOrDefault();
                if (zoneAlarmProcess != null)
                {
                    ErrorReport.NotifyUserOfProblem(
                        "The ZoneAlarm firewall may be blocking the Bloom server. "
                            + "Please check ZoneAlarm settings or try disabling it temporarily."
                    );
                }
            }
            catch
            {
                // Ignore errors checking for ZoneAlarm
            }
        }

        #endregion
    }
}
