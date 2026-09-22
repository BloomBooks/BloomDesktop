import { css } from "@emotion/react";
import { Chip } from "@mui/material";
import * as React from "react";
import { Span } from "../../../../react_components/l10nComponents";
import { kBloomBlue } from "../../../../utils/colorUtils";

/**
 * The right-hand panel of the Decodable Stages tab: how many words the selected stage can
 * decode, and which ones.
 *
 * Unlike the rest of the dialog this is not a view of the settings being edited. The words come
 * from the toolbox frame's Synphony data -- see how StagesTab builds them -- so this is the one
 * panel that shows what Bloom itself knows rather than what the dialog is holding.
 */
export const MatchingWordsPanel: React.FunctionComponent<{
    matchingWords: string[];
    fontName: string;
}> = (props) => (
    <div
        css={css`
            display: flex;
            flex-direction: column;
            min-width: 0;
            min-height: 0;
            background: #fafafa;
            border-left: 1px solid #e5e5e5;
        `}
    >
        <strong
            css={css`
                flex: 0 0 auto;
                padding: 22px 22px 0;
            `}
        >
            <span
                css={css`
                    color: ${kBloomBlue};
                `}
            >
                {props.matchingWords.length}{" "}
            </span>
            <Span l10nKey="ReaderSetup.MatchingWords">matching words</Span>
        </strong>
        <div
            css={css`
                flex: 1 1 auto;
                min-height: 0;
                overflow: auto;
                margin-top: 15px;
            `}
        >
            <div
                css={css`
                    display: flex;
                    flex-wrap: wrap;
                    align-content: flex-start;
                    gap: 8px;
                    min-width: 100%;
                    min-height: 100%;
                    box-sizing: border-box;
                    padding: 0 22px 22px;
                `}
            >
                {props.matchingWords.map((word) => (
                    <Chip
                        key={word}
                        label={word}
                        css={css`
                            height: auto;
                            padding: 4px 10px;
                            border-radius: 16px;
                            background: #f1f3f4;
                            color: #4a4a4a;
                            font-family: ${props.fontName};
                            font-size: 11pt;
                            .MuiChip-label {
                                padding: 0;
                            }
                        `}
                    />
                ))}
            </div>
        </div>
    </div>
);
