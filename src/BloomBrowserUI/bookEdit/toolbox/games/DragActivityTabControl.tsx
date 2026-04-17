import { css } from "@emotion/react";
import { ThemeProvider } from "@mui/material/styles";
import * as React from "react";
import {
    kBloomBlue,
    kDarkestBackground,
    toolboxTheme,
} from "../../../bloomMaterialUITheme";
import { getToolboxBundleExports } from "../../js/workspaceFrames";
import { useL10n } from "../../../react_components/l10nHooks";
import { default as PencilIcon } from "@mui/icons-material/Edit";
import { showGamePromptDialog } from "../games/GameTool";
import BloomButton from "../../../react_components/bloomButton";
import { getCanvasElementManager } from "../canvas/canvasElementUtils";

// This component is responsible for the Game Setup mode tabs in the Game tool.
// Although the code seems to belong in this folder with the other Game code, it is actually
// not part of the toolbox, since its component is part of the editable page iframe.
// Something weird seems to happen if we render an element there using the toolbox copy of
// ReactDOM.Render, so we go to some trouble to make the renderDragActivityTabControl be a function
// that the editable page iframe exports and to call it through getEditablePageBundleExports().
export const DragActivityTabControl: React.FunctionComponent<{
    activeTab: number;
}> = (props) => {
    const changeHandler = (tab: number) => {
        getToolboxBundleExports()?.setActiveDragActivityTab(tab);
        getCanvasElementManager()?.setActiveElement(undefined);
    };
    const setupMode = useL10n(
        "Game Setup mode:",
        "EditTab.Toolbox.DragActivity.SetupMode",
    );
    const startLabel = useL10n("Start", "EditTab.Toolbox.DragActivity.Start");
    const correctLabel = useL10n(
        "Correct",
        "EditTab.Toolbox.DragActivity.Correct",
    );
    const wrongLabel = useL10n("Wrong", "EditTab.Toolbox.DragActivity.Wrong");
    const playLabel = useL10n("Play", "EditTab.Toolbox.DragActivity.Play");

    const prompt = document.getElementsByClassName(
        "bloom-game-prompt",
    )[0] as HTMLElement;
    const promptL10nId =
        prompt?.getAttribute("data-prompt-button-l10nid") ?? null;
    const promptButtonContent = useL10n("", promptL10nId);

    return (
        <ThemeProvider theme={toolboxTheme}>
            <div
                className="above-page-control-typography"
                css={css`
                    width: 100%;
                    display: flex;
                    align-items: center;
                    // The mockup seems to have this a little dimmer than white, but I haven't found an existing constant
                    // that seems appropriate. This will do for a first approximation.
                    color: lightgray;
                    height: 24px;
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
                                    "data-prompt-button-l10nid",
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
                    className="above-page-control-typography"
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
    height: 24px;
    min-width: 32px; // override MUI's 64px
    font-family: Roboto, NotoSans, sans-serif;
    font-size: 9pt;
    font-weight: 400;
    line-height: 16px;
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

// also occurs in less files
export const kIdForDragActivityTabControl = "drag-activity-tab-control";

export const Tabs: React.FunctionComponent<{
    value: number;
    onChange: (newValue: number) => void;
    labels: string[];
    className?: string;
}> = (props) => {
    const changeHandler = (index: number) => {
        props.onChange(index);
    };
    return (
        <div
            css={css`
                display: flex;
                background-color: ${kDarkestBackground};
                align-items: center;
                height: 24px;
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
                            `,
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
