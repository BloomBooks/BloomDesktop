import { css } from "@emotion/react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material";
import * as React from "react";
import { kUiFontStack, lightTheme } from "../bloomMaterialUITheme";
import DefaultBookshelfControl from "../react_components/DefaultBookshelfControl";
import PageNumberStyleControl from "../react_components/pageNumberStyleControl";
import XmatterChooserControl from "../react_components/xmatterChooserControl";
import FontScriptSettingsControl from "./fontScriptSettingsControl";
import {
    bookMakingDividerCss,
    bookMakingPanelCss,
    tabMargins,
} from "./commonTabSettings";

import { WireUpForWinforms } from "../utils/WireUpWinform";

const BookMakingSettingsControl: React.FunctionComponent = () => {
    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div
                    css={css`
                        display: flex;
                        justify-content: space-between;
                        flex-direction: row;
                        margin: ${tabMargins.top} ${tabMargins.side}
                            ${tabMargins.bottom};
                        font-size: 10pt;
                        font-family: ${kUiFontStack};
                    `}
                >
                    {/* Left column: the font/keyboard/page-number controls,
                        grouped in a light rounded panel (design 1A). align-self
                        keeps the panel from stretching to the taller right
                        column's height. */}
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            flex: 2;
                            align-self: flex-start;
                            ${bookMakingPanelCss}
                        `}
                    >
                        <FontScriptSettingsControl />
                        <div css={bookMakingDividerCss} />
                        <PageNumberStyleControl />
                    </div>
                    <div
                        css={css`
                            flex-direction: column;
                            align-items: flex-start;
                            flex: 3;
                            margin-left: ${tabMargins.side};
                        `}
                    >
                        <XmatterChooserControl />
                        <DefaultBookshelfControl />
                    </div>
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

export default BookMakingSettingsControl;

WireUpForWinforms(BookMakingSettingsControl);
