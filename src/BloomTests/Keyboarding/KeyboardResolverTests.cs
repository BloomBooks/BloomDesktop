using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Collection;
using Bloom.Keyboarding;
using NUnit.Framework;

namespace BloomTests.Keyboarding
{
    /// <summary>
    /// Tests the resolver cascade (pinned system → pinned kmw → Automatic: OS best → cached fallback →
    /// default) and the background fallback-fetch's "write only CachedKmwFallbackKeyboard" behavior,
    /// all with fake collaborators (no network, no TSF activation).
    /// </summary>
    [TestFixture]
    public class KeyboardResolverTests
    {
        // ----- Fakes -------------------------------------------------------------------------

        private class FakeOsKeyboards : IOsKeyboardService
        {
            // Language subtag -> the OS keyboard Automatic should find for it.
            public readonly Dictionary<string, OsKeyboardInfo> ByLanguage = new Dictionary<
                string,
                OsKeyboardInfo
            >(StringComparer.OrdinalIgnoreCase);

            // libpalaso id -> installed OS keyboard (for pinned "system:" settings).
            public readonly Dictionary<string, OsKeyboardInfo> ById = new Dictionary<
                string,
                OsKeyboardInfo
            >(StringComparer.OrdinalIgnoreCase);

            public int ActivateCount;
            public int ActivateDefaultCount;

            public OsKeyboardInfo FindBestForLanguage(string tag)
            {
                ByLanguage.TryGetValue(Sub(tag), out var kb);
                return kb;
            }

            public OsKeyboardInfo FindById(string libPalasoKeyboardId)
            {
                ById.TryGetValue(libPalasoKeyboardId, out var kb);
                return kb;
            }

            public bool Activate(OsKeyboardInfo keyboard)
            {
                ActivateCount++;
                return true;
            }

            public bool ActivateDefault()
            {
                ActivateDefaultCount++;
                return true;
            }

            private static string Sub(string tag)
            {
                var dash = (tag ?? "").IndexOf('-');
                return dash < 0 ? (tag ?? "") : tag.Substring(0, dash);
            }
        }

        private class FakeFallback : IKmwFallbackService
        {
            // Language subtag -> suggested keyboard id (null-absent = offline/none for that language).
            public readonly Dictionary<string, string> Suggestions = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );

            public readonly List<(string id, string tag)> EnsureCachedCalls =
                new List<(string, string)>();

            public string GetTopSuggestion(string tag)
            {
                Suggestions.TryGetValue(tag, out var id);
                return id;
            }

            public void EnsureCached(string keyboardId, string tag)
            {
                EnsureCachedCalls.Add((keyboardId, tag));
            }
        }

        // ----- Helpers -----------------------------------------------------------------------

        private static WritingSystem MakeWs(
            string tag,
            string keyboard = "",
            string cachedFallback = ""
        )
        {
            var ws = new WritingSystem(() => "en") { Tag = tag };
            ws.Keyboard = keyboard;
            ws.CachedKmwFallbackKeyboard = cachedFallback;
            return ws;
        }

        private static CollectionSettings SettingsWith(params WritingSystem[] languages)
        {
            var settings = new CollectionSettings();
            settings.AllLanguages = languages.ToList();
            return settings;
        }

        // Builds a resolver whose background work runs synchronously, capturing persist calls.
        private static KeyboardResolver MakeResolver(
            CollectionSettings settings,
            FakeOsKeyboards os,
            FakeFallback fallback,
            List<WritingSystem> persisted
        )
        {
            return new KeyboardResolver(
                settings,
                os,
                fallback,
                ws => persisted.Add(ws),
                runInBackground: action => action() // synchronous for deterministic tests
            );
        }

        private static OsKeyboardInfo OsInfo(string id, string locale)
        {
            return new OsKeyboardInfo
            {
                Id = id,
                Locale = locale,
                Hkl = new IntPtr(0x041E041E),
            };
        }

        // ----- Cascade decisions -------------------------------------------------------------

        [Test]
        public void Resolve_NonLanguageMarkers_ResolveToDefault()
        {
            var os = new FakeOsKeyboards();
            var fallback = new FakeFallback();
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            foreach (var marker in new[] { "z", "*", "" })
            {
                var r = resolver.Resolve(marker);
                Assert.That(
                    r.Kind,
                    Is.EqualTo(KeyboardResolutionKind.Default),
                    $"marker '{marker}'"
                );
            }
        }

        [Test]
        public void Resolve_NoWritingSystemForLanguage_ResolvesToDefault()
        {
            var os = new FakeOsKeyboards();
            var fallback = new FakeFallback();
            var resolver = MakeResolver(
                SettingsWith(MakeWs("en")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            var r = resolver.Resolve("fr");
            Assert.That(r.Kind, Is.EqualTo(KeyboardResolutionKind.Default));
        }

        [Test]
        public void Resolve_Off_ReturnsOffAndDoesNotConsultOsOrCloud()
        {
            var os = new FakeOsKeyboards();
            // Sanity: an OS keyboard and a cloud suggestion both exist; "Off" must ignore them.
            os.ByLanguage["th"] = OsInfo("th-TH_Thai", "th-TH");
            var fallback = new FakeFallback();
            fallback.Suggestions["th"] = "thai_kedmanee";
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th", keyboard: "off")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            var r = resolver.Resolve("th");

            Assert.That(r.Kind, Is.EqualTo(KeyboardResolutionKind.Off));
            Assert.That(
                fallback.EnsureCachedCalls,
                Is.Empty,
                "Off must not do any KMW caching work"
            );
        }

        [Test]
        public void Resolve_PinnedSystem_Installed_UsesThatOsKeyboard()
        {
            var os = new FakeOsKeyboards();
            os.ById["th-TH_Thai Kedmanee"] = OsInfo("th-TH_Thai Kedmanee", "th-TH");
            // Sanity: a DIFFERENT (language-only) OS keyboard also exists; the pin must win over it.
            os.ByLanguage["th"] = OsInfo("th-TH_Other", "th-TH");
            var fallback = new FakeFallback();
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th", keyboard: "system:th-TH_Thai Kedmanee")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            var r = resolver.Resolve("th");

            Assert.That(r.Kind, Is.EqualTo(KeyboardResolutionKind.OsKeyboard));
            Assert.That(
                r.OsKeyboard.Id,
                Is.EqualTo("th-TH_Thai Kedmanee"),
                "the pinned keyboard, not the automatic one"
            );
        }

        [Test]
        public void Resolve_PinnedSystem_NotInstalledHere_FallsThroughToAutomatic()
        {
            var os = new FakeOsKeyboards();
            // The pinned id is NOT installed (ById empty), but an Automatic OS match exists.
            os.ByLanguage["th"] = OsInfo("th-TH_Auto", "th-TH");
            var fallback = new FakeFallback();
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th", keyboard: "system:not-installed-here")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            var r = resolver.Resolve("th");

            Assert.That(r.Kind, Is.EqualTo(KeyboardResolutionKind.OsKeyboard));
            Assert.That(r.OsKeyboard.Id, Is.EqualTo("th-TH_Auto"));
        }

        [Test]
        public void Resolve_PinnedKmw_ReturnsKmwAndEnsuresCached()
        {
            var os = new FakeOsKeyboards();
            var fallback = new FakeFallback();
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th", keyboard: "kmw:thai_kedmanee@th")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            var r = resolver.Resolve("th");

            Assert.That(r.Kind, Is.EqualTo(KeyboardResolutionKind.KeymanWeb));
            Assert.That(r.KmwKeyboardId, Is.EqualTo("thai_kedmanee"));
            Assert.That(r.KmwLanguageTag, Is.EqualTo("th"));
            Assert.That(
                fallback.EnsureCachedCalls,
                Has.Member(("thai_kedmanee", "th")),
                "a pinned kmw keyboard should be ensured cached"
            );
        }

        [Test]
        public void Resolve_Automatic_OsKeyboardExists_UsesOsNotKmw()
        {
            var os = new FakeOsKeyboards();
            os.ByLanguage["th"] = OsInfo("th-TH_Thai", "th-TH");
            var fallback = new FakeFallback();
            fallback.Suggestions["th"] = "should_not_be_used"; // sanity: we should NOT consult the cloud
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th")), // Automatic
                os,
                fallback,
                new List<WritingSystem>()
            );

            var r = resolver.Resolve("th");

            Assert.That(r.Kind, Is.EqualTo(KeyboardResolutionKind.OsKeyboard));
            Assert.That(
                fallback.EnsureCachedCalls,
                Is.Empty,
                "no KMW work when an OS keyboard exists"
            );
        }

        [Test]
        public void Resolve_Automatic_NoOs_CachedFallbackPresent_UsesKmw()
        {
            var os = new FakeOsKeyboards(); // no OS keyboard for th
            var fallback = new FakeFallback();
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th", cachedFallback: "thai_kedmanee")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            var r = resolver.Resolve("th");

            Assert.That(r.Kind, Is.EqualTo(KeyboardResolutionKind.KeymanWeb));
            Assert.That(r.KmwKeyboardId, Is.EqualTo("thai_kedmanee"));
            Assert.That(fallback.EnsureCachedCalls, Has.Member(("thai_kedmanee", "th")));
        }

        [Test]
        public void Resolve_Automatic_NoOs_NoCachedFallback_Offline_ReturnsDefaultAndDoesNotCache()
        {
            var os = new FakeOsKeyboards();
            var fallback = new FakeFallback(); // Suggestions empty => offline/no suggestion
            var persisted = new List<WritingSystem>();
            var thWs = MakeWs("th");
            var resolver = MakeResolver(SettingsWith(thWs), os, fallback, persisted);

            var first = resolver.Resolve("th");
            Assert.That(
                first.Kind,
                Is.EqualTo(KeyboardResolutionKind.Default),
                "offline with nothing cached -> default"
            );
            Assert.That(persisted, Is.Empty, "nothing to persist when offline");
            Assert.That(thWs.CachedKmwFallbackKeyboard, Is.Empty, "nothing cached while offline");

            // Because the transient default is NOT cached, a later resolve re-enters the cascade. Now
            // online, that resolve triggers the background fetch (which populates the fallback field);
            // the fetch is for the NEXT focus, so this resolve still returns Default.
            fallback.Suggestions["th"] = "thai_kedmanee";
            var second = resolver.Resolve("th");
            Assert.That(
                second.Kind,
                Is.EqualTo(KeyboardResolutionKind.Default),
                "the resolve that triggers the fetch still returns default (proves the offline default wasn't cached, or the cascade wouldn't have re-run)"
            );
            Assert.That(
                thWs.CachedKmwFallbackKeyboard,
                Is.EqualTo("thai_kedmanee"),
                "the background fetch populated the fallback field for next time"
            );

            // The now-populated fallback is used on the following focus.
            var third = resolver.Resolve("th");
            Assert.That(third.Kind, Is.EqualTo(KeyboardResolutionKind.KeymanWeb));
            Assert.That(third.KmwKeyboardId, Is.EqualTo("thai_kedmanee"));
        }

        // ----- Background fallback fetch: "write only CachedKmwFallbackKeyboard" -------------

        [Test]
        public void Resolve_Automatic_NoOs_Online_FetchesSavesAndCachesOnlyThatField()
        {
            var os = new FakeOsKeyboards();
            var fallback = new FakeFallback();
            fallback.Suggestions["th"] = "thai_kedmanee";
            var persisted = new List<WritingSystem>();

            var thWs = MakeWs("th");
            var enWs = MakeWs("en", keyboard: "system:en-US", cachedFallback: "");
            // Sanity: capture other languages' fields so we can prove they are untouched.
            var enKeyboardBefore = enWs.Keyboard;
            var enFallbackBefore = enWs.CachedKmwFallbackKeyboard;

            var resolver = MakeResolver(SettingsWith(thWs, enWs), os, fallback, persisted);

            // Sanity check before acting.
            Assert.That(
                thWs.CachedKmwFallbackKeyboard,
                Is.Empty,
                "precondition: no cached fallback yet"
            );

            resolver.Resolve("th"); // background fetch runs synchronously in tests

            // The fetched suggestion was written to ONLY the th writing system's fallback field.
            Assert.That(
                thWs.CachedKmwFallbackKeyboard,
                Is.EqualTo("thai_kedmanee"),
                "the suggestion should be saved to CachedKmwFallbackKeyboard"
            );
            Assert.That(thWs.Keyboard, Is.Empty, "the Keyboard (policy) field must NOT be changed");
            Assert.That(
                enWs.Keyboard,
                Is.EqualTo(enKeyboardBefore),
                "other language's Keyboard untouched"
            );
            Assert.That(
                enWs.CachedKmwFallbackKeyboard,
                Is.EqualTo(enFallbackBefore),
                "other language's fallback untouched"
            );

            Assert.That(
                persisted,
                Is.EqualTo(new[] { thWs }),
                "persist called once, for the th writing system only"
            );
            Assert.That(
                fallback.EnsureCachedCalls,
                Has.Member(("thai_kedmanee", "th")),
                "the fetched fallback should be pre-cached"
            );
        }

        [Test]
        public void Resolve_CachesStableDecisions_PerLanguage()
        {
            var os = new FakeOsKeyboards();
            os.ByLanguage["th"] = OsInfo("th-TH_Thai", "th-TH");
            var fallback = new FakeFallback();
            var resolver = MakeResolver(
                SettingsWith(MakeWs("th")),
                os,
                fallback,
                new List<WritingSystem>()
            );

            var first = resolver.Resolve("th");
            // Remove the OS keyboard; a cached decision should still return OsKeyboard (proving the cache).
            os.ByLanguage.Clear();
            var second = resolver.Resolve("th");

            Assert.That(second.Kind, Is.EqualTo(KeyboardResolutionKind.OsKeyboard));
            Assert.That(ReferenceEquals(first, second), Is.True, "same cached resolution instance");
        }
    }
}
