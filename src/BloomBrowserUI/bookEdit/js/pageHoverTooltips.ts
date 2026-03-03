import $ from "jquery";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

const kTooltipTargetClass = "bloom-page-tooltip-target";
const kGutterTargetClass = "bloom-gutter-tooltip-target";
const kGutterLeftClass = "bloom-gutter-target-side-left";
const kGutterRightClass = "bloom-gutter-target-side-right";
const kTooltipTextAttribute = "data-bloom-page-tooltip-text";
const kTooltipBubbleClass = "bloom-page-tooltip";
const kTooltipUiMarkerAttribute = "data-bloom-ui";
const kTooltipUiMarkerValue = "page-hover-tooltip";
const kDefaultGutterTooltipText =
    "Page Gutter: space for staples or other binding.";

let pageHoverTooltip: JQuery | undefined;
let gutterTooltipText = kDefaultGutterTooltipText;
let formatDialogTitleTooltipText = "Format";
let activeTooltipText: string | undefined;
let tooltipLocalizationInitialized = false;

interface MousePositionEvent {
    clientX: number;
    clientY: number;
}

// This module supports tooltips for page editing regions that may not have their own interactive DOM elements.
// For those cases, we create temporary "target" elements (class bloom-page-tooltip-target) and position
// them over the page region (currently, the paper gutter). These targets all get the bloom-ui class so they
// are treated as edit-time markup. Also, when saving, bloomEditing.ts uses
// getBodyInnerHtmlWithoutPageHoverTooltips() so these temporary elements are removed from the HTML that is
// written to disk, even though they remain available in the live editing DOM.

function hidePageHoverTooltip(): void {
    activeTooltipText = undefined;
    pageHoverTooltip?.hide();
}

function ensurePageHoverTooltipBubble(): void {
    if (pageHoverTooltip) {
        return;
    }

    pageHoverTooltip = $(`<div class='bloom-ui ${kTooltipBubbleClass}'></div>`);
    pageHoverTooltip.attr(kTooltipUiMarkerAttribute, kTooltipUiMarkerValue);
    pageHoverTooltip.text(gutterTooltipText);
    $("body").append(pageHoverTooltip);
}

function removeTooltipTargets(): void {
    $(`.${kTooltipTargetClass}`).remove();
}

function makeTooltipTarget(
    page: HTMLElement,
    extraClasses: string[],
    tooltipText: string,
): void {
    const target = document.createElement("div");
    target.classList.add("bloom-ui", kTooltipTargetClass, ...extraClasses);
    target.setAttribute(kTooltipTextAttribute, tooltipText);
    target.setAttribute(kTooltipUiMarkerAttribute, kTooltipUiMarkerValue);
    page.appendChild(target);
}

function addGutterTooltipTargets(): void {
    document.querySelectorAll("div.bloom-page").forEach((pageElement) => {
        const page = pageElement as HTMLElement;
        if (
            page.classList.contains("outsideFrontCover") ||
            page.classList.contains("outsideBackCover")
        ) {
            return;
        }

        if (page.classList.contains("side-left")) {
            makeTooltipTarget(
                page,
                [kGutterTargetClass, kGutterLeftClass],
                gutterTooltipText,
            );
            return;
        }

        if (page.classList.contains("side-right")) {
            makeTooltipTarget(
                page,
                [kGutterTargetClass, kGutterRightClass],
                gutterTooltipText,
            );
        }
    });
}

function updateGutterTooltipTexts(): void {
    document
        .querySelectorAll(`.${kGutterTargetClass}`)
        .forEach((targetElement) => {
            targetElement.setAttribute(
                kTooltipTextAttribute,
                gutterTooltipText,
            );
        });
}

function getTooltipTextForTarget(target: HTMLElement): string | undefined {
    if (target.id === "formatButton") {
        return formatDialogTitleTooltipText;
    }

    const value = target.getAttribute(kTooltipTextAttribute);
    return value || undefined;
}

function setActiveTooltipText(tooltipText: string | undefined): void {
    activeTooltipText = tooltipText;
    if (!activeTooltipText) {
        hidePageHoverTooltip();
        return;
    }

    pageHoverTooltip?.text(activeTooltipText).show();
}

function moveTooltipBubble(event: MousePositionEvent): void {
    if (!activeTooltipText) {
        return;
    }

    pageHoverTooltip?.css({
        left: event.clientX + 12,
        top: event.clientY + 12,
    });
}

function setupTooltipLocalization(): void {
    if (tooltipLocalizationInitialized) {
        return;
    }

    tooltipLocalizationInitialized = true;

    theOneLocalizationManager
        .asyncGetText("EditTab.Tooltip.Gutter", kDefaultGutterTooltipText, "")
        .done((result) => {
            gutterTooltipText = result;
            updateGutterTooltipTexts();
        });

    theOneLocalizationManager
        .asyncGetText("EditTab.FormatDialog.Format", "Format", "")
        .done((result) => {
            formatDialogTitleTooltipText = result;
        });
}

export function setupPageHoverTooltips(): void {
    ensurePageHoverTooltipBubble();
    removeTooltipTargets();
    addGutterTooltipTargets();
    setupTooltipLocalization();

    $(document)
        .off("mouseenter.bloomPageTooltip")
        .on(
            "mouseenter.bloomPageTooltip",
            `.${kTooltipTargetClass}, #formatButton`,
            function (e) {
                const target = this as HTMLElement;
                setActiveTooltipText(getTooltipTextForTarget(target));
                moveTooltipBubble(e as unknown as MousePositionEvent);
            },
        )
        .off("mousemove.bloomPageTooltip")
        .on("mousemove.bloomPageTooltip", function (e) {
            moveTooltipBubble(e as unknown as MousePositionEvent);
        })
        .off("mouseleave.bloomPageTooltip")
        .on(
            "mouseleave.bloomPageTooltip",
            `.${kTooltipTargetClass}, #formatButton`,
            () => {
                setActiveTooltipText(undefined);
            },
        );
}

export function cleanupPageHoverTooltips(): void {
    hidePageHoverTooltip();
    $(document).off(".bloomPageTooltip");
    removeTooltipTargets();
    pageHoverTooltip?.remove();
    pageHoverTooltip = undefined;
}

function removePageHoverTooltipMarkupFrom(root: ParentNode): void {
    root.querySelectorAll(
        `[${kTooltipUiMarkerAttribute}="${kTooltipUiMarkerValue}"]`,
    ).forEach((element) => {
        element.remove();
    });
}

export function getBodyInnerHtmlWithoutPageHoverTooltips(): string {
    const bodyClone = document.body.cloneNode(true) as HTMLElement;
    removePageHoverTooltipMarkupFrom(bodyClone);
    return bodyClone.innerHTML;
}
