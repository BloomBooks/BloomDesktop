import { Page } from "../../component-tester/playwrightTest";
import { prepareGetResponse } from "../../component-tester/apiInterceptors";
import { setTestComponent } from "../../component-tester/setTestComponent";
import { LinkTargetInfo } from "../LinkTargetChooser";

const mockBooks = [
    {
        id: "book1",
        title: "First Book",
        folderName: "First Book",
        thumbnail:
            "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%234CAF50'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 1%3C/text%3E%3C/svg%3E",
        pageLength: 5,
    },
    {
        id: "book2",
        title: "Second Book",
        folderName: "Second Book",
        thumbnail:
            "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%232196F3'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 2%3C/text%3E%3C/svg%3E",
        pageLength: 5,
    },
    {
        id: "book3",
        title: "Third Book",
        folderName: "Third Book",
        thumbnail:
            "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%23FF9800'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 3%3C/text%3E%3C/svg%3E",
        pageLength: 5,
    },
    {
        id: "book4",
        title: "Fourth Book",
        folderName: "Fourth Book",
        thumbnail:
            "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%23E91E63'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 4%3C/text%3E%3C/svg%3E",
        pageLength: 5,
    },
];

const booksApiPattern =
    /\/bloom\/api\/+collections\/books\?realTitle=(true|false)/;

let currentPage: Page | undefined;

export async function setupLinkTargetChooser(
    page: Page,
    props: {
        currentURL?: string;
        onClose?: () => void;
        onSelect?: (info: LinkTargetInfo) => void;
    } = {},
) {
    currentPage = page;

    prepareGetResponse(page, booksApiPattern, mockBooks);

    // Intercept page list APIs used by the PageList component
    const pages = [
        { key: "cover", caption: "Cover" },
        { key: "1", caption: "Page 1" },
        { key: "2", caption: "Page 2" },
    ];
    prepareGetResponse(page, "**/bloom/api/pageList/pages", {
        pages,
        selectedPageId: "cover",
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
        return pageId <= 0 ? "cover" : pageId.toString();
    }
    if (pageId === "p-cover") {
        return "cover";
    }
    if (pageId.startsWith("p-")) {
        return pageId.substring(2);
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
            state: "visible",
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
            const outlineStyle = getComputedStyle(element).outlineStyle;
            return outlineStyle !== "none";
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
