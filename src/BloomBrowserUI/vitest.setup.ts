import "@testing-library/jest-dom/vitest";
import { vi } from "vitest";

// Mock comicaljs to avoid webpack bundle loading issues
vi.mock("comicaljs", () => ({
    Bubble: vi.fn(),
    Node: vi.fn(),
    Parser: vi.fn(),
    BubbleSpec: {},
    TailSpec: {},
}));

// Mock localizationManager to provide language names for tests
vi.mock("./lib/localizationManager/localizationManager", () => {
    const languageNames: { [key: string]: string } = {
        en: "English",
        es: "español",
        fr: "français",
        tpi: "Tok Pisin",
    };

    const dictionary: { [key: string]: string } = {};

    const simpleFormat = (format: string, args: (string | undefined)[]) => {
        // Simple implementation of string formatting
        let result = format;
        args.forEach((arg, index) => {
            result = result.replace(
                new RegExp(`\\{${index}\\}`, "g"),
                arg || "",
            );
            result = result.replace(new RegExp(`%${index}`, "g"), arg || "");
        });
        return result;
    };

    return {
        default: {
            getLanguageName: (langTag: string) =>
                languageNames[langTag] || langTag,
            getText: (
                key: string,
                defaultText?: string,
                ...args: (string | undefined)[]
            ) => {
                const text = dictionary[key] || defaultText || key;
                if (args && args.length > 0) {
                    return simpleFormat(text, args);
                }
                return text;
            },
            asyncGetText: (
                key: string,
                defaultText?: string,
                ...args: (string | undefined)[]
            ) => {
                const text = dictionary[key] || defaultText || key;
                const formattedText =
                    args && args.length > 0 ? simpleFormat(text, args) : text;
                // Return a proper Promise that also has done/fail methods for jQuery Deferred compatibility
                const promise = Promise.resolve(formattedText);
                return Object.assign(promise, {
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    done: (callback: (result: any) => void) => {
                        promise.then(callback);
                        return promise;
                    },
                    fail: () => promise,
                });
            },
            isBypassEnabled: () => true, // Bypass localization in tests to avoid server calls
            getVernacularLang: () => "en", // Default vernacular language for tests
            getLanguage2Code: () => "tpi", // Matches GetSettings currentCollectionLanguage2
            getLanguage3Code: () => "fr", // Matches GetSettings currentCollectionLanguage3
            asyncGetTextAndSuccessInfo: (key: string) => {
                // Return a jQuery Deferred-like object
                const result = { success: true, text: key };
                return {
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    done: (callback: (result: any) => void) => {
                        callback(result);
                        return { fail: () => ({}) };
                    },
                    fail: () => ({ done: () => ({}) }),
                };
            },
            simpleFormat,
            loadStringsPromise: (_keys: string[], _lang: string | null) => {
                // Return a Promise that resolves immediately
                return Promise.resolve();
            },
            dictionary,
            // Add other methods as needed
        },
    };
});

// Manually mock HTMLCanvasElement.getContext for jsdom
// This is needed for libraries like comicaljs/paper that use canvas at module load time
HTMLCanvasElement.prototype.getContext = vi.fn(() => ({
    fillStyle: "",
    fillRect: vi.fn(),
    clearRect: vi.fn(),
    getImageData: vi.fn(() => ({ data: [] })),
    putImageData: vi.fn(),
    createImageData: vi.fn(() => []),
    setTransform: vi.fn(),
    drawImage: vi.fn(),
    save: vi.fn(),
    restore: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    lineTo: vi.fn(),
    closePath: vi.fn(),
    stroke: vi.fn(),
    translate: vi.fn(),
    scale: vi.fn(),
    rotate: vi.fn(),
    arc: vi.fn(),
    fill: vi.fn(),
    measureText: vi.fn(() => ({ width: 0 })),
    fillText: vi.fn(),
    transform: vi.fn(),
    rect: vi.fn(),
    clip: vi.fn(),
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
})) as any;

// Polyfill innerText for jsdom - it doesn't properly implement this property
// Use textContent as a fallback which jsdom does support
// Apply to multiple prototypes to cover all cases
[HTMLElement.prototype, Element.prototype, Node.prototype].forEach((proto) => {
    if (!Object.getOwnPropertyDescriptor(proto, "innerText")) {
        Object.defineProperty(proto, "innerText", {
            get() {
                // Ensure we always return a string, never undefined
                const text = this.textContent;
                return text !== null && text !== undefined ? text : "";
            },
            set(value) {
                this.textContent = value;
            },
            configurable: true,
        });
    }

    // Mock scrollIntoView which jsdom doesn't implement (only on Element types)
    if (proto === HTMLElement.prototype || proto === Element.prototype) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        if (!(proto as any).scrollIntoView) {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            (proto as any).scrollIntoView = vi.fn();
        }
    }
});

// Fix for CSSStyleSheet ownerNode in jsdom
// jsdom doesn't properly link styleSheets to their <style> element owners
// We need to handle both: appending <style> elements AND setting their title property

// First, intercept when title is set on a <style> element to also update the CSSStyleSheet
const originalTitleDescriptor = Object.getOwnPropertyDescriptor(
    HTMLElement.prototype,
    "title",
);
Object.defineProperty(HTMLStyleElement.prototype, "title", {
    get: function () {
        return originalTitleDescriptor?.get?.call(this);
    },
    set: function (value: string) {
        // Set the title on the element
        originalTitleDescriptor?.set?.call(this, value);

        // Find the corresponding stylesheet and update its title too
        const doc = this.ownerDocument || document;
        for (let i = 0; i < doc.styleSheets.length; i++) {
            const sheet = doc.styleSheets[i] as CSSStyleSheet;
            if (sheet.ownerNode === this) {
                Object.defineProperty(sheet, "title", {
                    value: value,
                    writable: true,
                    configurable: true,
                });
                break;
            }
        }
    },
    configurable: true,
});

// Override appendChild to link stylesheets when <style> elements are added
const originalHeadAppendChild = HTMLHeadElement.prototype.appendChild;
HTMLHeadElement.prototype.appendChild = function <T extends Node>(node: T): T {
    // Remember the count before we append
    const doc = node.ownerDocument || document;
    const beforeCount = doc.styleSheets.length;

    const result = originalHeadAppendChild.call(this, node);

    // When a <style> element is added, link it to its corresponding stylesheet immediately
    if (node instanceof HTMLStyleElement) {
        // The stylesheet should now be in document.styleSheets
        // Find the newly added one - it should be at the end
        const afterCount = doc.styleSheets.length;

        // If a new stylesheet was added, it's the last one
        if (afterCount > beforeCount) {
            const sheet = doc.styleSheets[afterCount - 1] as CSSStyleSheet;

            // Link it to this element
            Object.defineProperty(sheet, "ownerNode", {
                value: node,
                writable: true,
                configurable: true,
            });

            // Copy the title if it's already set
            if (node.title && !sheet.title) {
                Object.defineProperty(sheet, "title", {
                    value: node.title,
                    writable: true,
                    configurable: true,
                });
            }

            // Ensure cssRules is accessible - jsdom provides an empty array but we may need to enhance it
            try {
                // Check if cssRules exists and is accessible
                if (
                    !sheet.cssRules ||
                    sheet.cssRules === null ||
                    sheet.cssRules === undefined
                ) {
                    throw new Error("cssRules not accessible");
                }

                // jsdom should provide cssRules - just make sure they exist
                // Don't modify anything, let jsdom handle it natively
            } catch {
                // cssRules is not accessible, provide an empty array and insertRule/deleteRule
                const rules: CSSRule[] = [];
                Object.defineProperty(sheet, "cssRules", {
                    get() {
                        return rules;
                    },
                    configurable: true,
                });
                // Add insertRule if missing
                if (!sheet.insertRule) {
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    (sheet as any).insertRule = function (
                        rule: string,
                        index: number,
                    ) {
                        // Simple mock - just track that a rule was added
                        const mockRule = { cssText: rule } as CSSRule;
                        rules.splice(index, 0, mockRule);
                        return index;
                    };
                }
                // Add deleteRule if missing
                if (!sheet.deleteRule) {
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    (sheet as any).deleteRule = function (index: number) {
                        rules.splice(index, 1);
                    };
                }
            }
        }
    }

    return result;
};

// Polyfill HTMLElement.prototype.innerText for jsdom
if (!("innerText" in HTMLElement.prototype)) {
    Object.defineProperty(HTMLElement.prototype, "innerText", {
        get() {
            return this.textContent;
        },
        set(value: string) {
            this.textContent = value;
        },
    });
}

// Fix iframe onload events in jsdom - they don't fire automatically in certain scenarios
// Override appendChild to trigger onload for iframes
const originalAppendChild = HTMLElement.prototype.appendChild;
HTMLElement.prototype.appendChild = function <T extends Node>(node: T): T {
    const result = originalAppendChild.call(this, node);
    if (node instanceof HTMLIFrameElement) {
        // Trigger onload asynchronously
        setTimeout(() => {
            if (node.onload) {
                node.onload(new Event("load"));
            }
            node.dispatchEvent(new Event("load"));

            // Apply polyfills to iframe's content document prototypes
            if (node.contentWindow) {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const win = node.contentWindow as any;
                const iframePrototypes = [
                    win.HTMLElement.prototype,
                    win.Element.prototype,
                    win.Node.prototype,
                ];

                iframePrototypes.forEach((proto) => {
                    if (!Object.getOwnPropertyDescriptor(proto, "innerText")) {
                        Object.defineProperty(proto, "innerText", {
                            get() {
                                const text = this.textContent;
                                return text !== null && text !== undefined
                                    ? text
                                    : "";
                            },
                            set(value) {
                                this.textContent = value;
                            },
                            configurable: true,
                        });
                    }

                    // scrollIntoView for Elements
                    if (
                        proto === win.HTMLElement.prototype ||
                        proto === win.Element.prototype
                    ) {
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        if (!(proto as any).scrollIntoView) {
                            // eslint-disable-next-line @typescript-eslint/no-explicit-any
                            (proto as any).scrollIntoView = vi.fn();
                        }
                    }
                });
            }
        }, 0);
    }
    return result;
};

// Setup parent.window for tests that use iframes
if (!window.parent) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).parent = window;
}

import jQuery from "jquery";
globalThis.$ = jQuery;
globalThis.jQuery = jQuery;

// Mock jQuery localize plugin - used by many modules at load time
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(jQuery.fn as any).localize = vi.fn(function (callback?: () => void) {
    if (callback) {
        callback();
    }
    return this;
});

// Mock jQuery UI sortable plugin
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(jQuery.fn as any).sortable = vi.fn(function () {
    return this;
});

// Mock jQuery UI dialog plugin
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(jQuery.fn as any).dialog = vi.fn(function () {
    return this;
});

// Mock GetSettings function - injected by C# in production, needed by tests
// Based on GetSettingsMock.js from bookEdit/test
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).GetSettings = () => ({
    defaultSourceLanguage: "en",
    currentCollectionLanguage2: "tpi",
    currentCollectionLanguage3: "fr",
    languageForNewTextBoxes: "en",
    browserRoot: "",
    topics: [
        "Agriculture",
        "Animal Stories",
        "Business",
        "Culture",
        "Community Living",
        "Dictionary",
        "Environment",
        "Fiction",
        "Health",
        "How To",
        "Math",
        "Non Fiction",
        "Spiritual",
        "Personal Development",
        "Primer",
        "Science",
        "Tradition",
    ],
});
