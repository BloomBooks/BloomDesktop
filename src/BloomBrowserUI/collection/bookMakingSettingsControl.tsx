import { css } from "@emotion/react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material";
import * as React from "react";
import { kUiFontStack, lightTheme } from "../bloomMaterialUITheme";
import DefaultBookshelfControl from "../react_components/DefaultBookshelfControl";
import PageNumberStyleControl from "../react_components/pageNumberStyleControl";
import XmatterChooserControl from "../react_components/xmatterChooserControl";
import FontScriptSettingsControl from "./fontScriptSettingsControl";
import { tabMargins } from "./commonTabSettings";

import { WireUpForWinforms } from "../utils/WireUpWinform";
import BlorgLanguageQrCodeControl from "./BlorgLanguageQrCodeControl";

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
                    <div
                        css={css`
                            flex-direction: column;
                            flex: 2;
                        `}
                    >
                        <FontScriptSettingsControl />
                        <PageNumberStyleControl />
                        <BlorgLanguageQrCodeControl />
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
