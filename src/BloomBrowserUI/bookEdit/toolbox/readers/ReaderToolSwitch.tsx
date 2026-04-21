import { css } from "@emotion/react";
import * as React from "react";
import { ThemeProvider } from "@emotion/react";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import { ToolBox, applyToolboxStateToUpdatedPage } from "../toolbox";
import { BloomSwitch } from "../../../react_components/BloomSwitch";
import { postBoolean } from "../../../utils/bloomApi";
import { isReaderToolEnabledOnCurrentPage } from "./readerToolPageState";

export const ReaderToolSwitch: React.FunctionComponent<{
    isForLeveled: boolean;
}> = (props) => {
    const prefix = props.isForLeveled ? "leveled" : "decodable";

    // The page body will have a copy of the classes from the book's body.
    // So that is our record of whether the book is a reader.
    // Note, we could ask the server, but thankfully we don't need to.
    const [checked, setChecked] = React.useState<boolean>(() =>
        isReaderToolEnabledOnCurrentPage(props.isForLeveled),
    );
    const isFirstContentSync = React.useRef(true);

    // This component must update a non-React toolbox pane element, so we need an effect to keep
    // that external DOM synchronized whenever the controlled switch state changes.
    React.useEffect(() => {
        if (isFirstContentSync.current) {
            isFirstContentSync.current = false;
            return;
        }

        document
            .getElementById(prefix + "-reader-tool-content")
            ?.classList.toggle("turned-off", !checked);
    }, [prefix, checked]);

    return (
        <ThemeProvider theme={toolboxTheme}>
            <BloomSwitch
                size="small"
                css={css`
                    margin-left: 2px; // by experimentation. We have to override the default -11px.
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

                    // If we toggle the reader tool, we need to update the markup.
                    applyToolboxStateToUpdatedPage();

                    // Tell the server to update the body of the actual book.
                    // (Currently nothing automatically updates the classes from the page body back up to the book body,
                    //  and I don't know if adding such an update is safe.)
                    postBoolean(`toolbox/${prefix}`, checked);
                }}
            />
        </ThemeProvider>
    );
};
