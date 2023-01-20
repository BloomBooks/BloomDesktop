/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import { ThemeProvider } from "@material-ui/core";
import * as React from "react";
import { lightTheme } from "../bloomMaterialUITheme";
import DefaultBookshelfControl from "../react_components/DefaultBookshelfControl";
import PageNumberStyleControl from "../react_components/pageNumberStyleControl";
import XmatterChooserControl from "../react_components/xmatterChooserControl";
import FontScriptSettingsControl from "./fontScriptSettingsControl";
import { tabMargins } from "./commonTabSettings";

import { WireUpForWinforms } from "../utils/WireUpWinform";

const BookMakingSettingsControl: React.FunctionComponent = () => {
    return (
        <ThemeProvider theme={lightTheme}>
            <div
                css={css`
                    display: flex;
                    justify-content: space-between;
                    flex-direction: row;
                    margin: ${tabMargins.top} ${tabMargins.side}
                        ${tabMargins.bottom};
                    font-size: 10pt;
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
    );
};

export default BookMakingSettingsControl;

WireUpForWinforms(BookMakingSettingsControl);
