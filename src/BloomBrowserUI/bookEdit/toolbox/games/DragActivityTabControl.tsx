import { css } from "@emotion/react";
import { ThemeProvider } from "@mui/material/styles";
import * as React from "react";
import {
    kBloomBlue,
    kDarkestBackground,
    toolboxTheme
} from "../../../bloomMaterialUITheme";
import * as ReactDOM from "react-dom";
import { getToolboxBundleExports } from "../../js/bloomFrames";
import { useL10n } from "../../../react_components/l10nHooks";
import { default as PencilIcon } from "@mui/icons-material/Edit";
import { showGamePromptDialog } from "../games/GameTool";
import BloomButton from "../../../react_components/bloomButton";
import { getCanvasElementManager } from "../overlay/canvasElementUtils";

// This component is responsible for the Game Setup mode tabs in the Game tool.
// Although the code seems to belong in this folder with the other Game code, it is actually
// not part of the toolbox, since its component is part of the editable page iframe.
// Something weird seems to happen if we render an element there using the toolbox copy of
// ReactDOM.Render, so we go to some trouble to make the renderDragActivityTabControl be a function
// that the editable page iframe exports and to call it through getEditablePageBundleExports().
export const DragActivityTabControl: React.FunctionComponent<{
    activeTab: number;
}> = props => {
    const changeHandler = (tab: number) => {
        getToolboxBundleExports()?.setActiveDragActivityTab(tab);
        getCanvasElementManager()?.setActiveElement(undefined);
    };
    const setupMode = useL10n(
        "Game Setup mode:",
        "EditTab.Toolbox.DragActivity.SetupMode"
    );
    const startLabel = useL10n("Start", "EditTab.Toolbox.DragActivity.Start");
    const correctLabel = useL10n(
        "Correct",
        "EditTab.Toolbox.DragActivity.Correct"
    );
    const wrongLabel = useL10n("Wrong", "EditTab.Toolbox.DragActivity.Wrong");
    const playLabel = useL10n("Play", "EditTab.Toolbox.DragActivity.Play");

    const prompt = document.getElementsByClassName(
        "bloom-game-prompt"
    )[0] as HTMLElement;
    const promptL10nId =
        prompt?.getAttribute("data-prompt-button-l10nid") ?? null;
    const promptButtonContent = useL10n("", promptL10nId);

    return (
        <ThemeProvider theme={toolboxTheme}>
            <div
                css={css`
                    display: flex;
                    align-items: baseline;
                    // The mockup seems to have this a little dimmer than white, but I haven't found an existing constant
                    // that seems appropriate. This will do for a first approximation.
                    color: lightgray;
                    font-family: ${toolboxTheme.typography.fontFamily};
                    font-size: ${toolboxTheme.typography.fontSize}px;
                    margin-top: 2px;
                    padding: 8px 0px;
                `}
            >
                {promptButtonContent && (
                    <div>
                        {/* // This button is only visible in start mode. I'd prefer
                        to control that here but it's difficult. visibility
                        is controlled with #promptButton rules in editMode.less. */}
                        <BloomButton
                            id="promptButton"
                            onClick={() => showGamePromptDialog(false)}
                            enabled={true}
                            l10nKey={
                                prompt?.getAttribute(
                                    "data-prompt-button-l10nid"
                                ) || ""
                            }
                            iconBeforeText={<PencilIcon />}
                            css={buttonCss}
                        />
                    </div>
                )}
                <div
                    // right-aligns what comes after it.
                    css={css`
                        flex-grow: 1;
                    `}
                ></div>
                <div
                    css={css`
                        margin-right: 20px;
                    `}
                >
                    {setupMode}
                </div>
                <Tabs
                    value={props.activeTab}
                    onChange={changeHandler}
                    labels={[startLabel, correctLabel, wrongLabel, playLabel]}
                />
            </div>
        </ThemeProvider>
    );
};

const buttonCss = css`
    color: white;
    width: auto; // override MUI's 100%
    min-width: 32px; // override MUI's 64px
    font-weight: 400;
    padding: 0px 6px;
    & .MuiButton-startIcon {
        top: -1px;
        margin-right: 3px;
        margin-left: 1px;
        color: white;
        svg {
            font-size: 0.85rem;
        }
    }
`;

// This is the function that the editable page iframe exports so that the toolbox can call it
// to render the Start/Correct/Wrong/Play control.
// This deliberately does NOT use the cross-iframe getPage() function, because it MUST be
// called in a way that has it executing in the right context, where document refers to the
// page iframe document. The toolbox must call it through getEditablePageBundleExports().
// This is because ReactDOM.render seems to have trouble if we pass it an element that
// belongs to a different document.
export function renderDragActivityTabControl(currentTab: number) {
    const root = document.getElementById("drag-activity-tab-control");
    if (!root) {
        // not created yet, try later
        setTimeout(() => renderDragActivityTabControl(currentTab), 200);
        return;
    }
    ReactDOM.render(<DragActivityTabControl activeTab={currentTab} />, root);
}

export const Tabs: React.FunctionComponent<{
    value: number;
    onChange: (newValue: number) => void;
    labels: string[];
    className?: string;
}> = props => {
    const changeHandler = (index: number) => {
        props.onChange(index);
    };
    return (
        <div
            css={css`
                display: flex;
                background-color: ${kDarkestBackground};
            `}
            className={props.className}
        >
            {props.labels.map((label, index) => {
                const selected = index === props.value;
                return (
                    <BloomButton
                        key={label}
                        onClick={() => changeHandler(index)}
                        css={[
                            buttonCss,
                            css`
                                color: ${selected ? "white" : "lightgray"};
                                background-color: ${selected
                                    ? kBloomBlue
                                    : kDarkestBackground};
                                margin-left: 4px;
                            `
                        ]}
                        enabled={true}
                        l10nKey=""
                        alreadyLocalized={true}
                    >
                        {label}
                    </BloomButton>
                );
            })}
        </div>
    );
};

// return true if the element is part of the content of a Game
// that is currently being played
export function playingBloomGame(element: HTMLElement): boolean {
    return !!element.closest(".drag-activity-play");
}
