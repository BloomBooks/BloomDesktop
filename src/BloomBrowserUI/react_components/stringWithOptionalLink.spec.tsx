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

    it("produces just a span when no links exist in message", () => {
        const plainMessage = "This is plain text without any links";
        const rendered = renderToStaticMarkup(
            <StringWithOptionalLink message={plainMessage} />,
        );
        const wrapper = document.createElement("div");
        wrapper.innerHTML = rendered;

        const spanElements = wrapper.querySelectorAll("span");
        const linkElements = wrapper.querySelectorAll("a");

        expect(spanElements.length).toBe(1);
        expect(linkElements.length).toBe(0);
        expect(spanElements[0].textContent).toBe(plainMessage);
    });

    it("handles message that begins with a link", () => {
        const msgStartingWithLink =
            "<a href='/bloom/api/action'>Click</a> to continue";
        const rendered = renderToStaticMarkup(
            <StringWithOptionalLink message={msgStartingWithLink} />,
        );
        const wrapper = document.createElement("div");
        wrapper.innerHTML = rendered;

        const linkElements = wrapper.querySelectorAll("a");
        const spanElements = wrapper.querySelectorAll("span");

        expect(linkElements.length).toBe(1);
        expect(linkElements[0].textContent).toBe("Click");
        expect(spanElements.length).toBe(1);
        expect(spanElements[0].textContent).toBe(" to continue");
    });

    it("handles message that ends with a link", () => {
        const msgEndingWithLink =
            "Please visit <a href='http://bloomlibrary.org'>our site</a>";
        const rendered = renderToStaticMarkup(
            <StringWithOptionalLink message={msgEndingWithLink} />,
        );
        const wrapper = document.createElement("div");
        wrapper.innerHTML = rendered;

        const spanElements = wrapper.querySelectorAll("span");
        const linkElements = wrapper.querySelectorAll("a");

        expect(spanElements.length).toBe(1);
        expect(spanElements[0].textContent).toBe("Please visit ");
        expect(linkElements.length).toBe(1);
        expect(linkElements[0].textContent).toBe("our site");
    });

    it("handles consecutive links without text between", () => {
        const adjacentLinks =
            "Links: <a href='/bloom/api/first'>one</a><a href='http://example.org'>two</a>";
        const rendered = renderToStaticMarkup(
            <StringWithOptionalLink message={adjacentLinks} />,
        );
        const wrapper = document.createElement("div");
        wrapper.innerHTML = rendered;

        const linkElements = wrapper.querySelectorAll("a");
        const spanElements = wrapper.querySelectorAll("span");

        expect(linkElements.length).toBe(2);
        expect(linkElements[0].textContent).toBe("one");
        expect(linkElements[1].textContent).toBe("two");
        expect(spanElements.length).toBe(1);
        expect(spanElements[0].textContent).toBe("Links: ");
    });

    it("handles empty message string", () => {
        const emptyMessage = "";
        const rendered = renderToStaticMarkup(
            <StringWithOptionalLink message={emptyMessage} />,
        );
        const wrapper = document.createElement("div");
        wrapper.innerHTML = rendered;

        const spanElements = wrapper.querySelectorAll("span");
        expect(spanElements.length).toBe(1);
        expect(spanElements[0].textContent).toBe("");
    });
});
