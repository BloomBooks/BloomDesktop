import { processMarkdown } from "./readerSetup.ui";

function checkStrong(item: ChildNode, content: string) {
    expect(item instanceof HTMLElement).toBe(true);
    expect((item as HTMLElement).tagName).toBe("STRONG");
    expect(item.textContent).toBe(content);
}

function checkText(item: ChildNode, content: string) {
    expect(item instanceof HTMLElement).toBe(false);
    expect(item.textContent).toBe(content);
}

describe("processMarkdown tests", () => {
    it("converts ** to bold, middle twice", () => {
        const element = document.createElement("div");
        element.innerText = "This **is** some **bold** text";
        processMarkdown(element);
        expect(element.childNodes.length).toBe(5);
        checkText(element.childNodes[0], "This ");
        checkStrong(element.childNodes[1], "is");
        checkText(element.childNodes[2], " some ");
        checkStrong(element.childNodes[3], "bold");
        checkText(element.childNodes[4], " text");
    });

    it("converts ** to bold, initial and final", () => {
        const element = document.createElement("div");
        element.innerText = "**This** is some text that is **bold**";
        processMarkdown(element);
        expect(element.childNodes.length).toBe(3);
        expect(element.childNodes[0] instanceof HTMLElement).toBe(true);
        expect(element.childNodes[1] instanceof HTMLElement).toBe(false);
        expect(element.childNodes[2] instanceof HTMLElement).toBe(true);
        checkStrong(element.childNodes[0], "This");
        checkText(element.childNodes[1], " is some text that is ");
        checkStrong(element.childNodes[2], "bold");
    });
});
