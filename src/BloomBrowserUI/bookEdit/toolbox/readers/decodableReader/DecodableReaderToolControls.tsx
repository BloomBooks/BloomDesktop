import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { ReaderToolSwitch } from "../ReaderToolSwitch";
import { useState } from "react";
import { Span } from "../../../../react_components/l10nComponents";
import BloomButton from "../../../../react_components/bloomButton";
import { ArrowLeft, ArrowRight } from "@mui/icons-material";
import { css } from "@emotion/react";
import { Link } from "../../../../react_components/link";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";

const model = getTheOneReaderToolsModel();

const StageNav: React.FunctionComponent<{
    currentStage: number;
    changeFunction: (increment: boolean) => void;
}> = ({ currentStage, changeFunction }) => {
    return (
        <div>
            <BloomButton
                iconBeforeText={
                    currentStage > 1 ? (
                        <ArrowLeft
                            css={css`
                                color: white;
                            `}
                        />
                    ) : (
                        <></>
                    )
                }
                variant="text"
                disabled={currentStage <= 1}
                l10nKey=""
                hasText={false}
                enabled={true}
                onClick={() => changeFunction(false)}
                css={css`
                    width: 6px;
                    min-width: unset;
                    height: 18px;
                    margin-left: 5px;
                    margin-bottom: 8px;
                    padding-left: 10px;
                    padding-right: 3px;
                `}
            />
            <Span
                l10nKey="EditTab.Toolbox.DecodableReaderTool.StageNofM"
                l10nParam0={currentStage.toString()}
                l10nParam1={model.getNumberOfStages()?.toString()}
                css={css`
                    font-size: 20px;
                `}
            >
                Stage {0} of {1}
            </Span>
            <BloomButton
                iconBeforeText={
                    currentStage !== model.getNumberOfStages() ? (
                        <ArrowRight
                            css={css`
                                color: white;
                            `}
                        />
                    ) : (
                        <></>
                    )
                }
                variant="text"
                disabled={currentStage === model.getNumberOfStages()}
                l10nKey=""
                hasText={false}
                enabled={true}
                onClick={() => changeFunction(true)}
                css={css`
                    width: 16px;
                    min-width: unset;
                    height: 18px;
                    padding-left: 12px;
                    padding-right: 1px;
                    margin-bottom: 8px;
                `}
            />
        </div>
    );
};

const StageGraphemes: React.FunctionComponent<{
    currentGraphemes: string[];
}> = ({ currentGraphemes }) => {
    return (
        <div
            css={css`
                min-height: 0;
                margin-top: 20px;
                display: flex;
                flex-direction: column;
                flex: 1 1 auto;
                overflow: hidden;
            `}
        >
            <Span
                l10nKey="EditTab.Toolbox.DecodableReaderTool.LettersInThisStage"
                css={css`
                    margin-left: 8px;
                    color: #b0dee4;
                `}
            >
                Letters in this stage
            </Span>
            <div
                css={css`
                    display: flex;
                    flex-wrap: wrap;
                    row-gap: 8px;
                    min-height: 0;
                    overflow: auto;
                    margin-left: 8px;
                    margin-right: 8px;
                    margin-top: 8px;
                `}
            >
                {currentGraphemes.map((letter, index) => (
                    <div
                        key={index}
                        css={css`
                            width: 23px;
                        `}
                    >
                        {letter}
                    </div>
                ))}
            </div>
        </div>
    );
};

const SortButton: React.FunctionComponent<{
    sortType: 0 | 1 | 2;
    changeSortFunc: (which: 0 | 1 | 2) => void;
}> = ({ sortType, changeSortFunc }) => {
    let keyInsert: string;
    let tipInsert: string;
    let unicodeIcon: string;
    let shouldHighlight: boolean = false;
    switch (sortType) {
        case 0:
            keyInsert = "Alphabetically";
            tipInsert = "alphabetically";
            unicodeIcon = "\uf15d";
            shouldHighlight = model.sort === "alphabetic" ? true : false;
            break;
        case 1:
            keyInsert = "ByWordLength";
            tipInsert = "by word length";
            unicodeIcon = "\uf160";
            shouldHighlight = model.sort === "byLength" ? true : false;
            break;
        case 2:
            keyInsert = "ByFrequency";
            tipInsert = "by frequency";
            unicodeIcon = "\uf176\uf09a";
            shouldHighlight = model.sort === "byFrequency" ? true : false;
            break;
    }
    return (
        <BloomButton
            l10nKey={`EditTab.Toolbox.DecodableReaderTool.Sort${keyInsert}.Tooltip`}
            l10nTipEnglishEnabled={`Sort ${tipInsert}`}
            temporarilyDisableI18nWarning={true}
            variant="text"
            enabled={true}
            hasText={true}
            onClick={() => changeSortFunc(sortType)}
            css={css`
                font-family: FontAwesome;
                font-size: 9pt;
                width: 20px;
                min-width: unset;
                height: 18px;
                border-radius: 0;
                color: white;
                border: 1px solid white;
                background-color: ${shouldHighlight ? "grey" : "transparent"};

                &:hover {
                    background-color: ${shouldHighlight
                        ? "grey"
                        : "transparent"};
                }
            `}
        >
            {unicodeIcon}
        </BloomButton>
    );
};

const SortedStageWords: React.FunctionComponent<{
    stageSightWords: string[];
    allowedWords: string[];
    changeSortFunc: (which: 0 | 1 | 2) => void;
}> = ({ stageSightWords, allowedWords, changeSortFunc }) => {
    return (
        <div
            css={css`
                margin-top: 20px;
                min-height: 0;
                display: flex;
                flex-direction: column;
                flex: 1 1 auto;
                overflow: hidden;
            `}
        >
            <div>
                {model.synphony?.source.useAllowedWords === 1 ? (
                    <Span
                        l10nKey="EditTab.Toolbox.DecodableReaderTool.AllowedWordsInThisStage"
                        css={css`
                            margin-left: 8px;
                            color: #b0dee4;
                            display: inline-flex;
                            width: 110px;
                        `}
                    >
                        Allowed words in this stage
                    </Span>
                ) : (
                    <Span
                        l10nKey="EditTab.Toolbox.DecodableReaderTool.SampleWordsInThisStage"
                        css={css`
                            margin-left: 8px;
                            color: #b0dee4;
                            display: inline-flex;
                            width: 110px;
                        `}
                    >
                        Sample words in this stage
                    </Span>
                )}
                <SortButton sortType={0} changeSortFunc={changeSortFunc} />
                <SortButton sortType={1} changeSortFunc={changeSortFunc} />
                {model.synphony?.source.useAllowedWords === 0 && (
                    <SortButton sortType={2} changeSortFunc={changeSortFunc} />
                )}
            </div>
            <div
                css={css`
                    display: grid;
                    overflow: auto;
                    gap: 8px;
                    max-height: 300px;
                    margin-left: 8px;
                    margin-right: 8px;
                    margin-top: 8px;
                `}
            >
                {(model.synphony?.source.useAllowedWords === 1
                    ? allowedWords
                    : stageSightWords
                ).map((word, index) => (
                    <div key={index}>{word}</div>
                ))}
            </div>
        </div>
    );
};

export const DecodableReaderToolControls: React.FunctionComponent = () => {
    const [showTool, setShowTool] = useState<boolean>(
        isReaderToolEnabledOnCurrentPage(false),
    );
    const [currentStage, setCurrentStage] = useState<number>(
        model.getNumberOfStages() === 0 ? 0 : model.stageNumber,
    );
    const [currentGraphemes, setCurGraphemes] = useState<string[]>(
        model.getKnownGraphemesSorted(model.stageNumber),
    );
    const [stageSightWords, setStageSightWords] = useState<string[]>(
        model.getStageSightWordsSorted(model.stageNumber),
    );
    const [allowedWords, setAllowedWords] = useState<string[]>(
        model.getAllowedWordsSorted(model.stageNumber),
    );

    function updateState(): void {
        setCurrentStage(
            model.getNumberOfStages() === 0 ? 0 : model.stageNumber,
        );
        setCurGraphemes(model.getKnownGraphemesSorted(model.stageNumber));
        setStageSightWords(model.getStageSightWordsSorted(model.stageNumber));
        setAllowedWords(model.getAllowedWordsSorted(model.stageNumber));
    }

    model.refreshFunc = updateState;

    function changeStage(increment: boolean): void {
        if (increment) {
            model.incrementStage();
        } else {
            model.decrementStage();
        }
        updateState();
    }

    function changeSortFunc(which: 0 | 1 | 2): void {
        switch (which) {
            case 0:
                model.sortAlphabetically();
                break;
            case 1:
                model.sortByLength();
                break;
            case 2:
                model.sortByFrequency();
                break;
            default:
        }
        setStageSightWords(model.getStageSightWordsSorted(model.stageNumber));
        setAllowedWords(model.getAllowedWordsSorted(model.stageNumber));
    }

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                min-height: 0;
                overflow: hidden;
            `}
        >
            {showTool && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        flex: 1 1 auto;
                        min-height: 0;
                        overflow: hidden;
                    `}
                >
                    <div
                        css={css`
                            display: flex;
                            justify-content: right;
                            margin-bottom: 10px;
                        `}
                    >
                        <BloomButton
                            href="javascript:window.toolboxBundle.showSetupDialog('stages');"
                            l10nKey="EditTab.Toolbox.DecodableReaderTool.SetUpStages"
                            variant="text"
                            enabled={true}
                            hasText={true}
                            enabledImageFile="/bloom/bookEdit/toolbox/readers/edit-white.png"
                            css={css`
                                font-size: xx-small;
                                text-decoration: underline;
                                text-underline-offset: 1px;
                                height: 30px;
                                img {
                                    height: 14px;
                                    margin-right: 1px;
                                }
                            `}
                        >
                            Set Up Stages
                        </BloomButton>
                    </div>
                    <StageNav
                        currentStage={currentStage}
                        changeFunction={changeStage}
                    />
                    {model.synphony?.source.useAllowedWords === 0 && (
                        <StageGraphemes currentGraphemes={currentGraphemes} />
                    )}
                    <SortedStageWords
                        stageSightWords={stageSightWords}
                        allowedWords={allowedWords}
                        changeSortFunc={changeSortFunc}
                    />
                    {model.synphony?.source.useAllowedWords === 0 && (
                        <Link
                            l10nKey="EditTab.Toolbox.DecodableReaderTool.MakeLetterWordReport"
                            href="javascript:toolboxBundle.makeLetterWordList();"
                            css={css`
                                left: -3px;
                                padding: 8px;
                                padding-left: 11px;
                                background-color: #1a1a1a;
                                box-sizing: border-box;
                                width: calc(100% + 3px);
                                text-decoration: underline;
                            `}
                        >
                            Generate a letter and word list report
                        </Link>
                    )}
                    {allowedWords.length >= model.maxAllowedWords && (
                        <Span
                            l10nKey="EditTab.Toolbox.DecodableReaderTool.AllowedWordListTruncated"
                            l10nParam0={model.maxAllowedWords.toString()}
                            css={css`
                                padding: 8px 4px;
                                background-color: #1a1a1a;
                                color: red;
                            `}
                        >
                            Bloom can handle only the first {0} words.
                        </Span>
                    )}
                </div>
            )}
            <ReaderToolSwitch
                isForLeveled={false}
                changeDisplayFunc={() =>
                    showTool ? setShowTool(false) : setShowTool(true)
                }
            />
        </div>
    );
};
