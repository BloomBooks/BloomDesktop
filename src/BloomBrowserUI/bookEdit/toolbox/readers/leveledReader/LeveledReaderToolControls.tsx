import { FunctionComponent, useReducer, useRef, useState } from "react";
import { ReaderToolSwitch } from "../ReaderToolSwitch";
import { css, ThemeProvider } from "@emotion/react";
import { toolboxTheme } from "../../../../bloomMaterialUITheme";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import BloomButton from "../../../../react_components/bloomButton";
import { ReaderToolNav } from "../ReaderToolNav";
import { Div } from "../../../../react_components/l10nComponents";
import { kBloomLightBlue } from "../../../../utils/colorUtils";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { Link } from "../../../../react_components/link";
import { ContentCopySharp } from "@mui/icons-material";

const MaxActualRow: FunctionComponent = () => {
    return (
        <>
            {/* This div is intentionally included here, though empty, to ensure the grid
             syling works for the header row.*/}
            <div
                css={css`
                    grid-column: 1;
                `}
            ></div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Max"
                css={css`
                    max-width: 40px;
                    padding-right: 6px;
                    word-wrap: break-word;
                    overflow-wrap: break-word;
                    text-align: center;
                    align-self: start;
                `}
            >
                Max
            </Div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Actual"
                css={css`
                    max-width: 40px;
                    padding-right: 3px;
                    word-wrap: break-word;
                    overflow-wrap: break-word;
                    text-align: center;
                    align-self: start;
                `}
            >
                Actual
            </Div>
        </>
    );
};

const StatsRow: FunctionComponent<{
    l10nKeySuffix: string;
    maxNum: number;
    actualNum: number | string;
    children: string;
}> = (props) => {
    return (
        <>
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeySuffix}`}
                css={css`
                    min-width: 83px;
                    padding-right: 8px;
                    align-self: center;
                `}
            >
                {props.children}
            </Div>
            <div
                css={css`
                    max-width: 40px;
                    padding-right: 6px;
                    word-wrap: break-word;
                    overflow-wrap: break-word;
                    text-align: center;
                    align-self: start;
                `}
            >
                {props.maxNum !== 0 && props.maxNum !== Infinity
                    ? props.maxNum
                    : ""}
            </div>
            <div
                css={css`
                    max-width: 40px;
                    padding-right: 3px;
                    word-wrap: break-word;
                    overflow-wrap: break-word;
                    text-align: center;
                    align-self: start;
                    color: ${Number(props.actualNum) > props.maxNum &&
                    props.maxNum !== 0
                        ? "orange"
                        : "lightgreen"};
                `}
            >
                {props.actualNum}
            </div>
        </>
    );
};

const StatsGrid: FunctionComponent<{
    children: React.ReactNode;
    header1L10nText?: string;
    header2L10nText?: string;
}> = (props) => {
    return (
        <div
            css={css`
                display: inline-grid;
                row-gap: 4px;
                grid-template-columns:
                    minmax(83px, auto) minmax(min-content, auto)
                    minmax(min-content, auto);
                margin-bottom: 25px;
            `}
        >
            {props.header1L10nText && (
                <Div
                    l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.header1L10nText.replace(/\s/g, "")}`} // the ending of the l10n key is just the text, but with all spaces removed. So, remove all the spaces from the text to use it in the key.
                    css={css`
                        color: ${kBloomLightBlue};
                        grid-column: 1;
                    `}
                >
                    {props.header1L10nText}
                </Div>
            )}
            {props.header2L10nText && (
                <Div
                    l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.header2L10nText.replace(/\s/g, "")}`} // same deal here
                    css={css`
                        color: white;
                        grid-column: 1;
                    `}
                >
                    {props.header2L10nText}
                </Div>
            )}
            <MaxActualRow />
            {props.children}
        </div>
    );
};

// This simple function is used in LeveledReaderStats to cut all
// of the average values down to one decimal place
function formatAverage(value: number): string {
    return value.toFixed(1);
}

const LeveledReaderStats: FunctionComponent<{
    bookStats: { [key: string]: number };
}> = (props) => {
    const model = getTheOneReaderToolsModel();

    return (
        <div
            css={css`
                margin-left: 10px;
                margin-top: 20px;
            `}
        >
            <StatsGrid
                header1L10nText="Word Counts"
                header2L10nText="This Page"
            >
                <StatsRow
                    l10nKeySuffix="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={props.bookStats["actualWordsPerPage"]}
                >
                    per page
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="PerSentence"
                    maxNum={model.maxWordsPerSentenceOnThisPage()}
                    actualNum={props.bookStats["actualWordsPerSentence"]}
                >
                    longest sentence
                </StatsRow>
            </StatsGrid>
            {/* Here, header2 is used instead of header1 because the header text
            "This Book" is supposed to have the normal white color, and it is
            technically part of the "section" with the overall header "Word Counts" */}
            <StatsGrid header2L10nText="This Book">
                <StatsRow
                    l10nKeySuffix="Total"
                    maxNum={model.maxWordsPerBook()}
                    actualNum={props.bookStats["actualWordCount"]}
                >
                    total
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={props.bookStats["actualWordsPerPageBook"]}
                >
                    per page
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="Unique"
                    maxNum={model.maxUniqueWordsPerBook()}
                    actualNum={props.bookStats["actualUniqueWords"]}
                >
                    unique
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="MaxSentenceLength"
                    maxNum={0} // Here, maxNum is hardcoded as 0 because this stat is meant to be unlimtited.
                    actualNum={props.bookStats["actualMaxWordsPerSentence"]}
                >
                    longest sentence
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="Average"
                    maxNum={model.maxAverageWordsPerSentence()}
                    actualNum={formatAverage(
                        props.bookStats["actualAverageWordsPerSentence"],
                    )}
                >
                    avg per sentence
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="AveragePerPage"
                    maxNum={model.maxAverageWordsPerPage()}
                    actualNum={formatAverage(
                        props.bookStats["actualAverageWordsPerPage"],
                    )}
                >
                    avg per page
                </StatsRow>
            </StatsGrid>
            <StatsGrid header1L10nText="Word Lengths">
                <StatsRow
                    l10nKeySuffix="ThisPageLC"
                    maxNum={model.maxGlyphsPerWord()}
                    actualNum={props.bookStats["actualLettersPerWord"]}
                >
                    this page
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="MaxInBook"
                    maxNum={0} // maxNum is also hardcoded to be 0 here because the stat is intended to be unlimited
                    actualNum={props.bookStats["actualMaxGlyphsPerWord"]}
                >
                    max in book
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="AverageInBook"
                    maxNum={model.maxAverageGlyphsPerWord()}
                    actualNum={formatAverage(
                        props.bookStats["actualAverageGlyphsPerWord"],
                    )}
                >
                    avg in book
                </StatsRow>
            </StatsGrid>
            <StatsGrid header1L10nText="Sentence Counts">
                <StatsRow
                    l10nKeySuffix="ThisPageLC"
                    maxNum={model.maxSentencesPerPage()}
                    actualNum={props.bookStats["actualSentencesPerPage"]}
                >
                    this page
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="TotalInBook"
                    maxNum={model.maxSentencesPerBook()} // this function seems to always return 0, even if all of the level maximums in the leveled reader setup are checked and given values
                    actualNum={props.bookStats["actualSentenceCount"]}
                >
                    total in book
                </StatsRow>
                <StatsRow
                    l10nKeySuffix="AverageInBook"
                    maxNum={model.maxAverageSentencesPerPage()}
                    actualNum={formatAverage(
                        props.bookStats["actualAverageSentencesPerPage"],
                    )}
                >
                    avg in book
                </StatsRow>
            </StatsGrid>
        </div>
    );
};

const LeveledReaderList: FunctionComponent<{
    l10nKeySuffix: string;
    listItems: string[] | JSX.Element[];
    listHeaderText: string;
}> = (props) => {
    return (
        <div
            css={css`
                padding-left: 10px;
                margin-top: 15px;
                margin-bottom: 5px;
            `}
        >
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeySuffix}`}
                css={css`
                    color: ${kBloomLightBlue};
                `}
            >
                {props.listHeaderText}
            </Div>
            <ul
                css={css`
                    padding-left: 16px;
                    margin-top: 5px;
                `}
            >
                {props.listItems.map((item, index) => (
                    <li key={index}>{item}</li>
                ))}
            </ul>
        </div>
    );
};

export const LeveledReaderToolControls: FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();
    const [showTool, setShowTool] = useState<boolean>(
        isReaderToolEnabledOnCurrentPage(true),
    );

    // first, retrieve the starting stats, which are all zeroes, to avoid
    // any errors when trying to retrieve data from this dictionary
    const [bookStats, setBookStats] = useState<{ [key: string]: number }>(
        model.getStarterBookStats(),
    );

    // allows the leveled reader component to rerender whenever data changes
    const [, forceUpdate] = useReducer((x) => x + 1, 0);

    function updateState(): void {
        // only update the book stats state when all the book stats have
        // been placed in the actual book stats dictionary in the reader
        // tools model.
        if (model.areBookStatsReady()) {
            setBookStats(model.getActualBookStats());
        }
        forceUpdate();
    }

    function changeLevel(increment: boolean): void {
        if (increment) {
            model.incrementLevel();
        } else {
            model.decrementLevel();
        }
        forceUpdate();
    }

    const updateStateRef = useRef(updateState);
    updateStateRef.current = updateState;

    // This mount effect synchronizes this React component with the external reader tools model refresh callback,
    // ensuring this component rerenders whenever data changes
    useMountEffect(() => {
        model.refreshFuncLeveled = () => updateStateRef.current();
        return () => {
            model.refreshFuncLeveled = undefined;
        };
    });

    // This functions returns a list of Link components to be used
    // as list items for the LeveledReaderList component
    function getKeepInMindLinks(): JSX.Element[] {
        const linkContents = [
            "Vocabulary",
            "Formatting",
            "Predictability",
            "Illustration Support",
            "Choice of Topic",
        ];
        const linkSuffixes = [
            "Vocabulary",
            "Formatting",
            "Predictability",
            "IllustrationSupport",
            "ChoiceOfTopic",
        ];
        const listOfLinks: JSX.Element[] = [];
        for (let idx = 0; idx < linkContents.length; idx++) {
            listOfLinks.push(
                <Link
                    l10nKey={`EditTab.Toolbox.LeveledReaderTool.${linkSuffixes[idx]}`}
                    href={`api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=${linkSuffixes[idx]}`}
                    css={css`
                        text-decoration: underline;
                    `}
                >
                    {linkContents[idx]}
                </Link>,
            );
        }
        return listOfLinks;
    }

    return (
        <ThemeProvider theme={toolboxTheme}>
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                    height: 100%;
                    min-height: 0;
                    overflow-x: hidden;
                `}
            >
                {showTool && (
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            flex: 1 1 auto;
                            min-height: 0;
                            overflow-x: hidden;
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                margin-left: auto;
                                margin-bottom: 10px;
                                padding-right: 2px;
                            `}
                        >
                            <BloomButton
                                href="javascript:window.toolboxBundle.showSetupDialog('levels');"
                                l10nKey="EditTab.Toolbox.LeveledReaderTool.SetUpLevels"
                                variant="text"
                                enabled={true}
                                hasText={true}
                                enabledImageFile="/bloom/bookEdit/toolbox/readers/edit-white.png"
                                css={css`
                                    font-size: xx-small;
                                    text-decoration: underline;
                                    font-weight: normal;
                                    height: 22px;
                                    img {
                                        height: 14px;
                                        margin-right: 2px;
                                        margin-bottom: 2px;
                                    }
                                    &:hover {
                                        text-decoration: underline;
                                    }
                                `}
                            >
                                Set Up Levels
                            </BloomButton>
                        </div>
                        <ReaderToolNav
                            isForLeveled={true}
                            changeFunction={changeLevel}
                        />
                        <LeveledReaderStats bookStats={bookStats} />
                        <LeveledReaderList
                            l10nKeySuffix="FoThisLevel"
                            listHeaderText="For this Level"
                            listItems={getTheOneReaderToolsModel().getLevelReminders()}
                        />
                        <LeveledReaderList
                            l10nKeySuffix="KeepInMind"
                            listHeaderText="Keep in mind"
                            listItems={getKeepInMindLinks()}
                        />
                        <div
                            css={css`
                                display: flex;
                                margin-right: auto;
                                margin-left: 8px;
                                margin-bottom: 10px;
                            `}
                        >
                            <BloomButton
                                l10nKey="EditTab.Toolbox.LeveledReaderTool.CopyBookStatistics"
                                variant="text"
                                enabled={true}
                                hasText={true}
                                iconBeforeText={
                                    <ContentCopySharp
                                        css={css`
                                            color: white;
                                            margin-right: -4px;
                                        `}
                                    />
                                }
                                onClick={() =>
                                    model.copyLeveledReaderStatsToClipboard()
                                }
                                css={css`
                                    text-transform: uppercase;
                                    color: white;
                                    font-weight: normal;
                                    border-radius: 0;
                                    text-align: left;
                                    line-height: 15px;

                                    &:hover {
                                        background-color: black;
                                    }

                                    &:active {
                                        background-color: black;
                                        transform: translateY(2px);
                                    }
                                `}
                            >
                                Copy Book Stats
                            </BloomButton>
                        </div>
                    </div>
                )}
                <div>
                    <ReaderToolSwitch
                        isForLeveled={true}
                        changeDisplayFunc={() => setShowTool((prev) => !prev)}
                    />
                </div>
            </div>
        </ThemeProvider>
    );
};
