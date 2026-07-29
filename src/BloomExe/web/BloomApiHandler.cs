// Copyright (c) 2018 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;

namespace Bloom.Api
{
    /// <summary>
    /// This class handles the API (non-file) requests to the Bloom localhost HTTP server.
    /// Most of the API requests are handled by separate classes that register API calls
    /// with this class.  There are a couple of older API requests that are handled directly
    /// in this class's ProcessRequestAsync method (i18n/ and directoryWatcher/).
    ///
    /// This class also handles thread synchronization for those API requests that need it.
    /// </summary>
    public class BloomApiHandler
    {
        // This one can really be used as a dictionary, because the keys match exactly, not just as a prefix.
        private Dictionary<string, BaseEndpointRegistration> _exactEndpointRegistrations =
            new Dictionary<string, BaseEndpointRegistration>();

        private readonly object _endpointRegistrationsLock = new object();

        // Endpoints that are registered by the application container rather than the project context
        // and so should not get cleared when we switch project context
        private HashSet<string> _applicationLevelRegistrationKeys = new HashSet<string>();

        // Special lock for making thumbnails. See discussion at the one point of usage.
        private readonly SemaphoreSlim ThumbnailsAndPreviewsSyncObj = new SemaphoreSlim(1, 1);

        // We use two different locks to synchronize access to API requests.
        // This allows certain methods to run concurrently.
        private readonly SemaphoreSlim I18NLock = new SemaphoreSlim(1, 1); // used to synchronize access to I18N methods
        private readonly SemaphoreSlim SyncObj = new SemaphoreSlim(1, 1); // used to synchronize access to various other methods
        private readonly BookSelection _bookSelection;

        public CollectionSettings CurrentCollectionSettings { get; private set; }

        public BloomApiHandler(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

        /// <summary>
        /// Only used in BloomServer.SetCollectionSettingsDuringInitialization. See comment there.
        /// </summary>
        /// <param name="collectionSettings"></param>
        public void SetCollectionSettingsDuringInitialization(CollectionSettings collectionSettings)
        {
            CurrentCollectionSettings = collectionSettings;
        }

        /// <summary>
        /// Mark all already-registered handlers as application-level handlers.
        /// </summary>
        public void RecordApplicationLevelHandlers()
        {
            lock (_endpointRegistrationsLock)
            {
                _applicationLevelRegistrationKeys = new HashSet<string>(
                    _exactEndpointRegistrations.Keys
                );
            }
        }

        /// <summary>
        /// Clear all handlers that were not marked as application level handlers
        /// </summary>
        public void ClearProjectLevelHandlers()
        {
            lock (_endpointRegistrationsLock)
            {
                foreach (var key in _exactEndpointRegistrations.Keys.ToList())
                {
                    if (!_applicationLevelRegistrationKeys.Contains(key))
                    {
                        _exactEndpointRegistrations.Remove(key);
                    }
                }
            }
        }

        public void ClearEndpointHandlers()
        {
            lock (_endpointRegistrationsLock)
            {
                _exactEndpointRegistrations.Clear();
            }
        }

        /// <summary>
        /// Register some code that should be executed when a client (i.e. javascript) does an HTTP api call
        /// with a particular bloom/api URL.
        /// </summary>
        /// <param name="pattern">Simple string identifying the API that this can handle.
        /// This must match what comes after the "...bloom/api/" of the URL(not counting any params, i.e., anything after the ?)</param>
        /// <param name="handler">The method to call</param>
        /// <param name="handleOnUiThread">If true, the current thread will suspend until the UI thread can be used to call the method.
        /// This deliberately no longer has a default. It's something that should be thought about.
        /// Making it true can kill performance if you don't need it (BL-3452), and complicates exception handling and problem reporting (BL-4679).
        /// There's also danger of deadlock if something in the UI thread is somehow waiting for this request to complete.
        /// But, beware of race conditions or anything that manipulates UI controls if you make it false.</param>
        /// <param name="requiresSync">True if the handler wants the server to ensure no other thread is doing an api
        /// call while this one is running. This is our default behavior, ensuring that no API request can interfere with any
        /// other in any unexpected way...essentially all Bloom's data is safe from race conditions arising from
        /// server threads manipulating things on background threads. However, it makes it impossible for a new
        /// api call to interrupt a previous one. For example, when one api call is creating an epub preview
        /// and we get a new one saying we need to abort that (because one of the property buttons has changed),
        /// the epub that is being generated is obsolete and we want the new api call to go ahead so it can set a flag
        /// to abort the one in progress. To avoid race conditions, api calls that set requiresSync false should be kept small
        /// and simple and be very careful about touching objects that other API calls might interact with.</param>
        /// <remarks>This method name was previously used for the method now called RegisterEndpointLegacy,
        /// in which the pattern argument is regex-matched against the part of the URL between /bloom/api/ and the ?.
        /// That was very inefficient and should be used only where necessary. However, remember that this
        /// updated API can only be called if the pattern (prefixed by /bloom/api/) matches the entire localPath.</remarks>
        public EndpointRegistration RegisterEndpointHandler(
            string pattern,
            EndpointHandler handler,
            bool handleOnUiThread,
            bool requiresSync = true
        )
        {
            var registration = new EndpointRegistration()
            {
                Handler = handler,
                HandleOnUIThread = handleOnUiThread,
                RequiresSync = requiresSync,
                MeasurementLabel = pattern, // can be overridden... this is just a default
            };
            lock (_endpointRegistrationsLock)
            {
                _exactEndpointRegistrations.Add(
                    pattern.ToLowerInvariant().Trim(new char[] { '/' }),
                    registration
                );
            }
            return registration; // return it so the caller can say  RegisterEndpointHandler().Measurable();
        }

        public BaseEndpointRegistration RegisterAsyncEndpointHandler(
            string pattern,
            Func<ApiRequest, Task> handler,
            bool handleOnUiThread,
            bool requiresSync = true
        )
        {
            var registration = new AsyncEndpointRegistration()
            {
                Handler = handler,
                HandleOnUIThread = handleOnUiThread,
                RequiresSync = requiresSync,
                MeasurementLabel = pattern, // can be overridden... this is just a default
            };
            lock (_endpointRegistrationsLock)
            {
                _exactEndpointRegistrations.Add(
                    pattern.ToLowerInvariant().Trim(new char[] { '/' }),
                    registration
                );
            }
            return registration; // return it so the caller can say  RegisterEndpointHandler().Measurable();
        }

        /// <summary>
        /// Handle simple boolean reads/writes where the 'pattern' is exactly equal to the localPath of the URI to be handled
        /// (after removing the initial /bloom/api/, which should be in the URI but not in the pattern here).
        /// </summary>
        public void RegisterBooleanEndpointHandler(
            string pattern,
            Func<ApiRequest, bool> readAction,
            Action<ApiRequest, bool> writeAction,
            bool handleOnUiThread,
            bool requiresSync = true
        )
        {
            RegisterEndpointHandler(
                pattern,
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithBoolean(readAction(request));
                    }
                    else // post
                    {
                        writeAction(request, request.RequiredPostBooleanAsJson());
                        request.PostSucceeded();
                    }
                },
                handleOnUiThread,
                requiresSync
            );
        }

        /// <summary>
        /// Same as RegisterEndpointHandler, but for Endpoints that may be called by other endpoint handlers which are synchronous.
        /// If so, sets RequiresSync to false (because it would deadlock if a synchronous handler spawned off another synchronous handler)
        /// The caller should make sure that this endpoint handler can operate correctly without exclusive access to the lock!
        /// </summary>
        /// <param name="pattern">Simple string to match APIs that this can handle.
        /// This must match what comes after the ".../api/" of the URL (before any ? params)</param>
        /// <param name="handler">The method to call</param>
        /// <param name="handleOnUiThread">If true, the current thread will suspend until the UI thread can be used to call the method.
        /// This deliberately no longer has a default. It's something that should be thought about.
        /// Making it true can kill performance if you don't need it (BL-3452), and complicates exception handling and problem reporting (BL-4679).
        /// There's also danger of deadlock if something in the UI thread is somehow waiting for this request to complete.
        /// But, beware of race conditions or anything that manipulates UI controls if you make it false.</param>
        public void RegisterEndpointHandlerUsedByOthers(
            string pattern,
            EndpointHandler handler,
            bool handleOnUiThread
        )
        {
            lock (_endpointRegistrationsLock)
            {
                _exactEndpointRegistrations[pattern.ToLowerInvariant().Trim(new char[] { '/' })] =
                    new EndpointRegistration()
                    {
                        Handler = handler,
                        HandleOnUIThread = handleOnUiThread,
                        RequiresSync = false,
                    };
            }
        }

        /// <summary>
        /// Handle enum reads/writes (pattern must be an exact match)
        /// </summary>
        public void RegisterEnumEndpointHandler<T>(
            string pattern,
            Func<ApiRequest, T> readAction,
            Action<ApiRequest, T> writeAction,
            bool handleOnUiThread,
            bool requiresSync = true
        )
        {
            Debug.Assert(
                typeof(T).IsEnum,
                "Type passed to RegisterEnumEndpointHandler is not an Enum."
            );
            RegisterEndpointHandler(
                pattern,
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithEnum(readAction(request));
                    }
                    else // post
                    {
                        if (writeAction == null)
                        {
                            throw new ApplicationException(
                                $"Endpoint {pattern} is read only but received a post"
                            );
                        }
                        writeAction(request, request.RequiredPostEnumAsJson<T>());
                        request.PostSucceeded();
                    }
                },
                handleOnUiThread,
                requiresSync
            );
        }

        public const string ApiPrefix = "api/";

        // Every path should return false or send a response.
        // Otherwise we can get a timeout error as the browser waits for a response.
        //
        // NOTE: this method gets called on different threads!
        public async Task<bool> ProcessRequestAsync(IRequestInfo info, string localPath)
        {
            var localPathLc = localPath.ToLowerInvariant();
            if (localPathLc.StartsWith(ApiPrefix, StringComparison.InvariantCulture))
            {
                var endpointPath = localPath
                    .Substring(3)
                    .ToLowerInvariant()
                    .Trim(new char[] { '/' });
                BaseEndpointRegistration epRegistration;
                int exactCount,
                    appLevelCount;
                lock (_endpointRegistrationsLock)
                {
                    _exactEndpointRegistrations.TryGetValue(endpointPath, out epRegistration);
                    exactCount = _exactEndpointRegistrations.Count;
                    appLevelCount = _applicationLevelRegistrationKeys.Count;
                }

                if (epRegistration != null)
                {
                    return await ProcessRequestAsync(epRegistration, info, localPathLc);
                }
                if (exactCount <= appLevelCount)
                {
                    // There is some history (BL-15716) of a request...specifically api/edit/pageControls/cleanup... (now removed in BL-15934)
                    // being sent during shutdown or while restarting, and not being found.
                    // We hope to have made this unlikely or impossible, but just in case,
                    // handle such failures gracefully. We don't launch browsers before registering handlers,
                    // so if there are no project-level registrations, it's likely some sort of cleanup,
                    // and if we're lucky, it can be ignored. At least, that seems more helpful
                    // than reporting it as a missing file. In fact, if without these registrations,
                    // I don't think our normal error reporting will work properly. Seems best to just do nothing.
                    // This may at least allow the application to exit cleanly.
                    // We will log the problem in case that helps later diagnosis.
                    SIL.Reporting.Logger.WriteEvent(
                        $"BloomServer received API request {info.RawUrl} when no endpoints were registered"
                    );
                    info.WriteError(404, $"Server could not process {localPath}");
                    return true; // we sort of handled it.
                }
                if (ShouldReportMissingApiEndpoint(endpointPath))
                {
                    // otherwise it's a programmer error we want to know about.
                    ReportMissingApiEndpoint(info, localPath);
                }
                else if (IsExternalToolEndpoint(endpointPath))
                {
                    // Not worth bothering the user about (see IsExternalToolEndpoint), but
                    // knowing which tool asked for what is useful when diagnosing why an
                    // external integration silently did nothing.
                    SIL.Reporting.Logger.WriteEvent(
                        $"BloomServer received a request for the unregistered external-tool API endpoint {info.RawUrl}"
                    );
                }
                // If the user continues from there, we need to pretend to have handled
                // the request. Otherwise the caller will keep trying to handle it in
                // other ways.
                info.WriteError(404, "API endpoint not found");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Should a request for this (unregistered) endpoint be reported to the user as a problem?
        /// True for anything we believe only our own code would ask for, since that means a
        /// programmer error we want to hear about. False for the handful of endpoints we know
        /// get requested by clients we don't control. Note that either way the request still
        /// gets a 404; this only decides whether we also bother the user about it.
        /// The whole set is the subject of BL-16576.
        /// </summary>
        private static bool ShouldReportMissingApiEndpoint(string endpointPath)
        {
            // There are older books out in the wild in which the src for branding images included
            // this endpoint. We now handle getting branding images differently.
            // Note that this will eventually result in a 404. That's ok because
            // the docs in the wild have `onerror="this.style.display='none'"`,
            // so we don't get the missing image indicator in the preview. See BL-16300.
            if (endpointPath == "branding/image")
                return false;

            // We deliberately removed this endpoint in BL-15934, and no current code calls it.
            // But it is still being POSTed in the field (Sentry is still seeing it from 6.3.x
            // releases), presumably by javascript left over from before the removal. Since the
            // endpoint is gone for good, there is nothing the user or we can do about it.
            if (endpointPath == "edit/pagecontrols/cleanup")
                return false;

            return !IsExternalToolEndpoint(endpointPath);
        }

        /// <summary>
        /// Is this endpoint part of the API surface we expose to other processes (BloomBridge,
        /// automation scripts, the visual regression harness) rather than to our own browser?
        /// Those tools scan for a running Bloom and then discover what it can do by simply
        /// asking, treating a non-2xx answer as "this Bloom is too old for that". So a request
        /// for one of these that we can't satisfy is normal version negotiation with a client we
        /// don't control, not a programmer error, and the user (who may not even know a tool is
        /// talking to Bloom) should not see a problem report about it. The cost of covering the
        /// whole `external/` namespace rather than just today's endpoints is that a typo in some
        /// future endpoint of ours here won't report itself; that's a good trade, because nothing
        /// inside Bloom calls these, so such a mistake shows up immediately in the external tool.
        /// </summary>
        private static bool IsExternalToolEndpoint(string endpointPath)
        {
            return endpointPath == "common/instanceinfo"
                || endpointPath.StartsWith("external/", StringComparison.InvariantCulture);
        }

        private static void ReportMissingApiEndpoint(IRequestInfo info, string localPath)
        {
            var userMsg = LocalizationManager.GetString(
                "WebServer.Warning.NoApiEndpoint",
                "Cannot Find API Endpoint"
            );
            var detailMsg = String.Format(
                "Server could not find an API endpoint for {0}. LocalPath was {1}. Method was {2}.{3}",
                info.RawUrl,
                localPath,
                info.HttpMethod,
                Environment.NewLine
            );
            NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, userMsg, detailMsg);
        }

        private async Task<bool> ProcessRequestAsync(
            BaseEndpointRegistration endpointRegistration,
            IRequestInfo info,
            string localPathLc
        )
        {
            if (endpointRegistration.RequiresSync)
            {
                // A single synchronization object won't do, because when processing a request to create a thumbnail or update a preview,
                // we have to load the HTML page the thumbnail is based on, or other HTML pages (like one used to figure what's
                // visible in a preview). If the page content somehow includes
                // an api request (api/branding/image is one example), that request will deadlock if the
                // api/pageTemplateThumbnail request already has the main lock.
                // Another case is the Bloom Reader preview, where the whole UI is rebuilt at the same time as the preview.
                // This leads to multiple api requests racing with the preview one, and it was possible for all
                // the server threads to be processing these and waiting for SyncObject while the updatePreview
                // request held the lock...and the request for the page that would free the lock was sitting in
                // the queue, waiting for a thread.
                // To the best of my knowledge, there's no shared data between the thumbnailing and preview processes and any
                // other api requests, so it seems safe to have one lock that prevents working on multiple
                // thumbnails/previews at the same time, and one that prevents working on other api requests at the same time.
                var syncOn = SyncObj;
                if (
                    localPathLc.StartsWith(
                        "api/pagetemplatethumbnail",
                        StringComparison.InvariantCulture
                    )
                    || localPathLc == "api/publish/bloompub/thumbnail"
                    || localPathLc == "api/publish/bloompub/updatepreview"
                    || localPathLc == "api/publish/epub/updatepreview"
                )
                    syncOn = ThumbnailsAndPreviewsSyncObj;
                else if (localPathLc.StartsWith("api/i18n/"))
                    syncOn = I18NLock;

                // We wrap RegisterThreadBlocking/Unblocked around acquiring the lock.
                // SemaphoreSlim is used instead of Monitor so we can safely await while the lock is held.
                // See BL-15586.
                bool lockAcquired = false;
                try
                {
                    // Try to acquire lock
                    BloomServer._theOneInstance.RegisterThreadBlocking();
                    try
                    {
                        syncOn.Wait();
                        lockAcquired = true;
                    }
                    finally
                    {
                        BloomServer._theOneInstance.RegisterThreadUnblocked();
                    }

                    // Lock has been acquired.
                    await ApiRequest.Handle(
                        endpointRegistration,
                        info,
                        CurrentCollectionSettings,
                        _bookSelection.CurrentSelection
                    );

                    // Even if ApiRequest.Handle() fails, return true to indicate that the request was processed and there
                    // is no further need for the caller to continue trying to process the request as a filename.
                    // See https://issues.bloomlibrary.org/youtrack/issue/BL-6763.
                    return true;
                }
                finally
                {
                    if (lockAcquired)
                        syncOn.Release();
                }
            }
            else
            {
                // Up to api's that request no sync to do things right!
                await ApiRequest.Handle(
                    endpointRegistration,
                    info,
                    CurrentCollectionSettings,
                    _bookSelection.CurrentSelection
                );
                // Even if ApiRequest.Handle() fails, return true to indicate that the request was processed and there
                // is no further need for the caller to continue trying to process the request as a filename.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-6763.
                return true;
            }
        }
    }
}
