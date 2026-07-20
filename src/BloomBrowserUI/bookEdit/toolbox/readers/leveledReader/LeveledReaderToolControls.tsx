import { FunctionComponent, useReducer, useRef, useState } from "react";
import { ReaderToolSwitch } from "../ReaderToolSwitch";
import { css, ThemeProvider } from "@emotion/react";
import { toolboxTheme } from "../../../../bloomMaterialUITheme";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import BloomButton from "../../../../react_components/bloomButton";
import { ReaderToolNav } from "../ReaderToolNav";
import { Div, Span } from "../../../../react_components/l10nComponents";
import { kBloomLightBlue } from "../../../../utils/colorUtils";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { Link } from "../../../../react_components/link";
import {
    ContentCopySharp,
    EditOutlined,
    ReportProblemOutlined,
} from "@mui/icons-material";

// Colors used to flag a measure as within/over the current level's limit.
const kWithinLevelColor = "lightgreen";
const kOverLevelColor = "orange";
// Quiet grey used for the column headers and the (de-emphasized) Max values,
// so the eye lands on the colored Actual figures instead of the limits.
const kMutedColor = "rgba(255, 255, 255, 0.55)";

// Small, quiet uppercase styling shared by the Measure/Max/Actual column headers.
const kColumnHeaderCss = css`
    color: ${kMutedColor};
    font-size: x-small;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    align-self: end;
`;

const MaxActualRow: FunctionComponent = () => {
    return (
        <>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Measure"
                css={css`
                    ${kColumnHeaderCss};
                    grid-column: 1;
                `}
            >
                Measure
            </Div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Max"
                css={css`
                    ${kColumnHeaderCss};
                    max-width: 40px;
                    padding-right: 6px;
                    text-align: right;
                `}
            >
                Max
            </Div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Actual"
                css={css`
                    ${kColumnHeaderCss};
                    max-width: 40px;
                    padding-right: 3px;
                    text-align: right;
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
                    // The mockup capitalizes the first letter of each label
                    // (e.g. "Per page"). Doing it here keeps the localized
                    // source strings lowercase and avoids capitalizing every
                    // word, which text-transform: capitalize would do.
                    &::first-letter {
                        text-transform: uppercase;
                    }
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
                    text-align: right;
                    align-self: start;
                    // The limit is de-emphasized (quiet grey) so the eye lands
                    // on the colored Actual figure beside it.
                    color: ${kMutedColor};
                `}
            >
                {props.maxNum !== 0 && props.maxNum !== Infinity
                    ? props.maxNum
                    : "—"}
            </div>
            <div
                css={css`
                    max-width: 40px;
                    padding-right: 3px;
                    word-wrap: break-word;
                    overflow-wrap: break-word;
                    text-align: right;
                    align-self: start;
                    color: ${Number(props.actualNum) > props.maxNum &&
                    props.maxNum !== 0
                        ? kOverLevelColor
                        : kWithinLevelColor};
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
                        text-transform: uppercase;
                        letter-spacing: 0.08em;
                        // Span the section header across all three columns and
                        // underline it, so the section rule carries the structure
                        // (the individual data rows are intentionally rule-free).
                        grid-column: 1 / -1;
                        border-bottom: 1px solid rgba(255, 255, 255, 0.15);
                        padding-bottom: 5px;
                        margin-bottom: 2px;
                    `}
                >
                    {props.header1L10nText}
                </Div>
            )}
            {props.header2L10nText && (
                <Div
                    l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.header2L10nText.replace(/\s/g, "")}`} // same deal here
                    css={css`
                        // "This Page" / "This Book" are sub-labels within a
                        // section: quieter, uppercase, spanning the full width.
                        grid-column: 1 / -1;
                        color: rgba(255, 255, 255, 0.55);
                        font-size: x-small;
                        letter-spacing: 0.1em;
                        text-transform: uppercase;
                        margin-top: 6px;
                        // Half-em breathing room before the column headers/rows
                        // that follow the "This Page"/"This Book" sub-label.
                        margin-bottom: 0.5em;
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

// Legend explaining the green/orange coloring of the Actual figures.
const StatsLegend: FunctionComponent = () => {
    const swatch = (color: string) => css`
        display: inline-block;
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background-color: ${color};
        margin-right: 6px;
    `;
    const item = css`
        display: inline-flex;
        align-items: center;
        margin-left: 16px;
    `;
    return (
        <div
            css={css`
                display: flex;
                justify-content: flex-end;
                margin-top: 8px;
                font-size: x-small;
                color: rgba(255, 255, 255, 0.55);
            `}
        >
            <span css={item}>
                <span css={swatch(kWithinLevelColor)} />
                <Div l10nKey="EditTab.Toolbox.LeveledReaderTool.WithinLevel">
                    Within level
                </Div>
            </span>
            <span css={item}>
                <span css={swatch(kOverLevelColor)} />
                <Div l10nKey="EditTab.Toolbox.LeveledReaderTool.OverLevel">
                    Over level
                </Div>
            </span>
        </div>
    );
};

// Warning banner shown at the top of the stats when any measure with a
// limit exceeds that limit for the current level.
const OverLevelBanner: FunctionComponent<{ levelNumber: number }> = (props) => {
    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                gap: 8px;
                margin: 10px 0 0 0;
                padding: 8px 10px;
                border-left: 3px solid ${kOverLevelColor};
                background-color: rgba(255, 165, 0, 0.12);
            `}
        >
            <ReportProblemOutlined
                css={css`
                    color: ${kOverLevelColor};
                    font-size: 18px;
                `}
            />
            <Span
                l10nKey="EditTab.Toolbox.LeveledReaderTool.OverLevelWarning"
                l10nParam0={props.levelNumber.toString()}
                css={css`
                    font-weight: bold;
                `}
            >
                {"This book reads above Level {0}"}
            </Span>
        </div>
    );
};

// This simple function is used in LeveledReaderStats to cut all
// of the average values down to one decimal place
function formatAverage(value: number): string {
    return value.toFixed(1);
}

// True when at least one measured statistic exceeds the current level's
// limit. Mirrors the per-row orange coloring rule in StatsRow.
function isBookOverLevel(
    model: ReturnType<typeof getTheOneReaderToolsModel>,
    stats: { [key: string]: number },
): boolean {
    const pairs: Array<[number, number | string]> = [
        [model.maxWordsPerPage(), stats["actualWordsPerPage"]],
        [
            model.maxWordsPerSentenceOnThisPage(),
            stats["actualWordsPerSentence"],
        ],
        [model.maxWordsPerBook(), stats["actualWordCount"]],
        [model.maxWordsPerPage(), stats["actualWordsPerPageBook"]],
        [model.maxUniqueWordsPerBook(), stats["actualUniqueWords"]],
        [
            model.maxAverageWordsPerSentence(),
            stats["actualAverageWordsPerSentence"],
        ],
        [model.maxAverageWordsPerPage(), stats["actualAverageWordsPerPage"]],
        [model.maxGlyphsPerWord(), stats["actualLettersPerWord"]],
        [model.maxAverageGlyphsPerWord(), stats["actualAverageGlyphsPerWord"]],
        [model.maxSentencesPerPage(), stats["actualSentencesPerPage"]],
        [
            model.maxAverageSentencesPerPage(),
            stats["actualAverageSentencesPerPage"],
        ],
    ];
    // Compare against the actual value rounded to the same one decimal place
    // the rows display (see formatAverage), so the banner never disagrees with
    // the per-row coloring: e.g. a raw 5.04 shows as "5.0" within-level, so it
    // must not trip the "reads above level" banner either. Rounding is a no-op
    // for the integer stats.
    return pairs.some(([max, actual]) => {
        if (max === 0 || max === Infinity) {
            return false;
        }
        const roundedActual = Number(Number(actual).toFixed(1));
        return roundedActual > max;
    });
}

const LeveledReaderStats: FunctionComponent<{
    bookStats: { [key: string]: number };
}> = (props) => {
    const model = getTheOneReaderToolsModel();

    return (
        <div
            css={css`
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
            "This Book" is a sub-label within the "Word Counts" section. */}
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
                margin-top: 15px;
                margin-bottom: 5px;
            `}
        >
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeySuffix}`}
                css={css`
                    color: ${kBloomLightBlue};
                    // Match the stats section headers: uppercase with a
                    // full-width rule beneath.
                    text-transform: uppercase;
                    letter-spacing: 0.08em;
                    border-bottom: 1px solid rgba(255, 255, 255, 0.15);
                    padding-bottom: 5px;
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
                        color: ${kBloomLightBlue};
                        text-decoration: underline;
                    `}
                >
                    {linkContents[idx]}
                </Link>,
            );
        }
        return listOfLinks;
    }

    // "For this Level" holds level-specific reminders the team entered in the
    // setup dialog. An empty reminders field comes back as [""] (one blank
    // string), so filter out blank entries; the section is only shown below
    // when something real remains.
    const levelReminders = model
        .getLevelReminders()
        .filter((reminder) => reminder.trim().length > 0);

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
                            // A single horizontal padding on the whole panel keeps
                            // every row (including "Level x of y") on one left edge,
                            // instead of each section supplying its own left margin.
                            padding: 8px 12px 0 12px;
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                margin-left: auto;
                                margin-bottom: 10px;
                            `}
                        >
                            <BloomButton
                                href="javascript:window.toolboxBundle.showSetupDialog('levels');"
                                l10nKey="EditTab.Toolbox.LeveledReaderTool.SetUpLevels"
                                variant="text"
                                enabled={true}
                                hasText={true}
                                iconBeforeText={
                                    <EditOutlined
                                        css={css`
                                            color: ${kBloomLightBlue};
                                            font-size: 16px;
                                        `}
                                    />
                                }
                                css={css`
                                    font-size: 13px;
                                    text-decoration: underline;
                                    font-weight: normal;
                                    height: 22px;
                                    & .MuiButton-startIcon {
                                        margin-right: 3px;
                                    }
                                    // && beats MUI's .MuiButton-textPrimary so the
                                    // link text (and the currentColor icon) take
                                    // the teal accent.
                                    && {
                                        color: ${kBloomLightBlue};
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
                        {isBookOverLevel(model, bookStats) && (
                            <OverLevelBanner levelNumber={model.levelNumber} />
                        )}
                        <StatsLegend />
                        <LeveledReaderStats bookStats={bookStats} />
                        {levelReminders.length > 0 && (
                            <LeveledReaderList
                                l10nKeySuffix="FoThisLevel"
                                listHeaderText="For this Level"
                                listItems={levelReminders}
                            />
                        )}
                        <LeveledReaderList
                            l10nKeySuffix="KeepInMind"
                            listHeaderText="Keep in mind"
                            listItems={getKeepInMindLinks()}
                        />
                        <div
                            css={css`
                                display: flex;
                                margin-right: auto;
                                margin-top: 8px;
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
                                        `}
                                    />
                                }
                                onClick={() =>
                                    model.copyLeveledReaderStatsToClipboard()
                                }
                                css={css`
                                    // && beats MUI's own button styles so the
                                    // outline, padding and larger text all take.
                                    && {
                                        text-transform: uppercase;
                                        color: white;
                                        font-weight: normal;
                                        font-size: 11px;
                                        border: 1px solid
                                            rgba(255, 255, 255, 0.4);
                                        border-radius: 4px;
                                        padding: 9px 18px;
                                        line-height: 1.2;
                                    }

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
