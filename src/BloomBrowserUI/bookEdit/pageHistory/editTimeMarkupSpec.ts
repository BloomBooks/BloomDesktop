// Tests of the rules for edit-time markup (editTimeMarkup.ts): value by value, then through a page
// history that uses them, then CKEditor's filling character. The filling character tests use a
// stand-in for the CKEditor instance that BloomField keeps on a box as bloomCkEditor: it answers
// getCustomData("cke-fillingChar") as CKEditor does, with an object whose `$` is the node. No real
// CKEditor runs here.
import { afterEach, describe, expect, it, vi } from "vitest";
import {
    attributeValueToPutBack,
    createEditTimeMarkupFilter,
    isEditTimeElement,
    isCkEditorFillerBr,
    isDescriptionBoxOfABloomCanvas,
    isInsideEditTimeElement,
    isTrackedFillingCharNode,
    keptAttributeValue,
    keptClasses,
    withoutFillingChar,
    withoutVideoTimestamp,
} from "./editTimeMarkup";
import { stripTransientVideoTimestampParam } from "../js/bloomVideo";
import { createPageHistory } from "./pageHistory";
import { kGenericUndoDescriptionL10nKey } from "./pageHistoryTypes";
import type { PageHistory } from "./pageHistoryTypes";

let page: HTMLElement | undefined;
let history: PageHistory | undefined;

afterEach(() => {
    history?.dispose();
    page?.remove();
    history = undefined;
    page = undefined;
});

/** A .bloom-page with the given content, in the document. */
function makePage(innerHtml: string): HTMLElement {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = innerHtml;
    document.body.appendChild(page);
    return page;
}

function byId(id: string): HTMLElement {
    const element = page!.querySelector<HTMLElement>(`#${id}`);
    expect(element, `test setup: #${id} should be on the page`).not.toBeNull();
    return element!;
}

// MutationObserver queues its callback as a microtask when a change is made, before this await
// queues its own continuation, so by the time this resolves the observer has delivered every
// change made before the call.
async function letObserverDeliver(): Promise<void> {
    await Promise.resolve();
}

const kEditablePage =
    '<div class="bloom-translationGroup bloom-vertical-align-center" id="group">' +
    '<div class="bloom-editable bloom-visibility-code-on" id="box" lang="en" contenteditable="true"><p id="p">Hello <b id="b">bold</b> world</p></div>' +
    "</div>" +
    '<div class="bloom-translationGroup bloom-imageDescription" id="descGroup">' +
    '<div class="bloom-editable bloom-visibility-code-on" id="descBox" lang="en"><p>Description</p></div>' +
    "</div>" +
    '<div class="bloom-canvas" id="canvas"><img id="img" src="a.png" alt=""></div>' +
    '<div class="bloom-videoContainer"><video id="video"><source id="source" src="video/a.mp4"></video></div>';

describe("edit-time elements", () => {
    it("are the elements that the save path removes, and Comical's SVG", () => {
        makePage(
            '<div class="marginBox" id="margin">' +
                '<div class="textBox-identifier" id="label">Text Box</div>' +
                '<div class="bloom-ui" id="ui"><span id="inUi">x</span></div>' +
                '<div class="ui-resizable-handle" id="handle"></div>' +
                '<div data-table-overlay="1" id="overlay"></div>' +
                '<canvas class="comical-generated" id="comicalCanvas"></canvas>' +
                '<svg class="comical-generated" id="comicalSvg"><path id="bubble"></path></svg>' +
                '<div class="nicescroll-rails" id="rails"><div class="nicescroll-cursors" id="cursors"></div></div>' +
                '<canvas class="ui-cbOverlay" id="cbOverlay"></canvas>' +
                '<p id="para">a<span data-cke-bookmark="1" id="bookmark">&nbsp;</span>b</p>' +
                '<div id="cke_pastebin"></div>' +
                '<div data-cke-hidden-sel="1" id="hiddenSel"></div>' +
                '<div class="bloom-editable" id="editable"></div>' +
                "</div>" +
                '<div class="textBox-identifier" id="labelOutsideMarginBox"></div>',
        );
        for (const id of [
            "label",
            "ui",
            "handle",
            "overlay",
            "comicalCanvas",
            "comicalSvg",
            "rails",
            "cursors",
            "cbOverlay",
            "bookmark",
            "cke_pastebin",
            "hiddenSel",
        ]) {
            expect(isEditTimeElement(byId(id)), id).toBe(true);
        }
        for (const id of [
            "margin",
            "para",
            "editable",
            "inUi",
            "bubble",
            "labelOutsideMarginBox",
        ]) {
            expect(isEditTimeElement(byId(id)), id).toBe(false);
        }
        expect(isInsideEditTimeElement(byId("inUi"))).toBe(true);
        expect(isInsideEditTimeElement(byId("inUi").firstChild!)).toBe(true);
        expect(isInsideEditTimeElement(byId("bubble"))).toBe(true);
        expect(isInsideEditTimeElement(byId("para").firstChild!)).toBe(false);
        expect(isInsideEditTimeElement(byId("editable"))).toBe(false);
    });
});

describe("kept classes", () => {
    it("leave out cke_ tokens and every class that the save path removes", () => {
        expect(
            keptClasses(
                "bloom-cell cke_focus cell--selected bloom-pulse-fill bloom-pulse-border " +
                    "table--selected bloom-pointer-near bloom-current-table origami-layout-mode " +
                    "bloom-focusedCanvasElement bloom-hideImageButtons scrolling-bubble " +
                    "page-too-many-words-or-sentences cke_editable",
            ),
        ).toBe("bloom-cell");
    });

    it("leave out bloom-selected, which marks the selected video container", () => {
        expect(
            keptClasses(
                "bloom-videoContainer bloom-leadingElement bloom-selected",
            ),
        ).toBe("bloom-leadingElement bloom-videoContainer");
    });

    it("are undefined when the save keeps no class", () => {
        expect(keptClasses("cke_focus cell--selected")).toBeUndefined();
        expect(keptClasses("")).toBeUndefined();
        expect(keptAttributeValue(document.body, "class", undefined)).toBe(
            undefined,
        );
    });

    it("count niceScroll's renamed alignment classes as the class they name", () => {
        expect(
            keptClasses(
                "bloom-translationGroup bloom-vertical-align-center-removed",
            ),
        ).toBe(
            keptClasses("bloom-translationGroup bloom-vertical-align-center"),
        );
        expect(keptClasses("x bloom-vertical-align-bottom-removed")).toBe(
            "bloom-vertical-align-bottom x",
        );
    });

    it("do not depend on order, spacing or repeats", () => {
        expect(keptClasses("b  a cke_x a")).toBe(keptClasses("a b"));
    });

    it("leave out the classes that OverflowChecker works out from the content", () => {
        expect(
            keptClasses(
                "bloom-editable overflow thisOverflowingParent childOverflowingThis pageOverflows Layout-Problem-Detected",
            ),
        ).toBe("bloom-editable");
        expect(keptClasses("overflowing overflow-x")).toBe(
            "overflow-x overflowing",
        );
    });

    it("keep a class that only starts like an edit-time one", () => {
        expect(keptClasses("cke-focus scrolling-bubbles xcke_y")).toBe(
            "cke-focus scrolling-bubbles xcke_y",
        );
    });
});

describe("putting back a class value", () => {
    const element = document.createElement("div");

    it("keeps the edit-time classes there now and does not bring back old ones", () => {
        const kept = keptAttributeValue(element, "class", "a b cke_old");
        const result = attributeValueToPutBack(
            element,
            "class",
            kept,
            "a cke_focus c",
        );
        expect(result).toBe("a cke_focus b");
        expect(keptAttributeValue(element, "class", result)).toBe(kept);
    });

    it("keeps a class that niceScroll holds in its renamed form", () => {
        const kept = keptAttributeValue(
            element,
            "class",
            "group bloom-vertical-align-center old",
        );
        const result = attributeValueToPutBack(
            element,
            "class",
            kept,
            "group bloom-vertical-align-center-removed",
        );
        expect(result).toBe("group bloom-vertical-align-center-removed old");
        expect(keptAttributeValue(element, "class", result)).toBe(kept);
    });

    it("drops a renamed class whose class the kept value lacks", () => {
        expect(
            attributeValueToPutBack(
                element,
                "class",
                "group",
                "group bloom-vertical-align-center-removed",
            ),
        ).toBe("group");
    });

    it("leaves the value alone when its kept part is already right", () => {
        expect(
            attributeValueToPutBack(element, "class", "a b", "b cke_x  a"),
        ).toBe("b cke_x  a");
    });

    it("removes the attribute when nothing is left, and keeps one that holds only edit-time classes", () => {
        expect(
            attributeValueToPutBack(element, "class", undefined, "a"),
        ).toBeUndefined();
        expect(
            attributeValueToPutBack(element, "class", undefined, "cke_focus"),
        ).toBe("cke_focus");
        expect(attributeValueToPutBack(element, "class", "a", undefined)).toBe(
            "a",
        );
    });
});

describe("kept style", () => {
    it("leaves out bloom-table's hint colors and anchor-name on any element", () => {
        makePage(kEditablePage);
        expect(
            keptAttributeValue(
                byId("img"),
                "style",
                "--hint-top-color: red; left: 1px; --hint-right-color:blue;--hint-bottom-color: x; --hint-left-color: y; anchor-name: --a; --other: 3",
            ),
        ).toBe("left: 1px; --other: 3;");
    });

    it("leaves out niceScroll's properties only on elements that niceScroll may scroll", () => {
        makePage(
            kEditablePage + '<div class="scrollable" id="scrollable"></div>',
        );
        const niceScrollStyle =
            "overflow: hidden; overflow-x: auto; overflow-y: hidden; outline: none; outline-color: red; outline-style: solid; outline-width: 1px; width: 50px; color: red";
        expect(keptAttributeValue(byId("box"), "style", niceScrollStyle)).toBe(
            "color: red;",
        );
        expect(
            keptAttributeValue(byId("scrollable"), "style", niceScrollStyle),
        ).toBe("color: red;");
        // An image description's box, a box not shown, and other elements keep them.
        expect(
            keptAttributeValue(byId("descBox"), "style", "width: 50px"),
        ).toBe("width: 50px;");
        byId("box").classList.remove("bloom-visibility-code-on");
        expect(keptAttributeValue(byId("box"), "style", "width: 50px")).toBe(
            "width: 50px;",
        );
        expect(keptAttributeValue(byId("img"), "style", "width: 50px")).toBe(
            "width: 50px;",
        );
    });

    it("leaves out min-height only on a box, where OverflowChecker works it out", () => {
        makePage(kEditablePage);
        expect(
            keptAttributeValue(
                byId("box"),
                "style",
                "min-height: 24px; color: red",
            ),
        ).toBe("color: red;");
        expect(
            keptAttributeValue(byId("descBox"), "style", "min-height: 24px"),
        ).toBeUndefined();
        expect(
            keptAttributeValue(byId("img"), "style", "min-height: 24px"),
        ).toBe("min-height: 24px;");
        expect(
            keptAttributeValue(byId("group"), "style", "min-height: 24px"),
        ).toBe("min-height: 24px;");
    });

    it("does not depend on spacing, trailing semicolons or the case of names", () => {
        const element = document.createElement("div");
        expect(keptAttributeValue(element, "style", "LEFT:1px;top : 2px")).toBe(
            keptAttributeValue(element, "style", " left: 1px; top: 2px; "),
        );
        expect(
            keptAttributeValue(element, "style", "--Custom: 1; --custom: 2"),
        ).toBe("--Custom: 1; --custom: 2;");
    });

    it("splits only at semicolons outside quotes and brackets", () => {
        const element = document.createElement("div");
        const value =
            "background-image: url(\"data:image/svg+xml;utf8,<svg a='1;2'/>\"); content: 'a;b'; left: 1px";
        expect(keptAttributeValue(element, "style", value)).toBe(
            "background-image: url(\"data:image/svg+xml;utf8,<svg a='1;2'/>\"); content: 'a;b'; left: 1px;",
        );
    });

    it("is undefined when nothing is kept", () => {
        makePage(kEditablePage);
        expect(
            keptAttributeValue(byId("box"), "style", "width: 3px; outline: 0"),
        ).toBeUndefined();
        expect(
            keptAttributeValue(byId("box"), "style", "nonsense"),
        ).toBeUndefined();
    });

    it("is put back with the edit-time properties there now", () => {
        makePage(kEditablePage);
        const box = byId("box");
        const kept = keptAttributeValue(box, "style", "color: red; width: 9px");
        expect(kept).toBe("color: red;");
        const result = attributeValueToPutBack(
            box,
            "style",
            kept,
            "color: blue; overflow-y: hidden; width: 40px",
        );
        expect(result).toBe("color: red; overflow-y: hidden; width: 40px;");
        expect(keptAttributeValue(box, "style", result)).toBe(kept);
        expect(
            attributeValueToPutBack(box, "style", undefined, "color: blue"),
        ).toBeUndefined();
    });
});

describe("CKEditor's filler br", () => {
    it("is a br with no data-cke-eol in a block inside a box", () => {
        makePage(
            '<div class="bloom-editable" id="box"><p id="p"><br id="br"><span id="span"><br id="inSpan"></span></p><br id="inBox"></div>' +
                '<p id="outside"><br id="brOutside"></p>' +
                '<div class="bloom-editable"><p id="eolP"><br id="eol" data-cke-eol="1"></p></div>',
        );
        expect(isCkEditorFillerBr(byId("br"), byId("p"))).toBe(true);
        expect(isCkEditorFillerBr(byId("inBox"), byId("box"))).toBe(true);
        expect(isCkEditorFillerBr(byId("inSpan"), byId("span"))).toBe(false);
        expect(isCkEditorFillerBr(byId("brOutside"), byId("outside"))).toBe(
            false,
        );
        expect(isCkEditorFillerBr(byId("eol"), byId("eolP"))).toBe(false);
        expect(isCkEditorFillerBr(byId("span"), byId("p"))).toBe(false);
    });
});

describe("a description box", () => {
    it("is left out of child lists only as a child of a bloom-canvas", () => {
        makePage(kEditablePage);
        const box = document.createElement("div");
        box.className = "bloom-translationGroup bloom-imageDescription";
        byId("canvas").appendChild(box);
        expect(isDescriptionBoxOfABloomCanvas(box, byId("canvas"))).toBe(true);
        expect(
            isDescriptionBoxOfABloomCanvas(byId("descGroup"), page!),
            "one that is not in a bloom-canvas",
        ).toBe(false);
        expect(
            isDescriptionBoxOfABloomCanvas(byId("img"), byId("canvas")),
            "another child of a bloom-canvas",
        ).toBe(false);
    });
});

describe("the attributes that CKEditor sets on a box when it attaches", () => {
    it("are kept as nothing on a bloom-editable, and left as they are when put back", () => {
        const box = document.createElement("div");
        box.className = "bloom-editable";
        for (const [name, value] of [
            ["tabindex", "0"],
            ["spellcheck", "false"],
            ["role", "textbox"],
            ["aria-label", "false"],
        ]) {
            expect(keptAttributeValue(box, name, value), name).toBeUndefined();
            expect(attributeValueToPutBack(box, name, undefined, value)).toBe(
                value,
            );
        }
    });

    it("are kept everywhere else, and contenteditable is kept on a box too", () => {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        expect(
            keptAttributeValue(group, "tabindex", "2"),
            "a translation group's tabindex is the Talking Book playback order",
        ).toBe("2");
        const element = document.createElement("div");
        expect(keptAttributeValue(element, "role", "button")).toBe("button");
        const box = document.createElement("div");
        box.className = "bloom-editable";
        expect(keptAttributeValue(box, "contenteditable", "true")).toBe("true");
        expect(keptAttributeValue(box, "dir", "rtl")).toBe("rtl");
    });
});

describe("attributes that the save drops", () => {
    const element = document.createElement("div");

    it("are kept as nothing", () => {
        for (const [name, value] of [
            ["data-hasqtip", "true"],
            ["aria-describedby", "qtip-3"],
            ["ariasecondary-describedby", "qtip-4"],
            ["data-cke-saved-href", "x.htm"],
            ["data-cke-filter", "off"],
            ["data-table-attached", "1"],
            ["data-btable-anchor-name", "--a"],
            ["data-bloom-active", "true"],
        ]) {
            expect(keptAttributeValue(element, name, value), name).toBe(
                undefined,
            );
            expect(
                attributeValueToPutBack(element, name, undefined, value),
                `${name} stays as it is when put back`,
            ).toBe(value);
        }
    });

    it("do not include describedby values that are not qtip's, or other attributes", () => {
        expect(keptAttributeValue(element, "aria-describedby", "note1")).toBe(
            "note1",
        );
        expect(keptAttributeValue(element, "data-bubble", "{a}")).toBe("{a}");
        expect(keptAttributeValue(element, "id", "cke_1")).toBe("cke_1");
    });
});

describe("a video's src", () => {
    const urls = [
        "video/a.mp4?bloomVideoTransientTimestamp=123",
        "video/a.mp4?x=1&bloomVideoTransientTimestamp=123#t=2",
        "video/a.mp4?bloomVideoTransientTimestamp=123&x=1",
        "video/a.mp4#t=1,2",
        "video/a.mp4",
        "video/a%20b.mp4?x=1",
    ];

    it("is kept without the timestamp, as stripTransientVideoTimestampParam keeps it", () => {
        for (const url of urls) {
            expect(withoutVideoTimestamp(url), url).toBe(
                stripTransientVideoTimestampParam(url),
            );
        }
        expect(
            withoutVideoTimestamp(
                "video/a.mp4?x=1&bloomVideoTransientTimestamp=123#t=2",
            ),
        ).toBe("video/a.mp4?x=1#t=2");
    });

    it("is compared without the timestamp only on video and its source", () => {
        makePage(kEditablePage);
        const url = "video/a.mp4?bloomVideoTransientTimestamp=5";
        expect(keptAttributeValue(byId("video"), "src", url)).toBe(
            "video/a.mp4",
        );
        expect(keptAttributeValue(byId("source"), "src", url)).toBe(
            "video/a.mp4",
        );
        expect(keptAttributeValue(byId("img"), "src", url)).toBe(url);
    });
});

describe("a page history with the edit-time markup filter", () => {
    function setUp(innerHtml = kEditablePage): {
        history: PageHistory;
        unmarked: ReturnType<typeof vi.fn<(whatChanged: string) => void>>;
    } {
        makePage(innerHtml);
        const unmarked = vi.fn<(whatChanged: string) => void>();
        history = createPageHistory({
            root: page!,
            filter: createEditTimeMarkupFilter(),
            onUnmarkedChange: unmarked,
        });
        return { history, unmarked };
    }

    /** Make one step, then undo it, so that a redo is there to lose. */
    async function stepThenUndo(h: PageHistory): Promise<void> {
        await h.doTransaction("A", () =>
            byId("img").setAttribute("src", "b.png"),
        );
        await h.undo();
        expect(h.redoDescriptionL10nKey, "test setup").toBe("A");
    }

    it("ignores edit-time changes made outside any transaction: no warning, redo kept", async () => {
        const { history: h, unmarked } = setUp();
        await stepThenUndo(h);

        const box = byId("box");
        box.classList.add("cke_focus", "cke_editable");
        box.setAttribute("data-hasqtip", "true");
        box.setAttribute("aria-describedby", "qtip-1");
        box.style.overflowY = "hidden";
        box.style.width = "100px";
        box.classList.add("scrolling-bubble");
        const group = byId("group");
        group.classList.remove("bloom-vertical-align-center");
        group.classList.add("bloom-vertical-align-center-removed");
        byId("source").setAttribute(
            "src",
            "video/a.mp4?bloomVideoTransientTimestamp=99",
        );
        byId("canvas").setAttribute("data-bloom-active", "true");
        const ui = document.createElement("div");
        ui.className = "bloom-ui";
        byId("canvas").appendChild(ui);
        const bookmark = document.createElement("span");
        bookmark.setAttribute("data-cke-bookmark", "1");
        byId("p").insertBefore(bookmark, byId("b"));
        await letObserverDeliver();

        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("A");
        expect(h.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes no step for a transaction whose changes are all edit-time", async () => {
        const { history: h } = setUp();
        await stepThenUndo(h);
        await h.doTransaction("Focus", () => {
            byId("box").classList.add("cke_focus");
            byId("box").style.width = "5px";
            byId("group").setAttribute(
                "class",
                "bloom-vertical-align-center-removed bloom-translationGroup",
            );
        });
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        expect(h.redoDescriptionL10nKey).toBe("A");
    });

    it("keeps redo when OverflowChecker takes its classes off after an undo, and leaves them as they are when it puts a class value back", async () => {
        const { history: h, unmarked } = setUp();
        const box = byId("box");
        await h.doTransaction("Style", () => {
            box.classList.add("Heading1-style", "overflow");
            page!.classList.add("pageOverflows");
            byId("group").classList.add("childOverflowingThis");
        });
        expect(
            h.undoDescriptionL10nKey,
            "test setup: the step should hold the style class",
        ).toBe("Style");

        await h.undo();
        expect(box.classList.contains("Heading1-style")).toBe(false);
        expect(
            box.classList.contains("overflow"),
            "undo leaves OverflowChecker's class to it",
        ).toBe(true);
        // OverflowChecker works the classes out again for the restored content.
        box.classList.remove("overflow");
        page!.classList.remove("pageOverflows");
        byId("group").classList.remove("childOverflowingThis");
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("Style");

        await h.redo();
        expect(box.classList.contains("Heading1-style")).toBe(true);
        expect(box.classList.contains("overflow")).toBe(false);
        expect(page!.classList.contains("pageOverflows")).toBe(false);
    });

    it("makes no step for a transaction that changes only OverflowChecker's classes", async () => {
        const { history: h } = setUp();
        await h.doTransaction("Type", () => {
            byId("box").classList.add("overflow");
            page!.classList.add("pageOverflows");
        });
        expect(h.undoDescriptionL10nKey).toBeUndefined();
    });

    it("undoes a class change without bringing back an old cke_focus or removing the current one", async () => {
        const { history: h } = setUp();
        const box = byId("box");
        box.classList.add("cke_focus");
        await letObserverDeliver();
        await h.doTransaction("Style", () => {
            box.classList.add("Heading1-style");
            box.classList.remove("cke_focus");
        });
        box.classList.add("cke_editable");
        await letObserverDeliver();

        await h.undo();

        expect(box.classList.contains("Heading1-style")).toBe(false);
        expect(box.classList.contains("cke_focus")).toBe(false);
        expect(box.classList.contains("cke_editable")).toBe(true);
        expect(box.classList.contains("bloom-editable")).toBe(true);

        await h.redo();
        expect(box.classList.contains("Heading1-style")).toBe(true);
        expect(box.classList.contains("cke_editable")).toBe(true);
    });

    it("compares kept parts in the check before undo", async () => {
        const { history: h } = setUp();
        const img = byId("img");
        await h.doTransaction("Move", () => {
            img.setAttribute("style", "left: 1px");
            img.setAttribute("class", "a");
        });
        // Edit-time changes to values that the step holds, and a reformatting of the style.
        img.setAttribute("style", "anchor-name: --x;left:1px");
        img.setAttribute("class", "cke_x a");
        await letObserverDeliver();

        await h.undo();

        expect(h.redoDescriptionL10nKey, "the undo should not be refused").toBe(
            "Move",
        );
        expect(img.getAttribute("style")).toBe("anchor-name: --x;");
        expect(img.getAttribute("class")).toBe("cke_x");
    });

    it("puts back a style value and keeps niceScroll's properties there now", async () => {
        const { history: h } = setUp();
        const box = byId("box");
        await h.doTransaction("Color", () =>
            box.setAttribute("style", "color: red"),
        );
        box.setAttribute(
            "style",
            "color: red; overflow-y: hidden; width: 50px",
        );
        await letObserverDeliver();

        await h.undo();

        expect(box.getAttribute("style")).toBe(
            "overflow-y: hidden; width: 50px;",
        );
        await h.redo();
        expect(box.getAttribute("style")).toBe(
            "color: red; overflow-y: hidden; width: 50px;",
        );
    });

    it("keeps redo when OverflowChecker first gives a box its min-height after a quick undo of typing", async () => {
        const { history: h, unmarked } = setUp(
            '<div class="bloom-editable bloom-visibility-code-on" id="box" lang="en" contenteditable="true"><p id="p"><br></p></div>',
        );
        const box = byId("box");
        const paragraph = byId("p");
        expect(box.hasAttribute("style"), "test setup").toBe(false);
        await h.doTransaction("Type", () => {
            // As the browser does: the filler goes, the text comes.
            paragraph.firstChild!.remove();
            paragraph.appendChild(document.createTextNode("sun"));
        });
        await h.undo();
        expect(paragraph.textContent).toBe("");
        expect(h.redoDescriptionL10nKey, "test setup").toBe("Type");

        // OverflowChecker.CheckOnMinHeight's timer, set when the page loaded, fires now.
        box.style.minHeight = "";
        box.style.minHeight = "24px";
        await letObserverDeliver();

        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("Type");
        await h.redo();
        expect(paragraph.textContent).toBe("sun");
        expect(box.style.minHeight, "undo and redo leave it alone").toBe(
            "24px",
        );
        await h.undo();
        expect(paragraph.textContent).toBe("");
        expect(box.style.minHeight).toBe("24px");
    });

    it("records and puts back a change that the save keeps next to edit-time ones", async () => {
        const { history: h } = setUp();
        const source = byId("source");
        await h.doTransaction("Video", () =>
            source.setAttribute(
                "src",
                "video/b.mp4?bloomVideoTransientTimestamp=2",
            ),
        );
        expect(h.undoDescriptionL10nKey).toBe("Video");
        await h.undo();
        expect(source.getAttribute("src")).toBe("video/a.mp4");
    });

    it("neither records nor puts back changes inside Comical's SVG", async () => {
        const { history: h, unmarked } = setUp(
            kEditablePage +
                '<svg class="comical-generated" id="svg"><path id="path" d="M0"></path></svg>',
        );
        await stepThenUndo(h);
        byId("path").setAttribute("d", "M1");
        byId("svg").appendChild(
            document.createElementNS("http://www.w3.org/2000/svg", "path"),
        );
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("A");
    });

    /** What CKEditor's inline editor does to a box when it finishes attaching. */
    function attachCkEditor(box: HTMLElement): void {
        box.classList.add(
            "cke_editable",
            "cke_editable_inline",
            "cke_contents_ltr",
        );
        box.setAttribute("tabindex", "0");
        box.setAttribute("spellcheck", "false");
        box.setAttribute("contenteditable", "true");
        box.setAttribute("role", "textbox");
        box.setAttribute("aria-label", "false");
    }

    it("ignores CKEditor attaching to a box in the page: no warning, redo kept", async () => {
        const { history: h, unmarked } = setUp();
        await stepThenUndo(h);
        byId("box").setAttribute("contenteditable", "true");
        await letObserverDeliver();
        expect(h.redoDescriptionL10nKey, "test setup").toBe("A");

        attachCkEditor(byId("box"));
        await letObserverDeliver();

        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("A");
    });

    it("does not refuse the undo of a step that took out a box CKEditor attached to afterwards", async () => {
        const { history: h } = setUp();
        const group = byId("group");
        const box = byId("box");
        box.setAttribute("contenteditable", "true");
        const parent = group.parentNode!;
        await h.doTransaction("Duplicate", () => group.remove());
        await letObserverDeliver();

        // CKEditor finishes attaching to the box after it has left the page, and writes the
        // box's content again as new nodes.
        attachCkEditor(box);
        const content = box.innerHTML;
        box.innerHTML = content;

        await h.undo();
        expect(group.parentNode, "the undo should not be refused").toBe(parent);
        expect(h.redoDescriptionL10nKey).toBe("Duplicate");
    });

    it("does not refuse the undo of a step that took out a box whose empty paragraph CKEditor filled afterwards", async () => {
        const { history: h } = setUp(
            '<div class="bloom-translationGroup" id="group">' +
                '<div class="bloom-editable" id="box" lang="sw"><p id="empty"></p></div>' +
                "</div>" +
                '<img id="img" src="a.png">',
        );
        const group = byId("group");
        const empty = byId("empty");
        const parent = group.parentNode!;
        await h.doTransaction("Duplicate", () => group.remove());
        await letObserverDeliver();

        empty.appendChild(document.createElement("br"));

        await h.undo();
        expect(group.parentNode, "the undo should not be refused").toBe(parent);
        await h.redo();
        expect(group.isConnected).toBe(false);
    });

    it("keeps CKEditor adding and removing the filler of an empty paragraph out of every step", async () => {
        const { history: h, unmarked } = setUp(
            kEditablePage.replace('<p id="p">', '<p id="empty"></p><p id="p">'),
        );
        const empty = byId("empty");
        const filler = document.createElement("br");
        await stepThenUndo(h);

        // The bubble loses focus: CKEditor fills its empty paragraph, later takes it out again.
        empty.appendChild(filler);
        await letObserverDeliver();
        filler.remove();
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("A");

        // The filler arriving in the same batch as the next action does not join it either.
        const restoredRoots: Element[] = [];
        h.onAfterUndoOrRedo((restored) =>
            restoredRoots.push(...restored.roots),
        );
        await h.doTransaction("Add bubble", () => {
            empty.appendChild(filler);
            byId("img").setAttribute("src", "c.png");
        });
        await h.undo();
        expect(byId("img").getAttribute("src")).toBe("a.png");
        expect(filler.parentNode, "undo leaves the filler where it is").toBe(
            empty,
        );
        expect(restoredRoots).toEqual([byId("img")]);
    });

    it("counts a br that is not the filler of an empty paragraph", async () => {
        const { history: h } = setUp(
            '<div class="bloom-editable" id="box">' +
                '<p id="twoLines">a<br id="kept">b</p>' +
                '<p id="twoBreaks"><br id="first"><br id="second"></p>' +
                "</div>",
        );
        await h.doTransaction("Join lines", () => byId("kept").remove());
        await h.doTransaction("Delete line", () => byId("second").remove());
        expect(h.undoDescriptionL10nKey).toBe("Delete line");

        await h.undo();
        expect(byId("twoBreaks").childNodes.length).toBe(2);
        await h.undo();
        expect(byId("twoLines").innerHTML).toBe('a<br id="kept">b');
    });

    it("lets text typed into a filled empty paragraph be undone and redone", async () => {
        const { history: h } = setUp(
            '<div class="bloom-editable" id="box"><p id="empty"><br></p></div>',
        );
        const empty = byId("empty");
        await h.doTransaction("Type", () => {
            // As the browser does: the filler goes, the text comes.
            empty.firstChild!.remove();
            empty.appendChild(document.createTextNode("x"));
        });

        await h.undo();
        expect(empty.textContent).toBe("");
        await h.redo();
        expect(empty.textContent).toBe("x");
        await h.undo();
        expect(empty.textContent, "the second undo should not be refused").toBe(
            "",
        );
    });

    it("ignores bloom-selected: selecting a video container after an undo keeps redo, and undo leaves the selection as it is", async () => {
        const { history: h, unmarked } = setUp(
            '<div class="bloom-videoContainer bloom-leadingElement bloom-selected" id="first"></div>' +
                '<div class="bloom-videoContainer bloom-leadingElement" id="second"></div>' +
                '<img id="img" src="a.png">',
        );
        const first = byId("first");
        const second = byId("second");
        await h.doTransaction("Change picture", () =>
            byId("img").setAttribute("src", "b.png"),
        );
        await h.undo();
        expect(h.redoDescriptionL10nKey, "test setup").toBe("Change picture");

        // As selectVideoContainer does.
        first.classList.remove("bloom-selected");
        second.classList.add("bloom-selected");
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("Change picture");

        // Inside a step, the selection's move is not recorded either.
        await h.doTransaction("Change picture again", () => {
            second.classList.remove("bloom-selected");
            first.classList.add("bloom-selected");
            byId("img").setAttribute("src", "c.png");
        });
        await h.undo();
        expect(byId("img").getAttribute("src")).toBe("a.png");
        expect(first.classList.contains("bloom-selected")).toBe(true);
        expect(second.classList.contains("bloom-selected")).toBe(false);
    });

    it("ignores bloom-imageLoadError, which the img's own handlers keep in step with its src", async () => {
        const { history: h, unmarked } = setUp();
        const img = byId("img");
        await h.doTransaction("Change picture", () =>
            img.setAttribute("src", "missing.png"),
        );
        img.classList.add("bloom-imageLoadError");
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();

        await h.undo();
        expect(img.getAttribute("src")).toBe("a.png");
        expect(
            img.classList.contains("bloom-imageLoadError"),
            "undo leaves the class to the img's load handler",
        ).toBe(true);
        img.classList.remove("bloom-imageLoadError");
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("Change picture");
    });

    it("leaves a description box out of its bloom-canvas's child lists, and records what is typed into it", async () => {
        const { history: h, unmarked } = setUp();
        const canvas = byId("canvas");
        const element = document.createElement("div");
        element.className = "bloom-canvas-element";
        await h.doTransaction("Add", () => canvas.appendChild(element));
        await h.undo();
        expect(h.redoDescriptionL10nKey, "test setup").toBe("Add");

        // Tool setup adds the box with no transaction; it is no change.
        const box = document.createElement("div");
        box.className = "bloom-translationGroup bloom-imageDescription";
        box.innerHTML =
            '<div class="bloom-editable" lang="en"><p id="descP"></p></div>';
        canvas.appendChild(box);
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("Add");

        await h.redo();
        expect(element.parentElement).toBe(canvas);
        expect(box.parentElement).toBe(canvas);

        await h.doTransaction("Type", () => {
            byId("descP").textContent = "A dog";
        });
        await h.undo();
        expect(byId("descP").textContent).toBe("");
        await h.redo();
        expect(byId("descP").textContent).toBe("A dog");

        await h.undo();
        await h.undo();
        expect(element.isConnected).toBe(false);
        expect(box.parentElement).toBe(canvas);
        expect(byId("descP").textContent).toBe("");
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        expect(h.redoDescriptionL10nKey).toBe("Add");
    });

    it("refuses an undo when the text in a description box changed while its bloom-canvas was out of the page", async () => {
        const { history: h } = setUp();
        const canvas = byId("canvas");
        const box = document.createElement("div");
        box.className = "bloom-translationGroup bloom-imageDescription";
        box.innerHTML = '<div class="bloom-editable" lang="en"><p>A</p></div>';
        h.doOutsideHistory(() => canvas.appendChild(box));
        await h.doTransaction("Remove", () => canvas.remove());
        expect(h.undoDescriptionL10nKey, "test setup").toBe("Remove");

        h.doOutsideHistory(() => {
            box.querySelector("p")!.textContent = "B";
        });
        await h.undo();
        expect(
            canvas.isConnected,
            "the check should see the change inside the box",
        ).toBe(false);
        expect(h.undoDescriptionL10nKey).toBeUndefined();
    });
});

/**
 * A stand-in for the CKEditor instance on a box: it keeps the node holding the filling character
 * as CKEditor keeps it, as custom data on the editable.
 */
class StandInCkEditor {
    public tracked: Node | undefined;
    public constructor(box: HTMLElement) {
        (box as HTMLElement & { bloomCkEditor?: unknown }).bloomCkEditor = {
            editable: () => ({
                getCustomData: (key: string) =>
                    key === "cke-fillingChar" && this.tracked
                        ? { $: this.tracked }
                        : undefined,
                removeCustomData: (key: string) => {
                    if (key === "cke-fillingChar") this.tracked = undefined;
                },
            }),
        };
    }

    /** What CKEditor does at a collapsed selection: insert a new text node holding U+200B. */
    public insertFillingChar(parent: Node, before: Node | null): Text {
        const node = document.createTextNode("\u200B");
        this.tracked = node;
        parent.insertBefore(node, before);
        return node;
    }

    /**
     * What CKEditor does when it removes the filling character (on a selection change, before
     * getData(), and so on): forget the node and take every U+200B out of it.
     */
    public removeFillingChar(): void {
        const node = this.tracked as Text;
        this.tracked = undefined;
        node.data = node.data.replace(/\u200B( )?/g, (match) =>
            match[1] ? "\u00A0" : "",
        );
    }
}

/** Every U+200B in the page's text. */
function zeroWidthSpacesIn(root: Node): number {
    return (root.textContent ?? "").split("\u200B").length - 1;
}

describe("CKEditor's filling character", () => {
    function setUp(): {
        history: PageHistory;
        editor: StandInCkEditor;
        unmarked: ReturnType<typeof vi.fn<(whatChanged: string) => void>>;
    } {
        makePage(kEditablePage);
        const editor = new StandInCkEditor(byId("box"));
        const unmarked = vi.fn<(whatChanged: string) => void>();
        history = createPageHistory({
            root: page!,
            filter: createEditTimeMarkupFilter(),
            onUnmarkedChange: unmarked,
        });
        return { history, editor, unmarked };
    }

    it("is taken out with a following space turned into a no-break space", () => {
        expect(withoutFillingChar("\u200B")).toBe("");
        expect(withoutFillingChar("\u200Babc")).toBe("abc");
        expect(withoutFillingChar("\u200B x")).toBe("\u00A0x");
        expect(withoutFillingChar("a\u200Bb\u200Bc")).toBe("ab\u200Bc");
        expect(withoutFillingChar("abc")).toBe("abc");
    });

    it("is recognized only in the node that the box's editor tracks", () => {
        const { editor } = setUp();
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        expect(isTrackedFillingCharNode(node)).toBe(true);
        expect(isTrackedFillingCharNode(byId("p").firstChild!)).toBe(false);
        editor.tracked = undefined;
        expect(isTrackedFillingCharNode(node)).toBe(false);
    });

    it("is never put back after CKEditor has forgotten its node (BL-16490)", async () => {
        const { history: h, editor, unmarked } = setUp();
        let node!: Text;
        await h.doTransaction("Click", () => {
            node = editor.insertFillingChar(byId("p"), byId("b"));
        });
        await h.doTransaction("Type", () => node.appendData("ab"));
        expect(node.data, "test setup").toBe("\u200Bab");

        // CKEditor removes it on the next selection change, outside any transaction.
        editor.removeFillingChar();
        await letObserverDeliver();
        expect(node.data).toBe("ab");
        expect(
            unmarked,
            "taking out the filling character changes nothing",
        ).not.toHaveBeenCalled();

        expect(
            h.undoDescriptionL10nKey,
            "inserting the filling character made no step",
        ).toBe("Type");

        await h.undo();
        expect(node.data).toBe("");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        await h.redo();
        expect(node.data).toBe("ab");
        expect(byId("p").textContent).toBe("Hello abbold world");
        await h.undo();
        expect(byId("p").textContent).toBe("Hello bold world");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
    });

    it("is not recorded when CKEditor forgets its node inside a new transaction", async () => {
        const { history: h, editor } = setUp();
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        await letObserverDeliver();
        await h.doTransaction("Type", () => node.appendData("ab"));

        // A click elsewhere: Bloom opens a transaction, then CKEditor takes the character out.
        await h.doTransaction("Click elsewhere", () => {
            editor.removeFillingChar();
            byId("img").setAttribute("src", "c.png");
        });
        await h.undo();
        expect(node.data, "the click's undo should not touch the text").toBe(
            "ab",
        );
        await h.undo();
        expect(node.data).toBe("");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
    });

    it("is not recorded when CKEditor forgets its node in the same batch as the next typing", async () => {
        const { history: h, editor } = setUp();
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        await letObserverDeliver();
        node.appendData("a");
        await letObserverDeliver();
        await h.doTransaction("Type", () => {
            editor.removeFillingChar();
            node.appendData("b");
        });

        await h.undo();
        expect(node.data).toBe("a");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
    });

    it("is known in a node that CKEditor tracked before the history started", async () => {
        makePage(kEditablePage);
        const editor = new StandInCkEditor(byId("box"));
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        node.appendData("a");
        history = createPageHistory({
            root: page!,
            filter: createEditTimeMarkupFilter(),
            onUnmarkedChange: () => undefined,
        });
        await history.doTransaction("Type", () => {
            editor.removeFillingChar();
            node.appendData("b");
        });

        await history.undo();
        expect(node.data).toBe("a");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
    });

    it("is not put back by the rollback of a transaction in which CKEditor forgot its node", async () => {
        const { history: h, editor } = setUp();
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        node.appendData("a");
        await letObserverDeliver();
        const drag = h.beginTransaction("Drag");
        editor.removeFillingChar();
        await letObserverDeliver();
        node.appendData("b");
        byId("img").setAttribute("src", "c.png");

        drag.cancel();

        expect(byId("img").getAttribute("src")).toBe("a.png");
        expect(node.data).toBe("a");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
    });

    it("is taken out by a rollback while CKEditor still tracks its node", async () => {
        const { history: h, editor } = setUp();
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        await letObserverDeliver();
        const drag = h.beginTransaction("Drag");
        node.appendData("x");
        drag.cancel();
        // The rollback writes the kept text and makes CKEditor forget the node, so that nothing
        // later mistakes another U+200B in it for the filling character.
        expect(node.data).toBe("");
        expect(editor.tracked).toBeUndefined();
    });

    it("makes CKEditor forget a node that an undo writes while it still tracks it", async () => {
        const { history: h, editor } = setUp();
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        await letObserverDeliver();
        await h.doTransaction("Type", () => node.appendData("ab"));
        expect(editor.tracked, "test setup").toBe(node);

        await h.undo();

        expect(node.data).toBe("");
        expect(editor.tracked).toBeUndefined();
        await h.redo();
        expect(node.data).toBe("ab");
    });

    it("leaves real U+200B word breaks alone, in a tracked node and elsewhere", async () => {
        const { history: h, editor } = setUp();
        const thai = "\u0E01\u200B\u0E02";
        const text = byId("p").firstChild as Text;
        await h.doTransaction("Type Thai", () => {
            text.data = thai + " ";
        });
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        await letObserverDeliver();
        await h.doTransaction("Type Thai after the caret", () =>
            node.appendData(thai),
        );

        await h.undo();
        expect(node.data).toBe("");
        await h.redo();
        expect(node.data, "only the filling character goes").toBe(thai);
        await h.undo();
        await h.undo();
        expect(text.data).toBe("Hello ");
        await h.redo();
        expect(text.data).toBe(thai + " ");
    });

    it("does not count in a change that only CKEditor's insertion and removal of it made", async () => {
        const { history: h, editor, unmarked } = setUp();
        const node = editor.insertFillingChar(byId("p"), byId("b"));
        await letObserverDeliver();
        await h.doTransaction("Step", () =>
            byId("img").setAttribute("src", "b.png"),
        );
        await h.undo();
        unmarked.mockClear();

        editor.removeFillingChar();
        await letObserverDeliver();

        expect(node.data).toBe("");
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("Step");
    });
});

describe("the text node that holds CKEditor's filling character", () => {
    function setUp(): {
        history: PageHistory;
        editor: StandInCkEditor;
        unmarked: ReturnType<typeof vi.fn<(whatChanged: string) => void>>;
        emptyParagraph: HTMLElement;
    } {
        makePage(kEditablePage);
        byId("box").insertAdjacentHTML("beforeend", '<p id="empty"><br></p>');
        const editor = new StandInCkEditor(byId("box"));
        const unmarked = vi.fn<(whatChanged: string) => void>();
        history = createPageHistory({
            root: page!,
            filter: createEditTimeMarkupFilter(),
            onUnmarkedChange: unmarked,
        });
        return { history, editor, unmarked, emptyParagraph: byId("empty") };
    }

    /** Make one step, then undo it, so that a redo is there to lose. */
    async function stepThenUndo(h: PageHistory): Promise<void> {
        await h.doTransaction("A", () =>
            byId("img").setAttribute("src", "b.png"),
        );
        await h.undo();
        expect(h.redoDescriptionL10nKey, "test setup").toBe("A");
    }

    it("is no change when CKEditor inserts it after an undo, or takes it out again", async () => {
        const { history: h, editor, unmarked, emptyParagraph } = setUp();
        await stepThenUndo(h);

        // The next key press after the undo, with the caret in the empty paragraph.
        const node = editor.insertFillingChar(
            emptyParagraph,
            emptyParagraph.firstChild,
        );
        await letObserverDeliver();
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("A");

        editor.removeFillingChar();
        await letObserverDeliver();
        expect(node.data, "test setup").toBe("");
        expect(unmarked).not.toHaveBeenCalled();
        expect(h.redoDescriptionL10nKey).toBe("A");
        await h.redo();
        expect(byId("img").getAttribute("src")).toBe("b.png");
    });

    it("makes no step when inserted inside a transaction", async () => {
        const { history: h, editor, emptyParagraph } = setUp();
        await stepThenUndo(h);
        await h.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            editor.insertFillingChar(emptyParagraph, emptyParagraph.firstChild);
        });
        expect(h.undoDescriptionL10nKey).toBeUndefined();
        expect(h.redoDescriptionL10nKey).toBe("A");
    });

    it("joins the child list when text is typed into it, and an undo empties it and leaves older steps undoable", async () => {
        const { history: h, editor, emptyParagraph } = setUp();
        await h.doTransaction("A", () =>
            byId("img").setAttribute("src", "b.png"),
        );
        const node = editor.insertFillingChar(
            emptyParagraph,
            emptyParagraph.firstChild,
        );
        await letObserverDeliver();
        await h.doTransaction("Type", () => node.appendData("x"));

        await h.undo();
        expect(node.data).toBe("");
        expect(emptyParagraph.textContent).toBe("");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
        await h.undo();
        expect(
            byId("img").getAttribute("src"),
            "the older step should not be refused",
        ).toBe("a.png");
        await h.redo();
        await h.redo();
        expect(emptyParagraph.textContent).toBe("x");
        expect(emptyParagraph.firstChild).toBe(node);
    });

    it("leaves the child list when its text is deleted back to the filling character, and comes back on undo", async () => {
        const { history: h, editor, emptyParagraph } = setUp();
        const node = editor.insertFillingChar(
            emptyParagraph,
            emptyParagraph.firstChild,
        );
        await letObserverDeliver();
        await h.doTransaction("Type", () => node.appendData("x"));
        await h.doTransaction("Delete", () => node.deleteData(1, 1));
        expect(node.data, "test setup").toBe("\u200B");
        expect(h.undoDescriptionL10nKey).toBe("Delete");

        await h.undo();
        expect(node.data).toBe("x");
        expect(node.parentNode).toBe(emptyParagraph);
        await h.undo();
        expect(node.data).toBe("");
        await h.redo();
        await h.redo();
        expect(node.data).toBe("");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
    });

    it("is judged by its text at the start of a batch that also changes its parent's children", async () => {
        const { history: h, editor, emptyParagraph } = setUp();
        const node = editor.insertFillingChar(
            emptyParagraph,
            emptyParagraph.firstChild,
        );
        await letObserverDeliver();
        const bold = document.createElement("b");
        await h.doTransaction("Type", () => {
            node.appendData("x");
            emptyParagraph.appendChild(bold);
        });

        await h.undo();
        expect(bold.isConnected).toBe(false);
        expect(node.data).toBe("");
        await h.redo();
        expect(h.undoDescriptionL10nKey, "the redo should not be refused").toBe(
            "Type",
        );
        expect(bold.parentNode).toBe(emptyParagraph);
        expect(node.data).toBe("x");
    });

    /**
     * Type text into the node that holds the filling character, one input at a time in a typing
     * transaction, each just before the character, where the caret is, as the browser does; then
     * CKEditor takes the character out, still inside the transaction, and the transaction ends.
     */
    async function typeIntoFillingCharNode(
        h: PageHistory,
        editor: StandInCkEditor,
        node: Text,
        text: string,
    ): Promise<void> {
        const typing = h.beginTransaction(kGenericUndoDescriptionL10nKey);
        for (const character of text) {
            node.insertData(node.data.length - 1, character);
            await letObserverDeliver();
        }
        expect(node.data, "test setup").toBe(text + "​");
        editor.removeFillingChar();
        await letObserverDeliver();
        typing.end();
    }

    it("is put back by redo after merging empty text nodes took it out while it was empty, in a paragraph that held only CKEditor's filler br", async () => {
        const { history: h, editor, unmarked, emptyParagraph } = setUp();
        const filler = emptyParagraph.firstChild!;
        expect(filler.nodeName, "test setup").toBe("BR");
        // The click: CKEditor puts the filling character in front of the filler br.
        const node = editor.insertFillingChar(emptyParagraph, filler);
        await letObserverDeliver();
        // Typing into the node changes only its text: the br stays.
        await typeIntoFillingCharNode(h, editor, node, "sun");
        expect(emptyParagraph.childNodes.length, "test setup").toBe(2);
        expect(h.undoDescriptionL10nKey).toBe(kGenericUndoDescriptionL10nKey);

        await h.undo();
        expect(node.data).toBe("");
        expect(node.parentNode).toBe(emptyParagraph);
        // Bloom merges adjacent text nodes (EditableDivUtils), which removes empty ones; the
        // history sees no change, since an empty node that held the filling character is ignored.
        emptyParagraph.normalize();
        await letObserverDeliver();
        expect(node.parentNode, "test setup").toBeNull();
        expect(unmarked).not.toHaveBeenCalled();

        await h.redo();
        expect(node.parentNode).toBe(emptyParagraph);
        expect(emptyParagraph.textContent).toBe("sun");
        expect(emptyParagraph.firstChild).toBe(node);
        await h.undo();
        expect(emptyParagraph.textContent).toBe("");
        expect(zeroWidthSpacesIn(page!)).toBe(0);
        await h.redo();
        expect(emptyParagraph.textContent).toBe("sun");
        expect(emptyParagraph.firstChild).toBe(node);
        expect(zeroWidthSpacesIn(page!)).toBe(0);
    });

    it("is put back by redo after merging empty text nodes took it out while it was empty, in a paragraph that held a space", async () => {
        // What an e2e run saw in a speech bubble: <p> </p>, the filling character in a new node
        // in front of the space, "hello" typed in front of the character.
        const { history: h, editor, unmarked } = setUp();
        byId("box").insertAdjacentHTML("beforeend", '<p id="space"> </p>');
        await letObserverDeliver();
        const paragraph = byId("space");
        const space = paragraph.firstChild as Text;
        const node = editor.insertFillingChar(paragraph, space);
        await letObserverDeliver();
        await typeIntoFillingCharNode(h, editor, node, "hello");
        expect(paragraph.textContent, "test setup").toBe("hello ");

        await h.undo();
        expect(node.data).toBe("");
        expect(paragraph.textContent).toBe(" ");
        paragraph.normalize();
        await letObserverDeliver();
        expect(node.parentNode, "test setup").toBeNull();
        expect(unmarked).not.toHaveBeenCalled();

        await h.redo();
        expect(paragraph.textContent).toBe("hello ");
        expect(Array.from(paragraph.childNodes)).toEqual([node, space]);
        await h.undo();
        expect(paragraph.textContent).toBe(" ");
        await h.redo();
        expect(Array.from(paragraph.childNodes)).toEqual([node, space]);
        expect(paragraph.textContent).toBe("hello ");
    });

    it("is an ordinary text node once it holds text: removing it is recorded and undone", async () => {
        const { history: h, editor, emptyParagraph } = setUp();
        const node = editor.insertFillingChar(
            emptyParagraph,
            emptyParagraph.firstChild,
        );
        await letObserverDeliver();
        await h.doTransaction("Type", () => node.appendData("ab"));
        editor.removeFillingChar();
        await letObserverDeliver();

        await h.doTransaction("Cut", () => node.remove());
        await h.undo();

        expect(node.parentNode).toBe(emptyParagraph);
        expect(node.data).toBe("ab");
        expect(h.undoDescriptionL10nKey).toBe("Type");
    });
});

describe("a picture's transparent query parameter", () => {
    // The save strips it (HtmlDom.RemoveTransparencyParamFromImages) and the next load adds it
    // again from the picture's classes and the page's background. While editing, only two things
    // change it: the Canvas tool's Transparency menu, in the same step as the class it sets, and
    // the page background colour of Book and Page Settings, outside the history. So the filter
    // keeps it, and undo puts the class and the src back together.
    it("comes back with the class on undo and redo of a Transparency choice", async () => {
        makePage(
            '<div class="bloom-canvas" id="canvas"><img id="img" src="a.png?transparent=yes" alt=""></div>',
        );
        history = createPageHistory({
            root: page!,
            filter: createEditTimeMarkupFilter(),
            onUnmarkedChange: () => {},
        });
        const img = byId("img");
        await history.doTransaction("EditTab.Image.Transparency", () => {
            img.classList.add("bloom-opaque");
            img.setAttribute("src", "a.png");
        });

        await history.undo();

        expect(img.classList.contains("bloom-opaque")).toBe(false);
        expect(img.getAttribute("src")).toBe("a.png?transparent=yes");
        await history.redo();
        expect(img.classList.contains("bloom-opaque")).toBe(true);
        expect(img.getAttribute("src")).toBe("a.png");
    });
});
