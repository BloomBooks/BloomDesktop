using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.AiTranslation;
using Bloom.Book;
using Bloom.Collection;
using BloomTests.TeamCollection;
using NUnit.Framework;

namespace BloomTests.AiTranslation
{
    /// <summary>
    /// Tests for the dialog-free orchestration core of AiTranslationBookUpdater: RunEnginesAsync (the
    /// parallel translate phase) and ApplyOutcomes (the DOM-write phase). The dialog-hosting parts
    /// (RunWithProgressDialog, MakeDialog) require a real Form/ShowDialog and are not unit tested here;
    /// see the manual test plan for those.
    /// </summary>
    [TestFixture]
    public class AiTranslationBookUpdaterTests
    {
        private static HtmlDom MakeBookDom(string pagesHtml, string dataDivHtml = "")
        {
            return new HtmlDom(
                $@"<html><body>
                    <div id='bloomDataDiv'>{dataDivHtml}</div>
                    {pagesHtml}
                </body></html>"
            );
        }

        private static AiTranslationEngineSettings MakeEngine(string providerId) =>
            new AiTranslationEngineSettings { ProviderId = providerId, Enabled = true };

        [Test]
        public async Task RunEnginesAsync_NothingNeeded_MakesNoTranslateCalls()
        {
            var engine = MakeEngine("deepl");
            var aiTag = AiTranslationService.GetAiLanguageTag("es", "deepl");
            var currentFingerprint = AiTranslationBookScanner.ComputeFingerprint(
                "en",
                "Hello",
                aiTag
            );
            var dom = MakeBookDom(
                $@"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='{aiTag}' data-ai-fingerprint='{currentFingerprint}'>Hola</div>
                    </div>
                </div>"
            );
            var scanner = new AiTranslationBookScanner(dom, "es", new[] { engine }, new[] { "en" });
            var scan = scanner.Scan();
            Assert.That(
                scan.GroupsNeedingTranslation(engine),
                Is.Empty,
                "sanity check: the existing translation should already be current"
            );

            var callCount = 0;
            Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            )
            {
                callCount++;
                return Task.FromResult(segments);
            }

            var outcomes = await AiTranslationBookUpdater.RunEnginesAsync(
                scan,
                new[] { engine },
                translate,
                new ProgressSpy(),
                CancellationToken.None
            );

            Assert.That(
                callCount,
                Is.EqualTo(0),
                "no engine has work, so translate must never be called"
            );
            Assert.That(outcomes, Is.Empty);
        }

        [Test]
        public async Task RunEnginesAsync_TwoEngines_GroupsByLanguageAndAppliesCorrectTextsToCorrectGroups()
        {
            var deepl = MakeEngine("deepl");
            var google = MakeEngine("google");
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='fr'>Bonjour</div>
                    </div>
                </div>"
            );
            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new[] { deepl, google },
                new[] { "en", "fr" }
            );
            var scan = scanner.Scan();
            Assert.That(scan.Groups.Count, Is.EqualTo(2), "sanity check on fixture");

            var calls = new List<(string Engine, string Lang, string[] Segments)>();
            Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            )
            {
                lock (calls)
                    calls.Add((e.ProviderId, sourceLang, segments));
                return Task.FromResult(
                    segments.Select(s => $"{e.ProviderId}/{sourceLang}:{s}").ToArray()
                );
            }

            var outcomes = await AiTranslationBookUpdater.RunEnginesAsync(
                scan,
                new[] { deepl, google },
                translate,
                new ProgressSpy(),
                CancellationToken.None
            );

            Assert.That(outcomes.Count, Is.EqualTo(2), "both engines had work");
            Assert.That(calls.Count, Is.EqualTo(4), "2 engines x 2 source languages (en, fr) each");
            foreach (var outcome in outcomes)
            {
                Assert.That(outcome.Succeeded, Is.True);
                Assert.That(outcome.Translations.Count, Is.EqualTo(2));
                var byText = outcome.Translations.ToDictionary(
                    t => t.Group.SourceText,
                    t => t.TranslatedText
                );
                Assert.That(
                    byText["Hello"],
                    Is.EqualTo($"{outcome.Engine.ProviderId}/en:Hello"),
                    "the English group's translation should come from the en-language call"
                );
                Assert.That(
                    byText["Bonjour"],
                    Is.EqualTo($"{outcome.Engine.ProviderId}/fr:Bonjour"),
                    "the French group's translation should come from the fr-language call"
                );
            }
        }

        [Test]
        public async Task RunEnginesAsync_TwoEngines_RunConcurrentlyNotSequentially()
        {
            var deepl = MakeEngine("deepl");
            var google = MakeEngine("google");
            var dom = MakeBookDom(
                @"<div class='bloom-page'><div class='bloom-translationGroup'>
                    <div class='bloom-editable' lang='en'>Hello</div>
                </div></div>"
            );
            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new[] { deepl, google },
                new[] { "en" }
            );
            var scan = scanner.Scan();

            var deeplStarted = new TaskCompletionSource<bool>();
            var googleStarted = new TaskCompletionSource<bool>();

            async Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            )
            {
                if (e.ProviderId == "deepl")
                {
                    deeplStarted.SetResult(true);
                    await Task.WhenAny(googleStarted.Task, Task.Delay(5000, ct));
                }
                else
                {
                    googleStarted.SetResult(true);
                    await Task.WhenAny(deeplStarted.Task, Task.Delay(5000, ct));
                }
                return segments;
            }

            await AiTranslationBookUpdater.RunEnginesAsync(
                scan,
                new[] { deepl, google },
                translate,
                new ProgressSpy(),
                CancellationToken.None
            );

            Assert.That(
                deeplStarted.Task.IsCompletedSuccessfully
                    && googleStarted.Task.IsCompletedSuccessfully,
                Is.True,
                "each engine's translate call should have observed the other one having also started, proving they ran concurrently rather than one waiting for the other to fully finish"
            );
        }

        [Test]
        public async Task RunEnginesAsync_OneEngineThrows_OtherEnginesResultsStillApplied_FailureRecorded()
        {
            var deepl = MakeEngine("deepl");
            var google = MakeEngine("google");
            var dom = MakeBookDom(
                @"<div class='bloom-page'><div class='bloom-translationGroup'>
                    <div class='bloom-editable' lang='en'>Hello</div>
                </div></div>"
            );
            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new[] { deepl, google },
                new[] { "en" }
            );
            var scan = scanner.Scan();

            Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            )
            {
                if (e.ProviderId == "deepl")
                    throw new InvalidOperationException("DeepL quota exceeded");
                return Task.FromResult(segments.Select(s => $"google:{s}").ToArray());
            }

            var outcomes = await AiTranslationBookUpdater.RunEnginesAsync(
                scan,
                new[] { deepl, google },
                translate,
                new ProgressSpy(),
                CancellationToken.None
            );

            var deeplOutcome = outcomes.Single(o => o.Engine.ProviderId == "deepl");
            var googleOutcome = outcomes.Single(o => o.Engine.ProviderId == "google");

            Assert.That(deeplOutcome.Succeeded, Is.False);
            Assert.That(deeplOutcome.ErrorMessage, Does.Contain("DeepL quota exceeded"));
            Assert.That(deeplOutcome.Translations, Is.Empty);

            Assert.That(googleOutcome.Succeeded, Is.True);
            Assert.That(
                googleOutcome.Translations.Single().TranslatedText,
                Is.EqualTo("google:Hello")
            );
        }

        [Test]
        public void RunEnginesAsync_Cancelled_ThrowsAndLeavesDomUnchanged()
        {
            var engine = MakeEngine("deepl");
            var dom = MakeBookDom(
                @"<div class='bloom-page'><div class='bloom-translationGroup'>
                    <div class='bloom-editable' lang='en'>Hello</div>
                </div></div>"
            );
            var scanner = new AiTranslationBookScanner(dom, "es", new[] { engine }, new[] { "en" });
            var scan = scanner.Scan();
            var group = scan.Groups.Single();

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // simulate the user having already clicked Cancel

            Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            )
            {
                Assert.Fail(
                    "translate should never be called once cancellation has already been requested"
                );
                return Task.FromResult(segments);
            }

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await AiTranslationBookUpdater.RunEnginesAsync(
                    scan,
                    new[] { engine },
                    translate,
                    new ProgressSpy(),
                    cts.Token
                )
            );

            // Since RunEnginesAsync threw, the real caller (AiTranslationBookUpdater.RunWithProgressDialog)
            // never reaches ApplyOutcomes/RemoveStaleAiDivs/book.Save() -- so nothing should have touched
            // the DOM. Confirm no AI div was ever written for the one group that would otherwise have
            // been translated.
            var aiTag = AiTranslationService.GetAiLanguageTag("es", "deepl");
            Assert.That(
                group.GroupElement.SafeSelectElements($"div[@lang='{aiTag}']"),
                Is.Empty,
                "cancellation must leave the book DOM completely untouched"
            );
        }

        [Test]
        public async Task ApplyOutcomes_WritesEachEnginesTranslationsToTheDom_AndRemoveStaleAiDivsThenCleansUp()
        {
            var deepl = MakeEngine("deepl");
            var staleEngineTag = AiTranslationService.GetAiLanguageTag("es", "deepl");
            // A group that already has a stale (wrong-fingerprint) AI div, to prove the success path's
            // subsequent RemoveStaleAiDivs() call still cleans it up after ApplyOutcomes runs.
            var dom = MakeBookDom(
                $@"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>World</div>
                        <div class='bloom-editable' lang='{staleEngineTag}' data-ai-fingerprint='stale'>Old</div>
                    </div>
                </div>"
            );
            var scanner = new AiTranslationBookScanner(dom, "es", new[] { deepl }, new[] { "en" });
            var scan = scanner.Scan();

            Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            ) => Task.FromResult(segments.Select(s => $"ES:{s}").ToArray());

            var outcomes = await AiTranslationBookUpdater.RunEnginesAsync(
                scan,
                new[] { deepl },
                translate,
                new ProgressSpy(),
                CancellationToken.None
            );

            AiTranslationBookUpdater.ApplyOutcomes(scanner, outcomes);

            var aiTag = AiTranslationService.GetAiLanguageTag("es", "deepl");
            var helloGroupAiDiv = scan
                .Groups.First(g => g.SourceText == "Hello")
                .GroupElement.SafeSelectElements($"div[@lang='{aiTag}']")
                .Single();
            Assert.That(helloGroupAiDiv.InnerText, Is.EqualTo("ES:Hello"));

            var worldGroupAiDiv = scan
                .Groups.First(g => g.SourceText == "World")
                .GroupElement.SafeSelectElements($"div[@lang='{aiTag}']")
                .Single();
            Assert.That(
                worldGroupAiDiv.InnerText,
                Is.EqualTo("ES:World"),
                "ApplyTranslation should have replaced the previously-stale div with the fresh translation"
            );

            // Only on the success path does the real caller go on to call RemoveStaleAiDivs(); simulate
            // that here directly against the same scanner/DOM now that ApplyOutcomes has run.
            var removedCount = scanner.RemoveStaleAiDivs();
            Assert.That(
                removedCount,
                Is.EqualTo(0),
                "both AI divs are now current (freshly applied), so there should be nothing left to remove"
            );
        }

        [Test]
        public void CountStaleAiDivsWithoutMutating_ReportsCountWithoutTouchingTheRealDom()
        {
            var deepl = MakeEngine("deepl");
            var staleTag = AiTranslationService.GetAiLanguageTag("es", "deepl");
            var dom = MakeBookDom(
                $@"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='{staleTag}' data-ai-fingerprint='stale'>Old</div>
                    </div>
                </div>"
            );

            var countBefore = dom
                .RawDom.SafeSelectNodes("//div[@lang and contains(@lang,'-x-ai')]")
                .Length;
            Assert.That(countBefore, Is.EqualTo(1), "sanity check on fixture");

            var staleCount = AiTranslationBookUpdater.CountStaleAiDivsWithoutMutating(
                dom,
                "es",
                new[] { deepl },
                new[] { "en" }
            );

            Assert.That(staleCount, Is.EqualTo(1));
            // The real DOM must be untouched -- the stale div should still be there.
            var countAfter = dom
                .RawDom.SafeSelectNodes("//div[@lang and contains(@lang,'-x-ai')]")
                .Length;
            Assert.That(
                countAfter,
                Is.EqualTo(1),
                "CountStaleAiDivsWithoutMutating must not mutate the real book DOM"
            );
        }

        [Test]
        public async Task RunEnginesAsync_Alpha2FixedSource_DeeplPrioritySource_SameGroup()
        {
            var deepl = MakeEngine("deepl");
            var alpha2 = new AiTranslationEngineSettings
            {
                ProviderId = "alpha2",
                Enabled = true,
                SourceLanguageTag = "fr",
            };
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='fr'>Bonjour</div>
                    </div>
                </div>"
            );
            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new[] { deepl, alpha2 },
                new[] { "en" }
            );
            var scan = scanner.Scan();

            var calls = new List<(string Engine, string Lang, string[] Segments)>();
            Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            )
            {
                lock (calls)
                    calls.Add((e.ProviderId, sourceLang, segments));
                return Task.FromResult(segments.Select(s => $"{e.ProviderId}:{s}").ToArray());
            }

            await AiTranslationBookUpdater.RunEnginesAsync(
                scan,
                new[] { deepl, alpha2 },
                translate,
                new ProgressSpy(),
                CancellationToken.None
            );

            var deeplCall = calls.Single(c => c.Engine == "deepl");
            var alpha2Call = calls.Single(c => c.Engine == "alpha2");
            Assert.That(deeplCall.Lang, Is.EqualTo("en"), "deepl uses the priority source");
            Assert.That(deeplCall.Segments, Is.EqualTo(new[] { "Hello" }));
            Assert.That(
                alpha2Call.Lang,
                Is.EqualTo("fr"),
                "alpha2 uses its fixed configured source for the same group"
            );
            Assert.That(alpha2Call.Segments, Is.EqualTo(new[] { "Bonjour" }));
        }

        [Test]
        public async Task RunEnginesAsync_Alpha2_SkipsGroupWithNoSourceText_NoTranslateCallForIt()
        {
            var alpha2 = new AiTranslationEngineSettings
            {
                ProviderId = "alpha2",
                Enabled = true,
                SourceLanguageTag = "fr",
            };
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='fr'>Bonjour</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>World</div>
                    </div>
                </div>"
            );
            var scanner = new AiTranslationBookScanner(dom, "es", new[] { alpha2 }, new[] { "en" });
            var scan = scanner.Scan();
            Assert.That(scan.Groups.Count, Is.EqualTo(2), "sanity check on fixture");

            var translatedSegments = new List<string>();
            Task<string[]> translate(
                AiTranslationEngineSettings e,
                string[] segments,
                string sourceLang,
                CancellationToken ct
            )
            {
                lock (translatedSegments)
                    translatedSegments.AddRange(segments);
                return Task.FromResult(segments.Select(s => $"ES:{s}").ToArray());
            }

            var outcomes = await AiTranslationBookUpdater.RunEnginesAsync(
                scan,
                new[] { alpha2 },
                translate,
                new ProgressSpy(),
                CancellationToken.None
            );

            Assert.That(
                translatedSegments,
                Is.EqualTo(new[] { "Bonjour" }),
                "only the group with French text is translated; the English-only group is skipped"
            );
            Assert.That(outcomes.Single().Translations.Count, Is.EqualTo(1));
        }
    }
}
