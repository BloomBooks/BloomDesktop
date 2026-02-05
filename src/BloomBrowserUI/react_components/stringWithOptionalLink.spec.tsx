import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { renderToStaticMarkup } from "react-dom/server";
import { act } from "react-dom/test-utils";

vi.mock("../utils/bloomApi", () => ({
    post: vi.fn(),
    postString: vi.fn(),
}));

import { post, postString } from "../utils/bloomApi";
import { StringWithOptionalLink } from "./stringWithOptionalLink";

const postMock = vi.mocked(post);
const postStringMock = vi.mocked(postString);

describe("StringWithOptionalLink", () => {
    let container: HTMLDivElement | null = null;

    const renderIntoDom = (message: string) => {
        if (!container) {
            throw new Error("render container not initialized");
        }

        const target = container;
        act(() => {
            ReactDOM.render(
                <StringWithOptionalLink message={message} />,
                target,
            );
        });
        return target;
    };

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        vi.clearAllMocks();
    });

    afterEach(() => {
        if (container) {
            ReactDOM.unmountComponentAtNode(container);
            container.remove();
            container = null;
        }
        vi.clearAllMocks();
    });

    it("renders spans and anchors for multiple links", () => {
        const markup = renderToStaticMarkup(
            <StringWithOptionalLink
                message={
                    "Start <a href='/bloom/api/internal'>first</a> middle <a href='http://example.com'>second</a> end"
                }
            />,
        );
        const temp = document.createElement("div");
        temp.innerHTML = markup;

        const spans = temp.querySelectorAll("span");
        const anchors = temp.querySelectorAll("a");

        expect(spans.length).toBe(3);
        expect(spans[0].textContent).toBe("Start ");
        expect(spans[1].textContent).toBe(" middle ");
        expect(spans[2].textContent).toBe(" end");

        expect(anchors.length).toBe(2);
        expect(anchors[0].textContent).toBe("first");
        expect(anchors[0].getAttribute("href")).toBe("/bloom/api/internal");
        expect(anchors[1].textContent).toBe("second");
        expect(anchors[1].getAttribute("href")).toBe("http://example.com");
    });

    it("invokes post for internal links", () => {
        const host = renderIntoDom(
            "Do <a href='/bloom/api/doThing'>this</a> now",
        );
        const anchor = host.querySelector("a");
        expect(anchor).not.toBeNull();

        anchor?.dispatchEvent(
            new MouseEvent("click", { bubbles: true, cancelable: true }),
        );

        expect(postMock).toHaveBeenCalledWith("doThing");
        expect(postStringMock).not.toHaveBeenCalled();
    });

    it("invokes postString for external links", () => {
        const host = renderIntoDom(
            "Visit <a href='mailto:test@example.com'>email</a>",
        );
        const anchor = host.querySelector("a");
        expect(anchor).not.toBeNull();

        anchor?.dispatchEvent(
            new MouseEvent("click", { bubbles: true, cancelable: true }),
        );

        expect(postStringMock).toHaveBeenCalledWith(
            "link",
            "mailto:test@example.com",
        );
        expect(postMock).not.toHaveBeenCalled();
    });

    it("renders a single span for messages with no links", () => {
        const markup = renderToStaticMarkup(
            <StringWithOptionalLink message={"This is plain text"} />,
        );
        const temp = document.createElement("div");
        temp.innerHTML = markup;

        const spans = temp.querySelectorAll("span");
        const anchors = temp.querySelectorAll("a");

        expect(spans.length).toBe(1);
        expect(spans[0].textContent).toBe("This is plain text");
        expect(anchors.length).toBe(0);
    });

    it("handles messages starting with a link", () => {
        const markup = renderToStaticMarkup(
            <StringWithOptionalLink
                message={"<a href='/bloom/api/test'>Link</a> then text"}
            />,
        );
        const temp = document.createElement("div");
        temp.innerHTML = markup;

        const anchors = temp.querySelectorAll("a");
        const spans = temp.querySelectorAll("span");

        expect(anchors.length).toBe(1);
        expect(anchors[0].textContent).toBe("Link");
        expect(spans.length).toBe(1);
        expect(spans[0].textContent).toBe(" then text");
    });

    it("handles messages ending with a link", () => {
        const markup = renderToStaticMarkup(
            <StringWithOptionalLink
                message={"Text before <a href='/bloom/api/test'>Link</a>"}
            />,
        );
        const temp = document.createElement("div");
        temp.innerHTML = markup;

        const spans = temp.querySelectorAll("span");
        const anchors = temp.querySelectorAll("a");

        expect(spans.length).toBe(1);
        expect(spans[0].textContent).toBe("Text before ");
        expect(anchors.length).toBe(1);
        expect(anchors[0].textContent).toBe("Link");
    });

    it("handles consecutive links with no text between them", () => {
        const markup = renderToStaticMarkup(
            <StringWithOptionalLink
                message={
                    "<a href='/bloom/api/first'>First</a><a href='/bloom/api/second'>Second</a>"
                }
            />,
        );
        const temp = document.createElement("div");
        temp.innerHTML = markup;

        const anchors = temp.querySelectorAll("a");
        const spans = temp.querySelectorAll("span");

        expect(anchors.length).toBe(2);
        expect(anchors[0].textContent).toBe("First");
        expect(anchors[1].textContent).toBe("Second");
        expect(spans.length).toBe(0);
    });

    it("renders a single span for empty string", () => {
        const markup = renderToStaticMarkup(
            <StringWithOptionalLink message={""} />,
        );
        const temp = document.createElement("div");
        temp.innerHTML = markup;

        const spans = temp.querySelectorAll("span");
        expect(spans.length).toBe(1);
        expect(spans[0].textContent).toBe("");
    });
});
