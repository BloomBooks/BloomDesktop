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
    onClose?: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
    currentBookId?: string;
    books?: typeof mockBooks;
    pages?: Array<{
        key: string;
        caption: string;
        content?: string;
        isXMatter?: boolean;
    }>;
    pageLayout?: string;
    cssFiles?: string[];
    bookAttributes?: Record<string, string>;
    selectedPageId?: string;
    frontCoverPageId?: string;
    frontCoverCaption?: string;
}

const booksApiPattern =
    /\/bloom\/api\/+collections\/books\?realTitle=(true|false)/;

let currentPage: Page | undefined;
let currentFrontCoverId = "cover";

export async function setupLinkTargetChooser(
    page: Page,
    props: LinkTargetChooserSetupOptions = {},
) {
    currentPage = page;

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
            const coverKey = props.frontCoverPageId ?? "cover";
            const coverCaption = props.frontCoverCaption ?? "Cover";
            currentFrontCoverId = coverKey;

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
            console.log(
                "*******Using dynamic page list generation based on book ID",
                bookId,
                pages,
            );

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
        currentFrontCoverId = props.pages[0]?.key ?? "cover";
        prepareGetResponse(page, "**/bloom/api/pageList/pages**", {
            pages: props.pages.map((p, index) => ({
                key: p.key,
                caption: p.caption,
                content: p.content ?? "",
                isXMatter: p.isXMatter ?? index === 0,
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

    await page.route("**/bloom/api/pageList/bookFile/**", async (route) => {
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
            onClose: props.onClose,
            onSelect: props.onSelect,
        },
    );
}

const resolvePageIdentifier = (pageId: string | number) => {
    if (typeof pageId === "number") {
        return pageId <= 0 ? currentFrontCoverId : pageId.toString();
    }
    if (pageId === "p-cover") {
        return currentFrontCoverId;
    }
    if (pageId.startsWith("p-")) {
        return pageId.substring(2);
    }
    if (pageId === "cover") {
        return currentFrontCoverId;
    }
    return pageId;
};

export const urlEditor = {
    getInput: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        const selector =
            '[data-testid="url-input"] input, input[placeholder="Paste or enter a URL"]';
        await currentPage.waitForSelector(selector, {
            state: "attached",
            timeout: 5000,
        });
        return currentPage.locator(selector).first();
    },
    getPasteButton: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        return currentPage.getByTestId("paste-button");
    },
    getOpenButton: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        return currentPage.getByTestId("open-button");
    },
    getValue: async () => {
        const input = await urlEditor.getInput();
        return input.inputValue();
    },
    setValue: async (value: string) => {
        const input = await urlEditor.getInput();
        await input.fill(value);
    },
};

export const dialog = {
    getOKButton: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        return currentPage.getByRole("button", { name: "OK" });
    },
    getCloseButton: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        return currentPage.getByRole("button", { name: /close/i });
    },
    isOKEnabled: async () => {
        const okButton = await dialog.getOKButton();
        const isDisabled = await okButton.getAttribute("disabled");
        return isDisabled === null;
    },
};

export const bookList = {
    getBookCard: async (bookId: string) => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        return currentPage.locator(`[data-book-id="${bookId}"]`);
    },
    selectBook: async (bookId: string) => {
        const card = await bookList.getBookCard(bookId);
        await card.waitFor({ state: "visible", timeout: 5000 });
        await card.click();
    },
    isBookSelected: async (bookId: string) => {
        const card = await bookList.getBookCard(bookId);
        await card.waitFor({ state: "visible", timeout: 5000 });
        return card.evaluate((element) => {
            const target =
                (element as HTMLElement).querySelector(".MuiCard-root") ??
                element;
            const style = window.getComputedStyle(target as HTMLElement);
            return (
                style.outlineStyle !== "none" &&
                style.outlineWidth !== "0px" &&
                style.outlineColor !== "transparent" &&
                style.outlineColor !== "rgba(0, 0, 0, 0)"
            );
        });
    },
    waitForBooksToLoad: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        // Wait for at least one book card to appear
        await currentPage
            .locator("[data-book-id]")
            .first()
            .waitFor({ state: "visible", timeout: 5000 });
    },
};

export const pageList = {
    getPage: async (pageId: string | number) => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        return currentPage.getByTestId(`page-${resolvePageIdentifier(pageId)}`);
    },
    selectPage: async (pageId: string | number) => {
        const page = await pageList.getPage(pageId);
        await page.waitFor({ state: "visible", timeout: 5000 });
        await page.click();
    },
    isPageSelected: async (pageId: string | number) => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }

        const locator = await pageList.getPage(pageId);
        try {
            await locator.waitFor({ state: "visible", timeout: 5000 });
        } catch {
            return false;
        }

        return locator.evaluate((element) => {
            const style = getComputedStyle(element);
            const color = style.outlineColor;
            return (
                color !== "transparent" &&
                color !== "rgba(0, 0, 0, 0)" &&
                color !== "rgb(0, 0, 0)" // defensive fallback if theme changes
            );
        });
    },
    isPageDisabled: async (pageId: string | number) => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }

        const locator = await pageList.getPage(pageId);
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
};

export const errorDisplay = {
    getErrorMessage: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        return currentPage.getByTestId("error-message");
    },
    isVisible: async () => {
        if (!currentPage) {
            throw new Error(
                "Page not initialized. Call setupLinkTargetChooser first.",
            );
        }
        const errorMsg = currentPage.getByTestId("error-message");
        const count = await errorMsg.count();
        if (count === 0) {
            return false;
        }
        return errorMsg.isVisible();
    },
};
