import { jsx, css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import {
    kBloomBlue,
    kDarkestBackground,
    kUiFontStack,
    toolboxTheme
} from "../../../bloomMaterialUITheme";
import ReactDOM = require("react-dom");
import { getToolboxBundleExports } from "../../js/bloomFrames";
import { useL10n } from "../../../react_components/l10nHooks";

// This component is responsible for the Game Setup mode tabs in the Drag Activity tool.
// Although the code seems to belong in this folder with the other drag activity code, it is actually
// not part of the toolbox, since its component is part of the editable page iframe.
// Something weird seems to happen if we render an element there using the toolbox copy of
// ReactDOM.Render, so we go to some trouble to make the renderDragActivityTabControl be a function
// that the editable page iframe exports and to call it through getEditablePageBundleExports().
export const DragActivityTabControl: React.FunctionComponent<{
    activeTab: number;
}> = props => {
    const changeHandler = (tab: number) => {
        getToolboxBundleExports()?.setActiveDragActivityTab(tab);
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
    return (
        <ThemeProvider theme={toolboxTheme}>
            <div
                css={css`
                    display: flex;
                    // The mockup seems to have this a little dimmer than white, but I haven't found an existing constant
                    // that seems appropriate. This will do for a first approximation.
                    color: lightgray;
                `}
            >
                <div
                    css={css`
                        margin-top: 8px;
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
    classNane?: string;
}> = props => {
    const changeHandler = (index: number) => {
        props.onChange(index);
    };
    return (
        <div
            css={css`
                display: flex;
                background-color: ${kDarkestBackground};
                padding: 7px 8px 7px 0px;
            `}
            className={props.classNane}
        >
            {props.labels.map((child, index) => {
                const selected = index === props.value;
                return (
                    <button
                        key={child}
                        onClick={() => changeHandler(index)}
                        css={css`
                            font-family: ${kUiFontStack};
                            color: ${selected ? "white" : "lightgray"};
                            background-color: ${selected
                                ? kBloomBlue
                                : kDarkestBackground};
                            border: none;
                            padding: 2px 6px;
                            margin-left: 4px;
                            border-radius: 3px;
                        `}
                    >
                        {child}
                    </button>
                );
            })}
        </div>
    );
};
