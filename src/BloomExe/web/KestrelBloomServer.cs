// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Diagnostics;
using System.Globalization;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SIL.PlatformUtilities;
using SIL.Reporting;

namespace Bloom.Api
{
    /// <summary>
    /// ASP.NET Kestrel-based implementation of the Bloom local HTTP server.
    /// Phase 2.1 Implementation: Minimal server with port discovery and lifecycle.
    /// </summary>
    public class KestrelBloomServer : IBloomServer, IDisposable
    {
        #region Static Properties

        public static int portForHttp;
        public static string ServerUrl => "http://localhost:" + portForHttp.ToString(CultureInfo.InvariantCulture);
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
        private readonly BloomFileLocator _fileLocator;
        private readonly BloomApiHandler _apiHandler;

        #endregion

        #region Constants

        private const int kNumberOfPortsToTry = 20;

        #endregion

        #region Constructor

        public KestrelBloomServer(
            RuntimeImageProcessor cache,
            BookSelection bookSelection,
            BloomFileLocator fileLocator,
            BloomApiHandler apiHandler = null)
        {
            _cache = cache;
            _bookSelection = bookSelection;
            _fileLocator = fileLocator;
            _apiHandler = apiHandler;
            _theOneInstance = this;
        }

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
                var message = $"Could not start the Bloom server. Attempted {kNumberOfPortsToTry} ports.";
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
                        services.AddSingleton(_bookSelection);
                        services.AddSingleton(_cache);
                        services.AddSingleton(_fileLocator);
                        services.AddSingleton(_apiHandler);
                    })
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder
                            .UseKestrel(options => options.Listen(IPAddress.Loopback, portForHttp))
                            .Configure(app =>
                            {
                                app.UseRouting();
                                app.UseEndpoints(endpoints =>
                                {
                                    endpoints.MapGet("/", async context =>
                                    {
                                        context.Response.ContentType = "text/html";
                                        await context.Response.WriteAsync("<html><body><h1>Bloom Server</h1></body></html>");
                                    });

                                    endpoints.MapGet("/testconnection", async context =>
                                    {
                                        context.Response.ContentType = "text/plain";
                                        await context.Response.WriteAsync("OK");
                                    });

                                    // Placeholder for future /bloom/api/* routing
                                    // Will be implemented in Phase 2.2
                                });
                            });
                    });

                _host = hostBuilder.Build();
                var startTask = _host.StartAsync(_shutdownTokenSource.Token);

                // Wait up to 5 seconds for the server to start
                if (startTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    return true;
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
        /// </summary>
        private void VerifyWeAreNowListening()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var result = client.GetAsync($"{ServerUrl}/testconnection").Result;
                    if (!result.IsSuccessStatusCode)
                        throw new Exception("Server returned non-success status code");
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to verify server is listening on {ServerUrl}", e);
            }
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
                var zoneAlarmProcess = System.Diagnostics.Process.GetProcessesByName("ZoneAlarm").FirstOrDefault();
                if (zoneAlarmProcess != null)
                {
                    ErrorReport.NotifyUserOfProblem(
                        "The ZoneAlarm firewall may be blocking the Bloom server. " +
                        "Please check ZoneAlarm settings or try disabling it temporarily.");
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
