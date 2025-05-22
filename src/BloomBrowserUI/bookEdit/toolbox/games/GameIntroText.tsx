import * as React from "react";
import { css } from "@emotion/react";
import { Div } from "../../../react_components/l10nComponents";
import { GameType } from "./GameInfo";

const GameIntroText: React.FunctionComponent<{
    gameType: GameType;
}> = props => {
    let gameDetails = { instructionsL10nKey: "", headingL10nKey: "" };
    switch (props.gameType) {
        case GameType.DragLetterToTarget:
            // These are currently commented out in the .xlf since this
            // template is turned off for now.
            gameDetails = {
                instructionsL10nKey:
                    "EditTab.Toolbox.DragActivity.DragLetterInstructions",
                headingL10nKey: "EditTab.Toolbox.DragActivity.DragLetterHeading"
            };
            break;
        case GameType.DragSortSentence:
            // These are currently commented out in the .xlf since this
            // template is turned off for now.
            gameDetails = {
                instructionsL10nKey:
                    "EditTab.Toolbox.DragActivity.OrderSentenceInstructions",
                headingL10nKey:
                    "EditTab.Toolbox.DragActivity.OrderSentenceHeading"
            };
            break;
        case GameType.DragImageToTarget:
            // For now, we just aren't displaying instructions for this game.
            // It is actually a whole set of game templates now, and
            // "DragImageToTarget" is a misnomer for most.
            // Thus, the header and instructions are also not correct for most.
            // (The strings are commented out in the .xlf file.)
            // gameDetails = {
            //     instructionsL10nKey:
            //         "EditTab.Toolbox.DragActivity.DragImageInstructions",
            //     headingL10nKey: "EditTab.Toolbox.DragActivity.DragImageHeading"
            // };
            break;
        case GameType.ChooseImageFromWord:
            gameDetails = {
                instructionsL10nKey:
                    "EditTab.Toolbox.GameTool.ChooseImageFromWordInstructions",
                headingL10nKey:
                    "EditTab.Toolbox.GameTool.ChooseImageFromWordHeading"
            };
            break;
        case GameType.ChooseWordFromImage:
            gameDetails = {
                instructionsL10nKey:
                    "EditTab.Toolbox.GameTool.ChooseWordFromImageInstructions",
                headingL10nKey:
                    "EditTab.Toolbox.GameTool.ChooseWordFromImageHeading"
            };
            break;
        case GameType.CheckboxQuiz:
            gameDetails = {
                instructionsL10nKey:
                    "EditTab.Toolbox.GameTool.CheckboxQuizInstructions",
                headingL10nKey: "EditTab.Toolbox.GameTool.CheckboxQuizHeading"
            };
            break;
    }
    return (
        <>
            {gameDetails.headingL10nKey && (
                <Div
                    css={css`
                        margin-top: 10px;
                        font-weight: bold;
                        font-size: larger;
                    `}
                    l10nKey={gameDetails.headingL10nKey}
                ></Div>
            )}
            {gameDetails.instructionsL10nKey && (
                <Instructions l10nKey={gameDetails.instructionsL10nKey} />
            )}
        </>
    );
};

export const Instructions: React.FunctionComponent<{
    l10nKey: string;
    l10nTitleKey?: string;
}> = props => {
    return (
        <Div
            css={css`
                margin-top: 5px;
                font-style: italic;
            `}
            l10nKey={props.l10nKey}
        ></Div>
    );
};

export default GameIntroText;
