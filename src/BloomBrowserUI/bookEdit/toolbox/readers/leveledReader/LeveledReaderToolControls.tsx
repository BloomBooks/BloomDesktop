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

const StatsRow: FunctionComponent<{
    l10nKeyInsert: string;
    maxNum: number;
    actualNum: number | string;
    children: string;
}> = (props) => {
    return (
        <>
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
                css={css`
                    min-width: 83px;
                    padding-right: 5px;
                    align-self: center;
                `}
            >
                {props.children}
            </Div>
            <div
                css={css`
                    max-width: 40px;
                    padding-right: 3px;
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

const HeaderRow: FunctionComponent = () => {
    return (
        <>
            {/* This div is intentionally included here, though empty, to ensure the grid
             syling works for the header row.*/}
            <div
                css={css`
                    min-width: 83px;
                    padding-right: 5px;
                `}
            ></div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Max"
                css={css`
                    max-width: 40px;
                    padding-right: 3px;
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

const StatsGrid: FunctionComponent<{
    children: React.ReactNode;
}> = (props) => {
    return (
        <div
            css={css`
                display: grid;
                row-gap: 5px;
                grid-template-columns:
                    minmax(83px, max-content) minmax(min-content, 40px)
                    minmax(min-content, 40px);
                margin-bottom: 25px;
            `}
        >
            <HeaderRow />
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
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.WordCounts"
                css={css`
                    color: ${kBloomLightBlue};
                    margin-bottom: 4px;
                `}
            >
                Word Counts
            </Div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.ThisPage"
                css={css`
                    color: white;
                    margin-bottom: 4px;
                `}
            >
                This Page
            </Div>
            <StatsGrid>
                <StatsRow
                    l10nKeyInsert="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={props.bookStats["actualWordsPerPage"]}
                >
                    per page
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="PerSentence"
                    maxNum={model.maxWordsPerSentenceOnThisPage()}
                    actualNum={props.bookStats["actualWordsPerSentence"]}
                >
                    longest sentence
                </StatsRow>
            </StatsGrid>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.ThisBook"
                css={css`
                    color: white;
                    margin-bottom: 4px;
                `}
            >
                This Book
            </Div>
            <StatsGrid>
                <StatsRow
                    l10nKeyInsert="Total"
                    maxNum={model.maxWordsPerBook()}
                    actualNum={props.bookStats["actualWordCount"]}
                >
                    total
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={props.bookStats["actualWordsPerPageBook"]}
                >
                    per page
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="Unique"
                    maxNum={model.maxUniqueWordsPerBook()}
                    actualNum={props.bookStats["actualUniqueWords"]}
                >
                    unique
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="MaxSentenceLength"
                    maxNum={0}
                    actualNum={props.bookStats["actualMaxWordsPerSentence"]}
                >
                    longest sentence
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="Average"
                    maxNum={model.maxAverageWordsPerSentence()}
                    actualNum={formatAverage(
                        props.bookStats["actualAverageWordsPerSentence"],
                    )}
                >
                    avg per sentence
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="AveragePerPage"
                    maxNum={model.maxAverageWordsPerPage()}
                    actualNum={formatAverage(
                        props.bookStats["actualAverageWordsPerPage"],
                    )}
                >
                    avg per page
                </StatsRow>
            </StatsGrid>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.WordLengths"
                css={css`
                    color: ${kBloomLightBlue};
                    margin-bottom: 4px;
                `}
            >
                Word Lengths
            </Div>
            <StatsGrid>
                <StatsRow
                    l10nKeyInsert="ThisPageLC"
                    maxNum={model.maxGlyphsPerWord()}
                    actualNum={props.bookStats["actualLettersPerWord"]}
                >
                    this page
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="MaxInBook"
                    maxNum={0}
                    actualNum={props.bookStats["actualMaxGlyphsPerWord"]}
                >
                    max in book
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="AverageInBook"
                    maxNum={model.maxAverageGlyphsPerWord()}
                    actualNum={formatAverage(
                        props.bookStats["actualAverageGlyphsPerWord"],
                    )}
                >
                    avg in book
                </StatsRow>
            </StatsGrid>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.SentenceCounts"
                css={css`
                    color: ${kBloomLightBlue};
                    margin-bottom: 4px;
                `}
            >
                Sentence Counts
            </Div>
            <StatsGrid>
                <StatsRow
                    l10nKeyInsert="ThisPageLC"
                    maxNum={model.maxSentencesPerPage()}
                    actualNum={props.bookStats["actualSentencesPerPage"]}
                >
                    this page
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="TotalInBook"
                    maxNum={model.maxSentencesPerBook()}
                    actualNum={props.bookStats["actualSentenceCount"]}
                >
                    total in book
                </StatsRow>
                <StatsRow
                    l10nKeyInsert="AverageInBook"
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
    isLinkList: boolean;
    l10nKeyInsert: string;
    listHeaderText: string;
}> = (props) => {
    const listItems = props.isLinkList
        ? [
              "Vocabulary",
              "Formatting",
              "Predictability",
              "Illustration Support",
              "Choice of Topic",
          ]
        : getTheOneReaderToolsModel().getLevelReminders();
    return (
        <div
            css={css`
                padding-left: 10px;
                margin-top: 15px;
                margin-bottom: 5px;
            `}
        >
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
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
                {listItems.map((item, index) => (
                    <li key={index}>
                        {!props.isLinkList ? (
                            item
                        ) : (
                            <Link
                                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${item.replace(/\b\w/g, (char) => char.toUpperCase()).replace(/\s/g, "")}`} // Since the l10nKey ending is essentially the same as the text for the link, but with all the words capitalized and no spaces, this statement takes the item, capitalizes all the words, and then strips the spaces in between words.
                                href={`api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=${item.replace(/\b\w/g, (char) => char.toUpperCase()).replace(/\s/g, "")}`} // Similarly, the fragment used to direct the user to the specific section in the html page also needs to be capitalized words with no spaces
                                css={css`
                                    text-decoration: underline;
                                `}
                            >
                                {item}
                            </Link>
                        )}
                    </li>
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
                            isLinkList={false}
                            l10nKeyInsert="FoThisLevel"
                            listHeaderText="For this Level"
                        />
                        <LeveledReaderList
                            isLinkList={true}
                            l10nKeyInsert="KeepInMind"
                            listHeaderText="Keep in mind"
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
