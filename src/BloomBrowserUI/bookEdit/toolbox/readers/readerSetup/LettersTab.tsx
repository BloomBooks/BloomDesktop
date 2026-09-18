import { css } from "@emotion/react";
import * as React from "react";
import { useL10n } from "../../../../react_components/l10nHooks";
import { Div, Span } from "../../../../react_components/l10nComponents";
import {
    ReaderSettings,
} from "../ReaderSettings";
import { ReaderDialogTextarea } from "./ReaderDialogTextarea";
import {
    commonHeaderStyles,
    commonTextStyles,
    updateSettings,
} from "./readerDialogShared";

export const LettersTab: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: (value: ReaderSettings) => void;
    fontName: string;
}> = (props) => {
    const updateLetters = (value: string) => {
        updateSettings(props, (updatedSettings) => {
            updatedSettings.letters = value;
        });
    };
    const lettersBoxLabel = useL10n(
        "Letters and Letter Combinations",
        "ReaderSetup.Letters.Header",
    );
    return (
        <div
            css={css`
                grid-column: 1 / -1;
                min-width: 0;
                padding: 22px;
                box-sizing: border-box;
            `}
        >
            <Div
                l10nKey="ReaderSetup.Letters.Header"
                css={css`
                    margin-bottom: 7px;
                    ${commonHeaderStyles}
                `}
            >
                Letters and Letter Combinations
            </Div>
            <ReaderDialogTextarea
                updateSettings={updateLetters}
                value={props.settings.letters}
                ariaLabel={lettersBoxLabel}
                testId="reader-setup-letters-box"
                extraStyles={css`
                    display: block;
                    width: 325px;
                    height: 55px;
                    font-family: ${props.fontName};
                `}
            />
            <Div
                css={css`
                    margin-top: 7px;
                    margin-bottom: 24px;
                    ${commonTextStyles}
                `}
                l10nKey="ReaderSetup.Letters.Intro"
            >
                To help you make decodable readers, Bloom needs to know the
                letters and letter combinations that you will be teaching.
            </Div>
            <Div
                l10nKey="ReaderSetup.Letters.LetterHelp1"
                css={css`
                    max-width: 720px;
                    margin-bottom: 4px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                Separate each letter or letter combination with a space. For
                example, here is what we might use for the English language:
            </Div>
            <div
                css={css`
                    max-width: 720px;
                    margin-bottom: 24px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                a b c ch d e f g h i j k l m n ng o p q r s sh t th u v w x y z
                ' -
            </div>
            <div
                css={css`
                    max-width: 720px;
                    margin-bottom: 24px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                <Span l10nKey="ReaderSetup.Letters.LetterHelp2">
                    Notice that the English list includes symbols that are used
                    to make words, like ' in&nbsp;
                </Span>
                <Span
                    l10nKey="ReaderSetup.Letters.LetterHelp3"
                    css={css`
                        font-style: italic;
                    `}
                >
                    it's
                </Span>
                <Span l10nKey="ReaderSetup.Letters.LetterHelp4">.</Span>
            </div>
            <Div
                l10nKey="ReaderSetup.Letters.LetterHelp5"
                css={css`
                    max-width: 720px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                Do not include punctuation in this list. Bloom does not support
                the inclusion of punctuation in decodable stages.
            </Div>
        </div>
    );
};
