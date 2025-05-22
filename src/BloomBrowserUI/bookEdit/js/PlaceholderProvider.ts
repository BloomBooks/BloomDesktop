/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="./collectionSettings.d.ts" />
/// <reference path="bloomQtipUtils.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import bloomQtipUtils from "./bloomQtipUtils";

export default class PlaceholderProvider {
    // "Eager" Placeholders work like this:
    // If we have a translation in L1, we just set that bloom-editable (this is the "eager" part).
    // This is useful for prompts like "Drag the images to the right labels" that might very well
    // be what the author actually wants if he is working in a language  we know (but he might want
    // something more specific).
    // Else we get the translation in what we think will be the most helpful available language.
    // Then we show that with opacity 0.5 but we don't actually set the bloom-editable, to prevent
    // the wrong language being left in the field. This prompt shows only when the field is empty.
    // This code also shows non-eaget placeholders, which always display as a placeholder even
    // if available in L1. This is useful for prompts like "Put instructions here" that are never
    // the final text in any language.
    public static addPlaceholders(container: HTMLElement): void {
        const placeholderElements = Array.from(
            container.querySelectorAll(
                "[data-eager-placeholder-l10n-id], [data-placeholder-l10n-id]"
            )
        );
        placeholderElements.forEach(async element => {
            let eager = true;
            let l10nId = element.getAttribute(
                "data-eager-placeholder-l10n-id"
            ) as string;
            if (!l10nId) {
                eager = false;
                l10nId = element.getAttribute(
                    "data-placeholder-l10n-id"
                ) as string;
                if (!l10nId) return; // paranoia
            }
            const l1Editable = element.getElementsByClassName(
                "bloom-editable bloom-content1"
            )[0];
            if (!l1Editable) return;
            const l1Lang = l1Editable.getAttribute("lang");
            if (!l1Lang) return; // paranoia
            const placeholderData = await theOneLocalizationManager.asyncGetTextInLangWithLangFound(
                l10nId,
                l1Lang
            );
            if (eager && placeholderData.langFound === l1Lang) {
                // We have a translation in L1, so we set the text of the bloom-editable
                // (unless it already has text).
                if (PlaceholderProvider.isBlank(l1Editable as HTMLElement)) {
                    const para = l1Editable.getElementsByTagName("p")[0];
                    if (para) {
                        para.innerText = placeholderData.text;
                        var ckEditor = (l1Editable as any).bloomCkEditor;
                        if (ckEditor && !ckEditor.instanceReady) {
                            // This typicall happens during page load.
                            // We have a race condition beween getting the placeholder text and initializing the ckeditor
                            // for the editable, which is likely to reset the text. Both involve async code (here to get
                            // localization), and the ckeditor init also involves timeouts. So either could finish first.
                            // If there's a ckEditor that isn't ready yet, set the text again when it is.
                            ckEditor.on("instanceReady", e => {
                                // ckEditor may even have replaced the whole paragraph we found before.
                                const para2 = l1Editable.getElementsByTagName(
                                    "p"
                                )[0];
                                if (para2) {
                                    para2.innerText = placeholderData.text;
                                }
                            });
                        }
                    }
                }
                // If we have eager placeholder data in the right language, we don't make the other kind of placeholder.
                // Eager placeholders are used where the text is likely to be what the author actually wants to see,
                // and we don't want to risk the author thinking the text has been entered when it's just a placeholder.
                return;
            }
            // We don't have a translation in L1, or we have a non-eager placeholder,
            // so we'll show it as a placeholder (when the element is empty)
            if (!placeholderData) return; // paranoia
            // css turns this into a placeholder when bloom-blank is also present
            element.setAttribute(
                "data-placeholder-value",
                placeholderData.text
            );
            this.setBlankClass(l1Editable as HTMLElement); // right initial state
            // Arrange for the bloom-blank class to be set and cleared as appropriate when the editable changes.
            PlaceholderProvider.blankObserver.observe(l1Editable, {
                childList: true,
                subtree: true,
                characterData: true
            });
        });
    }
    // mutation observers are tricky. There does not appear to be a simple way of getting from the function
    // arguments to the object that was observed. Even a simple keystroke seems to produce at least three
    // mutation records, and some of them have detached targets that have no connection to the editable we
    // want. (Possibly they are CkEditor bookmarks that were inserted and then removed.)
    // Documentation on what records to expect for different kinds of mutation is poor.
    // It's definitely possible that one of them is a CharacterNode that doesn't have closest().
    // However, every change I've tried has at least one record with a target that is either the
    // bloom-editable or one of its descendants. So we should find it for at least one of them
    // by navigating up to an element and then using closest().
    static blankObserver = new MutationObserver(mutations => {
        for (let i = 0; i < mutations.length; i++) {
            let editable: Node | null = mutations[i].target;
            while (editable && editable.nodeType !== Node.ELEMENT_NODE)
                editable = editable.parentNode;
            if (!editable) continue; // we actually seem to get some disconnected nodes, I don't know why
            editable = (editable as HTMLElement).closest(".bloom-editable");
            if (editable) {
                PlaceholderProvider.setBlankClass(editable as HTMLElement);
                return;
            }
        }
    });

    public static setBlankClass(element: HTMLElement) {
        if (this.isBlank(element)) {
            element.classList.add("bloom-blank");
        } else {
            element.classList.remove("bloom-blank");
        }
    }

    public static isBlank(element: HTMLElement) {
        return (element.textContent ?? "").trim().length === 0;
    }
}
