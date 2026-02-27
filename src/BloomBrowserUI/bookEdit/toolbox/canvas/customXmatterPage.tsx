// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="../../js/collectionSettings.d.ts"/>
import * as ReactDOM from "react-dom";
import { CustomPageLayoutMenu } from "./customPageLayoutMenu";
import {
    CanvasElementManager,
    kBackgroundImageClass,
    theOneCanvasElementManager,
} from "../../js/CanvasElementManager";
import { EditableDivUtils } from "../../js/editableDivUtils";
import { kBloomCanvasClass, kCanvasElementClass } from "./canvasElementUtils";
import { getAsync, postString } from "../../../utils/bloomApi";
import { Bubble, BubbleSpec } from "comicaljs";
import { recomputeSourceBubblesForPage } from "../../js/bloomEditing";
import BloomSourceBubbles from "../../sourceBubbles/BloomSourceBubbles";
import { getToolboxBundleExports } from "../../js/bloomFrames";
import { ILanguageNameValues } from "../../bookSettings/FieldVisibilityGroup";

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

export function convertXmatterPageToCustom(page: HTMLElement): void {
    theOneCanvasElementManager?.turnOffCanvasElementEditing();
    // we need to get rid of the old ones before we switch things around,
    // since the remove code makes use of the existing divs that the
    // source bubbles are connected to.
    BloomSourceBubbles.removeSourceBubbles(page);

    const marginBox = page.getElementsByClassName(
        "marginBox",
    )[0] as HTMLElement;
    if (!marginBox) return; // paranoia and lint

    const contentElements = Array.from(
        marginBox.querySelectorAll("[data-book], [data-derived]"),
    );
    const newCanvasElements: HTMLElement[] = [];
    const newCeImages: HTMLElement[] = [];

    for (const elem of contentElements) {
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
                // We don't need to await this. We just want it done before the next time
                // we save the page, and the async result should arrive almost instantly.
                setDataDefault(ceContent as HTMLElement, lang || "");

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
            }
            // make a new canvas element to hold this. Not sure what problems it will
            // cause when it's NOT a TG, but we have at least topic and language name that
            // never are and are commonly on the front cover.
            const newCe = document.createElement("div");
            newCe.classList.add(kCanvasElementClass);
            newCanvasElements.push(newCe);
            newCe.appendChild(ceContent);
            // Before we move the tg, measure its size and make newCe match
            // Review: do we need to add allowance for borders/margins/padding?
            newCe.style.width = baseSizeOn.clientWidth + "px";
            newCe.style.height = baseSizeOn.clientHeight + "px";
            // set its top and left to where it currently is relative to the page
            const pageRect = page.getBoundingClientRect();
            const tgRect = baseSizeOn.getBoundingClientRect();
            const scale = EditableDivUtils.getPageScale();
            newCe.style.left = (tgRect.left - pageRect.left) / scale + "px";
            newCe.style.top = (tgRect.top - pageRect.top) / scale + "px";
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

    marginBox.innerHTML = ""; // remove all the existing content
    marginBox.appendChild(mainCanvas);
    // This needs to be after we measure positions of things!
    page.classList.add("bloom-customLayout");
    finishReactivatingPage(page);
}

async function setDataDefault(
    ceContent: HTMLElement,
    lang: string,
): Promise<void> {
    // We also want it to stay that way when C# code later updates visibility codes.
    // This is based on a setting in the TG. Using these generic codes means that if
    // the collection languages change, or we make a derivative, the right language
    // should still be made visible in each box.
    // settings/languageNames
    const languageNameValues = (await getAsync("settings/languageNames"))
        .data as ILanguageNameValues;
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
    getToolboxBundleExports()?.applyToolboxStateToPage();
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
        const canvasElement = derivedElement.closest(
            `.${kCanvasElementClass}`,
        ) as HTMLElement;
        if (!canvasElement) {
            continue;
        }

        const currentWidth = CanvasElementManager.pxToNumber(
            canvasElement.style.width,
        );
        const currentHeight = CanvasElementManager.pxToNumber(
            canvasElement.style.height,
        );
        if (currentWidth <= 0 || currentHeight <= 0) {
            continue;
        }

        const getRenderedHeight = (): number =>
            Math.ceil(derivedElement.getBoundingClientRect().height);
        const getRenderedWidth = (): number =>
            Math.ceil(
                Math.max(
                    derivedElement.getBoundingClientRect().width,
                    derivedElement.offsetWidth,
                ),
            );

        const overflowsVertically = (): boolean =>
            getRenderedHeight() > currentHeight + 1;
        const overflowsHorizontally = (): boolean => {
            const containerWidth = CanvasElementManager.pxToNumber(
                canvasElement.style.width,
                canvasElement.clientWidth,
            );
            return getRenderedWidth() > containerWidth + 1;
        };
        const hasOverflow = (): boolean =>
            overflowsVertically() || overflowsHorizontally();

        const oldWhiteSpace = derivedElement.style.whiteSpace;
        derivedElement.style.whiteSpace = "normal";

        let fittedWidth = currentWidth;
        if (hasOverflow()) {
            derivedElement.style.whiteSpace = "nowrap";
            const noWrapWidth = Math.max(currentWidth, getRenderedWidth());
            derivedElement.style.whiteSpace = "normal";

            canvasElement.style.width = `${noWrapWidth}px`;
            if (hasOverflow()) {
                fittedWidth = noWrapWidth;
            } else {
                let low = currentWidth;
                let high = noWrapWidth;
                while (high - low > 1) {
                    const mid = Math.floor((low + high) / 2);
                    canvasElement.style.width = `${mid}px`;
                    if (hasOverflow()) {
                        low = mid;
                    } else {
                        high = mid;
                    }
                }
                fittedWidth = high;
            }
        }
        derivedElement.style.whiteSpace = oldWhiteSpace;

        if (fittedWidth > currentWidth) {
            canvasElement.style.width = `${fittedWidth}px`;
        } else {
            canvasElement.style.width = `${currentWidth}px`;
        }

        const finalWidth = Math.max(currentWidth, fittedWidth);
        const maxLeft = Math.max(0, bloomCanvas.clientWidth - finalWidth);
        const maxTop = Math.max(0, bloomCanvas.clientHeight - currentHeight);
        const clampedLeft = Math.max(
            0,
            Math.min(canvasElement.offsetLeft, maxLeft),
        );
        const clampedTop = Math.max(
            0,
            Math.min(canvasElement.offsetTop, maxTop),
        );
        if (clampedLeft !== canvasElement.offsetLeft) {
            canvasElement.style.left = `${clampedLeft}px`;
        }
        if (clampedTop !== canvasElement.offsetTop) {
            canvasElement.style.top = `${clampedTop}px`;
        }
    }

    theOneCanvasElementManager?.ensureCanvasElementsIntersectParent(
        bloomCanvas,
    );
}

export function setupPageLayoutMenu(): void {
    const page = document.getElementsByClassName("bloom-page")[0];
    // only pages with data-custom-layout-id can be customized.
    if (!page || !page.hasAttribute("data-custom-layout-id")) return;

    const usingLegacyTheme = isUsingLegacyTheme();
    if (usingLegacyTheme && page.classList.contains("bloom-customLayout")) {
        postString("editView/toggleCustomPageLayout", page.getAttribute("id")!);
        return;
    }

    // Create the container if needed (which it usually will be, because the cover
    // is not a customPage and doesn't get one automatically). This duplicates
    // (but without jquery) some code in origami.ts
    let container: HTMLElement | undefined = document.getElementsByClassName(
        "above-page-control-container",
    )[0] as HTMLElement;
    if (!container) {
        container = document.createElement("div") as HTMLElement;
        container.classList.add("above-page-control-container");
        container.classList.add("bloom-ui");
        container.style.maxWidth = page.clientWidth + "px";
        // see commment in origami.ts about why we put it first.
        // the code there puts it at the start of #page-scaling-container, but that
        // is always the parent of .bloom-page, so this is equivalent.
        page.parentElement?.insertBefore(
            container,
            page.parentElement.firstChild,
        );
    }

    renderPageLayoutMenu(page as HTMLElement, container as HTMLElement);

    if (page.classList.contains("bloom-customLayout")) {
        ensureDerivedFieldsFitOnCustomPage(page as HTMLElement);
    }
}

function renderPageLayoutMenu(page: HTMLElement, container: HTMLElement): void {
    // Render a CustomPageLayoutMenu React component into this container
    const isCustomPage = page.classList.contains("bloom-customLayout");
    const usingLegacyTheme = isUsingLegacyTheme();
    ReactDOM.render(
        <CustomPageLayoutMenu
            isCustom={isCustomPage}
            disableCustomPage={usingLegacyTheme}
            setCustom={(selection) => {
                if (usingLegacyTheme && selection !== "standard") {
                    return;
                }
                if (selection === "customStartOver") {
                    convertXmatterPageToCustom(page);
                    renderPageLayoutMenu(page, container);
                    return;
                }
                postString(
                    "editView/toggleCustomPageLayout",
                    page.getAttribute("id")!,
                ).then((response) => {
                    if (
                        selection === "custom" &&
                        response &&
                        response.data === "false"
                    ) {
                        // making a custom cover for the first time
                        convertXmatterPageToCustom(page);
                        renderPageLayoutMenu(page, container);
                    }
                });
            }}
        />,
        container,
    );
}

function isUsingLegacyTheme(): boolean {
    return !!document.querySelector("link[href*='basePage-legacy-5-6.css']");
}
