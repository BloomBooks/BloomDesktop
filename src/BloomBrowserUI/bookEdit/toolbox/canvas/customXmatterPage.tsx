// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="../../js/collectionSettings.d.ts"/>
import {
    kBackgroundImageClass,
    showCanvasTool,
    theOneCanvasElementManager,
} from "../../js/CanvasElementManager";
import { EditableDivUtils } from "../../js/editableDivUtils";
import { kBloomCanvasClass, kCanvasElementClass } from "./canvasElementUtils";
import { ensureFieldFitsOnCustomPage } from "./derivedFieldFitting";
import { getAsync, postData, postString } from "../../../utils/bloomApi";
import { Bubble, BubbleSpec } from "comicaljs";
import {
    recomputeSourceBubblesForPage,
    wrapWithRequestPageContentDelay,
} from "../../js/bloomEditing";
import { updateAbovePageControls } from "../../js/AbovePageControls";
import BloomSourceBubbles from "../../sourceBubbles/BloomSourceBubbles";
import { ILanguageNameValues } from "../../bookAndPageSettings/FieldVisibilityGroup";
import { isLegacyThemeCssLoaded } from "../../bookAndPageSettings/appearanceThemeUtils";

/* Summary of how custom covers work
	- a page (currently just cover) which can be customized has a new attribute,
	data-custom-layout-id, with a value distinct from all other such pages.
    (When adding these, e.g., to enable a custom layout for another xmatter page,
    be sure to add it to all versions of the page that might need it. For example,
    many xmatter pages have a separate version for device books.)
	- When a page is put into custom mode, we add a class bloom-customLayout.
	- When we update xmatter for a page that has bloom-customLayout, we replace
	everything in the marginBox with the saved custom content.
	- BookData is enhanced to treat data-custom-layout-id much like data-book,
	except that (a) it is ignored if the element that has it does not also have class
	bloom-customLayout, and (b) it is restored before anything else, so that if there has been
    editing of elements like title in auto mode (or an older Bloom), we end up with the
	edited content, not what was saved in the custom page content.
    - When a page is first put into this mode, we duplicate everything in the old marginBox
    into the new page as canvas elements.
	- each canvas element is absolutely positioned to (initially) match where the content
    was on the page before conversion.
	- where multiple languages of something can be shown (e.g., title), we make
    a canvas element for each language that was visible at the time of conversion.
    We disable any appearance system visibility control and put a value in
    data-default-languages to indicate which of the collection languages
    should be shown there.
    - kludge: so that such elements keep their appearance-system default sizes,
    we keep the appearance-system special classes like bloom-contentSecond.
    C# code is also patched not to remove these.
	- previous versions of Bloom will ignore data-custom-layout-id. When the book is
	updated, the cover will revert to the standard layout. If it is later opened in
	a current Bloom, the bloom-customLayout class will have been lost, but the data
    is saved; the page can be switched to custom layout again. We may make a small
    change to 6.3 to prevent losing the class.
    - Note: this is limited to xmatter pages because we depend on the fact that
    all the content is kept in the data-div, so we can turn off the custom layout just
    by changing the class and re-running the DataBook process.
    - Note: the new mechanism is not supported in legacy (5.4) theme, and may well
    produce some odd results with customBookStyles, especially if they mess with
    visibility of different languages.
*/

async function convertXmatterPageToCustom(page: HTMLElement): Promise<void> {
    const marginBox = page.getElementsByClassName(
        "marginBox",
    )[0] as HTMLElement;
    if (!marginBox) return; // paranoia and lint

    const languageNameValues = await getLanguageNameValues();

    theOneCanvasElementManager?.turnOffCanvasElementEditing();
    // we need to get rid of the old ones before we switch things around,
    // since the remove code makes use of the existing divs that the
    // source bubbles are connected to.
    BloomSourceBubbles.removeSourceBubbles(page);

    const contentElements = Array.from(
        marginBox.querySelectorAll<HTMLElement>("[data-book], [data-derived]"),
    );
    const newCanvasElements: HTMLElement[] = [];
    const newCeImages: HTMLElement[] = [];
    const measurementHost = createMeasurementHost(marginBox);

    for (const elem of contentElements) {
        // If something was completely hidden on the source page (typically by a book setting),
        // making it appear on the custom page would be unexpected. This searches a few levels up,
        // since the data-book element is sometimes inside an element that gets hidden.
        if (EditableDivUtils.isInDisplayNone(elem)) {
            continue;
        }
        if (elem instanceof HTMLImageElement) {
            // The main cover image. We will clone it, but it will no longer be
            // a background image.
            const ce = elem.parentElement?.parentElement;
            if (ce && ce.classList.contains(kCanvasElementClass)) {
                const newCe = ce.cloneNode(true) as HTMLElement;
                newCeImages.push(newCe);
                newCe.classList.remove(kBackgroundImageClass);
                // set its top and left to where it currently is relative to the page
                const pageRect = page.getBoundingClientRect();
                // base this on the original!
                const tgRect = ce.getBoundingClientRect();
                const scale = EditableDivUtils.getPageScale();
                newCe.style.left = (tgRect.left - pageRect.left) / scale + "px";
                newCe.style.top = (tgRect.top - pageRect.top) / scale + "px";
            }
        } else {
            let ceContent = elem;
            const baseSizeOn = elem;
            let useContentBoundsSizing = false;
            if (elem.classList.contains("bloom-editable")) {
                const tg = elem.parentElement;
                if (!tg || !tg.classList.contains("bloom-translationGroup")) {
                    continue;
                }
                // we'll only make a CE for bloom-editables that are visible.
                // (the image description is a special case, it is typically not visible because rules hide the
                // containing TG)
                if (
                    !elem.classList.contains("bloom-visibility-code-on") ||
                    tg.classList.contains("bloom-imageDescription")
                ) {
                    continue;
                }

                ceContent = tg.cloneNode(true) as HTMLElement;
                const lang = elem.getAttribute("lang");
                const newEditable = ceContent.querySelector(
                    `.bloom-editable[lang='${lang}']`,
                ) as HTMLElement;

                // Now, we need to make the CE display just the one bloom-editable that it
                // was made for.
                for (const e of Array.from(
                    ceContent.getElementsByClassName("bloom-editable"),
                )) {
                    if (e !== newEditable) {
                        e.classList.remove("bloom-visibility-code-on");
                    }
                }
                setDataDefault(
                    ceContent as HTMLElement,
                    lang || "",
                    languageNameValues,
                );

                // Don't let the appearance system mess with which languages are visible here.
                ceContent.removeAttribute("data-visibility-variable");

                newEditable.classList.add("bloom-visibility-code-on");
                // I don't know why an editable that is not part of a CE yet would have one of these,
                // but if it does it is obsolete and will interfere with the position we're setting
                // here. (Maybe I only saw it on pages I'd already messed with?)
                Array.from(
                    ceContent.querySelectorAll("[data-bubble-alternate]"),
                ).forEach((e) => {
                    e.removeAttribute("data-bubble-alternate");
                });
            } else {
                // This is debatable. But I think having empty CEs around for things like optional
                // branding elements is not helpful.
                if (ceContent.clientHeight === 0 || ceContent.clientWidth === 0)
                    continue; // invisible, skip it.
                // tempting to move it, but we don't want to mess with the current page
                // until we finish the loop, so that nothing moves before we figure out
                // where to put its new canvas element.
                ceContent = elem.cloneNode(true) as HTMLElement;
                useContentBoundsSizing = true;
            }

            preserveHintMetadata(elem as HTMLElement, ceContent as HTMLElement);

            // make a new canvas element to hold this. Not sure what problems it will
            // cause when it's NOT a TG, but we have at least topic and language name that
            // never are and are commonly on the front cover.
            const newCe = document.createElement("div");
            newCe.classList.add(kCanvasElementClass);
            newCanvasElements.push(newCe);
            newCe.appendChild(ceContent);

            if (useContentBoundsSizing) {
                setCanvasElementSizeAndPositionFromVisibleContent(
                    newCe,
                    ceContent,
                    baseSizeOn,
                    page,
                    measurementHost,
                );
                // Once the CE itself is positioned/sized to account for source margins,
                // keep the root converted content from adding an extra offset.
                ceContent.style.margin = "0";
            } else {
                // Before we move the tg, measure its size and make newCe match
                // Review: do we need to add allowance for borders/margins/padding?
                const baseComputedStyle = window.getComputedStyle(baseSizeOn);
                const marginLeft =
                    Number.parseFloat(baseComputedStyle.marginLeft) || 0;
                const marginRight =
                    Number.parseFloat(baseComputedStyle.marginRight) || 0;
                const marginTop =
                    Number.parseFloat(baseComputedStyle.marginTop) || 0;
                const marginBottom =
                    Number.parseFloat(baseComputedStyle.marginBottom) || 0;
                newCe.style.width =
                    baseSizeOn.clientWidth + marginLeft + marginRight + "px";
                newCe.style.height =
                    baseSizeOn.clientHeight + marginTop + marginBottom + "px";
                // set its top and left to where it currently is relative to the page
                const pageRect = page.getBoundingClientRect();
                const tgRect = baseSizeOn.getBoundingClientRect();
                const scale = EditableDivUtils.getPageScale();
                newCe.style.left =
                    (tgRect.left - pageRect.left) / scale - marginLeft + "px";
                newCe.style.top =
                    (tgRect.top - pageRect.top) / scale - marginTop + "px";
            }
        }
    }
    const mainCanvas = document.createElement("div");
    mainCanvas.classList.add(kBloomCanvasClass);
    mainCanvas.classList.add("bloom-has-canvas-element");
    // We'll put the new images before (and with lower bubble index) than text, since
    // we may want to put text over images, and clicks should prefer the text.
    let level = 2; // level 1 will be the background image
    for (const newCE of newCeImages.concat(newCanvasElements)) {
        mainCanvas.appendChild(newCE);
        // as each one is added, we give it a bubble spec. getDefaultBubbleSpec
        // assigns a new 'level' to each, which is important for how our mouse event
        // handler figures out z-ordering and makes sure comicaljs doesn't think they
        // are a family of connected bubbles.
        const bubble = new Bubble(newCE);
        const bubbleSpec: BubbleSpec = Bubble.getDefaultBubbleSpec(
            newCE,
            "none",
        );
        bubbleSpec.level = level;
        level++;
        bubble.setBubbleSpec(bubbleSpec);
    }

    measurementHost.remove();
    marginBox.innerHTML = ""; // remove all the existing content
    marginBox.appendChild(mainCanvas);
    // This needs to be after we measure positions of things!
    page.classList.add("bloom-customLayout");
    finishReactivatingPage(page);
}

function createMeasurementHost(parent: HTMLElement): HTMLElement {
    const measurementHost = document.createElement("div");
    measurementHost.style.position = "absolute";
    measurementHost.style.left = "-100000px";
    measurementHost.style.top = "-100000px";
    measurementHost.style.visibility = "hidden";
    measurementHost.style.pointerEvents = "none";
    parent.appendChild(measurementHost);
    return measurementHost;
}

/** Returns true if the element has any visible CSS border on any side. */
function hasVisibleBorder(element: HTMLElement): boolean {
    const style = window.getComputedStyle(element);
    return ["top", "right", "bottom", "left"].some((side) => {
        const width = parseFloat(
            style.getPropertyValue(`border-${side}-width`),
        );
        const borderStyle = style.getPropertyValue(`border-${side}-style`);
        return width > 0 && borderStyle !== "none" && borderStyle !== "hidden";
    });
}

function getVisibleContentRect(element: HTMLElement): DOMRect {
    const range = document.createRange();
    range.selectNodeContents(element);
    const rects: DOMRect[] = Array.from(range.getClientRects()).filter(
        (rect) => rect.width > 0 && rect.height > 0,
    );

    // img elements need their full bounding rect included; the range-based approach
    // may not fully account for all image area.
    for (const img of Array.from(
        element.querySelectorAll<HTMLElement>("img"),
    )) {
        const rect = img.getBoundingClientRect();
        if (rect.width > 0 && rect.height > 0) {
            rects.push(rect);
        }
    }

    // Elements with visible borders need their full border box included,
    // since range rects only cover the content area inside the border.
    for (const child of [
        element,
        ...Array.from(element.querySelectorAll<HTMLElement>("*")),
    ]) {
        if (hasVisibleBorder(child)) {
            const rect = child.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) {
                rects.push(rect);
            }
        }
    }

    if (rects.length === 0) {
        return element.getBoundingClientRect();
    }

    let left = rects[0].left;
    let top = rects[0].top;
    let right = rects[0].right;
    let bottom = rects[0].bottom;
    for (const rect of rects.slice(1)) {
        left = Math.min(left, rect.left);
        top = Math.min(top, rect.top);
        right = Math.max(right, rect.right);
        bottom = Math.max(bottom, rect.bottom);
    }

    return new DOMRect(left, top, right - left, bottom - top);
}

function setCanvasElementSizeAndPositionFromVisibleContent(
    canvasElement: HTMLElement,
    canvasContent: HTMLElement,
    sourceElement: HTMLElement,
    page: HTMLElement,
    measurementHost: HTMLElement,
): void {
    const scale = EditableDivUtils.getPageScale() || 1;
    const sourceContentRect = getVisibleContentRect(sourceElement);
    const width = Math.max(1, Math.ceil(sourceContentRect.width / scale));
    const height = Math.max(1, Math.ceil(sourceContentRect.height / scale));
    canvasElement.style.width = `${width}px`;
    canvasElement.style.height = `${height}px`;

    const probeCanvasElement = document.createElement("div");
    probeCanvasElement.classList.add(kCanvasElementClass);
    probeCanvasElement.style.width = `${width}px`;
    probeCanvasElement.style.height = `${height}px`;
    const probeContent = canvasContent.cloneNode(true) as HTMLElement;
    probeCanvasElement.appendChild(probeContent);
    measurementHost.appendChild(probeCanvasElement);

    const probeContentRect = getVisibleContentRect(probeContent);
    const probeCanvasRect = probeCanvasElement.getBoundingClientRect();
    const contentOffsetLeft =
        (probeContentRect.left - probeCanvasRect.left) / scale;
    const contentOffsetTop =
        (probeContentRect.top - probeCanvasRect.top) / scale;

    probeCanvasElement.remove();

    const pageRect = page.getBoundingClientRect();
    canvasElement.style.left =
        (sourceContentRect.left - pageRect.left) / scale -
        contentOffsetLeft +
        "px";
    canvasElement.style.top =
        (sourceContentRect.top - pageRect.top) / scale -
        contentOffsetTop +
        "px";
}

async function getLanguageNameValues(): Promise<ILanguageNameValues> {
    return (await getAsync("settings/languageNames"))
        .data as ILanguageNameValues;
}

function copyHintAttributes(source: Element, target: HTMLElement): void {
    [
        "data-hint",
        "data-i18n",
        "data-link-text",
        "data-link-target",
        "data-functiononhintclick",
    ].forEach((attr) => {
        const value = source.getAttribute(attr);
        if (value && !target.hasAttribute(attr)) {
            target.setAttribute(attr, value);
        }
    });
}

function preserveHintMetadata(
    sourceElement: HTMLElement,
    convertedElement: HTMLElement,
): void {
    // If we've already preserved label content or explicit hint metadata, leave it alone.
    if (
        convertedElement.querySelector("label.bubble") ||
        convertedElement.hasAttribute("data-hint")
    ) {
        return;
    }

    const translationGroup = sourceElement.closest(
        ".bloom-translationGroup",
    ) as HTMLElement | null;
    if (!translationGroup) {
        copyHintAttributes(sourceElement, convertedElement);
        if (
            !convertedElement.hasAttribute("data-hint") &&
            sourceElement.parentElement
        ) {
            copyHintAttributes(sourceElement.parentElement, convertedElement);
        }
        return;
    }

    copyHintAttributes(translationGroup, convertedElement);

    const label = translationGroup.querySelector("label.bubble");
    if (!label) {
        if (
            !convertedElement.hasAttribute("data-hint") &&
            translationGroup.parentElement
        ) {
            copyHintAttributes(
                translationGroup.parentElement,
                convertedElement,
            );
        }
        return;
    }

    const labelText = label.textContent?.trim();
    if (labelText && !convertedElement.hasAttribute("data-hint")) {
        convertedElement.setAttribute("data-hint", labelText);
    }
    copyHintAttributes(label, convertedElement);

    if (
        !convertedElement.hasAttribute("data-hint") &&
        translationGroup.parentElement
    ) {
        copyHintAttributes(translationGroup.parentElement, convertedElement);
    }
}

function setDataDefault(
    ceContent: HTMLElement,
    lang: string,
    languageNameValues: ILanguageNameValues,
): void {
    // We also want it to stay that way when C# code later updates visibility codes.
    // This is based on a setting in the TG. Using these generic codes means that if
    // the collection languages change, or we make a derivative, the right language
    // should still be made visible in each box.
    if (languageNameValues.language1Tag === lang) {
        ceContent.setAttribute("data-default-languages", "V");
    } else if (languageNameValues.language2Tag === lang) {
        ceContent.setAttribute("data-default-languages", "N1");
    } else if (languageNameValues.language3Tag === lang) {
        ceContent.setAttribute("data-default-languages", "N2");
    }
}

function finishReactivatingPage(page: HTMLElement): void {
    // The divs that originally held source bubbles are gone
    recomputeSourceBubblesForPage(page);
    // it's a new canvas so we need to set it up
    theOneCanvasElementManager.turnOnCanvasElementEditing();
    ensureDerivedFieldsFitOnCustomPage(page);
    // Enable or show the canvas tool.
    showCanvasTool();
}

function ensureDerivedFieldsFitOnCustomPage(page: HTMLElement): void {
    if (!page.classList.contains("bloom-customLayout")) {
        return;
    }

    const bloomCanvas = page.getElementsByClassName(
        kBloomCanvasClass,
    )[0] as HTMLElement;
    if (!bloomCanvas) {
        return;
    }

    const derivedElements = Array.from(
        bloomCanvas.querySelectorAll("div[data-derived]"),
    ) as HTMLElement[];
    for (const derivedElement of derivedElements) {
        ensureFieldFitsOnCustomPage(derivedElement);
    }
}

export function setupPageLayoutMenu(): void {
    const page = document.getElementsByClassName("bloom-page")[0];
    // only pages with data-custom-layout-id can be customized.
    if (!page || !page.hasAttribute("data-custom-layout-id")) return;

    const usingLegacyTheme = isLegacyThemeCssLoaded();
    if (usingLegacyTheme && page.classList.contains("bloom-customLayout")) {
        toggleCustomPageLayout(page.getAttribute("id")!, true);
        return;
    }

    renderPageLayoutMenu(page as HTMLElement);

    if (page.classList.contains("bloom-customLayout")) {
        ensureDerivedFieldsFitOnCustomPage(page as HTMLElement);
        showCanvasTool();
    }
}

function toggleCustomPageLayout(
    pageId: string,
    keepCustomLayoutDataWhenSwitchingToStandard: boolean,
) {
    return postData("editView/toggleCustomPageLayout", {
        pageId,
        keepCustomLayoutDataWhenSwitchingToStandard:
            keepCustomLayoutDataWhenSwitchingToStandard ? "true" : "false",
    });
}

function renderPageLayoutMenu(page: HTMLElement): void {
    const isCustomPage = page.classList.contains("bloom-customLayout");
    const usingLegacyTheme = isLegacyThemeCssLoaded();
    updateAbovePageControls({
        showPageLayoutMenu: true,
        isCustomPageLayout: isCustomPage,
        disableCustomPage: usingLegacyTheme,
        onSetCustom: async (
            selection,
            keepCustomLayoutDataWhenSwitchingToStandard,
        ) => {
            if (usingLegacyTheme && selection !== "standard") {
                return;
            }
            const response = await toggleCustomPageLayout(
                page.getAttribute("id")!,
                keepCustomLayoutDataWhenSwitchingToStandard,
            );
            if (
                selection === "custom" &&
                response &&
                // C# returns the string "false" if we don't have any saved state for custom mode,
                // but currently something in axios converts that to a boolean false.
                // I'm not sure that might not change one day, so we check for both.
                (response.data === "false" || response.data === false)
            ) {
                // making a custom cover for the first time
                await wrapWithRequestPageContentDelay(
                    () => convertXmatterPageToCustom(page),
                    "customPageLayout-convertFirstTime",
                );
                // Persist the newly created custom layout state so a later toggle back
                // to standard has matching server-side state to work from.
                await postString(
                    "editView/jumpToPage",
                    page.getAttribute("id")!,
                );
                renderPageLayoutMenu(page);
            } else if (selection === "custom" && response) {
                showCanvasTool(); // otherwise called from convertXmatterPageToCustom()/finishReactivatingPage()
                renderPageLayoutMenu(page);
            } else if (response) {
                renderPageLayoutMenu(page);
            }
        },
    });
}
