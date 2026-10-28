using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Bloom.Collection;

namespace Bloom.Keyboarding
{
    /// <summary>
    /// What the resolver decided a focused field's keyboard should be.
    /// </summary>
    public enum KeyboardResolutionKind
    {
        /// <summary>No Bloom-supplied keyboard; switch the OS input language back to the default (English).</summary>
        Default,

        /// <summary>Bloom leaves the keyboard alone: no OS switch, no KeymanWeb (the "Off" setting).</summary>
        Off,

        /// <summary>Switch the OS input language to an installed input method (no KeymanWeb).</summary>
        OsKeyboard,

        /// <summary>Attach a KeymanWeb keyboard in the browser (no OS switch).</summary>
        KeymanWeb,
    }

    /// <summary>
    /// The resolved keyboard decision for one focused field.
    /// </summary>
    public class KeyboardResolution
    {
        /// <summary>Which branch of the cascade won.</summary>
        public KeyboardResolutionKind Kind;

        /// <summary>The field's (normalized) language tag.</summary>
        public string LanguageTag;

        /// <summary>For <see cref="KeyboardResolutionKind.OsKeyboard"/>, the OS keyboard to activate.</summary>
        public OsKeyboardInfo OsKeyboard;

        /// <summary>For <see cref="KeyboardResolutionKind.KeymanWeb"/>, the Keyman keyboard id to attach.</summary>
        public string KmwKeyboardId;

        /// <summary>For <see cref="KeyboardResolutionKind.KeymanWeb"/>, the BCP-47 tag the keyboard is for.</summary>
        public string KmwLanguageTag;
    }

    /// <summary>
    /// Supplies the Keyman-cloud fallback keyboard for a language and ensures its files are cached.
    /// Extracted as an interface so the resolver can be tested without network or disk.
    /// </summary>
    public interface IKmwFallbackService
    {
        /// <summary>The de-facto default (top search result) Keyman keyboard id for the language, or null if offline/none.</summary>
        string GetTopSuggestion(string tag);

        /// <summary>Ensure the keyboard's files are cached locally (may run in the background / be a no-op offline).</summary>
        void EnsureCached(string keyboardId, string tag);
    }

    /// <summary>
    /// Resolves, per machine and per focused field, which keyboard the edit view should use, following
    /// the plan's cascade: a pinned "system:" input method, else a pinned "kmw:" KeymanWeb keyboard,
    /// else Automatic (best installed OS keyboard → the collection's cached KeymanWeb fallback → the
    /// default English layout). Fields with no writing system (z, *, empty, source-bubble placeholders)
    /// resolve to the default.
    ///
    /// Decisions are cached per language for the session in a <see cref="ConcurrentDictionary{TKey,TValue}"/>;
    /// this is safe because changing a keyboard setting triggers a Bloom restart. The one decision we do
    /// NOT cache is a temporary "default" returned while a background fallback fetch is in flight, so the
    /// next focus can pick up the freshly-fetched fallback.
    ///
    /// Note the split between resolution and acquisition: the *decision* (which keyboard) is cached, but
    /// *acquisition* (getting a KeymanWeb keyboard's files onto disk) is deliberately not — see
    /// <see cref="EnsureAcquired"/>, which re-checks on every focus so a keyboard can be (re)downloaded on
    /// demand whenever it is needed but missing.
    /// </summary>
    public class KeyboardResolver
    {
        private readonly CollectionSettings _settings;
        private readonly IOsKeyboardService _osKeyboards;
        private readonly IKmwFallbackService _fallback;

        // Persists ONLY WritingSystem.CachedKmwFallbackKeyboard (marshalled + settings-dialog-guarded by
        // the real implementation). Injected so tests can observe the save without touching disk.
        private readonly Action<WritingSystem> _persistCachedFallback;

        // Runs a background action. Default: Task.Run. Tests pass a synchronous runner so they can
        // assert the fetch/persist side effects deterministically.
        private readonly Action<Action> _runInBackground;

        private readonly ConcurrentDictionary<string, KeyboardResolution> _sessionCache =
            new ConcurrentDictionary<string, KeyboardResolution>();

        // Language markers that are not real writing systems; always resolve to the default keyboard.
        private static readonly HashSet<string> kNonLanguages = new HashSet<string>(
            new[] { "z", "*", "" },
            StringComparer.OrdinalIgnoreCase
        );

        /// <summary>
        /// Create a resolver. All collaborators are injected so the cascade can be unit-tested with fakes.
        /// </summary>
        /// <param name="settings">The collection settings (writing systems + persistence).</param>
        /// <param name="osKeyboards">OS-keyboard queries + activation.</param>
        /// <param name="fallback">Keyman-cloud fallback lookup + caching.</param>
        /// <param name="persistCachedFallback">Persists only the CachedKmwFallbackKeyboard field of a writing system.</param>
        /// <param name="runInBackground">How to run background work; defaults to Task.Run.</param>
        public KeyboardResolver(
            CollectionSettings settings,
            IOsKeyboardService osKeyboards,
            IKmwFallbackService fallback,
            Action<WritingSystem> persistCachedFallback,
            Action<Action> runInBackground = null
        )
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _osKeyboards = osKeyboards ?? throw new ArgumentNullException(nameof(osKeyboards));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _persistCachedFallback =
                persistCachedFallback
                ?? throw new ArgumentNullException(nameof(persistCachedFallback));
            _runInBackground = runInBackground ?? (a => System.Threading.Tasks.Task.Run(a));
        }

        /// <summary>
        /// Resolve the keyboard decision for a focused field in the given language. Never throws for
        /// bad language data — an unknown/placeholder language resolves to the default keyboard.
        /// </summary>
        public KeyboardResolution Resolve(string lang)
        {
            var tag = (lang ?? "").Trim();

            if (kNonLanguages.Contains(tag))
                return Default(tag); // not a real writing system

            if (_sessionCache.TryGetValue(tag, out var cached))
            {
                EnsureAcquired(cached);
                return cached;
            }

            var resolution = ResolveUncached(tag, out var cacheable);
            if (cacheable)
                _sessionCache[tag] = resolution;
            EnsureAcquired(resolution);
            return resolution;
        }

        /// <summary>
        /// Acquisition, deliberately separated from the resolution/configuration above: whenever a
        /// focused field needs a KeymanWeb keyboard, make sure its files are on disk, re-checking on
        /// EVERY focus rather than once per session. This is what lets a keyboard be (re)acquired on
        /// demand: if the cache was never populated (e.g. a teammate who has the setting but not the
        /// files, or a first focus made while offline) or was deleted, the next focus fetches it.
        /// <see cref="IKmwFallbackService.EnsureCached"/> is a cheap no-op when the files are already
        /// present and never blocks (it downloads in the background), so calling it every focus is safe.
        /// </summary>
        private void EnsureAcquired(KeyboardResolution resolution)
        {
            if (resolution.Kind == KeyboardResolutionKind.KeymanWeb)
                _fallback.EnsureCached(resolution.KmwKeyboardId, resolution.KmwLanguageTag);
        }

        /// <summary>
        /// The cascade itself. <paramref name="cacheable"/> is false only for the transient "default while
        /// a fallback fetch is in flight" case, so we re-resolve on the next focus.
        /// </summary>
        private KeyboardResolution ResolveUncached(string tag, out bool cacheable)
        {
            cacheable = true;
            var ws = FindWritingSystem(tag);
            if (ws == null)
                return Default(tag); // no writing system for this language -> default

            var setting = KeyboardSetting.Parse(ws.Keyboard);

            // 0. Off: Bloom does not manage the keyboard for this language. Don't switch the OS input
            // and don't attach KeymanWeb; leave whatever the user has active.
            if (setting.SettingKind == KeyboardSetting.Kind.Off)
                return OffResolution(tag);

            // 1. Pinned system keyboard: use it if installed here, else fall through to Automatic.
            if (setting.SettingKind == KeyboardSetting.Kind.System)
            {
                var os = _osKeyboards.FindById(setting.Id);
                if (os != null)
                    return OsResolution(tag, os);
                // not installed on this machine: fall through to Automatic
            }

            // 2. Pinned KeymanWeb keyboard. Getting its files onto disk is acquisition, handled by
            // EnsureAcquired on every focus (not here) so it also covers a cache that was deleted,
            // never downloaded, or never synced to this machine.
            if (setting.SettingKind == KeyboardSetting.Kind.KeymanWeb)
            {
                var kmwTag = string.IsNullOrEmpty(setting.LanguageTag) ? tag : setting.LanguageTag;
                return KmwResolution(tag, setting.Id, kmwTag);
            }

            // 3. Automatic (also the fall-through when a pinned system keyboard isn't installed here).
            var best = _osKeyboards.FindBestForLanguage(tag);
            if (best != null)
                return OsResolution(tag, best); // an OS keyboard exists -> use it, no KMW

            // Acquisition of the fallback's files is handled by EnsureAcquired on every focus.
            if (!string.IsNullOrEmpty(ws.CachedKmwFallbackKeyboard))
                return KmwResolution(tag, ws.CachedKmwFallbackKeyboard, tag);

            // No OS keyboard and no cached fallback yet: try to fetch a suggestion in the background,
            // then fall back to the default for now. Don't cache this transient decision.
            KickBackgroundFallbackFetch(ws, tag);
            cacheable = false;
            return Default(tag);
        }

        /// <summary>
        /// In the background, look up the top Keyman suggestion for the language, save it to the writing
        /// system's CachedKmwFallbackKeyboard (persisted via the injected save), and pre-cache its files.
        /// A no-op when offline (GetTopSuggestion returns null).
        /// </summary>
        private void KickBackgroundFallbackFetch(WritingSystem ws, string tag)
        {
            _runInBackground(() =>
            {
                var suggestion = _fallback.GetTopSuggestion(tag);
                if (string.IsNullOrEmpty(suggestion))
                    return; // offline or no keyboard for this language; silent retry next time
                ws.CachedKmwFallbackKeyboard = suggestion;
                _persistCachedFallback(ws);
                _fallback.EnsureCached(suggestion, tag);
                // Drop any cached transient decision so the next focus re-resolves to the new fallback.
                _sessionCache.TryRemove(tag, out _);
            });
        }

        /// <summary>
        /// Find the writing system whose language subtag matches <paramref name="tag"/> (region/script
        /// ignored), preferring an exact tag match. Null if the collection has no such language.
        /// </summary>
        private WritingSystem FindWritingSystem(string tag)
        {
            var languages = _settings.AllLanguages;
            if (languages == null)
                return null;
            var exact = languages.FirstOrDefault(w =>
                string.Equals(w.Tag, tag, StringComparison.OrdinalIgnoreCase)
            );
            if (exact != null)
                return exact;
            var wantLang = LanguageSubtag(tag);
            return languages.FirstOrDefault(w =>
                !string.IsNullOrEmpty(w.Tag)
                && string.Equals(
                    LanguageSubtag(w.Tag),
                    wantLang,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        private static KeyboardResolution Default(string tag)
        {
            return new KeyboardResolution
            {
                Kind = KeyboardResolutionKind.Default,
                LanguageTag = tag,
            };
        }

        private static KeyboardResolution OffResolution(string tag)
        {
            return new KeyboardResolution { Kind = KeyboardResolutionKind.Off, LanguageTag = tag };
        }

        private static KeyboardResolution OsResolution(string tag, OsKeyboardInfo os)
        {
            return new KeyboardResolution
            {
                Kind = KeyboardResolutionKind.OsKeyboard,
                LanguageTag = tag,
                OsKeyboard = os,
            };
        }

        private static KeyboardResolution KmwResolution(
            string tag,
            string keyboardId,
            string kmwTag
        )
        {
            return new KeyboardResolution
            {
                Kind = KeyboardResolutionKind.KeymanWeb,
                LanguageTag = tag,
                KmwKeyboardId = keyboardId,
                KmwLanguageTag = kmwTag,
            };
        }

        private static string LanguageSubtag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "";
            var dash = tag.IndexOf('-');
            return (dash < 0 ? tag : tag.Substring(0, dash)).ToLowerInvariant();
        }
    }
}
