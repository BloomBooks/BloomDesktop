import { css } from "@emotion/react";
import * as React from "react";
import { ThemeProvider } from "@emotion/react";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import { ToolBox, applyToolboxStateToUpdatedPage } from "../toolbox";
import { BloomSwitch } from "../../../react_components/BloomSwitch";
import { postBoolean } from "../../../utils/bloomApi";
import {
    isReaderToolEnabledOnCurrentPage,
    updateReaderNumbersOnCurrentPage,
} from "./readerToolPageState";
import { getTheOneReaderToolsModel } from "./readerToolsModel";

export const ReaderToolSwitch: React.FunctionComponent<{
    isForLeveled: boolean;
    changeDisplayFunc?: () => void;
}> = (props) => {
    const prefix = props.isForLeveled ? "leveled" : "decodable";

    // The page body will have a copy of the classes from the book's body.
    // So that is our record of whether the book is a reader.
    // Note, we could ask the server, but thankfully we don't need to.
    const [checked, setChecked] = React.useState<boolean>(() =>
        isReaderToolEnabledOnCurrentPage(props.isForLeveled),
    );
    return (
        <ThemeProvider theme={toolboxTheme}>
            <BloomSwitch
                size="small"
                css={css`
                    margin-left: 2px; // by experimentation. We have to override the default -11px.
                    // Uppercase the label to match the uppercase button labels in
                    // the reader tool panels (Set Up, Copy Book Stats, etc.).
                    // (BL-16585)
                    text-transform: uppercase;
                `}
                l10nKey={
                    props.isForLeveled
                        ? "EditTab.Toolbox.LeveledReaderTool.BookIsNotLeveled"
                        : "EditTab.Toolbox.DecodableReaderTool.BookIsNotDecodable"
                }
                l10nKeyWhenChecked={
                    props.isForLeveled
                        ? "EditTab.Toolbox.LeveledReaderTool.BookIsLeveled"
                        : "EditTab.Toolbox.DecodableReaderTool.BookIsDecodable"
                }
                // Keep this controlled so rerenders can sync state without forcing an unmount/remount.
                checked={checked}
                onChange={(_, checked) => {
                    setChecked(checked);

                    // Set the class on the page we are currently working with in edit mode.
                    // This just ensures our display is correct while editing. Persisting the value is done below.
                    ToolBox.getPage()?.classList.toggle(
                        `${prefix}-reader`,
                        checked,
                    );

                    // The class alone is not enough: a branding stylesheet reads the level and
                    // the stage from the page body, so those have to follow the switch.
                    const model = getTheOneReaderToolsModel();
                    updateReaderNumbersOnCurrentPage(
                        model.levelNumber,
                        model.stageNumber,
                    );

                    // If we toggle the reader tool, we need to update the markup.
                    applyToolboxStateToUpdatedPage();

                    // Tell the server to update the body of the actual book.
                    // (Currently nothing automatically updates the classes from the page body back up to the book body,
                    //  and I don't know if adding such an update is safe.)
                    postBoolean(`toolbox/${prefix}`, checked);

                    if (props.changeDisplayFunc !== undefined) {
                        props.changeDisplayFunc();
                    }
                }}
            />
        </ThemeProvider>
    );
};
