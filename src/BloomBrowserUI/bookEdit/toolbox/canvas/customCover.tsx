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
                // was made for. Normally this is controlled by bloom-visibility-code-on.
                // But that is managed by C# code and it would be messy and duplicative
                // to make make special cases there for custom covers. So instead we'll add
                // our own class.
                newEditable.classList.add("bloom-custom-cover-only-visible");
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
