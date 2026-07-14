using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;
using Bloom.web;
using L10NSharp;
using SIL.Reporting;

namespace Bloom.AiTranslation
{
    /// <summary>
    /// Delegate matching AiTranslationService.TranslateSegmentsAsync's signature, so tests can
    /// substitute a fake translator without any network calls or a real AiTranslationService.
    /// </summary>
    public delegate Task<string[]> AiTranslateSegmentsDelegate(
        AiTranslationEngineSettings engine,
        string[] segments,
        string sourceLanguageTag,
        CancellationToken ct
    );

    /// <summary>
    /// One engine's outcome from the parallel translation phase of AiTranslationBookUpdater: either
    /// the (group, translatedText) pairs to apply, or the reason the engine failed. Nothing here is
    /// written to the book DOM until every engine's work has settled -- see
    /// AiTranslationBookUpdater.ApplyOutcomes.
    /// </summary>
    public class AiTranslationEngineOutcome
    {
        /// <summary>The engine this outcome is for.</summary>
        public AiTranslationEngineSettings Engine { get; }

        /// <summary>The (group, translatedText) pairs this engine produced, not yet applied to the DOM.</summary>
        public List<(AiTranslationGroupInfo Group, string TranslatedText)> Translations { get; } =
            new List<(AiTranslationGroupInfo, string)>();

        /// <summary>Null if this engine's translation succeeded; otherwise the error message.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>True if this engine completed without error.</summary>
        public bool Succeeded => ErrorMessage == null;

        /// <summary>Creates an (initially empty, successful) outcome for the given engine.</summary>
        public AiTranslationEngineOutcome(AiTranslationEngineSettings engine)
        {
            Engine = engine;
        }
    }

    /// <summary>
    /// Orchestrates whole-book AI translation: scans a book for translation work
    /// across the collection's active engines and, if any is found (missing/stale translations for
    /// an active engine, or orphaned AI content that needs cleanup), blocks behind a modal progress
    /// dialog while the engines translate in parallel, then applies every result, removes stale AI
    /// content, and saves the book. Called once per book, right before the user starts editing it --
    /// see EditingModel.OnBecomeVisible. If nothing needs doing (the normal case once a book is up to
    /// date), this returns immediately: no dialog, no network calls, no book changes.
    /// </summary>
    public class AiTranslationBookUpdater
    {
        private readonly AiTranslationService _aiTranslationService;
        private readonly CollectionSettings _collectionSettings;
        private readonly IBloomWebSocketServer _webSocketServer;

        /// <summary>
        /// Creates an updater for the current collection (AiTranslationService and CollectionSettings
        /// are both scoped one-per-collection by the normal Autofac project scope).
        /// </summary>
        public AiTranslationBookUpdater(
            AiTranslationService aiTranslationService,
            CollectionSettings collectionSettings,
            BloomWebSocketServer webSocketServer
        )
        {
            _aiTranslationService = aiTranslationService;
            _collectionSettings = collectionSettings;
            _webSocketServer = webSocketServer;
        }

        /// <summary>
        /// Scans the given book and, if any active engine needs to translate something or stale AI
        /// content needs removing, blocks (showing a modal progress dialog) until the work is done,
        /// applied, and saved -- or the user cancels, in which case the book is left completely
        /// untouched. Safe and near-instant to call when nothing needs doing (the normal case once a
        /// book is up to date): no network calls are made unless there is confirmed work.
        /// </summary>
        public void UpdateBookIfNeeded(Book.Book book)
        {
            var featureStatus = FeatureStatus.GetFeatureStatus(
                _collectionSettings.Subscription,
                FeatureName.AiSourceBubbles
            );
            var targetLanguageTag = _collectionSettings.AiTranslationTargetLanguageTag;
            if (
                !featureStatus.Visible
                || !featureStatus.Enabled
                || string.IsNullOrWhiteSpace(targetLanguageTag)
            )
                return;

            var activeEngines = AiTranslationService.GetActiveEngines(_collectionSettings);
            if (activeEngines.Count == 0)
                return;

            var sourceLanguagePriorities = GetSourceLanguagePriorities();
            var bookDom = book.OurHtmlDom;
            var scanner = new AiTranslationBookScanner(
                bookDom,
                targetLanguageTag,
                activeEngines,
                sourceLanguagePriorities
            );
            var scan = scanner.Scan();

            var hasTranslationWork = activeEngines.Any(engine =>
                scan.GroupsNeedingTranslation(engine).Count > 0
            );
            var staleDivCount = CountStaleAiDivsWithoutMutating(
                bookDom,
                targetLanguageTag,
                activeEngines,
                sourceLanguagePriorities
            );
            if (!hasTranslationWork && staleDivCount == 0)
                return; // nothing to do; this is the normal case once a book is up to date.

            RunWithProgressDialog(book, scanner, scan, activeEngines);
        }

        /// <summary>
        /// Runs the parallel translation phase (via RunEnginesAsync) behind a modal progress dialog,
        /// then applies the results, removes stale AI content, and saves the book -- unless the user
        /// cancelled, in which case nothing is applied, cleaned up, or saved.
        /// </summary>
        private void RunWithProgressDialog(
            Book.Book book,
            AiTranslationBookScanner scanner,
            AiTranslationBookScan scan,
            List<AiTranslationEngineSettings> activeEngines
        )
        {
            BrowserProgressDialog.DoWorkWithProgressDialog(
                _webSocketServer,
                MakeDialog,
                (progress, worker) =>
                {
                    using (var cts = new CancellationTokenSource())
                    using (
                        new System.Threading.Timer(
                            _ =>
                            {
                                if (worker.CancellationPending)
                                    cts.Cancel();
                            },
                            null,
                            0,
                            150
                        )
                    )
                    {
                        List<AiTranslationEngineOutcome> outcomes;
                        try
                        {
                            outcomes = RunEnginesAsync(
                                    scan,
                                    activeEngines,
                                    _aiTranslationService.TranslateSegmentsAsync,
                                    progress,
                                    cts.Token
                                )
                                .GetAwaiter()
                                .GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                            progress.Message(
                                "EditTab.AiTranslation.Cancelled",
                                "Cancelled.",
                                useL10nIdPrefix: false
                            );
                            return false; // book left completely untouched; close the dialog.
                        }

                        ApplyOutcomes(scanner, outcomes);
                        var removedStaleCount = scanner.RemoveStaleAiDivs();
                        book.Save();
                        Logger.WriteEvent(
                            $"AI translation: translated {outcomes.Sum(o => o.Translations.Count)} text box(es) "
                                + $"across {outcomes.Count} engine(s); removed {removedStaleCount} stale AI div(s)."
                        );

                        // Always keep the dialog open once the run finishes (whether or not any
                        // engine failed) so the user can read the per-engine result lines and
                        // dismiss it with the OK button when ready. The Cancel button is only
                        // present while work is in progress; on finishing, the dialog swaps it for
                        // OK (and, if there were errors, also shows the Report button).
                        return true;
                    }
                },
                owner: Shell.GetShellOrOtherOpenForm()
            );
        }

        /// <summary>
        /// Builds the modal Form used to host the progress dialog. This uses the older Form-based
        /// BrowserProgressDialog overload (ShowDialog(), which blocks the caller until the dialog
        /// closes) rather than the newer overload that embeds a progress dialog in an already-open web
        /// page. UpdateBookIfNeeded runs synchronously from EditingModel.OnBecomeVisible, before any
        /// Edit tab page -- and thus before any EmbeddedProgressDialog -- has been loaded, so there is
        /// no host page available at this point in the app lifecycle. The Form-based overload's
        /// ShowDialog() blocking behavior is also exactly what this feature needs: editing must not
        /// start until translation has completed, been cancelled, or failed.
        /// </summary>
        private static Form MakeDialog()
        {
            var dlg = new ReactDialog(
                "progressDialogBundle",
                new
                {
                    title = LocalizationManager.GetDynamicString(
                        appId: "Bloom",
                        id: "EditTab.AiTranslation.DialogTitle",
                        englishText: "Translating"
                    ),
                    titleColor = "white",
                    titleBackgroundColor = Palette.kBloomBlueHex,
                    showReportButton = "if-error",
                    showCancelButton = true,
                    // When the run finishes, dismiss with an OK button rather than the generic
                    // Close button (see ProgressDialog.tsx). Until then, only Cancel is shown.
                    showOkButtonWhenDone = true,
                },
                "Translating"
            );
            dlg.SetScaledSize(620, 400);
            return dlg;
        }

        /// <summary>
        /// Runs every engine that has translation work, in parallel, collecting (group, translatedText)
        /// results in memory without touching the book DOM (see ApplyOutcomes for that). Each engine's
        /// segments are grouped by source language so translate is called once per language per engine.
        /// A per-engine failure is isolated into that engine's outcome and does not affect the others;
        /// cancellation (via ct) aborts every engine and propagates out of this method entirely, since a
        /// cancelled run must not apply anything.
        /// </summary>
        internal static async Task<List<AiTranslationEngineOutcome>> RunEnginesAsync(
            AiTranslationBookScan scan,
            IEnumerable<AiTranslationEngineSettings> engines,
            AiTranslateSegmentsDelegate translate,
            IWebSocketProgress progress,
            CancellationToken ct
        )
        {
            var workByEngine = engines
                .Select(engine => new
                {
                    Engine = engine,
                    Groups = scan.GroupsNeedingTranslation(engine),
                })
                .Where(w => w.Groups.Count > 0)
                .ToList();

            var tasks = workByEngine.Select(w =>
                TranslateOneEngineAsync(w.Engine, w.Groups, translate, progress, ct)
            );
            var outcomes = await Task.WhenAll(tasks);
            return outcomes.ToList();
        }

        /// <summary>
        /// Translates one engine's groups, grouped by source language, isolating any failure into the
        /// returned outcome (cancellation is the one exception: it is rethrown so it aborts the whole
        /// run rather than being recorded as this engine's failure).
        /// </summary>
        private static async Task<AiTranslationEngineOutcome> TranslateOneEngineAsync(
            AiTranslationEngineSettings engine,
            List<AiTranslationGroupInfo> groups,
            AiTranslateSegmentsDelegate translate,
            IWebSocketProgress progress,
            CancellationToken ct
        )
        {
            var outcome = new AiTranslationEngineOutcome(engine);
            var engineName = AiTranslationService.GetProviderDisplayName(engine.ProviderId);
            try
            {
                progress.MessageWithParams(
                    "EditTab.AiTranslation.EngineTranslating",
                    "{0} is the translation engine's name (e.g. DeepL); {1} is how many text boxes are being translated",
                    "{0}: translating {1} text box(es)...",
                    ProgressKind.Progress,
                    engineName,
                    groups.Count
                );

                foreach (var languageGroup in groups.GroupBy(g => g.SourceLanguageTag))
                {
                    ct.ThrowIfCancellationRequested();
                    var groupList = languageGroup.ToList();
                    var segments = groupList.Select(g => g.SourceText).ToArray();
                    var translated = await translate(engine, segments, languageGroup.Key, ct);
                    for (var i = 0; i < groupList.Count; i++)
                    {
                        outcome.Translations.Add((groupList[i], translated[i]));
                    }
                }

                progress.MessageWithParams(
                    "EditTab.AiTranslation.EngineDone",
                    "{0} is the translation engine's name (e.g. DeepL)",
                    "{0}: done.",
                    ProgressKind.Progress,
                    engineName
                );
            }
            catch (OperationCanceledException)
            {
                throw; // cancellation aborts the whole run; not a per-engine failure.
            }
            catch (Exception ex)
            {
                outcome.ErrorMessage = ex.Message;
                progress.MessageWithParams(
                    "EditTab.AiTranslation.EngineError",
                    "{0} is the translation engine's name (e.g. DeepL); {1} is the error message",
                    "{0}: {1}",
                    ProgressKind.Error,
                    engineName,
                    ex.Message
                );
            }

            return outcome;
        }

        /// <summary>
        /// Writes every collected translation to the book DOM via scanner.ApplyTranslation. Called only
        /// after all engines have settled, and only when the run was not cancelled.
        /// </summary>
        internal static void ApplyOutcomes(
            AiTranslationBookScanner scanner,
            IEnumerable<AiTranslationEngineOutcome> outcomes
        )
        {
            foreach (var outcome in outcomes)
            {
                foreach (var (group, translatedText) in outcome.Translations)
                {
                    scanner.ApplyTranslation(group, outcome.Engine, translatedText);
                }
            }
        }

        /// <summary>
        /// Counts how many stale AI divs RemoveStaleAiDivs() would remove, without mutating the real
        /// book DOM: runs the (otherwise identical) removal against a throwaway clone.
        /// </summary>
        internal static int CountStaleAiDivsWithoutMutating(
            HtmlDom bookDom,
            string targetLanguageTag,
            IEnumerable<AiTranslationEngineSettings> activeEngines,
            IReadOnlyList<string> sourceLanguagePriorities
        )
        {
            var probeDom = new HtmlDom(bookDom.RawDom.Clone());
            var probeScanner = new AiTranslationBookScanner(
                probeDom,
                targetLanguageTag,
                activeEngines,
                sourceLanguagePriorities
            );
            return probeScanner.RemoveStaleAiDivs();
        }

        /// <summary>
        /// The ordered list of candidate source languages to prefer when a translation group has more
        /// than one bloom-editable to choose from: the user's last-viewed source languages, then the
        /// collection's L2/L3 tags, then English, deduped and with blanks removed.
        /// </summary>
        private IReadOnlyList<string> GetSourceLanguagePriorities()
        {
            return new[]
            {
                Settings.Default.LastSourceLanguageViewed,
                Settings.Default.LastSourceLanguageViewed2,
                _collectionSettings.Language2Tag,
                _collectionSettings.Language3Tag,
                "en",
            }
                .Where(tag => !string.IsNullOrEmpty(tag))
                .Distinct()
                .ToList();
        }
    }
}
