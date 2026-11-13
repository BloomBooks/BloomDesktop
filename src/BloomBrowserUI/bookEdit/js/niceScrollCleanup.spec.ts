import { describe, it, expect, beforeEach } from "vitest";
import { removeNiceScrollArtifacts } from "./niceScrollCleanup";

// A translationGroup whose editable has been given a niceScroll, in the state bloom-player's
// addScrollbarsToPage() and niceScroll between them leave it: the alignment class moved aside to
// its "-removed" marker, inline styles on the editable, and a rail (with its cursor inside)
// inserted into the nearest positioned ancestor.
function makeScrolledPage(): HTMLElement {
    const body = document.createElement("div"); // stands in for the cloned document.body
    body.innerHTML = `
        <div class="bloom-page numberedPage">
            <div class="marginBox">
                <div class="split-pane-component-inner">
                    <div class="bloom-translationGroup bloom-vertical-align-center-removed">
                        <div class="bloom-editable bloom-visibility-code-on"
                             style="overflow-y: hidden; overflow-x: hidden; outline: none; width: 379px;">
                            <p>Some text that overflows.</p>
                        </div>
                    </div>
                    <div class="nicescroll-rails nicescroll-rails-vr">
                        <div class="nicescroll-cursors"></div>
                    </div>
                </div>
            </div>
        </div>`;
    return body;
}

describe("removeNiceScrollArtifacts", () => {
    let body: HTMLElement;
    beforeEach(() => {
        body = makeScrolledPage();
    });

    it("sanity check: the test page starts out with all the artifacts", () => {
        expect(body.querySelectorAll(".nicescroll-rails").length).toBe(1);
        expect(body.querySelectorAll(".nicescroll-cursors").length).toBe(1);
        expect(
            body.querySelectorAll(".bloom-vertical-align-center-removed")
                .length,
        ).toBe(1);
        expect(
            body.querySelector<HTMLElement>(".bloom-editable")!.style.overflowY,
        ).toBe("hidden");
    });

    it("removes the rails and the cursors niceScroll inserted", () => {
        removeNiceScrollArtifacts(body);

        expect(body.querySelectorAll(".nicescroll-rails").length).toBe(0);
        expect(body.querySelectorAll(".nicescroll-cursors").length).toBe(0);
    });

    it("puts back the vertical alignment class, so we don't save the page having lost it", () => {
        removeNiceScrollArtifacts(body);

        const group = body.querySelector(".bloom-translationGroup")!;
        expect(group.classList.contains("bloom-vertical-align-center")).toBe(
            true,
        );
        expect(
            group.classList.contains("bloom-vertical-align-center-removed"),
        ).toBe(false);
    });

    it("puts back bloom-vertical-align-bottom too", () => {
        const group = body.querySelector(".bloom-translationGroup")!;
        group.classList.remove("bloom-vertical-align-center-removed");
        group.classList.add("bloom-vertical-align-bottom-removed");

        removeNiceScrollArtifacts(body);

        expect(group.classList.contains("bloom-vertical-align-bottom")).toBe(
            true,
        );
        expect(
            group.classList.contains("bloom-vertical-align-bottom-removed"),
        ).toBe(false);
    });

    it("removes the scrolling-bubble class added to a canvas element's editable", () => {
        const editable = body.querySelector(".bloom-editable")!;
        editable.classList.add("scrolling-bubble");

        removeNiceScrollArtifacts(body);

        expect(editable.classList.contains("scrolling-bubble")).toBe(false);
    });

    it("clears the inline styles niceScroll leaves, and the empty style attribute with them", () => {
        removeNiceScrollArtifacts(body);

        const editable = body.querySelector<HTMLElement>(".bloom-editable")!;
        expect(editable.style.overflowY).toBe("");
        expect(editable.style.overflowX).toBe("");
        expect(editable.style.outline).toBe("");
        expect(editable.style.width).toBe("");
        expect(editable.hasAttribute("style")).toBe(false);
    });

    it("keeps other inline styles on a box niceScroll did touch", () => {
        const editable = body.querySelector<HTMLElement>(".bloom-editable")!;
        editable.style.color = "red";

        removeNiceScrollArtifacts(body);

        expect(editable.style.color).toBe("red");
        expect(editable.style.overflowY).toBe("");
    });

    it("leaves alone an inline width on a box niceScroll never touched", () => {
        // No inline overflow-y, so this box was never given a niceScroll and its width is the
        // author's, not niceScroll's Chrome workaround.
        const editable = body.querySelector<HTMLElement>(".bloom-editable")!;
        editable.setAttribute("style", "width: 200px");

        removeNiceScrollArtifacts(body);

        expect(editable.style.width).toBe("200px");
    });

    it("does nothing to a page that never had scroll bars", () => {
        const untouched = document.createElement("div");
        untouched.innerHTML = `
            <div class="bloom-page">
                <div class="bloom-translationGroup bloom-vertical-align-center">
                    <div class="bloom-editable bloom-visibility-code-on"><p>Short.</p></div>
                </div>
            </div>`;
        const before = untouched.innerHTML;

        removeNiceScrollArtifacts(untouched);

        expect(untouched.innerHTML).toBe(before);
    });
});
