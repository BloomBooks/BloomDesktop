import { css } from "@emotion/react";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { post } from "../../utils/bloomApi";
import { useL10n } from "../../react_components/l10nHooks";
import { CustomPageLayoutMenu } from "../toolbox/canvas/customPageLayoutMenu";
import { CogIcon } from "./CogIcon";

interface IAbovePageControlsState {
    isGamePage: boolean;
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
    isGamePage: false,
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

    const container = document.getElementsByClassName(
        "above-page-control-container",
    )[0] as HTMLElement | undefined;
    if (container) {
        ReactDOM.unmountComponentAtNode(container);
        container.replaceChildren();
    }
}

function renderAbovePageControls(): void {
    const page = document.getElementsByClassName("bloom-page")[0] as
        | HTMLElement
        | undefined;
    if (!page) {
        return;
    }

    const container = getOrCreateAbovePageControlContainer(page);
    if (!container) {
        return;
    }

    ReactDOM.render(
        <AbovePageControls
            isGamePage={currentState.isGamePage}
            showChangeLayoutModeToggle={currentState.showChangeLayoutModeToggle}
            isChangeLayoutMode={currentState.isChangeLayoutMode}
            onChangeLayoutModeToggle={currentState.onChangeLayoutModeToggle}
            showPageLayoutMenu={currentState.showPageLayoutMenu}
            isCustomPageLayout={currentState.isCustomPageLayout}
            disableCustomPage={currentState.disableCustomPage}
            onSetCustom={currentState.onSetCustom}
        />,
        container,
    );
}

function getOrCreateAbovePageControlContainer(
    page: HTMLElement,
): HTMLElement | undefined {
    let container = document.getElementsByClassName(
        "above-page-control-container",
    )[0] as HTMLElement | undefined;

    if (!container) {
        container = document.createElement("div");
        container.classList.add("above-page-control-container");
        container.classList.add("bloom-ui");

        const pageScalingContainer = document.getElementById(
            "page-scaling-container",
        );
        if (pageScalingContainer) {
            pageScalingContainer.prepend(container);
        } else {
            page.parentElement?.insertBefore(
                container,
                page.parentElement.firstChild,
            );
        }
    }

    container.style.maxWidth = page.clientWidth + "px";
    return container;
}

const AbovePageControls: React.FunctionComponent<IAbovePageControlsState> = (
    props,
) => {
    if (props.isGamePage) {
        return <PageSettingsButton />;
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
                <PageSettingsButton />
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

const PageSettingsButton: React.FunctionComponent = () => {
    const label = useL10n("Page Settings", "PageSettings.Title");
    const title = useL10n("Open Page Settings...", "PageSettings.OpenTooltip");

    return (
        <button
            className="page-settings-button above-page-control-typography"
            title={title}
            onClick={() => post("editView/showPageSettingsDialog")}
        >
            <CogIcon
                className="page-settings-button-icon"
                aria-hidden="true"
                size={20}
            />
            <span className="page-settings-button-label">{label}</span>
        </button>
    );
};

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
                    id="myonoffswitch"
                    checked={props.isChecked}
                    onChange={() => props.onChange?.()}
                />
                <label className="onoffswitch-label" htmlFor="myonoffswitch">
                    <span className="onoffswitch-inner"></span>
                    <span className="onoffswitch-switch"></span>
                </label>
            </div>
        </div>
    );
};
