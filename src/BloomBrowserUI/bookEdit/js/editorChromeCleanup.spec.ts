import { describe, it, expect } from "vitest";
import { removeEditorChromeFromClone } from "./editorChromeCleanup";

// A clone of the body as it looks once the editor has finished waking up on an xmatter page: the
// page itself, plus everything CKEditor and qTip added around and inside it. Compare the same page
// as it sits on disk, which is just the .bloom-page div.
function makeClonedBodyWithChrome(): HTMLElement {
    const body = document.createElement("div"); // stands in for the cloned document.body
    body.innerHTML = `
        <div class="above-page-control-container bloom-ui">Change Layout</div>
        <div class="bloom-page cover" id="p1">
            <div class="marginBox">
                <div class="bloom-canvas">
                    <button class="changeImageButton imageButton bloom-ui"></button>
                    <div class="ui-resizable-handle ui-resizable-se"></div>
                    <img src="cover.jpg" alt="">
                </div>
                <div class="bloom-translationGroup" data-hasqtip="true"
                     aria-describedby="qtip-2">
                    <div class="bloom-editable normal-style cke_editable cke_focus"
                         lang="en" data-hasqtip="true" aria-describedby="qtip-0">
                        <p>The title</p>
                    </div>
                    <div class="cke_widget_wrapper"></div>
                </div>
                <div id="formatButton" class="bloom-ui"></div>
            </div>
        </div>
        <div id="cke_editor1" class="cke cke_1 cke_float" style="top: 429px; left: 12px;">
            <span class="cke_button">Bold</span>
        </div>
        <div id="qtip-0" class="qtip qtip-default" style="opacity: 1; top: 51.8438px;">
            <div class="qtip-content">Book title in Temein</div>
        </div>`;
    return body;
}

describe("removeEditorChromeFromClone", () => {
    it("removes the chrome and leaves the page itself alone", () => {
        const body = makeClonedBodyWithChrome();

        // Sanity check the fixture really is in the "editor is running" state, so that a test
        // which passes because the chrome was never there cannot masquerade as a passing test.
        expect(body.querySelectorAll(".bloom-ui").length).toBe(3);
        expect(body.querySelector("#cke_editor1")).not.toBeNull();
        expect(body.querySelector("div.qtip")).not.toBeNull();
        expect(body.querySelector(".ui-resizable-handle")).not.toBeNull();

        removeEditorChromeFromClone(body);

        expect(body.querySelectorAll(".bloom-ui").length).toBe(0);
        expect(body.querySelector("#cke_editor1")).toBeNull();
        expect(body.querySelector("div.qtip")).toBeNull();
        expect(body.querySelector(".ui-resizable-handle")).toBeNull();
        expect(body.querySelector(".cke_widget_wrapper")).toBeNull();

        // The page's own content survives untouched.
        const page = body.querySelector(".bloom-page")!;
        expect(page).not.toBeNull();
        expect(page.querySelector("img")!.getAttribute("src")).toBe(
            "cover.jpg",
        );
        expect(page.querySelector(".bloom-editable p")!.textContent).toBe(
            "The title",
        );
    });

    it("strips cke_ classes but keeps the classes that mean something to Bloom", () => {
        const body = makeClonedBodyWithChrome();
        const editableBefore = body.querySelector(".bloom-editable")!;
        expect(editableBefore.className).toContain("cke_editable");

        removeEditorChromeFromClone(body);

        const editable = body.querySelector(".bloom-editable")!;
        expect(editable.className).toBe("bloom-editable normal-style");
    });

    it("removes the class attribute entirely when only cke_ classes were on it", () => {
        const body = document.createElement("div");
        body.innerHTML = `<div class="bloom-page"><span class="cke_bogus">x</span></div>`;

        removeEditorChromeFromClone(body);

        const span = body.querySelector("span")!;
        expect(span).not.toBeNull(); // it is kept; only its class goes
        expect(span.hasAttribute("class")).toBe(false);
    });

    it("removes qTip's bookkeeping attributes, which otherwise churn between runs", () => {
        const body = makeClonedBodyWithChrome();
        expect(body.querySelectorAll("[data-hasqtip]").length).toBe(2);

        removeEditorChromeFromClone(body);

        expect(body.querySelectorAll("[data-hasqtip]").length).toBe(0);
        expect(body.querySelectorAll("[aria-describedby]").length).toBe(0);
    });

    it("keeps an aria-describedby that is not qTip's", () => {
        const body = document.createElement("div");
        body.innerHTML = `<div class="bloom-page"><img aria-describedby="figdesc7" src="a.png"></div>`;

        removeEditorChromeFromClone(body);

        expect(
            body.querySelector("img")!.getAttribute("aria-describedby"),
        ).toBe("figdesc7");
    });

    it("drops the regenerated ids from Comical's SVG but keeps the SVG itself", () => {
        // The SVG is saved on purpose -- it is what draws the bubbles for a reader that has no
        // Comical -- but paper.js stamps a fresh GUID into its ids on every redraw, which made
        // every page with a bubble look edited on every visit.
        const body = document.createElement("div");
        body.innerHTML = `<div class="bloom-page"><div class="bloom-canvas">
            <svg class="comical-generated" width="469" height="325">
                <path d="M-3,328v-331h475v331z" id="i39f5d0ed-c643-4bab-b1aa-a1b0095cce95outlineShape 1 1" fill="#000000"></path>
                <path d="M327,136v-109h146v109z" id="i39f5d0ed-c643-4bab-b1aa-a1b0095cce95outlineShape 1" fill="#ffffff"></path>
            </svg></div></div>`;
        expect(body.querySelectorAll("svg.comical-generated [id]").length).toBe(
            2,
        );

        removeEditorChromeFromClone(body);

        const svg = body.querySelector("svg.comical-generated")!;
        expect(svg).not.toBeNull(); // the drawing itself must survive
        expect(svg.querySelectorAll("[id]").length).toBe(0);
        // and the geometry, which is the part that actually means something, is untouched
        expect(svg.querySelectorAll("path").length).toBe(2);
        expect(svg.querySelector("path")!.getAttribute("d")).toBe(
            "M-3,328v-331h475v331z",
        );
    });

    it("leaves ids alone on an svg that is not Comical's", () => {
        const body = document.createElement("div");
        body.innerHTML = `<div class="bloom-page"><svg class="hand-drawn"><path id="keep-me"></path></svg></div>`;

        removeEditorChromeFromClone(body);

        expect(body.querySelector("#keep-me")).not.toBeNull();
    });

    it("does not remove CKEditor bookmark spans, whose ids also start with cke_", () => {
        const body = document.createElement("div");
        body.innerHTML = `<div class="bloom-page"><p>a<span id="cke_bm_71S"></span>b</p></div>`;

        removeEditorChromeFromClone(body);

        expect(body.querySelector("#cke_bm_71S")).not.toBeNull();
        expect(body.querySelector("p")!.textContent).toBe("ab");
    });
});
