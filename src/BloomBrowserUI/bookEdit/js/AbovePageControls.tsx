import { css } from "@emotion/react";
import * as React from "react";
import { renderRoot, unmountRoot } from "../../utils/reactRender";
import { useL10n } from "../../react_components/l10nHooks";
import { CustomPageLayoutMenu } from "../toolbox/canvas/customPageLayoutMenu";
import {
    DragActivityTabControl,
    kIdForDragActivityTabControl,
} from "../toolbox/games/DragActivityTabControl";
import { CogIcon } from "./CogIcon";
import { tryGetWorkspaceBundleExports } from "./workspaceFrames";

interface IAbovePageControlsState {
    isDragGamePage: boolean;
    activeGameTab: number;
    showChangeLayoutModeToggle: boolean;
    isChangeLayoutMode: boolean;
    onChangeLayoutModeToggle?: () => void;
    showPageLayoutMenu: boolean;
    isCustomPageLayout: boolean;
    disableCustomPage?: boolean;
    onSetCustom?: (
        value: "standard" | "custom",
        keepCustomLayoutDataWhenSwitchingToStandard: boolean,
    ) => void;
}

const defaultState: IAbovePageControlsState = {
    isDragGamePage: false,
    activeGameTab: 0,
    showChangeLayoutModeToggle: false,
    isChangeLayoutMode: false,
    showPageLayoutMenu: false,
    isCustomPageLayout: false,
};

let currentState: IAbovePageControlsState = defaultState;

export function updateAbovePageControls(
    stateUpdate: Partial<IAbovePageControlsState>,
): void {
    currentState = {
        ...currentState,
        ...stateUpdate,
    };

    renderAbovePageControls();
}

export function resetAbovePageControls(): void {
    currentState = defaultState;
    // Off-screen (e.g. process-book) there is no workspace frame, so this is a no-op there.
    // (Reached via pageUnloading() in bloomEditing.ts.)
    tryGetWorkspaceBundleExports()?.setToolboxEnabled(true);

    const container = document.getElementsByClassName(
        "above-page-control-container",
    )[0] as HTMLElement | undefined;
    if (container) {
        unmountRoot(container);
        container.replaceChildren();
    }
}

export function renderDragActivityTabControl(currentTab: number): void {
    currentState = {
        ...currentState,
        activeGameTab: currentTab,
    };

    if (currentState.isDragGamePage) {
        renderAbovePageControls();
    }
}

// Only the live Edit tab's page shows the controls. The same editing code also loads pages
// off-screen to process them, and the setup that runs on every page load asks for the controls
// there too, where nobody would see them.
function renderAbovePageControls(): void {
    if (!isLiveEditTabPage()) {
        return;
    }
    renderRoot(
        <AbovePageControls
            isDragGamePage={currentState.isDragGamePage}
            activeGameTab={currentState.activeGameTab}
            showChangeLayoutModeToggle={currentState.showChangeLayoutModeToggle}
            isChangeLayoutMode={currentState.isChangeLayoutMode}
            onChangeLayoutModeToggle={currentState.onChangeLayoutModeToggle}
            showPageLayoutMenu={currentState.showPageLayoutMenu}
            isCustomPageLayout={currentState.isCustomPageLayout}
            disableCustomPage={currentState.disableCustomPage}
            onSetCustom={currentState.onSetCustom}
        />,
        getOrCreateAbovePageControlContainer(),
    );
}

// True in the page that the Edit tab shows the user, and false in pages loaded off-screen to
// process them.
export function isLiveEditTabPage(): boolean {
    return window.frameElement?.id === "page";
}

// The element that holds the controls shown above the page: the bar fixed across the top of the
// page frame (see makeControlsBar()), made the first time it is needed.
function getOrCreateAbovePageControlContainer(): HTMLElement {
    let container = document.getElementsByClassName(
        "above-page-control-container",
    )[0] as HTMLElement | undefined;

    if (!container) {
        container = document.createElement("div");
        container.classList.add("above-page-control-container");
        container.classList.add("bloom-ui");
        makeControlsBar(container);
    }
    return container;
}

// Marks the container of the controls shown above the page as the bar fixed across the top of the
// page frame. pageRemovalMark.ts and the e2e tests find the bar by it.
const kControlsBarClass = "bloom-controls-bar";

// In the live Edit tab's page, the controls Bloom shows above the page live in a bar fixed across
// the top of the page frame, so they are in the same place whichever page is being edited, and
// whether or not the other pages are showing (see bookGridView.ts). The bar covers the space Bloom
// already leaves above the page (#labelAndLayoutPane), so the page is no further down than it would
// be without it. These rules go in with the bar, rather than in origamiEditing.less, because the
// page adds that stylesheet's link itself and it may not have loaded yet when the bar is made.
const kControlsBarStyles = `
body > .above-page-control-container.${kControlsBarClass} {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    height: var(--bloom-controls-bar-height);
    padding: 0 16px;
    box-sizing: border-box;
    z-index: 1000;
    background-color: var(--bloom-controls-bar-background);
}
`;

// Make the container into the bar fixed across the top of the page frame (see kControlsBarStyles),
// and put it in the body.
function makeControlsBar(container: HTMLElement): void {
    container.classList.add(kControlsBarClass);
    const style = document.createElement("style");
    style.textContent = kControlsBarStyles;
    document.head.appendChild(style);
    document.body.style.setProperty(
        "--bloom-controls-bar-background",
        getComputedStyle(document.body).backgroundColor,
    );
    document.body.style.setProperty(
        "--bloom-controls-bar-height",
        `${document.getElementById("labelAndLayoutPane")!.offsetHeight}px`,
    );
    document.body.appendChild(container);
}

const AbovePageControls: React.FunctionComponent<IAbovePageControlsState> = (
    props,
) => {
    if (props.isDragGamePage) {
        return (
            <div
                css={css`
                    width: 100%;
                    display: flex;
                    align-items: center;
                    gap: 12px;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        align-items: center;
                    `}
                >
                    {/* <PageSettingsButton /> */}
                </div>
                <div
                    id={kIdForDragActivityTabControl}
                    css={css`
                        flex: 1 1 auto;
                        min-width: 0;
                    `}
                >
                    <DragActivityTabControl activeTab={props.activeGameTab} />
                </div>
            </div>
        );
    }

    return (
        <div
            css={css`
                width: 100%;
                display: flex;
                justify-content: space-between;
                align-items: center;
            `}
        >
            <div
                css={css`
                    display: flex;
                    align-items: center;
                `}
            >
                {/* <PageSettingsButton /> */}
            </div>
            <div
                css={css`
                    display: flex;
                    align-items: center;
                    gap: 12px;
                `}
            >
                {props.showChangeLayoutModeToggle && (
                    <ChangeLayoutModeToggle
                        isChecked={props.isChangeLayoutMode}
                        onChange={props.onChangeLayoutModeToggle}
                    />
                )}
                {props.showPageLayoutMenu && props.onSetCustom && (
                    <CustomPageLayoutMenu
                        isCustom={props.isCustomPageLayout}
                        disableCustomPage={props.disableCustomPage}
                        setCustom={props.onSetCustom}
                    />
                )}
            </div>
        </div>
    );
};

// const PageSettingsButton: React.FunctionComponent = () => {
//     const label = useL10n("Page Settings", "PageSettings.Title");
//     const title = useL10n("Open Page Settings...", "PageSettings.OpenTooltip");

//     return (
//         <button
//             className="page-settings-button above-page-control-typography"
//             title={title}
//             onClick={() =>
//                 getWorkspaceBundleExports().showBookSettingsDialog("colors")
//             }
//         >
//             <CogIcon
//                 className="page-settings-button-icon"
//                 aria-hidden="true"
//                 size={20}
//             />
//             <span className="page-settings-button-label">{label}</span>
//         </button>
//     );
// };

const ChangeLayoutModeToggle: React.FunctionComponent<{
    isChecked: boolean;
    onChange?: () => void;
}> = (props) => {
    const label = useL10n("Change Layout", "EditTab.CustomPage.ChangeLayout");

    return (
        <div className="change-layout-mode-toggle above-page-control-typography bloom-ui">
            <div>{label}</div>
            <div className="onoffswitch">
                <input
                    type="checkbox"
                    name="onoffswitch"
                    className="onoffswitch-checkbox"
                    id="changeLayoutToggle"
                    checked={props.isChecked}
                    onChange={() => props.onChange?.()}
                />
                <label
                    className="onoffswitch-label"
                    htmlFor="changeLayoutToggle"
                >
                    <span className="onoffswitch-inner"></span>
                    <span className="onoffswitch-switch"></span>
                </label>
            </div>
        </div>
    );
};
