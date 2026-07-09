using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Collection;
using Bloom.Keyboarding;
using SIL.Windows.Forms.Keyboarding;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The edit view's keyboard API: tells the browser what to do for each focused field's language
    /// (attach a KeymanWeb keyboard, or nothing because we've switched the OS input method ourselves),
    /// and answers the long-press check. This is the C# half of plan item 4; the browser half is
    /// src/BloomBrowserUI/bookEdit/js/keymanWebIntegration.ts, whose reply contract this implements.
    /// </summary>
    public class KeyboardingConfigApi
    {
        private readonly CollectionSettings _collectionSettings;

        // Built lazily on first use (needs the collection folder, and there's no point paying for any
        // of it until a field is actually focused in the edit view).
        private KeyboardResolver _resolver;
        private CollectionKeyboardCache _cache;
        private OsKeyboards _osKeyboards;

        // Churn avoidance: the OS input language we last asked for. We only re-post a language change
        // when the target actually differs, so ordinary typing (which re-focuses fields constantly)
        // doesn't spam WM_INPUTLANGCHANGEREQUEST.
        private string _lastActivatedKey;

        /// <summary>
        /// Constructed by autofac with the collection settings (see ProjectContext registration).
        /// </summary>
        public KeyboardingConfigApi(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
        }

        /// <summary>
        /// Registers this controller's endpoints with the API handler.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "keyboarding/useLongpress",
                (ApiRequest request) =>
                {
                    try
                    {
                        //detect if some keyboarding system is active, e.g. KeyMan. If it is, don't enable LongPress
                        var form = Application.OpenForms.Cast<Form>().Last();
                        request.ReplyWithText(
                            SIL.Windows.Forms.Keyboarding.KeyboardController.IsFormUsingInputProcessor(
                                form
                            )
                                ? "false"
                                : "true"
                        );
                    }
                    catch (Exception error)
                    {
                        request.ReplyWithText("true"); // This is arbitrary. I don't know if it's better to assume keyman, or not.
                        NonFatalProblem.Report(
                            ModalIf.None,
                            PassiveIf.All,
                            "Error checking for keyman",
                            "",
                            error
                        );
                    }
                },
                handleOnUiThread: false
            );

            // Posted by the browser on every field focus (never per keystroke). Runs the resolver, does
            // the OS-keyboard switch as a side effect when needed, and replies with what the browser
            // should do for this field. handleOnUiThread because it reads WinForms handles and switches
            // the input language on Bloom's main window.
            apiHandler.RegisterEndpointHandler(
                "keyboarding/fieldFocused",
                HandleFieldFocused,
                handleOnUiThread: true
            );
        }

        /// <summary>
        /// Resolve the focused field's keyboard, apply the OS-switch side effect, and reply with the
        /// browser's marching orders (attach KeymanWeb, or get out of the way).
        /// </summary>
        private void HandleFieldFocused(ApiRequest request)
        {
            var lang = (string)request.RequiredPostDynamic().lang;
            var resolution = Resolver.Resolve(lang);

            // Decide whether the browser should attach KeymanWeb. We only say yes when the keyboard's
            // files are actually cached locally; otherwise the browser would try to load a .js that
            // isn't there. The resolver has already kicked a background download, so a later focus will
            // succeed.
            var useKmw =
                resolution.Kind == KeyboardResolutionKind.KeymanWeb
                && Cache.IsCached(resolution.KmwKeyboardId);

            ApplyOsActivation(resolution, useKmw);

            if (useKmw)
                request.ReplyWithJson(BuildKmwReply(resolution));
            else
                request.ReplyWithJson(
                    new Dictionary<string, object>
                    {
                        ["useKmw"] = false,
                        ["keyboardId"] = "",
                        ["languageTag"] = resolution.LanguageTag,
                        ["keyboardFileUrl"] = "",
                    }
                );
        }

        /// <summary>
        /// Switch the OS input language for this field if needed. KeymanWeb fields need no OS switch (KMW
        /// intercepts typing in the browser); everything else switches to the resolved OS keyboard or
        /// back to the default. Only acts when the target differs from what we last activated.
        /// </summary>
        private void ApplyOsActivation(KeyboardResolution resolution, bool useKmw)
        {
            if (useKmw)
                return; // KMW handles typing for this field; leave the OS input language alone.

            if (resolution.Kind == KeyboardResolutionKind.OsKeyboard)
            {
                var key = "os:" + resolution.OsKeyboard.Id;
                if (key != _lastActivatedKey)
                {
                    OsKeyboards.Activate(resolution.OsKeyboard);
                    _lastActivatedKey = key;
                }
                return;
            }

            // Default, or a KeymanWeb field whose files aren't cached yet: use the default layout so the
            // field is at least usable with Latin keys.
            if (_lastActivatedKey != "default")
            {
                OsKeyboards.ActivateDefault();
                _lastActivatedKey = "default";
            }
        }

        /// <summary>
        /// Build the reply for a KeymanWeb field, including font info from the cache manifest when the
        /// keyboard bundles fonts (fields are omitted otherwise).
        /// </summary>
        private Dictionary<string, object> BuildKmwReply(KeyboardResolution resolution)
        {
            var reply = new Dictionary<string, object>
            {
                ["useKmw"] = true,
                ["keyboardId"] = resolution.KmwKeyboardId,
                ["languageTag"] = resolution.KmwLanguageTag,
                ["keyboardFileUrl"] = Cache.GetJsUrl(resolution.KmwKeyboardId),
            };

            var manifest = Cache.TryGetInfo(resolution.KmwKeyboardId);
            if (manifest != null)
            {
                if (!string.IsNullOrEmpty(manifest.FontFamily))
                {
                    reply["fontFamily"] = manifest.FontFamily;
                    reply["fontUrls"] = manifest.FontFiles.Select(Cache.GetFontUrl).ToArray();
                }
                if (!string.IsNullOrEmpty(manifest.OskFontFamily))
                {
                    reply["oskFontFamily"] = manifest.OskFontFamily;
                    reply["oskFontUrls"] = manifest.OskFontFiles.Select(Cache.GetFontUrl).ToArray();
                }
            }

            return reply;
        }

        private CollectionKeyboardCache Cache
        {
            get
            {
                EnsureBuilt();
                return _cache;
            }
        }

        private KeyboardResolver Resolver
        {
            get
            {
                EnsureBuilt();
                return _resolver;
            }
        }

        private OsKeyboards OsKeyboards
        {
            get
            {
                EnsureBuilt();
                return _osKeyboards;
            }
        }

        /// <summary>
        /// Wire up the resolver, cache, OS-keyboard service, and cloud client the first time a field is
        /// focused. Deferred because none of it is needed until the user starts editing.
        /// </summary>
        private void EnsureBuilt()
        {
            if (_resolver != null)
                return;
            var cloudClient = new KeymanCloudClient();
            _cache = new CollectionKeyboardCache(_collectionSettings.FolderPath, cloudClient);
            _osKeyboards = new OsKeyboards();
            var fallback = new KmwFallbackService(cloudClient, _cache);
            _resolver = new KeyboardResolver(
                _collectionSettings,
                _osKeyboards,
                fallback,
                PersistCachedFallbackField
            );
        }

        /// <summary>
        /// Persist a just-fetched Automatic fallback keyboard. The resolver has already set the writing
        /// system's <see cref="WritingSystem.CachedKmwFallbackKeyboard"/> in memory (so this session uses
        /// it immediately); here we persist it to disk. Marshalled to the UI thread, and skipped while a
        /// settings dialog is open so we never clobber the user's in-progress edits (plan risk #6). The
        /// only settings editor is that dialog, so a full Save when it is closed writes exactly the live
        /// state with just this one derived field changed.
        /// </summary>
        private void PersistCachedFallbackField(WritingSystem writingSystem)
        {
            Action save = () =>
            {
                if (CollectionSettingsApi.DialogBeingEdited != null)
                    return; // dialog open: keep the in-memory value; it will be written on the dialog's own save
                _collectionSettings.Save();
            };

            var form = Shell.GetShellOrOtherOpenForm();
            if (form != null && form.InvokeRequired)
                form.BeginInvoke(save);
            else
                save();
        }

        /// <summary>
        /// The real <see cref="IKmwFallbackService"/>: looks up the top Keyman-cloud suggestion for a
        /// language and pre-caches keyboards into the collection's Keyboards folder.
        /// </summary>
        private class KmwFallbackService : IKmwFallbackService
        {
            private readonly KeymanCloudClient _client;
            private readonly CollectionKeyboardCache _cache;

            // Keyboards currently being downloaded, so repeated focuses of the same field don't start a
            // storm of duplicate downloads while the first is still running.
            private readonly HashSet<string> _inFlight = new HashSet<string>();

            public KmwFallbackService(KeymanCloudClient client, CollectionKeyboardCache cache)
            {
                _client = client;
                _cache = cache;
            }

            /// <summary>The top search result's keyboard id for the language, or null offline/none.</summary>
            public string GetTopSuggestion(string tag)
            {
                return _client.SearchKeyboardsForLanguage(tag).FirstOrDefault()?.Id;
            }

            /// <summary>
            /// Download the keyboard's files into the collection cache, in the BACKGROUND: the caller may
            /// be on the UI thread (the fieldFocused endpoint), and a download must never block typing.
            /// A no-op if already cached or already downloading; silent when offline.
            /// </summary>
            public void EnsureCached(string keyboardId, string tag)
            {
                if (_cache.IsCached(keyboardId))
                    return;
                lock (_inFlight)
                {
                    if (!_inFlight.Add(keyboardId))
                        return; // a download for this keyboard is already running
                }
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        _cache.EnsureDownloaded(keyboardId, tag);
                    }
                    finally
                    {
                        lock (_inFlight)
                            _inFlight.Remove(keyboardId);
                    }
                });
            }
        }
    }
}
