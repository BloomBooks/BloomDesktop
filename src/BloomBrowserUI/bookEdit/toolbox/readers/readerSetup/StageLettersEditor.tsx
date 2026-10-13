import { css } from "@emotion/react";
import * as React from "react";
import { Span } from "../../../../react_components/l10nComponents";
import { Link } from "../../../../react_components/link";
import { ReaderStage } from "../ReaderSettings";
import { kBloomBlue } from "../../../../utils/colorUtils";
import { ReaderDialogTextarea } from "./ReaderDialogTextarea";
import { commonHeaderStyles, commonTextStyles } from "./readerDialogShared";

/**
 * The Decodable Stages tab as it looks when the collection defines its stages by letters: the
 * sight words this stage adds, and the grid of the collection's alphabet from which its letters
 * are chosen.
 *
 * A letter already taught by an EARLIER stage is shown but cannot be picked -- StagesTab's
 * selectLetter ignores the click -- because a stage teaches only what is new at that point.
 */
export const StageLettersEditor: React.FunctionComponent<{
    stage: ReaderStage;
    fontName: string;
    allLetters: string[];
    stageLetters: Set<string>;
    previousLetters: Set<string>;
    selectLetter: (letter: string) => void;
    updateSightWords: (value: string) => void;
    sightWordsBoxLabel: string;
    setCurTab: (value: number) => void;
}> = (props) => (
    <div
        css={css`
            min-width: 0;
            padding: 22px;
        `}
    >
        <div
            css={css`
                margin-bottom: 7px;
                ${commonHeaderStyles}
            `}
        >
            <Span l10nKey="ReaderSetup.SightWordLabel">New Sight Words</Span>
        </div>
        <ReaderDialogTextarea
            onValueChange={props.updateSightWords}
            value={props.stage.sightWords}
            ariaLabel={props.sightWordsBoxLabel}
            testId="reader-setup-sight-words-box"
            extraStyles={css`
                display: block;
                width: 325px;
                height: 35px;
                font-family: ${props.fontName};
            `}
        />
        <div
            css={css`
                margin-top: 7px;
                margin-bottom: 20px;
                ${commonTextStyles}
            `}
        >
            <Span l10nKey="ReaderSetup.SeparateWordsWithSpaces">
                Separate words with spaces.
            </Span>
        </div>
        <div
            css={css`
                margin-bottom: 10px;
                ${commonHeaderStyles}
            `}
        >
            <Span l10nKey="ReaderSetup.SelectedLetters">
                Previous and New Letters
            </Span>
        </div>
        {props.allLetters.length === 0 ? (
            <div
                css={css`
                    margin: 20px 0;
                    ${commonTextStyles}
                `}
            >
                <Span l10nKey="ReaderSetup.FirstSetupAlphabet">First,</Span>{" "}
                <Link
                    l10nKey="ReaderSetup.SetupAlphabet"
                    onClick={() => props.setCurTab(0)}
                >
                    set up the alphabet for this language.
                </Link>
            </div>
        ) : (
            <div
                css={css`
                    display: grid;
                    grid-template-columns: repeat(7, 46px);
                    gap: 8px;
                `}
            >
                {props.allLetters.map((letter) => {
                    const isPrevious = props.previousLetters.has(letter);
                    const isCurrent = props.stageLetters.has(letter);
                    let textColor = "#b7bec5";
                    if (isCurrent) {
                        textColor = "white";
                    } else if (isPrevious) {
                        textColor = kBloomBlue;
                    }
                    return (
                        <button
                            key={letter}
                            data-testid={`reader-setup-letter-${letter}`}
                            onClick={() => props.selectLetter(letter)}
                            css={css`
                                width: 46px;
                                height: 46px;
                                border: ${isCurrent || isPrevious
                                    ? `1px solid ${kBloomBlue}`
                                    : "1px solid #e2e5e7"};
                                border-radius: 6px;
                                background: ${isCurrent ? kBloomBlue : "white"};
                                color: ${textColor};
                                cursor: ${isPrevious ? "default" : "pointer"};
                                font-family: ${props.fontName};
                                font-size: 14pt;
                                overflow: hidden;
                            `}
                        >
                            {letter}
                        </button>
                    );
                })}
            </div>
        )}
        <div
            css={css`
                margin-top: 12px;
                ${commonTextStyles}
            `}
        >
            <Span l10nKey="ReaderSetup.ClickLetter">
                Click on letters to add them to this stage.
            </Span>
        </div>
    </div>
);
