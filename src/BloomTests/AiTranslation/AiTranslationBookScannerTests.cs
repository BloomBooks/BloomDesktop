using System.Collections.Generic;
using System.Linq;
using Bloom.AiTranslation;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.AiTranslation
{
    [TestFixture]
    public class AiTranslationBookScannerTests
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

        [Test]
        public void Scan_FindsGroupsInDocumentOrder_WithSourceTextChosenFromPriorities()
        {
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>English 1</div>
                        <div class='bloom-editable' lang='fr'>French 1</div>
                    </div>
                </div>
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>English 2</div>
                        <div class='bloom-editable' lang='fr'>French 2</div>
                    </div>
                </div>"
            );
            Assert.That(
                dom.RawDom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-translationGroup')]"
                ).Length,
                Is.EqualTo(2),
                "sanity check: fixture should have 2 translation groups"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings>(),
                new[] { "fr", "en" }
            );

            var scan = scanner.Scan();

            Assert.That(scan.Groups.Count, Is.EqualTo(2));
            Assert.That(scan.Groups[0].SourceText, Is.EqualTo("French 1"));
            Assert.That(scan.Groups[0].SourceLanguageTag, Is.EqualTo("fr"));
            Assert.That(scan.Groups[1].SourceText, Is.EqualTo("French 2"));
        }

        [Test]
        public void Scan_PriorityFallback_UsesSecondPriorityWhenFirstEmpty_ThenAnyNonEmptyIfNoPriorityMatches()
        {
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='fr'></div>
                        <div class='bloom-editable' lang='en'>English text</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='es'>Spanish text</div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "de",
                new List<AiTranslationEngineSettings>(),
                new[] { "fr", "en" }
            );

            var scan = scanner.Scan();

            Assert.That(scan.Groups.Count, Is.EqualTo(2));
            Assert.That(
                scan.Groups[0].SourceText,
                Is.EqualTo("English text"),
                "first priority (fr) is empty, so it should fall back to the second priority (en)"
            );
            Assert.That(
                scan.Groups[1].SourceText,
                Is.EqualTo("Spanish text"),
                "no priority matches (only 'es' is present), so it should fall back to the first non-empty editable"
            );
        }

        [Test]
        public void ChooseSourceDiv_SkipsZLanguageAndAiLanguageEditables()
        {
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='z'>Z placeholder</div>
                        <div class='bloom-editable' lang='es-x-ai-deepl'>Existing AI text</div>
                        <div class='bloom-editable' lang='en'>English text</div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings>(),
                new string[0] // no priorities match, so this exercises the fallback path
            );

            var scan = scanner.Scan();

            Assert.That(scan.Groups.Count, Is.EqualTo(1));
            Assert.That(scan.Groups[0].SourceText, Is.EqualTo("English text"));
        }

        [Test]
        public void Scan_SkipsGroupsWithEmptyOrWhitespaceOnlySourceText()
        {
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>   </div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Real text</div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings>(),
                new[] { "en" }
            );

            var scan = scanner.Scan();

            Assert.That(scan.Groups.Count, Is.EqualTo(1));
            Assert.That(scan.Groups[0].SourceText, Is.EqualTo("Real text"));
        }

        [Test]
        public void Scan_SkipsNoSourceBubbleReadOnlyAndOtherBookDataGroups()
        {
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup bloom-no-source-bubble'>
                        <div class='bloom-editable' lang='en'>Should be skipped 1</div>
                    </div>
                    <div class='bloom-translationGroup bloom-readOnlyInTranslationMode'>
                        <div class='bloom-editable' lang='en'>Should be skipped 2</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' data-book='topic' lang='en'>Should be skipped 3</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Should be included</div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings>(),
                new[] { "en" }
            );

            var scan = scanner.Scan();

            Assert.That(scan.Groups.Count, Is.EqualTo(1));
            Assert.That(scan.Groups[0].SourceText, Is.EqualTo("Should be included"));
        }

        [Test]
        public void Scan_IncludesBookTitleGroup_WithIsBookTitleTrue()
        {
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' data-book='bookTitle' lang='en'>My Book</div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings>(),
                new[] { "en" }
            );

            var scan = scanner.Scan();

            Assert.That(scan.Groups.Count, Is.EqualTo(1));
            Assert.That(scan.Groups[0].IsBookTitle, Is.True);
            Assert.That(scan.Groups[0].SourceText, Is.EqualTo("My Book"));
        }

        [Test]
        public void GroupsNeedingTranslation_CurrentFingerprintNotIncluded_WrongFingerprintOrEmptyIncluded()
        {
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", Enabled = true };
            var aiTag = AiTranslationService.GetAiLanguageTag("es", "deepl");
            Assert.That(aiTag, Is.EqualTo("es-x-ai-deepl"), "sanity check: expected AI tag format");
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
                        <div class='bloom-editable' lang='{aiTag}' data-ai-fingerprint='{currentFingerprint}'>Bonjour</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>World</div>
                        <div class='bloom-editable' lang='{aiTag}' data-ai-fingerprint='stale-fingerprint'>OldTranslation</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Empty div case</div>
                        <div class='bloom-editable' lang='{aiTag}' data-ai-fingerprint='{currentFingerprint}'></div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings> { engine },
                new[] { "en" }
            );

            var scan = scanner.Scan();
            Assert.That(
                scan.Groups.Count,
                Is.EqualTo(3),
                "sanity check: all three groups should be eligible"
            );

            var needing = scan.GroupsNeedingTranslation(engine);

            Assert.That(needing.Count, Is.EqualTo(2));
            Assert.That(
                needing.Select(g => g.SourceText),
                Is.EquivalentTo(new[] { "World", "Empty div case" })
            );
        }

        [Test]
        public void ApplyTranslation_WritesDivWithClassLangFingerprint_AndReplacesOnSecondCall()
        {
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", Enabled = true };
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings> { engine },
                new[] { "en" }
            );
            var group = scanner.Scan().Groups.Single();
            Assert.That(
                group.SourceText,
                Is.EqualTo("Hello"),
                "sanity check on chosen source text"
            );

            scanner.ApplyTranslation(group, engine, "Hola");

            var aiTag = AiTranslationService.GetAiLanguageTag("es", "deepl");
            var aiDivs = group.GroupElement.SafeSelectElements($"div[@lang='{aiTag}']");
            Assert.That(
                aiDivs.Length,
                Is.EqualTo(1),
                "sanity check: exactly one AI div should exist after the first apply"
            );

            var aiDiv = aiDivs[0];
            Assert.That(aiDiv.InnerText, Is.EqualTo("Hola"));
            Assert.That(aiDiv.HasClass("bloom-editable"), Is.True);
            Assert.That(aiDiv.HasClass("bloom-ai-translation"), Is.True);
            var expectedFingerprint = AiTranslationBookScanner.ComputeFingerprint(
                "en",
                "Hello",
                aiTag
            );
            Assert.That(aiDiv.GetAttribute("data-ai-fingerprint"), Is.EqualTo(expectedFingerprint));

            // Applying again should replace the existing div, not duplicate it.
            scanner.ApplyTranslation(group, engine, "Hola de nuevo");
            var aiDivsAfterSecondApply = group.GroupElement.SafeSelectElements(
                $"div[@lang='{aiTag}']"
            );
            Assert.That(aiDivsAfterSecondApply.Length, Is.EqualTo(1));
            Assert.That(aiDivsAfterSecondApply[0].InnerText, Is.EqualTo("Hola de nuevo"));
        }

        [Test]
        public void ApplyTranslation_ForBookTitleGroup_WritesDataDivEntry()
        {
            var engine = new AiTranslationEngineSettings { ProviderId = "google", Enabled = true };
            var dom = MakeBookDom(
                @"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' data-book='bookTitle' lang='en'>My Book</div>
                    </div>
                </div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "fr",
                new List<AiTranslationEngineSettings> { engine },
                new[] { "en" }
            );
            var group = scanner.Scan().Groups.Single();
            Assert.That(
                group.IsBookTitle,
                Is.True,
                "sanity check: this group should be recognized as the book title group"
            );

            scanner.ApplyTranslation(group, engine, "Mon Livre");

            var aiTag = AiTranslationService.GetAiLanguageTag("fr", "google");
            var dataDiv =
                dom.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']") as SafeXmlElement;
            var titleEntries = dataDiv.SafeSelectElements(
                $"div[@data-book='bookTitle' and @lang='{aiTag}']"
            );
            Assert.That(titleEntries.Length, Is.EqualTo(1));
            Assert.That(titleEntries[0].InnerText, Is.EqualTo("Mon Livre"));
        }

        [Test]
        public void ApplyTranslation_BookTitleGroup_NoDataDivPresent_DoesNotThrow()
        {
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", Enabled = true };
            var dom = new HtmlDom(
                @"<html><body>
                    <div class='bloom-page'>
                        <div class='bloom-translationGroup'>
                            <div class='bloom-editable' data-book='bookTitle' lang='en'>My Book</div>
                        </div>
                    </div>
                </body></html>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new List<AiTranslationEngineSettings> { engine },
                new[] { "en" }
            );
            var group = scanner.Scan().Groups.Single();

            Assert.DoesNotThrow(() => scanner.ApplyTranslation(group, engine, "Mi Libro"));
        }

        [Test]
        public void RemoveStaleAiDivs_RemovesDisabledEngineAndStaleFingerprintAndOrphanedDivs_KeepsCurrentOnes_CleansDataDiv()
        {
            var enabledEngine = new AiTranslationEngineSettings
            {
                ProviderId = "deepl",
                Enabled = true,
            };
            const string targetTag = "es";
            var enabledAiTag = AiTranslationService.GetAiLanguageTag(targetTag, "deepl");
            var disabledAiTag = AiTranslationService.GetAiLanguageTag(targetTag, "google"); // google isn't in the enabled-engines list below
            var currentFingerprint = AiTranslationBookScanner.ComputeFingerprint(
                "en",
                "Hello",
                enabledAiTag
            );

            var dom = MakeBookDom(
                $@"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='{enabledAiTag}' data-ai-fingerprint='{currentFingerprint}'>Hola</div>
                        <div class='bloom-editable' lang='{disabledAiTag}' data-ai-fingerprint='whatever'>Should be removed (disabled engine)</div>
                    </div>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>World</div>
                        <div class='bloom-editable' lang='{enabledAiTag}' data-ai-fingerprint='stale'>Should be removed (stale fingerprint)</div>
                    </div>
                    <div class='bloom-translationGroup bloom-no-source-bubble'>
                        <div class='bloom-editable' lang='en'>Excluded group</div>
                        <div class='bloom-editable' lang='{enabledAiTag}' data-ai-fingerprint='irrelevant'>Should be removed (orphaned)</div>
                    </div>
                </div>",
                dataDivHtml: $@"<div data-book='bookTitle' lang='{enabledAiTag}'>Stale title translation</div>"
            );

            Assert.That(
                dom.RawDom.SafeSelectNodes("//div[@lang and contains(@lang,'-x-ai')]").Length,
                Is.EqualTo(5),
                "sanity check: fixture should start with 5 AI divs total (4 in pages, 1 in the data div)"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                targetTag,
                new List<AiTranslationEngineSettings> { enabledEngine },
                new[] { "en" }
            );

            var removedCount = scanner.RemoveStaleAiDivs();

            Assert.That(
                removedCount,
                Is.EqualTo(4),
                "3 in the pages (disabled-engine, stale-fingerprint, orphaned) plus the data-div bookTitle entry (no matching valid page-level div)"
            );

            var remainingAiDivs = dom.RawDom.SafeSelectElements(
                "//div[@lang and contains(@lang,'-x-ai')]"
            );
            Assert.That(remainingAiDivs.Length, Is.EqualTo(1));
            Assert.That(remainingAiDivs[0].GetAttribute("lang"), Is.EqualTo(enabledAiTag));
            Assert.That(remainingAiDivs[0].InnerText, Is.EqualTo("Hola"));
        }

        [Test]
        public void RemoveStaleAiDivs_KeepsCurrentBookTitleDataDivEntry_WhenPageLevelDivIsStillCurrent()
        {
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", Enabled = true };
            const string targetTag = "es";
            var aiTag = AiTranslationService.GetAiLanguageTag(targetTag, "deepl");
            var currentFingerprint = AiTranslationBookScanner.ComputeFingerprint(
                "en",
                "My Book",
                aiTag
            );

            var dom = MakeBookDom(
                $@"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' data-book='bookTitle' lang='en'>My Book</div>
                        <div class='bloom-editable' data-book='bookTitle' lang='{aiTag}' data-ai-fingerprint='{currentFingerprint}'>Mi Libro</div>
                    </div>
                </div>",
                dataDivHtml: $@"<div data-book='bookTitle' lang='{aiTag}'>Mi Libro</div>"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                targetTag,
                new List<AiTranslationEngineSettings> { engine },
                new[] { "en" }
            );

            var removedCount = scanner.RemoveStaleAiDivs();

            Assert.That(removedCount, Is.EqualTo(0));
            var dataDiv =
                dom.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']") as SafeXmlElement;
            Assert.That(
                dataDiv
                    .SafeSelectElements($"div[@data-book='bookTitle' and @lang='{aiTag}']")
                    .Length,
                Is.EqualTo(1),
                "the current data-div bookTitle entry should be kept since the page-level div is still current"
            );
        }

        [Test]
        public void RemoveAllAiDivs_RemovesEveryAiDivIncludingCurrentOnes_KeepsNonAiDivs_CleansDataDiv()
        {
            const string targetTag = "es";
            var deepLTag = AiTranslationService.GetAiLanguageTag(targetTag, "deepl");
            var googleTag = AiTranslationService.GetAiLanguageTag(targetTag, "google");
            // A perfectly current translation: RemoveStaleAiDivs would keep this, but RemoveAllAiDivs
            // must remove it too.
            var currentFingerprint = AiTranslationBookScanner.ComputeFingerprint(
                "en",
                "Hello",
                deepLTag
            );

            var dom = MakeBookDom(
                $@"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='{deepLTag}' data-ai-fingerprint='{currentFingerprint}'>Hola</div>
                        <div class='bloom-editable' lang='{googleTag}' data-ai-fingerprint='whatever'>Hola (google)</div>
                    </div>
                    <div class='bloom-translationGroup bloom-no-source-bubble'>
                        <div class='bloom-editable' lang='en'>Excluded group</div>
                        <div class='bloom-editable' lang='{deepLTag}' data-ai-fingerprint='irrelevant'>Orphaned AI text</div>
                    </div>
                </div>",
                dataDivHtml: $@"<div data-book='bookTitle' lang='{deepLTag}'>Title translation</div>"
            );

            Assert.That(
                dom.RawDom.SafeSelectNodes("//div[@lang and contains(@lang,'-x-ai')]").Length,
                Is.EqualTo(4),
                "sanity check: fixture should start with 4 AI divs total (3 in pages, 1 in the data div)"
            );

            // Constructed with no engine/target/priority context at all, to prove RemoveAllAiDivs
            // does not depend on any of them (this is how the menu command calls it).
            var scanner = new AiTranslationBookScanner(dom, null, null, null);

            var removedCount = scanner.RemoveAllAiDivs();

            Assert.That(
                removedCount,
                Is.EqualTo(4),
                "every AI div should be removed: current, disabled-engine, orphaned, and the data-div entry"
            );
            Assert.That(
                dom.RawDom.SafeSelectNodes("//div[@lang and contains(@lang,'-x-ai')]").Length,
                Is.EqualTo(0),
                "no AI divs should remain anywhere in the book"
            );
            // The non-AI source divs must be untouched.
            Assert.That(
                dom.RawDom.SafeSelectNodes("//div[@lang='en']").Length,
                Is.EqualTo(2),
                "both English source divs should be left in place"
            );
        }

        [Test]
        public void GroupsNeedingTranslation_FixedSourceEngine_UsesConfiguredSourceLanguageText()
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
                </div>"
            );
            var scanner = new AiTranslationBookScanner(
                dom,
                "es",
                new[] { alpha2 },
                new[] { "en" } // priority prefers en, but alpha2's fixed source must win for alpha2.
            );

            var needing = scanner.Scan().GroupsNeedingTranslation(alpha2);

            Assert.That(needing.Count, Is.EqualTo(1));
            Assert.That(
                needing[0].SourceLanguageTag,
                Is.EqualTo("fr"),
                "a fixed-source engine translates from its configured source, not the priority default"
            );
            Assert.That(needing[0].SourceText, Is.EqualTo("Bonjour"));
        }

        [Test]
        public void GroupsNeedingTranslation_FixedSourceEngine_MissingSourceLanguage_ExcludedAndCountedAsSkipped()
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
                    </div>
                </div>"
            );
            var scanner = new AiTranslationBookScanner(dom, "es", new[] { alpha2 }, new[] { "en" });
            var scan = scanner.Scan();
            Assert.That(scan.Groups.Count, Is.EqualTo(1), "sanity check: the group is eligible");

            Assert.That(
                scan.GroupsNeedingTranslation(alpha2),
                Is.Empty,
                "the group has no French text, so the fixed-source alpha2 engine must skip it"
            );
            Assert.That(scan.CountGroupsSkippedForEngine(alpha2), Is.EqualTo(1));
        }

        [Test]
        public void GroupsNeedingTranslation_SameGroup_Alpha2AndDeeplGetDifferentSources()
        {
            var deepl = new AiTranslationEngineSettings { ProviderId = "deepl", Enabled = true };
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

            var deeplWork = scan.GroupsNeedingTranslation(deepl).Single();
            var alpha2Work = scan.GroupsNeedingTranslation(alpha2).Single();

            Assert.That(
                deeplWork.SourceLanguageTag,
                Is.EqualTo("en"),
                "deepl uses the priority-chosen default source"
            );
            Assert.That(deeplWork.SourceText, Is.EqualTo("Hello"));
            Assert.That(
                alpha2Work.SourceLanguageTag,
                Is.EqualTo("fr"),
                "alpha2 uses its fixed configured source for the same group"
            );
            Assert.That(alpha2Work.SourceText, Is.EqualTo("Bonjour"));
        }

        [Test]
        public void RemoveStaleAiDivs_FixedSourceEngineLostItsSourceText_RemovesDiv()
        {
            var alpha2 = new AiTranslationEngineSettings
            {
                ProviderId = "alpha2",
                Enabled = true,
                SourceLanguageTag = "fr",
            };
            const string targetTag = "es";
            var alpha2AiTag = AiTranslationService.GetAiLanguageTag(targetTag, "alpha2");
            // The group has English text but NO French text, yet carries an alpha2 (French-source)
            // AI div left over from when French text existed. Since alpha2 can no longer resolve a
            // French source for the group, its div is stale and must be removed.
            var dom = MakeBookDom(
                $@"
                <div class='bloom-page'>
                    <div class='bloom-translationGroup'>
                        <div class='bloom-editable' lang='en'>Hello</div>
                        <div class='bloom-editable' lang='{alpha2AiTag}' data-ai-fingerprint='whatever'>Hola (from French, now orphaned)</div>
                    </div>
                </div>"
            );
            Assert.That(
                dom.RawDom.SafeSelectNodes("//div[@lang and contains(@lang,'-x-ai')]").Length,
                Is.EqualTo(1),
                "sanity check: fixture starts with one alpha2 AI div"
            );

            var scanner = new AiTranslationBookScanner(
                dom,
                targetTag,
                new[] { alpha2 },
                new[] { "en" }
            );

            var removedCount = scanner.RemoveStaleAiDivs();

            Assert.That(removedCount, Is.EqualTo(1));
            Assert.That(
                dom.RawDom.SafeSelectNodes("//div[@lang and contains(@lang,'-x-ai')]").Length,
                Is.EqualTo(0),
                "the fixed-source engine's div is stale because its source language is gone"
            );
        }
    }
}
