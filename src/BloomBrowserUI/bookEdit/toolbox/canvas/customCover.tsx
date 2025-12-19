// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="../../js/collectionSettings.d.ts"/>
import * as ReactDOM from "react-dom";
import * as React from "react";
import { CustomCoverMenu } from "./customCoverMenu";
import {
    kBackgroundImageClass,
    theOneCanvasElementManager,
} from "../../js/CanvasElementManager";
import { EditableDivUtils } from "../../js/editableDivUtils";
import { kBloomCanvasClass, kCanvasElementClass } from "./canvasElementUtils";
import { postString } from "../../../utils/bloomApi";
import { Bubble, BubbleSpec } from "comicaljs";
import { recomputeSourceBubblesForPage } from "../../js/bloomEditing";
import BloomSourceBubbles from "../../sourceBubbles/BloomSourceBubbles";
import { getToolboxBundleExports } from "../../js/bloomFrames";

/* Summary of how custom covers work
    Done:
        - a custom page is indicated by the presence of the class
          bloom-custom-cover on the bloom-page div.
        - a custom cover page has a single bloom-canvas div inside its marginBox,
          with one or more bloom-canvas-element divs inside it.
        - when first created, we make a canvas element for each text and image
          element that was on the cover page.
        - each canvas element is absolutely positioned to match where the content
          was on the page before conversion.
        - where multiple languages of something can be shown (e.g., title), we make
          a canvas element for each language that was visible at the time of conversion.
          We disable any appearance system visibility control and put a value in
          data-default-languages to indicate which of the collection languages
          should be shown there.
    Planned:
        - the content of the custom cover is stored in a sibling of the marginBox
        that has class bloom-customMarginBox. The auto-layout content is kept
        in the regular marginBox.
        - either the regular or custom margin box is visible, dependinng on whether
        the page has bloom-custom-cover.
        - the customMarginBox has data-book="customCover", so its entire content
        is saved in the data-div.
        - previous version of Bloom don't have a bloom-customMarginBox on their
        template cover pages, so they will just go away when the book is brought
        up to date there. But the data will survive in the data-div, so it will
        come back if the newer Bloom does a bring-book-up-to-date.
        - it's important that we save data-book values out of the visible marginBox.
        marginBox is marked data-ignore="bloom-custom-cover", and the custom margin box
        with data-ignore="!bloom-custom-cover" to achieve this.
        - it's important that we restore the content of the custom margin box
        before restoring any of the elements it contains, since if there has been
        editing of elements like title in auto mode (or an older Bloom), we want
        to end up with the edited content. The custom margin box has a class
        bloom-contains-child-data to tell the BookData class to restore it in
        a first pass.
*/

export function convertCoverPageToCustom(page: HTMLElement): void {
    theOneCanvasElementManager?.turnOffCanvasElementEditing();
    // we need to get rid of the old ones before we switch things around,
    // since the remove code makes use of the existing divs that the
    // source bubbles are connected to.
    BloomSourceBubbles.removeSourceBubbles(page);
    const marginBox = page.getElementsByClassName("marginBox")[0];
    if (!marginBox) return; // paranoia and lint

    // review: do we need
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
            let baseSizeOn = elem;
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
                // We also want it to stay that way when C# code later updates visibility codes.
                // This is based on a setting in the TG. Using these generic codes means that if
                // the collection languages change, or we make a derivative, the right language
                // should still be made visible in each box.
                const settings = GetSettings();
                if (settings.languageForNewTextBoxes === lang) {
                    ceContent.setAttribute("data-default-languages", "V");
                } else if (settings.defaultSourceLanguage === lang) {
                    ceContent.setAttribute("data-default-languages", "N1");
                } else if (settings.defaultSourceLanguage2 === lang) {
                    ceContent.setAttribute("data-default-languages", "N2");
                }
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
    marginBox.innerHTML = ""; // remove all existing content (now we got what we want)
    marginBox.appendChild(mainCanvas);
    // This needs to be after we measure positions of things!
    page.classList.add("bloom-custom-cover");
    // The divs that originally held source bubbles are gone
    recomputeSourceBubblesForPage(page);
    // it's a new canvas so we need to set it up
    theOneCanvasElementManager.turnOnCanvasElementEditing();
    getToolboxBundleExports()?.applyToolboxStateToPage();
}

export function setupCoverMenu(): void {
    const page = document.getElementsByClassName("bloom-page")[0];
    // currently only the outside front cover can be switched between auto and custom
    if (!page || !page.classList.contains("outsideFrontCover")) return;

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

    renderCoverMenu(page as HTMLElement, container as HTMLElement);
}

function renderCoverMenu(page: HTMLElement, container: HTMLElement): void {
    // Render a customCoverMenu React component into this container
    const isCustomCover = page.classList.contains("bloom-custom-cover");
    ReactDOM.render(
        <CustomCoverMenu
            isCustom={isCustomCover}
            setCustom={(newVal) => {
                if (newVal) {
                    convertCoverPageToCustom(page);
                } else {
                    page.classList.remove("bloom-custom-cover");
                    postString(
                        "editView/updateXmatter",
                        page.getAttribute("id")!,
                    );
                }
                renderCoverMenu(page, container);
            }}
        />,
        container,
    );
}

// Todo:
// - implement an affordance for calling this function. It should occupy the space
//   currently used for the origami or game tools.
// - also provide a way to turn it off. That will involve removing the class,
//   then a new api message that restores the cover using bringxmatterUptodate.
// - fix basePage.less so that pages with bloom-custom-cover have no padding
// - allow the overlay tool to work on custom covers
