import { Page } from "../../component-tester/playwrightTest";
import { prepareGetResponse } from "../../component-tester/apiInterceptors";
import { setTestComponent } from "../../component-tester/setTestComponent";
import { LinkTargetInfo } from "../LinkTargetChooser";

const createMockBook = (index: number) => {
    const colors = [
        "4CAF50",
        "2196F3",
        "FF9800",
        "E91E63",
        "9C27B0",
        "00BCD4",
        "CDDC39",
        "FF5722",
        "607D8B",
        "795548",
    ];
    const color = colors[index % colors.length];
    const bookNumber = index + 1;
    const bookId = `book${bookNumber}`;
    const pageCount = bookNumber * 10;

    return {
        id: bookId,
        title: `Book ${bookNumber}`,
        folderName: `${bookNumber} Book ${bookNumber}`,
        thumbnail: `data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%23${color}'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook ${bookNumber}%3C/text%3E%3C/svg%3E`,
        pageLength: pageCount + 1,
    };
};

const mockBooks = Array.from({ length: 10 }, (_, i) => createMockBook(i));

interface LinkTargetChooserSetupOptions {
    currentURL?: string;
    onCancel?: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
    currentBookId?: string;
    books?: typeof mockBooks;
    pages?: Array<{
        key: string;
        caption: string;
        content?: string;
        isXMatter?: boolean;
        disabled?: boolean;
    }>;
    pageLayout?: string;
    cssFiles?: string[];
    bookAttributes?: Record<string, string>;
    selectedPageId?: string;
    frontCoverPageId?: string;
    frontCoverCaption?: string;
    clipboardText?: string; // Mock clipboard content
}

const booksApiPattern =
    /\/bloom\/api\/+collections\/books\?realTitle=(true|false)/;

export interface LinkTargetChooserTestContext {
    page: Page;
    frontCoverId: string;
    cleanup: () => void;
    urlEditor: {
        getInput: () => Promise<ReturnType<Page["locator"]>>;
        getPasteButton: () => Promise<ReturnType<Page["getByTestId"]>>;
        getOpenButton: () => Promise<ReturnType<Page["getByTestId"]>>;
        getValue: () => Promise<string>;
        setValue: (value: string) => Promise<void>;
    };
    dialog: {
        getOKButton: () => Promise<ReturnType<Page["getByRole"]>>;
        getCancelButton: () => Promise<ReturnType<Page["getByRole"]>>;
        isOKEnabled: () => Promise<boolean>;
    };
    bookList: {
        getBookCard: (bookId: string) => Promise<ReturnType<Page["locator"]>>;
        selectBook: (bookId: string) => Promise<void>;
        isBookSelected: (bookId: string) => Promise<boolean>;
        waitForBooksToLoad: () => Promise<void>;
    };
    pageList: {
        getPage: (
            pageId: string | number,
        ) => Promise<ReturnType<Page["getByTestId"]>>;
        selectPage: (pageId: string | number) => Promise<void>;
        isPageSelected: (pageId: string | number) => Promise<boolean>;
        isPageDisabled: (pageId: string | number) => Promise<boolean>;
    };
    errorDisplay: {
        getErrorMessage: () => Promise<ReturnType<Page["getByTestId"]>>;
        isVisible: () => Promise<boolean>;
    };
}

const resolvePageIdentifier = (
    pageId: string | number,
    frontCoverId: string,
) => {
    if (typeof pageId === "number") {
        return pageId <= 0 ? frontCoverId : pageId.toString();
    }
    if (pageId === "p-cover") {
        return frontCoverId;
    }
    if (pageId.startsWith("p-")) {
        return pageId.substring(2);
    }
    if (pageId === "cover") {
        return frontCoverId;
    }
    return pageId;
};

export async function setupLinkTargetChooser(
    page: Page,
    props: LinkTargetChooserSetupOptions = {},
): Promise<LinkTargetChooserTestContext> {
    // we don't ever want to store the guid of a cover page, they get regenerated.
    // #cover works, but is usually not needed because we can just point at the book.
    let frontCoverId = props.frontCoverPageId ?? "cover";

    const books = props.books ?? mockBooks;
    prepareGetResponse(page, booksApiPattern, books);

    prepareGetResponse(
        page,
        "**/bloom/api/editView/currentBookId",
        props.currentBookId ?? "",
    );

    // Intercept page list APIs used by the PageList component
    // Generate dynamic pages based on book ID if not provided
    if (!props.pages) {
        await page.route("**/bloom/api/pageList/pages**", async (route) => {
            const url = new URL(route.request().url());
            const bookId = url.searchParams.get("book-id");
            const coverKey = frontCoverId;
            const coverCaption = props.frontCoverCaption ?? "Cover";

            // Find the book to get its page count
            const book = books.find((b) => b.id === bookId);
            const pageCount = book ? book.pageLength - 1 : 2; // pageLength includes cover

            const pages = [
                {
                    key: coverKey,
                    caption: coverCaption,
                    content: "",
                    isXMatter: true,
                },
                ...Array.from({ length: pageCount }, (_, i) => ({
                    key: `${i + 1}`,
                    caption: `Page ${i + 1}`,
                    content: "",
                    isXMatter: false,
                })),
            ];

            await route.fulfill({
                status: 200,
                contentType: "application/json",
                body: JSON.stringify({
                    pages,
                    selectedPageId: props.selectedPageId ?? "cover",
                    pageLayout: props.pageLayout ?? "A5Portrait",
                    cssFiles: props.cssFiles ?? [],
                }),
            });
        });
    } else {
        frontCoverId = props.pages[0]?.key ?? "cover";
        prepareGetResponse(page, "**/bloom/api/pageList/pages**", {
            pages: props.pages.map((p, index) => ({
                key: p.key,
                caption: p.caption,
                content: p.content ?? "",
                isXMatter: p.isXMatter ?? index === 0,
                disabled:
                    p.disabled ?? ((p.isXMatter ?? index === 0) && index !== 0),
            })),
            selectedPageId: props.selectedPageId ?? "cover",
            pageLayout: props.pageLayout ?? "A5Portrait",
            cssFiles: props.cssFiles ?? [],
        });
    }

    prepareGetResponse(
        page,
        "**/bloom/api/pageList/bookAttributesThatMayAffectDisplay**",
        props.bookAttributes ?? {},
    );

    // Mock clipboard API - returns empty string by default or the provided clipboardText
    // Tests can override this by calling prepareGetResponse again with their own mock
    prepareGetResponse(page, "**/bloom/api/common/clipboardText", {
        data: props.clipboardText ?? "",
    });

    await page.route("**/bloom/api/collections/bookFile**", async (route) => {
        await route.fulfill({
            status: 200,
            contentType: "text/css",
            body: "/* test css */",
        });
    });
    // Dynamic route for page content
    await page.route("**/bloom/api/pageList/pageContent**", async (route) => {
        const url = new URL(route.request().url());
        const id = url.searchParams.get("id") || "";
        const color =
            id === "cover" ? "4CAF50" : id === "1" ? "2196F3" : "FF9800";
        const caption =
            id === "cover" ? "Cover" : id === "1" ? "Page 1" : "Page 2";
        const html = `<div class='bloom-page' inert><div style='width:160px;height:100px;background:#${color};color:#fff;display:flex;align-items:center;justify-content:center;'>${caption}</div></div>`;
        await route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify({ data: { content: html } }),
        });
    });

    await setTestComponent(
        page,
        "../LinkTargetChooser/LinkTargetChooserDialog",
        "LinkTargetChooserDialog",
        {
            open: true,
            currentURL: props.currentURL || "",
            onCancel: props.onCancel,
            onSelect: props.onSelect,
        },
    );

    const cleanup = () => {
        // Cleanup function for resetting any test state if needed
        // Currently no cleanup required as we're not using module-level state
    };

    const context: LinkTargetChooserTestContext = {
        page,
        frontCoverId,
        cleanup,
        urlEditor: {
            getInput: async () => {
                const selector =
                    '[data-testid="url-input"] input, input[placeholder="Paste or enter a URL"]';
                await page.waitForSelector(selector, {
                    state: "attached",
                    timeout: 5000,
                });
                return page.locator(selector).first();
            },
            getPasteButton: async () => {
                return page.getByTestId("paste-button");
            },
            getOpenButton: async () => {
                return page.getByTestId("open-button");
            },
            getValue: async () => {
                const input = await context.urlEditor.getInput();
                return input.inputValue();
            },
            setValue: async (value: string) => {
                const input = await context.urlEditor.getInput();
                await input.fill(value);
            },
        },
        dialog: {
            getOKButton: async () => {
                return page.getByRole("button", { name: "OK" });
            },
            getCancelButton: async () => {
                return page.getByRole("button", { name: /cancel/i });
            },
            isOKEnabled: async () => {
                const okButton = await context.dialog.getOKButton();
                const isDisabled = await okButton.getAttribute("disabled");
                return isDisabled === null;
            },
        },
        bookList: {
            getBookCard: async (bookId: string) => {
                return page.locator(`[data-book-id="${bookId}"]`);
            },
            selectBook: async (bookId: string) => {
                const card = await context.bookList.getBookCard(bookId);
                await card.waitFor({ state: "visible", timeout: 5000 });
                await card.click();
            },
            isBookSelected: async (bookId: string) => {
                const card = await context.bookList.getBookCard(bookId);
                await card.waitFor({ state: "visible", timeout: 5000 });
                return card.evaluate((element) => {
                    const target = element as HTMLElement;
                    if (target.getAttribute("data-selected") === "true") {
                        return true;
                    }
                    if (
                        target.classList.contains("link-target-book--selected")
                    ) {
                        return true;
                    }
                    const nestedSelected = target.querySelector(
                        "[data-selected='true']",
                    );
                    if (nestedSelected) {
                        return true;
                    }
                    return false;
                });
            },
            waitForBooksToLoad: async () => {
                await page
                    .locator("[data-book-id]")
                    .first()
                    .waitFor({ state: "visible", timeout: 5000 });
            },
        },
        pageList: {
            getPage: async (pageId: string | number) => {
                return page.getByTestId(
                    `page-${resolvePageIdentifier(pageId, frontCoverId)}`,
                );
            },
            selectPage: async (pageId: string | number) => {
                const pageLoc = await context.pageList.getPage(pageId);
                await pageLoc.waitFor({ state: "visible", timeout: 5000 });
                await pageLoc.click();
            },
            isPageSelected: async (pageId: string | number) => {
                const locator = await context.pageList.getPage(pageId);
                try {
                    await locator.waitFor({ state: "visible", timeout: 5000 });
                } catch {
                    return false;
                }

                return locator.evaluate((element) => {
                    const target = element as HTMLElement;
                    if (target.getAttribute("data-selected") === "true") {
                        return true;
                    }
                    if (
                        target.classList.contains("link-target-page--selected")
                    ) {
                        return true;
                    }
                    return false;
                });
            },
            isPageDisabled: async (pageId: string | number) => {
                const locator = await context.pageList.getPage(pageId);
                try {
                    await locator.waitFor({ state: "visible", timeout: 5000 });
                } catch {
                    return false;
                }

                return locator.evaluate((element) => {
                    const target = element as HTMLElement;
                    const ariaDisabled = target.getAttribute("aria-disabled");
                    if (ariaDisabled === "true") {
                        return true;
                    }
                    const dataDisabled = target.getAttribute("data-disabled");
                    if (dataDisabled === "true") {
                        return true;
                    }
                    return target.classList.contains("disabled");
                });
            },
        },
        errorDisplay: {
            getErrorMessage: async () => {
                return page.getByTestId("error-message");
            },
            isVisible: async () => {
                const errorMsg = page.getByTestId("error-message");
                const count = await errorMsg.count();
                if (count === 0) {
                    return false;
                }
                return errorMsg.isVisible();
            },
        },
    };

    return context;
}
