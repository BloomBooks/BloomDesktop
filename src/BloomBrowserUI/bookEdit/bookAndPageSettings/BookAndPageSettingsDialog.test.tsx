import ReactDOM from "react-dom";
import {
    ConfigrArea,
    ConfigrPane,
    ConfigrPage,
    ConfigrStatic,
} from "@sillsdev/config-r";
import { afterEach, describe, expect, it } from "vitest";

let renderedContainer: HTMLDivElement | undefined;

function renderSettingsPane(
    initiallySelectedTopLevelPageKey: string,
): HTMLDivElement {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;

    ReactDOM.render(
        <ConfigrPane
            label="Settings"
            initialValues={{}}
            showAppBar={false}
            showJson={false}
            initiallySelectedTopLevelPageKey={initiallySelectedTopLevelPageKey}
        >
            <ConfigrArea key="bookArea" label="Book" pageKey="bookArea">
                <ConfigrPage
                    key="themeAndLayout"
                    label="Theme & Layout"
                    pageKey="themeAndLayout"
                >
                    {/* Test-only placeholder body; this spec only cares which tab ConfigrPane selects. */}
                    <ConfigrStatic>
                        <div>Theme &amp; Layout test content</div>
                    </ConfigrStatic>
                </ConfigrPage>
                <ConfigrPage key="cover" label="Cover" pageKey="cover">
                    <ConfigrStatic>
                        <div>Cover test content</div>
                    </ConfigrStatic>
                </ConfigrPage>
                <ConfigrPage
                    key="normalTextBoxLanguages"
                    label="Languages"
                    pageKey="normalTextBoxLanguages"
                >
                    <ConfigrStatic>
                        <div>Languages test content</div>
                    </ConfigrStatic>
                </ConfigrPage>
            </ConfigrArea>
            <ConfigrArea key="pageArea" label="Current Page" pageKey="pageArea">
                <ConfigrPage key="colors" label="Colors" pageKey="colors">
                    <ConfigrStatic>
                        <div>Colors test content</div>
                    </ConfigrStatic>
                </ConfigrPage>
            </ConfigrArea>
        </ConfigrPane>,
        container,
    );

    return container;
}

function getSelectedTabLabels(): string[] {
    return Array.from(
        document.querySelectorAll('[role="tab"][aria-selected="true"]'),
    ).map((tab) => tab.textContent ?? "");
}

afterEach(() => {
    if (renderedContainer) {
        ReactDOM.unmountComponentAtNode(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    document.body.innerHTML = "";
});

describe("ConfigrPane nested initial selection", () => {
    it("defaults to Theme & Layout when no page key is provided", () => {
        renderSettingsPane(undefined as unknown as string);

        expect(getSelectedTabLabels()).toContain("Theme & Layout");
    });

    it.each([
        ["cover", "Cover"],
        ["themeAndLayout", "Theme & Layout"],
        ["normalTextBoxLanguages", "Languages"],
        ["colors", "Colors"],
    ])(
        "shows nested page %s when passed as initiallySelectedTopLevelPageKey",
        (pageKey, expectedLabel) => {
            renderSettingsPane(pageKey);

            expect(getSelectedTabLabels()).toContain(expectedLabel);
        },
    );
});
