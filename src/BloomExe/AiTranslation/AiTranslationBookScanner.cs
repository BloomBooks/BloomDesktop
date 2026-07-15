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
    /// One eligible translation group found by AiTranslationBookScanner.Scan(): the chosen default
    /// source text to translate, the text of every candidate source language in the group, and
    /// whether a current (up to date) translation already exists per engine.
    /// </summary>
    public class AiTranslationGroupInfo
    {
        /// <summary>The bloom-translationGroup element this info describes.</summary>
        public SafeXmlElement GroupElement { get; }

        /// <summary>
        /// The language tag of the bloom-editable chosen (by source-language priority) as the
        /// default translation source. This is what the automatic-source engines (deepl/google) use.
        /// </summary>
        public string SourceLanguageTag { get; }

        /// <summary>The trimmed text of the default (priority-chosen) source bloom-editable.</summary>
        public string SourceText { get; }

        /// <summary>
        /// The trimmed text of every non-AI, non-"z", non-empty bloom-editable in this group, keyed
        /// by its lang attribute (in document order). Used by fixed-source engines (alpha2) to look
        /// up whether the group has text in the engine's configured source language.
        /// </summary>
        public IReadOnlyDictionary<string, string> TextsByLanguage { get; }

        /// <summary>True if this is the book's data-book="bookTitle" translation group.</summary>
        public bool IsBookTitle { get; }

        /// <summary>
        /// Creates group info for one eligible translation group. See
        /// AiTranslationBookScanner.Scan() for how the default source language/text, the
        /// per-language texts, and IsBookTitle are determined.
        /// </summary>
        public AiTranslationGroupInfo(
            SafeXmlElement groupElement,
            string sourceLanguageTag,
            string sourceText,
            IReadOnlyDictionary<string, string> textsByLanguage,
            bool isBookTitle
        )
        {
            GroupElement = groupElement;
            SourceLanguageTag = sourceLanguageTag;
            SourceText = sourceText;
            TextsByLanguage =
                textsByLanguage ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IsBookTitle = isBookTitle;
        }

        /// <summary>
        /// Resolves the source language tag and text this engine should translate from for this
        /// group. Automatic-source engines (deepl/google) always use the group's priority-chosen
        /// default. Fixed-source engines (alpha2) require text in their configured source language
        /// (matched on normalized primary subtag, so an "en" setting matches an "en-US" editable);
        /// if the group has no such text, this returns false and the group is ineligible for that
        /// engine.
        /// </summary>
        public bool TryGetSourceForEngine(
            AiTranslationEngineSettings engine,
            out string sourceLanguageTag,
            out string sourceText
        )
        {
            var fixedSource = AiTranslationBookScanner.GetFixedSourceLanguageTagOrNull(engine);
            if (fixedSource == null)
            {
                sourceLanguageTag = SourceLanguageTag;
                sourceText = SourceText;
                return !string.IsNullOrWhiteSpace(SourceText);
            }

            var wantedPrimarySubtag = AiTranslationBookScanner.GetPrimarySubtag(fixedSource);
            // Prefer an exact-normalized match, then fall back to any editable sharing the primary
            // subtag (in document order), so a fixed "en" source can use an "en-US" editable.
            foreach (var pair in TextsByLanguage)
            {
                if (
                    AiTranslationBookScanner.GetPrimarySubtag(pair.Key) == wantedPrimarySubtag
                    && !string.IsNullOrWhiteSpace(pair.Value)
                )
                {
                    sourceLanguageTag = pair.Key;
                    sourceText = pair.Value;
                    return true;
                }
            }

            sourceLanguageTag = null;
            sourceText = null;
            return false;
        }

        /// <summary>
        /// The fingerprint a translation div for the given engine must currently have, based on the
        /// engine's resolved source language/text for this group and the engine's AI language tag.
        /// Returns null if the engine has no usable source for this group (a fixed-source engine
        /// whose source language is absent), meaning any existing div for it is stale.
        /// </summary>
        public string GetExpectedFingerprint(
            AiTranslationEngineSettings engine,
            string targetLanguageTag
        )
        {
            if (!TryGetSourceForEngine(engine, out var sourceLanguageTag, out var sourceText))
                return null;

            var aiTag = AiTranslationService.GetAiLanguageTag(targetLanguageTag, engine.ProviderId);
            return AiTranslationBookScanner.ComputeFingerprint(
                sourceLanguageTag,
                sourceText,
                aiTag
            );
        }

        /// <summary>
        /// True if this group does NOT need (re)translation for the given engine: either the engine
        /// has no usable source for it (so there is nothing to translate), or it already has a
        /// non-empty translation div for the engine's AI language tag whose fingerprint matches the
        /// engine's current resolved source.
        /// </summary>
        public bool HasCurrentTranslation(
            AiTranslationEngineSettings engine,
            string targetLanguageTag
        )
        {
            var expectedFingerprint = GetExpectedFingerprint(engine, targetLanguageTag);
            if (expectedFingerprint == null)
                return true; // no source for this engine -> nothing to (re)translate.

            var aiTag = AiTranslationService.GetAiLanguageTag(targetLanguageTag, engine.ProviderId);
            var existing = GroupElement
                .SafeSelectElements($"div[@lang='{aiTag}']")
                .FirstOrDefault();
            if (existing == null)
                return false;

            var text = (existing.InnerText ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                return false;

            return existing.GetAttribute("data-ai-fingerprint") == expectedFingerprint;
        }
    }

    /// <summary>
    /// One group's resolved translation work for a specific engine: the group plus the source
    /// language/text that engine should translate from (which, for fixed-source engines, can differ
    /// from the group's default source). Returned by AiTranslationBookScan.GroupsNeedingTranslation.
    /// </summary>
    public class AiTranslationGroupSource
    {
        /// <summary>The group needing translation.</summary>
        public AiTranslationGroupInfo Group { get; }

        /// <summary>The language tag the engine should translate from for this group.</summary>
        public string SourceLanguageTag { get; }

        /// <summary>The source text the engine should translate for this group.</summary>
        public string SourceText { get; }

        /// <summary>Pairs a group with the engine-resolved source language/text.</summary>
        public AiTranslationGroupSource(
            AiTranslationGroupInfo group,
            string sourceLanguageTag,
            string sourceText
        )
        {
            Group = group;
            SourceLanguageTag = sourceLanguageTag;
            SourceText = sourceText;
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
        /// The ordered subset of Groups that still need a (re)translation for the given engine,
        /// each paired with the source language/text that engine should translate from. Groups the
        /// engine cannot translate (a fixed-source engine whose source language is absent from the
        /// group) are excluded, as are groups that already have a current translation.
        /// </summary>
        public List<AiTranslationGroupSource> GroupsNeedingTranslation(
            AiTranslationEngineSettings engine
        )
        {
            var result = new List<AiTranslationGroupSource>();
            foreach (var group in Groups)
            {
                if (
                    !group.TryGetSourceForEngine(
                        engine,
                        out var sourceLanguageTag,
                        out var sourceText
                    )
                )
                    continue;
                if (group.HasCurrentTranslation(engine, _targetLanguageTag))
                    continue;
                result.Add(new AiTranslationGroupSource(group, sourceLanguageTag, sourceText));
            }
            return result;
        }

        /// <summary>
        /// How many otherwise-eligible groups this engine must skip because it has no source text
        /// for them. Only fixed-source engines (alpha2) can skip groups this way -- an
        /// automatic-source engine always has the group's default source -- so this is always 0 for
        /// deepl/google. Used only to leave a trace in the progress log; the book-level skip itself
        /// is silent.
        /// </summary>
        public int CountGroupsSkippedForEngine(AiTranslationEngineSettings engine)
        {
            if (AiTranslationBookScanner.GetFixedSourceLanguageTagOrNull(engine) == null)
                return 0;
            return Groups.Count(group => !group.TryGetSourceForEngine(engine, out _, out _));
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
            // Fingerprint against the engine's own resolved source (which, for a fixed-source engine
            // like alpha2, may differ from the group's default source) so it matches what
            // GroupsNeedingTranslation/RemoveStaleAiDivs compute for the same engine. A group is only
            // ever applied for an engine that has a source for it, so this must resolve.
            if (!group.TryGetSourceForEngine(engine, out var sourceLanguageTag, out var sourceText))
            {
                throw new InvalidOperationException(
                    "ApplyTranslation called for an engine with no source for the group."
                );
            }
            var fingerprint = ComputeFingerprint(sourceLanguageTag, sourceText, aiTag);

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
                BuildTextsByLanguage(editables),
                isBookTitle
            );
        }

        /// <summary>
        /// Builds the lang -> trimmed-text map of every candidate source editable in a group: those
        /// with a lang attribute that is not an AI ("-x-ai") tag, not the "z" placeholder language,
        /// and whose text is non-empty. Preserves document order; when a lang appears more than once,
        /// the first non-empty occurrence wins.
        /// </summary>
        private static IReadOnlyDictionary<string, string> BuildTextsByLanguage(
            IEnumerable<SafeXmlElement> editables
        )
        {
            var textsByLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var editable in editables)
            {
                if (!editable.HasAttribute("lang"))
                    continue;
                var lang = editable.GetAttribute("lang");
                if (lang.Contains(kAiLangTagFragment) || lang == kZeroLang)
                    continue;
                var text = (editable.InnerText ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (!textsByLanguage.ContainsKey(lang))
                    textsByLanguage[lang] = text;
            }

            return textsByLanguage;
        }

        /// <summary>
        /// Returns the fixed source language tag an engine must translate from, or null if the
        /// engine chooses its source automatically per group. Only the alpha2 provider is
        /// fixed-source (its model selection is per source/target pair); its blank source defaults
        /// to English via GetEffectiveSourceLanguageTag.
        /// </summary>
        internal static string GetFixedSourceLanguageTagOrNull(AiTranslationEngineSettings engine)
        {
            if (AiTranslationService.NormalizeProviderId(engine.ProviderId) == "alpha2")
                return engine.GetEffectiveSourceLanguageTag();
            return null;
        }

        /// <summary>
        /// The lowercased primary (language) subtag of a Bloom language tag, used to match a fixed
        /// source language against a group's editables at primary-subtag granularity (e.g. an "en"
        /// setting matches an "en-US" editable).
        /// </summary>
        internal static string GetPrimarySubtag(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
                return string.Empty;
            return AiTranslationService
                .NormalizeBloomLanguageTag(languageTag)
                .Split('-')[0]
                .ToLowerInvariant();
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
        /// target language), or its fingerprint no longer matches the source the div's OWN engine
        /// would currently translate from (which, for a fixed-source engine, is that engine's
        /// configured source; a missing fixed source makes the div stale).
        /// </summary>
        private bool ShouldRemoveGroupAiDiv(
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

            var engine = ResolveEngineFromAiLang(lang);
            if (engine == null)
                return true; // no matching active engine (shouldn't happen given activeTags check).

            var expectedFingerprint = groupInfo.GetExpectedFingerprint(engine, _targetLanguageTag);
            if (expectedFingerprint == null)
                return true; // this engine has no source for the group now -> the div is stale.

            return aiDiv.GetAttribute("data-ai-fingerprint") != expectedFingerprint;
        }

        /// <summary>
        /// Finds the enabled engine that produced an AI div, by reading the provider id out of the
        /// div's AI language tag (e.g. "es-x-ai-alpha2" -> the alpha2 engine). Returns null if no
        /// enabled engine matches.
        /// </summary>
        private AiTranslationEngineSettings ResolveEngineFromAiLang(string lang)
        {
            if (string.IsNullOrEmpty(lang))
                return null;
            var markerIndex = lang.IndexOf(
                kAiLangTagFragment + "-",
                StringComparison.OrdinalIgnoreCase
            );
            if (markerIndex < 0)
                return null;
            var providerId = AiTranslationService.NormalizeProviderId(
                lang.Substring(markerIndex + kAiLangTagFragment.Length + 1)
            );
            return _enabledEngines.FirstOrDefault(e =>
                AiTranslationService.NormalizeProviderId(e.ProviderId) == providerId
            );
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
