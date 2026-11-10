import { css } from "@emotion/react";
import * as React from "react";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { useL10n } from "../../react_components/l10nHooks";
import { kBloomBlue } from "../../bloomMaterialUITheme";

// The booklet print settings, shown in a small window of their own (PublishPdfApi opens it)
// while the browser's print dialog is open. The print dialog covers everything in the
// page's own WebView2, so these instructions can't be shown there.
export const PrintInstructionsWindow: React.FunctionComponent<{
    // The localized settings, one per line. Missing for a paper size not recommended for booklets.
    helps?: string[];
    note?: string;
}> = (props) => {
    const helpHeader = useL10n(
        "Here are the settings you need for printing this booklet:",
        "PublishTab.PDF.Booklet.HelpHeader",
    );
    return (
        <div
            className="print-instructions"
            css={css`
                box-sizing: border-box;
                height: 100%;
                background-color: ${kBloomBlue};
                color: white;
                font-family: "Segoe UI", sans-serif;
                font-size: 14px;
                padding: 8px 15px;
                // PublishPdfApi sizes the window to fit the text we have; a longer
                // translation can still be scrolled.
                overflow-y: auto;
            `}
        >
            <div
                css={css`
                    font-weight: 600;
                    display: flex;
                    align-items: center;
                    margin-bottom: 8px;
                `}
            >
                <InfoOutlinedIcon
                    css={css`
                        padding-right: 5px;
                    `}
                />
                {helpHeader}
            </div>
            <div
                css={css`
                    padding-left: 5px;
                `}
            >
                {props.helps?.map((help, index) => (
                    <p
                        key={index}
                        css={css`
                            margin-block-start: 0px;
                            margin-block-end: 2px;
                        `}
                    >
                        {help}
                    </p>
                ))}
                {props.note && (
                    <p
                        css={css`
                            margin-block-start: 6px;
                            margin-block-end: 2px;
                        `}
                    >
                        {props.note}
                    </p>
                )}
            </div>
        </div>
    );
};

WireUpForWinforms(PrintInstructionsWindow, kBloomBlue);
