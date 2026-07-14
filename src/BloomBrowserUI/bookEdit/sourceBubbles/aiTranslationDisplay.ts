// Small helpers for displaying AI-translated content in the source bubbles UI. The
// translations themselves are produced by a C# batch process (see AiTranslationBookUpdater)
// before the book is opened for editing; this module only concerns itself with how the
// front end labels and filters that AI-generated content once it's already in the DOM.
import $ from "jquery";

/// True if the given lang tag (e.g. "fr-x-ai-deepl") is one Bloom's AI translation machinery
/// wrote, per the "-x-ai" private-use convention (see AiTranslationService.GetAiLanguageTag).
export function isAiLanguageTag(languageTag: string | undefined): boolean {
    return !!languageTag && languageTag.includes("-x-ai");
}

/// Maps a translation provider id (e.g. "deepl") to the human-facing name shown in AI tab
/// labels. Kept in sync with AiTranslationService.GetProviderDisplayName on the C# side.
export function getAiProviderDisplayName(providerId: string): string {
    switch (providerId) {
        case "deepl":
            return "DeepL";
        case "google":
            return "Google Translate";
        case "alpha2":
            return "SIL Alpha2";
        default:
            return providerId;
    }
}

/// The AI target language is typically not one of the book's languages, so Bloom's own
/// language name lookup often doesn't know it; the browser's Intl database usually does.
export function getLanguageNameFromBrowser(
    langTag: string,
): string | undefined {
    try {
        return new Intl.DisplayNames([navigator.language || "en"], {
            type: "language",
        }).of(langTag);
    } catch {
        // Intl.DisplayNames throws on structurally invalid tags; fall back to the raw tag.
        return undefined;
    }
}

/// Strips AI-translated divs (lang tag contains "-x-ai-") out of a cloned source-bubble div,
/// used when the user has turned AI source bubbles off. Only mutates the clone passed in;
/// the live .bloom-translationGroup in the book is never touched here.
export function removeAiTranslationDivsFromClone(divForBubble: JQuery): void {
    divForBubble.find("div[lang]").each((index, element) => {
        const langTag = element.getAttribute("lang") || "";
        if (isAiLanguageTag(langTag)) {
            element.remove();
        }
    });
}
