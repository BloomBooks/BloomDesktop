import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { ReaderToolSwitch } from "../ReaderToolSwitch";
import { useState } from "react";
import { Span } from "../../../../react_components/l10nComponents";
import BloomButton from "../../../../react_components/bloomButton";
import { ArrowLeft, ArrowRight } from "@mui/icons-material";
import { css } from "@emotion/react";
import { Link } from "../../../../react_components/link";

const model = getTheOneReaderToolsModel();

const GenLetterWordList: React.FunctionComponent = () => {
    return (
        <div id="make-letter-word-list-div1" className="clear1">
            <Link
                l10nKey="EditTab.Toolbox.DecodableReaderTool.MakeLetterWordReport"
                href="javascript:toolboxBundle.makeLetterWordList();"
            >
                Generate a letter and word list report
            </Link>
            <a
                href="javascript:toolboxBundle.makeLetterWordList();"
                data-i18n="EditTab.Toolbox.DecodableReaderTool.MakeLetterWordReport"
                id="make-letter-word-list1"
            >
                Generate a letter and word list report
            </a>
        </div>
    );
};

const CurrentStage: React.FunctionComponent<{
    currentStage: number;
    changeFunction: (increment: boolean) => void;
}> = ({ currentStage, changeFunction }) => {
    return (
        //<div className="stageLine1 clear1 noSelect1">
        //  <span id="decStage1" className="scroll-button ui-icon ui-icon-triangle-1-w" onClick={() => changeStage(false)}></span>
        //<span className="stageLabel stageLine1 noSelect1">
        <>
            <BloomButton
                iconBeforeText={<ArrowLeft style={{ color: "white" }} />}
                variant="text"
                l10nKey=""
                hasText={false}
                enabled={true}
                onClick={() => changeFunction(false)}
                css={css`
                    width: 16px;
                    min-width: unset;
                `}
            />
            <Span
                l10nKey="EditTab.Toolbox.DecodableReaderTool.StageNofM"
                l10nParam0={currentStage.toString()}
                l10nParam1={model.getNumberOfStages()?.toString()}
            >
                Stage {0} of {1}
            </Span>
            <BloomButton
                iconBeforeText={<ArrowRight style={{ color: "white" }} />}
                variant="text"
                l10nKey=""
                hasText={false}
                enabled={true}
                onClick={() => changeFunction(true)}
                css={css`
                    width: 16px;
                    min-width: unset;
                `}
            />
        </>
        //</span>
        //<span id="incStage1" className="scroll-button ui-icon ui-icon-triangle-1-e" onClick={() => changeStage(true)}></span>
        //</div>
    );
};

const LettersInStageTitle: React.FunctionComponent = () => {
    return (
        <div
            id="letters-in-this-stage1"
            className="section clear1"
            style={{ marginLeft: "6px" }}
        >
            <Span l10nKey="EditTab.Toolbox.DecodableReaderTool.LettersInThisStage">
                Letters in this stage
            </Span>
        </div>
    );
};

const LettersInStage: React.FunctionComponent<{
    currentGraphemes: string[];
}> = ({ currentGraphemes }) => {
    return (
        <div id="lettersTable1" className="tableHolder1 clear1">
            <div id="letterList1" className="letterList1">
                {currentGraphemes.map((letter, index) => (
                    <div key={index} className="letter">
                        {letter}
                    </div>
                ))}
            </div>
        </div>
    );
};

const SortTable: React.FunctionComponent = () => {
    return (
        <div className="section clear1" style={{ marginLeft: "6px" }}>
            <table>
                <tr>
                    <td style={{ paddingLeft: "0" }}>
                        <span
                            id="sample-words-this-stage"
                            data-i18n="EditTab.Toolbox.DecodableReaderTool.SampleWordsInThisStage"
                        >
                            Sample words in this stage
                        </span>
                        <span
                            id="allowed-words-this-stage"
                            data-i18n="EditTab.Toolbox.DecodableReaderTool.AllowedWordsInThisStage"
                        >
                            Allowed words in this stage
                        </span>
                    </td>
                    <td style={{ whiteSpace: "nowrap", paddingRight: "0" }}>
                        <div className="sortBlock1 clear1">
                            <div
                                id="sortAlphabetic"
                                className="sortItem1 rightBorder1 sortIconSelected"
                            >
                                <i
                                    className="fa fa-sort-alpha-asc"
                                    title="Sort alphabetically"
                                ></i>
                            </div>
                            <div
                                id="sortLength"
                                className="sortItem1 rightBorder1"
                            >
                                <i
                                    className="fa fa-sort-amount-asc"
                                    title="Sort by word length"
                                ></i>
                            </div>
                            <div id="sortFrequency" className="sortItem1">
                                <i
                                    className="fa fa-long-arrow-up"
                                    title="Sort by frequency"
                                ></i>
                                <i
                                    id="sortFrequency2"
                                    className="fa fa-facebook"
                                    title="Sort by frequency"
                                ></i>
                            </div>
                        </div>
                    </td>
                </tr>
            </table>
        </div>
    );
};

const WordsInStage: React.FunctionComponent = () => {
    return (
        <div className="tableHolder1 clear1">
            <div id="wordList" className="wordList1"></div>
        </div>
    );
};

const HiddenLabel: React.FunctionComponent = () => {
    return (
        <div id="hiddenWordListForDecodableReader" style={{ display: "none" }}>
            <label
                id="allowed_word_list_truncated_text"
                data-i18n="EditTab.Toolbox.DecodableReaderTool.AllowedWordListTruncated"
            >
                Bloom can handle only the first {0} words.
            </label>
        </div>
    );
};

const DecReaderToggle: React.FunctionComponent = () => {
    return (
        <div id="decodable-reader-tool-toggle-react-container1">
            <ReaderToolSwitch isForLeveled={false} />
        </div>
    );
};

export const DecodableReaderToolControls: React.FunctionComponent = () => {
    const [currentStage, setCurrentStage] = useState<number>(model.stageNumber);
    const [currentGraphemes, setCurGraphemes] = useState(
        model.getKnownGraphemesSorted(model.stageNumber),
    );

    function changeStage(increment: boolean): void {
        if (increment) {
            model.incrementStage();
        } else {
            model.decrementStage();
        }
        setCurrentStage(model.stageNumber);
        setCurGraphemes(model.getKnownGraphemesSorted(model.stageNumber));
    }

    return (
        <div>
            <div id="decodable-reader-tool-content1" className="turned-off1">
                <BloomButton
                    href="javascript:window.toolboxBundle.showSetupDialog('stages');"
                    l10nKey="EditTab.Toolbox.DecodableReaderTool.SetUpStages"
                    variant={"text"}
                    enabled={true}
                    hasText={true}
                    enabledImageFile="/bloom/bookEdit/toolbox/readers/edit-white.png"
                >
                    Set Up Stages
                </BloomButton>
                <CurrentStage
                    currentStage={currentStage}
                    changeFunction={changeStage}
                />
                <LettersInStageTitle />
                <LettersInStage currentGraphemes={currentGraphemes} />
                <SortTable />
                <WordsInStage />
                <Link
                    l10nKey="EditTab.Toolbox.DecodableReaderTool.MakeLetterWordReport"
                    href="javascript:toolboxBundle.makeLetterWordList();"
                >
                    Generate a letter and word list report
                </Link>
                <HiddenLabel />
            </div>
            <DecReaderToggle />
        </div>
    );
};
