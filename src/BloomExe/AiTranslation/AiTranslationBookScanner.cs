using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;

namespace Bloom.AiTranslation
{
    /// <summary>
    /// One eligible translation group found by AiTranslationBookScanner.Scan(): the chosen
    /// source text to translate, and whether a current (up to date) translation already exists
    /// per engine.
    /// </summary>
    public class AiTranslationGroupInfo
    {
        /// <summary>The bloom-translationGroup element this info describes.</summary>
        public SafeXmlElement GroupElement { get; }

        /// <summary>The language tag of the bloom-editable chosen as the translation source.</summary>
        public string SourceLanguageTag { get; }

        /// <summary>The trimmed text of the chosen source bloom-editable.</summary>
        public string SourceText { get; }

        /// <summary>True if this is the book's data-book="bookTitle" translation group.</summary>
        public bool IsBookTitle { get; }

        /// <summary>
        /// Creates group info for one eligible translation group. See
        /// AiTranslationBookScanner.Scan() for how the source language/text and IsBookTitle are
        /// determined.
        /// </summary>
        public AiTranslationGroupInfo(
            SafeXmlElement groupElement,
            string sourceLanguageTag,
            string sourceText,
            bool isBookTitle
        )
        {
            GroupElement = groupElement;
            SourceLanguageTag = sourceLanguageTag;
            SourceText = sourceText;
            IsBookTitle = isBookTitle;
        }

        /// <summary>
        /// The fingerprint a translation div for the given engine must currently have, based on
        /// this group's current source language/text and the engine's AI language tag.
        /// </summary>
        public string GetExpectedFingerprint(
            AiTranslationEngineSettings engine,
            string targetLanguageTag
        )
        {
            var aiTag = AiTranslationService.GetAiLanguageTag(targetLanguageTag, engine.ProviderId);
            return AiTranslationBookScanner.ComputeFingerprint(
                SourceLanguageTag,
                SourceText,
                aiTag
            );
        }

        /// <summary>
        /// True if this group already has a non-empty translation div for the given engine's AI
        /// language tag whose fingerprint matches the current source (i.e. it does NOT need
        /// (re)translation).
        /// </summary>
        public bool HasCurrentTranslation(
            AiTranslationEngineSettings engine,
            string targetLanguageTag
        )
        {
            var aiTag = AiTranslationService.GetAiLanguageTag(targetLanguageTag, engine.ProviderId);
            var existing = GroupElement
                .SafeSelectElements($"div[@lang='{aiTag}']")
                .FirstOrDefault();
            if (existing == null)
                return false;

            var text = (existing.InnerText ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                return false;

            return existing.GetAttribute("data-ai-fingerprint")
                == GetExpectedFingerprint(engine, targetLanguageTag);
        }
    }

    /// <summary>
    /// The result of AiTranslationBookScanner.Scan(): every eligible translation group found, in
    /// document order.
    /// </summary>
    public class AiTranslationBookScan
    {
        /// <summary>All eligible groups found, in document order.</summary>
        public List<AiTranslationGroupInfo> Groups { get; }

        private readonly string _targetLanguageTag;

        /// <summary>Wraps the groups found by a scan, remembering the target language they were scanned for.</summary>
        public AiTranslationBookScan(List<AiTranslationGroupInfo> groups, string targetLanguageTag)
        {
            Groups = groups;
            _targetLanguageTag = targetLanguageTag;
        }

        /// <summary>
        /// The ordered subset of Groups that still need a (re)translation for the given engine:
        /// those lacking a current, fingerprint-matching translation div.
        /// </summary>
        public List<AiTranslationGroupInfo> GroupsNeedingTranslation(
            AiTranslationEngineSettings engine
        )
        {
            return Groups.Where(g => !g.HasCurrentTranslation(engine, _targetLanguageTag)).ToList();
        }
    }

    /// <summary>
    /// Scans a book's DOM for translation groups eligible for AI translation, and
    /// applies or removes AI-generated translation divs. This class does no network calls and no
    /// orchestration; it is purely the scan + DOM read/write layer that the book updater (Phase 3)
    /// drives around calls to AiTranslationService.TranslateSegmentsAsync.
    /// </summary>
    public class AiTranslationBookScanner
    {
        private const string kAiLangTagFragment = "-x-ai";
        private const string kBloomEditableClass = "bloom-editable";
        private const string kAiTranslationClass = "bloom-ai-translation";
        private const string kBookTitleDataBookValue = "bookTitle";
        private const string kZeroLang = "z";

        private readonly HtmlDom _bookDom;
        private readonly string _targetLanguageTag;
        private readonly List<AiTranslationEngineSettings> _enabledEngines;
        private readonly IReadOnlyList<string> _sourceLanguagePriorities;

        /// <summary>
        /// Creates a scanner for one book's DOM. sourceLanguagePriorities is the ordered list of
        /// candidate language tags to prefer when choosing which bloom-editable's text is the
        /// translation source for a group (e.g. the user's last-viewed source languages, then the
        /// collection's L2/L3 tags, then "en").
        /// </summary>
        public AiTranslationBookScanner(
            HtmlDom bookDom,
            string targetLanguageTag,
            IEnumerable<AiTranslationEngineSettings> enabledEngines,
            IReadOnlyList<string> sourceLanguagePriorities
        )
        {
            _bookDom = bookDom ?? throw new ArgumentNullException(nameof(bookDom));
            _targetLanguageTag = targetLanguageTag;
            _enabledEngines = (
                enabledEngines ?? Enumerable.Empty<AiTranslationEngineSettings>()
            ).ToList();
            _sourceLanguagePriorities = sourceLanguagePriorities ?? Array.Empty<string>();
        }

        /// <summary>
        /// Scans all pages' translation groups, in document order, for groups eligible for AI
        /// translation (see TryBuildGroupInfo for eligibility rules), choosing a
        /// source text for each from sourceLanguagePriorities.
        /// </summary>
        public AiTranslationBookScan Scan()
        {
            var groups = new List<AiTranslationGroupInfo>();
            foreach (var page in SafeXmlElement.GetAllDivsWithClass(_bookDom.Body, "bloom-page"))
            {
                foreach (
                    var group in SafeXmlElement.GetAllDivsWithClass(page, "bloom-translationGroup")
                )
                {
                    var info = TryBuildGroupInfo(group);
                    if (info != null)
                        groups.Add(info);
                }
            }

            return new AiTranslationBookScan(groups, _targetLanguageTag);
        }

        /// <summary>
        /// Writes (creating or replacing) the AI translation div for one group+engine with the
        /// given translated text and a matching data-ai-fingerprint. For bookTitle groups, also
        /// writes/refreshes the matching entry in #bloomDataDiv (creates nothing if the data div
        /// doesn't exist; real books always have one, but we don't want to throw on a test DOM
        /// that omits it).
        /// </summary>
        public void ApplyTranslation(
            AiTranslationGroupInfo group,
            AiTranslationEngineSettings engine,
            string translatedText
        )
        {
            var aiTag = AiTranslationService.GetAiLanguageTag(
                _targetLanguageTag,
                engine.ProviderId
            );
            var fingerprint = ComputeFingerprint(group.SourceLanguageTag, group.SourceText, aiTag);

            WriteChildDiv(
                group.GroupElement,
                aiTag,
                translatedText,
                div =>
                {
                    div.AddClass(kBloomEditableClass);
                    div.AddClass(kAiTranslationClass);
                    div.SetAttribute("data-ai-fingerprint", fingerprint);
                }
            );

            if (!group.IsBookTitle)
                return;

            var dataDiv = FindBloomDataDiv();
            if (dataDiv == null)
                return; // real books always have one; nothing to do if a test DOM omits it.

            WriteChildDiv(
                dataDiv,
                aiTag,
                translatedText,
                div => div.SetAttribute("data-book", kBookTitleDataBookValue),
                matchAttribute: "data-book",
                matchValue: kBookTitleDataBookValue
            );
        }

        /// <summary>
        /// Removes AI translation divs that no longer belong: any div whose lang contains "-x-ai"
        /// under a group that is no longer eligible for AI translation at all (orphaned), or under
        /// an eligible group but for a disabled engine/inactive target language, or with a
        /// fingerprint that no longer matches the group's current source text (the source was
        /// edited since translation). Also cleans matching #bloomDataDiv bookTitle entries. Returns
        /// the total number of divs removed.
        /// </summary>
        public int RemoveStaleAiDivs()
        {
            var activeTags = new HashSet<string>(
                _enabledEngines.Select(e =>
                    AiTranslationService.GetAiLanguageTag(_targetLanguageTag, e.ProviderId)
                ),
                StringComparer.OrdinalIgnoreCase
            );
            var validBookTitleLangs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removedCount = 0;

            foreach (var page in SafeXmlElement.GetAllDivsWithClass(_bookDom.Body, "bloom-page"))
            {
                foreach (
                    var group in SafeXmlElement.GetAllDivsWithClass(page, "bloom-translationGroup")
                )
                {
                    // Rebuild eligibility from scratch (not just the eligible groups from Scan())
                    // so we also catch AI divs left behind in groups that are no longer eligible.
                    var groupInfo = TryBuildGroupInfo(group);
                    foreach (var aiDiv in GetAiChildDivs(group))
                    {
                        var lang = aiDiv.GetAttribute("lang");
                        if (ShouldRemoveGroupAiDiv(groupInfo, aiDiv, lang, activeTags))
                        {
                            group.RemoveChild(aiDiv);
                            removedCount++;
                        }
                        else if (groupInfo.IsBookTitle)
                        {
                            validBookTitleLangs.Add(lang);
                        }
                    }
                }
            }

            var dataDiv = FindBloomDataDiv();
            if (dataDiv != null)
            {
                foreach (var aiEntry in GetAiChildDivs(dataDiv))
                {
                    var isBookTitleEntry =
                        aiEntry.GetAttribute("data-book") == kBookTitleDataBookValue;
                    var lang = aiEntry.GetAttribute("lang");
                    // Any non-bookTitle AI entry in the data div is never written by this class,
                    // so it's inherently stale/orphaned. A bookTitle entry is only valid if its
                    // matching page-level translation div is still current (see loop above).
                    if (!isBookTitleEntry || !validBookTitleLangs.Contains(lang))
                    {
                        dataDiv.RemoveChild(aiEntry);
                        removedCount++;
                    }
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Removes every AI-generated translation div from the book -- all "-x-ai" divs under any
        /// bloom-translationGroup and all matching #bloomDataDiv entries -- regardless of engine,
        /// target language, or staleness. Unlike RemoveStaleAiDivs, this also removes current,
        /// in-use translations: it is the "remove all AI source translations" operation, and does
        /// not depend on which engines/target language the scanner was constructed with. Returns
        /// the total number of divs removed.
        /// </summary>
        public int RemoveAllAiDivs()
        {
            var removedCount = 0;

            foreach (var page in SafeXmlElement.GetAllDivsWithClass(_bookDom.Body, "bloom-page"))
            {
                foreach (
                    var group in SafeXmlElement.GetAllDivsWithClass(page, "bloom-translationGroup")
                )
                {
                    foreach (var aiDiv in GetAiChildDivs(group))
                    {
                        group.RemoveChild(aiDiv);
                        removedCount++;
                    }
                }
            }

            var dataDiv = FindBloomDataDiv();
            if (dataDiv != null)
            {
                foreach (var aiEntry in GetAiChildDivs(dataDiv))
                {
                    dataDiv.RemoveChild(aiEntry);
                    removedCount++;
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Computes the fingerprint stored in data-ai-fingerprint, used to detect a stale AI
        /// translation: the first 16 hex characters of the SHA-256 hash of
        /// "{sourceLanguageTag}\n{sourceText}\n{aiLanguageTag}".
        /// </summary>
        public static string ComputeFingerprint(
            string sourceLanguageTag,
            string sourceText,
            string aiLanguageTag
        )
        {
            var input = $"{sourceLanguageTag}\n{sourceText}\n{aiLanguageTag}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).Substring(0, 16);
        }

        /// <summary>
        /// Builds group info for one translation group, or returns null if the group should be
        /// skipped: explicitly excluded (bloom-no-source-bubble / bloom-readOnlyInTranslationMode),
        /// tied to book data other than the title, or lacking any usable source text.
        /// </summary>
        private AiTranslationGroupInfo TryBuildGroupInfo(SafeXmlElement group)
        {
            if (
                group.HasClass("bloom-no-source-bubble")
                || group.HasClass("bloom-readOnlyInTranslationMode")
            )
                return null;

            var editables = SafeXmlElement.GetAllDivsWithClass(group, kBloomEditableClass);

            var isBookTitle = false;
            foreach (var editable in editables)
            {
                if (!editable.HasAttribute("data-book"))
                    continue;
                if (editable.GetAttribute("data-book") == kBookTitleDataBookValue)
                    isBookTitle = true;
                else
                    return null; // tied to some other book-data item; not ours to translate.
            }

            var sourceDiv = ChooseSourceDiv(editables);
            if (sourceDiv == null)
                return null;

            var sourceText = (sourceDiv.InnerText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sourceText))
                return null;

            return new AiTranslationGroupInfo(
                group,
                sourceDiv.GetAttribute("lang"),
                sourceText,
                isBookTitle
            );
        }

        /// <summary>
        /// Chooses which bloom-editable's text is the translation source: the first non-empty
        /// match walking sourceLanguagePriorities in order (skipping AI and "z" language divs),
        /// falling back to the first non-empty, non-AI, non-"z" editable if no priority matches.
        /// </summary>
        private SafeXmlElement ChooseSourceDiv(IEnumerable<SafeXmlElement> editables)
        {
            var candidates = editables
                .Where(e => e.HasAttribute("lang"))
                .Where(e => !e.GetAttribute("lang").Contains(kAiLangTagFragment))
                .Where(e => e.GetAttribute("lang") != kZeroLang)
                .ToList();

            foreach (var priorityLang in _sourceLanguagePriorities)
            {
                var match = candidates.FirstOrDefault(e =>
                    e.GetAttribute("lang") == priorityLang
                    && !string.IsNullOrWhiteSpace(e.InnerText)
                );
                if (match != null)
                    return match;
            }

            return candidates.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.InnerText));
        }

        /// <summary>
        /// Direct child divs of parent whose lang attribute contains "-x-ai" (AI-generated content
        /// of any provider/target, active or not).
        /// </summary>
        private static List<SafeXmlElement> GetAiChildDivs(SafeXmlElement parent)
        {
            return parent
                .SafeSelectElements($"div[@lang and contains(@lang, '{kAiLangTagFragment}')]")
                .ToList();
        }

        /// <summary>
        /// Decides whether one existing AI div under a translation group should be removed:
        /// always if the group is no longer eligible for AI translation at all (its AI content is
        /// orphaned), otherwise if its language isn't currently active (disabled engine or changed
        /// target language), or its fingerprint no longer matches the group's current source text.
        /// </summary>
        private static bool ShouldRemoveGroupAiDiv(
            AiTranslationGroupInfo groupInfo,
            SafeXmlElement aiDiv,
            string lang,
            HashSet<string> activeTags
        )
        {
            if (groupInfo == null)
                return true;

            if (!activeTags.Contains(lang))
                return true;

            var text = (aiDiv.InnerText ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                return true;

            var expectedFingerprint = ComputeFingerprint(
                groupInfo.SourceLanguageTag,
                groupInfo.SourceText,
                lang
            );
            return aiDiv.GetAttribute("data-ai-fingerprint") != expectedFingerprint;
        }

        /// <summary>Finds the book's #bloomDataDiv element, or null if it isn't present.</summary>
        private SafeXmlElement FindBloomDataDiv()
        {
            return _bookDom.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']") as SafeXmlElement;
        }

        /// <summary>
        /// Creates or replaces the direct child div of parent with the given lang: removes any
        /// existing match (by lang, or by matchAttribute/matchValue when given, for cases like
        /// #bloomDataDiv where multiple data-book items could share a lang), then appends a fresh
        /// div with that lang and the given text (set via InnerText so markup in translations can't
        /// inject HTML), letting configureAttributes add any extra attributes/classes.
        /// </summary>
        private static void WriteChildDiv(
            SafeXmlElement parent,
            string lang,
            string text,
            Action<SafeXmlElement> configureAttributes,
            string matchAttribute = null,
            string matchValue = null
        )
        {
            var xpath =
                matchAttribute == null
                    ? $"div[@lang='{lang}']"
                    : $"div[@lang='{lang}' and @{matchAttribute}='{matchValue}']";
            var existing = parent.SafeSelectElements(xpath).FirstOrDefault();
            if (existing != null)
                parent.RemoveChild(existing);

            var newDiv = parent.AppendChild("div");
            newDiv.SetAttribute("lang", lang);
            configureAttributes(newDiv);
            newDiv.InnerText = text;
        }
    }
}
