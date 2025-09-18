import { css } from "@emotion/react";
import * as React from "react";
import { ThemeProvider } from "@emotion/react";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import { ToolBox, applyToolboxStateToUpdatedPage } from "../toolbox";
import { BloomSwitch } from "../../../react_components/BloomSwitch";
import { postBoolean } from "../../../utils/bloomApi";

export const ReaderToolSwitch: React.FunctionComponent<{
    isForLeveled: boolean;
}> = (props) => {
    const prefix = props.isForLeveled ? "leveled" : "decodable";

    // The page body will have a copy of the classes from the book's body.
    // So that is our record of whether the book is a reader.
    // Note, we could ask the server, but thankfully we don't need to.
    const checked = ToolBox.getPage()?.classList.contains(`${prefix}-reader`);
    if (checked) {
        document
            .getElementById(prefix + "-reader-tool-content")
            ?.classList.remove("turned-off");
    }

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
                // We're not making a controlled component, but we do want to control the initial state.
                defaultChecked={checked}
                onChange={(_, checked) => {
                    // Toggle the display of the reader tool UI.
                    document
                        .getElementById(prefix + "-reader-tool-content")
                        ?.classList.toggle("turned-off");

                    // Set the class on the page we are currently working with in edit mode.
                    // This just ensures our display is correct while editing. Persisting the value is done below.
                    ToolBox.getPage()?.classList.toggle(`${prefix}-reader`);

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
