import * as React from "react";
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
                <ConfigrPage key="cover" label="Cover" pageKey="cover">
                    <ConfigrStatic>
                        <div>Cover content</div>
                    </ConfigrStatic>
                </ConfigrPage>
                <ConfigrPage
                    key="contentPages"
                    label="Content Pages"
                    pageKey="contentPages"
                >
                    <ConfigrStatic>
                        <div>Content pages content</div>
                    </ConfigrStatic>
                </ConfigrPage>
            </ConfigrArea>
            <ConfigrArea key="pageArea" label="Current Page" pageKey="pageArea">
                <ConfigrPage key="colors" label="Colors" pageKey="colors">
                    <ConfigrStatic>
                        <div>Colors content</div>
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
    it.each([
        ["cover", "Cover"],
        ["contentPages", "Content Pages"],
        ["colors", "Colors"],
    ])(
        "shows nested page %s when passed as initiallySelectedTopLevelPageKey",
        (pageKey, expectedLabel) => {
            renderSettingsPane(pageKey);

            expect(getSelectedTabLabels()).toContain(expectedLabel);
        },
    );
});
