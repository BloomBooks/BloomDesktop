import * as ReactDOM from "react-dom";
import * as React from "react";
import { CustomCoverMenu } from "./customCoverMenu";
import {
    kBackgroundImageClass,
    theOneCanvasElementManager,
} from "../../js/CanvasElementManager";
import { EditableDivUtils } from "../../js/editableDivUtils";
import { kBloomCanvasClass, kCanvasElementClass } from "./canvasElementUtils";

export function convertCoverPageToCustom(page: HTMLElement): void {
    theOneCanvasElementManager?.turnOffCanvasElementEditing();
    page.classList.add("bloom-custom-cover");
    const marginBox = page.getElementsByClassName("marginBox")[0];
    if (!marginBox) return; // paranoia and lint

    // review: do we need
    const contentElements = Array.from(
        marginBox.querySelectorAll("[data-book], [data-derived]"),
    );
    const newCanvasElements: HTMLElement[] = [];
    const newCeContentElements: HTMLElement[] = [];
    for (const elem of contentElements) {
        if (elem instanceof HTMLImageElement) {
            // The main cover image. We will keep it, but it will no longer be
            // a background image.
            const ce = elem.parentElement?.parentElement;
            if (ce && ce.classList.contains(kCanvasElementClass)) {
                newCanvasElements.push(ce as HTMLElement);
                ce.classList.remove(kBackgroundImageClass);
                // We don't need this except to keep the two arrays aligned.
                newCeContentElements.push(ce.firstElementChild as HTMLElement);
                // set its top and left to where it currently is relative to the page
                const pageRect = page.getBoundingClientRect();
                const tgRect = ce.getBoundingClientRect();
                const scale = EditableDivUtils.getPageScale();
                ce.style.left = (tgRect.left - pageRect.left) / scale + "px";
                ce.style.top = (tgRect.top - pageRect.top) / scale + "px";
            }
        } else {
            let ceContent = elem;
            if (elem.classList.contains("bloom-editable")) {
                const tg = elem.parentElement;
                if (!tg || !tg.classList.contains("bloom-translationGroup"))
                    continue;
                // Enhance: if it is the title, make three CE's for three possible languages
                // that might be visible. Distinguish them somehow. Fiddle something so that
                // each is only visible if the corresponding cover language title is turned on.
                // Fiddle some more so that in each only the relevant language is visible.
                // I think it's best if all of them contain the title in all languages; then
                // the right thing can happen if the collection languages change.
                // But for now, if we already made a ce for it's parent, skip it.
                if (newCeContentElements.indexOf(tg) >= 0) continue;
                ceContent = tg; // we'll move the whole tg.
            }
            // This is debatable. But I think having empty CEs around for things like optional
            // branding elements is not helpful.
            if (ceContent.clientHeight === 0 || ceContent.clientWidth === 0)
                continue; // invisible, skip it.
            // make a new canvas element to hold this. Not sure what problems it will
            // cause when it's NOT a TG, but we have at least topic and language name that
            // never are and are commonly on the front cover.
            const newCe = document.createElement("div");
            newCe.classList.add(kCanvasElementClass);
            newCanvasElements.push(newCe);
            // We'd like to just move it, but that will affect its own size and position
            // and also other elements we want to measure.
            newCeContentElements.push(ceContent as HTMLElement);
            // Before we move the tg, measure its size and make newCe match
            // Review: do we need to add allowance for borders/margins/padding?
            newCe.style.width = ceContent.clientWidth + "px";
            newCe.style.height = ceContent.clientHeight + "px";
            // set its top and left to where it currently is relative to the page
            const pageRect = page.getBoundingClientRect();
            const tgRect = ceContent.getBoundingClientRect();
            const scale = EditableDivUtils.getPageScale();
            newCe.style.left = (tgRect.left - pageRect.left) / scale + "px";
            newCe.style.top = (tgRect.top - pageRect.top) / scale + "px";
        }
    }
    const mainCanvas = document.createElement("div");
    mainCanvas.classList.add(kBloomCanvasClass);
    mainCanvas.classList.add("bloom-has-canvas-element");
    for (let i = 0; i < newCanvasElements.length; i++) {
        newCanvasElements[i].appendChild(newCeContentElements[i]);
    }
    for (const newCE of newCanvasElements) {
        mainCanvas.appendChild(newCE);
    }
    marginBox.innerHTML = ""; // remove all existing content (now we got what we want)
    marginBox.appendChild(mainCanvas);
    // it's a new canvas so we need to set it up
    theOneCanvasElementManager.turnOnCanvasElementEditing();
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
                    // Todo: save and clean up
                }
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
